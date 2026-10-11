using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Button-based developer console (press ~ or Esc to toggle).
/// Provides quick, one-click debug actions for ghost events,
/// player cheats, environmental controls, and diagnostics.
/// </summary>
public class DeveloperConsoleManager : MonoBehaviour
{
    public static DeveloperConsoleManager Instance { get; private set; }
    public static bool IsConsoleOpen => Instance != null && Instance.isConsoleOpen;
    public static bool WasOpenThisFrame => IsConsoleOpen || (Instance != null && Instance.closedFrame == Time.frameCount);

    [SerializeField] private UIDocument consoleDocument;

    private VisualElement container;
    private Label statusLabel;
    private bool isConsoleOpen = false;
    private int closedFrame = -1;

    // Cheats state tracking
    private bool isSpeedBoosted = false;
    private bool isSlowMo = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (consoleDocument == null || consoleDocument.rootVisualElement == null) return;

        consoleDocument.sortingOrder = 100;
        var root = consoleDocument.rootVisualElement;

        container = root.Q<VisualElement>("DevConsoleContainer");
        statusLabel = root.Q<Label>("DevStatusLabel");

        // Close button
        var closeBtn = root.Q<Button>("CloseConsoleBtn");
        if (closeBtn != null) closeBtn.clicked += ToggleConsole;

        // ── Column 1: Hunt & Ghost AI ──
        BindBtn(root, "ForceHuntBtn", OnForceHunt);
        BindBtn(root, "StopHuntBtn", OnStopHunt);
        BindBtn(root, "ToggleGhostOutlineBtn", OnToggleGhostOutline);
        BindBtn(root, "TpGhostToPlayerBtn", OnTpGhostToPlayer);
        BindBtn(root, "TpPlayerToGhostBtn", OnTpPlayerToGhost);
        BindBtn(root, "ToggleBreadcrumbBtn", OnToggleBreadcrumbs);

