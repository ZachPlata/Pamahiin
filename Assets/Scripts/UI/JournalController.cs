using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class JournalController : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement journalOverlay;
    
    // Toggles
    private Toggle tglEmf5;
    private Toggle tglFreezing;
    private Toggle tglWriting;
    private Toggle tglDots;
    private Toggle tglSpiritBox;
    private Toggle tglOrbs;

    private DropdownField ghostTypeDropdown;
    private Button btnLockIn;
    
    private bool isJournalOpen = false;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        
        journalOverlay = root.Q<VisualElement>("journal-overlay");

        // Bind evidences
        tglEmf5 = root.Q<Toggle>("tgl-emf5");
        tglFreezing = root.Q<Toggle>("tgl-freezing");
        tglWriting = root.Q<Toggle>("tgl-writing");
        tglDots = root.Q<Toggle>("tgl-dots");
        tglSpiritBox = root.Q<Toggle>("tgl-spiritbox");
        tglOrbs = root.Q<Toggle>("tgl-orbs");

        ghostTypeDropdown = root.Q<DropdownField>("dropdown-ghost-type");

        btnLockIn = root.Q<Button>("btn-lock-in");
        btnLockIn.clicked += OnLockInClicked;

        root.Q<Button>("btn-close").clicked += ToggleJournal;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            ToggleJournal();
        }
    }

    private void ToggleJournal()
    {
        isJournalOpen = !isJournalOpen;

        if (isJournalOpen)
        {
            journalOverlay.RemoveFromClassList("hidden");
        }
        else
        {
            journalOverlay.AddToClassList("hidden");
        }
    }

    public static string LastLockedInGuess { get; private set; } = "None";

    private void OnLockInClicked()
    {
        string selectedGhost = ghostTypeDropdown.value;
        LastLockedInGuess = selectedGhost;
        Debug.Log($"Player locked in deduction: {selectedGhost}");
        
        // Pass deduction to GameMatchManager to trigger the end game reveal
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.LockInDeduction(selectedGhost);
            ToggleJournal(); // Close journal
        }
    }
}
