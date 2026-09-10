using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DotsProjectorItem : EquipmentItem
{
    [Header("DOTS Settings")]
    public float projectionRadius = 3f;
    public float coneAngle = 60f;
    [SerializeField] private Light2D dotsLight;

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsPoweredOn => isPoweredOn.Value;

    private CircleCollider2D triggerCollider;

    protected override void Awake()
    {
        base.Awake();
        itemName = "DOTS Projector";
        if (dotsLight != null)
        {
            dotsLight.enabled = false; // Guarantee disabled at start
        }

        triggerCollider = GetComponent<CircleCollider2D>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<CircleCollider2D>();
        }
        triggerCollider.isTrigger = true;
        triggerCollider.radius = projectionRadius;
        triggerCollider.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        UpdateVisuals();
    }

    protected override void Update()
    {
        base.Update();
    }

    public override void UsePrimary()
    {
        if (IsOwner)
        {
            TogglePowerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        
        bool isVisible = isInHand.Value || IsOnGround || IsPlaced;
        
        if (dotsLight != null)
        {
            dotsLight.enabled = isVisible && isPoweredOn.Value;
        }

        if (triggerCollider != null)
        {
            triggerCollider.enabled = isVisible && isPoweredOn.Value;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, projectionRadius);
    }
}
