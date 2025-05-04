using System.Text.Json.Serialization;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveFileFacet
{
    [JsonPropertyName("hashes")]
    public OneDriveHashesFacet Hashes { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; }
}