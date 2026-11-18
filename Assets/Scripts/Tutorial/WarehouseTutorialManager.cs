using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TutorialBay
{
    Bay1_Controls = 1,
    Bay2_PowerSanity = 2,
    Bay3_Evidence = 3,
    Bay4_Survival = 4,
    Bay5_Extraction = 5,
    Completed = 6
}

/// <summary>
/// State machine and progression coordinator for WarehouseTutorialScene.
/// Tracks Bay 1-5 steps, locks/unlocks bay NetworkDoors, configures real ParanormalManager
/// & GhostHandler evidence showcases (including Ghost Orbs and D.O.T.S.), coordinates the
/// safe TutorialGhostController + Guided Exorcism, and completes via Jeepney TruckEndGameButton.
/// </summary>
public class WarehouseTutorialManager : MonoBehaviour
{
    public static WarehouseTutorialManager Instance { get; private set; }

    [Header("Bay Doors (0=Bay1->2, 1=Bay2->3, 2=Bay3->4, 3=Bay4->5)")]
    [SerializeField] private NetworkDoor[] bayExitDoors = new NetworkDoor[4];

    [Header("Bay Spawn / Reset Points")]
    [SerializeField] private Transform[] bayResetPoints = new Transform[5];

    [Header("Scene References")]
    [SerializeField] private TutorialGhostController tutorialGhost;
    [SerializeField] private GhostHandler tutorialGhostHandler;
    [SerializeField] private Collider2D bay3RoomCollider;
    [SerializeField] private Transform bay3EmfAnchor;
    [SerializeField] private Transform bay3GhostIdleAnchor;
    [SerializeField] private DotsProjectorItem bay3ExampleDots;
    [SerializeField] private FuseBox bay2FuseBox;
    [SerializeField] private HouseLightSwitch bay2LightSwitch;
    [SerializeField] private HideZone bay4HideZone;
    [SerializeField] private GhostOrbSystem bay3GhostOrbs;

    public TutorialBay CurrentBay { get; private set; } = TutorialBay.Bay1_Controls;
    public event Action OnTutorialStateChanged;
    public event Action<string> OnTutorialHint;
    public event Action OnTutorialCompleted;

    private readonly Dictionary<string, bool> completedSteps = new Dictionary<string, bool>();
    private bool initialized = false;
    private bool flashlightPickedUpObserved = false;
    private bool flashlightInitialLightState = false;
    private bool sanityPillsConsumedOrRestored = false;
    private float nextDotsPulseTime = 0f;

    // Step ID constants
    public const string STEP_B1_PICKUP_FLASHLIGHT = "B1_PickupFlashlight";
    public const string STEP_B1_TOGGLE_FLASHLIGHT = "B1_ToggleFlashlight";
    public const string STEP_B1_OPEN_DOOR = "B1_OpenDoor";

    public const string STEP_B2_ENTER_DARKNESS = "B2_EnterDarkness";
    public const string STEP_B2_RESTORE_POWER = "B2_RestorePower";
    public const string STEP_B2_FLIP_LIGHT = "B2_FlipLight";

    public const string STEP_B3_EMF = "B3_EMF";
    public const string STEP_B3_THERMOMETER = "B3_Thermometer";
    public const string STEP_B3_SPIRIT_BOX = "B3_SpiritBox";
    public const string STEP_B3_WRITING = "B3_Writing";
    public const string STEP_B3_CAMERA_DOTS = "B3_CameraDots";

    public const string STEP_B4_CRUCIFIX = "B4_Crucifix";
    public const string STEP_B4_SURVIVE_HUNT = "B4_SurviveHunt";
    public const string STEP_B4_SANITY_PILLS = "B4_SanityPills";
    public const string STEP_B4_EXORCISM = "B4_Exorcism";

    public const string STEP_B5_JOURNAL_DEDUCTION = "B5_JournalDeduction";
    public const string STEP_B5_EXIT_CONSOLE = "B5_ExitConsole";

