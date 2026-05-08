using Content.Client._DV.CartridgeLoader.Cartridges;
using Content.Client.UserInterface.Controls;
using Content.Shared._DV.CartridgeLoader.Cartridges;
using Content.Shared._Omu.MobilePhone;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Omu.MobilePhone;

[UsedImplicitly]
public sealed class MobilePhoneBoundUserInterface : BoundUserInterface
{
    private FancyWindow? _window;
    private NanoChatUiFragment? _fragment;

    public MobilePhoneBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _fragment = new NanoChatUiFragment();
        _fragment.OnMessageSent += (type, number, content, job) =>
        {
            SendMessage(new MobilePhoneBuiMessage(type, number, content, job));
        };

        _window = new FancyWindow
        {
            Title = Loc.GetString("nano-chat-title"),
            SetSize = (500, 400),
        };
        _window.Contents.AddChild(_fragment);
        _window.OpenCentered();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is NanoChatUiState cast)
            _fragment?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Dispose();
    }
}
