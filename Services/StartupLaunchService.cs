using System;
using System.IO;
using CaptionScribe.Core.Logging;
using Microsoft.Win32;

namespace CaptionScribe.Services
{
    public sealed class StartupLaunchService : IStartupLaunchService
    {
        internal const string StartupArgument = "--startup";
        internal const string ValueName = "CaptionScribe";

        private static readonly byte[] EnabledApprovedBlob =
            { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private readonly IStartupRegistryStore _store;
        private readonly Func<string?> _exePath;
        private readonly ILog _log;

        public StartupLaunchService(ILog log)
            : this(new RegistryStartupStore(), ResolveExePath, log)
        {
        }

        internal StartupLaunchService(IStartupRegistryStore store, Func<string?> exePath, ILog? log = null)
        {
            _store = store;
            _exePath = exePath;
            _log = log ?? NullLog.Instance;
        }

        public bool IsEnabled()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_store.GetRunCommand()))
                    return false;
                byte[]? approved = _store.GetApproved();
                if (approved is null || approved.Length == 0)
                    return true;
                return (approved[0] & 1) == 0;
            }
            catch (Exception ex)
            {
                _log.Warning("Could not read the Windows startup setting. " + ex.Message);
                return false;
            }
        }

        internal static string? ResolveExePath()
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath)
                && File.Exists(processPath)
                && !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet",
                    StringComparison.OrdinalIgnoreCase))
                return processPath;

            string fallback = Path.Combine(AppContext.BaseDirectory, "CaptionScribe.exe");
            return File.Exists(fallback) ? fallback : processPath;
        }

        public void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                _store.DeleteRunCommand();
                _store.DeleteApproved();
                return;
            }

            string? exe = _exePath();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new InvalidOperationException(
                    "Caption Scribe's executable could not be found, so it cannot be added to startup.");

            _store.SetRunCommand("\"" + exe + "\" " + StartupArgument);
            try
            {
                _store.SetApproved((byte[])EnabledApprovedBlob.Clone());
            }
            catch
            {
                try { _store.DeleteRunCommand(); } catch { /* best-effort rollback */ }
                throw;
            }
        }
    }

    internal interface IStartupRegistryStore
    {
        void DeleteApproved();
        void DeleteRunCommand();
        byte[]? GetApproved();
        string? GetRunCommand();
        void SetApproved(byte[] data);
        void SetRunCommand(string command);
    }

    internal sealed class RegistryStartupStore : IStartupRegistryStore
    {
        private const string ApprovedKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public void DeleteApproved()
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            key?.DeleteValue(StartupLaunchService.ValueName, throwOnMissingValue: false);
        }

        public void DeleteRunCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(StartupLaunchService.ValueName, throwOnMissingValue: false);
        }

        public byte[]? GetApproved()
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            return key?.GetValue(StartupLaunchService.ValueName) as byte[];
        }

        public string? GetRunCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(StartupLaunchService.ValueName) as string;
        }

        private static RegistryKey OpenWritable(string path)
            => Registry.CurrentUser.CreateSubKey(path)
               ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");

        public void SetApproved(byte[] data)
        {
            using var key = OpenWritable(ApprovedKeyPath);
            key.SetValue(StartupLaunchService.ValueName, data, RegistryValueKind.Binary);
        }

        public void SetRunCommand(string command)
        {
            using var key = OpenWritable(RunKeyPath);
            key.SetValue(StartupLaunchService.ValueName, command, RegistryValueKind.String);
        }
    }
}
