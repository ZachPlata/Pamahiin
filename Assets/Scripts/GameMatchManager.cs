using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Server-side manager that handles match progression, specifically tracking journal deductions,
/// applying lobby contract difficulty &amp; van equipment loadout, Jeepney arrival/departure audio &amp; ramp
/// choreography, and triggering the end-game reveal sequence.
/// </summary>
public class GameMatchManager : NetworkBehaviour
{
    public static GameMatchManager Instance { get; private set; }

    public Action<string> OnMatchEnded; // Passes the true ghost name
    public event Action OnGhostContractInfoChanged;

    private struct GhostBackgroundTemplate
    {
        public readonly string title;
        public readonly string template;

        public GhostBackgroundTemplate(string title, string template)
        {
            this.title = title;
            this.template = template;
        }
    }

    private static readonly string[] MaleFirstNames =
    {
        "Tomas", "Mateo", "Ernesto", "Andres", "Victoriano", "Gregorio",
        "Ramon", "Arturo", "Celestino", "Mariano", "Teodoro", "Bernardo"
    };

    private static readonly string[] FemaleFirstNames =
    {
        "Clara", "Remedios", "Lucia", "Soledad", "Rosario", "Elena",
        "Consuelo", "Milagros", "Estrella", "Amparo", "Leonora", "Beatriz"
    };

    private static readonly string[] FilipinoLastNames =
    {
        "Villanueva", "Evangelista", "Pascual", "Bautista",
        "Magbanua", "Del Rosario", "Dimaculangan", "Navarro",
        "Santos", "Pangilinan", "Magsaysay", "Macapagal",
        "Tolentino", "Salazar", "Alcantara", "Vergara"
    };

    // Completely spoiler-free Barangay San Nicolas haunting records (zero entity species clues)
    private static readonly GhostBackgroundTemplate[] BackgroundTemplates =
    {
        new GhostBackgroundTemplate(
            "Former Property Owner",
            "Barangay deed records list {name} as the last owner of 6 Tanglewood Drive. After {subj} passed quietly inside the house, neighbors reported porch lights flickering at midnight and footsteps pacing behind locked curtains."),
        new GhostBackgroundTemplate(
            "Retired Watchmaker",
            "Known across Barangay San Nicolas for repairing clocks until dawn, {name} died alone during a brownout. Subsequent tenants fled after cold drafts, stopped timepieces, and whispered numbers echoed through the hall."),
        new GhostBackgroundTemplate(
            "Longtime Caretaker",
            "{name} tended this residence for nearly two decades while the original family lived abroad. Following {poss} sudden disappearance in 1998, barangay tanods refused night patrols past the front gate."),
        new GhostBackgroundTemplate(
            "Parish Choir Member",
            "A longtime volunteer at San Nicolas Chapel, {name} vanished after an evening storm knocked out the streetlamps. Residents still hear faint humming and interior doors clicking shut long after curfew."),
        new GhostBackgroundTemplate(
            "Late-Night Tailor",
            "Neighbors remember {name} working beside the front window well past midnight. Weeks after {poss} burial, passersby swore they saw someone peering from the hallway whenever heavy rain lashed the street."),
        new GhostBackgroundTemplate(
            "Night-Shift Nurse",
            "Returning home in the early hours after hospital duty, {name} suffered a fatal collapse inside the house. Delivery riders and neighbors now refuse to step onto the porch after dusk."),
        new GhostBackgroundTemplate(
            "Barangay Archivist",
            "{name} kept decades of Barangay San Nicolas civil ledgers locked inside the residence. Families who rented the property afterward left within three days, citing restless knocking from empty rooms."),
        new GhostBackgroundTemplate(
            "Missing Fisherman",
            "After {name}'s boat returned empty to the riverbend, {poss} belongings were stored at 6 Tanglewood Drive. Relatives sealed the house when damp footprints kept reappearing along the floorboards."),
        new GhostBackgroundTemplate(
            "Reclusive Councilor",
            "Once respected across the district, {name} grew reclusive in {poss} final years. Neighbors reported heavy doors latching by themselves and sudden chills spilling out from under the threshold."),
        new GhostBackgroundTemplate(
            "Unclaimed Boarder",
            "{name} rented a room here during the late eighties before vanishing without collecting {poss} luggage. Every caretaker hired since has abandoned the property before the first month ended.")
    };

