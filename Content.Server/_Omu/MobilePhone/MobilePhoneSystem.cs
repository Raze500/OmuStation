using Content.Server.Administration.Logs;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.Station.Systems;
using Content.Server._DV.CartridgeLoader.Cartridges;
using Content.Shared._DV.CartridgeLoader.Cartridges;
using Content.Shared._DV.NanoChat;
using Content.Shared._Omu.MobilePhone;
using Content.Shared.Access.Components;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Radio.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Omu.MobilePhone;

public sealed class MobilePhoneSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedNanoChatSystem _nanoChat = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;

    private int _maxNameLength;
    private int _maxIdJobLength;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobilePhoneComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<MobilePhoneComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<MobilePhoneComponent, MobilePhoneBuiMessage>(OnMessage);
        SubscribeLocalEvent<NanoChatMessageReceivedEvent>(OnGlobalMessageReceived);

        Subs.CVar(_cfgManager, CCVars.MaxNameLength, value => _maxNameLength = value, true);
        Subs.CVar(_cfgManager, CCVars.MaxIdJobLength, value => _maxIdJobLength = value, true);
    }

    private void OnUiOpened(Entity<MobilePhoneComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (TryComp<NanoChatCardComponent>(ent.Owner, out var card))
            _nanoChat.SetClosed((ent.Owner, card), false);

        UpdateUI(ent);
    }

    private void OnUiClosed(Entity<MobilePhoneComponent> ent, ref BoundUIClosedEvent args)
    {
        if (TryComp<NanoChatCardComponent>(ent.Owner, out var card))
            _nanoChat.SetClosed((ent.Owner, card), true);
    }

    private void OnGlobalMessageReceived(ref NanoChatMessageReceivedEvent args)
    {
        if (!TryComp<MobilePhoneComponent>(args.CardUid, out var phone))
            return;

        UpdateUI((args.CardUid, phone));
    }

    private void OnMessage(Entity<MobilePhoneComponent> ent, ref MobilePhoneBuiMessage msg)
    {
        if (!TryComp<NanoChatCardComponent>(ent.Owner, out var card))
            return;

        var cardEnt = (ent.Owner, card);

        switch (msg.Type)
        {
            case NanoChatUiMessageType.NewChat:
                HandleNewChat(cardEnt, msg);
                break;
            case NanoChatUiMessageType.SelectChat:
                HandleSelectChat(cardEnt, msg);
                break;
            case NanoChatUiMessageType.CloseChat:
                _nanoChat.SetCurrentChat(cardEnt, null);
                break;
            case NanoChatUiMessageType.ToggleMute:
                _nanoChat.SetNotificationsMuted(cardEnt, !_nanoChat.GetNotificationsMuted(cardEnt));
                break;
            case NanoChatUiMessageType.DeleteChat:
                HandleDeleteChat(cardEnt, msg);
                break;
            case NanoChatUiMessageType.SendMessage:
                HandleSendMessage(ent, cardEnt, msg);
                break;
            case NanoChatUiMessageType.ToggleListNumber:
                _nanoChat.SetListNumber(cardEnt, !_nanoChat.GetListNumber(cardEnt));
                break;
        }

        UpdateUI(ent);
    }

    private void HandleNewChat(Entity<NanoChatCardComponent> card, MobilePhoneBuiMessage msg)
    {
        if (msg.RecipientNumber == null || msg.Content == null || msg.RecipientNumber == card.Comp.Number)
            return;

        var name = msg.Content.Trim();
        if (name.Length > _maxNameLength)
            name = name[.._maxNameLength];

        var jobTitle = msg.RecipientJob?.Trim();
        if (jobTitle?.Length > _maxIdJobLength)
            jobTitle = jobTitle[.._maxIdJobLength];

        _nanoChat.SetRecipient(card, msg.RecipientNumber.Value, new NanoChatRecipient(msg.RecipientNumber.Value, name, jobTitle));

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"Mobile phone {ToPrettyString(card.Owner)} created NanoChat conversation with #{msg.RecipientNumber:D4}");
    }

    private void HandleSelectChat(Entity<NanoChatCardComponent> card, MobilePhoneBuiMessage msg)
    {
        if (msg.RecipientNumber == null)
            return;

        _nanoChat.SetCurrentChat(card, msg.RecipientNumber);

        if (_nanoChat.GetRecipient(card, msg.RecipientNumber.Value) is { } recipient)
            _nanoChat.SetRecipient(card, msg.RecipientNumber.Value, recipient with { HasUnread = false });
    }

    private void HandleDeleteChat(Entity<NanoChatCardComponent> card, MobilePhoneBuiMessage msg)
    {
        if (msg.RecipientNumber == null)
            return;

        _nanoChat.TryDeleteChat(card, msg.RecipientNumber.Value, true);

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"Mobile phone {ToPrettyString(card.Owner)} deleted NanoChat conversation with #{msg.RecipientNumber:D4}");
    }

    private void HandleSendMessage(Entity<MobilePhoneComponent> phone,
        Entity<NanoChatCardComponent> card,
        MobilePhoneBuiMessage msg)
    {
        if (msg.RecipientNumber == null || msg.Content == null || card.Comp.Number == null)
            return;

        if (!_nanoChat.EnsureRecipientExists(card, msg.RecipientNumber.Value, GetCardInfo(msg.RecipientNumber.Value)))
            return;

        var content = FormattedMessage.EscapeText(msg.Content.Trim());
        if (content.Length > NanoChatMessage.MaxContentLength)
            content = content[..NanoChatMessage.MaxContentLength];

        var message = new NanoChatMessage(_timing.CurTime, content, card.Comp.Number.Value);

        var (deliveryFailed, recipients) = AttemptMessageDelivery(phone, msg.RecipientNumber.Value);
        message = message with { DeliveryFailed = deliveryFailed };

        _nanoChat.AddMessage(card, msg.RecipientNumber.Value, message);

        _adminLogger.Add(LogType.Chat,
            LogImpact.Low,
            $"Mobile phone {ToPrettyString(phone.Owner)} sent NanoChat to #{msg.RecipientNumber:D4}: {content}{(deliveryFailed ? " [FAILED]" : "")}");

        if (deliveryFailed)
            return;

        foreach (var recipient in recipients)
            DeliverMessageToRecipient(card, recipient, message);
    }

    private (bool failed, List<Entity<NanoChatCardComponent>> recipients) AttemptMessageDelivery(
        Entity<MobilePhoneComponent> phone,
        uint recipientNumber)
    {
        if (!HasComp<ActiveRadioComponent>(phone.Owner))
            return (true, new List<Entity<NanoChatCardComponent>>());

        var foundCards = new List<Entity<NanoChatCardComponent>>();
        var cardQuery = EntityQueryEnumerator<NanoChatCardComponent>();
        while (cardQuery.MoveNext(out var cardUid, out var card))
        {
            if (card.Number == recipientNumber)
                foundCards.Add((cardUid, card));
        }

        if (foundCards.Count == 0)
            return (true, foundCards);

        var senderStation = _station.GetOwningStation(phone.Owner);
        var deliverable = new List<Entity<NanoChatCardComponent>>();

        foreach (var recipient in foundCards)
        {
            // Check PDA cartridge path
            var cartridgeQuery = EntityQueryEnumerator<NanoChatCartridgeComponent, ActiveRadioComponent>();
            var found = false;
            while (cartridgeQuery.MoveNext(out var receiverUid, out var receiverCart, out _))
            {
                if (receiverCart.Card != recipient.Owner)
                    continue;

                var recipientStation = _station.GetOwningStation(receiverUid);
                if (recipientStation == null || senderStation == null)
                    continue;
                if (recipientStation != senderStation)
                    continue;
                if (!HasActiveServer(senderStation.Value) || !HasActiveServer(recipientStation.Value))
                    continue;

                deliverable.Add(recipient);
                found = true;
                break;
            }

            if (found)
                continue;

            // Check standalone mobile phone path
            if (!HasComp<MobilePhoneComponent>(recipient.Owner) || !HasComp<ActiveRadioComponent>(recipient.Owner))
                continue;

            var phoneStation = _station.GetOwningStation(recipient.Owner);
            if (phoneStation == null || senderStation == null)
                continue;
            if (phoneStation != senderStation)
                continue;
            if (!HasActiveServer(senderStation.Value) || !HasActiveServer(phoneStation.Value))
                continue;

            deliverable.Add(recipient);
        }

        return (deliverable.Count == 0, deliverable);
    }

    private void DeliverMessageToRecipient(Entity<NanoChatCardComponent> sender,
        Entity<NanoChatCardComponent> recipient,
        NanoChatMessage message)
    {
        var senderNumber = sender.Comp.Number;
        if (senderNumber == null)
            return;

        if (!_nanoChat.EnsureRecipientExists(recipient, senderNumber.Value, GetCardInfo(senderNumber.Value)))
            return;

        _nanoChat.AddMessage(recipient, senderNumber.Value, message with { DeliveryFailed = false });

        if (_nanoChat.GetCurrentChat(recipient) != senderNumber)
        {
            var senderRecipient = _nanoChat.GetRecipient(recipient, senderNumber.Value);
            if (senderRecipient is { } sr)
                _nanoChat.SetRecipient(recipient, senderNumber.Value, sr with { HasUnread = true });
        }

        var msgEv = new NanoChatMessageReceivedEvent(recipient.Owner);
        RaiseLocalEvent(ref msgEv);
    }

    private NanoChatRecipient? GetCardInfo(uint number)
    {
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number != number)
                continue;

            string? jobTitle = null;
            var name = "Unknown";
            if (TryComp<IdCardComponent>(uid, out var idCard))
            {
                jobTitle = idCard.LocalizedJobTitle;
                name = idCard.FullName ?? name;
            }

            return new NanoChatRecipient(number, name, jobTitle);
        }

        return null;
    }

    private bool HasActiveServer(EntityUid station)
    {
        var query = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var power))
        {
            if (_station.GetOwningStation(uid) == station && power.Powered)
                return true;
        }

        return false;
    }

    public void UpdateUI(Entity<MobilePhoneComponent> phone)
    {
        if (!TryComp<NanoChatCardComponent>(phone.Owner, out var card))
            return;

        List<NanoChatRecipient>? contacts = null;
        if (_station.GetOwningStation(phone.Owner) is { } station)
        {
            contacts = new List<NanoChatRecipient>();
            var query = AllEntityQuery<NanoChatCardComponent, IdCardComponent>();
            while (query.MoveNext(out var entityId, out var nanoChatCard, out var idCardComp))
            {
                if (nanoChatCard.ListNumber &&
                    nanoChatCard.Number is uint num &&
                    idCardComp.FullName is string fullName &&
                    _station.GetOwningStation(entityId) == station)
                {
                    contacts.Add(new NanoChatRecipient(num, fullName));
                }
            }
            contacts.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        var state = new NanoChatUiState(
            card.Recipients,
            card.Messages,
            contacts,
            card.CurrentChat,
            card.Number ?? 0,
            card.MaxRecipients,
            card.NotificationsMuted,
            card.ListNumber);

        _ui.SetUiState(phone.Owner, MobilePhoneUiKey.Key, state);
    }
}