    private static readonly string[][] BayStepIds =
    {
        new[] { STEP_B1_PICKUP_FLASHLIGHT, STEP_B1_TOGGLE_FLASHLIGHT, STEP_B1_OPEN_DOOR },
        new[] { STEP_B2_ENTER_DARKNESS, STEP_B2_RESTORE_POWER, STEP_B2_FLIP_LIGHT },
        new[] { STEP_B3_EMF, STEP_B3_THERMOMETER, STEP_B3_SPIRIT_BOX, STEP_B3_WRITING, STEP_B3_CAMERA_DOTS },
        new[] { STEP_B4_CRUCIFIX, STEP_B4_SURVIVE_HUNT, STEP_B4_SANITY_PILLS, STEP_B4_EXORCISM },
        new[] { STEP_B5_JOURNAL_DEDUCTION, STEP_B5_EXIT_CONSOLE }
    };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeState();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (tutorialGhost != null)
        {
            tutorialGhost.OnHuntWarningTick -= HandleHuntWarningTick;
            tutorialGhost.OnHuntStarted -= HandleHuntStarted;
            tutorialGhost.OnPlayerCaughtSafely -= HandlePlayerCaughtSafely;
            tutorialGhost.OnHuntCompleted -= HandleHuntCompleted;
        }
    }

    private void Start()
    {
        EnsureTutorialNetworkObjectsSpawned();
        ConfigureBay3EvidenceShowcase();

        if (tutorialGhost != null)
        {
            tutorialGhost.OnHuntWarningTick += HandleHuntWarningTick;
            tutorialGhost.OnHuntStarted += HandleHuntStarted;
            tutorialGhost.OnPlayerCaughtSafely += HandlePlayerCaughtSafely;
            tutorialGhost.OnHuntCompleted += HandleHuntCompleted;
            if (bay3GhostIdleAnchor != null)
            {
                tutorialGhost.SetContainmentAnchor(bay3GhostIdleAnchor);
            }
        }
        SyncDoorLocks();
        EmitHint("Bay 1: Pick up the Flashlight with [L-Click], toggle it, then open the door with [E].");
    }

    private void EnsureTutorialNetworkObjectsSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        var netObjs = FindObjectsByType<NetworkObject>();
        foreach (var netObj in netObjs)
        {
            if (netObj != null && !netObj.IsSpawned && netObj.gameObject.scene == gameObject.scene)
            {
                netObj.Spawn(true);
            }
        }
    }

    private void ConfigureBay3EvidenceShowcase()
    {
        if (tutorialGhostHandler != null)
        {
            tutorialGhostHandler.ghostName = "Multo";
            tutorialGhostHandler.currentEvidence.Clear();
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.Crucifix);
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.EMF);
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.Freezing);
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.DOTS);
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.GhostWriting);
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.SpiritBox);
            tutorialGhostHandler.currentEvidence.Add(EvidenceType.GhostOrbs);

            if (bay3RoomCollider != null)
            {
                tutorialGhostHandler.ghostroomMarker = bay3RoomCollider;
            }
        }

        if (ParanormalManager.Instance != null && bay3RoomCollider != null)
        {
            Transform ghostXf = tutorialGhost != null ? tutorialGhost.transform : bay3RoomCollider.transform;
            ParanormalManager.Instance.SetGhostInfo(
                ghostXf,
                bay3RoomCollider,
                freezingEvidence: true,
                emf5Evidence: true,
                tutorialGhostHandler);

            Vector2 emfPos = bay3EmfAnchor != null ? (Vector2)bay3EmfAnchor.position : (Vector2)bay3RoomCollider.bounds.center;
            ParanormalManager.Instance.RegisterEvent(emfPos, 5, 9999f);
        }

        if (bay3GhostOrbs != null && bay3RoomCollider != null)
        {
            bay3GhostOrbs.BindRoomCollider(bay3RoomCollider);
            bay3GhostOrbs.Initialize(bay3RoomCollider);
        }
    }

    public void InitializeState()
    {
        if (initialized) return;
        initialized = true;
        CurrentBay = TutorialBay.Bay1_Controls;
        completedSteps.Clear();
        foreach (var steps in BayStepIds)
        {
            foreach (var s in steps)
            {
                completedSteps[s] = false;
            }
        }
        SyncDoorLocks();
    }

    public bool IsStepCompleted(string stepId)
    {
        return completedSteps.TryGetValue(stepId, out bool done) && done;
    }

    public bool CompleteStep(string stepId)
    {
        if (!initialized) InitializeState();
        if (!completedSteps.ContainsKey(stepId) || completedSteps[stepId]) return false;

        completedSteps[stepId] = true;
        EvaluateBayProgression();
        OnTutorialStateChanged?.Invoke();
        return true;
    }

    public bool AreAllBayStepsCompleted(TutorialBay bay)
    {
        int idx = (int)bay - 1;
        if (idx < 0 || idx >= BayStepIds.Length) return false;
        foreach (var stepId in BayStepIds[idx])
        {
            if (!IsStepCompleted(stepId)) return false;
        }
        return true;
    }

    public bool ShouldBayDoorBeLocked(int bayDoorIndex)
    {
        if (bayDoorIndex < 0 || bayDoorIndex >= 4) return false;

        int targetBayNumber = bayDoorIndex + 1;
        if ((int)CurrentBay > targetBayNumber) return false;
        if ((int)CurrentBay < targetBayNumber) return true;

        if (bayDoorIndex == 0)
        {
            return !(IsStepCompleted(STEP_B1_PICKUP_FLASHLIGHT) && IsStepCompleted(STEP_B1_TOGGLE_FLASHLIGHT));
        }

        return !AreAllBayStepsCompleted((TutorialBay)targetBayNumber);
    }

    private void EvaluateBayProgression()
    {
        if (CurrentBay == TutorialBay.Completed) return;

        if (AreAllBayStepsCompleted(CurrentBay))
        {
            if (CurrentBay == TutorialBay.Bay5_Extraction)
            {
                CurrentBay = TutorialBay.Completed;
                ProgressionAndLoadoutManager.MarkTutorialCompleted();
                OnTutorialCompleted?.Invoke();
            }
            else
            {
                CurrentBay = (TutorialBay)((int)CurrentBay + 1);
                OnEnteredBay(CurrentBay);
            }
        }

        SyncDoorLocks();
    }

    private void OnEnteredBay(TutorialBay bay)
    {
        switch (bay)
        {
            case TutorialBay.Bay2_PowerSanity:
                EmitHint("Bay 2: Darkness drains sanity! Restore power at the Fuse Box [E], then flip the Light Switch [E].");
                break;
            case TutorialBay.Bay3_Evidence:
                ConfigureBay3EvidenceShowcase();
                if (bay3ExampleDots != null && !bay3ExampleDots.IsPoweredOn)
                {
                    bay3ExampleDots.TogglePowerRpc();
                }
                EmitHint("Bay 3: Test all evidence tools! Notice the example Ghost Orbs & D.O.T.S. silhouette.");
                break;
            case TutorialBay.Bay4_Survival:
                if (tutorialGhost != null)
                {
                    tutorialGhost.SetContainmentAnchor(null);
                }
                EmitHint("Bay 4: Block a hunt with the Crucifix, hide during the 10s hunt, take Sanity Pills, then perform Exorcism [E].");
                break;
            case TutorialBay.Bay5_Extraction:
                EmitHint("Bay 5: Open Journal [J], select your ghost deduction, then board the Jeepney and press the End Game Button [E].");
                break;
        }
    }

    public void SyncDoorLocks()
    {
        if (bayExitDoors == null) return;
        for (int i = 0; i < bayExitDoors.Length; i++)
        {
            if (bayExitDoors[i] != null)
            {
                bayExitDoors[i].SetLocked(ShouldBayDoorBeLocked(i));
            }
        }
    }

    private void Update()
    {
        if (CurrentBay == TutorialBay.Completed) return;

        switch (CurrentBay)
        {
            case TutorialBay.Bay1_Controls:
                PollBay1();
                break;
            case TutorialBay.Bay2_PowerSanity:
                PollBay2();
                break;
            case TutorialBay.Bay3_Evidence:
                PollBay3();
                break;
            case TutorialBay.Bay4_Survival:
                PollBay4();
                break;
            case TutorialBay.Bay5_Extraction:
                PollBay5();
                break;
        }
    }

    private void PollBay1()
    {
        var flashlights = FindObjectsByType<FlashlightItem>();
        foreach (var fl in flashlights)
        {
            if (fl == null) continue;
            if (!fl.IsOnGround)
            {
                if (!flashlightPickedUpObserved)
                {
                    flashlightPickedUpObserved = true;
                    flashlightInitialLightState = fl.IsLightOn;
                    CompleteStep(STEP_B1_PICKUP_FLASHLIGHT);
                }
                else if (!IsStepCompleted(STEP_B1_TOGGLE_FLASHLIGHT) && fl.IsLightOn != flashlightInitialLightState)
                {
                    CompleteStep(STEP_B1_TOGGLE_FLASHLIGHT);
                }
            }
        }

        if (IsStepCompleted(STEP_B1_TOGGLE_FLASHLIGHT) &&
            bayExitDoors != null && bayExitDoors.Length > 0 &&
            bayExitDoors[0] != null && bayExitDoors[0].IsOpen)
        {
            CompleteStep(STEP_B1_OPEN_DOOR);
        }
    }

    private void PollBay2()
    {
        if (!IsStepCompleted(STEP_B2_ENTER_DARKNESS))
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                if (bayResetPoints != null && bayResetPoints.Length > 1 && bayResetPoints[1] != null)
                {
                    if (Vector2.Distance(player.transform.position, bayResetPoints[1].position) < 0.45f)
                    {
                        CompleteStep(STEP_B2_ENTER_DARKNESS);
                    }
                }
                else
                {
                    CompleteStep(STEP_B2_ENTER_DARKNESS);
                }
            }
        }

        if (bay2FuseBox != null && bay2FuseBox.IsEnabled)
        {
            CompleteStep(STEP_B2_RESTORE_POWER);
        }

        if (bay2LightSwitch != null && bay2LightSwitch.IsOn)
        {
            CompleteStep(STEP_B2_FLIP_LIGHT);
        }
    }

    private void PollBay3()
    {
        if (!IsStepCompleted(STEP_B3_EMF))
        {
            var emfs = FindObjectsByType<EMFReaderItem>();
            foreach (var emf in emfs)
            {
                if (emf != null && emf.IsPoweredOn && emf.CurrentEmfLevel >= 5)
                {
                    CompleteStep(STEP_B3_EMF);
                    break;
                }
            }
        }

        if (!IsStepCompleted(STEP_B3_THERMOMETER))
        {
            var thermos = FindObjectsByType<ThermometerItem>();
            foreach (var t in thermos)
            {
                if (t != null && t.IsPoweredOn && t.IsFreezing)
                {
                    CompleteStep(STEP_B3_THERMOMETER);
                    break;
                }
            }
        }

        if (!IsStepCompleted(STEP_B3_SPIRIT_BOX))
        {
            var boxes = FindObjectsByType<SpiritBoxItem>();
            foreach (var sb in boxes)
            {
                if (sb != null && sb.IsPoweredOn && sb.IsInsideGhostRoom())
                {
                    CompleteStep(STEP_B3_SPIRIT_BOX);
                    break;
                }
            }
        }

        if (!IsStepCompleted(STEP_B3_WRITING))
        {
            var books = FindObjectsByType<GhostWritingBookItem>();
            foreach (var b in books)
            {
                if (b != null && b.IsWritten)
                {
                    CompleteStep(STEP_B3_WRITING);
                    break;
                }
                if (b != null && b.IsOpened && tutorialGhost != null)
                {
                    tutorialGhost.TriggerGhostWriting(b);
                }
            }
        }

        // Continuously pulse the example D.O.T.S. silhouette in Bay 3
        if (Time.time >= nextDotsPulseTime && bay3RoomCollider != null)
        {
            nextDotsPulseTime = Time.time + 3.2f;
            var dotsList = FindObjectsByType<DotsProjectorItem>();
            foreach (var d in dotsList)
            {
                if (d != null && d.IsPoweredOn && !d.IsProjectingSilhouette)
                {
                    d.TriggerDotsProjectionOnServer(bay3RoomCollider, true, out _, 2.4f);
                }
            }
        }

        if (!IsStepCompleted(STEP_B3_CAMERA_DOTS))
        {
            var dots = FindObjectsByType<DotsProjectorItem>();
            foreach (var d in dots)
            {
                if (d != null && (d.IsPlaced || d.IsProjectingSilhouette))
                {
                    tutorialGhost?.TriggerDotsSilhouette();
                    CompleteStep(STEP_B3_CAMERA_DOTS);
                    break;
                }
            }
        }
    }

    private void PollBay4()
    {
        if (!IsStepCompleted(STEP_B4_CRUCIFIX))
        {
            var crucifixes = FindObjectsByType<CrucifixItem>();
            foreach (var c in crucifixes)
            {
                if (c != null && (c.IsBurned || (!c.IsOnGround && tutorialGhost != null && tutorialGhost.TryBlockWithCrucifix(c))))
                {
                    CompleteStep(STEP_B4_CRUCIFIX);
                    break;
                }
            }
        }

        if (IsStepCompleted(STEP_B4_CRUCIFIX) && !IsStepCompleted(STEP_B4_SURVIVE_HUNT))
        {
            if (tutorialGhost != null && !tutorialGhost.IsHunting && !tutorialGhost.IsHuntCompleted)
            {
                var player = FindAnyObjectByType<PlayerController>();
                Transform resetPt = (bayResetPoints != null && bayResetPoints.Length > 3) ? bayResetPoints[3] : null;
                tutorialGhost.StartNonLethalHunt(player != null ? player.transform : null, resetPt, bay4HideZone);
            }
        }

        if (IsStepCompleted(STEP_B4_SURVIVE_HUNT) && !IsStepCompleted(STEP_B4_SANITY_PILLS))
        {
            if (sanityPillsConsumedOrRestored)
            {
                CompleteStep(STEP_B4_SANITY_PILLS);
            }
            else
            {
                var pills = FindObjectsByType<SanityPillsItem>();
                if (pills.Length == 0)
                {
                    CompleteStep(STEP_B4_SANITY_PILLS);
                }
            }
        }
    }

    private void PollBay5()
    {
        if (!IsStepCompleted(STEP_B5_JOURNAL_DEDUCTION))
        {
            string guess = JournalController.LastLockedInGuess;
            if (!string.IsNullOrEmpty(guess) && guess != "None")
            {
                CompleteStep(STEP_B5_JOURNAL_DEDUCTION);
            }
        }
    }

    public void NotifySanityPillsUsed()
    {
        sanityPillsConsumedOrRestored = true;
        if (CurrentBay == TutorialBay.Bay4_Survival)
        {
            CompleteStep(STEP_B4_SANITY_PILLS);
            EmitHint("Sanity restored! Now interact with the Ritual Exorcism Circle [E] to banish the ghost.");
        }
    }

    public void TriggerGuidedExorcismStep()
    {
        if (tutorialGhost != null)
        {
            tutorialGhost.PlayBanishEffect();
        }
        CompleteStep(STEP_B4_EXORCISM);
        EmitHint("Entity banished! Proceed to Bay 5 for final Journal deduction & Jeepney extraction.");
    }

    public void CompleteTutorialContractStep()
    {
        CompleteStep(STEP_B5_EXIT_CONSOLE);
    }

    private void HandleHuntWarningTick(int secondsRemaining)
    {
        EmitHint($"Hunt starting in {secondsRemaining}s — Hide inside the cabinet HideZone and turn OFF your Flashlight!");
    }

    private void HandleHuntStarted()
    {
        EmitHint("Hunt active! Stay inside the cabinet HideZone with your Flashlight OFF for 10s.");
    }

    private void HandlePlayerCaughtSafely()
    {
        EmitHint("Caught! Resetting — get inside the cabinet HideZone and turn OFF your Flashlight before the hunt resumes!");
    }

    private void HandleHuntCompleted()
    {
        CompleteStep(STEP_B4_SURVIVE_HUNT);
        EmitHint("Hunt survived! Use Sanity Pills [L-Click], then banish the ghost at the Ritual Circle [E].");
    }

    public void EmitHint(string message)
    {
        OnTutorialHint?.Invoke(message);
    }

    public void SkipTutorial()
    {
        ProgressionAndLoadoutManager.MarkTutorialSkipped();
        ReturnToMenuOrLobby(false);
    }

    public void CompleteTutorial()
    {
        ProgressionAndLoadoutManager.MarkTutorialCompleted();
        ReturnToMenuOrLobby(true);
    }

    private void ReturnToMenuOrLobby(bool enterLobby)
    {
        if (NetworkController.Instance != null)
        {
            if (enterLobby)
            {
                NetworkController.Instance.EnterLobbyMap();
            }
            else
            {
                NetworkController.Instance.DisconnectAndReturnToMenu();
            }
            return;
        }

        SceneManager.LoadScene(enterLobby
            ? ProgressionAndLoadoutManager.LOBBY_SCENE_NAME
            : ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME);
    }

    public string GetBayTitle(TutorialBay bay)
    {
        switch (bay)
        {
            case TutorialBay.Bay1_Controls: return "Bay 1 — Controls & Channel Separation";
            case TutorialBay.Bay2_PowerSanity: return "Bay 2 — Power, Lights & Sanity";
            case TutorialBay.Bay3_Evidence: return "Bay 3 — Evidence, Orbs & D.O.T.S.";
            case TutorialBay.Bay4_Survival: return "Bay 4 — Hunt Survival & Exorcism";
            case TutorialBay.Bay5_Extraction: return "Bay 5 — Journal & Jeepney Extraction";
            default: return "Training Complete";
        }
    }

    public IReadOnlyList<(string stepId, string label, string keyBadge)> GetStepsForBay(TutorialBay bay)
    {
        switch (bay)
        {
            case TutorialBay.Bay1_Controls:
                return new[]
                {
                    (STEP_B1_PICKUP_FLASHLIGHT, "Pick up the Flashlight", "[L-Click]"),
                    (STEP_B1_TOGGLE_FLASHLIGHT, "Toggle Flashlight on/off", "[L-Click]"),
                    (STEP_B1_OPEN_DOOR, "Open Bay 1 exit door", "[E]")
                };
            case TutorialBay.Bay2_PowerSanity:
                return new[]
                {
                    (STEP_B2_ENTER_DARKNESS, "Enter the darkened corridor", "WASD"),
                    (STEP_B2_RESTORE_POWER, "Restore power at Fuse Box", "[E]"),
                    (STEP_B2_FLIP_LIGHT, "Flip the wall Light Switch", "[E]")
                };
            case TutorialBay.Bay3_Evidence:
                return new[]
                {
                    (STEP_B3_EMF, "Detect EMF Level 5 spike", "EMF"),
                    (STEP_B3_THERMOMETER, "Confirm Freezing Temps", "Thermo"),
                    (STEP_B3_SPIRIT_BOX, "Get a Spirit Box response", "Spirit Box"),
                    (STEP_B3_WRITING, "Place Book & observe writing", "[R-Click]"),
                    (STEP_B3_CAMERA_DOTS, "Observe Ghost Orbs & D.O.T.S.", "[R-Click]")
                };
            case TutorialBay.Bay4_Survival:
                return new[]
                {
                    (STEP_B4_CRUCIFIX, "Block hunt with Crucifix", "Crucifix"),
                    (STEP_B4_SURVIVE_HUNT, "Hide & turn off Flashlight (10s)", "HideZone"),
                    (STEP_B4_SANITY_PILLS, "Restore sanity with Pills", "[L-Click]"),
                    (STEP_B4_EXORCISM, "Banish entity at Ritual Circle", "[E]")
                };
            case TutorialBay.Bay5_Extraction:
                return new[]
                {
                    (STEP_B5_JOURNAL_DEDUCTION, "Log evidence & ghost in Journal", "[J]"),
                    (STEP_B5_EXIT_CONSOLE, "Board Jeepney & press End Game", "[E]")
                };
            default:
                return Array.Empty<(string, string, string)>();
        }
    }
}
