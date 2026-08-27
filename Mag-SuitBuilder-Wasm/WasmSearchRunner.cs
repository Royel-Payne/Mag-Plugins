using Mag.Shared;
using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;
using Mag_SuitBuilder.Search;

using MagSuitBuilderWeb.Models;
using MagSuitBuilderWeb.Services;

namespace MagSuitBuilderWasm;

/// <summary>
/// Single-threaded port of the local web app's SearchSession for the WebAssembly runtime:
/// no Thread/ThreadPool/PeriodicTimer. The armor search runs inline (blocking the worker —
/// the page's UI thread is unaffected); accessory searches are queued and drained one per
/// event-loop turn afterwards so a Stop message can be honored between them.
/// </summary>
internal sealed class WasmSearchRunner
{
	public Guid SearchId { get; } = Guid.NewGuid();
	public SuitStore Suits { get; } = new();

	readonly SearchRequest request;
	readonly WasmInventoryStore inventory;
	readonly Action<string, string> emit;
	readonly DateTime startedUtc = DateTime.UtcNow;

	SearcherConfiguration config;
	List<LeanMyWorldObject> searchItems;
	readonly Dictionary<ExtendedMyWorldObject, int> itemKeys = new(ReferenceEqualityComparer.Instance);
	ArmorSearcher armorSearcher;
	CompletedSuit baseSuit;

	readonly List<CompletedSuit> accessoryQueue = new();
	int armorSearcherHighestItemCount;
	volatile bool aborted;
	string state = "Preparing";
	DateTime lastProgress = DateTime.MinValue;
	int accessoriesQueued;

	public WasmSearchRunner(SearchRequest request, WasmInventoryStore inventory, Action<string, string> emit)
	{
		this.request = request;
		this.inventory = inventory;
		this.emit = emit;
	}

	public bool IsCompleted => state is "Completed" or "Aborted";

	public SearchStatusDto Status()
	{
		return new SearchStatusDto(
			SearchId,
			state,
			Math.Round((DateTime.UtcNow - startedUtc).TotalSeconds, 1),
			Suits.Count,
			state == "Running" ? 1 : 0,
			accessoriesQueued,
			0);
	}

	/// <summary>Mirror of SearchSession.Prepare (Mag-SuitBuilder-Web\Services\SearchSession.cs).</summary>
	public void Prepare()
	{
		if (request.Characters == null || request.Characters.Count == 0)
			throw new SearchValidationException("Select at least one character.");

		var loadedItems = inventory.ItemsFor(request.Characters);
		if (loadedItems.Count == 0)
			throw new SearchValidationException("The selected characters have no loaded items.");

		foreach (var item in loadedItems)
			itemKeys[item.Mwo] = item.ItemKey;

		var cantrips = new List<Spell>();
		foreach (var selection in request.Cantrips ?? [])
		{
			var spell = CantripCatalog.Resolve(selection);
			if (spell == null)
				throw new SearchValidationException("Unknown cantrip selection: " + (selection.FamilyKey ?? selection.SpellId?.ToString() ?? "(empty)"));
			if (!cantrips.Contains(spell))
				cantrips.Add(spell);
		}

		config = new SearcherConfiguration
		{
			CantripsToLookFor = cantrips,
			PrimaryArmorSet = request.PrimaryArmorSetId,
			SecondaryArmorSet = request.SecondaryArmorSetId,
			AllowSetTransfers = request.AllowSetTransfers,
			SingleThreaded = true,
		};

		var boundList = loadedItems
			.Select(i => i.Mwo)
			.Where(mwo => mwo.Locked || ServerFilters.ItemPassesFilters(mwo, request, cantrips))
			.ToList();

		searchItems = new List<LeanMyWorldObject>();
		foreach (var piece in boundList)
		{
			if (piece.Locked || (!piece.Exclude && config.ItemPassesRules(piece)))
				searchItems.Add(new LeanMyWorldObject(piece));
		}

		if (config.AllowSetTransfers)
			searchItems.AddRange(SetTinkering.GenerateVariants(boundList, config));

		var possibleSpells = new List<Spell>();
		var epicImpen = SpellTools.GetSpell(4667);

		foreach (var piece in searchItems)
		{
			piece.SpellsToUseInSearch.Clear();

			foreach (Spell spell in piece.ExtendedMyWorldObject.CachedSpells)
			{
				if (config.SpellPassesRules(spell) && !spell.IsOfSameFamilyAndGroup(epicImpen))
				{
					piece.SpellsToUseInSearch.Add(spell);

					if (!possibleSpells.Contains(spell))
						possibleSpells.Add(spell);
				}
			}
		}

		for (int i = possibleSpells.Count - 1; i >= 0; i--)
		{
			for (int j = 0; j < i; j++)
			{
				if (possibleSpells[j].IsOfSameFamilyAndGroup(possibleSpells[i]))
				{
					possibleSpells.RemoveAt(j);
					break;
				}
			}
		}

		if (possibleSpells.Count > 64)
			throw new SearchValidationException(cantrips.Count == 0
				? "No cantrips selected. Pick the cantrips to hunt for (or load a preset) — the search optimizes for your selection."
				: "Too many distinct spell families (" + possibleSpells.Count + "). The search can track at most 64.");

		var spellMap = new Dictionary<Spell, long>();
		for (int i = 0; i < possibleSpells.Count; i++)
			spellMap.Add(possibleSpells[i], 1L << i);

		foreach (var piece in searchItems)
		{
			piece.SpellBitmap = 0;

			foreach (var spell in piece.SpellsToUseInSearch)
			{
				foreach (var kvp in spellMap)
				{
					if (spell.IsOfSameFamilyAndGroup(kvp.Key))
						piece.SpellBitmap |= kvp.Value;
				}
			}
		}

		BuildBaseSuit();

		armorSearcher = new ArmorSearcher(config, searchItems, baseSuit);
		armorSearcher.SuitCreated += OnArmorSuitCreated;
	}

