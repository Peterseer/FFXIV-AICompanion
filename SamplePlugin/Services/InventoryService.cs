using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed record ItemMatch(uint ItemId, uint IconId, string Name, int Count, bool IsTradable, int EverywhereCount = -1)
{
    /// <summary>全部位置合计（装备/兵装库/雇员等），-1 表示未统计。</summary>
    public int TotalCount => EverywhereCount >= 0 ? EverywhereCount : Count;
}
/// <summary>
/// 背包 / 物品查询服务：将物品名解析为物品 ID，并读取当前背包中的数量。
/// </summary>
public sealed unsafe class InventoryService
{
    private readonly IDataManager dataManager;
    private readonly ActivityLog log;

    // 索引全部有名物品（含不可交易），按名称长度升序排列，模糊匹配时优先更短（更接近）的名称
    private List<IndexedItem> items = [];
    private int tradableCount;
    private bool initialized;

    private readonly record struct IndexedItem(string Name, uint Id, bool IsTradable);

    public InventoryService(IDataManager dataManager, ActivityLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        BuildItemCache();
    }

    /// <summary>强制重建索引。</summary>
    public void Rebuild()
    {
        initialized = false;
        BuildItemCache();
    }

    private void BuildItemCache()
    {
        var list = new List<IndexedItem>();
        var tradable = 0;
        var sheet = dataManager.GetExcelSheet<Item>();
        foreach (var row in sheet)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var isTradable = !row.IsUntradable;
            if (isTradable)
            {
                tradable++;
            }

            list.Add(new IndexedItem(name, row.RowId, isTradable));
        }

