# AI Execution Plan: Loading Intermissions, Animated Summoning Circle, Ghost Death Sprite Sheet & Dual-Voice Dying Shriek

> **Execution Directives for AI Session:**
> - **Token-Minimal Scope**: Inspect and modify **only** the files and directories listed in Section 2. **Do not scan the root directory or unrelated scene/prefab trees.**
> - **Art & Audio Policy**: Generate all new 16-bit sprite sheets and the dual-voice dying shriek WAV deterministically inside `Assets/Editor/BarangayAssetGenerator.cs`, while exposing Inspector / `Resources` override slots on runtime components so custom art or audio can replace generated assets without code edits.
> - **Verification Rule**: Do not claim compilation or runtime behavior passes unless actually checked in Unity / tests after the final edit.

---

## 1. Goal & Design Decisions

Implement five cohesive gameplay, visual, and audio upgrades across match transitions, the chalk exorcism ritual, and ghost defeat:

1. **Intermission Loading Screens (Lobby → Map & End Game → Ghost Reveal)**:
   - Show a synchronized intermission loading screen after clicking `▶ START INVESTIGATION` in `LobbyScene` before entering the target map (`BarangayMap` / `GameMapScene`), holding until network scene load + van gear spawning settle.
   - Show a second intermission loading screen after pressing the van `TruckEndGameButton` (following Jeepney ramp close & departure audio) **before** `EndGameRevealController` reveals the true ghost type.
2. **Standalone Summoning Circle Entity with Circular Hover Collider & `"Summon Ghost"` Prompt**:
   - Decouple the placed summoning circle from `RitualChalkItem`'s chalk-stick collider and `"Ritual Chalk x1"` equipment label: placing the chalk switches the visual to the summoning circle and spawns/activates a separate `SummoningCircle` object with its own 24–32-vertex circular `PolygonCollider2D`.
   - Hovering over the circle renders a circular `ItemHoverIndicator` outline and displays `"Summon Ghost"`.
3. **9-Frame Summoning Circle Sprite Sheet — Sequential One-by-One Candle Lighting & Delayed Summon**:
   - Generate a 9-frame horizontal 16-bit sprite sheet (`Frames 0–8`) depicting the chalk circle + 5 red candles lighting **one by one in a circle** (`Frames 0 → 5`) followed by all 5 flames waving with unique per-candle motion (`Frames 5–8`).
   - Hold ghost manifestation and minigame start until all 5 red candle flames have finished lighting sequentially around the circle.
4. **Ghost Exorcism Death Sprite Sheet Animation (Replacing Shake/Shrink Code)**:
   - Replace the `Random.insideUnitCircle` shake + `localScale -> Vector3.zero` coroutine in `HunterGhostBehavior.DyingRoutine()` with a stationary multi-frame sprite sheet animation where the ghost shrivels, raises/arches its arms skyward while emitting a bright burst of `Light2D` radiance, and burns down into ashes.
5. **Dual-Voice Interlapping Dying Ghost Shriek (`A2–D3` Dark + Normal-Octave High)**:
   - Replace the phase-distorted synth tone in `HunterGhostBehavior.CreateProceduralShriekClip()` and export a studio-clean procedural WAV (`ghost_dying_shriek.wav`) layering two interlapping vocal-formant voices: a deep guttural voice anchored in **`A2–D3` (`110–147 Hz`)** and a natural human-octave screaming voice (**`D4–C5` / `~294–523 Hz`**) without piercing ultrasonic whistle artifacts.

---

## 2. Target Directories & Files (Token-Minimal Scope)

Only inspect and modify files inside these specific directories/files — **do not scan the root directory**:

