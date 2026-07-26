# SlimeRancher2-AP

A [BepInEx 6](https://github.com/BepInEx/BepInEx) IL2CPP plugin that adds [Archipelago](https://archipelago.gg) multiworld randomizer support to **Slime Rancher 2**.

Treasure pods, gordo slimes, map nodes, and fabricator blueprints become location checks. Region access, vacpack upgrades, gadgets, and newbucks are randomized items across the multiworld.

> **Note:** This repository contains only the client-side BepInEx plugin. The Archipelago world definition (`.apworld` Python file) lives in a separate Archipelago fork.

---

## Installation (Players)

1. **Download BepInEx** — Unity IL2CPP x64, bleeding-edge build **be.755** (newer builds are known to fail — see the note below):
   - [Windows and Linux](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip)
   - [macOS](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.755%2B3fab71a.zip)
2. **Download the mod and the apworld** from the [GitHub Releases page](https://github.com/Rhelys/SlimeRancher2-AP/releases).
3. **Install BepInEx:**
   - Extract all of the BepInEx files into the game's root folder (the one containing the `.exe` — for Steam users, this is the folder that opens when you right-click Slime Rancher 2 → **Manage** → **Browse local files**).
   - **Linux players:** you must also add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to the game's Steam launch options. In Steam, right-click Slime Rancher 2 → **Properties** → **General** tab → **Launch Options**. (Thanks to @izzy for the troubleshooting!)
   - Launch the game once and let it fully load to the main menu, then close it — this generates the interop DLLs BepInEx needs.
   - Extract the mod zip into `BepInEx/plugins/` in the game folder.
4. **Connect before starting a new game.** Launch the game, go to **Settings → Archipelago**, and connect to your server (see [Connecting to Archipelago](#connecting-to-archipelago) below for details). **You must connect before starting a new game** — the mod needs the server connection to set up your randomized world correctly.
5. **Start a new game** in the save slot of your choice and enjoy!

> Some BepInEx builds newer than be.755 (e.g. be.784) fail with `MissingMethodException: AsmResolver.DotNet.ModuleDefinition..ctor` due to a Cpp2IL/AsmResolver version mismatch internal to BepInEx — this is a BepInEx bug, not a mod issue. If you hit it and no plugins load, use be.755 until a fixed build is available.

---

## Prerequisites (Building from Source)

| Requirement | Notes |
|---|---|
| **Slime Rancher 2** | Purchased and installed via Steam |
| **BepInEx 6 IL2CPP** | Same be.755 build linked above. Install into the SR2 game folder and **launch the game once** to generate interop DLLs in `BepInEx/interop/`. |
| **.NET 6 SDK** | [Download from Microsoft](https://dotnet.microsoft.com/download/dotnet/6.0) |

---

## Building for Testing

1. **Clone the repository**
   ```bash
   git clone https://github.com/Rhelys/SlimeRancher2-AP.git
   cd SlimeRancher2-AP
   ```

2. **Set your SR2 install path** (if not at the default Steam location)

   Create a `Directory.Build.props` file in the repo root:
   ```xml
   <Project>
     <PropertyGroup>
       <GameDir>D:\Games\SteamLibrary\steamapps\common\Slime Rancher 2</GameDir>
     </PropertyGroup>
   </Project>
   ```
   Alternatively, set the `GameDir` environment variable before building.

3. **Build in Debug configuration**
   ```bash
   dotnet build -c Debug
   ```
   This compiles the plugin and automatically copies the following files into `<GameDir>/BepInEx/plugins/SlimeRancher2-AP/`:
   - `SlimeRancher2-AP.dll`
   - `Archipelago.MultiClient.Net.dll`

4. **Launch Slime Rancher 2**

   BepInEx will load the plugin automatically. Check `BepInEx/LogOutput.log` for `[SlimeRancher2-AP] All patches applied.` to confirm it loaded.

### Building a Release ZIP

```bash
dotnet build -c Release
```

A `SlimeRancher2-AP.zip` will be produced in `bin/Release/`.

---

## Connecting to Archipelago

1. Launch SR2 and reach the main menu.
2. Click the **Archipelago** button (injected by the mod).
3. Enter your server details:
   - **Host:Port** — e.g. `archipelago.gg:38281`
   - **Slot Name** — your player name from the multiworld generation
   - **Password** — leave blank if the room has no password
4. Click **Connect**. The status HUD in the top-left will turn green when connected.

Connection details are saved automatically and pre-filled on the next launch.

---

## Notes for Contributors

- **Location/Item IDs** use base offset `819000`. The IDs in `Data/LocationConstants.cs` and `Data/ItemTable.cs` **must match exactly** with those in the companion Python `.apworld`. When adding or changing IDs, update both.
- **Game object names** in `Data/LocationTable.cs` (the `GameObjectName` field) must match the in-game `GameObject.name` values exactly. Verify these using ILSpy on the BepInEx-generated interop DLLs (`BepInEx/interop/`) or by logging `gameObject.name` values from a Postfix patch at runtime.
- **Interop DLLs** (`BepInEx/interop/Il2Cpp*.dll`) are generated locally by BepInEx and are **not** committed to this repository. The project will not compile until BepInEx has generated them.

---

## Slime Rancher 2 AI Usage Disclosure

- This implementation does **not** use any AI-generated art assets
- Generative AI is used for the following activities:
   - IL2CPP decompilation assistance and understanding how the functions are implemented in the game files
   - Initial Harmony (game-hooking framework) patch base implementation for each feature
   - Keeping location names, IDs, and logic rules consistent across the C# mod and Python apworld
   - Bug identification on a recurring cadence
   - Initial code comments
   - Github code interactions (commit messages, git commands, etc)
- All merged code, comments, and descriptions generated by AI are human-tested and reviewed prior to committing
- All release notes are human-generated