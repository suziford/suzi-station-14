// TEMPORARY: Remove Content.Client/_Suziford/TempPerfProfiler/ after optimization work is finished.

using Robust.Shared.Console;

namespace Content.Client._Suziford.TempPerfProfiler;

/// <summary>
/// TEMPORARY console command to open/toggle the Suzi Performance Profiler window.
/// </summary>
public sealed class TempPerfProfilerCommand : IConsoleCommand
{
    private static TempPerfProfilerWindow? _window;

    public string Command => "tempprof";
    public string Description => "[TEMP] Opens the temporary performance profiler & BEFORE/AFTER comparison window.";
    public string Help => "Usage: tempprof [clear]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 0 && args[0].Equals("clear", System.StringComparison.OrdinalIgnoreCase))
        {
            TempPerfProfilerStore.Baseline = null;
            TempPerfProfilerStore.Latest = null;
            shell.WriteLine("[TEMP] Saved profiler baseline/latest snapshots cleared.");
            return;
        }

        ToggleWindow();
    }

    internal static void ToggleWindow()
    {
        if (_window != null && _window.IsOpen)
        {
            _window.Close();
            _window = null;
            return;
        }

        _window = new TempPerfProfilerWindow();
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }
}

/// <summary>
/// TEMPORARY alias for <see cref="TempPerfProfilerCommand"/>.
/// </summary>
public sealed class SuziPerfProfilerAliasCommand : IConsoleCommand
{
    public string Command => "suziprof";
    public string Description => "[TEMP] Alias for tempprof.";
    public string Help => "Usage: suziprof [clear]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 0 && args[0].Equals("clear", System.StringComparison.OrdinalIgnoreCase))
        {
            TempPerfProfilerStore.Baseline = null;
            TempPerfProfilerStore.Latest = null;
            shell.WriteLine("[TEMP] Saved profiler baseline/latest snapshots cleared.");
            return;
        }

        TempPerfProfilerCommand.ToggleWindow();
    }
}
