# Pamahiin School Map Plan (`plan3.md`): 3-Floor Haunted School

## Overview
Build a 3-floor playable school map scene (`Assets/Scenes/SchoolMap.unity`) based strictly on the three uploaded floor-plan references (**Floor 1 → Floor 2 → Floor 3**), rendered in **Pure 90-Degree Top-Down 16-Bit SNES Pixel-Art Horror** style aligned with Pamahiin's existing orthographic pixel pipeline (`PixelScenePostProcessor`, `BarangayTextureImporter`, `URP 2D Light2D` + `ShadowCaster2D`).

Each floor shares the same symmetrical **H-shaped footprint** (`West Wing`, `Central Block` with North/South room rows split by a wide horizontal corridor, and `East Wing`), connected vertically by **4 corner stairwells (`NW`, `SW`, `NE`, `SE`)** on the inner courtyard/lawn-facing alcoves and flanked by **4 corner restrooms** along the outermost west and east wing walls on every story.

---

## 1. Visual Direction: Pure 90-Degree Top-Down 16-Bit SNES Pixel-Art Horror

### Camera & Projection Rules
- **True 90° Orthographic Top-Down:** Zero isometric tilt, zero 3/4 wall foreshortening, zero perspective vanishing lines. Walls are flat top-down structural caps (`0.18–0.24` world units thick) with crisp dark drop-edges and `ShadowCaster2D` occluders.
- **Pixel Density & Filtering:** `64 PPU` (or `32 PPU` snapped to the global pixel grid), `FilterMode.Point`, `TextureImporterCompression.Uncompressed`, `SpriteMeshType.FullRect`, matching `BarangayTextureImporter` and `PixelScenePostProcessor`.

### 16-Bit SNES Horror Palette Transformation
Convert the bright blue/white reference floor plan into a decayed, after-hours Philippine school at midnight while preserving the exact room layout and spatial readability:
- **Corridors & Floors:**
  - *Floor 1:* Grimy cracked institutional blue-slate & bone checkerboard tile (`#1C2430` / `#2A3342` with damp moss/scuff dither along baseboards).
  - *Floor 2:* Faded dusty slate concrete (`#232730`) with worn wood parquet in the **Library** & **Theater** (`#3E271A` / `#5A3926`) and red velvet seating accents (`#6E1E24`).
  - *Floor 3:* Aged linoleum & cold industrial tile (`#1D222B`) with raised anti-static server/comlab grid flooring (`#162228`).
- **Furniture & Props (16x16 / 32x32 / 64x32 Top-Down Sprites):**
  - Desks, chairs, teacher lecterns, chalkboards (top-down ledge + chalk tray), lockers, restroom stalls, workshop machinery, CRT/flat monitors in ComLabs, bookshelves, theater seats, Dean's mahogany desk.
  - Desaturated teal/cyan chair cushions (`#2B5B5E` / `#1E3F42`) to echo the reference layout while staying grounded in dark SNES horror contrast.
- **Horror Atmosphere:**
  - Near-pitch-black `Global Light 2D` (`#07090F`) so players rely on flashlight cones, flickering ceiling tube `Light2D`s (controlled by per-room `HouseLightSwitch` + `FuseBox`), emergency exit glow, and CRT monitor hum in Floor 3 ComLabs.
  - Environmental storytelling: overturned chairs, scattered test papers, chalk dust, flickering fluorescent strips, blood/ectoplasm smudges near stair landings, cracked restroom tiles.

---

## 2. Multi-Floor World Architecture & Navigation

### World-Space Layout of the 3 Floors
In Unity 2D top-down, stack the three H-shaped floor plates cleanly along the Y-axis with generous void separation (`~28–35` world units between floor centers) so `RoofFader` bounds, `Light2D` bleed, and spatial audio never overlap across stories:
- **Floor 1 (Ground Floor + Exterior Courtyard & Jeepney Staging):** Centered around `Y = 0`. Surrounded by the exterior **North Courtyard** (concrete plaza + grass strip + 3 trees, including 1 dead gnarled tree), **South Lawn**, perimeter school fence/gate, and the team's **Jeepney / Van** parked outside the north approach.
- **Floor 2 (Second Floor):** Centered around `Y = -36` (or `+36`; keeping exterior ground at `Y = 0` and upper stories stacked neatly below/above outside camera bounds).
- **Floor 3 (Third Floor):** Centered around `Y = -72`.
- **Burial Pit Stage (`BurialMapStage`):** Placed far off-bounds (e.g., `X = 80, Y = 80`) so victim death cutscenes remain isolated.

