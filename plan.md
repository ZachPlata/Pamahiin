# Pamahiin Bug Fixes & Hunting Behavior Overhaul Plan

## Overview
This plan covers six bug fixes and feature overhauls across doors, the summoning circle, chase atmosphere, HUD/tooltip UI, and Phasmophobia-aligned hunting mechanics (adapted to Pamahiin's 2D top-down scale and 20-ghost folklore roster):

1. **Door `openRef` Solid Collider & Mid-Animation Pass-Through** (`NetworkDoor.cs`)
2. **Summoning Circle Single-Player Exorcism Lock** (`SummoningCircle.cs`, `ExorcismManager.cs`)
3. **Ghost Chase Effect: Camera Zoom Pulse + Vignette Pulse** (`GhostAtmosphereController.cs`)
4. **Tutorial Tooltip Text Overflow Fix** (`HUD.uss`)
5. **Inventory Hotbar Text Font Size & Wrapping Fix** (`HUD.uss`)
6. **Phasmophobia-Style Hunting Behavior, Crucifix Rules & 20-Ghost Sanity Thresholds** (`GhostCatalog.cs`, `GhostHandler.cs`, `HunterGhostBehavior.cs`, `CrucifixItem.cs`, `NetworkDoor.cs`)

---

## 1. Door `openRef` Solid Collider & Mid-Animation Pass-Through

### Problem
In [NetworkDoor.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/NetworkDoor.cs):
- `CopyCollider(closedDoorRef.gameObject, runtimeDoorObj)` copies the collider from `closedDoorRef` onto `runtimeDoorObj`.
- When `disableObstacleColliderWhenOpen` is enabled, `ApplyProgress(float t)` disables `runtimeCollider` for `t >= 0.2f`, allowing players to walk straight through the open door panel at `openDoorRef` (`t == 1f`).
- Conversely, when `disableObstacleColliderWhenOpen` is disabled, `runtimeCollider` stays solid *during* the swing/lerp animation (`0 < t < 1`), causing players to snag on or be pushed by the moving door leaf.

### Plan
1. **Ensure Solid `BoxCollider2D` on `runtimeDoorObj` & `openDoorRef` Support (`NetworkDoor.cs`):**
   - In `InitializeFromReferences()` / `CopyCollider()`, guarantee `runtimeDoorObj` has a solid (`isTrigger = false`) `BoxCollider2D` (`runtimeCollider`).
   - Prefer `closedDoorRef` (or `openDoorRef` if `closedDoorRef` has no collider, or auto-generate a `BoxCollider2D` from `runtimeRenderer.sprite.bounds.size` as fallback) so the door panel always has a solid physical box collider matching the door sprite.
   - If `openDoorRef` has a distinct `BoxCollider2D` size/offset, optionally interpolate or swap to `openDoorRef`'s box dimensions at `t >= 0.98f` (and `closedDoorRef`'s at `t <= 0.02f`).
2. **Pass-Through ONLY During Opening/Closing Animation (`ApplyProgress` in `NetworkDoor.cs`):**
   - Replace the `disableObstacleColliderWhenOpen` check (`t < 0.2f`) with an explicit animation-window rule:
     - **At rest (Closed `t <= 0.02f` OR Open `t >= 0.98f`):** `runtimeCollider.enabled = true` (solid — players cannot pass through either the closed door or the open door panel at `openRef`).
     - **Mid-animation (`0.02f < t < 0.98f`):** `runtimeCollider.enabled = false` (players can pass smoothly through while the door is actively opening or closing).
   - **Overlap-Safe Re-Enable:** When `t` reaches `0f` or `1f`, if a living player's collider is currently overlapping `runtimeCollider`, temporarily ignore collision (`Physics2D.IgnoreCollision`) for that specific player until they exit the door leaf's bounds rather than trapping or teleporting them inside the collider.
   - Keep `interactionTriggerCollider.enabled = allowPlayerInteract` at all times so players can always open/close the door, and preserve `ApplyLocalSpectatorBypass` so dead spectators still walk through all doors.

---

## 2. Summoning Circle Single-Player Exorcism Lock

