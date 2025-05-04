using System.Text.Json.Serialization;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveSpecialFolderFacet
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}