// Modified for the Shadowgain fork (dark color mode and diagnostics wiring), 2026-08-24. See git history for details. [LGPL 2.1]
using System;
using System.Windows.Forms;

namespace Mag_SuitBuilder
{
	internal static class Program
	{
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			// To customize application configuration such as set high DPI settings or default font,
			// see https://aka.ms/applicationconfiguration.
			ApplicationConfiguration.Initialize();

#pragma warning disable WFO5001 // SetColorMode is marked experimental
			try { Application.SetColorMode(SystemColorMode.Dark); }
			catch { } // Older Windows builds without dark mode support
#pragma warning restore WFO5001

			SearchDiagnostics.Notify = msg => MessageBox.Show(msg);

			Application.Run(new Form1());
		}
	}
}