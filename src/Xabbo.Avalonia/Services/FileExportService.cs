using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Xabbo.Avalonia.Views;
using Xabbo.Services.Abstractions;

namespace Xabbo.Avalonia.Services;

public class FileExportService(Lazy<MainWindow> mainWindow) : IFileExportService
{
    private readonly Lazy<MainWindow> _mainWindow = mainWindow;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string?> ExportTextAsync(string defaultFileName, string content)
    {
        var topLevel = TopLevel.GetTopLevel(_mainWindow.Value);
        if (topLevel is null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Chat",
            SuggestedFileName = defaultFileName,
            FileTypeChoices =
            [
                new FilePickerFileType("Text File") { Patterns = ["*.txt"] }
            ]
        });

        if (file is null) return null;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);

        return file.Path.LocalPath;
    }

    public async Task<string?> ExportJsonAsync(string defaultFileName, object data)
    {
        var topLevel = TopLevel.GetTopLevel(_mainWindow.Value);
        if (topLevel is null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Chat",
            SuggestedFileName = defaultFileName,
            FileTypeChoices =
            [
                new FilePickerFileType("JSON File") { Patterns = ["*.json"] }
            ]
        });

        if (file is null) return null;

        await using var stream = await file.OpenWriteAsync();
        await JsonSerializer.SerializeAsync(stream, data, JsonOptions);

        return file.Path.LocalPath;
    }
}
