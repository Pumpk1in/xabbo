using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;

using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Views;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public partial class MainWindow : AppWindow
{
    public MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;

        if (Application.Current is { } app)
            app.ResourcesChanged += OnApplicationResourcesChanged;
        SyncProgressRingVisibility();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        TitleBarHost.ColumnDefinitions[3].Width = new GridLength(TitleBar.RightInset, GridUnitType.Pixel);
    }

    private void OnApplicationResourcesChanged(object? sender, global::Avalonia.Controls.ResourcesChangedEventArgs e)
        => SyncProgressRingVisibility();

    private void SyncProgressRingVisibility()
    {
        if (Application.Current is not { } app) return;
        bool isReady = app.Resources.TryGetResource("IsReady", null, out var v) && v is true;
        OverlayProgressRing.IsVisible = !isReady;
    }
}
