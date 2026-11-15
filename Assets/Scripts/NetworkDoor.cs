using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

public enum DoorMotionType
{
    ArcSwing,   // Automatically calculates the 2D hinge pivot from the two sprites and swings along the circular arc
    LinearLerp  // Linearly interpolates position and slerps rotation directly between the two poses
}

public class NetworkDoor : NetworkBehaviour, IInteractable
{
    public static readonly List<NetworkDoor> AllDoors = new List<NetworkDoor>();

    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> hasBeenOpened = new NetworkVariable<bool>(
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
    [SerializeField] private bool isFrontDoor = false; // Hunt locks front doors, also requires key to open initially
    [SerializeField] private bool requiresKey = false;
    [Tooltip("Whether this door is a hiding spot (closet or cabinet) where players conceal themselves.")]
    [SerializeField] private bool isHidingSpot = false;
    [Tooltip("Whether players can directly open/close this door via [E] or click (disabled for automated Jeepney ramp).")]
    [SerializeField] private bool allowPlayerInteract = true;

    [Header("Legacy Settings (Fallback if no references assigned)")]
    [SerializeField] private float openAngle = 90f;

    public bool IsOpen => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? isOpen.Value : localIsOpen;
    public bool IsLocked => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? isLocked.Value : localIsLocked;
    public bool IsFrontDoor => isFrontDoor;
    public bool RequiresKey => requiresKey;
    public bool IsHidingSpot => isHidingSpot;
    public bool AllowPlayerInteract => allowPlayerInteract;
    public float CurrentProgress => currentProgress;
    public bool IsKeyFound => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? isKeyFound.Value : localIsKeyFound;
    public bool HasBeenOpened => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        ? (hasBeenOpened.Value || isOpen.Value || localHasBeenOpened)
        : (localHasBeenOpened || localIsOpen);

    /// <summary>Returns true if the active scene contains at least one front door.</summary>
    public static bool HasAnyFrontDoor()
    {
        for (int i = 0; i < AllDoors.Count; i++)
        {
            if (AllDoors[i] != null && AllDoors[i].isFrontDoor)
                return true;
        }
        return false;
    }

    /// <summary>Returns true once any front door in the scene has been opened at least once.</summary>
    public static bool HasAnyFrontDoorOpened()
    {
        for (int i = 0; i < AllDoors.Count; i++)
        {
            NetworkDoor door = AllDoors[i];
            if (door != null && door.isFrontDoor && door.HasBeenOpened)
                return true;
        }
        return false;
    }

    private NetworkVariable<bool> isKeyFound = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool localIsOpen = false;
    private bool localIsLocked = false;
    private bool localIsKeyFound = false;
    private bool localHasBeenOpened = false;
    private bool hasTriggeredGhostSpawn = false;

    private static readonly FieldInfo ShadowApplyToSortingLayersField =
        typeof(ShadowCaster2D).GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic);

    // Runtime state
    private GameObject runtimeDoorObj;
    private SpriteRenderer runtimeRenderer;
    private Collider2D runtimeCollider;
    private Collider2D interactionTriggerCollider;
    private ShadowCaster2D runtimeShadowCaster;
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
    private bool snappedPivot = false;
    private bool usesReferenceSprites = false;

    // Legacy fallback state
    private Quaternion legacyClosedRotation;
    private Quaternion legacyTargetOpenRotation;

    private void OnEnable()
    {
        if (!AllDoors.Contains(this))
        {
            AllDoors.Add(this);
        }
    }

    private void OnDisable()
    {
        AllDoors.Remove(this);
    }