### Problem
In [SummoningCircle.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/SummoningCircle.cs#L579-L598) and [ExorcismManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/ExorcismManager.cs#L219-L309):
- `InitiateRitualClientRpc` broadcasts with `[Rpc(SendTo.ClientsAndHost)]`, opening `ghostSelectPopup` for **every** connected client simultaneously.
- Client interactions (`!IsServer`) run `InitiateRitualLocal` locally without locking on the server, allowing two players to interact and run/complete the exorcism twice.

### Plan
1. **Server-Authoritative Ritual Ownership (`ExorcismManager.cs`):**
   - Add networked state on `ExorcismManager`:
     - `NetworkVariable<ulong> activeRitualClientId` (default `ulong.MaxValue`, server-write).
     - `NetworkVariable<bool> syncedExorcismActive` (server-write).
     - `NetworkVariable<bool> syncedExorcismCompleted` (server-write).
   - Route `SummoningCircle.Interact()` through a `RequestInitiateRitualServerRpc(Vector3 diagramPos)` (or local call if offline).
   - On the server:
     - Reject if `syncedExorcismCompleted.Value`, `syncedExorcismActive.Value`, or `activeRitualClientId.Value != ulong.MaxValue`.
     - Verify the requesting player (`rpcParams.Receive.SenderClientId`) is alive (`IsAlive`) and within reach of the summoning circle.
     - Set `activeRitualClientId.Value = senderClientId` and `syncedExorcismActive.Value = true`.
     - Open `ghostSelectPopup` **only** on the initiating client using `RpcTarget.Single(senderClientId, RpcTargetUse.Temp)` (via `OpenGhostSelectClientRpc(diagramPos, RpcTarget.Single(...))`).
2. **Single-Player Minigame & Server-Validated Outcomes (`ExorcismManager.cs`):**
   - When the owning player clicks a ghost in `OnGhostSelected(string ghostName)`, send `SubmitGhostSelectionServerRpc(string ghostName)` to the server.
   - Server validates `senderClientId == activeRitualClientId.Value`, broadcasts the candle ignition animation (`PlayIgnitionSequence`) to all clients so everyone sees the circle light up, and starts the circular skill-check minigame (`exorcismOverlay`) **only** on the owning client.
   - When the minigame finishes (`EvaluateOutcome`), send `SubmitExorcismOutcomeServerRpc(bool minigamePassed, string guessedGhostName)` to the server.
   - Server validates `senderClientId == activeRitualClientId.Value && !syncedExorcismCompleted.Value` so exorcism success/failure can execute **at most once**:
     - **Success:** Set `syncedExorcismCompleted.Value = true`, `syncedExorcismActive.Value = false`, `activeRitualClientId.Value = ulong.MaxValue`, and trigger `TriggerSuccessClientRpc`.
     - **Failure:** Set `syncedExorcismActive.Value = false`, `activeRitualClientId.Value = ulong.MaxValue`, and trigger `TriggerFailureClientRpc` + `ghostHandler.StartHunt(true)`.
   - **Disconnect / Death Safeguard:** If the owning player disconnects or dies (`!IsAlive`) while holding the ritual lock before the minigame outcome is submitted, the server automatically releases `activeRitualClientId` and closes their popup so the circle does not soft-lock.
3. **Prompt Sync (`SummoningCircle.cs`):**
   - Update `GetInteractText()` and `Interact()` to check `syncedExorcismActive` / `syncedExorcismCompleted` so non-owning players immediately see `"Exorcism in Progress"` or `"Exorcism Completed"`.

---

## 3. Ghost Chasing Effect: Camera Zoom Pulse + Vignette Pulse

### Problem
In [GhostAtmosphereController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/GhostAtmosphereController.cs#L215-L225), `UpdateVignette` pulses `vignette.intensity.value` with proximity, while `UpdateCameraZoom` applies a static zoom-in (`baseOrthoSize - chaseZoomAmount * proximity`) instead of pulsing in and out.

### Plan
1. **Sync Camera Zoom In-and-Out Pulse with Chase Proximity (`GhostAtmosphereController.cs`):**
   - Keep the proximity-scaled pulsing vignette in `UpdateVignette`.
   - Update `UpdateCameraZoom(bool chaseActive, float proximity)`:
     - Expose `[SerializeField] private float chaseZoomMin = 0.06f;` and `[SerializeField] private float chaseZoomMax = 0.26f;` (orthographic units, replacing the single static `chaseZoomAmount` or keeping `chaseZoomAmount` as the amplitude).
     - When `chaseActive && proximity > 0f`, compute `float pulseSine = (Mathf.Sin(pulseTimer) + 1f) * 0.5f;` using the same `pulseTimer` cadence as the vignette/heartbeat, and compute:
       `float zoomOffset = Mathf.Lerp(chaseZoomMin, chaseZoomMax, pulseSine) * proximity;`
       `float targetSize = baseOrthoSize - zoomOffset;`
     - Lerp `mainCamera.orthographicSize` smoothly toward `targetSize` (with faster tracking during chase, e.g., `zoomLerpSpeed = 6f`, so the in/out breathing zoom is clearly visible at close range) and smoothly restore `baseOrthoSize` when the chase ends or in `OnPlayerCaught()`.

---

## 4. Tutorial Tooltip Text Overflow Fix

### Problem
As seen in the HUD screenshot (`EQUIPMENT: FLASHLIGHT` clipping past the right border of the toast card):
In [HUD.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uss#L302-L366):
- `.toast-header-row` uses `flex-direction: row;` without wrapping, and `.toast-title` lacks `flex-shrink: 1; min-width: 0;`.
- `.toast-key-badge` (`[L-CLICK] / [R-CLICK]`) pushes `.toast-title` past the `500px` `.tutorial-toast` border, and `.tutorial-toast` does not enforce `overflow: hidden`.

### Plan
1. **Constrain Toast Layout & Enable Clean Wrapping (`Assets/UI/HUD/HUD.uss`):**
   - `.tutorial-toast`: add `overflow: hidden;` and `max-width: 92%;` (with `flex-shrink: 1;`).
   - `.toast-header-row`: add `flex-wrap: wrap; width: 100%; align-items: center;`.
   - `.toast-key-badge`: add `flex-shrink: 0; margin-bottom: 4px;` (or keep spacing clean when wrapped onto a second line).
   - `.toast-title`: add `flex-shrink: 1; flex-grow: 1; min-width: 0; max-width: 100%; white-space: normal; font-size: 10px;` so long headers like `EQUIPMENT: FLASHLIGHT` or `EQUIPMENT: WRITING BOOK` never exceed the right border and wrap cleanly inside the card.
   - `.toast-desc`: add `width: 100%; flex-shrink: 1; min-width: 0; white-space: normal;` so body text always wraps inside the box.
2. **Verify All `TutorialTooltipManager.Catalog` Entries:**
   - Test the longest badge + title combinations (`Flashlight`, `WritingBook`, `GhostHunt`, `Movement`) at 1920x1080 and 1280x720 to confirm zero clipping.

---

## 5. Inventory Hotbar Text Font Size & Wrapping Fix

### Problem
In [HUD.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uss#L55-L113):
- `.inventory-slot` has `width: 98px; padding: 4px;` (effective text width ~`84px`), and `.inventory-slot-text` uses `font-size: 8px;` with the `PressStart2P` pixel font.
- `"Flashlight"` (10 characters) exceeds the slot width at `8px`, wrapping the final `'t'` onto a second line (`Flashligh` / `t`).

### Plan
1. **Adjust Slot Text Size & Slot Width (`Assets/UI/HUD/HUD.uss`):**
   - Reduce `.inventory-slot-text` `font-size` from `8px` to `6.5px` (or `7px`) and `line-height: 10px;`, with `max-width: 100%; overflow: hidden;`.
   - Slightly widen `.inventory-slot` from `98px` to `110px` (optional/minor `12px` increase that keeps the 3-slot hotbar compact while giving 11-character single words like `Flashlight` and `Thermometer` comfortable horizontal breathing room).
2. **Verify All 10 Catalog Item Names (`ProgressionAndLoadoutManager.Catalog`):**
   - Confirm none of `Flashlight`, `EMF Reader`, `Thermometer`, `Spirit Box`, `Ghost Writing Book`, `D.O.T.S. Projector`, `Video Camera`, `Crucifix`, `Sanity Pills`, `Ritual Chalk` split mid-word across lines.

---

## 6. Phasmophobia-Style Hunting Behavior & 20-Ghost Thresholds

### Pamahiin World-Scale Distance Calibration
Pamahiin's 2D top-down map uses smaller world units than Phasmophobia's 3D meters (`PlayerController.walkSpeed = 3f`, `maxReachRadius = 2.5f`, `HunterGhostBehavior.lineOfSightDistance = 8f`, `doorOpenReach = 2.0f`, `killDistance = 0.3f`, `CrucifixItem.blockRadius = 4f`, cabinet proximity `1.5f`). All Phasmophobia distances are recalibrated and exposed as `[SerializeField]` fields:

| Mechanic | Phasmophobia Reference | Pamahiin Calibrated Value | Where Configured |
|---|---|---|---|
| LoS Distance | ~20m | `8.0f` (existing) / `12.0f` chasing | `HunterGhostBehavior.lineOfSightDistance` |
| Active Equipment / Spirit Box Detection Radius | ~7.5m–10m | `9.0f` units | `HunterGhostBehavior.electronicDetectionRadius` |
| Door Open/Close Hearing Radius (non-cabinet) | Close vicinity | `5.5f` units | `HunterGhostBehavior.doorNoiseHearRadius` |
| Multi-Player LoS Close Override Distance | 2.0m | `2.0f` units | `HunterGhostBehavior.closeLoSOverrideDistance` |
| Opened Room Door Hiding Check | 1.5m from door | `1.3f` units from door | `HunterGhostBehavior.openedDoorHideCheckRadius` |
| Hiding Spot Waypoint Stand-Off & Force Open | Outside door / ~2m | `0.6f` outside / `2.0f` reach (`doorOpenReach`) | `HunterGhostBehavior` |
| Crucifix Hunt Block Radius | 3m–5m | `4.0f` (existing `blockRadius`) or inside `ghostroomMarker` | `CrucifixItem.blockRadius` |

### 6A. Regular Hunt Requirements & Team Sanity (`GhostHandler.cs`)
1. **Automatic Hunt Attempt Loop (`GhostHandler.Update` on Server):**
   - Once `hasActivated` is true, `!IsExorcised`, `!IsInSetupGracePeriod`, `currentState == GhostHandlerState.Dormant`, and `Time.time >= nextHuntAttemptTime`:
     - Poll for a hunt attempt every `huntCheckInterval` (e.g. `5f`–`8f` seconds, and enforce a `25f` second post-hunt / post-crucifix cooldown).
2. **Investigation Area Requirement:**
   - Helper `IsPlayerInInvestigationArea(PlayerController p)`: returns true if `p.IsAlive` and (`GameAudioManager.IsBasementPosition(p.transform.position)` OR any enclosed `RoofFader.IsPositionInside(p.transform.position)`).
   - Count living players inside the investigation area.
   - **If 0 living players are inside the investigation area:** abort the hunt attempt and instead trigger/refresh the **Favorite Room state** (anchor `eventGhost` inside `ghostroomMarker` and optionally trigger a minor favorite-room interaction/wander, without locking doors or starting a hunt).
3. **Living Team Average Sanity:**
   - Helper `GetLivingTeamAverageSanity()`: averages `p.Sanity` across **all living players** (`PlayerController.AllPlayers` where `p != null && p.IsAlive`), **regardless of whether they are inside or outside the investigation area**. Dead players (`!p.IsAlive`) are excluded.
   - For single-target ghosts (e.g., **Kaperosa**), check only the designated living obsession target's sanity (falling back to a new living target if the current target dies).
4. **Threshold & Ability Evaluation:**
   - Evaluate the ghost's effective hunt sanity threshold from `GhostCatalog` (including dynamic conditions such as lights on/off in `ghostroomMarker`, nearby player movement/sprinting, solo vs grouped players, or special ability rolls).
   - If effective team/target sanity `<= threshold` (or a special ability hunt procs), call `StartHunt(false)`.

### 6B. 20 Pamahiin Folklore Ghosts — Hunt Sanity Thresholds (For Review & Customization)
Please review and adjust any threshold percentages or conditions below before implementation. Each entry aligns with the ghost's existing folklore traits in [GhostCatalog.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/GhostCatalog.cs):

| # | Ghost Name | Condition | Hunt Sanity Threshold (%) | Special Hunt / Ability Notes |
|---|---|---|---|---|
| 1 | **Tikbalang** | Nearby player moving / sprinting | **65%** | Provoked by movement (similar to Dayan/Kormos); **45%** when nearby players stand still. |
| 2 | **Kapre** | Player inside favorite room / standard | **60%** (in ghost room) / **50%** (outside) | Territorial; drains nearby sanity faster while dormant. |
| 3 | **Manananggal** | Always / Rare Early-Hunt Ability | **70%** (Ability: **N/A**) | Aggressive hunter with higher chase speed; rare ability can initiate a hunt at any sanity after setup grace. |
| 4 | **Multo** | Always | **50%** | Standard baseline ghost behavior. |
| 5 | **Kaperosa** | Always | **50%** | **Only checks its chosen obsession target's sanity** instead of living team average (switches target if target dies). |
| 6 | **Pasatsat** | Always | **50%** | Standard threshold; higher chance to close/block interior doors near hallways. |
| 7 | **Kalag** | Solo vs Grouped in Ghost Room | **55%** (solo) / **35%** (2+ players in ghost room) | Shy around groups; refuses to hunt if multiple players are inside the ghost room together. |
| 8 | **Kumakatok** | Door recently opened/closed nearby | **60%** | Threshold rises to **60%** for 15s after a player interacts with an interior door; **45%** otherwise. |
| 9 | **Akop** | Only 1 living player inside house | **60%** | **40%** when 2+ living players are inside the house together (targets isolated investigators). |
| 10 | **Ibwa** | Always | **50%** | Standard threshold; higher prop-disruption frequency before hunting. |
| 11 | **Paring Pugot** | Ritual Chalk / Summoning Circle placed | **65%** | **50%** before any Summoning Circle is placed; **65%** once a circle exists in the house. |
| 12 | **Amalanhig** | Always | **60%** | Relentless foot-pursuer; standard speed ramp with sustained LoS. |
| 13 | **Santelmo** | Ghost room lights OFF or Fuse Box off | **60%** | **40%** when lights in the ghost's current room are ON (regardless of fuse box state if room light is lit). |
| 14 | **Bagat** | Nearby player sprinting | **65%** | **50%** when nearby players are walking/crouching/still. |
| 15 | **Mantiw** | Always | **40%** | Patient, low-threshold stalker. |
| 16 | **Batibat** | Ghost room lights OFF / ON | **60%** (lights OFF) / **40%** (lights ON) | Encourages keeping room lights active as noted in its Journal tip. |
| 17 | **Tiyanak** | Always | **65%** | Aggressive ambush predator. |
| 18 | **Tamawo** | Always | **40%** | Shy nature spirit; low hunt threshold. |
| 19 | **Mangkukutod** | Always / Post-failed ritual | **55%** (**70%** after failed exorcism) | Elevated aggression once disturbed by ritual interference. |
| 20 | **Tagamaling** | Shifting Lunar Phase (Calm vs Bloodthirsty) | **35%** (Calm) / **65%** (Bloodthirsty) | Alternates phases every 45–60s between calm dormancy and aggressive nocturnal hunts. |

*(Implementation note: Stored cleanly via a `GhostHuntProfile` struct on each `GhostCatalog.GhostDefinition` so thresholds can be edited in one place.)*

### 6C. Crucifix Hunt Prevention (`CrucifixItem.cs` & `GhostHandler.cs`)
- **1 Use per Crucifix + 25s Cooldown (All Ghosts):**
  - When `GhostHandler.StartHunt(false)` runs, check placed, unburned `CrucifixItem` instances within `crucifix.blockRadius` (`4f`) of the ghost's current position / spawn position or inside `ghostroomMarker`.
  - If `crucifix.TryBlockHunt()` succeeds (marks `isBurned.Value = true` — **1 hunt blocked per crucifix**):
    - Abort the hunt.
    - Set `nextHuntAttemptTime = Time.time + 25f` (**25-second cooldown** for all ghost types before the ghost can attempt another hunt).

### 6D. Door Noise Detection During Hunt (`NetworkDoor.cs` & `HunterGhostBehavior.cs`)
- During an active hunt (`GhostHandler.CurrentState != GhostHandlerState.Dormant`), when a living player opens or closes an interior room door (`!door.IsHidingSpot && !door.IsFrontDoor`):
  - Notify `HunterGhostBehavior.Instance?.OnPlayerDoorNoise(door.transform.position)` on the server.
  - Cabinet/closet doors (`door.IsHidingSpot == true`) **do not** trigger this noise event.
  - If the ghost is within `doorNoiseHearRadius` (`5.5f` units) and the door position is reachable on NavMesh, register it as a **Detection Waypoint** (subject to the waypoint lock rule below).

### 6E. Non-Chasing Detection, Sticky Last-Known Waypoint & Priority Rules (`HunterGhostBehavior.cs`)
1. **Fix "Turn Off Equipment & Ghost Forgets" Bug:**
   - Track per-player electronic activity in a server-side dictionary:
     - `activeDuration`: how long the player has continuously held powered-on equipment (`PlayerInventory.HasActiveHeldEquipment`) or had active `SpiritBoxItem` dialogue.
     - `detectionGraceExpiry`: when a player turns equipment off after having it active (even briefly, e.g. `>= 1.0f`s, or immediately on ping), keep a residual electronic detection window (`1.5f`s) and **immediately pin the exact player position** as a `DetectionWaypoint`.
2. **Detection Waypoint Lock-In (Last Known Location):**
   - Introduce explicit pursuit modes on `HunterGhostBehavior`:
     - `PursuitMode.None` (Roaming `Searching` / `Returning`)
     - `PursuitMode.DetectionWaypoint` (Investigating a pinned voice/electronic/door last-known location)
     - `PursuitMode.LineOfSight` (Active visual LoS chase, continuously updating `lastKnownPlayerPos`)
   - **Lock Rule:** Once `PursuitMode.DetectionWaypoint` is set to `pinnedWaypointPos`, **do NOT overwrite or redirect it** via another voice, electronic, or door detection until the ghost arrives at `pinnedWaypointPos` (within `0.5f` units) and finds no player — at which point it clears and returns to roaming (`Searching`).
   - Only **achieving active Visual LoS** with a player can interrupt/override `PursuitMode.DetectionWaypoint`.
3. **Detection Priority (Detection Waypoint vs. LoS Waypoint):**
   - On each evaluation tick, scan **detectable players via Spirit Box / active electronics / door noise** *before* scanning Visual LoS.
   - If the ghost detects the **same player** through both electronics/voice and Visual LoS simultaneously:
     - First move toward the **Detection Waypoint** (`pinnedWaypointPos`).
     - Then switch to the continuously-updated **LoS Waypoint**:
       - **Instantly (`0s`)** if only **1 living player** is inside the investigation area.
       - After a randomized delay of **`0f`–`20f` seconds** (clamped/cancelled if the ghost reaches the detection waypoint early or loses LoS) if **2+ living players** are inside the investigation area.
4. **Multi-Player Targeting & 3s Post-Kill Delay:**
   - Track living players in lobby join order (`PlayerController.AllPlayers` join order / `playerJoinIndex`).
   - When multiple players are detected simultaneously:
     - Before the distance check resolves (or during the post-kill delay), target the **first player in the join-order list**.
     - Once the distance check runs (immediately after the LoS check, unless delayed), switch to the player with the shortest **direct Euclidean distance** (`Vector2.Distance`, not NavMesh path distance).
     - **Post-Kill Delay:** After killing a player (`KillPlayer()`), set `distanceCheckDelayTimer = 3.0f` so multi-player distance re-evaluation is delayed for **3 seconds**.
5. **Active LoS Focus & Close-Range Override:**
   - While in `PursuitMode.LineOfSight` with unbroken Visual LoS to `chaseTarget`, remain focused on `chaseTarget` **unless** another living player enters within **`2.0f` units** (`closeLoSOverrideDistance`) of the ghost **while also having active Visual LoS**.
   - While in `PursuitMode.LineOfSight`, `lastKnownPlayerPos` continuously updates to `chaseTarget.transform.position`. If Visual LoS breaks, transition into `PursuitMode.DetectionWaypoint` locked at that final `lastKnownPlayerPos` until reached (then return to roaming if no player is seen).

### 6F. Hiding Spots with Doors & Hiding Behind Opened Room Doors (`HunterGhostBehavior.cs`, `NetworkDoor.cs`)
1. **Hiding Spots with Doors (`door.IsHidingSpot == true`):**
   - When a player is detected inside a closed closet/cabinet (via electronics/Spirit Box or unbroken LoS seeing them enter):
     - Set the ghost's waypoint to a walkable NavMesh point **just outside** that hiding door (`~0.6f` units outside the door toward the room/hallway, rather than inside the closed cabinet).
     - Upon arriving within `doorOpenReach` (`2.0f` units), call `OpenDoorAndConnected(targetedHidingDoor)` to force open the door(s).
     - Add a short `ghostForceLockoutTimer` (e.g. `1.2f`s) on `NetworkDoor` when forced open by the ghost so a player holding/spamming interact on the closet door cannot immediately pull it shut before the ghost checks LoS.
2. **Hiding Behind Opened Room Doors (`!door.IsHidingSpot && !door.IsFrontDoor && door.IsOpen`):**
   - When an opened interior room door is within the ghost's interaction range (`doorOpenReach = 2.0f` units), check if any living reachable player is within **`1.3f` units** (`openedDoorHideCheckRadius`) of that opened door.
   - If true:
     - Set a waypoint to that player's location and **forcefully close** the opened door (`door.GhostInteract(forceOpen: false)` with a brief `1.0f`s force-close lockout so the player cannot hold it open), exposing the player.

---

## Verification Checklist
- [ ] **Doors:** Cannot walk through closed doors or fully open door panels (`openRef`); can walk cleanly through the doorway/door arc only while `0.02 < currentProgress < 0.98`.
- [ ] **Summoning Circle:** Two players interacting simultaneously only opens the GUI for the first player; exorcism success/failure can only trigger once per ritual.
- [ ] **Chase Effect:** During a hunt chase, the camera smoothly pulses zoom in and out alongside the proximity-scaled pulsing vignette, and resets immediately on death/hunt end.
- [ ] **Tutorial Tooltip:** Long titles (`EQUIPMENT: FLASHLIGHT`) and descriptions stay strictly inside the `.tutorial-toast` border without clipping.
- [ ] **Inventory HUD:** `"Flashlight"` and all 10 item names fit cleanly inside `.inventory-slot` without splitting characters onto a second line.
- [ ] **Hunting Behavior:**
  - [ ] No hunt starts if 0 living players are inside the house (triggers favorite-room state instead).
  - [ ] Average sanity uses all living players (inside + outside), excluding dead players, and respects the confirmed 20-ghost thresholds.
  - [ ] Placed Crucifix blocks 1 hunt within radius and enforces a 25s hunt cooldown.
  - [ ] Ghost hears interior room doors opening/closing within `5.5f` units during a hunt (ignoring cabinet doors).
  - [ ] Flashing equipment on for 1–2s then turning it off pins a locked Detection Waypoint that the ghost checks before returning to roam.
  - [ ] Detection priority, 0s (solo) / 0–20s (multiplayer) LoS switch delay, 3s post-kill distance check delay, and `2.0f` close-LoS override work as specified.
  - [ ] Ghost forces open hiding closet doors (overriding player hold) and closes opened room doors when players hide within `1.3f` units behind them.
