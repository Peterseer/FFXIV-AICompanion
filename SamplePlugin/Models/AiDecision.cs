using System.Text.Json.Serialization;

namespace SamplePlugin.Models;

/// <summary>
/// AI 返回的结构化决策，用于驱动游戏内互动。
/// </summary>
public sealed class AiDecision
{
    [JsonPropertyName("shouldRespond")]
    public bool ShouldRespond { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("chat")]
    public AiChatAction? Chat { get; set; }

    /// <summary>
    /// 已拥有的标准情感动作命令名（不含/），如 wave、deny、no。仅在角色认为必要时使用。
    /// </summary>
    [JsonPropertyName("emote")]
    public string? Emote { get; set; }

    /// <summary>
    /// 自定义情感表达文本，将通过 /em 发送（不在消息栏显示默认动作文字）。
    /// </summary>
    [JsonPropertyName("customEmote")]
    public string? CustomEmote { get; set; }

    [JsonPropertyName("social")]
    public AiSocialAction? Social { get; set; }
}

public sealed class AiChatAction
{
    /// <summary>
    /// say | shout | tell | fc | ls | party | alliance
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "say";

    /// <summary>
    /// 私聊目标玩家名（不含服务器）。
    /// </summary>
    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    /// <summary>
    /// 私聊目标服务器名。
    /// </summary>
    [JsonPropertyName("targetWorld")]
    public string? TargetWorld { get; set; }

    /// <summary>
    /// 兼容旧字段：可为 "玩家名@服务器名" 或仅玩家名。
    /// </summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
