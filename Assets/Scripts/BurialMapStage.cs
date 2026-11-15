using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Off-bounds Burial Map pit stage positioned far outside the playable neighborhood at (25, 25, 0).
/// Built statically in BarangayMap.unity via BarangayMapSceneBuilder AND auto-constructed at runtime
/// if absent (e.g. in GameMapScene.unity) so local victim death cutscenes always have a valid pit stage.
/// </summary>
public class BurialMapStage : MonoBehaviour
{
    public static readonly Vector3 STAGE_CENTER = new Vector3(25f, 25f, 0f);

    public static BurialMapStage Instance { get; private set; }

    [SerializeField] private Transform cameraAnchor;
    [SerializeField] private Light2D burialLight;
    [SerializeField] private SpriteRenderer burialGhostRenderer;

    private static Sprite[] s_GhostFrames;
    private float animTimer;
    private bool stageActive;

    public Transform CameraAnchor
    {
        get
        {
            if (cameraAnchor == null)
            {
                cameraAnchor = transform.Find("BurialStageAnchor") ?? transform;
            }
            return cameraAnchor;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildStageIfEmpty();
        SetStageActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static BurialMapStage EnsureAndGet()
    {
        if (Instance != null)
        {
            Instance.BuildStageIfEmpty();
            return Instance;
        }

        var found = FindAnyObjectByType<BurialMapStage>(FindObjectsInactive.Include);
        if (found != null)
        {
            Instance = found;
            found.BuildStageIfEmpty();
            return found;
        }

        GameObject go = new GameObject("BurialMapStage");
        go.transform.position = STAGE_CENTER;
        var stage = go.AddComponent<BurialMapStage>();
        stage.BuildStageIfEmpty();
        return stage;
    }

    /// <summary>
    /// Populates the off-bounds pit geometry inside <paramref name="stageRoot"/>.
    /// Safe to invoke both in Edit Mode (BarangayMapSceneBuilder) and Play Mode fallback.
    /// </summary>
    public static void BuildStageHierarchy(GameObject stageRoot)
    {
        if (stageRoot == null) return;
        stageRoot.transform.position = STAGE_CENTER;

        if (stageRoot.transform.Find("BurialStageAnchor") != null) return;

        // 1. Anchor centered at (25, 25, 0) for CameraFollow override
        GameObject anchorGo = new GameObject("BurialStageAnchor");
        anchorGo.transform.SetParent(stageRoot.transform, false);
        anchorGo.transform.localPosition = Vector3.zero;

        // 2. Damp loamy dirt floor sized safely larger than orthographicSize=0.3 viewport (~1.40 x 0.96)
        Sprite floorSprite = PixelScenePostProcessor.LoadPixelSprite("BarangayMap/Burial/burial_dirt_floor", 64f);
        GameObject floorGo = new GameObject("PitFloor");
        floorGo.transform.SetParent(stageRoot.transform, false);
        floorGo.transform.localPosition = Vector3.zero;
        var floorSr = floorGo.AddComponent<SpriteRenderer>();
        floorSr.sprite = floorSprite;
        floorSr.drawMode = SpriteDrawMode.Tiled;
        floorSr.size = new Vector2(1.50f, 1.05f);
        floorSr.sortingLayerName = "Floor";
        floorSr.sortingOrder = -5;
        floorSr.color = Color.white;

        // 3. Raised earthen pit walls framing the viewport perimeter
        Sprite wallSprite = PixelScenePostProcessor.LoadPixelSprite("BarangayMap/Burial/burial_dirt_wall", 64f);
        CreatePitWall(stageRoot.transform, "PitWall_North", new Vector3(0f, 0.31f, 0f), new Vector2(1.40f, 0.14f), wallSprite);
        CreatePitWall(stageRoot.transform, "PitWall_South", new Vector3(0f, -0.31f, 0f), new Vector2(1.40f, 0.14f), wallSprite);
        CreatePitWall(stageRoot.transform, "PitWall_West",  new Vector3(-0.52f, 0f, 0f), new Vector2(0.16f, 0.72f), wallSprite);
        CreatePitWall(stageRoot.transform, "PitWall_East",  new Vector3(0.52f, 0f, 0f), new Vector2(0.16f, 0.72f), wallSprite);

        // 4. Scattered buried roots & skull clusters along corners
        Sprite rootSkull = PixelScenePostProcessor.LoadPixelSprite("BarangayMap/Burial/burial_roots_skull", 64f);
        Vector3[] propOffsets =
        {
            new Vector3(-0.26f,  0.14f, 0f),
            new Vector3( 0.28f,  0.12f, 0f),
            new Vector3(-0.22f, -0.15f, 0f),
            new Vector3( 0.24f, -0.14f, 0f)
        };
        for (int i = 0; i < propOffsets.Length; i++)
        {
            GameObject propGo = new GameObject($"PitRootsSkull_{i}");
            propGo.transform.SetParent(stageRoot.transform, false);
            propGo.transform.localPosition = propOffsets[i];
            propGo.transform.localScale = new Vector3(0.14f, 0.14f, 1f);
            propGo.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 15f : -22f) + i * 35f);
            var propSr = propGo.AddComponent<SpriteRenderer>();
            propSr.sprite = rootSkull;
            propSr.sortingLayerName = "Item";
            propSr.sortingOrder = 1;
        }

        // 5. Local grave pit 2D point light (inactive until death burial phase starts)
        GameObject lightGo = new GameObject("BurialLight2D");
        lightGo.transform.SetParent(stageRoot.transform, false);
        lightGo.transform.localPosition = Vector3.zero;
        var light2d = lightGo.AddComponent<Light2D>();
        light2d.lightType = Light2D.LightType.Point;
        light2d.color = new Color(0.55f, 0.68f, 0.84f, 1f);
        light2d.intensity = 0.65f;
        light2d.falloffIntensity = 0.68f;
        light2d.pointLightInnerAngle = 360f;
        light2d.pointLightOuterAngle = 360f;
        light2d.pointLightInnerRadius = 0.14f;
        light2d.pointLightOuterRadius = 0.78f;
        light2d.shadowsEnabled = false;
        light2d.targetSortingLayers = System.Array.ConvertAll(SortingLayer.layers, l => l.id);
        light2d.enabled = false;

        // 6. Local BurialGhost actor (top edge of grave facing downward)
        GameObject ghostGo = new GameObject("BurialGhost");
        ghostGo.transform.SetParent(stageRoot.transform, false);
        ghostGo.transform.localPosition = new Vector3(0f, 0.21f, 0f);
        ghostGo.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        ghostGo.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
        var ghostSr = ghostGo.AddComponent<SpriteRenderer>();
        ghostSr.sprite = PixelScenePostProcessor.LoadPixelSprite("ghost_hunt_0", 100f)
            ?? PixelScenePostProcessor.LoadPixelSprite("ghost_idle_0", 100f);
        ghostSr.sortingLayerName = "DOTSLayer";
        ghostSr.sortingOrder = 10;
        ghostSr.enabled = false;

        var comp = stageRoot.GetComponent<BurialMapStage>();
        if (comp != null)
        {
            comp.cameraAnchor = anchorGo.transform;
            comp.burialLight = light2d;
            comp.burialGhostRenderer = ghostSr;
        }
    }

