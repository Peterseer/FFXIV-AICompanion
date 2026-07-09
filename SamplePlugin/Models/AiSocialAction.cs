using System.Text.Json.Serialization;

namespace SamplePlugin.Models;

/// <summary>
/// AI 或管理员触发的社交/世界互动行为。
/// </summary>
public sealed class AiSocialAction
{
    /// <summary>
    /// friend | trade | teleport | invite | target | navigate
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    [JsonPropertyName("targetWorld")]
    public string? TargetWorld { get; set; }

    /// <summary>
    /// 传送目标水晶名称（部分匹配），action=teleport 时使用。
    /// </summary>
    [JsonPropertyName("aetheryteName")]
    public string? AetheryteName { get; set; }

    /// <summary>
    /// 交易物品名称（action=trade 时使用）。插件会在背包中查找该物品并校验数量。
    /// </summary>
    [JsonPropertyName("itemName")]
    public string? ItemName { get; set; }

    /// <summary>
    /// 交易物品数量（action=trade 时使用）。
    /// </summary>
    [JsonPropertyName("itemQuantity")]
    public int ItemQuantity { get; set; }

    /// <summary>
    /// 期望对方支付的金额（金币）。交易时插件会校验对方放入的金额是否一致，不符则自动拒绝。
    /// </summary>
    [JsonPropertyName("expectedGil")]
    public long ExpectedGil { get; set; }
}
