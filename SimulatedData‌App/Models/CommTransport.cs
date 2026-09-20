namespace SimulatedDataApp.Models;

/// <summary>
/// 模拟数据的发送方式。
/// </summary>
public enum CommTransport
{
    /// <summary>TCP 服务端（基于 NetCoreServer）。</summary>
    TcpServer,

    /// <summary>WebSocket 服务端（基于 NetCoreServer）。</summary>
    WebSocketServer,

    /// <summary>串口（基于 RJCP.SerialPortStream）。</summary>
    Serial
}