        // 升序排列：同等匹配条件下优先返回更短（更精确）的名称
        items = list.OrderBy(x => x.Name.Length).ToList();
        tradableCount = tradable;
        initialized = true;
        log.Info($"物品索引构建完成: {items.Count} 个物品（其中可交易 {tradable} 个）。");
    }

    private static readonly InventoryType[] MainBags =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    /// <summary>
    /// 读取主背包中某物品的数量，NQ + HQ 合计（需在游戏主线程调用）。
    /// 仅统计四个主背包格（与可交易范围一致），不含装备/兵装库/雇员/挎包。
    /// 更广范围请用游戏内 /isearch。
    /// </summary>
    public int GetCount(uint itemId)
    {
        return GetCount(itemId, ItemQualityPreference.Any);
    }

    /// <summary>
    /// 读取主背包中某物品的数量，可按 NQ/HQ 过滤（需在游戏主线程调用）。
    /// </summary>
    public int GetCount(uint itemId, ItemQualityPreference quality)
    {
        var inv = InventoryManager.Instance();
        if (inv == null || itemId == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var container in MainBags)
        {
            if (quality is ItemQualityPreference.Any or ItemQualityPreference.NqOnly)
            {
                total += inv->GetItemCountInContainer(itemId, container, false);
            }

            if (quality is ItemQualityPreference.Any or ItemQualityPreference.HqOnly)
            {
                total += inv->GetItemCountInContainer(itemId, container, true);
            }
        }

        return total;
    }

    /// <summary>
    /// 读取全部位置（含装备、兵装库）某物品数量，NQ + HQ 合计（需在游戏主线程调用）。
    /// </summary>
    public int GetCountEverywhere(uint itemId)
    {
        var inv = InventoryManager.Instance();
        if (inv == null || itemId == 0)
        {
            return 0;
        }

        return inv->GetInventoryItemCount(itemId, false, true, true)
             + inv->GetInventoryItemCount(itemId, true, true, true);
    }

    /// <summary>
    /// 按名称查找物品并返回背包数量（需在游戏主线程调用）。
    /// 匹配优先级：精确 > 前缀 > 包含查询(取最短) > 查询包含物品名(取最长)。
    /// </summary>
    public ItemMatch? FindItemByName(string query)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var q = NormalizeQuery(query);
        if (string.IsNullOrEmpty(q))
        {
            return null;
        }

        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;

        // 1. 精确匹配（优先背包中已有的）
        var exactList = items.Where(x => string.Equals(x.Name, q, cmp)).ToList();
        if (exactList.Count > 0)
        {
            return ToMatch(PreferInBag(exactList), includeEverywhere: true);
        }

        // 2. 前缀匹配：物品名以查询开头（items 已按长度升序，取最短）
        var prefix = items.FirstOrDefault(x => x.Name.StartsWith(q, cmp));
        if (prefix.Name != null)
        {
            return ToMatch(prefix, includeEverywhere: true);
        }

        // 3. 物品名包含查询：取最短（最接近查询）
        var contains = items.FirstOrDefault(x => x.Name.Contains(q, cmp));
        if (contains.Name != null)
        {
            return ToMatch(contains, includeEverywhere: true);
        }

        // 4. 查询包含物品名：取最长（最具体）
        var contained = items.LastOrDefault(x => q.Contains(x.Name, cmp));
        if (contained.Name != null)
        {
            return ToMatch(contained, includeEverywhere: true);
        }

        return null;
    }

    private ItemMatch ToMatch(IndexedItem item, bool includeEverywhere = false)
    {
        var bag = GetCount(item.Id);
        var everywhere = includeEverywhere ? GetCountEverywhere(item.Id) : -1;
        return new ItemMatch(item.Id, GetIconId(item.Id), item.Name, bag, item.IsTradable, everywhere);
    }

    public uint GetIconId(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        return dataManager.GetExcelSheet<Item>().GetRow(itemId).Icon;
    }

    /// <summary>
    /// 调用游戏内 /isearch，在背包/兵装库/雇员/挎包等全部位置搜索（结果输出到聊天栏）。
    /// </summary>
    public void SearchViaGameCommand(string query)
    {
        var q = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(q))
        {
            return;
        }

        // 尽量用已解析的规范名称，否则用原始查询
        var match = FindItemByName(q);
        var searchTerm = match?.Name ?? q;
        try
        {
            GameActionExecutor.SearchInventoryItem(searchTerm);
            log.Info($"已执行 /isearch {searchTerm}");
        }
        catch (Exception ex)
        {
            log.Warn($"执行 /isearch 失败: {ex.Message}");
        }
    }

    /// <summary>同名物品时优先返回背包中已持有的那一个。</summary>
    private IndexedItem PreferInBag(List<IndexedItem> candidates)
    {
        foreach (var c in candidates)
        {
            if (GetCount(c.Id) > 0 || GetCountEverywhere(c.Id) > 0)
            {
                return c;
            }
        }

        return candidates[0];
    }

    /// <summary>去除 HQ 标记 / 数量后缀等噪声。</summary>
    private static string NormalizeQuery(string query)
    {
        var q = query.Trim();
        // 去掉 HQ 标记
        q = q.Replace("（HQ）", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace("(HQ)", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace("HQ", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace("\uE03C", string.Empty) // HQ 私有符号
             .Trim();
        // 去掉尾部 "xN" / "*N" / "×N" 数量后缀
        q = Regex.Replace(q, @"[\s]*[x×*][\s]*\d+$", string.Empty, RegexOptions.IgnoreCase).Trim();
        return q;
    }

    /// <summary>
    /// 查找主背包中指定物品的第一个槽位（NQ 或 HQ，需在游戏主线程调用）。
    /// </summary>
    public (InventoryType Container, int Slot)? FindFirstSlot(uint itemId)
    {
        return FindFirstSlot(itemId, ItemQualityPreference.Any);
    }

    /// <summary>
    /// 查找主背包中指定物品的第一个槽位，可按 NQ/HQ 过滤（需在游戏主线程调用）。
    /// </summary>
    public (InventoryType Container, int Slot)? FindFirstSlot(uint itemId, ItemQualityPreference quality)
    {
        var inv = InventoryManager.Instance();
        if (inv == null || itemId == 0)
        {
            return null;
        }

        foreach (var container in MainBags)
        {
            var inventoryContainer = inv->GetInventoryContainer(container);
            if (inventoryContainer == null || !inventoryContainer->IsLoaded)
            {
                continue;
            }

            var size = Math.Min(inventoryContainer->Size, 140);
            for (var slot = 0; slot < size; slot++)
            {
                var item = inv->GetInventorySlot(container, slot);
                if (item == null || item->ItemId == 0 || item->Quantity <= 0)
                {
                    continue;
                }

                if (MatchesItemId(item, itemId, quality))
                {
                    return (container, slot);
                }
            }
        }

        return null;
    }

    private static bool MatchesItemId(
        InventoryItem* item,
        uint targetItemId,
        ItemQualityPreference quality = ItemQualityPreference.Any)
    {
        var slotItemId = item->ItemId;
        var baseId = item->GetBaseItemId();
        var isHq = item->IsHighQuality();
        if (baseId != targetItemId && slotItemId != targetItemId)
        {
            return false;
        }

        return quality switch
        {
            ItemQualityPreference.NqOnly => !isHq,
            ItemQualityPreference.HqOnly => isHq,
            _ => true,
        };
    }

    /// <summary>索引中物品总数。</summary>
    public int ItemCount
    {
        get
        {
            EnsureInitialized();
            return items.Count;
        }
    }

    /// <summary>可交易物品数量。</summary>
    public int TradableItemCount
    {
        get
        {
            EnsureInitialized();
            return tradableCount;
        }
    }

    /// <summary>
    /// 扫描一段文本，返回其中提到的、且在物品表中存在的物品及背包数量（需在游戏主线程调用）。
    /// 优先匹配较长名称，避免子串（如「鱼糕」）覆盖更具体的物品名（如「上等鱼糕」）。
    /// </summary>
    public IReadOnlyList<ItemMatch> ScanText(string text, int maxResults = 5)
    {
        EnsureInitialized();
        var results = new List<ItemMatch>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return results;
        }

        var seen = new HashSet<uint>();
        // items 按长度升序，逆序遍历即长名称在前，命中后跳过其内部的短子串
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (!text.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(item.Id))
            {
                results.Add(ToMatch(item, includeEverywhere: true));
                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 将扫描结果格式化为提示字符串，例如 "鱼糕: 23, 上等鱼糕: 0"。
    /// </summary>
    public string BuildHint(string text)
    {
        var matches = ScanText(text);
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", matches.Select(FormatItemCountHint));
    }

    private static string FormatItemCountHint(ItemMatch m)
    {
        if (m.EverywhereCount >= 0 && m.EverywhereCount != m.Count)
        {
            return $"{m.Name}: 背包{m.Count}/全部{m.EverywhereCount}";
        }

        return $"{m.Name}: {m.Count}";
    }
}
