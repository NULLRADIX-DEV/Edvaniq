using System.Text.Json;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Api;

// The one place that names an origin for the client: material or synthetic (#349). The client labels what has no
// material behind it as "ohne Materialbeleg".
internal static class Origins
{
    public static string Name(DocumentOrigin origin) => JsonNamingPolicy.CamelCase.ConvertName(origin.ToString());
}