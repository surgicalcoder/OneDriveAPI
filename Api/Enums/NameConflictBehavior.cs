using System.Runtime.Serialization;

namespace GoLive.OneDrive.Api.Enums;

public enum NameConflictBehavior
{
    [EnumMember(Value = "fail")]
    Fail,

    [EnumMember(Value = "replace")]
    Replace,

    [EnumMember(Value = "rename")]
    Rename
}