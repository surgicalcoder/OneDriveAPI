﻿using System.Text.Json.Serialization;
using GoLive.OneDrive.Api.Enums;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveSharingLinkFacet
{
    /// <summary>
    /// Url to access the item on which the permissions are applied
    /// </summary>
    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; }

    [JsonPropertyName("application")]
    public OneDriveIdentity Application { get; set; }

    /// <summary>
    /// Type of rights assigned to this item
    /// </summary>
    [JsonPropertyName("type")]
    public OneDriveLinkType Type { get; set; }
}