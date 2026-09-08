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

    private Button btnTikbalang;
    private Button btnKapre;
    private Button btnManananggal;
    
    private string selectedGhost = "None";
    private Button btnLockIn;
    
    private bool isJournalOpen = false;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 3;
        var root = uiDocument.rootVisualElement;
        
        journalOverlay = root.Q<VisualElement>("journal-overlay");

        // Bind evidences
        tglEmf5 = root.Q<Toggle>("tgl-emf5");
        tglFreezing = root.Q<Toggle>("tgl-freezing");
        tglWriting = root.Q<Toggle>("tgl-writing");
        tglDots = root.Q<Toggle>("tgl-dots");
        tglSpiritBox = root.Q<Toggle>("tgl-spiritbox");
        tglOrbs = root.Q<Toggle>("tgl-orbs");

        btnTikbalang = root.Q<Button>("btn-ghost-tikbalang");
        btnKapre = root.Q<Button>("btn-ghost-kapre");
        btnManananggal = root.Q<Button>("btn-ghost-manananggal");

        btnTikbalang.clicked += () => SelectGhost("Tikbalang");
        btnKapre.clicked += () => SelectGhost("Kapre");
        btnManananggal.clicked += () => SelectGhost("Manananggal");

        btnLockIn = root.Q<Button>("btn-lock-in");
        btnLockIn.clicked += OnLockInClicked;

        root.Q<Button>("btn-close").clicked += ToggleJournal;
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

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

    private void SelectGhost(string ghostName)
    {
        // Toggle off if same ghost clicked again
        if (selectedGhost == ghostName)
        {
            selectedGhost = "None";
            ResetEvidences();
            UpdateButtonVisuals();
            return;
        }

        selectedGhost = ghostName;
        ResetEvidences();
        UpdateButtonVisuals();

        // Strikethrough invalid evidences based on ghost
        if (ghostName == "Tikbalang")
        {
            DisableEvidence(tglFreezing);
            DisableEvidence(tglWriting);
            DisableEvidence(tglDots);
        }
        else if (ghostName == "Kapre")
        {
            DisableEvidence(tglWriting);
            DisableEvidence(tglSpiritBox);
            DisableEvidence(tglOrbs);
        }
        else if (ghostName == "Manananggal")
        {
            DisableEvidence(tglEmf5);
            DisableEvidence(tglDots);
            DisableEvidence(tglSpiritBox);
        }
    }

    private void DisableEvidence(Toggle tgl)
    {
        tgl.value = false;
        tgl.SetEnabled(false);
        tgl.AddToClassList("strikethrough");
    }

    private void ResetEvidences()
    {
        Toggle[] allToggles = { tglEmf5, tglFreezing, tglWriting, tglDots, tglSpiritBox, tglOrbs };
        foreach (var t in allToggles)
        {
            t.SetEnabled(true);
            t.RemoveFromClassList("strikethrough");
        }
    }

    private void UpdateButtonVisuals()
    {
        btnTikbalang.RemoveFromClassList("selected-ghost-btn");
        btnKapre.RemoveFromClassList("selected-ghost-btn");
        btnManananggal.RemoveFromClassList("selected-ghost-btn");

        if (selectedGhost == "Tikbalang") btnTikbalang.AddToClassList("selected-ghost-btn");
        else if (selectedGhost == "Kapre") btnKapre.AddToClassList("selected-ghost-btn");
        else if (selectedGhost == "Manananggal") btnManananggal.AddToClassList("selected-ghost-btn");
    }

    public static string LastLockedInGuess { get; private set; } = "None";

    private void OnLockInClicked()
    {
        if (selectedGhost == "None") return; // Must select a ghost first
        
        LastLockedInGuess = selectedGhost;
        Debug.Log($"Player locked in deduction: {selectedGhost}");
        
        // Pass deduction to GameMatchManager to trigger the end game reveal
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.LockInDeduction(selectedGhost);
        }
        
        ToggleJournal(); // Close journal
    }
}
