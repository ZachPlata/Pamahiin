using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DotsProjectorItem : EquipmentItem
{
    [Header("DOTS Settings")]
    public float projectionRadius = 3f;
    [SerializeField] private Light2D dotsLight;

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsPoweredOn => isPoweredOn.Value;

    protected override void Awake()
    {
        base.Awake();
        itemName = "DOTS Projector";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
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

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        if (dotsLight != null)
        {
            dotsLight.enabled = isPoweredOn.Value && (IsOnGround || IsInHand);
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, projectionRadius);
    }
}
