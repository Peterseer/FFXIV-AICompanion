using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using SamplePlugin.Services;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private bool isTestingConnection;
    private string? testResult;
    private bool autoScrollLog = true;
    private int lastLogCount;
    private int selectedTab;

    public MainWindow(Plugin plugin)
        : base("AI Companion###AICompanionMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        plugin.ActivityLog.Changed += OnLogChanged;
    }

    public void Dispose()
    {
        plugin.ActivityLog.Changed -= OnLogChanged;
    }

    private void OnLogChanged() { }

    public override void Draw()
    {
        plugin.UiAssets.DrawPanelBackground(0.04f);

        DrawHeader();
        DrawTabBar();

        if (selectedTab == 0)
        {
            DrawOverviewTab();
        }
        else
        {
            DrawLogTab();
        }
    }

    private void DrawHeader()
    {
        UiTheme.BeginSection();
        plugin.UiAssets.DrawGameIcon(60443, 26f);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextColored(UiTheme.AccentGold, "AI Companion");
        ImGui.TextColored(UiTheme.Muted, "FFXIV 智能互动助手");
        ImGui.EndGroup();

        var config = plugin.Configuration;
        var enabled = config.Enabled;
        var statusColor = enabled ? UiTheme.Success : UiTheme.Danger;
        var statusText = enabled ? "运行中" : "已停止";

        ImGui.SameLine();
        var badgeWidth = 88f;
        var offset = ImGui.GetContentRegionAvail().X - badgeWidth;
        if (offset > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        }

        if (UiTheme.StatusBadge(statusText, statusColor))
        {
            config.Enabled = !enabled;
            config.Save();
            plugin.ActivityLog.Info(config.Enabled ? "AI 互动已启用。" : "AI 互动已禁用。");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(enabled ? "点击关闭 AI 互动" : "点击启用 AI 互动");
        }

        UiTheme.EndSection();
    }

    private void DrawTabBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));
        UiTheme.Tab("概览", 0, ref selectedTab);
        ImGui.SameLine();
        UiTheme.Tab("活动日志", 1, ref selectedTab);
        ImGui.PopStyleVar();
        ImGui.Dummy(new Vector2(0, 2));
    }

    private void DrawOverviewTab()
    {
        UiTheme.BeginSection();
        UiTheme.SectionHeader("运行状态", plugin.OrchestratorLastDecision);
        UiTheme.Metric("模型", plugin.Configuration.Model);
        ImGui.SameLine(0, 16);
        UiTheme.Metric("语言", PluginLocale.LanguageDisplayName(plugin.Configuration.InteractionLanguage));
        UiTheme.Metric("运行时", plugin.RuntimeControls.StatusSummary);
        UiTheme.EndSection();

        UiTheme.BeginSection();
        UiTheme.SectionHeader("运行时开关", "等同管理员私聊 emote/chat/social on|off");
        DrawRuntimeToggles(plugin.RuntimeControls);
        UiTheme.EndSection();

        UiTheme.BeginSection();
        UiTheme.SectionHeader("服务状态");
        DrawServiceStatus();

        if (ImGui.Button("打开设置", new Vector2(120, 0)))
        {
            plugin.ToggleConfigUI();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(isTestingConnection))
        {
            if (ImGui.Button("测试 AI 连接", new Vector2(130, 0)))
            {
                _ = TestConnectionAsync();
            }
        }

        if (!string.IsNullOrEmpty(testResult))
        {
            ImGui.TextColored(testResult.Contains("成功") ? UiTheme.Success : UiTheme.Warning, testResult);
        }

        UiTheme.EndSection();
    }

    private void DrawLogTab()
    {
        UiTheme.BeginSection();
        ImGui.Checkbox("自动滚动", ref autoScrollLog);
        ImGui.Separator();

        var logLines = plugin.ActivityLog.Snapshot();
        using (var child = ImRaii.Child("ActivityLogChild", new Vector2(0, ImGui.GetContentRegionAvail().Y - 8), true))
        {
            foreach (var line in logLines)
            {
                var color = line.Contains("错误") || line.Contains("失败") || line.Contains("WARN")
                    ? UiTheme.Danger
                    : line.Contains("旅行") || line.Contains("传送") || line.Contains("寻路")
                        ? UiTheme.AccentBlue
                        : new Vector4(0.88f, 0.9f, 0.94f, 1f);
                ImGui.TextColored(color, line);
            }

            if (autoScrollLog && logLines.Count != lastLogCount && logLines.Count > 0)
            {
                ImGui.SetScrollHereY(1f);
            }
        }

        lastLogCount = logLines.Count;
        UiTheme.EndSection();
    }

    private static void DrawRuntimeToggles(RuntimeControls runtime)
    {
        var chat = runtime.ChatEnabled;
        if (ImGui.Checkbox("对话输出", ref chat))
        {
            runtime.ChatEnabled = chat;
        }

        ImGui.SameLine();
        var emote = runtime.EmoteEnabled;
        if (ImGui.Checkbox("情感动作", ref emote))
        {
            runtime.EmoteEnabled = emote;
        }

        ImGui.SameLine();
        var social = runtime.SocialEnabled;
        if (ImGui.Checkbox("社交行为", ref social))
        {
            runtime.SocialEnabled = social;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("重置"))
        {
            runtime.Reset();
        }
    }

    private void DrawServiceStatus()
    {
        var vnav = plugin.NavmeshIpc;
        var vnavText = vnav.IsAvailable
            ? vnav.IsReady() ? "vnav: 就绪" : "vnav: 未就绪"
            : "vnav: 未安装";
        var vnavColor = vnav.IsAvailable && vnav.IsReady() ? UiTheme.Success : UiTheme.Warning;

        plugin.UiAssets.DrawGameIcon(60401, 18f);
        ImGui.SameLine();
        UiTheme.Metric("寻路", vnavText, vnavColor);
        ImGui.SameLine(0, 12);
        plugin.UiAssets.DrawGameIcon(62101, 18f);
        ImGui.SameLine();
        UiTheme.Metric("交易", $"{plugin.TradeMonitor.PendingCount} 笔");
        ImGui.SameLine(0, 12);
        plugin.UiAssets.DrawGameIcon(61801, 18f);
        ImGui.SameLine();
        UiTheme.Metric("物品", $"{plugin.InventoryService.ItemCount} 项");
        ImGui.SameLine(0, 12);
        plugin.UiAssets.DrawGameIcon(60443, 18f);
        ImGui.SameLine();
        UiTheme.Metric("水晶", $"{plugin.AetheryteResolver.GetUnlockedAetherytes().Count} 个");
    }

    private async Task TestConnectionAsync()
    {
        isTestingConnection = true;
        testResult = "测试中...";
        try
        {
            var ok = await plugin.TestAiConnectionAsync().ConfigureAwait(true);
            testResult = ok ? "连接成功" : "连接失败，请查看日志";
        }
        catch (Exception ex)
        {
            testResult = $"测试异常: {ex.Message}";
        }
        finally
        {
            isTestingConnection = false;
        }
    }
}
