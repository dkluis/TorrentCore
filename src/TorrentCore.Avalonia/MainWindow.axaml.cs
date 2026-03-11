using Avalonia.Controls;
using TorrentCore.Avalonia.ViewModels;

namespace TorrentCore.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
