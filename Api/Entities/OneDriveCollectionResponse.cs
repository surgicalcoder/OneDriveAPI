﻿using System.Text.Json.Serialization;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveCollectionResponse<T> : OneDriveItemBase
{
    [JsonPropertyName("value")]
    public T[] Collection { get; set; }

    [JsonPropertyName("@odata.nextLink")]
    public string NextLink { get; set; }
}