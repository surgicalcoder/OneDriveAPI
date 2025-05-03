using System.Text.Json.Serialization;
using KoenZomers.OneDrive.Api.Enums;

namespace KoenZomers.OneDrive.Api.Entities;

internal class OneDriveUploadSessionItem
{
    [JsonPropertyName("@microsoft.graph.conflictBehavior")]
    public NameConflictBehavior FilenameConflictBehavior { get; set; }

    [JsonPropertyName("name")]
    public string Filename { get; set; }
}