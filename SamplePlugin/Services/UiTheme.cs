using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin.Services;

public static class UiTheme
{
    public static readonly Vector4 AccentGold = new(0.92f, 0.78f, 0.38f, 1f);
    public static readonly Vector4 AccentBlue = new(0.45f, 0.72f, 0.95f, 1f);
    public static readonly Vector4 Success = new(0.35f, 0.88f, 0.45f, 1f);
    public static readonly Vector4 Warning = new(0.95f, 0.72f, 0.25f, 1f);
    public static readonly Vector4 Danger = new(0.92f, 0.35f, 0.32f, 1f);
    public static readonly Vector4 Muted = new(0.72f, 0.75f, 0.80f, 1f);

    /// <summary>不透明卡片底，保证表单可读。</summary>
    public static readonly Vector4 CardBg = new(0.11f, 0.12f, 0.15f, 0.96f);

    public static readonly Vector4 CardBorder = new(0.42f, 0.38f, 0.26f, 1f);

    public static void SectionHeader(string title, string? subtitle = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, AccentGold);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();

        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.TextColored(Muted, subtitle);
        }
    }

    /// <summary>
    /// 视觉分组：背景绘制在内容下层（Channels），避免半透明遮罩盖住文字。
    /// </summary>
    public static void BeginSection()
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.BeginGroup();
    }

    public static void EndSection()
    {
        ImGui.EndGroup();
        ImGui.PopStyleVar();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        const float pad = 6f;
        min -= new Vector2(pad, pad);
        max += new Vector2(pad, pad);

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(CardBg), 6f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(CardBorder), 6f, ImDrawFlags.None, 1f);
        drawList.ChannelsSetCurrent(1);
        drawList.ChannelsMerge();

        ImGui.Dummy(new Vector2(0, 4));
    }

    public static bool StatusBadge(string label, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X, color.Y, color.Z, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X, color.Y, color.Z, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(color.X, color.Y, color.Z, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static void Metric(string label, string value, Vector4? valueColor = null)
    {
        ImGui.TextColored(Muted, label);
        ImGui.SameLine();
        ImGui.TextColored(valueColor ?? AccentBlue, value);
    }

    public static void MetricRow(string label, string value, Vector4? valueColor = null)
    {
        ImGui.TextColored(Muted, label);
        ImGui.SameLine(100f);
        ImGui.TextColored(valueColor ?? AccentBlue, value);
    }

    public static bool Tab(string label, int index, ref int selected)
    {
        var active = selected == index;
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(AccentGold.X, AccentGold.Y, AccentGold.Z, 0.72f));
            ImGui.PushStyleColor(ImGuiCol.Text, AccentGold);
        }

        var clicked = ImGui.Button(label);
        if (active)
        {
            ImGui.PopStyleColor(2);
        }

        if (clicked)
        {
            selected = index;
        }

        return clicked;
    }
}
