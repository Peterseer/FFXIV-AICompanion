using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace SamplePlugin.Services;

/// <summary>
/// 加载插件 UI 素材（FF14 官方风格 PNG）。
/// </summary>
public sealed class UiAssetService
{
    private readonly ITextureProvider textureProvider;
    private readonly string assetsRoot;
    private readonly Dictionary<string, ISharedImmediateTexture> textures = new(StringComparer.OrdinalIgnoreCase);

    public UiAssetService(ITextureProvider textureProvider, string assemblyDirectory)
    {
        this.textureProvider = textureProvider;
        assetsRoot = Path.Combine(assemblyDirectory, "Assets", "UI");
    }

    public void EnsureLoaded()
    {
        Load("panel_bg", "panel_bg.png");
    }

    /// <param name="alpha">装饰图透明度，建议 0.03~0.06，过高会影响表单可读性。</param>
    public void DrawPanelBackground(float alpha = 0.04f)
    {
        if (!textures.TryGetValue("panel_bg", out var tex))
        {
            return;
        }

        var wrap = tex.GetWrapOrEmpty();
        if (wrap.Handle == nint.Zero)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var tint = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1f, 1f, 1f, alpha));
        drawList.AddImage(wrap.Handle, min, max, System.Numerics.Vector2.Zero, System.Numerics.Vector2.One, tint);
    }

    public void DrawGameIcon(uint iconId, float size = 24f)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new System.Numerics.Vector2(size, size));
            return;
        }

        var texture = textureProvider.GetFromGameIcon(new GameIconLookup(iconId, false, true));
        var wrap = texture.GetWrapOrEmpty();
        if (wrap.Handle != nint.Zero)
        {
            ImGui.Image(wrap.Handle, new System.Numerics.Vector2(size, size));
        }
        else
        {
            ImGui.Dummy(new System.Numerics.Vector2(size, size));
        }
    }

    private void Load(string key, string fileName)
    {
        if (textures.ContainsKey(key))
        {
            return;
        }

        var path = Path.Combine(assetsRoot, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        textures[key] = textureProvider.GetFromFile(new FileInfo(path));
    }
}
