# Pamahiin — Complete Game & Technical Defense Documentation (For Judges)

> **Purpose of This Document:** A comprehensive, non-code reference explaining what *Pamahiin* is, every core feature and function in the game, what technologies, Unity packages, algorithms, and custom tools were used to build each part, and clear answers to anticipated judge questions.

---

## 1. Executive Summary — What Is *Pamahiin*?

- **Title:** *Pamahiin* (Filipino for *"superstitions"*)
- **Genre:** 1–4 Player Co-op 2D Top-Down Paranormal Investigation & Exorcism Horror
- **Art & Visual Direction:** 16-bit top-down pixel art (`FilterMode.Point`, pixel-per-unit calibrated sprites) combined with modern 2D Universal Render Pipeline (URP) dynamic lighting, hard real-time shadows, and atmospheric post-processing.
- **Cultural & Thematic Identity:** Instead of generic Western haunted houses and vans, *Pamahiin* grounds every system in **Philippine folklore and local settings**:
  - **Mobile Base:** A Filipino **Jeepney** replaces the traditional investigation van.
  - **20 Folklore Entities:** Every ghost is drawn from Filipino mythology and superstition (*Tikbalang*, *Kapre*, *Manananggal*, *Multo*, *Kaperosa*, *Pasatsat*, *Kalag*, *Kumakatok*, *Akop*, *Ibwa*, *Paring Pugot*, *Amalanhig*, *Santelmo*, *Bagat*, *Mantiw*, *Batibat*, *Tiyanak*, *Tamawo*, *Mangkukutod*, and *Tagamaling*).
  - **Local Haunting Grounds:** A residential barangay house (*Barangay San Nicolas — 6 Tanglewood Drive*), a traditional Filipino public cemetery (*Kampo Santo ng San Nicolas*) with multi-tiered *nitso* niches and *bantay* shed, and a 3-floor local high school (*San Nicolas High School*), plus a 5-bay *Warehouse Tutorial*.
- **Core Differentiator (Investigation + Active Exorcism):** Standard ghost-hunting games end once the player guesses the ghost's name and leaves. *Pamahiin* adds a high-risk **Exorcism Ritual** climax: players draw a summoning circle with **Ritual Chalk**, declare the ghost species, and complete a skill-based timing ritual to banish the entity—or suffer a cursed red-atmosphere hunt if they fail.

---

## 2. Technology Stack — "What Did We Use For What?"

