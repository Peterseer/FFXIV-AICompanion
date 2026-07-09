using System;
using System.Collections.Generic;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

/// <summary>
/// 交互语言文案与 AI 提示词辅助。UI 仍以中文为主，游戏内 AI 回复随语言切换。
/// </summary>
public static class PluginLocale
{
    public static bool IsEnglish(Configuration configuration) =>
        configuration.InteractionLanguage == PluginLanguage.English;

    public static string GetDefaultPersona(PluginLanguage language) => language switch
    {
        PluginLanguage.English =>
            """
            You are a friendly, polite FFXIV adventurer. Keep messages natural and concise, like a real player.
            Remember each player's name and home world. Use emotes only when truly needed; most replies should be text only.
            Always reply in English.
            """,
        _ =>
            """
            你是一名友好、有礼貌的 FF14 冒险者。说话自然简洁，像真实玩家一样交流。
            你会记住对话对象的玩家名和服务器名。只有在确实需要时才使用情感动作，大多数对话只需文字回复。
            请始终使用简体中文回复。
            """,
    };

    public static string LocalPlayerName(PluginLanguage language) =>
        language == PluginLanguage.English ? "Me" : "我";

    public static string InventoryContextPrefix(PluginLanguage language) =>
        language == PluginLanguage.English ? "[Inventory:" : "[背包:";

    public static string MapContextPrefix(PluginLanguage language) =>
        language == PluginLanguage.English ? "[Coords:" : "[坐标:";

    public static string EmoteContextPrefix(PluginLanguage language) =>
        language == PluginLanguage.English ? "[Emote]" : "[情感动作]";

    public static string EmoteTriggerPrefix(PluginLanguage language) =>
        language == PluginLanguage.English ? "[Emote-trigger]" : "[情感动作-触发]";

    public static string TriggerSuffix(PluginLanguage language) =>
        language == PluginLanguage.English ? "-trigger" : "-触发";

    public static string EchoDebugTriggerHint(PluginLanguage language) => language switch
    {
        PluginLanguage.English =>
            " [Echo debug: reply with chat.channel=echo only; no emotes or social actions.]",
        _ =>
            " [默语调试：请仅用 chat.channel=echo 文字回复，勿使用情感动作或社交行为。]",
    };

    public static string FormatQualityLabel(ItemQualityPreference quality, PluginLanguage language)
    {
        if (language == PluginLanguage.English)
        {
            return quality switch
            {
                ItemQualityPreference.NqOnly => " NQ only",
                ItemQualityPreference.HqOnly => " HQ only",
                _ => string.Empty,
            };
        }

        return quality switch
        {
            ItemQualityPreference.NqOnly => "仅 NQ",
            ItemQualityPreference.HqOnly => "仅 HQ",
            _ => "任意",
        };
    }

    public static IReadOnlyList<string> GetConfirmWords(PluginLanguage language) => language switch
    {
        PluginLanguage.English =>
        [
            "buy", "yes", "y", "ok", "sure", "please", "want", "deal", "confirm", "go", "ye", "yeah", "yep"
        ],
        _ =>
        [
            "要", "买", "来", "可以", "确认", "确定", "好的", "好", "行", "嗯", "恩", "ok", "yes", "y"
        ],
    };

    public static IReadOnlyList<string> GetRejectWords(PluginLanguage language) => language switch
    {
        PluginLanguage.English =>
        [
            "no", "n", "cancel", "nevermind", "don't", "dont", "skip", "nah", "pass", "not"
        ],
        _ =>
        [
            "不要", "不买", "算了", "取消", "不用", "先不", "no", "n"
        ],
    };

    public static string TradeCancelConfirmation(PluginLanguage language, string itemName) => language switch
    {
        PluginLanguage.English => $"Okay, cancelled the purchase confirmation for \"{itemName}\".",
        _ => $"好的，已取消「{itemName}」的购买确认。",
    };

    public static string TradeOpenNow(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "Sure, opening trade now.",
        _ => "好的，现在为你发起交易。",
    };

    public static string TradeInsufficientStock(
        PluginLanguage language,
        string itemName,
        string qualityLabel,
        int required,
        int bagCount) => language switch
    {
        PluginLanguage.English =>
            $"Sorry, \"{itemName}\"{qualityLabel} is out of stock. Need {required}, have {bagCount} in bags.",
        _ => $"抱歉，「{itemName}」{qualityLabel}库存不足，需要 {required}，当前背包 {bagCount}。",
    };

    public static string TradePurchasePrompt(
        PluginLanguage language,
        string itemName,
        string qualityLabel,
        int quantity,
        long expectedGil) => language switch
    {
        PluginLanguage.English =>
            $"\"{itemName}\"{qualityLabel} x{quantity}, price {expectedGil} gil. Want to buy?",
        _ => $"「{itemName}」{qualityLabel} x{quantity}，价格 {expectedGil} 金币。需要购买吗？",
    };

    public static string TradeWrongGilAmount(
        PluginLanguage language,
        long expectedGil,
        long remoteGil) => language switch
    {
        PluginLanguage.English =>
            $"Wrong amount — it should be {expectedGil} gil, but you put {remoteGil} gil. Trade cancelled.",
        _ => $"金额不对哦，应该是 {expectedGil} 金币，你放了 {remoteGil} 金币，交易已取消。",
    };

    public static string TradeUnplannedPrompt(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "Hi! What would you like to buy?",
        _ => "你好，请先告诉我你想买什么。",
    };

    public static string TradeUnplannedTimeout(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "No purchase confirmed yet, so I'm cancelling this trade.",
        _ => "暂时没有确认要交易的内容，本次交易先取消。",
    };

    public static string TradeCompleteThanks(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "Thanks! Enjoy.",
        _ => "谢谢惠顾，交易愉快！",
    };

    public static string AdminNotSet(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "not set",
        _ => "未设置",
    };

    public static string LanguageDisplayName(PluginLanguage language) => language switch
    {
        PluginLanguage.English => "English",
        _ => "简体中文",
    };
}
