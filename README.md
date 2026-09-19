# Project Hospital Mods by Loupito75

A collection of mods for **Project Hospital**, developed and maintained by **Loupito75**.

This repository contains the public source code and releases of my Project Hospital mods.

## Mods

| Mod                                                                       | Description                                                                                                      | First release  | Latest release                                                                                          | Links                                                                                                                                                                                                                                                                          |
| ------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- | -------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| [Hospital Always Lit](mods/Loupito75.HospitalAlwaysLit/)                  | Keeps selected hospital rooms illuminated even when they are not currently occupied.                             | 2026-09-07 | [1.1.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalAlwaysLit-v1.1.0)         | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3797438326)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3797438326)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3797438326/586185220309616025/) |
| [Hospital Traffic Control](mods/Loupito75.HospitalTrafficControl/)        | Improves character access, movement rules and traffic management inside the hospital.                            | 2026-09-07 | [1.1.1](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalTrafficControl-v1.1.1)    | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3797445876)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3797445876)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3797445876/571548834124107441/) |
| [Hospital Care Level Transfer](mods/Loupito75.HospitalCareLevelTransfer/) | Allows eligible HDU patients to transfer safely to a regular ward when high-priority care is no longer required. | 2026-09-08 | [1.0.1](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalCareLevelTransfer-v1.0.1) | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3798356542)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3798356542)<br>[Changelog](https://steamcommunity.com/workshop/filedetails/discussion/3798356542/571548834124106679/) |
| [Hospital Shift Handover](mods/Loupito75.HospitalShiftHandover/)          | Makes staff arrivals, shift changes and handovers more natural and realistic.                                    | 2026-09-10 | [1.0.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalShiftHandover-v1.0.0)     | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3799192971)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3799192971)                                                                                                          |
| [Hospital EMS](mods/Loupito75.HospitalEMS/)                               | Adds prehospital assessment and emergency care for patients arriving by ambulance or helicopter.                 | 2026-09-10 | [1.0.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalEMS-v1.0.0)               | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3799207833)<br>[Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3799207833)                                                                                                          |
| Hospital Patient Life 1.0.0 |  |  | [1.0.0](https://github.com/Loupito75/Project-Hospital-Mods/releases/tag/HospitalPatientLife-v1.0.0) |  |

## Installation

These are **BepInEx code mods**.

For complete BepInEx installation, troubleshooting and general mod installation instructions, see my Steam guide:

**[BepInEx Code Mods — Installation Guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3797459363)**

BepInEx can be downloaded from its official release page:

**[BepInEx — Official Releases](https://github.com/BepInEx/BepInEx/releases)**

### Installing from GitHub

When downloading a mod from this repository, you do **not** need to subscribe to its Steam Workshop item.

Instead:

1. Open the mod's latest GitHub Release.
2. Download the provided ZIP archive.
3. Extract the mod folder into:

```text
Project Hospital\BepInEx\plugins\
```

The resulting structure should look similar to:

```text
Project Hospital
└── BepInEx
    └── plugins
        └── Loupito75.ModName
            ├── Loupito75.ModName.dll
            └── additional files if required
```

The Steam guide still applies for installing BepInEx and troubleshooting. The only difference is where the mod files come from: **download the ZIP from GitHub instead of subscribing to the Steam Workshop item and copying its files.**

Avoid installing the same mod from both GitHub and the Steam Workshop at the same time, as this may result in duplicate plugin files.

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
Project Hospital\BepInEx\LogOutput.log
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
