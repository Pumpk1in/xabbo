using Avalonia.Controls;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();

        AttachedToVisualTree += (s, e) =>
        {
            if (DataContext is SettingsPageViewModel vm)
            {
                vm.RefreshHistoryCount();
            }
        };
    }
}