    // Completely spoiler-free Kampo Santo ng San Nicolas cemetery haunting records (zero entity species clues)
    private static readonly GhostBackgroundTemplate[] CemeteryBackgroundTemplates =
    {
        new GhostBackgroundTemplate(
            "Retired Sepulturero",
            "Parish burial ledgers record {name} as senior gravedigger at Kampo Santo ng San Nicolas for thirty years. After {subj} collapsed beside the old balete grove on All Souls' Eve, night caretakers heard shovels scraping stone well past midnight."),
        new GhostBackgroundTemplate(
            "All Souls' Caretaker",
            "Tasked with sweeping the northern nitso tiers and lighting altar candles, {name} vanished during a November typhoon. Visitors still report extinguished vigil flames relighting on their own along empty alleys."),
        new GhostBackgroundTemplate(
            "Mausoleum Benefactor",
            "{name} funded the construction of the southern family crypts before passing under disputed circumstances. Relatives abandoned the mausoleum grounds after heavy wrought-iron gates began latching behind mourners."),
        new GhostBackgroundTemplate(
            "Missing Candle Vendor",
            "Every dusk, {name} tended the flower and vigil stall just inside the cemetery gate. Weeks after {poss} sudden disappearance, fresh marigold petals and melted wax droplets continued appearing beside closed niches."),
        new GhostBackgroundTemplate(
            "Old Bell-Ringer",
            "Known across San Nicolas for tolling the parish curfew bell before cemetery closings, {name} was interred near the central cross plaza. Locals claim muffled footsteps circle the stone pedestal whenever fog settles over the graves."),
        new GhostBackgroundTemplate(
            "Unclaimed Niche Occupant",
            "Interred in an unmarked upper tier of the northeast nitso wall, {name} left no surviving kin to renew the plot lease. Groundskeepers who approached the alley after dark reported cold drafts spilling from sealed plaster."),
        new GhostBackgroundTemplate(
            "Parish Gravedigger",
            "Following decades of midnight burials during the cholera years, {name} kept careful tally books inside the bantay shed. Successors quit within days after hearing urgent knocking on the shed door from outside the empty yard."),
        new GhostBackgroundTemplate(
            "Late-Night Vigiler",
            "{name} kept silent bedside vigils inside the Delos Santos mausoleum long after visiting hours ended. Patrol tanods refuse to enter the southern lanes after spotting a lone figure standing near the stone altar."),
        new GhostBackgroundTemplate(
            "Stonecutter Apprentice",
            "Carving inscriptions across dozens of marble lapidas, {name} suffered a fatal accident near the southwest plots. Subsequent stonemasons discovered fresh chisel marks etched into untouched slabs overnight."),
        new GhostBackgroundTemplate(
            "Cemetery Gatekeeper",
            "{name} held the iron keys to Kampo Santo ng San Nicolas until {poss} death in 1994. Night guards still report the rusty west gate rattling violently even when no wind stirs the acacia leaves.")
    };

    private readonly NetworkVariable<int> ghostGenderIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> ghostFirstNameIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> ghostLastNameIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> ghostBackgroundIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> ghostCaseNumber = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> netIsTeamWipeGraceActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netTeamWipeSecondsRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static bool WasLocalPlayerDeadAtMatchEnd { get; private set; }

    public static void ResetLocalMatchEndFlags()
    {
        WasLocalPlayerDeadAtMatchEnd = false;
    }

