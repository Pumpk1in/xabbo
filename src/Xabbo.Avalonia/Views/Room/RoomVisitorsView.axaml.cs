using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class RoomVisitorsView : UserControl
{
    public RoomVisitorsView()
    {
        InitializeComponent();
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not RoomVisitorsViewModel vm)
            return;

        if (sender is not DataGrid dataGrid)
            return;

        vm.ContextSelection = dataGrid.SelectedItem as VisitorViewModel;
    }
}
