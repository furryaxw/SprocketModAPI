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
        string root = Path.Combine(Path.GetTempPath(), "SprocketModAPI-UiDebug-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "ui.debug.json");
        try
        {
            var missing = UiDebugSettings.Load(_ => { }, path);
            Check(File.Exists(path) && !missing.Enabled && missing.LogLifecycle && !missing.LogEveryFrame, "missing debug config defaults");
            File.WriteAllText(path, "{not-json");
            int warnings = 0;
            var corrupt = UiDebugSettings.Load(_ => warnings++, path);
            Check(!corrupt.Enabled && warnings == 1, "corrupt debug config fallback warning");
            File.WriteAllText(path, "{\"Enabled\":true,\"LogLifecycle\":true,\"LogEveryFrame\":false}");
            var enabled = UiDebugSettings.Load(_ => { }, path);
            var output = new System.Collections.Generic.List<string>();
            new UiDebugLog(corrupt, output.Add).Lifecycle("hidden");
            Check(output.Count == 0, "disabled UI trace");
            new UiDebugLog(enabled, output.Add).Lifecycle("visible");
            Check(output.Single() == "[SMA-UI-TRACE] visible", "enabled UI trace prefix");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }


    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"UI contract failed: {name}");
    }
}
