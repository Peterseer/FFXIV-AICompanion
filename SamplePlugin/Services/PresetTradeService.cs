using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

/// <summary>
/// 预设交易列表：管理可交易道具、期望价格，并在交易时解析匹配。
/// </summary>
public sealed class PresetTradeService
{
    private readonly Configuration configuration;
    private readonly InventoryService inventoryService;

    public PresetTradeService(Configuration configuration, InventoryService inventoryService)
    {
        this.configuration = configuration;
        this.inventoryService = inventoryService;
    }

    public IReadOnlyList<PresetTradeEntry> Entries => configuration.PresetTradeList;

    public IReadOnlyList<PresetTradeEntry> GetEnabledEntries() =>
        configuration.PresetTradeList.Where(e => e.Enabled).ToList();

    public PresetTradeEntry? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var q = name.Trim();
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;

        var exact = configuration.PresetTradeList.FirstOrDefault(
            e => e.Enabled && string.Equals(e.ItemName, q, cmp));
        if (exact != null)
        {
            return exact;
        }

        return configuration.PresetTradeList.FirstOrDefault(
            e => e.Enabled && (e.ItemName.Contains(q, cmp) || q.Contains(e.ItemName, cmp)));
    }

    public PresetTradeEntry? FindById(uint itemId) =>
        configuration.PresetTradeList.FirstOrDefault(e => e.Enabled && e.ItemId == itemId);

    public bool TryAdd(
        string itemNameQuery,
        int quantity,
        long expectedGil,
        ItemQualityPreference quality,
        out string error)
    {
        error = string.Empty;
        inventoryService.EnsureInitialized();

        var match = inventoryService.FindItemByName(itemNameQuery);
        if (match == null)
        {
            error = $"找不到物品: {itemNameQuery}";
            return false;
        }

        if (!match.IsTradable)
        {
            error = $"物品「{match.Name}」不可交易。";
            return false;
        }

        if (configuration.PresetTradeList.Any(e => e.ItemId == match.ItemId && e.Quality == quality))
        {
            error = $"列表中已有「{match.Name}」({FormatQualityShort(quality)}) 条目。";
            return false;
        }

        configuration.PresetTradeList.Add(new PresetTradeEntry
        {
            ItemId = match.ItemId,
            IconId = match.IconId,
            ItemName = match.Name,
            Quantity = Math.Max(1, quantity),
            ExpectedGil = Math.Max(0, expectedGil),
            Quality = quality,
            Enabled = true,
        });
        configuration.Save();
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= configuration.PresetTradeList.Count)
        {
            return;
        }

        configuration.PresetTradeList.RemoveAt(index);
        configuration.Save();
    }

    public void Save() => configuration.Save();

    /// <summary>读取背包中符合品质偏好的数量（需在游戏主线程调用）。</summary>
    public int GetBagCount(PresetTradeEntry entry) =>
        inventoryService.GetCount(entry.ItemId, entry.Quality);

    public uint GetIconId(PresetTradeEntry entry) =>
        entry.IconId != 0 ? entry.IconId : inventoryService.GetIconId(entry.ItemId);

    public PresetTradeLineStatus GetLineStatus(PresetTradeEntry entry)
    {
        var bag = GetBagCount(entry);
        return new PresetTradeLineStatus(entry, bag, bag >= entry.Quantity);
    }

    /// <summary>
    /// 根据预设表解析交易参数。若 AI 未填价格/数量，则使用预设值。
    /// </summary>
    public TradeResolution? TryResolveTrade(string? itemName, int itemQuantity, long expectedGil)
    {
        var preset = FindByName(itemName);
        if (preset == null)
        {
            return null;
        }

        return new TradeResolution(
            preset.ItemId,
            GetIconId(preset),
            preset.ItemName,
            preset.Quantity,
            preset.ExpectedGil,
            preset.Quality);
    }

    /// <summary>生成 AI 系统提示中的预设交易白名单。</summary>
    public string BuildPromptSection(PluginLanguage language)
    {
        var entries = GetEnabledEntries();
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (language == PluginLanguage.English)
        {
            sb.AppendLine("## Preset trade list (whitelist)");
            sb.AppendLine("Only items enabled below may be traded. Quantity, quality and expectedGil must match this list.");
            foreach (var e in entries)
            {
                var quality = FormatQualityShort(e.Quality);
                var qualityTag = quality.Length > 0 ? $" [{quality}]" : string.Empty;
                sb.AppendLine($"- {e.ItemName}{qualityTag} x{e.Quantity}: expectedGil={e.ExpectedGil} gil");
            }

            sb.AppendLine("When asked about these items: quote list prices and use social.trade (itemName + expectedGil from list).");
            sb.AppendLine("Refuse unlisted items. If inventory hints show insufficient stock, say out of stock — do not start trade.");
            return sb.ToString();
        }

        sb.AppendLine("## 预设交易清单（交易白名单）");
        sb.AppendLine("只能交易本清单中启用的物品。交易数量、品质与 expectedGil 必须以本表为准，不要自行改价或改数量。");
        foreach (var e in entries)
        {
            var quality = FormatQualityShort(e.Quality);
            var qualityTag = quality.Length > 0 ? $" [{quality}]" : string.Empty;
            sb.AppendLine($"- {e.ItemName}{qualityTag} x{e.Quantity}: expectedGil={e.ExpectedGil} 金币");
        }

        sb.AppendLine("玩家询价或收购以上物品时：按表中价格回复，使用 social.trade 发起交易（itemName 与表一致，expectedGil 与表一致）。");
        sb.AppendLine("未出现在本清单中的物品必须拒绝交易。若上下文提示背包数量不足，如实告知缺货，不要发起交易。");
        return sb.ToString();
    }

    /// <summary>生成 AI 系统提示中的预设交易白名单（使用配置语言）。</summary>
    public string BuildPromptSection(Configuration configuration) =>
        BuildPromptSection(configuration.InteractionLanguage);

    /// <summary>扫描消息文本，返回命中的预设物品及背包数量提示。</summary>
    public string BuildHint(string text, PluginLanguage language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var entry in GetEnabledEntries())
        {
            if (!text.Contains(entry.ItemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bag = GetBagCount(entry);
            var quality = FormatQualityShort(entry.Quality);
            var qualityTag = quality.Length > 0 ? $" {quality}" : string.Empty;
            var priceSuffix = language == PluginLanguage.English
                ? $"(price {entry.ExpectedGil} gil)"
                : $"(定价{entry.ExpectedGil}金币)";
            parts.Add($"{entry.ItemName}{qualityTag}: {bag} {priceSuffix}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    public string BuildHint(string text) => BuildHint(text, PluginLanguage.Chinese);

    public static string FormatQualityShort(ItemQualityPreference quality) => quality switch
    {
        ItemQualityPreference.NqOnly => "NQ",
        ItemQualityPreference.HqOnly => "HQ",
        _ => string.Empty,
    };

    public static string FormatQualityLabel(ItemQualityPreference quality) => quality switch
    {
        ItemQualityPreference.NqOnly => "仅 NQ",
        ItemQualityPreference.HqOnly => "仅 HQ",
        _ => "任意",
    };
}

public sealed record PresetTradeLineStatus(PresetTradeEntry Entry, int BagCount, bool HasEnough);

public sealed record TradeResolution(
    uint ItemId,
    uint IconId,
    string ItemName,
    int Quantity,
    long ExpectedGil,
    ItemQualityPreference Quality);