### Vertical Connectivity (4 Stairwells Across 3 Stories)
Unlike `BarangayMap`'s single `Main Floor <-> Basement` stair pair, the school has **4 stairwell shafts (`NW`, `SW`, `NE`, `SE`)** spanning 3 floors, all anchored at the **exact corner coordinates of the Floor 1 layout** (inner courtyard/lawn-facing alcoves, directly beside the outermost `Restroom`s) so players can build a reliable mental map across stories:
- **Reuse Existing Stair Assets (`BarangayMap`):**
  - Reuse `Assets/Resources/Sprites/BarangayMap/House/stairwell_up.png` (96×32 mahogany treads with brass rails & teal up-arrow) and `Assets/Resources/Sprites/BarangayMap/House/stairwell_down.png` (96×32 mahogany treads with brass rails & gold down-arrow), plus `Assets/Resources/Audio/stair_footsteps.wav` directly for **all** school staircases.
  - **Orientation & Visual Clarity:** Rotate each `96×32` flight (`0° / ±90° / 180°`) so the **entry landing** faces the open corridor doorway and the **shadowed end** meets the alcove back wall. Add a dim stairwell emergency `Light2D` in each alcove so players can spot the `Up` (teal) vs `Down` (gold) arrows in the dark.
- **Alcove Geometry & Anti-Accidental-Trigger Layout:**
  - **Recessed Triggers:** Keep each `StairTeleporter` `BoxCollider2D` (`isTrigger = true`) recessed inside the stair treads (not overhanging into the main hallway) so players walking past a stairwell in the corridor never trigger an accidental floor transition.
  - **Floor 1 (`NW`, `SW`, `NE`, `SE`):** Single-flight alcove placing `stairwell_up` (`→ 2nd Floor`) with `ArrivalFromF2` in the clear corridor-side landing.
  - **Floor 2 (`NW`, `SW`, `NE`, `SE`):** Dual-flight alcove at the identical local X/Y coordinates, with a **shared corridor-side landing vestibule** splitting into two parallel lanes separated by a thin **central balustrade/divider (`BoxCollider2D` + `ShadowCaster2D`)**: `stairwell_down` (`→ 1st Floor`) on one side and `stairwell_up` (`→ 3rd Floor`) on the other. This physical divider prevents players or ghosts from clipping the wrong flight when entering or arriving.
  - **Floor 3 (`NW`, `SW`, `NE`, `SE`):** Single-flight alcove placing `stairwell_down` (`→ 2nd Floor`) with `ArrivalFromF2` in the clear corridor-side landing.
- **Player Traversal (`StairTeleporter`):**
  - Connects **8 bidirectional `StairTeleporter` pairs** (16 primary teleporters total; `MidStairTeleporterNode` backup colliders are auto-generated with `isBackupNode = true` and `NavMeshModifier.ignoreFromBuild = true`):
    - `Floor 1 ↔ Floor 2` (`NW_F1↔F2`, `SW_F1↔F2`, `NE_F1↔F2`, `SE_F1↔F2`) — **4 bidirectional pairs**.
    - `Floor 2 ↔ Floor 3` (`NW_F2↔F3`, `SW_F2↔F3`, `NE_F2↔F3`, `SE_F2↔F3`) — **4 bidirectional pairs**.
  - **Safe Arrival Spawns:** Place every `ArrivalFrom*` transform in the clear landing outside both trigger colliders (`>= 0.35` world units clear of trigger edges and walls) so `StairTeleporter.arrivalCooldownSeconds` (`1.25s`) and spatial separation prevent ping-pong re-triggers.
  - Update `StairTeleporter.destinationFloorLabel` to show `"1st Floor"`, `"2nd Floor"`, `"3rd Floor"`.
