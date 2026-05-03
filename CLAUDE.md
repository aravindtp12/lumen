# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

PRISM — a 2D grid-based light-routing puzzle game (Sokoban + colored beams). Built with **Godot 4.6.2** + **C# / .NET 8**. The full design lives in [docs/PRISM_Design_Document_v2.docx](docs/PRISM_Design_Document_v2.docx); concept sketches in [sketches/](sketches/).

Main scene: `res://scenes/Game.tscn` → [scripts/Nodes/LevelScene.cs](scripts/Nodes/LevelScene.cs).

## Build / Run

Requires the Godot 4.6.2 .NET (Mono) editor and the .NET 8 SDK. The local Godot install is at `/Users/aravind/Downloads/Godot.app`.

```bash
# Build the C# assembly (needed any time C# changes — Godot won't pick up edits otherwise)
dotnet build PRISM.csproj

# Open the editor on this project
/Users/aravind/Downloads/Godot.app/Contents/MacOS/Godot --path .

# Run the game headless from the project root
/Users/aravind/Downloads/Godot.app/Contents/MacOS/Godot --path . scenes/Game.tscn
```

There is no test suite. The solver is pure (no Godot dependencies in `scripts/Core/BeamSolver.cs`), so it can be exercised from a console harness if one is added.

## Architecture

The codebase splits cleanly into **pure game logic** (no Godot types) and **Godot integration**. Keep that separation when adding features — anything in [scripts/Core/](scripts/Core/) and [scripts/Levels/](scripts/Levels/) (besides `LevelLoader`'s `ProjectSettings.GlobalizePath` call) should stay engine-agnostic.

### Layers

1. **Core data model** — [scripts/Core/](scripts/Core/)
   - [Grid.cs](scripts/Core/Grid.cs) — 2D `TileData[,]` plus id-indexed dictionaries for components, crates, pressure plates. `Clone()` is the basis of undo.
   - [GridTypes.cs](scripts/Core/GridTypes.cs), [Direction.cs](scripts/Core/Direction.cs), [BeamColor.cs](scripts/Core/BeamColor.cs) — enums and helpers. Directions are 8-way (N/S/E/W + diagonals); rotations are stored as **degrees in 45° steps** (0, 45, 90, …, 315).
   - [BeamSolver.cs](scripts/Core/BeamSolver.cs) — pure static solver, see below.
   - [GameState.cs](scripts/Core/GameState.cs) — owns the active `Grid`, undo stack (depth 100), `MovePlayer` / `Rotate` / `Undo` / `Reset`, fires `StateChanged` after every mutation. `Recompute()` runs the solver and updates receivers + crate illumination.

2. **Level format** — [scripts/Levels/](scripts/Levels/)
   - JSON files in [levels/world1/](levels/world1/) deserialised through [LevelData.cs](scripts/Levels/LevelData.cs) DTOs.
   - [LevelLoader.cs](scripts/Levels/LevelLoader.cs) reads via `res://` paths and builds a `Grid`. Enums are parsed case-insensitive; tiles default to `Floor`.

3. **Godot integration** — [scripts/Nodes/](scripts/Nodes/)
   - [LevelScene.cs](scripts/Nodes/LevelScene.cs) — root `Node2D`. Builds the scene graph in code (no `.tscn` for HUD), holds the `LevelPaths` catalogue, owns `GameState`, routes input, drives the renderer.
   - [WorldRenderer.cs](scripts/Nodes/WorldRenderer.cs) — single `Node2D` doing all drawing in `_Draw()`. Animates player/component motion (lerp), mirror rotation, doors, and beam pulse. `TileSize = 64`.

### BeamSolver — two-pass propagation

The solver is the trickiest piece; understand it before touching beam mechanics.

- **Pass 1 — propagation**: BFS-style queue seeded from every powered `Source`. Each ray is keyed by `(x, y, dir, color)` to dedupe. `Propagate` walks until it hits a wall, edge, crate, or component. `ApplyComponent` handles each component type and may `Enqueue` continuation rays (mirrors reflect via `MirrorTable`, splitters fork forward + perpendicular, prisms split white→RGB to forward/right/left, filters pass or subtract).
- **Pass 2 — intersection truncation**: any tile shared by ≥2 segments (except inside a `Mixer`) is an intersection. Each segment is truncated at its first intersection with `TerminationReason.Intersection`.
- Output: `SolverResult` carries `Segments`, `Intersections`, `ReceiverStates` (id → on/off), and `TileColors` (tile → set of colors passing through). The latter is what powers `ColorLight` plates and `ColorLocked` crate illumination.
- Receivers activate only when a non-truncated segment terminates *on* the receiver tile, with the matching color, entering the receiver's sensor face (the face opposite `comp.Facing`).
- `MaxSegments = 1000` is a safety cap against pathological loops.

### Player actions and pushing

`GameState.MovePlayer` first checks tile entry, then attempts `TryPush` if a component or crate occupies the destination. Components push if `Pushable`; crates push if not `ColorLocked`-and-unlit. Rotation steps are 45° (cw or ccw); only `Rotatable` components rotate. Pressure plates are evaluated after every action — `Weight` plates fire on player/crate/component on tile, `Light` / `ColorLight` plates are evaluated against `LastSolve.TileColors`. Plate activation toggles linked doors (`tile.DoorId == targetId`) and source power.

## Conventions and gotchas

- **Coordinates**: `(x, y)` with `y` increasing downward (north = `-y`). `DirectionHelper.ToVector` and `FromDegrees` encode this.
- **Rotation degrees**: only multiples of 45 are valid. `0° = N`, `90° = E`, `180° = S`, `270° = W`. The mirror table in [BeamSolver.cs](scripts/Core/BeamSolver.cs) only has entries for the rotations it supports — adding new mirror angles means adding rows there.
- **Component facing semantics differ by type**: `Source` emits in `Facing`; `Receiver`'s sensor face is `Facing.Opposite()`; `Splitter`/`Prism` accept input on `Facing.Opposite()`; `Mixer`'s output face is `Facing`.
- **Colors are additive RGB** with 7 valid combinations (R/G/B/Y/M/C/W). [BeamColorHelper](scripts/Core/BeamColor.cs) round-trips through `(bool r, bool g, bool b)`. Subtractive filters AND with the filter color.
- **Undo**: `Snapshot()` clones the entire grid before each successful action; cap is `MaxUndoDepth = 100`. Don't add mutations that bypass `Snapshot()` if they should be undoable.
- **Engine-agnostic core**: don't import `Godot` into `scripts/Core/` (current exception: `BeamColor.ToGodotColor` — keep that as the only one, or move it to a renderer-side helper if it grows).
- **Level catalogue is hardcoded** in `LevelScene.LevelPaths`. New levels must be added there as well as dropped into `levels/`. The catalogue currently references `level_03.json`, which does not exist on disk yet — pressing `N` past level 2 silently no-ops via the `index >= LevelPaths.Length` guard, but loading level 3 directly would throw.
- **Input**: WASD/Arrows move (with held-repeat at 0.22s delay / 0.10s rate), `R` / `Shift+R` rotate the component the player faces, `Z` undo, `Backspace` reset, `N` next level, `B` previous (debug).
