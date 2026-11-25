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

            // TrimEndingDirectorySeparator keeps a drive root as "D:\" instead of the drive-relative "D:".
            var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0].Trim().Trim('"')));

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

            // The updater window can stay open after a portable update, so wait for it with no deadline;
            // the deadline only bounds the file retries once it has exited.
            if (waitPid is { } p)
                WaitForProcessExit(p);

            if (!Directory.Exists(installRoot))
                return ExitFailure;

            var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
            var allSwapped = true;
            foreach (var (final, staged) in Pairs)
            {
                var finalPath = Path.Combine(installRoot, final);
                var stagedPath = Path.Combine(installRoot, staged);
                if (!File.Exists(stagedPath))
                    continue;

                TryDeleteWithRetry(finalPath, deadline);
                allSwapped &= TryMoveWithRetry(stagedPath, finalPath, deadline);
            }

            return allSwapped ? ExitSuccess : ExitFailure;
        }
        catch
        {
            return ExitFailure;
        }
    }

    // One handle held for the whole wait: Windows does not reuse a PID while a handle to it is open, so this
    // cannot end up waiting on an unrelated process that got the updater's PID.
    private static void WaitForProcessExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
        catch (InvalidOperationException)
        {
            // Already exited.
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

    private static bool TryMoveWithRetry(string sourcePath, string destPath, DateTime deadlineUtc)
    {
        if (!File.Exists(sourcePath))
            return true;
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
                return true;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }

        return false;
    }
}
