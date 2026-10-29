using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BmsConfig.Core
{
    public sealed class BmsInstall
    {
        public string Name { get; private set; }
        public string BaseDir { get; private set; }
        public string ConfigFolder { get { return Path.Combine(BaseDir, "User", "Config"); } }

        public BmsInstall(string name, string baseDir)
        {
            Name = name;
            BaseDir = baseDir;
        }

        public override string ToString() { return Name + "  -  " + BaseDir; }
    }

    /// <summary>
    /// Finds the side-by-side BMS installs, listed under HKLM\SOFTWARE\WOW6432Node\Benchmark Sims.
    /// </summary>
    public static class BmsInstalls
    {
        public static List<BmsInstall> Discover()
        {
            List<BmsInstall> installs = new List<BmsInstall>();
            try
            {
                // The 32-bit registry view maps to WOW6432Node from a 64-bit process, and to the
                // native hive from a 32-bit process -- so this works regardless of process bitness.
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey root = hklm.OpenSubKey(@"SOFTWARE\Benchmark Sims"))
                {
                    if (root != null)
                    {
                        foreach (string subKeyName in root.GetSubKeyNames())
                        {
                            using (RegistryKey sub = root.OpenSubKey(subKeyName))
                            {
                                string baseDir = (sub != null) ? sub.GetValue("baseDir") as string : null;
                                if (!string.IsNullOrWhiteSpace(baseDir))
                                    installs.Add(new BmsInstall(subKeyName, baseDir.Trim()));
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // No registry (or no access) -- the user can still browse to a folder.
            }

            installs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return installs;
        }

        /// <summary>The install whose baseDir contains the given exe directory, if any.</summary>
        public static BmsInstall FindContaining(IEnumerable<BmsInstall> installs, string exeDir)
        {
            string dir = NormalizeDir(exeDir);
            if (dir == null)
                return null;

            BmsInstall best = null;
            foreach (BmsInstall install in installs)
            {
                string baseDir = NormalizeDir(install.BaseDir);
                if (baseDir == null)
                    continue;

                if (string.Equals(dir, baseDir, StringComparison.OrdinalIgnoreCase)
                    || dir.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || baseDir.Length > NormalizeDir(best.BaseDir).Length)
                        best = install;
                }
            }
            return best;
        }

        static string NormalizeDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return null;
            try
            {
                return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
