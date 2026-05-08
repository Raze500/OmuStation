using Content.Shared._DV.CartridgeLoader.Cartridges;
using Robust.Shared.Serialization;

namespace Content.Shared._Omu.MobilePhone;

[Serializable, NetSerializable]
public sealed class MobilePhoneBuiMessage : BoundUserInterfaceMessage
{
    public readonly NanoChatUiMessageType Type;
    public readonly uint? RecipientNumber;
    public readonly string? Content;
    public readonly string? RecipientJob;

    public MobilePhoneBuiMessage(NanoChatUiMessageType type,
        uint? recipientNumber = null,
        string? content = null,
        string? recipientJob = null)
    {
        Type = type;
        RecipientNumber = recipientNumber;
        Content = content;
        RecipientJob = recipientJob;
    }
}