| System / Layer | Exact Technology / Package Used | Why We Chose It & How It Is Used |
|---|---|---|
| **Game Engine** | **Unity 6 (`6000.6.8f1`)** | Provides native 2D tooling, URP 2D rendering, UI Toolkit, and modern C# runtime performance. |
| **Rendering & Lighting** | **Universal Render Pipeline (`com.unity.render-pipelines.universal` 17.6.0)** | Powers 2D `Light2D` (global darkness, player flashlights, room lights, flickering candles, D.O.T.S. cones, fireflies), `ShadowCaster2D` for walls/furniture blocking light, and URP Post-Processing `Volume`s (vignette, chromatic aberration, CCTV night vision, ghost spectator tint). |
| **Multiplayer & Networking** | **Unity Netcode for GameObjects (`com.unity.netcode.gameobjects` 2.13.3)** + **Unity Transport (UTP)** + **Unity Services Multiplayer / Relay (`2.4.1`)** | Runs a **server-authoritative** multiplayer simulation for up to 4 players (1 Host + up to 3 Clients) across Singleplayer, LAN auto-discovery (UDP broadcast on port `47777`), and Online Party Codes via Unity Relay without requiring port forwarding. |
| **Client Movement Sync** | Custom **Client-Authoritative `ClientNetworkTransform`** + `Rigidbody2D` | Gives each player responsive, zero-lag local movement (`MovePosition`) while syncing positions cleanly to other clients and supporting instant floor/spawn teleports. |
| **AI Pathfinding & Multi-Floor Navigation** | **NavMeshPlus** + **Unity AI Navigation (`com.unity.ai.navigation` 2.0.16)** + Custom **`GhostNode`** Graph | Bakes walkable 2D navigation meshes around walls and props, connects multi-floor areas using bidirectional `NavMeshLink`s and `StairTeleporter`s, and provides a room-to-room `GhostNode` waypoint fallback for patrols, hunts, and stalking. |
| **User Interface** | **Unity UI Toolkit (`UIElements` — UXML + USS)** + **Unity UGUI** (for full-screen overlays) | UI Toolkit powers the 8-spread Field Journal, Jeepney Dashboard, Main Menu, Lobby, Pause Menu, HUD hotbar, Exorcism Dial, and End-Game Reveal with unified 16-bit theming; UGUI Canvas powers full-screen death/jumpscare sprite-sheet cutscenes. |
| **Input Handling** | **Unity Input System (`com.unity.inputsystem` 1.20.1)** + **Cinemachine (`6.6.0`)** / Custom `CameraFollow` | Handles player movement, sprint, crouch/hide, interactions, item drag/placement, and UI navigation; `CameraFollow` keeps the top-down camera locked to the local player (or burial stage during death) with a Z=0 child `2D_AudioListener` so 3D proximity audio distances remain accurate. |
| **2D Sprites, Animation & Tilemaps** | **Unity 2D Suite** (`2d.animation`, `2d.sprite`, `2d.tilemap`, `2d.aseprite`, `2d.psdimporter`) + **Custom Procedural C# Asset & Blueprint Generators** | All pixel-art tiles, props, silhouettes, and Journal floorplan blueprints for Barangay, Cemetery, and School maps are deterministically generated via custom Editor C# pixel-canvas tools (`*AssetGenerator.cs`, `*BlueprintGenerator.cs`, `*SceneBuilder.cs`) for visual consistency and rapid iteration. |
| **Audio Engine** | **Unity Audio (`AudioSource`, `AudioMixer`)** + Custom **`GameAudioManager`** | Uses 3D spatialized proximity `AudioSource`s, dynamic floor-attenuation (muting sounds across floors), weather audio clarity multipliers, heartbeat proximity scaling, and procedural/SFX synthesis. |
| **Persistence / Save Data** | **Unity `PlayerPrefs`** | Persists player currency, purchased loadout inventory, selected map/difficulty, audio levels, graphics quality, fullscreen, FPS toggle, and tutorial hint preferences locally across sessions. |
| **Automated Quality Assurance** | **Unity Test Framework (`1.8.0`)** + Custom EditMode Test Suites | 9 automated EditMode test suites verify ghost evidence uniqueness, scene architecture, NavMesh links, UI theme rules, tutorial bays, death/spectator layers, and D.O.T.S. silhouettes. |

---

## 3. Core Gameplay Loop (Step-by-Step)

1. **Main Menu & Matchmaking (`MainMenuScene` / `LobbyScene`):**
   - Players choose **Solo**, **Host Online (Unity Relay Party Code)**, **Join via Party Code**, or **LAN Auto-Discovery**.
   - In the Lobby, players interact with the **Briefing Table (`J`)** to select the **Map**, **Difficulty**, and buy/contribute **Equipment** from their personal currency (starting at **\$250** + 1 starter **Flashlight**) into the shared Jeepney pool.
2. **Loading & Insertion (`LoadingScene` -> Map Scene):**
   - A custom animated loading screen (`LoadingScreenManager`) displays a pixel-art Jeepney driving at night while the target map loads additively and synchronizes all networked objects.
   - Players spawn inside the **Jeepney**, a permanent `SafeZone` stocked with the team's equipment rack, the **Front Door/Gate Key**, and the **Jeepney Dashboard** (CCTV, Sanity, and Activity monitors).
3. **Exploration & Locating the Ghost Room:**
   - Players carry up to **3 items** (`PlayerInventory`) and use the key to unlock the building or cemetery gate (which triggers ghost activation and starts the setup grace timer).
   - Players sweep rooms or exterior zones using **Thermometer**, **EMF Reader**, **Video Camera**, **Spirit Box**, **Ghost Writing Book**, and **D.O.T.S. Projector** to pinpoint the ghost's favorite room and collect **3 out of 6** possible evidence types.
4. **Sanity Management & Hunt Survival:**
   - Being in unlit areas drains player **Sanity** (scaled by difficulty). Players restore sanity using **Sanity Pills** or pause drain by staying in lit rooms or the Jeepney.
   - When team average sanity drops below the ghost's **Hunt Threshold** (or a ghost-specific condition/failed exorcism triggers), the ghost initiates a **Hunt**: the front entrance locks, lights flicker, and the ghost chases players via line-of-sight raycasts and NavMesh navigation.
   - Players survive by breaking line of sight and **crouching (`C`) inside `HideZone`s** (closets, restroom stalls, lockers, desks, mausoleum benches, Balete roots) or blocking hunts proactively with a **Crucifix**.
