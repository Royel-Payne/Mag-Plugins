using Mag.Shared.Spells;

using MagSuitBuilderWeb.Models;

namespace MagSuitBuilderWeb.Services;

/// <summary>
/// The cantrip family matrix and presets, transcribed verbatim from the WinForms
/// CantripSelectorControl (Mag-SuitBuilder\Spells\CantripSelectorControl.cs: the constructor's
/// dataGridView cells, lines 21-75, and LoadDefaults, lines 233-369). If that control changes,
/// this table must change with it.
/// </summary>
public static class CantripCatalog
{
	public sealed record Family(string Key, string Name, int Column, int Row, int LegendaryId, int EpicId, int MajorId, int MinorId)
	{
		public int IdForLevel(string level) => level?.ToLowerInvariant() switch
		{
			"legendary" => LegendaryId,
			"epic" => EpicId,
			"major" => MajorId,
			"minor" => MinorId,
			_ => 0,
		};
	}

	public static readonly IReadOnlyList<Family> Families = new List<Family>
	{
		// Column 0: attributes
		new("strength", "Strength", 0, 0, 6107, 3965, 2576, 2583),
		new("endurance", "Endurance", 0, 1, 6104, 4226, 2573, 2580),
		new("coordination", "Coordination", 0, 2, 6103, 3963, 2572, 2579),
		new("quickness", "Quickness", 0, 3, 6106, 4019, 2575, 2582),
		new("focus", "Focus", 0, 4, 6105, 3964, 2574, 2581),
		new("willpower", "Willpower", 0, 5, 6101, 4227, 2577, 2584),
		// Column 1: wards
		new("slashing-ward", "Slashing Ward", 1, 0, 6085, 4678, 2614, 2621),
		new("piercing-ward", "Piercing Ward", 1, 1, 6084, 4677, 2613, 2620),
		new("bludgeoning-ward", "Bludgeoning Ward", 1, 2, 6081, 4674, 2610, 2617),
		new("flame-ward", "Flame Ward", 1, 3, 6082, 4675, 2611, 2618),
		new("frost-ward", "Frost Ward", 1, 4, 6083, 4676, 2612, 2619),
		new("acid-ward", "Acid Ward", 1, 5, 6080, 4673, 2609, 2616),
		new("storm-ward", "Storm Ward", 1, 6, 6079, 4679, 2615, 2622),
		// Column 2: magic
		new("life-magic", "Life Magic", 2, 0, 6060, 4700, 2520, 2555),
		new("creature-ench", "Creature Ench", 2, 1, 6046, 4689, 2507, 2542),
		new("item-ench", "Item Ench", 2, 2, 6056, 4697, 2516, 2551),
		new("war-magic", "War Magic", 2, 3, 6075, 4715, 2534, 2569),
		new("void-magic", "Void Magic", 2, 4, 6074, 5429, 5428, 5427),
		new("mana-c", "Mana C", 2, 5, 6064, 4705, 2525, 2560),
		new("arcane", "Arcane", 2, 6, 6041, 4684, 2502, 2537),
		// Column 3: weapon skills / defense
		new("missile", "Missile", 3, 0, 6044, 4687, 2505, 2540),
		new("heavy", "Heavy", 3, 1, 6072, 4712, 2531, 2566),
		new("light", "Light", 3, 2, 6043, 4686, 2504, 2539),
		new("finesse", "Finesse", 3, 3, 6047, 4691, 2509, 2544),
		new("healing", "Healing", 3, 4, 6053, 4694, 2513, 2548),
		new("shield", "Shield", 3, 5, 6069, 5896, 5891, 5886),
		// Column 4
		new("two-hand", "Two Hand", 4, 0, 6073, 5034, 5070, 5072),
		new("dual-wield", "Dual Wield", 4, 1, 6050, 5894, 5889, 5884),
		new("dirty-fighting", "Dirty Fighting", 4, 2, 6049, 5893, 5888, 5883),
		new("recklessness", "Recklessness", 4, 3, 6067, 5895, 5890, 5885),
		new("sneak-attack", "Sneak Attack", 4, 4, 6070, 5897, 5892, 5887),
		new("summoning", "Summoning", 4, 5, 6125, 6124, 6126, 6127),
		// Column 5
		new("invulnerability", "Invulnerability", 5, 0, 6055, 4696, 2515, 2550),
		new("magic-resistance", "Magic Resistance", 5, 1, 6063, 4704, 2524, 2559),
		new("impregnability", "Impregnability", 5, 2, 6054, 4695, 2514, 2549),
		new("armor", "Armor", 5, 3, 6102, 4911, 2571, 2578),
		new("deception", "Deception", 5, 4, 6048, 4020, 2510, 2545),
		new("person", "Person", 5, 5, 6066, 4707, 2527, 2562),
		new("monster", "Monster", 5, 6, 6065, 4706, 2526, 2561),
		// Column 6: tinkering / trade
		new("item-tinker", "Item Tinker", 6, 0, 6057, 4698, 2517, 2552),
		new("armor-tinker", "Armor Tinker", 6, 1, 6042, 4685, 2503, 2538),
		new("weapon-tinker", "Weapon Tinker", 6, 2, 6039, 4912, 2535, 2570),
		new("magic-item", "Magic Item", 6, 3, 6062, 4703, 2523, 2558),
		new("cooking", "Cooking", 6, 4, 6045, 4688, 2506, 2541),
		new("alchemy", "Alchemy", 6, 5, 6040, 4683, 2501, 2536),
		new("fletching", "Fletching", 6, 6, 6052, 4693, 2512, 2547),
	};

