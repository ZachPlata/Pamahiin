using Unity.Netcode;
using UnityEngine;

public class RitualChalkItem : EquipmentItem
{
    [Header("Chalk Visual Slots")]
    [Tooltip("Sprite used when the chalk is held in hand or on the ground/table as an item (unplaced / dropped).")]
    [SerializeField] private Sprite itemSprite;

    [Tooltip("Sprite used when the chalk has been placed on the floor (the ritual diagram).")]
    [SerializeField] private Sprite diagramSprite;

    [Header("Scale & Sizing")]
    [Tooltip("Desired diameter of the chalk circle on the floor in world units (camera viewport height is ~0.6, so 0.24 - 0.32 fits cleanly in rooms and van floor).")]
    [SerializeField, Range(0.15f, 0.35f)] private float diagramWorldDiameter = 0.30f;

    [Tooltip("Target length of the chalk stick in world units when held or dropped (calibrated to fit EquipmentSpawn slots).")]
    [SerializeField, Range(0.012f, 0.045f)] private float chalkItemTargetLength = 0.020f;

    [Header("Rendering Layers")]
    [Tooltip("Sorting layer applied to the chalk diagram when placed on the ground (Item renders above all house/room floors).")]
    [SerializeField] private string diagramSortingLayer = "Item";

    [Tooltip("Order in sorting layer for the placed diagram (10 ensures it renders above floors, rugs, and furniture bases).")]
    [SerializeField] private int diagramSortingOrder = 10;

    [Header("Optional Diagram Prefab")]
    [Tooltip("Optional SummoningCircle prefab to instantiate at placement position. If null, a standalone SummoningCircle entity is created at runtime.")]
    [SerializeField] private GameObject chalkDiagramPrefab;

    [Header("Exorcism Settings")]
    [Tooltip("Whether placing the chalk diagram automatically triggers the exorcism ghost-selection popup.")]
    [SerializeField] private bool autoStartOnPlace = false;

    private Vector3 baseLocalScale = Vector3.one;
    private bool hasBaseScale = false;
    private bool originalIsTrigger = false;
    private GameObject spawnedDiagramInstance = null;

    public Sprite ItemSprite => itemSprite;
    public Sprite DiagramSprite => diagramSprite;

    // Disabling dragging ensures Left-Click immediately interacts (picks up into inventory) rather than physics-dragging.
    public override bool CanDrag() => false;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Ritual Chalk";

        SanitizeSizing();

        int interactableLayer = LayerMask.NameToLayer("Interactable");
        if (interactableLayer != -1 && gameObject.layer != interactableLayer)
        {
            gameObject.layer = interactableLayer;
        }

        if (interactCollider != null)
        {
            originalIsTrigger = interactCollider.isTrigger;
        }

        if (!hasBaseScale)
        {
            baseLocalScale = transform.localScale;
            hasBaseScale = true;
        }

        TryResolveSprites();
        UpdateVisuals();
    }

    private void SanitizeSizing()
    {
        // Enforce hard realistic bounds against stale serialized inspector values (e.g. 1.0f or 2.8f)
        if (diagramWorldDiameter > 0.35f || diagramWorldDiameter < 0.15f)
        {
            diagramWorldDiameter = 0.26f;
        }

        if (chalkItemTargetLength > 0.045f || chalkItemTargetLength < 0.012f)
        {
            chalkItemTargetLength = 0.020f;
        }
    }

    /// <summary>Sets the shelf/hand target length for unplaced chalk while preserving diagramWorldDiameter when placed.</summary>
    public void SetShelfTargetLength(float targetLength)
    {
        chalkItemTargetLength = Mathf.Clamp(targetLength, 0.012f, 0.035f);
        if (spriteRenderer != null)
        {
            UpdateVisuals();
        }
    }

    protected override void OnSyncedScaleApplied(Vector3 appliedScale)
    {
        float itemSpriteWidth = (itemSprite != null && itemSprite.pixelsPerUnit > 0)
            ? (itemSprite.rect.width / itemSprite.pixelsPerUnit)
            : 4.64f;
        float derivedLength = Mathf.Abs(appliedScale.x) * itemSpriteWidth;
        if (derivedLength >= 0.012f && derivedLength <= 0.045f)
        {
            chalkItemTargetLength = Mathf.Clamp(derivedLength, 0.012f, 0.035f);
        }
        if (spriteRenderer != null)
        {
            UpdateVisuals();
        }
    }

    private void Start()
    {
        SanitizeSizing();
        UpdateVisuals();
    }

    private void TryResolveSprites()
    {
        if (itemSprite == null && spriteRenderer != null && spriteRenderer.sprite != null)
        {
            itemSprite = spriteRenderer.sprite;
        }

        // Search currently loaded sprites across memory if not assigned in Inspector
        if (diagramSprite == null)
        {
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s != null && s.name.IndexOf("chalk diagram", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    diagramSprite = s;
                    break;
                }
            }
        }

        if (itemSprite == null)
        {
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s != null && s.name.IndexOf("chalk", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    s.name.IndexOf("diagram", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    itemSprite = s;
                    break;
                }
            }
        }

