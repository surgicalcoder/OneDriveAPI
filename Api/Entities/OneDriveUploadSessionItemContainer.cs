using System.Text.Json.Serialization;

namespace GoLive.OneDrive.Api.Entities;

internal class OneDriveUploadSessionItemContainer : OneDriveItemBase
{
    [JsonPropertyName("item")]
    public OneDriveUploadSessionItem Item { get; set; }
}