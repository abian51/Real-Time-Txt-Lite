using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LCET
{
    internal static class ExecutableEntry
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                if (!Environment.Is64BitProcess) throw new InvalidOperationException("Windows x64 is required.");
                using (var windows = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;
                    if (windows == null || !int.TryParse(Convert.ToString(windows.GetValue("CurrentBuildNumber")), out build) || build < 22000)
                        throw new InvalidOperationException("Windows 11 or later is required.");
                }
                using (var framework = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (framework == null || Convert.ToInt32(framework.GetValue("Release", 0)) < 533320)
                        throw new InvalidOperationException(".NET Framework 4.8.1 Runtime is required.");
                }
                string root = AppDomain.CurrentDomain.BaseDirectory;
                var directory = new DirectoryInfo(root);
                if (directory.Name.Equals("bin", StringComparison.OrdinalIgnoreCase)) root = directory.Parent.FullName;
                foreach (string relative in new[] { @"web\index.html", @"web\app.js", @"web\app.css", @"web\languages.json", @"config\defaults.json" })
                    if (!File.Exists(Path.Combine(root, relative)))
                        throw new FileNotFoundException("Required file missing: " + relative + ". Extract the entire RTTL folder before starting.");
                Entry.Run(root, FindEdge());
                return 0;
            }
            catch (Exception exception)
            {
                Diagnostics.Write("startup", exception.GetType().Name);
                MessageBox.Show("RTTL startup failed: " + exception.Message +
                    "\nNo policy changes were made. If blocked by policy, ask IT to authorize this application.",
                    "real-time-txt-lite (RTTL)", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static string FindEdge()
        {
            var candidates = new System.Collections.Generic.List<string>();
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                candidates.Add(Path.Combine(folder, @"Microsoft\Edge\Application\msedge.exe"));
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                using (var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
                    if (key != null) candidates.Add(Convert.ToString(key.GetValue("")));
            foreach (string candidate in candidates)
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate) &&
                    FileVersionInfo.GetVersionInfo(candidate).FileMajorPart >= 148) return candidate;
            throw new FileNotFoundException("Microsoft Edge 148 or newer is required.");
        }
    }
}