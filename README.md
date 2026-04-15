# SlimeRancher2-AP

A [BepInEx 6](https://github.com/BepInEx/BepInEx) IL2CPP plugin that adds [Archipelago](https://archipelago.gg) multiworld randomizer support to **Slime Rancher 2**.

Treasure pods, gordo slimes, map nodes, and fabricator blueprints become location checks. Region access, vacpack upgrades, gadgets, and newbucks are randomized items across the multiworld.

> **Note:** This repository contains only the client-side BepInEx plugin. The Archipelago world definition (`.apworld` Python file) lives in a separate Archipelago fork.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **Slime Rancher 2** | Purchased and installed via Steam |
| **BepInEx 6 IL2CPP** | [Download from BepInEx releases](https://github.com/BepInEx/BepInEx/releases) — use the `BepInEx_Unity.IL2CPP_x64` build. Install into the SR2 game folder and **launch the game once** to generate interop DLLs in `BepInEx/interop/`. |
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

- **Location/Item IDs** use base offset `819000`. The IDs in `Data/LocationConstants.cs` and `Data/ItemConstants.cs` **must match exactly** with those in the companion Python `.apworld`. When adding or changing IDs, update both.
- **Game object names** in `Data/LocationTable.cs` (the `GameObjectName` field) must match the in-game `GameObject.name` values exactly. Verify these using ILSpy on the BepInEx-generated interop DLLs (`BepInEx/interop/`) or by logging `gameObject.name` values from a Postfix patch at runtime.
- **Interop DLLs** (`BepInEx/interop/Il2Cpp*.dll`) are generated locally by BepInEx and are **not** committed to this repository. The project will not compile until BepInEx has generated them.
- **MapNode patch** (`Patches/LocationPatches/MapNodePatch.cs`) has its `[HarmonyPatch]` attribute commented out pending confirmation of the exact class/method name via ILSpy.
- **MainMenu patch** (`Patches/UiPatches/MainMenuPatch.cs`) is similarly stubbed until the `MainMenuUI` class name is confirmed.
