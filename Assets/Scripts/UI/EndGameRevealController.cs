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

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 5;
        var root = uiDocument.rootVisualElement;
        
        endgameOverlay = root.Q<VisualElement>("endgame-overlay");

        lblTrueGhost = root.Q<Label>("lbl-true-ghost");
        lblYourGuess = root.Q<Label>("lbl-your-guess");
        lblResultStatus = root.Q<Label>("lbl-result-status");

        root.Q<Button>("btn-return-lobby").clicked += OnReturnClicked;
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
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            // Offline fallback
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            if (NetworkManager.Singleton.ConnectedClientsIds.Count <= 1)
            {
                // Singleplayer: Disconnect fully
                if (NetworkController.Instance != null)
                {
                    NetworkController.Instance.DisconnectAndReturnToMenu();
                }
            }
            else
            {
                // Multiplayer Host: Return to lobby (keep network alive)
                NetworkManager.Singleton.SceneManager.LoadScene("MainMenuScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            lblResultStatus.text = "Waiting for Host...";
        }
    }
}
