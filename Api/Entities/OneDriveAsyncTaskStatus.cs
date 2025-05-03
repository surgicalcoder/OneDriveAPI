using System.Text.Json.Serialization;
using KoenZomers.OneDrive.Api.Enums;

namespace KoenZomers.OneDrive.Api.Entities;

public class OneDriveAsyncTaskStatus : OneDriveItemBase
{
    [JsonPropertyName("operation")]
    public OneDriveAsyncJobType Operation { get; set; }

    [JsonPropertyName("percentageComplete")]
    public double PercentComplete { get; set; }

    [JsonPropertyName("status")]
    public OneDriveAsyncJobStatus Status { get; set; }
}