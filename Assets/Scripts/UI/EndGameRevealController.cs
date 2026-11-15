using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

[RequireComponent(typeof(UIDocument))]
public class EndGameRevealController : MonoBehaviour
{
    public struct PendingMatchRevealData
    {
        public bool isPending;
        public string trueGhostName;
        public string playerGuess;
        public bool isCorrectGhost;
        public bool isGhostDefeated;
        public bool hasPayout;
        public MatchPayoutBreakdown payout;
    }

    private static PendingMatchRevealData pendingReveal;
    private static EndGameRevealController activeInstance;

    public static bool HasPendingReveal => pendingReveal.isPending;
    public static bool IsOpen => activeInstance != null && activeInstance.isOverlayVisible;

    private UIDocument uiDocument;
    private VisualElement endgameOverlay;

    private Label lblTrueGhost;
    private Label lblYourGuess;
    private Label lblResultStatus;

    private Label lblPayoutEvidences;
    private Label lblPayoutGhost;
    private Label lblPayoutDefeat;
    private Label lblPayoutMultiplier;
    private Label lblPayoutTotal;
    private Button returnBtn;

    private bool hasAwardedPayout = false;
    private bool isOverlayVisible = false;
    private Button boundReturnButton;

    public static void ClearPendingReveal()
    {
        pendingReveal = default;
    }

    /// <summary>
    /// Evaluates evidence deductions and awards match currency BEFORE unloading the investigation map,
    /// persisting the report so it can be presented outside the map once back inside Safe House Lobby.
    /// </summary>
    public static void PreparePendingReveal(string trueGhostName, bool defeated)
    {
        string safeTrueName = string.IsNullOrEmpty(trueGhostName) ? "Unknown" : trueGhostName;
        string playerGuess = JournalController.LastLockedInGuess;
        bool isCorrectGhost = string.Equals(safeTrueName, playerGuess, System.StringComparison.OrdinalIgnoreCase);

        int correctEvidences = 0;
        if (JournalController.Instance != null)
        {
            correctEvidences = JournalController.Instance.GetCorrectEvidenceCount(safeTrueName);
        }

        bool isGhostDefeated = defeated ||
                               (GameMatchManager.Instance != null && GameMatchManager.Instance.IsGhostDefeated) ||
                               (ExorcismManager.Instance != null && ExorcismManager.Instance.IsExorcismCompleted);

        ProgressionAndLoadoutManager.EnsureInstance();
        var mgr = ProgressionAndLoadoutManager.Instance;
        MatchPayoutBreakdown breakdown = default;
        bool hasPayout = false;
        if (mgr != null)
        {
            breakdown = mgr.CalculateAndAwardMatchPayout(
                correctEvidences,
                isCorrectGhost,
                isGhostDefeated);
            hasPayout = true;
        }

        pendingReveal = new PendingMatchRevealData
        {
            isPending = true,
            trueGhostName = safeTrueName,
            playerGuess = string.IsNullOrEmpty(playerGuess) ? "None" : playerGuess,
            isCorrectGhost = isCorrectGhost,
            isGhostDefeated = isGhostDefeated,
            hasPayout = hasPayout,
            payout = breakdown
        };
    }

    public static bool TryPresentPendingReveal()
    {
        if (!pendingReveal.isPending)
        {
            return false;
        }

        PendingMatchRevealData data = pendingReveal;
        pendingReveal = default;

        EndGameRevealController controller = EnsureInstance();
        if (controller == null)
        {
            Debug.LogWarning("EndGameRevealController: Unable to instantiate EndGameUI in Lobby to present debriefing.");
            return false;
        }

        controller.ApplyRevealData(data);
        return true;
    }

