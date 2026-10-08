using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SimulatedDataApp.Views;

public partial class MainWindow : Window
{
    private bool _dragging;
    private Point _pointerStart;
    private PixelPoint _windowStart;

    public MainWindow()
    {
        InitializeComponent();

        // 标题栏：拖动 + 双击最大化
        TitleBar.PointerPressed += TitleBar_PointerPressed;
        TitleBar.PointerMoved += TitleBar_PointerMoved;
        TitleBar.PointerReleased += TitleBar_PointerReleased;
        TitleBar.DoubleTapped += TitleBar_DoubleTapped;

        // 三个控制按钮
        MinBtn.Click += (_, _) => { WindowState = WindowState.Minimized; UpdateMaxIcon(); };
        MaxBtn.Click += (_, _) => { ToggleMaximize(); UpdateMaxIcon(); };
        CloseBtn.Click += (_, _) => Close();

        UpdateMaxIcon();
    }

    // ---- 手动拖动窗口 ----

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        _pointerStart = e.GetPosition(this);
        _windowStart = Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        var delta = pos - _pointerStart;
        Position = new PixelPoint(
            (int)(_windowStart.X + delta.X),
            (int)(_windowStart.Y + delta.Y));
    }

    private void TitleBar_PointerReleased(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
        UpdateMaxIcon();
    }

    // ---- 最大化切换 ----

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaxIcon()
    {
        MaxBtnText.Text = WindowState == WindowState.Maximized ? "❐" : "□";
    }
}