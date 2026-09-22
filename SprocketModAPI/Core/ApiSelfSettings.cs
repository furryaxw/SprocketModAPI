using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    internal sealed class ApiSelfSettings : IDisposable
    {
        internal const string UiSection = "ui-diagnostics";
        internal const string KeybindingSection = "keybinding-diagnostics";

        internal const string UiDebugKey = "ui-debug";
        internal const string UiDebugLifecycleKey = "ui-debug-lifecycle";
        internal const string UiDebugEveryFrameKey = "ui-debug-every-frame";
        internal const string KeybindingDebugKey = "keybindings-debug";
        internal const string KeybindingDebugRoutingKey = "keybindings-debug-routing";
        internal const string KeybindingDebugBindingsKey = "keybindings-debug-bindings";
        internal const string KeybindingDebugEveryFrameKey = "keybindings-debug-every-frame";

        internal static ApiSelfSettings? Current { get; private set; }

        private readonly Action<string> warn;
        private IModConfigRegistration? registration;

        internal ApiSelfSettings(IModConfigService service, Action<string> warn)
        {
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            if (service == null) throw new ArgumentNullException(nameof(service));

            registration = service.Register(new ModConfigDefinition
            {
                ModId = ModIdentity.ResolveModId(typeof(ApiSelfSettings).Assembly),
                DisplayName = "Sprocket Mod API",
                Sections = new[]
                {
                    new ModConfigSectionDefinition
                    {
                        Id = UiSection,
                        Title = "UI diagnostics",
                        Description = "Diagnostic logging for the shared UI module. Off by default; only useful when reporting a UI problem."
                    },
                    new ModConfigSectionDefinition
                    {
                        Id = KeybindingSection,
                        Title = "Keybinding diagnostics",
                        Description = "Diagnostic logging for keybinding routing and bindings. Off by default."
                    }
                },
                Entries = new[]
                {
                    ModConfigEntryDefinition.Toggle(UiDebugKey, "UI debug log", false,
                        "Master switch for the UI diagnostic log ([SMA-UI-TRACE]).", UiSection),
                    ModConfigEntryDefinition.Toggle(UiDebugLifecycleKey, "UI debug: lifecycle", true,
                        "Log window/lifecycle events while the UI debug log is on.", UiSection),
                    ModConfigEntryDefinition.Toggle(UiDebugEveryFrameKey, "UI debug: every frame", false,
                        "Log every frame while the UI debug log is on. Noisy.", UiSection),
                    ModConfigEntryDefinition.Toggle(KeybindingDebugKey, "Keybinding debug log", false,
                        "Master switch for keybinding diagnostics ([SMA-KEY], [SMA-KEY-ROUTE]).", KeybindingSection),
                    ModConfigEntryDefinition.Toggle(KeybindingDebugRoutingKey, "Keybinding debug: routing", true,
                        "Log input routing decisions while keybinding diagnostics are on.", KeybindingSection),
                    ModConfigEntryDefinition.Toggle(KeybindingDebugBindingsKey, "Keybinding debug: bindings", true,
                        "Log binding changes while keybinding diagnostics are on.", KeybindingSection),
                    ModConfigEntryDefinition.Toggle(KeybindingDebugEveryFrameKey, "Keybinding debug: every frame", false,
                        "Repeat binding logs every frame while keybinding diagnostics are on. Noisy.", KeybindingSection)
                }
            });

            Current = this;
        }

        internal bool UiDebug => Get(UiDebugKey);

        internal bool UiDebugLifecycle => Get(UiDebugLifecycleKey);

        internal bool UiDebugEveryFrame => Get(UiDebugEveryFrameKey);

        internal bool KeybindingDebug => Get(KeybindingDebugKey);

        internal bool KeybindingDebugRouting => Get(KeybindingDebugRoutingKey);

        internal bool KeybindingDebugBindings => Get(KeybindingDebugBindingsKey);

        internal bool KeybindingDebugEveryFrame => Get(KeybindingDebugEveryFrameKey);

        // 已注册的条目键，供测试与文档对齐。
        internal static IReadOnlyList<string> AllKeys { get; } = new[]
        {
            UiDebugKey, UiDebugLifecycleKey, UiDebugEveryFrameKey,
            KeybindingDebugKey, KeybindingDebugRoutingKey, KeybindingDebugBindingsKey, KeybindingDebugEveryFrameKey
        };

        public void Dispose()
        {
            IModConfigRegistration? current = registration;
            registration = null;
            if (ReferenceEquals(Current, this))
                Current = null;

            try { current?.Dispose(); }
            catch (Exception exception) { warn($"[SMA] failed to release the API's own config page: {exception.Message}"); }
        }

        private bool Get(string key)
        {
            IModConfigRegistration? current = registration;
            if (current == null || current.IsDisposed)
                return false;

            try { return current.GetBool(key); }
            catch (Exception exception)
            {
                warn($"[SMA] API setting '{key}' is unavailable: {exception.Message}");
                return false;
            }
        }
    }
}
