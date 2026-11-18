using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum BarangayWeatherType
{
    NightFireflies = 0,
    Foggy = 1,
    RainThunderstorm = 2
}

/// <summary>
/// Server-authoritative Barangay dynamic weather system:
/// - 48% Night + Fireflies
/// - 25% Foggy (reduced outdoor Light2D radius + crisper indoor audio)
/// - 25% Rain & Thunderstorm (animated rain overlay, road puddles, lightning flashes, outdoor footstep ducking)
/// - 2% Red Moon (crimson sky tint, faster ghost events, extended hunts)
/// Also supports live overrides via Developer Console buttons.
/// </summary>
public class WeatherManager : NetworkBehaviour
{
    public static WeatherManager Instance { get; private set; }
    public static bool IsLocalInBurialMap { get; private set; }

    public static void SetLocalInBurialMap(bool inBurial)
    {
        IsLocalInBurialMap = inBurial;
        if (Instance != null)
        {
            if (inBurial)
            {
                Instance.lightningFlashAlpha = 0f;
                if (Instance.weatherAudioSource != null)
                {
                    Instance.weatherAudioSource.volume = 0f;
                }
            }
            Instance.hasAppliedGlobalLight = false;
            Instance.UpdateGlobalLighting();
            Instance.UpdateWeatherAudioProximity(immediate: true);
            if (Instance.CurrentWeather == BarangayWeatherType.RainThunderstorm)
            {
                Instance.UpdateRainEntities(false, default);
            }
        }
    }

    private const float FOG_OUTDOOR_LIGHT_MULTIPLIER = 0.65f;
    private const float FOG_INDOOR_AUDIO_CLARITY = 1.2f;
    private const float RAIN_OUTDOOR_FOOTSTEP_MULTIPLIER = 0.45f;
    private const float REDMOON_EVENT_INTERVAL_MULT = 0.55f;
    private const float REDMOON_HUNT_DURATION_MULT = 1.35f;

    [Header("Ambient References (Auto-Located If Null)")]
    [SerializeField] private Light2D globalLight;

    [Header("Custom Map Exterior Bounds (CemeteryMap)")]
    [SerializeField] private bool useCustomExteriorBounds = false;
    [SerializeField] private Rect[] customExteriorZones =
    {
        new Rect(-1.72f, -3.15f, 1.38f, 6.60f),
        new Rect(0.00f, -3.10f, 8.50f, 6.50f)
    };
    [SerializeField] private int customFireflyCount = 128;
    [SerializeField] private int customHeroFireflyLights = 18;
    [SerializeField] private int customFogWispCount = 72;
    [SerializeField] private float customRainGridSpacingX = 0.58f;
    [SerializeField] private float customRainGridSpacingY = 0.54f;
    [SerializeField] private string[] fireflyAnchorLightPrefixes =
    {
        "LampPostLight_",
        "CandleLight_",
        "CemeteryFloodLight_"
    };

    public bool UseCustomExteriorBounds => useCustomExteriorBounds;

    private Rect[] GetActiveExteriorZones()
    {
        return (useCustomExteriorBounds && customExteriorZones != null && customExteriorZones.Length > 0)
            ? customExteriorZones
            : ExteriorZones;
    }

    private NetworkVariable<int> netWeatherType = new NetworkVariable<int>(
        (int)BarangayWeatherType.NightFireflies,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> netIsRedMoon = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public BarangayWeatherType CurrentWeather => (BarangayWeatherType)netWeatherType.Value;
    public bool IsRedMoon => netIsRedMoon.Value;
    public bool IsWarmupReady { get; private set; }

    private struct FireflyEntity
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Light2D heroLight;
        public Vector3 anchorPos;
        public float proximityRadius;
        public float speed;
        public float phase;
        public float animTimer;
        public int currentFrame;
        public float fps;
        public float baseAlpha;
    }

    private struct RainClusterEntity
    {
        public SpriteRenderer renderer;
        public Vector3 worldPos;
        public float animTimer;
        public int currentFrame;
        public float fps;
    }

