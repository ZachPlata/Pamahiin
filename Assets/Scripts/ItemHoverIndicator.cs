using TMPro;
using UnityEngine;

/// <summary>
/// Displays a green border indicator around pickable items and their name on top
/// when the local player is hovering over them and is within reach.
/// </summary>
public class ItemHoverIndicator : MonoBehaviour
{
    public static ItemHoverIndicator Instance { get; private set; }

    [Header("Visual Settings")]
    [SerializeField] private Color borderColor = new Color(0.12f, 1f, 0.35f, 1f);
    [SerializeField] private float borderWidth = 0.035f;
    [SerializeField] private float padding = 0.05f;

    private LineRenderer lineRenderer;
    private TextMeshPro nameText;
    private GameObject bgObject;
    private SpriteRenderer bgRenderer;

    private GameObject currentTarget;
    private SpriteRenderer targetSpriteRenderer;

    private static Sprite defaultBgSprite;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupLineRenderer();
        SetupNameText();
        Hide();
    }

    private void SetupLineRenderer()
    {
        GameObject lineObj = new GameObject("BorderLine");
        lineObj.transform.SetParent(transform, false);

        lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 5;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = borderWidth;
        lineRenderer.endWidth = borderWidth;
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;
        lineRenderer.sortingOrder = 50;

        // Use unlit sprite shader so the indicator stays bright and vivid even in pitch dark rooms
        Shader unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
        if (unlitShader != null)
        {
            Material lineMat = new Material(unlitShader);
            lineMat.color = borderColor;
            lineRenderer.material = lineMat;
        }
    }

    private static Sprite GetOrCreateBgSprite()
    {
        if (defaultBgSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            defaultBgSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        return defaultBgSprite;
    }

    private void SetupNameText()
    {
        // 1. Text Mesh Pro label
        GameObject textObj = new GameObject("ItemNameText");
        textObj.transform.SetParent(transform, false);

        nameText = textObj.AddComponent<TextMeshPro>();
        nameText.fontSize = 2.6f;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = new Color(0.92f, 1f, 0.94f, 1f);
        nameText.outlineColor = Color.black;
        nameText.outlineWidth = 0.28f;
        nameText.sortingOrder = 55;

        // 2. Background pill badge behind text
        bgObject = new GameObject("TextBadgeBackground");
        bgObject.transform.SetParent(transform, false);

        bgRenderer = bgObject.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = GetOrCreateBgSprite();
        bgRenderer.color = new Color(0.04f, 0.08f, 0.05f, 0.85f);
        bgRenderer.sortingOrder = 52;
    }

    /// <summary>
    /// Shows the indicator over the specified item with its display name.
    /// </summary>
    public void Show(GameObject itemObj, string displayName)
    {
        if (itemObj == null)
        {
            Hide();
            return;
        }

        currentTarget = itemObj;
        targetSpriteRenderer = itemObj.GetComponentInChildren<SpriteRenderer>();

        if (nameText != null)
        {
            nameText.text = displayName;
        }

        if (lineRenderer != null) lineRenderer.enabled = true;
        if (nameText != null) nameText.enabled = true;
        if (bgObject != null) bgObject.SetActive(true);

        UpdateIndicatorTransform();
    }

    /// <summary>
    /// Hides the indicator.
    /// </summary>
    public void Hide()
    {
        currentTarget = null;
        targetSpriteRenderer = null;

        if (lineRenderer != null) lineRenderer.enabled = false;
        if (nameText != null) nameText.enabled = false;
        if (bgObject != null) bgObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (currentTarget == null || !currentTarget.activeInHierarchy)
        {
            Hide();
            return;
        }

        UpdateIndicatorTransform();
    }

    private void UpdateIndicatorTransform()
    {
        if (currentTarget == null) return;

        Vector3 p0, p1, p2, p3;

        // Use sprite renderer local bounds transformed into world space to follow item rotation precisely
        if (targetSpriteRenderer != null && targetSpriteRenderer.sprite != null)
        {
            Bounds b = targetSpriteRenderer.sprite.bounds;
            Vector3 c0 = new Vector3(b.min.x - padding, b.min.y - padding, 0f);
            Vector3 c1 = new Vector3(b.min.x - padding, b.max.y + padding, 0f);
            Vector3 c2 = new Vector3(b.max.x + padding, b.max.y + padding, 0f);
            Vector3 c3 = new Vector3(b.max.x + padding, b.min.y - padding, 0f);

            Transform t = targetSpriteRenderer.transform;
            p0 = t.TransformPoint(c0);
            p1 = t.TransformPoint(c1);
            p2 = t.TransformPoint(c2);
            p3 = t.TransformPoint(c3);
        }
        else
        {
            Collider2D col = currentTarget.GetComponent<Collider2D>();
            Bounds b = (col != null) ? col.bounds : new Bounds(currentTarget.transform.position, Vector3.one * 0.5f);
            p0 = new Vector3(b.min.x - padding, b.min.y - padding, 0f);
            p1 = new Vector3(b.min.x - padding, b.max.y + padding, 0f);
            p2 = new Vector3(b.max.x + padding, b.max.y + padding, 0f);
            p3 = new Vector3(b.max.x + padding, b.min.y - padding, 0f);
        }

        if (lineRenderer != null && lineRenderer.enabled)
        {
            lineRenderer.SetPosition(0, p0);
            lineRenderer.SetPosition(1, p1);
            lineRenderer.SetPosition(2, p2);
            lineRenderer.SetPosition(3, p3);
            lineRenderer.SetPosition(4, p0);
        }

        // Position name text label on top of the item
        float topY = Mathf.Max(p0.y, p1.y, p2.y, p3.y);
        float centerX = (p0.x + p1.x + p2.x + p3.x) * 0.25f;
        Vector3 labelPos = new Vector3(centerX, topY + 0.18f, 0f);

        if (nameText != null && nameText.enabled)
        {
            nameText.transform.position = labelPos;
            nameText.transform.rotation = Quaternion.identity;

            if (bgObject != null && bgObject.activeSelf)
            {
                bgObject.transform.position = labelPos;
                bgObject.transform.rotation = Quaternion.identity;
                float width = Mathf.Max(0.55f, nameText.preferredWidth + 0.2f);
                float height = Mathf.Max(0.24f, nameText.preferredHeight + 0.08f);
                bgObject.transform.localScale = new Vector3(width, height, 1f);
            }
        }
    }
}
