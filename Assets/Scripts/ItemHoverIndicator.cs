using TMPro;
using UnityEngine;

/// <summary>
/// Displays a green border indicator tracing the hovered item's PolygonCollider2D outline
/// and renders the item's name + remaining quantity directly above it.
/// </summary>
public class ItemHoverIndicator : MonoBehaviour
{
    public static ItemHoverIndicator Instance { get; private set; }

    [Header("Visual Settings")]
    [SerializeField] private Color borderColor = new Color(0.12f, 1f, 0.35f, 1f);
    [SerializeField] private float borderWidth = 0.005f;
    [SerializeField] private float padding = 0.006f;

    private LineRenderer lineRenderer;
    private TextMeshPro nameText;
    private GameObject bgObject;
    private SpriteRenderer bgRenderer;

    private GameObject currentTarget;
    private SpriteRenderer targetSpriteRenderer;
    private PolygonCollider2D targetPolygonCollider;
    private Collider2D targetFallbackCollider;
    private string currentDisplayName = "";
    private Vector3 topCenterWorldPos;

    private static Sprite defaultBgSprite;
    private static Texture2D guiWhiteTex;
    private GUIStyle labelTextStyle;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SetupLineRenderer();
        SetupNameText();
        Hide();
    }

    private void SetupLineRenderer()
    {
        GameObject lineObj = new GameObject("BorderLine");
        lineObj.transform.SetParent(transform, false);

        lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.numCapVertices = 2;
        lineRenderer.startWidth = borderWidth;
        lineRenderer.endWidth = borderWidth;
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;
        lineRenderer.sortingLayerName = "Roof";
        lineRenderer.sortingOrder = 500;

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
            tex.filterMode = FilterMode.Point;
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            defaultBgSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        return defaultBgSprite;
    }

    private static Texture2D GetGuiWhiteTex()
    {
        if (guiWhiteTex == null)
        {
            guiWhiteTex = new Texture2D(1, 1);
            guiWhiteTex.filterMode = FilterMode.Point;
            guiWhiteTex.SetPixel(0, 0, Color.white);
            guiWhiteTex.Apply();
        }
        return guiWhiteTex;
    }

    private void SetupNameText()
    {
        GameObject textObj = new GameObject("ItemNameText");
        textObj.transform.SetParent(transform, false);

        nameText = textObj.AddComponent<TextMeshPro>();
        nameText.fontSize = 0.35f;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = new Color(0.92f, 1f, 0.94f, 1f);
        nameText.outlineColor = Color.black;
        nameText.outlineWidth = 0.28f;
        nameText.sortingOrder = 505;
        var textRenderer = nameText.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            textRenderer.sortingLayerName = "Roof";
            textRenderer.sortingOrder = 505;
        }
        nameText.enabled = false;

        bgObject = new GameObject("TextBadgeBackground");
        bgObject.transform.SetParent(transform, false);

        bgRenderer = bgObject.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = GetOrCreateBgSprite();
        bgRenderer.color = new Color(0.04f, 0.08f, 0.05f, 0.85f);
        bgRenderer.sortingLayerName = "Roof";
        bgRenderer.sortingOrder = 502;
        bgObject.SetActive(false);
    }

    /// <summary>
    /// Shows the indicator over the specified item with its display name and quantity.
    /// </summary>
    public void Show(GameObject itemObj, string displayName)
    {
        if (itemObj == null)
        {
            Hide();
            return;
        }

        currentTarget = itemObj;
        currentDisplayName = displayName ?? "";
        targetSpriteRenderer = itemObj.GetComponentInChildren<SpriteRenderer>();
        targetPolygonCollider = itemObj.GetComponent<PolygonCollider2D>();
        if (targetPolygonCollider == null)
        {
            targetPolygonCollider = itemObj.GetComponentInChildren<PolygonCollider2D>();
        }
        targetFallbackCollider = itemObj.GetComponent<Collider2D>();

        if (nameText != null)
        {
            nameText.text = currentDisplayName;
        }

        if (lineRenderer != null) lineRenderer.enabled = true;

        UpdateIndicatorTransform();
    }

    /// <summary>
    /// Hides the indicator.
    /// </summary>
    public void Hide()
    {
        currentTarget = null;
        targetSpriteRenderer = null;
        targetPolygonCollider = null;
        targetFallbackCollider = null;
        currentDisplayName = "";

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

        Camera cam = Camera.main;
        float ortho = (cam != null && cam.orthographic) ? cam.orthographicSize : 0.4f;
        float dynamicBorderWidth = Mathf.Clamp(ortho * 0.009f, 0.002f, 0.012f);
        float worldPad = Mathf.Clamp(ortho * 0.008f, 0.0015f, 0.015f);

        if (lineRenderer != null)
        {
            lineRenderer.startWidth = dynamicBorderWidth;
            lineRenderer.endWidth = dynamicBorderWidth;
        }

        // 1. Prefer tracing the PolygonCollider2D contour if present
        if (targetPolygonCollider != null && targetPolygonCollider.pathCount > 0)
        {
            int bestPathIdx = 0;
            int maxPts = 0;
            for (int p = 0; p < targetPolygonCollider.pathCount; p++)
            {
                var candidate = targetPolygonCollider.GetPath(p);
                if (candidate != null && candidate.Length > maxPts)
                {
                    maxPts = candidate.Length;
                    bestPathIdx = p;
                }
            }

            Vector2[] localPts = targetPolygonCollider.GetPath(bestPathIdx);
            if (localPts != null && localPts.Length >= 3)
            {
                Transform t = targetPolygonCollider.transform;
                Vector2 offset = targetPolygonCollider.offset;

                // Compute polygon centroid in world space for subtle outward border padding
                Vector3 worldCentroid = Vector3.zero;
                Vector3[] worldPts = new Vector3[localPts.Length];
                for (int i = 0; i < localPts.Length; i++)
                {
                    Vector3 wp = t.TransformPoint(localPts[i] + offset);
                    wp.z = 0f;
                    worldPts[i] = wp;
                    worldCentroid += wp;
                }
                worldCentroid /= localPts.Length;

                float minX = float.MaxValue, maxX = float.MinValue;
                float topY = float.MinValue;

                for (int i = 0; i < worldPts.Length; i++)
                {
                    Vector3 dir = worldPts[i] - worldCentroid;
                    if (dir.sqrMagnitude > 0.0000001f)
                    {
                        worldPts[i] += dir.normalized * (worldPad * 0.5f);
                    }
                    if (worldPts[i].x < minX) minX = worldPts[i].x;
                    if (worldPts[i].x > maxX) maxX = worldPts[i].x;
                    if (worldPts[i].y > topY) topY = worldPts[i].y;
                }

                if (lineRenderer != null && lineRenderer.enabled)
                {
                    lineRenderer.loop = true;
                    lineRenderer.positionCount = worldPts.Length;
                    lineRenderer.SetPositions(worldPts);
                }

                topCenterWorldPos = new Vector3((minX + maxX) * 0.5f, topY, 0f);
                return;
            }
        }

        // 2. Fallback for non-polygon objects (e.g. BoxCollider2D on buttons)
        Vector3 p0, p1, p2, p3;
        if (targetFallbackCollider is BoxCollider2D boxCol)
        {
            Transform t = boxCol.transform;
            Vector2 half = boxCol.size * 0.5f;
            Vector2 off = boxCol.offset;
            p0 = t.TransformPoint(new Vector3(off.x - half.x, off.y - half.y, 0f));
            p1 = t.TransformPoint(new Vector3(off.x - half.x, off.y + half.y, 0f));
            p2 = t.TransformPoint(new Vector3(off.x + half.x, off.y + half.y, 0f));
            p3 = t.TransformPoint(new Vector3(off.x + half.x, off.y - half.y, 0f));
            p0.z = p1.z = p2.z = p3.z = 0f;
        }
        else if (targetSpriteRenderer != null && targetSpriteRenderer.sprite != null)
        {
            Bounds wb = targetSpriteRenderer.bounds;
            p0 = new Vector3(wb.min.x - worldPad, wb.min.y - worldPad, 0f);
            p1 = new Vector3(wb.min.x - worldPad, wb.max.y + worldPad, 0f);
            p2 = new Vector3(wb.max.x + worldPad, wb.max.y + worldPad, 0f);
            p3 = new Vector3(wb.max.x + worldPad, wb.min.y - worldPad, 0f);
        }
        else
        {
            Bounds wb = (targetFallbackCollider != null && targetFallbackCollider.bounds.size.sqrMagnitude > 0.00001f)
                ? targetFallbackCollider.bounds
                : new Bounds(currentTarget.transform.position, Vector3.one * 0.04f);
            p0 = new Vector3(wb.min.x - worldPad, wb.min.y - worldPad, 0f);
            p1 = new Vector3(wb.min.x - worldPad, wb.max.y + worldPad, 0f);
            p2 = new Vector3(wb.max.x + worldPad, wb.max.y + worldPad, 0f);
            p3 = new Vector3(wb.max.x + worldPad, wb.min.y - worldPad, 0f);
        }

        if (lineRenderer != null && lineRenderer.enabled)
        {
            lineRenderer.loop = true;
            lineRenderer.positionCount = 4;
            lineRenderer.SetPosition(0, p0);
            lineRenderer.SetPosition(1, p1);
            lineRenderer.SetPosition(2, p2);
            lineRenderer.SetPosition(3, p3);
        }

        float fallbackTopY = Mathf.Max(p0.y, p1.y, p2.y, p3.y);
        float fallbackCenterX = (p0.x + p1.x + p2.x + p3.x) * 0.25f;
        topCenterWorldPos = new Vector3(fallbackCenterX, fallbackTopY, 0f);
    }

    private void OnGUI()
    {
        if (currentTarget == null || !currentTarget.activeInHierarchy || string.IsNullOrEmpty(currentDisplayName))
            return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(topCenterWorldPos);
        if (screenPos.z < 0f) return;

        if (labelTextStyle == null)
        {
            labelTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                wordWrap = false
            };
            labelTextStyle.normal.textColor = new Color(0.88f, 1f, 0.90f, 1f);
        }

        GUIContent content = new GUIContent(currentDisplayName);
        Vector2 textSize = labelTextStyle.CalcSize(content);
        float boxWidth = Mathf.Max(76f, textSize.x + 18f);
        float boxHeight = Mathf.Max(24f, textSize.y + 8f);

        float guiX = screenPos.x - boxWidth * 0.5f;
        float guiY = (Screen.height - screenPos.y) - boxHeight - 6f;

        guiX = Mathf.Clamp(guiX, 4f, Screen.width - boxWidth - 4f);
        guiY = Mathf.Clamp(guiY, 4f, Screen.height - boxHeight - 4f);

        Rect outerRect = new Rect(guiX - 1f, guiY - 1f, boxWidth + 2f, boxHeight + 2f);
        Rect innerRect = new Rect(guiX, guiY, boxWidth, boxHeight);

        Texture2D tex = GetGuiWhiteTex();
        Color prevColor = GUI.color;

        GUI.color = borderColor;
        GUI.DrawTexture(outerRect, tex);

        GUI.color = new Color(0.04f, 0.09f, 0.05f, 0.92f);
        GUI.DrawTexture(innerRect, tex);

        GUI.color = Color.black;
        Rect shadowRect = new Rect(innerRect.x + 1f, innerRect.y + 1f, innerRect.width, innerRect.height);
        GUI.Label(shadowRect, content, labelTextStyle);

        GUI.color = Color.white;
        GUI.Label(innerRect, content, labelTextStyle);

        GUI.color = prevColor;
    }
}
