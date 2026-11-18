# Pamahiin — Complete Game Reference & Q&A Guide

> **Last Verified Against Codebase:** November 2026 (`Assets/Scripts/`, `Assets/Scenes/`, `ProjectSettings/`)

---

## 1. High-Level Overview

- **Title:** *Pamahiin* (Filipino for *"superstitions"*)
- **Genre:** 2D Top-Down Co-op Paranormal Investigation & Exorcism Horror
- **Art Style:** 16-bit pixel art (`FilterMode.Point`, crisp PPU sprites, 2D dynamic shadows & URP lighting)
- **Setting:** Haunted Filipino locations in San Nicolas—a residential barangay house, a public cemetery (*kampo santo*), and a 3-floor high school—investigated from a mobile **Jeepney** base of operations.
- **Players:** 1–4 players (Singleplayer, LAN Discovery, or Online Relay Party via 5–8 character party codes).
- **Core Hook:** Unlike standard ghost-hunting games that end at identification, *Pamahiin* combines **20 unique Philippine folklore entities** (covering all 20 combinations of 6 evidence types) with an **active Exorcism Ritual minigame** to banish the ghost before extracting.

---

## 2. Technology Stack & Architecture

| Layer | Technology / Package | Purpose in *Pamahiin* |
|---|---|---|
| **Engine** | Unity 6 (`6000.6.8f1`) | Core runtime & editor |
| **Rendering** | Universal Render Pipeline (`com.unity.render-pipelines.universal` 17.6.0) | 2D URP lights (`Light2D`), `ShadowCaster2D`, dark atmosphere & red-moon failure post-processing |
| **Networking** | Netcode for GameObjects (`com.unity.netcode.gameobjects` 2.13.3) + Unity Transport (UTP) + Unity Services Relay (`2.4.1`) | Server-authoritative ghost AI, synced `NetworkVariable`s, LAN UDP broadcast (`47777` / port `7777`), and Online Relay (`MAX_RELAY_PEER_CONNECTIONS = 3`, 4 players max) |
| **Pathfinding** | `NavMeshPlus` + `com.unity.ai.navigation` (2.0.16) | 2D NavMesh baking, multi-floor `NavMeshLink`s, `StairTeleporter`s, and `GhostNode` waypoint graphs |
| **UI System** | Unity UI Toolkit (`UIElements` / UXML + USS) | 8-spread Field Journal, Jeepney Dashboard, Main Menu, Pause Menu, HUD, Exorcism Dial, End-Game Reveal |
| **Testing** | Unity Test Framework (`1.8.0`) | EditMode tests (e.g., `CemeteryMapTests`, `UIThemeAuditTests`) |

---

## 3. Core Gameplay Loop

1. **Briefing & Loadout (`LobbyScene`):**
   - Players join solo or via LAN/Online Relay Party Code.
   - Use the **Briefing Table (`J`)** to select the **Map**, **Difficulty**, and buy/contribute **Equipment** into the shared Jeepney pool (players start with **\$250** and 1 starter **Flashlight**).
2. **Insertion (The Jeepney):**
   - Players spawn inside the **Jeepney** (safe zone, equipment rack, `TruckDashboardController` with live night-vision CCTV feeds, team Sanity monitor, and Ghost Activity graph).
   - Grab up to **3 items** (`PlayerInventory.MaxSlots = 3`) and pick up the **Front Door/Gate Key** (`FrontDoorKeyItem`) to enter the haunted site.
3. **Locate the Ghost Room & Gather Evidence:**
   - Sweep rooms/grounds using the **Thermometer**, **EMF Reader**, **Video Camera**, **Spirit Box**, **Ghost Writing Book**, and **D.O.T.S. Projector**.
   - Every contract randomly rolls 1 of **20 ghosts**, each defined by a unique **3-evidence signature** out of 6 proofs.
4. **Manage Sanity & Survive Hunts:**
   - Darkness drains sanity (`0.75/s` base, scaled by difficulty) when outside lit `SafeZone`s.
   - When sanity drops below the ghost's hunt threshold (or a special condition/failed ritual triggers), the ghost initiates a **Hunt**: the front exit locks, lights flicker, and the ghost patrols/chases using 2D vision cones and NavMesh pathfinding.
   - Players survive by breaking line of sight and **crouching (`C`) inside `HideZone`s** (desks, graves, alcoves, closets) or blocking hunts with a **Crucifix**.
