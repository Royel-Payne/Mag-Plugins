using System.Collections.Generic;

using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;

namespace Mag_SuitBuilder.Search
{
	/// <summary>
	/// Support for the custom ACE server rule that allows a loot attribute set to be removed from one
	/// piece of armor (the donor, which is destroyed) and applied to another piece that already has a set.
	/// The two pieces must have exactly matching coverage, but armor reduction may be applied to either
	/// piece first to make the coverages match. Only the 18 loot attribute sets can be transferred.
	/// </summary>
	static class SetTinkering
	{
		public const int FirstLootSetId = 13; // Soldier's Set
		public const int LastLootSetId = 30; // Dedication Set

		public static bool IsLootSet(int setId)
		{
			return setId >= FirstLootSetId && setId <= LastLootSetId;
		}

		/// <summary>
		/// True if this piece can participate in a set transfer, as either the target or the consumed donor:
		/// loot generated body armor that carries a loot attribute set.
		/// </summary>
		public static bool IsPotentialTransferPiece(ExtendedMyWorldObject item)
		{
			return item.EquippableSlots.IsBodyArmor() && item.Material != null && IsLootSet(item.ItemSetId);
		}

		/// <summary>
		/// Maps a single armor equip slot to the coverage a piece worn there provides.
		/// This is the inverse of the reduction option mapping used by ArmorSearcher bucketing.
		/// </summary>
		public static CoverageMask SlotToCoverage(EquipMask slot)
		{
			switch (slot)
			{
				case EquipMask.HeadWear:		return CoverageMask.Head;
				case EquipMask.ChestArmor:		return CoverageMask.OuterwearChest;
				case EquipMask.AbdomenArmor:	return CoverageMask.OuterwearAbdomen;
				case EquipMask.UpperArmArmor:	return CoverageMask.OuterwearUpperArms;
				case EquipMask.LowerArmArmor:	return CoverageMask.OuterwearLowerArms;
				case EquipMask.HandWear:		return CoverageMask.Hands;
				case EquipMask.UpperLegArmor:	return CoverageMask.OuterwearUpperLegs;
				case EquipMask.LowerLegArmor:	return CoverageMask.OuterwearLowerLegs;
				case EquipMask.FootWear:		return CoverageMask.Feet;
				default:						return CoverageMask.None;
			}
		}

		/// <summary>
		/// The single-slot coverages this piece can end up with: its current coverage if already single-slot,
		/// otherwise the slots it can be reduced to. Robes and unknown coverage combinations yield an empty list.
		/// </summary>
		static List<CoverageMask> SingleSlotCoverages(ExtendedMyWorldObject item)
		{
			List<CoverageMask> results = new List<CoverageMask>();

			try
			{
				foreach (CoverageMask option in item.Coverage.ReductionOptions())
				{
					if (option.GetTotalBitsSet() == 1)
						results.Add(option);
				}
			}
			catch { } // ReductionOptions throws on coverage combinations it doesn't recognize

			return results;
		}

		static int RelevantSpellCount(ExtendedMyWorldObject item, SearcherConfiguration config)
		{
			int count = 0;

			foreach (Spell spell in item.CachedSpells)
			{
				if (config.SpellPassesRules(spell))
					count++;
			}

			return count;
		}

		/// <summary>
		/// Builds hypothetical set-tinkered pieces for the search.
		/// For every eligible piece in the inventory and every concrete loot set selected in the configuration,
		/// a variant is created that wears the piece with the desired set, provided at least one donor of that
		/// set exists that can match coverage with it. Donor selection happens later, at suit build time.
		/// </summary>
		public static List<LeanMyWorldObject> GenerateVariants(IEnumerable<ExtendedMyWorldObject> inventory, SearcherConfiguration config)
		{
			List<LeanMyWorldObject> variants = new List<LeanMyWorldObject>();

			// Only build variants for the concrete loot sets the user asked for. Expanding "Any" into all 18 sets would explode the search space.
			List<int> setsToBuild = new List<int>();
			if (IsLootSet(config.PrimaryArmorSet))
				setsToBuild.Add(config.PrimaryArmorSet);
			if (IsLootSet(config.SecondaryArmorSet) && config.SecondaryArmorSet != config.PrimaryArmorSet)
				setsToBuild.Add(config.SecondaryArmorSet);

			if (setsToBuild.Count == 0)
				return variants;

			// Donor pools: for each wanted set, the physical pieces that could be consumed to provide
			// that set at each single-slot coverage they can be reduced to.
			Dictionary<int, Dictionary<CoverageMask, List<ExtendedMyWorldObject>>> donorPools = new Dictionary<int, Dictionary<CoverageMask, List<ExtendedMyWorldObject>>>();

			foreach (int setId in setsToBuild)
				donorPools[setId] = new Dictionary<CoverageMask, List<ExtendedMyWorldObject>>();

			foreach (ExtendedMyWorldObject item in inventory)
			{
				if (item.Locked || item.Exclude || !IsPotentialTransferPiece(item))
					continue;

				if (!donorPools.ContainsKey(item.ItemSetId))
					continue;

				foreach (CoverageMask coverage in SingleSlotCoverages(item))
				{
					List<ExtendedMyWorldObject> pool;
					if (!donorPools[item.ItemSetId].TryGetValue(coverage, out pool))
					{
						pool = new List<ExtendedMyWorldObject>();
						donorPools[item.ItemSetId][coverage] = pool;
					}

					pool.Add(item);
				}
			}

			// Sort each pool so that the donors we're most willing to burn come first:
			// inflexible pieces (fewest coverage options) before flexible ones, then junk before valuable.
			foreach (Dictionary<CoverageMask, List<ExtendedMyWorldObject>> poolsByCoverage in donorPools.Values)
			{
				foreach (List<ExtendedMyWorldObject> pool in poolsByCoverage.Values)
				{
					pool.Sort((a, b) =>
					{
						int result = SingleSlotCoverages(a).Count.CompareTo(SingleSlotCoverages(b).Count);
						if (result != 0) return result;

						result = RelevantSpellCount(a, config).CompareTo(RelevantSpellCount(b, config));
						if (result != 0) return result;

						return a.CalcedStartingArmorLevel.CompareTo(b.CalcedStartingArmorLevel);
					});
				}
			}

			foreach (ExtendedMyWorldObject target in inventory)
			{
				if (target.Locked || target.Exclude || !IsPotentialTransferPiece(target))
					continue;

				// A transfer onto a piece with no spells we're searching for gains nothing
				if (RelevantSpellCount(target, config) == 0)
					continue;

				foreach (int setId in setsToBuild)
				{
					if (target.ItemSetId == setId)
						continue;

					Dictionary<CoverageMask, List<ExtendedMyWorldObject>> poolsByCoverage = donorPools[setId];

					List<CoverageMask> wearableOptions = new List<CoverageMask>();

					foreach (CoverageMask coverage in SingleSlotCoverages(target))
					{
						if (poolsByCoverage.ContainsKey(coverage))
							wearableOptions.Add(coverage);
					}

					if (wearableOptions.Count > 0)
						variants.Add(new LeanMyWorldObject(target, setId, wearableOptions, poolsByCoverage));
				}
			}

			return variants;
		}

