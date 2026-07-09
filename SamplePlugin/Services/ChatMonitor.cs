using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class ChatMonitor : IDisposable
{
    private static readonly Regex EchoDebugPattern = new(
        @"^\s*aic(?:\s+|:)(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IChatGui chatGui;
    private readonly IPlayerState playerState;
    private readonly Configuration configuration;
    private readonly ActivityLog log;
    private readonly AdminCommandHandler adminHandler;
    private readonly PlayerMemory playerMemory;
    private readonly OutboundEchoGuard echoGuard;
    private readonly InventoryService inventoryService;
    private readonly PresetTradeService presetTradeService;
    private readonly TravelIntentService travelIntentService;
    private readonly InteractionSafetyService safetyService;

    public event Action<ChatContextEntry>? OnEvent;

    public ChatMonitor(
        IChatGui chatGui,
        IPlayerState playerState,
        Configuration configuration,
        ActivityLog log,
        AdminCommandHandler adminHandler,
        PlayerMemory playerMemory,
        OutboundEchoGuard echoGuard,
        InventoryService inventoryService,
        PresetTradeService presetTradeService,
        TravelIntentService travelIntentService,
        InteractionSafetyService safetyService)
    {
        this.chatGui = chatGui;
        this.playerState = playerState;
        this.configuration = configuration;
        this.log = log;
        this.adminHandler = adminHandler;
        this.playerMemory = playerMemory;
        this.echoGuard = echoGuard;
        this.inventoryService = inventoryService;
        this.presetTradeService = presetTradeService;
        this.travelIntentService = travelIntentService;
        this.safetyService = safetyService;

        chatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!configuration.Enabled)
        {
            return;
        }

        var type = message.LogKind;

        if (TryHandleEchoDebug(message, type))
        {
            return;
        }

        if (message.SourceKind == XivChatRelationKind.LocalPlayer || IsOutgoingTell(type))
        {
            return;
        }

        var channelName = MapChannel(type);
        if (!IsChannelEnabled(channelName))
        {
            return;
        }

        var identity = SeStringPlayerParser.ExtractSender(message.Sender);
        var senderText = identity.Name;
        var senderWorld = identity.World;
        var senderFullId = identity.FullId;
        var messageText = message.Message.TextValue.Trim();

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return;
        }

        if (IsLocalPlayer(senderText))
        {
            return;
        }

        if (echoGuard.IsLikelyEcho(channelName, messageText, senderFullId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(senderText))
        {
            playerMemory.Remember(senderText, senderWorld);
        }

        var isEmote = IsEmoteMessage(messageText) || channelName == "emote";
        if (isEmote && !configuration.ListenEmotes)
        {
            return;
        }

        if (isEmote && !IsEmoteTargetingLocalPlayer(messageText))
        {
            return;
        }

        var mapLink = ExtractMapLink(message.Message);
        var travelIntent = travelIntentService.Analyze(messageText, mapLink != null, configuration.InteractionLanguage);
        var presetHint = presetTradeService.BuildHint(messageText, configuration.InteractionLanguage);
        var inventoryHint = string.IsNullOrEmpty(presetHint)
            ? inventoryService.BuildHint(messageText)
            : presetHint;

        var entry = new ChatContextEntry
        {
            Channel = channelName,
            Sender = senderText,
            SenderWorld = senderWorld,
            SenderFullId = senderFullId,
            Message = messageText,
            IsLocalPlayer = false,
            IsEmote = isEmote,
            InventoryHint = inventoryHint,
            HasMapLink = mapLink != null,
            MapTerritoryId = mapLink?.TerritoryType.RowId ?? 0,
            MapId = mapLink?.Map.RowId ?? 0,
            MapRawX = mapLink?.RawX ?? 0,
            MapRawY = mapLink?.RawY ?? 0,
            MapWorldX = mapLink != null ? mapLink.RawX / 1000f : 0,
            MapWorldZ = mapLink != null ? mapLink.RawY / 1000f : 0,
            MapPlaceName = mapLink?.PlaceName ?? string.Empty,
            MapCoords = mapLink?.CoordinateString ?? string.Empty,
            TravelHint = travelIntent?.Hint ?? string.Empty,
            WantsTravelNavigation = travelIntent?.WantsNavigation ?? false,
            WantsTravelTeleport = travelIntent?.WantsTeleport ?? false,
            TravelAetheryteQuery = travelIntent?.AetheryteQuery ?? string.Empty,
        };

        if (adminHandler.TryHandle(entry))
        {
            return;
        }

        if (!safetyService.CanReceiveInteraction(entry))
        {
            return;
        }

        var lang = configuration.InteractionLanguage;
        var inventoryTag = PluginLocale.InventoryContextPrefix(lang);
        var mapTag = PluginLocale.MapContextPrefix(lang);
        var hintSuffix = string.IsNullOrEmpty(inventoryHint) ? string.Empty : $" {inventoryTag} {inventoryHint}]";
        var mapSuffix = mapLink != null ? $" {mapTag} {mapLink.PlaceName} {mapLink.CoordinateString}]" : string.Empty;
        var travelSuffix = string.IsNullOrEmpty(entry.TravelHint) ? string.Empty : $" {entry.TravelHint}";
        log.Info($"收到 [{channelName}] {senderFullId}: {Truncate(messageText, 80)}{hintSuffix}{mapSuffix}{travelSuffix}");
        OnEvent?.Invoke(entry);
    }

    private bool TryHandleEchoDebug(IHandleableChatMessage message, XivChatType type)
    {
        if (!configuration.EnableEchoDebug || type != XivChatType.Echo)
        {
            return false;
        }

        var messageText = message.Message.TextValue.Trim();
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        var match = EchoDebugPattern.Match(messageText);
        if (!match.Success)
        {
            return false;
        }

        var prompt = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var localName = playerState.CharacterName ?? string.Empty;
        var presetHint = presetTradeService.BuildHint(prompt, configuration.InteractionLanguage);
        var inventoryHint = string.IsNullOrEmpty(presetHint)
            ? inventoryService.BuildHint(prompt)
            : presetHint;

        var entry = new ChatContextEntry
        {
            Channel = "echo",
            Sender = localName,
            SenderFullId = localName,
            Message = prompt,
            IsLocalPlayer = false,
            IsEchoDebug = true,
            InventoryHint = inventoryHint,
        };

        var lang = configuration.InteractionLanguage;
        var inventoryTag = PluginLocale.InventoryContextPrefix(lang);
        var hintSuffix = string.IsNullOrEmpty(inventoryHint) ? string.Empty : $" {inventoryTag} {inventoryHint}]";
        log.Info($"默语调试 [echo] {localName}: {Truncate(prompt, 80)}{hintSuffix}");
        OnEvent?.Invoke(entry);
        return true;
    }

    private static MapLinkPayload? ExtractMapLink(SeString message)
    {
        foreach (var payload in message.Payloads)
        {
            if (payload is MapLinkPayload mapLink)
            {
                return mapLink;
            }
        }

        return null;
    }

    private bool IsLocalPlayer(string senderText)
    {
        var localName = playerState.CharacterName;
        return !string.IsNullOrEmpty(localName) &&
               string.Equals(senderText, localName, StringComparison.Ordinal);
    }

    private bool IsEmoteTargetingLocalPlayer(string messageText)
    {
        var localName = playerState.CharacterName;
        if (!string.IsNullOrWhiteSpace(localName)
            && messageText.Contains(localName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return messageText.Contains("对你", StringComparison.Ordinal)
               || messageText.Contains("對你", StringComparison.Ordinal)
               || messageText.Contains("向你", StringComparison.Ordinal)
               || messageText.Contains("朝你", StringComparison.Ordinal)
               || messageText.Contains("与你", StringComparison.Ordinal)
               || messageText.Contains("與你", StringComparison.Ordinal)
               || messageText.Contains("对您", StringComparison.Ordinal)
               || messageText.Contains("對您", StringComparison.Ordinal)
               || messageText.Contains("向您", StringComparison.Ordinal)
               || messageText.Contains("朝您", StringComparison.Ordinal)
               || messageText.Contains("at you", StringComparison.OrdinalIgnoreCase)
               || messageText.Contains("to you", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOutgoingTell(XivChatType type) =>
        type == XivChatType.TellOutgoing;

    private bool IsChannelEnabled(string channelName) => channelName switch
    {
        "say" => configuration.ListenSay,
        "shout" => configuration.ListenShout,
        "tell" => configuration.ListenTell,
        "fc" => configuration.ListenFc,
        "ls" => configuration.ListenLs,
        "party" => configuration.ListenParty,
        "alliance" => configuration.ListenAlliance,
        "emote" => configuration.ListenEmotes,
        _ => false,
    };

    private static string MapChannel(XivChatType type) => type switch
    {
        XivChatType.Say => "say",
        XivChatType.Shout => "shout",
        XivChatType.TellIncoming or XivChatType.TellOutgoing => "tell",
        XivChatType.FreeCompany => "fc",
        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
            or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 => "ls",
        XivChatType.Party => "party",
        XivChatType.Alliance => "alliance",
        XivChatType.StandardEmote or XivChatType.CustomEmote => "emote",
        _ => "other",
    };

    private static bool IsEmoteMessage(string message)
    {
        var trimmed = message.Trim();
        return trimmed.StartsWith('*') && trimmed.EndsWith('*');
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
