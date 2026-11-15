# Pamahiin — Next Steps & BarangayMap Audit Plan

## 1. Current Status & Audit Verdict (`BarangayMap`)

**Short answer:** `BarangayMap` is **close to feature-complete structurally, but not yet bug-free or ready to call done.**

Most gameplay systems in `Assets/Scenes/BarangayMap.unity` are present and wired (`NavMesh-NavhMesh.asset` baked at `29,752` bytes, `14` `NetworkDoor`s, `13` `HouseLightSwitch`es, `13` `GhostRoomMarker`s, `11` `GhostPropInteractable`s, `23` `GhostNode`s, `7` `HideZone`s, `2` `StairTeleporter`s + `StairNavMeshLink`, `FuseBox`, `RoofFader`s, `WeatherManager`, `ExorcismManager`, `GhostHandler`, `EquipmentSpawnPoint`, and `Assets/DefaultNetworkPrefabs.asset` registrations).

However, inspection of the codebase, `Assets/Resources/`, and Unity Console surfaced **verified missing assets, logic bugs, and asset/USS errors** that need to be resolved before sign-off.

---

## 2. Phase 1 — Fix Verified Bugs & Missing Assets (Priority)

### 2.1 Generate Missing Ritual & Ghost Death Resources (`Assets/Resources/`)
Commit `48c59e9` added the generator code (`Assets/Editor/BarangayAssetGenerator.cs`, `Assets/Editor/BarangayTextureImporter.cs`) and runtime loaders (`SummoningCircle.cs`, `HunterGhostBehavior.cs`, `ExorcismManager.cs`), **but the 3 generated runtime assets do not exist on disk**:

1. **`Assets/Resources/Sprites/Ritual/summoning_circle_sheet.png`**
   - Loaded by `SummoningCircle` (`Resources.Load<Texture2D>("Sprites/Ritual/summoning_circle_sheet")`).
   - *Current impact:* `ResolveFrames()` returns an empty array, so `PlayIgnitionSequence` skips the 9-frame / 5-candle lighting animation and jumps straight to `BurningLoop`.
2. **`Assets/Resources/Sprites/ghost_death_sheet.png`**
   - Loaded by `HunterGhostBehavior` (`Resources.Load<Texture2D>("Sprites/ghost_death_sheet")`).
   - *Current impact:* `ResolveDeathFrames()` returns an empty array and `DyingRoutine` skips the 12-frame pixel death dissolve animation.
3. **`Assets/Resources/Audio/ghost_dying_shriek.wav`**
   - Loaded by `ExorcismManager` (`GameAudioManager.GetClip("ghost_dying_shriek")`) and `HunterGhostBehavior` (`GameAudioManager.GetClip("ghost_dying_shriek") ?? CreateProceduralShriekClip()`).
   - *Current impact:* `HunterGhostBehavior` falls back to runtime procedural synthesis, whereas `ExorcismManager` receives `null` unless a `ghostDyingClip` override is assigned in Inspector.

**Action:**
- In Unity Editor, run **Pamahiin → Generate Ritual & Ghost Death Assets** (or **Pamahiin → Generate Barangay 16-Bit Assets** in `Assets/Editor/BarangayAssetGenerator.cs`).
- Verify all 3 files exist in `Assets/Resources/` and confirm `BarangayTextureImporter.cs` applied `FilterMode.Point`, uncompressed RGBA32, `isReadable = true`, and `pixelsPerUnit = 100` to both sprite sheets.

---

### 2.2 Fix `ShowIntermission` Argument Order on Match Exit
`LoadingScreenManager.ShowIntermission` (`Assets/Scripts/UI/LoadingScreenManager.cs`) has signature:
```csharp
public void ShowIntermission(string title, string subtitle, string tip = null, string tag = "PAMAHIIN // FIELD DISPATCH")
```
Two callers pass `"CASE DEBRIEFING"` as the **3rd positional argument (`tip`)** instead of named `tag: "CASE DEBRIEFING"`:
- `Assets/Scripts/GameMatchManager.cs` (`~lines 520-525`)
- `Assets/Scripts/TruckEndGameButton.cs` (`~lines 254-257`)

**Current impact:** Leaving `BarangayMap` displays `"CASE DEBRIEFING"` inside the `FIELD ADVISORY` tip box while leaving the top tag as `"PAMAHIIN // FIELD DISPATCH"`.

**Action:**
- Update both call sites to use named `tag: "CASE DEBRIEFING"` (and `tip: null` or a dedicated debriefing tip), matching `NetworkController.cs`.

---

### 2.3 Resolve Unity Console Errors & Warnings
Currently reported by Unity Console (`read_console`) and verified on disk:

