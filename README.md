# NO ATC Mod

An Air Traffic Control (ATC) mod for Nuclear Option, inspired by LotAtc functionality.

## Features

- **Radar Tracking**: Real-time tracking of all units within configurable radar range
- **Multiplayer Support**: Properly identifies friendly/hostile units using NetworkHQ (works in multiplayer)
- **BRAA Information**: Bearing, Range, Altitude, and Aspect calculations for all tracked units
- **ATC Window**: Overlay window displaying tracked units with detailed information
- **Unit Information**: Shows distance, altitude, speed, heading, and BRAA data
- **Friendly/Hostile Identification**: Color-coded unit status based on NetworkHQ
- **Optional Radar Coverage**: Realistic radar simulation with terrain masking and elevation limits (like LotAtc)
- **Pilot Names**: Shows player names in multiplayer for better identification
- **Configurable**: All settings accessible via BepInEx Configuration Manager

## Installation

1. Ensure you have BepInEx 5 installed for Nuclear Option https://github.com/bepinex/bepinex/releases
2. Ensure you have BepInEx Configuration Manager https://github.com/BepInEx/BepInEx.ConfigurationManager/releases
3. Download the 'ATF_ATC_MOD.zip'
4. Extract the contents of the folder
5. Place `ATF_ATC_MOD' into your bepinex plugins folder
6. Launch the game

## Configuration

Press **F1** in-game to open the Configuration Manager, then navigate to the **NO ATC Mod** section.

### Key Settings

- **Enabled**: Enable/disable the mod
- **Show ATC Window**: Toggle the ATC radar window visibility
- **Toggle Window Key**: Key to toggle the ATC window (default: F9)
- **Radar Range**: Maximum detection range in meters (default: 50,000m = 50km)
- **Radar Update Interval**: How often the radar updates (default: 0.1 seconds)
- **Show BRAA**: Display BRAA information for each unit
- **Show Unit Info**: Display detailed unit information
- **ATC Color**: Main color for the ATC display (default: green)
- **Enable Radar Coverage**: Enable realistic radar coverage simulation (optional)
- **Max Elevation**: Maximum altitude for radar detection (default: 50,000m)
- **Min Elevation**: Minimum altitude for radar detection (default: 0m)
- **Use Terrain Masking**: Enable terrain masking (units behind terrain cannot be detected)
- **Unit type filters**: **Aircraft**, **Ships**, **Ground Vehicles**, **Buildings** – choose which unit types appear in the ATC list (toggles in the window and in Settings).

### Icons (optional)

The mod shows small icons next to contacts on the map. By default it uses simple built-in shapes (green = friendly, red = hostile, gray = neutral). You can add your own PNGs in the **icons** folder next to the DLL (`BepInEx/plugins/NO_ATC_Mod/icons/`). Optional filenames:

- `aircraft_friendly.png`, `aircraft_hostile.png` – aircraft
- `unit_friendly.png`, `unit_hostile.png` – ships, ground, buildings
- `contact.png` – fallback for any contact

If a file is missing, the mod uses the built-in icon for that type.

## Usage

1. Press **F9** (or your configured key) to toggle the ATC window
2. The window will display all units within radar range
3. Units are sorted by distance (closest first)
4. Each unit shows:
   - Friendly/Hostile status
   - Distance from player
   - Altitude
   - Speed
   - Heading
   - BRAA information (if enabled)

## BRAA Information

BRAA stands for:
- **Bearing**: Direction to target in degrees (0-360°)
- **Range**: Distance to target
- **Altitude**: Target altitude
- **Aspect**: Angle between your heading and the target

## Notes

- This mod is inspired by LotAtc but designed specifically for Nuclear Option
- **Multiplayer Compatible**: Uses NetworkHQ to properly identify friendly/hostile units across all players
- The mod uses BepInEx Harmony patches to integrate with the game
- All tracked units are updated in real-time based on the configured update interval
- The window is draggable and resizable
- **Radar Coverage**: Optional feature that simulates realistic radar limitations:
  - Terrain masking: Units behind terrain cannot be detected
  - Elevation limits: Units above/below certain altitudes cannot be detected
  - Similar to LotAtc's advanced radar coverage system

## Compatibility

- Compatible with BepInEx 5
- Should work alongside other Nuclear Option mods
- Tested with NO_Tactitools and NOAutopilot mods

## License

This mod is provided as-is for use with Nuclear Option.
