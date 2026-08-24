using System.Reflection;

namespace MagSuitBuilderWeb.Services;

/// <summary>
/// Serves the UI from embedded resources so a published single-file exe needs no loose wwwroot
/// folder. During development (running from the project folder) the physical wwwroot exists and the
/// normal static-file middleware serves live files instead; this is only the fallback.
/// </summary>
public static class EmbeddedWwwroot
{
	static readonly Assembly Assembly = typeof(EmbeddedWwwroot).Assembly;
	const string Prefix = "MagSuitBuilderWeb.wwwroot.";

	static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".html"] = "text/html; charset=utf-8",
		[".css"] = "text/css; charset=utf-8",
		[".js"] = "text/javascript; charset=utf-8",
		[".json"] = "application/json",
		[".svg"] = "image/svg+xml",
		[".png"] = "image/png",
		[".ico"] = "image/x-icon",
		[".woff2"] = "font/woff2",
	};

	public static bool HasResources()
	{
		return Assembly.GetManifestResourceNames().Any(n => n.StartsWith(Prefix, StringComparison.Ordinal));
	}

	public static void Use(WebApplication app)
	{
		app.Use(async (context, next) =>
		{
			if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
			{
				await next();
				return;
			}

			var path = context.Request.Path.Value ?? "/";
			if (path == "/")
				path = "/index.html";

			// "/js/components/topbar.js" -> "MagSuitBuilderWeb.wwwroot.js.components.topbar.js"
			var resourceName = Prefix + path.TrimStart('/').Replace('/', '.');
			var extension = Path.GetExtension(path);

			if (!ContentTypes.TryGetValue(extension, out var contentType))
			{
				await next();
				return;
			}

			var stream = Assembly.GetManifestResourceStream(resourceName);
			if (stream == null)
			{
				await next();
				return;
			}

			await using (stream)
			{
				context.Response.ContentType = contentType;
				context.Response.Headers.CacheControl = "no-cache";
				context.Response.ContentLength = stream.Length;

				if (!HttpMethods.IsHead(context.Request.Method))
					await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
			}
		});
	}
}
