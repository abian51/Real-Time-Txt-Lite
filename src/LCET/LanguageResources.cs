using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LCET
{
    public static class LanguageResources
    {
        public static bool IsLanguageTag(string value)
        {
            return value != null && Regex.IsMatch(value, "\\A[a-z]{2,3}(?:-[a-zA-Z0-9]{2,8}){0,3}\\z", RegexOptions.CultureInvariant);
        }

        public static Dictionary<string, object> Evaluate(string source, string target, IEnumerable<string> installed)
        {
            if (!IsLanguageTag(source) || !IsLanguageTag(target)) throw new ArgumentException("Invalid language tag");
            string[] requested = new[] { source, target }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] missing = installed == null ? new string[0] : requested.Where(code => !installed.Any(tag => Matches(code, tag))).ToArray();
            return new Dictionary<string, object>
            {
                { "sourceLanguage", source }, { "targetLanguage", target },
                { "state", installed == null ? "unknown" : missing.Length == 0 ? "available" : "missing" },
                { "missing", missing }
            };
        }

        private static bool Matches(string code, string tag)
        {
            code = code.ToLowerInvariant();
            tag = tag.ToLowerInvariant();
            if (code == "zh" || code == "zh-hans") return tag == "zh-cn" || tag == "zh-sg";
            if (code == "zh-hant") return tag == "zh-tw" || tag == "zh-hk" || tag == "zh-mo";
            if (code == "yue") return tag == "zh-hk";
            if (code == "pt") return tag == "pt-br";
            return tag == code || tag.StartsWith(code + "-", StringComparison.Ordinal);
        }

        public static Dictionary<string, object> Check(string source, string target)
        {
            if (!IsLanguageTag(source) || !IsLanguageTag(target)) throw new ArgumentException("Invalid language tag");
            try
            {
                Type managerType = Type.GetType("Windows.Management.Deployment.PackageManager, Windows.Management.Deployment, ContentType=WindowsRuntime", true);
                object manager = Activator.CreateInstance(managerType);
                var packages = (IEnumerable)managerType.GetMethod("FindPackagesForUser", new[] { typeof(string) }).Invoke(manager, new object[] { "" });
                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object package in packages)
                {
                    object identity = package.GetType().GetProperty("Id").GetValue(package, null);
                    string name = (string)identity.GetType().GetProperty("Name").GetValue(identity, null);
                    Match match = Regex.Match(name, "^MicrosoftWindows\\.Speech\\.([a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8}){1,3})\\.[0-9]+$", RegexOptions.CultureInvariant);
                    if (!match.Success) continue;
                    object status = package.GetType().GetProperty("Status").GetValue(package, null);
                    if ((bool)status.GetType().GetMethod("VerifyIsOK").Invoke(status, null)) installed.Add(match.Groups[1].Value);
                }
                return Evaluate(source, target, installed);
            }
            catch (Exception exception)
            {
                Diagnostics.Write("language_resources", exception.GetType().Name);
                return Evaluate(source, target, null);
            }
        }
    }
}