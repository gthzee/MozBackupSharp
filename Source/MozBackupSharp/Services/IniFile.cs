using System;
using System.Collections.Generic;
using System.IO;

namespace MozBackupSharp.Services
{
    /// <summary>
    /// Small INI parser for Mozilla profiles.ini. It intentionally keeps only the
    /// behavior needed by this port: sections and key=value pairs.
    /// </summary>
    public sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections;

        public IniFile()
        {
            _sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> SectionNames
        {
            get { return _sections.Keys; }
        }

        public static IniFile Load(string path)
        {
            var ini = new IniFile();
            string current = string.Empty;
            ini.EnsureSection(current);

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    ini.EnsureSection(current);
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                ini.EnsureSection(current)[key] = value;
            }

            return ini;
        }

        public string Get(string section, string key, string defaultValue)
        {
            Dictionary<string, string> values;
            string value;
            if (_sections.TryGetValue(section, out values) && values.TryGetValue(key, out value))
                return value;
            return defaultValue;
        }

        public int GetInt(string section, string key, int defaultValue)
        {
            int value;
            if (int.TryParse(Get(section, key, string.Empty), out value))
                return value;
            return defaultValue;
        }

        private Dictionary<string, string> EnsureSection(string name)
        {
            Dictionary<string, string> values;
            if (!_sections.TryGetValue(name, out values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[name] = values;
            }
            return values;
        }
    }
}
