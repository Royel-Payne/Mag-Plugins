using System.Diagnostics;
using System.Text.Json;

using Mag.Shared.Spells;

using Mag_SuitBuilder;

using MagSuitBuilderWeb.Models;
using MagSuitBuilderWeb.Services;

MagSuitBuilderWeb.Services.ConsoleInterop.AttachParent();

int requestedPort = 5100;
bool openBrowser = true;

for (int i = 0; i < args.Length; i++)
{
	if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
		requestedPort = p;
	if (args[i] == "--no-browser")
		openBrowser = false;
}

// Fail fast if the embedded spell table didn't resolve (RootNamespace/resource-name contract)
if (SpellTools.GetSpell(2583) == null)
{
	Console.Error.WriteLine("FATAL: embedded Spells.csv did not load (resource name mismatch). Check RootNamespace.");
	System.Windows.Forms.MessageBox.Show("The embedded spell table failed to load — the app cannot run.", "Mag-SuitBuilder");
	return 1;
}

WebApplication BuildApp(string url)
{
	var builder = WebApplication.CreateBuilder(args);

	builder.Logging.SetMinimumLevel(LogLevel.Information);
	builder.Services.AddSingleton<InventoryStore>();
	builder.Services.AddSingleton<EventHub>();
	builder.Services.AddSingleton<SearchService>();
	builder.WebHost.UseUrls(url);

	var app = builder.Build();

	var inventory = app.Services.GetRequiredService<InventoryStore>();
	var hub = app.Services.GetRequiredService<EventHub>();
	var search = app.Services.GetRequiredService<SearchService>();
	var log = app.Services.GetRequiredService<ILogger<Program>>();

	inventory.IsSearchRunning = () => search.IsRunning;

	SearchDiagnostics.Notify = msg =>
	{
		log.LogWarning("{Message}", msg);
		hub.Publish("warning", new { message = msg });
	};

	if (Directory.Exists(app.Environment.WebRootPath))
	{
		// Development: serve live files from the physical wwwroot
		app.UseDefaultFiles();
		app.UseStaticFiles(new StaticFileOptions
		{
			// Always revalidate: browsers otherwise cache JS/CSS heuristically and serve stale UI
			// after an app update. ETags make this a cheap 304 on unchanged files.
			OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
		});
	}
	else
	{
		// Published single-file exe: the UI is embedded in the assembly
		EmbeddedWwwroot.Use(app);
	}

	// ---- Inventory ----

	app.MapPost("/api/inventory/load", (LoadRequest body) =>
	{
		if (!inventory.Load(body?.RootPath, out var error))
			return search.IsRunning ? Results.Conflict(new { error }) : Results.BadRequest(new { error });

		return Results.Ok(inventory.Snapshot());
	});

	app.MapGet("/api/inventory", () => Results.Ok(inventory.Snapshot()));

	app.MapPost("/api/items/{itemKey:int}/flags", (int itemKey, ItemFlagsRequest body) =>
	{
		if (!inventory.SetFlags(itemKey, body?.Locked, body?.Excluded, out var error))
			return search.IsRunning ? Results.Conflict(new { error }) : Results.NotFound(new { error });

		return Results.Ok();
	});

	// ---- Cantrips ----

	app.MapGet("/api/cantrips", () => Results.Ok(CantripCatalog.ToDto()));

	// ---- Search ----

	app.MapPost("/api/search", (SearchRequest request) =>
	{
		try
		{
			var session = search.Start(request);
			return Results.Accepted("/api/search/status", new { searchId = session.SearchId });
		}
		catch (SearchValidationException ex)
		{
			return Results.BadRequest(new { error = ex.Message });
		}
		catch (InvalidOperationException ex)
		{
			return Results.Conflict(new { error = ex.Message });
		}
	});

	app.MapPost("/api/search/stop", () =>
		search.Stop() ? Results.Ok() : Results.NotFound(new { error = "No active search." }));

	app.MapGet("/api/search/status", () =>
	{
		var session = search.Current;
		return session == null
			? Results.NotFound(new { error = "No search has run yet." })
			: Results.Ok(session.Status());
	});

	app.MapGet("/api/search/suits", (int? top) =>
	{
		var session = search.Current;
		return session == null
			? Results.NotFound(new { error = "No search has run yet." })
			: Results.Ok(session.Suits.RankedSnapshot(top is > 0 ? top.Value : SuitStore.Capacity).Select(e => e.Dto));
	});

	app.MapGet("/api/search/suits/{suitId:int}", (int suitId) =>
	{
		var entry = search.Current?.Suits.Get(suitId);
		return entry == null ? Results.NotFound() : Results.Ok(entry.Dto);
	});

	// ---- SSE ----

	var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	app.MapGet("/api/search/events", async (HttpContext context) =>
	{
		context.Response.Headers.ContentType = "text/event-stream";
		context.Response.Headers.CacheControl = "no-cache";
		context.Response.Headers["X-Accel-Buffering"] = "no";

		using var subscription = hub.Subscribe();

		// Snapshot first, so a (re)connecting client is always consistent without event replay
		var session = search.Current;
		var snapshot = new
		{
			searchId = session?.SearchId,
			state = session == null ? "Idle" : session.Status().State,
			status = session?.Status(),
			suits = session == null
				? Array.Empty<SuitDto>()
				: session.Suits.RankedSnapshot(SuitStore.Capacity).Select(e => e.Dto).ToArray(),
		};

		await WriteEventAsync(context, 0, "snapshot", JsonSerializer.Serialize(snapshot, jsonOptions));

		try
		{
			await foreach (var evt in subscription.Reader.ReadAllAsync(context.RequestAborted))
				await WriteEventAsync(context, evt.EventId, evt.Type, evt.JsonData);
		}
		catch (OperationCanceledException)
		{
			// client disconnected
		}
	});

	app.Lifetime.ApplicationStopping.Register(() => search.Stop());

	return app;
}

static async Task WriteEventAsync(HttpContext context, long id, string type, string jsonData)
{
	await context.Response.WriteAsync($"id: {id}\nevent: {type}\ndata: {jsonData}\n\n", context.RequestAborted);
	await context.Response.Body.FlushAsync(context.RequestAborted);
}

var app = BuildApp($"http://127.0.0.1:{requestedPort}");

try
{
	app.Start();
}
catch (IOException)
{
	Console.WriteLine($"Port {requestedPort} is in use; picking a free port.");
	await app.DisposeAsync();
	app = BuildApp("http://127.0.0.1:0");
	app.Start();
}

var appLog = app.Services.GetRequiredService<ILogger<Program>>();
var appInventory = app.Services.GetRequiredService<InventoryStore>();
var url = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{requestedPort}";

appLog.LogInformation("Mag-SuitBuilder Web running at {Url}", url);

// Load inventory from the default path on startup so the first page load has data
if (!appInventory.Load(null, out var loadError))
	appLog.LogWarning("Initial inventory load failed: {Error}", loadError);
else
	appLog.LogInformation("Loaded inventory from {Path}", appInventory.RootPath);

if (openBrowser)
{
	try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
	catch (Exception ex) { appLog.LogWarning("Could not open browser: {Error}", ex.Message); }
}

// No console window (WinExe): the tray icon is how the user opens and exits the app
TrayRunner.Start(url, () => app.Lifetime.StopApplication());

app.WaitForShutdown();
TrayRunner.HideIcon();
return 0;
