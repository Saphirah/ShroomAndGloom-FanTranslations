using System.ComponentModel;
using System.Diagnostics;

namespace ShroomGerman.Setup;

internal static class SteamLauncher
{
    internal static async Task<bool> StartAsync(string gamePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Steam handles sign-in, ownership and Family Sharing availability itself.
        using var steam = Process.Start(new ProcessStartInfo("steam://rungameid/3271280") { UseShellExecute = true });
        var expectedExe = Path.GetFullPath(Path.Combine(gamePath, "Shroom and Gloom.exe"));
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMinutes(2)) {
            cancellationToken.ThrowIfCancellationRequested();
            var running = await Task.Run(() => IsRunning(expectedExe), cancellationToken);
            if (running) return true;
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }

    private static bool IsRunning(string expectedExe)
    {
        foreach (var process in Process.GetProcessesByName("Shroom and Gloom")) {
            using (process) {
                try {
                    if (!process.HasExited && string.Equals(process.MainModule?.FileName, expectedExe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Win32Exception) { } // Steam may still be starting the process.
                catch (InvalidOperationException) { }
            }
        }
        return false;
    }
}
