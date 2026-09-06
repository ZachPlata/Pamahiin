using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class EndGameRevealController : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement endgameOverlay;

    private Label lblTrueGhost;
    private Label lblYourGuess;
    private Label lblResultStatus;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        
        endgameOverlay = root.Q<VisualElement>("endgame-overlay");

        lblTrueGhost = root.Q<Label>("lbl-true-ghost");
        lblYourGuess = root.Q<Label>("lbl-your-guess");
        lblResultStatus = root.Q<Label>("lbl-result-status");

        root.Q<Button>("btn-return-lobby").clicked += OnReturnClicked;

        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.OnMatchEnded += TriggerReveal;
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

        lblTrueGhost.text = trueGhostName.ToUpper();
        lblYourGuess.text = string.IsNullOrEmpty(playerGuess) ? "None" : playerGuess;

        bool isCorrect = string.Equals(trueGhostName, playerGuess, System.StringComparison.OrdinalIgnoreCase);

        if (isCorrect)
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

        // Show the UI
        endgameOverlay.RemoveFromClassList("hidden");
    }

    private void OnReturnClicked()
    {
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.DisconnectAndReturnToMenu();
        }
    }
}
