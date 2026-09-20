using System.Collections.Specialized;
using Avalonia.Controls;
using SimulatedDataApp.ViewModels;

namespace SimulatedDataApp.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.Logs.CollectionChanged += (_, args) =>
        {
            if (args.Action is NotifyCollectionChangedAction.Add
                or NotifyCollectionChangedAction.Reset)
            {
                // 新日志产生后自动滚动到底部。
                if (LogList.ItemCount > 0)
                    LogList.ScrollIntoView(LogList.ItemCount - 1);
            }
        };
    }
}
