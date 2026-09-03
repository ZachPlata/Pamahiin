# Game Design Document: 2D Top-Down Ghost Hunting (*Pamahiin*)

## 1. Core Gameplay Loop in 2D
The loop mirrors the rising tension of 3D ghost hunting, adapted for a top-down perspective where spatial awareness and resource management drive the horror.

- **Insertion (The Van):** Players spawn in a safe zone, view the objective board, select limited loadouts, and monitor map-wide sanity/activity feeds.
- **Sweep & Locate:** Players enter the structure. Because the 2D Field of View (FOV) restricts vision, players must coordinate sweeping room by room. Finding the "Ghost Room" relies on audio cues (footsteps, item throws) and localized temperature drops.
- **Evidence Gathering:** Players deploy static and active tools in the target room. The top-down view requires strategic placement of static tools (like cameras or DOTS) to maximize room coverage, turning the ghost room into a monitored kill-box.
- **The Hunt (Survival):** The ghost locks the exits. Players must navigate out of the ghost's FOV and find line-of-sight blockers. Survival is entirely dependent on map knowledge and remaining completely silent/stationary behind cover.
- **Extraction:** Players log their deduced ghost type and escape in the van to earn currency for loadout upgrades.

---

## 2. Translating 3D Mechanics to 2D
To maintain a terrifying atmosphere without a first-person perspective, we must restrict player information using a strict Fog of War and FOV system.

### Vision, Lighting, and Hiding
- **True FOV & Raycasting:** Players emit a 2D raycast cone (e.g., 90 degrees forward). Everything outside this cone is entirely black or shrouded in heavy fog. Flashlights dictate the length and width of this cone.
- **Dynamic Shadows:** Walls, doors, and tall furniture cast hard, infinite shadows. A ghost interacting with something just inside a shadow adds immense psychological horror.
- **Line-of-Sight (LOS) Hiding:** Objects have "height" tags:
  - A *tall wardrobe* blocks player vision and ghost vision entirely.
  - A *low desk* allows the player to see over it while standing, but features a "Hide Zone" trigger. If a player crouches behind the desk, their collision layer changes, causing the ghost's vision raycasts to pass over them.

### Equipment Adaptations
| Equipment | 2D Implementation Strategy |
| :--- | :--- |
| **EMF Reader** | A radial UI element around the player. As the player gets closer to an interaction point, directional blips appear on the radius (like a localized sonar), culminating in a solid red ring for EMF 5. |
| **Spirit Box** | The player holds a button and uses proximity voice chat. If successful, the ghost replies with spatial audio distortion and a momentary, localized UI waveform on the device itself. |
| **D.O.T.S. Projector** | Projects a 2D laser grid across the floor and walls of a room. When the ghost enters the grid, its invisible 2D sprite is rendered purely as a silhouette interacting with the laser matrix. |
| **Thermometer** | Projects a short, narrow detection cone. Sub-zero temperatures trigger a particle effect around the player's character model (visible breath), visible even in the dark. |

---

## 3. Ghost AI & State Machine
The ghost operates on a strictly defined state machine, utilizing a 2D NavMesh for organic, non-grid-bound movement that allows it to cut corners and move unpredictably.

- **Wander State:** The ghost selects a random NavMesh node within a set radius of its "Favorite Room." It travels there at low speed.
- **Interact State:** Triggered randomly during Wander. The ghost casts a short radius check for interactables (doors, light switches, cups) and triggers their specific animation/audio event.
- **Evidence State:** Based on the ghost type, it periodically rolls a chance to emit evidence (e.g., dropping the temperature, writing in a placed book, or passing through the DOTS grid).
- **Hunt State:**
  - **Phase 1 (Manifest):** Ghost locks the front door, flickers global lighting, and spawns at a NavMesh node out of player sight.
  - **Phase 2 (Search):** Ghost moves to the location where it last heard/saw a player. It casts a 360-degree short-range proximity check and a long-range 120-degree vision cone.
  - **Phase 3 (Chase):** If a player's hitbox collides with the ghost's vision cone, the ghost increases speed and paths directly to the player's current coordinate. If the player breaks LOS (turns a corner), the ghost paths to that exact corner and reverts to Phase 2.

---

## 4. Technology Stack & Networking
| Engine | Pros for 2D Ghost Hunting | Cons | Recommendation |
| :--- | :--- | :--- | :--- |
| **Godot 4** | Incredible built-in 2D lighting engine (native performant 2D shadows). Extremely lightweight. Native 2D NavMesh. Solid high-level multiplayer API. | Third-party integrations for advanced spatial audio (FMOD/Wwise) require community plugins. | Primary Choice for from-scratch 2D lighting and shadows. |
| **Unity** | Universal Render Pipeline (URP) handles 2D lights well. Netcode for GameObjects (NGO) is robust. Industry standard for FMOD/Wwise integration (crucial for horror). | Heavier engine overhead. 2D shadow caster setup can be finicky compared to Godot. | **Active Project Stack** for *Pamahiin*. |
| **GameMaker** | Phenomenal for rapid 2D prototyping and pixel-perfect rendering. | 2D dynamic shadows and complex FOV require heavy custom coding. Multiplayer setup is entirely manual. | Not Recommended for this specific feature set. |

---

## 5. MVP (Minimum Viable Product) Roadmap
To achieve a playable prototype, prioritize these foundational systems before adding content:

1. **Environment & FOV Foundation:**
   - Build a single, small test house (4-5 rooms).
   - Implement the top-down player controller.
   - Code the FOV raycasting and dynamic shadow system.
   - *Deliverable:* Player can walk through a pitch-black house using only a flashlight cone.
2. **Basic Ghost AI & NavMesh:**
   - Bake a 2D NavMesh.
   - Implement an invisible Ghost object that wanders between rooms.
   - Add basic interactions (ghost opening doors or throwing a physics object).
3. **Core Tools (The Loop):**
   - Implement the EMF (detects the interact event).
   - Implement the Thermometer (detects proximity to the ghost's anchor point).
   - *Deliverable:* Player can locate the ghost using tools.
4. **The Hunt Mechanics:**
   - Build the Ghost's vision cone.
   - Implement the Chase state and player death.
   - Implement the LOS obstacle layer (crouching behind a desk to break the Chase state).
5. **Multiplayer Sync:**
   - Connect two clients.
   - Sync player transforms, flashlight rotations, and ghost state. (Bolting multiplayer on early avoids rewrites).
