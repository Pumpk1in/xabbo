using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;

using Xabbo.Services.Abstractions;

namespace Xabbo.Avalonia.Services;

public class ClipboardService(Application app, IUiContext uiContext) : IClipboardService
{
    private readonly Application _app = app;
    private readonly IUiContext _uiContext = uiContext;

    public void SetText(string text)
    {
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: IClipboard clipboard })
        {
            clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: IClipboard clipboard })
            return null;

        return await clipboard.TryGetTextAsync();
    }
}