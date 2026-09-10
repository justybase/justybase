using System.Text.Json.Serialization;

namespace JustyBase.PluginCommon.Models;

/// <summary>
/// Host-side options for the UCanAccess database provider. Passwords remain in
/// <see cref="LoginDataModel.Password"/> and are never duplicated here.
/// </summary>
public sealed record AccessConnectionOptions
{
    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; } = true;

    [JsonPropertyName("showSchema")]
    public bool ShowSchema { get; init; }

    [JsonPropertyName("allowExternalLinks")]
    public bool AllowExternalLinks { get; init; }

    [JsonPropertyName("lazyLoad")]
    public bool LazyLoad { get; init; } = true;

    [JsonPropertyName("mirrorMode")]
    public string MirrorMode { get; init; } = "memory";
}
