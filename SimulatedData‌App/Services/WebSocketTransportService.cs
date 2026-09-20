using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;
using SimulatedDataApp.Models;

namespace SimulatedDataApp.Services;

/// <summary>
/// 基于 NetCoreServer 的 WebSocket 服务端。
/// 仅接受指定路径（如 /ws）上的连接，每行数据以文本帧发送给勾选的客户端。
/// </summary>
public sealed class WebSocketTransportService : ITransportService
{
    private readonly WsServerHost _server;
    private readonly string _path;
    private readonly ConcurrentDictionary<Guid, ClientEntry> _clients = new();

    public event Action<ClientInfo>? ClientConnected;
    public event Action<string>? ClientDisconnected;
    public event Action<string>? MessageLogged;

    public bool IsRunning => _server.IsStarted;

    public WebSocketTransportService(string ipAddress, int port, string path)
    {
        if (!IPAddress.TryParse(ipAddress.Trim(), out var address))
            throw new ArgumentException($"IP 地址格式不正确：{ipAddress}");
        _path = NormalizePath(path);
        _server = new WsServerHost(address, port, this);
    }

    public void Start()
    {
        if (!_server.Start())
            throw new InvalidOperationException("WebSocket 服务启动失败，端口可能已被占用。");
    }

    public void Stop() => _server.Stop();

    public int SendLine(string line)
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
                entry.Session.SendText(line);
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

    internal string Path => _path;

    internal void AddClient(Guid id, WsSession session, ClientInfo info)
    {
        _clients[id] = new ClientEntry(session, info);
        ClientConnected?.Invoke(info);
        MessageLogged?.Invoke($"WebSocket 客户端接入：{info.EndPoint}");
    }

    internal void RemoveClient(Guid id, ClientInfo? info)
    {
        if (_clients.TryRemove(id, out _) && info is not null)
        {
            ClientDisconnected?.Invoke(info.Id);
            MessageLogged?.Invoke($"WebSocket 客户端断开：{info.EndPoint}");
        }
    }

    internal void Log(string message) => MessageLogged?.Invoke(message);

    private static string NormalizePath(string? path)
    {
        path = (path ?? string.Empty).Trim();
        if (path.Length == 0)
            return "/";
        return path.StartsWith('/') ? path : "/" + path;
    }

    private sealed record ClientEntry(WsSession Session, ClientInfo Info);

    private sealed class WsServerHost : WsServer
    {
        private readonly WebSocketTransportService _owner;

        public WsServerHost(IPAddress address, int port, WebSocketTransportService owner)
            : base(address, port) => _owner = owner;

        protected override TcpSession CreateSession() => new ClientWsSession(this, _owner);

        protected override void OnStarted()
            => _owner.Log($"WebSocket 服务已启动，监听 ws://{Endpoint}{_owner.Path}");

        protected override void OnStopped()
            => _owner.Log("WebSocket 服务已停止");

        protected override void OnError(SocketError error)
            => _owner.Log($"WebSocket 服务发生错误：{error}");
    }

    private sealed class ClientWsSession : WsSession
    {
        private readonly WebSocketTransportService _owner;
        private ClientInfo? _info;

        public ClientWsSession(WsServer server, WebSocketTransportService owner)
            : base(server) => _owner = owner;

        // 返回 false 可拒绝本次升级握手。
        public override bool OnWsConnecting(HttpRequest request, HttpResponse response)
        {
            var requestPath = (request.Url ?? "/").Split('?')[0];
            if (!string.Equals(requestPath, _owner.Path, StringComparison.OrdinalIgnoreCase))
            {
                _owner.Log($"拒绝 WebSocket 连接：路径 {requestPath} 不匹配 {_owner.Path}");
                // 返回 false 时 NetCoreServer 会把 response 原样发给客户端，
                // 这里给一个 404，客户端握手会立即失败而不是一直挂起。
                response.MakeErrorResponse(404, "Not Found", "text/plain");
                return false;
            }
            return true;
        }

        public override void OnWsConnected(HttpRequest request)
        {
            var endpoint = Socket?.RemoteEndPoint?.ToString() ?? Id.ToString();
            _info = new ClientInfo
            {
                Id = Id.ToString(),
                EndPoint = endpoint
            };
            _owner.AddClient(Id, this, _info);
        }

        public override void OnWsDisconnected()
            => _owner.RemoveClient(Id, _info);

        public override void OnWsError(string error)
            => _owner.Log($"WebSocket 会话 {_info?.EndPoint ?? Id.ToString()} 错误：{error}");

        public override void OnWsError(SocketError error)
            => _owner.Log($"WebSocket 会话 {_info?.EndPoint ?? Id.ToString()} 错误：{error}");
    }
}
