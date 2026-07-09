using System;
using System.Collections.Generic;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class TradeIntentConfirmationService
{
    private static readonly string[] ConfirmWords =
    [
        "要", "买", "来", "可以", "确认", "确定", "好的", "好", "行", "嗯", "恩", "ok", "yes", "y"
    ];

    private static readonly string[] RejectWords =
    [
        "不要", "不买", "算了", "取消", "不用", "先不", "no", "n"
    ];

    private static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(2);

    private readonly PresetTradeService presetTradeService;
    private readonly InventoryService inventoryService;
    private readonly OutboundEchoGuard echoGuard;
    private readonly ActivityLog log;
    private readonly Dictionary<string, PendingTradeIntent> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object lockObj = new();

    public TradeIntentConfirmationService(
        PresetTradeService presetTradeService,
        InventoryService inventoryService,
        OutboundEchoGuard echoGuard,
        ActivityLog log)
    {
        this.presetTradeService = presetTradeService;
        this.inventoryService = inventoryService;
        this.echoGuard = echoGuard;
        this.log = log;
    }

    public bool TryHandleReply(ChatContextEntry trigger, out AiSocialAction? action)
    {
        action = null;
        var key = BuildKey(trigger);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        PendingTradeIntent? intent;
        lock (lockObj)
        {
            PruneExpired();
            if (!pending.TryGetValue(key, out intent))
            {
                return false;
            }
        }

        if (IsReject(trigger.Message))
        {
            lock (lockObj)
            {
                pending.Remove(key);
            }

            SendReply(trigger, $"好的，已取消「{intent.Resolution.ItemName}」的购买确认。");
            log.Info($"预设交易已取消: {trigger.SenderFullId} -> {intent.Resolution.ItemName}");
            return true;
        }

        if (!IsConfirm(trigger.Message))
        {
            return false;
        }

        lock (lockObj)
        {
            pending.Remove(key);
        }

        action = BuildTradeAction(trigger, intent.Resolution);
        SendReply(trigger, "好的，现在为你发起交易。");
        log.Info($"预设交易已确认: {trigger.SenderFullId} -> {intent.Resolution.ItemName}");
        return true;
    }

    public bool TryRequestFromMention(ChatContextEntry trigger)
    {
        var resolution = ResolveMention(trigger.Message);
        if (resolution == null)
        {
            return false;
        }

        return RequestConfirmation(trigger, resolution);
    }

    public bool TryRequestForAction(AiSocialAction action, ChatContextEntry trigger)
    {
        if (!string.Equals(action.Action, "trade", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var resolution = presetTradeService.TryResolveTrade(action.ItemName, action.ItemQuantity, action.ExpectedGil);
        if (resolution == null)
        {
            return false;
        }

        return RequestConfirmation(trigger, resolution);
    }

    private bool RequestConfirmation(ChatContextEntry trigger, TradeResolution resolution)
    {
        var key = BuildKey(trigger);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var bagCount = inventoryService.GetCount(resolution.ItemId, resolution.Quality);
        if (bagCount < resolution.Quantity)
        {
            SendReply(
                trigger,
                $"抱歉，「{resolution.ItemName}」{PresetTradeService.FormatQualityLabel(resolution.Quality)}库存不足，需要 {resolution.Quantity}，当前背包 {bagCount}。");
            return true;
        }

        lock (lockObj)
        {
            PruneExpired();
            pending[key] = new PendingTradeIntent(resolution, DateTime.UtcNow);
        }

        SendReply(
            trigger,
            $"「{resolution.ItemName}」{PresetTradeService.FormatQualityLabel(resolution.Quality)} x{resolution.Quantity}，价格 {resolution.ExpectedGil} 金币。需要购买吗？");
        log.Info($"已询问购买确认: {trigger.SenderFullId} -> {resolution.ItemName}");
        return true;
    }

    private TradeResolution? ResolveMention(string message)
    {
        foreach (var entry in presetTradeService.GetEnabledEntries())
        {
            if (!message.Contains(entry.ItemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return presetTradeService.TryResolveTrade(entry.ItemName, entry.Quantity, entry.ExpectedGil);
        }

        return null;
    }

    private static AiSocialAction BuildTradeAction(ChatContextEntry trigger, TradeResolution resolution) =>
        new()
        {
            Action = "trade",
            TargetName = trigger.Sender,
            TargetWorld = trigger.SenderWorld,
            ItemName = resolution.ItemName,
            ItemQuantity = resolution.Quantity,
            ExpectedGil = resolution.ExpectedGil,
        };

    private void SendReply(ChatContextEntry trigger, string message)
    {
        var chat = new AiChatAction
        {
            Channel = trigger.Channel,
            TargetName = trigger.Sender,
            TargetWorld = trigger.SenderWorld,
            Message = message,
        };

        if (chat.Channel is "whisper" or "tell" or "w")
        {
            GameActionExecutor.ResolveTellTarget(chat, trigger);
        }

        var targetId = string.IsNullOrEmpty(chat.TargetWorld)
            ? chat.TargetName ?? string.Empty
            : $"{chat.TargetName}@{chat.TargetWorld}";

        echoGuard.Record(chat.Channel, chat.Message, targetId);
        GameActionExecutor.SendChat(chat);
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in new List<KeyValuePair<string, PendingTradeIntent>>(pending))
        {
            if (now - pair.Value.CreatedAt > PendingTimeout)
            {
                pending.Remove(pair.Key);
            }
        }
    }

    private static bool IsConfirm(string message)
    {
        var text = Normalize(message);
        if (IsReject(text))
        {
            return false;
        }

        foreach (var word in ConfirmWords)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReject(string message)
    {
        var text = Normalize(message);
        foreach (var word in RejectWords)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string text) =>
        text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string BuildKey(ChatContextEntry trigger)
    {
        if (!string.IsNullOrWhiteSpace(trigger.SenderFullId))
        {
            return trigger.SenderFullId;
        }

        return string.IsNullOrWhiteSpace(trigger.SenderWorld)
            ? trigger.Sender
            : $"{trigger.Sender}@{trigger.SenderWorld}";
    }

    private sealed record PendingTradeIntent(TradeResolution Resolution, DateTime CreatedAt);
}
