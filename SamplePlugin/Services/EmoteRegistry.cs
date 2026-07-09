using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace SamplePlugin.Services;

public sealed class UnlockedEmote
{
    public ushort Id { get; init; }

    public string Command { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

public sealed unsafe class EmoteRegistry
{
    private readonly IDataManager dataManager;
    private readonly ActivityLog log;
    private List<UnlockedEmote> cached = [];
    private DateTime lastRefresh = DateTime.MinValue;

    public EmoteRegistry(IDataManager dataManager, ActivityLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public IReadOnlyList<UnlockedEmote> GetUnlockedEmotes(bool forceRefresh = false)
    {
        if (forceRefresh || DateTime.UtcNow - lastRefresh > TimeSpan.FromMinutes(5))
        {
            Refresh();
        }

        return cached;
    }

    public bool IsOwned(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var normalized = command.Trim().TrimStart('/').ToLowerInvariant();
        return GetUnlockedEmotes().Any(e => e.Command.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Refresh()
    {
        var result = new List<UnlockedEmote>();
        var uiState = UIState.Instance();
        if (uiState == null)
        {
            log.Warn("无法读取 UIState，情感动作列表刷新失败。");
            cached = result;
            return;
        }

        var sheet = dataManager.GetExcelSheet<Emote>();
        foreach (var row in sheet)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            if (!uiState->IsEmoteUnlocked((ushort)row.RowId))
            {
                continue;
            }

            var command = ResolveCommand(row);
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            result.Add(new UnlockedEmote
            {
                Id = (ushort)row.RowId,
                Command = command,
                DisplayName = row.Name.ExtractText(),
            });
        }

        cached = result.OrderBy(e => e.Command, StringComparer.OrdinalIgnoreCase).ToList();
        lastRefresh = DateTime.UtcNow;
        log.Info($"已刷新情感动作列表: {cached.Count} 个已解锁。");
    }

    private static string ResolveCommand(Emote row)
    {
        // Lumina Emote.Name 通常为英文命令名（如 wave、deny）
        var name = row.Name.ExtractText().Trim();
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // 去除空格，命令应为单个 token
        var token = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return token.ToLowerInvariant();
    }

    public string BuildPromptList(int maxItems = 80)
    {
        var emotes = GetUnlockedEmotes();
        if (emotes.Count == 0)
        {
            return "（当前未检测到已解锁情感动作）";
        }

        var items = emotes.Take(maxItems).Select(e => $"{e.Command}({e.DisplayName})");
        var text = string.Join(", ", items);
        if (emotes.Count > maxItems)
        {
            text += $"... 等共 {emotes.Count} 个";
        }

        return text;
    }
}