1. **Out-of-bounds Sprite Rect Error (`NEWMAP_0`):**
   - `Assets/Sprites/MAP/NEWMAP.png` is `1336x752` on disk, while `Assets/Sprites/MAP/NEWMAP.png.meta` still defines a `1920x1080` sprite rect for `NEWMAP_0`. Neither `Assets/Sprites/MAP/NEWMAP.png` nor `Assets/NEWMAP.png` is referenced by any scene or prefab.
   - *Action:* Delete the unused legacy `NEWMAP.png` reference images (or fix `NEWMAP_0`'s rect in Sprite Editor to fit `1336x752` if keeping it for reference).
2. **Unsupported USS Properties in UI Toolkit:**
   - `Assets/UI/SpiritBox/SpiritBoxDialogue.uss`:
     - Line `7`: remove `picking-mode: ignore;` (invalid USS property) and add `picking-mode="Ignore"` on `<ui:VisualElement name="SpiritBoxOverlay" ...>` in `Assets/UI/SpiritBox/SpiritBoxDialogue.uxml`.
     - Lines `17`, `41`, `121`: remove `box-shadow` (unsupported in Unity USS; use border/alpha styling if glow is desired).
   - `Assets/UI/Exorcism/ExorcismUI.uss`:
     - Lines `43`, `192`: replace unsupported `gap: 16px;` / `gap: 12px;` with spacing margins on child elements (e.g., `margin-left` / `margin-right`).
3. **TextMesh Pro Legacy Importer Warning:**
   - `Assets/TextMesh Pro/Fonts/LiberationSans.ttf.meta` warns that `TrueTypeFontImporter` is version 2 (below minimum 4).
   - *Action:* Re-import / re-save `LiberationSans.ttf` in the Unity Inspector to upgrade the `.meta` serializer version.

---

## 3. Phase 2 — Runtime & Multiplayer Verification Checklist (`BarangayMap`)

Once Phase 1 fixes are applied, run a full Play Mode pass (Solo + 2-player Host & Client) in `LobbyScene` → `BarangayMap` → `LobbyScene` and verify every flow:

- [ ] **Lobby Dispatch & Loadout Sync:** Journal map/difficulty/loadout controls (`JournalController`, `ProgressionAndLoadoutManager`), party code join, `TRAVELING TO INVESTIGATION SITE` intermission bar, van gear spawning in Jeepney (`EquipmentSpawnPoint` / `TruckGearSpawner`).
- [ ] **Multi-Floor Navigation & Audio:**
  - `StairTeleporter` fade & arrival positions (`Stairwell_MainFloor_Down` ↔ `Stairwell_Basement_Up`).
  - `HunterGhostBehavior` pursuing players across floors (`StairNavMeshLink` + `TraverseOffMeshLinkRoutine` + `TryBeginStairFollow` without getting stuck on stairwell walls).
  - `GameAudioManager.IsBasementPosition` (`y < -3.2f`) floor attenuation for footsteps, EMF, Spirit Box, ghost footsteps, and weather.
- [ ] **House Systems & Hiding:**
  - All `14` `NetworkDoor`s (front door lock during hunts + `FrontDoorKeyItem` unlock), `13` `HouseLightSwitch`es, and `FuseBox` overload/ghost trip.
  - All `7` `HideZone`s crouch stealth during active hunt LOS checks.
  - All `11` `GhostPropInteractable`s levitation/throw/knockdown/break and upright interaction.
- [ ] **Evidence & Exorcism End-to-End:**
  - Evidence collection across randomly selected `GhostRoomMarker`s (including Basement): EMF 5, Freezing Temps, Ghost Writing, Spirit Box, D.O.T.S., and Ghost Orbs on `VideoCameraItem` / Jeepney CCTV (`TruckDashboardController` + `RoofFader` camera hide + night vision).
  - `CrucifixItem` hunt block & angered repel.
  - `RitualChalkItem` placement → standalone `SummoningCircle` (`[E]` ignite, `5`-candle animation, ghost manifestation) → `ExorcismManager` skill check win (`ghost_death_sheet` dissolve + `ghost_dying_shriek.wav`) and loss/wrong-ghost hunt escalation.
- [ ] **Evacuation & Debriefing Return:**
  - `TruckEndGameButton` two-stage Jeepney ramp close & departure, `CASE DEBRIEFING` loading screen tag/tip display, and `EndGameRevealController` payout + relief flashlight check back in `LobbyScene`.

---

## 4. Phase 3 — Polish & Backlog Decisions

1. **Resolve Legacy Map (`GameMapScene`) Status:**
   - `ProgressionAndLoadoutManager.AvailableMaps` still exposes index `1` (`Abandoned House (Original Prototype)` → `GameMapScene`), which lacks `WeatherManager`, `HideZone`, `GhostPropInteractable`, `StairTeleporter`, and a Journal blueprint (`JournalController` explicitly marks blueprints unavailable outside `BarangayMap`).
   - *Decision needed:* Either remove `GameMapScene` from `AvailableMaps` and Build Settings (matching the cleanup comments already in `ProgressionAndLoadoutManager.cs`) or schedule a full parity pass.
2. **Interactable Hover Tooltip Consistency:**
   - `PlayerController.UpdateItemHoverIndicator()` shows hover prompts for equipment, `SummoningCircle`, `FrontDoorKeyItem`, `TruckEndGameButton`, `HouseLightSwitch`, `FuseBox`, and `TruckDashboardController`, but omits `GhostPropInteractable`. Add an upright/interact hover label for knocked-down props if desired.
3. **UI Text & Visual Polish:**
   - Verify Spirit Box UI glyphs (`SpiritBoxDialogue.uxml`) render cleanly in runtime font fallback and balance SFX/thunder/shriek levels during multiplayer hunts.

---

## 5. Phase 4 — Next Feature Milestones (After `BarangayMap` Sign-Off)

1. **Second Complete Investigation Map:** Build a second Barangay/Filipino folklore venue with full `WeatherManager`, multi-floor/outdoor layout, Journal blueprint, and baked NavMesh.
2. **Contract & Progression Depth:** Optional side objectives (e.g., witness ghost event, repel with crucifix, clean up knocked props) for bonus payout, plus equipment tier unlocks/consumable balancing in `LobbyScene`.
3. **Release / QA Readiness:** Windows Standalone build verification, Unity Input System migration planning (replacing deprecated legacy `Input Manager` calls over time), and repository cleanup (unused `_Recovery/` scenes and orphaned sprites).
