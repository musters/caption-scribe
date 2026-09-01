namespace CaptionScribe.Services
{
    /// <summary>Current-user Windows login registration (HKCU Run + StartupApproved).</summary>
    public interface IStartupLaunchService
    {
        bool IsEnabled();
        void SetEnabled(bool enabled);
    }
}
