using System;

namespace SamplePlugin.Models;

/// <summary>交易时偏好的物品品质。</summary>
public enum ItemQualityPreference
{
    /// <summary>NQ 与 HQ 均可。</summary>
    Any = 0,

    /// <summary>仅普通品质 (NQ)。</summary>
    NqOnly = 1,

    /// <summary>仅高品质 (HQ)。</summary>
    HqOnly = 2,
}

/// <summary>
/// 预设可交易道具条目：名称、数量、期望金币、品质偏好。
/// </summary>
[Serializable]
public sealed class PresetTradeEntry
{
    public uint ItemId { get; set; }

    public uint IconId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    /// <summary>每次交易的物品数量。</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>期望对方支付的金币（单价 × 数量，或整单总价由 UI 标注）。</summary>
    public long ExpectedGil { get; set; }

    public ItemQualityPreference Quality { get; set; } = ItemQualityPreference.Any;

    public bool Enabled { get; set; } = true;
}