5. **Deduction & Optional Exorcism Ritual:**
   - Players record discovered evidence in the **Field Journal (`J`)**, which automatically narrows down the **20 ghosts** to matching candidates.
   - *(Optional High-Reward Climax)* Players can draw a **Summoning Circle** using **Ritual Chalk**, select the ghost species they believe is haunting the site, and complete the **Exorcism Timing Minigame** (`ExorcismManager`): pressing prompted keys (`A`, `E`, `R`, `G`, `B`) as a rotating needle crosses the target zone **5 times** within **2 full rotations** and **fewer than 3 mistakes**.
   - **Success:** The ghost is trapped inside the burning circle, shriek-banished, and defeated (**+\$150 base bonus**).
   - **Failure (or Wrong Ghost Selected):** Triggers a cursed **Failed-Exorcism Hunt** with crimson lighting and heightened aggression.
6. **Extraction & Payout (`TruckEndGameButton` -> `EndGameRevealController`):**
   - Surviving players return to the Jeepney and press the departure button to extract. The End-Game Reveal screen breaks down evidence bonuses, ghost identification accuracy, exorcism bonus, difficulty multiplier, survival multiplier, and insurance refunds (if deceased).

---

## 4. Detailed Breakdown of Every System & Function

### 4.1 Maps & Architectural Systems
1. **Barangay San Nicolas — 6 Tanglewood Drive (`BarangayMap`):**
   - Multi-room residential house with porch, living areas, bedrooms, bathroom, garage, and a **Basement** connected via bidirectional `StairTeleporter`s and a `FuseBox`.
   - Uses `RoofFader` triggers so building roofs stay opaque from outside and smoothly fade out only when the local player enters (or views a CCTV camera inside).
2. **Kampo Santo ng San Nicolas — Public Cemetery (`CemeteryMap`):**
   - An outdoor `8.50 x 6.50` walled cemetery featuring a rusty wrought-iron `FrontGate`, Caretaker's *Bantay* Shed, multi-tiered *Nitso* niche alleys, 3 Family Mausoleums, Balete grove, and ~22 flickering gravestone candles (`CemeteryCandleManager`) synced over the network via bitmasks.
   - Uses `InvestigationZone` colliders so outdoor cemetery lanes count as valid ghost-hunting bounds while keeping the roadside and Jeepney safe.
3. **San Nicolas High School — 3 Floors (`SchoolMap`):**
   - Large 3-story H-wing school layout (`Floor 1`, `Floor 2`, `Floor 3`) with classrooms, multipurpose theater hall, library, computer labs, dean's office, workshop (`FuseBox`), locker alcoves, and 4 corner restrooms with stall hide spots.
   - **Multi-Floor Mechanics:**
     - `FloorZone` volumes track which floor (Exterior, F1, F2, F3) every player, prop, light, and ghost occupies.
     - **8 Bidirectional `StairTeleporter` Pairs** move players seamlessly across F1 ↔ F2 ↔ F3 with screen fade-to-black transitions, stair footstep audio, and anti-ping-pong cooldowns.
     - **8 Bidirectional `NavMeshLink`s** allow the ghost AI to traverse between floors when patrolling or chasing.
     - **Tabbed Blueprint Map** in the Field Journal lets players switch between `F1`, `F2`, and `F3` floorplans.
4. **5-Bay Warehouse Tutorial (`WarehouseTutorialScene`):**
   - Guided, self-paced onboarding map split into 5 bays:
     - **Bay 1:** Movement, Flashlight (`F`), and Door interaction (`E`).
     - **Bay 2:** Darkness sanity drain, `FuseBox` breaker restoration, and room light switches.
     - **Bay 3:** Hands-on testing of all evidence tools (EMF, Thermometer, Spirit Box, Ghost Writing Book, Video Camera, D.O.T.S. Projector).
     - **Bay 4:** Hunt survival, `HideZone` crouching (`C`), Crucifix repel, Sanity Pills, and practicing an Exorcism Ritual.
     - **Bay 5:** Field Journal deduction and extraction console.

