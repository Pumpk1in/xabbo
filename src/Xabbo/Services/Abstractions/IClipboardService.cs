namespace Xabbo.Services.Abstractions;

public interface IClipboardService
{
    void SetText(string text);
    Task<string?> GetTextAsync();
}
