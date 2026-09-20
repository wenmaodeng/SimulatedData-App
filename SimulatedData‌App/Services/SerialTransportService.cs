using System.Runtime.InteropServices;
using System.Text;
using RJCP.IO.Ports;
using RJCP.IO.Ports.Serial;
using SimulatedDataApp.Models;

namespace SimulatedDataApp.Services;

/// <summary>
/// 基于 RJCP.SerialPortStream 的串口发送通道。
/// 串口没有“客户端”概念，每行数据写入串口（行尾追加 CRLF）。
/// </summary>
public sealed class SerialTransportService : ITransportService
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPortStream? _serial;

    // 串口没有客户端概念，仅为满足统一接口而声明（事件不会触发）。
    public event Action<ClientInfo>? ClientConnected { add { } remove { } }
    public event Action<string>? ClientDisconnected { add { } remove { } }
    public event Action<string>? MessageLogged;

    public SerialTransportService(string portName, int baudRate)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("请选择串口号。");
        _portName = portName.Trim();
        _baudRate = baudRate;
    }

    public bool IsRunning => _serial?.IsOpen == true;

    /// <summary>枚举当前系统可用串口（按名称排序）。</summary>
    public static string[] GetPortNames()
    {
        // RJCP 3.x 中 GetPortNames 是实例方法。
        using var probe = CreateStream();
        return probe.GetPortNames()
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 创建串口流。RJCP 3.0.5 依赖的平台检测库不再把 macOS 识别为 Unix，
    /// 会导致默认构造抛出 NotSupportedException；因此在 macOS 上手动注入
    /// 基于自编译 libnserial 的 <see cref="UnixNativeSerial"/>。
    /// </summary>
    private static SerialPortStream CreateStream()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new SerialPortStream(new UnixNativeSerial());

        return new SerialPortStream();
    }

    public void Start()
    {
        var serial = CreateStream();
        serial.PortName = _portName;
        serial.BaudRate = _baudRate;
        // 8N1
        serial.DataBits = 8;
        serial.Parity = Parity.None;
        serial.StopBits = StopBits.One;
        serial.Open();
        _serial = serial;
        MessageLogged?.Invoke($"串口已打开：{_portName}，波特率 {_baudRate}，8N1");
    }

    public void Stop()
    {
        var serial = _serial;
        _serial = null;
        if (serial is null)
            return;

        try
        {
            if (serial.IsOpen)
                serial.Close();
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke($"关闭串口时发生错误：{ex.Message}");
        }
        finally
        {
            serial.Dispose();
            MessageLogged?.Invoke($"串口已关闭：{_portName}");
        }
    }

    public int SendLine(string line)
    {
        var serial = _serial;
        if (serial?.IsOpen != true)
            return 0;

        var payload = Encoding.UTF8.GetBytes(line + "\r\n");
        serial.Write(payload, 0, payload.Length);
        serial.Flush();
        return 1;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
