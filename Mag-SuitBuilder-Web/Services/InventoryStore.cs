using System.Text;
using System.Xml;
using System.Xml.Serialization;

using Mag.Shared;
using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;
using Mag_SuitBuilder.Search;

using MagSuitBuilderWeb.Models;

namespace MagSuitBuilderWeb.Services;

/// <summary>
/// Loads and holds the Mag-Tools inventory XML files, mirroring Form1.btnLoadFromDB_Click in the
/// WinForms app. Items get a process-local ItemKey identity (the in-game object id can be zero or
/// collide across servers); a reload invalidates all keys, so clients must refetch after a load.
/// </summary>
public sealed class InventoryStore
{
	public sealed record LoadedItem(int ItemKey, string Server, string Character, ExtendedMyWorldObject Mwo);

	readonly object gate = new();
	readonly List<LoadedItem> items = new();
	readonly Dictionary<int, LoadedItem> byKey = new();
	readonly List<string> warnings = new();
	readonly SortedDictionary<string, int> armorSets = new(StringComparer.OrdinalIgnoreCase);

	int nextItemKey = 1;

	public string RootPath { get; private set; } =
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Decal Plugins", "Mag-Tools");

	public DateTime LoadedAtUtc { get; private set; }

	/// <summary>Set by the host so inventory can't be reloaded or re-flagged mid-search.</summary>
	public Func<bool> IsSearchRunning { get; set; } = () => false;

	public bool Load(string rootPath, out string error)
	{
		lock (gate)
		{
			if (IsSearchRunning())
			{
				error = "A search is running. Stop it before reloading the inventory.";
				return false;
			}

			if (!string.IsNullOrWhiteSpace(rootPath))
				RootPath = rootPath;

			items.Clear();
			byKey.Clear();
			warnings.Clear();
			armorSets.Clear();
			nextItemKey = 1;

			string[] serverFolders;

			try
			{
				serverFolders = Directory.GetDirectories(RootPath);
			}
			catch (Exception ex)
			{
				error = "Unable to read inventory root path: " + RootPath + " (" + ex.Message + ")";
				LoadedAtUtc = DateTime.UtcNow;
				return false;
			}

			var serializer = new XmlSerializer(typeof(List<ExtendedMyWorldObject>));

			foreach (var serverFolder in serverFolders)
			{
				string serverName = Path.GetFileName(serverFolder);

				foreach (var file in Directory.GetFiles(serverFolder, "*.Inventory.xml", SearchOption.AllDirectories))
				{
					string characterName = Path.GetFileName(file);
					characterName = characterName.Substring(0, characterName.IndexOf('.'));

					try
					{
						// Same hack as the WinForms app: Mag-Tools serializes MyWorldObject; we deserialize
						// the derived type by rewriting the element names.
						var fileContents = File.ReadAllText(file).Replace("MyWorldObject", "ExtendedMyWorldObject");

						List<ExtendedMyWorldObject> myWorldObjects;
						using (var stream = new MemoryStream(Encoding.ASCII.GetBytes(fileContents)))
						using (var reader = XmlReader.Create(stream))
							myWorldObjects = (List<ExtendedMyWorldObject>)serializer.Deserialize(reader);

						foreach (var mwo in myWorldObjects)
						{
							mwo.Owner = characterName;
							mwo.Spells.RemoveAll(id => !SpellTools.IsAKnownSpell(id));
							mwo.BuiltItemSearchCache();

							if (mwo.ItemSetId != 0 && mwo.EquippableSlots.IsBodyArmor() && !armorSets.ContainsKey(mwo.ItemSet))
								armorSets.Add(mwo.ItemSet, mwo.ItemSetId);

							var loaded = new LoadedItem(nextItemKey++, serverName, characterName, mwo);
							items.Add(loaded);
							byKey.Add(loaded.ItemKey, loaded);
						}
					}
					catch (Exception ex)
					{
						warnings.Add("Error parsing file: " + file + " — " + ex.Message);
					}
				}
			}

			LoadedAtUtc = DateTime.UtcNow;
			error = null;
			return true;
		}
	}

	public LoadedItem Get(int itemKey)
	{
		lock (gate)
			return byKey.TryGetValue(itemKey, out var item) ? item : null;
	}

	public bool SetFlags(int itemKey, bool? locked, bool? excluded, out string error)
	{
		lock (gate)
		{
			if (IsSearchRunning())
			{
				error = "A search is running. Stop it before changing item flags.";
				return false;
			}

			if (!byKey.TryGetValue(itemKey, out var item))
			{
				error = "Unknown item key: " + itemKey;
				return false;
			}

			if (locked.HasValue)
			{
				item.Mwo.Locked = locked.Value;
				if (item.Mwo.Locked)
					item.Mwo.Exclude = false;
			}

			if (excluded.HasValue)
			{
				item.Mwo.Exclude = excluded.Value;
				if (item.Mwo.Exclude)
					item.Mwo.Locked = false;
			}

			error = null;
			return true;
		}
	}

	public IReadOnlyList<LoadedItem> ItemsFor(IReadOnlyList<CharacterRef> characters)
	{
		lock (gate)
		{
			var wanted = new HashSet<(string, string)>(
				characters.Select(c => (c.Server.ToLowerInvariant(), c.Character.ToLowerInvariant())));

			return items.Where(i => wanted.Contains((i.Server.ToLowerInvariant(), i.Character.ToLowerInvariant()))).ToList();
		}
	}

	public InventoryDto Snapshot()
	{
		lock (gate)
		{
			var servers = items
				.GroupBy(i => i.Server)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
				.Select(serverGroup => new ServerDto(
					serverGroup.Key,
					serverGroup
						.GroupBy(i => i.Character)
						.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
						.Select(charGroup => new CharacterDto(
							charGroup.Key,
							charGroup.Select(ToItemDto).ToList()))
						.ToList()))
				.ToList();

			return new InventoryDto(
				RootPath,
				LoadedAtUtc,
				warnings.ToList(),
				armorSets.Select(kvp => new ArmorSetDto(kvp.Value, kvp.Key)).OrderBy(s => s.Name).ToList(),
				servers);
		}
	}

	public static SpellDto ToSpellDto(Spell spell)
	{
		return new SpellDto(spell.Id, spell.Name, spell.CantripLevel.ToString());
	}

	static ItemDto ToItemDto(LoadedItem item)
	{
		var mwo = item.Mwo;

		return new ItemDto(
			item.ItemKey,
			mwo.Id,
			mwo.Name ?? "(unnamed)",
			mwo.Owner,
			item.Server,
			mwo.ObjClass.ToString(),
			mwo.EquippableSlots.ToString(),
			(int)mwo.EquippableSlots,
			mwo.Coverage.ToString(),
			(int)mwo.Coverage,
			mwo.EquippedSlot.ToString(),
			mwo.ItemSetId,
			mwo.ItemSetId != 0 ? SetTinkering.SetName(mwo.ItemSetId) : null,
			mwo.CalcedStartingArmorLevel,
			mwo.ArmorLevel,
			mwo.Material,
			mwo.Tinks,
			mwo.WieldLevel,
			mwo.SkillLevel,
			new RatingsDto(mwo.DamRating, mwo.DamResistRating, mwo.CritRating, mwo.CritResistRating,
				mwo.CritDamRating, mwo.CritDamResistRating, mwo.HealBoostRating, mwo.VitalityRating),
			mwo.CachedSpells.Select(ToSpellDto).ToList(),
			mwo.Locked,
			mwo.Exclude,
			new ItemInfo(mwo).ToString());
	}
}