5. **Deduction & Exorcism Ritual (`ExorcismManager`):**
   - Log evidence and deduce the entity in the **Field Journal (`J`)**.
   - *(Optional high-reward step)* Use **Ritual Chalk** to draw a **Summoning Circle**, select the suspected ghost species, and complete the **Exorcism Timing Minigame** (hit **5 chants** using prompted keys `A, E, R, G, B` on the rotating dial within **2 loops** and **< 3 mistakes**).
   - **Success:** Defeats the ghost (`+$150` base reward). **Failure:** Triggers a cursed red-atmosphere hunt (`IsExorcismFailedHunt`).
6. **Extraction & Payout (`TruckEndGameButton`):**
   - Return to the Jeepney and trigger departure to see the `EndGameRevealController` breakdown and earn currency.

---

## 4. Controls (Verified from `PlayerController.cs`)

| Action | Input |
|---|---|
| **Move** | `W A S D` / Arrow Keys |
| **Sprint** | Hold `Left Shift` |
| **Crouch / Hide (in `HideZone`)** | Hold `C` |
| **Interact (Doors, Switches, Key, Pickups, Ritual)** | `E` |
| **Toggle Flashlight / Held Item** | `F` |
| **Primary Use / Drag / Place** | `Left Click` |
| **Secondary Action** | `Right Click` |
| **Drop Held Item** | `G` |
| **Open / Close Field Journal** | `J` |

---

## 5. Maps & Training Facility

| Map Name | Scene File | Description & Key Features |
|---|---|---|
| **Barangay San Nicolas — 6 Tanglewood Drive** | `BarangayMap.unity` | Filipino residential house & yard (`BSN-####` case IDs). Multi-room layout with enclosed `RoofFader`s, `FuseBox`, light switches, hiding spots, and exterior weather. |
| **Kampo Santo ng San Nicolas — Public Cemetery** | `CemeteryMap.unity` | `8.50 x 6.50` walled cemetery (`KSN-####` case IDs) featuring a rusty wrought-iron `FrontGate`, Caretaker's *Bantay* Shed, multi-tiered *Nitso* maze alleys, 3 Family Mausoleums, Balete grove, ~22 animated flickering gravestone candles (`CemeteryCandleManager`), and full-map dynamic weather with fireflies clustering around light sources. |
| **San Nicolas High School (3 Floors)** | `SchoolMap.unity` | Large 3-floor H-wing school with `FloorZone`s, bidirectional `StairTeleporter`s, multi-floor `NavMeshLink`s, connected `GhostNode` graphs, and floor-tabbed blueprints (`F1 / F2 / F3`) in the Journal. |
| **Warehouse Tutorial** | `WarehouseTutorialScene.unity` | 5-bay guided training facility covering: **Bay 1** (Flashlight & Doors), **Bay 2** (Darkness, FuseBox & Lights), **Bay 3** (All 5 Evidence Tools), **Bay 4** (Crucifix, Hunt Survival, Sanity Pills & Exorcism), **Bay 5** (Journal Deduction & Exit). |

---

## 6. Difficulty Modes (`ProgressionAndLoadoutManager.cs`)

| Difficulty | Payout Multiplier | Setup Grace | Fuse Box Start | Sanity Drain | Sanity Pills Restore | Insurance Refund (on Death) |
|---|---|---|---|---|---|---|
| **Amateur** | `1.0x` | 5 minutes | ON | Slow (base) | 40% | 50% of loadout value |
| **Intermediate** | `2.0x` | 2 minutes | OFF | 1.5x | 35% | 25% of loadout value |
| **Professional** | `3.0x` | 0 minutes | ON | 2.0x | 20% | 0% |
| **Nightmare** | `4.0x` | 0 minutes | OFF | 2.0x | 15% | 0% (relentless ghost) |

### Payout Formula
- **Base Rewards:** `\$35` per correct evidence (max 3) + `\$100` for correct ghost type + `\$150` for defeating/exorcising the ghost.
- **Total Earned:** `Round(BaseReward * DifficultyMultiplier * SurvivalMultiplier)` where `SurvivalMultiplier` is `1.0x` if alive or `0.5x` if deceased.
- **Insurance:** If deceased, `Round(LoadoutValue * InsuranceRate)` is refunded.

---

## 7. Equipment Catalog (10 Items)

