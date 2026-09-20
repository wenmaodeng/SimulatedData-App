using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NetCoreServer;
using SimulatedDataApp.Models;

namespace SimulatedDataApp.Services;

/// <summary>
/// 基于 NetCoreServer 的 TCP 服务端。为每个连接维护 <see cref="ClientInfo"/>，
/// 只向勾选了“发送”的客户端逐行发送数据（行尾追加 CRLF）。
/// </summary>
public sealed class TcpTransportService : ITransportService
{
    private readonly TcpServerHost _server;
    private readonly ConcurrentDictionary<Guid, ClientEntry> _clients = new();

    public event Action<ClientInfo>? ClientConnected;
    public event Action<string>? ClientDisconnected;
    public event Action<string>? MessageLogged;

    public bool IsRunning => _server.IsStarted;

    public TcpTransportService(string ipAddress, int port)
    {
        if (!IPAddress.TryParse(ipAddress.Trim(), out var address))
            throw new ArgumentException($"IP 地址格式不正确：{ipAddress}");
        _server = new TcpServerHost(address, port, this);
    }

    public void Start()
    {
        if (!_server.Start())
            throw new InvalidOperationException("TCP 服务启动失败，端口可能已被占用。");
    }

    public void Stop() => _server.Stop();

    public int SendLine(string line)
        // TCP 文件行以 CRLF 结尾；TCP 不区分文本/二进制，asText 仅作语义占位。
        => SendData(Encoding.UTF8.GetBytes(line + "\r\n"), true);

    public int SendData(byte[] payload, bool asText)
    {
        var sent = 0;

        foreach (var entry in _clients.Values.ToArray())
        {
            var info = entry.Info;
            if (!info.Enabled)
                continue;
            if (!entry.Session.IsConnected)
                continue;

            try
            {
                entry.Session.Send(payload);
                info.SentCount++;
                sent++;
            }
            catch (Exception ex)
            {
                MessageLogged?.Invoke($"向 {info.EndPoint} 发送失败：{ex.Message}");
            }
        }

        return sent;
    }

    public void Dispose()
    {
        _server.Dispose();
        _clients.Clear();
    }

    internal void AddClient(Guid id, TcpSession session, ClientInfo info)
    {
        _clients[id] = new ClientEntry(session, info);
        ClientConnected?.Invoke(info);
        MessageLogged?.Invoke($"TCP 客户端接入：{info.EndPoint}");
    }

    internal void RemoveClient(Guid id, ClientInfo? info)
    {
        if (_clients.TryRemove(id, out _) && info is not null)
        {
            ClientDisconnected?.Invoke(info.Id);
            MessageLogged?.Invoke($"TCP 客户端断开：{info.EndPoint}");
        }
    }

    internal void Log(string message) => MessageLogged?.Invoke(message);

    private sealed record ClientEntry(TcpSession Session, ClientInfo Info);

    private sealed class TcpServerHost : TcpServer
    {
        private readonly TcpTransportService _owner;

        public TcpServerHost(IPAddress address, int port, TcpTransportService owner)
            : base(address, port) => _owner = owner;

        protected override TcpSession CreateSession() => new ClientTcpSession(this, _owner);

        protected override void OnStarted()
            => _owner.Log($"TCP 服务已启动，监听 {Endpoint}");

        protected override void OnStopped()
            => _owner.Log("TCP 服务已停止");

        protected override void OnError(SocketError error)
            => _owner.Log($"TCP 服务发生错误：{error}");
    }

    private sealed class ClientTcpSession : TcpSession
    {
        private readonly TcpTransportService _owner;
        private ClientInfo? _info;

        public ClientTcpSession(TcpServer server, TcpTransportService owner)
            : base(server) => _owner = owner;

        protected override void OnConnected()
        {
            var endpoint = Socket?.RemoteEndPoint?.ToString() ?? Id.ToString();
            _info = new ClientInfo
            {
                Id = Id.ToString(),
                EndPoint = endpoint
            };
            _owner.AddClient(Id, this, _info);
        }

        protected override void OnDisconnected()
            => _owner.RemoveClient(Id, _info);

        protected override void OnError(SocketError error)
            => _owner.Log($"TCP 会话 {_info?.EndPoint ?? Id.ToString()} 错误：{error}");
    }
}
