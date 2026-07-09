using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed unsafe class CharacterProfileService
{
    private readonly IDataManager dataManager;
    private readonly IPlayerState playerState;
    private readonly IUnlockState unlockState;
    private readonly ActivityLog log;
    private List<GearsetInfo> cachedGearsets = [];
    private DateTime lastGearsetRefresh = DateTime.MinValue;

    public CharacterProfileService(
        IDataManager dataManager,
        IPlayerState playerState,
        IUnlockState unlockState,
        ActivityLog log)
    {
        this.dataManager = dataManager;
        this.playerState = playerState;
        this.unlockState = unlockState;
        this.log = log;
    }

    public IReadOnlyList<GearsetInfo> GetGearsets(bool forceRefresh = false)
    {
        if (forceRefresh || DateTime.UtcNow - lastGearsetRefresh > TimeSpan.FromMinutes(2))
        {
            RefreshGearsets();
        }

        return cachedGearsets;
    }

    public void RefreshGearsets()
    {
        var result = new List<GearsetInfo>();
        var module = GetGearsetModule();
        if (module == null)
        {
            log.Warn("无法读取装备组模块，套装列表刷新失败。");
            cachedGearsets = result;
            return;
        }

        var mapping = module->EnabledGearsetIndex2EntryIndex;
        for (var enabledIndex = 0; enabledIndex < mapping.Length; enabledIndex++)
        {
            var internalId = mapping[enabledIndex];
            if (!module->IsValidGearset(internalId))
            {
                break;
            }

            ref var entry = ref module->Entries[internalId];
            if (!entry.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                continue;
            }

            var name = entry.NameString?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"套装 {enabledIndex + 1}";
            }

            result.Add(new GearsetInfo(
                enabledIndex + 1,
                internalId,
                name,
                ResolveClassJobName(entry.ClassJob),
                entry.ItemLevel));
        }

        cachedGearsets = result;
        lastGearsetRefresh = DateTime.UtcNow;
        log.Info($"已刷新装备组列表: {cachedGearsets.Count} 个。");
    }

    public GearsetInfo? FindGearset(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var list = GetGearsets();
        var q = query.Trim();

        if (int.TryParse(q, out var slot) && slot > 0)
        {
            return list.FirstOrDefault(g => g.EnabledSlot == slot);
        }

        return list.FirstOrDefault(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(g => g.JobName.Contains(q, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(g => q.Contains(g.Name, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryEquipGearset(string? gearsetName, int gearsetSlot, out string message)
    {
        message = string.Empty;
        GearsetInfo? match = null;

        if (gearsetSlot > 0)
        {
            match = GetGearsets().FirstOrDefault(g => g.EnabledSlot == gearsetSlot);
        }

        if (match == null && !string.IsNullOrWhiteSpace(gearsetName))
        {
            match = FindGearset(gearsetName);
        }

        if (match == null)
        {
            message = string.IsNullOrWhiteSpace(gearsetName) && gearsetSlot <= 0
                ? "缺少装备组名称或序号。"
                : $"未找到装备组: {gearsetName ?? gearsetSlot.ToString()}";
            return false;
        }

        var module = GetGearsetModule();
        if (module == null)
        {
            message = "装备组模块不可用。";
            return false;
        }

        if (!module->IsValidGearset(match.InternalId))
        {
            message = $"装备组无效: {match.Name}";
            return false;
        }

        var result = module->EquipGearset(match.InternalId, 0);
        if (result == 0)
        {
            message = $"已切换装备组 #{match.EnabledSlot} -> {match.Name} ({match.JobName} i{match.ItemLevel})";
            log.Info(message);
            return true;
        }

        if (GameActionExecutor.TryChangeGearset(match.EnabledSlot, out var slashError))
        {
            message = $"已通过 /gearset change 切换 -> #{match.EnabledSlot} {match.Name}";
            log.Info(message);
            return true;
        }

        message = slashError ?? $"切换装备组失败: {match.Name}";
        log.Warn(message);
        return false;
    }

    public string BuildJobSummary(PluginLanguage language, int maxJobs = 40)
    {
        if (!playerState.IsLoaded)
        {
            return language == PluginLanguage.English ? "(Character not loaded)" : "（角色未加载）";
        }

        var current = playerState.ClassJob;
        var currentName = current.Value.Name.ExtractText();
        var sb = new StringBuilder();

        if (language == PluginLanguage.English)
        {
            sb.Append($"Current: {currentName} Lv{playerState.Level}");
        }
        else
        {
            sb.Append($"当前: {currentName} Lv{playerState.Level}");
        }

        var unlocked = new List<string>();
        foreach (var row in dataManager.GetExcelSheet<ClassJob>())
        {
            if (row.RowId == 0 || row.ClassJobCategory.RowId == 0)
            {
                continue;
            }

            if (!unlockState.IsClassJobUnlocked(row))
            {
                continue;
            }

            var level = playerState.GetClassJobLevel(row);
            if (level <= 0)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (row.RowId == current.RowId)
            {
                continue;
            }

            unlocked.Add($"{name} Lv{level}");
        }

        unlocked.Sort(StringComparer.OrdinalIgnoreCase);
        if (unlocked.Count > 0)
        {
            var label = language == PluginLanguage.English ? "; Unlocked: " : "; 已解锁: ";
            sb.Append(label);
            sb.Append(string.Join(", ", unlocked.Take(maxJobs)));
            if (unlocked.Count > maxJobs)
            {
                sb.Append(language == PluginLanguage.English
                    ? $"... ({unlocked.Count} total)"
                    : $"... 等共 {unlocked.Count} 个");
            }
        }

        return sb.ToString();
    }

    public string BuildGearsetPromptList(PluginLanguage language, int maxItems = 30)
    {
        var list = GetGearsets();
        if (list.Count == 0)
        {
            return language == PluginLanguage.English
                ? "(No saved gearsets found)"
                : "（未检测到已保存装备组）";
        }

        var lines = list.Take(maxItems)
            .Select(g => $"#{g.EnabledSlot} {g.Name} ({g.JobName} i{g.ItemLevel})");
        var text = string.Join("; ", lines);
        if (list.Count > maxItems)
        {
            text += language == PluginLanguage.English
                ? $"... ({list.Count} total)"
                : $"... 等共 {list.Count} 个";
        }

        return text;
    }

    private string ResolveClassJobName(byte classJobId)
    {
        if (classJobId == 0)
        {
            return string.Empty;
        }

        var sheet = dataManager.GetExcelSheet<ClassJob>();
        if (!sheet.TryGetRow(classJobId, out var row))
        {
            return $"Job{classJobId}";
        }

        return row.Name.ExtractText();
    }

    private static RaptureGearsetModule* GetGearsetModule()
    {
        var uiModule = UIModule.Instance();
        return uiModule == null ? null : uiModule->GetRaptureGearsetModule();
    }
}
