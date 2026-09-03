using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class FlashlightItem : EquipmentItem
{
    [Header("Flashlight Settings")]
    [SerializeField] private Light2D spotlight;

    private NetworkVariable<bool> isLightOn = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
        isLightOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        UpdateVisuals();
    }

    public override void UsePrimary()
    {
        ToggleLightRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ToggleLightRpc()
    {
        isLightOn.Value = !isLightOn.Value;
    }

    // Retained for backward compatibility
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UseItemRpc()
    {
        ToggleLightRpc();
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        bool isVisible = isInHand.Value || IsOnGround;
        if (spotlight != null)
        {
            spotlight.enabled = isVisible && isLightOn.Value;
        }
    }
}