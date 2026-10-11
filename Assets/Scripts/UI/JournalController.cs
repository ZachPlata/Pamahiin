using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class JournalController : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement journalOverlay;

    // Spreads
    private const int TotalSpreads = 8;
    private VisualElement[] spreads = new VisualElement[TotalSpreads];
    private int currentSpread = 0;

    // Top Tabs
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

    // Two-way cross-filtering data structures
    private readonly System.Collections.Generic.Dictionary<string, Toggle> evidenceToggles = new System.Collections.Generic.Dictionary<string, Toggle>();
    private readonly System.Collections.Generic.Dictionary<string, Button> ghostButtons = new System.Collections.Generic.Dictionary<string, Button>();

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> ghostEvidenceMap =
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>
    {
        { "Tikbalang", new System.Collections.Generic.HashSet<string> { "EMF", "SpiritBox", "GhostOrbs" } },
        { "Kapre", new System.Collections.Generic.HashSet<string> { "EMF", "Freezing", "DOTS" } },
        { "Manananggal", new System.Collections.Generic.HashSet<string> { "Freezing", "GhostWriting", "GhostOrbs" } }
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
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 3;
        var root = uiDocument.rootVisualElement;

        journalOverlay = root.Q<VisualElement>("journal-overlay");

        // 1. Spreads
        for (int i = 0; i < TotalSpreads; i++)
        {
            spreads[i] = root.Q<VisualElement>($"spread-{i}");
        }

        // 2. Bookmark Tabs
        tabCover = root.Q<Button>("tab-cover");
        tabToc = root.Q<Button>("tab-toc");
        tabEquip = root.Q<Button>("tab-equip");
        tabGhosts = root.Q<Button>("tab-ghosts");
        tabEvidence = root.Q<Button>("tab-evidence");
        btnCloseTop = root.Q<Button>("btn-close-top");

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

        // Default spread: Cover
        ShowSpread(0);
    }

    public void ShowSpread(int index)
    {
        currentSpread = Mathf.Clamp(index, 0, TotalSpreads - 1);

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

        UpdateNavigationVisuals();
    }

    public void NextSpread()
    {
        if (currentSpread < TotalSpreads - 1)
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
            }
        }

        // Prev/Next buttons enable state
        if (btnPrevPage != null) btnPrevPage.SetEnabled(currentSpread > 0);
        if (btnNextPage != null) btnNextPage.SetEnabled(currentSpread < TotalSpreads - 1);

        // Update active tab bookmarks
        Button[] allTabs = { tabCover, tabToc, tabEquip, tabGhosts, tabEvidence };
        foreach (var t in allTabs)
        {
            if (t != null) t.RemoveFromClassList("active-tab");
        }

        if (currentSpread == 0 && tabCover != null) tabCover.AddToClassList("active-tab");
        else if (currentSpread == 1 && tabToc != null) tabToc.AddToClassList("active-tab");
        else if ((currentSpread == 2 || currentSpread == 3) && tabEquip != null) tabEquip.AddToClassList("active-tab");
        else if ((currentSpread >= 4 && currentSpread <= 6) && tabGhosts != null) tabGhosts.AddToClassList("active-tab");
        else if (currentSpread == 7 && tabEvidence != null) tabEvidence.AddToClassList("active-tab");
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        if (Input.GetKeyDown(KeyCode.J) || (isJournalOpen && Input.GetKeyDown(KeyCode.Escape)))
        {
            if (DeveloperConsoleManager.IsConsoleOpen) return;
            if (PauseMenuController.IsOpen) return;
            if (TruckDashboardController.IsOpen) return;
            if (SpiritBoxItem.IsLocalDialogueActive) return;
            ToggleJournal();
        }

        // Keyboard navigation while journal is open
        if (isJournalOpen)
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
        isJournalOpen = !isJournalOpen;

        if (isJournalOpen)
        {
            journalOverlay.RemoveFromClassList("hidden");
        }
        else
        {
            closedFrame = Time.frameCount;
            journalOverlay.AddToClassList("hidden");
        }
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
                var tickedEvidences = new System.Collections.Generic.HashSet<string>();
                foreach (var pair in evidenceToggles)
                {
                    if (pair.Value != null && pair.Value.value)
                    {
                        tickedEvidences.Add(pair.Key);
                    }
                }

                // Automatically disable/gray out any Ghost Types that do not possess ticked evidence
                var candidateGhosts = new System.Collections.Generic.List<string>();
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
                var possibleEvidences = new System.Collections.Generic.HashSet<string>();
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
