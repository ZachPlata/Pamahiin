# Pamahiin Warehouse Tutorial Plan

## Goal
Add a **Warehouse Tutorial** (`WarehouseTutorialScene.unity`)—a fast, linear, step-by-step training facility for new players that teaches all core Pamahiin mechanics in ~3–5 minutes using gated warehouse bays, a safe scripted ghost, and an interactive checklist HUD.

---

## Design Summary
- **Entry & Access**:
  - Dedicated **Tutorial** button on the Main Menu (`MainMenuController`) and an optional replay trigger in `LobbyScene.unity` (e.g., via `LobbyBriefingTableInteractable` or Lobby UI).
  - **First-Time Gate**: New players who have not completed or skipped the tutorial are prompted to enter the Warehouse Tutorial before unlocking Multiplayer / Contracts, with an explicit **Skip Tutorial** button.
- **Structure**: **Linear Gated Warehouse Bays** (Bays 1–5). Each bay locks the exit door/gate (`NetworkDoor`) until all steps in that bay are completed, then unlocks and highlights the next bay.
- **Ghost Behavior**: **Safe Scripted Ghost** (`TutorialGhostController`). Demonstrates evidence interactions on cue and runs a **non-lethal simulated hunt** (blocking with `CrucifixItem`, hiding in `HideZone`, and recovering with `SanityPillsItem`—if the ghost reaches the player, it resets the player to the bay entrance with a hint instead of triggering `DeathSequenceController`).
- **Persistence**: Track completion in `ProgressionAndLoadoutManager` (`PlayerPrefs` key `Pamahiin_TutorialCompleted_v1`), replayable anytime from Main Menu or Lobby.

---

## Warehouse Layout & Step-by-Step Bays

### Bay 1 — Movement, Controls & Channel Separation
- **Teaches**: `WASD` movement, `Shift` sprint, `[L-Click]` (equipment pickup & primary use) vs `[E]` (environment interaction), `[R-Click]` placement, `[1–3]` / scroll slot switching.
- **Steps**:
  1. Walk to the equipment table and pick up `FlashLightItem` with `[L-Click]`.
  2. Toggle the flashlight on/off with `[L-Click]`.
  3. Open the Bay 1 exit door with `[E]` (demonstrating that `[L-Click]` does not open doors).

### Bay 2 — Power, Light Switches & Sanity
- **Teaches**: `FuseBox`, `HouseLightSwitch`, and how darkness drains sanity on `PlayerHUDManager`.
- **Steps**:
  1. Walk into the darkened Bay 2 corridor and observe the sanity drain indicator.
  2. Restore power at the `FuseBox` using `[E]`.
  3. Flip the `HouseLightSwitch` with `[E]` to illuminate the bay and unlock Bay 3.

### Bay 3 — Evidence Gathering (Scripted Ghost Zone)
- **Teaches**: Core investigation tools against a scripted ghost inside a marked containment room.
- **Steps**:
  1. **EMF Reader (`EMFReaderItem`)**: Pick up and detect an EMF Level 5 spike on a ghost-disturbed prop.
  2. **Thermometer (`ThermometerItem`)**: Scan the marked cold spot to confirm freezing temperatures.
  3. **Spirit Box (`SpiritBoxItem`)**: Activate in the dark room and trigger a scripted response via `SpiritBoxDialogueUI`.
  4. **Ghost Writing (`GhostWritingBookItem`)**: Place the book with `[R-Click]` on the marked spot and watch the ghost write in it.
  5. **Video Camera & D.O.T.S. (`VideoCameraItem`, `DotsProjectorItem`)**: Place/aim to spot `GhostOrbSystem` orbs and a scripted D.O.T.S. silhouette.

### Bay 4 — Hunt Survival, Crucifix & Sanity Pills
- **Teaches**: `CrucifixItem` hunt prevention, `HideZone` stealth during a hunt, and `SanityPillsItem` recovery.
- **Steps**:
  1. **Crucifix Protection**: Place or hold `CrucifixItem` near the containment circle as the ghost attempts a scripted hunt—observe the crucifix burn/block the attempt.
  2. **Simulated Non-Lethal Hunt**: The ghost initiates a 10-second safe hunt; enter the `HideZone`, turn off `FlashLightItem`, and wait out the hunt. (Being caught resets the step safely with a tip.)
  3. **Sanity Recovery**: Use `SanityPillsItem` (`[L-Click]`) to restore sanity and unlock Bay 5.

