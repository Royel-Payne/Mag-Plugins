using System.Text.Json;
using System.Text.Json.Serialization;

using MagSuitBuilderWeb.Models;

namespace MagSuitBuilderWasm;

public sealed record WarningDto(string Message);
public sealed record SuitEvictedDto(int SuitId);

// Source-generated serialization: trim/AOT-safe, and payloads stay byte-compatible with the
// camelCase JSON the local web app streams over SSE.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(InventoryDto))]
[JsonSerializable(typeof(CantripsDto))]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SuitDto))]
[JsonSerializable(typeof(SearchStatusDto))]
[JsonSerializable(typeof(WarningDto))]
[JsonSerializable(typeof(SuitEvictedDto))]
public sealed partial class JsonContext : JsonSerializerContext
{
}

// Separate class: putting this options field inside JsonContext would race the source
// generator's own static initialization and silently produce a resolver-less options object.
public static class Json
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		TypeInfoResolver = JsonContext.Default,
	};
}
