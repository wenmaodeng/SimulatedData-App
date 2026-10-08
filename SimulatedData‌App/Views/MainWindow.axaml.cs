using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SimulatedDataApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 标题栏：双击最大化（拖动交给 Avalonia 的 ExtendClientArea 机制）
        TitleBar.DoubleTapped += TitleBar_DoubleTapped;

        // 三个控制按钮
        MinBtn.Click += (_, _) => { WindowState = WindowState.Minimized; UpdateMaxIcon(); };
        MaxBtn.Click += (_, _) => { ToggleMaximize(); UpdateMaxIcon(); };
        CloseBtn.Click += (_, _) => Close();

        UpdateMaxIcon();
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