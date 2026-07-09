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

    public MainWindow(Plugin plugin)
        : base("AI 互动###AICompanionMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        plugin.ActivityLog.Changed += OnLogChanged;
    }

    public void Dispose()
    {
        plugin.ActivityLog.Changed -= OnLogChanged;
    }

    private void OnLogChanged()
    {
        // 触发重绘；滚动在 Draw 中处理
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        var runtime = plugin.RuntimeControls;
        var statusColor = config.Enabled ? new Vector4(0.2f, 0.9f, 0.3f, 1f) : new Vector4(0.9f, 0.3f, 0.2f, 1f);
        var statusText = config.Enabled ? "运行中" : "已停止";

        ImGui.TextColored(statusColor, $"状态: {statusText}");
        ImGui.TextUnformatted($"模型: {config.Model}");
        ImGui.TextUnformatted($"运行时: {runtime.StatusSummary}");
        ImGui.TextUnformatted($"最近决策: {plugin.OrchestratorLastDecision}");

        DrawRuntimeToggles(runtime);
        DrawServiceStatus();

        ImGui.Spacing();

        if (ImGui.Button("打开设置"))
        {
            plugin.ToggleConfigUI();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(isTestingConnection))
        {
            if (ImGui.Button("测试 AI 连接"))
            {
                _ = TestConnectionAsync();
            }
        }

        if (!string.IsNullOrEmpty(testResult))
        {
            ImGui.TextUnformatted(testResult);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("活动日志");
        ImGui.SameLine();
        ImGui.Checkbox("自动滚动", ref autoScrollLog);
        ImGui.Separator();

        var logLines = plugin.ActivityLog.Snapshot();
        using (var child = ImRaii.Child("ActivityLogChild", new Vector2(0, 0), true))
        {
            foreach (var line in logLines)
            {
                ImGui.TextUnformatted(line);
            }

            if (autoScrollLog && logLines.Count != lastLogCount && logLines.Count > 0)
            {
                ImGui.SetScrollHereY(1f);
            }
        }

        lastLogCount = logLines.Count;
    }

    private static void DrawRuntimeToggles(RuntimeControls runtime)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("运行时开关（等同管理员私聊指令）");

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

        if (ImGui.Button("重置运行时开关"))
        {
            runtime.Reset();
        }
    }

    private void DrawServiceStatus()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("服务状态");

        var vnav = plugin.NavmeshIpc;
        var vnavText = vnav.IsAvailable
            ? vnav.IsReady() ? "vnav: 就绪" : "vnav: 未就绪"
            : "vnav: 未安装";
        ImGui.TextUnformatted(vnavText);
        ImGui.SameLine();
        ImGui.TextUnformatted($"交易监控: {plugin.TradeMonitor.PendingCount} 笔待处理");
        ImGui.SameLine();
        ImGui.TextUnformatted($"物品索引: {plugin.InventoryService.ItemCount}");
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
