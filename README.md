# MozBackupSharp
 
A C# WinForms application for backing up and restoring Firefox, Thunderbird, and other Mozilla-based browser profiles on Windows.

> An AI-generated port of the original [MozBackup](https://github.com/JasnaPaka/mozbackup/) tool — fully rewritten in C# using AI, not manually translated line by line.
 
---

## Screenshots
 
![Main](https://github.com/gthzee/MozBackupSharp/raw/main/MozBackupSharp.png)

![Password Protection](https://github.com/gthzee/MozBackupSharp/raw/main/Password.png)
 
![Backup](https://github.com/gthzee/MozBackupSharp/raw/main/Backup.png)
 
---
 
## Features
 
- **Backup & Restore** your browser profiles to a `.pcv` or `.zip` file
- **Two password protection modes** — Classic ZIP (ZipCrypto, compatible with original MozBackup) or AES (MozBackupSharp's own secure container)
- **Supports many browsers** — Firefox, Thunderbird, SeaMonkey, Waterfox, LibreWolf, Floorp, Pale Moon, and more
- **Choose what to back up** — bookmarks, passwords, cookies, extensions, mail folders, and more
- **Portable browser support** — manually select a profile folder for Tor Browser, Mullvad Browser, etc.
- **Extensible** — add custom browser profile paths via `ProfileLocations.ini`

---
 
## Requirements
 
- Windows
- [.NET Framework 4.5](https://dotnet.microsoft.com/en-us/download/dotnet-framework)
- Visual Studio 2015 (to build from source)

---
 
## Getting Started
 
1. **Close your browser** before running a backup or restore.
2. Open the app and **select your browser and profile**.
3. Choose **Backup** or **Restore** and follow the prompts.
4. Pick the data you want to include (bookmarks, passwords, extensions, etc.).
5. Optionally set a **password** to encrypt the backup file.

---
 
## Building from Source
 
1. Clone this repository.
2. Open `MozBackupSharp.sln` in Visual Studio 2015.
3. Build using `Debug|Any CPU` or `Release|Any CPU`.
 
---
 
## Backup File Format
 
- **No password:** the `.pcv` file is a standard ZIP archive (openable in 7-Zip or Windows Explorer).
- **Classic ZIP (ZipCrypto):** password-protected using the same format as the original MozBackup — compatible with other ZIP tools and the original app.
- **AES (MozBackupSharp):** a more secure encrypted container. Only MozBackupSharp can restore these files.
> **Tip:** Use **Classic ZIP** mode if you need compatibility with the original MozBackup or standard ZIP tools. Use **AES** mode for stronger encryption.
 
---
 
## Supported Browsers
 
| Family | Browsers |
|---|---|
| Firefox | Firefox, Waterfox, LibreWolf, Floorp, Zen Browser, Pale Moon, Basilisk, GNU IceCat, and more |
| Thunderbird | Thunderbird, Betterbird |
| SeaMonkey | SeaMonkey |
| Portable | Tor Browser, Mullvad Browser (via manual profile folder selection) |
 
---
 
## License
 
Licensed under [MPL 2.0](https://www.mozilla.org/en-US/MPL/2.0/), consistent with the upstream MozBackup project. Please retain all original license notices when distributing.
