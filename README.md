# AI Companion

Dalamud 插件：AI 驱动的 FF14 玩家互动（聊天回复、交易、组队等）。

## 自定义插件库（国服 XIVLauncherCN）

在启动器 **设置 → 实验性功能 → 自定义插件库** 中添加（推荐 `pluginmaster.json` 格式）：

```text
https://raw.githubusercontent.com/Peterseer/DalamudSamplePlugin/master/pluginmaster.json
```

也兼容旧格式 `repo.json`：

```text
https://raw.githubusercontent.com/Peterseer/DalamudSamplePlugin/master/repo.json
```

添加后即可在插件安装器里搜索 **AI Companion** 并安装/更新。

## 自动发布

推送到 `master` 分支后，GitHub Actions 会自动：

1. 编译 Release 并打包 `latest.zip`
2. 创建/更新对应版本的 [GitHub Release](https://github.com/Peterseer/DalamudSamplePlugin/releases)
3. 更新 `pluginmaster.json`（以及兼容用的 `repo.json`）中的下载链接与版本号

发布新版本时，只需修改 `SamplePlugin/SamplePlugin.csproj` 中的 `<Version>`，然后推送到 GitHub。

也可在 GitHub **Actions → Build and Publish Plugin → Run workflow** 手动触发。

## 本地开发

```powershell
dotnet build SamplePlugin/SamplePlugin.csproj -c Release
```

Release 产物位于 `SamplePlugin/bin/Release/AICompanion/`（含 `latest.zip` 与完整 manifest）。

默认 Dalamud 路径见 `Directory.Build.props`，可通过环境变量 `DALAMUD_HOME` 覆盖。

## 命令

- `/aic` — 打开主窗口