- **Ghost Navigation (`NavMeshLink` & `HunterGhostBehavior`):**
  - Keep walkable NavMesh extending right up to the mouth of each flight (within `<= 0.6` world units of each `StairTeleporter` center) with `NavMeshModifier.ignoreFromBuild = true` on the stair rects themselves, so `HunterGhostBehavior.UpdateStairFollow()`'s `<= 1.0f` arrival check never stalls against a divider or wall.
  - Add **8 bidirectional `NavMeshLink`s** (`StairNavMeshLink_<Corner>_<F1F2|F2F3>`) connecting each pair's `ArrivalFrom*` landing markers (`bidirectional = true`, `autoUpdate = true`, `costModifier = -1`), and generalize `StairNavLinkAutoSync` (or add a multi-link sync pass in `SchoolMapSceneBuilder.cs`) so `HunterGhostBehavior` can patrol and pursue seamlessly across all 3 floors.
- **Floor-Aware Systems Refactor (`FloorZone`):**
  - Currently `GameAudioManager.IsBasementPosition(pos)` checks `pos.y < -3.2f`, assuming only 2 floors.
  - Introduce a reusable `FloorZone` component (`int floorIndex`: `0 = Exterior`, `1 = Floor 1`, `2 = Floor 2`, `3 = Floor 3`) attached to each floor plate's trigger bounds:
    - `GameAudioManager`: Attenuate footsteps/doors/ghost SFX whenever `GetFloorIndex(soundPos) != GetFloorIndex(listenerPos)` (fallback to `BASEMENT_Y_THRESHOLD` when no `FloorZone` exists so `BarangayMap` stays untouched).
    - `EventGhostBehavior`: Restrict prop throws to the same `FloorZone` as the ghost.
    - `GhostHandler` & `HunterGhostBehavior`: Treat all `FloorZone`s + `RoofFader`s with `enclosedBuilding = true` as valid indoor investigation areas.
    - `WeatherManager`: Mask outdoor rain particles and fog clouds inside Floor 1, Floor 2, and Floor 3 `RoofFader` bounds so rain never falls inside upper stories.

---

## 3. Room-by-Room Map Blueprint (Ordered Floor 1 → Floor 3)

### Floor 1 — Ground Floor (Image 1)
- **Exterior Courtyard & Entrances (`entry`)**:
  - **North Courtyard:** Paved school walkway, grass planter strip, 3 top-down trees (2 dark olive canopies, 1 withered black tree), dim flickering courtyard lamp post, Jeepney staging outside north gate.
  - **South Lawn:** Enclosed overgrown grass yard between SW and SE wings.
  - **4 Ground Entrances (`entry`):**
    1. `Entry_NW_Outer` (north tip of NW lobby — primary front entry)
    2. `Entry_NW_Courtyard` (east wall of NW lobby opening to North Courtyard)
    3. `Entry_NE_Outer` (north tip of NE lobby)
    4. `Entry_NE_Courtyard` (west wall of NE lobby opening to North Courtyard)
    *(All 4 exterior entry doors lock automatically during a Ghost Hunt via `NetworkDoor.IsFrontDoor = true`.)*
- **West Wing (Floor 1)**:
  - `F1_Restroom_NW` (`Restroom`): 2 toilet stalls (`HideZone` enabled on stall doors), 2 sinks, cracked tile floor.
  - `F1_Stairs_NW` (`staiirs`): NW stairwell lobby leading up to Floor 2.
  - `F1_Classroom_W1` (`class room` — upper west): Teacher desk + student tables/chairs + chalkboard.
  - `F1_Classroom_W2` (`class room` — lower west): Teacher desk + student tables/chairs + chalkboard.
  - `F1_Restroom_SW` (`Restroom`): 2 toilet stalls (`HideZone`), sinks.
  - `F1_Stairs_SW` (`staiirs`): SW stairwell alcove leading up to Floor 2.
- **Central Block — North Row (Floor 1, `class rooms`)**:
  - `F1_Classroom_N1` through `F1_Classroom_N5`: 5 side-by-side partitioned classrooms with 2x3 teal student desk clusters, front chalkboards, and `GhostRoomMarker`s.
  - `F1_Storage_NE` (unlabeled dark closet/alcove on the far east end of the North Row): Narrow supply closet (`HideZone`).
