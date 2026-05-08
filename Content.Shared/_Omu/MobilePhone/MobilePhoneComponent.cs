using Robust.Shared.GameStates;

namespace Content.Shared._Omu.MobilePhone;

/// <summary>
///     Marks an entity as a standalone mobile phone that can access NanoChat.
///     The entity must also have a <see cref="Content.Shared._DV.NanoChat.NanoChatCardComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MobilePhoneComponent : Component
{
}
