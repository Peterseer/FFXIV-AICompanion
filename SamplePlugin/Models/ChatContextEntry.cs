using System;

namespace SamplePlugin.Models;

public sealed class ChatContextEntry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public string Channel { get; init; } = string.Empty;

    public string Sender { get; init; } = string.Empty;

    public string SenderWorld { get; init; } = string.Empty;

    public string SenderFullId { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public bool IsLocalPlayer { get; init; }

    public bool IsEmote { get; init; }

    public bool IsAdminCommand { get; init; }

    /// <summary>
    /// 由 /echo aic ... 触发的本地自测对话，回复走默语频道。
    /// </summary>
    public bool IsEchoDebug { get; init; }

    /// <summary>
    /// 背包库存参考（如 "鱼糕: 23, 冰晶: 5"），由 ChatMonitor 扫描消息中提到的物品后填充，供 AI 判断是否可交易。
    /// </summary>
    public string InventoryHint { get; init; } = string.Empty;

    // --- 地图坐标 / flag 信息 ---

    /// <summary>
    /// 消息中是否包含地图坐标链接（flag / &lt;flag&gt; 等）。
    /// </summary>
    public bool HasMapLink { get; init; }

    public uint MapTerritoryId { get; init; }

    public uint MapId { get; init; }

    public int MapRawX { get; init; }

    public int MapRawY { get; init; }

    /// <summary>
    /// 坐标对应的游戏世界 X（= RawX / 1000）。
    /// </summary>
    public float MapWorldX { get; init; }

    /// <summary>
    /// 坐标对应的游戏世界 Z（= RawY / 1000）。
    /// </summary>
    public float MapWorldZ { get; init; }

    public string MapPlaceName { get; init; } = string.Empty;

    public string MapCoords { get; init; } = string.Empty;

    /// <summary>检测到传送/寻路邀请时的 AI 提示。</summary>
    public string TravelHint { get; init; } = string.Empty;

    public bool WantsTravelNavigation { get; init; }

    public bool WantsTravelTeleport { get; init; }

    public string TravelAetheryteQuery { get; init; } = string.Empty;
}
