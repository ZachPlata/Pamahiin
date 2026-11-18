using System.Collections.Generic;
using System.IO;
using NavMeshPlus.Components;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the 3-floor San Nicolas High School map (Assets/Scenes/SchoolMap.unity)
/// from SchoolMapLayout and registers it in EditorBuildSettings.
/// </summary>
public static class SchoolMapSceneBuilder
{
    public const string SCHOOL_SCENE_PATH = "Assets/Scenes/SchoolMap.unity";
    private const string TEMPLATE_BARANGAY_PATH = "Assets/Scenes/BarangayMap.unity";
    private const string TEMPLATE_GAMEMAP_PATH = "Assets/Scenes/GameMapScene.unity";

    private struct StairSpawnPair
    {
        public string CornerName;
        public int LowerFloor;
        public int UpperFloor;
        public Transform LowerArrival;
        public Transform UpperArrival;
    }

    [MenuItem("Pamahiin/Build SchoolMap Scene")]
    public static void BuildSchoolMapScene()
    {
        // 1. Generate all crisp 64-PPU Point-filtered school sprites + Journal blueprints
        SchoolAssetGenerator.GenerateAllAssets();
        SchoolBlueprintGenerator.GenerateAllBlueprints();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // 2. Clone template scene (prefer BarangayMap for latest UI/Jeepney/Ghost setup; fallback GameMapScene)
        string templatePath = File.Exists(TEMPLATE_BARANGAY_PATH) ? TEMPLATE_BARANGAY_PATH : TEMPLATE_GAMEMAP_PATH;
        if (!File.Exists(templatePath))
        {
            Debug.LogError("[SchoolMapSceneBuilder] No template scene found at BarangayMap or GameMapScene.");
            return;
        }

        if (File.Exists(SCHOOL_SCENE_PATH))
        {
            AssetDatabase.DeleteAsset(SCHOOL_SCENE_PATH);
        }
        AssetDatabase.CopyAsset(templatePath, SCHOOL_SCENE_PATH);
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.OpenScene(SCHOOL_SCENE_PATH, OpenSceneMode.Single);
        GameObject envRoot = GameObject.Find("Environment");
        if (envRoot == null)
        {
            Debug.LogError("[SchoolMapSceneBuilder] Could not find Environment root in cloned scene.");
            return;
        }

        // 3. Preserve Van & clean template map geometry
        Transform vanTr = FindChildRecursive(envRoot.transform, "Van");
        Vector3 preservedVanScale = vanTr != null ? vanTr.localScale : new Vector3(0.0883f, 0.0890f, 0.3559f);
        Quaternion preservedVanRot = vanTr != null ? vanTr.rotation : Quaternion.Euler(0f, 0f, 90.069f);
        if (vanTr != null) vanTr.SetParent(null, true);

        envRoot.transform.position = Vector3.zero;
        envRoot.transform.rotation = Quaternion.identity;
        envRoot.transform.localScale = Vector3.one;

        DestroyNamedChild(envRoot.transform, "UPDATED Tanglewood Drive");
        DestroyNamedChild(envRoot.transform, "BarangayTanglewoodMap");
        DestroyNamedChild(envRoot.transform, "Jeepney");
        DestroyNamedChild(envRoot.transform, "WeatherManager");
        foreach (var existingLink in Object.FindObjectsByType<NavMeshLink>(FindObjectsInactive.Include))
        {
            if (existingLink != null) Object.DestroyImmediate(existingLink.gameObject);
        }
        foreach (var existingStage in Object.FindObjectsByType<BurialMapStage>(FindObjectsInactive.Include))
        {
            if (existingStage != null) Object.DestroyImmediate(existingStage.gameObject);
        }

        int wallLayer = ResolveLayer("Walls", 0);
        int interactLayer = ResolveLayer("Interactable", 0);
        int groundLayer = ResolveLayer("Ground", 0);

        if (vanTr != null)
        {
            vanTr.SetParent(envRoot.transform, false);
            vanTr.position = new Vector3(0f, 5.55f, 0f);
            vanTr.rotation = preservedVanRot;
            vanTr.localScale = preservedVanScale;
        }

        Transform lightSys = PrepareEmptyChild(envRoot.transform, "LightSystem");
        Transform ghostMgr = envRoot.transform.Find("Ghost Manager");
        Transform ghostMarkers = ghostMgr != null ? PrepareEmptyChild(ghostMgr, "Ghost Marker") : null;
        Transform pathSys = ghostMgr != null ? PrepareEmptyChild(ghostMgr, "PathFindingSystem") : null;
        Transform soundSys = envRoot.transform.Find("SoundSystem");

        Light2D globalLight = EnsureGlobalLight();

        // 4. Create School hierarchy containers
        GameObject mapRoot = new GameObject("SanNicolasSchoolMap");
        mapRoot.transform.SetParent(envRoot.transform, false);

        Transform extRoot = new GameObject("ExteriorCampus").transform;
        extRoot.SetParent(mapRoot.transform, false);
        Transform wallsRoot = new GameObject("Walls").transform;
        wallsRoot.SetParent(mapRoot.transform, false);
        Transform doorsRoot = new GameObject("Doors").transform;
        doorsRoot.SetParent(mapRoot.transform, false);
        Transform furnRoot = new GameObject("Furniture").transform;
        furnRoot.SetParent(mapRoot.transform, false);
        Transform propsRoot = new GameObject("GhostProps").transform;
        propsRoot.SetParent(mapRoot.transform, false);
        Transform zonesRoot = new GameObject("FloorZones").transform;
        zonesRoot.SetParent(mapRoot.transform, false);

        Transform[] floorRoots = new Transform[4];
        for (int f = 1; f <= 3; f++)
        {
            floorRoots[f] = new GameObject($"Floor{f}").transform;
            floorRoots[f].SetParent(mapRoot.transform, false);
        }

        if (vanTr != null)
        {
            SetupJeepneyAtCourtyard(vanTr, envRoot.transform, doorsRoot, wallLayer, interactLayer, groundLayer);
        }

        // 5. Exterior North Plaza, Courtyard, South Lawn & Perimeter
        BuildExteriorCampus(extRoot, wallLayer, groundLayer);

        // 6. All 3 Floors: Room Floors, Light Switches, GhostMarkers, FloorZones, RoofFaders
        List<SchoolMapLayout.RoomDef> rooms = SchoolMapLayout.BuildAllRooms();
        BuildRoomsLightingAndZones(rooms, floorRoots, zonesRoot, ghostMarkers, lightSys, wallLayer, interactLayer, groundLayer);

        // 7. Walls & NetworkDoors across Floors 1-3
        List<SchoolMapLayout.DoorDef> doors = SchoolMapLayout.BuildAllDoors();
        BuildSchoolWallsAndDoors(doors, wallsRoot, doorsRoot, wallLayer, interactLayer);

        // 8. Furniture & Animated School Props
        BuildSchoolFurniture(rooms, furnRoot, wallLayer);

        // 9. 8 Bidirectional StairTeleporter Pairs + F2 Split-Landing Balustrades
        List<StairSpawnPair> stairLinks = BuildAllStairTeleporters(floorRoots, wallsRoot, wallLayer, interactLayer);

        // 10. FuseBox in F1_Workshop, HideZones, GhostProps
        BuildSchoolFuseBox(furnRoot, interactLayer);
        BuildSchoolHideZones(floorRoots);
        BuildSchoolGhostProps(propsRoot);

        // 11. Connected Multi-Floor GhostNode Graph
        BuildSchoolGhostNodes(pathSys);

        // 12. Ghost Handler random room configuration + WeatherManager + Off-Bounds Burial Stage
        ConfigureGhostHandler(ghostMgr);
        CreateSchoolWeatherManager(envRoot.transform, globalLight);
        CreateOffBoundsBurialStage();

        // 13. Bake 2D NavMesh & Wire 8 Bidirectional Stair NavMeshLinks
        Physics2D.SyncTransforms();
        BakeSchoolNavMeshAndLinks(envRoot.transform, stairLinks);

        // 14. Clean SoundSystem triggers & configure dark camera background
        CleanSoundSystemAndCamera(soundSys);
        StripDuplicateGameSettingsManager();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, SCHOOL_SCENE_PATH);
        RegisterAllBuildScenes();

