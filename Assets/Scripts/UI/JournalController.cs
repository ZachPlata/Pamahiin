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

    // Spreads (0..7 = Standard Field Journal, 8 = Lobby / Multiplayer / Equipment Shop)
    private const int TotalSpreads = 9;
    private const int LobbySpreadIndex = 8;
    private VisualElement[] spreads = new VisualElement[TotalSpreads];
    private int currentSpread = 0;

    // Top Tabs
    private Button tabLobby;
    private Button tabCover;
    private Button tabToc;
    private Button tabEquip;
    private Button tabGhosts;
    private Button tabEvidence;
    private Button btnCloseTop;

    // Table of Contents Buttons
    private Button btnCoverOpen;
    private Button btnTocEquip1;
    private Button btnTocEquip2;
    private Button btnTocTikbalang;
    private Button btnTocKapre;
    private Button btnTocManananggal;
    private Button btnTocEvidence;

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

    private Button btnTikbalang;
    private Button btnKapre;
    private Button btnManananggal;

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
    private ScrollView loadoutShopList;

    // Two-way cross-filtering data structures
    private readonly Dictionary<string, Toggle> evidenceToggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<string, Button> ghostButtons = new Dictionary<string, Button>();

    private static readonly Dictionary<string, HashSet<string>> ghostEvidenceMap =
        new Dictionary<string, HashSet<string>>
    {
        { "Tikbalang", new HashSet<string> { "EMF", "SpiritBox", "GhostOrbs" } },
        { "Kapre", new HashSet<string> { "EMF", "Freezing", "DOTS" } },
        { "Manananggal", new HashSet<string> { "Freezing", "GhostWriting", "GhostOrbs" } }
    };

    private bool isUpdatingFilters = false;

    private bool isJournalOpen = false;
    private int closedFrame = -1;

    public static JournalController Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isJournalOpen;
    public static bool WasOpenThisFrame => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);
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
        tabEquip = root.Q<Button>("tab-equip");
        tabGhosts = root.Q<Button>("tab-ghosts");
        tabEvidence = root.Q<Button>("tab-evidence");
        btnCloseTop = root.Q<Button>("btn-close-top");

        if (tabLobby != null) tabLobby.clicked += () => ShowSpread(LobbySpreadIndex);
        if (tabCover != null) tabCover.clicked += () => ShowSpread(0);
        if (tabToc != null) tabToc.clicked += () => ShowSpread(1);
        if (tabEquip != null) tabEquip.clicked += () => ShowSpread(2);
        if (tabGhosts != null) tabGhosts.clicked += () => ShowSpread(4);
        if (tabEvidence != null) tabEvidence.clicked += () => ShowSpread(7);
        if (btnCloseTop != null) btnCloseTop.clicked += ToggleJournal;

        // 3. Cover Open Button
        btnCoverOpen = root.Q<Button>("btn-cover-open");
        if (btnCoverOpen != null) btnCoverOpen.clicked += () => ShowSpread(1);

        // 4. Table of Contents Jumps
        btnTocEquip1 = root.Q<Button>("btn-toc-equip1");
        btnTocEquip2 = root.Q<Button>("btn-toc-equip2");
        btnTocTikbalang = root.Q<Button>("btn-toc-tikbalang");
        btnTocKapre = root.Q<Button>("btn-toc-kapre");
        btnTocManananggal = root.Q<Button>("btn-toc-manananggal");
        btnTocEvidence = root.Q<Button>("btn-toc-evidence");

        if (btnTocEquip1 != null) btnTocEquip1.clicked += () => ShowSpread(2);
        if (btnTocEquip2 != null) btnTocEquip2.clicked += () => ShowSpread(3);
        if (btnTocTikbalang != null) btnTocTikbalang.clicked += () => ShowSpread(4);
        if (btnTocKapre != null) btnTocKapre.clicked += () => ShowSpread(5);
        if (btnTocManananggal != null) btnTocManananggal.clicked += () => ShowSpread(6);
        if (btnTocEvidence != null) btnTocEvidence.clicked += () => ShowSpread(7);

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

        // 7. Ghost Deduction Buttons
        ghostButtons.Clear();
        btnTikbalang = root.Q<Button>("btn-ghost-tikbalang");
        btnKapre = root.Q<Button>("btn-ghost-kapre");
        btnManananggal = root.Q<Button>("btn-ghost-manananggal");

        if (btnTikbalang != null) ghostButtons["Tikbalang"] = btnTikbalang;
        if (btnKapre != null) ghostButtons["Kapre"] = btnKapre;
        if (btnManananggal != null) ghostButtons["Manananggal"] = btnManananggal;

        if (btnTikbalang != null) btnTikbalang.clicked += () => SelectGhost("Tikbalang");
        if (btnKapre != null) btnKapre.clicked += () => SelectGhost("Kapre");
        if (btnManananggal != null) btnManananggal.clicked += () => SelectGhost("Manananggal");

        btnClose = root.Q<Button>("btn-close");
        if (btnClose != null) btnClose.clicked += ToggleJournal;

        // 8. Bind Lobby / Multiplayer / Equipment Loadout Spread (Spread 8)
        BindLobbySpreadControls(root);

        if (ProgressionAndLoadoutManager.Instance != null)
        {
            ProgressionAndLoadoutManager.Instance.OnCurrencyOrLoadoutChanged += RefreshLobbySpreadUI;
            ProgressionAndLoadoutManager.Instance.OnContractSettingsChanged += RefreshLobbySpreadUI;
        }
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.OnPartyStateChanged += RefreshLobbySpreadUI;
        }

        UpdateLobbyTabVisibility();

        // Default spread: Lobby spread if in LobbyScene, otherwise Cover
        if (ProgressionAndLoadoutManager.IsInLobbyScene)
        {
            ShowSpread(LobbySpreadIndex);
        }
        else
        {
            ShowSpread(0);
        }
    }

    private void OnDisable()
    {
        if (ProgressionAndLoadoutManager.Instance != null)
        {
            ProgressionAndLoadoutManager.Instance.OnCurrencyOrLoadoutChanged -= RefreshLobbySpreadUI;
            ProgressionAndLoadoutManager.Instance.OnContractSettingsChanged -= RefreshLobbySpreadUI;
        }
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.OnPartyStateChanged -= RefreshLobbySpreadUI;
        }
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
        loadoutShopList = root.Q<ScrollView>("loadout-shop-list");

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
            btnJoinParty.clicked += () =>
            {
                if (NetworkController.Instance != null && inputPartyCode != null)
                {
                    NetworkController.Instance.JoinMultiplayerByPartyCode(inputPartyCode.value);
                    RefreshLobbySpreadUI();
                }
            };
        }

        if (btnStartContract != null)
        {
            btnStartContract.clicked += () =>
            {
                if (NetworkController.Instance != null)
                {
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

        var mgr = ProgressionAndLoadoutManager.Instance;
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
        var mgr = ProgressionAndLoadoutManager.Instance;
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
        if (net != null)
        {
            if (lblPartyCode != null)
            {
                if (!string.IsNullOrEmpty(net.CurrentPartyCode))
                {
                    lblPartyCode.text = $"PARTY CODE: {net.CurrentPartyCode}";
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
        }

        bool isHostOrSolo = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
        int connectedCount = 1;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            connectedCount = NetworkManager.Singleton.IsServer
                ? Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count)
                : Mathf.Max(1, PlayerController.AllPlayers.Count);
        }

        if (lblPartyPlayers != null)
        {
            string roleStr = isHostOrSolo ? "Host" : "Client";
            lblPartyPlayers.text = $"Investigators in Lobby: {connectedCount} ({roleStr})";
        }

        if (btnStartContract != null)
        {
            btnStartContract.SetEnabled(isHostOrSolo);
            btnStartContract.text = isHostOrSolo
                ? "▶ START INVESTIGATION"
                : "WAITING FOR HOST TO START...";
        }

        // Currency & Equipment Loadout UI
        if (lblPlayerCurrency != null)
            lblPlayerCurrency.text = $"CURRENCY: ${mgr.Currency}";

        if (lblLoadoutSummary != null)
            lblLoadoutSummary.text = $"Van Gear: {mgr.GetTotalBroughtItemsCount()} Items (${mgr.GetTotalLoadoutValue()})";

        if (loadoutShopList != null)
        {
            foreach (var entry in mgr.Catalog)
            {
                int count = mgr.GetLoadoutCount(entry.id);
                var qtyLbl = loadoutShopList.Q<Label>($"lbl-qty-{entry.id}");
                if (qtyLbl != null)
                {
                    qtyLbl.text = $"{count} / {entry.maxCount}";
                }

                var btnMinus = loadoutShopList.Q<Button>($"btn-minus-{entry.id}");
                if (btnMinus != null)
                {
                    btnMinus.SetEnabled(count > 0);
                }

                var btnPlus = loadoutShopList.Q<Button>($"btn-plus-{entry.id}");
                if (btnPlus != null)
                {
                    btnPlus.SetEnabled(count < entry.maxCount && mgr.Currency >= entry.cost);
                }
            }
        }
    }

    private static void UpdateDifficultyBtnHighlight(Button btn, bool isActive)
    {
        if (btn == null) return;
        if (isActive) btn.AddToClassList("lobby-diff-btn-active");
        else btn.RemoveFromClassList("lobby-diff-btn-active");
    }

    private void UpdateLobbyTabVisibility()
    {
        bool inLobby = ProgressionAndLoadoutManager.IsInLobbyScene;
        if (tabLobby != null)
        {
            // Multiplayer & Lobby tab ONLY appears inside the Lobby Map and disappears inside the playable map
            tabLobby.style.display = inLobby ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!inLobby && currentSpread == LobbySpreadIndex)
        {
            ShowSpread(0);
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
                    lblPageIndicator.text = "Table of Contents  (Page 1 of 7)";
                    break;
                case 2:
                    lblPageIndicator.text = "Equipment Manual 1  (Page 2 of 7)";
                    break;
                case 3:
                    lblPageIndicator.text = "Equipment Manual 2  (Page 3 of 7)";
                    break;
                case 4:
                    lblPageIndicator.text = "Entity: Tikbalang  (Page 4 of 7)";
                    break;
                case 5:
                    lblPageIndicator.text = "Entity: Kapre  (Page 5 of 7)";
                    break;
                case 6:
                    lblPageIndicator.text = "Entity: Manananggal  (Page 6 of 7)";
                    break;
                case 7:
                    lblPageIndicator.text = "Deduction Checklist  (Page 7 of 7)";
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
        Button[] allTabs = { tabLobby, tabCover, tabToc, tabEquip, tabGhosts, tabEvidence };
        foreach (var t in allTabs)
        {
            if (t != null) t.RemoveFromClassList("active-tab");
        }

        if (currentSpread == LobbySpreadIndex && tabLobby != null) tabLobby.AddToClassList("active-tab");
        else if (currentSpread == 0 && tabCover != null) tabCover.AddToClassList("active-tab");
        else if (currentSpread == 1 && tabToc != null) tabToc.AddToClassList("active-tab");
        else if ((currentSpread == 2 || currentSpread == 3) && tabEquip != null) tabEquip.AddToClassList("active-tab");
        else if ((currentSpread >= 4 && currentSpread <= 6) && tabGhosts != null) tabGhosts.AddToClassList("active-tab");
        else if (currentSpread == 7 && tabEvidence != null) tabEvidence.AddToClassList("active-tab");
    }

    private void Update()
    {
        if (SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME) return;

        UpdateLobbyTabVisibility();

        if (Input.GetKeyDown(KeyCode.J) || (isJournalOpen && Input.GetKeyDown(KeyCode.Escape)))
        {
            if (DeveloperConsoleManager.IsConsoleOpen) return;
            if (PauseMenuController.IsOpen) return;
            if (TruckDashboardController.IsOpen) return;
            if (SpiritBoxItem.IsLocalDialogueActive) return;
            ToggleJournal();
        }

        // Keyboard navigation while journal is open (ignore A/D when typing in Party Code TextField)
        if (isJournalOpen && currentSpread != LobbySpreadIndex)
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
            journalOverlay.RemoveFromClassList("hidden");
            if (ProgressionAndLoadoutManager.IsInLobbyScene)
            {
                RefreshLobbySpreadUI();
                ShowSpread(LobbySpreadIndex);
            }
        }
        else
        {
            closedFrame = Time.frameCount;
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
                    if (GameMatchManager.Instance != null)
                    {
                        GameMatchManager.Instance.RecordDeduction(candidateGhosts[0]);
                    }
                }
                else
                {
                    LastLockedInGuess = "None";
                }
            }
        }
        finally
        {
            isUpdatingFilters = false;
        }
    }
}