### 4.2 The 20 Ghosts & Evidence Matrix (`GhostCatalog.cs`)
- **Mathematical Design:** There are **6 Evidence Types**:
  1. `EMF Level 5`
  2. `Freezing Temps`
  3. `Ghost Writing`
  4. `D.O.T.S. Projector`
  5. `Spirit Box`
  6. `Ghost Orbs`
- Because $\binom{6}{3} = 20$, **every single 3-evidence combination maps uniquely to 1 of the 20 ghosts** with zero duplicates and zero ambiguous overlap.
- **Unique Ghost Personalities & Hunt Rules:** Each ghost also carries distinct behavioral traits (for example, *Manananggal* has a 70% threshold and early hunt chance; *Santelmo* and *Batibat* drop their hunt threshold from 60% to 40% when ghost-room lights are ON; *Kaperosa* fixates on a single obsession target; *Tagamaling* spikes on lunar phase cycles; *Mangkukutod* spikes to 70% after a failed ritual).

### 4.3 Ghost AI & Paranormal Simulation (`GhostHandler`, `HunterGhostBehavior`, `EventGhostBehavior`, `ParanormalManager`)
- **Server-Authoritative State Machine:** Only the host/server runs ghost decisions (picking the random favorite room from `GhostRoomMarker`s, selecting the ghost species, rolling activity timers, starting hunts, and choosing kill targets), eliminating desyncs between players.
- **Patrol, Events & Roaming (`EventGhostBehavior` & `ParanormalManager`):**
  - Interacts with nearby `GhostPropInteractable`s (knocking over vases, rattling objects, triggering EMF spikes), toggles doors (`NetworkDoor`), flips light switches or trips the `FuseBox` breaker when overloaded, drops room temperatures in the ghost room, writes in `GhostWritingBookItem`s, spawns `GhostOrbSystem` particles, and manifests `DotsProjectorItem` silhouettes.
- **Hunting & Line-of-Sight (`HunterGhostBehavior`):**
  - Checks team sanity, Crucifix protection radius (`CrucifixItem`), and grace/setup timers before starting a hunt.
  - Uses 2D vision cones and `Physics2D` raycasts against `Walls` to detect visible, non-hiding players.
  - Uses `NavMeshAgent` (via `NavMeshPlus`) + `GhostNode` graphs + multi-floor `NavMeshLink`s to pursue targets, speed up on continuous line of sight, and search last-known positions when sight is broken.
  - **Crouch-Hiding (`HideZone`):** If a player enters a `HideZone` and holds Crouch (`C`), `IsHiddenFromGhost` blocks ghost sight detection (unless the ghost already has direct uninterrupted line of sight entering the hiding spot).

### 4.4 Player Mechanics, Interaction & Death/Spectator Flow
- **Movement & Controls (`PlayerController`):** Smooth 2D `Rigidbody2D` movement (`WASD`), stamina-managed Sprint (`Left Shift`), Crouch/Hide (`C`), mouse-aimed directional rotation (so flashlights and tool cones follow the cursor), and wall-clamped reach radius indicators.
- **3-Slot Inventory & Physics-Safe Dragging (`PlayerInventory`, `EquipmentItem`):**
  - Players hold up to **3 items** (`1/2/3` keys or scroll wheel), can **use (`F` / `Left Click`)**, **place** cameras/books/D.O.T.S./chalk onto floors (`Left Click`), **drag** placed gear within reach (clamped by wall raycasts), or **drop (`G`)** items.
- **Death Cutscene & Ghost Spectator Mode (`DeathSequenceController`, `PlayerCorpse`, `CorpseRagdollController`):**
  - When a ghost catches a player, the victim drops all carried gear, spawns an interactive `PlayerCorpse` / ragdoll (which teammates can inspect or drag), and plays a **local multi-stage 16-bit horror cutscene**:
    1. *"Psst!"* audio cue behind the victim.
    2. 20-frame creeping 16-bit Ghost Hands overlay crushing the screen to black.
    3. Hard cut to an off-bounds **Burial Pit Stage** (`BurialMapStage`) with damp grave ambience and a lunging ghost + 12-frame Jumpscare shriek.
    4. Transition into **`GhostSpectator` state**: the dead player moves onto the dedicated `GhostSpectator` physics layer (collides with walls and `StairTeleporter`s, passes harmlessly through ghosts and props, sees cyan-tinted fellow spectators, cannot pick up items or interfere with gameplay, and waits for team extraction or a Team Wipe grace timer).

