namespace CaptionScribe.Services
{
    /// <summary>Non-blocking notifications (implemented by the tray).</summary>
    public interface INotificationService
    {
        void Info(string message);
    }
}
