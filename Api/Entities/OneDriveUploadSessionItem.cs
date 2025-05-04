using System.Text.Json.Serialization;
using GoLive.OneDrive.Api.Enums;

namespace GoLive.OneDrive.Api.Entities;

internal class OneDriveUploadSessionItem
{
    [JsonPropertyName("@microsoft.graph.conflictBehavior")]
    public NameConflictBehavior FilenameConflictBehavior { get; set; }

    [JsonPropertyName("name")]
    public string Filename { get; set; }
}