- **Central Block — South Row (Floor 1, left to right)**:
  - `F1_Workshop` (`WORKSHOP`): Large industrial/TLE workshop with stained concrete/blue tile, heavy workbenches, vise/saw props, sink, and **Main `FuseBox`** location.
  - `F1_Classroom_S1` (`class room`): Drafting/study classroom with 2 vertical worktables and 8 chairs.
  - `F1_DeansOffice` (`Deans office`): 3-part administrative suite:
    - West dark inner office (filing cabinets + desk + `HideZone` wardrobe/closet)
    - Central reception & Dean's desk area (long counter + 2 front desks)
    - East dark annex office (side desk + records shelf)
  - `F1_Classroom_S2` (`class room`): Standard lecture room with 4x4 grid of student desks.
- **East Wing (Floor 1)**:
  - `F1_Restroom_NE` (`Restroom`): 2 toilet stalls (`HideZone`), sinks.
  - `F1_Stairs_NE` (`staiirs`): NE stairwell alcove leading up to Floor 2.
  - `F1_LockerAlcove` (`locker`): Recessed locker bay on the west side of the NE corridor just south of `Entry_NE_Courtyard` (with openable/hiding lockers).
  - `F1_Classroom_E1` (`class room`): Long vertical classroom along the east wall with dual rows of wall-facing desks/seats.
  - `F1_Restroom_SE` (`Restroom`): 2 toilet stalls (`HideZone`), sinks.
  - `F1_Stairs_SE` (`staiirs`): SE stairwell alcove leading up to Floor 2.

---

### Floor 2 — Second Floor (Image 2)
- **West Wing (Floor 2)**:
  - `F2_Restroom_NW` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F2_Stairs_NW` (`staiirs`): Split landing (`Down → F1`, `Up → F3`).
  - `F2_Classroom_W` (`class room`): Long West Wing classroom divided into 2–3 bays with desks and wall boards.
  - `F2_Restroom_SW` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F2_Stairs_SW` (`staiirs`): Split landing (`Down → F1`, `Up → F3`).
- **Central Block — North Row (Floor 2, `class rooms`)**:
  - `F2_Classroom_N1` through `F2_Classroom_N4`: 4 partitioned classrooms along the upper corridor (varying desk counts + dark shadowed corners).
- **Central Block — South Row (Floor 2, left to right)**:
  - `F2_Theater_Backstage` (unlabeled dark alcove at far left): Dark storage/backstage vestibule beside the theater.
  - `F2_MultipurposeHall` (`multpurpose hall/theatrer`): Large auditorium/theater room with raised wooden stage/podium on the west side and rows of teal theater seating (`GhostPropInteractable` chairs + ambient eerie stage spotlight).
  - `F2_LibraryDiscussion` (`Library discussion room,`): Narrow study room separating Theater and Library, furnished with 2 red armchairs, study table, and glass/wood partition.
  - `F2_Library` (`liibrary`): Large wood-floored library with L-shaped librarian checkout counter, 3 tall parallel bookshelf stacks (acting as line-of-sight blockers & stealth loop paths during hunts), and reading tables/chairs.
- **East Wing (Floor 2)**:
  - `F2_Restroom_NE` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F2_Stairs_NE` (`staiirs`): Split landing (`Down → F1`, `Up → F3`).
  - `F2_Classroom_E1` & `F2_Classroom_E2` (`class room`): 2 partitioned classrooms along the east wall.
  - `F2_Restroom_SE` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F2_Stairs_SE` (`staiirs`): Split landing (`Down → F1`, `Up → F3`).

---

### Floor 3 — Third Floor (Image 3)
- **West Wing (Floor 3)**:
  - `F3_Restroom_NW` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F3_Stairs_NW` (`staiirs`): Top landing (`Down → F2`).
  - `F3_JanitorCloset_NW` (unlabeled small dark square room south of `F3_Stairs_NW`): Tight utility closet (`HideZone`).
  - `F3_Classroom_W` (`class room`): Long West Wing classroom strip with desks/chairs.
  - `F3_Restroom_SW` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F3_Stairs_SW` (`staiirs`): Top landing (`Down → F2`).