    private struct FogWispEntity
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector3 anchorPos;
        public float proximityRadius;
        public float speed;
        public float phase;
        public float animTimer;
        public int currentFrame;
        public float fps;
        public float baseAlpha;
    }

    private struct PuddleEntity
    {
        public SpriteRenderer renderer;
        public float animTimer;
        public int currentFrame;
        public float fps;
    }

    // Strictly exterior zones (never overlap MainHouse interior walls/roofs or Basement)
    private static readonly Rect[] ExteriorZones =
    {
        new Rect(-1.68f, -2.70f, 1.32f, 5.35f), // Vertical Barangay street & west sidewalk [-1.68..-0.36]
        new Rect(-0.34f, -1.80f, 0.29f, 3.35f), // East sidewalk & front yard strip [-0.34..-0.05]
        new Rect(-0.30f,  1.70f, 4.85f, 0.88f), // North perimeter alley [y >= 1.70]
        new Rect(-0.30f, -2.65f, 4.85f, 0.74f), // South perimeter alley [y <= -1.91]
        new Rect( 4.53f, -1.85f, 0.86f, 3.45f)  // East perimeter alley [x >= 4.53]
    };

    [Header("Custom Exterior Weather Bounds (Multi-Floor Maps)")]
    [Tooltip("Optional ground-floor building perimeter rect for muffling indoor rain near exterior walls.")]
    [SerializeField] private Rect customBuildingPerimeter;

    private Rect[] ActiveExteriorZones =>
        HasCustomExteriorZones ? customExteriorZones : ExteriorZones;

    private bool HasCustomExteriorZones =>
        (useCustomExteriorBounds || customBuildingPerimeter.width > 0.01f)
        && customExteriorZones != null && customExteriorZones.Length > 0;

    /// <summary>
    /// Assigns map-specific exterior weather zones and ground-floor building perimeter bounds.
    /// </summary>
    public void ConfigureCustomBounds(Rect[] exteriorZones, Rect buildingPerimeter)
    {
        customExteriorZones = exteriorZones;
        customBuildingPerimeter = buildingPerimeter;
    }

    private const int FIREFLY_COUNT = 64;
    private const int HERO_FIREFLY_LIGHTS = 12;
    private const int ROAD_RAIN_ROWS = 12;
    private const int PERIMETER_RAIN_CLUSTER_COUNT = 10;
    private const int FOG_WISP_COUNT = 30;
    private const float VEHICLE_FOG_EXCLUSION_PADDING = 0.18f;
    private const float VEHICLE_RAIN_EXCLUSION_PADDING = 0.03f;
    private const float FOG_HOUSE_WALL_CLEARANCE = 0.34f;

    private enum VehicleExclusionMode
    {
        None,
        Fog,
        Rain
    }

    private AudioSource weatherAudioSource;
    private Transform weatherAudioEmitter;
    private Material fogUnlitMaterial;
    private Sprite[] fireflyFrames;
    private Sprite[] fogFrames;
    private Sprite[] rainFrames;
    private Sprite[] puddleFrames;

    private GameObject firefliesContainer;
    private GameObject rainContainer;
    private GameObject fogContainer;

    private readonly List<FireflyEntity> fireflyEntities = new List<FireflyEntity>();
    private readonly List<RainClusterEntity> rainEntities = new List<RainClusterEntity>();
    private readonly List<FogWispEntity> fogEntities = new List<FogWispEntity>();
    private readonly List<PuddleEntity> puddleEntities = new List<PuddleEntity>();
    private readonly List<RoofFader> cachedRoofs = new List<RoofFader>();
    private readonly List<Rect> vehicleFogExclusionRects = new List<Rect>();
    private readonly List<Rect> vehicleRainExclusionRects = new List<Rect>();
    private readonly List<Rect> enclosedBuildingFogRects = new List<Rect>();
    private readonly Dictionary<Light2D, float> baseLightOuterRadii = new Dictionary<Light2D, float>();
    private readonly List<Light2D> cachedFogAffectedLights = new List<Light2D>();

    private Texture2D runtimeGroupedRainTex;
    private float rainUpdateTimer;
    private float puddleUpdateTimer;
    private Camera cachedMainCamera;
    private float lightningTimer;
    private float lightningFlashAlpha;
    private float lightScanTimer;
    private float fogLightCacheRefreshTimer;
    private float audioBlendTimer;
    private bool localPlayerIndoors;
    private bool hasAppliedGlobalLight;
    private Color lastAppliedGlobalColor;
    private float lastAppliedGlobalIntensity;

    private void Awake()
    {
        Instance = this;
        GameAudioManager.RefreshSceneBasementFlag();
        var emitterGo = new GameObject("WeatherAudioEmitter");
        emitterGo.transform.SetParent(transform, false);
        weatherAudioEmitter = emitterGo.transform;

        weatherAudioSource = emitterGo.AddComponent<AudioSource>();
        weatherAudioSource.loop = true;
        weatherAudioSource.playOnAwake = false;
        GameAudioManager.ConfigureProximityAudioSource(weatherAudioSource, 0.45f, 2.4f, 1.0f);
        weatherAudioSource.volume = 0f;

        if (globalLight == null)
        {
            foreach (var light in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
            {
                if (light.lightType == Light2D.LightType.Global)
                {
                    globalLight = light;
                    break;
                }
            }
        }

        if (globalLight != null)
        {
            // Ensure Global Light 2D illuminates all sorting layers including "Van" and "Wall" even in legacy-baked scenes.
            globalLight.targetSortingLayers = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);
        }

        Shader unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        if (unlitShader != null)
        {
            fogUnlitMaterial = new Material(unlitShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        LoadWeatherSheets();
        CacheRoofsAndPuddles();
        BuildWorldWeatherEntities();
        IsWarmupReady = true;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        RestoreAllLightRadii();
        if (fogUnlitMaterial != null)
        {
            Destroy(fogUnlitMaterial);
            fogUnlitMaterial = null;
        }
        if (runtimeGroupedRainTex != null)
        {
            Destroy(runtimeGroupedRainTex);
            runtimeGroupedRainTex = null;
        }
        if (Instance == this) Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            RollInitialWeather();
        }

        netWeatherType.OnValueChanged += (_, _) => ApplyWeatherState();
        netIsRedMoon.OnValueChanged += (_, _) => ApplyWeatherState();
        ApplyWeatherState();
    }

    private void RollInitialWeather()
    {
        // 48% Night + Fireflies, 25% Foggy, 25% Rain/Thunderstorm, 2% Red Moon
        float roll = Random.value;
        if (roll < 0.02f)
        {
            netWeatherType.Value = (int)BarangayWeatherType.NightFireflies;
            netIsRedMoon.Value = true;
        }
        else if (roll < 0.50f)
        {
            netWeatherType.Value = (int)BarangayWeatherType.NightFireflies;
            netIsRedMoon.Value = false;
        }
        else if (roll < 0.75f)
        {
            netWeatherType.Value = (int)BarangayWeatherType.Foggy;
            netIsRedMoon.Value = false;
        }
        else
        {
            netWeatherType.Value = (int)BarangayWeatherType.RainThunderstorm;
            netIsRedMoon.Value = false;
        }

        Debug.Log($"[WeatherManager] Rolled weather={CurrentWeather}, redMoon={IsRedMoon}");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetWeatherPresetServerRpc(int weatherIndex, bool redMoon)
    {
        netWeatherType.Value = Mathf.Clamp(weatherIndex, 0, 2);
        netIsRedMoon.Value = redMoon;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ToggleRedMoonServerRpc()
    {
        netIsRedMoon.Value = !netIsRedMoon.Value;
    }

    private void LoadWeatherSheets()
    {
        fireflyFrames = SliceSheet("Weather/fireflies_sheet", 8, 128f);
        fogFrames = SliceSheet("Weather/fog_clouds_sheet", 8, 48f);
        rainFrames = LoadOrBuildGroupedRainFrames();
        puddleFrames = SliceSheet("Weather/puddle_ripples_sheet", 7, 64f);
    }

    private Sprite[] LoadOrBuildGroupedRainFrames()
    {
        const int frameCount = 7;
        const int frameW = 64;
        const int frameH = 48;
        const int sheetW = frameW * frameCount;
        const float ppu = 100f;

        Texture2D existing = Resources.Load<Texture2D>("Sprites/BarangayMap/Weather/rain_streaks_sheet");
        if (existing != null && existing.width >= sheetW && existing.height >= frameH)
        {
            return SliceSheet("Weather/rain_streaks_sheet", frameCount, ppu);
        }

        Color32[] pixels = new Color32[sheetW * frameH];
        Color32 streakTail = new Color32(132, 168, 218, 150);
        Color32 streakTip = new Color32(204, 228, 255, 225);
        Color32 ringCol = new Color32(174, 208, 244, 185);
        Color32 fadeCol = new Color32(148, 186, 228, 95);
        int[] dropX = { 4, 10, 17, 23, 29, 35, 42, 48, 54, 60 };
        int[] dropGroundY = { 5, 18, 9, 23, 4, 15, 21, 7, 13, 20 };
        int[] dropPhase = { 0, 3, 5, 1, 4, 6, 2, 0, 3, 5 };

        for (int f = 0; f < frameCount; f++)
        {
            int ox = f * frameW;
            for (int d = 0; d < 10; d++)
            {
                int subF = (f + dropPhase[d]) % frameCount;
                int cx = ox + dropX[d];
                int gy = dropGroundY[d];
                if (subF <= 3)
                {
                    int topY = gy + 22 - subF * 5;
                    int botY = topY - 6;
                    DrawLinePixels(pixels, sheetW, frameH, cx + 1, topY, cx, botY, streakTail);
                    BlendPixel(pixels, sheetW, frameH, cx, botY, streakTip);
                }
                else if (subF == 4)
                {
                    DrawLinePixels(pixels, sheetW, frameH, cx + 1, gy + 4, cx, gy, streakTail);
                    BlendPixel(pixels, sheetW, frameH, cx - 1, gy + 1, streakTip);
                    BlendPixel(pixels, sheetW, frameH, cx + 1, gy + 1, streakTip);
                }
                else if (subF == 5)
                {
                    BlendPixel(pixels, sheetW, frameH, cx - 2, gy, ringCol);
                    BlendPixel(pixels, sheetW, frameH, cx + 2, gy, ringCol);
                    BlendPixel(pixels, sheetW, frameH, cx, gy - 1, ringCol);
                    BlendPixel(pixels, sheetW, frameH, cx, gy + 1, ringCol);
                    BlendPixel(pixels, sheetW, frameH, cx - 1, gy + 3, streakTip);
                    BlendPixel(pixels, sheetW, frameH, cx + 1, gy + 3, streakTip);
                }
                else
                {
                    BlendPixel(pixels, sheetW, frameH, cx - 3, gy, fadeCol);
                    BlendPixel(pixels, sheetW, frameH, cx + 3, gy, fadeCol);
                    BlendPixel(pixels, sheetW, frameH, cx, gy - 1, fadeCol);
                    BlendPixel(pixels, sheetW, frameH, cx, gy + 1, fadeCol);
                }
            }
        }

        runtimeGroupedRainTex = new Texture2D(sheetW, frameH, TextureFormat.RGBA32, false)
        {
            name = "RuntimeGroupedRainSheet",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        runtimeGroupedRainTex.SetPixels32(pixels);
        runtimeGroupedRainTex.Apply(false, true);

        Sprite[] result = new Sprite[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            result[i] = Sprite.Create(
                runtimeGroupedRainTex,
                new Rect(i * frameW, 0, frameW, frameH),
                new Vector2(0.5f, 0.5f),
                ppu,
                0,
                SpriteMeshType.FullRect);
            result[i].name = $"rain_grouped_f{i}";
        }
        return result;
    }

    private static void BlendPixel(Color32[] pixels, int width, int height, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= width || y >= height || c.a == 0) return;
        int idx = y * width + x;
        if (c.a == 255)
        {
            pixels[idx] = c;
            return;
        }
        Color32 dst = pixels[idx];
        float a = c.a / 255f;
        float inv = 1f - a;
        pixels[idx] = new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * a + dst.r * inv), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * a + dst.g * inv), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * a + dst.b * inv), 0, 255),
            (byte)Mathf.Clamp(Mathf.Max(c.a, dst.a), 0, 255));
    }

    private static void DrawLinePixels(Color32[] pixels, int width, int height, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            BlendPixel(pixels, width, height, x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static Sprite[] SliceSheet(string relPath, int count, float ppu)
    {
        Texture2D tex = Resources.Load<Texture2D>($"Sprites/BarangayMap/{relPath}");
        if (tex == null) return System.Array.Empty<Sprite>();
        tex.filterMode = FilterMode.Point;

        int fw = Mathf.Max(1, tex.width / count);
        int fh = tex.height;
        Sprite[] result = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = Sprite.Create(
                tex,
                new Rect(i * fw, 0, fw, fh),
                new Vector2(0.5f, 0.5f),
                ppu,
                0,
                SpriteMeshType.FullRect);
            result[i].name = $"{relPath}_f{i}";
        }
        return result;
    }

    private void CacheRoofsAndPuddles()
    {
        cachedRoofs.Clear();
        cachedRoofs.AddRange(FindObjectsByType<RoofFader>(FindObjectsInactive.Include));
        CacheVehicleExclusionBounds();

        puddleEntities.Clear();
        foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include))
        {
            if (sr != null && sr.gameObject.name.StartsWith("WeatherPuddle"))
            {
                puddleEntities.Add(new PuddleEntity
                {
                    renderer = sr,
                    animTimer = Random.Range(0f, 0.14f),
                    currentFrame = Random.Range(0, 7),
                    fps = Random.Range(7f, 10f)
                });
            }
        }
    }

    private void CacheVehicleExclusionBounds()
    {
        vehicleFogExclusionRects.Clear();
        vehicleRainExclusionRects.Clear();
        enclosedBuildingFogRects.Clear();

        for (int i = 0; i < cachedRoofs.Count; i++)
        {
            RoofFader rf = cachedRoofs[i];
            if (rf == null) continue;
            if (rf.IsEnclosedBuilding)
            {
                AddEnclosedBuildingFogBounds(rf);
                continue;
            }
            AddVehicleExclusionFromGameObject(rf.gameObject);
        }

        foreach (SafeZone sz in FindObjectsByType<SafeZone>(FindObjectsInactive.Exclude))
        {
            if (sz != null)
            {
                AddVehicleExclusionFromGameObject(sz.gameObject);
            }
        }

        GameObject jeepneyBody = GameObject.Find("JeepneyBody");
        if (jeepneyBody != null) AddVehicleExclusionFromGameObject(jeepneyBody);

        GameObject jeepneySafeZone = GameObject.Find("JeepneySafeZone");
        if (jeepneySafeZone != null) AddVehicleExclusionFromGameObject(jeepneySafeZone);
    }

    private void AddEnclosedBuildingFogBounds(RoofFader rf)
    {
        foreach (Collider2D col in rf.GetComponents<Collider2D>())
        {
            if (col == null) continue;
            Bounds bounds = col.bounds;
            if (bounds.size.x <= 0.01f || bounds.size.y <= 0.01f) continue;

            enclosedBuildingFogRects.Add(ExpandBoundsToRect(bounds, FOG_HOUSE_WALL_CLEARANCE));
        }
    }

    private void AddVehicleExclusionFromGameObject(GameObject go)
    {
        if (go == null) return;

        Collider2D col = go.GetComponent<Collider2D>();
        if (col != null)
        {
            TryAddVehicleExclusionBounds(col.bounds);
            return;
        }

        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            TryAddVehicleExclusionBounds(rend.bounds);
        }
    }

    private void TryAddVehicleExclusionBounds(Bounds bounds)
    {
        if (bounds.size.x <= 0.01f || bounds.size.y <= 0.01f) return;

        vehicleFogExclusionRects.Add(ExpandBoundsToRect(bounds, VEHICLE_FOG_EXCLUSION_PADDING));
        vehicleRainExclusionRects.Add(ExpandBoundsToRect(bounds, VEHICLE_RAIN_EXCLUSION_PADDING));
    }

    private static Rect ExpandBoundsToRect(Bounds bounds, float padding)
    {
        return Rect.MinMaxRect(
            bounds.min.x - padding,
            bounds.min.y - padding,
            bounds.max.x + padding,
            bounds.max.y + padding);
    }

    private void BuildWorldWeatherEntities()
    {
        int roofLayerId = SortingLayer.NameToID("Roof");
        int weatherSortingLayerId = roofLayerId != 0 ? roofLayerId : SortingLayer.NameToID("Default");
        int floorLayerId = SortingLayer.NameToID("Floor");
        int fogSortingLayerId = floorLayerId != 0 ? floorLayerId : SortingLayer.NameToID("Default");
        int[] allSortingLayerIds = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);

        Random.State oldState = Random.state;
        Random.InitState(6464);

        // Locate active outdoor lights to attract ~70-75% of fireflies
        List<Vector3> anchorLightPositions = new List<Vector3>();
        foreach (var l in FindObjectsByType<Light2D>(FindObjectsInactive.Include))
        {
            if (l == null) continue;
            if (useCustomExteriorBounds)
            {
                if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
                if (!MatchesAnyPrefix(l.gameObject.name, fireflyAnchorLightPrefixes)) continue;
                Vector3 lp = new Vector3(l.transform.position.x, l.transform.position.y, 0f);
                if (!IsPositionIndoors(lp))
                {
                    anchorLightPositions.Add(lp);
                }
            }
            else if (l.gameObject.name.StartsWith("LampPostLight_"))
            {
                anchorLightPositions.Add(new Vector3(l.transform.position.x, l.transform.position.y, 0f));
            }
        }

        // 1. Fireflies (world-space entities: ~70-75% circling lights, ~25-30% exterior zones)
        firefliesContainer = new GameObject("Fireflies_WorldEntities");
        firefliesContainer.transform.SetParent(transform, false);
        fireflyEntities.Clear();
        int totalFireflies = useCustomExteriorBounds ? Mathf.Max(16, customFireflyCount) : FIREFLY_COUNT;
        int heroLightCount = useCustomExteriorBounds ? Mathf.Max(0, customHeroFireflyLights) : HERO_FIREFLY_LIGHTS;
        float lightQuotaRatio = useCustomExteriorBounds ? 0.75f : 0.70f;
        int streetlampFireflyQuota = anchorLightPositions.Count > 0 ? Mathf.RoundToInt(totalFireflies * lightQuotaRatio) : 0;
        for (int i = 0; i < totalFireflies; i++)
        {
            bool nearStreetlamp = i < streetlampFireflyQuota;
            Vector3 anchor;
            if (nearStreetlamp)
            {
                Vector3 lampPos = anchorLightPositions[i % anchorLightPositions.Count];
                Vector2 randDir = Random.insideUnitCircle;
                if (randDir.sqrMagnitude < 0.0001f) randDir = Vector2.right;
                Vector2 ringOffset = randDir.normalized * (useCustomExteriorBounds ? Random.Range(0.08f, 0.32f) : Random.Range(0.14f, 0.36f));
                anchor = ClampPointOutdoors(lampPos + new Vector3(ringOffset.x, ringOffset.y, 0f));
            }
            else
            {
                anchor = useCustomExteriorBounds ? SampleExteriorAnchor(out _) : SampleCurbOrYardAnchor();
            }

            GameObject go = new GameObject($"Firefly_{i}");
            go.transform.SetParent(firefliesContainer.transform, false);
            go.transform.position = SnapTo16BitGrid(anchor);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = weatherSortingLayerId;
            sr.sortingOrder = 10;
            int startFrame = Random.Range(0, Mathf.Max(1, fireflyFrames.Length));
            if (fireflyFrames.Length > 0) sr.sprite = fireflyFrames[startFrame];

            Light2D heroLight = null;
            if (i < heroLightCount)
            {
                heroLight = go.AddComponent<Light2D>();
                heroLight.lightType = Light2D.LightType.Point;
                heroLight.color = new Color(0.84f, 1.0f, 0.48f, 1f);
                heroLight.intensity = 0.14f;
                heroLight.pointLightInnerRadius = 0.01f;
                heroLight.pointLightOuterRadius = 0.17f;
                heroLight.targetSortingLayers = allSortingLayerIds;
            }

            fireflyEntities.Add(new FireflyEntity
            {
                transform = go.transform,
                renderer = sr,
                heroLight = heroLight,
                anchorPos = anchor,
                proximityRadius = nearStreetlamp ? Random.Range(0.10f, 0.22f) : Random.Range(0.14f, 0.26f),
                speed = Random.Range(0.85f, 1.65f),
                phase = Random.Range(0f, Mathf.PI * 2f),
                animTimer = Random.Range(0f, 0.15f),
                currentFrame = startFrame,
                fps = Random.Range(6.5f, 9.5f),
                baseAlpha = Random.Range(0.75f, 1.0f)
            });
        }

        // 2. Static World-Space Grouped RainCluster Entities
        rainContainer = new GameObject("Rain_WorldEntities");
        rainContainer.transform.SetParent(transform, false);
        rainEntities.Clear();

        int clusterIdx = 0;
        if (useCustomExteriorBounds)
        {
            Rect[] activeZones = GetActiveExteriorZones();
            float stepX = Mathf.Max(0.25f, customRainGridSpacingX);
            float stepY = Mathf.Max(0.25f, customRainGridSpacingY);
            for (int z = 0; z < activeZones.Length; z++)
            {
                Rect zone = activeZones[z];
                for (float y = zone.yMin + 0.12f; y <= zone.yMax - 0.12f; y += stepY)
                {
                    for (float x = zone.xMin + 0.12f; x <= zone.xMax - 0.12f; x += stepX)
                    {
                        Vector3 candidate = new Vector3(
                            Mathf.Clamp(x + Random.Range(-0.06f, 0.06f), zone.xMin + 0.06f, zone.xMax - 0.06f),
                            Mathf.Clamp(y + Random.Range(-0.06f, 0.06f), zone.yMin + 0.06f, zone.yMax - 0.06f),
                            0f);
                        if (!IsRainBlockedPosition(candidate))
                        {
                            AddRainCluster(candidate, weatherSortingLayerId, ref clusterIdx);
                        }
                    }
                }
            }
        }
        else
        {
            float[] roadColumnsX = { -1.50f, -1.08f, -0.64f, -0.22f };
            for (int row = 0; row < ROAD_RAIN_ROWS; row++)
            {
                float baseY = Mathf.Lerp(-2.60f, 2.60f, row / (float)(ROAD_RAIN_ROWS - 1));
                for (int col = 0; col < roadColumnsX.Length; col++)
                {
                    float x = roadColumnsX[col] + Random.Range(-0.05f, 0.05f);
                    float y = baseY + Random.Range(-0.05f, 0.05f);
                    if (col == 3 && (y < -1.80f || y > 1.60f))
                    {
                        continue;
                    }

                    Vector3 candidate = new Vector3(x, y, 0f);
                    if (IsRainBlockedPosition(candidate))
                    {
                        float shiftedX = col <= 1 ? -1.48f : -0.20f;
                        Vector3 shifted = new Vector3(shiftedX + Random.Range(-0.04f, 0.04f), y, 0f);
                        if (IsRainBlockedPosition(shifted))
                        {
                            continue;
                        }
                        candidate = shifted;
                    }

                    AddRainCluster(candidate, weatherSortingLayerId, ref clusterIdx);
                }
            }

            for (int i = 0; i < PERIMETER_RAIN_CLUSTER_COUNT; i++)
            {
                Vector3 alleyAnchor = SamplePerimeterAlleyRainAnchor(i);
                if (!IsRainBlockedPosition(alleyAnchor))
                {
                    AddRainCluster(alleyAnchor, weatherSortingLayerId, ref clusterIdx);
                }
            }
        }

        // 3. Fog Wisps (world-space entities on Floor +10, beneath Jeepney/props/walls/roofs)
        fogContainer = new GameObject("Fog_WorldEntities");
        fogContainer.transform.SetParent(transform, false);
        fogEntities.Clear();
        int totalFogWisps = useCustomExteriorBounds ? Mathf.Max(16, customFogWispCount) : FOG_WISP_COUNT;
        for (int i = 0; i < totalFogWisps; i++)
        {
            Vector3 anchor = SampleExteriorAnchor(out _, VehicleExclusionMode.Fog);
            GameObject go = new GameObject($"FogWisp_{i}");
            go.transform.SetParent(fogContainer.transform, false);
            go.transform.position = SnapTo16BitGrid(anchor);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = fogSortingLayerId;
            sr.sortingOrder = 10;
            if (fogUnlitMaterial != null) sr.sharedMaterial = fogUnlitMaterial;
            int startFrame = Random.Range(0, Mathf.Max(1, fogFrames.Length));
            if (fogFrames.Length > 0) sr.sprite = fogFrames[startFrame];

            fogEntities.Add(new FogWispEntity
            {
                transform = go.transform,
                renderer = sr,
                anchorPos = anchor,
                proximityRadius = Random.Range(0.38f, 0.72f),
                speed = Random.Range(0.22f, 0.45f),
                phase = Random.Range(0f, Mathf.PI * 2f),
                animTimer = Random.Range(0f, 0.22f),
                currentFrame = startFrame,
                fps = Random.Range(4.5f, 6.5f),
                baseAlpha = Random.Range(0.46f, 0.72f)
            });
        }

        Random.state = oldState;
    }

    private Vector3 SampleCurbOrYardAnchor()
    {
        if (HasCustomExteriorZones)
        {
            return SampleExteriorAnchor(out _);
        }

        float[] curbX = { -1.62f, -1.52f, -0.38f, -0.30f, -0.14f };
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Vector3 candidate = new Vector3(
                curbX[Random.Range(0, curbX.Length)] + Random.Range(-0.04f, 0.04f),
                Random.Range(-2.55f, 2.55f),
                0f);
            if (!IsPositionIndoors(candidate))
            {
                return candidate;
            }
        }
        return SampleExteriorAnchor(out _);
    }

    private static bool MatchesAnyPrefix(string name, string[] prefixes)
    {
        if (string.IsNullOrEmpty(name) || prefixes == null) return false;
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (!string.IsNullOrEmpty(prefixes[i]) && name.StartsWith(prefixes[i]))
            {
                return true;
            }
        }
        return false;
    }

    private Vector3 ClampPointOutdoors(Vector3 candidate)
    {
        if (!IsPositionIndoors(candidate)) return candidate;
        if (useCustomExteriorBounds)
        {
            Vector3 pushed = PushOutsideRectList(candidate, enclosedBuildingFogRects);
            if (!IsPositionIndoors(pushed)) return pushed;
        }
        if (HasCustomExteriorZones)
        {
            Rect[] zones = ActiveExteriorZones;
            Vector3 best = new Vector3(zones[0].center.x, zones[0].center.y, 0f);
            float bestSqr = float.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                Rect z = zones[i];
                float cx = Mathf.Clamp(candidate.x, z.xMin, z.xMax);
                float cy = Mathf.Clamp(candidate.y, z.yMin, z.yMax);
                Vector3 clamped = new Vector3(cx, cy, 0f);
                if (IsPositionIndoors(clamped)) continue;
                float sqr = (clamped - candidate).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = clamped;
                }
            }
            return best;
        }
        return new Vector3(Mathf.Min(-0.18f, candidate.x), candidate.y, 0f);
    }

    private void AddRainCluster(Vector3 worldPos, int weatherSortingLayerId, ref int clusterIdx)
    {
        Vector3 snappedPos = SnapTo16BitGrid(worldPos);
        GameObject go = new GameObject($"RainCluster_{clusterIdx++}");
        go.transform.SetParent(rainContainer.transform, false);
        go.transform.position = snappedPos;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerID = weatherSortingLayerId;
        sr.sortingOrder = 10;
        if (fogUnlitMaterial != null) sr.sharedMaterial = fogUnlitMaterial;
        int startFrame = Random.Range(0, Mathf.Max(1, rainFrames.Length));
        if (rainFrames != null && rainFrames.Length > 0) sr.sprite = rainFrames[startFrame];

        rainEntities.Add(new RainClusterEntity
        {
            renderer = sr,
            worldPos = snappedPos,
            animTimer = Random.Range(0f, 0.08f),
            currentFrame = startFrame,
            fps = Random.Range(12.5f, 15.5f)
        });
    }

    private Vector3 SamplePerimeterAlleyRainAnchor(int index)
    {
        // Distribute across North (2), South (3), and East (4) exterior perimeter alleys
        int zoneIdx = 2 + (index % 3);
        Rect zone = ExteriorZones[zoneIdx];
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(zone.xMin + 0.10f, zone.xMax - 0.10f),
                Random.Range(zone.yMin + 0.08f, zone.yMax - 0.08f),
                0f);
            if (!IsRainBlockedPosition(candidate))
            {
                return candidate;
            }
        }
        return SampleExteriorAnchor(out _, VehicleExclusionMode.Rain);
    }

    private Vector3 SampleExteriorAnchor(out int zoneIndex, VehicleExclusionMode exclusionMode = VehicleExclusionMode.None)
    {
        Rect[] zones = ActiveExteriorZones;
        float totalArea = 0f;
        for (int z = 0; z < zones.Length; z++)
        {
            totalArea += zones[z].width * zones[z].height;
        }

        int maxAttempts = exclusionMode != VehicleExclusionMode.None ? 64 : 32;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float pick = Random.Range(0f, totalArea);
            zoneIndex = 0;
            float accum = 0f;
            for (int z = 0; z < zones.Length; z++)
            {
                accum += zones[z].width * zones[z].height;
                if (pick <= accum)
                {
                    zoneIndex = z;
                    break;
                }
            }

            Rect zone = zones[zoneIndex];
            Vector3 candidate = new Vector3(
                Random.Range(zone.xMin, zone.xMax),
                Random.Range(zone.yMin, zone.yMax),
                0f);

            if (!IsBlockedForMode(candidate, exclusionMode))
            {
                return candidate;
            }
        }

        if (HasCustomExteriorZones)
        {
            zoneIndex = 0;
            Rect first = zones[0];
            Vector3 fallback = new Vector3(first.center.x, first.center.y, 0f);
            if (exclusionMode == VehicleExclusionMode.Fog && IsFogBlockedPosition(fallback))
            {
                fallback = PushOutsideFogBlocked(fallback, fallback);
            }
            return fallback;
        }

        // Safe exterior fallbacks away from Jeepney and house walls
        zoneIndex = 0;
        Vector3 streetFallback = new Vector3(Random.Range(-1.58f, -1.32f), Random.Range(-2.45f, 2.45f), 0f);
        if (!IsBlockedForMode(streetFallback, exclusionMode))
        {
            return streetFallback;
        }

        zoneIndex = Mathf.Min(2, zones.Length - 1);
        Vector3 safeFallback = new Vector3(Random.Range(0.25f, 3.85f), Random.Range(2.05f, 2.48f), 0f);
        if (exclusionMode == VehicleExclusionMode.Fog && IsFogBlockedPosition(safeFallback))
        {
            safeFallback = PushOutsideFogBlocked(safeFallback, streetFallback);
        }
        return safeFallback;
    }

    private Vector3 SampleExteriorOffsetInZone(Vector3 anchor, int zoneIndex, float radius, VehicleExclusionMode exclusionMode = VehicleExclusionMode.None)
    {
        Rect[] zones = ActiveExteriorZones;
        Rect zone = zones[Mathf.Clamp(zoneIndex, 0, zones.Length - 1)];
        int attempts = exclusionMode != VehicleExclusionMode.None ? 10 : 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 rnd = Random.insideUnitCircle * radius;
            Vector3 target = anchor + new Vector3(rnd.x, rnd.y, 0f);
            target.x = Mathf.Clamp(target.x, zone.xMin, zone.xMax);
            target.y = Mathf.Clamp(target.y, zone.yMin, zone.yMax);

            if (!IsBlockedForMode(target, exclusionMode))
            {
                return target - anchor;
            }
        }
        return Vector3.zero;
    }

    private bool IsBlockedForMode(Vector3 worldPos, VehicleExclusionMode exclusionMode)
    {
        return exclusionMode switch
        {
            VehicleExclusionMode.Fog => IsFogBlockedPosition(worldPos),
            VehicleExclusionMode.Rain => IsRainBlockedPosition(worldPos),
            _ => IsPositionIndoors(worldPos)
        };
    }

    private static Vector3 SnapTo16BitGrid(Vector3 pos)
    {
        pos.x = Mathf.Round(pos.x * 200f) / 200f;
        pos.y = Mathf.Round(pos.y * 200f) / 200f;
        pos.z = 0f;
        return pos;
    }

    public bool IsPositionIndoors(Vector3 worldPos)
    {
        if (FloorZone.HasAnyZones())
        {
            if (FloorZone.IsPositionInsideInteriorFloor(worldPos))
            {
                return true;
            }
        }
        else if (!useCustomExteriorBounds
            && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != ProgressionAndLoadoutManager.CEMETERY_MAP_SCENE_NAME
            && worldPos.y < -3.2f)
        {
            // Legacy BarangayMap basement sits below y = -3.2f and is always indoors
            return true;
        }

        bool needsRoofRefresh = cachedRoofs.Count == 0;
        if (!needsRoofRefresh)
        {
            for (int i = 0; i < cachedRoofs.Count; i++)
            {
                if (cachedRoofs[i] == null)
                {
                    needsRoofRefresh = true;
                    break;
                }
            }
        }

        if (needsRoofRefresh)
        {
            cachedRoofs.Clear();
            cachedRoofs.AddRange(FindObjectsByType<RoofFader>(FindObjectsInactive.Include));
        }

        for (int i = 0; i < cachedRoofs.Count; i++)
        {
            var rf = cachedRoofs[i];
            if (rf != null && rf.IsEnclosedBuilding && rf.IsPositionInside(worldPos))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPositionInsideRects(Vector3 worldPos, List<Rect> rects)
    {
        Vector2 p = new Vector2(worldPos.x, worldPos.y);
        for (int i = 0; i < rects.Count; i++)
        {
            if (rects[i].Contains(p))
                return true;
        }
        return false;
    }

    private bool IsRainBlockedPosition(Vector3 worldPos)
    {
        return IsPositionIndoors(worldPos) || IsPositionInsideRects(worldPos, vehicleRainExclusionRects);
    }

    private bool IsFogBlockedPosition(Vector3 worldPos)
    {
        return IsPositionIndoors(worldPos)
            || IsPositionInsideRects(worldPos, enclosedBuildingFogRects)
            || IsPositionInsideRects(worldPos, vehicleFogExclusionRects);
    }

    private Vector3 PushOutsideFogBlocked(Vector3 candidate, Vector3 fallbackAnchor)
    {
        Vector3 adjusted = PushOutsideRectList(candidate, vehicleFogExclusionRects);
        adjusted = PushOutsideRectList(adjusted, enclosedBuildingFogRects);
        return IsFogBlockedPosition(adjusted) ? fallbackAnchor : adjusted;
    }

    private static Vector3 PushOutsideRectList(Vector3 candidate, List<Rect> rects)
    {
        Vector3 adjusted = candidate;
        for (int i = 0; i < rects.Count; i++)
        {
            Rect r = rects[i];
            if (!r.Contains(new Vector2(adjusted.x, adjusted.y))) continue;

            float dLeft = Mathf.Abs(adjusted.x - r.xMin);
            float dRight = Mathf.Abs(r.xMax - adjusted.x);
            float dBottom = Mathf.Abs(adjusted.y - r.yMin);
            float dTop = Mathf.Abs(r.yMax - adjusted.y);

            float min = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
            const float margin = 0.02f;
            if (min == dLeft) adjusted.x = r.xMin - margin;
            else if (min == dRight) adjusted.x = r.xMax + margin;
            else if (min == dBottom) adjusted.y = r.yMin - margin;
            else adjusted.y = r.yMax + margin;
        }

        return adjusted;
    }

    public static float GetEventIntervalMultiplier()
    {
        if (Instance == null) return 1f;
        return Instance.IsRedMoon ? REDMOON_EVENT_INTERVAL_MULT : 1f;
    }

    public static float GetHuntDurationMultiplier()
    {
        if (Instance == null) return 1f;
        return Instance.IsRedMoon ? REDMOON_HUNT_DURATION_MULT : 1f;
    }

    public static float GetAudioClarityMultiplier(Vector3 worldPos)
    {
        if (Instance == null) return 1f;
        if (Instance.CurrentWeather == BarangayWeatherType.Foggy && Instance.IsPositionIndoors(worldPos))
        {
            return FOG_INDOOR_AUDIO_CLARITY;
        }
        return 1f;
    }

    public static float GetFootstepWeatherMultiplier(Vector3 worldPos)
    {
        if (Instance == null) return 1f;
        if (Instance.CurrentWeather == BarangayWeatherType.RainThunderstorm && !Instance.IsPositionIndoors(worldPos))
        {
            return RAIN_OUTDOOR_FOOTSTEP_MULTIPLIER;
        }
        return 1f;
    }

    private void ApplyWeatherState()
    {
        RestoreAllLightRadii();
        hasAppliedGlobalLight = false;
        fogLightCacheRefreshTimer = 0f;
        lightScanTimer = 0f;
        lightningTimer = Random.Range(7f, 15f);
        lightningFlashAlpha = 0f;

        bool isFireflies = CurrentWeather == BarangayWeatherType.NightFireflies;
        bool isFog = CurrentWeather == BarangayWeatherType.Foggy;
        bool isRain = CurrentWeather == BarangayWeatherType.RainThunderstorm;

        if (PlayerController.LocalInstance != null)
        {
            localPlayerIndoors = IsPositionIndoors(PlayerController.LocalInstance.transform.position);
        }

        if (firefliesContainer != null) firefliesContainer.SetActive(isFireflies);
        if (fogContainer != null) fogContainer.SetActive(isFog);
        if (rainContainer != null) rainContainer.SetActive(isRain && !IsLocalInBurialMap);

        if (isRain)
        {
            rainUpdateTimer = 0.05f;
            UpdateRainEntities(false, default);
        }

        for (int i = 0; i < puddleEntities.Count; i++)
        {
            var sr = puddleEntities[i].renderer;
            if (sr != null)
            {
                sr.gameObject.SetActive(isRain);
                if (puddleFrames != null && puddleFrames.Length > 0)
                {
                    sr.sprite = puddleFrames[puddleEntities[i].currentFrame % puddleFrames.Length];
                }
            }
        }

        UpdateGlobalLighting();
        UpdateAmbientAudio(forceClipReset: true);
        IsWarmupReady = true;
    }

    private void UpdateGlobalLighting()
    {
        if (globalLight == null) return;
        if (ExorcismManager.Instance != null && ExorcismManager.Instance.IsRedEnvironmentActive)
        {
            hasAppliedGlobalLight = false;
            return;
        }

        Color targetColor;
        float targetIntensity;

        if (IsLocalInBurialMap)
        {
            targetColor = new Color(0.28f, 0.34f, 0.44f);
            targetIntensity = 0.006f;
        }
        else if (IsRedMoon)
        {
            targetColor = new Color(0.58f, 0.14f, 0.15f);
            targetIntensity = 0.014f + lightningFlashAlpha * 0.42f;
        }
        else
        {
            switch (CurrentWeather)
            {
                case BarangayWeatherType.Foggy:
                    targetColor = new Color(0.48f, 0.56f, 0.64f);
                    targetIntensity = 0.008f;
                    break;

                case BarangayWeatherType.RainThunderstorm:
                    targetColor = Color.Lerp(new Color(0.34f, 0.42f, 0.60f), new Color(0.82f, 0.9f, 1f), lightningFlashAlpha);
                    targetIntensity = 0.006f + lightningFlashAlpha * 0.65f;
                    break;

                default:
                    targetColor = new Color(0.38f, 0.46f, 0.64f);
                    targetIntensity = 0.006f;
                    break;
            }
        }

        if (!hasAppliedGlobalLight || !ColorsApproximatelyEqual(lastAppliedGlobalColor, targetColor))
        {
            globalLight.color = targetColor;
            lastAppliedGlobalColor = targetColor;
        }

        if (!hasAppliedGlobalLight || Mathf.Abs(lastAppliedGlobalIntensity - targetIntensity) > 0.0001f)
        {
            globalLight.intensity = targetIntensity;
            lastAppliedGlobalIntensity = targetIntensity;
        }

        hasAppliedGlobalLight = true;
    }

    private void UpdateAmbientAudio(bool forceClipReset)
    {
        if (weatherAudioSource == null) return;

        string clipName = CurrentWeather switch
        {
            BarangayWeatherType.Foggy => "weather_wind_fog",
            BarangayWeatherType.RainThunderstorm => "weather_rain_loop",
            _ => "weather_night_crickets"
        };

        AudioClip desiredClip = GameAudioManager.GetClip(clipName);
        if (desiredClip != null && (forceClipReset || weatherAudioSource.clip != desiredClip))
        {
            weatherAudioSource.clip = desiredClip;
            weatherAudioSource.Play();
        }

        UpdateWeatherAudioProximity(immediate: forceClipReset);
    }

    private void UpdateWeatherAudioProximity(bool immediate = false)
    {
        if (weatherAudioSource == null || weatherAudioEmitter == null) return;

        if (IsLocalInBurialMap)
        {
            weatherAudioSource.volume = 0f;
            if (immediate) weatherAudioSource.Stop();
            return;
        }

        bool hasPlayer = GameAudioManager.TryGetListenerWorldPosition(out Vector3 listenerPos);
        if (hasPlayer)
        {
            localPlayerIndoors = IsPositionIndoors(listenerPos);
        }

        float targetVol = 0f;
        float listenerZ = GameAudioManager.GetActiveListenerZ();
        float sfxScale = GameAudioManager.GetEffectiveSfxVolume(1f);

        if (CurrentWeather == BarangayWeatherType.NightFireflies)
        {
            // Outside fireflies must be strictly silent indoors (main floor and basement) and proximity-based outside.
            if (hasPlayer && !localPlayerIndoors && TryGetNearestFireflyPosition(listenerPos, out Vector3 fireflyPos))
            {
                weatherAudioEmitter.position = new Vector3(fireflyPos.x, fireflyPos.y, listenerZ);
                GameAudioManager.ConfigureProximityAudioSource(weatherAudioSource, 0.45f, 2.4f, 1.0f);
                targetVol = 0.24f * sfxScale;
            }
            else
            {
                targetVol = 0f;
            }
        }
        else if (hasPlayer && (FloorZone.HasAnyZones() || !GameAudioManager.IsBasementPosition(listenerPos)))
        {
            float weatherBase = CurrentWeather == BarangayWeatherType.RainThunderstorm ? 0.34f : 0.22f;
            if (!localPlayerIndoors)
            {
                weatherAudioEmitter.position = new Vector3(listenerPos.x, listenerPos.y, listenerZ);
                GameAudioManager.ConfigureProximityAudioSource(weatherAudioSource, 1.0f, 4.0f, 1.0f);
                targetVol = weatherBase * sfxScale;
            }
            else if (FloorZone.HasAnyZones())
            {
                int floor = FloorZone.GetFloorIndex(listenerPos);
                if (floor <= FloorZone.FLOOR_FIRST)
                {
                    Vector3 extPos = GetNearestExteriorEdgePoint(listenerPos);
                    weatherAudioEmitter.position = new Vector3(extPos.x, extPos.y, listenerZ);
                    GameAudioManager.ConfigureProximityAudioSource(weatherAudioSource, 0.4f, 2.8f, 1.0f);
                    targetVol = weatherBase * 0.42f * sfxScale;
                }
                else
                {
                    // Floor 2 is softly muffled between slabs; Floor 3 is directly under the roof.
                    weatherAudioEmitter.position = new Vector3(listenerPos.x, listenerPos.y, listenerZ);
                    GameAudioManager.ConfigureProximityAudioSource(weatherAudioSource, 1.0f, 4.0f, 1.0f);
                    float roofScale = floor >= FloorZone.FLOOR_THIRD ? 0.34f : 0.14f;
                    targetVol = weatherBase * roofScale * sfxScale;
                }
            }
            else
            {
                Vector3 extPos = GetNearestExteriorEdgePoint(listenerPos);
                weatherAudioEmitter.position = new Vector3(extPos.x, extPos.y, listenerZ);
                GameAudioManager.ConfigureProximityAudioSource(weatherAudioSource, 0.4f, 2.8f, 1.0f);
                targetVol = weatherBase * 0.42f * sfxScale;
            }
        }

        float clampedTarget = Mathf.Clamp01(targetVol);
        weatherAudioSource.volume = immediate
            ? clampedTarget
            : Mathf.MoveTowards(weatherAudioSource.volume, clampedTarget, Time.deltaTime * 2.5f);

        if (weatherAudioSource.clip != null)
        {
            if (weatherAudioSource.volume > 0.002f && !weatherAudioSource.isPlaying)
            {
                weatherAudioSource.Play();
            }
            else if (weatherAudioSource.volume <= 0.002f && weatherAudioSource.isPlaying)
            {
                weatherAudioSource.Pause();
            }
        }
    }

    private bool TryGetNearestFireflyPosition(Vector3 listenerPos, out Vector3 nearestPos)
    {
        nearestPos = Vector3.zero;
        float bestSqr = float.MaxValue;
        bool found = false;

        for (int i = 0; i < fireflyEntities.Count; i++)
        {
            Transform tr = fireflyEntities[i].transform;
            if (tr == null) continue;
            Vector3 p = tr.position;
            float sqr = (p.x - listenerPos.x) * (p.x - listenerPos.x) + (p.y - listenerPos.y) * (p.y - listenerPos.y);
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                nearestPos = p;
                found = true;
            }
        }
        return found;
    }

    private Vector3 GetNearestExteriorEdgePoint(Vector3 pos)
    {
        if (useCustomExteriorBounds)
        {
            for (int i = 0; i < cachedRoofs.Count; i++)
            {
                RoofFader rf = cachedRoofs[i];
                if (rf == null || !rf.IsEnclosedBuilding || !rf.IsPositionInside(pos)) continue;

                foreach (Collider2D col in rf.GetComponents<Collider2D>())
                {
                    if (col == null) continue;
                    Bounds b = col.bounds;
                    if (!col.OverlapPoint(pos) && !b.Contains(new Vector3(pos.x, pos.y, b.center.z))) continue;

                    float rWest = Mathf.Abs(pos.x - (b.min.x - 0.06f));
                    float rEast = Mathf.Abs((b.max.x + 0.06f) - pos.x);
                    float rSouth = Mathf.Abs(pos.y - (b.min.y - 0.06f));
                    float rNorth = Mathf.Abs((b.max.y + 0.06f) - pos.y);
                    float rMin = Mathf.Min(Mathf.Min(rWest, rEast), Mathf.Min(rSouth, rNorth));

                    if (rMin == rWest) return new Vector3(b.min.x - 0.06f, pos.y, 0f);
                    if (rMin == rEast) return new Vector3(b.max.x + 0.06f, pos.y, 0f);
                    if (rMin == rSouth) return new Vector3(pos.x, b.min.y - 0.06f, 0f);
                    return new Vector3(pos.x, b.max.y + 0.06f, 0f);
                }
            }
        }

        if (customBuildingPerimeter.width > 0.01f && customBuildingPerimeter.height > 0.01f)
        {
            float dWest = Mathf.Abs(pos.x - customBuildingPerimeter.xMin);
            float dEast = Mathf.Abs(customBuildingPerimeter.xMax - pos.x);
            float dSouth = Mathf.Abs(pos.y - customBuildingPerimeter.yMin);
            float dNorth = Mathf.Abs(customBuildingPerimeter.yMax - pos.y);
            float min = Mathf.Min(Mathf.Min(dWest, dEast), Mathf.Min(dSouth, dNorth));

            if (min == dWest) return new Vector3(customBuildingPerimeter.xMin, pos.y, 0f);
            if (min == dEast) return new Vector3(customBuildingPerimeter.xMax, pos.y, 0f);
            if (min == dSouth) return new Vector3(pos.x, customBuildingPerimeter.yMin, 0f);
            return new Vector3(pos.x, customBuildingPerimeter.yMax, 0f);
        }

        // Main house spans approximately x=[0.00..4.48], y=[-1.85..+1.65]; push out to closest perimeter edge.
        float bWest = Mathf.Abs(pos.x - (-0.08f));
        float bEast = Mathf.Abs(4.55f - pos.x);
        float bSouth = Mathf.Abs(pos.y - (-1.92f));
        float bNorth = Mathf.Abs(1.72f - pos.y);
        float bMin = Mathf.Min(Mathf.Min(bWest, bEast), Mathf.Min(bSouth, bNorth));

        if (bMin == bWest) return new Vector3(-0.08f, pos.y, 0f);
        if (bMin == bEast) return new Vector3(4.55f, pos.y, 0f);
        if (bMin == bSouth) return new Vector3(pos.x, -1.92f, 0f);
        return new Vector3(pos.x, 1.72f, 0f);
    }

    private void Update()
    {
        if (IsLocalInBurialMap)
        {
            lightningFlashAlpha = 0f;
            UpdateWeatherAudioProximity(immediate: false);
            UpdateGlobalLighting();
            return;
        }

        // Track whether local player is currently under a house roof
        audioBlendTimer -= Time.deltaTime;
        if (audioBlendTimer <= 0f)
        {
            audioBlendTimer = 0.12f;
            var local = PlayerController.LocalInstance;
            if (local != null)
            {
                localPlayerIndoors = IsPositionIndoors(local.transform.position);
            }
            UpdateAmbientAudio(forceClipReset: false);
        }
        else
        {
            UpdateWeatherAudioProximity(immediate: false);
        }

        bool hasCamRect = TryGetCameraWorldRect(1.25f, out Rect camRect);

        // Advance world-space animated weather entities
        switch (CurrentWeather)
        {
            case BarangayWeatherType.NightFireflies:
                UpdateFireflies(hasCamRect, in camRect);
                break;

            case BarangayWeatherType.Foggy:
                UpdateFogWisps(hasCamRect, in camRect);
                break;

            case BarangayWeatherType.RainThunderstorm:
                UpdateRainEntities(hasCamRect, in camRect);
                UpdatePuddles(hasCamRect, in camRect);
                break;
        }

        // Handle lightning during rainstorms (or rare Red Moon thunder)
        if (CurrentWeather == BarangayWeatherType.RainThunderstorm || IsRedMoon)
        {
            lightningTimer -= Time.deltaTime;
            if (lightningTimer <= 0f)
            {
                lightningTimer = Random.Range(10f, 22f);
                lightningFlashAlpha = 1f;
                GameAudioManager.TryGetListenerWorldPosition(out Vector3 listenerPos);
                if (FloorZone.HasAnyZones())
                {
                    int floor = FloorZone.GetFloorIndex(listenerPos);
                    Vector3 thunderPos = new Vector3(listenerPos.x - 1.2f, listenerPos.y, 0f);
                    float thunderVol = !localPlayerIndoors
                        ? 0.85f
                        : (floor >= FloorZone.FLOOR_THIRD ? 0.48f : (floor == FloorZone.FLOOR_SECOND ? 0.24f : 0.42f));
                    GameAudioManager.PlayAt("weather_thunder_clap", thunderPos, thunderVol, 0.9f, Random.Range(0.92f, 1.08f), 1.5f, 12.0f, 1.0f);
                }
                else
                {
                    Vector3 thunderPos = new Vector3(-1.2f, listenerPos.y, 0f);
                    float thunderVol = localPlayerIndoors ? (GameAudioManager.IsBasementPosition(listenerPos) ? 0.10f : 0.42f) : 0.85f;
                    GameAudioManager.PlayAt("weather_thunder_clap", thunderPos, thunderVol, 0.9f, Random.Range(0.92f, 1.08f), 1.5f, 12.0f, 0.15f);
                }
            }
        }

        if (lightningFlashAlpha > 0f)
        {
            lightningFlashAlpha = Mathf.Max(0f, lightningFlashAlpha - Time.deltaTime * 3.2f);
        }

        UpdateGlobalLighting();

        // Periodically apply Fog outdoor Light2D radius scaling (skip completely in non-Foggy weather)
        if (CurrentWeather == BarangayWeatherType.Foggy)
        {
            lightScanTimer -= Time.deltaTime;
            fogLightCacheRefreshTimer -= Time.deltaTime;
            if (lightScanTimer <= 0f)
            {
                lightScanTimer = 0.25f;
                ApplyFogRadiusAdjustments();
            }
        }
        else if (baseLightOuterRadii.Count > 0)
        {
            RestoreAllLightRadii();
        }
    }

    private bool TryGetCameraWorldRect(float padding, out Rect rect)
    {
        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
        {
            cachedMainCamera = Camera.main;
        }

        if (cachedMainCamera == null || !cachedMainCamera.orthographic)
        {
            rect = default;
            return false;
        }

        float halfH = cachedMainCamera.orthographicSize + padding;
        float halfW = cachedMainCamera.orthographicSize * cachedMainCamera.aspect + padding;
        Vector3 cp = cachedMainCamera.transform.position;
        rect = new Rect(cp.x - halfW, cp.y - halfH, halfW * 2f, halfH * 2f);
        return true;
    }

    private static bool IsNearRect(in Rect rect, Vector3 pos, float margin)
    {
        return pos.x >= rect.xMin - margin && pos.x <= rect.xMax + margin
            && pos.y >= rect.yMin - margin && pos.y <= rect.yMax + margin;
    }

    private static bool ColorsApproximatelyEqual(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.002f
            && Mathf.Abs(a.g - b.g) < 0.002f
            && Mathf.Abs(a.b - b.b) < 0.002f
            && Mathf.Abs(a.a - b.a) < 0.002f;
    }

    private void UpdateFireflies(bool hasCamRect, in Rect camRect)
    {
        if (fireflyEntities.Count == 0) return;

        float t = Time.time;
        float dt = Time.deltaTime;
        Color baseTint = IsRedMoon ? new Color(1.0f, 0.42f, 0.36f, 1f) : new Color(0.94f, 1.0f, 0.72f, 1f);
        Color lightTint = IsRedMoon ? new Color(1.0f, 0.36f, 0.32f, 1f) : new Color(0.84f, 1.0f, 0.48f, 1f);

        for (int i = 0; i < fireflyEntities.Count; i++)
        {
            FireflyEntity e = fireflyEntities[i];
            if (e.transform == null) continue;

            if (hasCamRect && !IsNearRect(in camRect, e.anchorPos, e.proximityRadius + 1.0f))
            {
                if (e.renderer != null && e.renderer.enabled) e.renderer.enabled = false;
                if (e.heroLight != null && e.heroLight.enabled) e.heroLight.enabled = false;
                continue;
            }

            if (e.renderer != null && !e.renderer.enabled) e.renderer.enabled = true;

            float ox = Mathf.Sin(t * e.speed + e.phase) * e.proximityRadius;
            float oy = Mathf.Cos(t * (e.speed * 0.78f) + e.phase * 1.37f) * (e.proximityRadius * 0.85f);
            Vector3 nextPos = e.anchorPos + new Vector3(ox, oy, 0f);
            e.transform.position = SnapTo16BitGrid(nextPos);

            if (fireflyFrames != null && fireflyFrames.Length > 0 && e.renderer != null)
            {
                e.animTimer += dt;
                float step = 1f / e.fps;
                if (e.animTimer >= step)
                {
                    e.animTimer -= step;
                    e.currentFrame = (e.currentFrame + 1) % fireflyFrames.Length;
                    e.renderer.sprite = fireflyFrames[e.currentFrame];
                }
            }

            float shimmer = 0.62f + 0.38f * Mathf.Sin(t * (e.speed * 2.1f) + e.phase);
            if (e.renderer != null)
            {
                Color c = baseTint;
                c.a = e.baseAlpha * shimmer;
                if (!ColorsApproximatelyEqual(e.renderer.color, c))
                {
                    e.renderer.color = c;
                }
            }

            if (e.heroLight != null)
            {
                bool justEnabled = !e.heroLight.enabled;
                if (justEnabled)
                {
                    e.heroLight.enabled = true;
                }
                if (justEnabled || !ColorsApproximatelyEqual(e.heroLight.color, lightTint))
                {
                    e.heroLight.color = lightTint;
                }
                float targetIntensity = 0.08f + 0.10f * shimmer;
                if (justEnabled || Mathf.Abs(e.heroLight.intensity - targetIntensity) > 0.004f)
                {
                    e.heroLight.intensity = targetIntensity;
                }
            }

            fireflyEntities[i] = e;
        }
    }

    private void UpdateRainEntities(bool hasCamRect, in Rect camRect)
    {
        if (rainContainer == null) return;

        if (IsLocalInBurialMap)
        {
            if (rainContainer.activeSelf) rainContainer.SetActive(false);
            return;
        }

        if (!rainContainer.activeSelf)
        {
            rainContainer.SetActive(true);
        }

        rainUpdateTimer += Time.deltaTime;
        if (rainUpdateTimer < 0.05f) return;
        float dt = rainUpdateTimer;
        rainUpdateTimer = 0f;

        if (rainEntities.Count == 0) return;

        Color tint = IsRedMoon ? new Color(1.0f, 0.56f, 0.54f, 0.88f) : new Color(0.90f, 0.96f, 1.0f, 0.88f);
        bool hasFrames = rainFrames != null && rainFrames.Length > 0;

        for (int i = 0; i < rainEntities.Count; i++)
        {
            RainClusterEntity e = rainEntities[i];
            if (e.renderer == null) continue;

            if (hasCamRect && !IsNearRect(in camRect, e.worldPos, 0.75f))
            {
                if (e.renderer.enabled) e.renderer.enabled = false;
                continue;
            }

            if (!e.renderer.enabled) e.renderer.enabled = true;
            if (!ColorsApproximatelyEqual(e.renderer.color, tint)) e.renderer.color = tint;

            if (hasFrames)
            {
                e.animTimer += dt;
                float step = 1f / e.fps;
                if (e.animTimer >= step)
                {
                    e.animTimer -= step;
                    e.currentFrame = (e.currentFrame + 1) % rainFrames.Length;
                    e.renderer.sprite = rainFrames[e.currentFrame];
                }
            }

            rainEntities[i] = e;
        }
    }

    private void UpdateFogWisps(bool hasCamRect, in Rect camRect)
    {
        if (fogEntities.Count == 0) return;

        float t = Time.time;
        float dt = Time.deltaTime;
        Color tint = IsRedMoon ? new Color(0.36f, 0.14f, 0.16f, 1f) : new Color(0.24f, 0.29f, 0.36f, 1f);

        for (int i = 0; i < fogEntities.Count; i++)
        {
            FogWispEntity e = fogEntities[i];
            if (e.transform == null) continue;

            if (hasCamRect && !IsNearRect(in camRect, e.anchorPos, e.proximityRadius + 1.2f))
            {
                if (e.renderer != null && e.renderer.enabled) e.renderer.enabled = false;
                continue;
            }

            if (e.renderer != null && !e.renderer.enabled) e.renderer.enabled = true;

            float ox = Mathf.Sin(t * e.speed + e.phase) * e.proximityRadius;
            float oy = Mathf.Cos(t * (e.speed * 0.65f) + e.phase * 1.19f) * (e.proximityRadius * 0.55f);
            Vector3 nextPos = e.anchorPos + new Vector3(ox, oy, 0f);
            if (IsFogBlockedPosition(nextPos))
            {
                nextPos = PushOutsideFogBlocked(nextPos, e.anchorPos);
            }
            e.transform.position = SnapTo16BitGrid(nextPos);

            if (fogFrames != null && fogFrames.Length > 0 && e.renderer != null)
            {
                e.animTimer += dt;
                float step = 1f / e.fps;
                if (e.animTimer >= step)
                {
                    e.animTimer -= step;
                    e.currentFrame = (e.currentFrame + 1) % fogFrames.Length;
                    e.renderer.sprite = fogFrames[e.currentFrame];
                }
            }

            if (e.renderer != null)
            {
                Color c = tint;
                c.a = e.baseAlpha * (localPlayerIndoors ? 0.38f : 0.85f);
                if (!ColorsApproximatelyEqual(e.renderer.color, c))
                {
                    e.renderer.color = c;
                }
            }

            fogEntities[i] = e;
        }
    }

    private void UpdatePuddles(bool hasCamRect, in Rect camRect)
    {
        if (puddleFrames == null || puddleFrames.Length == 0) return;

        puddleUpdateTimer += Time.deltaTime;
        if (puddleUpdateTimer < 0.10f) return;
        float dt = puddleUpdateTimer;
        puddleUpdateTimer = 0f;

        for (int i = 0; i < puddleEntities.Count; i++)
        {
            PuddleEntity p = puddleEntities[i];
            if (p.renderer == null || !p.renderer.gameObject.activeInHierarchy) continue;
            if (hasCamRect && !IsNearRect(in camRect, p.renderer.transform.position, 0.5f)) continue;

            p.animTimer += dt;
            float step = 1f / p.fps;
            if (p.animTimer >= step)
            {
                p.animTimer -= step;
                p.currentFrame = (p.currentFrame + 1) % puddleFrames.Length;
                p.renderer.sprite = puddleFrames[p.currentFrame];
            }

            puddleEntities[i] = p;
        }
    }

    private void RefreshFogAffectedLightsCache()
    {
        cachedFogAffectedLights.Clear();
        foreach (var light in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
        {
            if (light == null || light.lightType != Light2D.LightType.Point) continue;
            string lightName = light.gameObject.name;
            if (lightName.Contains("NightVision")) continue;

            // Affect player lights (personal/POV), flashlight/spot lights, streetlamps, and jeepney headlights outside
            bool isWeatherAffectedLight = light.GetComponentInParent<PlayerController>() != null
                || light.GetComponentInParent<FlashlightItem>() != null
                || lightName.StartsWith("LampPostLight_")
                || lightName.StartsWith("JeepneyHeadlight")
                || lightName.StartsWith("CemeteryFloodLight_");
            if (!isWeatherAffectedLight) continue;

            cachedFogAffectedLights.Add(light);
            if (!baseLightOuterRadii.ContainsKey(light))
            {
                baseLightOuterRadii[light] = light.pointLightOuterRadius;
            }
        }
    }

    private void ApplyFogRadiusAdjustments()
    {
        if (CurrentWeather != BarangayWeatherType.Foggy)
        {
            RestoreAllLightRadii();
            return;
        }

        if (fogLightCacheRefreshTimer <= 0f || cachedFogAffectedLights.Count == 0)
        {
            fogLightCacheRefreshTimer = 2.5f;
            RefreshFogAffectedLightsCache();
        }

        for (int i = cachedFogAffectedLights.Count - 1; i >= 0; i--)
        {
            Light2D light = cachedFogAffectedLights[i];
            if (light == null || !light.gameObject.activeInHierarchy)
            {
                cachedFogAffectedLights.RemoveAt(i);
                continue;
            }

            if (!baseLightOuterRadii.TryGetValue(light, out float origRadius))
            {
                origRadius = light.pointLightOuterRadius;
                baseLightOuterRadii[light] = origRadius;
            }

            bool indoors = IsPositionIndoors(light.transform.position);
            float targetRadius = indoors ? origRadius : origRadius * FOG_OUTDOOR_LIGHT_MULTIPLIER;
            if (!Mathf.Approximately(light.pointLightOuterRadius, targetRadius))
            {
                light.pointLightOuterRadius = targetRadius;
            }
        }
    }

    private void RestoreAllLightRadii()
    {
        if (baseLightOuterRadii.Count > 0)
        {
            foreach (var kv in baseLightOuterRadii)
            {
                if (kv.Key != null)
                {
                    kv.Key.pointLightOuterRadius = kv.Value;
                }
            }
            baseLightOuterRadii.Clear();
        }
        cachedFogAffectedLights.Clear();
    }
}
