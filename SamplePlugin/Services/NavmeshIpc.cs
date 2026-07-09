using System;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace SamplePlugin.Services;

/// <summary>
/// 对外部 vnavmesh 插件的 IPC 封装。若未安装 vnavmesh，所有调用将安全失败并返回 false。
/// </summary>
public sealed class NavmeshIpc
{
    private readonly ActivityLog log;
    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<object> pathStop;

    public NavmeshIpc(IDalamudPluginInterface pluginInterface, ActivityLog log)
    {
        this.log = log;
        navIsReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveTo = pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathfindAndMoveCloseTo = pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathStop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    /// <summary>
    /// 是否检测到 vnavmesh 插件（IPC 可调用）。
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                _ = navIsReady.InvokeFunc();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 导航网格是否已构建完成、可寻路。
    /// </summary>
    public bool IsReady()
    {
        try
        {
            return navIsReady.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    public bool MoveTo(Vector3 destination, bool fly = false)
    {
        try
        {
            return pathfindAndMoveTo.InvokeFunc(destination, fly);
        }
        catch (Exception ex)
        {
            log.Warn($"vnav 寻路调用失败: {ex.Message}");
            return false;
        }
    }

    public bool MoveCloseTo(Vector3 destination, float range, bool fly = false)
    {
        try
        {
            return pathfindAndMoveCloseTo.InvokeFunc(destination, fly, range);
        }
        catch (Exception ex)
        {
            log.Warn($"vnav 寻路(范围)调用失败: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            pathStop.InvokeAction();
        }
        catch
        {
            // 忽略：未安装或调用失败
        }
    }
}
