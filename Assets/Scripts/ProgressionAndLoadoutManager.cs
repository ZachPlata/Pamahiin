using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
    public int totalEarned;
    public int newBalance;
}

/// <summary>
/// Manages persistent in-game currency, lobby mission configuration (Map, Difficulty, Party Code),
/// van ghost equipment loadout selection, and end-of-match currency payouts.
/// </summary>
public class ProgressionAndLoadoutManager : MonoBehaviour
{
    public static ProgressionAndLoadoutManager Instance { get; private set; }

    public const string LOBBY_SCENE_NAME = "LobbyScene";
    public const string GAME_MAP_SCENE_NAME = "GameMapScene";
    public const string MAIN_MENU_SCENE_NAME = "MainMenuScene";

    private const string PREF_CURRENCY_INIT = "Pamahiin_Currency_Initialized_v1";
    private const string PREF_CURRENCY = "Pamahiin_Currency_v1";
    private const string PREF_DIFFICULTY = "Pamahiin_Difficulty_v1";
    private const string PREF_MAP_INDEX = "Pamahiin_MapIndex_v1";
    private const string PREF_LOADOUT_PREFIX = "Pamahiin_Loadout_v1_";

    public const int STARTER_CURRENCY = 250;
    public const int REWARD_PER_CORRECT_EVIDENCE = 35;
    public const int REWARD_CORRECT_GHOST_TYPE = 100;
    public const int REWARD_DEFEAT_GHOST = 150;

    public event Action OnCurrencyOrLoadoutChanged;
    public event Action OnContractSettingsChanged;

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
        "Abandoned House (Baryo San Nicolas)"
    };

    private readonly Dictionary<string, int> loadoutCounts = new Dictionary<string, int>();

    public int Currency { get; private set; }
    public ContractDifficulty SelectedDifficulty { get; private set; } = ContractDifficulty.Amateur;
    public int SelectedMapIndex { get; private set; } = 0;

    public static bool IsInLobbyScene =>
        SceneManager.GetActiveScene().name == LOBBY_SCENE_NAME;

    public static void EnsureInstance()
    {
        if (Instance != null) return;
        var existing = FindAnyObjectByType<ProgressionAndLoadoutManager>();
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        var go = new GameObject("ProgressionAndLoadoutManager");
        Instance = go.AddComponent<ProgressionAndLoadoutManager>();
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
            Currency = STARTER_CURRENCY;
            PlayerPrefs.SetInt(PREF_CURRENCY, Currency);
            PlayerPrefs.SetInt(PREF_CURRENCY_INIT, 1);

            // Give 1 free starter Flashlight in initial loadout so brand-new players immediately have light
            PlayerPrefs.SetInt(PREF_LOADOUT_PREFIX + "Flashlight", 1);
            PlayerPrefs.Save();
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

    private void SaveStateToPrefs()
    {
        PlayerPrefs.SetInt(PREF_CURRENCY, Currency);
        PlayerPrefs.SetInt(PREF_DIFFICULTY, (int)SelectedDifficulty);
        PlayerPrefs.SetInt(PREF_MAP_INDEX, SelectedMapIndex);

        foreach (var entry in Catalog)
        {
            int count = GetLoadoutCount(entry.id);
            PlayerPrefs.SetInt(PREF_LOADOUT_PREFIX + entry.id, count);
        }

        PlayerPrefs.Save();
    }

    public int GetLoadoutCount(string equipmentId)
    {
        if (string.IsNullOrEmpty(equipmentId)) return 0;
        return loadoutCounts.TryGetValue(equipmentId, out int count) ? count : 0;
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
        if (Currency < entry.cost) return false;

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

        int current = GetLoadoutCount(equipmentId);
        if (current <= 0) return false;

        Currency += entry.cost;
        loadoutCounts[equipmentId] = current - 1;
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
        return true;
    }

    public void AddCurrency(int amount)
    {
        if (amount == 0) return;
        Currency = Mathf.Max(0, Currency + amount);
        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    /// <summary>
    /// Clears the consumed van loadout at the end of a completed match so players purchase fresh gear
    /// for the next investigation using their earned payout. Ensures broke players still get a free Flashlight.
    /// </summary>
    public void ConsumeLoadoutAfterCompletedMatch()
    {
        foreach (var entry in Catalog)
        {
            loadoutCounts[entry.id] = 0;
        }

        // If player has less than $20 and 0 items, provide a free relief Flashlight so they are never stuck
        if (Currency < 20)
        {
            loadoutCounts["Flashlight"] = 1;
        }

        SaveStateToPrefs();
        OnCurrencyOrLoadoutChanged?.Invoke();
    }

    public void SetDifficulty(ContractDifficulty difficulty)
    {
        SelectedDifficulty = difficulty;
        SaveStateToPrefs();
        OnContractSettingsChanged?.Invoke();
    }

    public void SetMapIndex(int mapIndex)
    {
        SelectedMapIndex = Mathf.Clamp(mapIndex, 0, AvailableMaps.Length - 1);
        SaveStateToPrefs();
        OnContractSettingsChanged?.Invoke();
    }

    public string GetSelectedMapDisplayName()
    {
        return AvailableMaps[Mathf.Clamp(SelectedMapIndex, 0, AvailableMaps.Length - 1)];
    }

    public string GetSelectedSceneName()
    {
        return GAME_MAP_SCENE_NAME;
    }

    public static float GetDifficultyRewardMultiplier(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur: return 1.0f;
            case ContractDifficulty.Intermediate: return 1.5f;
            case ContractDifficulty.Professional: return 2.0f;
            case ContractDifficulty.Nightmare: return 3.0f;
            default: return 1.0f;
        }
    }

    public static string GetDifficultyDescription(ContractDifficulty difficulty)
    {
        switch (difficulty)
        {
            case ContractDifficulty.Amateur:
                return "1.0x Payout | Standard sanity drain & shorter hunts.";
            case ContractDifficulty.Intermediate:
                return "1.5x Payout | 1.25x sanity drain & longer hunts.";
            case ContractDifficulty.Professional:
                return "2.0x Payout | 1.5x sanity drain & aggressive hunts.";
            case ContractDifficulty.Nightmare:
                return "3.0x Payout | 2.0x sanity drain & relentless hunts.";
            default:
                return "1.0x Payout";
        }
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
        bool ghostDefeated)
    {
        int clampedEvidences = Mathf.Clamp(correctEvidenceCount, 0, 3);
        int evidenceReward = clampedEvidences * REWARD_PER_CORRECT_EVIDENCE;
        int ghostTypeReward = correctGhostType ? REWARD_CORRECT_GHOST_TYPE : 0;
        int defeatReward = ghostDefeated ? REWARD_DEFEAT_GHOST : 0;

        int baseReward = evidenceReward + ghostTypeReward + defeatReward;
        float multiplier = GetDifficultyRewardMultiplier(SelectedDifficulty);
        int totalEarned = Mathf.RoundToInt(baseReward * multiplier);

        // Consume the brought equipment for this completed contract and credit the payout
        ConsumeLoadoutAfterCompletedMatch();
        AddCurrency(totalEarned);

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
            totalEarned = totalEarned,
            newBalance = Currency
        };
    }
}

