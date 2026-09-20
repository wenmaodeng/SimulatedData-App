using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatedDataApp.Models;
using SimulatedDataApp.Services;

namespace SimulatedDataApp.ViewModels;

/// <summary>通信方式下拉框选项。</summary>
public sealed class ModeOption
{
    public CommTransport Mode { get; }
    public string Text { get; }

    public ModeOption(CommTransport mode, string text)
    {
        Mode = mode;
        Text = text;
    }
}

public partial class MainViewModel : ViewModelBase
{
    /// <summary>每行数据的发送间隔（毫秒），需求固定为 200ms。</summary>
    private const int SendIntervalMs = 200;

    private const int MaxLogEntries = 500;

    // 通信设置
    [ObservableProperty]
    private CommTransport _selectedMode = CommTransport.TcpServer;

    [ObservableProperty]
    private string _ipAddress = "0.0.0.0";

    [ObservableProperty]
    private int _port = 8888;

    [ObservableProperty]
    private string _wsPath = "/ws";

    [ObservableProperty]
    private ObservableCollection<string> _serialPorts = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty]
    private int _selectedBaudRate = 115200;

    // 数据文件 / 发送状态
    [ObservableProperty]
    private string? _dataFilePath;

    [ObservableProperty]
    private bool _loop;

    // 手动发送
    [ObservableProperty]
    private string _manualData = string.Empty;

    [ObservableProperty]
    private bool _manualIsHex;

    [ObservableProperty]
    private bool _running;

    [ObservableProperty]
    private int _totalLines;

    [ObservableProperty]
    private int _currentLineNumber;

    [ObservableProperty]
    private string? _currentLineText;

    [ObservableProperty]
    private long _sentLines;

    [ObservableProperty]
    private int _clientCount;

    [ObservableProperty]
    private string _statusText = "就绪";

    public ObservableCollection<ClientInfo> Clients { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    public int[] BaudRates { get; } =
    {
        1200, 2400, 4800, 9600, 19200, 38400, 57600,
        115200, 230400, 460800, 921600
    };

    public IReadOnlyList<ModeOption> ModeOptions { get; } = new[]
    {
        new ModeOption(CommTransport.TcpServer, "TCP 服务端"),
        new ModeOption(CommTransport.WebSocketServer, "WebSocket 服务端"),
        new ModeOption(CommTransport.Serial, "串口")
    };

    public int SendInterval => SendIntervalMs;

    /// <summary>手动发送输入框的提示文本，随字符串 / HEX 模式切换。</summary>
    public string ManualPlaceholder => ManualIsHex
        ? "HEX 格式，字节间可用空格分隔，如：48 65 6C 6C 6F"
        : "输入要手动发送的字符串（UTF-8 原样发送，不追加换行）";

    public bool IsNetworkMode => SelectedMode != CommTransport.Serial;
    public bool IsSerialMode => SelectedMode == CommTransport.Serial;
    public bool IsWebSocketMode => SelectedMode == CommTransport.WebSocketServer;

    /// <summary>Avalonia ComboBox 没有 SelectedValue，直接绑定选中的选项对象。</summary>
    public ModeOption? SelectedModeOption
    {
        get => ModeOptions.FirstOrDefault(o => o.Mode == SelectedMode);
        set
        {
            if (value is not null && value.Mode != SelectedMode)
                SelectedMode = value.Mode;
        }
    }

    private IReadOnlyList<string>? _lines;
    private ITransportService? _transport;
    private CancellationTokenSource? _sendCts;

    public MainViewModel()
    {
        if (!Avalonia.Controls.Design.IsDesignMode)
            RefreshPorts();
    }

    partial void OnSelectedModeChanged(CommTransport value)
    {
        OnPropertyChanged(nameof(IsNetworkMode));
        OnPropertyChanged(nameof(IsSerialMode));
        OnPropertyChanged(nameof(IsWebSocketMode));
        OnPropertyChanged(nameof(SelectedModeOption));
    }

    partial void OnRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RefreshPortsCommand.NotifyCanExecuteChanged();
        BrowseFileCommand.NotifyCanExecuteChanged();
        ManualSendCommand.NotifyCanExecuteChanged();
    }

    partial void OnManualDataChanged(string value)
        => ManualSendCommand.NotifyCanExecuteChanged();

    partial void OnManualIsHexChanged(bool value)
        => OnPropertyChanged(nameof(ManualPlaceholder));

    [RelayCommand(CanExecute = nameof(CanRefreshPorts))]
    private void RefreshPorts()
    {
        var previous = SelectedSerialPort;
        SerialPorts.Clear();
        try
        {
            foreach (var name in SerialTransportService.GetPortNames())
                SerialPorts.Add(name);

            if (previous is not null && SerialPorts.Contains(previous))
                SelectedSerialPort = previous;
            else
                SelectedSerialPort = SerialPorts.FirstOrDefault();

            Log($"已枚举到 {SerialPorts.Count} 个串口。");
        }
        catch (Exception ex)
        {
            Log($"枚举串口失败：{ex.Message}");
            Log("提示：RJCP.SerialPortStream 在 Linux/macOS 上依赖原生 libnserial 库。");
        }
    }

    private bool CanRefreshPorts() => !Running;

    [RelayCommand(CanExecute = nameof(CanBrowseFile))]
    private async Task BrowseFileAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择模拟数据文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("文本文件")
                {
                    Patterns = new[] { "*.txt", "*.log", "*.csv", "*.dat", "*.json" },
                    MimeTypes = new[] { "text/plain" }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0)
            return;

        DataFilePath = files[0].Path.LocalPath;
        LoadDataFile();
    }

    private bool CanBrowseFile() => !Running;

    private void LoadDataFile()
    {
        try
        {
            _lines = File.ReadAllLines(DataFilePath!);
            TotalLines = _lines.Count;
            CurrentLineNumber = 0;
            CurrentLineText = null;
            SentLines = 0;
            Log($"已加载数据文件：{DataFilePath}，共 {TotalLines} 行。");
            if (TotalLines == 0)
                Log("警告：文件内容为空。");
        }
        catch (Exception ex)
        {
            _lines = null;
            TotalLines = 0;
            Log($"读取数据文件失败：{ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!EnsureReadyToStart())
            return;

        try
        {
            _transport = CreateTransport();
            _transport.ClientConnected += OnClientConnected;
            _transport.ClientDisconnected += OnClientDisconnected;
            _transport.MessageLogged += OnMessageLogged;
            _transport.Start();
        }
        catch (Exception ex)
        {
            Log($"启动失败：{ex.Message}");
            CleanupTransport();
            return;
        }

        Running = true;

        // 未加载数据文件：仅打开通道（监听 / 打开串口），等待手动发送。
        if (_lines is not { Count: > 0 })
        {
            StatusText = SelectedMode == CommTransport.Serial
                ? "串口已打开，可手动发送数据"
                : "服务监听中，可手动发送数据";
            Log("未选择数据文件，通道保持打开，可使用“手动发送”；点击“停止”关闭通道。");
            return;
        }

        StatusText = "正在发送数据…";

        _sendCts = new CancellationTokenSource();
        var completed = await SendLoopAsync(_sendCts.Token);
        await ShutdownAsync(completed);
    }

    /// <summary>按固定间隔逐行发送文件数据，返回是否自然发送完成（未被停止/出错打断）。</summary>
    private async Task<bool> SendLoopAsync(CancellationToken token)
    {
        var completed = true;
        var index = 0;

        while (!token.IsCancellationRequested)
        {
            if (index >= _lines!.Count)
            {
                if (Loop)
                {
                    index = 0;
                    Log("数据已发送完一轮，从头开始循环发送。");
                }
                else
                {
                    break;
                }
            }

            var line = _lines[index];
            CurrentLineNumber = index + 1;
            CurrentLineText = line;

            try
            {
                var targets = _transport!.SendLine(line);
                SentLines++;
                if (SelectedMode == CommTransport.Serial && targets == 0)
                    Log("警告：串口未就绪，本行数据未发出。");
            }
            catch (Exception ex)
            {
                Log($"发送数据时出错：{ex.Message}");
                completed = false;
                break;
            }

            index++;

            try
            {
                await Task.Delay(SendIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                completed = false;
                break;
            }
        }

        return completed && !token.IsCancellationRequested;
    }

    private bool CanStart() => !Running;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        StatusText = "正在停止…";

        // 纯手动模式下没有定时循环，直接关闭通道。
        if (_sendCts is null)
        {
            _ = ShutdownAsync(false);
            return;
        }

        _sendCts.Cancel();
    }

    private bool CanStop() => Running;

    [RelayCommand(CanExecute = nameof(CanManualSend))]
    private void ManualSend()
    {
        var transport = _transport;
        if (transport is null || !transport.IsRunning)
        {
            Log("通道未启动，请先点击“开始发送”打开通道。");
            return;
        }

        if (!ManualInputCodec.TryEncode(ManualData, ManualIsHex, out var payload, out var error))
        {
            Log("手动发送失败：" + error);
            return;
        }

        try
        {
            // 字符串模式按文本帧发送，HEX 模式按二进制帧发送。
            var targets = transport.SendData(payload, !ManualIsHex);
            SentLines++;
            CurrentLineText = ManualIsHex ? ManualInputCodec.ToHexString(payload) : ManualData;

            string targetDesc = SelectedMode == CommTransport.Serial
                ? string.Empty
                : targets > 0
                    ? $"，投递到 {targets} 个客户端"
                    : "，但当前没有勾选的在线客户端";
            var preview = ManualIsHex ? ManualInputCodec.ToHexString(payload) : ManualData;
            Log($"手动发送（{(ManualIsHex ? "HEX" : "字符串")}）{payload.Length} 字节{targetDesc}：{preview}");
        }
        catch (Exception ex)
        {
            Log($"手动发送失败：{ex.Message}");
        }
    }

    private bool CanManualSend() => Running && !string.IsNullOrWhiteSpace(ManualData);

    private bool EnsureReadyToStart()
    {
        // 数据文件为可选项：不选文件时仅打开通道，使用手动发送。
        if (!string.IsNullOrWhiteSpace(DataFilePath))
        {
            if (!File.Exists(DataFilePath))
            {
                Log($"数据文件不存在：{DataFilePath}");
                return false;
            }

            if (_lines is null)
                LoadDataFile();
        }

        if (Port is <= 0 or > 65535)
        {
            Log("端口号必须在 1 ~ 65535 之间。");
            return false;
        }

        if (SelectedMode == CommTransport.Serial && string.IsNullOrWhiteSpace(SelectedSerialPort))
        {
            Log("请选择串口号。");
            return false;
        }

        return true;
    }

    private ITransportService CreateTransport() => SelectedMode switch
    {
        CommTransport.TcpServer => new TcpTransportService(IpAddress, Port),
        CommTransport.WebSocketServer => new WebSocketTransportService(IpAddress, Port, WsPath),
        CommTransport.Serial => new SerialTransportService(SelectedSerialPort!, SelectedBaudRate),
        _ => throw new InvalidOperationException($"不支持的通信方式：{SelectedMode}")
    };

    private async Task ShutdownAsync(bool completed)
    {
        var transport = _transport;
        if (transport is not null)
        {
            transport.ClientConnected -= OnClientConnected;
            transport.ClientDisconnected -= OnClientDisconnected;
            transport.MessageLogged -= OnMessageLogged;
            try
            {
                transport.Stop();
            }
            catch (Exception ex)
            {
                Log($"停止通道时出错：{ex.Message}");
            }
            transport.Dispose();
            _transport = null;
        }

        _sendCts = null;
        Clients.Clear();
        ClientCount = 0;
        Running = false;
        StatusText = completed ? "全部数据发送完成" : "已停止";
        Log(completed ? "全部数据发送完成。" : "已停止发送。");
        await Task.CompletedTask;
    }

    private void CleanupTransport()
    {
        var transport = _transport;
        if (transport is null)
            return;

        transport.ClientConnected -= OnClientConnected;
        transport.ClientDisconnected -= OnClientDisconnected;
        transport.MessageLogged -= OnMessageLogged;
        transport.Dispose();
        _transport = null;
    }

    private void OnClientConnected(ClientInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Clients.Add(info);
            ClientCount = Clients.Count;
        });
    }

    private void OnClientDisconnected(string id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (var i = 0; i < Clients.Count; i++)
            {
                if (Clients[i].Id != id)
                    continue;
                Clients.RemoveAt(i);
                break;
            }
            ClientCount = Clients.Count;
        });
    }

    private void OnMessageLogged(string message) => Log(message);

    private void Log(string message)
    {
        void Append()
        {
            Logs.Add($"{DateTime.Now:HH:mm:ss.fff}  {message}");
            while (Logs.Count > MaxLogEntries)
                Logs.RemoveAt(0);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Append();
        else
            Dispatcher.UIThread.Post(Append);
    }
}
