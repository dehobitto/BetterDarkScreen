namespace BetterDarkScreen;

internal static class Program
{
    private const string MutexName = @"Local\BetterDarkScreen.SingleInstance";
    private const string ShowEventName = @"Local\BetterDarkScreen.Show";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask it to show its panel.
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
            return;
        }

        // Always restore normal gamma if we crash.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { try { GammaRamp.ResetAll(); } catch { } };
        Application.ThreadException += (_, e) => { try { GammaRamp.ResetAll(); } catch { } };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var app = new TrayApp(args.Contains("--settings", StringComparer.OrdinalIgnoreCase));

        var sync = SynchronizationContext.Current!;
        var reg = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => sync.Post(_ => app.Activate(), null), null, Timeout.Infinite, false);

        Application.ApplicationExit += (_, _) => { try { GammaRamp.ResetAll(); } catch { } };
        Application.Run(app);
        reg.Unregister(null);
    }
}
