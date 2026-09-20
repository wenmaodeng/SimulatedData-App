using CommunityToolkit.Mvvm.ComponentModel;

namespace SimulatedDataApp.Models;

/// <summary>
/// 一个已连接的网络客户端（TCP / WebSocket）。
/// </summary>
public partial class ClientInfo : ObservableObject
{
    /// <summary>会话唯一标识（NetCoreServer 会话 Id）。</summary>
    public required string Id { get; init; }

    /// <summary>远端地址，用于界面展示。</summary>
    public required string EndPoint { get; init; }

    /// <summary>是否向该客户端发送模拟数据（界面勾选）。</summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>已发送到该客户端的数据行数。</summary>
    [ObservableProperty]
    private long _sentCount;
}
