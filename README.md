# Urbex Simulator

A first-person multiplayer urban exploration game built in Unity 6. Climb, sneak, and document abandoned places with friends - take photos with a handheld camera, light your way with a flashlight, and try not to fall.

> Work in progress. Expect rough edges, placeholder art, and rapid changes.

## Features

- **First-person movement** - WASD walk/sprint, stamina-gated sprinting, jump, headroom-aware crouch (currently disabled while being reworked), and a climbing controller for short ledges and walls.
- **Handheld camera item** - Press `C` to equip the viewfinder. Scroll to zoom (wide to telephoto), left click to take a photo. Captures are saved as PNGs to disk along with a per-match manifest so you can find your shots later.
- **Flashlight item** - Real pickup-and-drop physical item with a wide, FOV-independent cone spotlight so player FOV settings don't give a lighting advantage.
- **Hotbar inventory** - 5 slots (camera + 4 inventory), select with `C` / `1` / `2` / `3` / `4`. Pick up items with `E` into the active slot or the first open slot; drop the held item with `Q`. Item names render inside each slot, keybinds underneath.
- **Server-authoritative multiplayer** - Built on FishNet with Steam transport via FishySteamworks. World items are replicated, players spawn on a deterministic 3x3 jittered grid, and remote players are smoothed with interpolation + light extrapolation.
- **Player health** - Server-authoritative HP, regen-after-delay, and fall damage validated server-side from owner-reported impacts.
- **UI Toolkit HUD** - Health/stamina bars, hotbar, viewfinder overlay, and a centered context prompt for interactions.

## Project setup

### Requirements

- **Unity 6** (`6000.4.2f1`) - install from Unity Hub. Other Unity 6 patch versions will likely work but are untested.
- **Steam client** running and logged in for local multiplayer testing (FishySteamworks uses Steam P2P).
- Git LFS is not currently required.

### Cloning and opening

```bash
git clone https://github.com/spencerboggs/urbex-simulator.git
cd urbex-simulator
```

Open the project folder in Unity Hub with Unity 6000.4.2f1. The first import will pull packages over the network (FishNet and Steamworks.NET are referenced as Git packages - see `Packages/manifest.json`).

Recommended entry point: open `Assets/Scenes/MainMenu.unity` and press Play.

### Steam app ID

`steam_appid.txt` ships with `480` (Valve's Spacewar test app). That's fine for local development - once you have your own Steam app ID, replace it in `steam_appid.txt`.

## Controls


| Action                        | Key             |
| ----------------------------- | --------------- |
| Move                          | `W` `A` `S` `D` |
| Sprint                        | `Left Shift`    |
| Jump                          | `Space`         |
| Look                          | Mouse           |
| Equip / stow camera (slot 0)  | `C`             |
| Select inventory slots 1–4    | `1` `2` `3` `4` |
| Pick up focused item          | `E`             |
| Drop held item                | `Q`             |
| Take photo (camera equipped)  | `Left Click`    |
| Zoom camera (camera equipped) | Scroll wheel    |


## Project layout

```
Assets/
  FishNet/              # FishNet networking + FishySteamworks transport
  Prefabs/              # TestPlayer, PlayerHUD, CameraViewfinderHUD, etc.
  Scenes/               # MainMenu, Lobby, World, TestWorld
  Scripts/
    Inventory/          # Item types, world items, flashlight visuals
    Input/              # Input binding helpers
    Multiplayer/        # NetworkSessionController, NetworkSceneFlow, PlayerLocalControls
    Photo/              # Handheld camera, gameplay capture, photo roll session
    Player/             # Movement, health, inventory, flashlight, climbing, look
    Steamworks.NET/     # Steam manager
    UI/                 # PlayerHUD, CameraViewfinderUI, lobby/main menu UI
  UI/                   # UXML/USS assets (PlayerHUD, CameraViewfinder)
  UI Toolkit/           # PanelSettings + theme
```

## Multiplayer

Hosting and joining is wired through `NetworkSessionController`:

- **Host** starts the FishNet server + client and loads the lobby.
- **Client** connects to a host by Steam ID (`steamId64`) when the Steam transport is active, or by `host[:port]` when a non-Steam transport is configured for local dev.
- Once the host moves to the gameplay scene, every connected player is spawned by `PlayerSpawner` using the gameplay player prefab. Player transforms replicate at ~20 Hz with interpolation on remote proxies.

Photos are written to the local filesystem under a per-match folder so each player keeps their own roll.

## Roadmap

- Replace placeholder art with proper urbex-themed models and environments.
- Rework crouch so it lowers the controller height without scaling the player root (currently disabled to avoid shrinking held items).
- Persistent world-item state for late joiners.
- More items beyond the camera and flashlight.
- Settings menu (sensitivity, FOV, audio).

## License

MIT