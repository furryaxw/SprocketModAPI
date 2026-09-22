using System;
using System.IO;
using Il2CppInterop.Runtime.Injection;
using MelonLoader.Utils;

namespace SprocketModAPI
{
    internal sealed class ModMenuService : IModMenuService, IDisposable
    {
        private readonly ModMenuWindow window;

        internal ModMenuService(ModMenuWindow window)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
        }

        public bool IsOpen => window.IsVisible;

        public event Action<bool>? VisibilityChanged;

        public void Open() => window.Open();
        public void Close() => window.Close();
        public void Toggle() => window.Toggle();

        internal void NotifyVisibility(bool visible) => VisibilityChanged?.Invoke(visible);

        public void Dispose() => VisibilityChanged = null;
    }

    // Mod 菜单模块：注册 `IModMenuService`，入口是「设置 → General 左下角」的原生观感按钮。
    // 键位动作 `mod-menu` **默认不绑键**（全空 ⇒ 玩家想用快捷键就在键位窗口里自己绑）；
    // 打开期间通过 `AcquireInputBlock` 阻断其他 API 动作。
    internal sealed class ModMenuModule : IRuntimeModule
    {
        // 设置页观察者用它回到当前入口实例（与键位模块的 `Controller` 同一套路）。
        internal static ModMenuSettingsEntry? Entry { get; private set; }

        private Action<string>? warn;
        private Action<string>? info;
        private ModMenuWindow? window;
        private ModMenuService? service;
        private ModMenuSettingsEntry? entry;
        private IDisposable? serviceRegistration;
        private IInputActionHandle? toggleAction;

        public void Initialize(RuntimeModuleContext context)
        {
            warn = context.Warn;
            info = context.Info;
            RegisterIl2CppTypes(context.Error);

            if (!context.Services.TryGet(out IModMetadataService? metadata) || metadata == null)
            {
                context.Error("[SMA-MENU] metadata service is unavailable; the mod menu was not started.");
                return;
            }

            if (!context.Services.TryGet(out IModConfigService? config) || config == null)
            {
                context.Error("[SMA-MENU] config service is unavailable; the mod menu was not started.");
                return;
            }

            if (!context.Services.TryGet(out IInputService? input) || input == null)
            {
                context.Error("[SMA-MENU] input service is unavailable; the mod menu was not started.");
                return;
            }

            string modsDirectory = Path.GetDirectoryName(typeof(ModMenuModule).Assembly.Location) ?? "";
            string gameRoot = string.IsNullOrEmpty(modsDirectory) ? "" : Directory.GetParent(modsDirectory)?.FullName ?? "";
            string pluginsDirectory = string.IsNullOrEmpty(gameRoot) ? "" : Path.Combine(gameRoot, "Plugins");
            string userLibsDirectory = string.IsNullOrEmpty(gameRoot) ? "" : Path.Combine(gameRoot, "UserLibs");
            ModMenuService? pending = null;
            window = new ModMenuWindow(metadata, config, input, modsDirectory, pluginsDirectory, userLibsDirectory,
                context.Warn, context.Error, visible =>
                {
                    pending?.NotifyVisibility(visible);
                    Entry?.RefreshVisibility();
                }, context.Info);
            pending = new ModMenuService(window);
            service = pending;
            serviceRegistration = context.Services.Register<IModMenuService>(service);

            entry = new ModMenuSettingsEntry(window, context.Warn, context.Info);
            Entry = entry;

            var definition = new ModActionDefinition
            {
                ActionId = "mod-menu",
                DisplayName = "Mod menu",
                Category = "Sprocket Mod API",
                Description = "Open the in-game mod menu",
                Contexts = InputContextMask.Gameplay | InputContextMask.Designer | InputContextMask.MainMenu | InputContextMask.PauseMenu
            };
            toggleAction = input.RegisterAction(definition);
            toggleAction.Pressed += OnTogglePressed;
            // 注册后 ModId 才推断出来：窗口用它把开关动作从自己的输入锁里豁免掉。
            window.SetToggleActionId(definition.StableId);
        }

        public void Update()
        {
            window?.Update();
            entry?.Update();
        }

        public void SceneLoaded(int buildIndex, string sceneName)
        {
            entry?.SceneLoaded(sceneName);
        }

        public void SceneUnloaded(int buildIndex, string sceneName)
        {
            entry?.SceneUnloaded(sceneName);
        }

        public void Dispose()
        {
            if (toggleAction != null)
            {
                try
                {
                    toggleAction.Pressed -= OnTogglePressed;
                    toggleAction.Dispose();
                }
                catch (Exception exception)
                {
                    warn?.Invoke($"[SMA-MENU] failed to release the mod menu keybind: {exception.Message}");
                }

                toggleAction = null;
            }

            serviceRegistration?.Dispose();
            serviceRegistration = null;

            Entry = null;
            entry?.Dispose();
            entry = null;

            service?.Dispose();
            service = null;
            window?.Dispose();
            window = null;
        }

        private void OnTogglePressed() => window?.Toggle();

        // 观察者组件必须先注册进 Il2Cpp 域，否则 `GetComponent<ModMenuSettingsWatcher>()` 会抛
        // TypeInitializationException；而失败的泛型类型初始化器会被永久缓存，重试多少次都好不了。
        private static void RegisterIl2CppTypes(Action<string> error)
        {
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModMenuSettingsWatcher>();
                ClassInjector.RegisterTypeInIl2Cpp<ModMenuActionButtonsWatcher>();
            }
            catch (Exception exception)
            {
                error($"[SMA-MENU] settings entry observer registration failed: {exception}");
            }
        }
    }
}