	/// <summary>Base suit from Locked pieces — same auto-reduce policy as the local web app.</summary>
	void BuildBaseSuit()
	{
		baseSuit = new CompletedSuit();

		for (int slotCount = 1; slotCount <= 5; slotCount++)
		{
			foreach (var item in searchItems)
			{
				if (item.EquippableSlots == EquipMask.None || item.EquippableSlots == EquipMask.MeleeWeapon || item.EquippableSlots == EquipMask.MissileWeapon || item.EquippableSlots == EquipMask.TwoHanded || item.EquippableSlots == EquipMask.Held || item.EquippableSlots == EquipMask.MissileAmmo)
					continue;
				if (item.EquippableSlots == EquipMask.Cloak || item.EquippableSlots == EquipMask.SigilOne || item.EquippableSlots == EquipMask.SigilTwo || item.EquippableSlots == EquipMask.SigilThree)
					continue;

				if (!item.ExtendedMyWorldObject.Locked || item.EquippableSlots.GetTotalBitsSet() != slotCount)
					continue;

				try
				{
					if (item.EquippableSlots.GetTotalBitsSet() > 1 && item.EquippableSlots.IsBodyArmor() && item.Material != null)
					{
						EquipMask slotFlag = EquipMask.None;

						foreach (var option in item.Coverage.ReductionOptions())
						{
							if (option == CoverageMask.OuterwearChest && baseSuit[EquipMask.ChestArmor] == null) { slotFlag = EquipMask.ChestArmor; break; }
							if (option == CoverageMask.OuterwearUpperArms && baseSuit[EquipMask.UpperArmArmor] == null) { slotFlag = EquipMask.UpperArmArmor; break; }
							if (option == CoverageMask.OuterwearLowerArms && baseSuit[EquipMask.LowerArmArmor] == null) { slotFlag = EquipMask.LowerArmArmor; break; }
							if (option == CoverageMask.OuterwearAbdomen && baseSuit[EquipMask.AbdomenArmor] == null) { slotFlag = EquipMask.AbdomenArmor; break; }
							if (option == CoverageMask.OuterwearUpperLegs && baseSuit[EquipMask.UpperLegArmor] == null) { slotFlag = EquipMask.UpperLegArmor; break; }
							if (option == CoverageMask.OuterwearLowerLegs && baseSuit[EquipMask.LowerLegArmor] == null) { slotFlag = EquipMask.LowerLegArmor; break; }
							if (option == CoverageMask.Feet && baseSuit[EquipMask.FootWear] == null) { slotFlag = EquipMask.FootWear; break; }
						}

						if (slotFlag == EquipMask.None)
							Warn("Unable to reduce locked piece " + item.ExtendedMyWorldObject.Name + " into an open single slot.");
						else
							baseSuit.AddItem(slotFlag, item);
					}
					else if (!baseSuit.AddItem(item))
						Warn("Failed to add " + item.ExtendedMyWorldObject.Name + " to base suit of armor.");
				}
				catch (ArgumentException)
				{
					Warn("Failed to add " + item.ExtendedMyWorldObject.Name + " to base suit of armor. It overlaps another piece.");
				}
			}
		}
	}

	public async Task RunAsync()
	{
		// Let the StartSearch export return its reply before the heavy work begins
		await Task.Yield();

		try
		{
			await RunCoreAsync();
		}
		catch (Exception ex)
		{
			aborted = true;
			state = "Aborted";
			Warn("Search failed: " + ex.Message);
			emit("completed", System.Text.Json.JsonSerializer.Serialize(Status(), Json.Options));
		}
	}

