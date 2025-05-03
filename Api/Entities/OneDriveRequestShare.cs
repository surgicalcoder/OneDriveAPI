using System.Text.Json.Serialization;
using KoenZomers.OneDrive.Api.Enums;

namespace KoenZomers.OneDrive.Api.Entities;

/// <summary>
/// Message to request sharing of an item
/// </summary>
internal class OneDriveRequestShare : OneDriveItemBase
{
    /// <summary>
    /// Type of sharing to request
    /// </summary>
    [JsonPropertyName("type")]
    public OneDriveLinkType SharingType { get; set; }

    /// <summary>
    /// Scope of the access to the shared item
    /// </summary>
    [JsonPropertyName("scope")]
    public OneDriveSharingScope? Scope { get; set; }
}