### Bay 5 — Journal & Contract Wrap-Up
- **Teaches**: `JournalController` evidence deduction (`GhostCatalog`) and `TruckEndGameButton` / `TruckDashboardController` extraction.
- **Steps**:
  1. Open the Journal (`[J]`), check off the gathered evidence, and select the matching ghost type.
  2. Interact with the Warehouse Exit Truck Console (`TruckEndGameButton`) with `[E]` to finish the tutorial contract.
  3. Display completion summary, set `Pamahiin_TutorialCompleted_v1 = 1`, and return to `MainMenuScene` / `LobbyScene`.

---

## Technical Architecture & Target Files

### 1. New Scene & Core Tutorial Scripts
- **`Assets/Scenes/WarehouseTutorialScene.unity`** *(new)*:
  - Single-player local `NetworkManager.StartHost()` session (reuses all existing `NetworkBehaviour` equipment, doors, fuse box, light switches, hide zones, and HUD without duplicating gameplay code).
  - 5 sequential warehouse bays with clear floor signage, spotlights, and gated `NetworkDoor` dividers.
- **`Assets/Scripts/Tutorial/WarehouseTutorialManager.cs`** *(new)*:
  - State machine tracking `TutorialBay` (1–5) and individual step flags.
  - Locks/unlocks bay `NetworkDoor`s (`AllowPlayerInteract`), spawns/highlights next objective markers, and triggers `TutorialTooltipManager` hints.
  - Provides `SkipTutorial()` (callable from `PauseMenuController` or confirmation modal) and `CompleteTutorial()`.
- **`Assets/Scripts/Tutorial/TutorialGhostController.cs`** *(new)*:
  - Lightweight scripted ghost controller for Bays 3 & 4.
  - Emits guaranteed evidence triggers (EMF 5, Freezing Temp, Spirit Box reply, Writing, Ghost Orbs, D.O.T.S.) and runs the non-lethal hunt sequence in Bay 4.
- **`Assets/Scripts/UI/WarehouseTutorialHUD.cs`** *(new)*:
  - Compact top-left/top-right checklist UI (Bay title, current step checkboxes, keybind badges, and progress bar `Bay X / 5`).

### 2. Existing Files to Update
- **`Assets/Scripts/ProgressionAndLoadoutManager.cs`**:
  - Add `WAREHOUSE_TUTORIAL_SCENE_NAME = "WarehouseTutorialScene"` and `PREF_TUTORIAL_COMPLETED = "Pamahiin_TutorialCompleted_v1"`.
  - Add `HasCompletedOrSkippedTutorial()`, `MarkTutorialCompleted()`, and `MarkTutorialSkipped()`.
- **`Assets/Scripts/UI/MainMenuController.cs`** (and Main Menu UXML/USS):
  - Add a **Tutorial** button on the main menu.
  - Gate Multiplayer / Play entry when `!HasCompletedOrSkippedTutorial()` with a prompt offering **Start Tutorial (Recommended)** or **Skip Tutorial**.
- **`Assets/Scripts/LobbySceneController.cs`** / **`Assets/Scripts/LobbyBriefingTableInteractable.cs`**:
  - Add an optional **"Replay Warehouse Tutorial"** action in the Lobby so players can revisit training anytime.
- **`Assets/Scripts/UI/PauseMenuController.cs`**:
  - Add a **Skip Tutorial** / **Restart Tutorial** button when active inside `WarehouseTutorialScene`.

---

## Verification Plan
1. **Build Check**:
   - `dotnet build Assembly-CSharp.csproj -nologo` -> `0 Error(s)`.
   - `dotnet build Pamahiin.EditModeTests.csproj -nologo` -> `0 Error(s)`.
2. **EditMode Tests** (`Assets/Tests/EditMode/WarehouseTutorialTests.cs`):
   - Verify `ProgressionAndLoadoutManager` tutorial completion/skip persistence (`PlayerPrefs`).
   - Verify `WarehouseTutorialManager` bay progression & door unlock transitions from Bay 1 through Bay 5.
3. **Playtest Checklist (Unity Editor)**:
   - First-launch gate prompts for Tutorial and allows **Skip Tutorial**.
   - Main Menu **Tutorial** button loads `WarehouseTutorialScene.unity` cleanly in offline/local host mode.
   - All 5 bays complete end-to-end without soft-locks, including non-lethal hunt reset if caught in Bay 4.
   - Completing Bay 5 unlocks Multiplayer/Contracts and returns cleanly to `MainMenuScene` / `LobbyScene`.
