using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace musicApp.Updater;

internal static class ApplyUpdateService
{
    private const string MainExe = "musicApp.exe";
    private const string SwapExe = "musicApp-updater-swap.exe";

    /// <summary>True if <paramref name="mainModulePath"/> is <c>musicApp.exe</c> from this install folder.</summary>
    private static bool IsMusicAppMainModuleForInstall(string? mainModulePath, string installRootNormalized)
    {
        if (mainModulePath == null)
            return false;
        var root = installRootNormalized;
        return mainModulePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(mainModulePath, Path.Combine(root, MainExe), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMusicAppRunningFromInstallRoot(string installRoot)
    {
        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExe)))
        {
            try
            {
                if (!IsMusicAppMainModuleForInstall(p.MainModule?.FileName, root))
                    continue;
                p.Kill();
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

        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        KillMusicAppProcesses(root);

        PurgeInstallExceptUpdater(root);

        var stagedNewUpdater = CopyPortableExtractToInstall(extractDir, root);

        if (launchMusicApp)
            TryLaunchMusicAppFromInstallRoot(root);

        if (stagedNewUpdater)
            TryStartUpdaterSwapDetached(root);
    }

    public static void ApplyInstaller(string installRoot, string installerPath, bool launchMusicApp)
    {
        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsMusicAppRunningFromInstallRoot(root))
            return false;
        return TryLaunch(Path.Combine(root, MainExe), root);
    }

    /// <summary>
    /// Removes the main app from the install tree while preserving anything whose name starts with
    /// <c>musicApp-updater</c> (live updater, staged <c>-n</c> payloads, and <c>musicApp-updater-swap</c>).
    /// </summary>
    private static void PurgeInstallExceptUpdater(string installRoot)
    {
        foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("musicApp-updater", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
                throw new IOException($"Could not delete file in install folder: {file}");
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(installRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch
            {
                // ignore
            }
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

        var psi = new ProcessStartInfo
        {
            FileName = swapPath,
            Arguments = $"\"{installRoot}\" --pid {Environment.ProcessId} --max-wait-sec 90",
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
