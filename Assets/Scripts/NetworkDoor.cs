using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum DoorMotionType
{
    ArcSwing,   // Automatically calculates the 2D hinge pivot from the two sprites and swings along the circular arc
    LinearLerp  // Linearly interpolates position and slerps rotation directly between the two poses
}

public class NetworkDoor : NetworkBehaviour, IInteractable
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Reference Sprites (Placed in Scene)")]
    [Tooltip("Pre-placed sprite defining the door in its resting/closed position, rotation, scale, and visuals.")]
    [SerializeField] private SpriteRenderer closedDoorRef;

    [Tooltip("Pre-placed sprite defining the door in its opened position, rotation, and scale.")]
    [SerializeField] private SpriteRenderer openDoorRef;

    [Header("Motion Settings")]
    [Tooltip("ArcSwing calculates the natural hinge pivot from the two poses. LinearLerp moves in a straight line.")]
    [SerializeField] private DoorMotionType motionType = DoorMotionType.ArcSwing;

    [Tooltip("Speed at which the door opens and closes.")]
    [SerializeField] private float swingSpeed = 5f;

    [Tooltip("If the open reference sprite has a different sprite asset assigned, swap to it when open.")]
    [SerializeField] private bool swapSpriteWhenOpen = true;

    [Tooltip("Optionally disable the door's obstacle collider when open.")]
    [SerializeField] private bool disableObstacleColliderWhenOpen = false;

    [Header("Door Settings")]
    [SerializeField] private bool isExitDoor = false; // Hunt locks exit doors

    [Header("Legacy Settings (Fallback if no references assigned)")]
    [SerializeField] private float openAngle = 90f;

    public bool IsOpen => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? isOpen.Value : localIsOpen;
    public bool IsLocked => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? isLocked.Value : localIsLocked;
    public bool IsExitDoor => isExitDoor;

    private bool localIsOpen = false;
    private bool localIsLocked = false;

    // Runtime state
    private GameObject runtimeDoorObj;
    private SpriteRenderer runtimeRenderer;
    private Collider2D runtimeCollider;
    private float currentProgress = 0f;

    // Cached poses
    private Vector3 closedPos;
    private Quaternion closedRot;
    private Vector3 closedScale;
    private float closedAngleZ;

    private Vector3 openPos;
    private Quaternion openRot;
    private Vector3 openScale;
    private float openAngleZ;

    private Vector2 pivotPoint;
    private bool hasValidPivot = false;
    private bool usesReferenceSprites = false;

    // Legacy fallback state
    private Quaternion legacyClosedRotation;
    private Quaternion legacyTargetOpenRotation;

    private void Awake()
    {
        if (closedDoorRef != null && openDoorRef != null)
        {
            usesReferenceSprites = true;
            InitializeFromReferences();
        }
        else
        {
            usesReferenceSprites = false;
            legacyClosedRotation = transform.rotation;
            legacyTargetOpenRotation = legacyClosedRotation * Quaternion.Euler(0, 0, openAngle);
        }
    }

    private void InitializeFromReferences()
    {
        // Cache world transform poses
        closedPos = closedDoorRef.transform.position;
        closedRot = closedDoorRef.transform.rotation;
        closedScale = closedDoorRef.transform.lossyScale;
        closedAngleZ = closedDoorRef.transform.eulerAngles.z;

        openPos = openDoorRef.transform.position;
        openRot = openDoorRef.transform.rotation;
        openScale = openDoorRef.transform.lossyScale;
        openAngleZ = openDoorRef.transform.eulerAngles.z;

        // Calculate hinge pivot if arc swing is used
        float deltaDeg = Mathf.DeltaAngle(closedAngleZ, openAngleZ);
        if (Mathf.Abs(deltaDeg) >= 0.5f)
        {
            pivotPoint = CalculateHingePivot(closedPos, closedAngleZ, openPos, openAngleZ);
            hasValidPivot = true;
        }
        else
        {
            hasValidPivot = false;
        }

        // Create runtime door visual object
        runtimeDoorObj = new GameObject($"{gameObject.name}_RuntimeDoor");
        runtimeDoorObj.transform.SetParent(transform, true);
        runtimeDoorObj.layer = closedDoorRef.gameObject.layer;
        runtimeDoorObj.tag = closedDoorRef.gameObject.tag;

        // Setup SpriteRenderer
        runtimeRenderer = runtimeDoorObj.AddComponent<SpriteRenderer>();
        CopySpriteRendererProperties(closedDoorRef, runtimeRenderer);

        // Copy or setup Collider
        CopyCollider(closedDoorRef.gameObject, runtimeDoorObj);

        // Add interaction proxy so clicking or interacting with the runtime door works
        var proxy = runtimeDoorObj.AddComponent<DoorInteractionProxy>();
        proxy.Setup(this);

        // Copy ShadowCaster2D if universal 2D shadow caster exists
        TrySetupShadowCaster();

        // Hide reference sprites so only the runtime door is active
        HideReferenceObject(closedDoorRef.gameObject);
        HideReferenceObject(openDoorRef.gameObject);

        // Apply initial closed pose
        ApplyProgress(0f);
    }

    private void HideReferenceObject(GameObject refObj)
    {
        if (refObj == null) return;
        if (refObj == gameObject)
        {
            // Don't disable the root NetworkDoor object!
            var sr = refObj.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }
        else
        {
            refObj.SetActive(false);
        }
    }

    private void CopySpriteRendererProperties(SpriteRenderer source, SpriteRenderer target)
    {
        if (source == null || target == null) return;
        target.sprite = source.sprite;
        target.color = source.color;
        target.sharedMaterial = source.sharedMaterial;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder;
        target.flipX = source.flipX;
        target.flipY = source.flipY;
        target.drawMode = source.drawMode;
        target.size = source.size;
        target.tileMode = source.tileMode;
        target.maskInteraction = source.maskInteraction;
        target.renderingLayerMask = source.renderingLayerMask;
    }

    private void CopyCollider(GameObject sourceObj, GameObject targetObj)
    {
        Collider2D refCol = sourceObj.GetComponent<Collider2D>();
        if (refCol is BoxCollider2D box)
        {
            BoxCollider2D newBox = targetObj.AddComponent<BoxCollider2D>();
            newBox.size = box.size;
            newBox.offset = box.offset;
            newBox.isTrigger = box.isTrigger;
            newBox.sharedMaterial = box.sharedMaterial;
            runtimeCollider = newBox;
        }
        else if (refCol is CircleCollider2D circle)
        {
            CircleCollider2D newCircle = targetObj.AddComponent<CircleCollider2D>();
            newCircle.radius = circle.radius;
            newCircle.offset = circle.offset;
            newCircle.isTrigger = circle.isTrigger;
            newCircle.sharedMaterial = circle.sharedMaterial;
            runtimeCollider = newCircle;
        }
        else if (refCol is PolygonCollider2D poly)
        {
            PolygonCollider2D newPoly = targetObj.AddComponent<PolygonCollider2D>();
            newPoly.points = poly.points;
            newPoly.isTrigger = poly.isTrigger;
            newPoly.sharedMaterial = poly.sharedMaterial;
            runtimeCollider = newPoly;
        }
    }

    private void TrySetupShadowCaster()
    {
        var sourceShadow = closedDoorRef.GetComponent<ShadowCaster2D>() ?? GetComponent<ShadowCaster2D>();
        if (sourceShadow != null)
        {
            // If ShadowCaster2D is on this parent GameObject, disable it so it doesn't leave a static shadow over the doorway
            var parentShadow = GetComponent<ShadowCaster2D>();
            if (parentShadow != null)
            {
                parentShadow.enabled = false;
            }

            var targetShadow = runtimeDoorObj.AddComponent<ShadowCaster2D>();
            targetShadow.castsShadows = sourceShadow.castsShadows;
            targetShadow.selfShadows = sourceShadow.selfShadows;
        }
    }

    public static Vector2 CalculateHingePivot(Vector2 p1, float theta1Deg, Vector2 p2, float theta2Deg)
    {
        float deltaDeg = Mathf.DeltaAngle(theta1Deg, theta2Deg);
        if (Mathf.Abs(deltaDeg) < 0.5f)
        {
            return (p1 + p2) * 0.5f;
        }

        float rad = deltaDeg * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(rad);
        float sinA = Mathf.Sin(rad);

        // R * p1
        float rp1_x = cosA * p1.x - sinA * p1.y;
        float rp1_y = sinA * p1.x + cosA * p1.y;

        // v = p2 - R * p1
        float vx = p2.x - rp1_x;
        float vy = p2.y - rp1_y;

        // M = I - R = [1 - cosA,  sinA]
        //             [-sinA, 1 - cosA]
        float a = 1f - cosA;
        float b = sinA;
        float det = a * a + b * b;

        if (det < 1e-6f)
        {
            return (p1 + p2) * 0.5f;
        }

        // M^-1 * v = (1 / det) * [a, -b; b, a] * [vx; vy]
        float cx = (a * vx - b * vy) / det;
        float cy = (b * vx + a * vy) / det;

        return new Vector2(cx, cy);
    }

    public void EvaluatePose(float t, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        scale = Vector3.Lerp(closedScale, openScale, t);
        rot = Quaternion.Slerp(closedRot, openRot, t);

        if (motionType == DoorMotionType.ArcSwing && hasValidPivot)
        {
            float deltaDeg = Mathf.DeltaAngle(closedAngleZ, openAngleZ);
            float currentAngleDelta = deltaDeg * t;
            Quaternion rotFromClosed = Quaternion.Euler(0, 0, currentAngleDelta);

            Vector3 vStart = (Vector3)closedPos - (Vector3)pivotPoint;
            Vector3 vEndRotatedBack = Quaternion.Euler(0, 0, -deltaDeg) * ((Vector3)openPos - (Vector3)pivotPoint);
            Vector3 baseOffset = Vector3.Lerp(vStart, vEndRotatedBack, t);

            pos = (Vector3)pivotPoint + rotFromClosed * baseOffset;
            pos.z = Mathf.Lerp(closedPos.z, openPos.z, t);
        }
        else
        {
            pos = Vector3.Lerp(closedPos, openPos, t);
        }
    }

    private void ApplyProgress(float t)
    {
        if (runtimeDoorObj == null) return;

        EvaluatePose(t, out Vector3 pos, out Quaternion rot, out Vector3 scale);
        runtimeDoorObj.transform.position = pos;
        runtimeDoorObj.transform.rotation = rot;

        // Maintain world scale regardless of parent scale
        Transform parentTrans = runtimeDoorObj.transform.parent;
        if (parentTrans != null && parentTrans.lossyScale.x != 0 && parentTrans.lossyScale.y != 0)
        {
            runtimeDoorObj.transform.localScale = new Vector3(
                scale.x / parentTrans.lossyScale.x,
                scale.y / parentTrans.lossyScale.y,
                scale.z / (parentTrans.lossyScale.z != 0 ? parentTrans.lossyScale.z : 1f)
            );
        }
        else
        {
            runtimeDoorObj.transform.localScale = scale;
        }

        // Swap sprite if configured and open sprite is different
        if (swapSpriteWhenOpen && runtimeRenderer != null && openDoorRef != null && openDoorRef.sprite != null && openDoorRef.sprite != closedDoorRef.sprite)
        {
            runtimeRenderer.sprite = (t >= 0.5f) ? openDoorRef.sprite : closedDoorRef.sprite;
        }

        // Disable obstacle collider when open if enabled
        if (disableObstacleColliderWhenOpen && runtimeCollider != null)
        {
            runtimeCollider.enabled = (t < 0.2f);
        }
    }

    private void Update()
    {
        if (usesReferenceSprites)
        {
            float targetT = IsOpen ? 1f : 0f;
            if (Mathf.Abs(currentProgress - targetT) > 0.0001f)
            {
                currentProgress = Mathf.Lerp(currentProgress, targetT, Time.deltaTime * swingSpeed);
                if (Mathf.Abs(currentProgress - targetT) < 0.001f)
                {
                    currentProgress = targetT;
                }
                ApplyProgress(currentProgress);
            }
        }
        else
        {
            // Legacy fallback
            Quaternion targetRotation = IsOpen ? legacyTargetOpenRotation : legacyClosedRotation;
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * swingSpeed);
        }
    }

    public void Interact()
    {
        if (IsLocked)
        {
            // Door is locked (e.g. during a Hunt)
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsServer)
        {
            ToggleDoor();
        }
        else
        {
            ToggleDoorRpc();
        }
    }

    private void ToggleDoor()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                isOpen.Value = !isOpen.Value;
            }
        }
        else
        {
            localIsOpen = !localIsOpen;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleDoorRpc()
    {
        if (isLocked.Value) return;
        ToggleDoor();
    }

    /// <summary>
    /// Called by the Ghost AI on the server to slam, close, or open a door.
    /// Emits an EMF level 2 paranormal event.
    /// </summary>
    public void GhostInteract(bool? forceOpen = null)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return;
        if (IsLocked) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            isOpen.Value = forceOpen.HasValue ? forceOpen.Value : !isOpen.Value;
        }
        else
        {
            localIsOpen = forceOpen.HasValue ? forceOpen.Value : !localIsOpen;
        }

        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 2, 25f);
        }
    }

    /// <summary>
    /// Locks or unlocks the door (used by Ghost Hunt Manifestation phase).
    /// </summary>
    public void SetLocked(bool locked)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            isLocked.Value = locked;
            if (locked)
            {
                isOpen.Value = false; // Closed when locked
            }
        }
        else
        {
            localIsLocked = locked;
            if (locked)
            {
                localIsOpen = false;
            }
        }
    }

    public string GetInteractText()
    {
        if (IsLocked) return "Locked";
        return IsOpen ? "Close Door" : "Open Door";
    }

    private void OnDrawGizmosSelected()
    {
        if (closedDoorRef == null || openDoorRef == null) return;

        Vector3 p1 = closedDoorRef.transform.position;
        Vector3 p2 = openDoorRef.transform.position;
        float deg1 = closedDoorRef.transform.eulerAngles.z;
        float deg2 = openDoorRef.transform.eulerAngles.z;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(p1, 0.08f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p2, 0.08f);

        float deltaDeg = Mathf.DeltaAngle(deg1, deg2);
        if (motionType == DoorMotionType.ArcSwing && Mathf.Abs(deltaDeg) >= 0.5f)
        {
            Vector2 pivot = CalculateHingePivot(p1, deg1, p2, deg2);
            Vector3 pivot3 = new Vector3(pivot.x, pivot.y, p1.z);

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(pivot3, 0.06f);
            Gizmos.DrawLine(pivot3, p1);
            Gizmos.DrawLine(pivot3, p2);

            int segments = 20;
            Vector3 prevArcPoint = p1;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Quaternion rot = Quaternion.Euler(0, 0, deltaDeg * t);
                Vector3 baseOffset = Vector3.Lerp(
                    p1 - pivot3,
                    Quaternion.Euler(0, 0, -deltaDeg) * (p2 - pivot3),
                    t
                );
                Vector3 pt = pivot3 + rot * baseOffset;
                Gizmos.DrawLine(prevArcPoint, pt);
                prevArcPoint = pt;
            }
        }
        else
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p1, p2);
        }
    }
}