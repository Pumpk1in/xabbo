using Avalonia.Controls;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class RoomBansView : UserControl
{
    public RoomBansView()
    {
        InitializeComponent();
    }

    private void DataGridBans_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is RoomBansViewModel viewModel)
        {
            viewModel.SelectedBans = DataGridBans.SelectedItems;
        }
    }
}
