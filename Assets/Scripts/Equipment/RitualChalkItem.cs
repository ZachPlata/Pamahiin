using Unity.Netcode;
using UnityEngine;

public class RitualChalkItem : EquipmentItem
{
    [Header("Chalk Visual Slots")]
    [Tooltip("Sprite used when the chalk is held in hand or on the ground/table as an item (unplaced / dropped).")]
    [SerializeField] private Sprite itemSprite;

    [Tooltip("Sprite used when the chalk has been placed on the floor (the ritual diagram).")]
    [SerializeField] private Sprite diagramSprite;

    [Tooltip("Scale multiplier applied to the diagram when placed so the ritual circle looks properly sized on the floor.")]
    [SerializeField, Range(0.5f, 10f)] private float diagramScaleMultiplier = 2.5f;

    [Header("Exorcism Settings")]
    [Tooltip("Whether placing the chalk diagram automatically triggers the exorcism ghost-selection popup.")]
    [SerializeField] private bool autoStartOnPlace = true;

    private Vector3 baseLocalScale = Vector3.one;
    private bool hasBaseScale = false;
    private bool originalIsTrigger = false;

    public Sprite ItemSprite => itemSprite;
    public Sprite DiagramSprite => diagramSprite;

    // Disabling dragging ensures Left-Click immediately interacts (picks up into inventory) rather than physics-dragging.
    public override bool CanDrag() => false;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Ritual Chalk";

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

        if (itemSprite == null && spriteRenderer != null && spriteRenderer.sprite != null)
        {
            itemSprite = spriteRenderer.sprite;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Automatically link the project's chalk sprites if empty, while allowing manual inspector assignment
        if (itemSprite == null)
        {
            itemSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/EQUIPMENTS/chalk.png");
        }
        if (diagramSprite == null)
        {
            diagramSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/EQUIPMENTS/chalk diagram.png");
        }
    }
#endif

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        UpdateVisuals();
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);
        UpdateVisuals();
    }

    protected override void OnPlacedChanged(bool placed)
    {
        base.OnPlacedChanged(placed);
        UpdateVisuals();

        if (placed && autoStartOnPlace)
        {
            TriggerRitualPopup();
        }
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        if (spriteRenderer == null) return;

        if (!hasBaseScale)
        {
            baseLocalScale = transform.localScale;
            hasBaseScale = true;
        }

        if (IsPlaced)
        {
            // Placed State: show ritual diagram sprite and adjust scale
            if (diagramSprite != null)
            {
                spriteRenderer.sprite = diagramSprite;
            }
            else if (itemSprite != null)
            {
                spriteRenderer.sprite = itemSprite;
            }

            transform.localScale = baseLocalScale * Mathf.Max(0.1f, diagramScaleMultiplier);

            // Change layer to the "Van" sorting layer so the sprite becomes clearly visible on the floor
            spriteRenderer.sortingLayerName = "Van";
            spriteRenderer.sortingOrder = 5;

            // Allow walking smoothly across the chalk diagram on the floor
            if (interactCollider != null)
            {
                interactCollider.isTrigger = true;
            }
        }
        else
        {
            // Unplaced / Dropped State: show chalk item sprite and restore base scale
            if (itemSprite != null)
            {
                spriteRenderer.sprite = itemSprite;
            }

            transform.localScale = baseLocalScale;
            spriteRenderer.sortingLayerName = "Item";
            spriteRenderer.sortingOrder = 0;

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
