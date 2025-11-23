# NoBurden

![License](https://img.shields.io/badge/license-AGPL--3.0-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![ACE Version](https://img.shields.io/badge/ACE-compatible-green)

A server mod for Asheron's Call Emulator (ACE) that disables encumbrance/burden mechanics for players at or below a configurable character level.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Configuration](#configuration)
- [Installation](#installation)
- [Usage](#usage)
- [Commands](#commands)
- [How It Works](#how-it-works)
- [Building](#building)
- [Project Structure](#project-structure)
- [Development](#development)
- [Technical Details](#technical-details)
- [License](#license)

## Overview

NoBurden is a Harmony-based mod that allows new or low-level players to carry items without encumbrance penalties. This is useful for servers that want to reduce the burden (pun intended) on new players during early progression.

**Use case:** A new player at level 5 can carry as many items as they want without being burdened. Once they reach level 11 (default threshold is 10), burden mechanics apply normally.

## Features

✅ **Dynamic burden prevention** - Players below the threshold carry unlimited items
✅ **Zero encumbrance value** - Burden indicator never shows for protected players
✅ **Level-up warning** - Red warning message when player crosses the threshold
✅ **Admin reload command** - Hot-reload settings without server restart
✅ **Helper extension** - Other code can check burden status via `.IsBurdenIgnored()`

## Configuration

### Settings.json

The mod configuration is stored in `Settings.json`:

```json
{
  "BurdenThresholdLevel": 10
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `BurdenThresholdLevel` | Long | 10 | Character level at which burden starts applying. Players at or below this level ignore encumbrance penalties. Set to 0 to disable the mod (burden applies to all players). |

### Changing Settings at Runtime

Edit `Settings.json` and the mod will automatically detect changes via file watcher. For manual reload:

**In-game (admin only):**
```
/noburden reload
```

**Console:**
```
noburden reload
```

Both commands will reload the settings and report what changed:
```
NoBurden settings reloaded. Burden threshold: 5 (was 10)
```

## Installation

### Prerequisites

- ACE Server (compatible with .NET 8.0 builds)
- Mod support enabled in your ACE configuration
- Write access to the MODS directory

### Deploy the Mod

1. Build the project (see [Building](#building) section)
2. Copy the compiled files to your server's mods directory:
   ```
   D:\YourServer\Mods\NoBurden\
   ```
3. Files needed:
   - `NoBurden.dll` - The compiled mod
   - `Meta.json` - Mod metadata
   - `Settings.json` - Configuration (created automatically if missing)

### Verify Installation

On server startup, you should see logs like:
```
NoBurden started successfully!
Burden disabled for characters level 5 and below
```

In-game, admins can verify the mod is working:
```
/noburden status
NoBurden - Current burden threshold: 10
```

## Usage

### For Players

Players below the configured level can carry unlimited items without encumbrance effects:
- No burden debuff
- Carry weight has no impact
- Items take up inventory slots normally

When leveling up and crossing the threshold, players see a red warning:
```
WARNING: Your level has reached 50 you now suffer from the effects of burden!
(This effect may not be applied until the next time you log in.)
```

### For Admins

#### Check Current Setting
```
/noburden status
```
Shows the current burden threshold level.

#### Change Threshold
1. Edit `Settings.json` in the mod directory
2. Change `BurdenThresholdLevel` to desired level
3. Save the file
4. Run `/noburden reload` or `noburden reload` (console) to apply immediately

**Example:** To disable burden for all players under level 20:
```json
{
  "BurdenThresholdLevel": 20
}
```

#### Disable the Mod Entirely
```json
{
  "BurdenThresholdLevel": 0
}
```

This sets the threshold to 0, making burden apply to all players (default ACE behavior).

## Commands

### `/noburden` (Admin Command)

Main command for NoBurden mod management. Works both in-game (with `/` prefix) and in console (without `/` prefix).

**Access Level:** Admin only

#### Subcommands

##### `status` - Show Current Threshold
**Usage:** `/noburden status` or `noburden status`
**Output:**
```
NoBurden - Current burden threshold: 10
```

##### `reload` - Reload from Settings.json
**Usage:** `/noburden reload` or `noburden reload`
**Description:** Reloads Settings.json from disk

**Output Examples:**
```
NoBurden settings reloaded. Burden threshold: 10
```

```
NoBurden settings reloaded. Burden threshold: 15 (was 10)
```

##### `limit <level>` - Set Threshold
**Usage:** `/noburden limit <level>` or `noburden limit <level>`
**Description:** Changes the burden threshold and saves to Settings.json immediately

**Examples:**
```
/noburden limit 15
NoBurden burden threshold updated: 15 (was 10)
```

```
/noburden limit 20
NoBurden burden threshold updated: 20 (was 15)
```

**Error Handling:**
```
NoBurden burden threshold updated: 0
```
(Level 0 disables the mod - burden applies to all players)

##### `default` - Reset to Default
**Usage:** `/noburden default` or `noburden default`
**Description:** Resets the threshold to the default value (10)

**Output:**
```
NoBurden threshold reset to default: 10 (was 20)
```

### Default Behavior
If no subcommand is provided, help and status are shown:
```
/noburden
=== NoBurden Commands ===
/noburden status - Show current threshold
/noburden reload - Reload from Settings.json
/noburden limit <level> - Set threshold level
/noburden default - Reset to default (10)
NoBurden - Current burden threshold: 10
```

## Building

### Requirements

- .NET 8.0 SDK or later
- Visual Studio 2022 or VS Code (optional)
- NuGet packages: `ACEmulator.ACE.Shared` and `Lib.Harmony 2.3.3`

### Build Output

Compiled files are automatically copied to:
```
D:\DEV\ACE SERVER\ACHARD-TEST\MODS\NoBurden\
```

## Project Structure

```
NoBurden/
├── NoBurden.sln              # Solution file
├── NoBurden.csproj           # Project configuration
├── Mod.cs                    # Mod entry point
├── PatchClass.cs             # Harmony patches and commands
├── GlobalUsings.cs           # Global using statements
├── Meta.json                 # Mod metadata
├── Settings.json             # Configuration file
└── README.md                 # This file

NoBurden.dll                  # Compiled mod (output)
```

### File Descriptions

| File | Purpose |
|------|---------|
| `Mod.cs` | Entry point - initializes the mod and patch class |
| `PatchClass.cs` | All Harmony patches and admin commands |
| `GlobalUsings.cs` | Global imports for ACE, Harmony, System namespaces |
| `Meta.json` | Metadata for server mod manager (name, version, description) |
| `Settings.json` | Runtime configuration, auto-created if missing |

## How It Works

NoBurden uses four Harmony patches and one helper extension to disable burden mechanics:

### 1. Encumbrance Capacity Patch
**Method:** `Player.GetEncumbranceCapacity()`
**Effect:** Returns effectively unlimited capacity (10,000,000) for players below threshold
**Purpose:** Prevents the encumbered status effect from triggering

### 2. Encumbrance Value Setter Patch
**Method:** `WorldObject.EncumbranceVal` (setter)
**Effect:** Sets burden value to 0 for protected players
**Purpose:** Ensures the burden indicator never displays

### 3. Level-Up Detection (Prefix)
**Method:** `Player.UpdateXpAndLevel()` (Prefix)
**Effect:** Captures player level BEFORE level-up processing
**Purpose:** Allows detection of threshold crossing

### 4. Level-Up Detection (Postfix)
**Method:** `Player.UpdateXpAndLevel()` (Postfix)
**Effect:** Detects if player crossed threshold and sends red warning message
**Purpose:** Notifies player when burden protection ends

### 5. Helper Extension
**Method:** `IsBurdenIgnored()` (extension on Player)
**Usage:** `if (player.IsBurdenIgnored()) { /* custom logic */ }`
**Purpose:** Allows other code to check if a player is protected

### Settings Caching

To avoid repeated `Settings.json` reads in hot paths:
- `CachedThreshold` static variable stores the current level
- Updated only during `OnStartSuccess()` and when `/noburden reload` is called
- Patches use the cached value instead of hitting the file

### File Watching

The framework automatically watches `Settings.json` for changes:
- When file is edited and saved, `FileSystemWatcher` detects the change
- Mod is automatically restarted (Stop → Start)
- New settings loaded from disk
- No need to manually reload (though `/noburden reload` command also works)

## Development

### Code Examples

#### Accessing Settings
```csharp
// In any method
if (PatchClass.Settings.BurdenThresholdLevel > 0)
{
    // Burden protection is enabled
}
```

#### Using Helper Extension
```csharp
// Check if a player is protected
if (player.IsBurdenIgnored())
{
    ModManager.Log($"{player.Name} is protected from burden");
}
```

### Key Classes

- **PatchClass** - Inherits from `BasicPatch<Settings>`, contains all patches
- **Settings** - POCO class with configurable properties
- **Harmony** - Uses version 2.3.3 for runtime method patching
- **BasicPatch<T>** - Framework base class handling lifecycle and settings management

## Technical Details

### .NET and Framework Versions

- **Target:** .NET 8.0
- **Harmony:** 2.3.3
- **ACE.Shared:** 1.x via NuGet
- **Realms Support:** Conditional compilation with `REALM` constant

### Dependencies

- `ACEmulator.ACE.Shared` - ACE server types and utilities
- `Lib.Harmony` - Runtime method patching library
- `.NET System libraries` - Standard .NET functionality

## License

This project is licensed under the **GNU Affero General Public License v3.0** (AGPL-3.0).

See the [LICENSE](LICENSE) file for full details, or visit https://www.gnu.org/licenses/agpl-3.0.html

### Summary

- ✅ You can use, modify, and distribute this mod
- ✅ Source code must be made available
- ✅ Same license must be applied to derivatives
- ⚠️ Network use requires source code availability (AGPL clause)

## Contributing

Contributions welcome! For modifications:

1. Fork or clone this repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly with an ACE server
5. Submit a pull request or share feedback

## Resources

- **Harmony Documentation:** https://harmony.pardeike.net/
- **ACE Server:** https://github.com/ACEmulator/ACE
- **ACE.BaseMod Reference:** Look at sample mods for best practices

## Support

For issues or questions:

1. Check server logs for error messages
2. Review the [Installation](#installation) section for setup issues
3. Check the [Configuration](#configuration) section for settings-related questions

---

**NoBurden v1.0** - Made for ACE Emulator
Last Updated: November 2025
