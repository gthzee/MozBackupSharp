using System;

namespace MozBackupSharp.Core
{
    [Flags]
    public enum BackupComponent
    {
        None = 0,
        BookmarksAndHistory = 1,
        Passwords = 2,
        Cookies = 4,
        FormHistory = 8,
        Preferences = 16,
        Extensions = 32,
        Certificates = 64,
        Mail = 128,
        AddressBooks = 256,
        OtherImportantFiles = 512,
        All = BookmarksAndHistory | Passwords | Cookies | FormHistory | Preferences |
              Extensions | Certificates | Mail | AddressBooks | OtherImportantFiles
    }
}
