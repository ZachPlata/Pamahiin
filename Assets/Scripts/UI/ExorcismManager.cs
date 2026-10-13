using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(UIDocument))]
public class ExorcismManager : NetworkBehaviour
{
    public static ExorcismManager Instance { get; private set; }

    private UIDocument uiDocument;

    // 1. Ghost Selection Popup Elements
    private VisualElement ghostSelectPopup;
    private Button btnTikbalang;
    private Button btnKapre;
    private Button btnManananggal;

    // 2. Circular Minigame Overlay Elements
    private VisualElement exorcismOverlay;
    private VisualElement circleDial;
    private VisualElement targetBarContainer;
    private VisualElement needleContainer;
    private Label promptKeyLabel;
    private Label lblChants;
    private Label lblLoop;
    private Label lblMistakes;
    private Label lblFeedback;

    // 3. Dialogue / Banner Elements
    private VisualElement dialogueBanner;
    private Label bannerTitle;
    private Label bannerText;
    private Label bannerSubtext;

    // 4. Crucifix Repel Elements
    private VisualElement repelBanner;
    private Label repelTimerLabel;

    [Header("Exorcism Audio Settings")]
    [Tooltip("Audio clip played when the ghost is dying/exorcised.")]
    [SerializeField] private AudioClip dyingShriekClip;

    [Header("Minigame Settings")]
    [SerializeField] private float baseRotationSpeed = 160f;
    [SerializeField] private int requiredChants = 5;
    [SerializeField] private int maxLoops = 2;
    [SerializeField] private int maxMistakes = 3;
    [Tooltip("Angular tolerance in degrees for registering a successful overlap with the target bar.")]
    [SerializeField] private float hitTolerance = 22f;

    // Runtime state
    private Vector3 chalkDiagramPos = Vector3.zero;
    private string selectedGhostGuess = "";
    private bool isMinigameActive = false;
    private float needleAngle = 0f;
    private float targetAngle = 180f;
    private int currentLoops = 0;
    private int mistakesCount = 0;
    private int successfulChants = 0;
    private float currentRotationSpeed = 160f;

    // Key Prompts: A, E, R, G, B
    private readonly KeyCode[] promptKeys = new KeyCode[] { KeyCode.A, KeyCode.E, KeyCode.R, KeyCode.G, KeyCode.B };
    private readonly string[] promptKeyNames = new string[] { "A", "E", "R", "G", "B" };
    private int currentKeyIndex = 0;

    // Red volume for failure state
    private Volume redAtmosphereVolume;
    private Coroutine repelCoroutine;
    private Coroutine bannerHideCoroutine;
    private bool isExorcismActive = false;
    private bool isExorcismCompleted = false;
    private bool uiButtonsBound = false;

    public bool IsMinigameActive => isMinigameActive;
    public bool IsExorcismActive => isExorcismActive || isMinigameActive;
    public bool IsExorcismCompleted => isExorcismCompleted;

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

    private void OnEnable()
    {
        EnsureUIReferences();
    }

