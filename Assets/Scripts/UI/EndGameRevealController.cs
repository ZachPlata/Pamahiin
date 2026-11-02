using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

[RequireComponent(typeof(UIDocument))]
public class EndGameRevealController : MonoBehaviour
{
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

    private bool hasAwardedPayout = false;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        ProgressionAndLoadoutManager.EnsureInstance();
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 25;
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

        var returnBtn = root.Q<Button>("btn-return-lobby");
        if (returnBtn != null)
        {
            returnBtn.clicked += OnReturnClicked;
        }
    }

    private void Start()
    {
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.OnMatchEnded += TriggerReveal;
        }
        else
        {
            Debug.LogWarning("EndGameRevealController: GameMatchManager.Instance is null! Event not subscribed.");
        }
    }

    private void OnDisable()
    {
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.OnMatchEnded -= TriggerReveal;
        }
    }

    public void TriggerReveal(string trueGhostName)
    {
        string playerGuess = JournalController.LastLockedInGuess;

        if (lblTrueGhost != null) lblTrueGhost.text = trueGhostName.ToUpper();
        if (lblYourGuess != null) lblYourGuess.text = string.IsNullOrEmpty(playerGuess) ? "None" : playerGuess;

        bool isCorrectGhost = string.Equals(trueGhostName, playerGuess, System.StringComparison.OrdinalIgnoreCase);

        if (lblResultStatus != null)
        {
            if (isCorrectGhost)
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

        // Calculate and award In-Game Currency
        if (!hasAwardedPayout)
        {
            hasAwardedPayout = true;

            int correctEvidences = 0;
            if (JournalController.Instance != null)
            {
                correctEvidences = JournalController.Instance.GetCorrectEvidenceCount(trueGhostName);
            }

            bool isGhostDefeated = (GameMatchManager.Instance != null && GameMatchManager.Instance.IsGhostDefeated) ||
                                   (ExorcismManager.Instance != null && ExorcismManager.Instance.IsExorcismCompleted);

            ProgressionAndLoadoutManager.EnsureInstance();
            var mgr = ProgressionAndLoadoutManager.Instance;
            if (mgr != null)
            {
                MatchPayoutBreakdown breakdown = mgr.CalculateAndAwardMatchPayout(
                    correctEvidences,
                    isCorrectGhost,
                    isGhostDefeated);

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
        }

        // Show the UI
        if (endgameOverlay != null)
        {
            endgameOverlay.RemoveFromClassList("hidden");
        }
    }

    private void OnReturnClicked()
    {
        // Re-enable local player controller if it was disabled on match end
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME);
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
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
