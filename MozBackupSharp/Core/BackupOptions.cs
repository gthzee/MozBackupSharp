namespace MozBackupSharp.Core
{
    public sealed class BackupOptions
    {
        public BackupOptions()
        {
            Components = BackupComponent.All;
            IncludeUnknownFiles = true;
        }

        public MozillaProfile Profile { get; set; }
        public string ArchivePath { get; set; }
        public string Password { get; set; }
        public bool UseAesEncryption { get; set; }
        public BackupComponent Components { get; set; }
        public bool IncludeUnknownFiles { get; set; }
    }
}
