using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum ContractDifficulty
{
    Amateur = 0,
    Intermediate = 1,
    Professional = 2,
    Nightmare = 3
}

[Serializable]
public class EquipmentCatalogEntry
{
    public string id;
    public string displayName;
    public int cost;
    public int maxCount;
    public string description;
    [NonSerialized] public Type componentType;

    public EquipmentCatalogEntry(string id, string displayName, int cost, int maxCount, string description, Type componentType)
    {
        this.id = id;
        this.displayName = displayName;
        this.cost = cost;
        this.maxCount = maxCount;
        this.description = description;
        this.componentType = componentType;
    }
}

public struct MatchPayoutBreakdown
{
    public int correctEvidenceCount;
    public int evidenceReward;
    public bool correctGhostType;
    public int ghostTypeReward;
    public bool ghostDefeated;
    public int ghostDefeatedReward;
    public ContractDifficulty difficulty;
    public float difficultyMultiplier;
    public bool isDeceased;
    public float survivalMultiplier;
    public int insuranceRefund;
    public int totalEarned;
    public int newBalance;
}

public struct LobbyPlayerSlotEntry
{
    public ulong clientId;
    public int slotIndex;
}

public struct LoadoutContributorEntry
{
    public ulong clientId;
    public int slotIndex;
    public int count;
}

/// <summary>
/// Manages persistent in-game currency, lobby mission configuration (Map, Difficulty, Party Code),
/// van ghost equipment loadout selection, and end-of-match currency payouts.
/// </summary>
public class ProgressionAndLoadoutManager : MonoBehaviour
{
    public static ProgressionAndLoadoutManager Instance { get; private set; }

    public const string LOBBY_SCENE_NAME = "LobbyScene";
    public const string BARANGAY_MAP_SCENE_NAME = "BarangayMap";
    public const string CEMETERY_MAP_SCENE_NAME = "CemeteryMap";
    public const string SCHOOL_MAP_SCENE_NAME = "SchoolMap";
    public const string GAME_MAP_SCENE_NAME = "GameMapScene"; // Legacy prototype scene (kept on disk)
    public const string MAIN_MENU_SCENE_NAME = "MainMenuScene";
    public const string WAREHOUSE_TUTORIAL_SCENE_NAME = "WarehouseTutorialScene";

    public const string PREF_TUTORIAL_COMPLETED = "Pamahiin_TutorialCompleted_v1";

    private const string PREF_CURRENCY_INIT_LEGACY = "Pamahiin_Currency_Initialized_v1";
    private const string PREF_CURRENCY_LEGACY = "Pamahiin_Currency_v1";
    private const string PREF_LOADOUT_PREFIX_LEGACY = "Pamahiin_Loadout_v1_";

    private const string PREF_CURRENCY_INIT = "Pamahiin_Currency_Initialized_v2";
    private const string PREF_CURRENCY = "Pamahiin_Currency_v2";
    private const string PREF_DIFFICULTY = "Pamahiin_Difficulty_v1";
    private const string PREF_MAP_INDEX = "Pamahiin_MapIndex_v1";
    private const string PREF_LOADOUT_PREFIX = "Pamahiin_Loadout_v2_";

    public const int STARTER_CURRENCY = 250;
    public const int REWARD_PER_CORRECT_EVIDENCE = 35;
    public const int REWARD_CORRECT_GHOST_TYPE = 100;
    public const int REWARD_DEFEAT_GHOST = 150;
    public const float DECEASED_PAYOUT_MULTIPLIER = 0.5f;

    public event Action OnCurrencyOrLoadoutChanged;
    public event Action OnContractSettingsChanged;

    private static readonly string[] KnownEquipmentIds =
    {
        "Flashlight",
        "EMFReader",
        "Thermometer",
        "SpiritBox",
        "GhostWritingBook",
        "DotsProjector",
        "VideoCamera",
        "Crucifix",
        "SanityPills",
        "RitualChalk"
    };

    public readonly List<EquipmentCatalogEntry> Catalog = new List<EquipmentCatalogEntry>
    {
        new EquipmentCatalogEntry("Flashlight", "Flashlight", 20, 4, "Illuminates dark rooms and reduces sanity drain.", typeof(FlashlightItem)),
        new EquipmentCatalogEntry("EMFReader", "EMF Reader", 35, 2, "Detects electromagnetic spikes up to EMF Level 5.", typeof(EMFReaderItem)),
        new EquipmentCatalogEntry("Thermometer", "Thermometer", 30, 2, "Measures ambient room temperature & Freezing Temps.", typeof(ThermometerItem)),
        new EquipmentCatalogEntry("SpiritBox", "Spirit Box", 45, 2, "Communicates with vocal entities in the dark.", typeof(SpiritBoxItem)),
        new EquipmentCatalogEntry("GhostWritingBook", "Ghost Writing Book", 40, 2, "Place on the ground for entities to write in.", typeof(GhostWritingBookItem)),
        new EquipmentCatalogEntry("DotsProjector", "D.O.T.S. Projector", 55, 2, "Projects a laser grid that reveals D.O.T.S. silhouettes.", typeof(DotsProjectorItem)),
        new EquipmentCatalogEntry("VideoCamera", "Video Camera", 50, 4, "Streams live night-vision feeds &Ghost Orbs to the van.", typeof(VideoCameraItem)),
        new EquipmentCatalogEntry("Crucifix", "Crucifix", 60, 2, "Prevents the ghost from initiating a hunt nearby.", typeof(CrucifixItem)),
        new EquipmentCatalogEntry("SanityPills", "Sanity Pills", 45, 4, "Restores 40% sanity when consumed.", typeof(SanityPillsItem)),
        new EquipmentCatalogEntry("RitualChalk", "Ritual Chalk", 50, 2, "Required to draw the exorcism circle and defeat the ghost.", typeof(RitualChalkItem))
    };