        // ── Column 2: Ghost Manifestations ──
        BindBtn(root, "ForceEventBtn", OnForceGhostEvent);
        BindBtn(root, "ForceThrowBtn", () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.ThrowObject));
        BindBtn(root, "ForceWriteBtn", () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.WriteInBook));
        BindBtn(root, "ForceDotsBtn", () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.DotsManifestation));
        BindBtn(root, "ForceOrbBtn", () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.GhostOrbsManifestation));
        BindBtn(root, "ForceSpiritBoxBtn", () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.SpiritBoxTalk));
        BindBtn(root, "ForceLowSanityBtn", () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.LowSanityManifestation));

        // ── Column 3: Player Cheats ──
        BindBtn(root, "ToggleGodModeBtn", OnToggleGodMode);
        BindBtn(root, "RestoreSanityBtn", OnRestoreSanity);
        BindBtn(root, "DrainSanityBtn", OnDrainSanity);
        BindBtn(root, "RevivePlayerBtn", OnRevivePlayer);
        BindBtn(root, "ToggleSpeedBoostBtn", OnToggleSpeedBoost);
        BindBtn(root, "ForceEMF5Btn", OnForceEMF5);

        // ── Column 4: House & System ──
        BindBtn(root, "TurnAllLightsOnBtn", OnTurnAllLightsOn);
        BindBtn(root, "TurnAllLightsOffBtn", OnTurnAllLightsOff);
        BindBtn(root, "TripFuseBoxBtn", OnTripFuseBox);
        BindBtn(root, "RestoreFuseBoxBtn", OnRestoreFuseBox);
        BindBtn(root, "ToggleFpsBtn", OnToggleFps);
        BindBtn(root, "ToggleTimescaleBtn", OnToggleTimescale);
    }

    private void BindBtn(VisualElement root, string name, Action action)
    {
        var btn = root.Q<Button>(name);
        if (btn != null)
        {
            btn.clicked += action;
        }
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        // Press ~ or ` to toggle; press Esc to close if open
        if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Tilde) || (isConsoleOpen && Input.GetKeyDown(KeyCode.Escape)))
        {
            ToggleConsole();
        }
    }

    public void ToggleConsole()
    {
        if (container == null) return;

        isConsoleOpen = !isConsoleOpen;
        container.style.display = isConsoleOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (isConsoleOpen)
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            SetStatus("Console opened.");
        }
        else
        {
            closedFrame = Time.frameCount;
        }
    }

    private void SetStatus(string msg)
    {
        if (statusLabel != null)
        {
            statusLabel.text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        }
        Debug.Log($"[DevConsole] {msg}");
    }

    // ── Hunt & Ghost AI ────────────────────────────────────────────────────

    private void OnForceHunt()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            ghost.StartHunt();
            SetStatus("Forced Hunt Started.");
        }
        else SetStatus("GhostHandler not found.");
    }

    private void OnStopHunt()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            if (!ghost.IsServer) { SetStatus("Only host can stop a hunt."); return; }
            ghost.EndHunt();
            SetStatus("Hunt Stopped.");
        }
        else SetStatus("GhostHandler not found.");
    }

    private void OnToggleGhostOutline()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            ghost.ToggleDevVisibility();
            SetStatus("Toggled Ghost Debug Visibility.");
        }
        else SetStatus("GhostHandler not found.");
    }

    private void OnTpGhostToPlayer()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        var player = GetLocalPlayer();
        if (ghost == null || player == null) { SetStatus("Ghost or Player not found."); return; }

        if (ghost.hunterGhost != null) ghost.hunterGhost.transform.position = player.transform.position;
        ghost.transform.position = player.transform.position;
        SetStatus($"Teleported Ghost to Player ({player.transform.position.x:F1}, {player.transform.position.y:F1}).");
    }

    private void OnTpPlayerToGhost()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        var player = GetLocalPlayer();
        if (ghost == null || player == null) { SetStatus("Ghost or Player not found."); return; }

        player.transform.position = ghost.transform.position;
        SetStatus($"Teleported Player to Ghost ({ghost.transform.position.x:F1}, {ghost.transform.position.y:F1}).");
    }

    private void OnToggleBreadcrumbs()
    {
        HunterGhostBehavior.ShowBreadcrumbs = !HunterGhostBehavior.ShowBreadcrumbs;
        SetStatus($"Breadcrumbs: {(HunterGhostBehavior.ShowBreadcrumbs ? "ON" : "OFF")}");
    }

    // ── Ghost Events ───────────────────────────────────────────────────────

    private void OnForceGhostEvent()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            ghost.TriggerGhostEvent();
            if (ParanormalManager.Instance != null)
            {
                ParanormalManager.Instance.RegisterEvent(ghost.transform.position, 3, 20f);
            }
            SetStatus("Forced Random Ghost Event.");
        }
        else SetStatus("GhostHandler not found.");
    }

    private void OnForceSpecificEvent(EventGhostBehavior.GhostEventType type)
    {
        var ghostHandler = FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null && ghostHandler.eventGhost != null)
        {
            ghostHandler.eventGhost.ForceEvent(type);
            SetStatus($"Forced Event: {type}");
        }
        else SetStatus("EventGhostBehavior not found.");
    }

    // ── Player Cheats ──────────────────────────────────────────────────────

    private void OnToggleGodMode()
    {
        var localPlayer = GetLocalPlayer();
        if (localPlayer != null)
        {
            if (localPlayer.IsServer)
            {
                localPlayer.isGodMode.Value = !localPlayer.isGodMode.Value;
                SetStatus($"God Mode: {(localPlayer.isGodMode.Value ? "ENABLED" : "DISABLED")}");
            }
            else
            {
                SetStatus("God Mode requires Server authority (Host only).");
            }
        }
        else SetStatus("Local player not found.");
    }

    private void OnRestoreSanity()
    {
        var player = GetLocalPlayer();
        if (player != null)
        {
            if (player.IsServer)
            {
                player.RestoreSanity(100f);
                SetStatus("Restored Sanity to 100%.");
            }
            else SetStatus("Sanity restoration is host only.");
        }
    }

    private void OnDrainSanity()
    {
        var player = GetLocalPlayer();
        if (player != null)
        {
            if (player.IsServer)
            {
                player.RestoreSanity(-player.Sanity);
                SetStatus("Drained Sanity to 0%.");
            }
            else SetStatus("Host only.");
        }
    }

    private void OnRevivePlayer()
    {
        var player = GetLocalPlayer();
        if (player != null)
        {
            if (player.IsServer)
            {
                bool revived = player.RevivePlayer();
                SetStatus(revived ? "Player revived!" : "Player is already alive.");
            }
            else SetStatus("Revive is host only.");
        }
    }

    private void OnToggleSpeedBoost()
    {
        var player = GetLocalPlayer();
        if (player != null)
        {
            isSpeedBoosted = !isSpeedBoosted;
            player.WalkSpeed = isSpeedBoosted ? 6f : 3f;
            player.SprintSpeed = isSpeedBoosted ? 11f : 5.5f;
            SetStatus($"Speed Boost: {(isSpeedBoosted ? "2x SPEED" : "NORMAL SPEED")}");
        }
    }

    private void OnForceEMF5()
    {
        var paranormal = ParanormalManager.Instance;
        var ghost = FindAnyObjectByType<GhostHandler>();

        if (paranormal != null && ghost != null)
        {
            paranormal.RegisterEvent(ghost.transform.position, 5, 20f);
            SetStatus("Forced EMF 5 at Ghost Location.");
        }
        else SetStatus("ParanormalManager or Ghost not found.");
    }

    // ── House & Environment ────────────────────────────────────────────────

    private void OnTurnAllLightsOn()
    {
        var switches = FindObjectsByType<HouseLightSwitch>();
        int count = 0;
        foreach (var sw in switches)
        {
            sw.ForceOn();
            count++;
        }
        SetStatus($"Turned ON {count} light switches.");
    }

    private void OnTurnAllLightsOff()
    {
        var switches = FindObjectsByType<HouseLightSwitch>();
        int count = 0;
        foreach (var sw in switches)
        {
            sw.ForceOff();
            count++;
        }
        SetStatus($"Turned OFF {count} light switches.");
    }

    private void OnTripFuseBox()
    {
        if (FuseBox.Instance != null)
        {
            FuseBox.Instance.GhostTrip();
            SetStatus("Fuse Box tripped (Power OFF).");
        }
        else SetStatus("FuseBox not found.");
    }

    private void OnRestoreFuseBox()
    {
        if (FuseBox.Instance != null)
        {
            FuseBox.Instance.Interact();
            SetStatus("Fuse Box restored (Power ON).");
        }
        else SetStatus("FuseBox not found.");
    }

    private void OnToggleFps()
    {
        if (GameSettingsManager.Instance != null)
        {
            bool nextState = !GameSettingsManager.Instance.ShowFPS;
            GameSettingsManager.Instance.SetShowFPS(nextState);
            SetStatus($"FPS Counter: {(nextState ? "ON" : "OFF")}");
        }
        else SetStatus("GameSettingsManager not found.");
    }

    private void OnToggleTimescale()
    {
        isSlowMo = !isSlowMo;
        Time.timeScale = isSlowMo ? 0.4f : 1.0f;
        SetStatus($"Simulation Speed: {(isSlowMo ? "0.4x (Slow-Mo)" : "1.0x (Normal)")}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static PlayerController GetLocalPlayer()
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null)
            return NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();

        foreach (var p in PlayerController.AllPlayers)
            if (p != null) return p;

        return null;
    }
}
