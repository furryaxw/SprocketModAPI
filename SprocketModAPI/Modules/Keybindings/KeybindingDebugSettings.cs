using System;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;

namespace SprocketModAPI
{
    internal sealed class KeybindingDebugSettings
    {
        internal bool Enabled { get; private set; }
        internal bool LogRouting { get; private set; } = true;
        internal bool LogBindings { get; private set; } = true;
        internal bool LogEveryFrame { get; private set; }

        internal static KeybindingDebugSettings Load(Action<string> warn)
        {
            string path = Path.Combine(MelonEnvironment.UserDataDirectory, "SprocketModAPI", "keybindings.debug.json");
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, JsonSerializer.Serialize(new ConfigFile(), new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                    return new KeybindingDebugSettings();
                }

                ConfigFile? config = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path));
                if (config == null)
                    throw new InvalidDataException("Debug configuration is empty.");
                return new KeybindingDebugSettings
                {
                    Enabled = config.Enabled,
                    LogRouting = config.LogRouting,
                    LogBindings = config.LogBindings,
                    LogEveryFrame = config.LogEveryFrame
                };
            }
            catch (Exception exception)
            {
                warn($"[SMA] keybinding debug configuration unavailable: {exception.Message}");
                return new KeybindingDebugSettings();
            }
        }

        private sealed class ConfigFile
        {
            public bool Enabled { get; set; }
            public bool LogRouting { get; set; } = true;
            public bool LogBindings { get; set; } = true;
            public bool LogEveryFrame { get; set; }
        }
    }

    internal sealed class KeybindingDebugLog
    {
        private readonly KeybindingDebugSettings settings;
        private readonly Action<string> write;
        internal KeybindingDebugLog(KeybindingDebugSettings settings, Action<string> write)
        {
            this.settings = settings;
            this.write = write;
        }

        internal bool Enabled => settings.Enabled;
        internal bool LogEveryFrame => settings.LogEveryFrame;
        internal void Routing(string message)
        {
            if (settings.Enabled && settings.LogRouting)
                write($"[SMA-KEY-ROUTE] {message}");
        }

        internal void Binding(string message, bool force = false)
        {
            if (settings.Enabled && settings.LogBindings && (force || settings.LogEveryFrame))
                write($"[SMA-KEY] {message}");
        }
    }
}
