using UnityEngine;
using UnityEngine.UIElements;

public class DeveloperConsoleManager : MonoBehaviour
{
    [SerializeField] private UIDocument consoleDocument;
    
    private VisualElement container;
    private bool isConsoleOpen = false;

    private void OnEnable()
    {
        if (consoleDocument == null || consoleDocument.rootVisualElement == null) return;

        consoleDocument.sortingOrder = 100;
        var root = consoleDocument.rootVisualElement;
        container = root.Q<VisualElement>("DevConsoleContainer");

        var forceHuntBtn = root.Q<Button>("ForceHuntBtn");
        var stopHuntBtn = root.Q<Button>("StopHuntBtn");
        var forceEMF5Btn = root.Q<Button>("ForceEMF5Btn");
        var forceEventBtn = root.Q<Button>("ForceEventBtn");

        if (forceHuntBtn != null) forceHuntBtn.clicked += OnForceHunt;
        if (stopHuntBtn != null) stopHuntBtn.clicked += OnStopHunt;
        if (forceEMF5Btn != null) forceEMF5Btn.clicked += OnForceEMF5;
        if (forceEventBtn != null) forceEventBtn.clicked += OnForceGhostEvent;
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Tilde))
        {
            ToggleConsole();
        }
    }

    private void ToggleConsole()
    {
        if (container == null) return;
        
        isConsoleOpen = !isConsoleOpen;
        container.style.display = isConsoleOpen ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnForceHunt()
    {
        var ghost = FindAnyObjectByType<GhostController>();
        if (ghost != null)
        {
            ghost.ForceStartHunt();
            Debug.Log("[DevConsole] Forced Hunt Started");
        }
    }

    private void OnStopHunt()
    {
        var ghost = FindAnyObjectByType<GhostController>();
        if (ghost != null)
        {
            ghost.EndHunt();
            Debug.Log("[DevConsole] Hunt Stopped");
        }
    }

    private void OnForceEMF5()
    {
        var paranormal = ParanormalManager.Instance;
        var ghost = FindAnyObjectByType<GhostController>();
        
        if (paranormal != null && ghost != null)
        {
            paranormal.RegisterEvent(ghost.transform.position, 5, 20f);
            Debug.Log("[DevConsole] Forced EMF 5 at Ghost Location");
        }
    }

    private void OnForceGhostEvent()
    {
        var paranormal = ParanormalManager.Instance;
        var ghost = FindAnyObjectByType<GhostController>();
        
        if (paranormal != null && ghost != null)
        {
            // Level 3 or 4 could be ghost presence/interaction
            paranormal.RegisterEvent(ghost.transform.position, 3, 20f);
            Debug.Log("[DevConsole] Forced Ghost Event at Ghost Location");
        }
    }
}
