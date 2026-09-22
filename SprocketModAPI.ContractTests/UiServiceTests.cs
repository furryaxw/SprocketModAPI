using System;
using System.IO;
using System.Linq;
using SprocketModAPI;

internal static class UiServiceTests
{
    internal static void Run()
    {
        CheckStatusContracts();
        CheckDebugConfiguration();
        var backend = new FakeUiBackend();
        var scope = new UiScopeCore("test-mod", backend);
        UnityEngine.Transform? parent = null;
        int clicks = 0;

        UiCreateResult<IUiMenuButtonHandle> buttonResult = scope.CreateMenuButtonAsync(new UiMenuButtonDefinition
        {
            Parent = parent,
            Text = "Start",
            OnClick = () => clicks++
        }).GetAwaiter().GetResult();
        Check(buttonResult.Succeeded, "fake menu button creation");
        Check(buttonResult.Value!.Text == "Start" && buttonResult.Value.Enabled, "menu button initial state");
        var fakeButton = (FakeUiBackend.FakeUiHandle)buttonResult.Value;
        fakeButton.Click();
        Check(clicks == 1, "menu button callback");
        buttonResult.Value.Text = "Updated";
        buttonResult.Value.Enabled = false;
        Check(buttonResult.Value.Text == "Updated" && !buttonResult.Value.Enabled, "menu button state update");

        UiCreateResult<IUiMenuButtonHandle> throwingResult = scope.CreateMenuButtonAsync(new UiMenuButtonDefinition
        {
            Parent = parent,
            OnClick = () => throw new InvalidOperationException("expected callback failure")
        }).GetAwaiter().GetResult();
        ((FakeUiBackend.FakeUiHandle)throwingResult.Value!).Click();
        Check(!throwingResult.Value!.IsDisposed, "callback exception is isolated");

        var cancelled = new System.Threading.CancellationToken(canceled: true);
        UiCreateResult<IUiMenuButtonHandle> cancelledResult = scope.CreateMenuButtonAsync(new UiMenuButtonDefinition { Parent = parent }, cancelled).GetAwaiter().GetResult();
        Check(cancelledResult.Failure == UiFailureCode.Cancelled, "cancelled creation is structured");

        UiCreateResult<IUiMenuButtonHandle> menuResult = scope.CreateMenuButtonAsync(new UiMenuButtonDefinition
        {
            Parent = parent,
            Text = "Menu",
            Selected = true
        }).GetAwaiter().GetResult();
        Check(menuResult.Succeeded && menuResult.Value!.Selected, "fake menu button creation and selection");
        menuResult.Value!.Selected = false;
        Check(!menuResult.Value!.Selected, "menu button selection update");

        scope.Dispose();
        scope.Dispose();
        Check(buttonResult.Value.IsDisposed && menuResult.Value.IsDisposed, "scope disposal is idempotent and releases handles");
        UiCreateResult<IUiMenuButtonHandle> disposedResult = scope.CreateMenuButtonAsync(new UiMenuButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        Check(disposedResult.Failure == UiFailureCode.OwnerDisposed, "disposed scope rejects creation");

        var unavailable = new FakeUiBackend(UiCapability.None);
        var unavailableScope = new UiScopeCore("test-mod", unavailable);
        UiCreateResult<IUiMenuButtonHandle> unavailableResult = unavailableScope.CreateMenuButtonAsync(new UiMenuButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        Check(unavailableResult.Failure == UiFailureCode.CapabilityUnavailable, "unavailable capability is structured");
        unavailableScope.Dispose();
        unavailable.Dispose();

        var invalidParent = new FakeUiBackend(requireParent: true);
        var invalidParentScope = new UiScopeCore("test-mod", invalidParent);
        UiCreateResult<IUiMenuButtonHandle> invalidParentResult = invalidParentScope.CreateMenuButtonAsync(new UiMenuButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        Check(invalidParentResult.Failure == UiFailureCode.InvalidParent, "invalid parent is structured");
        invalidParentScope.Dispose();
        invalidParent.Dispose();

        var sceneBackend = new FakeUiBackend();
        var sceneScope = new UiScopeCore("test-mod", sceneBackend);
        UiCreateResult<IUiMenuButtonHandle> sceneResult = sceneScope.CreateMenuButtonAsync(new UiMenuButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        sceneBackend.SceneUnloaded();
        Check(sceneResult.Value!.IsDisposed, "scene unload releases handles");
        sceneScope.Dispose();
    }

    private static void CheckStatusContracts()
    {
        var snapshot = new UiCapabilitySnapshot
        {
            GameVersion = new Version(0, 2, 53, 2), Available = UiCapability.MenuButton,
            SceneName = "MainMenu", IsMainMenuReady = true, MenuGeneration = 7
        };
        Check(snapshot.SceneName == "MainMenu" && snapshot.IsMainMenuReady && snapshot.MenuGeneration == 7, "status snapshot fields");
        Check(typeof(UiStatusChangedEventArgs).GetProperty("Previous") != null && typeof(UiStatusChangedEventArgs).GetProperty("Current") != null, "status event args snapshots");
        Check(!typeof(UiStatusChangedEventArgs).GetProperties().Any(property => property.PropertyType.FullName?.Contains("UnityEngine") == true), "status event hides Unity objects");

        var broadcaster = new UiStatusBroadcaster(_ => { });
        int received = 0;
        broadcaster.StatusChanged += (_, _) => throw new InvalidOperationException("expected");
        broadcaster.StatusChanged += (_, args) => { received++; Check(args.Previous.MenuGeneration == 0 && args.Current.MenuGeneration == 1, "event previous/current"); };
        broadcaster.Publish(broadcaster, new UiStatusChangedEventArgs(new UiCapabilitySnapshot(), new UiCapabilitySnapshot { MenuGeneration = 1 }));
        Check(received == 1, "status subscriber isolation");
        broadcaster.Dispose();
    }

    private static void CheckDebugConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SprocketModAPI-UiDebug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new ModConfigService(directory, _ => { });

            // 配置页还没建起来时：一律不输出，也不抛。
            var output = new System.Collections.Generic.List<string>();
            new UiDebugLog(null, output.Add).Lifecycle("hidden");
            Check(output.Count == 0, "UI trace stays silent while the API's own config page is missing");

            var settings = new ApiSelfSettings(service, _ => { });
            IModConfigRegistration? page = service.Find("furryaxw.sprocket-mod-api");
            Check(page != null && ReferenceEquals(ApiSelfSettings.Current, settings),
                "the API's own config page is registered under its declared id and becomes current");
            Check(page!.Snapshot.Entries.Count == ApiSelfSettings.AllKeys.Count,
                "every declared API setting is registered as an entry");

            var log = new UiDebugLog(settings, output.Add);
            log.Lifecycle("hidden");
            log.EveryFrame("hidden");
            Check(output.Count == 0, "UI trace is off by default");

            page.SetBool(ApiSelfSettings.UiDebugKey, true);
            log.Lifecycle("visible");
            Check(output.Single() == "[SMA-UI-TRACE] visible", "flipping the master switch takes effect with no restart");
            output.Clear();
            log.EveryFrame("frame");
            Check(output.Count == 0, "every-frame logging waits for its own switch");
            page.SetBool(ApiSelfSettings.UiDebugEveryFrameKey, true);
            log.EveryFrame("frame");
            Check(output.Single() == "[SMA-UI-TRACE] frame", "every-frame logging obeys its own switch");

            var keyOutput = new System.Collections.Generic.List<string>();
            var keyLog = new KeybindingDebugLog(settings, keyOutput.Add);
            keyLog.Routing("route");
            keyLog.Binding("binding", force: true);
            Check(keyOutput.Count == 0, "keybinding diagnostics are off by default");

            page.SetBool(ApiSelfSettings.KeybindingDebugKey, true);
            keyLog.Routing("route");
            Check(keyOutput.Single() == "[SMA-KEY-ROUTE] route", "keybinding routing logs once its master switch is on");
            keyOutput.Clear();
            keyLog.Binding("binding", force: true);
            Check(keyOutput.Single() == "[SMA-KEY] binding", "forced binding logs respect the master switch");

            settings.Dispose();
            Check(ApiSelfSettings.Current == null && service.Find("furryaxw.sprocket-mod-api") == null,
                "disposing the API settings releases its config page");
            var after = new System.Collections.Generic.List<string>();
            new UiDebugLog(settings, after.Add).Lifecycle("hidden");
            new KeybindingDebugLog(settings, after.Add).Routing("route");
            Check(after.Count == 0, "a disposed settings handle never throws and never logs");

            service.Dispose();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }


    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"UI contract failed: {name}");
    }
}
