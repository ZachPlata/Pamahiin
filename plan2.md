# Plan: D.O.T.S. Projector Walking Green Dots Silhouette Overhaul

## 1. Goals & Scope

Replace the current D.O.T.S. manifestation behavior—where `EventGhostBehavior` turns the physical `EventGhost` entity visible at 50% opacity and dashes it across the room—with a self-contained **walking green-dots silhouette sprite-sheet animation** rendered and managed by [DotsProjectorItem.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/DotsProjectorItem.cs).

### Primary Goals
1. **Never Reveal `EventGhost` for D.O.T.S.:** Keep `EventGhost` completely hidden (`isVisiblyManifested = false`, `SpriteRenderer.enabled = false`) and physically undisturbed during D.O.T.S. evidence triggers.
2. **Literal Green Dots Silhouette of `ZombieLabcoat1.png`:** Create a dedicated multi-frame walking sprite sheet asset derived directly from the silhouette of [ZombieLabcoat1.png](file:///d:/Github%20Projects/Pamahiin/Assets/Sprites/CHARACTERS/ZombieLabcoat1.png), composed strictly of discrete glowing green laser dots on transparent gaps (no solid fills, no black borders, no skin/orange/blood colors).
3. **Sprite Sheet Walking Animation Inside the Beam:** Animate the green-dots silhouette walking across the powered D.O.T.S. projector's illuminated cone (`DOTSLayer`), oriented smoothly along its walk vector, matching BarangayMap house/character world scale, and synced cleanly across host and clients.
4. **Preserve & Fix Equipment Contracts:** Keep existing evidence gating (`GhostHandler.HasEvidence(EvidenceType.DOTS)` / `IsEvidenceAllowed(EvidenceType.DOTS)`), Developer Console testing (`ForceDotsBtn`), `ParanormalManager` event registration, dead-player guards, placement/pickup rules, and clean cancellation on power-off or state transitions.

### Out of Scope
- Changing full ghost events, hunt visuals, or `HunterGhost` behavior.
- Replacing `ghost_idle_0.png` / `ghost_walk_0.png` used by `GhostPixelAnimator` for actual `EventGhost` / `HunterGhost` manifestations (those remain handled independently).

---

## 2. Current Implementation & Root Causes

Inspection of the current scripts, prefabs, scenes, and sprite assets reveals why the existing D.O.T.S. behavior exposes `EventGhost` and has visual/scale mismatches:

1. **D.O.T.S. Reveals & Teleports the `EventGhost` Entity:**
   - In [EventGhostBehavior.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/EventGhostBehavior.cs), `GhostEventType.DotsManifestation` (lines 228–249, 403–425, 513–549, 668–686) finds an active `DotsProjectorItem`, sets `isVisiblyManifested.Value = true`, moves/teleports `EventGhost` through a dash (`dotsDashSpeed`), and fires `TriggerDotsDashClientRpc` / `EndDotsDashClientRpc`.
   - `UpdateVisuals(bool visible)` (lines 130–149) enables `EventGhost`'s `ghostSprite` (`SpriteRenderer`) at `alpha = isDashing ? 0.5f : 1f`, while `GhostPixelAnimator` ([PixelSpriteAnimator.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/PixelSpriteAnimator.cs)) drives `ghost_idle_0` / `ghost_walk_0` (which are static copies of `ZombieLabcoat1.png`).
   - Because `GhostHandler.PushGhostInfoToParanormalManager` (line 232) binds `eventGhost.transform`, and `EventGhost` in [BarangayMap.unity](file:///d:/Github%20Projects/Pamahiin/Assets/Scenes/BarangayMap.unity) has a non-trigger `CircleCollider2D` (`radius: 0.2`, `m_IsTrigger: 0` at line 60703), dashing `EventGhost` moves the physical ghost entity into the beam instead of projecting an ethereal laser-dot silhouette.

2. **Unused & Off-Spec `Dots Silhouette.png` Asset:**
   - [Dots Silhouette.png](file:///d:/Github%20Projects/Pamahiin/Assets/Sprites/CHARACTERS/Dots%20Silhouette.png) (`64×64`, `guid: 067d6cf38306d524697dcac4808e6f9b`) exists in the repository but is completely unreferenced by any script, prefab, or scene.
   - Visually, it is a single static upside-down green-tinted recolor of `ZombieLabcoat1.png` with solid fill, black borders, and orange hands still intact—not an upright `+Y`-aligned discrete green-dots silhouette and not a walking sprite sheet.

3. **Extreme Root Scale & Axis Orientation on `DotsProjector.prefab`:**
   - [DotsProjector.prefab](file:///d:/Github%20Projects/Pamahiin/Assets/Prefabs/DotsProjector.prefab) has root `localScale = (0.0065, 0.0065, 0.0065)` (and [TruckGearSpawner.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/TruckGearSpawner.cs) calls `eq.SetSyncedScale(...)` at spawn). Any child `SpriteRenderer` added naively under the projector root shrinks by `0.0065x`.
   - The projector sprite and child `Spot Light 2D` (`localPos = (3.07, 0, 0)`, `localRot = 270°`, `localScale = (-0.078337, -0.078337, -0.078337)`) are authored pointing along local **`+X`** (`transform.right`), which is why [DotsProjectorItem.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/DotsProjectorItem.cs) uses `HeldRotationOffset => Quaternion.Euler(0f, 0f, 90f)` (line 50) to align `+X` with player forward (`+Y`).

4. **Unused `CircleCollider2D` & Uncalibrated Range in `DotsProjectorItem.cs`:**
   - `DotsProjectorItem.Awake()` adds/configures `CircleCollider2D triggerCollider` with `radius = projectionRadius` (`3f`), nothing consumes its trigger events, and [PlayerController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/PlayerController.cs) (line 974) explicitly ignores colliders `!= equipment.InteractCollider` (`PolygonCollider2D`).
   - Meanwhile, `BarangayMap` rooms are roughly `0.66 × 0.56` (`MasterCloset`) to `2.80 × 1.70` (`Basement`) world units ([BarangayMapSceneBuilder.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Editor/BarangayMapSceneBuilder.cs)), so `projectionRadius = 3f` (3 world units) spans multiple rooms/walls unless calibrated to the actual room & beam scale (~`0.6`–`0.9` world units).

---

## 3. Art Direction, Sprite Sheet Asset & Animation Pipeline

### 3.1 Source Silhouette Reference (`ZombieLabcoat1.png`)
From inspected dimensions and pixel bounds of [ZombieLabcoat1.png](file:///d:/Github%20Projects/Pamahiin/Assets/Sprites/CHARACTERS/ZombieLabcoat1.png):
- **Dimensions:** `64×64` px PNG (`100` PPU; `ZombieLabcoat1_0` sub-rect `(11, 17, 37, 41)`).
- **Silhouette Bounding Box:** Opaque pixels span `x = [17..46]` (`width = 30 px`) and `y = [8..45]` (`height = 38 px`, measured from PNG top).
- **Orientation & Top-Down Form:** Upright character facing **`+Y`** (top of sprite) with rounded rear shoulders/labcoat U-body, dark head centered inside the lower U-bend, and two outstretched arms pointing toward **`+Y`**.

### 3.2 New Sprite Sheet Spec (`ghost_dots_walk_sheet.png`)
Create a new 8-frame horizontal sprite sheet derived directly from `ZombieLabcoat1.png`:
- **Asset Path:** `Assets/Resources/Sprites/CHARACTERS/ghost_dots_walk_sheet.png`
  - Placed under `Resources` so both prefab/Animator `PPtr` asset references and runtime fallback (`Resources.Load` / `Resources.LoadAll<Sprite>`) work reliably in builds and tests.
- **Deprecate Unused Asset:** Remove or archive [Dots Silhouette.png](file:///d:/Github%20Projects/Pamahiin/Assets/Sprites/CHARACTERS/Dots%20Silhouette.png) (currently unreferenced) once `ghost_dots_walk_sheet.png` is generated so there is only one authoritative D.O.T.S. silhouette texture.
- **Sheet Geometry:**
  - Total size: `512 × 64` px (`8` columns × `1` row, `64 × 64` px per frame).
  - Pivot: Center `(0.5, 0.5)` so arbitrary walk-direction Z rotations stay centered on the torso/head axis.
  - Facing: **Strictly `+Y`** (matching `ZombieLabcoat1.png`, never upside-down).
- **Literal Green Dots Silhouette Rendering Rules:**
  - **100% Dot Matrix Silhouette:** Sample pixels strictly within each frame's `ZombieLabcoat1` top-down silhouette mask using a crisp staggered/blue-noise D.O.T.S. laser lattice (`1×1 px` dots and sparse `2×2 px` laser clusters separated by `1–2 px` transparent `alpha = 0` gaps so the floor/projection surface reads through the figure).
  - **Zero Solid Borders or Non-DOTS Colors:** Strip all black outlines, white coat shading, blood/grey/orange skin tones.
  - **Emerald/Laser Green Palette:**
    - Bright core laser dots (head crown, upper shoulders, leading arms): `#9BFF9E` / `#4CFF64` (`alpha 230–255`)
    - Mid-density body/labcoat lattice dots: `#1FE03B` (`alpha 185–225`)
    - Peripheral edge/stride scatter dots: `#0E9B24` (`alpha 110–160`)
    - Gaps between dots: `(0, 0, 0, 0)` transparent.
- **8-Frame Top-Down Walking Animation Cycle (Facing `+Y`):**
  - **Frames 0–1 (Left Stride Reach):** Left outstretched arm advances `+2 px` toward `+Y`, right arm pulls `-2 px` back; shoulders yaw subtly counter-clockwise (`~-4°`) with right rear labcoat edge trailing slightly.
  - **Frames 2–3 (Mid-Stride Passing L→R):** Passes cleanly through the canonical `ZombieLabcoat1.png` silhouette proportions with subtle head bob and shifted laser-dot phase.
  - **Frames 4–5 (Right Stride Reach):** Mirror of Frames 0–1—right arm advances `+2 px` toward `+Y`, left arm pulls `-2 px` back; shoulders yaw subtly clockwise (`~+4°`) with left rear coat edge trailing.
  - **Frames 6–7 (Mid-Stride Passing R→L):** Smooth transition back toward Frame 0 so the 8-frame cycle loops seamlessly.
  - **Laser Scan Shimmer:** Shift the internal dot-lattice phase by `1 px` on alternating frames so the silhouette looks alive with projected laser dots sweeping across a walking figure even on a per-pixel grid.

### 3.3 Texture Importer, Clip & Controller Generation
1. **`Assets/Editor/BarangayTextureImporter.cs`:**
   - Extend `TryGetSlicedSheetSpec(...)` (line 51) with a spec for `ghost_dots_walk_sheet.png`:
     - `SpriteImportMode.Multiple`, grid `8 × 1` (`64 × 64` px frames: `ghost_dots_walk_sheet_0` .. `ghost_dots_walk_sheet_7`).
     - `pixelsPerUnit = 100f`, `filterMode = FilterMode.Point`, `textureCompression = TextureImporterCompression.Uncompressed`, `RGBA32`, mipmaps off, `spriteAlignment = Center`.
2. **`Assets/Editor/BarangayAssetGenerator.cs`:**
   - Reuse `CreateOrUpdateSpriteClip(...)` (line 2521) and `CreateOrUpdateController(...)` (line 2563) to generate:
     - `Assets/Resources/Animations/DotsSilhouetteWalk.anim` (`LoopTime = true`, `10` fps -> `0.8s` loop, `PPtrCurve` targeting `SpriteRenderer.m_Sprite` through frames `0..7`).
     - `Assets/Resources/Animations/DotsSilhouetteAnim.controller` (single default looping state `DotsWalk` referencing `DotsSilhouetteWalk.anim`).

---

## 4. Architecture: `DotsProjectorItem.cs` & `EventGhostBehavior.cs`

### 4.1 Ownership & High-Level Flow
Instead of making `EventGhost` visible and dashing its GameObject through the room, **`DotsProjectorItem` becomes the sole owner of D.O.T.S. silhouette rendering**, while **`EventGhostBehavior` acts purely as the server-side ghost AI event scheduler** (selecting when D.O.T.S. occurs and picking an eligible powered projector):

1. **Trigger Decision (Server):**
   - When `EventGhostBehavior` evaluates or forces `GhostEventType.DotsManifestation` (via natural `ChooseAndExecuteEvent` or `DeveloperConsoleManager`'s `ForceDotsBtn`):
     - Verify `!ghostHandler.IsExorcised`, `ghostHandler.HasEvidence(EvidenceType.DOTS)`, and `ghostHandler.IsEvidenceAllowed(EvidenceType.DOTS)`.
     - Find an eligible active `DotsProjectorItem` from `EquipmentItem.AllEquipment` where:
       - `dots.IsPoweredOn`
       - `(dots.IsPlaced || dots.IsOnGround || dots.IsInHand) && !dots.IsHeldByDeadPlayer()`
       - For normal gameplay (`!ignoreRequirements`): projector is inside or overlapping `ghostHandler.ghostroomMarker` (using `ContainsPoint` + beam-range check toward room center/sample point so a projector placed just at a doorway aiming inward also works cleanly).
   - **No `EventGhost` Visual/Movement Mutation:**
     - **Do NOT** set `isVisiblyManifested.Value = true`.
     - **Do NOT** set `isDashing = true`, modify `EventGhost.transform.position`, or trigger `TriggerDotsDashClientRpc` / `EndDotsDashClientRpc` on `EventGhostBehavior`.
   - Call `selectedProjector.TriggerDotsProjectionOnServer(...)`, and keep `ParanormalManager.Instance?.RegisterEvent(projectionPoint, 2, 5f)` so ghost activity meters still react realistically.

2. **Silhouette Walk Playback (Synchronized via `DotsProjectorItem`):**
   - `DotsProjectorItem` computes a world-space walk segment `(startWorldPos, endWorldPos)` inside its active forward cone (using projector origin, beam direction `dotsLight != null ? dotsLight.transform.up : transform.right`, calibrated beam radius, and `coneAngle`, optionally clipped inside `ghostHandler.ghostroomMarker` bounds).
   - Broadcast via `PlayDotsSilhouetteClientRpc(Vector2 startPos, Vector2 endPos, float duration)` (plus lightweight sequence token or synced projection state so late callers/cancels remain coherent).
   - Each client activates its dedicated `DotsSilhouetteVisual` helper object, plays `DotsSilhouetteAnim.controller` (`DotsWalk`, plus a deterministic `10 fps` sprite-array fallback if controller/animator is missing), interpolates across `startPos -> endPos` over `duration` (~`1.5s–2.2s`), fades alpha smoothly in/out at entry/exit (`0.25s` ramp + cone boundary attenuation), and deactivates cleanly at completion.

### 4.2 Dedicated Silhouette Child Object, Scale & Orientation Math
Because [DotsProjector.prefab](file:///d:/Github%20Projects/Pamahiin/Assets/Prefabs/DotsProjector.prefab) has `localScale = (0.0065, 0.0065, 0.0065)` (and runtime `SetSyncedScale` support in `EquipmentItem.cs`):

1. **Prefab Hierarchy (`DotsProjector.prefab`):**
   - Add child `DotsSilhouetteVisual` (disabled by default) containing:
     - `SpriteRenderer`: default sprite `ghost_dots_walk_sheet_0`, `SortingLayer = "DOTSLayer"`, `sortingOrder = 5` (renders cleanly above `Floor`, `Item`, and corpses, below `Wall`/`Roof`, matching [TagManager.asset](file:///d:/Github%20Projects/Pamahiin/ProjectSettings/TagManager.asset) and [Renderer2D.asset](file:///d:/Github%20Projects/Pamahiin/Assets/Settings/Renderer2D.asset)).
     - `Animator`: controller `DotsSilhouetteAnim`.
     - Wire `[SerializeField] private SpriteRenderer silhouetteRenderer` and `[SerializeField] private Animator silhouetteAnimator` on `DotsProjectorItem.cs` (with defensive runtime creation fallback in `Awake()` if an older prefab instance lacks the child).

2. **World Scale Compensation (Matching `EventGhost` & `Player` Size):**
   - In [Ghost Handler.prefab](file:///d:/Github%20Projects/Pamahiin/Assets/Prefabs/Ghost%20Handler.prefab), `EventGhost` uses scale `~0.28` (`0.27977845`), giving `~0.104 × 0.115` world units; [Player.prefab](file:///d:/Github%20Projects/Pamahiin/Assets/Prefabs/Player.prefab) is `~0.1078 × 0.0787` world units.
   - With `64×64` frames at `100 PPU` (where the `30×38 px` silhouette spans `0.30 × 0.38` units), a target world scale of `silhouetteWorldScale = 0.28f` produces a `~0.084 × 0.106` world-unit ghost silhouette—matching `EventGhost` and `Player` proportions accurately inside `BarangayMap` rooms.
   - On every active `LateUpdate()` (so any `SetSyncedScale` or parent held rotation is compensated continuously):
     ```csharp
     Vector3 parentScale = transform.lossyScale;
     float sx = Mathf.Abs(parentScale.x) > 1e-5f ? silhouetteWorldScale / parentScale.x : silhouetteWorldScale;
     float sy = Mathf.Abs(parentScale.y) > 1e-5f ? silhouetteWorldScale / parentScale.y : silhouetteWorldScale;
     silhouetteTransform.localScale = new Vector3(sx, sy, 1f);
     ```

3. **World Rotation Alignment (`+Y` Upright Sprite -> Walk Direction):**
   - Because `ghost_dots_walk_sheet` faces **`+Y`** (top of sprite), align its world Z rotation strictly to its walk direction vector `dir = (endPos - startPos).normalized`:
     ```csharp
     float walkAngleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
     silhouetteTransform.rotation = Quaternion.Euler(0f, 0f, walkAngleDeg);
     ```
   - This keeps the silhouette walking forward in `dir` regardless of whether the `DotsProjectorItem` itself is rotated `90°` in a player's hand or angled diagonally on a floor.

### 4.3 Beam Geometry, Lighting Visibility & Cancellation Safety
1. **Beam Forward & Range Calibration:**
   - Default `projectionRadius` should be calibrated down from `3f` to house/room scale (e.g. `~0.75f` world units, `minWalkOffset = 0.18f`..`0.65f` along beam cone, `coneAngle = 60f`), ensuring silhouettes walk convincingly inside rooms like `MasterCloset` (`0.66 × 0.56`), `BoysBedroom` (`0.92 × 1.15`), and `FoyerHall` (`2.48 × 0.48`) without spawning behind far exterior walls.
   - Compute beam forward reliably from `(Vector2)(dotsLight != null ? dotsLight.transform.up : transform.right).normalized`.
2. **Dark Room / CCTV / Spectator Visibility:**
   - Assign an unlit sprite material (or shader configured not to black out in unlit rooms) to `silhouetteRenderer` while modulating `silhouetteRenderer.color` alpha smoothly via:
     - Entry/exit fade curve (`t` near `0` and `1`)
     - Subtle shimmer coupled to `dotsLight.intensity` (the existing 16-bit stepped shimmer in `DotsProjectorItem.Update()`)
     - Cone containment factor so dots smoothly appear inside the green projector beam and never render as an opaque cutout floating in pitch darkness outside the cone.
   - Because `SortingLayer = "DOTSLayer"` is included in `Renderer2D` and visible to `MainCamera`, `VideoCameraItem` night vision / truck CCTV feeds, and dead spectators, every viewer sees the projected green dots identically.
3. **Strict Cancellation Guards:**
   - Cancel active silhouette playback immediately (and fire `CancelDotsSilhouetteClientRpc()` when triggered from server) if any of the following occur:
     - `isPoweredOn.Value` becomes `false` (player turns off projector)
     - `IsHeldByDeadPlayer()` becomes `true` or holder dies
     - Item is stowed in non-active hand / hidden (`!isInHand && !IsOnGround && !IsPlaced`) or despawned (`OnNetworkDespawn` / `OnDisable`)
     - `GhostHandler.CurrentState` leaves `Dormant` (e.g. Hunt starts) or `IsExorcised` becomes `true`.
4. **Remove Broken Runtime `CircleCollider2D`:**
   - Remove the unused `triggerCollider` (`CircleCollider2D`) creation and toggling from `DotsProjectorItem.cs` (`Awake()` / `UpdateVisuals()`). `EquipmentItem`'s existing `PolygonCollider2D` remains the sole `InteractCollider`.

---

## 5. File-by-File Execution Plan

| Order | File / Asset | Action | Summary of Changes |
| :---: | :--- | :--- | :--- |
| 1 | `Assets/Resources/Sprites/CHARACTERS/ghost_dots_walk_sheet.png` | **Create** | Generate `512×64` (8 frames × `64×64`) sprite sheet derived from [ZombieLabcoat1.png](file:///d:/Github%20Projects/Pamahiin/Assets/Sprites/CHARACTERS/ZombieLabcoat1.png) silhouette: upright `+Y` facing, top-down 8-frame arm/shoulder/coat walking cycle rendered strictly as discrete neon/emerald green laser dots with transparent gaps. |
| 2 | `Assets/Sprites/CHARACTERS/Dots Silhouette.png` (+ `.meta`) | **Delete / Deprecate** | Remove unused legacy upside-down solid recolor asset so only `ghost_dots_walk_sheet.png` is referenced. |
| 3 | [BarangayTextureImporter.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Editor/BarangayTextureImporter.cs) | **Modify** | Add `ghost_dots_walk_sheet.png` slice definition in `TryGetSlicedSheetSpec` (`SpriteImportMode.Multiple`, `8×1`, `64×64`, `Point`, `RGBA32`, `Uncompressed`, center pivot, `100 PPU`). |
| 4 | [BarangayAssetGenerator.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Editor/BarangayAssetGenerator.cs) & `.anim` / `.controller` | **Modify + Generate** | Add generator support & emit `Assets/Resources/Animations/DotsSilhouetteWalk.anim` (`10 fps` looping `SpriteRenderer.m_Sprite` `PPtrCurve`) and `Assets/Resources/Animations/DotsSilhouetteAnim.controller`. |
| 5 | [DotsProjectorItem.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/DotsProjectorItem.cs) | **Modify** | Remove unused `CircleCollider2D` logic; calibrate `projectionRadius`; add `DotsSilhouetteVisual` `SpriteRenderer` + `Animator` ownership, scale compensation (`0.0065` -> `~0.28` world scale), walk-direction rotation alignment, `TriggerDotsProjectionOnServer` / `PlayDotsSilhouetteClientRpc` / `CancelDotsSilhouetteClientRpc`, fallback sprite-frame stepping, fade/shimmer envelope, and strict power/stow/death cancellation. |
| 6 | [DotsProjector.prefab](file:///d:/Github%20Projects/Pamahiin/Assets/Prefabs/DotsProjector.prefab) | **Modify** | Add `DotsSilhouetteVisual` child (`SpriteRenderer` on `DOTSLayer`, `order = 5`, `Animator` with `DotsSilhouetteAnim.controller`, disabled initially); wire serialized fields & calibrated `projectionRadius` on `DotsProjectorItem`. |
| 7 | [EventGhostBehavior.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/EventGhostBehavior.cs) | **Modify** | Refactor `DotsManifestation` execution (`ExecuteEvent`, `ChooseAndExecuteEvent`, `TriggerDotsDashClientRpc`, `EndDotsDashClientRpc`, `UpdateVisuals`) so `EventGhost` stays completely invisible and does not physically dash; instead validate `DOTS` evidence/room, select an active `DotsProjectorItem`, call `dots.TriggerDotsProjectionOnServer(...)`, and cancel projection if a hunt/exorcism interrupts. Verify [DeveloperConsoleManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/DeveloperConsoleManager.cs) `ForceDotsBtn` flows cleanly through this path. |
| 8 | `Assets/Tests/EditMode/DotsProjectorSilhouetteTests.cs` | **Create** | Add EditMode tests validating texture slicing (`8` sprites), `.anim` frame count/loop, scale compensation against `0.0065` root scale, walk-angle rotation math, cancellation on `UpdateVisuals()` / power-off / dead holder, and `EventGhostBehavior` remaining invisible during `DotsManifestation`. |

---

## 6. Verification Plan

1. **Sprite Sheet & Animation Asset Verification (Editor):**
   - Confirm `ghost_dots_walk_sheet.png` imports cleanly via `BarangayTextureImporter` as `512×64` with `8` slices (`64×64`), `FilterMode.Point`, `RGBA32`, `Uncompressed`, upright `+Y` orientation, and transparent gaps between green dots matching `ZombieLabcoat1.png`.
   - Confirm `DotsSilhouetteWalk.anim` (`LoopTime = true`) references all `8` sub-sprites in order (`0..7`) and `DotsSilhouetteAnim.controller` plays cleanly in preview.
2. **Automated EditMode Tests (`unityMCP:run_tests`):**
   - Run the new `DotsProjectorSilhouetteTests` alongside existing tests (including [PlayerDeathSpectatorTests.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Tests/EditMode/PlayerDeathSpectatorTests.cs)):
     - Silhouette world scale stays consistent (`~0.28`) when parented to `0.0065` `DotsProjector` root or scaled via `SetSyncedScale`.
     - Silhouette world rotation aligns `+Y` sprite axis to walk direction vector (`Atan2(y, x) * Rad2Deg - 90`).
     - Turning power off, stowing, or holding as a dead player immediately disables `DotsSilhouetteVisual`.
     - Triggering `DotsManifestation` never sets `EventGhost` `SpriteRenderer.enabled = true` or `isVisiblyManifested = true`.
3. **In-Scene PlayMode, Dev Console & Multiplayer Checks (`BarangayMap.unity`):**
   - **Direct Dev Console Test (`ForceDotsBtn`):** Spawn `DotsProjector.prefab`, place on floor & hold in hand facing a dark room, turn ON, trigger `ForceDotsBtn` -> observe walking green-dots silhouette inside the green beam cone while `EventGhost` stays completely invisible (verify in Hierarchy/Scene View that `EventGhost`'s `SpriteRenderer` remains disabled and `EventGhost` does not teleport into the projector).
   - **Non-DOTS Evidence Ghost Check:** Set ghost type without `EvidenceType.DOTS` -> verify `DotsManifestation` never triggers the silhouette; `ForceDotsBtn` respects dev console behavior cleanly without showing `EventGhost`.
   - **Cancellation Mid-Walk:** Toggle projector OFF, pick up / stow projector, or kill the holding player mid-silhouette walk -> silhouette vanishes immediately without lingering frame artifacts.
   - **Client / CCTV / Spectator Visibility:** Test Host + Client sync (client sees the silhouette at identical world scale/angle/position), Truck CCTV feed (`VideoCameraItem`), and dead spectator view.
