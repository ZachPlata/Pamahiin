using System.Collections.Generic;
using NavMeshPlus.Components;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Fixes IR night-vision placeholder SpriteRenderers in GameMapScene and builds
/// the full pure top-down 16-bit BarangayMap scene (6 Tanglewood Drive + Barangay San Nicolas + Basement).
/// </summary>
public static class BarangayMapSceneBuilder
{
    private const string GAME_MAP_SCENE_PATH = "Assets/Scenes/GameMapScene.unity";
    private const string BARANGAY_SCENE_PATH = "Assets/Scenes/BarangayMap.unity";
    private const string SPRITE_PREFIX = "Assets/Resources/Sprites/BarangayMap/";

    private sealed class RoomSpec
    {
        public string Name;
        public Rect Bounds;
        public Rect? ExtraBounds;
        public string FloorSprite;
        public SurfaceType Surface;
        public bool IsGhostRoom = true;
        public bool HasSwitch = true;
        public Vector2? SwitchPos;
    }

    [MenuItem("Pamahiin/Reset Player Progression ($250)")]
    public static void ResetPlayerProgressionMenu()
    {
        ProgressionAndLoadoutManager.ClearPersistedProgressionPrefs();
        if (Application.isPlaying)
        {
            var mgr = ProgressionAndLoadoutManager.Instance ?? Object.FindAnyObjectByType<ProgressionAndLoadoutManager>();
            if (mgr != null) mgr.ResetProgressionToDefault();
        }
        Debug.Log("[Pamahiin] Player progression PlayerPrefs reset to default ($250 & starter loadout).");
    }

    [MenuItem("Pamahiin/Currency/Add $100")]
    public static void AddCurrency100Menu()
    {
        int newBalance = ProgressionAndLoadoutManager.AdjustCurrencyPersisted(100);
        Debug.Log($"[Pamahiin] Added $100. New balance: ${newBalance}");
    }

    [MenuItem("Pamahiin/Currency/Add $500")]
    public static void AddCurrency500Menu()
    {
        int newBalance = ProgressionAndLoadoutManager.AdjustCurrencyPersisted(500);
        Debug.Log($"[Pamahiin] Added $500. New balance: ${newBalance}");
    }

    [MenuItem("Pamahiin/Currency/Remove $100")]
    public static void RemoveCurrency100Menu()
    {
        int newBalance = ProgressionAndLoadoutManager.AdjustCurrencyPersisted(-100);
        Debug.Log($"[Pamahiin] Removed $100 (clamped at $0). New balance: ${newBalance}");
    }

    [MenuItem("Pamahiin/Currency/Set to $0")]
    public static void SetCurrencyZeroMenu()
    {
        int newBalance = ProgressionAndLoadoutManager.SetCurrencyPersisted(0);
        Debug.Log($"[Pamahiin] Currency set to ${newBalance}.");
    }

    [MenuItem("Pamahiin/Build BarangayMap Scene")]
    public static void BuildBarangayMapScene()
    {
        // 1. Ensure all 16-bit pixel sprites and WAV audio clips exist
        BarangayAssetGenerator.GenerateAllAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // 2. Patch GameMapScene Bug 1 placeholders safely in place
        FixGameMapScenePlaceholders();

        // 3. Clone GameMapScene as the template (preserving UI, Managers, Van, Ghost Handler prefab)
        if (FileExists(BARANGAY_SCENE_PATH))
        {
            AssetDatabase.DeleteAsset(BARANGAY_SCENE_PATH);
        }
        AssetDatabase.CopyAsset(GAME_MAP_SCENE_PATH, BARANGAY_SCENE_PATH);
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.OpenScene(BARANGAY_SCENE_PATH, OpenSceneMode.Single);

        // 4. Clean & relocate preserved core hierarchies cleanly to origin space
        GameObject envRoot = GameObject.Find("Environment");
        if (envRoot == null)
        {
            Debug.LogError("[BarangayMapSceneBuilder] Could not find Environment root in cloned scene.");
            return;
        }

        // Preserve Van (including PlayerSpawn, EquipmentSpawn, EndGameButton, FrontDoorKey) out before tearing down prototype house
        Transform vanTr = FindChildRecursive(envRoot.transform, "Van");
        Vector3 preservedVanWorldScale = vanTr != null ? vanTr.lossyScale : new Vector3(0.0883f, 0.0890f, 0.3559f);
        Quaternion preservedVanWorldRot = vanTr != null ? vanTr.rotation : Quaternion.Euler(0f, 0f, 90.069f);
        if (vanTr != null)
        {
            vanTr.SetParent(null, true);
        }

        envRoot.transform.position = Vector3.zero;
        envRoot.transform.rotation = Quaternion.identity;
        envRoot.transform.localScale = Vector3.one;

        // Destroy legacy Tanglewood prototype object
        Transform oldMapTr = envRoot.transform.Find("UPDATED Tanglewood Drive");
        if (oldMapTr != null) Object.DestroyImmediate(oldMapTr.gameObject);

        int wallLayer = LayerMask.NameToLayer("Walls");
        int interactLayer = LayerMask.NameToLayer("Interactable");
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (wallLayer < 0) wallLayer = 0;
        if (interactLayer < 0) interactLayer = 0;
        if (groundLayer < 0) groundLayer = 0;

        // Reparent Van vertically along right street lane/curb at (-0.82, 0.28, 0) and build 16-bit Jeepney overhaul
        if (vanTr != null)
        {
            vanTr.SetParent(envRoot.transform, false);
            vanTr.position = new Vector3(-0.82f, 0.28f, 0f);
            vanTr.rotation = preservedVanWorldRot;
            vanTr.localScale = preservedVanWorldScale;
        }

        // Reset/locate LightSystem, Ghost Manager, SoundSystem, NavhMesh
        Transform lightSys = PrepareEmptyChild(envRoot.transform, "LightSystem");
        Transform ghostMgr = envRoot.transform.Find("Ghost Manager");
        Transform ghostMarkers = ghostMgr != null ? PrepareEmptyChild(ghostMgr, "Ghost Marker") : null;
        Transform pathSys = ghostMgr != null ? PrepareEmptyChild(ghostMgr, "PathFindingSystem") : null;
        Transform soundSys = envRoot.transform.Find("SoundSystem");

        // Configure Global Light 2D (pitch-dark horror ambient contrast)
        Light2D globalLight = EnsureGlobalLight();

        // Setup Map Containers
        GameObject mapRoot = new GameObject("BarangayTanglewoodMap");
        mapRoot.transform.SetParent(envRoot.transform, false);

        Transform extRoot = new GameObject("ExteriorBarangay").transform;
        extRoot.SetParent(mapRoot.transform, false);
        Transform houseRoot = new GameObject("MainHouse_6Tanglewood").transform;
        houseRoot.SetParent(mapRoot.transform, false);
        Transform basementRoot = new GameObject("BasementLevel").transform;
        basementRoot.SetParent(mapRoot.transform, false);
        Transform wallsRoot = new GameObject("Walls").transform;
        wallsRoot.SetParent(mapRoot.transform, false);
        Transform doorsRoot = new GameObject("Doors").transform;
        doorsRoot.SetParent(mapRoot.transform, false);
        Transform furnRoot = new GameObject("Furniture").transform;
        furnRoot.SetParent(mapRoot.transform, false);
        Transform propsRoot = new GameObject("GhostProps").transform;
        propsRoot.SetParent(mapRoot.transform, false);

        if (vanTr != null)
        {
            FixVanAndKeyLayout(vanTr, envRoot.transform, doorsRoot, wallLayer, interactLayer, groundLayer);
        }

        // 5. Build Exterior Barangay San Nicolas Street, Streetlamps, Trash, Tricycle, Packed Neighbor Facades, Road Puddles
        BuildExteriorBarangay(extRoot, wallLayer, groundLayer);

        // 6. Build Main House Porch + 11 Interior Areas (6 Tanglewood Drive) & Basement
        List<RoomSpec> rooms = BuildHouseAndBasementGeometry(
            houseRoot, basementRoot, wallsRoot, doorsRoot, furnRoot, ghostMarkers, lightSys, wallLayer, interactLayer, groundLayer);

        // 7. Build RoofFader over Main House interior [0..4.35, -1.65..+1.45]
        BuildMainHouseRoof(houseRoot);

        // 8. Place StairTeleporters + FuseBox + HideZones + GhostProps
        BuildStairTeleporters(houseRoot, basementRoot, interactLayer);
        BuildHideZones(houseRoot, basementRoot);
        BuildFuseBox(furnRoot, interactLayer);
        BuildGhostProps(propsRoot);

        // 9. Build Connected GhostNode Graph (upstairs + downstairs)
        BuildGhostNodeGraph(pathSys);

        // 10. Configure Ghost Handler (clear preset marker so Ghost chooses random room)
        ConfigureGhostHandler(ghostMgr);

        // 11. Add WeatherManager (server rolls 48% Night, 25% Fog, 25% Rain, 2% Red Moon)
        CreateWeatherManager(envRoot.transform, globalLight);

        // 12. Stage Off-Bounds Burial Sequence Pit at (25, 25, 0)
        CreateBurialMapStage();

        // 13. Bake 2D NavMesh & wire bidirectional Stair NavMeshLink
        BakeNavMeshAndLink(envRoot.transform);

        // 13. Clean SoundSystem triggers & legacy ambient noise loop, and adjust Camera background
        if (soundSys != null)
        {
            foreach (var src in soundSys.GetComponents<AudioSource>())
            {
                src.Stop();
                src.playOnAwake = false;
                src.loop = false;
                src.enabled = false;
            }
            for (int i = soundSys.childCount - 1; i >= 0; i--)
            {
                var c = soundSys.GetChild(i);
                if (c.name.Contains("Ambient Noise")) Object.DestroyImmediate(c.gameObject);
            }
        }
        if (Camera.main != null)
        {
            Camera.main.backgroundColor = new Color(0.02f, 0.024f, 0.038f, 1f);
        }

        // Save Scene & Register in EditorBuildSettings
        StripDuplicateGameSettingsManager();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, BARANGAY_SCENE_PATH);
        RegisterSceneInBuildSettings();

