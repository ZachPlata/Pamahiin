using Unity.Netcode;
using UnityEngine;

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
    private bool cameraSettingsStored = false;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Video Camera";
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
                cameraSettingsStored = true;
            }
        }

        if (localCamera != null)
        {
            // Only active if we are holding it and it's powered on
            bool isHolding = (ownerClientId.Value != ulong.MaxValue && 
                              NetworkManager.Singleton != null && 
                              ownerClientId.Value == NetworkManager.Singleton.LocalClientId && 
                              isInHand.Value);

            bool nightVisionActive = isHolding && isPoweredOn.Value;
            
            if (nightVisionActive)
            {
                // Reveal ghost orbs layer and change bg tint
                localCamera.backgroundColor = nightVisionColor;
                
                int ghostOrbLayer = LayerMask.NameToLayer("GhostOrb");
                if (ghostOrbLayer != -1)
                {
                    localCamera.cullingMask |= (1 << ghostOrbLayer);
                }
            }
            else
            {
                // Restore settings
                if (cameraSettingsStored)
                {
                    localCamera.backgroundColor = originalCameraColor;
                    int ghostOrbLayer = LayerMask.NameToLayer("GhostOrb");
                    if (ghostOrbLayer != -1)
                    {
                        localCamera.cullingMask &= ~(1 << ghostOrbLayer);
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        // Cleanup just in case we are destroyed while holding it
        if (localCamera != null && cameraSettingsStored)
        {
            localCamera.backgroundColor = originalCameraColor;
            int ghostOrbLayer = LayerMask.NameToLayer("GhostOrb");
            if (ghostOrbLayer != -1)
            {
                localCamera.cullingMask &= ~(1 << ghostOrbLayer);
            }
        }
    }
}