    public readonly string[] AvailableMaps = new string[]
    {
        "Barangay San Nicolas — 6 Tanglewood Drive",    // Index 0 -> BarangayMap
        "Kampo Santo ng San Nicolas — Public Cemetery", // Index 1 -> CemeteryMap
        "San Nicolas High School (3 Floors)"            // Index 2 -> SchoolMap
    };

    private readonly Dictionary<string, int> loadoutCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, Dictionary<ulong, int>> multiplayerContributions = new Dictionary<string, Dictionary<ulong, int>>();
    private readonly List<LobbyPlayerSlotEntry> lobbyPlayerSlots = new List<LobbyPlayerSlotEntry>();
    private readonly Dictionary<string, int> pendingRemoveCounts = new Dictionary<string, int>();

    private static readonly Color32[] PlayerSlotColors =
    {
        new Color32(59, 130, 246, 255),   // P1 (Host): Cobalt Blue
        new Color32(239, 68, 68, 255),    // P2: Coral Red
        new Color32(34, 197, 94, 255),    // P3: Emerald Green
        new Color32(234, 179, 8, 255),    // P4: Amber Gold
        new Color32(217, 70, 239, 255),   // P5+: Orchid
        new Color32(6, 182, 212, 255)     // P6+: Cyan
    };

    private bool isMultiplayerClientSession = false;
    private int pendingPurchaseCost = 0;

    public int Currency { get; private set; }
    public ContractDifficulty SelectedDifficulty { get; private set; } = ContractDifficulty.Amateur;
    public int SelectedMapIndex { get; private set; } = 0;
    public bool IsMultiplayerClientSession => isMultiplayerClientSession;

    public static Color GetPlayerColor(int slotIndex)
    {
        int idx = ((slotIndex % PlayerSlotColors.Length) + PlayerSlotColors.Length) % PlayerSlotColors.Length;
        return PlayerSlotColors[idx];
    }

    public static string GetPlayerColorHex(int slotIndex)
    {
        Color32 c = GetPlayerColor(slotIndex);
        return $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }

    public static ulong LocalNetworkClientId =>
        (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            ? NetworkManager.Singleton.LocalClientId
            : 0UL;

    public static bool IsNetworkHostOrOffline =>
        NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

    public static bool IsConnectedMultiplayerClient =>
        NetworkManager.Singleton != null &&
        NetworkManager.Singleton.IsListening &&
        NetworkManager.Singleton.IsClient &&
        !NetworkManager.Singleton.IsServer;

    public int GetAvailableCurrency() => Mathf.Max(0, Currency - pendingPurchaseCost);

    public static bool IsInLobbyScene =>
        SceneManager.GetActiveScene().name == LOBBY_SCENE_NAME;

    public static bool IsInWarehouseTutorialScene =>
        SceneManager.GetActiveScene().name == WAREHOUSE_TUTORIAL_SCENE_NAME;

    public static bool HasCompletedOrSkippedTutorial()
    {
        return PlayerPrefs.GetInt(PREF_TUTORIAL_COMPLETED, 0) == 1;
    }

    public static void MarkTutorialCompleted()
    {
        PlayerPrefs.SetInt(PREF_TUTORIAL_COMPLETED, 1);
        PlayerPrefs.Save();
    }

    public static void MarkTutorialSkipped()
    {
        PlayerPrefs.SetInt(PREF_TUTORIAL_COMPLETED, 1);
        PlayerPrefs.Save();
    }

    public static void ResetTutorialCompletion()
    {
        PlayerPrefs.DeleteKey(PREF_TUTORIAL_COMPLETED);
        PlayerPrefs.Save();
    }

    public static ProgressionAndLoadoutManager EnsureInstance()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<ProgressionAndLoadoutManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("ProgressionAndLoadoutManager");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ProgressionAndLoadoutManager>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadStateFromPrefs();
    }

    private void LoadStateFromPrefs()
    {
        if (PlayerPrefs.GetInt(PREF_CURRENCY_INIT, 0) == 0)
        {
            InitializeDefaultProgressionPrefs();
        }
        else
        {
            Currency = Mathf.Max(0, PlayerPrefs.GetInt(PREF_CURRENCY, STARTER_CURRENCY));
        }

        SelectedDifficulty = (ContractDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(PREF_DIFFICULTY, 0), 0, 3);
        SelectedMapIndex = Mathf.Clamp(PlayerPrefs.GetInt(PREF_MAP_INDEX, 0), 0, AvailableMaps.Length - 1);

        loadoutCounts.Clear();
        foreach (var entry in Catalog)
        {
            int savedCount = Mathf.Clamp(PlayerPrefs.GetInt(PREF_LOADOUT_PREFIX + entry.id, 0), 0, entry.maxCount);
            loadoutCounts[entry.id] = savedCount;
        }
    }

