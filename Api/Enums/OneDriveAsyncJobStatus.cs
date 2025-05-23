﻿using System.Runtime.Serialization;

namespace GoLive.OneDrive.Api.Enums;

public enum OneDriveAsyncJobStatus
{
    [EnumMember(Value = "NotStarted")]
    NotStarted,

    [EnumMember(Value = "InProgress")]
    InProgress,

    [EnumMember(Value = "Complete")]
    Complete
}