    private static void CreatePitWall(Transform parent, string name, Vector3 localPos, Vector2 size, Sprite wallSprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = wallSprite;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = size;
        sr.sortingLayerName = "Wall";
        sr.sortingOrder = 5;
        sr.color = Color.white;
    }

    public void BuildStageIfEmpty()
    {
        BuildStageHierarchy(gameObject);
        if (cameraAnchor == null) cameraAnchor = transform.Find("BurialStageAnchor");
        if (burialLight == null) burialLight = transform.Find("BurialLight2D")?.GetComponent<Light2D>();
        if (burialGhostRenderer == null) burialGhostRenderer = transform.Find("BurialGhost")?.GetComponent<SpriteRenderer>();
    }

    public void SetStageActive(bool active)
    {
        stageActive = active;
        BuildStageIfEmpty();

        if (burialLight != null)
        {
            burialLight.enabled = active;
        }

        if (burialGhostRenderer != null)
        {
            burialGhostRenderer.enabled = active;
            if (active)
            {
                SetLungeProgress(0f);
            }
        }
    }

    /// <summary>
    /// Moves and scales the BurialGhost smoothly along a quadratic acceleration curve toward the pit camera center.
    /// t = 0: standing still at pit edge; t = 1: right in front of the camera lens.
    /// </summary>
    public void SetLungeProgress(float t)
    {
        if (burialGhostRenderer == null) return;
        float clamped = Mathf.Clamp01(t);
        float quad = clamped * clamped;

        Vector3 startLocal = new Vector3(0f, 0.21f, 0f);
        Vector3 endLocal = new Vector3(0f, 0.01f, 0f);
        burialGhostRenderer.transform.localPosition = Vector3.Lerp(startLocal, endLocal, quad);

        float scale = Mathf.Lerp(0.24f, 0.54f, quad);
        burialGhostRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void Update()
    {
        if (!stageActive || burialGhostRenderer == null || !burialGhostRenderer.enabled) return;

        EnsureGhostFramesLoaded();
        if (s_GhostFrames == null || s_GhostFrames.Length == 0) return;

        animTimer += Time.deltaTime * 8f;
        int idx = Mathf.FloorToInt(animTimer) % s_GhostFrames.Length;
        if (s_GhostFrames[idx] != null)
        {
            burialGhostRenderer.sprite = s_GhostFrames[idx];
        }
    }

    private static void EnsureGhostFramesLoaded()
    {
        if (s_GhostFrames != null && s_GhostFrames.Length > 0 && s_GhostFrames[0] != null) return;
        s_GhostFrames = new[]
        {
            PixelScenePostProcessor.LoadPixelSprite("ghost_hunt_0", 100f),
            PixelScenePostProcessor.LoadPixelSprite("ghost_hunt_1", 100f)
        };
    }
}
