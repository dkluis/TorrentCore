#region

using Avalonia.Controls;
using Avalonia.Input;
using TorrentCore.Avalonia.ViewModels;

#endregion

namespace TorrentCore.Avalonia.Views;

public partial class TorrentsView : UserControl
{
    public TorrentsView() { InitializeComponent(); }

    private async void MagnetUriTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TorrentsViewModel viewModel)
        {
            return;
        }

        if (e.Key is not Key.V && e.Key is not Key.Insert)
        {
            return;
        }

        var hasPasteModifier = e.Key == Key.V ?
                e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control) :
                e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!hasPasteModifier)
        {
            return;
        }

        e.Handled = true;
        await viewModel.PasteMagnetAsync();
    }
}
