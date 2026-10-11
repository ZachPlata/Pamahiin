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
    [Tooltip("Desired diameter of the chalk circle on the floor in world units (camera viewport height is ~0.6, so 0.22 - 0.25 fits cleanly in rooms and van floor).")]
    [SerializeField, Range(0.15f, 0.35f)] private float diagramWorldDiameter = 0.24f;

    [Tooltip("Target length of the chalk stick in world units when held or dropped (crucifix is ~0.035, so 0.025 fits player hands).")]
    [SerializeField, Range(0.015f, 0.045f)] private float chalkItemTargetLength = 0.025f;

    [Header("Rendering Layers")]
    [Tooltip("Sorting layer applied to the chalk diagram when placed on the ground (Item renders above all house/room floors).")]
    [SerializeField] private string diagramSortingLayer = "Item";

    [Tooltip("Order in sorting layer for the placed diagram (10 ensures it renders above floors, rugs, and furniture bases).")]
    [SerializeField] private int diagramSortingOrder = 10;

    [Header("Optional Diagram Prefab")]
    [Tooltip("Optional prefab to instantiate at placement position. If null, this chalk item itself renders the diagram sprite.")]
    [SerializeField] private GameObject chalkDiagramPrefab;

    [Header("Exorcism Settings")]
    [Tooltip("Whether placing the chalk diagram automatically triggers the exorcism ghost-selection popup.")]
    [SerializeField] private bool autoStartOnPlace = true;

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
            diagramWorldDiameter = 0.24f;
        }

        if (chalkItemTargetLength > 0.045f || chalkItemTargetLength < 0.015f)
        {
            chalkItemTargetLength = 0.025f;
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

    protected override void OnPlacedChanged(bool placed)
    {
        base.OnPlacedChanged(placed);

        if (placed)
        {
            // 1. Detach from any parent container (e.g. truck table with miniature scale or rotation)
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            // 2. Enforce Z-coordinate is precisely 0f so it is never culled by 2D camera clipping
            Vector3 worldPos = transform.position;
            worldPos.z = 0f;
            transform.position = worldPos;

            // 3. Optional prefab instantiation support
            if (chalkDiagramPrefab != null && spawnedDiagramInstance == null)
            {
                spawnedDiagramInstance = Instantiate(chalkDiagramPrefab, worldPos, transform.rotation);
                spawnedDiagramInstance.SetActive(true);

                float diagSpriteWidth = (diagramSprite != null && diagramSprite.pixelsPerUnit > 0)
                    ? (diagramSprite.rect.width / diagramSprite.pixelsPerUnit)
                    : 4.95f;
                float effectiveDiameter = Mathf.Clamp(diagramWorldDiameter, 0.15f, 0.28f);
                float diagScale = (diagSpriteWidth > 0.01f) ? (effectiveDiameter / diagSpriteWidth) : 0.0485f;
                spawnedDiagramInstance.transform.localScale = new Vector3(diagScale, diagScale, 1f);

                var sr = spawnedDiagramInstance.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.enabled = true;
                    sr.sortingLayerName = diagramSortingLayer;
                    sr.sortingOrder = diagramSortingOrder;
                }

                // If diagram prefab has NetworkObject and we are server, spawn it
                var netObj = spawnedDiagramInstance.GetComponent<NetworkObject>();
                if (netObj != null && IsServer && !netObj.IsSpawned)
                {
                    netObj.Spawn();
                }
            }
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
            TriggerRitualPopup();
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

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        if (spriteRenderer == null) return;

        SanitizeSizing();
        TryResolveSprites();

        // 1. Compute diagram world scale (fits inside rooms/van, ~0.24 world units diameter)
        float diagSpriteWidth = (diagramSprite != null && diagramSprite.pixelsPerUnit > 0)
            ? (diagramSprite.rect.width / diagramSprite.pixelsPerUnit)
            : 4.95f;
        float effectiveDiagramDiameter = Mathf.Clamp(diagramWorldDiameter, 0.15f, 0.28f);
        float diagramScale = (diagSpriteWidth > 0.01f) ? (effectiveDiagramDiameter / diagSpriteWidth) : 0.0485f;

        // 2. Compute chalk item scale (sized to ~0.025 world units, matching crucifix and player hands)
        float itemSpriteWidth = (itemSprite != null && itemSprite.pixelsPerUnit > 0)
            ? (itemSprite.rect.width / itemSprite.pixelsPerUnit)
            : 4.64f;
        float effectiveItemLength = Mathf.Clamp(chalkItemTargetLength, 0.015f, 0.035f);
        float itemScale = (itemSpriteWidth > 0.01f) ? (effectiveItemLength / itemSpriteWidth) : 0.0054f;

        if (IsPlaced)
        {
            // Placed State: show ritual diagram
            if (chalkDiagramPrefab != null && spawnedDiagramInstance != null)
            {
                spawnedDiagramInstance.transform.localScale = new Vector3(diagramScale, diagramScale, 1f);
                spriteRenderer.enabled = false; // Rendered by instantiated prefab
            }
            else
            {
                spriteRenderer.enabled = true;
                if (diagramSprite != null)
                {
                    spriteRenderer.sprite = diagramSprite;
                }
                else if (itemSprite != null)
                {
                    spriteRenderer.sprite = itemSprite;
                }

                ApplyWorldScale(diagramScale);
                spriteRenderer.sortingLayerName = diagramSortingLayer;
                spriteRenderer.sortingOrder = diagramSortingOrder;
                spriteRenderer.color = Color.white;
            }

            // Allow walking smoothly across the chalk diagram on the floor
            if (interactCollider != null)
            {
                interactCollider.enabled = true;
                interactCollider.isTrigger = true;
            }
        }
        else
        {
            // Unplaced / Dropped State: show chalk item sprite and apply calibrated handheld item scale
            spriteRenderer.enabled = true;
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
            }
        }
    }

    public override void Interact()
    {
        if (IsPlaced)
        {
            // Interacting with the placed diagram triggers the exorcism ghost-selection popup if not already running
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
            if (exorcismManager != null && exorcismManager.IsMinigameActive)
            {
                return "Exorcism in Progress";
            }
            return "Begin Exorcism";
        }

        return base.GetInteractText();
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
