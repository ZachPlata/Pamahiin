using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fuse Box acts as a middleman between players and the house light system.
/// Light switches will not work unless the fuse box is enabled.
/// During hunts, the ghost has a 15% chance (checked periodically) to trip the fuse box,
/// which kills ALL lights in the house. Players must manually re-enable it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class FuseBox : NetworkBehaviour, IInteractable
{
    [Header("Fuse Box Settings")]
    [Tooltip("Maximum number of lights allowed on before the fuse box becomes overloaded. " +
             "Set to 0 to disable the overload mechanic.")]
    [SerializeField] private int maxLightsBeforeOverload = 5;

    private NetworkVariable<bool> isEnabled = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Whether the fuse box is currently supplying power to the house.</summary>
    public bool IsEnabled => isEnabled.Value;

    public static FuseBox Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("FuseBox: Multiple FuseBox instances detected! Using the latest one.");
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        isEnabled.OnValueChanged += (oldVal, newVal) => OnFuseStateChanged(newVal);

        if (IsServer)
        {
            isEnabled.Value = false;
            KillAllLights();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // --- IInteractable ---

    public void Interact()
    {
        ToggleFuseBoxRpc();
    }

    public string GetInteractText()
    {
        return isEnabled.Value ? "Turn Off Fuse Box" : "Turn On Fuse Box";
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPosition) { }
    public void OnDragEnd(ulong clientId) { }

    // --- Core Logic ---

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleFuseBoxRpc()
    {
        isEnabled.Value = !isEnabled.Value;
        Debug.Log($"FuseBox toggled to: {isEnabled.Value}");

        if (!isEnabled.Value)
        {
            // Turning off fuse box kills ALL lights
            KillAllLights();
        }
    }

    /// <summary>
    /// Called by the ghost during a hunt to trip the fuse box.
    /// </summary>
    public void GhostTrip()
    {
        if (!IsServer) return;
        if (!isEnabled.Value) return; // Already tripped

        isEnabled.Value = false;
        KillAllLights();
        Debug.Log("FuseBox: Ghost tripped the fuse box!");

        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 3, 15f);
        }
    }

    /// <summary>
    /// Returns true if the fuse box is currently overloaded (too many lights on).
    /// </summary>
    public bool IsOverloaded()
    {
        if (maxLightsBeforeOverload <= 0) return false;

        int activeLightCount = 0;
        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        foreach (var sw in switches)
        {
            if (sw.IsOn) activeLightCount++;
        }

        return activeLightCount >= maxLightsBeforeOverload;
    }

    /// <summary>
    /// Returns the number of currently active light switches.
    /// </summary>
    public int GetActiveLightCount()
    {
        int count = 0;
        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        foreach (var sw in switches)
        {
            if (sw.IsOn) count++;
        }
        return count;
    }

    private void KillAllLights()
    {
        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        foreach (var sw in switches)
        {
            if (sw.IsOn)
            {
                sw.ForceOff();
            }
        }
    }

    private void OnFuseStateChanged(bool newState)
    {
        if (!newState)
        {
            // Fuse tripped — all lights go out (handled by KillAllLights on server,
            // but clients see the light switches update via their own NetworkVariables)
        }
    }
}