| Equipment | Cost | Max Count | Purpose |
|---|---|---|---|
| **Flashlight** | \$20 | 4 | Illuminates dark rooms and reduces sanity drain. |
| **EMF Reader** | \$35 | 2 | Detects electromagnetic spikes up to **EMF Level 5**. |
| **Thermometer** | \$30 | 2 | Measures ambient room temperature & **Freezing Temps**. |
| **Spirit Box** | \$45 | 2 | Communicates with vocal entities in the dark. |
| **Ghost Writing Book** | \$40 | 2 | Place on the ground for entities to write in. |
| **D.O.T.S. Projector** | \$55 | 2 | Projects a laser grid that reveals **D.O.T.S.** silhouettes. |
| **Video Camera** | \$50 | 4 | Streams live night-vision feeds & **Ghost Orbs** to the Jeepney. |
| **Crucifix** | \$60 | 2 | Prevents the ghost from initiating a hunt nearby. |
| **Sanity Pills** | \$45 | 4 | Restores sanity when consumed (scaled by difficulty). |
| **Ritual Chalk** | \$50 | 2 | Required to draw the exorcism circle and defeat the ghost. |

---

## 8. The 20 Philippine Folklore Ghosts (`GhostCatalog.cs`)

There are **6 evidence types** (`EMF Level 5`, `Freezing Temps`, `Ghost Writing`, `D.O.T.S. Projector`, `Spirit Box`, `Ghost Orbs`), and $\binom{6}{3} = 20$ unique 3-evidence combinations—one for each ghost:

| # | Ghost | Subtitle | 3 Evidences | Hunt Threshold / Special Condition |
|---|---|---|---|---|
| 1 | **Tikbalang** | The Forest Trickster (*Mapaglarong Nilalang*) | EMF, Spirit Box, Ghost Orbs | 45% (65% if player moving nearby) |
| 2 | **Kapre** | The Balete Giant (*Higante sa Dilim*) | EMF, Freezing, D.O.T.S. | 50% (60% if player in ghost room) |
| 3 | **Manananggal** | The Severed Predator (*Lumilipad na Aswang*) | Freezing, Ghost Writing, Ghost Orbs | 70% (Early Hunt Ability: 8% chance) |
| 4 | **Multo** | The Restless Spirit (*Ligaw na Kaluluwa*) | Freezing, Spirit Box, Ghost Orbs | 50% (Always) |
| 5 | **Kaperosa** | White Lady (*Kaperosa sa Daan*) | Freezing, D.O.T.S., Ghost Orbs | 50% (Single Obsession Target) |
| 6 | **Pasatsat** | The Shrouded Corpse (*Balot sa Banig*) | Freezing, Ghost Writing, D.O.T.S. | 50% (Always) |
| 7 | **Kalag** | The Wandering Soul (*Kalag ng Yumao*) | Ghost Writing, Spirit Box, Ghost Orbs | 55% (35% when grouped in ghost room) |
| 8 | **Kumakatok** | The Omen Knockers (*Tagakatok ng Tadhana*) | EMF, Ghost Writing, Spirit Box | 45% (60% on recent interior door interaction) |
| 9 | **Akop** | The Clinging Dread (*Mapang-akit na Anino*) | EMF, Freezing, Ghost Orbs | 40% (60% when solo in house/compound) |
| 10 | **Ibwa** | The Grave Stalker (*Anino sa Lamay*) | EMF, Freezing, Ghost Writing | 50% (Always) |
| 11 | **Paring Pugot** | Headless Priest (*Paring Walang Ulo*) | EMF, Ghost Writing, D.O.T.S. | 50% (65% when Summoning Circle placed) |
| 12 | **Amalanhig** | The Risen Corpse (*Buhay na Bangkay*) | EMF, Freezing, Spirit Box | 60% (Always) |
| 13 | **Santelmo** | Apoy ni San Telmo (*Spirit Fire*) | EMF, D.O.T.S., Ghost Orbs | 60% (40% when ghost room lights are ON) |
| 14 | **Bagat** | The Crossroads Omen (*Halimaw sa Dilim*) | EMF, D.O.T.S., Spirit Box | 50% (65% if player sprinting nearby) |
| 15 | **Mantiw** | The Towering Specter (*Matangkad na Multo*) | Ghost Writing, D.O.T.S., Ghost Orbs | 40% (Always) |
| 16 | **Batibat** | The Nightmare Hag (*Tagapagdala ng Bangungot*) | Freezing, Ghost Writing, Spirit Box | 60% (40% when ghost room lights are ON) |
| 17 | **Tiyanak** | The Crying Fiend (*Demonyong Sanggol*) | Freezing, D.O.T.S., Spirit Box | 65% (Always) |
| 18 | **Tamawo** | The Fair Trickster (*Engkanto ng Kalikasan*) | D.O.T.S., Spirit Box, Ghost Orbs | 40% (Always) |
| 19 | **Mangkukutod** | The Hex Weaver (*Sumpa ng Dilim*) | EMF, Ghost Writing, Ghost Orbs | 55% (70% on failed ritual) |
| 20 | **Tagamaling** | The Lunar Fiend (*Halimaw sa Bilog ng Buwan*) | Ghost Writing, D.O.T.S., Spirit Box | 35% (65% on Lunar Phase Cycle) |

