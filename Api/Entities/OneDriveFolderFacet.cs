using System.Text.Json.Serialization;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveFolderFacet
{
    [JsonPropertyName("childCount")]
    public long ChildCount { get; set; }
}