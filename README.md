# AI Companion

Dalamud 插件源码：AI 驱动的 FF14 玩家互动（聊天回复、交易、组队等）。

## 安装（玩家）

在 XIVLauncherCN **设置 → 实验性功能 → 自定义插件库** 中添加插件库地址（非本仓库）：

```text
https://raw.githubusercontent.com/Peterseer/DalamudPlugins/main/pluginmaster.json
```

然后在插件安装器搜索 **AI Companion** 安装/更新。

## 仓库分工

| 仓库 | 用途 |
|------|------|
| [Peterseer/DalamudSamplePlugin](https://github.com/Peterseer/DalamudSamplePlugin) | 插件源码 + Release 包 |
| [Peterseer/DalamudPlugins](https://github.com/Peterseer/DalamudPlugins) | `pluginmaster.json` 索引 + 图标 |

## 自动发布

推送到 `master` 后 GitHub Actions 会：

1. 编译 Release 并上传 `latest.zip` 到 [Releases](https://github.com/Peterseer/DalamudSamplePlugin/releases)
2. 自动更新 [DalamudPlugins](https://github.com/Peterseer/DalamudPlugins) 中的 `pluginmaster.json`

发布新版本：修改 `SamplePlugin/SamplePlugin.csproj` 的 `<Version>` 后推送即可。

## 本地开发

```powershell
dotnet build SamplePlugin/SamplePlugin.csproj -c Release
```

Release 产物：`SamplePlugin/bin/Release/AICompanion/latest.zip`

## 命令

- `/aic` — 打开主窗口
