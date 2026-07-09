using System;
using System.Collections.Generic;

namespace MozBackupSharp.Core
{
    public sealed class BackupManifest
    {
        public BackupManifest()
        {
            CreatedUtc = DateTime.UtcNow;
            Files = new List<string>();
        }

        public string Format { get; set; }
        public string Tool { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Application { get; set; }
        public string ProfileName { get; set; }
        public string SourcePath { get; set; }
        public BackupComponent Components { get; set; }
        public List<string> Files { get; private set; }
    }
}
