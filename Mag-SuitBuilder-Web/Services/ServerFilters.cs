using Mag.Shared;
using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;
using Mag_SuitBuilder.Search;

using MagSuitBuilderWeb.Models;

namespace MagSuitBuilderWeb.Services;

/// <summary>
/// Server-side port of the search-relevant subset of FiltersControl.ItemPassesFilters
/// (Mag-SuitBuilder\Equipment\FiltersControl.cs:111-384), including the AllowSetTransfers
/// donor-visibility bypass. Weapons/wands/salvage/etc. never reach the solver's buckets, so they
/// are excluded from search input here; regex/ratings/wield filters are deferred to v2 (display
/// filtering happens client-side).
/// </summary>
public static class ServerFilters
{
	public static bool ItemPassesFilters(ExtendedMyWorldObject mwo, SearchRequest request, IReadOnlyList<Spell> cantrips)
	{
		var f = request.Filters ?? SearchFilters.Default;

		if (f.RemoveEquipped && mwo.EquippedSlot != EquipMask.None)
			return false;

		if (f.RemoveUnequipped && mwo.EquippedSlot == EquipMask.None)
			return false;

		bool isArmorOrClothing = mwo.ObjClass == ObjectClass.Armor || mwo.ObjClass == ObjectClass.Clothing;

		int minCore = f.MinBaseArmorLevel ?? 0;
		int maxCore = f.MaxBaseArmorLevel ?? 9999;
		if (isArmorOrClothing && mwo.EquippableSlots.IsCoreBodyArmor() &&
			(mwo.CalcedStartingArmorLevel < minCore || mwo.CalcedStartingArmorLevel > maxCore))
			return false;

		int minExtremity = f.MinExtremityArmorLevel ?? 0;
		int maxExtremity = f.MaxExtremityArmorLevel ?? 9999;
		if (isArmorOrClothing && mwo.EquippableSlots.IsExtremityBodyArmor() &&
			(mwo.CalcedStartingArmorLevel < minExtremity || mwo.CalcedStartingArmorLevel > maxExtremity))
			return false;

		if (mwo.EquippableSlots.IsBodyArmor())
		{
			if (!f.IncludeBodyArmorClothing)
				return false;
		}
		else if (mwo.EquippableSlots.IsUnderwear())
		{
			if (!f.IncludeShirtsPants)
				return false;
		}
		else if (mwo.ObjClass == ObjectClass.Jewelry)
		{
			if (!f.IncludeJewelry)
				return false;

			if (!f.JewelryNecklace && mwo.EquippableSlots == EquipMask.NeckWear) return false;
			if (!f.JewelryTrinket && mwo.EquippableSlots == EquipMask.TrinketOne) return false;
			if (!f.JewelryBracelet && mwo.EquippableSlots == (EquipMask.WristWearLeft | EquipMask.WristWearRight)) return false;
			if (!f.JewelryRing && mwo.EquippableSlots == (EquipMask.FingerWearLeft | EquipMask.FingerWearRight)) return false;
		}
		else
		{
			// Weapons, casters, salvage, containers, etc. — the armor/accessory searchers never
			// bucket these, so they are not search input.
			return false;
		}

		// When set transfers are allowed, any loot body armor carrying a loot attribute set is a
		// potential donor (or transfer target) and must stay visible to the search.
		bool potentialSetTransferPiece = request.AllowSetTransfers && SetTinkering.IsPotentialTransferPiece(mwo);

		if (mwo.EquippableSlots.IsBodyArmor())
		{
			if (request.PrimaryArmorSetId == 0 && request.SecondaryArmorSetId == 0 && mwo.ItemSetId != 0)
				return false;

			if (request.PrimaryArmorSetId != 255 && request.SecondaryArmorSetId != 255 && !potentialSetTransferPiece)
			{
				if (request.PrimaryArmorSetId != mwo.ItemSetId && request.SecondaryArmorSetId != mwo.ItemSetId)
					return false;
			}
		}

		int legendaries = 0;
		int epics = 0;
		foreach (Spell spell in mwo.CachedSpells)
		{
			if (spell.CantripLevel >= Spell.CantripLevels.Legendary) legendaries++;
			if (spell.CantripLevel >= Spell.CantripLevels.Epic) epics++;
		}

		if (legendaries < (f.MinLegendaries ?? 0) || legendaries > (f.MaxLegendaries ?? 99) ||
			epics < (f.MinEpics ?? 0) || epics > (f.MaxEpics ?? 99))
			return false;

		// Spell selector: item must carry at least one of the desired cantrips (or better),
		// unless it's a potential set-transfer piece.
		if (cantrips.Count > 0 && !potentialSetTransferPiece)
		{
			bool hasMatch = false;

			foreach (var spell in mwo.CachedSpells)
			{
				foreach (var desired in cantrips)
				{
					if (spell.IsSameOrSurpasses(desired))
					{
						hasMatch = true;
						break;
					}
				}

				if (hasMatch)
					break;
			}

			if (!hasMatch)
				return false;
		}

		return true;
	}
}
