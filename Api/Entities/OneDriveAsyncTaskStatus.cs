using System.Text.Json.Serialization;
using GoLive.OneDrive.Api.Enums;

namespace GoLive.OneDrive.Api.Entities;

public class OneDriveAsyncTaskStatus : OneDriveItemBase
{
    [JsonPropertyName("operation")]
    public OneDriveAsyncJobType Operation { get; set; }

    [JsonPropertyName("percentageComplete")]
    public double PercentComplete { get; set; }

    [JsonPropertyName("status")]
    public OneDriveAsyncJobStatus Status { get; set; }
}