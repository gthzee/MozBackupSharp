using System;
using System.Globalization;
using System.Text;
using MozBackupSharp.Core;

namespace MozBackupSharp.Services
{
    /// <summary>
    /// Minimal JSON writer/reader to keep the VS2015 project dependency-free.
    /// It is only intended for the manifest written by this application.
    /// </summary>
    public static class JsonLite
    {
        public static string ToJson(BackupManifest manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            AppendProperty(sb, "format", manifest.Format, true);
            AppendProperty(sb, "tool", manifest.Tool, true);
            AppendProperty(sb, "createdUtc", manifest.CreatedUtc.ToString("o", CultureInfo.InvariantCulture), true);
            AppendProperty(sb, "application", manifest.Application, true);
            AppendProperty(sb, "profileName", manifest.ProfileName, true);
            AppendProperty(sb, "sourcePath", manifest.SourcePath, true);
            AppendProperty(sb, "components", ((long)manifest.Components).ToString(CultureInfo.InvariantCulture), true, false);
            sb.AppendLine("  \"files\": [");
            for (int i = 0; i < manifest.Files.Count; i++)
            {
                sb.Append("    \"").Append(Escape(manifest.Files[i])).Append("\"");
                if (i < manifest.Files.Count - 1)
                    sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static BackupManifest FromJson(string json)
        {
            var manifest = new BackupManifest();
            manifest.Format = ReadString(json, "format");
            manifest.Tool = ReadString(json, "tool");
            manifest.Application = ReadString(json, "application");
            manifest.ProfileName = ReadString(json, "profileName");
            manifest.SourcePath = ReadString(json, "sourcePath");

            DateTime created;
            if (DateTime.TryParse(ReadString(json, "createdUtc"), null, DateTimeStyles.RoundtripKind, out created))
                manifest.CreatedUtc = created;

            long components;
            if (long.TryParse(ReadRaw(json, "components"), NumberStyles.Integer, CultureInfo.InvariantCulture, out components))
                manifest.Components = (BackupComponent)components;

            return manifest;
        }

        private static void AppendProperty(StringBuilder sb, string name, string value, bool comma)
        {
            AppendProperty(sb, name, value, comma, true);
        }

        private static void AppendProperty(StringBuilder sb, string name, string value, bool comma, bool quoteValue)
        {
            sb.Append("  \"").Append(Escape(name)).Append("\": ");
            if (quoteValue)
                sb.Append("\"").Append(Escape(value ?? string.Empty)).Append("\"");
            else
                sb.Append(value ?? "0");
            if (comma)
                sb.Append(',');
            sb.AppendLine();
        }

        private static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            var sb = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(c))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string ReadString(string json, string property)
        {
            int valueStart;
            if (!FindPropertyValue(json, property, out valueStart) || valueStart >= json.Length || json[valueStart] != '"')
                return string.Empty;

            valueStart++;
            var sb = new StringBuilder();
            for (int i = valueStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                    return sb.ToString();
                if (c == '\\' && i + 1 < json.Length)
                {
                    char escaped = json[++i];
                    switch (escaped)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(escaped); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ReadRaw(string json, string property)
        {
            int valueStart;
            if (!FindPropertyValue(json, property, out valueStart))
                return string.Empty;

            int valueEnd = valueStart;
            while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '\r' && json[valueEnd] != '\n' && json[valueEnd] != '}')
                valueEnd++;
            return json.Substring(valueStart, valueEnd - valueStart).Trim().Trim('"');
        }

        private static bool FindPropertyValue(string json, string property, out int valueStart)
        {
            valueStart = -1;
            if (json == null)
                return false;

            string token = "\"" + property + "\"";
            int propertyIndex = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
                return false;

            int colon = json.IndexOf(':', propertyIndex + token.Length);
            if (colon < 0)
                return false;

            valueStart = colon + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            return valueStart < json.Length;
        }
    }
}
