using System;

namespace Mag_SuitBuilder
{
	/// <summary>
	/// Pluggable sink for non-fatal solver warnings, so the search code stays UI-free.
	/// The WinForms app wires this to MessageBox.Show; the web host wires it to logging.
	/// Defaults to a no-op.
	/// </summary>
	public static class SearchDiagnostics
	{
		public static Action<string> Notify = _ => { };
	}
}
