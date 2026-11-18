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

    public const string DefaultCannotReachMessage = "I can't reach this!";

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
    private Camera cachedMainCam;
    private bool isShowing = false;
    private GUIContent cachedGuiContent;
    private string cachedGuiContentText = "";
    private Vector2 cachedLabelSize;

    // Floating player speech bubble state
    private Transform speechBubbleTarget;
    private string speechBubbleText = "";
    private float speechBubbleStartTime = -10f;
    private float speechBubbleDuration = 1.35f;
    private bool isSpeechBubbleActive = false;
    private GUIStyle speechBubbleStyle;
    private GUIContent cachedSpeechContent;
    private string cachedSpeechContentText = "";
    private Vector2 cachedSpeechSize;

    public bool IsSpeechBubbleVisible =>
        isSpeechBubbleActive &&
        speechBubbleTarget != null &&
        (Time.unscaledTime - speechBubbleStartTime) < speechBubbleDuration &&
        !string.IsNullOrEmpty(speechBubbleText);

    public string CurrentSpeechBubbleText => IsSpeechBubbleVisible ? speechBubbleText : "";
    public Transform CurrentSpeechBubbleTarget => IsSpeechBubbleVisible ? speechBubbleTarget : null;

    // Reusable buffers & transform dirty state to prevent per-frame PolygonCollider allocations
    private readonly System.Collections.Generic.List<Vector2> reusableLocalPts = new System.Collections.Generic.List<Vector2>(32);
    private Vector3[] reusableWorldPts = System.Array.Empty<Vector3>();
    private PolygonCollider2D cachedPolyTarget;
    private int cachedPolyBestPathIdx = -1;
    private Vector3 lastTargetPos;
    private Quaternion lastTargetRot;
    private Vector3 lastTargetScale;
    private float lastCamOrtho = -1f;
    private GameObject lastTransformTarget;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
            return;
        }
        Instance = this;
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }

        SetupLineRenderer();
        SetupNameText();
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
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
        var pixelTmpFont = PamahiinUIThemeUtility.ResolvePixelTmpFont();
        if (pixelTmpFont != null)
        {
            nameText.font = pixelTmpFont;
        }
        nameText.fontSize = 0.32f;
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

        bool sameTarget = isShowing && currentTarget == itemObj;
        currentTarget = itemObj;
        isShowing = true;
        currentDisplayName = displayName ?? "";

        if (!sameTarget)
        {
            targetSpriteRenderer = itemObj.GetComponentInChildren<SpriteRenderer>();
            targetPolygonCollider = itemObj.GetComponent<PolygonCollider2D>();
            if (targetPolygonCollider == null)
            {
                targetPolygonCollider = itemObj.GetComponentInChildren<PolygonCollider2D>();
            }
            targetFallbackCollider = SelectEnabledFallbackCollider(itemObj);
            lastTransformTarget = null;
        }

        if (nameText != null && nameText.text != currentDisplayName)
        {
            nameText.text = currentDisplayName;
        }

        if (lineRenderer != null && !lineRenderer.enabled) lineRenderer.enabled = true;

        if (!sameTarget)
        {
            UpdateIndicatorTransform();
        }
    }

    private static Collider2D SelectEnabledFallbackCollider(GameObject itemObj)
    {
        Collider2D[] cols = itemObj.GetComponents<Collider2D>();
        Collider2D firstEnabled = null;
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || !cols[i].enabled) continue;
            if (cols[i] is BoxCollider2D) return cols[i];
            if (firstEnabled == null) firstEnabled = cols[i];
        }
        return firstEnabled ?? itemObj.GetComponent<Collider2D>();
    }

    /// <summary>
    /// Hides the indicator.
    /// </summary>
    public void Hide()
    {
        isShowing = false;
        currentTarget = null;
        targetSpriteRenderer = null;
        targetPolygonCollider = null;
        targetFallbackCollider = null;
        cachedPolyTarget = null;
        cachedPolyBestPathIdx = -1;
        lastTransformTarget = null;
        currentDisplayName = "";

        if (lineRenderer != null && lineRenderer.enabled) lineRenderer.enabled = false;
        if (nameText != null && nameText.enabled) nameText.enabled = false;
        if (bgObject != null && bgObject.activeSelf) bgObject.SetActive(false);
    }

    public static void TriggerSpeechBubble(Transform playerTransform, string message = DefaultCannotReachMessage, float duration = 1.35f)
    {
        if (playerTransform == null) return;
        if (Instance == null)
        {
            var go = new GameObject("ItemHoverIndicator");
            var indicator = go.AddComponent<ItemHoverIndicator>();
            if (Instance == null)
            {
                Instance = indicator;
            }
        }
        Instance?.ShowPlayerSpeechBubble(playerTransform, message, duration);
    }

    public void ShowPlayerSpeechBubble(Transform playerTransform, string message = DefaultCannotReachMessage, float duration = 1.35f)
    {
        if (playerTransform == null || string.IsNullOrEmpty(message)) return;
        speechBubbleTarget = playerTransform;
        speechBubbleText = message;
        speechBubbleDuration = Mathf.Max(0.25f, duration);
        speechBubbleStartTime = Time.unscaledTime;
        isSpeechBubbleActive = true;
    }

    public void HideSpeechBubble()
    {
        isSpeechBubbleActive = false;
        speechBubbleTarget = null;
        speechBubbleText = "";
    }

    private Camera ResolveMainCamera()
    {
        if (cachedMainCam != null && cachedMainCam.isActiveAndEnabled) return cachedMainCam;
        cachedMainCam = Camera.main;
        return cachedMainCam;
    }

    private void LateUpdate()
    {
        if (currentTarget == null || !currentTarget.activeInHierarchy)
        {
            if (isShowing) Hide();
            return;
        }

        UpdateIndicatorTransform();
    }

    private void UpdateIndicatorTransform()
    {
        if (currentTarget == null) return;

        Camera cam = ResolveMainCamera();
        float ortho = (cam != null && cam.orthographic) ? cam.orthographicSize : 0.4f;

        Transform targetTr = currentTarget.transform;
        if (lastTransformTarget == currentTarget &&
            lastTargetPos == targetTr.position &&
            lastTargetRot == targetTr.rotation &&
            lastTargetScale == targetTr.lossyScale &&
            Mathf.Abs(lastCamOrtho - ortho) < 0.0005f)
        {
            return;
        }

        lastTransformTarget = currentTarget;
        lastTargetPos = targetTr.position;
        lastTargetRot = targetTr.rotation;
        lastTargetScale = targetTr.lossyScale;
        lastCamOrtho = ortho;

        float dynamicBorderWidth = Mathf.Clamp(ortho * 0.009f, 0.002f, 0.012f);
        float worldPad = Mathf.Clamp(ortho * 0.008f, 0.0015f, 0.015f);

        if (lineRenderer != null)
        {
            lineRenderer.startWidth = dynamicBorderWidth;
            lineRenderer.endWidth = dynamicBorderWidth;
        }

        // 1. Prefer tracing the PolygonCollider2D contour if present (zero per-frame GetPath allocations)
        if (targetPolygonCollider != null && targetPolygonCollider.pathCount > 0)
        {
            if (cachedPolyTarget != targetPolygonCollider || cachedPolyBestPathIdx < 0 || cachedPolyBestPathIdx >= targetPolygonCollider.pathCount)
            {
                cachedPolyTarget = targetPolygonCollider;
                cachedPolyBestPathIdx = 0;
                int maxPts = 0;
                for (int p = 0; p < targetPolygonCollider.pathCount; p++)
                {
                    targetPolygonCollider.GetPath(p, reusableLocalPts);
                    if (reusableLocalPts.Count > maxPts)
                    {
                        maxPts = reusableLocalPts.Count;
                        cachedPolyBestPathIdx = p;
                    }
                }
            }

            targetPolygonCollider.GetPath(cachedPolyBestPathIdx, reusableLocalPts);
            int ptCount = reusableLocalPts.Count;
            if (ptCount >= 3)
            {
                if (reusableWorldPts.Length != ptCount)
                {
                    reusableWorldPts = new Vector3[ptCount];
                }

                Transform t = targetPolygonCollider.transform;
                Vector2 offset = targetPolygonCollider.offset;

                // Compute polygon centroid in world space for subtle outward border padding
                Vector3 worldCentroid = Vector3.zero;
                for (int i = 0; i < ptCount; i++)
                {
                    Vector3 wp = t.TransformPoint(reusableLocalPts[i] + offset);
                    wp.z = 0f;
                    reusableWorldPts[i] = wp;
                    worldCentroid += wp;
                }
                worldCentroid /= ptCount;

                float minX = float.MaxValue, maxX = float.MinValue;
                float topY = float.MinValue;

                for (int i = 0; i < ptCount; i++)
                {
                    Vector3 dir = reusableWorldPts[i] - worldCentroid;
                    if (dir.sqrMagnitude > 0.0000001f)
                    {
                        reusableWorldPts[i] += dir.normalized * (worldPad * 0.5f);
                    }
                    if (reusableWorldPts[i].x < minX) minX = reusableWorldPts[i].x;
                    if (reusableWorldPts[i].x > maxX) maxX = reusableWorldPts[i].x;
                    if (reusableWorldPts[i].y > topY) topY = reusableWorldPts[i].y;
                }

                if (lineRenderer != null && lineRenderer.enabled)
                {
                    lineRenderer.loop = true;
                    lineRenderer.positionCount = ptCount;
                    lineRenderer.SetPositions(reusableWorldPts);
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
        Camera cam = ResolveMainCamera();
        if (cam == null) return;

        DrawHoveredItemLabelGUI(cam);
        DrawPlayerSpeechBubbleGUI(cam);
    }

    private void DrawHoveredItemLabelGUI(Camera cam)
    {
        if (!isShowing || currentTarget == null || !currentTarget.activeInHierarchy || string.IsNullOrEmpty(currentDisplayName))
            return;

        Vector3 screenPos = cam.WorldToScreenPoint(topCenterWorldPos);
        if (screenPos.z < 0f) return;

        if (labelTextStyle == null)
        {
            Font pixelFont = PamahiinUIThemeUtility.ResolvePixelFont();
            labelTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                font = pixelFont,
                fontStyle = FontStyle.Normal,
                fontSize = 11,
                wordWrap = false
            };
            labelTextStyle.normal.textColor = new Color(0.95f, 0.91f, 0.84f, 1f);
        }
        else if (labelTextStyle.font == null)
        {
            Font resolved = PamahiinUIThemeUtility.ResolvePixelFont();
            if (resolved != null)
            {
                labelTextStyle.font = resolved;
                cachedGuiContentText = null;
            }
        }

        if (cachedGuiContent == null || cachedGuiContentText != currentDisplayName)
        {
            cachedGuiContentText = currentDisplayName;
            cachedGuiContent = new GUIContent(currentDisplayName);
            cachedLabelSize = labelTextStyle.CalcSize(cachedGuiContent);
        }

        float boxWidth = Mathf.Max(84f, cachedLabelSize.x + 20f);
        float boxHeight = Mathf.Max(24f, cachedLabelSize.y + 10f);

        float guiX = screenPos.x - boxWidth * 0.5f;
        float guiY = (Screen.height - screenPos.y) - boxHeight - 6f;

        guiX = Mathf.Clamp(guiX, 4f, Screen.width - boxWidth - 4f);
        guiY = Mathf.Clamp(guiY, 4f, Screen.height - boxHeight - 4f);

        Rect outerRect = new Rect(guiX - 2f, guiY - 2f, boxWidth + 4f, boxHeight + 4f);
        Rect innerRect = new Rect(guiX, guiY, boxWidth, boxHeight);

        Texture2D tex = GetGuiWhiteTex();
        Color prevColor = GUI.color;

        // Aged brass outer pixel border (16-bit Pamahiin theme)
        GUI.color = new Color(0.72f, 0.56f, 0.30f, 1f);
        GUI.DrawTexture(outerRect, tex);

        // Dark obsidian inner fill
        GUI.color = new Color(0.06f, 0.05f, 0.07f, 0.96f);
        GUI.DrawTexture(innerRect, tex);

        GUI.color = Color.black;
        Rect shadowRect = new Rect(innerRect.x + 1f, innerRect.y + 1f, innerRect.width, innerRect.height);
        GUI.Label(shadowRect, cachedGuiContent, labelTextStyle);

        GUI.color = Color.white;
        GUI.Label(innerRect, cachedGuiContent, labelTextStyle);

        GUI.color = prevColor;
    }

    private void DrawPlayerSpeechBubbleGUI(Camera cam)
    {
        if (!IsSpeechBubbleVisible)
        {
            isSpeechBubbleActive = false;
            return;
        }

        float elapsed = Mathf.Max(0f, Time.unscaledTime - speechBubbleStartTime);
        float normalized = Mathf.Clamp01(elapsed / speechBubbleDuration);
        float fadeWindow = Mathf.Min(0.35f, speechBubbleDuration * 0.4f);
        float remaining = speechBubbleDuration - elapsed;
        float alpha = (fadeWindow > 0.001f && remaining < fadeWindow)
            ? Mathf.Clamp01(remaining / fadeWindow)
            : 1f;
        if (alpha <= 0.01f) return;

        Vector3 bubbleWorldPos = speechBubbleTarget.position + new Vector3(0f, 0.065f + normalized * 0.015f, 0f);
        Vector3 screenPos = cam.WorldToScreenPoint(bubbleWorldPos);
        if (screenPos.z < 0f) return;

        if (speechBubbleStyle == null)
        {
            Font pixelFont = PamahiinUIThemeUtility.ResolvePixelFont();
            speechBubbleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                font = pixelFont,
                fontStyle = FontStyle.Normal,
                fontSize = 11,
                wordWrap = false
            };
            speechBubbleStyle.normal.textColor = new Color(0.96f, 0.93f, 0.88f, 1f);
        }
        else if (speechBubbleStyle.font == null)
        {
            Font resolved = PamahiinUIThemeUtility.ResolvePixelFont();
            if (resolved != null)
            {
                speechBubbleStyle.font = resolved;
                cachedSpeechContentText = null;
            }
        }

        if (cachedSpeechContent == null || cachedSpeechContentText != speechBubbleText)
        {
            cachedSpeechContentText = speechBubbleText;
            cachedSpeechContent = new GUIContent(speechBubbleText);
            cachedSpeechSize = speechBubbleStyle.CalcSize(cachedSpeechContent);
        }

        float boxWidth = Mathf.Max(92f, cachedSpeechSize.x + 18f);
        float boxHeight = Mathf.Max(22f, cachedSpeechSize.y + 8f);

        float guiX = Mathf.Clamp(screenPos.x - boxWidth * 0.5f, 4f, Screen.width - boxWidth - 4f);
        float guiY = Mathf.Clamp((Screen.height - screenPos.y) - boxHeight - 6f, 4f, Screen.height - boxHeight - 8f);

        Rect outerRect = new Rect(guiX - 2f, guiY - 2f, boxWidth + 4f, boxHeight + 4f);
        Rect innerRect = new Rect(guiX, guiY, boxWidth, boxHeight);

        float tailCenterX = Mathf.Clamp(screenPos.x, guiX + 6f, guiX + boxWidth - 6f);
        Rect tailOuterRect = new Rect(tailCenterX - 3f, guiY + boxHeight + 1f, 6f, 4f);
        Rect tailInnerRect = new Rect(tailCenterX - 2f, guiY + boxHeight, 4f, 3f);

        Texture2D tex = GetGuiWhiteTex();
        Color prevColor = GUI.color;

        // Subtle aged-ivory/gray border with pointer tick underneath
        GUI.color = new Color(0.75f, 0.72f, 0.65f, 0.92f * alpha);
        GUI.DrawTexture(outerRect, tex);
        GUI.DrawTexture(tailOuterRect, tex);

        // Dark obsidian inner fill
        GUI.color = new Color(0.06f, 0.05f, 0.07f, 0.94f * alpha);
        GUI.DrawTexture(innerRect, tex);
        GUI.DrawTexture(tailInnerRect, tex);

        GUI.color = new Color(0f, 0f, 0f, alpha);
        Rect shadowRect = new Rect(innerRect.x + 1f, innerRect.y + 1f, innerRect.width, innerRect.height);
        GUI.Label(shadowRect, cachedSpeechContent, speechBubbleStyle);

        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(innerRect, cachedSpeechContent, speechBubbleStyle);

        GUI.color = prevColor;
    }
}
