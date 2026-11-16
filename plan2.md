# Pamahiin Jeepney Road Background Loading Scene Plan (`plan2.md`)

## Overview
Create a vertical 2D top-down driving background scene (`LoadingScene`) for the Pamahiin loading screen where the team's Jeepney appears to drive northward along a dark rural road at night. In reality, the Jeepney and road placement stay static in the middle of the frame while sprite-sheet animations (moving road texture, waving trees, vertical wind lines) and top-to-bottom scrolling roadside trees create the forward-driving illusion with zero dead space at any screen aspect ratio.

---

## Existing Assets & Architecture
- **Jeepney Assets** (`Assets/Resources/Sprites/BarangayMap/Jeepney/`):
  - `jeepney_roof.png` — top-down Jeepney roof/chassis focal sprite positioned at the center of the road (`(0, 0)`), facing upward.
  - Optional subtle detail layers: warm headlight beams/cones ahead of the Jeepney and rear taillights for night-road depth.
- **Road Asset** (`Assets/Resources/Sprites/BarangayMap/Exterior/`):
  - `asphalt_road.png` — base top-down asphalt road texture/reference used to build the seamless vertical road and animated road strip.
- **Loading UI Overlay**:
  - `Assets/Scripts/UI/LoadingScreenManager.cs`
  - `Assets/Resources/UI/LoadingScreen.uxml` & `Assets/Resources/UI/LoadingScreen.uss`

---

## Visual & Technical Requirements

### 1. New Sprite Sheets (`Assets/Resources/Sprites/Loading/`)
Generate/import matching 16-bit top-down pixel-art sprite sheets (point-filtered, crisp pixel edges, night palette matching `BarangayMap`):
1. **Animated Road Sprite Sheet (`road_moving_sheet.png`)**
   - Multi-frame looping horizontal strip (e.g., 4–8 frames stacked cleanly for seamless vertical step motion) derived from `asphalt_road.png` aesthetics (dark asphalt grain, faded center lane dashes, subtle shoulder gravel/wet sheen).
   - Stepping through the sprite-sheet frames loops the road surface downward so the road itself looks like it is continuously rushing underneath the stationary Jeepney.
2. **Waving Trees Sprite Sheet (`trees_waving_sheet.png`)**
   - Top-down tropical Philippine roadside tree canopies (e.g., mango/narra/palm variants across frames) with gentle wind-sway leaf animation looping in place on each tree object.
3. **Vertical Wind Lines Sprite Sheet (`wind_lines_sheet.png`)**
   - Looping vertical pixel-art wind streak frames (`1` to `N` frames) oriented vertically along the road corridor to convey speed and night air rushing past the Jeepney.

### 2. Static Centerpiece & Vertical Road Layout (`Assets/Scenes/LoadingScene.unity`)
- **Camera (`Main Camera`)**:
  - Orthographic 2D camera centered at `(0, 0, -10)` sized (`orthographicSize` ~ `5`–`6`) so the full vertical road + left/right tree verges fill the screen completely.
- **Full-Bleed Zero-Dead-Space Background Coverage**:
  - Wide roadside ground/verge backdrop layer (`z = +2`) spanning beyond ultrawide (`21:9`) and tall (`16:10` / `4:3`) camera bounds so no camera clear-color void or black dead space ever appears on screen edges.
  - **Vertical Road**: Centered vertically (`x = 0`), extending continuously from above the top viewport edge to below the bottom viewport edge, playing `road_moving_sheet.png` via `SpriteAnimator` / `Animator`.
- **Static Jeepney (`x = 0, y = 0`)**:
  - Centered in the middle of the vertical road (`jeepney_roof.png`), facing north (`+Y`), completely static in world space (optional ultra-subtle 1px sprite jitter or headlight flicker toggleable cleanly, zero world translation).
- **Waving & Scrolling Roadside Trees**:
  - Tree columns lining both left and right edges of the vertical road (plus deeper outer verge trees so ultrawide displays have dense foliage to the edges).
  - Each tree plays the waving sprite-sheet animation continuously (`trees_waving_sheet.png`) while a lightweight driver script (`LoadingRoadParallax` / `ScrollingRoadProp`) translates the trees strictly downward (`-Y`, from **top to bottom** of the viewport) and wraps each tree seamlessly back above the top boundary once it passes below the bottom boundary.
- **Vertical Wind Lines**:
  - Overlay/road-corridor sprite strips playing `wind_lines_sheet.png` vertically along the road and flanking air streams (either purely frame-animated in place or combined with fast top-to-bottom vertical pass-through).

### 3. Editor Builder & Scene Integration
- **Editor Builder (`Assets/Editor/LoadingSceneBuilder.cs`)**:
  - Idempotent menu/CLI tool (`Pamahiin/Build Loading Scene`) that slices the 3 sprite sheets, generates `.anim` / `AnimatorController` assets (or assigns frame arrays to a lightweight unscaled-time sprite-sheet player), constructs `Assets/Scenes/LoadingScene.unity`, and registers it cleanly in `EditorBuildSettings`.
  - Runs animations using `Time.unscaledDeltaTime` so background motion continues smoothly even during paused gameplay or async scene-loading hitches.
- **Loading Overlay Integration (`LoadingScreenManager.cs` & `LoadingScreen.uss`)**:
  - Make `.loading-overlay` backdrop semi-transparent (keeping `.loading-vignette` dark edges and the centered dossier card crisp and readable) or stream the `LoadingScene` camera onto a fullscreen backdrop behind the dossier UI.
  - Depending on integration choice, either load `LoadingScene` additively / render its camera output into `LoadingScreenManager` whenever intermission starts, or instantiate its self-contained `LoadingRoadBackdrop` rig directly under `LoadingScreenManager`.

### 4. Verification Plan
- **Visual / Aspect-Ratio Check**: Verify framing at `16:9` (`1920x1080`), `16:10`, and `21:9` in Unity Game view to confirm zero dead space around road edges, roadside verges, and tree wrap bounds.
- **EditMode Automated Tests**:
  - Verify `LoadingScene.unity` exists, contains the centered static Jeepney, vertical animated road, waving tree instances, wind lines, and scrolling driver component.
  - Verify sprite sheets (`road_moving_sheet`, `trees_waving_sheet`, `wind_lines_sheet`) have valid sliced sprites, and tree wrap logic moves `y` strictly downward (top -> bottom) and wraps above the top bound cleanly.
