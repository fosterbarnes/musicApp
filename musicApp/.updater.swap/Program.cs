using System.Diagnostics;

namespace musicApp.Updater.Swap;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    private static readonly (string Final, string Staged)[] Pairs =
    [
        ("musicApp-updater.exe", "musicApp-updater-n.exe"),
        ("musicApp-updater.dll", "musicApp-updater-n.dll"),
        ("musicApp-updater.deps.json", "musicApp-updater-n.deps.json"),
        ("musicApp-updater.runtimeconfig.json", "musicApp-updater-n.runtimeconfig.json"),
        ("musicApp-updater.pdb", "musicApp-updater-n.pdb"),
    ];

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                return ExitFailure;
            }

            var installRoot = Path.GetFullPath(args[0].Trim().Trim('"'))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            int? waitPid = null;
            var maxWaitSec = 90;
            for (var i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                    && int.TryParse(args[++i], out var pid))
                    waitPid = pid;
                else if (string.Equals(args[i], "--max-wait-sec", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                         && int.TryParse(args[++i], out var m) && m > 0)
                    maxWaitSec = m;
            }

            if (waitPid is { } p)
                WaitForProcessExit(p, TimeSpan.FromSeconds(maxWaitSec));

            if (!Directory.Exists(installRoot))
                return ExitFailure;

            var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
            foreach (var (final, staged) in Pairs)
            {
                var finalPath = Path.Combine(installRoot, final);
                var stagedPath = Path.Combine(installRoot, staged);
                if (!File.Exists(stagedPath))
                    continue;

                TryDeleteWithRetry(finalPath, deadline);
                TryMoveWithRetry(stagedPath, finalPath, deadline);
            }

            return ExitSuccess;
        }
        catch
        {
            return ExitFailure;
        }
    }

    private static void WaitForProcessExit(int pid, TimeSpan maxWait)
    {
        var deadline = DateTime.UtcNow + maxWait;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            Thread.Sleep(200);
        }
    }

    private static void TryDeleteWithRetry(string path, DateTime deadlineUtc)
    {
        if (!File.Exists(path))
            return;
        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void TryMoveWithRetry(string sourcePath, string destPath, DateTime deadlineUtc)
    {
        if (!File.Exists(sourcePath))
            return;
        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                if (File.Exists(destPath))
                {
                    File.SetAttributes(destPath, FileAttributes.Normal);
                    File.Delete(destPath);
                }

                File.Move(sourcePath, destPath, overwrite: true);
                return;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }
}
