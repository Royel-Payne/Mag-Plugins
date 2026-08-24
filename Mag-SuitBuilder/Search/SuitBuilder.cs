// Modified for the Shadowgain fork (TryPush/PushResolved donor accounting for attribute set transfers), 2026-08-24. See git history for details. [LGPL 2.1]

using System.Collections.Generic;

using Mag.Shared.Constants;
//using Mag.Shared.Spells;

using Mag_SuitBuilder.Equipment;

namespace Mag_SuitBuilder.Search
{
	class SuitBuilder
	{
		public SuitBuilder()
		{
			for (int i = 0; i < slotCache.Length; i++)
				slotCache[i] = new PieceSlotCache();
		}

		private class PieceSlotCache
		{
			public LeanMyWorldObject Piece;
			public EquipMask Slot;
			public ExtendedMyWorldObject ReservedDonor; // Non-null only when Piece is a set-tinkered variant
			//public int SpellCount; // Used for the old search compare method
		}

		// Every physical item this suit uses, worn or reserved as a consumed set-transfer donor.
		// Invariant: {worn pieces} and {reserved donors} are disjoint, so no item is ever worn twice,
		// worn and consumed, or consumed for two different transfers.
		readonly HashSet<ExtendedMyWorldObject> usedPhysicalItems = new HashSet<ExtendedMyWorldObject>(ReferenceEqualityComparer.Instance);

		readonly PieceSlotCache[] slotCache = new PieceSlotCache[17];
		readonly long[] spellBitmaps = new long[17];
		int nextOpenCacheIndex;

		EquipMask occupiedSlots = EquipMask.None;

		//readonly Spell[] spells = new Spell[17 * 6]; // Used for the old search compare method
		//int nextOpenSpellIndex;

		readonly int[] armorSetCountById = new int[256];

		public int TotalBaseArmorLevel { get; private set; }

		public int TotalBodyArmorPieces { get; private set; }

		/// <summary>
		/// Tries to add a piece to the suit at the given slot.
		/// Fails if the physical item is already used by this suit (worn in another slot, or reserved as a
		/// consumed set-transfer donor), or if the piece is a set-tinkered variant and no free donor of the
		/// transferred set is available for the worn coverage.
		/// </summary>
		public bool TryPush(LeanMyWorldObject item, EquipMask slot)
		{
			if (usedPhysicalItems.Contains(item.ExtendedMyWorldObject))
				return false;

			ExtendedMyWorldObject donor = null;

			if (item.IsSetTinkeredVariant)
			{
				CoverageMask coverage = SetTinkering.SlotToCoverage(slot);

				List<ExtendedMyWorldObject> pool;
				if (coverage == CoverageMask.None || !item.DonorsByCoverage.TryGetValue(coverage, out pool))
					return false;

				// Pools are sorted so inflexible/least valuable donors are consumed first
				foreach (ExtendedMyWorldObject candidate in pool)
				{
					if (candidate != item.ExtendedMyWorldObject && !usedPhysicalItems.Contains(candidate))
					{
						donor = candidate;
						break;
					}
				}

				if (donor == null)
					return false;
			}

			PushResolved(item, slot, donor);

			return true;
		}

		/// <summary>
		/// Adds a piece with an already-chosen donor (or none). Used by Clone() and starting-suit replay so
		/// donor reservations are reproduced exactly rather than re-chosen.
		/// </summary>
		public void PushResolved(LeanMyWorldObject item, EquipMask slot, ExtendedMyWorldObject consumedDonor)
		{
			slotCache[nextOpenCacheIndex].Piece = item;
			slotCache[nextOpenCacheIndex].Slot = slot;
			slotCache[nextOpenCacheIndex].ReservedDonor = consumedDonor;

			usedPhysicalItems.Add(item.ExtendedMyWorldObject);

			if (consumedDonor != null)
				usedPhysicalItems.Add(consumedDonor);
			//slotCache[nextOpenCacheIndex].SpellCount = item.SpellsToUseInSearch.Count; // Used for the old search compare method

			occupiedSlots |= slot;

			// Used for the old search compare method
			/*for (int i = 0; i < item.SpellsToUseInSearch.Count; i++)
			{
				spells[nextOpenSpellIndex] = item.SpellsToUseInSearch[i];
				nextOpenSpellIndex++;
			}*/

			if (nextOpenCacheIndex == 0)
				spellBitmaps[nextOpenCacheIndex] = item.SpellBitmap;
			else
				spellBitmaps[nextOpenCacheIndex] = spellBitmaps[nextOpenCacheIndex - 1] | item.SpellBitmap;

			nextOpenCacheIndex++;

			if (item.ItemSetId != -1)
				armorSetCountById[item.ItemSetId]++;

			if (item.CalcedStartingArmorLevel > 0)
				TotalBaseArmorLevel += (item.CalcedStartingArmorLevel * slot.GetTotalBitsSet());

			if (slot.IsBodyArmor())
				TotalBodyArmorPieces++;
		}

