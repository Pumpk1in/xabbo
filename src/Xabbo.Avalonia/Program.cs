using System;
using System.IO;
using ReactiveUI;
using Avalonia;
using Avalonia.ReactiveUI;
using Avalonia.Media.Fonts;
using Avalonia.Media;

namespace Xabbo.Avalonia;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.Avalonia);
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "xabbo", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled exception:\n{e.ExceptionObject}\n\n";
            File.AppendAllText(logPath, msg);
        }
        catch { /* never throw from crash handler */ }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .ConfigureFonts(fm => {
            fm.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:"),
                new Uri("avares://Xabbo.Avalonia/Assets/Fonts")));
        })
        .With(new FontManagerOptions
        {
            DefaultFamilyName = "fonts:#IBM Plex Sans",
            FontFallbacks = [ new FontFallback { FontFamily = "fonts:#Noto Emoji" } ]
        })
        .UseReactiveUI();
}