        Debug.Log("[BarangayMapSceneBuilder] Successfully built and baked Assets/Scenes/BarangayMap.unity!");
    }

    /// <summary>Creates the inactive off-bounds Burial Pit stage at (25, 25, 0) inside BarangayMap.</summary>
    public static void CreateBurialMapStage()
    {
        foreach (var existing in Object.FindObjectsByType<BurialMapStage>(FindObjectsInactive.Include))
        {
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        var existingByName = GameObject.Find("BurialMapStage");
        if (existingByName != null) Object.DestroyImmediate(existingByName);

        GameObject stageGo = new GameObject("BurialMapStage");
        stageGo.transform.position = BurialMapStage.STAGE_CENTER;
        var stage = stageGo.AddComponent<BurialMapStage>();
        stage.BuildStageIfEmpty();
    }

    private static bool StripDuplicateGameSettingsManager()
    {
        bool removedAny = false;
        foreach (var matchMgr in Object.FindObjectsByType<GameMatchManager>(FindObjectsInactive.Include))
        {
            if (matchMgr == null) continue;
            var dupSettings = matchMgr.GetComponent<GameSettingsManager>();
            if (dupSettings != null)
            {
                Object.DestroyImmediate(dupSettings);
                removedAny = true;
                Debug.Log($"[BarangayMapSceneBuilder] Removed duplicate GameSettingsManager component from '{matchMgr.gameObject.name}'.");
            }
        }
        return removedAny;
    }

    private static void FixGameMapScenePlaceholders()
    {
        Scene gameScene = EditorSceneManager.OpenScene(GAME_MAP_SCENE_PATH, OpenSceneMode.Single);
        bool changed = false;

        foreach (var sz in Object.FindObjectsByType<SafeZone>(FindObjectsInactive.Include))
        {
            var sr = sz.GetComponent<SpriteRenderer>();
            if (sr != null && sr.enabled)
            {
                sr.enabled = false;
                changed = true;
            }
        }

        foreach (var hz in Object.FindObjectsByType<HideZone>(FindObjectsInactive.Include))
        {
            var sr = hz.GetComponent<SpriteRenderer>();
            if (sr != null && sr.enabled)
            {
                sr.enabled = false;
                changed = true;
            }
        }

        foreach (var sw in Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Include))
        {
            var sr = sw.GetComponent<SpriteRenderer>();
            if (sr != null && sr.enabled && sr.sprite != null && sr.sprite.name == "Square")
            {
                sr.enabled = false;
                changed = true;
            }
        }

        foreach (var light in Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include))
        {
            if (light.lightType == Light2D.LightType.Global && !light.enabled)
            {
                light.enabled = true;
                light.intensity = 0.045f;
                changed = true;
            }
        }

        if (StripDuplicateGameSettingsManager())
        {
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(gameScene);
            EditorSceneManager.SaveScene(gameScene, GAME_MAP_SCENE_PATH);
        }
    }

    private static void FixVanAndKeyLayout(
        Transform vanTr, Transform envRoot, Transform doorsRoot, int wallLayer, int interactLayer, int groundLayer)
    {
        foreach (var sr in vanTr.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr.GetComponent<SafeZone>() != null ||
                sr.GetComponent<EquipmentSpawnPoint>() != null ||
                sr.GetComponent<PlayerSpawnPoint>() != null)
            {
                sr.enabled = false;
            }
        }

        // Disable legacy prototype van interior art & rotated van walls (preserving Spawns, SafeZone, Key, EndGameButton, Van Light)
        for (int i = vanTr.childCount - 1; i >= 0; i--)
        {
            Transform child = vanTr.GetChild(i);
            string n = child.name.ToLowerInvariant();
            if (n == "van inside" || n == "wall")
            {
                child.gameObject.SetActive(false);
            }
        }

        Vector3 center = vanTr.position;
        float cx = center.x;
        float cy = center.y;

        // Place PlayerSpawn, EquipmentSpawn, FrontDoorKey, and EndGameButton cleanly inside the elongated passenger bay
        var playerSpawn = vanTr.GetComponentInChildren<PlayerSpawnPoint>(true);
        if (playerSpawn != null)
        {
            playerSpawn.transform.position = new Vector3(cx + 0.015f, cy - 0.18f, 0f);
        }

        var equipSpawn = vanTr.GetComponentInChildren<EquipmentSpawnPoint>(true);
        if (equipSpawn != null)
        {
            equipSpawn.transform.position = new Vector3(cx - 0.15f, cy - 0.16f, 0f);
        }

        var key = vanTr.GetComponentInChildren<FrontDoorKeyItem>(true);
        if (key != null)
        {
            key.transform.position = new Vector3(cx + 0.14f, cy + 0.21f, 0f);
            key.transform.localScale = Vector3.one * 0.14f;
        }

        var endBtn = vanTr.GetComponentInChildren<TruckEndGameButton>(true);
        if (endBtn != null)
        {
            endBtn.transform.position = new Vector3(cx + 0.16f, cy - 0.46f, 0f);
        }

        var vanLight = vanTr.GetComponentInChildren<Light2D>(true);
        if (vanLight != null)
        {
            vanLight.transform.position = new Vector3(cx, cy - 0.16f, 0f);
            vanLight.lightType = Light2D.LightType.Point;
            vanLight.color = new Color(1.0f, 0.88f, 0.68f, 1f);
            vanLight.intensity = 0.65f;
            vanLight.pointLightInnerRadius = 0.12f;
            vanLight.pointLightOuterRadius = 0.72f;
            vanLight.shadowsEnabled = true;
            vanLight.shadowIntensity = 0.85f;
            AssignAllSortingLayers(vanLight);
        }

        // Disable legacy rotated/scaled SafeZone on Van so our world-aligned JeepneySafeZone covers the elongated bay cleanly
        foreach (var legacySz in vanTr.GetComponentsInChildren<SafeZone>(true))
        {
            var legacyCol = legacySz.GetComponent<Collider2D>();
            if (legacyCol != null) legacyCol.enabled = false;
            legacySz.enabled = false;
            if (legacySz.gameObject != vanTr.gameObject)
            {
                legacySz.gameObject.SetActive(false);
            }
        }

        // Unscaled world-aligned container for crisp North-facing (+Y) 16-bit Jeepney visuals, shadows, headlights & roof
        GameObject jeepneyRoot = new GameObject("Jeepney");
        jeepneyRoot.transform.SetParent(envRoot, false);
        jeepneyRoot.transform.position = Vector3.zero;
        jeepneyRoot.transform.rotation = Quaternion.identity;
        jeepneyRoot.transform.localScale = Vector3.one;

        // 1. Elongated Open Passenger Interior & Cab Body (0.56 x 1.38 @ 104x256 px)
        Rect bodyRect = new Rect(cx - 0.28f, cy - 0.69f, 0.56f, 1.38f);
        GameObject bodyGo = CreateSpriteRect(jeepneyRoot.transform, "JeepneyBody",
            LoadSprite("Jeepney/jeepney_body_interior"), bodyRect, "Van", 0, 0);
        bodyGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // 1b. World-aligned SafeZone spanning the full elongated passenger bay ([cx-0.24..cx+0.24], [cy-0.66..cy+0.28])
        GameObject safeZoneGo = new GameObject("JeepneySafeZone");
        safeZoneGo.transform.SetParent(jeepneyRoot.transform, false);
        safeZoneGo.transform.position = new Vector3(cx, cy - 0.19f, 0f);
        var szCol = safeZoneGo.AddComponent<BoxCollider2D>();
        szCol.isTrigger = true;
        szCol.size = new Vector2(0.48f, 0.94f);
        safeZoneGo.AddComponent<SafeZone>();
        safeZoneGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // 1c. Bulkhead-mounted CRT CCTV Dashboard TV sprite + subtle cyan-green phosphor screen glow
        Rect tvRect = new Rect(cx - 0.105f, cy + 0.165f, 0.21f, 0.088f);
        GameObject tvPropGo = CreateSpriteRect(jeepneyRoot.transform, "JeepneyDashboardTV",
            LoadSprite("Jeepney/jeepney_dashboard_tv"), tvRect, "Van", 2, 0);
        tvPropGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject tvGlowGo = new GameObject("JeepneyDashboardTV_Glow");
        tvGlowGo.transform.SetParent(jeepneyRoot.transform, false);
        tvGlowGo.transform.position = new Vector3(cx, cy + 0.209f, 0f);
        var tvLight = tvGlowGo.AddComponent<Light2D>();
        tvLight.lightType = Light2D.LightType.Point;
        tvLight.color = new Color(0.28f, 0.92f, 0.72f, 1f);
        tvLight.intensity = 0.34f;
        tvLight.pointLightInnerRadius = 0.02f;
        tvLight.pointLightOuterRadius = 0.20f;
        tvLight.shadowsEnabled = false;
        AssignAllSortingLayers(tvLight);

        // Restore & align TruckDashboard UI collider over the bulkhead-mounted CRT TV monitor
        var dashboard = Object.FindAnyObjectByType<TruckDashboardController>(FindObjectsInactive.Include);
        if (dashboard != null)
        {
            GameObject dashGo = dashboard.gameObject;
            dashGo.SetActive(true);
            dashGo.layer = interactLayer;
            dashGo.transform.position = new Vector3(cx, cy + 0.209f, 0f);
            dashGo.transform.rotation = Quaternion.identity;

            Vector3 parentLossy = dashGo.transform.parent != null ? dashGo.transform.parent.lossyScale : Vector3.one;
            float sx = Mathf.Abs(parentLossy.x) > 0.0001f ? parentLossy.x : 1f;
            float sy = Mathf.Abs(parentLossy.y) > 0.0001f ? parentLossy.y : 1f;
            dashGo.transform.localScale = new Vector3(0.21f / sx, 0.088f / sy, 1f);

            var dashBox = dashGo.GetComponent<BoxCollider2D>();
            if (dashBox == null)
            {
                foreach (var existingCol in dashGo.GetComponents<Collider2D>())
                {
                    Object.DestroyImmediate(existingCol);
                }
                dashBox = dashGo.AddComponent<BoxCollider2D>();
            }
            dashBox.size = Vector2.one;
            dashBox.offset = Vector2.zero;
            dashBox.isTrigger = true;
            dashBox.enabled = true;
        }

        // 2. Walkable passenger bay + rear ramp apron floor trigger
        Rect walkRect = new Rect(cx - 0.18f, cy - 0.84f, 0.36f, 1.12f);
        GameObject bayFloor = new GameObject("JeepneyBayWalkable");
        bayFloor.transform.SetParent(jeepneyRoot.transform, false);
        bayFloor.transform.position = new Vector3(walkRect.center.x, walkRect.center.y, 0f);
        bayFloor.layer = groundLayer;
        var bayNmm = bayFloor.AddComponent<NavMeshModifier>();
        bayNmm.overrideArea = true;
        bayNmm.area = 1; // Not Walkable exterior safe zone bay
        var bayCol = bayFloor.AddComponent<BoxCollider2D>();
        bayCol.isTrigger = true;
        bayCol.size = walkRect.size;
        var bayFs = bayFloor.AddComponent<FloorSurface>();
        bayFs.surfaceType = SurfaceType.Concrete;
        bayFs.priority = 3;

        // 3. Clean shadow-casting Jeepney hull colliders (open only at center rear entrance [cx-0.12 .. cx+0.12])
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_West", new Rect(cx - 0.27f, cy - 0.68f, 0.06f, 1.36f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_East", new Rect(cx + 0.21f, cy - 0.68f, 0.06f, 1.36f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_CabBulkhead", new Rect(cx - 0.21f, cy + 0.253f, 0.42f, 0.43f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_RearL", new Rect(cx - 0.21f, cy - 0.68f, 0.09f, 0.05f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_RearR", new Rect(cx + 0.12f, cy - 0.68f, 0.09f, 0.05f), wallLayer);

        // 4. Dual front headlight spotlights shining North (+Y) down the dark street
        CreateJeepneyHeadlight(jeepneyRoot.transform, "JeepneyHeadlight_L", new Vector3(cx - 0.17f, cy + 0.67f, 0f));
        CreateJeepneyHeadlight(jeepneyRoot.transform, "JeepneyHeadlight_R", new Vector3(cx + 0.17f, cy + 0.67f, 0f));

        // 5. Jeepney Roof overlay with RoofFader (enclosedBuilding = false so outdoor weather stays outdoor)
        GameObject roofGo = CreateSpriteRect(jeepneyRoot.transform, "JeepneyRoof",
            LoadSprite("Jeepney/jeepney_roof"), bodyRect, "Roof", 1, 0);
        roofGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var roofCol = roofGo.AddComponent<BoxCollider2D>();
        roofCol.isTrigger = true;
        roofCol.size = new Vector2(0.40f, 0.92f);
        roofCol.offset = new Vector2(0f, -0.18f);
        var roofFader = roofGo.AddComponent<RoofFader>();
        SerializedObject rso = new SerializedObject(roofFader);
        var rRenderers = rso.FindProperty("spriteRenderers");
        rRenderers.arraySize = 1;
        rRenderers.GetArrayElementAtIndex(0).objectReferenceValue = roofGo.GetComponent<SpriteRenderer>();
        var enclosedProp = rso.FindProperty("enclosedBuilding");
        if (enclosedProp != null) enclosedProp.boolValue = false;
        rso.ApplyModifiedPropertiesWithoutUndo();

        // 6. Automated Jeepney rear boarding ramp (opens on arrival, closes on EndGameButton departure)
        CreateJeepneyRampDoor(doorsRoot, new Vector2(cx, cy - 0.665f), wallLayer, interactLayer);
    }

    private static void AddInvisibleShadowWall(Transform parent, string name, Rect rect, int wallLayer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        go.layer = wallLayer;

        var col = go.AddComponent<BoxCollider2D>();
        col.size = rect.size;

        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;

        AddShadowCaster2D(go);
    }

    private static void CreateJeepneyHeadlight(Transform parent, string name, Vector3 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;

        var light = go.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1.0f, 0.94f, 0.78f, 1f);
        light.intensity = 0.95f;
        light.falloffIntensity = 0.55f;
        light.pointLightInnerAngle = 26f;
        light.pointLightOuterAngle = 58f;
        light.pointLightInnerRadius = 0.22f;
        light.pointLightOuterRadius = 2.75f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 1.0f;
        AssignAllSortingLayers(light);
    }

    private static void CreateJeepneyRampDoor(Transform doorsRoot, Vector2 closedCenter, int wallLayer, int interactLayer)
    {
        Sprite closedSpr = LoadSprite("Jeepney/jeepney_ramp_closed");
        Sprite openSpr = LoadSprite("Jeepney/jeepney_ramp_open");

        GameObject doorRoot = new GameObject("JeepneyRampDoor");
        doorRoot.transform.SetParent(doorsRoot, false);
        doorRoot.transform.position = new Vector3(closedCenter.x, closedCenter.y, 0f);
        doorRoot.layer = interactLayer;
        doorRoot.AddComponent<NetworkObject>();

        // Closed barrier across the rear threshold
        GameObject cRef = new GameObject("ClosedRef");
        cRef.transform.SetParent(doorRoot.transform, false);
        cRef.transform.localPosition = Vector3.zero;
        cRef.layer = wallLayer;
        var cSr = cRef.AddComponent<SpriteRenderer>();
        cSr.sprite = closedSpr;
        cSr.sortingLayerName = "Van";
        cSr.sortingOrder = 2;
        if (closedSpr != null)
        {
            cRef.transform.localScale = new Vector3(0.24f / closedSpr.bounds.size.x, 0.055f / closedSpr.bounds.size.y, 1f);
            var col = cRef.AddComponent<BoxCollider2D>();
            col.size = closedSpr.bounds.size;
        }
        AddShadowCaster2D(cRef);
        cRef.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Open lowered treadplate ramp extending onto the street asphalt (-Y)
        GameObject oRef = new GameObject("OpenRef");
        oRef.transform.SetParent(doorRoot.transform, false);
        oRef.transform.localPosition = new Vector3(0f, -0.075f, 0f);
        oRef.layer = wallLayer;
        var oSr = oRef.AddComponent<SpriteRenderer>();
        oSr.sprite = openSpr != null ? openSpr : closedSpr;
        oSr.sortingLayerName = "Van";
        oSr.sortingOrder = -1;
        if (oSr.sprite != null)
        {
            oRef.transform.localScale = new Vector3(0.24f / oSr.sprite.bounds.size.x, 0.16f / oSr.sprite.bounds.size.y, 1f);
        }
        oRef.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        var nd = doorRoot.AddComponent<NetworkDoor>();
        SerializedObject so = new SerializedObject(nd);
        so.FindProperty("closedDoorRef").objectReferenceValue = cSr;
        so.FindProperty("openDoorRef").objectReferenceValue = oSr;
        so.FindProperty("motionType").enumValueIndex = (int)DoorMotionType.LinearLerp;
        so.FindProperty("swingSpeed").floatValue = 6f;
        so.FindProperty("swapSpriteWhenOpen").boolValue = true;
        so.FindProperty("disableObstacleColliderWhenOpen").boolValue = true;
        so.FindProperty("allowPlayerInteract").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Light2D EnsureGlobalLight()
    {
        Light2D globalLight = null;
        foreach (var l in Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include))
        {
            if (l.lightType == Light2D.LightType.Global)
            {
                globalLight = l;
                break;
            }
        }

        if (globalLight == null)
        {
            var go = new GameObject("Global Light 2D");
            globalLight = go.AddComponent<Light2D>();
            globalLight.lightType = Light2D.LightType.Global;
        }

        globalLight.enabled = true;
        globalLight.intensity = 0.006f;
        globalLight.color = new Color(0.38f, 0.46f, 0.64f);
        AssignAllSortingLayers(globalLight);
        return globalLight;
    }

    private static void BuildExteriorBarangay(Transform parent, int wallLayer, int groundLayer)
    {
        // Continuous vertical (North-South) 2-lane barangay street along west frontage x=[-1.72..-0.34], y=[-2.85..+2.85]
        Sprite roadSprite = LoadSprite("Exterior/asphalt_road");
        Rect roadRect = new Rect(-1.72f, -2.85f, 1.38f, 5.70f);
        var roadGo = CreateTiledSpriteRect(parent, "BarangayRoadTile", roadSprite, roadRect, "Floor", -10, groundLayer);
        var roadNmm = roadGo.AddComponent<NavMeshModifier>();
        roadNmm.overrideArea = true;
        roadNmm.area = 1; // Not Walkable outside road

        var roadCol = roadGo.AddComponent<BoxCollider2D>();
        roadCol.isTrigger = true;
        roadCol.size = new Vector2(roadRect.width, roadRect.height);
        var roadFs = roadGo.AddComponent<FloorSurface>();
        roadFs.surfaceType = SurfaceType.Pavement;
        roadFs.priority = 1;

        // Front walkway porch leading from street curb (-0.34) to recessed FrontDoor (0.28, 0.06)
        Sprite porchSprite = LoadSprite("House/floor_porch_tile");
        Rect porchRect = new Rect(-0.34f, -0.10f, 0.62f, 0.34f);
        var porchGo = CreateTiledSpriteRect(parent, "FrontPorch", porchSprite, porchRect, "Floor", -5, groundLayer);
        var porchNmm = porchGo.AddComponent<NavMeshModifier>();
        porchNmm.overrideArea = true;
        porchNmm.area = 1; // Not Walkable exterior porch
        var porchCol = porchGo.AddComponent<BoxCollider2D>();
        porchCol.isTrigger = true;
        porchCol.size = new Vector2(porchRect.width, porchRect.height);
        var porchFs = porchGo.AddComponent<FloorSurface>();
        porchFs.surfaceType = SurfaceType.Concrete;
        porchFs.priority = 2;

        // Parked Tricycle vertically along the west road shoulder
        CreateSolidProp(parent, "ParkedTricycle", LoadSprite("Exterior/tricycle_topdown"),
            new Rect(-1.52f, -1.95f, 0.38f, 0.50f), "Item", 0, wallLayer);

        // Roadside Trash scatters along vertical road shoulders (visual + non-blocking)
        Sprite trashSprite = LoadSprite("Exterior/trash_scatter");
        Vector2[] trashPos =
        {
            new Vector2(-1.46f, 1.15f),
            new Vector2(-0.52f, 1.48f),
            new Vector2(-0.54f, -1.35f),
            new Vector2(-1.44f, -2.35f)
        };
        for (int i = 0; i < trashPos.Length; i++)
        {
            var tr = CreateSpriteRect(parent, $"TrashScatter_{i}", trashSprite,
                new Rect(trashPos[i].x - 0.12f, trashPos[i].y - 0.12f, 0.24f, 0.24f), "Floor", -2, 0);
            var nmm = tr.AddComponent<NavMeshModifier>();
            nmm.ignoreFromBuild = true;
        }

        // 10 Exterior Weather Puddles along vertical road gutters and sidewalk edges (toggled by WeatherManager during Rain)
        Texture2D puddleTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SPRITE_PREFIX}Weather/puddle_ripples_sheet.png");
        Sprite defaultPuddleSprite = LoadSprite("Weather/puddle_ripples_sheet");
        if (puddleTex != null)
        {
            defaultPuddleSprite = Sprite.Create(puddleTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
        }
        Vector2[] puddlePos =
        {
            new Vector2(-0.42f, 1.18f),
            new Vector2(-1.48f, 0.42f),
            new Vector2(-0.56f, -0.68f),
            new Vector2(-1.45f, -1.18f),
            new Vector2(-0.52f, -1.85f),
            new Vector2(-1.28f, 1.78f),
            new Vector2(-0.48f, 2.08f),
            new Vector2(-1.52f, -0.22f),
            new Vector2(-1.18f, -2.30f),
            new Vector2(-0.62f, 1.38f)
        };
        for (int i = 0; i < puddlePos.Length; i++)
        {
            var pg = CreateSpriteRect(parent, $"WeatherPuddle_{i}", defaultPuddleSprite,
                new Rect(puddlePos[i].x - 0.14f, puddlePos[i].y - 0.10f, 0.28f, 0.20f), "Floor", -1, 0);
            var sr = pg.GetComponent<SpriteRenderer>();
            sr.color = new Color(1f, 1f, 1f, 0.8f);
            pg.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            pg.SetActive(false); // Shown on RainThunderstorm
        }

        // Packed Neighbor Houses with Concrete Firewalls & Ragged GI Roofs at crisp 64 PPU proportions
        (string name, string spr, Rect rect)[] neighbors =
        {
            ("Neighbor_North1_Firewall", "Exterior/neighbor_house_firewall_1", new Rect(-0.15f, 1.72f, 1.75f, 1.06f)),
            ("Neighbor_North2_Ragged", "Exterior/neighbor_roof_ragged_1", new Rect(1.62f, 1.72f, 1.25f, 1.15f)),
            ("Neighbor_North3_NoFirewall", "Exterior/neighbor_house_nofirewall_1", new Rect(2.89f, 1.72f, 1.62f, 1.06f)),
            ("Neighbor_South1_Firewall", "Exterior/neighbor_house_firewall_1", new Rect(-0.12f, -2.98f, 1.75f, 1.06f)),
            ("Neighbor_South2_Ragged", "Exterior/neighbor_roof_ragged_2", new Rect(1.66f, -2.98f, 1.50f, 1.06f)),
            ("Neighbor_South3_NoFirewall", "Exterior/neighbor_house_nofirewall_1", new Rect(3.18f, -2.98f, 1.62f, 1.06f)),
            ("Neighbor_East1_Ragged", "Exterior/neighbor_house_nofirewall_2", new Rect(4.56f, 0.05f, 1.25f, 1.62f)),
            ("Neighbor_East2_Firewall", "Exterior/neighbor_house_firewall_2", new Rect(4.56f, -1.65f, 1.25f, 1.62f)),
            ("Neighbor_West1_NoFirewall", "Exterior/neighbor_house_nofirewall_2", new Rect(-3.02f, 1.05f, 1.25f, 1.62f)),
            ("Neighbor_West2_Ragged", "Exterior/neighbor_roof_ragged_1", new Rect(-3.02f, -0.62f, 1.25f, 1.50f)),
            ("Neighbor_West3_Firewall", "Exterior/neighbor_house_firewall_2", new Rect(-3.02f, -2.30f, 1.25f, 1.62f)),
        };

        foreach (var n in neighbors)
        {
            CreateSolidProp(parent, n.name, LoadSprite(n.spr), n.rect, "Roof", 2, wallLayer);
        }

        // 5 Sidewalk cast-iron streetlamps with warm amber sodium-vapor glow & wall shadows
        BuildStreetLamps(parent, wallLayer);

        // Invisible street perimeter blocker at North, South, and West boundaries of street lane (no shadow casting)
        Sprite wallSpr = LoadSprite("House/wall_concrete_top");
        AddWallSegment(parent, "StreetBoundary_North", new Rect(-1.75f, 2.82f, 1.45f, 0.08f), wallLayer, wallSpr, castShadows: false);
        AddWallSegment(parent, "StreetBoundary_South", new Rect(-1.75f, -2.88f, 1.45f, 0.08f), wallLayer, wallSpr, castShadows: false);
        AddWallSegment(parent, "StreetBoundary_West", new Rect(-1.76f, -2.88f, 0.08f, 5.78f), wallLayer, wallSpr, castShadows: false);
    }

    private static void BuildStreetLamps(Transform parent, int wallLayer)
    {
        Sprite lampSpr = LoadSprite("Exterior/lamp_post");
        (Vector2 pos, float bulbOffsetX)[] lamps =
        {
            (new Vector2(-1.60f,  1.95f),  0.03f),
            (new Vector2(-0.40f,  1.15f), -0.03f),
            (new Vector2(-1.60f,  0.25f),  0.03f),
            (new Vector2(-0.40f, -0.85f), -0.03f),
            (new Vector2(-1.60f, -1.75f),  0.03f)
        };

        for (int i = 0; i < lamps.Length; i++)
        {
            Vector2 p = lamps[i].pos;
            GameObject postGo = CreateSpriteRect(parent, $"LampPost_{i}", lampSpr,
                new Rect(p.x - 0.09f, p.y - 0.16f, 0.18f, 0.42f), "Item", 4, wallLayer);
            var postCol = postGo.AddComponent<BoxCollider2D>();
            postCol.size = new Vector2(0.06f, 0.08f);
            postCol.offset = new Vector2(0f, -0.10f);
            postGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            GameObject lightGo = new GameObject($"LampPostLight_{i}");
            lightGo.transform.SetParent(postGo.transform, false);
            lightGo.transform.position = new Vector3(p.x + lamps[i].bulbOffsetX, p.y + 0.12f, 0f);
            lightGo.transform.localScale = Vector3.one;

            var l = lightGo.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = new Color(1.0f, 0.78f, 0.45f, 1f);
            l.intensity = 0.85f;
            l.falloffIntensity = 0.65f;
            l.pointLightInnerRadius = 0.14f;
            l.pointLightOuterRadius = 1.65f;
            l.shadowsEnabled = true;
            l.shadowIntensity = 1.0f;
            AssignAllSortingLayers(l);
        }
    }

    private static List<RoomSpec> GetHouseAndBasementRoomSpecs()
    {
        // Complete 6 Tanglewood Drive Main Floor (x=[0..4.48], y=[-1.85..+1.65]) + Basement ([0.64..3.80, -5.80..-4.10])
        return new List<RoomSpec>
        {
            new RoomSpec { Name = "BoysBedroom", Bounds = new Rect(0.00f, 0.40f, 0.92f, 1.15f), ExtraBounds = new Rect(0.00f, 0.22f, 0.28f, 0.18f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, SwitchPos = new Vector2(0.80f, 0.48f) },
            new RoomSpec { Name = "MainBathroom", Bounds = new Rect(0.92f, 0.40f, 0.62f, 1.15f), FloorSprite = "House/floor_bathroom_tile", Surface = SurfaceType.Concrete, SwitchPos = new Vector2(1.00f, 0.48f) },
            new RoomSpec { Name = "HallwayCloset", Bounds = new Rect(1.54f, 0.40f, 0.34f, 0.30f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, IsGhostRoom = false, HasSwitch = false },
            new RoomSpec { Name = "Nursery", Bounds = new Rect(1.88f, 0.40f, 0.88f, 1.25f), ExtraBounds = new Rect(1.54f, 0.70f, 0.34f, 0.95f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, SwitchPos = new Vector2(1.98f, 0.48f) },
            new RoomSpec { Name = "MasterBedroom", Bounds = new Rect(2.76f, 0.40f, 1.06f, 1.15f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, SwitchPos = new Vector2(2.86f, 0.48f) },
            new RoomSpec { Name = "MasterBathroom", Bounds = new Rect(3.82f, 0.96f, 0.66f, 0.59f), FloorSprite = "House/floor_bathroom_tile", Surface = SurfaceType.Concrete, SwitchPos = new Vector2(3.90f, 1.34f) },
            new RoomSpec { Name = "MasterCloset", Bounds = new Rect(3.82f, 0.40f, 0.66f, 0.56f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, IsGhostRoom = true, SwitchPos = new Vector2(3.90f, 0.82f) },
            new RoomSpec { Name = "FoyerHall", Bounds = new Rect(0.28f, -0.08f, 2.48f, 0.48f), ExtraBounds = new Rect(1.62f, -0.34f, 0.34f, 0.26f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, SwitchPos = new Vector2(0.42f, -0.02f) },
            new RoomSpec { Name = "LivingRoom", Bounds = new Rect(2.76f, -0.42f, 1.72f, 0.82f), ExtraBounds = new Rect(2.42f, -0.42f, 0.34f, 0.34f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Carpet, SwitchPos = new Vector2(2.86f, -0.34f) },
            new RoomSpec { Name = "MainFloorStairwell", Bounds = new Rect(3.12f, -0.78f, 1.36f, 0.36f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, IsGhostRoom = false, HasSwitch = false },
            new RoomSpec { Name = "Garage", Bounds = new Rect(0.00f, -1.85f, 1.62f, 1.77f), FloorSprite = "House/floor_garage_concrete", Surface = SurfaceType.Concrete, SwitchPos = new Vector2(1.52f, -0.36f) },
            new RoomSpec { Name = "UtilityRoom", Bounds = new Rect(1.62f, -0.78f, 0.80f, 0.44f), ExtraBounds = new Rect(1.96f, -0.34f, 0.46f, 0.26f), FloorSprite = "House/floor_bathroom_tile", Surface = SurfaceType.Concrete, SwitchPos = new Vector2(2.32f, -0.38f) },
            new RoomSpec { Name = "Kitchen", Bounds = new Rect(1.62f, -1.85f, 1.63f, 1.07f), ExtraBounds = new Rect(2.42f, -0.78f, 0.70f, 0.36f), FloorSprite = "House/floor_bathroom_tile", Surface = SurfaceType.Concrete, SwitchPos = new Vector2(2.50f, -0.86f) },
            new RoomSpec { Name = "DiningRoom", Bounds = new Rect(3.25f, -1.85f, 1.23f, 1.07f), FloorSprite = "House/floor_wood_tile", Surface = SurfaceType.Wood, SwitchPos = new Vector2(3.35f, -0.86f) },
            new RoomSpec { Name = "Basement", Bounds = new Rect(1.00f, -5.80f, 2.80f, 1.70f), ExtraBounds = new Rect(0.64f, -5.55f, 0.36f, 0.80f), FloorSprite = "House/floor_basement_concrete", Surface = SurfaceType.Concrete, SwitchPos = new Vector2(1.08f, -4.58f) }
        };
    }

    private static List<RoomSpec> BuildHouseAndBasementGeometry(
        Transform houseRoot, Transform basementRoot, Transform wallsRoot, Transform doorsRoot, Transform furnRoot,
        Transform ghostMarkers, Transform lightSys, int wallLayer, int interactLayer, int groundLayer)
    {
        Sprite wallSpr = LoadSprite("House/wall_concrete_top");
        Sprite doorClosed = LoadSprite("House/door_wood_closed");
        Sprite doorOpen = LoadSprite("House/door_wood_open");
        Sprite cabClosed = LoadSprite("House/cabinet_closed");
        Sprite cabOpen = LoadSprite("House/cabinet_open");
        Sprite windowSpr = LoadSprite("House/window_frame_top");

        List<RoomSpec> rooms = GetHouseAndBasementRoomSpecs();

        // Instantiate tiled floors, FloorSurface triggers, GhostRoomMarkers, Lights + Switches + SafeZones
        foreach (var r in rooms)
        {
            Transform targetParent = r.Name == "Basement" ? basementRoot : houseRoot;
            Sprite floorSpr = LoadSprite(r.FloorSprite);
            CreateRoomFloor(targetParent, r.Name, r.Bounds, floorSpr, r.Surface, groundLayer);
            if (r.ExtraBounds.HasValue)
            {
                CreateRoomFloor(targetParent, r.Name + "_Alcove", r.ExtraBounds.Value, floorSpr, r.Surface, groundLayer);
            }

            if (r.IsGhostRoom && ghostMarkers != null)
            {
                CreateGhostRoomMarker(ghostMarkers, r.Name, r.Bounds);
            }

            if (r.HasSwitch)
            {
                CreateRoomLightingAndSwitch(lightSys, r.Name, r.Bounds, wallLayer, interactLayer, r.SwitchPos, r.ExtraBounds);
            }
        }

        // --- OUTER HOUSE PERIMETER WALLS (thickness = 0.055f) ---
        float wt = 0.055f;
        // North stepped perimeter (Boys + MainBath [0..1.54], Nursery bump [1.54..2.76], Master [2.76..4.48])
        AddWallSegment(wallsRoot, "Ext_NorthWall_West", new Rect(-wt * 0.5f, 1.55f - wt * 0.5f, 1.54f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_NorthWall_NurseryL", new Rect(1.54f - wt * 0.5f, 1.55f, wt, 0.10f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_NorthWall_NurseryTop", new Rect(1.54f - wt * 0.5f, 1.65f - wt * 0.5f, 1.22f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_NorthWall_NurseryR", new Rect(2.76f - wt * 0.5f, 1.55f, wt, 0.10f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_NorthWall_East", new Rect(2.76f - wt * 0.5f, 1.55f - wt * 0.5f, 1.72f + wt, wt), wallLayer, wallSpr);

        // East & South perimeter
        AddWallSegment(wallsRoot, "Ext_EastWall", new Rect(4.48f - wt * 0.5f, -1.85f, wt, 3.40f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_SouthWall", new Rect(-wt * 0.5f, -1.85f - wt * 0.5f, 4.48f + wt, wt), wallLayer, wallSpr);

        // West stepped perimeter around BoysBedroom, BoysCloset, recessed FrontDoor (0.28, 0.06), and Garage
        AddWallSegment(wallsRoot, "Ext_WestWall_Boys", new Rect(-wt * 0.5f, 0.22f, wt, 1.33f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_WestWall_BoysClosetBot", new Rect(-wt * 0.5f, 0.22f - wt * 0.5f, 0.28f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_WestWall_BoysClosetSide", new Rect(0.28f - wt * 0.5f, 0.22f, wt, 0.18f), wallLayer, wallSpr);
        AddVerticalWallWithDoor(wallsRoot, doorsRoot, "Ext_WestWall_Porch", 0.28f, -0.08f, 0.22f, 0.06f,
            wt, wallSpr, doorClosed, doorOpen, wallLayer, interactLayer, "FrontDoor", isFrontDoor: true, requiresKey: true, swingDeg: -90f);
        AddWallSegment(wallsRoot, "Ext_WestWall_GarageTop", new Rect(-wt * 0.5f, -0.08f - wt * 0.5f, 0.28f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Ext_WestWall_Garage", new Rect(-wt * 0.5f, -1.85f, wt, 1.77f), wallLayer, wallSpr);

        // Exterior Windows matching 6 Tanglewood Drive schematic
        CreateWindowAccent(wallsRoot, "Win_BoysWest", windowSpr, new Vector2(0.00f, 0.96f), vertical: true);
        CreateWindowAccent(wallsRoot, "Win_MainBathNorth", windowSpr, new Vector2(1.23f, 1.55f), vertical: false);
        CreateWindowAccent(wallsRoot, "Win_NurseryNorth", windowSpr, new Vector2(2.24f, 1.65f), vertical: false);
        CreateWindowAccent(wallsRoot, "Win_MasterNorth", windowSpr, new Vector2(3.28f, 1.55f), vertical: false);
        CreateWindowAccent(wallsRoot, "Win_MasterBathEast", windowSpr, new Vector2(4.48f, 1.26f), vertical: true);
        CreateWindowAccent(wallsRoot, "Win_LivingEast", windowSpr, new Vector2(4.48f, 0.00f), vertical: true);
        CreateWindowAccent(wallsRoot, "Win_DiningEast", windowSpr, new Vector2(4.48f, -1.32f), vertical: true);
        CreateWindowAccent(wallsRoot, "Win_DiningSouth", windowSpr, new Vector2(3.86f, -1.85f), vertical: false);
        CreateWindowAccent(wallsRoot, "Win_KitchenSouth", windowSpr, new Vector2(2.35f, -1.85f), vertical: false);

        // --- INTERIOR ROOM PARTITIONS WITH CLEAN DOORWAYS ---
        // North Wing vertical dividers
        AddWallSegment(wallsRoot, "Div_Boys_Bath", new Rect(0.92f - wt * 0.5f, 0.40f, wt, 1.15f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Div_Bath_Nursery", new Rect(1.54f - wt * 0.5f, 0.40f, wt, 1.15f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Div_HallCloset_Nursery", new Rect(1.88f - wt * 0.5f, 0.40f, wt, 0.30f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Div_HallCloset_Top", new Rect(1.54f, 0.70f - wt * 0.5f, 0.34f, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Div_Nursery_Master", new Rect(2.76f - wt * 0.5f, 0.40f, wt, 1.15f), wallLayer, wallSpr);

        // Master Bathroom & Master Closet (stacked on far east wing)
        AddVerticalWallWithDoor(wallsRoot, doorsRoot, "Div_Master_Bath", 3.82f, 0.96f, 1.55f, 1.18f,
            wt, wallSpr, doorClosed, doorOpen, wallLayer, interactLayer, "Door_MasterBath", swingDeg: -90f);
        AddVerticalWallWithDoor(wallsRoot, doorsRoot, "Div_Master_Closet", 3.82f, 0.40f, 0.96f, 0.64f,
            wt, wallSpr, doorClosed, doorOpen, wallLayer, interactLayer, "Door_MasterCloset", swingDeg: -90f);
        AddWallSegment(wallsRoot, "Div_MasterBath_Closet", new Rect(3.82f, 0.96f - wt * 0.5f, 0.66f, wt), wallLayer, wallSpr);

        // North Corridor horizontal wall (y = 0.40f) extending to xEnd = 4.48f to enclose MasterCloset's south partition
        AddHorizontalSegmentWithDoors(wallsRoot, doorsRoot, "Wall_NorthCorridor", 0.00f, 4.48f, 0.40f,
            new[]
            {
                ("Boys_Cabinet_Door", 0.14f, true, 0f),
                ("Door_BoysBed", 0.64f, false, 90f),
                ("Door_MainBath", 1.18f, false, 90f),
                ("Hallway_Cabinet_Door", 1.71f, true, 0f),
                ("Door_Nursery", 2.16f, false, 90f),
                ("Door_MasterBed", 3.04f, false, 90f)
            },
            wt, wallSpr, doorClosed, doorOpen, cabClosed, cabOpen, wallLayer, interactLayer);

        // Foyer South Wall (y = -0.08f, x = 0.28..2.42) with FoyerCloset at x=1.79 and open east transition into LivingRoom
        AddWallSegment(wallsRoot, "Wall_Foyer_Garage", new Rect(0.28f, -0.08f - wt * 0.5f, 1.34f, wt), wallLayer, wallSpr);
        AddHorizontalWallWithDoor(wallsRoot, doorsRoot, "Wall_Foyer_Closet", 1.62f, 1.96f, -0.08f, 1.79f,
            wt, wallSpr, cabClosed, cabOpen, wallLayer, interactLayer, "Foyer_Cabinet_Door", isHidingSpot: true, swingDeg: 0f);
        AddWallSegment(wallsRoot, "Wall_Foyer_UtilityTop", new Rect(1.96f, -0.08f - wt * 0.5f, 0.46f, wt), wallLayer, wallSpr);

        // FoyerCloset / UtilityRoom chamfered divider (seals FoyerCloset [1.62..1.96, -0.34..-0.08] from UtilityRoom)
        AddWallSegment(wallsRoot, "Div_FoyerCloset_Bot", new Rect(1.62f, -0.34f - wt * 0.5f, 0.34f, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Div_FoyerCloset_Right", new Rect(1.96f - wt * 0.5f, -0.34f, wt, 0.26f), wallLayer, wallSpr);
        AddChamferWallPiece(wallsRoot, "Utility_ChamferCorner", new Vector2(1.94f, -0.32f), 0.14f, wt, 45f, wallLayer, wallSpr);

        // Garage East Wall (x = 1.62f, y = -1.85..-0.08) -> single door in north-east corner into UtilityRoom at y = -0.56f
        AddVerticalWallWithDoor(wallsRoot, doorsRoot, "Wall_Garage_East", 1.62f, -1.85f, -0.08f, -0.56f,
            wt, wallSpr, doorClosed, doorOpen, wallLayer, interactLayer, "Door_Garage_Utility", swingDeg: -90f);

        // UtilityRoom South Wall (separating UtilityRoom from Kitchen) & East Wall (into central passage at y = -0.56f)
        AddWallSegment(wallsRoot, "Wall_Utility_South", new Rect(1.62f, -0.78f - wt * 0.5f, 0.80f, wt), wallLayer, wallSpr);
        AddVerticalWallWithDoor(wallsRoot, doorsRoot, "Wall_Utility_East", 2.42f, -0.78f, -0.08f, -0.56f,
            wt, wallSpr, doorClosed, doorOpen, wallLayer, interactLayer, "Door_Utility_East", swingDeg: -90f);

        // Main-Floor Basement Stairwell enclosure (x=[3.12..4.48], y=[-0.78..-0.42]) between LivingRoom and DiningRoom
        AddWallSegment(wallsRoot, "Wall_Stairs_North", new Rect(3.12f, -0.42f - wt * 0.5f, 1.36f, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Wall_Stairs_South", new Rect(3.12f, -0.78f - wt * 0.5f, 1.36f, wt), wallLayer, wallSpr);
        AddVerticalWallWithDoor(wallsRoot, doorsRoot, "Wall_Stairs_West", 3.12f, -0.78f, -0.42f, -0.60f,
            wt, wallSpr, doorClosed, doorOpen, wallLayer, interactLayer, "Door_BasementStairs", swingDeg: 90f);

        // --- BASEMENT OUTER WALLS ([1.00..3.80, -5.80..-4.10] + SW alcove [0.64..1.00, -5.55..-4.75]) ---
        AddWallSegment(wallsRoot, "Bsm_NorthWall", new Rect(1.00f - wt * 0.5f, -4.10f - wt * 0.5f, 2.80f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_SouthWall", new Rect(1.00f - wt * 0.5f, -5.80f - wt * 0.5f, 2.80f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_EastWall", new Rect(3.80f - wt * 0.5f, -5.80f, wt, 1.70f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_WestUpper", new Rect(1.00f - wt * 0.5f, -4.75f, wt, 0.65f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_AlcoveTop", new Rect(0.64f - wt * 0.5f, -4.75f - wt * 0.5f, 0.36f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_AlcoveWest", new Rect(0.64f - wt * 0.5f, -5.55f, wt, 0.80f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_AlcoveBot", new Rect(0.64f - wt * 0.5f, -5.55f - wt * 0.5f, 0.36f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_WestLower", new Rect(1.00f - wt * 0.5f, -5.80f, wt, 0.25f), wallLayer, wallSpr);
        // Basement NW horizontal staircase divider (opening west at x=1.00..1.42)
        AddWallSegment(wallsRoot, "Bsm_Stair_South", new Rect(1.42f, -4.46f - wt * 0.5f, 0.88f, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bsm_Stair_East", new Rect(2.28f - wt * 0.5f, -4.46f, wt, 0.36f), wallLayer, wallSpr);

        // --- INTERIOR AREA RUGS (Non-blocking floor accents) ---
        CreateNonBlockingProp(furnRoot, "Boys_Rug", LoadSprite("House/Furniture/furn_rug_boys"), new Rect(0.32f, 0.65f, 0.38f, 0.28f), "Floor", -2);
        CreateNonBlockingProp(furnRoot, "Nursery_Rug", LoadSprite("House/Furniture/furn_rug_nursery"), new Rect(2.10f, 0.88f, 0.36f, 0.36f), "Floor", -2);
        CreateNonBlockingProp(furnRoot, "Living_Rug", LoadSprite("House/Furniture/furn_rug_living"), new Rect(3.26f, -0.30f, 0.64f, 0.56f), "Floor", -2);

        // --- FURNITURE PLACEMENTS (Pure 90° Top-Down Overhead Sprites + Blockers) ---
        CreateSolidProp(furnRoot, "Boys_Bed", LoadSprite("House/Furniture/furn_bed"), new Rect(0.08f, 1.05f, 0.48f, 0.36f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Boys_Desk", LoadSprite("House/Furniture/furn_desk"), new Rect(0.56f, 1.25f, 0.30f, 0.20f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "MainBath_Set", LoadSprite("House/Furniture/furn_bathroom_set"), new Rect(0.98f, 1.05f, 0.46f, 0.42f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Nursery_Crib", LoadSprite("House/Furniture/furn_crib"), new Rect(1.62f, 1.22f, 0.36f, 0.28f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Nursery_Dresser", LoadSprite("House/Furniture/furn_dresser"), new Rect(2.30f, 1.38f, 0.38f, 0.18f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Master_Bed", LoadSprite("House/Furniture/furn_bed"), new Rect(2.86f, 1.02f, 0.56f, 0.42f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Master_Dresser", LoadSprite("House/Furniture/furn_dresser"), new Rect(3.26f, 0.48f, 0.36f, 0.18f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "MasterBath_Set", LoadSprite("House/Furniture/furn_bathroom_set"), new Rect(4.02f, 1.02f, 0.40f, 0.44f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Living_Sofa", LoadSprite("House/Furniture/furn_sofa"), new Rect(3.18f, -0.02f, 0.66f, 0.26f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Living_TVStand", LoadSprite("House/Furniture/furn_tv_stand"), new Rect(3.28f, -0.38f, 0.50f, 0.14f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Utility_WasherDryer", LoadSprite("House/Furniture/furn_washer_dryer"), new Rect(1.98f, -0.30f, 0.40f, 0.20f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Garage_Car", LoadSprite("House/Furniture/furn_garage_car"), new Rect(0.32f, -1.48f, 0.96f, 0.54f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Garage_Workbench", LoadSprite("House/Furniture/furn_workbench"), new Rect(0.55f, -0.34f, 0.64f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Kitchen_Counter", LoadSprite("House/Furniture/furn_counter_stove"), new Rect(1.72f, -1.78f, 0.88f, 0.28f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Kitchen_Fridge", LoadSprite("House/Furniture/furn_fridge"), new Rect(2.68f, -1.78f, 0.28f, 0.28f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Kitchen_Island", LoadSprite("House/Furniture/furn_kitchen_island"), new Rect(2.15f, -1.32f, 0.62f, 0.32f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Dining_Table", LoadSprite("House/Furniture/furn_dining_table"), new Rect(3.62f, -1.45f, 0.56f, 0.44f), "Item", 0, wallLayer);

        // Basement 4 structural support pillars (2x2 grid) + workbench
        Sprite pillarSpr = LoadSprite("House/Furniture/furn_pillar");
        CreateSolidProp(furnRoot, "Basement_Pillar_NW", pillarSpr, new Rect(1.68f, -4.85f, 0.22f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Basement_Pillar_NE", pillarSpr, new Rect(2.68f, -4.85f, 0.22f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Basement_Pillar_SW", pillarSpr, new Rect(1.68f, -5.45f, 0.22f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Basement_Pillar_SE", pillarSpr, new Rect(2.68f, -5.45f, 0.22f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(furnRoot, "Basement_Workbench", LoadSprite("House/Furniture/furn_workbench"), new Rect(2.85f, -5.66f, 0.72f, 0.30f), "Item", 0, wallLayer);

        return rooms;
    }

    private static void BuildMainHouseRoof(Transform houseRoot)
    {
        GameObject roofGo = new GameObject("MainHouseRoof");
        roofGo.transform.SetParent(houseRoot, false);
        roofGo.transform.position = Vector3.zero;
        roofGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        Sprite roofSpr = LoadSprite("House/roof_main_gi");
        (string name, Rect rect)[] roofPanels =
        {
            ("Roof_NorthWing", new Rect(0.00f, 0.22f, 4.48f, 1.33f)),
            ("Roof_NurseryBump", new Rect(1.54f, 1.55f, 1.22f, 0.10f)),
            ("Roof_CenterBand", new Rect(0.28f, -0.08f, 4.20f, 0.30f)),
            ("Roof_SouthWing", new Rect(0.00f, -1.85f, 4.48f, 1.77f))
        };

        List<SpriteRenderer> panelRenderers = new List<SpriteRenderer>(roofPanels.Length);
        foreach (var p in roofPanels)
        {
            GameObject panel = CreateTiledSpriteRect(roofGo.transform, p.name, roofSpr, p.rect, "Roof", 0, 0);
            panel.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            panelRenderers.Add(panel.GetComponent<SpriteRenderer>());

            var col = roofGo.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.offset = p.rect.center;
            col.size = p.rect.size;
        }

        var fader = roofGo.AddComponent<RoofFader>();
        SerializedObject so = new SerializedObject(fader);
        var arr = so.FindProperty("spriteRenderers");
        arr.arraySize = panelRenderers.Count;
        for (int i = 0; i < panelRenderers.Count; i++)
        {
            arr.GetArrayElementAtIndex(i).objectReferenceValue = panelRenderers[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildStairTeleporters(Transform houseRoot, Transform basementRoot, int interactLayer)
    {
        // Main Floor Horizontal Stairwell Down (inside MainFloorStairwell x=[3.12..4.48, -0.78..-0.42], descending eastward)
        Sprite stairDownSpr = LoadSprite("House/stairwell_down");
        GameObject upStair = CreateSpriteRect(houseRoot, "Stairwell_MainFloor_Down", stairDownSpr,
            new Rect(3.66f, -0.74f, 0.68f, 0.28f), "Floor", 2, interactLayer);
        var upCol = upStair.AddComponent<BoxCollider2D>();
        upCol.isTrigger = true;
        upStair.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var upSpawn = new GameObject("ArrivalFromBasement").transform;
        upSpawn.SetParent(houseRoot, false);
        upSpawn.position = new Vector3(3.36f, -0.60f, 0f); // Clear west landing inside stairwell

        // Basement Horizontal Stairwell Up (in NW corner of Basement [1.52..2.24, -4.42..-4.14], descending westward)
        Sprite stairUpSpr = LoadSprite("House/stairwell_up");
        GameObject downStair = CreateSpriteRect(basementRoot, "Stairwell_Basement_Up", stairUpSpr,
            new Rect(1.52f, -4.42f, 0.72f, 0.28f), "Floor", 2, interactLayer);
        var downCol = downStair.AddComponent<BoxCollider2D>();
        downCol.isTrigger = true;
        downStair.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var downSpawn = new GameObject("ArrivalFromMainFloor").transform;
        downSpawn.SetParent(basementRoot, false);
        downSpawn.position = new Vector3(1.22f, -4.28f, 0f); // Clear west landing in Basement

        var tpUp = upStair.AddComponent<StairTeleporter>();
        var tpDown = downStair.AddComponent<StairTeleporter>();
        tpUp.Configure(tpDown, upSpawn, "Basement");
        tpDown.Configure(tpUp, downSpawn, "Main Floor");
        tpUp.EnsureMidSpriteBackupNode();
        tpDown.EnsureMidSpriteBackupNode();

        Transform upMid = upStair.transform.Find("MidStairTeleporterNode");
        if (upMid != null) upMid.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Transform downMid = downStair.transform.Find("MidStairTeleporterNode");
        if (downMid != null) downMid.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static void BuildHideZones(Transform houseRoot, Transform basementRoot)
    {
        (Transform parent, string name, Rect rect)[] zones =
        {
            (houseRoot, "HideZone_BoysCloset", new Rect(0.03f, 0.23f, 0.22f, 0.15f)),
            (houseRoot, "HideZone_HallwayCloset", new Rect(1.57f, 0.43f, 0.28f, 0.24f)),
            (houseRoot, "HideZone_FoyerCloset", new Rect(1.65f, -0.32f, 0.28f, 0.22f)),
            (houseRoot, "HideZone_MasterCloset", new Rect(3.92f, 0.46f, 0.48f, 0.44f)),
            (houseRoot, "HideZone_UtilityLocker", new Rect(1.66f, -0.76f, 0.28f, 0.16f)),
            (houseRoot, "HideZone_GarageCorner", new Rect(0.08f, -1.76f, 0.36f, 0.36f)),
            (basementRoot, "HideZone_BasementStorage", new Rect(3.22f, -4.58f, 0.46f, 0.38f))
        };

        foreach (var z in zones)
        {
            GameObject go = new GameObject(z.name);
            go.transform.SetParent(z.parent, false);
            go.transform.position = new Vector3(z.rect.center.x, z.rect.center.y, 0f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(z.rect.width, z.rect.height);
            go.AddComponent<HideZone>();
        }
    }

    private static void BuildFuseBox(Transform furnRoot, int interactLayer)
    {
        GameObject fbGo = new GameObject("FuseBox");
        fbGo.transform.SetParent(furnRoot, false);
        fbGo.transform.position = new Vector3(1.50f, -1.18f, 0f);
        fbGo.layer = interactLayer;

        Sprite fbSpr = LoadSprite("House/Furniture/furn_fusebox");
        var sr = fbGo.AddComponent<SpriteRenderer>();
        sr.sprite = fbSpr;
        sr.sortingLayerName = "Item";
        sr.sortingOrder = 1;
        if (fbSpr != null)
        {
            fbGo.transform.localScale = new Vector3(0.14f / fbSpr.bounds.size.x, 0.14f / fbSpr.bounds.size.y, 1f);
        }

        var col = fbGo.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        fbGo.AddComponent<NetworkObject>();
        fbGo.AddComponent<FuseBox>();
        fbGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static void BuildGhostProps(Transform propsRoot)
    {
        (string name, string display, string sheet, Vector2 pos, bool breakable, string sfx)[] props =
        {
            ("Prop_KitchenPlate1", "Ceramic Plate", "Props/prop_plate_sheet", new Vector2(2.30f, -1.18f), true, "prop_shatter_glass"),
            ("Prop_KitchenCup", "Coffee Mug", "Props/prop_cup_sheet", new Vector2(2.58f, -1.18f), true, "prop_shatter_glass"),
            ("Prop_DiningBottle", "Glass Bottle", "Props/prop_glass_bottle_sheet", new Vector2(3.88f, -1.24f), true, "prop_shatter_glass"),
            ("Prop_LivingRemote", "TV Remote", "Props/prop_tv_remote_sheet", new Vector2(3.50f, -0.14f), false, "prop_knock_wood"),
            ("Prop_LivingFrame", "Family Photo", "Props/prop_picture_frame_sheet", new Vector2(2.90f, 0.18f), true, "prop_shatter_glass"),
            ("Prop_DiningChair", "Dining Chair", "Props/prop_chair_sheet", new Vector2(3.48f, -1.18f), false, "prop_knock_wood"),
            ("Prop_BoysShoe", "Slipper", "Props/prop_shoe_sheet", new Vector2(0.48f, 0.58f), false, "prop_knock_wood"),
            ("Prop_MasterBottle", "Perfume Bottle", "Props/prop_glass_bottle_sheet", new Vector2(3.50f, 0.88f), true, "prop_shatter_glass"),
            ("Prop_GaragePaintCan", "Paint Can", "Props/prop_paint_can_sheet", new Vector2(1.36f, -0.88f), false, "prop_knock_wood"),
            ("Prop_BasementPaintCan", "Old Paint Can", "Props/prop_paint_can_sheet", new Vector2(3.12f, -5.28f), false, "prop_knock_wood"),
            ("Prop_BasementBottle", "Dusty Bottle", "Props/prop_glass_bottle_sheet", new Vector2(2.24f, -5.08f), true, "prop_shatter_glass")
        };

        foreach (var p in props)
        {
            GameObject go = new GameObject(p.name);
            go.transform.SetParent(propsRoot, false);
            go.transform.position = new Vector3(p.pos.x, p.pos.y, 0f);
            go.transform.localScale = new Vector3(0.14f, 0.14f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Item";
            sr.sortingOrder = 2;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.22f;
            col.isTrigger = true;

            go.AddComponent<NetworkObject>();
            var prop = go.AddComponent<GhostPropInteractable>();
            prop.Configure(p.display, p.sheet, p.breakable, p.sfx);
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }
    }

    private static void BuildGhostNodeGraph(Transform pathSys)
    {
        if (pathSys == null) return;

        Vector2[] pts =
        {
            /*0*/  new Vector2( 0.38f, 0.06f),  // Inside Front Door threshold (ghost never exits outside)
            /*1*/  new Vector2( 0.58f, 0.08f),  // Foyer West
            /*2*/  new Vector2(1.38f, 0.12f),   // Foyer Mid
            /*3*/  new Vector2(2.22f, 0.12f),   // Foyer East
            /*4*/  new Vector2(0.54f, 0.78f),   // BoysBedroom
            /*5*/  new Vector2(1.22f, 0.78f),   // MainBathroom
            /*6*/  new Vector2(2.16f, 0.85f),   // Nursery
            /*7*/  new Vector2(3.24f, 0.75f),   // MasterBedroom
            /*8*/  new Vector2(3.94f, 1.18f),   // MasterBathroom
            /*9*/  new Vector2(4.10f, 0.65f),   // MasterCloset
            /*10*/ new Vector2(2.96f, 0.08f),   // LivingRoom
            /*11*/ new Vector2(2.76f, -0.56f),  // Central Junction
            /*12*/ new Vector2(2.04f, -0.56f),  // UtilityRoom
            /*13*/ new Vector2(1.38f, -0.56f),  // Garage North
            /*14*/ new Vector2(1.40f, -1.48f),  // Garage South
            /*15*/ new Vector2(2.86f, -0.92f),  // Kitchen North
            /*16*/ new Vector2(1.92f, -1.25f),  // Kitchen South
            /*17*/ new Vector2(3.85f, -1.10f),  // DiningRoom
            /*18*/ new Vector2(3.36f, -0.60f),  // MainFloor Stair Landing
            /*19*/ new Vector2(1.22f, -4.28f),  // Basement Stair Landing
            /*20*/ new Vector2(1.25f, -5.12f),  // Basement West
            /*21*/ new Vector2(2.22f, -5.12f),  // Basement Mid
            /*22*/ new Vector2(3.22f, -5.12f)   // Basement East
        };

        List<GhostNode> nodes = new List<GhostNode>(pts.Length);
        for (int i = 0; i < pts.Length; i++)
        {
            GameObject g = new GameObject($"GhostNode_{i}");
            g.transform.SetParent(pathSys, false);
            g.transform.position = new Vector3(pts[i].x, pts[i].y, 0f);
            nodes.Add(g.AddComponent<GhostNode>());
        }

        void Link(int a, int b)
        {
            if (!nodes[a].neighbors.Contains(nodes[b])) nodes[a].neighbors.Add(nodes[b]);
            if (!nodes[b].neighbors.Contains(nodes[a])) nodes[b].neighbors.Add(nodes[a]);
        }

        Link(0, 1); Link(1, 2); Link(2, 3);
        Link(1, 4); Link(2, 5); Link(3, 6); Link(3, 10); Link(3, 11);
        Link(10, 7); Link(7, 8); Link(7, 9); Link(10, 11);
        Link(11, 12); Link(11, 15); Link(11, 18);
        Link(12, 13); Link(13, 14);
        Link(15, 16); Link(15, 17);
        Link(18, 19); // Across Stair NavMeshLink!
        Link(19, 20); Link(20, 21); Link(21, 22);
    }

    private static void ConfigureGhostHandler(Transform ghostMgr)
    {
        if (ghostMgr == null) return;
        GhostHandler handler = Object.FindAnyObjectByType<GhostHandler>();
        if (handler != null)
        {
            SerializedObject so = new SerializedObject(handler);
            var markerProp = so.FindProperty("ghostroomMarker");
            if (markerProp != null) markerProp.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void CreateWeatherManager(Transform envRoot, Light2D globalLight)
    {
        GameObject wmGo = new GameObject("WeatherManager");
        wmGo.transform.SetParent(envRoot, false);
        wmGo.AddComponent<NetworkObject>();
        var wm = wmGo.AddComponent<WeatherManager>();

        SerializedObject so = new SerializedObject(wm);
        var glProp = so.FindProperty("globalLight");
        if (glProp != null) glProp.objectReferenceValue = globalLight;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BakeNavMeshAndLink(Transform envRoot)
    {
        NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            GameObject nmGo = new GameObject("NavhMesh");
            nmGo.transform.SetParent(envRoot, false);
            surface = nmGo.AddComponent<NavMeshSurface>();
        }

        BarangayMapFixer.BakeAndPersistNavMeshSurface(surface, BARANGAY_SCENE_PATH);

        // Bidirectional NavMeshLink across Main Floor Stair Landing <-> Basement Stair Landing
        GameObject linkGo = new GameObject("StairNavMeshLink");
        linkGo.transform.SetParent(envRoot, false);
        linkGo.transform.position = Vector3.zero;
        var link = linkGo.AddComponent<NavMeshLink>();
        link.agentTypeID = surface.agentTypeID;
        // Endpoints follow the ArrivalFrom markers (kept in sync live by StairNavLinkAutoSync)
        GameObject startMarkerGo = GameObject.Find(StairNavLinkAutoSync.StartMarkerName);
        GameObject endMarkerGo = GameObject.Find(StairNavLinkAutoSync.EndMarkerName);
        link.startPoint = startMarkerGo != null ? startMarkerGo.transform.position : new Vector3(3.36f, -0.60f, 0f);
        link.endPoint = endMarkerGo != null ? endMarkerGo.transform.position : new Vector3(1.22f, -4.28f, 0f);
        link.width = 0.22f;
        link.costModifier = -1;
        link.bidirectional = true;
        link.autoUpdate = true;
        link.area = 0;
        link.UpdateLink();
    }

    private static void RegisterSceneInBuildSettings()
    {
        var desiredPaths = new[]
        {
            "Assets/Scenes/MainMenuScene.unity",
            "Assets/Scenes/LobbyScene.unity",
            BARANGAY_SCENE_PATH,
            "Assets/Scenes/CemeteryMap.unity",
            "Assets/Scenes/LoadingScene.unity",
            "Assets/Scenes/WarehouseTutorialScene.unity"
        };

        var buildScenes = new List<EditorBuildSettingsScene>();
        foreach (string p in desiredPaths)
        {
            if (FileExists(p))
            {
                buildScenes.Add(new EditorBuildSettingsScene(p, true));
            }
        }
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    // --- Primitive Scene Builders ---

    private static GameObject CreateSpriteRect(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int layer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        go.layer = layer;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = sortOrder;

        if (sprite != null)
        {
            Vector2 sz = sprite.bounds.size;
            go.transform.localScale = new Vector3(
                rect.width / Mathf.Max(0.001f, sz.x),
                rect.height / Mathf.Max(0.001f, sz.y),
                1f);
        }
        return go;
    }

    private static GameObject CreateTiledSpriteRect(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int layer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        go.transform.localScale = Vector3.one;
        go.layer = layer;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = sortOrder;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(rect.width, rect.height);
        return go;
    }

    private static GameObject CreateNonBlockingProp(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder)
    {
        GameObject go = CreateSpriteRect(parent, name, sprite, rect, sortingLayer, sortOrder, 0);
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        return go;
    }

    private static void CreateWindowAccent(Transform wallsRoot, string name, Sprite windowSpr, Vector2 center, bool vertical)
    {
        GameObject go = CreateSpriteRect(wallsRoot, name, windowSpr,
            new Rect(center.x - 0.17f, center.y - 0.032f, 0.34f, 0.064f), "Wall", 2, 0);
        if (vertical) go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static void AddChamferWallPiece(Transform wallsRoot, string name, Vector2 center, float length, float thickness, float angleDeg, int wallLayer, Sprite wallSprite)
    {
        GameObject go = CreateTiledSpriteRect(wallsRoot, name, wallSprite,
            new Rect(center.x - length * 0.5f, center.y - thickness * 0.5f, length, thickness), "Wall", 0, wallLayer);
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(length, thickness);
        AddShadowCaster2D(go);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;
    }

    private static GameObject CreateSolidProp(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int wallLayer)
    {
        GameObject go = CreateSpriteRect(parent, name, sprite, rect, sortingLayer, sortOrder, wallLayer);
        var col = go.AddComponent<BoxCollider2D>();
        if (sprite != null) col.size = sprite.bounds.size;
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1; // Not Walkable
        return go;
    }

    private static void AddWallSegment(Transform parent, string name, Rect rect, int wallLayer, Sprite wallSprite, bool castShadows = true)
    {
        GameObject go = CreateTiledSpriteRect(parent, name, wallSprite, rect, "Wall", 0, wallLayer);
        if (!castShadows)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(rect.width, rect.height);
        if (castShadows)
        {
            AddShadowCaster2D(go);
        }
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1; // Not Walkable
    }

    private static void AddHorizontalWallWithDoor(
        Transform wallsRoot, Transform doorsRoot, string name, float xMin, float xMax, float wallY, float doorX,
        float wt, Sprite wallSpr, Sprite doorClosed, Sprite doorOpen, int wallLayer, int interactLayer,
        string doorName = null, bool isHidingSpot = false, float swingDeg = 90f)
    {
        float halfGap = isHidingSpot ? 0.15f : 0.13f;
        if (doorX - halfGap > xMin + 0.01f)
            AddWallSegment(wallsRoot, name + "_L", new Rect(xMin, wallY - wt * 0.5f, (doorX - halfGap) - xMin, wt), wallLayer, wallSpr);
        if (xMax > doorX + halfGap + 0.01f)
            AddWallSegment(wallsRoot, name + "_R", new Rect(doorX + halfGap, wallY - wt * 0.5f, xMax - (doorX + halfGap), wt), wallLayer, wallSpr);
        CreateNetworkDoor(doorsRoot, doorName ?? (name + "_Door"), new Vector2(doorX, wallY), verticalWall: false,
            doorClosed, doorOpen, false, false, isHidingSpot, wallLayer, interactLayer, swingDeg);
    }

    private static void AddVerticalWallWithDoor(
        Transform wallsRoot, Transform doorsRoot, string name, float wallX, float yMin, float yMax, float doorY,
        float wt, Sprite wallSpr, Sprite doorClosed, Sprite doorOpen, int wallLayer, int interactLayer,
        string doorName = null, bool isFrontDoor = false, bool requiresKey = false, bool isHidingSpot = false, float swingDeg = -90f)
    {
        float halfGap = isHidingSpot ? 0.15f : 0.13f;
        if (doorY - halfGap > yMin + 0.01f)
            AddWallSegment(wallsRoot, name + "_B", new Rect(wallX - wt * 0.5f, yMin, wt, (doorY - halfGap) - yMin), wallLayer, wallSpr);
        if (yMax > doorY + halfGap + 0.01f)
            AddWallSegment(wallsRoot, name + "_T", new Rect(wallX - wt * 0.5f, doorY + halfGap, wt, yMax - (doorY + halfGap)), wallLayer, wallSpr);
        CreateNetworkDoor(doorsRoot, doorName ?? (name + "_Door"), new Vector2(wallX, doorY), verticalWall: true,
            doorClosed, doorOpen, isFrontDoor, requiresKey, isHidingSpot, wallLayer, interactLayer, swingDeg);
    }

    private static void AddHorizontalSegmentWithDoors(
        Transform wallsRoot, Transform doorsRoot, string baseName, float xStart, float xEnd, float yWall,
        (string doorName, float xCenter, bool isHidingSpot, float swingDeg)[] doors, float wt, Sprite wallSpr,
        Sprite doorClosed, Sprite doorOpen, Sprite cabClosed, Sprite cabOpen, int wallLayer, int interactLayer)
    {
        float cursor = xStart;
        for (int i = 0; i < doors.Length; i++)
        {
            bool hide = doors[i].isHidingSpot;
            float halfGap = hide ? 0.14f : 0.13f;
            float dLeft = doors[i].xCenter - halfGap;
            if (dLeft > cursor + 0.01f)
            {
                AddWallSegment(wallsRoot, $"{baseName}_Seg{i}", new Rect(cursor, yWall - wt * 0.5f, dLeft - cursor, wt), wallLayer, wallSpr);
            }
            CreateNetworkDoor(doorsRoot, doors[i].doorName, new Vector2(doors[i].xCenter, yWall), verticalWall: false,
                hide ? cabClosed : doorClosed, hide ? cabOpen : doorOpen, false, false, hide, wallLayer, interactLayer, doors[i].swingDeg);
            cursor = doors[i].xCenter + halfGap;
        }
        if (xEnd > cursor + 0.01f)
        {
            AddWallSegment(wallsRoot, $"{baseName}_SegEnd", new Rect(cursor, yWall - wt * 0.5f, xEnd - cursor, wt), wallLayer, wallSpr);
        }
    }

    private static void CreateNetworkDoor(
        Transform parent, string name, Vector2 center, bool verticalWall, Sprite closedSpr, Sprite openSpr,
        bool isFrontDoor, bool requiresKey, bool isHidingSpot, int wallLayer, int interactLayer, float swingDeg = 90f)
    {
        GameObject doorRoot = new GameObject(name);
        doorRoot.transform.SetParent(parent, false);
        doorRoot.transform.position = new Vector3(center.x, center.y, 0f);
        doorRoot.layer = interactLayer;
        doorRoot.AddComponent<NetworkObject>();

        float length = isHidingSpot ? 0.30f : 0.25f;
        float thick = isHidingSpot ? 0.10f : 0.065f;
        float closedAngle = verticalWall ? 90f : 0f;

        GameObject cRef = new GameObject("ClosedRef");
        cRef.transform.SetParent(doorRoot.transform, false);
        cRef.transform.localPosition = Vector3.zero;
        cRef.layer = wallLayer;
        var cSr = cRef.AddComponent<SpriteRenderer>();
        cSr.sprite = closedSpr;
        cSr.sortingLayerName = "Wall";
        cSr.sortingOrder = 1;
        if (closedSpr != null)
        {
            cRef.transform.localScale = new Vector3(length / closedSpr.bounds.size.x, thick / closedSpr.bounds.size.y, 1f);
            var col = cRef.AddComponent<BoxCollider2D>();
            col.size = closedSpr.bounds.size;
        }
        AddShadowCaster2D(cRef);
        cRef.transform.localRotation = Quaternion.Euler(0f, 0f, closedAngle);
        var cRefNmm = cRef.AddComponent<NavMeshModifier>();
        if (isFrontDoor)
        {
            // Front door is a hard Not Walkable seam so ghosts can never path outside the house.
            cRefNmm.ignoreFromBuild = false;
            cRefNmm.overrideArea = true;
            cRefNmm.area = 1; // Not Walkable
        }
        else
        {
            cRefNmm.ignoreFromBuild = true;
        }

        GameObject oRef = new GameObject("OpenRef");
        oRef.transform.SetParent(doorRoot.transform, false);
        oRef.layer = wallLayer;
        var oSr = oRef.AddComponent<SpriteRenderer>();
        bool useSwapSprite = isHidingSpot && openSpr != null;
        oSr.sprite = useSwapSprite ? openSpr : closedSpr;
        oSr.sortingLayerName = "Wall";
        oSr.sortingOrder = 1;
        if (oSr.sprite != null)
        {
            oRef.transform.localScale = new Vector3(length / oSr.sprite.bounds.size.x, thick / oSr.sprite.bounds.size.y, 1f);
        }

        // True hinge-edge pivot calculation around the -0.5*length edge so ArcSwing traces a pure circle
        if (Mathf.Abs(swingDeg) >= 0.5f && !isHidingSpot)
        {
            Vector3 vClosed = Quaternion.Euler(0f, 0f, closedAngle) * new Vector3(length * 0.5f, 0f, 0f);
            Vector3 hingeLocal = -vClosed;
            Vector3 openLocalPos = hingeLocal + Quaternion.Euler(0f, 0f, swingDeg) * vClosed;
            oRef.transform.localPosition = openLocalPos;
            oRef.transform.localRotation = Quaternion.Euler(0f, 0f, closedAngle + swingDeg);
        }
        else
        {
            oRef.transform.localPosition = Vector3.zero;
            oRef.transform.localRotation = Quaternion.Euler(0f, 0f, closedAngle);
        }
        oRef.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        var nd = doorRoot.AddComponent<NetworkDoor>();
        SerializedObject so = new SerializedObject(nd);
        so.FindProperty("closedDoorRef").objectReferenceValue = cSr;
        so.FindProperty("openDoorRef").objectReferenceValue = oSr;
        so.FindProperty("isFrontDoor").boolValue = isFrontDoor;
        so.FindProperty("requiresKey").boolValue = requiresKey;
        so.FindProperty("isHidingSpot").boolValue = isHidingSpot;
        so.FindProperty("swapSpriteWhenOpen").boolValue = isHidingSpot;
        so.FindProperty("disableObstacleColliderWhenOpen").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddShadowCaster2D(GameObject go)
    {
        if (go == null) return;
        var sc = go.AddComponent<ShadowCaster2D>();
        sc.castsShadows = true;
        sc.selfShadows = false;
        SerializedObject sso = new SerializedObject(sc);
        AssignSortingLayersProperty(sso, excludeWall: true);
        sso.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignAllSortingLayers(Light2D light)
    {
        if (light == null) return;
        SerializedObject lso = new SerializedObject(light);
        AssignSortingLayersProperty(lso, excludeWall: false);
        lso.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignSortingLayersProperty(SerializedObject so, bool excludeWall)
    {
        var layersProp = so.FindProperty("m_ApplyToSortingLayers");
        if (layersProp == null) return;

        var layers = SortingLayer.layers;
        var targetIds = new List<int>(layers.Length);
        for (int i = 0; i < layers.Length; i++)
        {
            if (excludeWall && layers[i].name == "Wall") continue;
            targetIds.Add(layers[i].id);
        }

        layersProp.arraySize = targetIds.Count;
        for (int i = 0; i < targetIds.Count; i++)
        {
            layersProp.GetArrayElementAtIndex(i).intValue = targetIds[i];
        }
    }

    private static void CreateRoomFloor(Transform parent, string name, Rect rect, Sprite sprite, SurfaceType surface, int groundLayer)
    {
        GameObject go = CreateTiledSpriteRect(parent, $"Floor_{name}", sprite, rect, "Floor", -4, groundLayer);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 0; // Walkable

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(rect.width, rect.height);
        var fs = go.AddComponent<FloorSurface>();
        fs.surfaceType = surface;
        fs.priority = 2;
    }

    private static void CreateGhostRoomMarker(Transform ghostMarkers, string name, Rect rect)
    {
        GameObject marker = new GameObject(name);
        marker.transform.SetParent(ghostMarkers, false);
        marker.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);

        var sr = marker.AddComponent<SpriteRenderer>();
        sr.enabled = false;
        var col = marker.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(Mathf.Max(0.2f, rect.width - 0.1f), Mathf.Max(0.2f, rect.height - 0.1f));
        marker.AddComponent<GhostRoomMarker>();
        marker.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static void CreateRoomLightingAndSwitch(Transform lightSys, string roomName, Rect rect, int wallLayer, int interactLayer, Vector2? switchPos = null, Rect? extraBounds = null)
    {
        if (lightSys == null) return;

        GameObject swGo = new GameObject($"{roomName} LS");
        swGo.transform.SetParent(lightSys, false);
        Vector2 pos = switchPos ?? new Vector2(rect.xMin + 0.14f, rect.yMin + 0.14f);
        swGo.transform.position = new Vector3(pos.x, pos.y, 0f);
        swGo.layer = interactLayer;

        // Small subtle light-switch cap sprite (not an oversized white Square placeholder)
        Sprite capSpr = LoadSprite("House/wall_concrete_top");
        var swSr = swGo.AddComponent<SpriteRenderer>();
        swSr.sprite = capSpr;
        swSr.color = new Color(0.86f, 0.82f, 0.65f, 1f);
        swSr.sortingLayerName = "Item";
        swSr.sortingOrder = 1;
        if (capSpr != null)
        {
            swGo.transform.localScale = new Vector3(0.05f / capSpr.bounds.size.x, 0.05f / capSpr.bounds.size.y, 1f);
        }

        var trig = swGo.AddComponent<BoxCollider2D>();
        trig.isTrigger = true;

        swGo.AddComponent<NetworkObject>();
        var hls = swGo.AddComponent<HouseLightSwitch>();
        hls.EnsureSwitchSetup();
        swGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Room Light2D
        GameObject lightGo = new GameObject($"{roomName}_Light2D");
        lightGo.transform.SetParent(swGo.transform, false);
        lightGo.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        lightGo.transform.localScale = Vector3.one;
        var light = lightGo.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1f, 0.94f, 0.80f, 1f);
        light.intensity = 0.95f;
        light.pointLightOuterRadius = Mathf.Max(0.9f, Mathf.Max(rect.width, rect.height) * 0.85f);
        light.pointLightInnerRadius = 0.15f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 0.55f;
        light.enabled = false;

        AssignAllSortingLayers(light);

        // SafeZone trigger (starts inactive, NO visible SpriteRenderer!)
        GameObject szGo = new GameObject($"{roomName}_SafeZone");
        szGo.transform.SetParent(lightSys, false);
        ApplySafeZoneBounds(szGo, rect, extraBounds);
        szGo.AddComponent<SafeZone>().RefreshColliders();
        szGo.SetActive(false);

        SerializedObject swSo = new SerializedObject(hls);
        var clProp = swSo.FindProperty("controlledLights");
        clProp.arraySize = 1;
        clProp.GetArrayElementAtIndex(0).objectReferenceValue = light;
        var szProp = swSo.FindProperty("litSafeZones");
        szProp.arraySize = 1;
        szProp.GetArrayElementAtIndex(0).objectReferenceValue = szGo;
        swSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ApplySafeZoneBounds(GameObject szGo, Rect bounds, Rect? extraBounds)
    {
        if (szGo == null) return;

        szGo.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, 0f);
        szGo.transform.localRotation = Quaternion.identity;
        szGo.transform.localScale = Vector3.one;

        var existingColliders = szGo.GetComponents<Collider2D>();
        for (int i = 0; i < existingColliders.Length; i++)
        {
            if (existingColliders[i] != null && !(existingColliders[i] is BoxCollider2D))
            {
                Object.DestroyImmediate(existingColliders[i]);
            }
        }

        int desiredBoxCount = extraBounds.HasValue ? 2 : 1;
        var boxColliders = new List<BoxCollider2D>(szGo.GetComponents<BoxCollider2D>());
        while (boxColliders.Count > desiredBoxCount)
        {
            int last = boxColliders.Count - 1;
            Object.DestroyImmediate(boxColliders[last]);
            boxColliders.RemoveAt(last);
        }
        while (boxColliders.Count < desiredBoxCount)
        {
            boxColliders.Add(szGo.AddComponent<BoxCollider2D>());
        }

        BoxCollider2D primary = boxColliders[0];
        primary.enabled = true;
        primary.isTrigger = true;
        primary.offset = Vector2.zero;
        primary.size = bounds.size;

        if (extraBounds.HasValue)
        {
            BoxCollider2D extra = boxColliders[1];
            extra.enabled = true;
            extra.isTrigger = true;
            extra.offset = extraBounds.Value.center - bounds.center;
            extra.size = extraBounds.Value.size;
        }
    }

    [MenuItem("Pamahiin/Normalize BarangayMap SafeZones")]
    public static void NormalizeBarangayMapSafeZones()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != BARANGAY_SCENE_PATH)
        {
            if (!FileExists(BARANGAY_SCENE_PATH))
            {
                Debug.LogError($"[BarangayMapSceneBuilder] Scene not found at '{BARANGAY_SCENE_PATH}'.");
                return;
            }
            activeScene = EditorSceneManager.OpenScene(BARANGAY_SCENE_PATH, OpenSceneMode.Single);
        }

        GameObject envGo = null;
        foreach (var rootGo in activeScene.GetRootGameObjects())
        {
            if (rootGo.name == "Environment")
            {
                envGo = rootGo;
                break;
            }
        }
        if (envGo == null) envGo = GameObject.Find("Environment");
        if (envGo == null)
        {
            Debug.LogError("[BarangayMapSceneBuilder] Could not find 'Environment' root in BarangayMap.");
            return;
        }

        Transform lightSys = envGo.transform.Find("LightSystem");
        if (lightSys == null)
        {
            Debug.LogError("[BarangayMapSceneBuilder] Could not find 'Environment/LightSystem' in BarangayMap.");
            return;
        }

        // Preserve world transforms of switch/light children while resetting LightSystem's scaled transform to identity
        var children = new List<Transform>(lightSys.childCount);
        for (int i = 0; i < lightSys.childCount; i++)
        {
            children.Add(lightSys.GetChild(i));
        }
        for (int i = 0; i < children.Count; i++)
        {
            children[i].SetParent(null, true);
        }

        lightSys.localPosition = Vector3.zero;
        lightSys.localRotation = Quaternion.identity;
        lightSys.localScale = Vector3.one;

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] != null)
            {
                children[i].SetParent(lightSys, true);
            }
        }

        List<RoomSpec> rooms = GetHouseAndBasementRoomSpecs();
        var expectedSafeZoneNames = new HashSet<string>();
        foreach (var r in rooms)
        {
            if (r.HasSwitch)
            {
                expectedSafeZoneNames.Add($"{r.Name}_SafeZone");
            }
        }

        // Remove duplicate / stray SafeZone clones such as "Nursery_SafeZone (1)"
        var seenSafeZoneNames = new HashSet<string>();
        for (int i = lightSys.childCount - 1; i >= 0; i--)
        {
            Transform child = lightSys.GetChild(i);
            bool isStrayClone = child.name.Contains("_SafeZone (") ||
                                (expectedSafeZoneNames.Contains(child.name) && !seenSafeZoneNames.Add(child.name));
            if (isStrayClone)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        int normalizedCount = 0;
        foreach (var r in rooms)
        {
            if (!r.HasSwitch) continue;

            string szName = $"{r.Name}_SafeZone";
            Transform szTr = lightSys.Find(szName);
            GameObject szGo;
            if (szTr == null)
            {
                szGo = new GameObject(szName);
                szGo.transform.SetParent(lightSys, false);
            }
            else
            {
                szGo = szTr.gameObject;
            }

            ApplySafeZoneBounds(szGo, r.Bounds, r.ExtraBounds);

            var szComp = szGo.GetComponent<SafeZone>();
            if (szComp == null) szComp = szGo.AddComponent<SafeZone>();
            szComp.RefreshColliders();

            var sr = szGo.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;

            Transform swTr = lightSys.Find($"{r.Name} LS");
            if (swTr != null)
            {
                var hls = swTr.GetComponent<HouseLightSwitch>();
                if (hls != null)
                {
                    SerializedObject swSo = new SerializedObject(hls);
                    var szProp = swSo.FindProperty("litSafeZones");
                    if (szProp != null)
                    {
                        szProp.arraySize = 1;
                        szProp.GetArrayElementAtIndex(0).objectReferenceValue = szGo;
                        swSo.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(hls);
                    }
                    szGo.SetActive(hls.IsOn);
                }
            }

            EditorUtility.SetDirty(szGo);
            EditorUtility.SetDirty(szComp);
            normalizedCount++;
        }

        Transform jeepSzTr = FindChildRecursive(envGo.transform, "JeepneySafeZone");
        if (jeepSzTr != null)
        {
            jeepSzTr.localRotation = Quaternion.identity;
            jeepSzTr.localScale = Vector3.one;
            var jeepCols = jeepSzTr.GetComponents<Collider2D>();
            for (int i = 0; i < jeepCols.Length; i++)
            {
                if (jeepCols[i] != null) jeepCols[i].isTrigger = true;
            }
            var jeepSz = jeepSzTr.GetComponent<SafeZone>();
            if (jeepSz == null) jeepSz = jeepSzTr.gameObject.AddComponent<SafeZone>();
            jeepSz.RefreshColliders();
            var jeepSr = jeepSzTr.GetComponent<SpriteRenderer>();
            if (jeepSr != null) jeepSr.enabled = false;
            EditorUtility.SetDirty(jeepSzTr.gameObject);
            EditorUtility.SetDirty(jeepSz);
        }

        EditorUtility.SetDirty(lightSys.gameObject);
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene, BARANGAY_SCENE_PATH);
        Debug.Log($"[BarangayMapSceneBuilder] Normalized {normalizedCount} room SafeZones and LightSystem in {BARANGAY_SCENE_PATH}.");
    }

    private static Transform PrepareEmptyChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
        }
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(t.GetChild(i).gameObject);
        }
        return t;
    }

    private static Transform FindChildRecursive(Transform parent, string targetName)
    {
        if (parent.name == targetName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindChildRecursive(parent.GetChild(i), targetName);
            if (found != null) return found;
        }
        return null;
    }

    private static Sprite LoadSprite(string relNoExt)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>($"{SPRITE_PREFIX}{relNoExt}.png");
    }

    private static bool FileExists(string assetPath)
    {
        return System.IO.File.Exists(System.IO.Path.GetFullPath(assetPath));
    }
}
