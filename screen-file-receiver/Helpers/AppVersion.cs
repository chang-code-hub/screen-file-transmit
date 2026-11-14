using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace screen_file_transmit
{
    /// <summary>
    /// Version information for UI display and `sfr -v` / `sfs -v` CLI output.
    /// The version is written into Properties/AssemblyInfo.cs at publish time by
    /// npm/publish.js (these projects set GenerateAssemblyInfo=false, so the SDK
    /// cannot generate the version attributes itself).
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>Full version string, e.g. "2.2.9".</summary>
        public static string Info
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    // Strip any "+metadata" build-info suffix (NuGet SemVer 2.0 style)
                    var plusIndex = info.IndexOf('+');
                    return plusIndex >= 0 ? info.Substring(0, plusIndex) : info;
                }

                var fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                return string.IsNullOrWhiteSpace(fileVersion) ? "0.0.0" : fileVersion;
            }
        }
    }

    /// <summary>
    /// Handles the `-v` / `--version` command-line flag. When detected, prints
    /// the version to the attached console (if any) and shuts down the
    /// application before the GUI starts.
    /// </summary>
    internal static class VersionFlag
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        private const int AttachParentProcess = -1;

        /// <summary>
        /// Returns true if the version flag was handled (and the app should exit).
        /// </summary>
        public static bool Handle(string[] args)
        {
            if (args == null || args.Length == 0)
                return false;

            var flag = args.FirstOrDefault(a =>
                string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase));

            if (flag == null)
                return false;

            // When launched via npm's bin shim (a .cmd wrapper), stdout may not be
            // attached to a console yet. Attach to the parent console so the
            // version string is actually printed for the user.
            AttachConsole(AttachParentProcess);

            Console.WriteLine(AppVersion.Info);

            // WPF auto-generates Main() from App.xaml (StartupUri), so this code runs
            // from Application.OnStartup. Environment.Exit is used instead of
            // Application.Shutdown() to skip the StartupUri window-creation step that
            // follows OnStartup and keep the console output clean.
            Environment.Exit(0);
            return true; // unreachable, but keeps intent clear for callers
        }
    }
}