    private void Awake()
    {
        if (!isHidingSpot && (gameObject.name.ToLower().Contains("cabinet") || gameObject.name.ToLower().Contains("closet")))
        {
            isHidingSpot = true;
        }

        if (closedDoorRef != null && openDoorRef != null)
        {
            // Auto-correct if references were assigned inverted in the inspector (OpenRef in closed slot, ClosedRef in open slot)
            if (closedDoorRef.name.ToLower().Contains("open") && openDoorRef.name.ToLower().Contains("closed"))
            {
                Debug.LogWarning($"NetworkDoor: '{gameObject.name}' has closedDoorRef ('{closedDoorRef.name}') and openDoorRef ('{openDoorRef.name}') inverted! Auto-swapping references.");
                var temp = closedDoorRef;
                closedDoorRef = openDoorRef;
                openDoorRef = temp;
            }

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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (isOpen.Value)
        {
            localHasBeenOpened = true;
            if (IsServer) hasBeenOpened.Value = true;
        }
        if (usesReferenceSprites)
        {
            currentProgress = isOpen.Value ? 1f : 0f;
            ApplyProgress(currentProgress);
        }
        isOpen.OnValueChanged += (oldVal, newVal) =>
        {
            if (newVal)
            {
                localHasBeenOpened = true;
                if (IsServer) hasBeenOpened.Value = true;
            }
            if (oldVal != newVal)
            {
                if (newVal) GameAudioManager.PlayDoorOpen(transform.position);
                else GameAudioManager.PlayDoorClose(transform.position);
            }
            if (newVal && isFrontDoor)
            {
                TutorialTooltipManager.TriggerHint(TutorialHintId.HousePower);
            }
        };
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
        snappedPivot = false;
        if (Mathf.Abs(deltaDeg) >= 0.5f)
        {
            pivotPoint = CalculateHingePivot(closedPos, closedAngleZ, openPos, openAngleZ);
            float maxExtent = closedDoorRef.bounds.extents.magnitude;
            if (maxExtent > 0.001f && Vector2.Distance(pivotPoint, closedPos) > maxExtent * 1.35f)
            {
                float halfX = closedDoorRef.sprite != null
                    ? closedDoorRef.sprite.bounds.extents.x * Mathf.Abs(closedScale.x)
                    : closedDoorRef.bounds.extents.x;
                pivotPoint = (Vector2)closedPos + (Vector2)(closedRot * new Vector3(-halfX, 0f, 0f));
                snappedPivot = true;
            }
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

        // Copy obstacle collider
        CopyCollider(closedDoorRef.gameObject, runtimeDoorObj);

        if (allowPlayerInteract)
        {
            // Persistent trigger collider keeps open doors interactive even when obstacle collider disables
            CreatePersistentInteractionTrigger(closedDoorRef.gameObject, runtimeDoorObj);
            var proxy = runtimeDoorObj.AddComponent<DoorInteractionProxy>();
            proxy.Setup(this);
        }

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

    private void CreatePersistentInteractionTrigger(GameObject sourceObj, GameObject targetObj)
    {
        Collider2D refCol = sourceObj.GetComponent<Collider2D>();
        if (refCol is BoxCollider2D box)
        {
            BoxCollider2D trig = targetObj.AddComponent<BoxCollider2D>();
            trig.size = new Vector2(Mathf.Max(box.size.x, 0.25f), Mathf.Max(box.size.y, 0.25f));
            trig.offset = box.offset;
            trig.isTrigger = true;
            interactionTriggerCollider = trig;
        }
        else if (refCol is CircleCollider2D circle)
        {
            CircleCollider2D trig = targetObj.AddComponent<CircleCollider2D>();
            trig.radius = Mathf.Max(circle.radius, 0.15f);
            trig.offset = circle.offset;
            trig.isTrigger = true;
            interactionTriggerCollider = trig;
        }
        else if (refCol is PolygonCollider2D poly)
        {
            PolygonCollider2D trig = targetObj.AddComponent<PolygonCollider2D>();
            trig.points = poly.points;
            trig.isTrigger = true;
            interactionTriggerCollider = trig;
        }
        else
        {
            BoxCollider2D trig = targetObj.AddComponent<BoxCollider2D>();
            trig.size = closedDoorRef != null && closedDoorRef.sprite != null
                ? (Vector2)closedDoorRef.sprite.bounds.size
                : Vector2.one * 0.3f;
            trig.isTrigger = true;
            interactionTriggerCollider = trig;
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

            runtimeShadowCaster = runtimeDoorObj.AddComponent<ShadowCaster2D>();
            runtimeShadowCaster.castsShadows = sourceShadow.castsShadows;
            runtimeShadowCaster.selfShadows = sourceShadow.selfShadows;
            ApplyDoorShadowSortingLayers(sourceShadow, runtimeShadowCaster);
        }
    }

    private static void ApplyDoorShadowSortingLayers(ShadowCaster2D source, ShadowCaster2D target)
    {
        if (ShadowApplyToSortingLayersField == null || target == null) return;

        int[] sourceLayers = source != null
            ? ShadowApplyToSortingLayersField.GetValue(source) as int[]
            : null;
        int wallLayerId = GetSortingLayerId("Wall");

        if (sourceLayers != null && sourceLayers.Length > 0)
        {
            List<int> filtered = new List<int>(sourceLayers.Length);
            for (int i = 0; i < sourceLayers.Length; i++)
            {
                if (wallLayerId != int.MinValue && sourceLayers[i] == wallLayerId) continue;
                filtered.Add(sourceLayers[i]);
            }
            ShadowApplyToSortingLayersField.SetValue(target, filtered.ToArray());
            return;
        }

        SortingLayer[] allLayers = SortingLayer.layers;
        List<int> nonWallLayers = new List<int>(allLayers.Length);
        for (int i = 0; i < allLayers.Length; i++)
        {
            if (allLayers[i].name == "Wall") continue;
            nonWallLayers.Add(allLayers[i].id);
        }
        ShadowApplyToSortingLayersField.SetValue(target, nonWallLayers.ToArray());
    }

    private static int GetSortingLayerId(string layerName)
    {
        SortingLayer[] layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name == layerName) return layers[i].id;
        }
        return int.MinValue;
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
            Vector3 vEndRotatedBack = snappedPivot
                ? vStart
                : Quaternion.Euler(0, 0, -deltaDeg) * ((Vector3)openPos - (Vector3)pivotPoint);
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
        bool didSwapSprite = swapSpriteWhenOpen && runtimeRenderer != null && openDoorRef != null && openDoorRef.sprite != null && openDoorRef.sprite != closedDoorRef.sprite;
        if (didSwapSprite)
        {
            runtimeRenderer.sprite = (t >= 0.5f) ? openDoorRef.sprite : closedDoorRef.sprite;
        }

        // Disable obstacle collider when open if enabled (interactionTriggerCollider stays enabled)
        if (disableObstacleColliderWhenOpen && runtimeCollider != null)
        {
            runtimeCollider.enabled = (t < 0.2f);
        }

        if (interactionTriggerCollider != null)
        {
            interactionTriggerCollider.enabled = allowPlayerInteract;
        }

        // For non-swinging swap doors (e.g. cabinets / ramps), disable shadow blocker when open
        if (runtimeShadowCaster != null && didSwapSprite)
        {
            runtimeShadowCaster.enabled = (t < 0.2f);
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

    public void UnlockWithKey()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer && requiresKey)
            {
                isKeyFound.Value = true;
            }
        }
        else
        {
            localIsKeyFound = true;
        }
    }