	async Task RunCoreAsync()
	{
		state = "Running";

		if (baseSuit.Count > 0)
			EmitSuit(baseSuit, isBase: true);

		// Armor phase: synchronous — blocks the worker's loop; suits still stream out because
		// postMessage from inside the blocked computation delivers immediately.
		armorSearcher.Start();

		// Accessory phase: one searcher per event-loop turn so queued Stop messages get a chance.
		var candidates = accessoryQueue.Where(s => s.Count >= armorSearcherHighestItemCount).ToList();
		accessoriesQueued = candidates.Count;

		foreach (var suit in candidates)
		{
			if (aborted)
				break;

			await Task.Yield();

			var accessorySearcher = new AccessorySearcher(new SearcherConfiguration { SingleThreaded = true }, searchItems, suit);
			accessorySearcher.SuitCreated += OnAccessorySuitCreated;
			accessorySearcher.Start();
			accessorySearcher.SuitCreated -= OnAccessorySuitCreated;

			accessoriesQueued--;
		}

		state = aborted ? "Aborted" : "Completed";
		emit("completed", System.Text.Json.JsonSerializer.Serialize(Status(), Json.Options));
	}

	void OnArmorSuitCreated(CompletedSuit suit)
	{
		EmitSuit(suit);

		if (suit.Count > armorSearcherHighestItemCount)
			armorSearcherHighestItemCount = suit.Count;

		accessoryQueue.Add(suit);
	}

	void OnAccessorySuitCreated(CompletedSuit suit)
	{
		EmitSuit(suit);
	}

	public void Stop()
	{
		aborted = true;
		armorSearcher?.Stop();
	}

	void Warn(string message)
	{
		emit("warning", System.Text.Json.JsonSerializer.Serialize(new WarningDto(message), Json.Options));
	}

	void EmitSuit(CompletedSuit suit, bool isBase = false)
	{
		var result = Suits.TryAdd(suit, (id, parentId, b) => BuildDto(id, parentId, b, suit), isBase);

		if (result is { } r)
		{
			emit("suit", System.Text.Json.JsonSerializer.Serialize(r.Added.Dto, Json.Options));

			if (r.Evicted != null)
				emit("suit-evicted", System.Text.Json.JsonSerializer.Serialize(new SuitEvictedDto(r.Evicted.SuitId), Json.Options));
		}

		if ((DateTime.UtcNow - lastProgress).TotalSeconds >= 1)
		{
			lastProgress = DateTime.UtcNow;
			emit("progress", System.Text.Json.JsonSerializer.Serialize(Status(), Json.Options));
		}
	}

	// Mirror of SearchSession.BuildDto
	SuitDto BuildDto(int suitId, int? parentSuitId, bool isBase, CompletedSuit suit)
	{
		var pieces = new List<SuitPieceDto>();
		var setCounts = new Dictionary<int, int>();

		foreach (var kvp in suit)
		{
			var piece = kvp.Value;
			var mwo = piece.ExtendedMyWorldObject;

			if (piece.ItemSetId != 0)
				setCounts[piece.ItemSetId] = setCounts.TryGetValue(piece.ItemSetId, out var c) ? c + 1 : 1;

			DonorDto donorDto = null;
			IReadOnlyList<string> instructions = null;

			if (piece.IsSetTinkeredVariant)
			{
				var donor = suit.GetConsumedDonor(piece);

				if (donor != null)
					donorDto = new DonorDto(itemKeys.TryGetValue(donor, out var dk) ? dk : 0, donor.Name, donor.Owner, new ItemInfo(donor).ToString());

				instructions = SetTinkering.GetInstructionLines(piece, kvp.Key, donor);
			}

			pieces.Add(new SuitPieceDto(
				kvp.Key.ToString(),
				(int)kvp.Key,
				itemKeys.TryGetValue(mwo, out var key) ? key : 0,
				mwo.Name ?? "(unnamed)",
				mwo.Owner,
				piece.CalcedStartingArmorLevel,
				piece.ItemSetId,
				piece.ItemSetId != 0 ? SetTinkering.SetName(piece.ItemSetId) : null,
				piece.OriginalSetId,
				piece.OriginalSetId != 0 ? SetTinkering.SetName(piece.OriginalSetId) : null,
				piece.IsSetTinkeredVariant,
				donorDto,
				instructions,
				piece.SpellsToUseInSearch.Select(InventoryStore.ToSpellDto).ToList(),
				mwo.CachedSpells.Select(InventoryStore.ToSpellDto).ToList(),
				new ItemInfo(mwo).ToString()));
		}

		return new SuitDto(
			suitId,
			parentSuitId,
			isBase,
			suit.Count,
			suit.TotalBaseArmorLevel,
			suit.TotalEffectiveLegendaries,
			suit.TotalEffectiveEpics,
			suit.TotalEffectiveMajors,
			suit.TotalSetTinkers,
			Math.Min(5, suit.CountOfSet(config.PrimaryArmorSet)),
			Math.Min(4, suit.CountOfSet(config.SecondaryArmorSet)),
			suit.ToString(),
			setCounts.Select(kvp => new SetCountDto(kvp.Key, SetTinkering.SetName(kvp.Key), kvp.Value))
				.OrderByDescending(s => s.Count).ToList(),
			pieces);
	}
}