- **Central Block — North Row (Floor 3, `comlabs`)**:
  - `F3_ComLab_1` (`comlabs` — west): Instructor desk + 2 rows × 5 computer workstations (10 terminals).
  - `F3_ComLab_2` (`comlabs` — center): Instructor desk + 2 rows × 7 computer workstations (14 terminals).
  - `F3_ComLab_3` (`comlabs` — east): Instructor desk + 2 rows × 7 computer workstations (14 terminals).
  *(Horror touch: occasional flickering CRT screens when power is ON, and subtle EMF interference near server racks/monitors only when ghost interacts with terminals!)*
- **Central Block — South Row (Floor 3, left to right)**:
  - `F3_Utility_SW_Upper` & `F3_Utility_SW_Lower` (2 stacked unlabeled dark rooms on far left): Locked/interactable electrical & archive closets (`HideZone` / secondary FuseBox breaker panel option).
  - `F3_Classroom_S1`, `F3_Classroom_S2`, `F3_Classroom_S3` (`class room`): 3 partitioned lecture/lab classrooms with demonstration tables, whiteboards, and rear cabinets.
  - `F3_Classroom_S4` (`class room`): Classroom/lab featuring a U-shaped reception/demo counter.
  - `F3_ControlSuite_SE` (unlabeled dark multi-partitioned room on far right): Partitioned dark AV/Server/Security booth maze with narrow doorways (high-tension stealth/looping room).
- **East Wing (Floor 3)**:
  - `F3_Restroom_NE` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F3_Stairs_NE` (`staiirs`): Top landing (`Down → F2`).
  - `F3_Classroom_E` (`class room`): Long East Wing classroom strip with wall-aligned desks.
  - `F3_Restroom_SE` (`Restroom`): 2 stalls (`HideZone`), sinks.
  - `F3_Stairs_SE` (`staiirs`): Top landing (`Down → F2`).

---

## 4. 16-Bit SNES Pixel-Art Asset Pipeline (`SchoolAssetGenerator.cs`)

Create `Assets/Editor/SchoolAssetGenerator.cs` generating crisp `FilterMode.Point` sprites under `Assets/Resources/Sprites/SchoolMap/`:
1. **Floor Tiles (`Floors/`):**
   - `tile_hall_f1_checker.png` (stained blue-slate checkerboard)
   - `tile_hall_f2_f3_concrete.png` (cracked institutional slate concrete)
   - `tile_classroom_grid.png` (scuffed linoleum grid)
   - `tile_restroom_mosaic.png` (grimy dark restroom tile)
   - `tile_library_wood.png` (aged dark mahogany planks)
   - `tile_workshop_concrete.png` (oil-stained concrete)
   - `tile_comlab_raised.png` (server/computer lab tile)
   - *Stairs (Reused):* Load `BarangayMap/House/stairwell_up` & `BarangayMap/House/stairwell_down` directly (no duplicate stair sprite needed)
   - `tile_courtyard_paving.png` & `tile_grass_lawn.png` (exterior plaza & dead/overgrown grass)
2. **Walls & Doors (`Walls/`):**
   - `wall_school_plaster.png`, `wall_school_trim_cap.png` (top-down wall segments with crisp `#100C0E` outlines)
   - `door_school_wood.png`, `door_metal_exit.png`, `door_stall.png`, `locker_door.png`
3. **School Horror Props & Furniture (`Furniture/`, `Props/`):**
   - `desk_student.png`, `desk_teacher.png`, `desk_dean_mahogany.png`, `desk_u_counter.png`
   - `chair_teal.png`, `chair_theater_red.png`, `bench_workshop.png`
   - `pc_workstation_off.png`, `pc_workstation_flicker_sheet.png` (2-frame glow)
   - `bookshelf_topdown.png`, `locker_bank.png`, `toilet_stall_topdown.png`, `sink_unit.png`
   - `chalkboard_ledge.png`, `tree_canopy_sheet.png`, `tree_withered.png`
   - `prop_*_sheet.png` (animated 16-bit disturbance frames for chairs, books, keyboards, mugs, papers, tools — compatible with `GhostPropInteractable`).

