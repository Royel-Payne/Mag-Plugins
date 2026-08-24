using System.Text;
using System.Xml;
using System.Xml.Serialization;

using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;

using MagSuitBuilderWeb.Models;
using MagSuitBuilderWeb.Services;

namespace MagSuitBuilderWasm;

/// <summary>
/// Browser-side inventory: the visitor drops/picks their Mag-Tools XML files, and the file
/// CONTENTS arrive here (no filesystem in WASM). Server name comes from the first path segment
/// of the picked folder structure ("Shadowgain/Character.Inventory.xml"), character name from
/// the file stem — same conventions as the local apps.
/// </summary>
internal sealed class WasmInventoryStore
{
	readonly List<InventoryStore.LoadedItem> items = new();
	readonly Dictionary<int, InventoryStore.LoadedItem> byKey = new();
	readonly List<string> warnings = new();
	readonly SortedDictionary<string, int> armorSets = new(StringComparer.OrdinalIgnoreCase);

	int nextItemKey = 1;

	public InventoryDto Load(string[] relativePaths, string[] xmlContents)
	{
		items.Clear();
		byKey.Clear();
		warnings.Clear();
		armorSets.Clear();
		nextItemKey = 1;

		var serializer = new XmlSerializer(typeof(List<ExtendedMyWorldObject>));

		for (int i = 0; i < xmlContents.Length; i++)
		{
			var path = (relativePaths.Length > i ? relativePaths[i] : null) ?? "Local/Unknown.Inventory.xml";
			var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

			string fileName = segments.Length > 0 ? segments[^1] : "Unknown.Inventory.xml";
			// Server = the file's PARENT folder (Mag-Tools layout: <root>\<Server>\Char.Inventory.xml),
			// so picking either the Mag-Tools root or a single server folder both work.
			string serverName = segments.Length > 1 ? segments[^2] : "Local";
			int dot = fileName.IndexOf('.');
			string characterName = dot > 0 ? fileName.Substring(0, dot) : fileName;

			try
			{
				// Same hack as the desktop apps: Mag-Tools serializes MyWorldObject; we
				// deserialize the derived type by rewriting the element names.
				var contents = xmlContents[i].Replace("MyWorldObject", "ExtendedMyWorldObject");

				List<ExtendedMyWorldObject> myWorldObjects;
				using (var reader = XmlReader.Create(new StringReader(contents)))
					myWorldObjects = (List<ExtendedMyWorldObject>)serializer.Deserialize(reader);

				foreach (var mwo in myWorldObjects)
				{
					mwo.Owner = characterName;
					mwo.Spells.RemoveAll(id => !SpellTools.IsAKnownSpell(id));
					mwo.BuiltItemSearchCache();

					if (mwo.ItemSetId != 0 && mwo.EquippableSlots.IsBodyArmor() && !armorSets.ContainsKey(mwo.ItemSet))
						armorSets.Add(mwo.ItemSet, mwo.ItemSetId);

					var loaded = new InventoryStore.LoadedItem(nextItemKey++, serverName, characterName, mwo);
					items.Add(loaded);
					byKey.Add(loaded.ItemKey, loaded);
				}
			}
			catch (Exception ex)
			{
				warnings.Add("Error parsing " + fileName + " — " + ex.Message);
			}
		}

		return Snapshot();
	}

	public bool SetFlags(int itemKey, bool? locked, bool? excluded)
	{
		if (!byKey.TryGetValue(itemKey, out var item))
			return false;

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

		return true;
	}

	public IReadOnlyList<InventoryStore.LoadedItem> ItemsFor(IReadOnlyList<CharacterRef> characters)
	{
		var wanted = new HashSet<(string, string)>(
			characters.Select(c => (c.Server.ToLowerInvariant(), c.Character.ToLowerInvariant())));

		return items.Where(i => wanted.Contains((i.Server.ToLowerInvariant(), i.Character.ToLowerInvariant()))).ToList();
	}

	public InventoryDto Snapshot()
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
						charGroup.Select(InventoryStore.ToItemDto).ToList()))
					.ToList()))
			.ToList();

		return new InventoryDto(
			"(browser)",
			DateTime.UtcNow,
			warnings.ToList(),
			armorSets.Select(kvp => new ArmorSetDto(kvp.Value, kvp.Key)).OrderBy(s => s.Name).ToList(),
			servers);
	}
}
