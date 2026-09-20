using System;
using SimulatedDataApp.Models;

namespace SimulatedDataApp.Services;

/// <summary>
/// 数据发送通道的统一抽象：TCP 服务端、WebSocket 服务端、串口各自实现。
/// 回调可能在工作线程触发，订阅方需要自行切回 UI 线程。
/// </summary>
public interface ITransportService : IDisposable
{
    /// <summary>有客户端接入（仅网络方式会触发）。</summary>
    event Action<ClientInfo>? ClientConnected;

    /// <summary>有客户端断开（参数为 ClientInfo.Id）。</summary>
    event Action<string>? ClientDisconnected;

    /// <summary>需要展示到界面日志区的消息。</summary>
    event Action<string>? MessageLogged;

    /// <summary>通道是否已启动。</summary>
    bool IsRunning { get; }

    /// <summary>启动监听 / 打开串口。</summary>
    void Start();

    /// <summary>停止监听 / 关闭串口。</summary>
    void Stop();

    /// <summary>
    /// 发送一行数据。
    /// </summary>
    /// <param name="line">文本文件中的一行（不含换行符）。</param>
    /// <returns>实际投递到的目标数量（选中的客户端数；串口成功为 1）。</returns>
    int SendLine(string line);
}
