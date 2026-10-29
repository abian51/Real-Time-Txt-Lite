using System;
using System.IO;
using System.Web.Script.Serialization;

namespace LCET
{
    public sealed class Settings
    {
        public string SourceLanguage { get; set; } = "en";
        private string targetLanguage;
        public string TargetLanguage
        {
            get { return targetLanguage ?? (SourceLanguage == "en" ? "zh" : "en"); }
            set { targetLanguage = value; }
        }
        public bool LanguageConfirmed { get; set; } = true;
        public int LanguageSetupVersion { get; set; }
        public int DebounceMs { get; set; } = 700;
        public int PollMs { get; set; } = 250;
        public double FontSize { get; set; } = 22;
        public int TranslationLines { get; set; } = 4;
        public string TranslationColor { get; set; } = "Black";
        public double Opacity { get; set; } = .5;
        public int OverlayLayoutVersion { get; set; }
        public bool FollowWindowsCaptions { get; set; } = true;
        public double Width { get; set; } = 900;
        public double Height { get; set; } = 124;
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;

        public static string UserDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LCET"); }
        }

        public static Settings Load(string root)
        {
            string path = Path.Combine(UserDirectory, "settings.json");
            if (!File.Exists(path)) path = Path.Combine(root, "config", "defaults.json");
            return LoadFile(path);
        }

        private static Settings LoadFile(string path)
        {
            var serializer = new JavaScriptSerializer();
            try
            {
                var settings = serializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
                if (settings.OverlayLayoutVersion < 1)
                {
                    settings.Opacity = .5;
                    if (settings.Height == 540) settings.Height = 300;
                    settings.OverlayLayoutVersion = 1;
                }
                if (settings.OverlayLayoutVersion < 2)
                {
                    if (settings.Height == 300) settings.Height = 240;
                    settings.OverlayLayoutVersion = 2;
                }
                if (settings.OverlayLayoutVersion < 3)
                {
                    if (settings.Height == 240) settings.Height = 160;
                    settings.OverlayLayoutVersion = 3;
                }
                if (settings.OverlayLayoutVersion < 4)
                {
                    if (settings.Height == 160) settings.Height = 124;
                    settings.OverlayLayoutVersion = 4;
                }
                if (Array.IndexOf(new[] { "Black", "White", "Green", "Blue", "Red" }, settings.TranslationColor) < 0)
                    settings.TranslationColor = "Black";
                if (!LanguageResources.IsLanguageTag(settings.SourceLanguage)) settings.SourceLanguage = "en";
                if (!LanguageResources.IsLanguageTag(settings.TargetLanguage) || settings.TargetLanguage == settings.SourceLanguage)
                    settings.TargetLanguage = settings.SourceLanguage == "en" ? "zh" : "en";
                if (settings.LanguageSetupVersion < 1)
                {
                    if (settings.SourceLanguage == "en") settings.LanguageConfirmed = true;
                    settings.LanguageSetupVersion = 1;
                }
                settings.DebounceMs = Math.Max(300, Math.Min(1000, settings.DebounceMs));
                settings.PollMs = Math.Max(200, Math.Min(1000, settings.PollMs));
                settings.FontSize = Math.Max(14, Math.Min(40, settings.FontSize));
                settings.TranslationLines = Math.Max(2, Math.Min(6, settings.TranslationLines));
                settings.Opacity = Math.Max(0, Math.Min(1, settings.Opacity));
                settings.Width = Math.Max(420, Math.Min(1800, settings.Width));
                settings.Height = Math.Max(96, Math.Min(1200, settings.Height));
                return settings;
            }
            catch { return new Settings { LanguageSetupVersion = 1 }; }
        }

        public void Save()
        {
            Directory.CreateDirectory(UserDirectory);
            File.WriteAllText(Path.Combine(UserDirectory, "settings.json"), new JavaScriptSerializer().Serialize(this));
        }
    }

    public static class Diagnostics
    {
        private static readonly object gate = new object();
        public static void Write(string module, string code, double duration = 0)
        {
            lock (gate)
            {
                try
                {
                    string folder = Path.Combine(Settings.UserDirectory, "logs");
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, "technical.log");
                    if (File.Exists(file) && new FileInfo(file).Length > 262144)
                    {
                        string old = file + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    File.AppendAllText(file, DateTime.UtcNow.ToString("o") + " " + module + " " + code + " " + duration.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "ms" + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}