### 4.5 Dynamic Weather, Audio & Immersion Systems
- **Server-Authoritative Dynamic Weather (`WeatherManager`):**
  - Rolls weather at match start and syncs it to all clients:
    - **48% Night + Fireflies:** Ambient firefly particles + hero `Light2D` fireflies that naturally gravitate toward outdoor lamp posts, candles, and floodlights.
    - **25% Foggy:** Reduced outdoor light radius, atmospheric fog wisps, and slightly crisper indoor audio (`1.2x` clarity).
    - **25% Rain & Thunderstorm:** Animated rain streaks, ground puddles, periodic lightning flashes with thunder audio, and outdoor footstep volume ducking.
    - **2% Red Moon (Rare Event):** Crimson sky tint, faster ghost event intervals (`0.55x` interval), and extended hunt durations (`1.35x`).
- **Floor-Aware 3D Proximity Audio (`GameAudioManager`, `PlayerFootstepAudio`):**
  - Differentiates surface footsteps (`FloorSurface`: wood, tile, dirt, stone, metal) and attenuates/mutes sound sources on other floors using `FloorZone` checks so players on Floor 1 don't get confused by footsteps on Floor 3.
  - **Proximity Heartbeat & Camera Pulse (`GhostAtmosphereController`):** Scales heartbeat volume/tempo and URP Vignette + subtle camera zoom-in (`0.10x`) based on ghost distance during a hunt, stopping the instant a kill occurs.

### 4.6 Jeepney Base, Field Journal & Progression UI
- **Jeepney Dashboard (`TruckDashboardController`):**
  - Interactive monitor inside the Jeepney with 3 live tabs:
    1. **CCTV Feed:** Renders a live `RenderTexture` from placed `VideoCameraItem`s (auto-cycling cameras, hiding `RoofFader` roofs for the viewed room, and toggling **Night Vision** `Light2D` + green post-processing filter to spot **Ghost Orbs**).
    2. **Team Sanity Monitor:** Live per-player sanity readout.
    3. **Ghost Activity Monitor:** Real-time paranormal activity bar/level readout.
- **8-Spread Field Journal (`JournalController`):**
  - Accessible anywhere with `J`: Cover, Table of Contents, Map Blueprint (with F1/F2/F3 floor tabs on SchoolMap), Equipment Guide, 20-Ghost Compendium (with Filipino folklore lore, sigils, and behavior hints), Evidence & Deduction Matrix (interactive checkbox filtering that strikes out impossible ghosts), and Lobby/Shop spread.
- **Difficulty Modes & Payout Economics (`ProgressionAndLoadoutManager`):**
  - **4 Difficulties:** **Amateur** (`1.0x`, 5m setup grace, FuseBox ON, 40% pills, 50% death insurance), **Intermediate** (`2.0x`, 2m grace, FuseBox OFF, 35% pills, 25% insurance), **Professional** (`3.0x`, 0m grace, FuseBox ON, 20% pills, 0% insurance), and **Nightmare** (`4.0x`, 0m grace, FuseBox OFF, 15% pills, 0% insurance, relentless ghost).
  - **Payout Formula:** `Round((Evidence * $35 + CorrectGhost * $100 + ExorcismDefeat * $150) * DifficultyMultiplier * SurvivalMultiplier)` where SurvivalMultiplier is `1.0x` (alive) or `0.5x` (deceased), plus Loadout Insurance refund if deceased.
- **Multiplayer Moderation & Settings (`VoteKickManager`, `PauseMenuController`, `GameSettingsManager`, `DeveloperConsoleManager`):**
  - Host direct-kick and democratic client vote-kick (`>= 50%` of non-host clients), full audio/graphics/accessibility toggles, and a built-in Developer Console for live QA testing (speed boost, weather override, force hunt, teleport-to-ghost/player).

---

## 5. Automated Testing & Engineering Rigor

We built **9 EditMode test suites** under `Assets/Tests/EditMode/` using **Unity Test Framework (`1.8.0`)** to prevent regressions across maps, UI, and game rules:

