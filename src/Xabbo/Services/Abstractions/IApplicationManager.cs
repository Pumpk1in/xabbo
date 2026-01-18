namespace Xabbo.Services.Abstractions;

public interface IApplicationManager
{
    void BringToFront();
    void FlashWindow();
    void ShowNotification(string title, string message);
}