		public static string SetName(int setId)
		{
			return Dictionaries.AttributeSetInfo.ContainsKey(setId) ? Dictionaries.AttributeSetInfo[setId] : "Id: " + setId;
		}

		public static string CoverageName(CoverageMask singleSlotCoverage)
		{
			switch (singleSlotCoverage)
			{
				case CoverageMask.Head:					return "Head";
				case CoverageMask.OuterwearChest:		return "Chest";
				case CoverageMask.OuterwearAbdomen:		return "Abdomen";
				case CoverageMask.OuterwearUpperArms:	return "Upper Arms";
				case CoverageMask.OuterwearLowerArms:	return "Lower Arms";
				case CoverageMask.Hands:				return "Hands";
				case CoverageMask.OuterwearUpperLegs:	return "Upper Legs";
				case CoverageMask.OuterwearLowerLegs:	return "Lower Legs";
				case CoverageMask.Feet:					return "Feet";
				default:								return singleSlotCoverage.ToString();
			}
		}

		/// <summary>
		/// The tailoring tool that reduces a piece down to the given single-slot coverage.
		/// </summary>
		public static string ReductionToolFor(CoverageMask reducedTo)
		{
			switch (reducedTo)
			{
				case CoverageMask.OuterwearChest:
				case CoverageMask.OuterwearAbdomen:
				case CoverageMask.OuterwearUpperArms:
					return "Armor Main Reduction Tool";

				case CoverageMask.OuterwearUpperLegs:
					return "Armor Middle Reduction Tool";

				case CoverageMask.OuterwearLowerArms:
				case CoverageMask.OuterwearLowerLegs:
				case CoverageMask.Feet:
					return "Armor Lower Reduction Tool";

				default:
					return "an armor reduction tool";
			}
		}

		static string Describe(ExtendedMyWorldObject item)
		{
			return item.Name + " (" + item.Owner + ")";
		}

		/// <summary>
		/// The in-game steps required to realize a set-tinkered variant worn at the given slot.
		/// </summary>
		public static List<string> GetInstructionLines(LeanMyWorldObject variant, EquipMask wornSlot, ExtendedMyWorldObject donor)
		{
			List<string> lines = new List<string>();

			ExtendedMyWorldObject target = variant.ExtendedMyWorldObject;

			if (donor == null)
			{
				lines.Add("No donor was recorded for " + Describe(target) + ". This is a bug.");
				return lines;
			}

			CoverageMask wornCoverage = SlotToCoverage(wornSlot);
			string setName = SetName(variant.ItemSetId);

			bool targetNeedsReduction = target.Coverage != wornCoverage;

			if (donor.Coverage == target.Coverage)
			{
				// Coverages already match, so transfer first; the target only needs reducing to be worn in this suit
				lines.Add("Transfer " + setName + " from " + Describe(donor) + " onto " + Describe(target) + " - coverages already match; donor is destroyed");

				if (targetNeedsReduction)
					lines.Add("Reduce " + Describe(target) + " to " + CoverageName(wornCoverage) + " with " + ReductionToolFor(wornCoverage));
			}
			else
			{
				if (targetNeedsReduction)
					lines.Add("Reduce " + Describe(target) + " to " + CoverageName(wornCoverage) + " with " + ReductionToolFor(wornCoverage));

				if (donor.Coverage != wornCoverage)
					lines.Add("Reduce " + Describe(donor) + " to " + CoverageName(wornCoverage) + " with " + ReductionToolFor(wornCoverage));

				lines.Add("Transfer " + setName + " from " + Describe(donor) + " onto " + Describe(target) + " - donor is destroyed");
			}

			return lines;
		}
	}
}