1. **`GhostCatalogTests.cs`** — Verifies all 20 ghosts have unique 3-evidence combinations out of 6 evidence types and valid catalog metadata.
2. **`SchoolMapArchitectureTests.cs`** — Validates all 3 floors, 16 bidirectional `StairTeleporter`s with arrival clearance, 8 `NavMeshLink`s, 3 `FloorZone`s, `RoofFader`s, `FuseBox`, `HideZone`s, and `GhostRoomMarker`s.
3. **`CemeteryMapTests.cs`** — Validates Cemetery scene build registration, NavMesh coverage, `InvestigationZone`s, spawn points, doors, and candles.
4. **`WarehouseTutorialTests.cs`** — Validates all 5 tutorial bays, sequential progression gates, and interactable triggers.
5. **`PlayerDeathSpectatorTests.cs`** — Verifies `GhostSpectator` physics layer isolation (ignores ghosts/props, collides with walls/stairs), corpse drag wall clamping, team-wipe grace durations (`3s` multiplayer / `15s` solo), and blocking dead spectators from picking up items.
6. **`DotsProjectorSilhouetteTests.cs`** — Validates D.O.T.S. cone projection and fade envelope math.
7. **`UIThemeAuditTests.cs`** — Audits `.uss` stylesheets across UI Toolkit screens to enforce consistent 16-bit palette, typography, and layout standards.
8. **`LoadingSceneTests.cs`** — Validates loading screen transitions and Jeepney road backdrop setup.
9. **`PlaytesterFeedbackUxTests.cs`** — Verifies UX fixes for reach indicators, hover labels, and feedback prompts.

---

## 6. Judge Q&A Cheat Sheet (Quick Verbal Talking Points)

- **"What engine and rendering pipeline did you use?"**
  - Unity 6 (`6000.6.8f1`) with the **Universal Render Pipeline (URP) 2D**. We chose 2D URP because `Light2D` and `ShadowCaster2D` let us create genuine horror tension—walls and furniture cast real-time hard shadows so players only see what their flashlight or room lights illuminate.
- **"How did you implement multiplayer?"**
  - **Unity Netcode for GameObjects (NGO)** over **Unity Transport (UTP)**. It supports Solo, automatic **LAN discovery** via UDP broadcast (`47777`), and **Online Co-op** via **Unity Relay** party codes (1 Host + up to 3 Clients) with server-authoritative ghost AI and client-authoritative player movement (`ClientNetworkTransform`).
- **"How does ghost pathfinding work across a 3-floor high school in 2D?"**
  - We use **NavMeshPlus** (Unity's 2D NavMesh extension) combined with bidirectional **`NavMeshLink`s** across floors for the ghost, **`StairTeleporter`s** with screen-fade transitions for players, **`FloorZone`s** to isolate sound and weather per floor, and a **`GhostNode`** graph so the ghost patrols rooms intelligently.
- **"How did you create the 2D pixel art and maps so consistently?"**
  - We wrote **procedural C# pixel-art and blueprint generators** in `Assets/Editor/` (`BarangayAssetGenerator`, `CemeteryAssetGenerator`, `SchoolAssetGenerator`, and matching `*BlueprintGenerator` and `*SceneBuilder` tools) alongside Unity's 2D Sprite/Tilemap tools. Every sprite is point-filtered at uniform PPU, and every Journal blueprint is generated directly from the exact world coordinates of the map.
- **"What makes *Pamahiin* stand out from *Phasmophobia*?"**
  - Three things: (1) **100% Filipino folklore & settings**—20 local entities, a Jeepney base, *nitso* cemetery, and barangay/school architecture; (2) **2D tactical stealth horror** using real-time shadow cones and crouch-hiding behind cover; and (3) an **active Exorcism Ritual** where players don't just identify the ghost—they risk drawing a chalk circle and completing a timing minigame to banish it.
- **"How do dead players stay engaged without breaking game balance?"**
  - After a 16-bit Ghost Hands + Burial Pit jumpscare sequence, dead players enter **Ghost Spectator Mode** on an isolated physics layer (`GhostSpectator`). They can still roam across all floors (using stairs), observe surviving teammates in cyan spectator vision, and wait for extraction—while being strictly prevented from picking up equipment or interacting with live gameplay objects.
- **"How did you test and ensure the game works reliably?"**
  - Besides live multiplayer playtesting and an in-game Developer Console, we wrote **9 automated EditMode test suites** (Unity Test Framework) that automatically verify our 20-ghost evidence math, NavMesh/stair wiring, UI theme consistency, tutorial flow, and death/spectator physics rules.
