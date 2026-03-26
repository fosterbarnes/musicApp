using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace musicApp.Updater;

internal static class UpdaterPreferences
{
    private static string PreferencesPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "musicApp", "preferences.json");

    public static bool ReadCheckForUpdates()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return false;
            var json = File.ReadAllText(PreferencesPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("general", out var g))
                return false;
            return g.TryGetProperty("checkForUpdates", out var c) && c.ValueKind == JsonValueKind.True;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public static bool ReadAutomaticallyInstallUpdates()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return false;
            var json = File.ReadAllText(PreferencesPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("general", out var g)
                && g.TryGetProperty("automaticallyInstallUpdates", out var b)
                && b.ValueKind == JsonValueKind.True)
                return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public static bool ReadLaunchAppAfterUpdate()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return false;
            var json = File.ReadAllText(PreferencesPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("general", out var g)
                && g.TryGetProperty("launchAppAfterUpdate", out var b))
                return b.ValueKind == JsonValueKind.True;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public static void WriteCheckForUpdates(bool value)
    {
        try
        {
            JsonObject root;
            if (File.Exists(PreferencesPath))
            {
                var text = File.ReadAllText(PreferencesPath);
                root = JsonNode.Parse(text)?.AsObject() ?? [];
            }
            else
            {
                root = [];
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            }

            root["general"] ??= new JsonObject();
            if (root["general"] is not JsonObject gen)
            {
                gen = [];
                root["general"] = gen;
            }

            gen["checkForUpdates"] = value;

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(PreferencesPath, root.ToJsonString(opts));
        }
        catch
        {
            // ignore
        }
    }

    public static void WriteAutomaticallyInstallUpdates(bool value)
    {
        try
        {
            JsonObject root;
            if (File.Exists(PreferencesPath))
            {
                var text = File.ReadAllText(PreferencesPath);
                root = JsonNode.Parse(text)?.AsObject() ?? [];
            }
            else
            {
                root = [];
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            }

            root["general"] ??= new JsonObject();
            if (root["general"] is not JsonObject gen)
            {
                gen = [];
                root["general"] = gen;
            }

            gen["automaticallyInstallUpdates"] = value;

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(PreferencesPath, root.ToJsonString(opts));
        }
        catch
        {
            // ignore
        }
    }

    public static void WriteLaunchAppAfterUpdate(bool value)
    {
        try
        {
            JsonObject root;
            if (File.Exists(PreferencesPath))
            {
                var text = File.ReadAllText(PreferencesPath);
                root = JsonNode.Parse(text)?.AsObject() ?? [];
            }
            else
            {
                root = [];
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            }

            root["general"] ??= new JsonObject();
            if (root["general"] is not JsonObject gen)
            {
                gen = [];
                root["general"] = gen;
            }

            gen["launchAppAfterUpdate"] = value;

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(PreferencesPath, root.ToJsonString(opts));
        }
        catch
        {
            // ignore
        }
    }
}
