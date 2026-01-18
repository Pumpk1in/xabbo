namespace Xabbo.Services.Abstractions;

public interface IFileExportService
{
    Task<string?> ExportTextAsync(string defaultFileName, string content);
    Task<string?> ExportJsonAsync(string defaultFileName, object data);
}
