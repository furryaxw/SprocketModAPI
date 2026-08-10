using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MelonLoader.Utils;

namespace SprocketModAPI
{
    internal sealed class UiDebugSettings
    {
        internal bool Enabled { get; private set; }
        internal bool LogLifecycle { get; private set; } = true;
        internal bool LogEveryFrame { get; private set; }

        internal static UiDebugSettings Load(Action<string> warn, string? configurationPath = null)
        {
            string path = configurationPath ?? GetDefaultPath();
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonSerializer.Serialize(new ConfigFile(), new JsonSerializerOptions { WriteIndented = true }));
                    return new UiDebugSettings();
                }
                ConfigFile? config = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path));
                if (config == null) throw new InvalidDataException("Debug configuration is empty.");
                return new UiDebugSettings { Enabled = config.Enabled, LogLifecycle = config.LogLifecycle, LogEveryFrame = config.LogEveryFrame };
            }
            catch (Exception exception)
            {
                warn($"[SMA] UI debug configuration unavailable: {exception.Message}");
                return new UiDebugSettings();
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetDefaultPath() => Path.Combine(MelonEnvironment.UserDataDirectory, "SprocketModAPI", "ui.debug.json");
        private sealed class ConfigFile { public bool Enabled { get; set; } public bool LogLifecycle { get; set; } = true; public bool LogEveryFrame { get; set; } }
    }

    internal sealed class UiDebugLog
    {
        private readonly UiDebugSettings settings; private readonly Action<string> write;
        internal UiDebugLog(UiDebugSettings settings, Action<string> write) { this.settings = settings; this.write = write; }
        internal void Lifecycle(string message) { if (settings.Enabled && settings.LogLifecycle) write($"[SMA-UI-TRACE] {message}"); }
        internal void EveryFrame(string message) { if (settings.Enabled && settings.LogEveryFrame) write($"[SMA-UI-TRACE] {message}"); }
    }

    internal sealed class UiStatusBroadcaster : IDisposable
    {
        private readonly Action<string> warn;
        private bool disposed;
        internal UiStatusBroadcaster(Action<string> warn) { this.warn = warn; }
        internal event EventHandler<UiStatusChangedEventArgs>? StatusChanged;
        internal void Publish(object sender, UiStatusChangedEventArgs args)
        {
            if (disposed) return;
            EventHandler<UiStatusChangedEventArgs>? subscribers = StatusChanged;
            if (subscribers == null) return;
            foreach (EventHandler<UiStatusChangedEventArgs> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(sender, args); }
                catch (Exception exception) { warn($"[SMA-UI] status subscriber failed: {exception}"); }
            }
        }
        public void Dispose() { disposed = true; StatusChanged = null; }
    }
}