    public static float GetTeamWipeGraceDuration(bool isMultiplayer)
    {
        return isMultiplayer ? 3f : 15f;
    }

    private int localGenderIndex = -1;
    private int localFirstNameIndex = -1;
    private int localLastNameIndex = -1;
    private int localBackgroundIndex = -1;
    private int localCaseNumber = -1;
    private bool hasSubscribedProfileCallbacks = false;
    private bool hasSubscribedNetworkCallbacks = false;

    private Dictionary<ulong, string> playerDeductions = new Dictionary<ulong, string>();
    private string cachedTrueGhostName = "";
    private bool isGhostDefeated = false;
    private bool hasPlayedArrivalSequence = false;
    private bool isMatchEnding = false;
    private bool localTeamWipeGraceActive = false;
    private float localTeamWipeSecondsRemaining = 0f;
    private int maxObservedConnectedPlayers = 1;
    private Coroutine teamWipeCoroutine;
    private NetworkDoor cachedRampDoor;

    public bool IsGhostDefeated => isGhostDefeated;
    public bool IsMatchEnding => isMatchEnding;
    public bool IsTeamWipeGraceActive =>
        (UseNetworkedContractProfile ? netIsTeamWipeGraceActive.Value : localTeamWipeGraceActive) && !isMatchEnding;
    public float TeamWipeSecondsRemaining =>
        UseNetworkedContractProfile ? netTeamWipeSecondsRemaining.Value : localTeamWipeSecondsRemaining;

    private bool UseNetworkedContractProfile =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    private int ActiveGenderIndex => UseNetworkedContractProfile ? ghostGenderIndex.Value : localGenderIndex;
    private int ActiveFirstNameIndex => UseNetworkedContractProfile ? ghostFirstNameIndex.Value : localFirstNameIndex;
    private int ActiveLastNameIndex => UseNetworkedContractProfile ? ghostLastNameIndex.Value : localLastNameIndex;
    private int ActiveBackgroundIndex => UseNetworkedContractProfile ? ghostBackgroundIndex.Value : localBackgroundIndex;
    private int ActiveCaseNumber => UseNetworkedContractProfile ? ghostCaseNumber.Value : localCaseNumber;