| Directory / File | Target File(s) | Role in Plan |
|---|---|---|
| `Assets/UI/LoadingScreen/` *(new)* & `Assets/Resources/UI/` *(new)* | `LoadingScreen.uxml`, `LoadingScreen.uss` *(or placed directly in `Assets/Resources/UI/LoadingScreen/`)* | Persistent intermission UI Toolkit layout and horror dossier styling |
| `Assets/Scripts/UI/` | `LoadingScreenManager.cs` *(new)*, `JournalController.cs`, `EndGameRevealController.cs`, `ExorcismManager.cs` | Intermission controller; hook Start Investigation, End Game reveal gating, sequence candle ignition before summoning ghost/minigame, and default `ghost_dying_shriek` clip fallback |
| `Assets/Scripts/` | `NetworkController.cs`, `GameMatchManager.cs`, `PlayerController.cs`, `HunterGhostBehavior.cs`, `PixelSpriteAnimator.cs` | Synced travel loading message; End Game intermission sequencing; `SummoningCircle` hover branch; ghost death sprite-sheet playback & `GhostPixelAnimator` pause; dual-voice shriek fallback |
| `Assets/Scripts/Equipment/` | `RitualChalkItem.cs`, `SummoningCircle.cs` *(new)* | Transform placed chalk into standalone `SummoningCircle` with circular `PolygonCollider2D`, `"Summon Ghost"` prompt, and 9-frame sequential candle ignition + waving loop |
| `Assets/Prefabs/` | `RitualChalkItem.prefab`, `SummoningCircle.prefab` *(new, optional/auto-wired)* | Wire `chalkDiagramPrefab` / override fields if prefab-backed |
| `Assets/Editor/` | `BarangayAssetGenerator.cs`, `BarangayTextureImporter.cs` | Generate `summoning_circle_sheet.png`, `ghost_death_sheet.png`, and `ghost_dying_shriek.wav` with crisp Point import settings |
| `Assets/Resources/Sprites/` & `Assets/Resources/Audio/` | `Ritual/summoning_circle_sheet.png` *(new)*, `ghost_death_sheet.png` *(new)*, `ghost_dying_shriek.wav` *(new)* | Generated 16-bit sprite sheets and dual-voice shriek WAV (mirrored to `Assets/Audio/Sound Effects/Retro16Bit/`) |

---

## 3. Verified Current State & Integration Points

