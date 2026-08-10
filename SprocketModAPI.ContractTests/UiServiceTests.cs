using System;
using SprocketModAPI;

internal static class UiServiceTests
{
    internal static void Run()
    {
        var backend = new FakeUiBackend();
        var scope = new UiScopeCore("test-mod", backend);
        UnityEngine.Transform? parent = null;
        int clicks = 0;

        UiCreateResult<IUiButtonHandle> buttonResult = scope.CreateButtonAsync(new UiButtonDefinition
        {
            Parent = parent,
            Text = "Start",
            OnClick = () => clicks++
        }).GetAwaiter().GetResult();
        Check(buttonResult.Succeeded, "fake button creation");
        Check(buttonResult.Value!.Text == "Start" && buttonResult.Value.Enabled, "button initial state");
        var fakeButton = (FakeUiBackend.FakeUiHandle)buttonResult.Value;
        fakeButton.Click();
        Check(clicks == 1, "button callback");
        buttonResult.Value.Text = "Updated";
        buttonResult.Value.Enabled = false;
        Check(buttonResult.Value.Text == "Updated" && !buttonResult.Value.Enabled, "button state update");

        UiCreateResult<IUiButtonHandle> throwingResult = scope.CreateButtonAsync(new UiButtonDefinition
        {
            Parent = parent,
            OnClick = () => throw new InvalidOperationException("expected callback failure")
        }).GetAwaiter().GetResult();
        ((FakeUiBackend.FakeUiHandle)throwingResult.Value!).Click();
        Check(!throwingResult.Value!.IsDisposed, "callback exception is isolated");

        var cancelled = new System.Threading.CancellationToken(canceled: true);
        UiCreateResult<IUiButtonHandle> cancelledResult = scope.CreateButtonAsync(new UiButtonDefinition { Parent = parent }, cancelled).GetAwaiter().GetResult();
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
        UiCreateResult<IUiButtonHandle> disposedResult = scope.CreateButtonAsync(new UiButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        Check(disposedResult.Failure == UiFailureCode.OwnerDisposed, "disposed scope rejects creation");

        var unavailable = new FakeUiBackend(UiCapability.MenuButton);
        var unavailableScope = new UiScopeCore("test-mod", unavailable);
        UiCreateResult<IUiButtonHandle> unavailableResult = unavailableScope.CreateButtonAsync(new UiButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        Check(unavailableResult.Failure == UiFailureCode.CapabilityUnavailable, "unavailable capability is structured");
        unavailableScope.Dispose();
        unavailable.Dispose();

        var invalidParent = new FakeUiBackend(requireParent: true);
        var invalidParentScope = new UiScopeCore("test-mod", invalidParent);
        UiCreateResult<IUiButtonHandle> invalidParentResult = invalidParentScope.CreateButtonAsync(new UiButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        Check(invalidParentResult.Failure == UiFailureCode.InvalidParent, "invalid parent is structured");
        invalidParentScope.Dispose();
        invalidParent.Dispose();

        var sceneBackend = new FakeUiBackend();
        var sceneScope = new UiScopeCore("test-mod", sceneBackend);
        UiCreateResult<IUiButtonHandle> sceneResult = sceneScope.CreateButtonAsync(new UiButtonDefinition { Parent = parent }).GetAwaiter().GetResult();
        sceneBackend.SceneUnloaded();
        Check(sceneResult.Value!.IsDisposed, "scene unload releases handles");
        sceneScope.Dispose();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"UI contract failed: {name}");
    }
}
