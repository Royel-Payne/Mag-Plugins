// Modified for the Shadowgain fork (AllowSetTransfers option), 2026-08-24. See git history for details. [LGPL 2.1]
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Mag_SuitBuilder.Equipment;

using Mag.Shared.Constants;
using Mag.Shared.Spells;

namespace Mag_SuitBuilder.Search
{
	class SearcherConfiguration
	{
		public SearcherConfiguration()
		{
			CantripsToLookFor = new Collection<Spell>();
		}

		public ICollection<Spell> CantripsToLookFor { get; set; }

		/// <summary>
		/// Armor set Id. 0 = None, 255 = Any
		/// </summary>
		public int PrimaryArmorSet { get; set; }

		/// <summary>
		/// Armor set Id. 0 = None, 255 = Any
		/// </summary>
		public int SecondaryArmorSet { get; set; }

		/// <summary>
		/// Custom ACE server rule: allow moving a loot attribute set from a donor piece (which is destroyed)
		/// onto another piece with matching coverage. When enabled, the search also considers hypothetical
		/// set-tinkered pieces for the concrete Primary/Secondary sets selected.
		/// </summary>
		public bool AllowSetTransfers { get; set; }

		/// <summary>
		/// Run the armor search without spawning parallel work. For runtimes without real
		/// threads (e.g. WebAssembly in the browser) where Parallel.ForEach cannot be used.
		/// </summary>
		public bool SingleThreaded { get; set; }


		public bool ItemPassesRules(ExtendedMyWorldObject item)
		{
			if (CantripsToLookFor.Count > 0)
			{
				foreach (Spell cantrip in CantripsToLookFor)
				{
					foreach (Spell itemSpell in item.CachedSpells)
					{
						if (itemSpell.IsSameOrSurpasses(cantrip))
							goto end;
					}
				}

				end: ;
			}

			// If we're don't want to use any set pieces, remove them
			if (PrimaryArmorSet == 0 && SecondaryArmorSet == 0 && item.EquippableSlots.IsBodyArmor() && item.ItemSetId != 0)
				return false;

			// If we're building a two set armor suit, and we don't want any blanks or fillers, remove any pieces of armor of other sets
			if (PrimaryArmorSet != 0 && SecondaryArmorSet != 0 && PrimaryArmorSet != 255 && SecondaryArmorSet != 255 &&
				item.EquippableSlots.IsBodyArmor() && item.ItemSetId != PrimaryArmorSet && item.ItemSetId != SecondaryArmorSet)
				return false;

			return true;
		}

		public bool SpellPassesRules(Spell spell)
		{
			if (CantripsToLookFor.Count == 0)
				return true;

			foreach (Spell cantrip in CantripsToLookFor)
			{
				if (spell.IsSameOrSurpasses(cantrip))
					return true;
			}

			return false;
		}
	}
}
