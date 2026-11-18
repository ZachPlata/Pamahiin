using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class JournalController : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement journalOverlay;

    // Spreads (0..6 = Standard Field Journal, 7 = Lobby / Multiplayer / Equipment Shop)
    private const int TotalSpreads = 8;
    private const int BlueprintSpreadIndex = 2;
    private const int DossierSpreadIndex = 5;
    private const int EvidenceSpreadIndex = 6;
    private const int LobbySpreadIndex = 7;
    private VisualElement[] spreads = new VisualElement[TotalSpreads];
    private int currentSpread = 0;

    // Top Tabs
    private Button tabLobby;
    private Button tabCover;
    private Button tabToc;
    private Button tabBlueprint;
    private Button tabEquip;
    private Button tabGhosts;
    private Button tabEvidence;
    private Button btnCloseTop;

    // Table of Contents Buttons
    private Button btnCoverOpen;
    private Button btnTocBlueprint;
    private Button btnTocEquip1;
    private Button btnTocEquip2;
    private Button btnTocGhosts;
    private Button btnTocEvidence;

    // Entity Compendium Controls (Spread 5)
    private VisualElement dossierSelectorGrid;
    private VisualElement dossierArtImage;
    private VisualElement dossierSigilBox;
    private Label lblDossierSigilIndex;
    private Label lblDossierSigilMono;
    private Label lblDossierCaption;
    private Button btnDossierPrev;
    private Button btnDossierNext;
    private Label lblDossierCounter;
    private Label lblDossierName;
    private Label lblDossierSubtitle;
    private Label lblDossierLore;
    private VisualElement dossierTraitsContainer;
    private VisualElement dossierEvidenceChips;
    private Label lblDossierTip;
    private ScrollView dossierDetailScroll;
    private readonly List<Button> dossierSelectorButtons = new List<Button>();
    private int currentDossierIndex = 0;

    // Case Briefing & Blueprint Controls (Spread 2)
    private Label lblCaseId;
    private Label lblCaseDifficulty;
    private Label lblCaseGhostName;
    private Label lblCaseBgTitle;
    private Label lblCaseGhostBg;
    private Label lblBlueprintMapName;
    private Label lblBlueprintSubtitle;
    private VisualElement blueprintFloorTabs;
    private Button btnBlueprintF1;
    private Button btnBlueprintF2;
    private Button btnBlueprintF3;
    private VisualElement blueprintImage;
    private Label lblBlueprintUnavailable;
    private int selectedSchoolFloor = 1;
    private GameMatchManager subscribedMatchManager;

    // Bottom Navigation
    private Button btnPrevPage;
    private Button btnNextPage;
    private Button btnTocJump;
    private Label lblPageIndicator;

    // Evidence & Deduction Toggles
    private Toggle tglEmf5;
    private Toggle tglFreezing;
    private Toggle tglWriting;
    private Toggle tglDots;
    private Toggle tglSpiritBox;
    private Toggle tglOrbs;

    private VisualElement ghostButtonsContainer;
    private Label lblDeductionStatus;

    private string selectedGhost = "None";
    private Button btnClose;

    // Lobby / Multiplayer / Loadout Controls (Spread 8)
    private Button btnMapPrev;
    private Button btnMapNext;
    private Label lblSelectedMap;
    private Button btnDiffAmateur;
    private Button btnDiffIntermediate;
    private Button btnDiffProfessional;
    private Button btnDiffNightmare;
    private Label lblDifficultyDesc;
    private Button btnCreateParty;
    private Label lblPartyCode;
    private TextField inputPartyCode;
    private Button btnJoinParty;
    private Label lblPartyPlayers;
    private Label lblPartyStatus;
    private Button btnStartContract;
    private Label lblPlayerCurrency;
    private Label lblLoadoutSummary;
    private VisualElement lobbyPlayerLegend;
    private ScrollView loadoutShopList;

    // Two-way cross-filtering data structures
    private readonly Dictionary<string, Toggle> evidenceToggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<string, Button> ghostButtons = new Dictionary<string, Button>();

    private static readonly Dictionary<string, HashSet<string>> ghostEvidenceMap =
        GhostCatalog.BuildJournalEvidenceMap();

    private bool isUpdatingFilters = false;

    private bool isJournalOpen = false;
    private bool isStartingContract = false;
    private bool isPartyCodeInputFocused = false;
    private int textInputBlurFrame = -1;
    private int closedFrame = -1;

    public static JournalController Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isJournalOpen;
    public static bool WasOpenThisFrame => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);
    public static bool IsTextInputFocused => IsOpen && Instance != null && Instance.ComputeTextInputFocused();
    public bool IsJournalOpen => isJournalOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        uiDocument = GetComponent<UIDocument>();
        ProgressionAndLoadoutManager.EnsureInstance();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static bool IsElementHierarchyVisible(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current.ClassListContains("hidden") ||
                current.ClassListContains("hidden-spread") ||
                (current.style.display.keyword != StyleKeyword.Null && current.style.display.value == DisplayStyle.None) ||
                !current.visible)
            {
                return false;
            }
        }
        return true;
    }

    private bool ComputeTextInputFocused()
    {
        if (!isJournalOpen || currentSpread != LobbySpreadIndex ||
            (inputPartyCode != null && !IsElementHierarchyVisible(inputPartyCode)))
        {
            isPartyCodeInputFocused = false;
        }

        if (isPartyCodeInputFocused)
        {
            return true;
        }

        IPanel panel = journalOverlay?.panel ?? uiDocument?.rootVisualElement?.panel;
        if (panel?.focusController?.focusedElement is VisualElement focusedElement &&
            IsElementHierarchyVisible(focusedElement))
        {
            return focusedElement is TextField || focusedElement.GetFirstAncestorOfType<TextField>() != null;
        }

        return false;
    }

    private void BlurPartyCodeInput()
    {
        bool wasFocused = isPartyCodeInputFocused;
        isPartyCodeInputFocused = false;

        if (inputPartyCode != null)
        {
            inputPartyCode.Blur();
        }

        IPanel panel = journalOverlay?.panel ?? uiDocument?.rootVisualElement?.panel;
        if (panel?.focusController?.focusedElement is VisualElement focusedElement &&
            (focusedElement == inputPartyCode ||
             (inputPartyCode != null && focusedElement.GetFirstAncestorOfType<TextField>() == inputPartyCode)))
        {
            focusedElement.Blur();
            wasFocused = true;
        }

        if (wasFocused)
        {
            textInputBlurFrame = Time.frameCount;
        }
    }

    private void SubmitPartyCodeJoin()
    {
        if (NetworkController.Instance != null && inputPartyCode != null)
        {
            NetworkController.Instance.JoinMultiplayerByPartyCode(inputPartyCode.value);
            RefreshLobbySpreadUI();
        }
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 10;
        var root = uiDocument.rootVisualElement;

        journalOverlay = root.Q<VisualElement>("journal-overlay");

        // 1. Spreads
        for (int i = 0; i < TotalSpreads; i++)
        {
            spreads[i] = root.Q<VisualElement>($"spread-{i}");
        }

        // 2. Bookmark Tabs
        tabLobby = root.Q<Button>("tab-lobby");
        tabCover = root.Q<Button>("tab-cover");
        tabToc = root.Q<Button>("tab-toc");
        tabBlueprint = root.Q<Button>("tab-blueprint");
        tabEquip = root.Q<Button>("tab-equip");
        tabGhosts = root.Q<Button>("tab-ghosts");
        tabEvidence = root.Q<Button>("tab-evidence");
        btnCloseTop = root.Q<Button>("btn-close-top");

        if (tabLobby != null) tabLobby.clicked += () => ShowSpread(LobbySpreadIndex);
        if (tabCover != null) tabCover.clicked += () => ShowSpread(0);
        if (tabToc != null) tabToc.clicked += () => ShowSpread(1);
        if (tabBlueprint != null) tabBlueprint.clicked += () => ShowSpread(BlueprintSpreadIndex);
        if (tabEquip != null) tabEquip.clicked += () => ShowSpread(3);
        if (tabGhosts != null) tabGhosts.clicked += () => ShowSpread(DossierSpreadIndex);
        if (tabEvidence != null) tabEvidence.clicked += () => ShowSpread(EvidenceSpreadIndex);
        if (btnCloseTop != null) btnCloseTop.clicked += ToggleJournal;

        // 3. Cover Open Button
        btnCoverOpen = root.Q<Button>("btn-cover-open");
        if (btnCoverOpen != null) btnCoverOpen.clicked += () => ShowSpread(1);

        // 4. Table of Contents Jumps
        btnTocBlueprint = root.Q<Button>("btn-toc-blueprint");
        btnTocEquip1 = root.Q<Button>("btn-toc-equip1");
        btnTocEquip2 = root.Q<Button>("btn-toc-equip2");
        btnTocGhosts = root.Q<Button>("btn-toc-ghosts");
        btnTocEvidence = root.Q<Button>("btn-toc-evidence");

        if (btnTocBlueprint != null) btnTocBlueprint.clicked += () => ShowSpread(BlueprintSpreadIndex);
        if (btnTocEquip1 != null) btnTocEquip1.clicked += () => ShowSpread(3);
        if (btnTocEquip2 != null) btnTocEquip2.clicked += () => ShowSpread(4);
        if (btnTocGhosts != null) btnTocGhosts.clicked += () => ShowSpread(DossierSpreadIndex);
        if (btnTocEvidence != null) btnTocEvidence.clicked += () => ShowSpread(EvidenceSpreadIndex);

        // 5. Bottom Navigation Bar
        btnPrevPage = root.Q<Button>("btn-prev-page");
        btnNextPage = root.Q<Button>("btn-next-page");
        btnTocJump = root.Q<Button>("btn-toc-jump");
        lblPageIndicator = root.Q<Label>("lbl-page-indicator");

        if (btnPrevPage != null) btnPrevPage.clicked += PrevSpread;
        if (btnNextPage != null) btnNextPage.clicked += NextSpread;
        if (btnTocJump != null) btnTocJump.clicked += () => ShowSpread(1);

        // 6. Bind Evidences Checklist
        evidenceToggles.Clear();
        tglEmf5 = root.Q<Toggle>("tgl-emf5");
        tglFreezing = root.Q<Toggle>("tgl-freezing");
        tglWriting = root.Q<Toggle>("tgl-writing");
        tglDots = root.Q<Toggle>("tgl-dots");
        tglSpiritBox = root.Q<Toggle>("tgl-spiritbox");
        tglOrbs = root.Q<Toggle>("tgl-orbs");

        if (tglEmf5 != null) evidenceToggles["EMF"] = tglEmf5;
        if (tglFreezing != null) evidenceToggles["Freezing"] = tglFreezing;
        if (tglWriting != null) evidenceToggles["GhostWriting"] = tglWriting;
        if (tglDots != null) evidenceToggles["DOTS"] = tglDots;
        if (tglSpiritBox != null) evidenceToggles["SpiritBox"] = tglSpiritBox;
        if (tglOrbs != null) evidenceToggles["GhostOrbs"] = tglOrbs;

        foreach (var pair in evidenceToggles)
        {
            var key = pair.Key;
            var tgl = pair.Value;
            if (tgl != null)
            {
                tgl.RegisterValueChangedCallback(evt => OnEvidenceToggleChanged(key, evt.newValue));
            }
        }

        // 7. Ghost Deduction Buttons (20 entities from GhostCatalog)
        ghostButtons.Clear();
        ghostButtonsContainer = root.Q<VisualElement>("ghost-buttons-container");
        lblDeductionStatus = root.Q<Label>("lbl-deduction-status");

        if (ghostButtonsContainer != null)
        {
            ghostButtonsContainer.Clear();
            var allGhosts = GhostCatalog.AllGhosts;
            for (int i = 0; i < allGhosts.Count; i += 2)
            {
                var row = new VisualElement();
                row.AddToClassList("deduction-ghosts-row");

                for (int col = 0; col < 2 && (i + col) < allGhosts.Count; col++)
                {
                    string ghostId = allGhosts[i + col].Id;
                    var btn = new Button
                    {
                        name = $"btn-ghost-{ghostId.ToLowerInvariant().Replace(" ", "-")}",
                        text = ghostId
                    };
                    btn.AddToClassList("ghost-button");
                    btn.clicked += () => SelectGhost(ghostId);
                    row.Add(btn);
                    ghostButtons[ghostId] = btn;
                }

                ghostButtonsContainer.Add(row);
            }
        }

        btnClose = root.Q<Button>("btn-close");
        if (btnClose != null) btnClose.clicked += ToggleJournal;

        // 8. Bind Entity Compendium (Spread 5), Case Briefing & Map Blueprint (Spread 2) and Lobby Spread (Spread 7)
        BindEntityCompendiumControls(root);
        BindCaseBriefingAndBlueprintControls(root);
        BindLobbySpreadControls(root);
        UpdateCrossFilters();

        var loadoutMgr = ProgressionAndLoadoutManager.EnsureInstance();
        if (loadoutMgr != null)
        {
            loadoutMgr.OnCurrencyOrLoadoutChanged -= RefreshLobbySpreadUI;
            loadoutMgr.OnContractSettingsChanged -= HandleContractSettingsChanged;
            loadoutMgr.OnCurrencyOrLoadoutChanged += RefreshLobbySpreadUI;
            loadoutMgr.OnContractSettingsChanged += HandleContractSettingsChanged;
        }
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.OnPartyStateChanged -= RefreshLobbySpreadUI;
            NetworkController.Instance.OnPartyStateChanged += RefreshLobbySpreadUI;
        }

        EnsureMatchManagerSubscription();
        RefreshCaseBriefingAndBlueprintUI();
        UpdateLobbyTabVisibility();

        // Default spread: Lobby spread if in LobbyScene, otherwise Case Briefing & Blueprint (Spread 2)
        if (ProgressionAndLoadoutManager.IsInLobbyScene)
        {
            ShowSpread(LobbySpreadIndex);
            if (NetworkController.Instance != null && NetworkController.Instance.ConsumeReopenLobbyJournalFlag())
            {
                OpenToLobbyTab();
            }
        }
        else
        {
            ShowSpread(BlueprintSpreadIndex);
        }
    }

    private void OnDisable()
    {
        BlurPartyCodeInput();

        if (ProgressionAndLoadoutManager.Instance != null)
        {
            ProgressionAndLoadoutManager.Instance.OnCurrencyOrLoadoutChanged -= RefreshLobbySpreadUI;
            ProgressionAndLoadoutManager.Instance.OnContractSettingsChanged -= HandleContractSettingsChanged;
        }
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.OnPartyStateChanged -= RefreshLobbySpreadUI;
        }
        if (subscribedMatchManager != null)
        {
            subscribedMatchManager.OnGhostContractInfoChanged -= RefreshCaseBriefingAndBlueprintUI;
            subscribedMatchManager = null;
        }
    }

    private void HandleContractSettingsChanged()
    {
        RefreshLobbySpreadUI();
        RefreshCaseBriefingAndBlueprintUI();
    }

    private void EnsureMatchManagerSubscription()
    {
        var current = GameMatchManager.Instance;
        if (subscribedMatchManager == current) return;

        if (subscribedMatchManager != null)
        {
            subscribedMatchManager.OnGhostContractInfoChanged -= RefreshCaseBriefingAndBlueprintUI;
        }

        subscribedMatchManager = current;
        if (subscribedMatchManager != null)
        {
            subscribedMatchManager.OnGhostContractInfoChanged -= RefreshCaseBriefingAndBlueprintUI;
            subscribedMatchManager.OnGhostContractInfoChanged += RefreshCaseBriefingAndBlueprintUI;
        }
    }

    private void BindEntityCompendiumControls(VisualElement root)
    {
        dossierSelectorGrid = root.Q<VisualElement>("dossier-selector-grid");
        dossierDetailScroll = root.Q<ScrollView>("dossier-detail-scroll");
        dossierArtImage = root.Q<VisualElement>("dossier-art-image");
        dossierSigilBox = root.Q<VisualElement>("dossier-sigil-box");
        lblDossierSigilIndex = root.Q<Label>("lbl-dossier-sigil-index");
        lblDossierSigilMono = root.Q<Label>("lbl-dossier-sigil-mono");
        lblDossierCaption = root.Q<Label>("lbl-dossier-caption");
        btnDossierPrev = root.Q<Button>("btn-dossier-prev");
        btnDossierNext = root.Q<Button>("btn-dossier-next");
        lblDossierCounter = root.Q<Label>("lbl-dossier-counter");
        lblDossierName = root.Q<Label>("lbl-dossier-name");
        lblDossierSubtitle = root.Q<Label>("lbl-dossier-subtitle");
        lblDossierLore = root.Q<Label>("lbl-dossier-lore");
        dossierTraitsContainer = root.Q<VisualElement>("dossier-traits-container");
        dossierEvidenceChips = root.Q<VisualElement>("dossier-evidence-chips");
        lblDossierTip = root.Q<Label>("lbl-dossier-tip");

        dossierSelectorButtons.Clear();
        if (dossierSelectorGrid != null)
        {
            dossierSelectorGrid.Clear();
            var allGhosts = GhostCatalog.AllGhosts;
            for (int i = 0; i < allGhosts.Count; i += 2)
            {
                var row = new VisualElement();
                row.AddToClassList("dossier-selector-row");

                for (int col = 0; col < 2 && (i + col) < allGhosts.Count; col++)
                {
                    int idx = i + col;
                    var ghost = allGhosts[idx];
                    var btn = new Button
                    {
                        name = $"btn-dossier-{ghost.Id.ToLowerInvariant().Replace(" ", "-")}",
                        text = $"{idx + 1:D2}. {ghost.Id}"
                    };
                    btn.AddToClassList("dossier-select-btn");
                    btn.clicked += () => ShowDossierEntity(idx);
                    row.Add(btn);
                    dossierSelectorButtons.Add(btn);
                }

                dossierSelectorGrid.Add(row);
            }
        }

        if (btnDossierPrev != null)
        {
            btnDossierPrev.clicked += () =>
            {
                int total = GhostCatalog.AllGhosts.Count;
                if (total > 0) ShowDossierEntity((currentDossierIndex - 1 + total) % total);
            };
        }

        if (btnDossierNext != null)
        {
            btnDossierNext.clicked += () =>
            {
                int total = GhostCatalog.AllGhosts.Count;
                if (total > 0) ShowDossierEntity((currentDossierIndex + 1) % total);
            };
        }

        ShowDossierEntity(0);
    }

    private void ShowDossierEntity(int index)
    {
        var allGhosts = GhostCatalog.AllGhosts;
        if (allGhosts.Count == 0) return;

        currentDossierIndex = Mathf.Clamp(index, 0, allGhosts.Count - 1);
        var ghost = allGhosts[currentDossierIndex];

        for (int i = 0; i < dossierSelectorButtons.Count; i++)
        {
            var btn = dossierSelectorButtons[i];
            if (btn == null) continue;
            if (i == currentDossierIndex) btn.AddToClassList("dossier-select-btn-active");
            else btn.RemoveFromClassList("dossier-select-btn-active");
        }

        if (lblDossierCounter != null) lblDossierCounter.text = $"{currentDossierIndex + 1:D2} / {allGhosts.Count:D2}";
        if (lblDossierCaption != null) lblDossierCaption.text = ghost.ScientificTag;
        if (lblDossierName != null) lblDossierName.text = ghost.Id.ToUpperInvariant();
        if (lblDossierSubtitle != null) lblDossierSubtitle.text = ghost.Subtitle;
        if (lblDossierLore != null) lblDossierLore.text = ghost.Lore;
        if (lblDossierTip != null) lblDossierTip.text = ghost.Tip;

        if (dossierArtImage != null)
        {
            for (int i = 0; i < allGhosts.Count; i++)
            {
                string cssClass = allGhosts[i].ArtCssClass;
                if (!string.IsNullOrEmpty(cssClass))
                {
                    dossierArtImage.RemoveFromClassList(cssClass);
                }
            }

            if (!string.IsNullOrEmpty(ghost.ArtCssClass))
            {
                dossierArtImage.RemoveFromClassList("hidden");
                dossierArtImage.AddToClassList(ghost.ArtCssClass);
                if (dossierSigilBox != null) dossierSigilBox.AddToClassList("hidden");
            }
            else
            {
                dossierArtImage.AddToClassList("hidden");
                if (dossierSigilBox != null) dossierSigilBox.RemoveFromClassList("hidden");
                if (lblDossierSigilIndex != null) lblDossierSigilIndex.text = $"ENTITY #{currentDossierIndex + 1:D2}";
                if (lblDossierSigilMono != null) lblDossierSigilMono.text = BuildMonogram(ghost.Id);
            }
        }

        if (dossierDetailScroll != null)
        {
            dossierDetailScroll.scrollOffset = Vector2.zero;
        }

        if (dossierTraitsContainer != null)
        {
            dossierTraitsContainer.Clear();
            for (int i = 0; i < ghost.Traits.Length; i++)
            {
                var traitLbl = new Label($"• {ghost.Traits[i]}");
                traitLbl.AddToClassList("ghost-trait-bullet");
                dossierTraitsContainer.Add(traitLbl);
            }
        }

        if (dossierEvidenceChips != null)
        {
            dossierEvidenceChips.Clear();
            for (int i = 0; i < ghost.Evidences.Length; i++)
            {
                var chip = new Label(GhostCatalog.EvidenceTypeToDisplayLabel(ghost.Evidences[i]));
                chip.AddToClassList("evidence-chip");
                dossierEvidenceChips.Add(chip);
            }
        }
    }

    private static string BuildMonogram(string ghostId)
    {
        if (string.IsNullOrEmpty(ghostId)) return "???";
        string cleaned = ghostId.Replace(" ", "");
        if (cleaned.Length <= 3) return cleaned.ToUpperInvariant();
        return $"{char.ToUpperInvariant(cleaned[0])}{char.ToUpperInvariant(cleaned[1])}{char.ToUpperInvariant(cleaned[cleaned.Length - 1])}";
    }

    private void BindCaseBriefingAndBlueprintControls(VisualElement root)
    {
        lblCaseId = root.Q<Label>("lbl-case-id");
        lblCaseDifficulty = root.Q<Label>("lbl-case-difficulty");
        lblCaseGhostName = root.Q<Label>("lbl-case-ghost-name");
        lblCaseBgTitle = root.Q<Label>("lbl-case-bg-title");
        lblCaseGhostBg = root.Q<Label>("lbl-case-ghost-bg");
        lblBlueprintMapName = root.Q<Label>("lbl-blueprint-map-name");
        lblBlueprintSubtitle = root.Q<Label>("lbl-blueprint-subtitle");
        blueprintFloorTabs = root.Q<VisualElement>("blueprint-floor-tabs");
        btnBlueprintF1 = root.Q<Button>("btn-blueprint-f1");
        btnBlueprintF2 = root.Q<Button>("btn-blueprint-f2");
        btnBlueprintF3 = root.Q<Button>("btn-blueprint-f3");
        blueprintImage = root.Q<VisualElement>("blueprint-image");
        lblBlueprintUnavailable = root.Q<Label>("lbl-blueprint-unavailable");

        if (btnBlueprintF1 != null) btnBlueprintF1.clicked += () => SelectSchoolBlueprintFloor(1);
        if (btnBlueprintF2 != null) btnBlueprintF2.clicked += () => SelectSchoolBlueprintFloor(2);
        if (btnBlueprintF3 != null) btnBlueprintF3.clicked += () => SelectSchoolBlueprintFloor(3);
    }

    private void SelectSchoolBlueprintFloor(int floor)
    {
        selectedSchoolFloor = Mathf.Clamp(floor, 1, 3);
        RefreshCaseBriefingAndBlueprintUI();
    }

    private static string FormatDifficultyBadge(ContractDifficulty difficulty)
    {
        float mult = ProgressionAndLoadoutManager.GetDifficultyRewardMultiplier(difficulty);
        return $"{difficulty.ToString().ToUpperInvariant()} ({mult:0.0}x)";
    }

    private void RefreshCaseBriefingAndBlueprintUI()
    {
        var mgr = ProgressionAndLoadoutManager.EnsureInstance();
        bool inLobby = ProgressionAndLoadoutManager.IsInLobbyScene;
        ContractDifficulty diff = mgr != null ? mgr.SelectedDifficulty : ContractDifficulty.Amateur;

        if (lblCaseDifficulty != null)
        {
            lblCaseDifficulty.text = FormatDifficultyBadge(diff);
        }

        if (inLobby)
        {
            if (lblCaseId != null) lblCaseId.text = "PENDING";
            if (lblCaseGhostName != null) lblCaseGhostName.text = "PENDING ASSIGNMENT";
            if (lblCaseBgTitle != null) lblCaseBgTitle.text = "Awaiting Dispatch";
            if (lblCaseGhostBg != null)
            {
                lblCaseGhostBg.text = "Contract briefing and spirit dossier will be issued upon arrival at the investigation site.";
            }

            string selectedScene = mgr != null ? mgr.GetSelectedSceneName() : ProgressionAndLoadoutManager.BARANGAY_MAP_SCENE_NAME;
            string mapTitle = mgr != null ? mgr.GetSelectedMapDisplayName() : "Barangay San Nicolas — 6 Tanglewood Drive";

            if (lblBlueprintMapName != null) lblBlueprintMapName.text = mapTitle;
            ApplyBlueprintMapMode(selectedScene);
            return;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        bool isCemeteryScene = activeScene == ProgressionAndLoadoutManager.CEMETERY_MAP_SCENE_NAME;
        bool isSchoolScene = activeScene == ProgressionAndLoadoutManager.SCHOOL_MAP_SCENE_NAME;

        EnsureMatchManagerSubscription();
        var matchMgr = GameMatchManager.Instance;
        if (matchMgr != null && matchMgr.HasAssignedGhostProfile)
        {
            if (lblCaseId != null) lblCaseId.text = matchMgr.AssignedCaseId;
            if (lblCaseGhostName != null) lblCaseGhostName.text = matchMgr.AssignedGhostName;
            if (lblCaseBgTitle != null) lblCaseBgTitle.text = matchMgr.AssignedGhostBackgroundTitle;
            if (lblCaseGhostBg != null) lblCaseGhostBg.text = matchMgr.AssignedGhostBackground;
        }
        else
        {
            if (lblCaseId != null) lblCaseId.text = "SYNCING";
            if (lblCaseGhostName != null) lblCaseGhostName.text = "Investigating...";
            if (lblCaseBgTitle != null)
            {
                lblCaseBgTitle.text = isCemeteryScene
                    ? "Kampo Santo Incident Record"
                    : (isSchoolScene ? "Campus Incident Record" : "Barangay Incident Record");
            }
            if (lblCaseGhostBg != null)
            {
                lblCaseGhostBg.text = isCemeteryScene
                    ? "Retrieving Kampo Santo ng San Nicolas burial ledgers and witness statements..."
                    : (isSchoolScene
                        ? "Retrieving San Nicolas High School campus records and witness statements..."
                        : "Retrieving Barangay San Nicolas civil records and witness statements...");
            }
        }

        if (lblBlueprintMapName != null)
        {
            lblBlueprintMapName.text = activeScene switch
            {
                ProgressionAndLoadoutManager.BARANGAY_MAP_SCENE_NAME => "Barangay San Nicolas — 6 Tanglewood Drive",
                ProgressionAndLoadoutManager.CEMETERY_MAP_SCENE_NAME => "Kampo Santo ng San Nicolas — Public Cemetery",
                ProgressionAndLoadoutManager.SCHOOL_MAP_SCENE_NAME => "San Nicolas High School (3 Floors)",
                _ => "Floor Plan Unavailable"
            };
        }
        ApplyBlueprintMapMode(activeScene);
    }

    private void ApplyBlueprintMapMode(string targetScene)
    {
        bool isBarangay = targetScene == ProgressionAndLoadoutManager.BARANGAY_MAP_SCENE_NAME;
        bool isCemetery = targetScene == ProgressionAndLoadoutManager.CEMETERY_MAP_SCENE_NAME;
        bool isSchool = targetScene == ProgressionAndLoadoutManager.SCHOOL_MAP_SCENE_NAME;
        bool available = isBarangay || isCemetery || isSchool;

        if (blueprintFloorTabs != null)
        {
            if (isSchool) blueprintFloorTabs.RemoveFromClassList("hidden");
            else blueprintFloorTabs.AddToClassList("hidden");
        }

        if (lblBlueprintSubtitle != null)
        {
            if (isBarangay)
            {
                lblBlueprintSubtitle.text = "General Floor Plan (Main Floor & Basement)";
            }
            else if (isCemetery)
            {
                lblBlueprintSubtitle.text = "Cemetery Grounds & Mausoleum Layout";
            }
            else if (isSchool)
            {
                lblBlueprintSubtitle.text = selectedSchoolFloor switch
                {
                    2 => "2nd Floor Schematic — Upper Campus Wings",
                    3 => "3rd Floor Schematic — Top Campus Wings",
                    _ => "1st Floor Schematic — Ground Campus & Courtyard"
                };
            }
            else
            {
                lblBlueprintSubtitle.text = "Floor Plan Unavailable";
            }
        }

        if (blueprintImage != null)
        {
            blueprintImage.RemoveFromClassList("blueprint-barangay");
            blueprintImage.RemoveFromClassList("blueprint-cemetery");
            blueprintImage.RemoveFromClassList("blueprint-school-f1");
            blueprintImage.RemoveFromClassList("blueprint-school-f2");
            blueprintImage.RemoveFromClassList("blueprint-school-f3");

            if (available)
            {
                blueprintImage.RemoveFromClassList("hidden");
                if (isBarangay)
                {
                    blueprintImage.AddToClassList("blueprint-barangay");
                }
                else if (isCemetery)
                {
                    blueprintImage.AddToClassList("blueprint-cemetery");
                }
                else
                {
                    blueprintImage.AddToClassList($"blueprint-school-f{Mathf.Clamp(selectedSchoolFloor, 1, 3)}");
                }
            }
            else
            {
                blueprintImage.AddToClassList("hidden");
            }
        }

        UpdateSchoolBlueprintFloorTabButton(btnBlueprintF1, selectedSchoolFloor == 1);
        UpdateSchoolBlueprintFloorTabButton(btnBlueprintF2, selectedSchoolFloor == 2);
        UpdateSchoolBlueprintFloorTabButton(btnBlueprintF3, selectedSchoolFloor == 3);

        if (lblBlueprintUnavailable != null)
        {
            if (available)
            {
                lblBlueprintUnavailable.AddToClassList("hidden");
            }
            else
            {
                lblBlueprintUnavailable.text = "No floor plan available for this location.";
                lblBlueprintUnavailable.RemoveFromClassList("hidden");
            }
        }
    }

    private static void UpdateSchoolBlueprintFloorTabButton(Button btn, bool active)
    {
        if (btn == null) return;
        if (active) btn.AddToClassList("blueprint-floor-tab-active");
        else btn.RemoveFromClassList("blueprint-floor-tab-active");
    }

    private void BindLobbySpreadControls(VisualElement root)
    {
        btnMapPrev = root.Q<Button>("btn-map-prev");
        btnMapNext = root.Q<Button>("btn-map-next");
        lblSelectedMap = root.Q<Label>("lbl-selected-map");

        btnDiffAmateur = root.Q<Button>("btn-diff-amateur");
        btnDiffIntermediate = root.Q<Button>("btn-diff-intermediate");
        btnDiffProfessional = root.Q<Button>("btn-diff-professional");
        btnDiffNightmare = root.Q<Button>("btn-diff-nightmare");
        lblDifficultyDesc = root.Q<Label>("lbl-difficulty-desc");

        btnCreateParty = root.Q<Button>("btn-create-party");
        lblPartyCode = root.Q<Label>("lbl-party-code");
        inputPartyCode = root.Q<TextField>("input-party-code");
        btnJoinParty = root.Q<Button>("btn-join-party");
        lblPartyPlayers = root.Q<Label>("lbl-party-players");
        lblPartyStatus = root.Q<Label>("lbl-party-status");
        btnStartContract = root.Q<Button>("btn-start-contract");

        lblPlayerCurrency = root.Q<Label>("lbl-player-currency");
        lblLoadoutSummary = root.Q<Label>("lbl-loadout-summary");
        lobbyPlayerLegend = root.Q<VisualElement>("lobby-player-legend");
        loadoutShopList = root.Q<ScrollView>("loadout-shop-list");

        var btnReplayTutorial = root.Q<Button>("btn-replay-tutorial");
        if (btnReplayTutorial != null)
        {
            btnReplayTutorial.clicked += () =>
            {
                if (isJournalOpen) ToggleJournal();
                NetworkController.Instance?.EnterWarehouseTutorial();
            };
        }

        if (btnMapPrev != null)
        {
            btnMapPrev.clicked += () =>
            {
                var mgr = ProgressionAndLoadoutManager.Instance;
                if (mgr != null) mgr.SetMapIndex(mgr.SelectedMapIndex - 1);
            };
        }

        if (btnMapNext != null)
        {
            btnMapNext.clicked += () =>
            {
                var mgr = ProgressionAndLoadoutManager.Instance;
                if (mgr != null) mgr.SetMapIndex(mgr.SelectedMapIndex + 1);
            };
        }

        if (btnDiffAmateur != null)
            btnDiffAmateur.clicked += () => ProgressionAndLoadoutManager.Instance?.SetDifficulty(ContractDifficulty.Amateur);
        if (btnDiffIntermediate != null)
            btnDiffIntermediate.clicked += () => ProgressionAndLoadoutManager.Instance?.SetDifficulty(ContractDifficulty.Intermediate);
        if (btnDiffProfessional != null)
            btnDiffProfessional.clicked += () => ProgressionAndLoadoutManager.Instance?.SetDifficulty(ContractDifficulty.Professional);
        if (btnDiffNightmare != null)
            btnDiffNightmare.clicked += () => ProgressionAndLoadoutManager.Instance?.SetDifficulty(ContractDifficulty.Nightmare);

        if (btnCreateParty != null)
        {
            btnCreateParty.clicked += () =>
            {
                if (NetworkController.Instance != null)
                {
                    string code = NetworkController.Instance.CreateMultiplayerPartyCode();
                    if (!string.IsNullOrEmpty(code))
                    {
                        GUIUtility.systemCopyBuffer = code;
                    }
                    RefreshLobbySpreadUI();
                }
            };
        }

        if (btnJoinParty != null)
        {
            btnJoinParty.clicked += SubmitPartyCodeJoin;
        }

        if (inputPartyCode != null)
        {
            inputPartyCode.RegisterCallback<FocusInEvent>(_ => isPartyCodeInputFocused = true);
            inputPartyCode.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (evt.relatedTarget is VisualElement nextFocused &&
                    (nextFocused == inputPartyCode || nextFocused.GetFirstAncestorOfType<TextField>() == inputPartyCode))
                {
                    return;
                }

                isPartyCodeInputFocused = false;
                textInputBlurFrame = Time.frameCount;
            });
            inputPartyCode.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    BlurPartyCodeInput();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    SubmitPartyCodeJoin();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        if (btnStartContract != null)
        {
            isStartingContract = false;
            btnStartContract.clicked += () =>
            {
                if (isStartingContract || !ProgressionAndLoadoutManager.IsNetworkHostOrOffline)
                {
                    return;
                }

                if (NetworkController.Instance != null)
                {
                    isStartingContract = true;
                    btnStartContract.SetEnabled(false);
                    if (isJournalOpen) ToggleJournal();
                    NetworkController.Instance.LoadGameScene();
                }
            };
        }

        BuildLoadoutShopList();
        RefreshLobbySpreadUI();
    }

    private void BuildLoadoutShopList()
    {
        if (loadoutShopList == null) return;
        loadoutShopList.Clear();

        var mgr = ProgressionAndLoadoutManager.EnsureInstance();
        if (mgr == null) return;

        foreach (var entry in mgr.Catalog)
        {
            string itemId = entry.id;
            VisualElement row = new VisualElement();
            row.AddToClassList("loadout-item-row");

            VisualElement infoCol = new VisualElement();
            infoCol.AddToClassList("loadout-item-info");

            Label titleLbl = new Label($"{entry.displayName}  —  ${entry.cost}");
            titleLbl.AddToClassList("loadout-item-title");

            Label subLbl = new Label(entry.description);
            subLbl.AddToClassList("loadout-item-sub");

            infoCol.Add(titleLbl);
            infoCol.Add(subLbl);

            VisualElement controls = new VisualElement();
            controls.AddToClassList("loadout-item-controls");

            VisualElement contribRow = new VisualElement();
            contribRow.name = $"pips-{itemId}";
            contribRow.AddToClassList("loadout-contrib-row");

            Button btnMinus = new Button(() =>
            {
                ProgressionAndLoadoutManager.Instance?.TryRemoveEquipmentAndRefund(itemId);
            })
            { text = "-" };
            btnMinus.name = $"btn-minus-{itemId}";
            btnMinus.AddToClassList("loadout-qty-btn");

            Label qtyLbl = new Label($"{mgr.GetLoadoutCount(itemId)} / {entry.maxCount}");
            qtyLbl.name = $"lbl-qty-{itemId}";
            qtyLbl.AddToClassList("loadout-qty-label");

            Button btnPlus = new Button(() =>
            {
                ProgressionAndLoadoutManager.Instance?.TryBuyEquipment(itemId);
            })
            { text = "+" };
            btnPlus.name = $"btn-plus-{itemId}";
            btnPlus.AddToClassList("loadout-qty-btn");

            controls.Add(contribRow);
            controls.Add(btnMinus);
            controls.Add(qtyLbl);
            controls.Add(btnPlus);

            row.Add(infoCol);
            row.Add(controls);

            loadoutShopList.Add(row);
        }
    }

    public void RefreshLobbySpreadUI()
    {
        var mgr = ProgressionAndLoadoutManager.EnsureInstance();
        if (mgr == null) return;

        if (lblSelectedMap != null)
            lblSelectedMap.text = mgr.GetSelectedMapDisplayName();

        if (lblDifficultyDesc != null)
            lblDifficultyDesc.text = ProgressionAndLoadoutManager.GetDifficultyDescription(mgr.SelectedDifficulty);

        UpdateDifficultyBtnHighlight(btnDiffAmateur, mgr.SelectedDifficulty == ContractDifficulty.Amateur);
        UpdateDifficultyBtnHighlight(btnDiffIntermediate, mgr.SelectedDifficulty == ContractDifficulty.Intermediate);
        UpdateDifficultyBtnHighlight(btnDiffProfessional, mgr.SelectedDifficulty == ContractDifficulty.Professional);
        UpdateDifficultyBtnHighlight(btnDiffNightmare, mgr.SelectedDifficulty == ContractDifficulty.Nightmare);

        // Multiplayer Party UI
        var net = NetworkController.Instance;
        bool isHostOrSolo = ProgressionAndLoadoutManager.IsNetworkHostOrOffline;

        if (net != null)
        {
            if (lblPartyCode != null)
            {
                if (!string.IsNullOrEmpty(net.CurrentPartyCode))
                {
                    string modeBadge = net.IsOnlineRelayParty ? "ONLINE" : "LAN";
                    lblPartyCode.text = $"PARTY CODE ({modeBadge}): {net.CurrentPartyCode}";
                }
                else
                {
                    lblPartyCode.text = "PARTY CODE: SOLO (CLICK CREATE ABOVE)";
                }
            }

            if (lblPartyStatus != null && !string.IsNullOrEmpty(net.LastStatusMessage))
            {
                lblPartyStatus.text = net.LastStatusMessage;
            }

            if (btnCreateParty != null)
            {
                bool canCreate = isHostOrSolo && !net.IsCreatingParty && !net.IsConnectingToParty;
                btnCreateParty.SetEnabled(canCreate);
                if (net.IsCreatingParty)
                {
                    btnCreateParty.text = "CREATING ONLINE PARTY...";
                }
                else if (net.IsMultiplayerPartyOpen && !string.IsNullOrEmpty(net.CurrentPartyCode))
                {
                    btnCreateParty.text = $"PARTY OPEN ({net.CurrentPartyCode})";
                }
                else
                {
                    btnCreateParty.text = "CREATE MULTIPLAYER PARTY CODE";
                }
            }

            if (btnJoinParty != null)
            {
                btnJoinParty.SetEnabled(!net.IsConnectingToParty && !net.IsCreatingParty);
                btnJoinParty.text = net.IsConnectingToParty ? "JOINING..." : "JOIN PARTY";
            }
        }

        int connectedCount = 1;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            connectedCount = NetworkManager.Singleton.IsServer
                ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count)
                : Mathf.Max(1, mgr.GetLobbyPlayerSlots().Count, PlayerController.AllPlayers.Count);
        }

        btnMapPrev?.SetEnabled(isHostOrSolo);
        btnMapNext?.SetEnabled(isHostOrSolo);
        btnDiffAmateur?.SetEnabled(isHostOrSolo);
        btnDiffIntermediate?.SetEnabled(isHostOrSolo);
        btnDiffProfessional?.SetEnabled(isHostOrSolo);
        btnDiffNightmare?.SetEnabled(isHostOrSolo);

        if (lblPartyPlayers != null)
        {
            string roleStr = isHostOrSolo ? "Host" : "Client";
            lblPartyPlayers.text = $"Investigators in Lobby: {connectedCount} ({roleStr})";
        }

        if (btnStartContract != null)
        {
            btnStartContract.SetEnabled(isHostOrSolo && !isStartingContract);
            btnStartContract.text = isHostOrSolo
                ? "▶ START INVESTIGATION"
                : "WAITING FOR HOST TO START...";
        }

        // Currency, Player Legend & Equipment Loadout UI
        int availableCurrency = mgr.GetAvailableCurrency();
        if (lblPlayerCurrency != null)
            lblPlayerCurrency.text = $"CURRENCY: ${availableCurrency}";

        if (lblLoadoutSummary != null)
            lblLoadoutSummary.text = $"Van Gear: {mgr.GetTotalBroughtItemsCount()} Items (${mgr.GetTotalLoadoutValue()})";

        RefreshPlayerLegend(mgr);

        if (loadoutShopList != null)
        {
            foreach (var entry in mgr.Catalog)
            {
                int count = mgr.GetLoadoutCount(entry.id);
                int ownRemovableCount = mgr.GetOwnContributionCount(entry.id) - mgr.GetPendingRemoveCount(entry.id);

                var contribRow = loadoutShopList.Q<VisualElement>($"pips-{entry.id}");
                RefreshItemContributors(contribRow, mgr.GetEquipmentContributors(entry.id));

                var qtyLbl = loadoutShopList.Q<Label>($"lbl-qty-{entry.id}");
                if (qtyLbl != null)
                {
                    qtyLbl.text = $"{count} / {entry.maxCount}";
                }

                var btnMinus = loadoutShopList.Q<Button>($"btn-minus-{entry.id}");
                if (btnMinus != null)
                {
                    btnMinus.SetEnabled(ownRemovableCount > 0);
                }

                var btnPlus = loadoutShopList.Q<Button>($"btn-plus-{entry.id}");
                if (btnPlus != null)
                {
                    btnPlus.SetEnabled(count < entry.maxCount && availableCurrency >= entry.cost);
                }
            }
        }
    }

    private void RefreshPlayerLegend(ProgressionAndLoadoutManager mgr)
    {
        if (lobbyPlayerLegend == null) return;
        lobbyPlayerLegend.Clear();

        var slots = new List<LobbyPlayerSlotEntry>(mgr.GetLobbyPlayerSlots());
        slots.Sort((a, b) => a.slotIndex.CompareTo(b.slotIndex));
        ulong localId = ProgressionAndLoadoutManager.LocalNetworkClientId;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            bool isLocal = slot.clientId == localId;
            string roleTag = slot.slotIndex == 0 ? (isLocal ? "You" : "Host") : (isLocal ? "You" : string.Empty);
            string label = string.IsNullOrEmpty(roleTag)
                ? $"P{slot.slotIndex + 1}"
                : $"P{slot.slotIndex + 1} ({roleTag})";

            lobbyPlayerLegend.Add(CreateColoredBadge(
                label,
                ProgressionAndLoadoutManager.GetPlayerColor(slot.slotIndex),
                "lobby-legend-pill",
                "lobby-legend-dot",
                "lobby-legend-text"
            ));
        }
    }

    private static void RefreshItemContributors(VisualElement container, List<LoadoutContributorEntry> contributors)
    {
        if (container == null) return;
        container.Clear();
        if (contributors == null || contributors.Count == 0) return;

        for (int i = 0; i < contributors.Count; i++)
        {
            var c = contributors[i];
            container.Add(CreateColoredBadge(
                c.count.ToString(),
                ProgressionAndLoadoutManager.GetPlayerColor(c.slotIndex),
                "loadout-contrib-pill",
                "loadout-contrib-dot",
                "loadout-contrib-count"
            ));
        }
    }

    private static VisualElement CreateColoredBadge(
        string text,
        Color color,
        string pillClass,
        string dotClass,
        string textClass)
    {
        var pill = new VisualElement();
        pill.AddToClassList(pillClass);
        pill.style.borderTopColor = color;
        pill.style.borderBottomColor = color;
        pill.style.borderLeftColor = color;
        pill.style.borderRightColor = color;

        var dot = new VisualElement();
        dot.AddToClassList(dotClass);
        dot.style.backgroundColor = color;

        var lbl = new Label(text);
        lbl.AddToClassList(textClass);

        pill.Add(dot);
        pill.Add(lbl);
        return pill;
    }

    private static void UpdateDifficultyBtnHighlight(Button btn, bool isActive)
    {
        if (btn == null) return;
        if (isActive) btn.AddToClassList("lobby-diff-btn-active");
        else btn.RemoveFromClassList("lobby-diff-btn-active");
    }

    private bool? lastLobbyTabVisible;

    private void UpdateLobbyTabVisibility()
    {
        bool inLobby = ProgressionAndLoadoutManager.IsInLobbyScene;
        if (tabLobby != null && lastLobbyTabVisible != inLobby)
        {
            lastLobbyTabVisible = inLobby;
            // Multiplayer & Lobby tab ONLY appears inside the Lobby Map and disappears inside the playable map
            tabLobby.style.display = inLobby ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!inLobby && currentSpread == LobbySpreadIndex)
        {
            ShowSpread(BlueprintSpreadIndex);
        }
    }

    public void OpenToLobbyTab()
    {
        UpdateLobbyTabVisibility();
        if (!isJournalOpen)
        {
            isJournalOpen = true;
            journalOverlay.RemoveFromClassList("hidden");
        }
        RefreshLobbySpreadUI();
        ShowSpread(LobbySpreadIndex);
    }

    public void ShowSpread(int index)
    {
        bool inLobby = ProgressionAndLoadoutManager.IsInLobbyScene;
        int maxAllowedSpread = inLobby ? LobbySpreadIndex : (TotalSpreads - 2);
        currentSpread = Mathf.Clamp(index, 0, maxAllowedSpread);

        for (int i = 0; i < TotalSpreads; i++)
        {
            if (spreads[i] == null) continue;
            if (i == currentSpread)
            {
                spreads[i].RemoveFromClassList("hidden-spread");
            }
            else
            {
                spreads[i].AddToClassList("hidden-spread");
            }
        }

        if (currentSpread == LobbySpreadIndex)
        {
            RefreshLobbySpreadUI();
        }
        else
        {
            BlurPartyCodeInput();
            if (currentSpread == BlueprintSpreadIndex)
            {
                RefreshCaseBriefingAndBlueprintUI();
            }
        }

        UpdateNavigationVisuals();
    }

    public void NextSpread()
    {
        bool inLobby = ProgressionAndLoadoutManager.IsInLobbyScene;
        int maxAllowedSpread = inLobby ? LobbySpreadIndex : (TotalSpreads - 2);
        if (currentSpread < maxAllowedSpread)
        {
            ShowSpread(currentSpread + 1);
        }
    }

    public void PrevSpread()
    {
        if (currentSpread > 0)
        {
            ShowSpread(currentSpread - 1);
        }
    }

    private void UpdateNavigationVisuals()
    {
        bool inLobby = ProgressionAndLoadoutManager.IsInLobbyScene;
        int maxAllowedSpread = inLobby ? LobbySpreadIndex : (TotalSpreads - 2);

        // Update indicator label
        if (lblPageIndicator != null)
        {
            switch (currentSpread)
            {
                case 0:
                    lblPageIndicator.text = "Cover Page";
                    break;
                case 1:
                    lblPageIndicator.text = "Table of Contents  (Page 1 of 6)";
                    break;
                case 2:
                    lblPageIndicator.text = "Case Briefing & Blueprint  (Page 2 of 6)";
                    break;
                case 3:
                    lblPageIndicator.text = "Equipment Manual 1  (Page 3 of 6)";
                    break;
                case 4:
                    lblPageIndicator.text = "Equipment Manual 2  (Page 4 of 6)";
                    break;
                case DossierSpreadIndex:
                    lblPageIndicator.text = "Entity Compendium  (Page 5 of 6)";
                    break;
                case EvidenceSpreadIndex:
                    lblPageIndicator.text = "Deduction Checklist  (Page 6 of 6)";
                    break;
                case LobbySpreadIndex:
                    lblPageIndicator.text = "Safe House Contract, Multiplayer & Equipment Loadout";
                    break;
            }
        }

        // Prev/Next buttons enable state
        if (btnPrevPage != null) btnPrevPage.SetEnabled(currentSpread > 0);
        if (btnNextPage != null) btnNextPage.SetEnabled(currentSpread < maxAllowedSpread);

        // Update active tab bookmarks
        Button[] allTabs = { tabLobby, tabCover, tabToc, tabBlueprint, tabEquip, tabGhosts, tabEvidence };
        foreach (var t in allTabs)
        {
            if (t != null) t.RemoveFromClassList("active-tab");
        }

        if (currentSpread == LobbySpreadIndex && tabLobby != null) tabLobby.AddToClassList("active-tab");
        else if (currentSpread == 0 && tabCover != null) tabCover.AddToClassList("active-tab");
        else if (currentSpread == 1 && tabToc != null) tabToc.AddToClassList("active-tab");
        else if (currentSpread == BlueprintSpreadIndex && tabBlueprint != null) tabBlueprint.AddToClassList("active-tab");
        else if ((currentSpread == 3 || currentSpread == 4) && tabEquip != null) tabEquip.AddToClassList("active-tab");
        else if (currentSpread == DossierSpreadIndex && tabGhosts != null) tabGhosts.AddToClassList("active-tab");
        else if (currentSpread == EvidenceSpreadIndex && tabEvidence != null) tabEvidence.AddToClassList("active-tab");
    }

    private void Update()
    {
        if (SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME) return;

        UpdateLobbyTabVisibility();
        if (!ProgressionAndLoadoutManager.IsInLobbyScene && subscribedMatchManager == null && GameMatchManager.Instance != null)
        {
            EnsureMatchManagerSubscription();
            RefreshCaseBriefingAndBlueprintUI();
        }

        if (isJournalOpen)
        {
            bool textInputFocused = ComputeTextInputFocused();
            bool blockKeysForTextInput = textInputFocused || textInputBlurFrame == Time.frameCount;
            if (blockKeysForTextInput)
            {
                if (textInputFocused && Input.GetKeyDown(KeyCode.Escape))
                {
                    BlurPartyCodeInput();
                }
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.J) || (isJournalOpen && Input.GetKeyDown(KeyCode.Escape)))
        {
            if (DeveloperConsoleManager.IsConsoleOpen) return;
            if (PauseMenuController.IsOpen) return;
            if (TruckDashboardController.IsOpen) return;
            if (SpiritBoxItem.IsLocalDialogueActive) return;
            if (ExorcismManager.IsUIOpen) return;
            ToggleJournal();
        }

        // Keyboard navigation while journal is open (ignore A/D when typing in Party Code TextField or DevConsole)
        if (isJournalOpen && currentSpread != LobbySpreadIndex && !DeveloperConsoleManager.IsConsoleOpen)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                PrevSpread();
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                NextSpread();
            }
        }
    }

    public void ToggleJournal()
    {
        UpdateLobbyTabVisibility();
        isJournalOpen = !isJournalOpen;

        if (isJournalOpen)
        {
            if (SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.SCHOOL_MAP_SCENE_NAME &&
                PlayerController.LocalInstance != null)
            {
                int playerFloor = FloorZone.GetFloorIndex(PlayerController.LocalInstance.transform.position);
                if (playerFloor >= FloorZone.FLOOR_FIRST && playerFloor <= FloorZone.FLOOR_THIRD)
                {
                    selectedSchoolFloor = playerFloor;
                }
            }

            journalOverlay.RemoveFromClassList("hidden");
            RefreshCaseBriefingAndBlueprintUI();
            if (ProgressionAndLoadoutManager.IsInLobbyScene)
            {
                RefreshLobbySpreadUI();
                ShowSpread(LobbySpreadIndex);
            }
        }
        else
        {
            closedFrame = Time.frameCount;
            BlurPartyCodeInput();
            journalOverlay.AddToClassList("hidden");
        }
    }

    /// <summary>
    /// Counts how many of the player's ticked evidences in the Journal match the true ghost's actual evidences.
    /// Used at the end of the match to award currency per correct evidence found.
    /// </summary>
    public int GetCorrectEvidenceCount(string trueGhostName)
    {
        if (string.IsNullOrEmpty(trueGhostName) || !ghostEvidenceMap.TryGetValue(trueGhostName, out var actualEvidences))
        {
            return 0;
        }

        int correctCount = 0;
        foreach (var pair in evidenceToggles)
        {
            if (pair.Value != null && pair.Value.value && actualEvidences.Contains(pair.Key))
            {
                correctCount++;
            }
        }
        return correctCount;
    }

    private void OnEvidenceToggleChanged(string evidenceKey, bool isChecked)
    {
        if (isUpdatingFilters) return;

        // If a ghost is currently selected, check if this toggle change contradicts it
        if (selectedGhost != "None")
        {
            if (ghostEvidenceMap.TryGetValue(selectedGhost, out var ghostEvs))
            {
                // If user checked an evidence that does not belong to the selected ghost,
                // deselect it so evidence-first cross filtering takes over
                if (isChecked && !ghostEvs.Contains(evidenceKey))
                {
                    selectedGhost = "None";
                }
            }
        }

        UpdateCrossFilters();
    }

    private void SelectGhost(string ghostName)
    {
        // Toggle off if same ghost clicked again
        if (selectedGhost == ghostName)
        {
            selectedGhost = "None";
        }
        else
        {
            selectedGhost = ghostName;
        }

        UpdateCrossFilters();
    }

    public static string LastLockedInGuess { get; set; } = "None";

    private void UpdateCrossFilters()
    {
        if (isUpdatingFilters) return;
        isUpdatingFilters = true;

        try
        {
            // Case 1: Specific Ghost is selected (Ghost -> Evidence)
            if (selectedGhost != "None" && ghostEvidenceMap.TryGetValue(selectedGhost, out var ghostEvidences))
            {
                // Automatically disable/gray out any evidence that does not belong to that ghost
                foreach (var pair in evidenceToggles)
                {
                    string evKey = pair.Key;
                    Toggle tgl = pair.Value;
                    if (tgl == null) continue;

                    if (ghostEvidences.Contains(evKey))
                    {
                        tgl.SetEnabled(true);
                        tgl.RemoveFromClassList("strikethrough");
                    }
                    else
                    {
                        tgl.value = false;
                        tgl.SetEnabled(false);
                        tgl.AddToClassList("strikethrough");
                    }
                }

                // Update ghost button styles
                foreach (var pair in ghostButtons)
                {
                    string gName = pair.Key;
                    Button btn = pair.Value;
                    if (btn == null) continue;

                    btn.RemoveFromClassList("selected-ghost-btn");
                    btn.RemoveFromClassList("ghost-btn-disabled");
                    btn.SetEnabled(true);

                    if (gName == selectedGhost)
                    {
                        btn.AddToClassList("selected-ghost-btn");
                    }
                }

                LastLockedInGuess = selectedGhost;
                if (lblDeductionStatus != null)
                {
                    lblDeductionStatus.text = $"Locked Deduction: {selectedGhost}";
                }
                if (GameMatchManager.Instance != null)
                {
                    GameMatchManager.Instance.RecordDeduction(selectedGhost);
                }
            }
            else
            {
                // Case 2: No ghost selected (Evidence -> Ghost)
                // Gather all ticked evidences
                var tickedEvidences = new HashSet<string>();
                foreach (var pair in evidenceToggles)
                {
                    if (pair.Value != null && pair.Value.value)
                    {
                        tickedEvidences.Add(pair.Key);
                    }
                }

                // Automatically disable/gray out any Ghost Types that do not possess ticked evidence
                var candidateGhosts = new List<string>();
                foreach (var pair in ghostEvidenceMap)
                {
                    string gName = pair.Key;
                    var gEvs = pair.Value;

                    bool hasAllTicked = true;
                    foreach (var ticked in tickedEvidences)
                    {
                        if (!gEvs.Contains(ticked))
                        {
                            hasAllTicked = false;
                            break;
                        }
                    }

                    Button btn = ghostButtons.ContainsKey(gName) ? ghostButtons[gName] : null;
                    if (hasAllTicked)
                    {
                        candidateGhosts.Add(gName);
                        if (btn != null)
                        {
                            btn.SetEnabled(true);
                            btn.RemoveFromClassList("ghost-btn-disabled");
                            btn.RemoveFromClassList("selected-ghost-btn");
                        }
                    }
                    else
                    {
                        if (btn != null)
                        {
                            btn.SetEnabled(false);
                            btn.AddToClassList("ghost-btn-disabled");
                            btn.RemoveFromClassList("selected-ghost-btn");
                        }
                    }
                }

                // Collect remaining possible evidences from candidate ghosts
                var possibleEvidences = new HashSet<string>();
                foreach (var cGhost in candidateGhosts)
                {
                    if (ghostEvidenceMap.TryGetValue(cGhost, out var evs))
                    {
                        foreach (var e in evs) possibleEvidences.Add(e);
                    }
                }

                // If candidate ghosts exist and evidence is ticked, gray out impossible evidences
                foreach (var pair in evidenceToggles)
                {
                    string evKey = pair.Key;
                    Toggle tgl = pair.Value;
                    if (tgl == null) continue;

                    if (tickedEvidences.Count > 0 && !possibleEvidences.Contains(evKey))
                    {
                        tgl.value = false;
                        tgl.SetEnabled(false);
                        tgl.AddToClassList("strikethrough");
                    }
                    else
                    {
                        tgl.SetEnabled(true);
                        tgl.RemoveFromClassList("strikethrough");
                    }
                }

                if (candidateGhosts.Count == 1)
                {
                    LastLockedInGuess = candidateGhosts[0];
                    if (lblDeductionStatus != null)
                    {
                        lblDeductionStatus.text = $"Confirmed Match: {candidateGhosts[0]}";
                    }
                    if (GameMatchManager.Instance != null)
                    {
                        GameMatchManager.Instance.RecordDeduction(candidateGhosts[0]);
                    }
                }
                else
                {
                    LastLockedInGuess = "None";
                    if (lblDeductionStatus != null)
                    {
                        lblDeductionStatus.text = tickedEvidences.Count == 0
                            ? $"Select up to 3 evidences to narrow down all {ghostEvidenceMap.Count} entities."
                            : $"{candidateGhosts.Count} matching entities remaining.";
                    }
                }
            }
        }
        finally
        {
            isUpdatingFilters = false;
        }
    }
}
