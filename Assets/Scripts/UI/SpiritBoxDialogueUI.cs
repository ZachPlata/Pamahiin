using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Client-sided Spirit Box dialogue and frequency scanning interface.
/// Overrides player movement and controls while conversing with the entity.
/// Allows selecting 1 of 5 questions via scroll wheel or numeric keys 1-5,
/// broadcasting via Right Click or Enter, and turning off / cancelling via Left Click.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SpiritBoxDialogueUI : MonoBehaviour
{
    public static SpiritBoxDialogueUI Instance { get; private set; }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset dialogueUxml;

    private VisualElement overlay;
    private VisualElement panel;
    private Label frequencyLabel;
    private Label screenStatusLabel;
    private Label screenTranscriptLabel;
    private Label signalMeter;
    private Button[] optionButtons = new Button[5];
    private Label[] optionTexts = new Label[5];
    private Label[] optionArrows = new Label[5];

    private SpiritBoxItem activeSpiritBox;
    private int highlightedIndex = 0;
    private bool isDialogueOpen = false;
    private bool isTransmitting = false;
    private float transmitCooldown = 0f;
    private float sweepTimer = 0f;
    private int openedFrame = -1;
    private float openTime = -1f;
    private int closedFrame = -1;

    public bool IsOpen => isDialogueOpen;
    public static bool WasOpenThisFrame => SpiritBoxItem.IsLocalDialogueActive || (Instance != null && Instance.closedFrame == Time.frameCount);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        // Automatic self-repair: borrow PanelSettings from existing UI documents if missing
        if (uiDocument != null)
        {
            if (uiDocument.panelSettings == null)
            {
                var allDocs = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
                foreach (var d in allDocs)
                {
                    if (d != uiDocument && d.panelSettings != null)
                    {
                        uiDocument.panelSettings = d.panelSettings;
                        break;
                    }
                }
            }

            if (uiDocument.visualTreeAsset == null && dialogueUxml != null)
            {
                uiDocument.visualTreeAsset = dialogueUxml;
            }

            uiDocument.sortingOrder = 50;
        }
    }

    private void OnEnable()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (uiDocument == null) return;

        // Ensure high sorting order so it displays prominently above standard gameplay HUD
        uiDocument.sortingOrder = 50;

        if (uiDocument.panelSettings == null)
        {
            var allDocs = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            foreach (var d in allDocs)
            {
                if (d != uiDocument && d.panelSettings != null)
                {
                    uiDocument.panelSettings = d.panelSettings;
                    break;
                }
            }
        }

        if (uiDocument.visualTreeAsset == null && dialogueUxml != null)
        {
            uiDocument.visualTreeAsset = dialogueUxml;
        }

        var root = uiDocument.rootVisualElement;
        if (root == null) return;

        overlay = root.Q<VisualElement>("SpiritBoxOverlay");
        panel = root.Q<VisualElement>("SpiritBoxPanel");
        frequencyLabel = root.Q<Label>("FrequencyLabel");
        screenStatusLabel = root.Q<Label>("ScreenStatusLabel");
        screenTranscriptLabel = root.Q<Label>("ScreenTranscriptLabel");
        signalMeter = root.Q<Label>("SignalMeter");

        for (int i = 0; i < 5; i++)
        {
            int index = i; // Closure capture
            optionButtons[i] = root.Q<Button>($"Option{i}");
            optionTexts[i] = root.Q<Label>($"OptionText{i}");
            if (optionButtons[i] != null)
            {
                optionArrows[i] = optionButtons[i].Q<Label>(className: "sb-option-arrow");
                
                // Hovering an option highlights it
                optionButtons[i].RegisterCallback<MouseEnterEvent>(evt => SetHighlightedIndex(index));
                
                // Clicking an option button submits it
                optionButtons[i].clicked += () => OnButtonClicked(index);
            }
        }

        // Hide overlay initially
        if (overlay != null)
        {
            overlay.style.display = DisplayStyle.None;
        }
    }

    private void Update()
    {
        if (!isDialogueOpen || activeSpiritBox == null) return;

        // 1. Radio sweep effect animation on frequency readout
        sweepTimer += Time.deltaTime;
        if (sweepTimer >= 0.15f)
        {
            sweepTimer = 0f;
            float randomMhz = Random.Range(87.5f, 108.0f);
            if (frequencyLabel != null)
            {
                frequencyLabel.text = $"FM {randomMhz:F1} MHz";
            }
        }

        // 2. Cooldown timer for inquiry transmissions
        if (isTransmitting)
        {
            transmitCooldown -= Time.deltaTime;
            if (transmitCooldown <= 0f)
            {
                isTransmitting = false;
            }
        }

        // Debounce: ignore clicks on the activation frame and for 0.25s after opening
        if (Time.frameCount == openedFrame || (Time.unscaledTime - openTime) < 0.25f)
        {
            return;
        }

        // ESC -> Turn off Spirit Box and cancel talking
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            activeSpiritBox.TurnOffAndCancel();
            CloseDialogue();
            return;
        }

        // 3. Left Click (Mouse 0) -> Turn off Spirit Box and cancel talking
        if (Input.GetMouseButtonDown(0))
        {
            activeSpiritBox.TurnOffAndCancel();
            CloseDialogue();
            return;
        }

        // 4. Right Click (Mouse 1) -> Pick / Broadcast currently highlighted option
        if (Input.GetMouseButtonDown(1))
        {
            SubmitHighlightedQuestion();
            return;
        }

        // 5. Scroll Wheel -> Navigate through dialogue options
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0.02f)
        {
            CycleSelection(-1); // Up / previous
        }
        else if (scroll < -0.02f)
        {
            CycleSelection(1);  // Down / next
        }

        // 6. Numeric Keys (1 to 5) -> Direct select and transmit
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            SelectAndSubmit(0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            SelectAndSubmit(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            SelectAndSubmit(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            SelectAndSubmit(3);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
        {
            SelectAndSubmit(4);
        }

        // 7. Enter / Space -> Confirm and transmit currently highlighted option
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            SubmitHighlightedQuestion();
        }
    }

    public void OpenDialogue(SpiritBoxItem box, string[] questions)
    {
        if (box == null) return;

        activeSpiritBox = box;
        isDialogueOpen = true;
        isTransmitting = false;
        SpiritBoxItem.IsLocalDialogueActive = true;
        openedFrame = Time.frameCount;
        openTime = Time.unscaledTime;

        if (overlay == null)
        {
            InitializeUI();
        }

        if (overlay != null)
        {
            overlay.style.display = DisplayStyle.Flex;
            overlay.style.visibility = Visibility.Visible;
            overlay.BringToFront();
        }
        else
        {
            Debug.LogError("[SpiritBoxDialogueUI] Overlay element could not be found! Make sure SpiritBoxDialogue.uxml is linked.");
        }

        // Populate question texts if available
        if (questions != null)
        {
            for (int i = 0; i < 5 && i < questions.Length; i++)
            {
                if (optionTexts[i] != null && !string.IsNullOrEmpty(questions[i]))
                {
                    optionTexts[i].text = questions[i];
                }
            }
        }

        highlightedIndex = 0;
        UpdateHighlight();

        if (screenStatusLabel != null)
        {
            screenStatusLabel.text = "[SWEEPING FREQUENCIES...]";
            screenStatusLabel.style.color = new StyleColor(new Color(0.33f, 0.77f, 0.55f));
        }

        if (screenTranscriptLabel != null)
        {
            screenTranscriptLabel.text = "Awaiting inquiry... Select a phrase to broadcast.";
            screenTranscriptLabel.style.color = new StyleColor(new Color(0.89f, 0.98f, 0.94f));
        }

        if (signalMeter != null)
        {
            signalMeter.text = "SIG: |||||";
        }

        Debug.Log("[SpiritBoxDialogueUI] Opened Spirit Box dialogue UI successfully.");
    }

    public void CloseDialogue()
    {
        isDialogueOpen = false;
        activeSpiritBox = null;
        isTransmitting = false;
        SpiritBoxItem.IsLocalDialogueActive = false;
        closedFrame = Time.frameCount;

        if (overlay != null)
        {
            overlay.style.display = DisplayStyle.None;
        }

        Debug.Log("[SpiritBoxDialogueUI] Closed Spirit Box dialogue UI.");
    }

    public void CycleSelection(int delta)
    {
        highlightedIndex = (highlightedIndex + delta + 5) % 5;
        UpdateHighlight();
    }

    public void SetHighlightedIndex(int index)
    {
        if (index >= 0 && index < 5)
        {
            highlightedIndex = index;
            UpdateHighlight();
        }
    }

    private void UpdateHighlight()
    {
        for (int i = 0; i < 5; i++)
        {
            if (optionButtons[i] == null) continue;

            if (i == highlightedIndex)
            {
                optionButtons[i].AddToClassList("sb-option-selected");
            }
            else
            {
                optionButtons[i].RemoveFromClassList("sb-option-selected");
            }
        }
    }

    private void OnButtonClicked(int index)
    {
        if (!isDialogueOpen || isTransmitting) return;
        highlightedIndex = index;
        UpdateHighlight();
        SubmitQuestion(index);
    }

    private void SelectAndSubmit(int index)
    {
        if (!isDialogueOpen || isTransmitting) return;
        SetHighlightedIndex(index);
        SubmitQuestion(index);
    }

    private void SubmitHighlightedQuestion()
    {
        if (!isDialogueOpen || isTransmitting) return;
        SubmitQuestion(highlightedIndex);
    }

    private void SubmitQuestion(int index)
    {
        if (activeSpiritBox == null || isTransmitting) return;

        isTransmitting = true;
        transmitCooldown = 2.5f; // Wait for audio response before next inquiry

        string questionText = (optionTexts[index] != null) ? optionTexts[index].text : $"Question {index + 1}";

        if (screenStatusLabel != null)
        {
            screenStatusLabel.text = "[BROADCASTING INQUIRY...]";
            screenStatusLabel.style.color = new StyleColor(new Color(1f, 0.75f, 0.2f));
        }

        if (screenTranscriptLabel != null)
        {
            screenTranscriptLabel.text = $"You: \"{questionText}\"";
            screenTranscriptLabel.style.color = new StyleColor(new Color(1f, 0.9f, 0.6f));
        }

        activeSpiritBox.AskQuestion(index);
    }

    public void ShowResponse(string question, string responseText, bool hasEvidence)
    {
        if (!isDialogueOpen) return;

        if (hasEvidence)
        {
            if (screenStatusLabel != null)
            {
                screenStatusLabel.text = ">> TRANSMISSION CAPTURED <<";
                screenStatusLabel.style.color = new StyleColor(new Color(0.23f, 0.94f, 0.55f));
            }

            if (screenTranscriptLabel != null)
            {
                screenTranscriptLabel.text = $"Ghost: {responseText}";
                screenTranscriptLabel.style.color = new StyleColor(new Color(0.3f, 1f, 0.7f));
            }

            if (signalMeter != null)
            {
                signalMeter.text = "SIG: [MAX]";
            }
        }
        else
        {
            if (screenStatusLabel != null)
            {
                screenStatusLabel.text = "[STATIC // NO CARRIER]";
                screenStatusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            }

            if (screenTranscriptLabel != null)
            {
                screenTranscriptLabel.text = $"{responseText}";
                screenTranscriptLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            }

            if (signalMeter != null)
            {
                signalMeter.text = "SIG: - - -";
            }
        }
    }
}
