using System.Runtime.Serialization;

namespace GoLive.OneDrive.Api.Enums;

public enum OneDriveKnownFolder
{
    [EnumMember(Value = "approot")]
    AppFolder,

    [EnumMember(Value = "documents")]
    Documents,

    [EnumMember(Value = "photos")]
    Photos,

    [EnumMember(Value = "cameraroll")]
    CameraRoll,

    [EnumMember(Value = "public")]
    Public
}