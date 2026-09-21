# Project Hospital Mods by Loupito75

A collection of mods for **Project Hospital**, developed and maintained by **Loupito75**.

This repository contains the public source code and releases of my Project Hospital mods.

## Mods

| Mod | Description | First release | Latest release | Links |
| --- | --- | --- | --- | --- |
| [Hospital Always Lit](mods/Loupito75.HospitalAlwaysLit/) | Keeps selected hospital rooms illuminated even when they are not currently occupied. | 2026-09-07 | [1.2.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalAlwaysLit-v1.2.0) | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3797438326)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3797438326)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3797438326/586185220309616025/) |
| [Hospital Traffic Control](mods/Loupito75.HospitalTrafficControl/) | Improves character access, movement rules and traffic management inside the hospital. | 2026-09-07 | [1.2.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalTrafficControl-v1.2.0) | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3797445876)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3797445876)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3797445876/571548834124107441/) |
| [Hospital Care Level Transfer](mods/Loupito75.HospitalCareLevelTransfer/) | Allows eligible HDU patients to transfer safely to a regular ward when high-priority care is no longer required. | 2026-09-08 | [1.1.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalCareLevelTransfer-v1.1.0) | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3798356542)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3798356542)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3798356542/571548834124106679/) |
| Hospital Shift Handover | Makes staff arrivals, shift changes and handovers more natural and realistic. | 2026-09-10 | — | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3799192971)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3799192971)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3799192971/570423638738861380/) |
| [Hospital EMS](mods/Loupito75.HospitalEMS/) | Adds prehospital assessment and emergency care for patients arriving by ambulance or helicopter. | 2026-09-10 | [1.0.1](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalEMS-v1.0.1) | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3799207833)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3799207833)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3799207833/570423638738861302/) |
| [Hospital Patient Life](mods/Loupito75.HospitalPatientLife/) | Makes hospitalized patients behave more naturally between procedures with cafeteria visits, leisure activities, improved needs handling, staggered sleep schedules and smarter movement choices. | 2026-09-18 | [1.0.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalPatientLife-v1.0.0) | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3803626360)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3803626360)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3803626360/570423638738764762/) |

## Installation

These are **BepInEx code mods**.

For complete BepInEx installation instructions for Windows and macOS, code mod installation, updates and troubleshooting, see my Steam guide:

**[BepInEx Code Mods — Installation Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3797459363)**

BepInEx can be downloaded from its official release page:

**[BepInEx — Official Releases](https://github.com/BepInEx/BepInEx/releases)**

Download files are available in the **Assets** section of each BepInEx release. See the installation guide above for the correct package for your platform.

### Installing from GitHub

When downloading a mod from this repository, you do **not** need to subscribe to its Steam Workshop item.

Instead:

1. Use the Latest release column in the table above to open the mod's latest GitHub Release.
2. In the Assets section near the bottom of the release page, download the provided mod ZIP archive.
3. Do not download GitHub's automatically generated Source code archives.
4. Extract the complete mod folder into:

```text
BepInEx/plugins
```

The resulting structure should look similar to:

```text
Project Hospital
└── BepInEx
    └── plugins
        └── Loupito75.ModName
            ├── Loupito75.ModName.dll
            ├── configuration XML files if included
            └── additional files or folders if required
```

Always install the complete mod package. Some mods may include configuration XML files, a Database folder or other files required at runtime.

The Steam guide still applies for installing BepInEx and troubleshooting. The only difference is where the mod files come from: **download the ZIP from GitHub instead of subscribing to the Steam Workshop item and copying its files.**

If you install a mod from GitHub, subscribing to the same Steam Workshop item is not required.

Avoid keeping multiple installed copies of the same plugin under BepInEx/plugins, as BepInEx may load duplicate plugin files.

### Updating Mods

Files installed inside `BepInEx/plugins` are **not updated automatically**.

Check the **Latest release** column in the mod table above to see whether a newer GitHub version is available.

When updating a mod, replace its DLL and any other required runtime files or folders with those included in the new release.

If you have customized an XML or configuration file, **do not automatically overwrite it**. Back it up first and check the release notes or changelog for new or changed configuration options.

For complete update instructions, see the [BepInEx Code Mods — Installation Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3797459363).

## Repository structure

```text
mods/
└── Loupito75.ModName/
    ├── source files
    └── configuration files when applicable
```

Each mod has its own directory containing its public source code and configuration files when applicable.

Compiled release packages are distributed through GitHub Releases and are not stored directly in the repository.

## Releases

Each public version is published through the GitHub **Releases** section of this repository.

Release tags identify both the mod and its version.

Example:

```text
ModName-vX.X.X
```

The downloadable archive follows the same principle:

```text
Loupito75.ModName-vX.X.X.zip
```

The mod ZIP is available in the **Assets** section of its GitHub Release.

For installation, use this ZIP rather than GitHub's automatically generated **Source code** archives.

## Versioning

Public releases use three-part version numbers:

```text
MAJOR.MINOR.PATCH
```

Examples:

```text
1.0.0
1.0.1
1.1.0
```

Version numbers shown in the mod, plugin metadata, assembly information, documentation and GitHub Release are kept synchronized.

## Bug reports and suggestions

Bug reports and suggestions should be posted in the **Steam Workshop Discussions** of the corresponding mod.

Use the links in the mod table above to access its Workshop page, discussions and changelog.

When reporting a problem, please include the mod version, your Project Hospital version, steps to reproduce the issue and the relevant contents of:

```text
BepInEx/LogOutput.log
```

## Compatibility

These mods are developed specifically for **Project Hospital**.

Game updates may occasionally require mod updates. Check the latest release notes and changelog if a mod stops working after a Project Hospital update.

## Source code

The mods in this repository are developed using C#, BepInEx, Harmony / HarmonyLib, Unity and Project Hospital's game APIs.

Project Hospital game assemblies, assets and other proprietary game files are **not distributed with this repository**.

## Disclaimer

These are unofficial community mods.

They are not affiliated with, endorsed by, or supported by Oxymoron Games or the developer or publisher of Project Hospital.

Project Hospital and related names and assets belong to their respective owners.

## License

The source code in this repository is publicly available for viewing purposes only.

**All rights reserved.** No permission is granted to use, copy, modify, redistribute, repackage, incorporate into another project or create derivative works from the source code without prior written permission from **Loupito75**.

See [LICENSE](LICENSE) for full terms.

---

Developed by **Loupito75**.
