using UnityEngine;
using UnityEngine.UIElements;

public class DeveloperConsoleManager : MonoBehaviour
{
    [SerializeField] private UIDocument consoleDocument;
    
    private VisualElement container;
    private bool isConsoleOpen = false;

    private void OnEnable()
    {
        if (consoleDocument == null || consoleDocument.rootVisualElement == null) return;

        consoleDocument.sortingOrder = 100;
        var root = consoleDocument.rootVisualElement;
        container = root.Q<VisualElement>("DevConsoleContainer");

        var forceHuntBtn = root.Q<Button>("ForceHuntBtn");
        var stopHuntBtn = root.Q<Button>("StopHuntBtn");
        var forceEMF5Btn = root.Q<Button>("ForceEMF5Btn");
        var forceEventBtn = root.Q<Button>("ForceEventBtn");
        var toggleGhostOutlineBtn = root.Q<Button>("ToggleGhostOutlineBtn");
        
        var forceThrowBtn = root.Q<Button>("ForceThrowBtn");
        var forceWriteBtn = root.Q<Button>("ForceWriteBtn");
        var forceDotsBtn = root.Q<Button>("ForceDotsBtn");
        var forceOrbBtn = root.Q<Button>("ForceOrbBtn");
        var forceSpiritBoxBtn = root.Q<Button>("ForceSpiritBoxBtn");
        var forceLowSanityBtn = root.Q<Button>("ForceLowSanityBtn");
        var toggleGodModeBtn = root.Q<Button>("ToggleGodModeBtn");

        if (forceHuntBtn != null) forceHuntBtn.clicked += OnForceHunt;
        if (stopHuntBtn != null) stopHuntBtn.clicked += OnStopHunt;
        if (forceEMF5Btn != null) forceEMF5Btn.clicked += OnForceEMF5;
        if (forceEventBtn != null) forceEventBtn.clicked += OnForceGhostEvent;
        if (toggleGhostOutlineBtn != null) toggleGhostOutlineBtn.clicked += OnToggleGhostOutline;
        
        if (forceThrowBtn != null) forceThrowBtn.clicked += () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.ThrowObject);
        if (forceWriteBtn != null) forceWriteBtn.clicked += () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.WriteInBook);
        if (forceDotsBtn != null) forceDotsBtn.clicked += () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.DotsManifestation);
        if (forceOrbBtn != null) forceOrbBtn.clicked += () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.GhostOrbsManifestation);
        if (forceSpiritBoxBtn != null) forceSpiritBoxBtn.clicked += () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.SpiritBoxTalk);
        if (forceLowSanityBtn != null) forceLowSanityBtn.clicked += () => OnForceSpecificEvent(EventGhostBehavior.GhostEventType.LowSanityManifestation);
        
        if (toggleGodModeBtn != null) toggleGodModeBtn.clicked += OnToggleGodMode;

        var toggleBreadcrumbBtn = root.Q<Button>("ToggleBreadcrumbBtn");
        if (toggleBreadcrumbBtn != null) toggleBreadcrumbBtn.clicked += OnToggleBreadcrumbs;
    }

    private void OnToggleBreadcrumbs()
    {
        HunterGhostBehavior.ShowBreadcrumbs = !HunterGhostBehavior.ShowBreadcrumbs;
        Debug.Log($"[DevConsole] Breadcrumbs Visibility: {HunterGhostBehavior.ShowBreadcrumbs}");
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Tilde))
        {
            ToggleConsole();
        }
    }

    private void OnToggleGodMode()
    {
        var localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
        if (localPlayer != null)
        {
            // Normally needs a ServerRpc, but since it's dev console we might just assume Server authority for now
            // Or if it requires a ServerRpc, we should add one. But let's check if the variable is writeable.
            // I set it to WritePermission.Server, so we need Server authority. The host can use this.
            if (localPlayer.IsServer)
            {
                localPlayer.isGodMode.Value = !localPlayer.isGodMode.Value;
                Debug.Log($"[DevConsole] God Mode Toggled: {localPlayer.isGodMode.Value}");
            }
            else
            {
                Debug.LogWarning("[DevConsole] God Mode can only be toggled by the host!");
            }
        }
    }

    private void ToggleConsole()
    {
        if (container == null) return;
        
        isConsoleOpen = !isConsoleOpen;
        container.style.display = isConsoleOpen ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnForceSpecificEvent(EventGhostBehavior.GhostEventType type)
    {
        var ghostHandler = FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null && ghostHandler.eventGhost != null)
        {
            ghostHandler.eventGhost.ForceEvent(type);
            Debug.Log($"[DevConsole] Forced Specific Ghost Event: {type}");
        }
    }

    private void OnForceHunt()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            ghost.StartHunt();
            Debug.Log("[DevConsole] Forced Hunt Started");
        }
    }

    private void OnStopHunt()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            ghost.EndHunt();
            Debug.Log("[DevConsole] Hunt Stopped");
        }
    }

    private void OnForceEMF5()
    {
        var paranormal = ParanormalManager.Instance;
        var ghost = FindAnyObjectByType<GhostHandler>();
        
        if (paranormal != null && ghost != null)
        {
            paranormal.RegisterEvent(ghost.transform.position, 5, 20f);
            Debug.Log("[DevConsole] Forced EMF 5 at Ghost Location");
        }
    }

    private void OnForceGhostEvent()
    {
        var paranormal = ParanormalManager.Instance;
        var ghost = FindAnyObjectByType<GhostHandler>();
        
        if (paranormal != null && ghost != null)
        {
            ghost.TriggerGhostEvent(); // New method to show the event ghost sprite
            paranormal.RegisterEvent(ghost.transform.position, 3, 20f);
            Debug.Log("[DevConsole] Forced Ghost Event at Ghost Location");
        }
    }

    private void OnToggleGhostOutline()
    {
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            // Outline toggle can just force the ghost to be permanently visible for debugging
            ghost.ToggleDevVisibility();
            Debug.Log("[DevConsole] Toggled Ghost Visibility for Debugging");
        }
    }
}