#if UNITY_EDITOR
        if (diagramSprite == null)
        {
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/EQUIPMENTS/chalk diagram.png");
            foreach (var a in assets)
            {
                if (a is Sprite s)
                {
                    diagramSprite = s;
                    break;
                }
            }
        }

        if (itemSprite == null)
        {
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/EQUIPMENTS/chalk 1.png");
            foreach (var a in assets)
            {
                if (a is Sprite s)
                {
                    itemSprite = s;
                    break;
                }
            }
        }
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SanitizeSizing();
        TryResolveSprites();
        UpdateVisuals();
    }
#endif

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        SanitizeSizing();
        TryResolveSprites();
        UpdateVisuals();
    }

    protected override void Update()
    {
        base.Update();

        // While held, detach from any truck table container so it doesn't inherit parent 11x scale
        if (ownerClientId.Value != ulong.MaxValue && transform.parent != null)
        {
            transform.SetParent(null, true);
            UpdateVisuals();
        }
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);

        // When picked up into hand or inventory, detach from truck container so it doesn't inherit parent scale
        if (newOwnerId != ulong.MaxValue && transform.parent != null)
        {
            transform.SetParent(null, true);
        }

        UpdateVisuals();
    }

    private void EnsureSpawnedSummoningCircle(Vector3 worldPos)
    {
        if (spawnedDiagramInstance != null) return;

        worldPos.z = 0f;
        if (chalkDiagramPrefab != null)
        {
            spawnedDiagramInstance = Instantiate(chalkDiagramPrefab, worldPos, Quaternion.identity);
            spawnedDiagramInstance.SetActive(true);
        }
        else
        {
            spawnedDiagramInstance = new GameObject("SummoningCircle");
            spawnedDiagramInstance.transform.position = worldPos;
            spawnedDiagramInstance.transform.rotation = Quaternion.identity;
        }

        var circle = spawnedDiagramInstance.GetComponent<SummoningCircle>();
        if (circle == null)
        {
            circle = spawnedDiagramInstance.AddComponent<SummoningCircle>();
        }

        float effectiveDiameter = Mathf.Clamp(diagramWorldDiameter, 0.15f, 0.35f);
        circle.Configure(effectiveDiameter, diagramSortingLayer, diagramSortingOrder, diagramSprite);
    }

    protected override void OnPlacedChanged(bool placed)
    {
        base.OnPlacedChanged(placed);

        if (placed)
        {
            GameAudioManager.PlayChalkDraw(transform.position);
            // 1. Detach from any parent container (e.g. truck table with miniature scale or rotation)
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            // 2. Enforce Z-coordinate is precisely 0f so it is never culled by 2D camera clipping
            Vector3 worldPos = transform.position;
            worldPos.z = 0f;
            transform.position = worldPos;

            // 3. Spawn standalone SummoningCircle synchronously on Server and Clients
            EnsureSpawnedSummoningCircle(worldPos);
        }
        else
        {
            if (spawnedDiagramInstance != null)
            {
                Destroy(spawnedDiagramInstance);
                spawnedDiagramInstance = null;
            }
        }

        UpdateVisuals();

        if (placed && autoStartOnPlace)
        {
            var exorcismManager = GetExorcismManager();
            if (exorcismManager != null && !exorcismManager.IsExorcismActive && !exorcismManager.IsExorcismCompleted)
            {
                TriggerRitualPopup();
            }
        }
    }

    private void ApplyWorldScale(float desiredWorldScale)
    {
        if (transform.parent != null)
        {
            Vector3 pScale = transform.parent.lossyScale;
            float sx = Mathf.Abs(pScale.x) > 0.0001f ? desiredWorldScale / Mathf.Abs(pScale.x) : desiredWorldScale;
            float sy = Mathf.Abs(pScale.y) > 0.0001f ? desiredWorldScale / Mathf.Abs(pScale.y) : desiredWorldScale;
            transform.localScale = new Vector3(sx, sy, 1f);
        }
        else
        {
            transform.localScale = new Vector3(desiredWorldScale, desiredWorldScale, 1f);
        }
    }

    public override void OnDestroy()
    {
        if (spawnedDiagramInstance != null)
        {
            Destroy(spawnedDiagramInstance);
            spawnedDiagramInstance = null;
        }
        base.OnDestroy();
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        if (spriteRenderer == null) return;

        SanitizeSizing();
        TryResolveSprites();

        // Compute chalk item scale (sized to ~0.025 world units, matching crucifix and player hands)
        float itemSpriteWidth = (itemSprite != null && itemSprite.pixelsPerUnit > 0)
            ? (itemSprite.rect.width / itemSprite.pixelsPerUnit)
            : 4.64f;
        float effectiveItemLength = Mathf.Clamp(chalkItemTargetLength, 0.012f, 0.035f);
        float itemScale = (itemSpriteWidth > 0.01f) ? (effectiveItemLength / itemSpriteWidth) : 0.0054f;

        if (IsPlaced)
        {
            // Decouple placed circle from the chalk stick: hide stick sprite & disable chalk collider
            if (Application.isPlaying)
            {
                EnsureSpawnedSummoningCircle(transform.position);
            }
            spriteRenderer.enabled = false;
            if (interactCollider != null)
            {
                interactCollider.enabled = false;
            }
        }
        else
        {
            // Unplaced / Dropped / In-Hand State: hide if deselected in inventory
            bool isVisible = isInHand.Value || IsOnGround;
            spriteRenderer.enabled = isVisible;
            if (!isVisible)
            {
                if (interactCollider != null)
                {
                    interactCollider.enabled = false;
                }
                return;
            }

            if (itemSprite != null)
            {
                spriteRenderer.sprite = itemSprite;
            }

            // The chalk item sprite uses calibrated handheld item scale (~0.025 world units)
            ApplyWorldScale(itemScale);

            spriteRenderer.sortingLayerName = "Item";
            spriteRenderer.sortingOrder = 0;
            spriteRenderer.color = Color.white;

            if (interactCollider != null)
            {
                interactCollider.isTrigger = originalIsTrigger;
                interactCollider.enabled = IsOnGround;
            }
        }
    }

    public override void Interact()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        if (IsPlaced)
        {
            var exorcismManager = GetExorcismManager();
            var nearbyCircle = SummoningCircle.FindClosest(transform.position, 0.75f);
            if ((exorcismManager != null && (exorcismManager.IsExorcismActive || exorcismManager.IsExorcismCompleted)) ||
                (nearbyCircle != null && nearbyCircle.IsIgniting))
            {
                return;
            }
            TriggerRitualPopup();
        }
        else
        {
            // Interacting with the item on the ground or shelf picks it up into inventory
            base.Interact();
        }
    }

    public override string GetInteractText()
    {
        if (IsPlaced)
        {
            var exorcismManager = GetExorcismManager();
            if (exorcismManager != null)
            {
                if (exorcismManager.IsExorcismCompleted)
                {
                    return "Exorcism Completed";
                }
                if (exorcismManager.IsExorcismActive)
                {
                    return "Exorcism in Progress";
                }
            }
            return "Summon Ghost";
        }

        return base.GetInteractText();
    }

    /// <summary>
    /// Validates whether the Ritual Chalk is currently inside the active Ghost Room bounds.
    /// Exactly matches SpiritBoxItem.IsInsideGhostRoom().
    /// </summary>
    public bool IsInsideGhostRoom()
    {
        return GhostRoomMarker.IsInsideGhostRoom(transform.position);
    }

    /// <summary>
    /// Validates whether a specific world position is inside the active Ghost Room bounds.
    /// </summary>
    public bool IsInsideGhostRoom(Vector3 position)
    {
        return GhostRoomMarker.IsInsideGhostRoom(position);
    }

    /// <summary>
    /// Validates whether the chalk can be placed / right-clicked.
    /// Strictly only allows placement if inside the GhostRoomMarker.
    /// </summary>
    public override bool CanPlace(Vector3 targetPosition)
    {
        if (!IsInsideGhostRoom() && !IsInsideGhostRoom(targetPosition))
        {
            Debug.Log("[RitualChalkItem] Location check failed: Ritual Chalk can only be placed inside the Ghost Room.");
            return false;
        }

        if (!IsInsideGhostRoom(targetPosition))
        {
            Debug.Log("[RitualChalkItem] Target placement location is outside the Ghost Room.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Triggered on Right Click while held in hand.
    /// Strictly only allows using/placing if inside the GhostRoomMarker.
    /// </summary>
    public override void UseSecondary()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        if (!IsInsideGhostRoom())
        {
            Debug.Log("[RitualChalkItem] Location check failed: Ritual Chalk can only be right-clicked inside the Ghost Room.");
            return;
        }

        base.UseSecondary();
    }

    public override void UsePrimary()
    {
        // When holding the chalk, notify the player to right click to place it on the floor
        Debug.Log("[Ritual Chalk] Press Right Click to draw the ritual chalk diagram on the floor.");
    }

    public void TriggerRitualPopup()
    {
        var exorcismManager = GetExorcismManager();
        if (exorcismManager != null)
        {
            exorcismManager.InitiateRitual(transform.position);
        }
        else
        {
            Debug.LogWarning("[Ritual Chalk] ExorcismManager not found in scene!");
        }
    }

    private ExorcismManager GetExorcismManager()
    {
        return ExorcismManager.Instance != null ? ExorcismManager.Instance : Object.FindAnyObjectByType<ExorcismManager>();
    }
}
