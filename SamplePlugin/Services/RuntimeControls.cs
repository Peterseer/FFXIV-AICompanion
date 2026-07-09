using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin.Services;

public sealed class RuntimeControls
{
    public bool ChatEnabled { get; set; } = true;

    public bool EmoteEnabled { get; set; } = true;

    public bool SocialEnabled { get; set; } = true;

    public string StatusSummary =>
        $"对话: {(ChatEnabled ? "开" : "关")} | 情感动作: {(EmoteEnabled ? "开" : "关")} | 社交: {(SocialEnabled ? "开" : "关")}";

    public void Reset()
    {
        ChatEnabled = true;
        EmoteEnabled = true;
        SocialEnabled = true;
    }

    /// <summary>
    /// 插件主开关、社交自动化、交易与运行时开关均开启时，才允许发起交易询问/回复。
    /// </summary>
    public static bool CanOfferTradeInteraction(Configuration configuration, RuntimeControls runtime) =>
        configuration.Enabled
        && configuration.AllowSocialAutomation
        && configuration.AllowTrade
        && runtime.SocialEnabled
        && runtime.ChatEnabled;

    /// <summary>
    /// 允许监控并自动处理已注册的交易（含 UI 操作与私聊通知）。
    /// </summary>
    public static bool CanMonitorTrade(Configuration configuration, RuntimeControls runtime) =>
        configuration.Enabled
        && configuration.AllowTrade
        && configuration.AllowTradeMonitor
        && runtime.SocialEnabled;
}

public sealed class PlayerMemory
{
    private readonly Dictionary<string, PlayerRecord> records = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public void Remember(string name, string world)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(world) ? name : $"{name}@{world}";
        lock (sync)
        {
            records[key] = new PlayerRecord
            {
                Name = name,
                World = world ?? string.Empty,
                LastSeen = DateTime.UtcNow,
            };
        }
    }

    public PlayerRecord? Get(string name, string world)
    {
        var key = string.IsNullOrWhiteSpace(world) ? name : $"{name}@{world}";
        lock (sync)
        {
            return records.TryGetValue(key, out var record) ? record : null;
        }
    }

    public IReadOnlyList<PlayerRecord> Snapshot()
    {
        lock (sync)
        {
            return records.Values.OrderByDescending(r => r.LastSeen).ToList();
        }
    }
}

public sealed class PlayerRecord
{
    public string Name { get; init; } = string.Empty;

    public string World { get; init; } = string.Empty;

    public string FullId => string.IsNullOrEmpty(World) ? Name : $"{Name}@{World}";

    public DateTime LastSeen { get; init; }
}
