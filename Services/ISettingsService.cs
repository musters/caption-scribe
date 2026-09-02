using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Loads and persists <see cref="AppSettings"/>.</summary>
    public interface ISettingsService
    {
        AppSettings Load();
        bool Save(AppSettings settings);
    }
}
