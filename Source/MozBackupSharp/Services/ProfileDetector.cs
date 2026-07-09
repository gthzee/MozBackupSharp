using System;
using System.Collections.Generic;
using System.IO;
using MozBackupSharp.Core;

namespace MozBackupSharp.Services
{
    public sealed class ProfileDetector
    {
        private const string ExternalLocationsFileName = "ProfileLocations.ini";

        private static readonly ProductSpec[] BuiltInProducts = new ProductSpec[]
        {
            // Mozilla family products.
            new ProductSpec(ApplicationKind.Firefox, "Mozilla Firefox", true, true, "Mozilla\\Firefox"),
            new ProductSpec(ApplicationKind.Thunderbird, "Mozilla Thunderbird", true, true, "Thunderbird"),
            new ProductSpec(ApplicationKind.SeaMonkey, "SeaMonkey", true, true, "Mozilla\\SeaMonkey"),

            // Current/common Firefox forks. Most Firefox-family browsers keep a
            // Firefox-compatible profiles.ini layout under AppData\Roaming.
            new ProductSpec(ApplicationKind.FirefoxFork, "Waterfox", true, true, "Waterfox"),
            new ProductSpec(ApplicationKind.FirefoxFork, "LibreWolf", true, true, "LibreWolf", "librewolf"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Floorp", true, true, "Floorp"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Zen Browser", true, true, "Zen", "zen"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Pale Moon", true, true, "Moonchild Productions\\Pale Moon"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Basilisk", true, true, "Moonchild Productions\\Basilisk"),
            new ProductSpec(ApplicationKind.FirefoxFork, "K-Meleon", true, true, "K-Meleon"),
            new ProductSpec(ApplicationKind.FirefoxFork, "GNU IceCat", true, true, "GNU\\IceCat", "IceCat", "icecat"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Comodo IceDragon", true, true, "Comodo\\IceDragon"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Cyberfox", true, true, "8pecxstudios\\Cyberfox"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Mercury", true, true, "Mercury"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Mypal", true, true, "Mypal"),
            new ProductSpec(ApplicationKind.FirefoxFork, "Flock", true, true, "Flock\\Browser", "Flock"),

            // Thunderbird-family fork support is useful because the backup engine
            // also handles mail/address-book profile data.
            new ProductSpec(ApplicationKind.ThunderbirdFork, "Betterbird", true, true, "Betterbird")
        };

        public IList<DetectedApplication> DetectInstalledApplications()
        {
            var apps = new List<DetectedApplication>();
            var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IList<ProductSpec> products = GetProductSpecs();

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddKnownProducts(apps, seenRoots, products, roaming, false);

            // A few forks and repackages place profiles.ini in LocalAppData. For
            // Firefox itself LocalAppData normally stores cache, so local entries
            // are accepted only when a real profiles.ini exists.
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AddKnownProducts(apps, seenRoots, products, local, true);

            return apps;
        }

        public DetectedApplication CreateCustomApplication(string profileFolder)
        {
            var app = new DetectedApplication
            {
                Kind = ApplicationKind.Custom,
                Name = "Custom profile folder",
                RootDirectory = profileFolder,
                ProfilesIniPath = string.Empty
            };
            app.Profiles.Add(new MozillaProfile
            {
                ApplicationKind = ApplicationKind.Custom,
                Name = new DirectoryInfo(profileFolder).Name,
                Path = profileFolder,
                RootDirectory = profileFolder,
                IsRelative = false,
                IsDefault = true
            });
            return app;
        }

        public DetectedApplication CreateApplicationFromProfilesIniFolder(string profilesIniFolder, string displayName)
        {
            if (string.IsNullOrWhiteSpace(profilesIniFolder))
                throw new ArgumentException("A profiles.ini folder must be selected.", "profilesIniFolder");

            string root = profilesIniFolder;
            if (File.Exists(root) && string.Equals(Path.GetFileName(root), "profiles.ini", StringComparison.OrdinalIgnoreCase))
                root = Path.GetDirectoryName(root);

            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("Folder not found: " + root);

            string name = string.IsNullOrWhiteSpace(displayName) ? new DirectoryInfo(root).Name : displayName;
            DetectedApplication app = LoadApplication(ApplicationKind.FirefoxFork, name, root, true);
            if (app == null || app.Profiles.Count == 0)
                throw new InvalidOperationException("The selected folder does not contain a readable profiles.ini or Profiles subfolder.");

            return app;
        }

        private static IList<ProductSpec> GetProductSpecs()
        {
            var products = new List<ProductSpec>(BuiltInProducts);
            products.AddRange(LoadExternalProductSpecs());
            return products;
        }

        private static IEnumerable<ProductSpec> LoadExternalProductSpecs()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExternalLocationsFileName);
            if (!File.Exists(configPath))
                yield break;

            IniFile ini;
            try
            {
                ini = IniFile.Load(configPath);
            }
            catch
            {
                yield break;
            }

