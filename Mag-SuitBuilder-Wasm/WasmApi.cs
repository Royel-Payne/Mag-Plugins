using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using Mag.Shared.Spells;

using MagSuitBuilderWeb.Models;
using MagSuitBuilderWeb.Services;

namespace MagSuitBuilderWasm;

/// <summary>
/// The JS-facing surface. Lives inside a Web Worker (see wwwroot/worker.js); events flow out
/// through the imported events.emit and carry the same names/payloads as the local web app's
/// SSE stream, so the UI's adapter is a drop-in replacement for its fetch/EventSource layer.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class WasmApi
{
	static readonly WasmInventoryStore Inventory = new();
	static WasmSearchRunner current;

	[JSImport("events.emit", "worker")]
	internal static partial void Emit(string type, string json);

	[JSExport]
	public static string LoadInventory(string[] relativePaths, string[] xmlContents)
	{
		if (SpellTools.GetSpell(2583) == null)
			throw new InvalidOperationException("Embedded spell table failed to load.");

		var snapshot = Inventory.Load(relativePaths, xmlContents);
		return JsonSerializer.Serialize(snapshot, Json.Options);
	}

	[JSExport]
	public static string GetCantrips()
	{
		return JsonSerializer.Serialize(CantripCatalog.ToDto(), Json.Options);
	}

	[JSExport]
	public static bool SetItemFlags(int itemKey, bool locked, bool excluded, bool hasLocked, bool hasExcluded)
	{
		return Inventory.SetFlags(itemKey, hasLocked ? locked : null, hasExcluded ? excluded : null);
	}

	[JSExport]
	public static void StartSearch(string requestJson)
	{
		if (current is { IsCompleted: false })
			throw new InvalidOperationException("A search is already running.");

		var request = JsonSerializer.Deserialize<SearchRequest>(requestJson, Json.Options);

		var runner = new WasmSearchRunner(request, Inventory, Emit);
		runner.Prepare(); // throws SearchValidationException with a user-readable message
		current = runner;

		Mag_SuitBuilder.SearchDiagnostics.Notify = msg =>
			Emit("warning", JsonSerializer.Serialize(new WarningDto(msg), Json.Options));

		// Fire and forget: RunAsync yields before the heavy work, so this export returns
		// immediately and results stream out via events while the worker loop is busy.
		_ = runner.RunAsync();
	}

	[JSExport]
	public static void StopSearch()
	{
		current?.Stop();
	}

	[JSExport]
	public static string GetStatus()
	{
		return current == null ? null : JsonSerializer.Serialize(current.Status(), Json.Options);
	}
}

public static class Program
{
	public static void Main()
	{
		// Exports-only app: the runtime is driven from worker.js via WasmApi.
	}
}
