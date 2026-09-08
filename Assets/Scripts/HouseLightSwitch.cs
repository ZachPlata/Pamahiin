using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Collider2D))]
public class HouseLightSwitch : NetworkBehaviour, IInteractable
{
    [Tooltip("The Light2D components to toggle on and off.")]
    [SerializeField] private List<Light2D> controlledLights = new List<Light2D>();

    [Tooltip("The GameObject with the SafeZone script and Trigger Collider. This prevents sanity drain when the light is on.")]
    [SerializeField] private GameObject litSafeZone;

    private NetworkVariable<bool> isLightOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsOn => isLightOn.Value;

    public override void OnNetworkSpawn()
    {
        isLightOn.OnValueChanged += (oldVal, newVal) => ApplyLightState(newVal);
        ApplyLightState(isLightOn.Value);
    }

    public void Interact()
    {
        ToggleLightServerRpc();
    }

    public string GetInteractText()
    {
        return isLightOn.Value ? "Turn Off Light" : "Turn On Light";
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPosition) { }
    public void OnDragEnd(ulong clientId) { }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleLightServerRpc()
    {
        isLightOn.Value = !isLightOn.Value;
        Debug.Log($"Player toggled light switch {gameObject.name} to {isLightOn.Value}");
    }

    /// <summary>
    /// Called by the GhostController to spook players by turning off the lights.
    /// Must be called on the server.
    /// </summary>
    public void GhostTurnOff()
    {
        if (!IsServer) return;
        if (isLightOn.Value)
        {
            isLightOn.Value = false;
            Debug.Log($"Ghost turned off light switch {gameObject.name}");
            
            // Optional: Register paranormal event for EMF readers
            if (ParanormalManager.Instance != null)
            {
                ParanormalManager.Instance.RegisterEvent(transform.position, 2, 10f);
            }
        }
    }

    private void ApplyLightState(bool state)
    {
        foreach (var light in controlledLights)
        {
            if (light != null)
            {
                light.enabled = state;
            }
        }

        if (litSafeZone != null)
        {
            litSafeZone.SetActive(state);
        }
    }
}
