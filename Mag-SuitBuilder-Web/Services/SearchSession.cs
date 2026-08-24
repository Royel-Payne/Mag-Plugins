using System.Collections.Concurrent;

using Mag.Shared;
using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;
using Mag_SuitBuilder.Search;

using MagSuitBuilderWeb.Models;

namespace MagSuitBuilderWeb.Services;

public sealed class SearchValidationException(string message) : Exception(message);

/// <summary>
/// One suit search: a headless port of the WinForms pipeline
/// (Form1.btnCalculatePossibilities_Click, armorSearcher_SuitCreated, ThreadFinished,
/// btnStopCalculating_Click). Emits suits to the EventHub as they are found.
/// </summary>
internal sealed class SearchSession
{
	public Guid SearchId { get; } = Guid.NewGuid();
	public SuitStore Suits { get; } = new();

	readonly SearchRequest request;
	readonly InventoryStore inventory;
	readonly EventHub hub;
	readonly DateTime startedUtc = DateTime.UtcNow;

	SearcherConfiguration config;
	List<LeanMyWorldObject> searchItems;
	readonly Dictionary<ExtendedMyWorldObject, int> itemKeys = new(ReferenceEqualityComparer.Instance);
	ArmorSearcher armorSearcher;
	CompletedSuit baseSuit;

	long armorThreadCounter;
	long accessoryThreadQueueCounter;
	long accessoryThreadRunningCounter;
	int armorSearcherHighestItemCount;
	volatile bool abortedSearch;
	readonly object lockObject = new();
	readonly ConcurrentDictionary<Searcher, int> accessorySearchers = new();
	int completionFired;
	volatile string state = "Preparing";
	readonly CancellationTokenSource progressCts = new();

	public SearchSession(SearchRequest request, InventoryStore inventory, EventHub hub)
	{
		this.request = request;
		this.inventory = inventory;
		this.hub = hub;
	}

	public bool IsCompleted => Volatile.Read(ref completionFired) == 1;

	public SearchStatusDto Status()
	{
		return new SearchStatusDto(
			SearchId,
			state,
			Math.Round((DateTime.UtcNow - startedUtc).TotalSeconds, 1),
			Suits.Count,
			Interlocked.Read(ref armorThreadCounter),
			Interlocked.Read(ref accessoryThreadQueueCounter),
			Interlocked.Read(ref accessoryThreadRunningCounter));
	}

