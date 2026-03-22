using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MusicApp.Helpers;

public static class Mp3tagLauncher
{
    public const string DownloadUrl = "https://www.mp3tag.de/en/download.html";

    public static string? TryFindExecutable()
    {
        foreach (var path in EnumerateCandidatePaths())
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        foreach (var baseKey in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in AppPathsFrom(baseKey, false))
                yield return path;
            foreach (var path in AppPathsFrom(baseKey, true))
                yield return path;
        }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            yield return Path.Combine(pf, "Mp3tag", "Mp3tag.exe");

        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pfx86))
            yield return Path.Combine(pfx86, "Mp3tag", "Mp3tag.exe");
    }

    private static IEnumerable<string> AppPathsFrom(RegistryKey baseKey, bool wow64)
    {
        var subPath = wow64
            ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Mp3tag.exe"
            : @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Mp3tag.exe";

        using var key = baseKey.OpenSubKey(subPath);
        var v = key?.GetValue("") as string;
        if (!string.IsNullOrWhiteSpace(v))
            yield return v;
    }

    public static bool TryOpenFile(string audioPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(audioPath))
        {
            error = "No file path.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(audioPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = "File not found.";
            return false;
        }

        var exe = TryFindExecutable();
        if (string.IsNullOrEmpty(exe))
            return false;

        var arg = "/fn:\"" + fullPath.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void OpenDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = DownloadUrl,
            UseShellExecute = true
        });
    }
}
