using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A completely revamped Flashlight that strictly follows the Player's position,
/// supports the new interaction/dragging controls, and defaults to OFF.
/// </summary>
public class FlashlightItem : EquipmentItem
{
    [Header("Flashlight Settings")]
    [Tooltip("The Light2D component that acts as the flashlight beam.")]
    [SerializeField] private Light2D spotlight;

    // Networked state for whether the flashlight is turned on or off
    // Defaults to false (Off at the start of the round)
    private NetworkVariable<bool> isLightOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsLightOn => isLightOn.Value;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Flashlight";
        
        if (spotlight == null)
        {
            spotlight = GetComponentInChildren<Light2D>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to changes so the light toggles instantly for all clients
        isLightOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        
        UpdateVisuals();
    }

    /// <summary>
    /// Critical Fix: Explicitly override the Update loop to guarantee Unity doesn't skip it.
    /// This ensures the flashlight ALWAYS moves with the player, even when hidden in the inventory.
    /// </summary>
    protected override void Update()
    {
        base.Update(); // Calls EquipmentItem.Update() which handles the movement and anchoring
    }

    /// <summary>
    /// Triggered by PlayerController when the player clicks Left Mouse Button (Primary Use).
    /// </summary>
    public override void UsePrimary()
    {
        if (IsOwner) // Only the person holding it can toggle it
        {
            ToggleLightRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ToggleLightRpc()
    {
        isLightOn.Value = !isLightOn.Value;
    }

    /// <summary>
    /// Controls whether the light and the sprite itself are visible.
    /// If the item is in your inventory but not currently held (isInHand = false), it goes invisible.
    /// </summary>
    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        // The item itself is visible if it is held, on the ground, or placed.
        bool isVisible = isInHand.Value || IsOnGround || IsPlaced;
        
        if (spotlight != null)
        {
            // The light beam should ONLY be enabled if the flashlight is visible AND turned on
            spotlight.enabled = isVisible && isLightOn.Value;
        }
    }
}