		public void Pop()
		{
			usedPhysicalItems.Remove(slotCache[nextOpenCacheIndex - 1].Piece.ExtendedMyWorldObject);

			if (slotCache[nextOpenCacheIndex - 1].ReservedDonor != null)
			{
				usedPhysicalItems.Remove(slotCache[nextOpenCacheIndex - 1].ReservedDonor);
				slotCache[nextOpenCacheIndex - 1].ReservedDonor = null;
			}

			occupiedSlots ^= slotCache[nextOpenCacheIndex - 1].Slot;

			//nextOpenSpellIndex -= slotCache[nextOpenCacheIndex - 1].SpellCount; // Used for the old search compare method

			armorSetCountById[slotCache[nextOpenCacheIndex - 1].Piece.ItemSetId]--;

			if (slotCache[nextOpenCacheIndex - 1].Piece.CalcedStartingArmorLevel > 0)
				TotalBaseArmorLevel -= (slotCache[nextOpenCacheIndex - 1].Piece.CalcedStartingArmorLevel * slotCache[nextOpenCacheIndex - 1].Slot.GetTotalBitsSet());

			if (slotCache[nextOpenCacheIndex - 1].Slot.IsBodyArmor())
				TotalBodyArmorPieces--;

			nextOpenCacheIndex--;
		}

		public bool SlotIsOpen(EquipMask slot)
		{
			return ((occupiedSlots & slot) == 0);
		}

		public bool HasRoomForArmorSet(int primarySetToBuild, int secondarySetToBuild, int setPieceToAdd)
		{
			if (primarySetToBuild == 255 || secondarySetToBuild == 255)
				return true;

			if (primarySetToBuild != setPieceToAdd && secondarySetToBuild != setPieceToAdd)
				return false;

			if (primarySetToBuild == setPieceToAdd && armorSetCountById[setPieceToAdd] >= 5)
				return false;

			if (secondarySetToBuild == setPieceToAdd && armorSetCountById[setPieceToAdd] >= 4)
				return false;

			return true;
		}

		public bool CanGetBeneficialSpellFrom(LeanMyWorldObject item)
		{
			if (nextOpenCacheIndex == 0)
				return true;

			return (spellBitmaps[nextOpenCacheIndex - 1] | item.SpellBitmap) != spellBitmaps[nextOpenCacheIndex - 1];

			// Used for the old search compare method
			// This whole approach needs to be optimized.
			// This is the biggest time waster in the entire search process.

			/*foreach (Spell itemSpell in item.SpellsToUseInSearch)
			{
				for (int j = 0; j < nextOpenSpellIndex; j++) // For here is faster than foreach
				{
					if (spells[j].IsSameOrSurpasses(itemSpell))
						goto end;
				}

				return true;

				end: ;
			}

			return false;*/
		}

		public int Count
		{
			get { return nextOpenCacheIndex; }
		}

		public SuitBuilder Clone()
		{
			SuitBuilder newSuit = new SuitBuilder();

			for (int i = 0; i < nextOpenCacheIndex; i++)
				newSuit.PushResolved(slotCache[i].Piece, slotCache[i].Slot, slotCache[i].ReservedDonor);

			return newSuit;
		}

		public CompletedSuit CreateCompletedSuit()
		{
			CompletedSuit suit = new CompletedSuit();

			for (int i = 0; i < nextOpenCacheIndex; i++)
				suit.AddItem(slotCache[i].Slot, slotCache[i].Piece, slotCache[i].ReservedDonor);

			return suit;
		}
	}
}
