using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections;

[RequireComponent(typeof(Collider2D))]
public class HouseLightSwitch : NetworkBehaviour, IInteractable
{
    [Tooltip("The Light2D components to toggle on and off.")]
    [SerializeField] private List<Light2D> controlledLights = new List<Light2D>();

    [Tooltip("The GameObject(s) with the SafeZone script and Trigger Collider. This prevents sanity drain when the light is on.")]
    [SerializeField] private List<GameObject> litSafeZones = new List<GameObject>();

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
        if (FuseBox.Instance != null && !FuseBox.Instance.IsEnabled)
        {
            return "No Power (Fuse Box Off)";
        }
        return isLightOn.Value ? "Turn Off Light" : "Turn On Light";
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPosition) { }
    public void OnDragEnd(ulong clientId) { }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleLightServerRpc()
    {
        // Block turning ON lights if the fuse box is off
        if (!isLightOn.Value && FuseBox.Instance != null && !FuseBox.Instance.IsEnabled)
        {
            Debug.Log($"Light switch {gameObject.name} blocked — fuse box is off!");
            return;
        }

        isLightOn.Value = !isLightOn.Value;
        Debug.Log($"Player toggled light switch {gameObject.name} to {isLightOn.Value}");
    }

    /// <summary>
    /// Force-turns off this light. Called by FuseBox when it gets tripped.
    /// Must be called on the server.
    /// </summary>
    public void ForceOff()
    {
        if (!IsServer) return;
        if (isLightOn.Value)
        {
            isLightOn.Value = false;
        }
    }

    /// <summary>
    /// Force-turns on this light (e.g. via dev console).
    /// Must be called on the server.
    /// </summary>
    public void ForceOn()
    {
        if (!IsServer) return;
        if (!isLightOn.Value)
        {
            isLightOn.Value = true;
        }
    }

    /// <summary>
    /// Triggers a flicker effect on this light during a ghost hunt.
    /// Only flickers lights that are currently ON.
    /// </summary>
    public void HuntFlicker()
    {
        if (!isLightOn.Value) return;
        FlickerEffect(0.5f);
    }

    /// <summary>
    /// Called by the ghost to spook players by turning off the lights.
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

        foreach (var safeZone in litSafeZones)
        {
            if (safeZone != null)
            {
                safeZone.SetActive(state);
            }
        }
    }

    private Coroutine currentFlickerRoutine;

    public void FlickerEffect(float duration)
    {
        if (currentFlickerRoutine != null)
        {
            StopCoroutine(currentFlickerRoutine);
        }
        currentFlickerRoutine = StartCoroutine(FlickerRoutine(duration));
    }

    private System.Collections.IEnumerator FlickerRoutine(float duration)
    {
        float timer = 0f;
        while (timer < duration)
        {
            // Rapidly toggle lights locally without affecting the networked state
            bool randomState = Random.value > 0.5f;
            foreach (var light in controlledLights)
            {
                if (light != null) light.enabled = randomState;
            }
            
            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
            timer += 0.1f; // Rough estimation to save performance
        }
        
        // Restore actual state
        ApplyLightState(isLightOn.Value);
    }
}
