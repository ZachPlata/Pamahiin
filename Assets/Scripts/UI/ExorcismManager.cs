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
    private VisualElement ghostButtonsContainer;
    private readonly List<Button> ghostSelectionButtons = new List<Button>();
    private readonly Dictionary<string, Button> ghostSelectionButtonMap = new Dictionary<string, Button>(System.StringComparer.OrdinalIgnoreCase);

    // 2. Circular Minigame Overlay Elements
    private VisualElement exorcismOverlay;
    private VisualElement circleDial;
    private VisualElement targetBarContainer;
    private VisualElement targetHitLine;
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
    [SerializeField] private float hitTolerance = 8.5f;

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

    [Header("Exorcism Failure Atmosphere")]
    [SerializeField] private float redLightTransitionDuration = 3.2f;

    // Server-authoritative multiplayer ritual ownership lock
    private NetworkVariable<ulong> activeRitualClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> syncedExorcismActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> syncedExorcismCompleted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> syncedRitualChanting = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Red volume & smooth transition state for failure state
    private Volume redAtmosphereVolume;
    private readonly Dictionary<Light2D, Color> originalLightColors = new Dictionary<Light2D, Color>();
    private Coroutine redEnvTransitionCoroutine;
    private Coroutine summonSequenceCoroutine;
    private Coroutine repelCoroutine;
    private Coroutine bannerHideCoroutine;
    private bool isExorcismActive = false;
    private bool isExorcismCompleted = false;
    private bool isOwningLocalRitual = false;
    private int closedFrame = -1;
    private bool uiButtonsBound = false;

    private bool IsNetworkSpawned =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkObject != null && IsSpawned;

    public static bool IsUIOpen => Instance != null && Instance.IsUiPanelOpen;
    public static bool WasOpenThisFrame => IsUIOpen || (Instance != null && Instance.closedFrame == Time.frameCount);

    public bool IsMinigameActive => isMinigameActive;
    public bool IsExorcismActive => IsNetworkSpawned
        ? (syncedExorcismActive.Value || isExorcismActive || isMinigameActive)
        : (isExorcismActive || isMinigameActive);
    public bool IsExorcismCompleted => IsNetworkSpawned
        ? (syncedExorcismCompleted.Value || isExorcismCompleted)
        : isExorcismCompleted;
    public bool IsRedEnvironmentActive { get; private set; }
    public bool IsUiPanelOpen =>
        isMinigameActive ||
        (ghostSelectPopup != null && !ghostSelectPopup.ClassListContains("hidden")) ||
        (exorcismOverlay != null && !exorcismOverlay.ClassListContains("hidden"));

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

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        base.OnDestroy();
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
                ghostButtonsContainer = root.Q<VisualElement>("ghost-buttons-container");

                if (ghostButtonsContainer != null && (!uiButtonsBound || ghostButtonsContainer.childCount == 0))
                {
                    ghostButtonsContainer.Clear();
                    ghostSelectionButtons.Clear();
                    ghostSelectionButtonMap.Clear();

                    var ghosts = GhostCatalog.AllGhosts;
                    for (int i = 0; i < ghosts.Count; i++)
                    {
                        string ghostId = ghosts[i].Id;
                        var btn = new Button
                        {
                            name = $"btn-ghost-{ghostId.ToLowerInvariant().Replace(" ", "-")}",
                            text = ghostId.ToUpperInvariant()
                        };
                        btn.AddToClassList("ghost-btn");
                        btn.clicked += () => OnGhostSelected(ghostId);

                        ghostButtonsContainer.Add(btn);
                        ghostSelectionButtons.Add(btn);
                        ghostSelectionButtonMap[ghostId] = btn;
                    }

                    uiButtonsBound = true;
                }

                // Circular Minigame
                exorcismOverlay = root.Q<VisualElement>("exorcism-overlay");
                circleDial = root.Q<VisualElement>("circle-dial");
                targetBarContainer = root.Q<VisualElement>("target-bar-container");
                targetHitLine = root.Q<VisualElement>("target-hit-line");
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
        for (int i = 0; i < ghostSelectionButtons.Count; i++)
        {
            if (ghostSelectionButtons[i] != null)
            {
                ghostSelectionButtons[i].SetEnabled(interactable);
            }
        }
    }

    private void RefreshGuessedGhostHighlight()
    {
        string currentGuess = JournalController.LastLockedInGuess;
        foreach (var pair in ghostSelectionButtonMap)
        {
            if (pair.Value == null) continue;
            bool isGuessed = !string.IsNullOrEmpty(currentGuess) &&
                             !currentGuess.Equals("None", System.StringComparison.OrdinalIgnoreCase) &&
                             pair.Key.Equals(currentGuess, System.StringComparison.OrdinalIgnoreCase);
            if (isGuessed) pair.Value.AddToClassList("ghost-btn-guessed");
            else pair.Value.RemoveFromClassList("ghost-btn-guessed");
        }
    }

    /// <summary>
    /// Triggered when the chalk diagram is placed or interacted with on the floor.
    /// Requests a server-authoritative ritual lock and opens the ghost selection popup only for the initiating player.
    /// </summary>
    public void InitiateRitual(Vector3 diagramPos)
    {
        if (IsExorcismCompleted || IsExorcismActive)
        {
            return;
        }

        chalkDiagramPos = diagramPos;

        if (IsNetworkSpawned)
        {
            RequestInitiateRitualServerRpc(diagramPos);
        }
        else
        {
            InitiateRitualLocal(diagramPos);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestInitiateRitualServerRpc(Vector3 diagramPos, RpcParams rpcParams = default)
    {
        if (syncedExorcismCompleted.Value || syncedExorcismActive.Value || activeRitualClientId.Value != ulong.MaxValue)
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!PlayerController.TryGetByClientId(senderClientId, out var sender) || sender == null || !sender.IsAlive)
        {
            return;
        }

        if (Vector2.Distance(sender.transform.position, diagramPos) > sender.MaxReachRadius + 1.5f)
        {
            return;
        }

        activeRitualClientId.Value = senderClientId;
        syncedExorcismActive.Value = true;
        syncedRitualChanting.Value = false;
        chalkDiagramPos = diagramPos;

        OpenGhostSelectClientRpc(diagramPos, RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void OpenGhostSelectClientRpc(Vector3 diagramPos, RpcParams rpcParams = default)
    {
        InitiateRitualLocal(diagramPos);
    }

    private void InitiateRitualLocal(Vector3 diagramPos)
    {
        if (isExorcismCompleted || isExorcismActive || isMinigameActive)
        {
            return;
        }

        isOwningLocalRitual = true;
        isExorcismActive = true;
        chalkDiagramPos = diagramPos;
        EnsureUIReferences();
        SetGhostButtonsInteractable(true);
        RefreshGuessedGhostHighlight();

        if (ghostSelectPopup != null)
        {
            ghostSelectPopup.RemoveFromClassList("hidden");
        }
    }

    private void CancelGhostSelectPopup()
    {
        closedFrame = Time.frameCount;
        SetGhostButtonsInteractable(false);
        if (ghostSelectPopup != null)
        {
            ghostSelectPopup.AddToClassList("hidden");
        }
        isExorcismActive = false;
        isOwningLocalRitual = false;

        if (IsNetworkSpawned)
        {
            CancelGhostSelectServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelGhostSelectServerRpc(RpcParams rpcParams = default)
    {
        if (!syncedExorcismActive.Value || syncedRitualChanting.Value || syncedExorcismCompleted.Value)
        {
            return;
        }

        if (rpcParams.Receive.SenderClientId != activeRitualClientId.Value)
        {
            return;
        }

        activeRitualClientId.Value = ulong.MaxValue;
        syncedExorcismActive.Value = false;
        isExorcismActive = false;
    }

    private void OnGhostSelected(string ghostName)
    {
        if (!isExorcismActive || isMinigameActive || !isOwningLocalRitual)
        {
            return;
        }

        // Disable immediately upon the first click to prevent double clicks and duplicate starts
        SetGhostButtonsInteractable(false);

        selectedGhostGuess = ghostName;
        JournalController.LastLockedInGuess = ghostName;
        Debug.Log($"[ExorcismManager] Ghost type selected: {ghostName}");

        closedFrame = Time.frameCount;
        if (ghostSelectPopup != null)
        {
            ghostSelectPopup.AddToClassList("hidden");
        }

        if (IsNetworkSpawned)
        {
            SubmitGhostSelectionServerRpc(ghostName, chalkDiagramPos);
        }
        else
        {
            StartCircularSkillCheckLocal(chalkDiagramPos, true);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitGhostSelectionServerRpc(string ghostName, Vector3 diagramPos, RpcParams rpcParams = default)
    {
        if (syncedExorcismCompleted.Value || !syncedExorcismActive.Value || syncedRitualChanting.Value)
        {
            return;
        }

        if (rpcParams.Receive.SenderClientId != activeRitualClientId.Value)
        {
            return;
        }

        syncedRitualChanting.Value = true;
        selectedGhostGuess = ghostName;
        chalkDiagramPos = diagramPos;

        StartRitualSequenceClientRpc(diagramPos, activeRitualClientId.Value, ghostName);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartRitualSequenceClientRpc(Vector3 diagramPos, ulong ritualOwnerClientId, string guessedGhostName)
    {
        selectedGhostGuess = guessedGhostName;
        bool isLocalOwner = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == ritualOwnerClientId;
        isOwningLocalRitual = isLocalOwner;
        StartCircularSkillCheckLocal(diagramPos, isLocalOwner);
    }

    private void StartCircularSkillCheckLocal(Vector3 diagramPos, bool showMinigameForLocalPlayer)
    {
        chalkDiagramPos = diagramPos;
        EnsureUIReferences();

        if (summonSequenceCoroutine != null)
        {
            StopCoroutine(summonSequenceCoroutine);
        }
        summonSequenceCoroutine = StartCoroutine(SummonAndBeginMinigameRoutine(diagramPos, showMinigameForLocalPlayer));
    }

    private IEnumerator SummonAndBeginMinigameRoutine(Vector3 diagramPos, bool showMinigameForLocalPlayer)
    {
        isExorcismActive = true;
        isMinigameActive = false;
        if (exorcismOverlay != null) exorcismOverlay.AddToClassList("hidden");

        SummoningCircle circle = SummoningCircle.FindClosest(diagramPos, 0.75f);
        if (circle != null)
        {
            bool candlesLit = false;
            circle.PlayIgnitionSequence(() => candlesLit = true);

            float waitElapsed = 0f;
            float maxWait = Mathf.Max(12f, circle.ExpectedIgnitionDuration + 2.5f);
            while (!candlesLit && waitElapsed < maxWait)
            {
                waitElapsed += Time.deltaTime;
                yield return null;
            }
        }

        // 1. Teleport and freeze the hunter ghost inside the burning summoning circle
        SetupGhostInRitualCircle(diagramPos);

        // 2. Reset minigame parameters after all 5 flames finish lighting
        needleAngle = 0f;
        currentLoops = 0;
        mistakesCount = 0;
        successfulChants = 0;
        ApplyDifficultyMinigameSettings();
        summonSequenceCoroutine = null;

        if (showMinigameForLocalPlayer)
        {
            isMinigameActive = true;
            PickNewKeyAndTarget();
            UpdateMinigameUI();

            if (lblFeedback != null) lblFeedback.text = $"Press key when needle overlaps the target bar (Max {maxLoops} loops)!";
            if (exorcismOverlay != null) exorcismOverlay.RemoveFromClassList("hidden");
        }
    }

    private void ApplyDifficultyMinigameSettings()
    {
        ContractDifficulty difficulty = ProgressionAndLoadoutManager.IsInWarehouseTutorialScene
            ? ContractDifficulty.Amateur
            : (ProgressionAndLoadoutManager.Instance != null
                ? ProgressionAndLoadoutManager.Instance.SelectedDifficulty
                : ContractDifficulty.Amateur);

        float targetBarWidthPx;
        float speedMultiplier;

        switch (difficulty)
        {
            case ContractDifficulty.Amateur:
                targetBarWidthPx = 40f;
                hitTolerance = 12f;
                speedMultiplier = 1.0f;
                maxMistakes = 5;
                maxLoops = 3;
                break;
            case ContractDifficulty.Intermediate:
                targetBarWidthPx = 28f;
                hitTolerance = 8.5f;
                speedMultiplier = 1.5f;
                maxMistakes = 3;
                maxLoops = 2;
                break;
            case ContractDifficulty.Professional:
                targetBarWidthPx = 20f;
                hitTolerance = 6f;
                speedMultiplier = 1.75f;
                maxMistakes = 2;
                maxLoops = 2;
                break;
            case ContractDifficulty.Nightmare:
                targetBarWidthPx = 14f;
                hitTolerance = 4.2f;
                speedMultiplier = 1.75f;
                maxMistakes = 1;
                maxLoops = 2;
                break;
            default:
                targetBarWidthPx = 28f;
                hitTolerance = 8.5f;
                speedMultiplier = 1.0f;
                maxMistakes = 3;
                maxLoops = 2;
                break;
        }

        currentRotationSpeed = baseRotationSpeed * speedMultiplier;

        if (targetHitLine != null)
        {
            targetHitLine.style.width = targetBarWidthPx;
        }
    }

    private void SetupGhostInRitualCircle(Vector3 centerPos)
    {
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null && ghostHandler.hunterGhost != null)
        {
            var hunter = ghostHandler.hunterGhost;
            hunter.transform.position = new Vector3(centerPos.x, centerPos.y, 0f);

            var agent = hunter.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                if (agent.isOnNavMesh) agent.Warp(centerPos);
                agent.isStopped = true;
            }

            // Disable movement script so ghost is frozen in place during chanting
            hunter.enabled = false;
            hunter.GetComponent<GhostPixelAnimator>()?.SetDyingOverride(false);

            // Enable its SpriteRenderer above the summoning circle (Item order 10)
            var sr = hunter.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sr.enabled = true;
                sr.color = Color.white;
                sr.sortingLayerName = "Item";
                sr.sortingOrder = 50;
            }
            return;
        }

        if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene)
        {
            var tutorialGhost = Object.FindAnyObjectByType<TutorialGhostController>();
            tutorialGhost?.PinToRitualCircle(centerPos);
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
        // Server disconnect / death safeguard: release ritual lock if the owning player leaves or dies mid-ritual
        if (IsNetworkSpawned && IsServer && syncedExorcismActive.Value && activeRitualClientId.Value != ulong.MaxValue)
        {
            bool ownerAliveAndConnected =
                PlayerController.TryGetByClientId(activeRitualClientId.Value, out var ownerPlayer) &&
                ownerPlayer != null &&
                ownerPlayer.IsAlive;

            if (!ownerAliveAndConnected)
            {
                activeRitualClientId.Value = ulong.MaxValue;
                syncedExorcismActive.Value = false;
                syncedRitualChanting.Value = false;
                isExorcismActive = false;
                AbortRitualLockClientRpc(chalkDiagramPos);
            }
        }

        // Allow cancelling during Ghost Selection popup via Esc or walking out of reach
        if (ghostSelectPopup != null && !ghostSelectPopup.ClassListContains("hidden") && !isMinigameActive)
        {
            bool escPressed = Input.GetKeyDown(KeyCode.Escape);
            bool outOfReachOrDead = PlayerController.LocalInstance == null ||
                                    !PlayerController.LocalInstance.IsAlive ||
                                    Vector2.Distance(PlayerController.LocalInstance.transform.position, chalkDiagramPos) > PlayerController.LocalInstance.MaxReachRadius + 1.2f;
            if (escPressed || outOfReachOrDead)
            {
                CancelGhostSelectPopup();
                return;
            }
        }

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
        closedFrame = Time.frameCount;
        if (exorcismOverlay != null) exorcismOverlay.AddToClassList("hidden");

        bool isMinigameSuccess = (mistakesCount < maxMistakes && successfulChants >= requiredChants);

        if (IsNetworkSpawned)
        {
            SubmitExorcismOutcomeServerRpc(isMinigameSuccess, selectedGhostGuess);
            return;
        }

        string actualGhostName = "";
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            actualGhostName = ghostHandler.ghostName;
        }

        bool isGhostCorrect = !string.IsNullOrEmpty(selectedGhostGuess) &&
                              (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene ||
                               selectedGhostGuess.Equals(actualGhostName, System.StringComparison.OrdinalIgnoreCase));

        if (isGhostCorrect && isMinigameSuccess)
        {
            isExorcismActive = false;
            isExorcismCompleted = true;
            isOwningLocalRitual = false;
            TriggerSuccessLocal(selectedGhostGuess);
        }
        else
        {
            isExorcismActive = false;
            isOwningLocalRitual = false;
            ghostHandler?.NotifyRitualFailed();
            TriggerFailureLocal();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitExorcismOutcomeServerRpc(bool minigamePassed, string guessedGhostName, RpcParams rpcParams = default)
    {
        if (syncedExorcismCompleted.Value || !syncedExorcismActive.Value)
        {
            return;
        }

        if (rpcParams.Receive.SenderClientId != activeRitualClientId.Value)
        {
            return;
        }

        activeRitualClientId.Value = ulong.MaxValue;
        syncedExorcismActive.Value = false;
        syncedRitualChanting.Value = false;
        isExorcismActive = false;
        selectedGhostGuess = guessedGhostName;

        string actualGhostName = "";
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            actualGhostName = ghostHandler.ghostName;
        }

        bool isGhostCorrect = !string.IsNullOrEmpty(guessedGhostName) &&
                              (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene ||
                               (!string.IsNullOrEmpty(actualGhostName) &&
                                guessedGhostName.Equals(actualGhostName, System.StringComparison.OrdinalIgnoreCase)));

        if (isGhostCorrect && minigamePassed)
        {
            syncedExorcismCompleted.Value = true;
            isExorcismCompleted = true;
            TriggerSuccessClientRpc(guessedGhostName);
        }
        else
        {
            ghostHandler?.NotifyRitualFailed();
            TriggerFailureClientRpc();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AbortRitualLockClientRpc(Vector3 diagramPos)
    {
        if (summonSequenceCoroutine != null)
        {
            StopCoroutine(summonSequenceCoroutine);
            summonSequenceCoroutine = null;
        }

        closedFrame = Time.frameCount;
        isMinigameActive = false;
        isExorcismActive = false;
        isOwningLocalRitual = false;

        SetGhostButtonsInteractable(false);
        if (ghostSelectPopup != null) ghostSelectPopup.AddToClassList("hidden");
        if (exorcismOverlay != null) exorcismOverlay.AddToClassList("hidden");

        SummoningCircle circle = SummoningCircle.FindClosest(diagramPos, 0.75f);
        if (circle != null)
        {
            circle.ResetToUnlit();
        }

        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null && ghostHandler.hunterGhost != null)
        {
            ghostHandler.hunterGhost.enabled = true;
            if (ghostHandler.CurrentState == GhostHandlerState.Dormant)
            {
                var sr = ghostHandler.hunterGhost.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.enabled = false;
                    sr.color = Color.white;
                    sr.sortingOrder = 10;
                }
            }
        }
        else if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene)
        {
            Object.FindAnyObjectByType<TutorialGhostController>()?.ReleaseFromRitualCircle();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerSuccessClientRpc(string guessedGhostName)
    {
        isExorcismActive = false;
        isExorcismCompleted = true;
        isOwningLocalRitual = false;
        TriggerSuccessLocal(guessedGhostName);
    }

    private void TriggerSuccessLocal(string guessedGhostName)
    {
        if (!string.IsNullOrEmpty(guessedGhostName))
        {
            selectedGhostGuess = guessedGhostName;
        }
        Debug.Log("[ExorcismManager] SUCCESS! Correct ghost identified and minigame passed.");

        // 1. Ghost shakes, plays dying shriek, and slowly disappears (GhostHandler is kept intact)
        Vector3 manifestPos = (chalkDiagramPos != Vector3.zero) ? chalkDiagramPos : transform.position;
        AudioClip shriekClip = dyingShriekClip != null
            ? dyingShriekClip
            : GameAudioManager.GetClip("ghost_dying_shriek");

        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            // Immediately halt ongoing hunts and unlock doors so players are safe
            ghostHandler.EndHunt();
            ghostHandler.SetDoorsLocked(false);

            if (ghostHandler.hunterGhost != null)
            {
                // Manifest the dying ghost directly at the ritual chalk position so players clearly witness the dying sequence
                ghostHandler.hunterGhost.StartDyingSequence(shriekClip, manifestPos);
            }
            else
            {
                if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene)
                {
                    Object.FindAnyObjectByType<TutorialGhostController>()?.PlayBanishEffect(manifestPos, shriekClip);
                }
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

        if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene && WarehouseTutorialManager.Instance != null)
        {
            WarehouseTutorialManager.Instance.TriggerGuidedExorcismStep();
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
        isExorcismActive = false;
        isOwningLocalRitual = false;
        TriggerFailureLocal();
    }

    private void TriggerFailureLocal()
    {
        Debug.Log("[ExorcismManager] FAILURE! Activating ghost angered state and turning global light red.");

        SummoningCircle circle = SummoningCircle.FindClosest(chalkDiagramPos, 0.75f);
        if (circle != null)
        {
            circle.ResetToUnlit();
        }

        // Reusing the standard hunt process on GhostHandler with isFromFailedExorcism = true
        if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene)
        {
            Object.FindAnyObjectByType<TutorialGhostController>()?.ReleaseFromRitualCircle();
        }
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            if (ghostHandler.hunterGhost != null)
            {
                ghostHandler.hunterGhost.enabled = true;
            }

            bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (!networkActive)
            {
                if (ghostHandler.hunterGhost != null)
                {
                    ghostHandler.hunterGhost.OnHuntStarted();
                    ghostHandler.hunterGhost.EnterAngeredState();
                }
                SetRedEnvironment(true);
            }
            else if (IsServer || !IsSpawned)
            {
                ghostHandler.StartHunt(true);
            }
        }
        else
        {
            SetRedEnvironment(true);
        }

        // Show failure warning dialogue banner
        ShowDialogueBanner(
            "[!] RITUAL FAILED: GHOST ANGERED [!]",
            "The entity has broken free in an Angered State!",
            "USE A CRUCIFIX TO REPEL IT! Hide in a cabinet or escape the house!"
        );
    }

    public void SetRedEnvironment(bool active)
    {
        if (active)
        {
            IsRedEnvironmentActive = true;
            TurnEnvironmentRed();
        }
        else
        {
            RestoreEnvironmentLighting();
        }
    }

    private static bool ShouldTintLightRed(Light2D l)
    {
        if (l == null) return false;
        string lowerName = l.gameObject.name.ToLowerInvariant();
        if (lowerName.Contains("candle") || lowerName.Contains("ghostauralight") || lowerName.Contains("flashlight"))
        {
            return false;
        }
        if (l.transform.parent != null && l.transform.parent.name.ToLowerInvariant().Contains("flashlight"))
        {
            return false;
        }
        return true;
    }

    private void TurnEnvironmentRed()
    {
        EnsureRedAtmosphereVolume();
        if (redEnvTransitionCoroutine != null)
        {
            StopCoroutine(redEnvTransitionCoroutine);
        }
        redEnvTransitionCoroutine = StartCoroutine(TransitionRedEnvironmentRoutine(true, Mathf.Max(0.5f, redLightTransitionDuration)));
    }

    private void RestoreEnvironmentLighting()
    {
        if (redEnvTransitionCoroutine != null)
        {
            StopCoroutine(redEnvTransitionCoroutine);
        }
        redEnvTransitionCoroutine = StartCoroutine(TransitionRedEnvironmentRoutine(false, 1.5f));
    }

    private IEnumerator TransitionRedEnvironmentRoutine(bool toRed, float duration)
    {
        var lights = Object.FindObjectsByType<Light2D>(FindObjectsInactive.Exclude);
        var activeTargets = new List<Light2D>();
        var startColors = new Dictionary<Light2D, Color>();

        foreach (var l in lights)
        {
            if (!ShouldTintLightRed(l)) continue;
            activeTargets.Add(l);
            startColors[l] = l.color;
            if (toRed && !originalLightColors.ContainsKey(l))
            {
                originalLightColors[l] = l.color;
            }
        }

        float startWeight = redAtmosphereVolume != null ? redAtmosphereVolume.weight : 0f;
        float targetWeight = toRed ? 1f : 0f;
        Color targetRedLight = new Color(1f, 0.08f, 0.08f, 1f);
        Color targetRedGlobal = new Color(0.85f, 0.06f, 0.06f, 1f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            for (int i = 0; i < activeTargets.Count; i++)
            {
                Light2D l = activeTargets[i];
                if (l == null) continue;

                Color from = startColors[l];
                Color dest = toRed
                    ? (l.lightType == Light2D.LightType.Global ? targetRedGlobal : targetRedLight)
                    : (originalLightColors.TryGetValue(l, out Color orig) ? orig : Color.white);
                l.color = Color.Lerp(from, dest, t);
            }

            if (redAtmosphereVolume != null)
            {
                redAtmosphereVolume.weight = Mathf.Lerp(startWeight, targetWeight, t);
            }

            yield return null;
        }

        for (int i = 0; i < activeTargets.Count; i++)
        {
            Light2D l = activeTargets[i];
            if (l == null) continue;
            l.color = toRed
                ? (l.lightType == Light2D.LightType.Global ? targetRedGlobal : targetRedLight)
                : (originalLightColors.TryGetValue(l, out Color orig) ? orig : Color.white);
        }

        if (redAtmosphereVolume != null)
        {
            redAtmosphereVolume.weight = targetWeight;
        }

        if (!toRed)
        {
            originalLightColors.Clear();
            IsRedEnvironmentActive = false;
        }

        redEnvTransitionCoroutine = null;
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