    public static EndGameRevealController EnsureInstance()
    {
        var existing = UnityEngine.Object.FindAnyObjectByType<EndGameRevealController>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
            }
            return existing;
        }

        VisualTreeAsset vta = Resources.Load<VisualTreeAsset>("UI/EndGameUI");
        if (vta == null)
        {
            Debug.LogWarning("EndGameRevealController: Resources/UI/EndGameUI.uxml not found!");
            return null;
        }

        var go = new GameObject("EndGameRevealController_Lobby");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ResolvePanelSettings();
        doc.visualTreeAsset = vta;
        doc.sortingOrder = 25;

        return go.AddComponent<EndGameRevealController>();
    }

    private static PanelSettings ResolvePanelSettings()
    {
        var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
        for (int i = 0; i < docs.Length; i++)
        {
            if (docs[i] != null && docs[i].panelSettings != null)
            {
                return docs[i].panelSettings;
            }
        }

        var loaded = Resources.FindObjectsOfTypeAll<PanelSettings>();
        if (loaded != null && loaded.Length > 0 && loaded[0] != null)
        {
            return loaded[0];
        }

        var runtimeSettings = ScriptableObject.CreateInstance<PanelSettings>();
        runtimeSettings.name = "EndGameRevealRuntimePanelSettings";
        runtimeSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        runtimeSettings.referenceResolution = new Vector2Int(1920, 1080);
        runtimeSettings.match = 0.5f;
        return runtimeSettings;
    }

    private void Awake()
    {
        activeInstance = this;
        uiDocument = GetComponent<UIDocument>();
        ProgressionAndLoadoutManager.EnsureInstance();
    }

    private void OnEnable()
    {
        activeInstance = this;
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null)
        {
            uiDocument.sortingOrder = 25;
        }
        EnsureUIElementsBound();
    }

    private void EnsureUIElementsBound()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;
        var root = uiDocument.rootVisualElement;

        endgameOverlay = root.Q<VisualElement>("endgame-overlay");

        lblTrueGhost = root.Q<Label>("lbl-true-ghost");
        lblYourGuess = root.Q<Label>("lbl-your-guess");
        lblResultStatus = root.Q<Label>("lbl-result-status");

        lblPayoutEvidences = root.Q<Label>("lbl-payout-evidences");
        lblPayoutGhost = root.Q<Label>("lbl-payout-ghost");
        lblPayoutDefeat = root.Q<Label>("lbl-payout-defeat");
        lblPayoutMultiplier = root.Q<Label>("lbl-payout-multiplier");
        lblPayoutTotal = root.Q<Label>("lbl-payout-total");

        returnBtn = root.Q<Button>("btn-return-lobby");
        if (returnBtn != null && boundReturnButton != returnBtn)
        {
            if (boundReturnButton != null)
            {
                boundReturnButton.clicked -= OnReturnClicked;
            }
            returnBtn.clicked += OnReturnClicked;
            boundReturnButton = returnBtn;
        }
    }

    private void Start()
    {
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.OnMatchEnded += TriggerReveal;
        }

        if (ProgressionAndLoadoutManager.IsInLobbyScene)
        {
            TryPresentPendingReveal();
        }
    }

    private void OnDisable()
    {
        isOverlayVisible = false;
        if (boundReturnButton != null)
        {
            boundReturnButton.clicked -= OnReturnClicked;
            boundReturnButton = null;
        }

        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.OnMatchEnded -= TriggerReveal;
        }
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }
    }

    public void TriggerReveal(string trueGhostName)
    {
        if (!hasAwardedPayout)
        {
            PreparePendingReveal(
                trueGhostName,
                (GameMatchManager.Instance != null && GameMatchManager.Instance.IsGhostDefeated) ||
                (ExorcismManager.Instance != null && ExorcismManager.Instance.IsExorcismCompleted));
            hasAwardedPayout = true;
        }

        if (pendingReveal.isPending)
        {
            PendingMatchRevealData data = pendingReveal;
            pendingReveal = default;
            ApplyRevealData(data);
        }

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.HideIntermission(0.35f);
        }
    }

    private void ApplyRevealData(PendingMatchRevealData data)
    {
        EnsureUIElementsBound();
        ReenableLocalPlayerComponent();

        if (lblTrueGhost != null) lblTrueGhost.text = (data.trueGhostName ?? "UNKNOWN").ToUpper();
        if (lblYourGuess != null) lblYourGuess.text = string.IsNullOrEmpty(data.playerGuess) ? "None" : data.playerGuess;

        if (lblResultStatus != null)
        {
            if (data.isCorrectGhost)
            {
                lblResultStatus.text = "SUCCESS";
                lblResultStatus.RemoveFromClassList("failure");
                lblResultStatus.AddToClassList("success");
            }
            else
            {
                lblResultStatus.text = "FAILED";
                lblResultStatus.RemoveFromClassList("success");
                lblResultStatus.AddToClassList("failure");
            }
        }

        if (data.hasPayout)
        {
            MatchPayoutBreakdown breakdown = data.payout;
            if (lblPayoutEvidences != null)
                lblPayoutEvidences.text = $"Correct Evidences ({breakdown.correctEvidenceCount}/3): +${breakdown.evidenceReward}";
            if (lblPayoutGhost != null)
                lblPayoutGhost.text = $"Correct Ghost Type ({(breakdown.correctGhostType ? "Yes" : "No")}): +${breakdown.ghostTypeReward}";
            if (lblPayoutDefeat != null)
                lblPayoutDefeat.text = $"Ghost Defeated Bonus ({(breakdown.ghostDefeated ? "Exorcised!" : "Not Defeated")}): +${breakdown.ghostDefeatedReward}";
            if (lblPayoutMultiplier != null)
                lblPayoutMultiplier.text = $"Difficulty Multiplier ({breakdown.difficulty}): {breakdown.difficultyMultiplier:0.0}x";
            if (lblPayoutTotal != null)
                lblPayoutTotal.text = $"TOTAL EARNED: +${breakdown.totalEarned}   |   NEW BALANCE: ${breakdown.newBalance}";
        }

        if (returnBtn != null)
        {
            returnBtn.text = ProgressionAndLoadoutManager.IsInLobbyScene
                ? "Continue in Safe House Lobby"
                : "Return to Safe House Lobby";
        }

        if (endgameOverlay != null)
        {
            endgameOverlay.RemoveFromClassList("hidden");
            isOverlayVisible = true;
        }
    }

    private void HideOverlay()
    {
        isOverlayVisible = false;
        if (endgameOverlay != null)
        {
            endgameOverlay.AddToClassList("hidden");
        }
    }

    private static void ReenableLocalPlayerComponent()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;
        }
        else if (PlayerController.LocalInstance != null)
        {
            PlayerController.LocalInstance.enabled = true;
        }
    }

    private void OnReturnClicked()
    {
        ReenableLocalPlayerComponent();

        if (ProgressionAndLoadoutManager.IsInLobbyScene)
        {
            ClearPendingReveal();
            JournalController.LastLockedInGuess = "None";
            HideOverlay();
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            HideOverlay();
            UnityEngine.SceneManagement.SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME);
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            TruckGearSpawner.CleanupAllGhostOrbs();
            HideOverlay();

            if (NetworkController.Instance != null)
            {
                NetworkController.Instance.ReturnToLobbyFromMatch();
            }
            else
            {
                NetworkManager.Singleton.SceneManager.LoadScene(
                    ProgressionAndLoadoutManager.LOBBY_SCENE_NAME,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            if (lblResultStatus != null)
            {
                lblResultStatus.text = "Waiting for Host...";
            }
        }
    }
}
