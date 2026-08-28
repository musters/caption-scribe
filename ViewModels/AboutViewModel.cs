using System.Reflection;

namespace CaptionScribe.ViewModels
{
    /// <summary>Read-only display values for the About dialog.</summary>
    public sealed class AboutViewModel
    {
        public string Version { get; }
        public string AutoSavePath { get; }

        public AboutViewModel(string autoSavePath)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Version = $"Version {version?.ToString(3)}";
            AutoSavePath = $"Autosaving to:\n{autoSavePath}";
        }
    }
}
