using System;
using System.Windows;
using musicApp.Helpers;

namespace musicApp
{
    public partial class App : Application
    {
        private static bool _pendingOpenSettings;
        private static bool _pendingOpenInfo;
        private static string? _pendingSettingsSection;
        private static string? _pendingInfoSection;

        internal static LaunchParseResult TakeLaunchPending()
        {
            var r = new LaunchParseResult
            {
                OpenSettings = _pendingOpenSettings,
                OpenInfo = _pendingOpenInfo,
                SettingsSection = _pendingSettingsSection,
                InfoSection = _pendingInfoSection,
            };
            _pendingOpenSettings = false;
            _pendingOpenInfo = false;
            _pendingSettingsSection = null;
            _pendingInfoSection = null;
            return r;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var parsed = LaunchArgsParser.Parse(e.Args);
            _pendingOpenSettings = parsed.OpenSettings;
            _pendingOpenInfo = parsed.OpenInfo;
            _pendingSettingsSection = parsed.SettingsSection;
            _pendingInfoSection = parsed.InfoSection;
            base.OnStartup(e);
        }
    }
}
