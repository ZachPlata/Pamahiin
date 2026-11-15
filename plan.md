# Pamahiin UI Revamp Plan — Sharp 16-Bit Pixel Design & Centralized Typography

## Goals & Scope

Transform Pamahiin's fragmented UI stylesheets into a cohesive, retro **16-bit Filipino folk-horror pixel UI** where every screen, popup, HUD overlay, button, input control, and world/screen-space prompt shares:
1. **Unified Typography (`PressStart2P-Regular.ttf`):** Eliminate the current mix of `PressStart2P` (Main Menu title only), `VT323` (Main Menu body/buttons), default Unity sans-serif (all other UI Toolkit screens), `GUI.skin.label` (`ItemHoverIndicator`), and `LiberationSans SDF` (`TMP Settings`). Apply `PressStart2P-Regular.ttf` globally across every text element.
2. **Sharp / 16-Bit Pixelated Framing (Zero Soft Corners):** Eliminate all soft `border-radius` curves across all `.uss` files (`border-radius: 0px` everywhere), replacing rounded cards, pills, dots, and buttons with crisp sharp corners, stepped pixel bevel borders (or `FilterMode.Point` 9-slice pixel-art border textures), and square pixel LEDs/badges.
3. **Centralized Theme & Component Architecture:** Replace duplicated inline panel/button/font CSS rules across 10 separate UXML/USS pairs with a single centralized theme (`PamahiinTheme.uss` + `PamahiinRuntimeTheme.tss`) wired into the shared `Assets/UI Toolkit/PanelSettings.asset` and explicitly `@import`-ed by dynamically spawned `Resources` UI overlays (`LoadingScreen`, `EndGameUI`).
4. **Priority Focus Screens Called Out in Request:**
   - [Assets/Resources/UI/LoadingScreen.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/LoadingScreen.uxml) & [Assets/Resources/UI/LoadingScreen.uss](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/LoadingScreen.uss) (+ [LoadingScreenManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/LoadingScreenManager.cs))
   - [Assets/Scripts/UI/TutorialTooltipManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/TutorialTooltipManager.cs) (+ `#TutorialToast*` elements in [HUD.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uxml) & [HUD.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uss))
   - Every remaining in-game, menu, overlay, and prompt UI so no screen looks visually mismatched.

---

## Current UI Audit & Findings

