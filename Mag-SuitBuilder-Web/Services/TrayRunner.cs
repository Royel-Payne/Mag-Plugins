using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MagSuitBuilderWeb.Services;

/// <summary>
/// The app runs windowless (WinExe, no console), so the system tray icon is how the user knows
/// it's alive and how they open or exit it. Runs a WinForms message loop on its own STA thread.
/// </summary>
public static class TrayRunner
{
	static NotifyIcon icon;

	public static Thread Start(string url, Action requestShutdown)
	{
		var thread = new Thread(() => Run(url, requestShutdown))
		{
			IsBackground = true,
			Name = "TrayIcon",
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return thread;
	}

	static void Run(string url, Action requestShutdown)
	{
		void Open()
		{
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch { }
		}

		var menu = new ContextMenuStrip();
		menu.Items.Add("Open Mag-SuitBuilder", null, (_, _) => Open());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Exit", null, (_, _) =>
		{
			HideIcon();
			requestShutdown();
			Application.ExitThread();
		});

		icon = new NotifyIcon
		{
			Icon = CreateShieldIcon(),
			Text = "Mag-SuitBuilder — " + url,
			Visible = true,
			ContextMenuStrip = menu,
		};

		icon.DoubleClick += (_, _) => Open();

		try
		{
			Application.Run();
		}
		finally
		{
			HideIcon();
		}
	}

	/// <summary>Hide before exit so no ghost icon lingers in the tray.</summary>
	public static void HideIcon()
	{
		try
		{
			if (icon != null)
			{
				icon.Visible = false;
				icon.Dispose();
				icon = null;
			}
		}
		catch { }
	}

	// The amber shield from the app's favicon, drawn at runtime so no .ico file needs shipping
	static Icon CreateShieldIcon()
	{
		using var bmp = new Bitmap(32, 32);

		using (var g = Graphics.FromImage(bmp))
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;

			using var path = new GraphicsPath();
			path.AddPolygon(new[]
			{
				new PointF(16, 2), new PointF(29, 7), new PointF(29, 15),
				new PointF(16, 30), new PointF(3, 15), new PointF(3, 7),
			});

			using var pen = new Pen(Color.FromArgb(251, 146, 60), 3f) { LineJoin = LineJoin.Round };
			g.DrawPath(pen, path);
		}

		nint handle = bmp.GetHicon();
		using var temp = Icon.FromHandle(handle);
		var result = (Icon)temp.Clone();
		DestroyIcon(handle);
		return result;
	}

	[DllImport("user32.dll")]
	static extern bool DestroyIcon(nint handle);
}

/// <summary>Reattaches the parent console so `dotnet run` from a terminal still shows logs.</summary>
public static class ConsoleInterop
{
	[DllImport("kernel32.dll")]
	static extern bool AttachConsole(int processId);

	public static void AttachParent()
	{
		try { AttachConsole(-1); }
		catch { }
	}
}