    private static bool IsCemeteryMapActive()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (activeScene == ProgressionAndLoadoutManager.CEMETERY_MAP_SCENE_NAME) return true;
        if (activeScene == ProgressionAndLoadoutManager.BARANGAY_MAP_SCENE_NAME) return false;
        return ProgressionAndLoadoutManager.Instance != null &&
               ProgressionAndLoadoutManager.Instance.GetSelectedSceneName() == ProgressionAndLoadoutManager.CEMETERY_MAP_SCENE_NAME;
    }

    public bool HasAssignedGhostProfile =>
        ActiveGenderIndex >= 0 &&
        ActiveFirstNameIndex >= 0 &&
        ActiveLastNameIndex >= 0 &&
        ActiveBackgroundIndex >= 0 &&
        ActiveCaseNumber > 0;

    public string AssignedCaseId =>
        HasAssignedGhostProfile
            ? $"{(IsCemeteryMapActive() ? "KSN" : "BSN")}-{ActiveCaseNumber:D4}"
            : "SYNCING";

    public string AssignedGhostName
    {
        get
        {
            if (!HasAssignedGhostProfile) return "Investigating...";
            bool isFemale = ActiveGenderIndex == 1;
            string[] firstPool = isFemale ? FemaleFirstNames : MaleFirstNames;
            string first = firstPool[Mathf.Clamp(ActiveFirstNameIndex, 0, firstPool.Length - 1)];
            string last = FilipinoLastNames[Mathf.Clamp(ActiveLastNameIndex, 0, FilipinoLastNames.Length - 1)];
            return $"{first} {last}";
        }
    }

    public string AssignedGhostBackgroundTitle
    {
        get
        {
            bool isCemetery = IsCemeteryMapActive();
            if (!HasAssignedGhostProfile)
            {
                return isCemetery ? "Kampo Santo Incident Record" : "Barangay Incident Record";
            }
            GhostBackgroundTemplate[] pool = isCemetery ? CemeteryBackgroundTemplates : BackgroundTemplates;
            int idx = Mathf.Clamp(ActiveBackgroundIndex, 0, pool.Length - 1);
            return pool[idx].title;
        }
    }

    public string AssignedGhostBackground
    {
        get
        {
            bool isCemetery = IsCemeteryMapActive();
            if (!HasAssignedGhostProfile)
            {
                return isCemetery
                    ? "Retrieving Kampo Santo ng San Nicolas burial ledgers and witness statements..."
                    : "Retrieving Barangay San Nicolas civil records and witness statements...";
            }

            bool isFemale = ActiveGenderIndex == 1;
            GhostBackgroundTemplate[] pool = isCemetery ? CemeteryBackgroundTemplates : BackgroundTemplates;
            int idx = Mathf.Clamp(ActiveBackgroundIndex, 0, pool.Length - 1);
            string fullName = AssignedGhostName;
            string subj = isFemale ? "she" : "he";
            string subjCap = isFemale ? "She" : "He";
            string obj = isFemale ? "her" : "him";
            string poss = isFemale ? "her" : "his";

            return pool[idx].template
                .Replace("{name}", fullName)
                .Replace("{subjCap}", subjCap)
                .Replace("{subj}", subj)
                .Replace("{obj}", obj)
                .Replace("{poss}", poss);
        }
    }

    public void MarkGhostDefeated()
    {
        isGhostDefeated = true;
    }

    public void SetTrueGhostName(string ghostName)
    {
        if (!string.IsNullOrEmpty(ghostName) && !ghostName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            cachedTrueGhostName = ghostName;
            Debug.Log($"[GameMatchManager] True Ghost Name cached: {cachedTrueGhostName}");
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ProgressionAndLoadoutManager.EnsureInstance();
    }

    private void Start()
    {
        // Reset previous match deduction guess & local death status at start of a new investigation
        JournalController.LastLockedInGuess = "None";
        ResetLocalMatchEndFlags();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer)
        {
            EnsureGhostContractProfileAssigned();
            EnsureVanLoadoutAndDifficultyApplied();
            TriggerArrivalSequence();
        }
    }

    public override void OnNetworkSpawn()
    {
        SubscribeProfileCallbacks();
        SubscribeServerNetworkCallbacks();
        ResetLocalMatchEndFlags();

        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
            {
                maxObservedConnectedPlayers = Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count);
            }
            EnsureGhostContractProfileAssigned();
            EnsureVanLoadoutAndDifficultyApplied();
        }

        TriggerArrivalSequence();
        NotifyGhostContractInfoChanged();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeProfileCallbacks();
        UnsubscribeServerNetworkCallbacks();
        StopTeamWipeRoutine();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnsubscribeProfileCallbacks();
        UnsubscribeServerNetworkCallbacks();
        StopTeamWipeRoutine();
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    private void SubscribeServerNetworkCallbacks()
    {
        if (hasSubscribedNetworkCallbacks || !IsServer || NetworkManager.Singleton == null) return;
        hasSubscribedNetworkCallbacks = true;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleServerClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleServerClientDisconnected;
    }

    private void UnsubscribeServerNetworkCallbacks()
    {
        if (!hasSubscribedNetworkCallbacks) return;
        hasSubscribedNetworkCallbacks = false;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleServerClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleServerClientDisconnected;
        }
    }

    private void HandleServerClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null)
        {
            maxObservedConnectedPlayers = Mathf.Max(maxObservedConnectedPlayers, NetworkManager.Singleton.ConnectedClientsIds.Count);
        }
    }

    private void HandleServerClientDisconnected(ulong clientId)
    {
        CheckAllPlayersDead(clientId);
    }

    private void SubscribeProfileCallbacks()
    {
        if (hasSubscribedProfileCallbacks) return;
        hasSubscribedProfileCallbacks = true;
        ghostGenderIndex.OnValueChanged += OnContractProfileIntChanged;
        ghostFirstNameIndex.OnValueChanged += OnContractProfileIntChanged;
        ghostLastNameIndex.OnValueChanged += OnContractProfileIntChanged;
        ghostBackgroundIndex.OnValueChanged += OnContractProfileIntChanged;
        ghostCaseNumber.OnValueChanged += OnContractProfileIntChanged;
    }

    private void UnsubscribeProfileCallbacks()
    {
        if (!hasSubscribedProfileCallbacks) return;
        hasSubscribedProfileCallbacks = false;
        ghostGenderIndex.OnValueChanged -= OnContractProfileIntChanged;
        ghostFirstNameIndex.OnValueChanged -= OnContractProfileIntChanged;
        ghostLastNameIndex.OnValueChanged -= OnContractProfileIntChanged;
        ghostBackgroundIndex.OnValueChanged -= OnContractProfileIntChanged;
        ghostCaseNumber.OnValueChanged -= OnContractProfileIntChanged;
    }

    private void OnContractProfileIntChanged(int previousValue, int newValue)
    {
        NotifyGhostContractInfoChanged();
    }

    private void NotifyGhostContractInfoChanged()
    {
        OnGhostContractInfoChanged?.Invoke();
    }

    private void EnsureGhostContractProfileAssigned()
    {
        bool canAssign = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
        if (!canAssign) return;

        if (localGenderIndex < 0 || localFirstNameIndex < 0 || localLastNameIndex < 0 || localBackgroundIndex < 0 || localCaseNumber <= 0)
        {
            localGenderIndex = UnityEngine.Random.Range(0, 2);
            string[] firstPool = localGenderIndex == 1 ? FemaleFirstNames : MaleFirstNames;
            localFirstNameIndex = UnityEngine.Random.Range(0, firstPool.Length);
            localLastNameIndex = UnityEngine.Random.Range(0, FilipinoLastNames.Length);
            localBackgroundIndex = UnityEngine.Random.Range(0, BackgroundTemplates.Length);
            localCaseNumber = UnityEngine.Random.Range(1000, 10000);
        }

        if (IsSpawned && IsServer)
        {
            if (ghostGenderIndex.Value < 0) ghostGenderIndex.Value = localGenderIndex;
            if (ghostFirstNameIndex.Value < 0) ghostFirstNameIndex.Value = localFirstNameIndex;
            if (ghostLastNameIndex.Value < 0) ghostLastNameIndex.Value = localLastNameIndex;
            if (ghostBackgroundIndex.Value < 0) ghostBackgroundIndex.Value = localBackgroundIndex;
            if (ghostCaseNumber.Value <= 0) ghostCaseNumber.Value = localCaseNumber;
        }

        NotifyGhostContractInfoChanged();
    }

    private NetworkDoor FindJeepneyRampDoor()
    {
        if (cachedRampDoor != null) return cachedRampDoor;
        var go = GameObject.Find("JeepneyRampDoor");
        if (go != null) cachedRampDoor = go.GetComponent<NetworkDoor>();
        return cachedRampDoor;
    }

    private void TriggerArrivalSequence()
    {
        if (hasPlayedArrivalSequence) return;
        hasPlayedArrivalSequence = true;
        StartCoroutine(JeepneyArrivalRoutine());
    }

    private IEnumerator JeepneyArrivalRoutine()
    {
        yield return new WaitForSeconds(0.15f);

        NetworkDoor ramp = FindJeepneyRampDoor();
        Vector3 sfxPos = ramp != null ? ramp.transform.position : Vector3.zero;
        GameAudioManager.PlayTruckArriveStop(sfxPos);

        // Ramp intentionally remains closed on arrival; player opens it via TruckEndGameButton.
    }

    private void EnsureVanLoadoutAndDifficultyApplied()
    {
        // 1. In offline standalone play, spawn selected equipment immediately.
        // In active Netcode sessions, NetworkController spawns equipment after OnLoadEventCompleted so all clients are synced first.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            var spawner = FindAnyObjectByType<TruckGearSpawner>();
            if (spawner == null)
            {
                spawner = new GameObject("TruckGearSpawner").AddComponent<TruckGearSpawner>();
            }
            spawner.SpawnSelectedLoadoutInVan();
        }

        // 2. Apply selected Contract Difficulty modifiers to GhostHandler
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null && ProgressionAndLoadoutManager.Instance != null)
        {
            ContractDifficulty diff = ProgressionAndLoadoutManager.Instance.SelectedDifficulty;
            ghost.ResetSetupGracePeriod(ProgressionAndLoadoutManager.GetDifficultySetupTimeSeconds(diff));

            switch (diff)
            {
                case ContractDifficulty.Amateur:
                    ghost.huntDurationMin = 18f;
                    ghost.huntDurationMax = 26f;
                    break;
                case ContractDifficulty.Intermediate:
                    ghost.huntDurationMin = 24f;
                    ghost.huntDurationMax = 34f;
                    break;
                case ContractDifficulty.Professional:
                    ghost.huntDurationMin = 30f;
                    ghost.huntDurationMax = 42f;
                    break;
                case ContractDifficulty.Nightmare:
                    ghost.huntDurationMin = 38f;
                    ghost.huntDurationMax = 52f;
                    break;
            }
        }
    }

    public void LockInDeduction(string ghostType)
    {
        RecordDeduction(ghostType);
    }

    public void RecordDeduction(string ghostType)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            SubmitDeductionServerRpc(ghostType);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitDeductionServerRpc(string ghostType, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!playerDeductions.ContainsKey(clientId))
        {
            playerDeductions.Add(clientId, ghostType);
        }
        else
        {
            playerDeductions[clientId] = ghostType;
        }

        Debug.Log($"Server recorded deduction '{ghostType}' from client {clientId}. Match continues until van End Game button is pressed.");
    }

    public void RequestEndMatch()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer)
        {
            ForceEndMatch();
            return;
        }

        if (IsSpawned)
        {
            RequestEndMatchServerRpc();
        }
        else
        {
            ForceEndMatch();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndMatchServerRpc()
    {
        ForceEndMatch();
    }

    public void RequestEarlyTeamWipeEndMatch()
    {
        if (isMatchEnding) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            RequestEarlyTeamWipeEndMatchServerRpc();
            return;
        }

        if (IsTeamWipeGraceActive)
        {
            ForceEndMatch();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEarlyTeamWipeEndMatchServerRpc()
    {
        if (isMatchEnding || !IsTeamWipeGraceActive) return;
        ForceEndMatch();
    }

    /// <summary>
    /// Evaluates whether all active connected players have finished dying and entered GhostSpectator.
    /// Starts the 3s multiplayer / 15s solo wipe countdown when zero Alive or DyingSequence players remain,
    /// and cancels cleanly if any player revives.
    /// </summary>
    public void CheckAllPlayersDead(ulong? disconnectedClientId = null)
    {
        if (isMatchEnding) return;

        bool isListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isListening && !NetworkManager.Singleton.IsServer) return;

        if (AreAllConnectedPlayersDeadAndSettled(disconnectedClientId, out int totalCount))
        {
            if (totalCount > 0 && teamWipeCoroutine == null)
            {
                teamWipeCoroutine = StartCoroutine(TeamWipeGraceRoutine());
            }
        }
        else
        {
            StopTeamWipeRoutine();
        }
    }

    private bool AreAllConnectedPlayersDeadAndSettled(ulong? disconnectedClientId, out int totalPlayers)
    {
        totalPlayers = 0;
        int aliveOrDyingCount = 0;
        int spectatorCount = 0;

        bool isListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isListening)
        {
            var clients = NetworkManager.Singleton.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                var client = clients[i];
                if (disconnectedClientId.HasValue && client.ClientId == disconnectedClientId.Value) continue;

                PlayerController pc = null;
                if (client.PlayerObject != null)
                {
                    pc = client.PlayerObject.GetComponent<PlayerController>();
                }
                if (pc == null)
                {
                    PlayerController.TryGetByClientId(client.ClientId, out pc);
                }
                if (pc == null) continue;

                totalPlayers++;
                if (pc.IsAlive || pc.IsDyingSequence) aliveOrDyingCount++;
                else if (pc.IsGhostSpectator) spectatorCount++;
            }
        }
        else
        {
            for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
            {
                var pc = PlayerController.AllPlayers[i];
                if (pc == null) continue;
                totalPlayers++;
                if (pc.IsAlive || pc.IsDyingSequence) aliveOrDyingCount++;
                else if (pc.IsGhostSpectator) spectatorCount++;
            }
        }

        maxObservedConnectedPlayers = Mathf.Max(maxObservedConnectedPlayers, totalPlayers);
        return totalPlayers > 0 && spectatorCount > 0 && aliveOrDyingCount == 0;
    }

    private IEnumerator TeamWipeGraceRoutine()
    {
        bool isListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool isMultiplayer = isListening && (NetworkManager.Singleton.ConnectedClientsIds.Count > 1 || maxObservedConnectedPlayers > 1);
        float remaining = GetTeamWipeGraceDuration(isMultiplayer);

        SetTeamWipeState(true, remaining);
        float nextSyncTime = 0.25f;

        while (remaining > 0f && !isMatchEnding)
        {
            if (!AreAllConnectedPlayersDeadAndSettled(null, out _))
            {
                SetTeamWipeState(false, 0f);
                teamWipeCoroutine = null;
                yield break;
            }

            float dt = Time.unscaledDeltaTime;
            remaining = Mathf.Max(0f, remaining - dt);
            nextSyncTime -= dt;

            if (nextSyncTime <= 0f || remaining <= 0f)
            {
                nextSyncTime = 0.25f;
                SetTeamWipeState(true, remaining);
            }

            yield return null;
        }

        teamWipeCoroutine = null;
        SetTeamWipeState(false, 0f);

        if (!isMatchEnding)
        {
            ForceEndMatch();
        }
    }

    private void StopTeamWipeRoutine()
    {
        if (teamWipeCoroutine != null)
        {
            StopCoroutine(teamWipeCoroutine);
            teamWipeCoroutine = null;
        }
        SetTeamWipeState(false, 0f);
    }

    private void SetTeamWipeState(bool active, float secondsRemaining)
    {
        localTeamWipeGraceActive = active;
        localTeamWipeSecondsRemaining = secondsRemaining;

        if (IsSpawned && IsServer)
        {
            netIsTeamWipeGraceActive.Value = active;
            netTeamWipeSecondsRemaining.Value = secondsRemaining;
        }
    }

    public void ForceEndMatch()
    {
        if (isMatchEnding) return;
        isMatchEnding = true;
        StopTeamWipeRoutine();

        string actualGhostName = cachedTrueGhostName;
        var ghost = UnityEngine.Object.FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            if (ghost.IsExorcised) isGhostDefeated = true;
            if (!string.IsNullOrEmpty(ghost.ghostName) && !ghost.ghostName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                actualGhostName = ghost.ghostName;
                cachedTrueGhostName = ghost.ghostName;
            }
        }

        if (ExorcismManager.Instance != null && ExorcismManager.Instance.IsExorcismCompleted)
        {
            isGhostDefeated = true;
        }

        if (string.IsNullOrEmpty(actualGhostName))
        {
            actualGhostName = "Unknown Entity";
        }

        // Server commands Jeepney rear ramp to close immediately on departure
        NetworkDoor ramp = FindJeepneyRampDoor();
        if (ramp != null)
        {
            ramp.SetDoorOpen(false);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && NetworkManager.Singleton.IsServer)
        {
            EndMatchClientRpc(actualGhostName, isGhostDefeated);
            return;
        }

        ExecuteLocalEndMatchReveal(actualGhostName, isGhostDefeated);
    }

    [ClientRpc]
    private void EndMatchClientRpc(string trueName, bool defeated)
    {
        isMatchEnding = true;
        ExecuteLocalEndMatchReveal(trueName, defeated);
    }

    private void ExecuteLocalEndMatchReveal(string trueName, bool defeated)
    {
        isGhostDefeated = defeated;
        WasLocalPlayerDeadAtMatchEnd = PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive;

        // Clear spectator fog/burial visuals smoothly as the debrief transition begins
        WeatherManager.SetLocalInBurialMap(false);
        DeathSequenceController.Instance?.CancelAndReset();
        GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);

        // Lock player controls immediately so players cannot exit once departing
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;
        }
        else if (PlayerController.LocalInstance != null)
        {
            PlayerController.LocalInstance.enabled = false;
        }

        StartCoroutine(WaitForTruckAudioThenReturnToLobbyRoutine(trueName, defeated));
    }

    private IEnumerator WaitForTruckAudioThenReturnToLobbyRoutine(string trueName, bool defeated)
    {
        NetworkDoor ramp = FindJeepneyRampDoor();
        Vector3 sfxPos = ramp != null ? ramp.transform.position : Vector3.zero;
        float departDuration = GameAudioManager.PlayTruckStartDepart(sfxPos);
        if (departDuration <= 0f) departDuration = 1.5f;

        float elapsed = 0f;
        float maxWait = departDuration + 1.5f;
        while (elapsed < departDuration || (ramp != null && ramp.CurrentProgress > 0.02f && elapsed < maxWait))
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Persist final deduction & payout before unloading the map scene;
        // the ghost reveal screen opens after returning to the Safe House Lobby.
        EndGameRevealController.PreparePendingReveal(trueName, defeated);

        bool isConnectedRemoteClient = NetworkManager.Singleton != null &&
                                       NetworkManager.Singleton.IsListening &&
                                       NetworkManager.Singleton.IsClient &&
                                       !NetworkManager.Singleton.IsServer;

        var loadingMgr = LoadingScreenManager.EnsureInstance();
        if (loadingMgr != null)
        {
            loadingMgr.ShowIntermission(
                "DEPARTING INVESTIGATION SITE",
                isConnectedRemoteClient
                    ? "Returning to Safe House Lobby & awaiting Host transit..."
                    : "Unloading investigation area & returning to Safe House Lobby...",
                "CASE DEBRIEFING");
            loadingMgr.SetProgress(isConnectedRemoteClient ? 0.28f : 0.25f, "Packing van & closing case file...");
        }

        if (isConnectedRemoteClient)
        {
            yield break;
        }

        float holdDuration = 1.25f;
        float holdElapsed = 0f;
        while (holdElapsed < holdDuration)
        {
            holdElapsed += Time.unscaledDeltaTime;
            if (loadingMgr != null)
            {
                loadingMgr.SetProgress(
                    Mathf.Lerp(0.25f, 0.48f, Mathf.Clamp01(holdElapsed / holdDuration)),
                    "Deactivating investigation zone...");
            }
            yield return null;
        }

        TruckGearSpawner.CleanupAllGhostOrbs();

        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.ReturnToLobbyFromMatch();
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                ProgressionAndLoadoutManager.LOBBY_SCENE_NAME,
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME);
        }
    }

    public string GetLocalDeduction()
    {
        return null;
    }
}
