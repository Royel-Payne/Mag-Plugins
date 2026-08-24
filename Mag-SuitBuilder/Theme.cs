using System.Drawing;
using System.Windows.Forms;

namespace Mag_SuitBuilder
{
	/// <summary>
	/// Dark theme for the app. Application.SetColorMode(Dark) handles the title bar and most base
	/// controls; this fills in what it doesn't reach: DataGridViews, TreeViews, tab pages, and the
	/// equipment card panels.
	/// </summary>
	static class Theme
	{
		public static readonly Color Background		= Color.FromArgb(24, 24, 27);	// window base
		public static readonly Color Surface		= Color.FromArgb(32, 32, 36);	// cards, grids, trees
		public static readonly Color SurfaceAlt		= Color.FromArgb(42, 42, 47);	// alternating rows
		public static readonly Color Header			= Color.FromArgb(48, 48, 54);	// grid headers
		public static readonly Color Border			= Color.FromArgb(63, 63, 70);
		public static readonly Color Text			= Color.FromArgb(228, 228, 231);
		public static readonly Color TextMuted		= Color.FromArgb(161, 161, 170);
		public static readonly Color Selection		= Color.FromArgb(51, 65, 85);	// selected rows/nodes
		public static readonly Color SetTinker		= Color.FromArgb(251, 146, 60);	// set-tinkered highlight

		// Cantrip level cell colors; dark enough that the grid's light text stays readable
		public static readonly Color Legendary		= Color.FromArgb(180, 83, 9);	// amber
		public static readonly Color Epic			= Color.FromArgb(21, 128, 61);	// green
		public static readonly Color Major			= Color.FromArgb(190, 24, 93);	// pink
		public static readonly Color Minor			= Color.FromArgb(29, 78, 216);	// blue

		// Bright variants for the legend chips, which render with dark text (they can appear disabled,
		// where WinForms draws grayed text regardless of the label's ForeColor)
		public static readonly Color LegendaryBright	= Color.FromArgb(251, 191, 36);
		public static readonly Color EpicBright			= Color.FromArgb(74, 222, 128);
		public static readonly Color MajorBright		= Color.FromArgb(244, 114, 182);
		public static readonly Color MinorBright		= Color.FromArgb(96, 165, 250);

		public static void Apply(Control root)
		{
			Style(root);

			foreach (Control child in root.Controls)
				Apply(child);
		}

		static void Style(Control control)
		{
			if (control is Form form)
			{
				form.BackColor = Background;
				form.ForeColor = Text;
			}
			else if (control is DataGridView grid)
				StyleGrid(grid);
			else if (control is TreeView tree)
			{
				tree.BackColor = Surface;
				tree.ForeColor = Text;
				tree.LineColor = Border;
				tree.BorderStyle = BorderStyle.FixedSingle;
			}
			else if (control is TabPage page)
			{
				page.UseVisualStyleBackColor = false;
				page.BackColor = Background;
				page.ForeColor = Text;
			}
			else if (control is UserControl userControl)
			{
				// FiltersControl, EquipmentPieceControl and CantripSelectorControl cards
				userControl.BackColor = Surface;
				userControl.ForeColor = Text;
			}
			else if (control is TextBox textBox)
			{
				textBox.BackColor = Surface;
				textBox.ForeColor = Text;
				textBox.BorderStyle = BorderStyle.FixedSingle;
			}
			else if (control is ListBox listBox)
			{
				listBox.BackColor = Surface;
				listBox.ForeColor = Text;
			}
		}

		static void StyleGrid(DataGridView grid)
		{
			grid.EnableHeadersVisualStyles = false;
			grid.BackgroundColor = Surface;
			grid.GridColor = Border;
			grid.BorderStyle = BorderStyle.FixedSingle;

			// The cantrip selector hides its headers and communicates state purely through cell
			// colors, so selection must stay invisible there. Real data grids get a selection color.
			bool selectable = grid.ColumnHeadersVisible;

			grid.DefaultCellStyle.BackColor = Surface;
			grid.DefaultCellStyle.ForeColor = Text;
			grid.DefaultCellStyle.SelectionBackColor = selectable ? Selection : Surface;
			grid.DefaultCellStyle.SelectionForeColor = Text;

			grid.AlternatingRowsDefaultCellStyle.BackColor = selectable ? SurfaceAlt : Surface;
			grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
			grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = selectable ? Selection : Surface;
			grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Text;

			grid.ColumnHeadersDefaultCellStyle.BackColor = Header;
			grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
			grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
			grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;

			grid.RowHeadersDefaultCellStyle.BackColor = Header;
			grid.RowHeadersDefaultCellStyle.ForeColor = Text;
			grid.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
			grid.RowHeadersDefaultCellStyle.SelectionForeColor = Text;
		}
	}
}