---

## 9. Q&A Preparation — Likely Questions & Talking Points

### Q1: What makes *Pamahiin* unique compared to other ghost-hunting games like *Phasmophobia*?
- **Philippine Folklore & Culture:** Instead of generic Western ghosts, all 20 entities are rooted in Filipino mythology (*Tikbalang*, *Kapre*, *Manananggal*, *Tiyanak*, *Paring Pugot*, etc.), set in authentic Filipino environments (a Barangay house, *Kampo Santo* cemetery with *nitso* niches, and a 3-floor high school), with a **Jeepney** replacing the standard van.
- **Active Exorcism Mechanic:** Players don't just identify the ghost and leave—they can use **Ritual Chalk** to draw a Summoning Circle and perform a high-stakes **Exorcism Timing Minigame**. Failing it triggers a cursed red-atmosphere hunt.
- **2D Top-Down Tactical Horror:** Translates 3D horror into 2D using hard dynamic shadows (`ShadowCaster2D`), directional visibility, `RoofFader` building interiors, and crouch-hiding behind cover (`HideZone`).

### Q2: Why did you choose a 2D top-down perspective instead of 3D?
- It allows crisp 16-bit pixel-art atmosphere while keeping system requirements accessible.
- Horror is preserved through **strict information restriction**: 2D URP dynamic shadows cast hard darkness behind walls and furniture, `RoofFader` hides building interiors until entered, and players must rely on audio cues, Jeepney CCTV feeds, and narrow tool cones.

### Q3: How does Ghost AI and hiding work in 2D?
- **Navigation:** Ghosts use a baked 2D **NavMesh** (`NavMeshPlus`) combined with a connected `GhostNode` waypoint graph (and `NavMeshLink`s / `StairTeleporter`s on the 3-floor school map).
- **Investigation Bounds:** Indoor buildings use `RoofFader` bounds, while outdoor maps like `CemeteryMap` use `InvestigationZone` colliders so the ghost can hunt across graves and *nitso* alleys without escaping onto the road or into the Jeepney.
- **Hiding:** Designated `HideZone` triggers (desks, mausoleum benches, Balete roots, alleys) make a player invisible to ghost vision raycasts when holding **Crouch (`C`)** (`IsHiddenFromGhost`).

### Q4: How does Multiplayer work?
- Built on **Unity Netcode for GameObjects (NGO)** with **Unity Transport (UTP)**.
- Supports **Singleplayer**, **LAN Auto-Discovery** (UDP port `47777`), and **Online Co-op via Unity Relay** using a 5–8 character Party Code for up to **4 players** (1 Host + 3 Clients).
- Gameplay state (ghost room, evidence, sanity, door states, candle bitmasks, exorcism lock, and Jeepney loadout) is **server-authoritative** and synced via `NetworkVariable`s and RPCs.

### Q5: How do you onboard new players?
- We built a dedicated **5-Bay Warehouse Tutorial** (`WarehouseTutorialScene`) that walks players step-by-step through movement/flashlights, breaker/lighting mechanics, every evidence tool, surviving a hunt with a Crucifix/Sanity Pills, performing an Exorcism, and logging deductions in the Field Journal.

### Q6: How do you ensure code quality and stability?
- **Single Source of Truth:** `GhostCatalog.cs` mathematically guarantees all $\binom{6}{3} = 20$ evidence combinations are unique and consistent across AI, Journal, and Exorcism UI.
- **Automated EditMode Tests:** `CemeteryMapTests.cs` validates scene registration, NavMesh pathability, spawn points, and doors; `UIThemeAuditTests.cs` enforces consistent 16-bit UI Toolkit styling across all `.uss` files.
