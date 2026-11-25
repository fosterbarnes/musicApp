using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace musicApp.Updater;

internal static class ApplyUpdateService
{
    private const string MainExe = "musicApp.exe";
    private const string SwapExe = "musicApp-updater-swap.exe";

    // TrimEndingDirectorySeparator keeps a drive root as "D:\" (plain TrimEnd made it the drive-relative "D:").
    private static string NormalizeInstallRoot(string installRoot) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));

    /// <summary>True if <paramref name="mainModulePath"/> is <c>musicApp.exe</c> from this install folder.</summary>
    private static bool IsMusicAppMainModuleForInstall(string? mainModulePath, string installRootNormalized)
    {
        if (mainModulePath == null)
            return false;
        var root = installRootNormalized;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return mainModulePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               || string.Equals(mainModulePath, Path.Combine(root, MainExe), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMusicAppRunningFromInstallRoot(string installRoot)
    {
        var root = NormalizeInstallRoot(installRoot);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExe)))
        {
            try
            {
                if (IsMusicAppMainModuleForInstall(p.MainModule?.FileName, root))
                    return true;
            }
            catch
            {
                // ignore access errors (e.g. MainModule on some processes)
            }
            finally
            {
                p.Dispose();
            }
        }

        return false;
    }

    public static void KillMusicAppProcesses(string installRoot)
    {
        var root = NormalizeInstallRoot(installRoot);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExe)))
        {
            try
            {
                if (!IsMusicAppMainModuleForInstall(p.MainModule?.FileName, root))
                    continue;
                p.Kill();
                // Kill is async; the purge that follows needs the exe and its dlls unlocked.
                p.WaitForExit(10_000);
            }
            catch
            {
                // ignore access errors
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    public static void ApplyPortableZip(string installRoot, string zipPath, string workDir, bool launchMusicApp)
    {
        var extractDir = Path.Combine(workDir, "extracted");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var root = NormalizeInstallRoot(installRoot);
        KillMusicAppProcesses(root);

        // The old app is moved aside, not deleted, until the new one is fully copied. Deleting first left the
        // folder with no app when the copy failed (disk full, file in use).
        var backup = Path.Combine(root, BackupDirName);
        MoveInstallToBackup(root, backup);
        bool stagedNewUpdater;
        try
        {
            stagedNewUpdater = CopyPortableExtractToInstall(extractDir, root);
        }
        catch
        {
            RestoreInstallFromBackup(root, backup);
            throw;
        }
        TryDeleteDirectory(backup);

        if (launchMusicApp)
            TryLaunchMusicAppFromInstallRoot(root);

        if (stagedNewUpdater)
            TryStartUpdaterSwapDetached(root);
    }

    public static void ApplyInstaller(string installRoot, string installerPath, bool launchMusicApp)
    {
        var root = NormalizeInstallRoot(installRoot);
        KillMusicAppProcesses(root);

        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"{root}\"",
            UseShellExecute = false,
        };

        using var p = Process.Start(psi);
        if (p == null)
            throw new IOException("Could not start the installer.");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new IOException($"Installer exited with code {p.ExitCode}.");

        if (launchMusicApp)
            TryLaunchMusicAppFromInstallRoot(root);
    }

    /// <summary>
    /// Starts musicApp from the install folder if it is not already running from that folder (single instance from updater).
    /// </summary>
    public static bool TryLaunchMusicAppFromInstallRoot(string installRoot)
    {
        var root = NormalizeInstallRoot(installRoot);
        if (IsMusicAppRunningFromInstallRoot(root))
            return false;
        return TryLaunch(Path.Combine(root, MainExe), root);
    }

    private const string BackupDirName = ".update-backup";

    /// <summary>
    /// Moves the main app out of the install tree into <paramref name="backup"/> (same relative paths), keeping
    /// anything named <c>musicApp-updater*</c> (live updater, staged <c>-n</c> payloads, swap). If a move fails,
    /// everything already moved goes back and the error is thrown, so the install is never left half-empty.
    /// </summary>
    private static void MoveInstallToBackup(string installRoot, string backup)
    {
        // A leftover backup: if the app is missing, an update was killed mid-way and the backup holds the only
        // copy of the old app, so put it back. If the app is there, the backup is just one that could not be
        // deleted after a good update; restoring it would put old files over the good install.
        if (File.Exists(Path.Combine(installRoot, MainExe)))
            TryDeleteDirectory(backup);
        else
            RestoreInstallFromBackup(installRoot, backup);

        var backupPrefix = backup + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("musicApp-updater", StringComparison.OrdinalIgnoreCase)
                        && !f.StartsWith(backupPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        try
        {
            foreach (var file in files)
            {
                var target = Path.Combine(backup, Path.GetRelativePath(installRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.SetAttributes(file, FileAttributes.Normal);
                File.Move(file, target);
            }
        }
        catch (Exception ex)
        {
            RestoreInstallFromBackup(installRoot, backup);
            throw new IOException($"Could not move the old app aside in the install folder: {ex.Message}", ex);
        }

        DeleteEmptyDirectories(installRoot);
    }

    /// <summary>Moves every backed-up file back over whatever the failed copy wrote.</summary>
    private static void RestoreInstallFromBackup(string installRoot, string backup)
    {
        if (!Directory.Exists(backup))
            return;
        foreach (var file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories).ToList())
        {
            var target = Path.Combine(installRoot, Path.GetRelativePath(backup, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(file, target, overwrite: true);
        }
        TryDeleteDirectory(backup);
    }

    private static void DeleteEmptyDirectories(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch
            {
                // A folder still in use or not empty stays; the new files are copied around it.
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Leftover backup only costs disk space; the next update clears it.
        }
    }

    /// <summary>
    /// Copies extracted release files into the install folder. Payloads named like <c>musicApp-updater.*</c>
    /// are written as <c>musicApp-updater-n.*</c> so the running process does not overwrite its loaded assemblies.
    /// <c>musicApp-updater-swap.exe</c> is copied to its final name (not in use during update).
    /// </summary>
    private static bool CopyPortableExtractToInstall(string sourceDir, string destDir)
    {
        var stagedUpdater = false;

        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dirPath);
            var target = Path.Combine(destDir, rel);
            Directory.CreateDirectory(target);
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, filePath);
            var destRel = MapPortableDestinationRelativePath(rel);
            if (Path.GetFileName(destRel).StartsWith("musicApp-updater-n", StringComparison.OrdinalIgnoreCase))
                stagedUpdater = true;

            var target = Path.Combine(destDir, destRel);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.Copy(filePath, target, overwrite: true);
        }

        return stagedUpdater;
    }

    private static string MapPortableDestinationRelativePath(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        var dir = Path.GetDirectoryName(relativePath);
        var mappedName = MapUpdaterPayloadToStagedFileName(fileName);
        return string.IsNullOrEmpty(dir) ? mappedName : Path.Combine(dir, mappedName);
    }

    private static string MapUpdaterPayloadToStagedFileName(string fileName)
    {
        if (fileName.StartsWith("musicApp-updater-swap", StringComparison.OrdinalIgnoreCase))
            return fileName;
        if (fileName.StartsWith("musicApp-updater-n.", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("musicApp-updater-n.exe", StringComparison.OrdinalIgnoreCase))
            return fileName;
        if (fileName.Equals("musicApp-updater.exe", StringComparison.OrdinalIgnoreCase))
            return "musicApp-updater-n.exe";
        if (fileName.StartsWith("musicApp-updater.", StringComparison.OrdinalIgnoreCase))
            return "musicApp-updater-n." + fileName["musicApp-updater.".Length..];
        return fileName;
    }

    private static void TryStartUpdaterSwapDetached(string installRoot)
    {
        var swapPath = Path.Combine(installRoot, SwapExe);
        if (!File.Exists(swapPath))
            throw new IOException($"New updater files were staged but {SwapExe} was not found in the install folder.");

        // A drive root ends in '\', and "D:\" would read as an escaped quote that swallows the other arguments.
        var rootArg = Path.EndsInDirectorySeparator(installRoot) ? installRoot + "." : installRoot;
        var psi = new ProcessStartInfo
        {
            FileName = swapPath,
            Arguments = $"\"{rootArg}\" --pid {Environment.ProcessId} --max-wait-sec 90",
            WorkingDirectory = installRoot,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (Process.Start(psi) is null)
            throw new IOException($"Could not start {SwapExe}.");
    }

    private static bool TryLaunch(string exePath, string workingDirectory)
    {
        try
        {
            if (!File.Exists(exePath))
                return false;
            if (Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                }) is null)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