1. **Start Investigation & End Game Transition Flow**:
   - **Lobby → Map**: Clicking `btn-start-contract` (`"▶ START INVESTIGATION"`, [`JournalUI.uxml`](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Journal/JournalUI.uxml#L421)) invokes [`JournalController.cs`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/JournalController.cs#L601-L611), which closes the journal and calls [`NetworkController.LoadGameScene()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/NetworkController.cs#L469-L483) directly via `NetworkManager.Singleton.SceneManager.LoadScene(...)` with no intermission screen.
   - **End Game → Reveal**: [`TruckEndGameButton.Interact()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/TruckEndGameButton.cs#L172-L185) calls `GameMatchManager.RequestEndMatch()`. [`GameMatchManager.ExecuteLocalEndMatchReveal()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/GameMatchManager.cs#L475-L521) disables `PlayerController`, runs `WaitForTruckAudioThenRevealRoutine()` (waiting for `PlayTruckStartDepart` and Jeepney ramp closure), and immediately invokes `OnMatchEnded` → [`EndGameRevealController.TriggerReveal()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/EndGameRevealController.cs#L73-L139).
   - **UI Toolkit Pattern**: [`SpiritBoxDialogueUI.cs`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/SpiritBoxDialogueUI.cs#L56-L68) and `Assets/UI Toolkit/PanelSettings.asset` show how dynamically spawned or persistent `UIDocument` overlays reuse an active `PanelSettings` asset safely across scene loads.

2. **Ritual Chalk Placement, Colliders & Hover Rendering**:
   - [`RitualChalkItem.prefab`](file:///d:/Github%20Projects/Pamahiin/Assets/Prefabs/RitualChalkItem.prefab#L168-L212) carries a `BoxCollider2D` sized to `chalk 1.png` (`4.5 × 1.5`), which [`EquipmentItem.Awake()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/EquipmentItem.cs#L95-L119) swaps for a `PolygonCollider2D` traced from the chalk stick—not a circle—when unplaced.
   - [`RitualChalkItem.OnPlacedChanged()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/RitualChalkItem.cs#L224-L290) swaps `spriteRenderer.sprite` to `diagramSprite` (if `chalkDiagramPrefab == null`) and immediately calls `TriggerRitualPopup()` when `autoStartOnPlace == true` ([line 33](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/RitualChalkItem.cs#L33)), blocking hover inspection.
   - [`PlayerController.UpdateItemHoverIndicator()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/PlayerController.cs#L776-L831) intercepts any `EquipmentItem` with `IsOnGround || IsPlaced` and hardcodes `$"{equipment.ItemName} x{stackCount}"` (`"Ritual Chalk x1"`), bypassing [`RitualChalkItem.GetInteractText()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/RitualChalkItem.cs#L415-L435) (which currently returns `"Begin Exorcism"`).
   - [`ItemHoverIndicator.UpdateIndicatorTransform()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/ItemHoverIndicator.cs#L218-L276) automatically traces any `PolygonCollider2D` on the hovered `GameObject` into the green outline `LineRenderer`.
   - [`ExorcismManager.OnGhostSelected()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/ExorcismManager.cs#L206-L234) → [`StartCircularSkillCheckLocal()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/ExorcismManager.cs#L242-L263) currently calls `SetupGhostInRitualCircle(diagramPos)` and opens `exorcism-overlay` immediately without waiting for any ritual candle-lighting sequence.

3. **Ghost Exorcism Death & Audio Synthesis**:
   - [`ExorcismManager.TriggerSuccessLocal()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/ExorcismManager.cs#L458-L485) invokes `ghostHandler.hunterGhost.StartDyingSequence(dyingShriekClip, manifestPos)`.
   - [`GhostPixelAnimator.Update()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/PixelSpriteAnimator.cs#L183-L211) overwrites `HunterGhost`'s `SpriteRenderer.sprite` every frame from `idleFrames`/`walkFrames`/`huntFrames` (`64×64` sprites at `100` PPU) and must be paused during death playback.
   - [`HunterGhostBehavior.DyingRoutine()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/HunterGhostBehavior.cs#L1249-L1323) currently performs random `transform.position` shaking (`Random.insideUnitCircle`) and `localScale` shrinking, then calls `ghostHandler.OnGhostExorcised()` ([`GhostHandler.cs`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/GhostHandler.cs#L354-L384)).
   - [`HunterGhostBehavior.CreateProceduralShriekClip()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/HunterGhostBehavior.cs#L1325-L1365) multiplies a sliding `freq` by `t` (`2 * PI * (freq + vibrato) * t`) without phase accumulation and adds raw white noise, producing unnatural synth/FM artifacts.
   - [`BarangayAssetGenerator.cs`](file:///d:/Github%20Projects/Pamahiin/Assets/Editor/BarangayAssetGenerator.cs#L1241-L1542) and [`WeatherManager.SliceSheet()`](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/WeatherManager.cs#L271-L292) establish the project's horizontal sprite-sheet slicing and deterministic 16-bit WAV export patterns.

---

## 4. Planned Changes

### Step 1: Implement Persistent Intermission Loading Screen System
1. **Create Loading Screen UI Toolkit Assets (`Assets/Resources/UI/LoadingScreen.uxml` & `LoadingScreen.uss`)**:
   - Build a full-screen dark atmospheric overlay (`#07080c` / aged dossier vignette) with:
     - Top classification tag (`PAMAHIIN // FIELD DISPATCH` or `CASE DEBRIEFING`),
     - Primary header label (`lbl-loading-title`, e.g., `"TRAVELING TO INVESTIGATION SITE"` / `"RETURNING TO SAFE HOUSE"`),
     - Subtitle/location or status line (`lbl-loading-subtitle`, e.g., active map display name + difficulty or `"Compiling evidence & Jeepney dashcam logs..."`),
     - Animated 16-bit style progress bar (`loading-progress-fill`) and rotating folklore field tip label (`lbl-loading-tip`),
     - Smooth opacity transition classes (`hidden` / `loading-visible`) matching existing UXML overlays (`sortingOrder = 100` so it sits above HUD, Journal, and EndGame UI).
2. **Create `LoadingScreenManager` (`Assets/Scripts/UI/LoadingScreenManager.cs`)**:
   - Implement a `DontDestroyOnLoad` singleton (auto-ensured via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` or `EnsureInstance()`) that creates/owns a `UIDocument` loaded from `Resources/UI/LoadingScreen`, resolving `PanelSettings` from scene `UIDocument`s or `Resources` (mirroring `SpiritBoxDialogueUI.cs`).
   - Expose clean coroutine/callback APIs:
     - `ShowIntermission(string title, string subtitle, string tip = null)`
     - `SetProgress(float normalized01, string statusText = null)`
     - `HideIntermission(float fadeDuration = 0.35f)`
     - `RunTimedIntermission(string title, string subtitle, float minSeconds, Func<bool> readyCondition, Action onComplete)`
   - Suppress player input / interaction while visible (`IsVisible` flag checked if needed, without breaking scene cleanup).
3. **Wire Lobby → Map Intermission (`Assets/Scripts/UI/JournalController.cs` & `Assets/Scripts/NetworkController.cs`)**:
   - Add a `CustomMessagingManager` named message (`MSG_MATCH_LOADING_INTERMISSION`) in `NetworkController.cs` following the existing `MSG_VAN_GEAR_MANIFEST` pattern (`RegisterClientLobbyMessagingHandlers()` / `RegisterServerLobbyMessagingHandlers()`).
   - Refactor `JournalController.cs` (`btnStartContract.clicked`) and `NetworkController.LoadGameScene()`:
     - Disable duplicate clicks on `btnStartContract`, close the journal, and trigger `ShowIntermission(...)` on Host and broadcast `MSG_MATCH_LOADING_INTERMISSION` (containing selected map name & difficulty) to all connected Clients.
     - Hold an explicit intermission coroutine on the Server (`~2.0s` minimum readable briefing transition), then call `NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single)` (or `SceneManager.LoadScene` in offline fallback).
     - Keep `LoadingScreenManager` visible through `OnSceneLoaded` and `SpawnSceneEquipmentWhenReadyRoutine()` / client `OnVanGearManifestReceived`, advancing progress smoothly (`0.2 -> 0.7 -> 1.0`), then fade out once the map and equipment are spawned.
4. **Wire End Game Button → Ghost Type Reveal Intermission (`Assets/Scripts/GameMatchManager.cs` & `Assets/Scripts/UI/EndGameRevealController.cs`)**:
   - Update `GameMatchManager.WaitForTruckAudioThenRevealRoutine(string trueName)` (`Assets/Scripts/GameMatchManager.cs#L493-L521`):
     - After the Jeepney ramp closes and `PlayTruckStartDepart` finishes, call `LoadingScreenManager.EnsureInstance().RunTimedIntermission(...)` with non-spoiling copy (`"DEPARTING INVESTIGATION SITE"`, `"Extracting evidence records & finalizing contract payout..."`, `~2.0s–2.4s`), keeping `trueName` completely hidden during the intermission.
     - On intermission completion, invoke `OnMatchEnded.Invoke(trueName)` / `EndGameRevealController.TriggerReveal(trueName)` cleanly, then fade out the loading screen as `endgame-overlay` appears underneath.

---

### Step 2: Generate 16-Bit Sprite Sheets & Dual-Voice Dying Shriek (`Assets/Editor/BarangayAssetGenerator.cs` & `BarangayTextureImporter.cs`)
1. **Generate 9-Frame Summoning Circle & Sequential 5-Candle Sprite Sheet (`Assets/Resources/Sprites/Ritual/summoning_circle_sheet.png`)**:
   - Add `GenerateSummoningCircleSheet()` to `BarangayAssetGenerator.cs` (invokable via existing `Generate All` menu and a dedicated `[MenuItem("Pamahiin/Generate Ritual & Ghost Death Assets")]`):
     - Produce a horizontal 9-frame sprite sheet (`9 × 96×96` pixels = `864×96`, `FilterMode.Point`, `PPU = 100`, uncompressed RGBA32):
       - **Base Diagram & Candles (all frames)**: Crisp chalk-white/bone outer ritual circle (`radius ~38px`), concentric inner sigil ring, 5-pointed ritual star, and 5 symmetrical red-wax candle pedestals placed in circular order at `72°` intervals (`angle = -90° + k * 72°`, `k = 0..4`).
       - **Frame 0 (Unlit Ready State)**: Full chalk circle and all 5 red candles unlit on the ring (resting frame when chalk is first placed).
       - **Frames 1–5 (One-by-One Circular Ignition)**: Light the 5 red candles sequentially clockwise around the circle one candle per frame (`Frame 1`: Candle 1 ignites; `Frame 2`: Candles 1–2 lit; `Frame 3`: Candles 1–3 lit; `Frame 4`: Candles 1–4 lit; `Frame 5`: Candle 5 ignites so **all 5 flames are lit**). Each already-lit candle advances into unique flame-flicker poses as the remaining candles ignite, casting a warm dark-red halo onto the chalk beneath lit vertices.
       - **Frames 5–8 (All 5 Lit — Unique Flame Waving Loop)**: All 5 candles burn steadily with crimson/red/ember-orange flames (`#FF2218`, `#CC1111`, `#FF6633` tip), where each candle `k ∈ [0..4]` uses a **unique phase offset, curl direction, and flicker height function** (`phaseOffset = k * 1.37f` + asymmetric tip sway) so no two flames wave identically across loop frames `5..8` (wrapping seamlessly from frame 8 back to frame 5).
2. **Generate 12-Frame Ghost Exorcism Death Sprite Sheet (`Assets/Resources/Sprites/ghost_death_sheet.png`)**:
   - Add `GenerateGhostDeathSheet()` to `BarangayAssetGenerator.cs` matching `ghost_idle_0.png` (`64×64` per frame, 12 horizontal frames = `768×64`, `FilterMode.Point`, `PPU = 100`):
     - **Frames 0–2 (Shrivel)**: Ghost silhouette compresses horizontally/vertically and hunches inward with jagged spectral ripples.
     - **Frames 3–6 (Arms Skyward / Arc + Light Emission)**: Ghost throws both arms upward into an open skyward arc while bright golden-white/pale-cyan purification rays erupt from its torso/head outward across the `64×64` frame.
     - **Frames 7–9 (Incineration)**: Ember-orange and white-hot fire consumes the arcing silhouette from the limbs inward, breaking the body into rising sparks.
     - **Frames 10–11 (Burn to Ashes)**: Silhouette collapses into a compact smoldering pile of dark grey/charcoal ash with faint fading red/amber embers on the ground (`frame 11` nearly dispersed).
3. **Update `BarangayTextureImporter.cs`**:
   - Ensure `Assets/Resources/Sprites/Ritual/` and `ghost_death_sheet.png` import with `FilterMode.Point`, `TextureImporterCompression.Uncompressed`, `mipmapEnabled = false`, `isReadable = true`, and `SpriteMeshType.FullRect` (matching `Weather` / `Props` sheets).
4. **Synthesize Dual-Voice Interlapping Dying Ghost Shriek (`ghost_dying_shriek.wav`)**:
   - Add `GenerateGhostDyingShriekWav()` in `BarangayAssetGenerator.cs` writing `Assets/Resources/Audio/ghost_dying_shriek.wav` and mirroring to `Assets/Audio/Sound Effects/Retro16Bit/ghost_dying_shriek.wav` (`44100 Hz`, `2.8s` duration):
     - **Voice 1 — Deep Guttural Voice (`A2–D3`)**:
       - Use strict phase accumulation (`phaseLow += 2 * PI * freqLow / sr`) sweeping organically from **`D3 (146.83 Hz)`** down to **`A2 (110.0 Hz)`** with subtle `6.2 Hz` vocal tremor (`±3.5 Hz`) and sub-glottal grit. Never drop below `A2 (~110 Hz)` so it stays deep and resonant rather than muddy `A1`.
       - Shape a sawtooth/glottal-pulse blend through dual vocal chest formants (`F1 ~ 360 Hz`, `F2 ~ 780 Hz` bandpass SVF) + breathy low air rasp.
     - **Voice 2 — Natural Human-Octave High Scream (`D4–C5` / `~294–523 Hz`)**:
       - Use strict phase accumulation (`phaseHigh += 2 * PI * freqHigh / sr`) starting at `~340 Hz`, surging in agonizing vocal strain up to `~494–523 Hz` (`B4–C5`, comfortably within natural human screaming range—not shrill 900 Hz+ synth), then cracking/wailing down to `~311 Hz` (`D#4`).
       - Shape through `"AAH/EEH"` upper vocal formants (`F1 ~ 720 Hz`, `F2 ~ 1380 Hz` SVF) with `7.5 Hz` frantic vibrato and slight consonant/raspy air turbulence.
     - **Interlapping Composition & Polish**:
       - Stagger intensity envelopes so the high voice strikes immediately (`0.04s–1.15s`), the deep `A2–D3` voice surges underneath and overlaps (`0.22s–2.10s`), both clash in a brief dual-voice peak around `1.25s–1.65s`, and the high voice breaks away into a fading `A2` ember/ash breath tail (`2.10s–2.80s`).
       - Apply gentle soft-clip saturation (`tanh`), simple multi-tap short room reverb (`23ms`, `41ms`, `67ms`), and `NormalizePeak(..., 0.90f)`.

---

### Step 3: Refactor Ritual Chalk, Build `SummoningCircle` & Sequence Ignition Before Ghost Summon (`RitualChalkItem.cs`, `SummoningCircle.cs`, `ExorcismManager.cs`, `PlayerController.cs`)
1. **Create `SummoningCircle` (`Assets/Scripts/Equipment/SummoningCircle.cs`)**:
   - Implement `MonoBehaviour, IInteractable` (used either on `SummoningCircle.prefab` or constructed cleanly at runtime if no prefab is assigned):
     - **Visuals, 3-State Animation & Override Slots**:
       - `[SerializeField] private Texture2D customSpriteSheet;`
       - `[SerializeField] private Sprite[] customFrames;`
       - `[SerializeField] private int sheetFrameCount = 9;`
       - `[SerializeField] private int ignitionEndFrame = 5;` *(Frame 5 = all 5 candles lit)*
       - `[SerializeField] private int loopStartFrame = 5;` *(Loops frames `5..8` with all 5 flames waving)*
       - `[SerializeField] private float ignitionFrameRate = 3.5f;` *(Clear visible 1-by-1 circular candle lighting, ~1.4–1.7s total)*
       - `[SerializeField] private float loopFrameRate = 8f;`
       - Slice `Resources/Sprites/Ritual/summoning_circle_sheet` via `Sprite.Create` (`FilterMode.Point`, `PPU = 100`) unless overridden by `customFrames`/`customSpriteSheet`.
       - **Placed / Ready State**: Hold on `Frame 0` (summoning circle + 5 unlit red candles) with `Light2D` off/subtle.
       - **Igniting State (`PlayIgnitionSequence(Action onAllCandlesLit)`)**: Step sequentially through `Frames 0 → 1 → 2 → 3 → 4 → 5` at `ignitionFrameRate` so the 5 candles ignite one by one around the circle, ramping crimson `Light2D` intensity (`#FF3B28`) with each candle lit (optional subtle ignition SFX tick per candle). Once `ignitionEndFrame` (`Frame 5`) completes, enter the continuous `Frames 5..8` waving loop at `loopFrameRate` and invoke `onAllCandlesLit`.
     - **Unique Circular `PolygonCollider2D`**:
       - Require/configure a `PolygonCollider2D` (`isTrigger = true`, `LayerMask.NameToLayer("Interactable")`) and populate its path explicitly via `SetPath(0, circleVertices)` using 28–32 evenly spaced points around the circle radius matching the drawn chalk ring in local space.
       - Ensure `ItemHoverIndicator.UpdateIndicatorTransform()` reads this polygon directly so the green hover border traces a true circle around the summoning ring.
     - **`IInteractable` Contract**:
       - `CanDrag() => false;`
       - `GetInteractText()`: Return `"Summon Ghost"` when idle/ready; `"Exorcism in Progress"` while igniting or during active exorcism; `"Exorcism Completed"` if `ExorcismManager.IsExorcismCompleted`.
       - `Interact()`: Guard against active/completed exorcism, then call `ExorcismManager.InitiateRitual(transform.position)`.
2. **Update `RitualChalkItem.cs` (`Assets/Scripts/Equipment/RitualChalkItem.cs`)**:
   - Change `autoStartOnPlace` default to `false` so placing the chalk spawns the standalone `SummoningCircle` at `Frame 0` (5 unlit candles) and lets players hover its circular collider (`"Summon Ghost"`) and interact to summon the ghost (while preserving the `[SerializeField]` flag if auto-start is desired).
   - Update `OnPlacedChanged(bool placed)` and `UpdateVisuals()`:
     - When `placed == true`, set `transform.position` `z = 0f`, detach from any van parent, **hide the chalk stick's own `SpriteRenderer` (`enabled = false`) and disable its chalk-stick `interactCollider` (`enabled = false`)**, and instantiate/activate the standalone `SummoningCircle` entity (`chalkDiagramPrefab` if assigned, else runtime `SummoningCircle` object scaled to `diagramWorldDiameter`, e.g., `0.26f–0.30f` world units).
     - Ensure `SummoningCircle` spawns synchronously on both Server and Clients because `isPlaced` (`NetworkVariable<bool>`) replicates via `EquipmentItem.OnNetworkSpawn()` / `OnPlacedChanged()`.
     - When `placed == false`, destroy `spawnedDiagramInstance` and restore the chalk stick sprite & collider.
3. **Sequence Candle Ignition Before Summoning Ghost & Starting Minigame (`Assets/Scripts/UI/ExorcismManager.cs`)**:
   - Update `StartCircularSkillCheckLocal(Vector3 diagramPos)` ([lines 242–263](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/ExorcismManager.cs#L242-L263)) so selecting a ghost in `ghost-select-popup` starts a `SummonAndBeginMinigameRoutine(diagramPos)` coroutine instead of spawning the ghost and opening `exorcism-overlay` immediately:
     - Locate the `SummoningCircle` at `diagramPos` (closest instance within ~`0.5f`).
     - Keep `isExorcismActive = true`, keep `isMinigameActive = false`, and keep `exorcismOverlay` hidden while `summoningCircle.PlayIgnitionSequence(...)` lights all 5 red candles one by one around the circle (`Frames 0 → 5`).
     - **Once all 5 flames finish lighting** (if no `SummoningCircle` is found, proceed immediately as fallback), call `SetupGhostInRitualCircle(diagramPos)` to manifest `HunterGhost` inside the burning circle (with sorting order above the circle so the ghost stands cleanly inside the 5 lit flames), initialize minigame state (`isMinigameActive = true`), and show `exorcismOverlay`.
4. **Update `PlayerController.cs` (`Assets/Scripts/PlayerController.cs`)**:
   - In `UpdateItemHoverIndicator()` ([lines 776–831](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/PlayerController.cs#L776-L831)):
     - Add an explicit check `else if (currentlyHoveredInteractable is SummoningCircle summoningCircle)` calling `TryShowWorldInteractableHover(summoningCircle, summoningCircle.GetInteractText())` so hovering displays `"Summon Ghost"` (and never `"Ritual Chalk x1"`).
     - Also guard the `EquipmentItem` branch (`if (currentlyHoveredInteractable is EquipmentItem equipment)`) so a placed `RitualChalkItem` never displays `"Ritual Chalk x1"` if its collider is ever hit directly.
   - In `CalculateHoveredInteractable()` ([lines 728–742](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/PlayerController.cs#L728-L742)):
     - Include `interactable is SummoningCircle` in the interactive item priority (`isEquipOrKey || interactable is SummoningCircle`) so nearby floor triggers never steal cursor hover from the summoning circle.

---

### Step 4: Replace Exorcised Ghost Death Loop with Sprite Sheet Animation & Enhanced Shriek (`HunterGhostBehavior.cs`, `PixelSpriteAnimator.cs`, `ExorcismManager.cs`)
1. **Pause `GhostPixelAnimator` During Exorcism Death (`Assets/Scripts/PixelSpriteAnimator.cs`)**:
   - Add `private bool isDyingOverride = false;` and `public void SetDyingOverride(bool active)` on `GhostPixelAnimator`.
   - Early-return at the top of `GhostPixelAnimator.Update()` when `isDyingOverride` is `true` so idle/hunt frames never overwrite the death sprite sheet.
2. **Refactor `HunterGhostBehavior.DyingRoutine()` (`Assets/Scripts/HunterGhostBehavior.cs`)**:
   - Add Inspector override fields on `HunterGhostBehavior`:
     - `[SerializeField] private Texture2D customDeathSpriteSheet;`
     - `[SerializeField] private Sprite[] customDeathFrames;`
     - `[SerializeField] private int deathFrameCount = 12;`
     - `[SerializeField] private float deathAnimationDuration = 2.8f;`
     - `[SerializeField] private AudioClip customDyingShriekClip;`
   - Replace the random shake & scale-shrink loop in `DyingRoutine(AudioClip dyingClip, Vector3? manifestPosition)`:
     - Keep transform stationary at `manifestPosition` (preserving natural ghost scale, no `Random.insideUnitCircle` jitter and no `localScale -> Vector3.zero`).
     - Notify `GetComponent<GhostPixelAnimator>()?.SetDyingOverride(true)`.
     - Load sliced frames from `customDeathFrames` / `customDeathSpriteSheet` / `Resources.Load<Texture2D>("Sprites/ghost_death_sheet")` (`FilterMode.Point`, `PPU = 100`, sliced into `deathFrameCount` frames).
     - Play the shriek audio clip in priority order: `dyingClip` (if explicitly assigned) → `customDyingShriekClip` → `GameAudioManager.GetClip("ghost_dying_shriek")` → upgraded `CreateProceduralShriekClip()`.
     - Step cleanly through all 12 sprite-sheet frames across `deathAnimationDuration`:
       - **Shrivel phase (`0.0–0.25`)**: dim pale aura (`Light2D` soft flicker).
       - **Arms-Arc Light Emission phase (`0.25–0.58`)**: ramp `ghostLight` color to brilliant purification white-gold (`Color(1f, 0.94f, 0.72f)`) with peak intensity (`~3.8f`) and expanded `pointLightOuterRadius` (`~4.8f`), syncing with the skyward arm-arc rays on the sprite sheet.
       - **Burn to Ashes phase (`0.58–1.0`)**: transition `ghostLight` to warm dying ember-orange (`Color(1f, 0.35f, 0.08f)`) fading down to `0f` as the sprite burns into ashes and fades out cleanly on the final frame.
     - Restore/clear override state cleanly if reused, then execute `ghostHandler.OnGhostExorcised()` and `gameObject.SetActive(false)` as before.
3. **Update `CreateProceduralShriekClip()` & `ExorcismManager` (`HunterGhostBehavior.cs` & `ExorcismManager.cs`)**:
   - Rewrite `HunterGhostBehavior.CreateProceduralShriekClip()` to mirror the phase-accumulated dual-voice `A2–D3` + natural human-octave formant synthesis so even without the generated WAV asset, the fallback audio sounds identical.
   - Update `ExorcismManager` (`Assets/Scripts/UI/ExorcismManager.cs#L474`) so if `dyingShriekClip` is null, it resolves `GameAudioManager.GetClip("ghost_dying_shriek")` before falling back to `CreateProceduralShriekClip()`.

---

## 5. Edge Cases & Verification Checklist

1. **Multiplayer & Offline Intermission Parity**:
   - **Lobby → Map**: Host presses `▶ START INVESTIGATION` → Host and all connected Clients simultaneously display `LoadingScreenManager` with the selected map & difficulty before scene switch, hold cleanly until `OnLoadEventCompleted` and `TruckGearSpawner` complete, and fade out together with working controls. Test solo/non-listening fallback too.
   - **End Game → Reveal**: Any player completes Stage 2 on `TruckEndGameButton` → ramp closes, Jeepney departure SFX plays, `LoadingScreenManager` intermission runs *without leaking the ghost name*, then `EndGameRevealController` reveals the true ghost, deduction result, and payout cleanly.
2. **Summoning Circle Hover, Sequential Candle Ignition & Minigame Gating**:
   - Unplaced `RitualChalkItem` on the shelf/ground still shows `"Ritual Chalk x1"` and picks up normally into inventory.
   - Right-clicking inside `GhostRoomMarker` places the chalk: chalk-stick visual disappears and standalone `SummoningCircle` appears at `Frame 0` (chalk circle + 5 unlit red candles) without immediately covering the screen with `ghost-select-popup`.
   - Moving cursor onto the summoning circle traces a clean **circular** green outline via `ItemHoverIndicator` and displays `"Summon Ghost"`.
   - Clicking or pressing `[E]` opens `ExorcismManager`'s ghost selection popup; upon confirming the ghost type, the 5 red candles light **one by one clockwise around the circle** (`Frames 0 → 5`) while hover text reports `"Exorcism in Progress"`.
   - **Only once all 5 candle flames finish lighting** does the ghost manifest inside the circle and the circular skill-check minigame start, while `Frames 5..8` loop with each candle flame waving independently.
   - Verify placement fails cleanly outside `GhostRoomMarker` and replicates identically across Host and Clients.
3. **Ghost Exorcism Death Sprite Sheet & Dual-Voice Shriek**:
   - Completing the exorcism minigame with the correct ghost guess plays the 12-frame `ghost_death_sheet` animation stationary at the circle center (shrivel → arms skyward arc with bright `Light2D` flash → burn to ashes), without `GhostPixelAnimator` fighting the sprite or `HunterGhostBehavior` shaking/shrinking via transform.
   - Verify `ghost_dying_shriek.wav` plays two clearly audible overlapping voices—deep `A2–D3` (`110–147 Hz`) dark growl + natural human-octave high scream (`~294–523 Hz`)—with zero FM/phase buzz, and `GhostHandler.IsExorcised` + `GameMatchManager.IsGhostDefeated` still award the defeat bonus at End Game.