    private void InitializeDefaultProgressionPrefs()
    {
        Currency = STARTER_CURRENCY;
        PlayerPrefs.SetInt(PREF_CURRENCY, Currency);
        PlayerPrefs.SetInt(PREF_CURRENCY_INIT, 1);

        loadoutCounts.Clear();
        foreach (var entry in Catalog)
        {
            int starterCount = entry.id == "Flashlight" ? 1 : 0;
            loadoutCounts[entry.id] = starterCount;
            PlayerPrefs.SetInt(PREF_LOADOUT_PREFIX + entry.id, starterCount);
        }

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Clears persisted currency and loadout keys and restores the starter $250 balance + 1 starter Flashlight.
    /// </summary>
    public void ResetProgressionToDefault()
    {
        ClearPersistedProgressionPrefs(Catalog);
        InitializeDefaultProgressionPrefs();
        if (IsNetworkHostOrOffline)
        {
            InitializeServerLobbyLedger();
            NetworkController.Instance?.BroadcastLobbyLoadoutSync();
        }
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public static void ClearPersistedProgressionPrefs(IEnumerable<EquipmentCatalogEntry> catalog = null)
    {
        PlayerPrefs.DeleteKey(PREF_CURRENCY_INIT_LEGACY);
        PlayerPrefs.DeleteKey(PREF_CURRENCY_LEGACY);
        PlayerPrefs.DeleteKey(PREF_CURRENCY_INIT);
        PlayerPrefs.DeleteKey(PREF_CURRENCY);

        if (catalog != null)
        {
            foreach (var entry in catalog)
            {
                PlayerPrefs.DeleteKey(PREF_LOADOUT_PREFIX_LEGACY + entry.id);
                PlayerPrefs.DeleteKey(PREF_LOADOUT_PREFIX + entry.id);
            }
        }
        else
        {
            for (int i = 0; i < KnownEquipmentIds.Length; i++)
            {
                string eqId = KnownEquipmentIds[i];
                PlayerPrefs.DeleteKey(PREF_LOADOUT_PREFIX_LEGACY + eqId);
                PlayerPrefs.DeleteKey(PREF_LOADOUT_PREFIX + eqId);
            }
        }
        PlayerPrefs.Save();
    }

    private void SaveCurrencyOnlyToPrefs()
    {
        PlayerPrefs.SetInt(PREF_CURRENCY, Currency);
        PlayerPrefs.SetInt(PREF_CURRENCY_INIT, 1);
        PlayerPrefs.Save();
    }

    private void SaveStateToPrefs()
    {
        // Connected non-host clients only persist their personal currency so they never overwrite
        // the host's loadout/contract PlayerPrefs when testing multiplayer on the same machine.
        if (isMultiplayerClientSession || IsConnectedMultiplayerClient)
        {
            SaveCurrencyOnlyToPrefs();
            return;
        }

        PlayerPrefs.SetInt(PREF_CURRENCY, Currency);
        PlayerPrefs.SetInt(PREF_CURRENCY_INIT, 1);
        PlayerPrefs.SetInt(PREF_DIFFICULTY, (int)SelectedDifficulty);
        PlayerPrefs.SetInt(PREF_MAP_INDEX, SelectedMapIndex);

        // On an active multiplayer server, persist ONLY the host's own personal contributions,
        // never the combined team loadoutCounts (which include remote players' purchases).
        bool useServerOwnContributions = NetworkManager.Singleton != null &&
                                         NetworkManager.Singleton.IsListening &&
                                         NetworkManager.Singleton.IsServer &&
                                         multiplayerContributions.Count > 0;

        foreach (var entry in Catalog)
        {
            int count = useServerOwnContributions ? GetOwnContributionCount(entry.id) : GetLoadoutCount(entry.id);
            PlayerPrefs.SetInt(PREF_LOADOUT_PREFIX + entry.id, count);
        }

        PlayerPrefs.Save();
    }

    public int GetLoadoutCount(string equipmentId)
    {
        if (string.IsNullOrEmpty(equipmentId)) return 0;
        return loadoutCounts.TryGetValue(equipmentId, out int count) ? count : 0;
    }

    public int GetOwnContributionCount(string equipmentId)
    {
        if (string.IsNullOrEmpty(equipmentId)) return 0;
        if (!isMultiplayerClientSession && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            return GetLoadoutCount(equipmentId);
        }

        if (multiplayerContributions.TryGetValue(equipmentId, out var map) &&
            map.TryGetValue(LocalNetworkClientId, out int own))
        {
            return own;
        }
        return 0;
    }

    public int GetPendingRemoveCount(string equipmentId)
    {
        if (string.IsNullOrEmpty(equipmentId)) return 0;
        return pendingRemoveCounts.TryGetValue(equipmentId, out int pending) ? pending : 0;
    }

    public IReadOnlyList<LobbyPlayerSlotEntry> GetLobbyPlayerSlots()
    {
        if (lobbyPlayerSlots.Count == 0 && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            lobbyPlayerSlots.Add(new LobbyPlayerSlotEntry { clientId = 0UL, slotIndex = 0 });
        }
        return lobbyPlayerSlots;
    }

    public int GetPlayerSlotIndex(ulong clientId)
    {
        for (int i = 0; i < lobbyPlayerSlots.Count; i++)
        {
            if (lobbyPlayerSlots[i].clientId == clientId) return lobbyPlayerSlots[i].slotIndex;
        }
        return 0;
    }

    /// <summary>
    /// Returns per-player contribution breakdown (sorted by player color slot) for UI indicator badges.
    /// </summary>
    public List<LoadoutContributorEntry> GetEquipmentContributors(string equipmentId)
    {
        var result = new List<LoadoutContributorEntry>();
        if (string.IsNullOrEmpty(equipmentId)) return result;

        bool networkActive = isMultiplayerClientSession ||
                             (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);

        if (!networkActive)
        {
            int soloCount = GetLoadoutCount(equipmentId);
            if (soloCount > 0)
            {
                result.Add(new LoadoutContributorEntry { clientId = 0UL, slotIndex = 0, count = soloCount });
            }
            return result;
        }

        if (multiplayerContributions.TryGetValue(equipmentId, out var map))
        {
            foreach (var kvp in map)
            {
                if (kvp.Value <= 0) continue;
                result.Add(new LoadoutContributorEntry
                {
                    clientId = kvp.Key,
                    slotIndex = GetPlayerSlotIndex(kvp.Key),
                    count = kvp.Value
                });
            }
            result.Sort((a, b) => a.slotIndex.CompareTo(b.slotIndex));
        }

        return result;
    }

    public int GetLoadoutCountByType(Type componentType)
    {
        if (componentType == null) return 0;
        foreach (var entry in Catalog)
        {
            if (entry.componentType == componentType || componentType.IsSubclassOf(entry.componentType))
            {
                return GetLoadoutCount(entry.id);
            }
        }
        return 0;
    }

    public int GetTotalBroughtItemsCount()
    {
        int total = 0;
        foreach (var kvp in loadoutCounts)
        {
            total += kvp.Value;
        }
        return total;
    }

    public int GetTotalLoadoutValue()
    {
        int total = 0;
        foreach (var entry in Catalog)
        {
            total += GetLoadoutCount(entry.id) * entry.cost;
        }
        return total;
    }

    public bool TryBuyEquipment(string equipmentId)
    {
        var entry = Catalog.Find(e => e.id == equipmentId);
        if (entry == null) return false;

        int current = GetLoadoutCount(equipmentId);
        if (current >= entry.maxCount) return false;
        if (GetAvailableCurrency() < entry.cost) return false;

        if (IsConnectedMultiplayerClient)
        {
            if (NetworkController.Instance == null) return false;
            pendingPurchaseCost += entry.cost;
            OnCurrencyOrLoadoutChanged?.Invoke();
            NetworkController.Instance.SendLobbyLoadoutRequestToServer(1, equipmentId);
            return true;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
        {
            if (!ServerProcessBuyRequest(LocalNetworkClientId, equipmentId)) return false;
            Currency -= entry.cost;
            SaveStateToPrefs();
            NetworkController.Instance?.BroadcastLobbyLoadoutSync();
            OnCurrencyOrLoadoutChanged?.Invoke();
            return true;
        }

        Currency -= entry.cost;
        loadoutCounts[equipmentId] = current + 1;
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
        return true;
    }

    public bool TryRemoveEquipmentAndRefund(string equipmentId)
    {
        var entry = Catalog.Find(e => e.id == equipmentId);
        if (entry == null) return false;

        int removableOwn = GetOwnContributionCount(equipmentId) - GetPendingRemoveCount(equipmentId);
        if (removableOwn <= 0) return false;

        if (IsConnectedMultiplayerClient)
        {
            if (NetworkController.Instance == null) return false;
            pendingRemoveCounts[equipmentId] = GetPendingRemoveCount(equipmentId) + 1;
            OnCurrencyOrLoadoutChanged?.Invoke();
            NetworkController.Instance.SendLobbyLoadoutRequestToServer(2, equipmentId);
            return true;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
        {
            if (!ServerProcessRemoveRequest(LocalNetworkClientId, equipmentId)) return false;
            Currency += entry.cost;
            SaveStateToPrefs();
            NetworkController.Instance?.BroadcastLobbyLoadoutSync();
            OnCurrencyOrLoadoutChanged?.Invoke();
            return true;
        }

        Currency += entry.cost;
        loadoutCounts[equipmentId] = Mathf.Max(0, GetLoadoutCount(equipmentId) - 1);
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
        return true;
    }

    #region Multiplayer Shared Lobby Loadout Sync

    /// <summary>
    /// Seeds the server's authoritative Van loadout ledger from the host's persisted loadout.
    /// </summary>
    public void InitializeServerLobbyLedger()
    {
        isMultiplayerClientSession = false;
        pendingPurchaseCost = 0;
        pendingRemoveCounts.Clear();
        lobbyPlayerSlots.Clear();
        multiplayerContributions.Clear();

        ulong hostId = NetworkManager.ServerClientId;
        EnsurePlayerSlot(hostId);

        foreach (var entry in Catalog)
        {
            var contrib = new Dictionary<ulong, int>();
            int count = Mathf.Clamp(GetLoadoutCount(entry.id), 0, entry.maxCount);
            if (count > 0)
            {
                contrib[hostId] = count;
            }
            multiplayerContributions[entry.id] = contrib;
        }

        RecalculateTotalCountsFromContributions();
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void ServerOnClientConnected(ulong clientId)
    {
        if (!IsNetworkHostOrOffline) return;
        EnsurePlayerSlot(clientId);
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void ServerOnClientDisconnected(ulong clientId)
    {
        if (!IsNetworkHostOrOffline) return;
        if (clientId == NetworkManager.ServerClientId) return;

        for (int i = lobbyPlayerSlots.Count - 1; i >= 0; i--)
        {
            if (lobbyPlayerSlots[i].clientId == clientId)
            {
                lobbyPlayerSlots.RemoveAt(i);
            }
        }

        if (IsInLobbyScene)
        {
            foreach (var entry in Catalog)
            {
                if (multiplayerContributions.TryGetValue(entry.id, out var map))
                {
                    map.Remove(clientId);
                }
            }
            RecalculateTotalCountsFromContributions();
        }

        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public bool ServerProcessBuyRequest(ulong clientId, string equipmentId)
    {
        var entry = Catalog.Find(e => e.id == equipmentId);
        if (entry == null) return false;

        EnsurePlayerSlot(clientId);
        int currentTotal = GetLoadoutCount(equipmentId);
        if (currentTotal >= entry.maxCount) return false;

        if (!multiplayerContributions.TryGetValue(equipmentId, out var map))
        {
            map = new Dictionary<ulong, int>();
            multiplayerContributions[equipmentId] = map;
        }

        map[clientId] = (map.TryGetValue(clientId, out int curOwn) ? curOwn : 0) + 1;
        RecalculateTotalCountsFromContributions();
        OnCurrencyOrLoadoutChanged?.Invoke();
        return true;
    }

    public bool ServerProcessRemoveRequest(ulong clientId, string equipmentId)
    {
        var entry = Catalog.Find(e => e.id == equipmentId);
        if (entry == null) return false;

        if (!multiplayerContributions.TryGetValue(equipmentId, out var map) ||
            !map.TryGetValue(clientId, out int curOwn) ||
            curOwn <= 0)
        {
            return false;
        }

        if (curOwn <= 1) map.Remove(clientId);
        else map[clientId] = curOwn - 1;

        RecalculateTotalCountsFromContributions();
        OnCurrencyOrLoadoutChanged?.Invoke();
        return true;
    }

    public void OnJoinedMultiplayerLobbyAsClient()
    {
        isMultiplayerClientSession = true;
        pendingPurchaseCost = 0;
        pendingRemoveCounts.Clear();
        lobbyPlayerSlots.Clear();
        multiplayerContributions.Clear();
        foreach (var entry in Catalog)
        {
            loadoutCounts[entry.id] = 0;
            multiplayerContributions[entry.id] = new Dictionary<ulong, int>();
        }
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void OnLeftMultiplayerLobbyAsClient()
    {
        if (!isMultiplayerClientSession) return;

        // If the client exits before a match starts, refund what they contributed in this lobby.
        if (IsInLobbyScene)
        {
            int refund = 0;
            foreach (var entry in Catalog)
            {
                int own = GetOwnContributionCount(entry.id);
                if (own > 0) refund += own * entry.cost;
            }
            if (refund > 0)
            {
                Currency += refund;
                SaveCurrencyOnlyToPrefs();
            }
        }

        isMultiplayerClientSession = false;
        pendingPurchaseCost = 0;
        pendingReliefFlashlightAfterMatch = false;
        hasConsumedCurrentMatchLoadout = false;
        pendingRemoveCounts.Clear();
        lobbyPlayerSlots.Clear();
        multiplayerContributions.Clear();
        LoadStateFromPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
        OnContractSettingsChanged?.Invoke();
    }

    public void HandleClientPurchaseAck(byte op, string equipmentId, bool approved)
    {
        var entry = Catalog.Find(e => e.id == equipmentId);
        if (entry != null)
        {
            if (op == 1)
            {
                pendingPurchaseCost = Mathf.Max(0, pendingPurchaseCost - entry.cost);
                if (approved)
                {
                    Currency = Mathf.Max(0, Currency - entry.cost);
                    SaveCurrencyOnlyToPrefs();
                }
            }
            else if (op == 2)
            {
                int pending = GetPendingRemoveCount(equipmentId);
                if (pending > 0) pendingRemoveCounts[equipmentId] = pending - 1;
                if (approved)
                {
                    Currency += entry.cost;
                    SaveCurrencyOnlyToPrefs();
                }
            }
        }
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void WriteSyncSnapshot(FastBufferWriter writer)
    {
        writer.WriteValueSafe((byte)SelectedDifficulty);
        writer.WriteValueSafe(SelectedMapIndex);

        ushort slotCount = (ushort)Mathf.Min(lobbyPlayerSlots.Count, ushort.MaxValue);
        writer.WriteValueSafe(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            writer.WriteValueSafe(lobbyPlayerSlots[i].clientId);
            writer.WriteValueSafe((ushort)lobbyPlayerSlots[i].slotIndex);
        }

        byte catCount = (byte)Mathf.Min(Catalog.Count, byte.MaxValue);
        writer.WriteValueSafe(catCount);
        for (int c = 0; c < catCount; c++)
        {
            string eqId = Catalog[c].id;
            if (multiplayerContributions.TryGetValue(eqId, out var map))
            {
                int nonZero = 0;
                foreach (var kvp in map) if (kvp.Value > 0) nonZero++;
                byte n = (byte)Mathf.Min(nonZero, byte.MaxValue);
                writer.WriteValueSafe(n);
                int written = 0;
                foreach (var kvp in map)
                {
                    if (kvp.Value <= 0 || written >= n) continue;
                    writer.WriteValueSafe(kvp.Key);
                    writer.WriteValueSafe((ushort)kvp.Value);
                    written++;
                }
            }
            else
            {
                writer.WriteValueSafe((byte)0);
            }
        }
    }

    public void ApplySyncSnapshot(FastBufferReader reader)
    {
        isMultiplayerClientSession = !IsNetworkHostOrOffline;

        reader.ReadValueSafe(out byte diffByte);
        reader.ReadValueSafe(out int mapIndex);

        ContractDifficulty syncedDiff = (ContractDifficulty)Mathf.Clamp(diffByte, 0, 3);
        int syncedMap = Mathf.Clamp(mapIndex, 0, Mathf.Max(0, AvailableMaps.Length - 1));
        bool contractChanged = SelectedDifficulty != syncedDiff || SelectedMapIndex != syncedMap;
        SelectedDifficulty = syncedDiff;
        SelectedMapIndex = syncedMap;

        lobbyPlayerSlots.Clear();
        reader.ReadValueSafe(out ushort slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            reader.ReadValueSafe(out ulong cid);
            reader.ReadValueSafe(out ushort slotIdx);
            lobbyPlayerSlots.Add(new LobbyPlayerSlotEntry { clientId = cid, slotIndex = slotIdx });
        }

        multiplayerContributions.Clear();
        for (int i = 0; i < Catalog.Count; i++)
        {
            multiplayerContributions[Catalog[i].id] = new Dictionary<ulong, int>();
        }

        reader.ReadValueSafe(out byte catCount);
        for (int c = 0; c < catCount; c++)
        {
            reader.ReadValueSafe(out byte contributorCount);
            if (c < Catalog.Count)
            {
                var map = multiplayerContributions[Catalog[c].id];
                for (int j = 0; j < contributorCount; j++)
                {
                    reader.ReadValueSafe(out ulong cid);
                    reader.ReadValueSafe(out ushort cnt);
                    if (cnt > 0) map[cid] = cnt;
                }
            }
            else
            {
                for (int j = 0; j < contributorCount; j++)
                {
                    reader.ReadValueSafe(out ulong _);
                    reader.ReadValueSafe(out ushort _);
                }
            }
        }

        RecalculateTotalCountsFromContributions();
        if (contractChanged) OnContractSettingsChanged?.Invoke();
        OnCurrencyOrLoadoutChanged?.Invoke();

        if (IsInLobbyScene && ConsumePendingReliefFlashlightIfBroke())
        {
            NetworkController.Instance?.SendLobbyLoadoutRequestToServer(3, "Flashlight");
        }
    }

    private void EnsurePlayerSlot(ulong clientId)
    {
        for (int i = 0; i < lobbyPlayerSlots.Count; i++)
        {
            if (lobbyPlayerSlots[i].clientId == clientId) return;
        }

        if (clientId == NetworkManager.ServerClientId)
        {
            lobbyPlayerSlots.Insert(0, new LobbyPlayerSlotEntry { clientId = clientId, slotIndex = 0 });
            return;
        }

        int candidate = 1;
        bool used = true;
        while (used)
        {
            used = false;
            for (int i = 0; i < lobbyPlayerSlots.Count; i++)
            {
                if (lobbyPlayerSlots[i].slotIndex == candidate)
                {
                    used = true;
                    candidate++;
                    break;
                }
            }
        }

        lobbyPlayerSlots.Add(new LobbyPlayerSlotEntry { clientId = clientId, slotIndex = candidate });
    }

    private void RecalculateTotalCountsFromContributions()
    {
        for (int i = 0; i < Catalog.Count; i++)
        {
            var entry = Catalog[i];
            int sum = 0;
            if (multiplayerContributions.TryGetValue(entry.id, out var map))
            {
                foreach (var kvp in map)
                {
                    sum += kvp.Value;
                }
            }
            loadoutCounts[entry.id] = Mathf.Clamp(sum, 0, entry.maxCount);
        }
    }

    #endregion

    public void AddCurrency(int amount)
    {
        if (amount == 0) return;
        Currency = Mathf.Max(0, Currency + amount);
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void SetCurrency(int amount)
    {
        int clamped = Mathf.Max(0, amount);
        if (Currency == clamped) return;
        Currency = clamped;
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    /// <summary>
    /// Adjusts currency by delta in either Play Mode (live manager) or Edit Mode (PlayerPrefs directly).
    /// Returns the resulting clamped currency balance.
    /// </summary>
    public static int AdjustCurrencyPersisted(int delta)
    {
        var mgr = Instance ?? FindAnyObjectByType<ProgressionAndLoadoutManager>();
        if (mgr != null)
        {
            mgr.AddCurrency(delta);
            return mgr.Currency;
        }

        int current = PlayerPrefs.GetInt(PREF_CURRENCY_INIT, 0) == 0
            ? STARTER_CURRENCY
            : Mathf.Max(0, PlayerPrefs.GetInt(PREF_CURRENCY, STARTER_CURRENCY));
        return SetCurrencyPersisted(current + delta);
    }

    /// <summary>
    /// Sets currency in either Play Mode (live manager) or Edit Mode (PlayerPrefs directly).
    /// Returns the resulting clamped currency balance.
    /// </summary>
    public static int SetCurrencyPersisted(int amount)
    {
        int clamped = Mathf.Max(0, amount);
        var mgr = Instance ?? FindAnyObjectByType<ProgressionAndLoadoutManager>();
        if (mgr != null)
        {
            mgr.SetCurrency(clamped);
            return mgr.Currency;
        }

        if (PlayerPrefs.GetInt(PREF_CURRENCY_INIT, 0) == 0)
        {
            // Seed starter Flashlight so first Play Mode run keeps default starter gear.
            PlayerPrefs.SetInt(PREF_LOADOUT_PREFIX + "Flashlight", 1);
            PlayerPrefs.SetInt(PREF_CURRENCY_INIT, 1);
        }

        PlayerPrefs.SetInt(PREF_CURRENCY, clamped);
        PlayerPrefs.Save();
        return clamped;
    }

    private bool pendingReliefFlashlightAfterMatch = false;
    private bool hasConsumedCurrentMatchLoadout = false;

    public void MarkMatchLoadoutActive()
    {
        hasConsumedCurrentMatchLoadout = false;
        pendingReliefFlashlightAfterMatch = false;
    }

    public void EnsureMatchLoadoutConsumedOnExit()
    {
        if (!hasConsumedCurrentMatchLoadout)
        {
            ConsumeLoadoutAfterCompletedMatch();
        }
    }

    public bool ConsumePendingReliefFlashlightIfBroke()
    {
        if (!pendingReliefFlashlightAfterMatch) return false;
        pendingReliefFlashlightAfterMatch = false;
        return Currency < 20 && GetOwnContributionCount("Flashlight") == 0;
    }

    public bool ServerProcessReliefFlashlightRequest(ulong clientId)
    {
        var entry = Catalog.Find(e => e.id == "Flashlight");
        if (entry == null) return false;

        EnsurePlayerSlot(clientId);
        if (GetLoadoutCount("Flashlight") >= entry.maxCount) return false;

        if (!multiplayerContributions.TryGetValue("Flashlight", out var map))
        {
            map = new Dictionary<ulong, int>();
            multiplayerContributions["Flashlight"] = map;
        }

        if (map.TryGetValue(clientId, out int own) && own > 0) return false;

        map[clientId] = 1;
        RecalculateTotalCountsFromContributions();
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Clears the consumed van loadout at the end of a completed match so players purchase fresh gear
    /// for the next investigation using their earned payout. Ensures broke players still get a free $0-relief Flashlight.
    /// </summary>
    public void ConsumeLoadoutAfterCompletedMatch()
    {
        hasConsumedCurrentMatchLoadout = true;
        pendingPurchaseCost = 0;
        pendingRemoveCounts.Clear();

        foreach (var entry in Catalog)
        {
            loadoutCounts[entry.id] = 0;
            if (multiplayerContributions.TryGetValue(entry.id, out var map))
            {
                map.Clear();
            }
        }

        // Provide a free $0-relief Flashlight if a player ends broke (< $20)
        if (Currency < 20)
        {
            if (IsNetworkHostOrOffline)
            {
                loadoutCounts["Flashlight"] = 1;
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                {
                    if (!multiplayerContributions.ContainsKey("Flashlight"))
                    {
                        multiplayerContributions["Flashlight"] = new Dictionary<ulong, int>();
                    }
                    multiplayerContributions["Flashlight"][NetworkManager.ServerClientId] = 1;
                }
            }
            else if (isMultiplayerClientSession || IsConnectedMultiplayerClient)
            {
                pendingReliefFlashlightAfterMatch = true;
            }
        }

        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void SetDifficulty(ContractDifficulty difficulty)
    {
        if (!IsNetworkHostOrOffline) return;
        SelectedDifficulty = difficulty;
        SaveStateToPrefs();
        NetworkController.Instance?.BroadcastLobbyLoadoutSync();
        OnContractSettingsChanged?.Invoke();
    }

    public void SetMapIndex(int mapIndex)
    {
        if (!IsNetworkHostOrOffline) return;
        int len = Mathf.Max(1, AvailableMaps.Length);
        SelectedMapIndex = ((mapIndex % len) + len) % len;
        SaveStateToPrefs();
        NetworkController.Instance?.BroadcastLobbyLoadoutSync();
        OnContractSettingsChanged?.Invoke();
    }

    public string GetSelectedMapDisplayName()
    {
        return AvailableMaps[Mathf.Clamp(SelectedMapIndex, 0, AvailableMaps.Length - 1)];
    }

    public string GetSelectedSceneName()
    {
        return SelectedMapIndex switch
        {
            1 => CEMETERY_MAP_SCENE_NAME,
            2 => SCHOOL_MAP_SCENE_NAME,
            _ => BARANGAY_MAP_SCENE_NAME
        };
    }

    public static bool IsGameplayMapScene(string sceneName)
    {
        return sceneName == BARANGAY_MAP_SCENE_NAME
            || sceneName == CEMETERY_MAP_SCENE_NAME
            || sceneName == SCHOOL_MAP_SCENE_NAME;
    }

    public static float GetDifficultyRewardMultiplier(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 1.0f;
            case ContractDifficulty.Intermediate: return 2.0f;
            case ContractDifficulty.Professional: return 3.0f;
            case ContractDifficulty.Nightmare: return 4.0f;
            default: return 1.0f;
        }
    }

    public static float GetDifficultySetupTimeSeconds(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 300f;
            case ContractDifficulty.Intermediate: return 120f;
            case ContractDifficulty.Professional:
            case ContractDifficulty.Nightmare:
                return 0f;
            default: return 300f;
        }
    }

    public static float GetDifficultyHuntGracePeriodSeconds(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 5f;
            case ContractDifficulty.Intermediate: return 3f;
            case ContractDifficulty.Professional: return 1f;
            case ContractDifficulty.Nightmare: return 0f;
            default: return 5f;
        }
    }

    public static float GetFailedExorcismPauseSeconds(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 8f;
            case ContractDifficulty.Intermediate: return 5f;
            case ContractDifficulty.Professional: return 3f;
            case ContractDifficulty.Nightmare: return 1f;
            default: return 8f;
        }
    }

    public static float GetDifficultySanityDrainMultiplier(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 1.0f;
            case ContractDifficulty.Intermediate: return 1.5f;
            case ContractDifficulty.Professional: return 2.0f;
            case ContractDifficulty.Nightmare: return 2.0f;
            default: return 1.0f;
        }
    }

    public static float GetDifficultySanityPillRestore(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 40f;
            case ContractDifficulty.Intermediate: return 35f;
            case ContractDifficulty.Professional: return 20f;
            case ContractDifficulty.Nightmare: return 15f;
            default: return 40f;
        }
    }

    public static bool GetDifficultyFuseBoxStartsOn(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return true;
            case ContractDifficulty.Intermediate: return false;
            case ContractDifficulty.Professional: return true;
            case ContractDifficulty.Nightmare: return false;
            default: return true;
        }
    }

    public static float GetDifficultyInsuranceRate(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 0.5f;
            case ContractDifficulty.Intermediate: return 0.25f;
            case ContractDifficulty.Professional:
            case ContractDifficulty.Nightmare:
                return 0f;
            default: return 0.5f;
        }
    }

    public static float GetDifficultyEventIntervalMultiplier(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 0.85f;
            case ContractDifficulty.Intermediate: return 1.15f;
            case ContractDifficulty.Professional: return 1.45f;
            case ContractDifficulty.Nightmare: return 1.75f;
            default: return 1.0f;
        }
    }

    public static string GetDifficultyDescription(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur:
                return "1.0x Payout | 5m setup, fuse box ON, slow sanity drain, 40% pills, 50% insurance.";
            case ContractDifficulty.Intermediate:
                return "2.0x Payout | 2m setup, fuse box OFF, 1.5x sanity drain, 35% pills, 25% insurance.";
            case ContractDifficulty.Professional:
                return "3.0x Payout | 0m setup, fuse box ON, 2.0x sanity drain, 20% pills, no insurance.";
            case ContractDifficulty.Nightmare:
                return "4.0x Payout | 0m setup, fuse box OFF, 2.0x sanity drain, 15% pills, relentless ghost.";
            default:
                return "1.0x Payout";
        }
    }

    public int CalculateCurrentLoadoutValue()
    {
        int total = 0;
        foreach (var entry in Catalog)
        {
            int count = loadoutCounts.TryGetValue(entry.id, out int c) ? c : 0;
            if (count > 0 && entry.cost > 0)
            {
                total += count * entry.cost;
            }
        }
        return total;
    }

    /// <summary>
    /// Calculates and awards the end-of-match payout based on:
    /// 1. Correct Evidences checked in the Journal
    /// 2. Getting the right Ghost Type in the Journal
    /// 3. Extra bonus for defeating (exorcising) the ghost
    /// 4. Selected Difficulty Multiplier
    /// </summary>
    public MatchPayoutBreakdown CalculateAndAwardMatchPayout(
        int correctEvidenceCount,
        bool correctGhostType,
        bool ghostDefeated,
        bool isDeceased = false)
    {
        int clampedEvidences = Mathf.Clamp(correctEvidenceCount, 0, 3);
        int evidenceReward = clampedEvidences * REWARD_PER_CORRECT_EVIDENCE;
        int ghostTypeReward = correctGhostType ? REWARD_CORRECT_GHOST_TYPE : 0;
        int defeatReward = ghostDefeated ? REWARD_DEFEAT_GHOST : 0;

        int baseReward = evidenceReward + ghostTypeReward + defeatReward;
        float multiplier = GetDifficultyRewardMultiplier(SelectedDifficulty);
        float survivalMult = isDeceased ? DECEASED_PAYOUT_MULTIPLIER : 1.0f;
        int totalEarned = Mathf.RoundToInt(baseReward * multiplier * survivalMult);

        int insuranceRefund = 0;
        if (isDeceased)
        {
            float insuranceRate = GetDifficultyInsuranceRate(SelectedDifficulty);
            if (insuranceRate > 0f)
            {
                insuranceRefund = Mathf.RoundToInt(CalculateCurrentLoadoutValue() * insuranceRate);
            }
        }

        // Credit the payout and insurance BEFORE consuming/resetting loadout so post-match broke relief checks the updated balance
        AddCurrency(totalEarned + insuranceRefund);
        ConsumeLoadoutAfterCompletedMatch();

        return new MatchPayoutBreakdown
        {
            correctEvidenceCount = clampedEvidences,
            evidenceReward = evidenceReward,
            correctGhostType = correctGhostType,
            ghostTypeReward = ghostTypeReward,
            ghostDefeated = ghostDefeated,
            ghostDefeatedReward = defeatReward,
            difficulty = SelectedDifficulty,
            difficultyMultiplier = multiplier,
            isDeceased = isDeceased,
            survivalMultiplier = survivalMult,
            insuranceRefund = insuranceRefund,
            totalEarned = totalEarned,
            newBalance = Currency
        };
    }
}

