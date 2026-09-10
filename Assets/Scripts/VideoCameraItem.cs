using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VideoCameraItem : EquipmentItem
{
    [Header("Video Camera Settings")]
    [SerializeField] private Color nightVisionColor = new Color(0.2f, 0.2f, 0.2f); // Black and white/night vision tint
    
    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsPoweredOn => isPoweredOn.Value;

    private Camera localCamera;
    private Color originalCameraColor;
    private int originalCullingMask;
    private float originalOrthoSize;
    private bool cameraSettingsStored = false;

    private Volume nightVisionVolume;
    private Light2D nightVisionLight;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Video Camera";
        SetupNightVisionEffects();
    }

    private void SetupNightVisionEffects()
    {
        // 1. Setup Volume for Black and White
        GameObject volumeObj = new GameObject("NightVisionVolume");
        volumeObj.transform.SetParent(transform);
        nightVisionVolume = volumeObj.AddComponent<Volume>();
        nightVisionVolume.isGlobal = true;
        nightVisionVolume.priority = 100;
        
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        if (profile.Add<ColorAdjustments>(true))
        {
            if (profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj.saturation.Override(-100f);
            }
        }
        nightVisionVolume.sharedProfile = profile;
        nightVisionVolume.weight = 0f;

        // 2. Setup Global Light2D to see in the dark
        GameObject lightObj = new GameObject("NightVisionLight");
        lightObj.transform.SetParent(transform);
        nightVisionLight = lightObj.AddComponent<Light2D>();
        nightVisionLight.lightType = Light2D.LightType.Global;
        nightVisionLight.color = Color.white;
        nightVisionLight.intensity = 1.0f; // Adjust this if you want it brighter
        nightVisionLight.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateNightVisionVisuals();
    }

    public override void UsePrimary()
    {
        TogglePowerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);
        UpdateNightVisionVisuals();
    }

    protected override void OnInHandChanged(bool inHand)
    {
        base.OnInHandChanged(inHand);
        UpdateNightVisionVisuals();
    }

    private void UpdateNightVisionVisuals()
    {
        if (localCamera == null)
        {
            localCamera = Camera.main;
            if (localCamera != null && !cameraSettingsStored)
            {
                originalCameraColor = localCamera.backgroundColor;
                originalCullingMask = localCamera.cullingMask;
                originalOrthoSize = localCamera.orthographicSize;
                cameraSettingsStored = true;
            }
        }

        bool isHolding = (ownerClientId.Value != ulong.MaxValue && 
                            NetworkManager.Singleton != null && 
                            ownerClientId.Value == NetworkManager.Singleton.LocalClientId && 
                            isInHand.Value);

        bool nightVisionActive = isHolding && isPoweredOn.Value;

        if (localCamera != null)
        {
            if (nightVisionActive)
            {
                localCamera.backgroundColor = nightVisionColor;
                if (cameraSettingsStored)
                {
                    localCamera.orthographicSize = originalOrthoSize - 0.05f;
                }
                
                int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
                if (ghostOrbLayer != -1)
                {
                    localCamera.cullingMask |= (1 << ghostOrbLayer);
                }
            }
            else
            {
                if (cameraSettingsStored)
                {
                    localCamera.backgroundColor = originalCameraColor;
                    localCamera.orthographicSize = originalOrthoSize;
                    
                    int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
                    if (ghostOrbLayer != -1)
                    {
                        localCamera.cullingMask &= ~(1 << ghostOrbLayer);
                    }
                }
            }
        }

        if (nightVisionVolume != null)
        {
            nightVisionVolume.weight = nightVisionActive ? 1f : 0f;
        }

        if (nightVisionLight != null)
        {
            nightVisionLight.enabled = nightVisionActive;
        }
    }

    public override void OnDestroy()
    {
        if (localCamera != null && cameraSettingsStored)
        {
            localCamera.backgroundColor = originalCameraColor;
            localCamera.orthographicSize = originalOrthoSize;
            
            int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
            if (ghostOrbLayer != -1)
            {
                localCamera.cullingMask &= ~(1 << ghostOrbLayer);
            }
        }
    }
}
