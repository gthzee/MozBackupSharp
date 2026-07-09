using System;

namespace MozBackupSharp.Core
{
    public sealed class MozillaProfile
    {
        public MozillaProfile()
        {
            Name = string.Empty;
            Path = string.Empty;
            RootDirectory = string.Empty;
        }

        public string Name { get; set; }
        public string Path { get; set; }
        public string RootDirectory { get; set; }
        public bool IsRelative { get; set; }
        public bool IsDefault { get; set; }
        public ApplicationKind ApplicationKind { get; set; }

        public string DisplayName
        {
            get
            {
                string defaultText = IsDefault ? " (default)" : string.Empty;
                return string.Format("{0}{1} - {2}", Name, defaultText, FullPath);
            }
        }

        public string FullPath
        {
            get
            {
                if (string.IsNullOrEmpty(Path))
                    return string.Empty;
                if (!IsRelative || System.IO.Path.IsPathRooted(Path))
                    return Path;
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(RootDirectory, Path));
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