---

## 5. Scene Builder, Blueprint & Code Integration

### Files to Create / Update
1. **`Assets/Editor/SchoolAssetGenerator.cs`** *(New)*: Procedural 16-bit SNES horror sprite generator + `SchoolTextureImporter` integration for `FilterMode.Point`.
2. **`Assets/Editor/SchoolMapSceneBuilder.cs`** *(New)*:
   - Menu item `Pamahiin/Build SchoolMap Scene`.
   - Clones core managers, `Van` (Jeepney), `Ghost Manager`, `SoundSystem`, and `UI` from `BarangayMap.unity` / `GameMapScene.unity`.
   - Builds Floor 1 (`Y = 0`), Floor 2 (`Y = -36`), Floor 3 (`Y = -72`) from structured `RoomSpec` tables matching the exact room grid in Section 3.
   - Places `NetworkDoor`s with solid `BoxCollider2D` + `ShadowCaster2D`, `HouseLightSwitch` + `Light2D` in every room, `HideZone`s in restrooms/closets/lockers/under theater & office desks, `GhostRoomMarker` on every valid hauntable room (~35+ rooms across 3 floors; excluding stairwells & exterior), `GhostNode` graph across all hallways & rooms, and bakes 2D `NavMesh` + 8 bidirectional stair `NavMeshLink`s.
   - Registers `Assets/Scenes/SchoolMap.unity` in `EditorBuildSettings`.
3. **`Assets/Editor/SchoolBlueprintGenerator.cs`** *(New)*:
   - Generates dark mahogany & brass Journal schematic PNGs (`blueprint_school_f1.png`, `blueprint_school_f2.png`, `blueprint_school_f3.png`) directly from `SchoolMapSceneBuilder` coordinates so players can inspect all 3 floors in the Journal.
4. **`Assets/Scripts/FloorZone.cs`** *(New)* & Multi-Floor Hooks:
   - Add `FloorZone` (`int floorIndex`) + `FloorZone.GetFloorIndex(Vector3 pos)` helper.
   - Update `GameAudioManager.cs`, `EventGhostBehavior.cs`, `GhostHandler.cs`, `HunterGhostBehavior.cs`, and `WeatherManager.cs` to consult `FloorZone` when present (preserving `BASEMENT_Y_THRESHOLD` fallback for `BarangayMap`).
5. **`Assets/Scripts/ProgressionAndLoadoutManager.cs`** *(Update)*:
   - Add `"San Nicolas High School (3 Floors)"` (`SCHOOL_MAP_SCENE_NAME = "SchoolMap"`) to `AvailableMaps` and `GetSelectedSceneName()` / `IsGameplayMapScene()`.

---

## 6. Verification Checklist
- [ ] **Layout Fidelity:** Every labeled room (`Restroom`, `staiirs`, `entry`, `locker`, `class room` / `class rooms`, `WORKSHOP`, `Deans office`, `multpurpose hall/theatrer`, `Library discussion room,`, `liibrary`, `comlabs`) and unlabeled alcove matches Floors 1–3 from the reference images.
- [ ] **Art Style:** All floor, wall, prop, and exterior sprites use `FilterMode.Point`, 90° top-down orthographic projection, crisp `#100C0E` outlines, and dark 16-bit SNES horror lighting with `ShadowCaster2D` walls.
- [ ] **3-Floor Traversal:** All 8 `StairTeleporter` pairs (`F1↔F2` and `F2↔F3` at `NW`, `SW`, `NE`, `SE`) teleport players cleanly without soft-locks, and `NavMeshLink`s allow `HunterGhostBehavior` to follow/roam across all 3 floors.
- [ ] **Floor Isolation:** Audio attenuation (`GameAudioManager`), prop throws (`EventGhostBehavior`), weather masking (`WeatherManager`), and `RoofFader` fade strictly per floor without cross-floor bleed.
- [ ] **Ghost & Gameplay Loop:** `GhostHandler` selects a valid `GhostRoomMarker` on any of the 3 floors, all 4 exterior `entry` doors lock during hunts, `FuseBox` toggles interior lights across floors, and `BurialMapStage` triggers cleanly on player death.
