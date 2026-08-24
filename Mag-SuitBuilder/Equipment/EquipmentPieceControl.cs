// Modified for the Shadowgain fork (set-tinkered piece display with donor tooltip), 2026-08-24. See git history for details. [LGPL 2.1]
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using Mag.Shared.Constants;
using Mag.Shared.Spells;

using Mag_SuitBuilder.Search;

namespace Mag_SuitBuilder.Equipment
{
	public partial class EquipmentPieceControl : UserControl
	{
		public EquipmentPieceControl()
		{
			InitializeComponent();

			toolTip.AutoPopDelay = 30000;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public EquipMask EquippableSlots { get; set; }
		ExtendedMyWorldObject mwo;

		readonly ToolTip toolTip = new ToolTip();

		public bool CanEquip(ExtendedMyWorldObject piece)
		{
			return (piece.EquippableSlots & EquippableSlots) == EquippableSlots;
		}

		internal void SetEquipmentPiece(LeanMyWorldObject leanPiece, string setTinkerInstructions = null)
		{
			ExtendedMyWorldObject piece = leanPiece == null ? null : leanPiece.ExtendedMyWorldObject;

			mwo = null;

			lblCharacter.Text = null;
			lblItemName.Text = null;

			lblAL.Text = null;
			lblRating.Text = null;
			lblArmorSet.Text = null;
			lblArmorSet.ForeColor = SystemColors.ControlText;

			toolTip.SetToolTip(lblArmorSet, null);
			toolTip.SetToolTip(lblItemName, null);

			lblSpell1.Text = null;
			lblSpell2.Text = null;
			lblSpell3.Text = null;
			lblSpell4.Text = null;
			lblSpell5.Text = null;
			lblSpell6.Text = null;

			chkLocked.Enabled = false;
			chkLocked.Checked = false;
			chkExclude.Enabled = false;
			chkExclude.Checked = false;

			mwo = piece;

			if (piece == null || !CanEquip(piece))
				return;

			lblCharacter.Text = piece.Owner;
			lblItemName.Text = piece.Name;

			if (piece.CalcedStartingArmorLevel > 0)
				lblAL.Text = piece.CalcedStartingArmorLevel.ToString(CultureInfo.InvariantCulture);

			if (piece.TotalRating > 0)
				lblRating.Text = "[" + piece.TotalRating + "]";

			if (leanPiece.IsSetTinkeredVariant)
			{
				// Hypothetical piece: show the set it would have after the transfer
				lblArmorSet.Text = SetTinkering.SetName(leanPiece.ItemSetId) + " (T)";
				lblArmorSet.ForeColor = Theme.SetTinker;

				toolTip.SetToolTip(lblArmorSet, setTinkerInstructions);
				toolTip.SetToolTip(lblItemName, setTinkerInstructions);
			}
			else
				lblArmorSet.Text = piece.ItemSet;

			List<Spell> spellsInOrder = new List<Spell>();

			for (Spell.CantripLevels level = Spell.CantripLevels.Legendary; level > Spell.CantripLevels.None; level--)
			{
				foreach (Spell spell in piece.CachedSpells)
				{
					if (spellsInOrder.Contains(spell))
						continue;

					if (spell.CantripLevel >= level)
						spellsInOrder.Add(spell);
				}
			}

			for (Spell.BuffLevels level = Spell.BuffLevels.VIII; level >= Spell.BuffLevels.None; level--)
			{
				foreach (Spell spell in piece.CachedSpells)
				{
					if (spellsInOrder.Contains(spell))
						continue;

					if (spell.BuffLevel >= level)
						spellsInOrder.Add(spell);
				}
			}

			if (spellsInOrder.Count > 0) lblSpell1.Text = spellsInOrder[0].ToString();
			if (spellsInOrder.Count > 1) lblSpell2.Text = spellsInOrder[1].ToString();
			if (spellsInOrder.Count > 2) lblSpell3.Text = spellsInOrder[2].ToString();
			if (spellsInOrder.Count > 3) lblSpell4.Text = spellsInOrder[3].ToString();
			if (spellsInOrder.Count > 4) lblSpell5.Text = spellsInOrder[4].ToString();
			if (spellsInOrder.Count > 5) lblSpell6.Text = spellsInOrder[5].ToString();

			// Locking a set-tinkered variant would lock the base item with its original set, contradicting what's shown.
			// Excluding is fine: it removes the base item (and therefore all its variants) from the next search.
			chkLocked.Enabled = !leanPiece.IsSetTinkeredVariant;
			chkLocked.Checked = piece.Locked && !leanPiece.IsSetTinkeredVariant;
			chkExclude.Enabled = true;
			chkExclude.Checked = piece.Exclude;
		}

		private void chkLocked_CheckedChanged(object sender, EventArgs e)
		{
			if (mwo == null)
				return;

			mwo.Locked = chkLocked.Checked;
			if (mwo.Locked && mwo.Exclude)
			{
				mwo.Exclude = false;
				chkExclude.Checked = false;
			}
		}

		private void chkExclude_CheckedChanged(object sender, EventArgs e)
		{
			if (mwo == null)
				return;

			mwo.Exclude = chkExclude.Checked;
			if (mwo.Locked && mwo.Exclude)
			{
				mwo.Locked = false;
				chkLocked.Checked = false;
			}
		}
	}
}
