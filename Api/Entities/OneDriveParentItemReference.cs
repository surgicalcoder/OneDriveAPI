using System.Text.Json.Serialization;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveParentItemReference : OneDriveItemBase
{
    [JsonPropertyName("parentReference")]
    public OneDriveItemReference ParentReference { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}