        Debug.Log("[SchoolMapSceneBuilder] Built & baked Assets/Scenes/SchoolMap.unity successfully.");
    }

    // ----------------------------------------------------------------------
    // Jeepney & Exterior Campus
    // ----------------------------------------------------------------------

    private static void SetupJeepneyAtCourtyard(
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

        for (int i = vanTr.childCount - 1; i >= 0; i--)
        {
            Transform child = vanTr.GetChild(i);
            string n = child.name.ToLowerInvariant();
            if (n == "van inside" || n == "wall") child.gameObject.SetActive(false);
        }

        float cx = vanTr.position.x;
        float cy = vanTr.position.y;

        var playerSpawn = vanTr.GetComponentInChildren<PlayerSpawnPoint>(true);
        if (playerSpawn != null) playerSpawn.transform.position = new Vector3(cx + 0.015f, cy - 0.18f, 0f);

        var equipSpawn = vanTr.GetComponentInChildren<EquipmentSpawnPoint>(true);
        if (equipSpawn != null) equipSpawn.transform.position = new Vector3(cx - 0.15f, cy - 0.16f, 0f);

        var key = vanTr.GetComponentInChildren<FrontDoorKeyItem>(true);
        if (key != null)
        {
            key.transform.position = new Vector3(cx + 0.14f, cy + 0.21f, 0f);
            key.transform.localScale = Vector3.one * 0.14f;
        }

        var endBtn = vanTr.GetComponentInChildren<TruckEndGameButton>(true);
        if (endBtn != null) endBtn.transform.position = new Vector3(cx + 0.16f, cy - 0.46f, 0f);

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

        foreach (var legacySz in vanTr.GetComponentsInChildren<SafeZone>(true))
        {
            var legacyCol = legacySz.GetComponent<Collider2D>();
            if (legacyCol != null) legacyCol.enabled = false;
            legacySz.enabled = false;
            if (legacySz.gameObject != vanTr.gameObject) legacySz.gameObject.SetActive(false);
        }

        GameObject jeepneyRoot = new GameObject("Jeepney");
        jeepneyRoot.transform.SetParent(envRoot, false);

        Rect bodyRect = new Rect(cx - 0.28f, cy - 0.69f, 0.56f, 1.38f);
        CreateSpriteRect(jeepneyRoot.transform, "JeepneyBody",
            LoadBarangaySprite("Jeepney/jeepney_body_interior"), bodyRect, "Van", 0, 0)
            .AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject safeZoneGo = new GameObject("JeepneySafeZone");
        safeZoneGo.transform.SetParent(jeepneyRoot.transform, false);
        safeZoneGo.transform.position = new Vector3(cx, cy - 0.19f, 0f);
        var szCol = safeZoneGo.AddComponent<BoxCollider2D>();
        szCol.isTrigger = true;
        szCol.size = new Vector2(0.48f, 0.94f);
        safeZoneGo.AddComponent<SafeZone>();
        safeZoneGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        Rect tvRect = new Rect(cx - 0.105f, cy + 0.165f, 0.21f, 0.088f);
        CreateSpriteRect(jeepneyRoot.transform, "JeepneyDashboardTV",
            LoadBarangaySprite("Jeepney/jeepney_dashboard_tv"), tvRect, "Van", 2, 0)
            .AddComponent<NavMeshModifier>().ignoreFromBuild = true;

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
            if (dashBox == null) dashBox = dashGo.AddComponent<BoxCollider2D>();
            dashBox.size = Vector2.one;
            dashBox.offset = Vector2.zero;
            dashBox.isTrigger = true;
            dashBox.enabled = true;
        }

        Rect walkRect = new Rect(cx - 0.18f, cy - 0.84f, 0.36f, 1.12f);
        GameObject bayFloor = new GameObject("JeepneyBayWalkable");
        bayFloor.transform.SetParent(jeepneyRoot.transform, false);
        bayFloor.transform.position = new Vector3(walkRect.center.x, walkRect.center.y, 0f);
        bayFloor.layer = groundLayer;
        var bayNmm = bayFloor.AddComponent<NavMeshModifier>();
        bayNmm.overrideArea = true;
        bayNmm.area = 1;
        var bayCol = bayFloor.AddComponent<BoxCollider2D>();
        bayCol.isTrigger = true;
        bayCol.size = walkRect.size;
        var bayFs = bayFloor.AddComponent<FloorSurface>();
        bayFs.surfaceType = SurfaceType.Concrete;
        bayFs.priority = 3;

        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_West", new Rect(cx - 0.27f, cy - 0.68f, 0.06f, 1.36f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_East", new Rect(cx + 0.21f, cy - 0.68f, 0.06f, 1.36f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_CabBulkhead", new Rect(cx - 0.21f, cy + 0.253f, 0.42f, 0.43f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_RearL", new Rect(cx - 0.21f, cy - 0.68f, 0.09f, 0.05f), wallLayer);
        AddInvisibleShadowWall(jeepneyRoot.transform, "JeepneyWall_RearR", new Rect(cx + 0.12f, cy - 0.68f, 0.09f, 0.05f), wallLayer);

        CreateJeepneyHeadlight(jeepneyRoot.transform, "JeepneyHeadlight_L", new Vector3(cx - 0.17f, cy + 0.67f, 0f));
        CreateJeepneyHeadlight(jeepneyRoot.transform, "JeepneyHeadlight_R", new Vector3(cx + 0.17f, cy + 0.67f, 0f));

        GameObject roofGo = CreateSpriteRect(jeepneyRoot.transform, "JeepneyRoof",
            LoadBarangaySprite("Jeepney/jeepney_roof"), bodyRect, "Roof", 1, 0);
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

        CreateJeepneyRampDoor(doorsRoot, new Vector2(cx, cy - 0.665f), wallLayer, interactLayer);
    }

    private static void BuildExteriorCampus(Transform extRoot, int wallLayer, int groundLayer)
    {
        Sprite courtyardSpr = LoadSchoolSprite("Floors/tile_courtyard_paving");
        Sprite grassSpr = LoadSchoolSprite("Floors/tile_grass_lawn");
        Sprite capWallSpr = LoadSchoolSprite("Walls/wall_school_trim_cap");

        // North Courtyard Plaza (between wings) & North Staging Driveway
        CreateWalkableExterior(extRoot, "Ext_NorthCourtyard", courtyardSpr, new Rect(-3.50f, 2.25f, 7.00f, 2.00f), SurfaceType.Concrete, groundLayer);
        CreateWalkableExterior(extRoot, "Ext_NorthDriveway", courtyardSpr, new Rect(-4.60f, 4.25f, 9.20f, 2.75f), SurfaceType.Concrete, groundLayer);

        // North flanking lawns + South Courtyard + South Lawn + Perimeter grass strips
        CreateWalkableExterior(extRoot, "Ext_NorthWestLawn", grassSpr, new Rect(-7.25f, 4.25f, 2.65f, 2.75f), SurfaceType.Grass, groundLayer);
        CreateWalkableExterior(extRoot, "Ext_NorthEastLawn", grassSpr, new Rect(4.60f, 4.25f, 2.65f, 2.75f), SurfaceType.Grass, groundLayer);
        CreateWalkableExterior(extRoot, "Ext_WestSideLawn", grassSpr, new Rect(-7.25f, -4.25f, 0.75f, 8.50f), SurfaceType.Grass, groundLayer);
        CreateWalkableExterior(extRoot, "Ext_EastSideLawn", grassSpr, new Rect(6.50f, -4.25f, 0.75f, 8.50f), SurfaceType.Grass, groundLayer);
        CreateWalkableExterior(extRoot, "Ext_SouthCourtyard", grassSpr, new Rect(-3.50f, -4.25f, 7.00f, 2.00f), SurfaceType.Grass, groundLayer);
        CreateWalkableExterior(extRoot, "Ext_SouthLawn", grassSpr, new Rect(-7.25f, -6.25f, 14.50f, 2.00f), SurfaceType.Grass, groundLayer);

        // Campus Boundary Iron/Concrete Perimeter Fence
        float pThick = 0.15f;
        AddWallSegment(extRoot, "CampusFence_North", new Rect(-7.35f, 7.00f, 14.70f, pThick), wallLayer, capWallSpr);
        AddWallSegment(extRoot, "CampusFence_South", new Rect(-7.35f, -6.40f, 14.70f, pThick), wallLayer, capWallSpr);
        AddWallSegment(extRoot, "CampusFence_West", new Rect(-7.40f, -6.40f, pThick, 13.55f), wallLayer, capWallSpr);
        AddWallSegment(extRoot, "CampusFence_East", new Rect(7.25f, -6.40f, pThick, 13.55f), wallLayer, capWallSpr);

        // Streetlamp / Campus Courtyard Lamps (named LampPostLight_* for WeatherManager fireflies!)
        Vector2[] lamps =
        {
            new Vector2(-2.20f, 4.05f),
            new Vector2( 2.20f, 4.05f),
            new Vector2(-1.80f, 5.95f),
            new Vector2( 1.80f, 5.95f),
            new Vector2(-2.10f, -3.55f),
            new Vector2( 2.10f, -3.55f)
        };
        for (int i = 0; i < lamps.Length; i++)
        {
            GameObject lampGo = new GameObject($"LampPostLight_{i}");
            lampGo.transform.SetParent(extRoot, false);
            lampGo.transform.position = new Vector3(lamps[i].x, lamps[i].y, 0f);
            var l2d = lampGo.AddComponent<Light2D>();
            l2d.lightType = Light2D.LightType.Point;
            l2d.color = new Color(1.0f, 0.84f, 0.56f, 1f);
            l2d.intensity = 0.52f;
            l2d.pointLightInnerRadius = 0.08f;
            l2d.pointLightOuterRadius = 1.65f;
            l2d.shadowsEnabled = true;
            l2d.shadowIntensity = 0.65f;
            AssignAllSortingLayers(l2d);
        }

        // Swaying Tree Canopies & Withered Skeletal Trees
        Sprite treeStaticSpr = LoadSchoolSprite("Furniture/tree_canopy_sheet");
        Vector2[] canopyPositions =
        {
            new Vector2(-5.75f, 5.55f),
            new Vector2( 5.75f, 5.55f),
            new Vector2(-2.55f, 3.15f),
            new Vector2( 2.55f, 3.15f),
            new Vector2(-5.65f, -5.20f),
            new Vector2( 5.65f, -5.20f)
        };
        for (int i = 0; i < canopyPositions.Length; i++)
        {
            Vector2 tp = canopyPositions[i];
            GameObject tGo = CreateSpriteRect(extRoot, $"CourtyardTree_{i}", treeStaticSpr,
                new Rect(tp.x - 0.45f, tp.y - 0.45f, 0.90f, 0.90f), "Roof", 2, wallLayer);
            var anim = tGo.AddComponent<SchoolPropAnimator>();
            anim.Configure("SchoolMap/Furniture/tree_canopy_sheet", 2, 1.5f, false, "", 64f);
            var col = tGo.AddComponent<CircleCollider2D>();
            col.radius = 0.20f;
            var nmm = tGo.AddComponent<NavMeshModifier>();
            nmm.overrideArea = true;
            nmm.area = 1;
        }

        Sprite witheredSpr = LoadSchoolSprite("Furniture/tree_withered");
        Vector2[] witheredPos = { new Vector2(-1.40f, -3.45f), new Vector2(1.40f, -3.45f) };
        for (int i = 0; i < witheredPos.Length; i++)
        {
            Vector2 wp = witheredPos[i];
            GameObject wGo = CreateSpriteRect(extRoot, $"WitheredTree_{i}", witheredSpr,
                new Rect(wp.x - 0.35f, wp.y - 0.35f, 0.70f, 0.70f), "Item", 1, wallLayer);
            wGo.AddComponent<CircleCollider2D>().radius = 0.14f;
            var nmm = wGo.AddComponent<NavMeshModifier>();
            nmm.overrideArea = true;
            nmm.area = 1;
        }
    }

    // ----------------------------------------------------------------------
    // Floors, FloorZones, RoofFaders, Lighting & Ghost Markers
    // ----------------------------------------------------------------------

    private static void BuildRoomsLightingAndZones(
        List<SchoolMapLayout.RoomDef> rooms,
        Transform[] floorRoots,
        Transform zonesRoot,
        Transform ghostMarkers,
        Transform lightSys,
        int wallLayer,
        int interactLayer,
        int groundLayer)
    {
        // Build FloorZones & RoofFaders for Floors 1..3
        Sprite roofTileSpr = LoadBarangaySprite("House/roof_main_gi");
        if (roofTileSpr == null) roofTileSpr = LoadSchoolSprite("Walls/wall_school_trim_cap");

        for (int f = 1; f <= 3; f++)
        {
            Rect[] wingRects = SchoolMapLayout.GetFloorWingBounds(f);

            // FloorZone (exact 3 H-wing triggers)
            GameObject fzGo = new GameObject($"FloorZone_F{f}");
            fzGo.transform.SetParent(zonesRoot, false);
            for (int w = 0; w < wingRects.Length; w++)
            {
                var col = fzGo.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.offset = wingRects[w].center;
                col.size = wingRects[w].size;
            }
            var fz = fzGo.AddComponent<FloorZone>();
            fz.Configure(f, true, $"Floor {f}");
            fzGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            // Enclosed RoofFader (H-wing panels, fades per floor and masks outdoor rain/fog)
            GameObject roofGo = new GameObject($"Floor{f}_RoofFader");
            roofGo.transform.SetParent(floorRoots[f], false);
            roofGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var panelRenderers = new List<SpriteRenderer>(wingRects.Length);
            for (int w = 0; w < wingRects.Length; w++)
            {
                GameObject panel = CreateTiledSpriteRect(roofGo.transform, $"RoofPanel_F{f}_{w}", roofTileSpr, wingRects[w], "Roof", 0, 0);
                var pr = panel.GetComponent<SpriteRenderer>();
                pr.color = new Color(0.18f, 0.20f, 0.26f, 1f);
                panel.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
                panelRenderers.Add(pr);

                var col = roofGo.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.offset = wingRects[w].center;
                col.size = wingRects[w].size;
            }

            var rf = roofGo.AddComponent<RoofFader>();
            SerializedObject rfSo = new SerializedObject(rf);
            var arr = rfSo.FindProperty("spriteRenderers");
            arr.arraySize = panelRenderers.Count;
            for (int i = 0; i < panelRenderers.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = panelRenderers[i];
            var enclosedProp = rfSo.FindProperty("enclosedBuilding");
            if (enclosedProp != null) enclosedProp.boolValue = true;
            rfSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // Build each room's floor tile, optional GhostRoomMarker, and HouseLightSwitch/Light2D/SafeZone
        foreach (var room in rooms)
        {
            Rect r = room.WorldRect;
            Sprite floorSpr = GetFloorSpriteForStyle(room.FloorStyle);
            CreateRoomFloor(floorRoots[room.Floor], room.Id, floorSpr, r, room.SurfaceType, groundLayer);

            if (room.HasGhostMarker && ghostMarkers != null)
            {
                CreateGhostRoomMarker(ghostMarkers, room.Id, r);
            }

            if (room.HasLightSwitch && lightSys != null)
            {
                Vector2? swWorld = room.SwitchLocalPos.HasValue
                    ? room.SwitchLocalPos.Value + new Vector2(0f, SchoolMapLayout.GetFloorBaseY(room.Floor))
                    : (Vector2?)null;
                CreateRoomLightingAndSwitch(lightSys, room.Id, r, wallLayer, interactLayer, swWorld);
            }
        }
    }

    private static Sprite GetFloorSpriteForStyle(SchoolMapLayout.FloorStyle style)
    {
        return style switch
        {
            SchoolMapLayout.FloorStyle.HallCheckerF1 => LoadSchoolSprite("Floors/tile_hall_f1_checker"),
            SchoolMapLayout.FloorStyle.HallConcrete => LoadSchoolSprite("Floors/tile_hall_f2_f3_concrete"),
            SchoolMapLayout.FloorStyle.ClassroomGrid => LoadSchoolSprite("Floors/tile_classroom_grid"),
            SchoolMapLayout.FloorStyle.RestroomMosaic => LoadSchoolSprite("Floors/tile_restroom_mosaic"),
            SchoolMapLayout.FloorStyle.LibraryWood => LoadSchoolSprite("Floors/tile_library_wood"),
            SchoolMapLayout.FloorStyle.WorkshopConcrete => LoadSchoolSprite("Floors/tile_workshop_concrete"),
            SchoolMapLayout.FloorStyle.ComLabRaised => LoadSchoolSprite("Floors/tile_comlab_raised"),
            SchoolMapLayout.FloorStyle.CourtyardPaving => LoadSchoolSprite("Floors/tile_courtyard_paving"),
            SchoolMapLayout.FloorStyle.GrassLawn => LoadSchoolSprite("Floors/tile_grass_lawn"),
            _ => LoadSchoolSprite("Floors/tile_hall_f2_f3_concrete")
        };
    }

    // ----------------------------------------------------------------------
    // Walls & Doors across all 3 Floors
    // ----------------------------------------------------------------------

    private static void BuildSchoolWallsAndDoors(
        List<SchoolMapLayout.DoorDef> doors,
        Transform wallsRoot,
        Transform doorsRoot,
        int wallLayer,
        int interactLayer)
    {
        Sprite extWallSpr = LoadSchoolSprite("Walls/wall_school_trim_cap");
        Sprite intWallSpr = LoadSchoolSprite("Walls/wall_school_plaster");
        Sprite woodDoorSpr = LoadSchoolSprite("Walls/door_school_wood");
        Sprite metalDoorSpr = LoadSchoolSprite("Walls/door_metal_exit");
        Sprite stallDoorSpr = LoadSchoolSprite("Walls/door_stall");
        Sprite lockerDoorSpr = LoadSchoolSprite("Walls/locker_door");

        float wtExt = SchoolMapLayout.EXT_WALL_THICK;
        float wtInt = SchoolMapLayout.INT_WALL_THICK;

        // Index doors by name for clean wall carving
        var doorMap = new Dictionary<string, SchoolMapLayout.DoorDef>();
        foreach (var d in doors) doorMap[d.Name] = d;

        for (int f = 1; f <= 3; f++)
        {
            float baseY = SchoolMapLayout.GetFloorBaseY(f);
            Transform fwRoot = new GameObject($"Walls_F{f}").transform;
            fwRoot.SetParent(wallsRoot, false);

            // 1. Exterior H-Building Perimeter Shell
            // West Outer Wall
            AddWallSegment(fwRoot, $"F{f}_Ext_West", new Rect(-6.50f - wtExt * 0.5f, baseY - 4.25f, wtExt, 8.50f), wallLayer, extWallSpr);
            // East Outer Wall
            AddWallSegment(fwRoot, $"F{f}_Ext_East", new Rect(6.50f - wtExt * 0.5f, baseY - 4.25f, wtExt, 8.50f), wallLayer, extWallSpr);
            // South Wing Tips & South Bridge Exterior
            AddWallSegment(fwRoot, $"F{f}_Ext_SouthW", new Rect(-6.50f, baseY - 4.25f - wtExt * 0.5f, 3.00f, wtExt), wallLayer, extWallSpr);
            AddWallSegment(fwRoot, $"F{f}_Ext_SouthE", new Rect(3.50f, baseY - 4.25f - wtExt * 0.5f, 3.00f, wtExt), wallLayer, extWallSpr);
            AddWallSegment(fwRoot, $"F{f}_Ext_SouthBridge", new Rect(-3.50f, baseY - 2.25f - wtExt * 0.5f, 7.00f, wtExt), wallLayer, extWallSpr);
            AddWallSegment(fwRoot, $"F{f}_Ext_NorthBridge", new Rect(-3.50f, baseY + 2.25f - wtExt * 0.5f, 7.00f, wtExt), wallLayer, extWallSpr);
            // Inner wing courtyard flanks South of bridge
            AddWallSegment(fwRoot, $"F{f}_Ext_CourtSW", new Rect(-3.50f - wtExt * 0.5f, baseY - 4.25f, wtExt, 2.00f), wallLayer, extWallSpr);
            AddWallSegment(fwRoot, $"F{f}_Ext_CourtSE", new Rect(3.50f - wtExt * 0.5f, baseY - 4.25f, wtExt, 2.00f), wallLayer, extWallSpr);

            if (f == 1)
            {
                // North Wing Outer Walls WITH Entry_NW_Outer & Entry_NE_Outer
                AddHorizontalWallWithDoor(fwRoot, doorsRoot, "F1_Ext_NorthW", -6.50f, -3.50f, baseY + 4.25f, -4.00f,
                    wtExt, extWallSpr, metalDoorSpr, metalDoorSpr, wallLayer, interactLayer, "Entry_NW_Outer",
                    isFrontDoor: true, requiresKey: true, swingDeg: -90f);
                AddHorizontalWallWithDoor(fwRoot, doorsRoot, "F1_Ext_NorthE", 3.50f, 6.50f, baseY + 4.25f, 4.00f,
                    wtExt, extWallSpr, metalDoorSpr, metalDoorSpr, wallLayer, interactLayer, "Entry_NE_Outer",
                    isFrontDoor: true, requiresKey: true, swingDeg: -90f);
                // Inner wing courtyard flanks North of bridge WITH Entry_NW_Courtyard & Entry_NE_Courtyard
                AddVerticalWallWithDoor(fwRoot, doorsRoot, "F1_Ext_CourtNW", -3.50f, baseY + 2.25f, baseY + 4.25f, baseY + 3.45f,
                    wtExt, extWallSpr, metalDoorSpr, metalDoorSpr, wallLayer, interactLayer, "Entry_NW_Courtyard",
                    isFrontDoor: true, requiresKey: true, swingDeg: 90f);
                AddVerticalWallWithDoor(fwRoot, doorsRoot, "F1_Ext_CourtNE", 3.50f, baseY + 2.25f, baseY + 4.25f, baseY + 3.45f,
                    wtExt, extWallSpr, metalDoorSpr, metalDoorSpr, wallLayer, interactLayer, "Entry_NE_Courtyard",
                    isFrontDoor: true, requiresKey: true, swingDeg: -90f);
            }
            else
            {
                AddWallSegment(fwRoot, $"F{f}_Ext_NorthW", new Rect(-6.50f, baseY + 4.25f - wtExt * 0.5f, 3.00f, wtExt), wallLayer, extWallSpr);
                AddWallSegment(fwRoot, $"F{f}_Ext_NorthE", new Rect(3.50f, baseY + 4.25f - wtExt * 0.5f, 3.00f, wtExt), wallLayer, extWallSpr);
                AddWallSegment(fwRoot, $"F{f}_Ext_CourtNW", new Rect(-3.50f - wtExt * 0.5f, baseY + 2.25f, wtExt, 2.00f), wallLayer, extWallSpr);
                AddWallSegment(fwRoot, $"F{f}_Ext_CourtNE", new Rect(3.50f - wtExt * 0.5f, baseY + 2.25f, wtExt, 2.00f), wallLayer, extWallSpr);
            }

            // 2. Corner Restrooms & Stairwell Partition Walls (consistent across F1-F3!)
            // NW Restroom & NW Stairwell horizontal dividers
            AddWallSegment(fwRoot, $"F{f}_W_RestroomNW_Bot", new Rect(-6.50f, baseY + 2.85f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);
            AddWallSegment(fwRoot, $"F{f}_W_StairsNW_Bot", new Rect(-6.50f, baseY + 1.65f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);
            AddWallSegment(fwRoot, $"F{f}_W_StairsSW_Top", new Rect(-6.50f, baseY - 1.65f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);
            AddWallSegment(fwRoot, $"F{f}_W_RestroomSW_Top", new Rect(-6.50f, baseY - 2.85f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);

            AddWallSegment(fwRoot, $"F{f}_E_RestroomNE_Bot", new Rect(4.50f, baseY + 2.85f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);
            AddWallSegment(fwRoot, $"F{f}_E_StairsNE_Bot", new Rect(4.50f, baseY + 1.65f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);
            AddWallSegment(fwRoot, $"F{f}_E_StairsSE_Top", new Rect(4.50f, baseY - 1.65f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);
            AddWallSegment(fwRoot, $"F{f}_E_RestroomSE_Top", new Rect(4.50f, baseY - 2.85f - wtInt * 0.5f, 2.00f, wtInt), wallLayer, intWallSpr);

            // West & East Restroom Doors + Open Stairwell Archways (leaving y ∈ [cy-0.38..cy+0.38] unobstructed!)
            AddVerticalWallWithDoor(fwRoot, doorsRoot, $"W_RestroomNW_F{f}", -4.50f, baseY + 2.85f, baseY + 4.25f, baseY + 3.55f,
                wtInt, intWallSpr, woodDoorSpr, woodDoorSpr, wallLayer, interactLayer, $"Door_F{f}_Restroom_NW", swingDeg: 90f);
            AddArchwayWall(fwRoot, $"F{f}_Arch_NW", -4.50f, baseY + 1.65f, baseY + 2.85f, baseY + 2.25f, 0.38f, wtInt, wallLayer, intWallSpr);
            AddArchwayWall(fwRoot, $"F{f}_Arch_SW", -4.50f, baseY - 2.85f, baseY - 1.65f, baseY - 2.25f, 0.38f, wtInt, wallLayer, intWallSpr);
            AddVerticalWallWithDoor(fwRoot, doorsRoot, $"W_RestroomSW_F{f}", -4.50f, baseY - 4.25f, baseY - 2.85f, baseY - 3.55f,
                wtInt, intWallSpr, woodDoorSpr, woodDoorSpr, wallLayer, interactLayer, $"Door_F{f}_Restroom_SW", swingDeg: -90f);

            AddVerticalWallWithDoor(fwRoot, doorsRoot, $"E_RestroomNE_F{f}", 4.50f, baseY + 2.85f, baseY + 4.25f, baseY + 3.55f,
                wtInt, intWallSpr, woodDoorSpr, woodDoorSpr, wallLayer, interactLayer, $"Door_F{f}_Restroom_NE", swingDeg: -90f);
            AddArchwayWall(fwRoot, $"F{f}_Arch_NE", 4.50f, baseY + 1.65f, baseY + 2.85f, baseY + 2.25f, 0.38f, wtInt, wallLayer, intWallSpr);
            AddArchwayWall(fwRoot, $"F{f}_Arch_SE", 4.50f, baseY - 2.85f, baseY - 1.65f, baseY - 2.25f, 0.38f, wtInt, wallLayer, intWallSpr);
            AddVerticalWallWithDoor(fwRoot, doorsRoot, $"E_RestroomSE_F{f}", 4.50f, baseY - 4.25f, baseY - 2.85f, baseY - 3.55f,
                wtInt, intWallSpr, woodDoorSpr, woodDoorSpr, wallLayer, interactLayer, $"Door_F{f}_Restroom_SE", swingDeg: 90f);

            // Toilet Stall Hide Doors on every Restroom
            AddRestroomStallDoors(doorsRoot, wallLayer, interactLayer, stallDoorSpr, f, baseY);

            // 3. Floor-Specific Middle Wings & Central Bridge Partitions
            if (f == 1) BuildFloor1InteriorWallsAndDoors(fwRoot, doorsRoot, baseY, wtInt, intWallSpr, woodDoorSpr, lockerDoorSpr, wallLayer, interactLayer);
            else if (f == 2) BuildFloor2InteriorWallsAndDoors(fwRoot, doorsRoot, baseY, wtInt, intWallSpr, woodDoorSpr, wallLayer, interactLayer);
            else BuildFloor3InteriorWallsAndDoors(fwRoot, doorsRoot, baseY, wtInt, intWallSpr, woodDoorSpr, wallLayer, interactLayer);
        }
    }

    private static void AddRestroomStallDoors(
        Transform doorsRoot, int wallLayer, int interactLayer, Sprite stallSpr, int floor, float baseY)
    {
        (string corner, float x1, float x2, float yLocal)[] specs =
        {
            ("NW", -5.95f, -5.45f,  3.80f),
            ("SW", -5.95f, -5.45f, -3.80f),
            ("NE",  5.45f,  5.95f,  3.80f),
            ("SE",  5.45f,  5.95f, -3.80f)
        };
        foreach (var s in specs)
        {
            CreateNetworkDoor(doorsRoot, $"Stall_F{floor}_{s.corner}_1", new Vector2(s.x1, baseY + s.yLocal), false,
                stallSpr, stallSpr, false, false, true, wallLayer, interactLayer, 90f);
            CreateNetworkDoor(doorsRoot, $"Stall_F{floor}_{s.corner}_2", new Vector2(s.x2, baseY + s.yLocal), false,
                stallSpr, stallSpr, false, false, true, wallLayer, interactLayer, 90f);
        }
    }

    private static void BuildFloor1InteriorWallsAndDoors(
        Transform fw, Transform dr, float baseY, float wt, Sprite wallSpr, Sprite doorSpr, Sprite lockerSpr, int wallLayer, int interactLayer)
    {
        // West Classrooms W1 & W2
        AddWallSegment(fw, "F1_W_DivMid", new Rect(-6.50f, baseY - wt * 0.5f, 2.00f, wt), wallLayer, wallSpr);
        AddVerticalWallWithDoor(fw, dr, "F1_W1", -4.50f, baseY, baseY + 1.65f, baseY + 0.85f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F1_Classroom_W1", swingDeg: 90f);
        AddVerticalWallWithDoor(fw, dr, "F1_W2", -4.50f, baseY - 1.65f, baseY, baseY - 0.85f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F1_Classroom_W2", swingDeg: -90f);

        // Central Spine Walls (with open corridor mouths at x = -3.50 & +3.50 for y ∈ [-0.45..+0.45]!)
        AddWallSegment(fw, "F1_Bridge_SepW_N", new Rect(-3.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F1_Bridge_SepE_N", new Rect( 3.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F1_Bridge_SepW_S", new Rect(-3.50f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F1_Bridge_SepE_S", new Rect( 3.50f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);

        // North Row partitions (N1..N5 + Storage_NE) & doors along SPINE_Y_MAX
        float[] nDivs = { -2.25f, -1.00f, 0.25f, 1.50f, 2.75f };
        for (int i = 0; i < nDivs.Length; i++)
        {
            AddWallSegment(fw, $"F1_NDiv_{i}", new Rect(nDivs[i] - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        }
        var northDoors = new List<(string, float, bool, float)>();
        for (int i = 0; i < 5; i++)
        {
            northDoors.Add(($"Door_F1_Classroom_N{i + 1}", -3.50f + i * 1.25f + 0.625f, false, 90f));
        }
        northDoors.Add(("Door_F1_Storage_NE", 3.125f, false, 90f));
        AddHorizontalSegmentWithDoors(fw, dr, "F1_SpineNorth", -3.50f, 3.50f, baseY + 0.45f, northDoors.ToArray(),
            wt, wallSpr, doorSpr, doorSpr, doorSpr, doorSpr, wallLayer, interactLayer);

        // South Row partitions (Workshop | Classroom_S1 | Deans Inner | Reception | Annex | Classroom_S2)
        float[] sDivsFull = { -1.50f, -0.25f, 2.25f };
        for (int i = 0; i < sDivsFull.Length; i++)
        {
            AddWallSegment(fw, $"F1_SDivFull_{i}", new Rect(sDivsFull[i] - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);
        }
        AddVerticalWallWithDoor(fw, dr, "F1_DeanInner", 0.50f, baseY - 2.25f, baseY - 0.45f, baseY - 1.25f,
            wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F1_DeansInnerLink", swingDeg: 90f);
        AddVerticalWallWithDoor(fw, dr, "F1_DeanAnnex", 1.50f, baseY - 2.25f, baseY - 0.45f, baseY - 1.25f,
            wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F1_DeansAnnexLink", swingDeg: -90f);

        var southDoors = new[]
        {
            ("Door_F1_Workshop", -2.50f, false, -90f),
            ("Door_F1_Classroom_S1", -0.875f, false, -90f),
            ("Door_F1_DeansReception", 1.00f, false, -90f),
            ("Door_F1_Classroom_S2", 2.875f, false, -90f)
        };
        AddHorizontalSegmentWithDoors(fw, dr, "F1_SpineSouth", -3.50f, 3.50f, baseY - 0.45f, southDoors,
            wt, wallSpr, doorSpr, doorSpr, doorSpr, doorSpr, wallLayer, interactLayer);

        // East Wing: Classroom_E1 (dual doors) + F1_LockerAlcove recess
        AddVerticalWallWithDoor(fw, dr, "F1_E1_Top", 4.50f, baseY, baseY + 1.65f, baseY + 0.90f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F1_Classroom_E1_Top", swingDeg: -90f);
        AddVerticalWallWithDoor(fw, dr, "F1_E1_Bot", 4.50f, baseY - 1.65f, baseY, baseY - 0.90f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F1_Classroom_E1_Bot", swingDeg: -90f);

        AddWallSegment(fw, "F1_LockerAlcove_Top", new Rect(3.50f, baseY + 2.90f - wt * 0.5f, 0.48f, wt), wallLayer, wallSpr);
        AddWallSegment(fw, "F1_LockerAlcove_Mid", new Rect(3.50f, baseY + 2.54f - wt * 0.5f, 0.44f, wt), wallLayer, wallSpr);
        AddWallSegment(fw, "F1_LockerAlcove_Bot", new Rect(3.50f, baseY + 2.25f - wt * 0.5f, 0.48f, wt), wallLayer, wallSpr);
        CreateNetworkDoor(dr, "Locker_F1_HideA", new Vector2(3.94f, baseY + 2.72f), true, lockerSpr, lockerSpr, false, false, true, wallLayer, interactLayer, 0f);
        CreateNetworkDoor(dr, "Locker_F1_HideB", new Vector2(3.94f, baseY + 2.39f), true, lockerSpr, lockerSpr, false, false, true, wallLayer, interactLayer, 0f);
    }

    private static void BuildFloor2InteriorWallsAndDoors(
        Transform fw, Transform dr, float baseY, float wt, Sprite wallSpr, Sprite doorSpr, int wallLayer, int interactLayer)
    {
        // West Wing: Single Long Classroom W with 2 corridor doors
        AddVerticalWallWithDoor(fw, dr, "F2_W_Top", -4.50f, baseY, baseY + 1.65f, baseY + 0.90f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F2_Classroom_W_Top", swingDeg: 90f);
        AddVerticalWallWithDoor(fw, dr, "F2_W_Bot", -4.50f, baseY - 1.65f, baseY, baseY - 0.90f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F2_Classroom_W_Bot", swingDeg: 90f);

        // Central Spine Side Seams
        AddWallSegment(fw, "F2_Bridge_SepW_N", new Rect(-3.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F2_Bridge_SepE_N", new Rect( 3.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F2_Bridge_SepW_S", new Rect(-3.50f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F2_Bridge_SepE_S", new Rect( 3.50f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);

        // North Row: 4 classrooms
        float[] nDivs = { -1.75f, 0.00f, 1.75f };
        for (int i = 0; i < nDivs.Length; i++)
        {
            AddWallSegment(fw, $"F2_NDiv_{i}", new Rect(nDivs[i] - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        }
        var northDoors = new (string, float, bool, float)[4];
        for (int i = 0; i < 4; i++) northDoors[i] = ($"Door_F2_Classroom_N{i + 1}", -3.50f + i * 1.75f + 0.875f, false, 90f);
        AddHorizontalSegmentWithDoors(fw, dr, "F2_SpineNorth", -3.50f, 3.50f, baseY + 0.45f, northDoors,
            wt, wallSpr, doorSpr, doorSpr, doorSpr, doorSpr, wallLayer, interactLayer);

        // South Row: Theater Backstage | Multipurpose Hall | Library Discussion | Library
        AddVerticalWallWithDoor(fw, dr, "F2_BackstageLink", -2.75f, baseY - 2.25f, baseY - 0.45f, baseY - 1.35f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F2_Backstage", swingDeg: 90f);
        AddWallSegment(fw, "F2_SDiv_TheaterDisc", new Rect(-0.25f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F2_SDiv_DiscLib", new Rect(0.75f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);

        var southDoors = new[]
        {
            ("Door_F2_Multipurpose_W", -1.85f, false, -90f),
            ("Door_F2_Multipurpose_E", -0.75f, false, -90f),
            ("Door_F2_LibraryDiscussion", 0.25f, false, -90f),
            ("Door_F2_Library_W", 1.45f, false, -90f),
            ("Door_F2_Library_E", 2.85f, false, -90f)
        };
        AddHorizontalSegmentWithDoors(fw, dr, "F2_SpineSouth", -3.50f, 3.50f, baseY - 0.45f, southDoors,
            wt, wallSpr, doorSpr, doorSpr, doorSpr, doorSpr, wallLayer, interactLayer);

        // East Wing: Classroom E1 & E2
        AddWallSegment(fw, "F2_E_DivMid", new Rect(4.50f, baseY - wt * 0.5f, 2.00f, wt), wallLayer, wallSpr);
        AddVerticalWallWithDoor(fw, dr, "F2_E1", 4.50f, baseY, baseY + 1.65f, baseY + 0.85f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F2_Classroom_E1", swingDeg: -90f);
        AddVerticalWallWithDoor(fw, dr, "F2_E2", 4.50f, baseY - 1.65f, baseY, baseY - 0.85f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F2_Classroom_E2", swingDeg: -90f);
    }

    private static void BuildFloor3InteriorWallsAndDoors(
        Transform fw, Transform dr, float baseY, float wt, Sprite wallSpr, Sprite doorSpr, int wallLayer, int interactLayer)
    {
        // West Wing: Janitor Closet NW + Classroom W
        AddWallSegment(fw, "F3_W_ClosetDiv", new Rect(-6.50f, baseY + 0.80f - wt * 0.5f, 2.00f, wt), wallLayer, wallSpr);
        AddVerticalWallWithDoor(fw, dr, "F3_W_Closet", -4.50f, baseY + 0.80f, baseY + 1.65f, baseY + 1.22f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F3_JanitorCloset_NW", swingDeg: 90f);
        AddVerticalWallWithDoor(fw, dr, "F3_W_Class", -4.50f, baseY - 1.65f, baseY + 0.80f, baseY - 0.50f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F3_Classroom_W", swingDeg: 90f);

        // Central Spine Side Seams
        AddWallSegment(fw, "F3_Bridge_SepW_N", new Rect(-3.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F3_Bridge_SepE_N", new Rect( 3.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F3_Bridge_SepW_S", new Rect(-3.50f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F3_Bridge_SepE_S", new Rect( 3.50f - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);

        // North Row: ComLab 1, 2, 3
        AddWallSegment(fw, "F3_NDiv_Com12", new Rect(-1.50f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        AddWallSegment(fw, "F3_NDiv_Com23", new Rect( 1.00f - wt * 0.5f, baseY + 0.45f, wt, 1.80f), wallLayer, wallSpr);
        var northDoors = new[]
        {
            ("Door_F3_ComLab_1", -2.50f, false, 90f),
            ("Door_F3_ComLab_2", -0.25f, false, 90f),
            ("Door_F3_ComLab_3",  2.25f, false, 90f)
        };
        AddHorizontalSegmentWithDoors(fw, dr, "F3_SpineNorth", -3.50f, 3.50f, baseY + 0.45f, northDoors,
            wt, wallSpr, doorSpr, doorSpr, doorSpr, doorSpr, wallLayer, interactLayer);

        // South Row: 2 stacked SW utilities, 4 South Classrooms, Partitioned Control Suite SE
        float[] sDivs = { -2.625f, -1.375f, -0.125f, 1.125f, 2.375f };
        for (int i = 0; i < sDivs.Length; i++)
        {
            AddWallSegment(fw, $"F3_SDiv_{i}", new Rect(sDivs[i] - wt * 0.5f, baseY - 2.25f, wt, 1.80f), wallLayer, wallSpr);
        }
        AddHorizontalWallWithDoor(fw, dr, "F3_UtilMid", -3.50f, -2.625f, baseY - 1.35f, -3.0625f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F3_UtilityLink", swingDeg: -90f);
        AddHorizontalWallWithDoor(fw, dr, "F3_CtrlMid", 2.375f, 3.50f, baseY - 1.35f, 2.9375f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F3_ControlSuite_Inner", swingDeg: -90f);

        var southDoors = new[]
        {
            ("Door_F3_UtilityUpper", -3.0625f, false, -90f),
            ("Door_F3_Classroom_S1", -2.00f, false, -90f),
            ("Door_F3_Classroom_S2", -0.75f, false, -90f),
            ("Door_F3_Classroom_S3",  0.50f, false, -90f),
            ("Door_F3_Classroom_S4",  1.75f, false, -90f),
            ("Door_F3_ControlSuite",  2.9375f, false, -90f)
        };
        AddHorizontalSegmentWithDoors(fw, dr, "F3_SpineSouth", -3.50f, 3.50f, baseY - 0.45f, southDoors,
            wt, wallSpr, doorSpr, doorSpr, doorSpr, doorSpr, wallLayer, interactLayer);

        // East Wing: Classroom E
        AddVerticalWallWithDoor(fw, dr, "F3_E_Top", 4.50f, baseY, baseY + 1.65f, baseY + 0.90f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F3_Classroom_E_Top", swingDeg: -90f);
        AddVerticalWallWithDoor(fw, dr, "F3_E_Bot", 4.50f, baseY - 1.65f, baseY, baseY - 0.90f, wt, wallSpr, doorSpr, doorSpr, wallLayer, interactLayer, "Door_F3_Classroom_E_Bot", swingDeg: -90f);
    }

    // ----------------------------------------------------------------------
    // School Furniture & Horror Props
    // ----------------------------------------------------------------------

    private static void BuildSchoolFurniture(List<SchoolMapLayout.RoomDef> rooms, Transform furnRoot, int wallLayer)
    {
        Sprite deskStudSpr = LoadSchoolSprite("Furniture/desk_student");
        Sprite deskTeachSpr = LoadSchoolSprite("Furniture/desk_teacher");
        Sprite deskDeanSpr = LoadSchoolSprite("Furniture/desk_dean_mahogany");
        Sprite deskUSpr = LoadSchoolSprite("Furniture/desk_u_counter");
        Sprite chairTealSpr = LoadSchoolSprite("Furniture/chair_teal");
        Sprite chairTheaterSpr = LoadSchoolSprite("Furniture/chair_theater_red");
        Sprite benchSpr = LoadSchoolSprite("Furniture/bench_workshop");
        Sprite pcOffSpr = LoadSchoolSprite("Furniture/pc_workstation_off");
        Sprite bookshelfSpr = LoadSchoolSprite("Furniture/bookshelf_topdown");
        Sprite lockerBankSpr = LoadSchoolSprite("Furniture/locker_bank");
        Sprite toiletSpr = LoadSchoolSprite("Furniture/toilet_stall_topdown");
        Sprite sinkSpr = LoadSchoolSprite("Furniture/sink_unit");
        Sprite boardSpr = LoadSchoolSprite("Furniture/chalkboard_ledge");

        foreach (var room in rooms)
        {
            Rect r = room.WorldRect;
            if (room.Role == SchoolMapLayout.RoomRole.Restroom)
            {
                bool northTip = room.LocalRect.yMin > 0f;
                bool westSide = room.LocalRect.xMin < 0f;
                float toiletY = northTip ? r.yMax - 0.42f : r.yMin + 0.04f;
                float stallX1 = westSide ? r.xMin + 0.36f : r.xMin + 0.62f;
                float stallX2 = stallX1 + 0.50f;

                CreateNonBlockingProp(furnRoot, $"{room.Id}_Stall1", toiletSpr, new Rect(stallX1, toiletY, 0.34f, 0.38f), "Item", 0, northTip ? 0f : 180f);
                CreateNonBlockingProp(furnRoot, $"{room.Id}_Stall2", toiletSpr, new Rect(stallX2, toiletY, 0.34f, 0.38f), "Item", 0, northTip ? 0f : 180f);

                float sinkY = northTip ? r.yMin + 0.14f : r.yMax - 0.34f;
                float sinkX = westSide ? r.xMin + 0.28f : r.xMax - 0.56f;
                CreateSolidProp(furnRoot, $"{room.Id}_Sink1", sinkSpr, new Rect(sinkX, sinkY, 0.28f, 0.20f), "Item", 0, wallLayer);
                CreateSolidProp(furnRoot, $"{room.Id}_Sink2", sinkSpr, new Rect(sinkX + 0.36f, sinkY, 0.28f, 0.20f), "Item", 0, wallLayer);
                continue;
            }

            if (room.Id == "F1_LockerAlcove")
            {
                CreateSolidProp(furnRoot, "F1_LockerBank_Rear", lockerBankSpr, new Rect(r.xMin, r.yMin + 0.05f, 0.14f, 0.55f), "Item", 0, wallLayer, 90f);
                continue;
            }

            if (room.Id == "F1_Workshop")
            {
                CreateSolidProp(furnRoot, "Workshop_Bench_NW", benchSpr, new Rect(r.xMin + 0.25f, r.yMax - 0.42f, 0.72f, 0.24f), "Item", 0, wallLayer);
                CreateSolidProp(furnRoot, "Workshop_Bench_Center", benchSpr, new Rect(r.center.x - 0.36f, r.center.y - 0.12f, 0.72f, 0.24f), "Item", 0, wallLayer);
                CreateSolidProp(furnRoot, "Workshop_Sink", sinkSpr, new Rect(r.xMax - 0.42f, r.yMin + 0.12f, 0.28f, 0.22f), "Item", 0, wallLayer);
                continue;
            }

            if (room.Id == "F1_DeansOffice_Reception")
            {
                CreateSolidProp(furnRoot, "Deans_ExecutiveDesk", deskDeanSpr, new Rect(r.center.x - 0.34f, r.yMin + 0.28f, 0.68f, 0.30f), "Item", 0, wallLayer);
                CreateNonBlockingProp(furnRoot, "Deans_Chair", chairTheaterSpr, new Rect(r.center.x - 0.11f, r.yMin + 0.10f, 0.22f, 0.22f), "Item", 0);
                continue;
            }
            if (room.Id == "F1_DeansOffice_Inner" || room.Id == "F1_DeansOffice_Annex")
            {
                CreateSolidProp(furnRoot, $"{room.Id}_Desk", deskTeachSpr, new Rect(r.center.x - 0.22f, r.center.y - 0.12f, 0.44f, 0.24f), "Item", 0, wallLayer);
                CreateSolidProp(furnRoot, $"{room.Id}_Shelf", bookshelfSpr, new Rect(r.xMin + 0.10f, r.yMin + 0.08f, 0.54f, 0.20f), "Item", 0, wallLayer);
                continue;
            }

            if (room.Id == "F2_MultipurposeHall")
            {
                // Stage chalkboard/podium on west edge & rows of red theater chairs
                CreateSolidProp(furnRoot, "Theater_StagePodium", deskTeachSpr, new Rect(r.xMin + 0.18f, r.center.y - 0.18f, 0.30f, 0.56f), "Item", 0, wallLayer, 90f);
                CreateNonBlockingProp(furnRoot, "Theater_StageChalkboard", boardSpr, new Rect(r.xMin + 0.04f, r.center.y - 0.35f, 0.14f, 0.70f), "Item", 1, 90f);
                for (int c = 0; c < 4; c++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        float sx = r.xMin + 0.85f + c * 0.38f;
                        float sy = r.yMin + 0.28f + row * 0.42f;
                        CreateNonBlockingProp(furnRoot, $"Theater_Seat_{c}_{row}", chairTheaterSpr, new Rect(sx, sy, 0.20f, 0.20f), "Item", 0, 90f);
                    }
                }
                continue;
            }

            if (room.Id == "F2_LibraryDiscussion")
            {
                CreateSolidProp(furnRoot, "Discussion_Table", deskTeachSpr, new Rect(r.center.x - 0.24f, r.center.y - 0.14f, 0.48f, 0.28f), "Item", 0, wallLayer);
                CreateNonBlockingProp(furnRoot, "Discussion_ChairL", chairTheaterSpr, new Rect(r.center.x - 0.18f, r.center.y + 0.20f, 0.22f, 0.22f), "Item", 0);
                CreateNonBlockingProp(furnRoot, "Discussion_ChairR", chairTheaterSpr, new Rect(r.center.x - 0.04f, r.center.y - 0.42f, 0.22f, 0.22f), "Item", 0, 180f);
                continue;
            }

            if (room.Id == "F2_Library")
            {
                // U/L checkout counter on NW corner + 3 parallel tall bookshelf stacks (cover/looping!) + reading tables
                CreateSolidProp(furnRoot, "Library_CheckoutCounter", deskUSpr, new Rect(r.xMin + 0.14f, r.yMax - 0.58f, 0.72f, 0.42f), "Item", 0, wallLayer);
                for (int s = 0; s < 3; s++)
                {
                    float sx = r.xMin + 1.05f + s * 0.52f;
                    GameObject shelfGo = CreateSolidProp(furnRoot, $"Library_Bookshelf_{s}", bookshelfSpr, new Rect(sx, r.yMin + 0.34f, 0.22f, 0.88f), "Item", 0, wallLayer, 90f);
                    AddShadowCaster2D(shelfGo);
                }
                CreateSolidProp(furnRoot, "Library_ReadingDesk", deskTeachSpr, new Rect(r.xMin + 0.24f, r.yMin + 0.24f, 0.48f, 0.24f), "Item", 0, wallLayer);
                continue;
            }

            if (room.Role == SchoolMapLayout.RoomRole.ComLab)
            {
                int cols = room.Id == "F3_ComLab_1" ? 5 : 7;
                float stepX = (r.width - 0.46f) / cols;
                // Instructor desk at back north end
                CreateSolidProp(furnRoot, $"{room.Id}_InstructorDesk", deskTeachSpr, new Rect(r.xMin + 0.18f, r.yMax - 0.34f, 0.48f, 0.22f), "Item", 0, wallLayer);
                CreateNonBlockingProp(furnRoot, $"{room.Id}_Board", boardSpr, new Rect(r.center.x - 0.35f, r.yMax - 0.14f, 0.70f, 0.12f), "Item", 1);

                for (int row = 0; row < 2; row++)
                {
                    float pcY = r.yMin + 0.28f + row * 0.62f;
                    for (int c = 0; c < cols; c++)
                    {
                        float pcX = r.xMin + 0.22f + c * stepX;
                        GameObject pcGo = CreateSolidProp(furnRoot, $"{room.Id}_PC_{row}_{c}", pcOffSpr, new Rect(pcX, pcY, 0.28f, 0.22f), "Item", 0, wallLayer);
                        var pcAnim = pcGo.AddComponent<SchoolPropAnimator>();
                        pcAnim.Configure("SchoolMap/Furniture/pc_workstation_flicker_sheet", 2, 2.5f + (c % 3) * 0.6f,
                            powerRequired: true, offRes: "SchoolMap/Furniture/pc_workstation_off", ppu: 64f);
                        CreateNonBlockingProp(furnRoot, $"{room.Id}_Chair_{row}_{c}", chairTealSpr, new Rect(pcX + 0.06f, pcY - 0.16f, 0.16f, 0.16f), "Item", 0);
                    }
                }
                continue;
            }

            if (room.Id == "F3_Classroom_S4")
            {
                // U-shaped classroom lab demo counter
                CreateSolidProp(furnRoot, "S4_UCounter", deskUSpr, new Rect(r.center.x - 0.34f, r.yMin + 0.24f, 0.68f, 0.42f), "Item", 0, wallLayer);
                CreateNonBlockingProp(furnRoot, "S4_Board", boardSpr, new Rect(r.center.x - 0.34f, r.yMin + 0.06f, 0.68f, 0.12f), "Item", 1);
                continue;
            }

            if (room.Id == "F3_ControlSuite_SE")
            {
                CreateSolidProp(furnRoot, "ControlSuite_RackA", benchSpr, new Rect(r.xMin + 0.12f, r.yMin + 0.22f, 0.56f, 0.22f), "Item", 0, wallLayer);
                CreateSolidProp(furnRoot, "ControlSuite_DeskB", deskTeachSpr, new Rect(r.xMin + 0.48f, r.yMax - 0.42f, 0.48f, 0.22f), "Item", 0, wallLayer);
                continue;
            }

            if (room.Role == SchoolMapLayout.RoomRole.Closet)
            {
                CreateSolidProp(furnRoot, $"{room.Id}_Cabinet", bookshelfSpr, new Rect(r.xMin + 0.10f, r.center.y - 0.10f, 0.42f, 0.18f), "Item", 0, wallLayer);
                continue;
            }

            if (room.Role == SchoolMapLayout.RoomRole.Classroom)
            {
                PopulateStandardClassroom(furnRoot, room, r, deskStudSpr, deskTeachSpr, chairTealSpr, boardSpr, wallLayer);
            }
        }
    }

    private static void PopulateStandardClassroom(
        Transform furnRoot, SchoolMapLayout.RoomDef room, Rect r,
        Sprite deskStudSpr, Sprite deskTeachSpr, Sprite chairSpr, Sprite boardSpr, int wallLayer)
    {
        bool tallWing = r.height > r.width * 1.25f;
        if (tallWing)
        {
            // East/West Wing vertical classroom: teacher desk & board at North, twin desk columns leaving wide corridor path
            CreateNonBlockingProp(furnRoot, $"{room.Id}_Board", boardSpr, new Rect(r.center.x - 0.35f, r.yMax - 0.14f, 0.70f, 0.12f), "Item", 1);
            CreateSolidProp(furnRoot, $"{room.Id}_TeacherDesk", deskTeachSpr, new Rect(r.center.x - 0.24f, r.yMax - 0.42f, 0.48f, 0.22f), "Item", 0, wallLayer);

            int rows = r.height > 2.4f ? 4 : 2;
            for (int row = 0; row < rows; row++)
            {
                float dy = r.yMin + 0.26f + row * ((r.height - 0.85f) / Mathf.Max(1, rows));
                for (int c = 0; c < 2; c++)
                {
                    float dx = r.xMin + 0.38f + c * 0.78f;
                    CreateSolidProp(furnRoot, $"{room.Id}_Desk_{row}_{c}", deskStudSpr, new Rect(dx, dy, 0.28f, 0.19f), "Item", 0, wallLayer);
                    CreateNonBlockingProp(furnRoot, $"{room.Id}_Chair_{row}_{c}", chairSpr, new Rect(dx + 0.06f, dy - 0.15f, 0.16f, 0.16f), "Item", 0);
                }
            }
        }
        else
        {
            // Central North or South partitioned classroom: board away from Spine door, 2x2 or 2x3 compact desk cluster
            bool northSide = room.LocalRect.yMin > 0f;
            float boardY = northSide ? r.yMax - 0.13f : r.yMin + 0.04f;
            float teachY = northSide ? r.yMax - 0.38f : r.yMin + 0.18f;
            CreateNonBlockingProp(furnRoot, $"{room.Id}_Board", boardSpr, new Rect(r.center.x - 0.32f, boardY, 0.64f, 0.11f), "Item", 1);
            CreateSolidProp(furnRoot, $"{room.Id}_TeacherDesk", deskTeachSpr, new Rect(r.center.x - 0.20f, teachY, 0.40f, 0.18f), "Item", 0, wallLayer);

            int cols = r.width >= 1.6f ? 3 : 2;
            float startX = r.xMin + 0.28f;
            float colStep = (r.width - 0.56f) / Mathf.Max(1, cols);
            for (int c = 0; c < cols; c++)
            {
                for (int row = 0; row < 2; row++)
                {
                    float dx = startX + c * colStep;
                    float dy = northSide ? (r.yMin + 0.34f + row * 0.40f) : (r.yMax - 0.68f - row * 0.40f);
                    CreateSolidProp(furnRoot, $"{room.Id}_Desk_{c}_{row}", deskStudSpr, new Rect(dx, dy, 0.24f, 0.16f), "Item", 0, wallLayer);
                    CreateNonBlockingProp(furnRoot, $"{room.Id}_Chair_{c}_{row}", chairSpr, new Rect(dx + 0.04f, dy + (northSide ? -0.14f : 0.16f), 0.15f, 0.15f), "Item", 0, northSide ? 0f : 180f);
                }
            }
        }
    }

    // ----------------------------------------------------------------------
    // 8 Bidirectional StairTeleporter Pairs
    // ----------------------------------------------------------------------

    private static List<StairSpawnPair> BuildAllStairTeleporters(
        Transform[] floorRoots, Transform wallsRoot, int wallLayer, int interactLayer)
    {
        Sprite stairUpSpr = LoadBarangaySprite("House/stairwell_up");
        Sprite stairDownSpr = LoadBarangaySprite("House/stairwell_down");
        Sprite capWallSpr = LoadSchoolSprite("Walls/wall_school_trim_cap");

        var pairs = new List<StairSpawnPair>(8);

        foreach (var sc in SchoolMapLayout.StairCorners)
        {
            float stairCenterX = sc.IsWestWing ? -6.00f : 6.00f;
            float singleArrX = sc.IsWestWing ? -5.25f : 5.25f;
            float splitArrX = sc.IsWestWing ? -5.18f : 5.18f;
            float stairRotZ = sc.IsWestWing ? 180f : 0f; // tread visual oriented toward center landing

            float y1 = SchoolMapLayout.FLOOR_1_Y + sc.LocalCenterY;
            float y2 = SchoolMapLayout.FLOOR_2_Y + sc.LocalCenterY;
            float y3 = SchoolMapLayout.FLOOR_3_Y + sc.LocalCenterY;

            // --- Floor 1 Stair Up (F1 -> F2) ---
            CreateStairNode(floorRoots[1], $"Stair_{sc.CornerName}_F1_To_F2", stairUpSpr,
                stairCenterX, y1, 0.76f, 0.44f, stairRotZ, sc.IsWestWing, interactLayer,
                $"Arrival_{sc.CornerName}_F1_From_F2", new Vector2(singleArrX, y1),
                out StairTeleporter tp1To2, out Transform arr1From2);

            // --- Floor 2 Split Landing: Down to F1 (upper bay) + Balustrade + Up to F3 (lower bay) ---
            float balXMin = sc.IsWestWing ? -6.42f : 5.38f;
            AddWallSegment(wallsRoot, $"Balustrade_{sc.CornerName}_F2",
                new Rect(balXMin, y2 - 0.05f, 1.04f, 0.10f), wallLayer, capWallSpr, castShadows: true);

            CreateStairNode(floorRoots[2], $"Stair_{sc.CornerName}_F2_To_F1", stairDownSpr,
                stairCenterX, y2 + 0.31f, 0.74f, 0.38f, stairRotZ, sc.IsWestWing, interactLayer,
                $"Arrival_{sc.CornerName}_F2_From_F1", new Vector2(splitArrX, y2 + 0.31f),
                out StairTeleporter tp2To1, out Transform arr2From1);

            CreateStairNode(floorRoots[2], $"Stair_{sc.CornerName}_F2_To_F3", stairUpSpr,
                stairCenterX, y2 - 0.31f, 0.74f, 0.38f, stairRotZ, sc.IsWestWing, interactLayer,
                $"Arrival_{sc.CornerName}_F2_From_F3", new Vector2(splitArrX, y2 - 0.31f),
                out StairTeleporter tp2To3, out Transform arr2From3);

            // --- Floor 3 Stair Down (F3 -> F2) ---
            CreateStairNode(floorRoots[3], $"Stair_{sc.CornerName}_F3_To_F2", stairDownSpr,
                stairCenterX, y3, 0.76f, 0.44f, stairRotZ, sc.IsWestWing, interactLayer,
                $"Arrival_{sc.CornerName}_F3_From_F2", new Vector2(singleArrX, y3),
                out StairTeleporter tp3To2, out Transform arr3From2);

            // Wire bidirectional links & Floor labels
            tp1To2.Configure(tp2To1, arr2From1, "2nd Floor");
            tp2To1.Configure(tp1To2, arr1From2, "1st Floor");
            tp2To3.Configure(tp3To2, arr3From2, "3rd Floor");
            tp3To2.Configure(tp2To3, arr2From3, "2nd Floor");

            tp1To2.EnsureMidSpriteBackupNode();
            tp2To1.EnsureMidSpriteBackupNode();
            tp2To3.EnsureMidSpriteBackupNode();
            tp3To2.EnsureMidSpriteBackupNode();

            DisableNavMeshOnMidNode(tp1To2.transform);
            DisableNavMeshOnMidNode(tp2To1.transform);
            DisableNavMeshOnMidNode(tp2To3.transform);
            DisableNavMeshOnMidNode(tp3To2.transform);

            pairs.Add(new StairSpawnPair
            {
                CornerName = sc.CornerName,
                LowerFloor = 1,
                UpperFloor = 2,
                LowerArrival = arr1From2,
                UpperArrival = arr2From1
            });
            pairs.Add(new StairSpawnPair
            {
                CornerName = sc.CornerName,
                LowerFloor = 2,
                UpperFloor = 3,
                LowerArrival = arr2From3,
                UpperArrival = arr3From2
            });
        }

        return pairs;
    }

    private static void CreateStairNode(
        Transform parent,
        string name,
        Sprite sprite,
        float cx,
        float cy,
        float w,
        float h,
        float rotZ,
        bool isWestWing,
        int interactLayer,
        string arrivalName,
        Vector2 arrivalPos,
        out StairTeleporter teleporter,
        out Transform arrivalTransform)
    {
        GameObject stairGo = new GameObject(name);
        stairGo.transform.SetParent(parent, false);
        stairGo.transform.position = new Vector3(cx, cy, 0f);
        stairGo.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        stairGo.layer = interactLayer;

        var sr = stairGo.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = "Floor";
        sr.sortingOrder = 2;
        if (sprite != null)
        {
            stairGo.transform.localScale = new Vector3(w / sprite.bounds.size.x, h / sprite.bounds.size.y, 1f);
        }
        stairGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Recessed trigger collider covering the outer half of treads (>= 0.70u clear of arrival)
        var box = stairGo.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        if (sprite != null)
        {
            box.size = new Vector2(sprite.bounds.size.x * 0.48f, sprite.bounds.size.y * 0.90f);
            box.offset = new Vector2(sprite.bounds.size.x * 0.24f, 0f);
        }

        teleporter = stairGo.AddComponent<StairTeleporter>();

        GameObject arrGo = new GameObject(arrivalName);
        arrGo.transform.SetParent(parent, false);
        arrGo.transform.position = new Vector3(arrivalPos.x, arrivalPos.y, 0f);
        arrivalTransform = arrGo.transform;
    }

    private static void DisableNavMeshOnMidNode(Transform stairTransform)
    {
        Transform mid = stairTransform.Find("MidStairTeleporterNode");
        if (mid != null && mid.GetComponent<NavMeshModifier>() == null)
        {
            mid.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }
    }

    // ----------------------------------------------------------------------
    // FuseBox, HideZones, GhostProps & GhostNode Graph
    // ----------------------------------------------------------------------

    private static void BuildSchoolFuseBox(Transform furnRoot, int interactLayer)
    {
        GameObject fbGo = new GameObject("FuseBox");
        fbGo.transform.SetParent(furnRoot, false);
        fbGo.transform.position = new Vector3(-3.36f, -1.65f, 0f);
        fbGo.layer = interactLayer;

        Sprite fbSpr = LoadBarangaySprite("House/Furniture/furn_fusebox");
        var sr = fbGo.AddComponent<SpriteRenderer>();
        sr.sprite = fbSpr;
        sr.sortingLayerName = "Item";
        sr.sortingOrder = 2;
        if (fbSpr != null)
        {
            fbGo.transform.localScale = new Vector3(0.16f / fbSpr.bounds.size.x, 0.16f / fbSpr.bounds.size.y, 1f);
        }

        fbGo.AddComponent<BoxCollider2D>().isTrigger = true;
        fbGo.AddComponent<NetworkObject>();
        var fb = fbGo.AddComponent<FuseBox>();
        SerializedObject so = new SerializedObject(fb);
        var maxProp = so.FindProperty("maxLightsBeforeOverload");
        if (maxProp != null) maxProp.intValue = 9;
        so.ApplyModifiedPropertiesWithoutUndo();
        fbGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static void BuildSchoolHideZones(Transform[] floorRoots)
    {
        foreach (var hz in SchoolMapLayout.BuildAllHideZones())
        {
            Rect wr = hz.WorldRect;
            GameObject go = new GameObject(hz.Name);
            go.transform.SetParent(floorRoots[hz.Floor], false);
            go.transform.position = new Vector3(wr.center.x, wr.center.y, 0f);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = wr.size;
            go.AddComponent<HideZone>();
        }
    }

    private static void BuildSchoolGhostProps(Transform propsRoot)
    {
        foreach (var p in SchoolMapLayout.BuildAllGhostProps())
        {
            GameObject go = new GameObject(p.Name);
            go.transform.SetParent(propsRoot, false);
            go.transform.position = new Vector3(p.WorldPos.x, p.WorldPos.y, 0f);
            go.transform.localScale = new Vector3(0.14f, 0.14f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Item";
            sr.sortingOrder = 2;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.22f;
            col.isTrigger = true;

            go.AddComponent<NetworkObject>();
            var prop = go.AddComponent<GhostPropInteractable>();
            prop.Configure(p.DisplayName, p.SheetResource, p.Breakable, p.SfxName);
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }
    }

    private static void BuildSchoolGhostNodes(Transform pathSys)
    {
        if (pathSys == null) return;

        var rawNodes = new List<Vector2>();
        for (int f = 1; f <= 3; f++)
        {
            float y = SchoolMapLayout.GetFloorBaseY(f);
            // West & East Corridors + Central Spine + Stair Arrival landings
            rawNodes.Add(new Vector2(-4.00f, y + 3.55f));
            rawNodes.Add(new Vector2(-4.00f, y + 2.25f));
            rawNodes.Add(new Vector2(-5.25f, y + 2.25f)); // NW stair landing
            rawNodes.Add(new Vector2(-4.00f, y + 0.00f));
            rawNodes.Add(new Vector2(-4.00f, y - 2.25f));
            rawNodes.Add(new Vector2(-5.25f, y - 2.25f)); // SW stair landing
            rawNodes.Add(new Vector2(-4.00f, y - 3.55f));

            rawNodes.Add(new Vector2(-2.20f, y + 0.00f));
            rawNodes.Add(new Vector2( 0.00f, y + 0.00f));
            rawNodes.Add(new Vector2( 2.20f, y + 0.00f));

            rawNodes.Add(new Vector2( 4.00f, y + 3.55f));
            rawNodes.Add(new Vector2( 4.00f, y + 2.25f));
            rawNodes.Add(new Vector2( 5.25f, y + 2.25f)); // NE stair landing
            rawNodes.Add(new Vector2( 4.00f, y + 0.00f));
            rawNodes.Add(new Vector2( 4.00f, y - 2.25f));
            rawNodes.Add(new Vector2( 5.25f, y - 2.25f)); // SE stair landing
            rawNodes.Add(new Vector2( 4.00f, y - 3.55f));
        }

        // Add room centers so ghosts roam smoothly inside classrooms, theater, library, comlabs
        foreach (var room in SchoolMapLayout.BuildAllRooms())
        {
            if (room.HasGhostMarker)
            {
                rawNodes.Add(room.WorldRect.center);
            }
        }

        var createdNodes = new List<GhostNode>(rawNodes.Count);
        for (int i = 0; i < rawNodes.Count; i++)
        {
            GameObject go = new GameObject($"GhostNode_{i}");
            go.transform.SetParent(pathSys, false);
            go.transform.position = new Vector3(rawNodes[i].x, rawNodes[i].y, 0f);
            createdNodes.Add(go.AddComponent<GhostNode>());
        }

        // Connect intra-floor nodes within reasonable corridor proximity
        for (int i = 0; i < createdNodes.Count; i++)
        {
            var list = new List<GhostNode>();
            Vector2 pi = createdNodes[i].transform.position;
            for (int j = 0; j < createdNodes.Count; j++)
            {
                if (i == j) continue;
                Vector2 pj = createdNodes[j].transform.position;
                if (Mathf.Abs(pi.y - pj.y) > 8.0f) continue; // Keep intra-floor graph clean (NavMesh handles stairs)
                if (Vector2.Distance(pi, pj) <= 2.45f)
                {
                    list.Add(createdNodes[j]);
                }
            }
            createdNodes[i].neighbors = list;
        }
    }

    // ----------------------------------------------------------------------
    // Managers, Weather, Off-Bounds Burial, NavMesh & Build Settings
    // ----------------------------------------------------------------------

    private static void ConfigureGhostHandler(Transform ghostMgr)
    {
        if (ghostMgr == null) return;
        foreach (var gh in ghostMgr.GetComponentsInChildren<GhostHandler>(true))
        {
            SerializedObject so = new SerializedObject(gh);
            var prop = so.FindProperty("ghostRoom");
            if (prop != null) prop.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void CreateSchoolWeatherManager(Transform envRoot, Light2D globalLight)
    {
        GameObject go = new GameObject("WeatherManager");
        go.transform.SetParent(envRoot, false);
        go.AddComponent<NetworkObject>();
        var wm = go.AddComponent<WeatherManager>();

        Rect[] schoolExteriorZones =
        {
            new Rect(-3.40f,  2.35f,  6.80f, 1.85f), // North Courtyard Plaza
            new Rect(-7.10f,  4.35f, 14.20f, 2.55f), // North Entrance Plaza & Lawn
            new Rect(-3.40f, -4.15f,  6.80f, 1.85f), // South Courtyard Grass
            new Rect(-7.10f, -6.10f, 14.20f, 1.80f), // South Outer Lawn
            new Rect(-7.10f, -4.20f,  0.55f, 8.40f), // West Perimeter Walk
            new Rect( 6.55f, -4.20f,  0.55f, 8.40f)  // East Perimeter Walk
        };
        Rect buildingPerimeter = new Rect(-6.50f, -4.25f, 13.00f, 8.50f);
        wm.ConfigureCustomBounds(schoolExteriorZones, buildingPerimeter);

        SerializedObject so = new SerializedObject(wm);
        var glProp = so.FindProperty("globalLight");
        if (glProp != null && globalLight != null) glProp.objectReferenceValue = globalLight;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateOffBoundsBurialStage()
    {
        GameObject stageGo = new GameObject("BurialMapStage");
        stageGo.transform.position = BurialMapStage.STAGE_CENTER;
        var stage = stageGo.AddComponent<BurialMapStage>();
        stage.BuildStageIfEmpty();
    }

    private static void BakeSchoolNavMeshAndLinks(Transform envRoot, List<StairSpawnPair> stairPairs)
    {
        Transform navTr = envRoot.Find("NavhMesh");
        if (navTr == null) navTr = envRoot.Find("NavMesh");
        if (navTr == null)
        {
            var go = new GameObject("NavhMesh");
            go.transform.SetParent(envRoot, false);
            navTr = go.transform;
        }
        var surface = navTr.GetComponent<NavMeshSurface>();
        if (surface == null) surface = navTr.gameObject.AddComponent<NavMeshSurface>();

        SerializedObject nso = new SerializedObject(surface);
        var voxelProp = nso.FindProperty("m_OverrideVoxelSize");
        if (voxelProp != null) voxelProp.boolValue = true;
        var voxelVal = nso.FindProperty("m_VoxelSize");
        if (voxelVal != null) voxelVal.floatValue = 0.01f;
        nso.ApplyModifiedPropertiesWithoutUndo();

        BarangayMapFixer.BakeAndPersistNavMeshSurface(surface, SCHOOL_SCENE_PATH);

        foreach (var pair in stairPairs)
        {
            string linkName = $"StairNavMeshLink_{pair.CornerName}_F{pair.LowerFloor}F{pair.UpperFloor}";
            GameObject linkGo = new GameObject(linkName);
            linkGo.transform.SetParent(envRoot, false);
            linkGo.transform.position = Vector3.zero;
            var link = linkGo.AddComponent<NavMeshLink>();
            link.agentTypeID = surface.agentTypeID;
            link.startPoint = pair.LowerArrival != null ? pair.LowerArrival.position : Vector3.zero;
            link.endPoint = pair.UpperArrival != null ? pair.UpperArrival.position : Vector3.zero;
            link.width = 0.28f;
            link.costModifier = -1;
            link.bidirectional = true;
            link.autoUpdate = true;
            link.area = 0;
            link.UpdateLink();
        }
    }

    private static void RegisterAllBuildScenes()
    {
        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool hasSchool = false;
        foreach (var s in buildScenes)
        {
            if (s != null && s.path == SCHOOL_SCENE_PATH)
            {
                s.enabled = true;
                hasSchool = true;
                break;
            }
        }
        if (!hasSchool && File.Exists(SCHOOL_SCENE_PATH))
        {
            buildScenes.Add(new EditorBuildSettingsScene(SCHOOL_SCENE_PATH, true));
        }
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    // ----------------------------------------------------------------------
    // Low-level Geometry / Sprite / Component Helpers
    // ----------------------------------------------------------------------

    private static void AddArchwayWall(
        Transform wallsRoot, string name, float wallX, float yMin, float yMax, float openCenterY, float halfGap,
        float wt, int wallLayer, Sprite wallSpr)
    {
        if (openCenterY - halfGap > yMin + 0.01f)
            AddWallSegment(wallsRoot, $"{name}_B", new Rect(wallX - wt * 0.5f, yMin, wt, (openCenterY - halfGap) - yMin), wallLayer, wallSpr);
        if (yMax > openCenterY + halfGap + 0.01f)
            AddWallSegment(wallsRoot, $"{name}_T", new Rect(wallX - wt * 0.5f, openCenterY + halfGap, wt, yMax - (openCenterY + halfGap)), wallLayer, wallSpr);
    }

    private static void AddWallSegment(Transform parent, string name, Rect rect, int wallLayer, Sprite wallSprite, bool castShadows = true)
    {
        GameObject go = CreateTiledSpriteRect(parent, name, wallSprite, rect, "Wall", 0, wallLayer);
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(rect.width, rect.height);
        if (castShadows) AddShadowCaster2D(go);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;
    }

    private static void AddHorizontalWallWithDoor(
        Transform wallsRoot, Transform doorsRoot, string name, float xMin, float xMax, float wallY, float doorX,
        float wt, Sprite wallSpr, Sprite doorClosed, Sprite doorOpen, int wallLayer, int interactLayer,
        string doorName, bool isFrontDoor = false, bool requiresKey = false, bool isHidingSpot = false, float swingDeg = 90f)
    {
        float halfGap = isHidingSpot ? 0.15f : 0.13f;
        if (doorX - halfGap > xMin + 0.01f)
            AddWallSegment(wallsRoot, name + "_L", new Rect(xMin, wallY - wt * 0.5f, (doorX - halfGap) - xMin, wt), wallLayer, wallSpr);
        if (xMax > doorX + halfGap + 0.01f)
            AddWallSegment(wallsRoot, name + "_R", new Rect(doorX + halfGap, wallY - wt * 0.5f, xMax - (doorX + halfGap), wt), wallLayer, wallSpr);
        CreateNetworkDoor(doorsRoot, doorName, new Vector2(doorX, wallY), false,
            doorClosed, doorOpen, isFrontDoor, requiresKey, isHidingSpot, wallLayer, interactLayer, swingDeg);
    }

    private static void AddVerticalWallWithDoor(
        Transform wallsRoot, Transform doorsRoot, string name, float wallX, float yMin, float yMax, float doorY,
        float wt, Sprite wallSpr, Sprite doorClosed, Sprite doorOpen, int wallLayer, int interactLayer,
        string doorName, bool isFrontDoor = false, bool requiresKey = false, bool isHidingSpot = false, float swingDeg = -90f)
    {
        float halfGap = isHidingSpot ? 0.15f : 0.13f;
        if (doorY - halfGap > yMin + 0.01f)
            AddWallSegment(wallsRoot, name + "_B", new Rect(wallX - wt * 0.5f, yMin, wt, (doorY - halfGap) - yMin), wallLayer, wallSpr);
        if (yMax > doorY + halfGap + 0.01f)
            AddWallSegment(wallsRoot, name + "_T", new Rect(wallX - wt * 0.5f, doorY + halfGap, wt, yMax - (doorY + halfGap)), wallLayer, wallSpr);
        CreateNetworkDoor(doorsRoot, doorName, new Vector2(wallX, doorY), true,
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
            CreateNetworkDoor(doorsRoot, doors[i].doorName, new Vector2(doors[i].xCenter, yWall), false,
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
            cRefNmm.ignoreFromBuild = false;
            cRefNmm.overrideArea = true;
            cRefNmm.area = 1;
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

    private static void CreateWalkableExterior(
        Transform parent, string name, Sprite sprite, Rect rect, SurfaceType surface, int groundLayer)
    {
        GameObject go = CreateTiledSpriteRect(parent, name, sprite, rect, "Floor", 0, groundLayer);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 0;
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = rect.size;
        var fs = go.AddComponent<FloorSurface>();
        fs.surfaceType = surface;
        fs.priority = 1;
    }

    private static void CreateRoomFloor(
        Transform parent, string name, Sprite sprite, Rect rect, SurfaceType surface, int groundLayer)
    {
        GameObject go = CreateTiledSpriteRect(parent, name, sprite, rect, "Floor", 1, groundLayer);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 0;
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = rect.size;
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

    private static void CreateRoomLightingAndSwitch(
        Transform lightSys, string roomName, Rect rect, int wallLayer, int interactLayer, Vector2? switchPos)
    {
        GameObject swGo = new GameObject($"{roomName} LS");
        swGo.transform.SetParent(lightSys, false);
        Vector2 pos = switchPos ?? new Vector2(rect.xMin + 0.14f, rect.yMin + 0.14f);
        swGo.transform.position = new Vector3(pos.x, pos.y, 0f);
        swGo.layer = interactLayer;

        Sprite capSpr = LoadSchoolSprite("Walls/wall_school_trim_cap");
        var swSr = swGo.AddComponent<SpriteRenderer>();
        swSr.sprite = capSpr;
        swSr.color = new Color(0.86f, 0.82f, 0.65f, 1f);
        swSr.sortingLayerName = "Item";
        swSr.sortingOrder = 1;
        if (capSpr != null)
        {
            swGo.transform.localScale = new Vector3(0.05f / capSpr.bounds.size.x, 0.05f / capSpr.bounds.size.y, 1f);
        }

        swGo.AddComponent<BoxCollider2D>().isTrigger = true;
        swGo.AddComponent<NetworkObject>();
        var hls = swGo.AddComponent<HouseLightSwitch>();
        hls.EnsureSwitchSetup();
        swGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject lightGo = new GameObject($"{roomName}_Light2D");
        lightGo.transform.SetParent(swGo.transform, false);
        lightGo.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        lightGo.transform.localScale = Vector3.one;
        var light = lightGo.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1f, 0.94f, 0.80f, 1f);
        light.intensity = 0.95f;
        light.pointLightOuterRadius = Mathf.Max(0.95f, Mathf.Max(rect.width, rect.height) * 0.78f);
        light.pointLightInnerRadius = 0.15f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 0.55f;
        light.enabled = false;
        AssignAllSortingLayers(light);

        GameObject szGo = new GameObject($"{roomName}_SafeZone");
        szGo.transform.SetParent(lightSys, false);
        szGo.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        var szCol = szGo.AddComponent<BoxCollider2D>();
        szCol.isTrigger = true;
        szCol.size = rect.size;
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

    private static GameObject CreateSolidProp(
        Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int wallLayer, float rotZ = 0f)
    {
        GameObject go = CreateSpriteRect(parent, name, sprite, rect, sortingLayer, sortOrder, wallLayer, rotZ);
        var col = go.AddComponent<BoxCollider2D>();
        if (sprite != null) col.size = sprite.bounds.size;
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;
        return go;
    }

    private static GameObject CreateNonBlockingProp(
        Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, float rotZ = 0f)
    {
        GameObject go = CreateSpriteRect(parent, name, sprite, rect, sortingLayer, sortOrder, 0, rotZ);
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        return go;
    }

    private static GameObject CreateSpriteRect(
        Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int layer, float rotZ = 0f)
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
            float w = Mathf.Abs(rotZ) > 45f && Mathf.Abs(rotZ) < 135f ? rect.height : rect.width;
            float h = Mathf.Abs(rotZ) > 45f && Mathf.Abs(rotZ) < 135f ? rect.width : rect.height;
            go.transform.localScale = new Vector3(w / Mathf.Max(0.001f, sz.x), h / Mathf.Max(0.001f, sz.y), 1f);
        }
        if (Mathf.Abs(rotZ) > 0.01f) go.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        return go;
    }

    private static GameObject CreateTiledSpriteRect(
        Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int layer)
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
        sr.size = rect.size;
        return go;
    }

    private static void AddInvisibleShadowWall(Transform parent, string name, Rect rect, int wallLayer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        go.layer = wallLayer;
        go.AddComponent<BoxCollider2D>().size = rect.size;
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
        var light = go.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1.0f, 0.94f, 0.78f, 1f);
        light.intensity = 1.05f;
        light.pointLightInnerRadius = 0.08f;
        light.pointLightOuterRadius = 1.25f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 0.75f;
        AssignAllSortingLayers(light);
    }

    private static void CreateJeepneyRampDoor(Transform doorsRoot, Vector2 pos, int wallLayer, int interactLayer)
    {
        Sprite doorSpr = LoadBarangaySprite("House/door_main_oak");
        GameObject rampGo = new GameObject("JeepneyRampDoor");
        rampGo.transform.SetParent(doorsRoot, false);
        rampGo.transform.position = new Vector3(pos.x, pos.y, 0f);
        rampGo.layer = interactLayer;
        rampGo.AddComponent<NetworkObject>();

        GameObject cRef = new GameObject("ClosedRef");
        cRef.transform.SetParent(rampGo.transform, false);
        cRef.layer = wallLayer;
        var cSr = cRef.AddComponent<SpriteRenderer>();
        cSr.sprite = doorSpr;
        cSr.sortingLayerName = "Wall";
        cSr.sortingOrder = 2;
        if (doorSpr != null)
        {
            cRef.transform.localScale = new Vector3(0.24f / doorSpr.bounds.size.x, 0.045f / doorSpr.bounds.size.y, 1f);
            cRef.AddComponent<BoxCollider2D>().size = doorSpr.bounds.size;
        }
        cRef.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject oRef = new GameObject("OpenRef");
        oRef.transform.SetParent(rampGo.transform, false);
        oRef.transform.localPosition = new Vector3(-0.12f, -0.09f, 0f);
        oRef.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        oRef.layer = wallLayer;
        var oSr = oRef.AddComponent<SpriteRenderer>();
        oSr.sprite = doorSpr;
        oSr.sortingLayerName = "Wall";
        oSr.sortingOrder = 2;
        if (doorSpr != null) oRef.transform.localScale = cRef.transform.localScale;
        oRef.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        var nd = rampGo.AddComponent<NetworkDoor>();
        SerializedObject so = new SerializedObject(nd);
        so.FindProperty("closedDoorRef").objectReferenceValue = cSr;
        so.FindProperty("openDoorRef").objectReferenceValue = oSr;
        so.FindProperty("isFrontDoor").boolValue = false;
        so.FindProperty("requiresKey").boolValue = false;
        so.FindProperty("isHidingSpot").boolValue = false;
        so.FindProperty("allowPlayerInteract").boolValue = false;
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
        for (int i = 0; i < targetIds.Count; i++) layersProp.GetArrayElementAtIndex(i).intValue = targetIds[i];
    }

    private static Light2D EnsureGlobalLight()
    {
        foreach (var l in Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include))
        {
            if (l != null && l.lightType == Light2D.LightType.Global)
            {
                l.enabled = true;
                l.intensity = 0.045f;
                return l;
            }
        }
        GameObject glGo = new GameObject("Global Light 2D");
        var gl = glGo.AddComponent<Light2D>();
        gl.lightType = Light2D.LightType.Global;
        gl.intensity = 0.045f;
        return gl;
    }

    private static void CleanSoundSystemAndCamera(Transform soundSys)
    {
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
    }

    private static void StripDuplicateGameSettingsManager()
    {
        foreach (var matchMgr in Object.FindObjectsByType<GameMatchManager>(FindObjectsInactive.Include))
        {
            if (matchMgr == null) continue;
            var dupSettings = matchMgr.GetComponent<GameSettingsManager>();
            if (dupSettings != null) Object.DestroyImmediate(dupSettings);
        }
    }

    private static Sprite LoadSchoolSprite(string rel) => PixelScenePostProcessor.LoadPixelSprite($"SchoolMap/{rel}", 64f);
    private static Sprite LoadBarangaySprite(string rel) => PixelScenePostProcessor.LoadPixelSprite($"BarangayMap/{rel}", 64f);

    private static int ResolveLayer(string name, int fallback)
    {
        int layer = LayerMask.NameToLayer(name);
        return layer < 0 ? fallback : layer;
    }

    private static void DestroyNamedChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    private static Transform PrepareEmptyChild(Transform parent, string name)
    {
        Transform tr = parent.Find(name);
        if (tr == null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
        for (int i = tr.childCount - 1; i >= 0; i--) Object.DestroyImmediate(tr.GetChild(i).gameObject);
        return tr;
    }

    private static Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root == null) return null;
        if (root.name == targetName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), targetName);
            if (found != null) return found;
        }
        return null;
    }
}