    public void Interact()
    {
        if (!allowPlayerInteract) return;

        if (IsLocked)
        {
            // Door is locked (e.g. during a Hunt)
            GameAudioManager.PlayDoorLocked(transform.position);
            return;
        }

        if (requiresKey && !IsKeyFound)
        {
            // Requires front door key
            GameAudioManager.PlayDoorLocked(transform.position);
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

    /// <summary>
    /// Programmatically opens or closes a door (used by automated Jeepney ramp arrival/departure).
    /// </summary>
    public void SetDoorOpen(bool open, bool immediate = false)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                isOpen.Value = open;
                if (open) hasBeenOpened.Value = true;
            }
            if (open) localHasBeenOpened = true;
            if (immediate)
            {
                currentProgress = open ? 1f : 0f;
                ApplyProgress(currentProgress);
            }
        }
        else
        {
            localIsOpen = open;
            if (open) localHasBeenOpened = true;
            if (immediate)
            {
                currentProgress = open ? 1f : 0f;
                ApplyProgress(currentProgress);
            }
        }
    }

    private void ToggleDoor()
    {
        Debug.Log($"Player toggled door: {gameObject.name}");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                isOpen.Value = !isOpen.Value;
                if (isOpen.Value)
                {
                    hasBeenOpened.Value = true;
                    localHasBeenOpened = true;
                }
                if (isFrontDoor && isOpen.Value && !hasTriggeredGhostSpawn)
                {
                    Debug.Log("NetworkDoor: Front door opened! Searching for GhostHandler...");
                    hasTriggeredGhostSpawn = true;
                    var ghost = Object.FindAnyObjectByType<GhostHandler>();
                    if (ghost != null)
                    {
                        Debug.Log("NetworkDoor: Found GhostHandler. Calling ActivateGhost().");
                        ghost.ActivateGhost();
                    }
                    else
                    {
                        Debug.LogError("NetworkDoor: GhostHandler not found in the scene!");
                    }
                }
            }
        }
        else
        {
            localIsOpen = !localIsOpen;
            if (localIsOpen) localHasBeenOpened = true;
            if (isFrontDoor && localIsOpen)
            {
                TutorialTooltipManager.TriggerHint(TutorialHintId.HousePower);
            }
            if (isFrontDoor && localIsOpen && !hasTriggeredGhostSpawn)
            {
                Debug.Log("NetworkDoor: Local front door opened! Searching for GhostHandler...");
                hasTriggeredGhostSpawn = true;
                var ghost = Object.FindAnyObjectByType<GhostHandler>();
                if (ghost != null)
                {
                    Debug.Log("NetworkDoor: Found GhostHandler locally. Calling ActivateGhost().");
                    ghost.ActivateGhost();
                }
                else
                {
                    Debug.LogError("NetworkDoor: GhostHandler not found locally in the scene!");
                }
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleDoorRpc()
    {
        if (!allowPlayerInteract || isLocked.Value) return;
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
            if (isOpen.Value)
            {
                hasBeenOpened.Value = true;
                localHasBeenOpened = true;
            }
        }
        else
        {
            localIsOpen = forceOpen.HasValue ? forceOpen.Value : !localIsOpen;
            if (localIsOpen) localHasBeenOpened = true;
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
        if (!allowPlayerInteract) return string.Empty;
        if (IsLocked) return "Locked";
        return IsOpen ? "Close Door" : "Open Door";
    }

    public bool CanDrag() => false; // Doors are now toggled with left click per Phase 5
    public void OnDragBegin(ulong clientId) { Interact(); } // Temporary fallback: just interact when dragged
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }

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