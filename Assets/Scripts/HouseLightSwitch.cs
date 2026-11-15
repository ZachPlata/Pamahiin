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

    [Header("Switch Indicator Light")]
    [SerializeField] private Light2D indicatorLight;
    [SerializeField] private float indicatorRadius = 0.18f;
    [SerializeField] private float indicatorIntensityOn = 0.7f;
    [SerializeField] private float indicatorIntensityOff = 0.45f;
    [SerializeField] private Color indicatorColorOn = new Color(1f, 0.94f, 0.68f, 1f);
    [SerializeField] private Color indicatorColorOff = new Color(0.95f, 0.85f, 0.55f, 1f);

    private NetworkVariable<bool> isLightOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsOn => isLightOn.Value;

    private void Awake()
    {
        EnsureSwitchSetup();
    }

    private void OnEnable()
    {
        UpdateIndicatorLightScale();
        UpdateIndicatorLightVisual(isLightOn.Value);
    }

    public override void OnNetworkSpawn()
    {
        EnsureSwitchSetup();

        isLightOn.OnValueChanged += (oldVal, newVal) =>
        {
            if (oldVal != newVal) GameAudioManager.PlayLightSwitch(transform.position);
            ApplyLightState(newVal);
        };
        ApplyLightState(isLightOn.Value);
    }

    /// <summary>
    /// Replaces legacy oversized CircleCollider2D triggers with a sprite-fitting BoxCollider2D
    /// and guarantees the small switch indicator Light2D exists without altering switch scale.
    /// </summary>
    public void EnsureSwitchSetup()
    {
        EnsureBoxCollider();
        EnsureIndicatorLight();
    }

    private void EnsureBoxCollider()
    {
        // Add BoxCollider2D BEFORE removing CircleCollider2D so [RequireComponent(typeof(Collider2D))] never blocks removal.
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        bool addedBox = box == null;
        if (addedBox)
        {
            box = gameObject.AddComponent<BoxCollider2D>();
        }

        bool removedCircle = false;
        CircleCollider2D[] circles = GetComponents<CircleCollider2D>();
        for (int i = 0; i < circles.Length; i++)
        {
            circles[i].enabled = false;
            circles[i].radius = 0.001f;
            DestroyImmediate(circles[i]);
            removedCircle = true;
        }

        box.isTrigger = true;
        if ((removedCircle || addedBox) && TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
        {
            Bounds spriteBounds = sr.sprite.bounds;
            box.offset = spriteBounds.center;
            box.size = new Vector2(
                Mathf.Max(0.05f, spriteBounds.size.x),
                Mathf.Max(0.05f, spriteBounds.size.y));
        }
    }

    private void EnsureIndicatorLight()
    {
        if (indicatorLight == null)
        {
            Transform existing = transform.Find("SwitchIndicatorLight");
            if (existing != null)
            {
                indicatorLight = existing.GetComponent<Light2D>();
            }
        }

        if (indicatorLight == null)
        {
            var lightGo = new GameObject("SwitchIndicatorLight");
            lightGo.transform.SetParent(transform, false);
            if (TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
            {
                lightGo.transform.localPosition = sr.sprite.bounds.center;
            }
            indicatorLight = lightGo.AddComponent<Light2D>();
        }

        // Indicator must stay out of controlledLights so switches remain visible in dark rooms
        if (controlledLights != null && indicatorLight != null)
        {
            controlledLights.Remove(indicatorLight);
        }

        if (indicatorLight == null) return;

        indicatorLight.lightType = Light2D.LightType.Point;
        indicatorLight.pointLightInnerAngle = 360f;
        indicatorLight.pointLightOuterAngle = 360f;
        indicatorLight.pointLightInnerRadius = 0.02f;
        indicatorLight.pointLightOuterRadius = indicatorRadius;
        indicatorLight.falloffIntensity = 0.65f;
        indicatorLight.shadowsEnabled = false;
        indicatorLight.targetSortingLayers = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);
        indicatorLight.enabled = true;

        UpdateIndicatorLightScale();
        UpdateIndicatorLightVisual(isLightOn.Value);
    }

    private void UpdateIndicatorLightScale()
    {
        if (indicatorLight == null) return;
        // Compensate parent lossyScale (e.g. manually scaled 1x3 or 3x1 switches) so the indicator glow stays circular.
        Vector3 lossy = transform.lossyScale;
        float sx = Mathf.Abs(lossy.x) > 0.0001f ? 1f / Mathf.Abs(lossy.x) : 1f;
        float sy = Mathf.Abs(lossy.y) > 0.0001f ? 1f / Mathf.Abs(lossy.y) : 1f;
        indicatorLight.transform.localScale = new Vector3(sx, sy, 1f);
    }

    private void UpdateIndicatorLightVisual(bool state)
    {
        if (indicatorLight == null) return;
        indicatorLight.enabled = true;
        indicatorLight.intensity = state ? indicatorIntensityOn : indicatorIntensityOff;
        indicatorLight.color = state ? indicatorColorOn : indicatorColorOff;
    }

    public void Interact()
    {
        ToggleLightServerRpc();
    }

    public string GetInteractText()
    {
        if (!isLightOn.Value && FuseBox.Instance != null && !FuseBox.Instance.IsEnabled)
        {
            return "Turn on fuse box first!";
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

        UpdateIndicatorLightVisual(state);
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
