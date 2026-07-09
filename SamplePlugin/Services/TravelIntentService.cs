using System;
using System.Collections.Generic;
using System.Linq;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed record TravelIntent(
    bool WantsNavigation,
    bool WantsTeleport,
    string? AetheryteQuery,
    string Hint);

/// <summary>
/// 从聊天文本识别传送/寻路意图，并生成 AI 与兜底逻辑可用的提示。
/// </summary>
public sealed class TravelIntentService
{
    private static readonly string[] EnglishInviteKeywords =
    [
        "come", "meet me", "meet at", "head to", "head over", "join me", "get here",
        "come here", "come to", "teleport", "tp to", "travel to", "visit", "find me",
        "where i am", "my location", "this flag", "the flag", "coordinates", "coords",
        "map link", "on my way", "waiting at", "i'm at", "im at", "i am at",
    ];

    private static readonly string[] ChineseInviteKeywords =
    [
        "来", "找我", "过来", "来这", "来这里", "来这儿", "坐标", "flag", "传送",
        "水晶", "我在", "等你", "到这里", "来一下", "过来一下", "到我这",
    ];

    private readonly AetheryteResolver aetheryteResolver;

    public TravelIntentService(AetheryteResolver aetheryteResolver)
    {
        this.aetheryteResolver = aetheryteResolver;
    }

    public TravelIntent? Analyze(string message, bool hasMapLink, PluginLanguage language)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return hasMapLink ? BuildNavigateIntent(language) : null;
        }

        var text = message.Trim();
        var lower = text.ToLowerInvariant();
        var keywords = language == PluginLanguage.English ? EnglishInviteKeywords : ChineseInviteKeywords;
        var hasInvite = hasMapLink || keywords.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (!hasInvite)
        {
            var aetheryteOnly = TryExtractAetheryteQuery(text);
            if (aetheryteOnly != null)
            {
                return BuildTeleportIntent(aetheryteOnly, language);
            }

            return null;
        }

        if (hasMapLink)
        {
            return BuildNavigateIntent(language);
        }

        var query = TryExtractAetheryteQuery(text);
        return query != null
            ? BuildTeleportIntent(query, language)
            : BuildTeleportIntentFromMessage(text, language);
    }

    public string? TryExtractAetheryteQuery(string message)
    {
        foreach (var alias in AetheryteResolver.GetAliasKeys())
        {
            if (message.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                return alias;
            }
        }

        var match = aetheryteResolver.FindByName(message);
        return match?.Name;
    }

    private TravelIntent? BuildTeleportIntentFromMessage(string message, PluginLanguage language)
    {
        var match = aetheryteResolver.FindByName(message);
        if (match == null)
        {
            return null;
        }

        return BuildTeleportIntent(match.Name, language);
    }

    private static TravelIntent BuildNavigateIntent(PluginLanguage language) => language switch
    {
        PluginLanguage.English => new TravelIntent(
            WantsNavigation: true,
            WantsTeleport: false,
            AetheryteQuery: null,
            Hint: "[Travel: map flag detected — you MUST use social.navigate with action \"navigate\" and no extra fields.]"),
        _ => new TravelIntent(
            WantsNavigation: true,
            WantsTeleport: false,
            AetheryteQuery: null,
            Hint: "[旅行: 检测到地图坐标 — 必须使用 social.navigate，action 为 navigate，无需额外参数。]"),
    };

    private static TravelIntent BuildTeleportIntent(string query, PluginLanguage language) => language switch
    {
        PluginLanguage.English => new TravelIntent(
            WantsNavigation: false,
            WantsTeleport: true,
            AetheryteQuery: query,
            Hint: $"[Travel: location invite detected — use social.teleport with aetheryteName matching \"{query}\".]"),
        _ => new TravelIntent(
            WantsNavigation: false,
            WantsTeleport: true,
            AetheryteQuery: query,
            Hint: $"[旅行: 检测到地点邀请 — 请使用 social.teleport，aetheryteName 匹配「{query}」。]"),
    };
}
