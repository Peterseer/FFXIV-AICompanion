using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using SamplePlugin.Models;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 8;

    public bool IsConfigWindowMovable { get; set; } = true;

    /// <summary>
    /// AI 交互语言：影响提示词、游戏内自动回复与上下文标记。
    /// </summary>
    public PluginLanguage InteractionLanguage { get; set; } = PluginLanguage.Chinese;

    // --- AI 核心设置 ---
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 启用 /echo aic ... 默语自测（仅自己可见，不触发公屏）。
    /// </summary>
    public bool EnableEchoDebug { get; set; } = true;

    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";

    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// AI 角色人设（Persona），描述性格、说话风格与背景。
    /// </summary>
    public string Persona { get; set; } =
        """
        你是一名友好、有礼貌的 FF14 冒险者。说话自然简洁，像真实玩家一样交流。
        你会记住对话对象的玩家名和服务器名。只有在确实需要时才使用情感动作，大多数对话只需文字回复。
        """;

    /// <summary>
    /// 管理员身份，格式：玩家名@服务器名。仅该玩家可通过私聊发送控制指令。
    /// </summary>
    public string AdminId { get; set; } = string.Empty;

    public int ResponseCooldownSeconds { get; set; } = 5;

    public int MaxContextMessages { get; set; } = 20;

    // --- 频道监听 ---
    public bool ListenSay { get; set; } = true;

    public bool ListenShout { get; set; } = true;

    public bool ListenTell { get; set; } = true;

    public bool ListenFc { get; set; } = true;

    public bool ListenLs { get; set; } = true;

    public bool ListenParty { get; set; } = true;

    public bool ListenAlliance { get; set; } = false;

    public bool ListenEmotes { get; set; } = true;

    public bool AllowSayChannelOutput { get; set; } = true;

    public bool InteractOnlyWithFriendsOrParty { get; set; } = false;

    // --- 行为开关 ---
    public bool AllowEmoteResponse { get; set; } = true;

    public bool AllowSocialAutomation { get; set; } = false;

    public bool AllowFriendRequest { get; set; } = false;

    public bool AllowTrade { get; set; } = false;

    public bool AllowTeleport { get; set; } = false;

    public bool AllowPartyInvite { get; set; } = false;

    /// <summary>
    /// 允许 AI 读取自身职业等级与装备组列表（注入提示词上下文）。
    /// </summary>
    public bool AllowCharacterProfileRead { get; set; } = true;

    /// <summary>
    /// 允许 AI 通过 social.gearset 切换装备组（可切职或外观）。
    /// </summary>
    public bool AllowGearsetSwitch { get; set; } = false;

    /// <summary>
    /// 交易时自动监控对方放入的金额，不符则拒绝并告知。
    /// </summary>
    public bool AllowTradeMonitor { get; set; } = true;

    /// <summary>
    /// 交易监控时自动点击「提出条件」与「确认」（卖家侧）。
    /// </summary>
    public bool AllowTradeAutoConfirm { get; set; } = true;

    /// <summary>
    /// 允许通过 vnavmesh IPC 自动寻路至 flag 坐标（需安装 vnavmesh 插件）。
    /// </summary>
    public bool AllowNavigation { get; set; } = true;

    /// <summary>
    /// 预设可交易道具列表：手动配置物品名、期望价格、NQ/HQ 偏好。
    /// AI 交易与金额校验均以此表为准。
    /// </summary>
    public List<PresetTradeEntry> PresetTradeList { get; set; } = [];

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