	/// <summary>Setup phase — mirror of Form1.cs:405-538. Throws SearchValidationException on bad input.</summary>
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
		};

		// boundList equivalent: Locked pieces bypass filters (they must reach the search to seed
		// the base suit), everything else passes the server-side filter set.
		var boundList = loadedItems
			.Select(i => i.Mwo)
			.Where(mwo => mwo.Locked || ServerFilters.ItemPassesFilters(mwo, request, cantrips))
			.ToList();

		// Form1.cs:414-418
		searchItems = new List<LeanMyWorldObject>();
		foreach (var piece in boundList)
		{
			if (piece.Locked || (!piece.Exclude && config.ItemPassesRules(piece)))
				searchItems.Add(new LeanMyWorldObject(piece));
		}

		// Set-transfer variants must be added before the spell pass (Form1.cs:425-430)
		if (config.AllowSetTransfers)
			searchItems.AddRange(SetTinkering.GenerateVariants(boundList, config));

		// Spell scrub + family dedupe + bitmap (Form1.cs:432-496)
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
		armorSearcher.SuitCreated += ArmorSuitCreated;
		armorSearcher.SearchCompleted += CheckFinished;
	}

	/// <summary>Base suit from Locked pieces (Form1.cs:487-538) — the Yes/No reduce prompt becomes auto-reduce.</summary>
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
						// Auto-reduce into the first open single slot (the WinForms "Yes" path)
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

	public void Start()
	{
		state = "Running";
		Interlocked.Exchange(ref armorThreadCounter, 1);

		if (baseSuit.Count > 0)
			EmitSuit(baseSuit, isBase: true);

		new Thread(() =>
		{
			try
			{
				armorSearcher.Start();
			}
			catch (Exception ex)
			{
				Warn("Search failed: " + ex.Message);
				abortedSearch = true;
			}

			Interlocked.Decrement(ref armorThreadCounter);
			CheckFinished();
		})
		{ IsBackground = true, Name = "ArmorSearcher" }.Start();

		_ = PublishProgressLoop();
	}

	async Task PublishProgressLoop()
	{
		try
		{
			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

			while (await timer.WaitForNextTickAsync(progressCts.Token))
				hub.Publish("progress", Status());
		}
		catch (OperationCanceledException) { }
	}

	// Port of Form1.armorSearcher_SuitCreated (Form1.cs:578-630)
	void ArmorSuitCreated(CompletedSuit obj)
	{
		EmitSuit(obj);

		lock (lockObject)
		{
			if (obj.Count < armorSearcherHighestItemCount)
				return;

			if (obj.Count > armorSearcherHighestItemCount)
			{
				armorSearcherHighestItemCount = obj.Count;

				foreach (var kvp in accessorySearchers)
				{
					if (kvp.Value < armorSearcherHighestItemCount && kvp.Key.Running)
						kvp.Key.Stop();
				}
			}
		}

		Interlocked.Increment(ref accessoryThreadQueueCounter);

		ThreadPool.QueueUserWorkItem(_ =>
		{
			AccessorySearcher accSearcher;

			lock (lockObject)
			{
				if (abortedSearch || obj.Count < armorSearcherHighestItemCount)
				{
					Interlocked.Decrement(ref accessoryThreadQueueCounter);
					CheckFinished();
					return;
				}

				Interlocked.Increment(ref accessoryThreadRunningCounter);

				accSearcher = new AccessorySearcher(new SearcherConfiguration(), searchItems, obj);
				accessorySearchers.TryAdd(accSearcher, obj.Count);
			}

			accSearcher.SuitCreated += AccessorySuitCreated;
			accSearcher.Start();
			accSearcher.SuitCreated -= AccessorySuitCreated;

			Interlocked.Decrement(ref accessoryThreadRunningCounter);
			Interlocked.Decrement(ref accessoryThreadQueueCounter);
			CheckFinished();
		});
	}

	void AccessorySuitCreated(CompletedSuit obj)
	{
		EmitSuit(obj);
	}

	public void Stop()
	{
		if (IsCompleted)
			return;

		state = "Stopping";
		abortedSearch = true;

		armorSearcher?.Stop();

		foreach (var searcher in accessorySearchers.Keys)
		{
			if (searcher != null && searcher.Running)
				searcher.Stop();
		}

		CheckFinished();
	}

	void CheckFinished()
	{
		if (Interlocked.Read(ref armorThreadCounter) != 0 || Interlocked.Read(ref accessoryThreadQueueCounter) != 0)
			return;

		if (Interlocked.CompareExchange(ref completionFired, 1, 0) != 0)
			return;

		state = abortedSearch ? "Aborted" : "Completed";
		progressCts.Cancel();
		hub.Publish("completed", Status());
	}

	void Warn(string message)
	{
		hub.Publish("warning", new { message });
	}

	void EmitSuit(CompletedSuit suit, bool isBase = false)
	{
		var result = Suits.TryAdd(suit, (id, parentId, b) => BuildDto(id, parentId, b, suit), isBase);

		if (result is { } r)
		{
			hub.Publish("suit", r.Added.Dto);

			if (r.Evicted != null)
				hub.Publish("suit-evicted", new { suitId = r.Evicted.SuitId });
		}
	}

	// Safe on the emitting thread: CompletedSuit is immutable after CreateCompletedSuit()
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
			suit.ToString(),
			setCounts.Select(kvp => new SetCountDto(kvp.Key, SetTinkering.SetName(kvp.Key), kvp.Value))
				.OrderByDescending(s => s.Count).ToList(),
			pieces);
	}
}

/// <summary>Owns the single active search. A new search is rejected while one is running.</summary>
internal sealed class SearchService(InventoryStore inventory, EventHub hub)
{
	readonly object gate = new();

	public SearchSession Current { get; private set; }

	public bool IsRunning
	{
		get { lock (gate) return Current is { IsCompleted: false }; }
	}

	/// <summary>Starts a search; throws SearchValidationException (400) or InvalidOperationException (409).</summary>
	public SearchSession Start(SearchRequest request)
	{
		lock (gate)
		{
			if (Current is { IsCompleted: false })
				throw new InvalidOperationException("A search is already running. Stop it first.");

			var session = new SearchSession(request, inventory, hub);
			session.Prepare();
			Current = session;
			session.Start();
			return session;
		}
	}

	public bool Stop()
	{
		lock (gate)
		{
			if (Current == null || Current.IsCompleted)
				return false;

			Current.Stop();
			return true;
		}
	}
}