            foreach (string section in ini.SectionNames)
            {
                if (!section.StartsWith("Product", StringComparison.OrdinalIgnoreCase) &&
                    !section.StartsWith("Browser", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = ini.Get(section, "Name", string.Empty);
                string rootsText = ini.Get(section, "Roots", string.Empty);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rootsText))
                    continue;

                ApplicationKind kind = ParseApplicationKind(ini.Get(section, "Kind", "FirefoxFork"));
                string scope = ini.Get(section, "Scope", "Both");
                bool searchRoaming = !scope.Equals("Local", StringComparison.OrdinalIgnoreCase);
                bool searchLocal = !scope.Equals("Roaming", StringComparison.OrdinalIgnoreCase);
                string[] roots = SplitRoots(rootsText);
                if (roots.Length == 0)
                    continue;

                yield return new ProductSpec(kind, name, searchRoaming, searchLocal, roots);
            }
        }

        private static ApplicationKind ParseApplicationKind(string value)
        {
            if (value.Equals("Firefox", StringComparison.OrdinalIgnoreCase)) return ApplicationKind.Firefox;
            if (value.Equals("Thunderbird", StringComparison.OrdinalIgnoreCase)) return ApplicationKind.Thunderbird;
            if (value.Equals("ThunderbirdFork", StringComparison.OrdinalIgnoreCase)) return ApplicationKind.ThunderbirdFork;
            if (value.Equals("SeaMonkey", StringComparison.OrdinalIgnoreCase)) return ApplicationKind.SeaMonkey;
            if (value.Equals("Custom", StringComparison.OrdinalIgnoreCase)) return ApplicationKind.Custom;
            return ApplicationKind.FirefoxFork;
        }

        private static string[] SplitRoots(string rootsText)
        {
            string[] parts = rootsText.Split(new char[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var roots = new List<string>();
            foreach (string part in parts)
            {
                string root = part.Trim();
                if (root.Length > 0)
                    roots.Add(root);
            }
            return roots.ToArray();
        }

        private static void AddKnownProducts(IList<DetectedApplication> apps, ISet<string> seenRoots, IList<ProductSpec> products, string baseDirectory, bool localAppData)
        {
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
                return;

            foreach (ProductSpec product in products)
            {
                if (localAppData && !product.SearchLocal)
                    continue;
                if (!localAppData && !product.SearchRoaming)
                    continue;

                foreach (string relativeRoot in product.RelativeRoots)
                {
                    string root = Path.Combine(baseDirectory, relativeRoot);
                    string normalizedRoot = NormalizeRoot(root);
                    if (seenRoots.Contains(normalizedRoot))
                        continue;

                    DetectedApplication app = LoadApplication(product.Kind, product.Name + (localAppData ? " (local)" : string.Empty), root, !localAppData);
                    if (app == null || app.Profiles.Count == 0)
                        continue;

                    seenRoots.Add(normalizedRoot);
                    apps.Add(app);
                }
            }
        }

        private static DetectedApplication LoadApplication(ApplicationKind kind, string name, string rootDirectory, bool allowProfilesFolderFallback)
        {
            string profilesIni = Path.Combine(rootDirectory, "profiles.ini");
            var app = new DetectedApplication
            {
                Kind = kind,
                Name = name,
                ProfilesIniPath = File.Exists(profilesIni) ? profilesIni : string.Empty,
                RootDirectory = rootDirectory
            };

            if (File.Exists(profilesIni))
                LoadProfilesFromIni(app, profilesIni, rootDirectory, kind);

            // Some forks or portable builds do not write profiles.ini but still use
            // a Profiles folder. This fallback makes those profiles visible without
            // hard-coding every possible fork-specific implementation detail.
            if (allowProfilesFolderFallback && app.Profiles.Count == 0)
                LoadProfilesFromProfilesFolder(app, rootDirectory, kind);

            return app.Profiles.Count > 0 ? app : null;
        }

        private static void LoadProfilesFromIni(DetectedApplication app, string profilesIni, string rootDirectory, ApplicationKind kind)
        {
            try
            {
                IniFile ini = IniFile.Load(profilesIni);
                foreach (string section in ini.SectionNames)
                {
                    if (!section.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string path = ini.Get(section, "Path", string.Empty);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var profile = new MozillaProfile
                    {
                        ApplicationKind = kind,
                        Name = ini.Get(section, "Name", section),
                        Path = path,
                        RootDirectory = rootDirectory,
                        IsRelative = ini.GetInt(section, "IsRelative", 1) != 0,
                        IsDefault = ini.GetInt(section, "Default", 0) != 0
                    };

                    if (Directory.Exists(profile.FullPath))
                        app.Profiles.Add(profile);
                }
            }
            catch
            {
                // Ignore one malformed profiles.ini and let fallback detection try.
            }
        }

        private static void LoadProfilesFromProfilesFolder(DetectedApplication app, string rootDirectory, ApplicationKind kind)
        {
            string profilesDirectory = Path.Combine(rootDirectory, "Profiles");
            if (!Directory.Exists(profilesDirectory))
                return;

            try
            {
                foreach (string profileDirectory in Directory.GetDirectories(profilesDirectory))
                {
                    var directory = new DirectoryInfo(profileDirectory);
                    app.Profiles.Add(new MozillaProfile
                    {
                        ApplicationKind = kind,
                        Name = directory.Name,
                        Path = profileDirectory,
                        RootDirectory = rootDirectory,
                        IsRelative = false,
                        IsDefault = app.Profiles.Count == 0
                    });
                }
            }
            catch
            {
                // Ignore unreadable profile folders.
            }
        }

        private static string NormalizeRoot(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private sealed class ProductSpec
        {
            public ProductSpec(ApplicationKind kind, string name, bool searchRoaming, bool searchLocal, params string[] relativeRoots)
            {
                Kind = kind;
                Name = name;
                SearchRoaming = searchRoaming;
                SearchLocal = searchLocal;
                RelativeRoots = relativeRoots ?? new string[0];
            }

            public ApplicationKind Kind { get; private set; }
            public string Name { get; private set; }
            public bool SearchRoaming { get; private set; }
            public bool SearchLocal { get; private set; }
            public string[] RelativeRoots { get; private set; }
        }
    }
}
