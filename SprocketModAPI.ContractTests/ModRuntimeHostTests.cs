using System;
using System.Collections.Generic;
using SprocketModAPI;

internal static class ModRuntimeHostTests
{
    internal static void Run()
    {
        CheckRegistrationOrderAndDisposal();
        CheckFailingCallbackIsIsolated();
        CheckSceneChangeArguments();
        CheckLateUpdatesAreSeparate();
        CheckShutdownOrder();
        CheckDisposedHostStopsDelivery();
    }

    // LateUpdate 登记与 Update 登记互相独立。
    private static void CheckLateUpdatesAreSeparate()
    {
        var observed = new List<string>();
        var host = new ModRuntimeHost(_ => { });
        host.AddUpdate(() => observed.Add("update"));
        host.AddLateUpdate(() => observed.Add("late"));

        host.Tick();
        host.LateTick();

        Check(observed.Count == 2 && observed[0] == "update" && observed[1] == "late",
            "late updates are delivered in their own phase");
    }

    // 退出回调按登记顺序执行一次。
    private static void CheckShutdownOrder()
    {
        var observed = new List<string>();
        var host = new ModRuntimeHost(_ => { });
        host.AddShutdown(() => observed.Add("first"));
        host.AddShutdown(() => observed.Add("second"));

        host.Shutdown();

        Check(observed.Count == 2 && observed[0] == "first" && observed[1] == "second",
            "shutdown callbacks run in registration order");
    }

    // 登记顺序就是回调顺序，注销之后不再回调。
    private static void CheckRegistrationOrderAndDisposal()
    {
        var observed = new List<string>();
        var host = new ModRuntimeHost(_ => { });

        IDisposable first = host.AddUpdate(() => observed.Add("first"));
        host.AddUpdate(() => observed.Add("second"));

        host.Tick();
        Check(observed.Count == 2 && observed[0] == "first" && observed[1] == "second",
            "updates run in registration order");

        first.Dispose();
        observed.Clear();
        host.Tick();
        Check(observed.Count == 1 && observed[0] == "second",
            "a disposed registration stops receiving updates");
    }

    // 抛异常的登记被停用并只报一次，其余登记继续收到回调。
    private static void CheckFailingCallbackIsIsolated()
    {
        var observed = new List<string>();
        var errors = new List<string>();
        var host = new ModRuntimeHost(errors.Add);

        host.AddUpdate(() => throw new InvalidOperationException("boom"));
        host.AddUpdate(() => observed.Add("after"));

        host.Tick();
        host.Tick();

        Check(errors.Count == 1, "a failing callback is reported once");
        Check(observed.Count == 2, "the remaining registrations still receive every tick");
    }

    // 场景回调拿到 (新场景, 上一个场景)。
    private static void CheckSceneChangeArguments()
    {
        var observed = new List<string>();
        var host = new ModRuntimeHost(_ => { });
        host.AddSceneChanged((current, previous) => observed.Add(current + "<" + previous));

        host.SceneChanged("Battle", "MainMenu");

        Check(observed.Count == 1 && observed[0] == "Battle<MainMenu",
            "scene change reports the new scene and the previous one");
    }

    // 宿主停用后不再分发，也不再接受新登记。
    private static void CheckDisposedHostStopsDelivery()
    {
        var observed = new List<string>();
        var host = new ModRuntimeHost(_ => { });
        host.AddUpdate(() => observed.Add("tick"));
        host.Dispose();

        host.Tick();

        bool rejected = false;
        try { host.AddUpdate(() => observed.Add("late")); }
        catch (ObjectDisposedException) { rejected = true; }

        Check(observed.Count == 0, "a disposed host delivers nothing");
        Check(rejected, "a disposed host rejects new registrations");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("ModRuntimeHost contract violated: " + message);
    }
}