	static readonly string[] AttrsAll = { "strength", "endurance", "coordination", "quickness", "focus", "willpower" };
	static readonly string[] AttrsCaster = { "endurance", "coordination", "quickness", "focus", "willpower" };
	static readonly string[] Wards = { "slashing-ward", "piercing-ward", "bludgeoning-ward", "flame-ward", "frost-ward", "acid-ward", "storm-ward" };
	static readonly string[] Defense = { "invulnerability", "magic-resistance", "armor" };

	// Preset -> family keys, all at Legendary level (parity with LoadDefaults)
	public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Presets = new Dictionary<string, IReadOnlyList<string>>
	{
		["Generic"] = AttrsAll.Concat(Wards).Concat(Defense).ToList(),
		["War"] = AttrsCaster.Concat(Wards).Concat(Defense).Append("war-magic").ToList(),
		["Void"] = AttrsCaster.Concat(Wards).Concat(Defense).Append("void-magic").ToList(),
		["Missile"] = AttrsAll.Concat(Wards).Concat(Defense).Concat(new[] { "missile", "healing", "fletching" }).ToList(),
		["Heavy"] = AttrsAll.Concat(Wards).Concat(Defense).Concat(new[] { "heavy", "healing" }).ToList(),
		["Light"] = AttrsAll.Concat(Wards).Concat(Defense).Concat(new[] { "light", "healing" }).ToList(),
		["Finesse"] = AttrsAll.Concat(Wards).Concat(Defense).Concat(new[] { "finesse", "healing" }).ToList(),
		["Two Hand"] = AttrsAll.Concat(Wards).Concat(Defense).Concat(new[] { "two-hand", "healing" }).ToList(),
		["Dual Wield"] = AttrsAll.Concat(Wards).Concat(Defense).Concat(new[] { "dual-wield", "healing" }).ToList(),
		["Tinker"] = new[] { "strength", "endurance", "coordination", "focus", "item-tinker", "armor-tinker", "weapon-tinker", "magic-item", "cooking", "alchemy" }.ToList(),
	};

	public static Family Find(string familyKey)
	{
		return Families.FirstOrDefault(f => string.Equals(f.Key, familyKey, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Resolves a selection to a Spell, or null if unknown.</summary>
	public static Spell Resolve(CantripSelection selection)
	{
		if (selection.SpellId is int spellId and > 0)
			return SpellTools.GetSpell(spellId);

		var family = Find(selection.FamilyKey);
		if (family == null)
			return null;

		int id = family.IdForLevel(selection.Level);
		return id > 0 ? SpellTools.GetSpell(id) : null;
	}

	public static CantripsDto ToDto()
	{
		CantripLevelDto Level(int id)
		{
			var spell = SpellTools.GetSpell(id);
			return new CantripLevelDto(id, spell?.Name ?? ("Spell " + id));
		}

		return new CantripsDto(
			Families.Select(f => new CantripFamilyDto(f.Key, f.Name, f.Column, f.Row,
				Level(f.LegendaryId), Level(f.EpicId), Level(f.MajorId), Level(f.MinorId))).ToList(),
			Presets);
	}
}