    private void EnsureUIReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (uiDocument != null)
        {
            uiDocument.sortingOrder = 4;
            var root = uiDocument.rootVisualElement;
            if (root != null)
            {
                // Ghost Selection Popup
                ghostSelectPopup = root.Q<VisualElement>("ghost-select-popup");
                btnTikbalang = root.Q<Button>("btn-ghost-tikbalang");
                btnKapre = root.Q<Button>("btn-ghost-kapre");
                btnManananggal = root.Q<Button>("btn-ghost-manananggal");

                if (!uiButtonsBound)
                {
                    if (btnTikbalang != null) btnTikbalang.clicked += () => OnGhostSelected("Tikbalang");
                    if (btnKapre != null) btnKapre.clicked += () => OnGhostSelected("Kapre");
                    if (btnManananggal != null) btnManananggal.clicked += () => OnGhostSelected("Manananggal");
                    uiButtonsBound = true;
                }

                // Circular Minigame
                exorcismOverlay = root.Q<VisualElement>("exorcism-overlay");
                circleDial = root.Q<VisualElement>("circle-dial");
                targetBarContainer = root.Q<VisualElement>("target-bar-container");
                needleContainer = root.Q<VisualElement>("needle-container");
                promptKeyLabel = root.Q<Label>("prompt-key-label");
                lblChants = root.Q<Label>("lbl-chants");
                lblLoop = root.Q<Label>("lbl-loop");
                lblMistakes = root.Q<Label>("lbl-mistakes");
                lblFeedback = root.Q<Label>("lbl-feedback");

                // Dialogue Banner
                dialogueBanner = root.Q<VisualElement>("dialogue-banner");
                bannerTitle = root.Q<Label>("banner-title");
                bannerText = root.Q<Label>("banner-text");
                bannerSubtext = root.Q<Label>("banner-subtext");

                // Repel Banner
                repelBanner = root.Q<VisualElement>("repel-banner");
                repelTimerLabel = root.Q<Label>("repel-timer-label");
            }
        }
    }

    // ── Initiation & UI Popup ──────────────────────────────────────────

    private void SetGhostButtonsInteractable(bool interactable)
    {
        if (btnTikbalang != null) btnTikbalang.SetEnabled(interactable);
        if (btnKapre != null) btnKapre.SetEnabled(interactable);
        if (btnManananggal != null) btnManananggal.SetEnabled(interactable);
    }

    /// <summary>
    /// Triggered when the chalk diagram is placed or interacted with on the floor.
    /// Opens the ghost selection popup to finalize the guess before the minigame starts.
    /// </summary>
    public void InitiateRitual(Vector3 diagramPos)
    {
        if (isExorcismCompleted || isExorcismActive || isMinigameActive)
        {
            return;
        }

        chalkDiagramPos = diagramPos;

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            InitiateRitualClientRpc(diagramPos);
        }
        else
        {
            InitiateRitualLocal(diagramPos);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void InitiateRitualClientRpc(Vector3 diagramPos)
    {
        InitiateRitualLocal(diagramPos);
    }

    private void InitiateRitualLocal(Vector3 diagramPos)
    {
        if (isExorcismCompleted || isExorcismActive || isMinigameActive)
        {
            return;
        }

        isExorcismActive = true;
        chalkDiagramPos = diagramPos;
        EnsureUIReferences();
        SetGhostButtonsInteractable(true);

        if (ghostSelectPopup != null)
        {
            ghostSelectPopup.RemoveFromClassList("hidden");
        }
    }

    private void OnGhostSelected(string ghostName)
    {
        if (!isExorcismActive || isMinigameActive)
        {
            return;
        }

        // Disable immediately upon the first click to prevent double clicks and duplicate starts
        SetGhostButtonsInteractable(false);

        selectedGhostGuess = ghostName;
        JournalController.LastLockedInGuess = ghostName;
        Debug.Log($"[ExorcismManager] Ghost type selected: {ghostName}");

        if (ghostSelectPopup != null)
        {
            ghostSelectPopup.AddToClassList("hidden");
        }

        // Start the circular skill check minigame
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            StartCircularSkillCheckClientRpc(chalkDiagramPos);
        }
        else
        {
            StartCircularSkillCheckLocal(chalkDiagramPos);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartCircularSkillCheckClientRpc(Vector3 diagramPos)
    {
        StartCircularSkillCheckLocal(diagramPos);
    }

    private void StartCircularSkillCheckLocal(Vector3 diagramPos)
    {
        chalkDiagramPos = diagramPos;
        EnsureUIReferences();

        // 1. Teleport and freeze the hunter ghost inside the chalk circle
        SetupGhostInRitualCircle(diagramPos);

        // 2. Reset minigame parameters
        needleAngle = 0f;
        currentLoops = 0;
        mistakesCount = 0;
        successfulChants = 0;
        currentRotationSpeed = baseRotationSpeed;
        isMinigameActive = true;

        PickNewKeyAndTarget();
        UpdateMinigameUI();

        if (lblFeedback != null) lblFeedback.text = "Press key when needle overlaps the target bar (Max 2 loops)!";
        if (exorcismOverlay != null) exorcismOverlay.RemoveFromClassList("hidden");
    }

    private void SetupGhostInRitualCircle(Vector3 centerPos)
    {
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null && ghostHandler.hunterGhost != null)
        {
            var hunter = ghostHandler.hunterGhost;
            hunter.transform.position = centerPos;

            var agent = hunter.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                if (agent.isOnNavMesh) agent.Warp(centerPos);
                agent.isStopped = true;
            }

            // Disable movement script so ghost is frozen in place
            hunter.enabled = false;

            // Enable its SpriteRenderer and set sorting layer to Van so it is clearly visible
            var sr = hunter.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sr.enabled = true;
                sr.sortingLayerName = "Van";
                sr.sortingOrder = 10;
            }
        }
    }

    private void PickNewKeyAndTarget()
    {
        currentKeyIndex = Random.Range(0, promptKeys.Length);
        if (promptKeyLabel != null)
        {
            promptKeyLabel.text = promptKeyNames[currentKeyIndex];
        }

        // Randomize target angle on the circle (between 60° and 300° so it gives ample reaction from 0°)
        targetAngle = Random.Range(60f, 300f);
        if (targetBarContainer != null)
        {
            targetBarContainer.style.rotate = new Rotate(Angle.Degrees(targetAngle));
        }
    }

    private void UpdateMinigameUI()
    {
        if (lblChants != null) lblChants.text = $"Chants: {successfulChants} / {requiredChants}";
        if (lblLoop != null) lblLoop.text = $"Loop: {currentLoops + 1} / {maxLoops}";
        if (lblMistakes != null) lblMistakes.text = $"Mistakes: {mistakesCount} / {maxMistakes}";
    }

    private void Update()
    {
        if (!isMinigameActive) return;

        // 1. Rotate the needle
        needleAngle += currentRotationSpeed * Time.deltaTime;
        if (needleAngle >= 360f)
        {
            needleAngle -= 360f;
            currentLoops++;
            UpdateMinigameUI();

            if (currentLoops >= maxLoops)
            {
                OnMistake("Too slow! Time expired.");
            }
        }

        if (needleContainer != null)
        {
            needleContainer.style.rotate = new Rotate(Angle.Degrees(needleAngle));
        }

        // 2. Keyboard Input: check A, E, R, G, B with target bar overlap timing
        for (int i = 0; i < promptKeys.Length; i++)
        {
            if (Input.GetKeyDown(promptKeys[i]))
            {
                if (i != currentKeyIndex)
                {
                    OnMistake($"Wrong key! Pressed {promptKeyNames[i]}, required {promptKeyNames[currentKeyIndex]}.");
                }
                else
                {
                    // Check if the moving indicator overlaps with the target bar
                    float angleDiff = Mathf.Abs(Mathf.DeltaAngle(needleAngle, targetAngle));
                    if (angleDiff <= hitTolerance)
                    {
                        OnCorrectKey();
                    }
                    else
                    {
                        OnMistake("Missed target bar! Timing was off.");
                    }
                }
                break;
            }
        }
    }

    private void OnCorrectKey()
    {
        successfulChants++;
        needleAngle = 0f;
        currentLoops = 0;
        currentRotationSpeed += 15f; // Fast-paced acceleration

        if (lblFeedback != null) lblFeedback.text = "Target hit! Chant resonant!";

        if (successfulChants >= requiredChants)
        {
            EvaluateOutcome();
        }
        else
        {
            PickNewKeyAndTarget();
            UpdateMinigameUI();
        }
    }

    private void OnMistake(string reason)
    {
        mistakesCount++;
        needleAngle = 0f;
        currentLoops = 0;

        if (lblFeedback != null) lblFeedback.text = reason;
        UpdateMinigameUI();

        if (mistakesCount >= maxMistakes)
        {
            EvaluateOutcome();
        }
        else
        {
            PickNewKeyAndTarget();
        }
    }

    // ── Outcomes (Success vs Failure) ──────────────────────────────────

    private void EvaluateOutcome()
    {
        isMinigameActive = false;
        if (exorcismOverlay != null) exorcismOverlay.AddToClassList("hidden");

        string actualGhostName = "";
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            actualGhostName = ghostHandler.ghostName;
        }

        bool isGhostCorrect = !string.IsNullOrEmpty(selectedGhostGuess) &&
                              selectedGhostGuess.Equals(actualGhostName, System.StringComparison.OrdinalIgnoreCase);

        bool isMinigameSuccess = (mistakesCount < maxMistakes && successfulChants >= requiredChants);

        if (isGhostCorrect && isMinigameSuccess)
        {
            isExorcismActive = false;
            isExorcismCompleted = true;
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                TriggerSuccessClientRpc();
            }
            else
            {
                TriggerSuccessLocal();
            }
        }
        else
        {
            isExorcismActive = false;
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
            {
                TriggerFailureClientRpc();
            }
            else
            {
                TriggerFailureLocal();
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerSuccessClientRpc()
    {
        TriggerSuccessLocal();
    }

    private void TriggerSuccessLocal()
    {
        Debug.Log("[ExorcismManager] SUCCESS! Correct ghost identified and minigame passed.");

        // 1. Ghost shakes, plays dying shriek, and slowly disappears (GhostHandler is kept intact)
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            // Immediately halt ongoing hunts and unlock doors so players are safe
            ghostHandler.EndHunt();
            ghostHandler.SetDoorsLocked(false);

            if (ghostHandler.hunterGhost != null)
            {
                // Manifest the dying ghost directly at the ritual chalk position so players clearly witness the dying sequence
                Vector3 manifestPos = (chalkDiagramPos != Vector3.zero) ? chalkDiagramPos : transform.position;
                ghostHandler.hunterGhost.StartDyingSequence(dyingShriekClip, manifestPos);
            }
            else
            {
                ghostHandler.OnGhostExorcised();
            }

            if (GameMatchManager.Instance != null && !string.IsNullOrEmpty(ghostHandler.ghostName))
            {
                GameMatchManager.Instance.SetTrueGhostName(ghostHandler.ghostName);
            }
        }
        else if (GameMatchManager.Instance != null && !string.IsNullOrEmpty(selectedGhostGuess))
        {
            GameMatchManager.Instance.SetTrueGhostName(selectedGhostGuess);
        }

        // 2. Record deduction for the van / endgame screen
        JournalController.LastLockedInGuess = selectedGhostGuess;
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.RecordDeduction(selectedGhostGuess);
        }

        // 3. Show dialogue banner: "Ghost has been exorcised. Correct ghost identified."
        ShowDialogueBanner(
            "EXORCISM COMPLETE",
            $"Entity banished ({selectedGhostGuess}). Investigation successful!",
            "Return to the van and press the 'End Game' button to leave and review mission results."
        );
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerFailureClientRpc()
    {
        TriggerFailureLocal();
    }

    private void TriggerFailureLocal()
    {
        Debug.Log("[ExorcismManager] FAILURE! Triggering standard hunt process with failed exorcism condition.");

        // Reusing the standard hunt process on GhostHandler with isFromFailedExorcism = true
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            if (IsServer || !IsSpawned)
            {
                ghostHandler.StartHunt(true);
            }
            else
            {
                RequestFailedExorcismHuntServerRpc();
            }
        }

        // Show failure warning dialogue banner
        ShowDialogueBanner(
            "⚠ RITUAL FAILED: GHOST ANGERED ⚠",
            "The entity has broken free in an Angered State!",
            "USE A CRUCIFIX TO REPEL IT! Hide in a cabinet or escape the house!"
        );
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestFailedExorcismHuntServerRpc()
    {
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            ghostHandler.StartHunt(true);
        }
    }

    public void SetRedEnvironment(bool active)
    {
        if (active)
        {
            TurnEnvironmentRed();
        }
        else
        {
            RestoreEnvironmentLighting();
        }
    }

    private void TurnEnvironmentRed()
    {
        // 1. Tint 2D lights blood-red
        var lights = Object.FindObjectsByType<Light2D>(FindObjectsInactive.Exclude);
        foreach (var l in lights)
        {
            if (l.transform.parent != null && l.transform.parent.name.ToLower().Contains("flashlight")) continue;
            l.color = new Color(1f, 0.12f, 0.12f, 1f);
        }

        // 2. Global Volume red color adjustments
        EnsureRedAtmosphereVolume();
        if (redAtmosphereVolume != null)
        {
            redAtmosphereVolume.weight = 1f;
        }
    }

    private void RestoreEnvironmentLighting()
    {
        if (redAtmosphereVolume != null)
        {
            redAtmosphereVolume.weight = 0f;
        }

        var lights = Object.FindObjectsByType<Light2D>(FindObjectsInactive.Exclude);
        foreach (var l in lights)
        {
            if (l.transform.parent != null && l.transform.parent.name.ToLower().Contains("flashlight")) continue;
            l.color = Color.white;
        }
    }

    private void EnsureRedAtmosphereVolume()
    {
        if (redAtmosphereVolume == null)
        {
            GameObject volumeObj = new GameObject("Exorcism_RedAtmosphere_Volume");
            volumeObj.transform.SetParent(null);
            redAtmosphereVolume = volumeObj.AddComponent<Volume>();
            redAtmosphereVolume.isGlobal = true;
            redAtmosphereVolume.priority = 250;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            if (profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj.colorFilter.overrideState = true;
                colorAdj.colorFilter.value = new Color(1f, 0.18f, 0.18f);
                colorAdj.saturation.overrideState = true;
                colorAdj.saturation.value = 50f;
            }
            else
            {
                var newAdj = profile.Add<ColorAdjustments>(true);
                newAdj.colorFilter.overrideState = true;
                newAdj.colorFilter.value = new Color(1f, 0.18f, 0.18f);
                newAdj.saturation.overrideState = true;
                newAdj.saturation.value = 50f;
            }

            redAtmosphereVolume.sharedProfile = profile;
        }
    }

    // ── Dialogue and Repel Banners ─────────────────────────────────────

    public void ShowDialogueBanner(string title, string text, string subtext, float duration = 8f)
    {
        EnsureUIReferences();
        if (dialogueBanner == null) return;

        if (bannerTitle != null) bannerTitle.text = title;
        if (bannerText != null) bannerText.text = text;
        if (bannerSubtext != null) bannerSubtext.text = subtext;

        dialogueBanner.RemoveFromClassList("hidden");

        if (bannerHideCoroutine != null) StopCoroutine(bannerHideCoroutine);
        bannerHideCoroutine = StartCoroutine(HideBannerAfterDelay(dialogueBanner, duration));
    }

    public void ShowRepelBanner(float duration = 10f)
    {
        EnsureUIReferences();
        if (repelBanner == null) return;

        if (repelCoroutine != null) StopCoroutine(repelCoroutine);
        repelCoroutine = StartCoroutine(RepelCountdownRoutine(duration));
    }

    private IEnumerator RepelCountdownRoutine(float duration)
    {
        repelBanner.RemoveFromClassList("hidden");
        float timeLeft = duration;

        while (timeLeft > 0f)
        {
            if (repelTimerLabel != null)
            {
                repelTimerLabel.text = $"{Mathf.CeilToInt(timeLeft)}s REMAINING";
            }
            timeLeft -= Time.deltaTime;
            yield return null;
        }

        if (repelTimerLabel != null) repelTimerLabel.text = "0s REMAINING";
        repelBanner.AddToClassList("hidden");
    }

    private IEnumerator HideBannerAfterDelay(VisualElement banner, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (banner != null)
        {
            banner.AddToClassList("hidden");
        }
    }

    // Backward compatibility wrapper for old callers
    public void StartExorcism()
    {
        InitiateRitual(transform.position);
    }
}
