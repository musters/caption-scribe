using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaptionScribe.Core.Logging;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    public sealed class SettingsService : ISettingsService
    {
        private readonly string _path;
        private readonly string _appConfigDir;
        private readonly ILog _log;

        public SettingsService(ILog log, string? directory = null, string? appConfigDirectory = null)
        {
            _log = log;
            var dir = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CaptionScribe");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "settings.json");
            _appConfigDir = appConfigDirectory ?? AppContext.BaseDirectory;
        }

        public AppSettings Load()
        {
            // App-config defaults (appsettings.json) overlaid by the per-user settings file.
            var merged = LoadAppConfigSection() ?? new JsonObject();

            try
            {
                if (File.Exists(_path) && JsonNode.Parse(File.ReadAllText(_path)) is JsonObject user)
                {
                    foreach (var pair in user)
                        merged[pair.Key] = pair.Value?.DeepClone();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Could not read the user settings file; using defaults. " + ex.Message);
            }

            try
            {
                return merged.Deserialize<AppSettings>() ?? new AppSettings();
            }
            catch (Exception ex)
            {
                _log.Warning("Could not parse settings; using defaults. " + ex.Message);
                return new AppSettings();
            }
        }

        private JsonObject? LoadAppConfigSection()
        {
            try
            {
                var configPath = Path.Combine(_appConfigDir, "appsettings.json");
                if (File.Exists(configPath)
                    && JsonNode.Parse(File.ReadAllText(configPath)) is JsonObject root
                    && root["CaptionScribe"] is JsonObject section)
                {
                    return section;
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Could not read appsettings.json; using defaults. " + ex.Message);
            }
            return null;
        }

        public bool Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_path, json);
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning("Could not save settings. " + ex.Message);
                return false;
            }
        }
    }
}