### 1. Asset & Font Audit
- **Attached Font Verification:** The attached `PressStart2P-Regular.ttf` is byte-for-byte identical (SHA256 `8D0248E41694FDD875DBCDE859EE1BAE5982ECFDC6C7E5E451B48950D29BA95A`, `116,008` bytes) to [Assets/Fonts/PressStart2P-Regular.ttf](file:///d:/Github%20Projects/Pamahiin/Assets/Fonts/PressStart2P-Regular.ttf) (`guid: f5d0dd39011a2df4a91cb18cb087e786`). No duplicate font import is needed.
- **Current Font Usage Fragmentation:**
  - `MainMenu.uss` sets `PressStart2P-Regular.ttf` (with `-unity-font-definition: initial;`) **only** on `.title-label`, while `.subtitle-label`, `.menu-button`, `.menu-input`, `.player-slot`, `.control-text`, `.section-label`, `.slider-label`, and `.slider-value` use [Assets/Fonts/VT323-Regular.ttf](file:///d:/Github%20Projects/Pamahiin/Assets/Fonts/VT323-Regular.ttf).
  - `LoadingScreen.uss`, `HUD.uss`, `PauseMenu.uss`, `JournalUI.uss`, `ExorcismUI.uss`, `TruckDashboard.uss`, `SpiritBoxDialogue.uss`, `EndGameUI.uss`, and `DevConsole.uss` declare **no** `-unity-font` / `-unity-font-definition` rules at all, falling back to Unity's default runtime sans-serif font.
  - [Assets/UI Toolkit/PanelSettings.asset](file:///d:/Github%20Projects/Pamahiin/Assets/UI%20Toolkit/PanelSettings.asset) (`m_ReferenceResolution: 1200x800`, `m_ScaleMode: 1`, shared by all scene `UIDocument` components across `MainMenuScene`, `LobbyScene`, `BarangayMap`, `GameMapScene`) sets `textSettings: {fileID: 0}` and points to [UnityDefaultRuntimeTheme.tss](file:///d:/Github%20Projects/Pamahiin/Assets/UI%20Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss) (`@import url("unity-theme://default");`).
  - [Assets/Scripts/ItemHoverIndicator.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/ItemHoverIndicator.cs) draws item hover prompts via `OnGUI()` using `new GUIStyle(GUI.skin.label)` at bold `14px`.
  - [Assets/TextMesh Pro/Resources/TMP Settings.asset](file:///d:/Github%20Projects/Pamahiin/Assets/TextMesh%20Pro/Resources/TMP%20Settings.asset) defaults to `LiberationSans SDF` (`ThermometerItem.cs` uses `TMP_Text`, though active build scenes currently contain zero scene `Canvas`/`TMP_Text` objects).

### 2. Glyph Coverage & Monospace Width Calibration (`PressStart2P`)
Inspecting `PressStart2P-Regular.ttf` via `GlyphTypeface` shows `657` glyphs where `AdvanceWidth('M') = 1.0` and `Height = 1.0` (a true 1:1 square monospace bitmap grid). Adopting `PressStart2P` universally introduces two critical technical constraints that must be planned upfront:

1. **Wide Monospace Metrics:** `PressStart2P` characters are noticeably wider than `VT323` or Arial/LiberationSans at identical `px` sizes. Keeping existing `13px–28px` body/button font sizes without rescaling will clip labels or blow out fixed-width containers (`360px` PauseMenu, `166px` Exorcism buttons, `440px` TutorialToast, `300px` MainMenu buttons, `960px` Journal spread).
   - **Rule:** Adopt a pixel-friendly typographic scale (preferring `8px`/`10px`/`12px`/`16px`/`24px`/`48px`, with `-unity-font-style: normal;` since `PressStart2P` only ships Regular weight and fake bold smears pixel strokes), and increase container min-widths/line-heights (`white-space: normal;` where wrapping is allowed) cleanly per screen.
2. **Unsupported Unicode Symbols Found in Existing UXML / C# Strings:** Direct glyph cmap inspection revealed several non-ASCII characters currently in use that **do not exist** in `PressStart2P-Regular.ttf` (which would render as empty tofu boxes `□`):

| Character | Codepoint | In `PressStart2P`? | Current Location(s) | Planned Replacement |
| :--- | :--- | :---: | :--- | :--- |
| `►` | `U+25BA` | No | `SpiritBoxDialogue.uxml` | `▶` (`U+25B6`) or `>` |
| `✕` / `✖` | `U+2715` / `U+2716` | No | `JournalUI.uxml`, `PauseMenu.uxml` | `X` or `[X]` |
| `⚙` | `U+2699` | No | `PauseMenu.uxml` | Remove glyph (`OPTIONS`) |
| `👥` | `U+1F465` | No | `PauseMenu.uxml` | Remove glyph (`PLAYERS`) |
| `📖` | `U+1F4D6` | No | `JournalUI.uxml` | Remove glyph (`PAMAHIIN JOURNAL`) |
| `⚠` | `U+26A0` | No | `ExorcismUI.uxml` | `[!]` |
| `●` | `U+25CF` | No | `TruckDashboardController.cs` | Square CSS pixel dot + plain text (`IR NIGHT VISION: ON (B&W)`) |
| `▶`, `◀`, `▲`, `▼`, `—`, `°`, `•`, `←`, `→`, `…`, `★` | Supported | **Yes** | `PauseMenu`, `JournalUI`, `TruckDashboard`, `EndGameUI` | Keep as-is |

### 3. Soft Corners, Unsupported CSS, Duplicates, & Runtime `PanelSettings` Gaps
- **Global Soft Corners (`border-radius > 0`):** Every `.uss` file except `EndGameUI.uss` (except `.deceased-badge: 3px`) uses `border-radius` ranging from `1.5px` to `16px`, plus `95px`/`50px` circles in `ExorcismUI.uss`.
- **Unreliable UI Toolkit CSS:** `SpiritBoxDialogue.uss` uses `box-shadow` in 3 rules, and `MainMenu.uss` uses float `scale: 1.05` on `.menu-button:hover` (blurring raster/pixel text during scaling). Both should be replaced with crisp pixel border highlights and `2px` stepped press/hover offsets.
- **Duplicate `EndGameUI` Files:** Identical copies exist at [Assets/UI/EndGame/EndGameUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/EndGame/EndGameUI.uxml) (referenced by `BarangayMap.unity` & `GameMapScene.unity`) and [Assets/Resources/UI/EndGameUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/EndGameUI.uxml) (loaded dynamically by `EndGameRevealController.EnsureInstance()` in `LobbyScene`). These should share the centralized theme (and optionally consolidate to a single source of truth).
- **Runtime `UIDocument` Theme Risks:**
  - `LoadingScreenManager.Awake()` runs `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` and `ResolvePanelSettings()` creates a fallback `PanelSettings` (`1920x1080`, `match: 0.5f`, **no `themeUss`**) when no scene `UIDocument` exists yet.
  - `SpiritBoxItem.EnsureDialogueUI()` creates a bare `new GameObject("SpiritBoxDialogueUI")` with `AddComponent<UIDocument>()` if missing in `GameMapScene` without assigning `PanelSettings`.
  - `TutorialTooltipManager.GetToastRefs()` falls back to `Object.FindAnyObjectByType<UIDocument>()` if `BindUI()` hasn't run yet—risking grabbing `LoadingScreen` or `DevConsole` instead of the HUD `UIDocument` (`PlayerHUDManager`).

---

## Centralized Design System Specification

### Visual Direction: 16-Bit Folk-Horror Pixel UI
Instead of generic flat rectangles (`border-radius: 0px` + `1px` line), give containers an unmistakable **16-bit pixel frame** identity using **nested stepped borders** (or clean `FilterMode.Point` 9-slice pixel-art border textures in `Assets/Resources/UI/Sprites/`):
- **Stepped Outer Frame (Zero-Radius Double Bevel):**
  - Outer crisp `2px` obsidian border (`#07070A`)
  - Inner highlight/rim via a `2px` top/left light bevel and `2px` bottom/right shadow bevel (`border-top-color`, `border-left-color` vs `border-bottom-color`, `border-right-color` supported directly in Unity UI Toolkit!), OR a crisp `3x3` / `9-slice` pixel-corner notched frame sprite (`-unity-slice-left/right/top/bottom`, `-unity-background-scale-mode: stretch-to-fill`, `FilterMode.Point`, uncompressed) so corners remain sharp/stepped (`1–2px` pixel notches, zero anti-aliased curve).
- **Crisp Square Pixel Indicators:** All circular LEDs/dots (`power-indicator`, `emf-dot`, `nv-rec-dot`, `cctv-rec-dot`, `sb-led-dot`, `lobby-legend-dot`, `loadout-contrib-dot`) become sharp square pixel blocks (`6x6px`–`10x10px`, `border-radius: 0px`, `1px` dark pixel outline).
- **Audio & Tactile Consistency:** Keep all interactive triggers as Unity `<ui:Button>` elements (or add `.pm-btn` to `<ui:Button>` elements) so `GameAudioManager.WireAllUIButtons()` automatically attaches `Resources/Audio/ui_click.wav` alongside uniform `:hover` / `:active` pixel transitions.

### Color & Surface Tokens (`--pm-*` CSS Custom Properties in `PamahiinTheme.uss`)
- **Primary Dark Horror Surfaces:**
  - `--pm-bg-void`: `rgba(6, 7, 11, 0.94)` (main modals, pause, end-game, loading backdrop)
  - `--pm-bg-panel`: `rgb(17, 19, 27)` (panel body)
  - `--pm-bg-panel-elevated`: `rgb(27, 30, 42)` (cards, HUD slots, key badges, hover fills)
  - `--pm-bg-scrim`: `rgba(0, 0, 0, 0.82)` (fullscreen overlays)
- **Frame & Bevel Borders:**
  - `--pm-border-outer`: `rgb(7, 8, 12)`
  - `--pm-border-subtle`: `rgb(56, 62, 82)`
  - `--pm-border-light`: `rgb(104, 112, 140)` (top/left 16-bit highlight edge)
  - `--pm-border-brass`: `rgb(186, 142, 68)` (gold/brass accent for titles, selected badges, Journal/Exorcism headers)
  - `--pm-border-crimson`: `rgb(196, 48, 48)` (horror danger/active trim)
- **Domain Variant Accents (Preserving Per-Feature Identity Under One System):**
  - **Standard / Menu / Pause / HUD / Loading / EndGame:** Obsidian + Blood Crimson (`rgb(200, 46, 46)`) + Aged Brass (`rgb(228, 184, 98)`).
  - **Journal (`JournalUI`):** Keep the warm parchment & dark mahogany wood readability (`rgb(218, 200, 166)` page, `rgb(38, 24, 16)` ink, `rgb(154, 122, 72)` brass) while migrating every panel, tab, ghost card, button, dot, and border to **sharp pixel frames + `PressStart2P` typography**.
  - **Electronics (`TruckDashboard`, `SpiritBoxDialogue`, `DevConsole`):** Keep CRT phosphor green (`rgb(70, 255, 110)` / `rgb(18, 30, 22)`), Spirit Box amber/cyan static screen, and crisp terminal green/cyan accents, unified under the same sharp pixel bezel geometry and `PressStart2P` font tokens.
- **Text Tokens:**
  - `--pm-text-primary`: `rgb(240, 236, 224)` (warm off-white, high contrast on obsidian)
  - `--pm-text-muted`: `rgb(154, 160, 178)`
  - `--pm-text-danger`: `rgb(245, 86, 86)`
  - `--pm-text-success`: `rgb(96, 228, 134)`
  - `--pm-text-warning`: `rgb(244, 198, 86)`
- **Typography Tokens:**
  - `--pm-font-main`: `url("project://database/Assets/Fonts/PressStart2P-Regular.ttf")`
  - `--pm-size-xs`: `8px` (micro tags, hotkey pips, version/FPS counter)
  - `--pm-size-sm`: `10px` (HUD readouts, badges, secondary descriptions, toast body)
  - `--pm-size-md`: `12px` (standard buttons, inputs, menu items, toast title, journal body)
  - `--pm-size-lg`: `16px` (section headers, modal titles, ghost verdict)
  - `--pm-size-xl`: `24px` (overlay titles, death banner)
  - `--pm-size-hero`: `48px` (Main Menu `PAMAHIIN` title—rescaled from `80px` if needed or crisp `48px`/`64px` pixel multiple)

### Centralized Asset & Stylesheet Architecture
1. **`Assets/Resources/UI/PamahiinTheme.uss`** *(in `Resources` so both scene `.uss` files and `Resources`-loaded `LoadingScreen.uss` / `EndGameUI.uss` can `@import` it reliably with zero broken references)*:
   - Global `TextElement`, `Label`, `Button`, `TextField`, `Toggle`, `Slider`, `ScrollView`, `Scroller` font & sharp-corner resets:
     - `-unity-font: url("project://database/Assets/Fonts/PressStart2P-Regular.ttf");`
     - `-unity-font-definition: initial;`
     - `-unity-font-style: normal;`
     - `border-radius: 0px;`
   - Reusable utility/component classes:
     - `.pm-panel`, `.pm-panel--bevel`, `.pm-panel--crimson`, `.pm-panel--brass`, `.pm-panel--crt`
     - `.pm-btn`, `.pm-btn--primary`, `.pm-btn--secondary`, `.pm-btn--danger`, `.pm-btn--brass`, `.pm-btn--ghost`, `.pm-btn--sm` (consistent `2px` dark outer border + stepped top/left highlight bevel + crisp `:hover` brightness/border shift + `:active` `2px` downward shift + `:disabled` muted state)
     - `.pm-input` (`TextField > .unity-text-field__input` sharp box, dark fill, brass focus border)
     - `.pm-slider` (square pixel dragger handle, crisp `0px` tracker bar)
     - `.pm-toggle` (square pixel checkbox `[X]` style)
     - `.pm-scrollbar` (square vertical scrollbar thumb/track replacing rounded Unity default bars)
     - `.pm-badge`, `.pm-keycap`, `.pm-led`, `.pm-progress-track`, `.pm-progress-fill`
2. **`Assets/Resources/UI/PamahiinRuntimeTheme.tss`** + **`Assets/Resources/UI/PamahiinPanelSettings.asset`** (plus updating existing [Assets/UI Toolkit/PanelSettings.asset](file:///d:/Github%20Projects/Pamahiin/Assets/UI%20Toolkit/PanelSettings.asset)):
   - `PamahiinRuntimeTheme.tss` imports `unity-theme://default` followed by `@import url("PamahiinTheme.uss");`.
   - `Assets/UI Toolkit/PanelSettings.asset` points `themeUss` to `PamahiinRuntimeTheme.tss` (and attaches a shared `PanelTextSettings` asset if desired for global TextCore pixel configuration).
   - Provide a small static helper (e.g., `PamahiinUIThemeUtility.ResolveRuntimePanelSettings(PanelSettings existing)`) loaded from `Resources` so dynamic `UIDocument` spawners (`LoadingScreenManager`, `EndGameRevealController`, `SpiritBoxItem.EnsureDialogueUI()`) **never** create a blank un-themed fallback `PanelSettings` missing `PamahiinRuntimeTheme.tss`.

---

## Screen-by-Screen Revamp Plan

### 1. Priority Target: Loading Screen
Files: [Assets/Resources/UI/LoadingScreen.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/LoadingScreen.uxml), [Assets/Resources/UI/LoadingScreen.uss](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/LoadingScreen.uss), [Assets/Scripts/UI/LoadingScreenManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/LoadingScreenManager.cs)
- **Typography & Glyphs:** Import `PamahiinTheme.uss` at the top of `LoadingScreen.uss`. Set `PressStart2P` across loading title/header (`16px`–`20px`), scene subtitle (`12px`), lore/tip text (`10px`, `line-height: 18px`, `white-space: normal;`), and percentage/status text (`10px`).
- **Sharp Pixel Redesign:**
  - `.loading-frame`: Replace `border-radius: 6px` with `border-radius: 0px`, applying `.pm-panel--bevel` + crimson/brass inner rim (`2px` border + crisp corner accents or 9-slice pixel frame). Ensure width accommodates `PressStart2P` tip text cleanly (e.g., `width: 560px; max-width: 90%;`).
  - `.loading-progress-track` & fill bar: Remove `border-radius: 2px`; convert into a crisp `16px`–`18px` tall chunky 16-bit progress bar with a `2px` outer dark frame, `2px` inner gap (`padding: 2px`), and solid/stepped crimson-gold fill.
- **Controller Fix (`LoadingScreenManager.cs`):**
  - Update `ResolvePanelSettings()` to fall back to the shared `Resources`-loaded `PanelSettings` (with `PamahiinRuntimeTheme.tss`) instead of creating an empty runtime `PanelSettings` with `themeUss = null`, eliminating unstyled-frame flashes during initial `BeforeSceneLoad` boot.

### 2. Priority Target: Tutorial Toast & In-Game HUD
Files: [Assets/Scripts/UI/TutorialTooltipManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/TutorialTooltipManager.cs), [Assets/UI/HUD/HUD.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uxml), [Assets/UI/HUD/HUD.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uss), [Assets/Scripts/UI/PlayerHUDManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/PlayerHUDManager.cs)
- **`TutorialTooltipManager.cs` Structure & Robustness:**
  - Fix the unsafe `FindAnyObjectByType<UIDocument>()` fallback in `GetToastRefs()` (currently line ~321): resolve via `PlayerHUDManager` (`FindAnyObjectByType<PlayerHUDManager>()?.GetComponent<UIDocument>()`) or iterate `FindObjectsByType<UIDocument>()` for `doc.rootVisualElement?.Q("TutorialToast") != null`, ensuring tutorial tooltips never accidentally bind to `LoadingScreen` or `DevConsole`.
  - Audit default/C# tutorial string lengths against `PressStart2P` width so `#ToastTitle` (`12px`, brass/gold) and `#ToastDescription` (`9px–10px`, `white-space: normal;`, generous `line-height: 16px`) wrap cleanly inside `#TutorialToast`.
- **`#TutorialToast` Visual Revamp (`HUD.uss`):**
  - Remove `border-radius: 6px` on `.tutorial-toast`, `4px` on `.toast-key-badge`, and `1.5px` on `.toast-progress-track`.
  - Apply sharp `.pm-panel--brass` styling to `#TutorialToast` (expand width from `440px` to `480px–520px` / `max-width: 92%` for monospace `PressStart2P` breathing room), raise contrast with `#0A0B10` fill + stepped bevel border.
  - Convert `#ToastKeyBadge` (`.toast-key-badge`) into an authentic **pixel keycap button** (`border-radius: 0px`, `border-width: 2px`, thicker bottom bevel `border-bottom-width: 4px`, centered `PressStart2P` `11px` glyph).
  - Convert `#ToastProgressBar` / `.toast-progress-track` into a razor-sharp `4px` full-width bottom pixel timer bar (`border-radius: 0px`).
- **Remaining HUD Elements (`HUD.uxml` / `HUD.uss`):**
  - Top-left Pause `<ui:Button>`: convert to unified `.pm-btn .pm-btn--sm` with sharp corners + `PressStart2P`.
  - Inventory Slots (`.inventory-slot`): remove `border-radius: 2px`; use crisp `2px` pixel frame (`border-radius: 0px`), square `8x8px` `.power-indicator` pixel LED (`border-radius: 0px`), square `.emf-dot` pixel blocks (`border-radius: 0px`), and `PressStart2P` `8px–10px` slot index & temperature readouts (verify `°C` renders cleanly—`U+00B0` is confirmed present in `PressStart2P`).
  - Night-Vision Overlay (`.nv-badge-cluster`, `.nv-rec-dot`, `.nv-battery-box`): `border-radius: 0px` everywhere; turn `.nv-rec-dot` into a blinking `8x8px` square pixel REC block.
  - Spectator HUD Banner (`.spectator-pill`, `.spectator-end-btn`) & `#DeathOverlay` (`.death-panel`, `.death-button`): remove `3px`/`4px`/`2px` radii; unify buttons onto `.pm-btn--primary` / `.pm-btn--danger` and panels onto `.pm-panel--crimson`.

### 3. Main Menu
Files: [Assets/UI/MainMenu/MainMenu.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/MainMenu/MainMenu.uxml), [Assets/UI/MainMenu/MainMenu.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/MainMenu/MainMenu.uss), [Assets/Scripts/UI/MainMenuController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/MainMenuController.cs)
- Replace **all** `VT323-Regular.ttf` references in `MainMenu.uss` with centralized `PamahiinTheme.uss` (`PressStart2P-Regular.ttf`).
- Rescale typography cleanly so nothing clips: `.title-label` (`56px–64px`, normal weight, crisp stepped drop shadow instead of blurry `-unity-font-style: bold`), `.subtitle-label` (`11px–12px`), `.menu-button` (`12px–13px`, widen `.menu-button` to `340px–360px` to accommodate wide `PressStart2P` uppercase labels cleanly), `.menu-input`, `.player-slot`, `.control-text`, `.section-label`, `.slider-label`, `.slider-value` (`10px–11px`).
- Set `border-radius: 0px` and apply `.pm-panel--bevel` on `.join-container`, `.player-list-container`, `.player-slot`, `.controls-list`, `.settings-container`.
- Replace `.menu-button` zero-border + blurry `scale: 1.05` hover effect with centralized `.pm-btn` pixel border + sharp crimson/brass fill hover & press offset.

### 4. Pause Menu
Files: [Assets/UI/PauseMenu/PauseMenu.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/PauseMenu/PauseMenu.uxml), [Assets/UI/PauseMenu/PauseMenu.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/PauseMenu/PauseMenu.uss), [Assets/Scripts/UI/PauseMenuController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/PauseMenuController.cs)
- Remove unsupported Unicode icons (`⚙`, `👥`, `✖`) from `PauseMenu.uxml` (`OPTIONS`, `PLAYERS`, `X` / `BACK`) while keeping supported `▶` / `←` glyphs.
- Eliminate `border-radius` (`6px`, `4px`, `3px`) across `.menu-container`, `.menu-button`, `.btn-kick`, `.btn-vote`. Expand `.menu-container` width (`360px` -> `420px–460px`) so `PressStart2P` buttons, `.fps-label`, `.player-row`, `.player-name`, and `.player-badge` never wrap or clip.
- Unify `.menu-button`, `.btn-kick`, and `.btn-vote` onto `.pm-btn` / `.pm-btn--danger` / `.pm-btn--sm` classes.

### 5. Investigation Journal (`JournalUI`)
Files: [Assets/UI/Journal/JournalUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Journal/JournalUI.uxml), [Assets/UI/Journal/JournalUI.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Journal/JournalUI.uss), [Assets/Scripts/UI/JournalController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/JournalController.cs)
- Preserve the iconic **warm parchment / dark wood / brass leather** palette while applying `PamahiinTheme.uss`, `border-radius: 0px`, and `PressStart2P` typography across all 8 spreads (`0..7`).
- Clean unsupported glyphs (`✕` -> `X`, `📖` -> remove icon) in `JournalUI.uxml`.
- Audit container sizes and text wrapping across dynamic C#-spawned elements in `JournalController.cs`: `.deduction-ghosts-row`, `.ghost-button`, `.dossier-selector-row`, `.dossier-select-btn`, `.ghost-trait-bullet`, `.evidence-chip`, `.loadout-item-row`, `.loadout-qty-btn`, `.lobby-legend-pill` / `.lobby-legend-dot`, `.loadout-contrib-pill` / `.loadout-contrib-dot`. Ensure ghost trait lists, lore blurbs, and 2-column deduction grids fit cleanly at `8px–10px` body / `12px–14px` headings without overflowing `.book-container` (`1200x800` reference resolution).
- Convert navigation tabs, page flip buttons (`▶`, `◀`), close button, quantity `+`/`-` buttons, and loadout contribute/deduction buttons to sharp pixel `.pm-btn` variants and square `.pm-led` indicator dots.

### 6. Exorcism Ritual UI
Files: [Assets/UI/Exorcism/ExorcismUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Exorcism/ExorcismUI.uxml), [Assets/UI/Exorcism/ExorcismUI.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Exorcism/ExorcismUI.uss), [Assets/Scripts/UI/ExorcismManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/ExorcismManager.cs)
- Replace `⚠` in `ExorcismUI.uxml` with `[!]`.
- Remove all soft `border-radius` curves (`12px`, `7px`, `16px`, `6px`, `10px`, `4px`, `3px`) from `.popup-panel`, `.ghost-btn`, `.circular-minigame-container`, `.stat-badge`, `.banner-box`, `.repel-box`, `.target-hit-line`, `.needle-pointer` -> crisp `0px` pixel geometry + `.pm-panel--crimson` / `.pm-btn`. Widen `.ghost-btn` min-width/height if needed for 2-line `PressStart2P` ghost names.
- **Special Case — Skill-Check Dial (`.circle-dial` `95px` & `.center-rune` `50px`):** Pure sharp squares look awkward for an angular rotating timing check, while smooth CSS `border-radius: 95px` clashes with a pixelated UI. Replace the CSS `border-radius` ring fill with a **pixel-art 16-bit stepped octagonal/circular ritual dial sprite** (`FilterMode.Point`, transparent outside ring, `0px` CSS `border-radius` on the container), keeping `ExorcismManager`'s angular `target-hit-line` and `needle-pointer` rotation math untouched!

### 7. Jeepney / Truck Dashboard & Spirit Box Dialogue
Files:
- [Assets/UI/TruckDashboard/TruckDashboard.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/TruckDashboard/TruckDashboard.uxml), [Assets/UI/TruckDashboard/TruckDashboard.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/TruckDashboard/TruckDashboard.uss), [Assets/Scripts/UI/TruckDashboardController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/TruckDashboardController.cs)
- [Assets/UI/SpiritBox/SpiritBoxDialogue.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/SpiritBox/SpiritBoxDialogue.uxml), [Assets/UI/SpiritBox/SpiritBoxDialogue.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/SpiritBox/SpiritBoxDialogue.uss), [Assets/Scripts/UI/SpiritBoxDialogueUI.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/SpiritBoxDialogueUI.cs), [Assets/Scripts/Equipment/SpiritBoxItem.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/SpiritBoxItem.cs)
- **TruckDashboard:** Apply `PamahiinTheme.uss` (`PressStart2P` CRT readout tokens `9px–12px`), `border-radius: 0px` on `.cctv-rec-dot`, `.cctv-cam-badge`, `.cctv-nv-badge`, `.control-button`, and C#-injected `.sanity-row` / `.sanity-name` / `.sanity-value`. Replace `"● IR NIGHT VISION: ON (B&W)"` in `TruckDashboardController.cs` with `"IR NIGHT VISION: ON (B&W)"` paired with a square pixel `.cctv-nv-dot` indicator so no tofu glyph appears. Unify `.control-button` onto `.pm-btn--crt`.
- **SpiritBoxDialogue:** Replace `►` (`U+25BA`) in `SpiritBoxDialogue.uxml` with `▶` (`U+25B6`) or `>`. Remove `border-radius` (`6px`, `5px`, `4px`, `3px`) and all unsupported `box-shadow` declarations in `SpiritBoxDialogue.uss`. Convert `.sb-panel`, `.sb-led-dot`, `.sb-screen`, `.sb-option-btn`, and `.sb-option-badge` to `.pm-panel--bevel` + `.pm-btn` with `PressStart2P` `9px–12px` spacing. Fix `SpiritBoxItem.EnsureDialogueUI()` to assign `PamahiinPanelSettings` on dynamically created `UIDocument`.

### 8. End Game Summary & Developer Console
Files:
- [Assets/UI/EndGame/EndGameUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/EndGame/EndGameUI.uxml), [Assets/UI/EndGame/EndGameUI.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/EndGame/EndGameUI.uss), [Assets/Resources/UI/EndGameUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/EndGameUI.uxml), [Assets/Resources/UI/EndGameUI.uss](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/EndGameUI.uss), [Assets/Scripts/UI/EndGameRevealController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/EndGameRevealController.cs)
- [Assets/UI/Console/DevConsole.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Console/DevConsole.uxml), [Assets/UI/Console/DevConsole.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Console/DevConsole.uss), [Assets/Scripts/UI/DeveloperConsoleManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/DeveloperConsoleManager.cs)
- **EndGameUI Deduplication & Upgrade:** Ensure `Assets/Resources/UI/EndGameUI.{uxml,uss}` and `Assets/UI/EndGame/EndGameUI.{uxml,uss}` stay identical (or point scene `UIDocument`s to the single `Resources` asset and remove the duplicate) and `@import` `PamahiinTheme.uss`. Replace `.deceased-badge` `border-radius: 3px` with `0px` crimson badge, apply `.pm-btn--primary` to `.return-button`, and calibrate `PressStart2P` payout/outcome sizes (`10px–20px`). Ensure `EndGameRevealController.EnsureInstance()` uses `PamahiinUIThemeUtility.ResolveRuntimePanelSettings(...)`.
- **DevConsole:** Remove `border-radius` (`10px`, `6px`, `5px`) from `.dev-console-panel`, `.dev-console-button`, `.dev-console-close-button`, `.dev-currency-input > .unity-text-field__input`; unify onto `.pm-panel--bevel`, `.pm-btn`, `.pm-input`, and `PressStart2P` (`9px–12px`).

### 9. Non-UI-Toolkit Text Prompts (`OnGUI` & `TextMeshPro`)
Files: [Assets/Scripts/ItemHoverIndicator.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/ItemHoverIndicator.cs), [Assets/TextMesh Pro/Resources/TMP Settings.asset](file:///d:/Github%20Projects/Pamahiin/Assets/TextMesh%20Pro/Resources/TMP%20Settings.asset), [Assets/Scripts/ThermometerItem.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/ThermometerItem.cs)
- **`ItemHoverIndicator.cs` (`OnGUI`):** `ItemHoverIndicator` already draws a sharp `1px` bordered dark tooltip box via `GUI.DrawTexture`. Update `EnsureStyles()` so `tooltipStyle` uses `PressStart2P-Regular.ttf` (loaded from `Resources` or serialized field, fallback cleanly if null) at crisp `10px–11px` (`FontStyle.Normal` instead of `FontStyle.Bold`), with `2px` pixel border outline matching `--pm-border-brass` / `--pm-bg-panel` instead of plain gray. (Longer term optional enhancement: migrate hover tooltip into `HUD.uxml` so it scales natively with `PanelSettings`, though styling `OnGUI` with `PressStart2P` is low-risk and immediate.)
- **`TMP Settings.asset` / `ThermometerItem`:** Generate a pixel-crisp `PressStart2P-Regular SDF` / Bitmap `TMP_FontAsset` (`Assets/TextMesh Pro/Resources/Fonts & Materials/`) and assign it as the default font asset in `TMP Settings.asset` + any `TMP_Text` prefab references (using `°` `U+00B0`, which is confirmed in `PressStart2P`).

---

## File-by-File Execution Plan

| Order | File / Asset | Action | Key Changes |
| :---: | :--- | :--- | :--- |
| 1 | `Assets/Resources/UI/PamahiinTheme.uss` | Create | Central tokens (`--pm-*`), `PressStart2P` global font/style reset, `border-radius: 0px`, unified `.pm-panel*`, `.pm-btn*`, `.pm-input`, `.pm-slider`, `.pm-toggle`, `.pm-scrollbar`, `.pm-badge`, `.pm-keycap`, `.pm-led`, `.pm-progress-*` |
| 2 | `Assets/Resources/UI/PamahiinRuntimeTheme.tss` | Create | `@import url("unity-theme://default");` + `@import url("PamahiinTheme.uss");` |
| 3 | `Assets/Resources/UI/Sprites/` | Create (optional 9-slice) | Crisp `Point`-filtered pixel-frame 9-slice border sprite + stepped circular `ExorcismDial.png` sprite |
| 4 | [Assets/UI Toolkit/PanelSettings.asset](file:///d:/Github%20Projects/Pamahiin/Assets/UI%20Toolkit/PanelSettings.asset) + `Assets/Resources/UI/PamahiinPanelSettings.asset` | Modify / Create | Wire `PamahiinRuntimeTheme.tss` into `PanelSettings.themeUss`; add `Resources` copy + `PamahiinUIThemeUtility.cs` helper for dynamic `UIDocument`s |
| 5 | `Assets/Scripts/UI/PamahiinUIThemeUtility.cs` | Create | Centralized runtime `PanelSettings` resolver replacing duplicate fallback creators |
| 6 | [LoadingScreen.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/LoadingScreen.uxml), [LoadingScreen.uss](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/LoadingScreen.uss), [LoadingScreenManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/LoadingScreenManager.cs) | Modify | `@import "PamahiinTheme.uss"`, sharp `.loading-frame` + chunky 16-bit `.loading-progress-track`, `PressStart2P` sizing (`10px–18px`), themed `ResolvePanelSettings()` |
| 7 | [TutorialTooltipManager.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/TutorialTooltipManager.cs), [HUD.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uxml), [HUD.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/HUD/HUD.uss) | Modify | Fix `GetToastRefs()` fallback targeting; widen `#TutorialToast`; sharp bevel frame, pixel keycap `#ToastKeyBadge`, `4px` sharp `#ToastProgressBar`; square LEDs/slots/NV/death/spectator banners; `PressStart2P` sizes |
| 8 | [MainMenu.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/MainMenu/MainMenu.uxml), [MainMenu.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/MainMenu/MainMenu.uss) | Modify | Replace all `VT323` with `PressStart2P`, remove `scale: 1.05` & all `border-radius`, apply `.pm-btn` & `.pm-panel--bevel`, adjust button/panel widths |
| 9 | [PauseMenu.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/PauseMenu/PauseMenu.uxml), [PauseMenu.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/PauseMenu/PauseMenu.uss) | Modify | Replace unsupported `⚙`/`👥`/`✖` glyphs, remove `border-radius`, widen container for `PressStart2P`, unify `.menu-button`/`.btn-kick`/`.btn-vote` onto `.pm-btn*` |
| 10 | [JournalUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Journal/JournalUI.uxml), [JournalUI.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Journal/JournalUI.uss), [JournalController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/JournalController.cs) | Modify | Replace `✕`/`📖`, preserve parchment palette, `border-radius: 0px`, pixel borders & square legend dots, calibrate `PressStart2P` sizes/wrapping across all 8 spreads & dynamic rows |
| 11 | [ExorcismUI.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Exorcism/ExorcismUI.uxml), [ExorcismUI.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Exorcism/ExorcismUI.uss) | Modify | Replace `⚠` with `[!]`, remove soft panel/button/badge radii, swap smooth `circle-dial` CSS radius for pixel-art stepped ring sprite |
| 12 | [TruckDashboard.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/TruckDashboard/TruckDashboard.uss), [TruckDashboardController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/TruckDashboardController.cs) | Modify | Apply CRT `.pm-panel--crt` & `.pm-btn--crt`, square LEDs, remove `●` string prefix, `PressStart2P` sizing |
| 13 | [SpiritBoxDialogue.uxml](file:///d:/Github%20Projects/Pamahiin/Assets/UI/SpiritBox/SpiritBoxDialogue.uxml), [SpiritBoxDialogue.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/SpiritBox/SpiritBoxDialogue.uss), [SpiritBoxItem.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/Equipment/SpiritBoxItem.cs) | Modify | Replace `►` with `▶`, strip `border-radius` & `box-shadow`, unify options onto `.pm-btn`, wire `PamahiinPanelSettings` on dynamic spawn |
| 14 | [Assets/Resources/UI/EndGameUI.{uxml,uss}](file:///d:/Github%20Projects/Pamahiin/Assets/Resources/UI/EndGameUI.uss), [Assets/UI/EndGame/EndGameUI.{uxml,uss}](file:///d:/Github%20Projects/Pamahiin/Assets/UI/EndGame/EndGameUI.uss), [EndGameRevealController.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/UI/EndGameRevealController.cs) | Modify / Deduplicate | Sync or consolidate duplicate copies, `@import "PamahiinTheme.uss"`, `border-radius: 0px`, `.pm-btn--primary` on `.return-button`, `PressStart2P` sizing |
| 15 | [DevConsole.uss](file:///d:/Github%20Projects/Pamahiin/Assets/UI/Console/DevConsole.uss) | Modify | Apply `.pm-panel--bevel`, `.pm-btn`, `.pm-input`, `border-radius: 0px`, `PressStart2P` |
| 16 | [ItemHoverIndicator.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Scripts/ItemHoverIndicator.cs) & [TMP Settings.asset](file:///d:/Github%20Projects/Pamahiin/Assets/TextMesh%20Pro/Resources/TMP%20Settings.asset) | Modify | Wire `PressStart2P` into `OnGUI` hover tooltip (`FontStyle.Normal`, brass pixel frame) & set `PressStart2P` TMP FontAsset as TMP default |
| 17 | `Assets/Tests/EditMode/UIThemeAuditTests.cs` | Create | Automated regression checks covering every `.uss`, `.uxml`, scene `UIDocument`, and UI script |

---

## Verification Plan

1. **Automated Static / EditMode Regression Tests (`Assets/Tests/EditMode/UIThemeAuditTests.cs`):**
   - **Zero Soft Corners Check:** Assert every `.uss` file under `Assets/UI/` and `Assets/Resources/UI/` contains no non-zero `border-radius` declarations (`border-radius:\s*[1-9]` == 0 matches).
   - **Zero `VT323` / Fallback Font Check:** Assert `VT323` is no longer referenced in any `.uss`, `.uxml`, or `.cs` UI file, and `PamahiinTheme.uss` + `PamahiinRuntimeTheme.tss` resolve cleanly with `PressStart2P-Regular.ttf`.
   - **Glyph Safety Check:** Scan every UXML text string and UI C# literal against `PressStart2P-Regular.ttf`'s supported codepoint table so no `►`, `✕`, `✖`, `⚙`, `👥`, `📖`, `⚠`, or `●` glyphs remain.
   - **Runtime Theme Wiring Check:** Verify `LoadingScreenManager`, `EndGameRevealController`, `SpiritBoxItem`, and `TutorialTooltipManager` resolve valid `PanelSettings` with `PamahiinRuntimeTheme.tss` and HUD-scoped toast queries.
   - Re-run existing `EditMode` tests ([PlayerDeathSpectatorTests.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Tests/EditMode/PlayerDeathSpectatorTests.cs), [GhostCatalogTests.cs](file:///d:/Github%20Projects/Pamahiin/Assets/Tests/EditMode/GhostCatalogTests.cs)).
2. **Resolution & Multi-Scene PlayMode Inspection (in Unity Editor across active build scenes):**
   - Test at `1200x800` (reference resolution), `1920x1080` (16:9), `1280x720` (720p), and `2560x1440`:
     - **Before scene UI loads:** Trigger `LoadingScreenManager` (`MainMenuScene` -> `LobbyScene` -> `BarangayMap`) and verify zero unstyled font flashes, sharp loading bezel, and readable multi-line tip wrapping.
     - **Tutorial Toast:** Trigger movement, equipment pickup, and exorcism toasts via `TutorialTooltipManager` on live HUD and verify `#ToastKeyBadge`, `#ToastTitle`, long wrapped `#ToastDescription`, and bottom `#ToastProgressBar` do not clip or overlap HUD slots.
     - **Interactive Menus & Modals:** Open `MainMenu` (Play / Join / Controls / Settings sliders & toggles), `PauseMenu` (player kick/vote rows), `JournalUI` (all 8 tabs + ghost deduction + loadout stepper), `TruckDashboard`, `SpiritBoxDialogue`, `ExorcismUI` (dial alignment + banners), `EndGameUI`, and `DevConsole`. Confirm hover/press states, `ui_click.wav` audio feedback, crisp pixel edges, and zero clipped labels.
