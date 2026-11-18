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
/// Builds Assets/Scenes/CemeteryMap.unity (Kampo Santo ng San Nicolas — Public Cemetery),
/// a pure 90-degree top-down 16-bit contract map (~8.50 x 6.50 compound + west road)
/// using modular sector builders and automated NavMesh path verification.
/// </summary>
public static class CemeteryMapSceneBuilder
{
    public const string GAME_MAP_SCENE_PATH = "Assets/Scenes/GameMapScene.unity";
    public const string CEMETERY_SCENE_PATH = "Assets/Scenes/CemeteryMap.unity";
    private const string BARANGAY_SPRITE_PREFIX = "Assets/Resources/Sprites/BarangayMap/";
    private const string CEMETERY_SPRITE_PREFIX = "Assets/Resources/Sprites/CemeteryMap/";

    [MenuItem("Pamahiin/Build CemeteryMap Scene")]
    public static void BuildCemeteryMapScene()
    {
        // 1. Ensure reused Barangay assets, Cemetery pixel art, and Journal blueprint exist
        BarangayAssetGenerator.GenerateAllAssets();
        CemeteryAssetGenerator.GenerateAllAssets();
        CemeteryBlueprintGenerator.GenerateBlueprintPng();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // 2. Clone GameMapScene.unity as template (preserves Managers, Van, Ghost Handler, UI)
        if (FileExists(CEMETERY_SCENE_PATH))
        {
            AssetDatabase.DeleteAsset(CEMETERY_SCENE_PATH);
        }
        AssetDatabase.CopyAsset(GAME_MAP_SCENE_PATH, CEMETERY_SCENE_PATH);
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.OpenScene(CEMETERY_SCENE_PATH, OpenSceneMode.Single);

        GameObject envRoot = GameObject.Find("Environment");
        if (envRoot == null)
        {
            Debug.LogError("[CemeteryMapSceneBuilder] Could not find Environment root in cloned GameMapScene.");
            return;
        }

        // Preserve Van before resetting Environment transform
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

        Transform oldMapTr = envRoot.transform.Find("UPDATED Tanglewood Drive");
        if (oldMapTr != null) Object.DestroyImmediate(oldMapTr.gameObject);

        int wallLayer = LayerMask.NameToLayer("Walls");
        int interactLayer = LayerMask.NameToLayer("Interactable");
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (wallLayer < 0) wallLayer = 0;
        if (interactLayer < 0) interactLayer = 0;
        if (groundLayer < 0) groundLayer = 0;

        if (vanTr != null)
        {
            vanTr.SetParent(envRoot.transform, false);
            vanTr.position = new Vector3(-0.82f, 0.28f, 0f);
            vanTr.rotation = preservedVanWorldRot;
            vanTr.localScale = preservedVanWorldScale;
        }

        Transform lightSys = PrepareEmptyChild(envRoot.transform, "LightSystem");
        Transform ghostMgr = envRoot.transform.Find("Ghost Manager");
        Transform ghostMarkers = ghostMgr != null ? PrepareEmptyChild(ghostMgr, "Ghost Marker") : null;
        Transform pathSys = ghostMgr != null ? PrepareEmptyChild(ghostMgr, "PathFindingSystem") : null;
        Transform soundSys = envRoot.transform.Find("SoundSystem");

        Light2D globalLight = EnsureGlobalLight();

        GameObject mapRoot = new GameObject("KampoSantoCemeteryMap");
        mapRoot.transform.SetParent(envRoot.transform, false);

        Transform roadRoot = new GameObject("WestRoad").transform;
        roadRoot.SetParent(mapRoot.transform, false);
        Transform compoundRoot = new GameObject("CemeteryCompound").transform;
        compoundRoot.SetParent(mapRoot.transform, false);
        Transform wallsRoot = new GameObject("Walls").transform;
        wallsRoot.SetParent(mapRoot.transform, false);
        Transform doorsRoot = new GameObject("Doors").transform;
        doorsRoot.SetParent(mapRoot.transform, false);
        Transform buildingsRoot = new GameObject("EnclosedBuildings").transform;
        buildingsRoot.SetParent(mapRoot.transform, false);
        Transform landmarksRoot = new GameObject("GravesAndNitso").transform;
        landmarksRoot.SetParent(mapRoot.transform, false);
        Transform propsRoot = new GameObject("GhostProps").transform;
        propsRoot.SetParent(mapRoot.transform, false);
        Transform candlesRoot = new GameObject("CemeteryCandles").transform;
        candlesRoot.SetParent(mapRoot.transform, false);

        // 3. Reposition Van & build 16-bit Jeepney staging bay
        if (vanTr != null)
        {
            BuildJeepneyStagingBay(vanTr, envRoot.transform, doorsRoot, wallLayer, interactLayer, groundLayer);
        }

        // 4. Modular Sector Builders
        BuildWestRoadAndEntranceApron(roadRoot, wallLayer, groundLayer);
        BuildPerimeterAndFrontGate(compoundRoot, wallsRoot, doorsRoot, wallLayer, interactLayer, groundLayer);
        BuildMainAvenuesAndCrossPlaza(compoundRoot, landmarksRoot, ghostMarkers, lightSys, wallLayer, interactLayer, groundLayer);
        BuildBantayShedAndYard(buildingsRoot, wallsRoot, doorsRoot, landmarksRoot, ghostMarkers, lightSys, wallLayer, interactLayer, groundLayer);
        BuildGrassBurialsAndBalete(landmarksRoot, ghostMarkers, lightSys, wallLayer, interactLayer);
        BuildNitsoMaze(compoundRoot, landmarksRoot, ghostMarkers, lightSys, wallLayer, interactLayer, groundLayer);
        BuildFamilyMausoleums(compoundRoot, buildingsRoot, wallsRoot, doorsRoot, ghostMarkers, lightSys, wallLayer, interactLayer, groundLayer);
        BuildCandlesAndProps(envRoot.transform, candlesRoot, propsRoot, interactLayer);
        BuildInvestigationZone(compoundRoot);
        BuildGhostNodeGraph(pathSys);

        // 5. Configure GhostHandler, WeatherManager, BurialMapStage & NavMesh
        ConfigureGhostHandler(ghostMgr);
        CreateCemeteryWeatherManager(envRoot.transform, globalLight);
        BarangayMapSceneBuilder.CreateBurialMapStage();
        BakeAndVerifyNavMesh(envRoot.transform, ghostMgr);

        // 6. Clean legacy SoundSystem & camera backdrop
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
                Transform c = soundSys.GetChild(i);
                if (c.name.Contains("Ambient Noise")) Object.DestroyImmediate(c.gameObject);
            }
        }
        if (Camera.main != null)
        {
            Camera.main.backgroundColor = new Color(0.02f, 0.024f, 0.038f, 1f);
        }

        StripDuplicateGameSettingsManager();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, CEMETERY_SCENE_PATH);
        RegisterAllScenesInBuildSettings();

        Debug.Log("[CemeteryMapSceneBuilder] Successfully built and baked Assets/Scenes/CemeteryMap.unity!");
    }

    // -----------------------------------------------------------------------
    // Sector 1: Jeepney Staging Bay & West Road
    // -----------------------------------------------------------------------
    private static void BuildJeepneyStagingBay(
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
            if (n == "van inside" || n == "wall")
            {
                child.gameObject.SetActive(false);
            }
        }

        Vector3 center = vanTr.position;
        float cx = center.x;
        float cy = center.y;

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
        GameObject bodyGo = CreateSpriteRect(jeepneyRoot.transform, "JeepneyBody",
            LoadBarangaySprite("Jeepney/jeepney_body_interior"), bodyRect, "Van", 0, 0);
        bodyGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject safeZoneGo = new GameObject("JeepneySafeZone");
        safeZoneGo.transform.SetParent(jeepneyRoot.transform, false);
        safeZoneGo.transform.position = new Vector3(cx, cy - 0.19f, 0f);
        var szCol = safeZoneGo.AddComponent<BoxCollider2D>();
        szCol.isTrigger = true;
        szCol.size = new Vector2(0.48f, 0.94f);
        safeZoneGo.AddComponent<SafeZone>();
        safeZoneGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        Rect tvRect = new Rect(cx - 0.105f, cy + 0.165f, 0.21f, 0.088f);
        GameObject tvPropGo = CreateSpriteRect(jeepneyRoot.transform, "JeepneyDashboardTV",
            LoadBarangaySprite("Jeepney/jeepney_dashboard_tv"), tvRect, "Van", 2, 0);
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
                foreach (var existingCol in dashGo.GetComponents<Collider2D>()) Object.DestroyImmediate(existingCol);
                dashBox = dashGo.AddComponent<BoxCollider2D>();
            }
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

    private static void BuildWestRoadAndEntranceApron(Transform parent, int wallLayer, int groundLayer)
    {
        Sprite roadSprite = LoadBarangaySprite("Exterior/asphalt_road");
        Rect roadRect = new Rect(-1.72f, -3.15f, 1.38f, 6.60f);
        var roadGo = CreateTiledSpriteRect(parent, "CemeteryRoadTile", roadSprite, roadRect, "Floor", -10, groundLayer);
        var roadNmm = roadGo.AddComponent<NavMeshModifier>();
        roadNmm.overrideArea = true;
        roadNmm.area = 1;
        var roadCol = roadGo.AddComponent<BoxCollider2D>();
        roadCol.isTrigger = true;
        roadCol.size = roadRect.size;
        var roadFs = roadGo.AddComponent<FloorSurface>();
        roadFs.surfaceType = SurfaceType.Pavement;
        roadFs.priority = 1;

        Sprite porchSprite = LoadBarangaySprite("House/floor_porch_tile");
        Rect apronRect = new Rect(-0.34f, -0.14f, 0.34f, 0.52f);
        var apronGo = CreateTiledSpriteRect(parent, "EntranceApron", porchSprite, apronRect, "Floor", -5, groundLayer);
        var apronNmm = apronGo.AddComponent<NavMeshModifier>();
        apronNmm.overrideArea = true;
        apronNmm.area = 1;
        var apronCol = apronGo.AddComponent<BoxCollider2D>();
        apronCol.isTrigger = true;
        apronCol.size = apronRect.size;
        var apronFs = apronGo.AddComponent<FloorSurface>();
        apronFs.surfaceType = SurfaceType.Concrete;
        apronFs.priority = 2;

        CreateSolidProp(parent, "ParkedTricycle", LoadBarangaySprite("Exterior/tricycle_topdown"),
            new Rect(-1.52f, -2.10f, 0.38f, 0.50f), "Item", 0, wallLayer);

        Sprite trashSprite = LoadBarangaySprite("Exterior/trash_scatter");
        Vector2[] trashPos =
        {
            new Vector2(-1.46f, 1.25f),
            new Vector2(-0.52f, 2.05f),
            new Vector2(-0.54f, -1.45f),
            new Vector2(-1.44f, -2.65f)
        };
        for (int i = 0; i < trashPos.Length; i++)
        {
            var tr = CreateSpriteRect(parent, $"TrashScatter_{i}", trashSprite,
                new Rect(trashPos[i].x - 0.12f, trashPos[i].y - 0.12f, 0.24f, 0.24f), "Floor", -2, 0);
            tr.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }

        // Road + Compound outdoor puddles (inactive by default; WeatherManager activates during Rain)
        Texture2D puddleTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BARANGAY_SPRITE_PREFIX}Weather/puddle_ripples_sheet.png");
        Sprite defaultPuddleSprite = LoadBarangaySprite("Weather/puddle_ripples_sheet");
        if (puddleTex != null)
        {
            defaultPuddleSprite = Sprite.Create(puddleTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
        }
        Vector2[] puddlePos =
        {
            new Vector2(-0.46f, 2.25f), new Vector2(-1.46f, 1.85f), new Vector2(-0.52f, 0.85f),
            new Vector2(-1.48f, -0.25f), new Vector2(-0.52f, -1.35f), new Vector2(-1.42f, -2.45f),
            new Vector2(0.65f, 0.48f), new Vector2(1.85f, 1.80f), new Vector2(2.35f, -0.68f),
            new Vector2(2.45f, -2.65f), new Vector2(3.45f, 2.45f), new Vector2(5.10f, 1.32f),
            new Vector2(6.40f, 1.32f), new Vector2(7.50f, 0.58f), new Vector2(4.95f, 2.95f),
            new Vector2(6.95f, 2.95f)
        };
        for (int i = 0; i < puddlePos.Length; i++)
        {
            var pg = CreateSpriteRect(parent, $"WeatherPuddle_{i}", defaultPuddleSprite,
                new Rect(puddlePos[i].x - 0.14f, puddlePos[i].y - 0.10f, 0.28f, 0.20f), "Floor", -1, 0);
            pg.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, 0.8f);
            pg.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            pg.SetActive(false);
        }

        // 6 Streetlamps along the West Road
        Sprite lampSpr = LoadBarangaySprite("Exterior/lamp_post");
        (Vector2 pos, float bulbOffsetX)[] lamps =
        {
            (new Vector2(-1.60f,  2.65f),  0.03f),
            (new Vector2(-0.40f,  1.55f), -0.03f),
            (new Vector2(-1.60f,  0.45f),  0.03f),
            (new Vector2(-0.40f, -0.65f), -0.03f),
            (new Vector2(-1.60f, -1.75f),  0.03f),
            (new Vector2(-0.40f, -2.75f), -0.03f)
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

        Sprite wallSpr = LoadBarangaySprite("House/wall_concrete_top");
        AddWallSegment(parent, "StreetBoundary_North", new Rect(-1.75f, 3.42f, 1.75f, 0.08f), wallLayer, wallSpr, castShadows: false);
        AddWallSegment(parent, "StreetBoundary_South", new Rect(-1.75f, -3.18f, 1.75f, 0.08f), wallLayer, wallSpr, castShadows: false);
        AddWallSegment(parent, "StreetBoundary_West", new Rect(-1.76f, -3.18f, 0.08f, 6.68f), wallLayer, wallSpr, castShadows: false);
    }

    // -----------------------------------------------------------------------
    // Sector 2: Perimeter Walls, Base Walkable Turf & Single-Sprite FrontGate
    // -----------------------------------------------------------------------
    private static void BuildPerimeterAndFrontGate(
        Transform compoundRoot, Transform wallsRoot, Transform doorsRoot, int wallLayer, int interactLayer, int groundLayer)
    {
        Rect compoundRect = new Rect(0.00f, -3.10f, 8.50f, 6.50f);
        CreateFloorArea(compoundRoot, "CemeteryGrassBase", LoadCemeterySprite("cemetery_grass"),
            compoundRect, SurfaceType.Grass, priority: 1, sortOrder: -4, groundLayer: groundLayer, walkable: true);

        Sprite wallSpr = LoadBarangaySprite("House/wall_concrete_top");
        float wt = 0.06f;
        AddWallSegment(wallsRoot, "Perim_North", new Rect(-wt * 0.5f, 3.40f - wt * 0.5f, 8.50f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Perim_South", new Rect(-wt * 0.5f, -3.10f - wt * 0.5f, 8.50f + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Perim_East", new Rect(8.50f - wt * 0.5f, -3.10f, wt, 6.50f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Perim_West_South", new Rect(-wt * 0.5f, -3.10f, wt, 2.97f), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Perim_West_North", new Rect(-wt * 0.5f, 0.37f, wt, 3.03f), wallLayer, wallSpr);

        // Single-sprite wrought-iron FrontGate (y=[-0.13..+0.37], centered at (0.00, 0.12), south hinge at (0, -0.25))
        Sprite gateSpr = LoadCemeterySprite("gate_iron");
        CreateNetworkDoor(
            doorsRoot, "FrontGate", new Vector2(0.00f, 0.12f), verticalWall: true,
            closedSpr: gateSpr, openSpr: gateSpr,
            length: 0.50f, thick: 0.08f,
            isFrontDoor: true, requiresKey: true, isHidingSpot: false,
            wallLayer: wallLayer, interactLayer: interactLayer, swingDeg: -90f, swapSprite: false);
    }

    // -----------------------------------------------------------------------
    // Sector 3: Main Stone Avenues & Central Cross Plaza
    // -----------------------------------------------------------------------
    private static void BuildMainAvenuesAndCrossPlaza(
        Transform compoundRoot, Transform landmarksRoot, Transform ghostMarkers, Transform lightSys,
        int wallLayer, int interactLayer, int groundLayer)
    {
        Sprite stoneSpr = LoadCemeterySprite("cemetery_path_stone");
        CreateFloorArea(compoundRoot, "Avenue_EastWest", stoneSpr, new Rect(0.02f, -0.16f, 8.33f, 0.56f),
            SurfaceType.Concrete, priority: 2, sortOrder: -3, groundLayer: groundLayer, walkable: true);
        CreateFloorArea(compoundRoot, "Avenue_NorthSouth", stoneSpr, new Rect(3.80f, -2.95f, 0.56f, 6.20f),
            SurfaceType.Concrete, priority: 2, sortOrder: -3, groundLayer: groundLayer, walkable: true);

        Rect plazaRect = new Rect(3.53f, -0.43f, 1.10f, 1.10f);
        CreateFloorArea(compoundRoot, "CentralCrossPlaza_Floor", stoneSpr, plazaRect,
            SurfaceType.Concrete, priority: 2, sortOrder: -2, groundLayer: groundLayer, walkable: true);

        CreateSolidShadowProp(landmarksRoot, "CentralCrossMonument", LoadCemeterySprite("cemetery_cross_monument"),
            new Rect(3.92f, -0.04f, 0.32f, 0.32f), "Item", 1, wallLayer);

        Sprite benchSpr = LoadCemeterySprite("cemetery_bench");
        CreateSolidProp(landmarksRoot, "PlazaBench_W", benchSpr, new Rect(3.57f, 0.45f, 0.32f, 0.14f), "Item", 0, wallLayer);
        CreateSolidProp(landmarksRoot, "PlazaBench_E", benchSpr, new Rect(4.27f, 0.45f, 0.32f, 0.14f), "Item", 0, wallLayer);
        CreateHideZone(landmarksRoot, "HideZone_CentralCross", new Rect(3.91f, 0.34f, 0.34f, 0.22f));

        // 6 Interactive Lamp Posts along the inner stone avenues (connected to FuseBox + lit SafeZones)
        (string name, Vector2 pos, float bulbOffsetX)[] avenueLamps =
        {
            ("AvenueLamp_West_N", new Vector2(1.35f,  0.44f),  0.03f),
            ("AvenueLamp_West_S", new Vector2(2.55f, -0.20f), -0.03f),
            ("AvenueLamp_East_N", new Vector2(5.65f,  0.44f),  0.03f),
            ("AvenueLamp_East_S", new Vector2(7.15f, -0.20f), -0.03f),
            ("AvenueLamp_North",  new Vector2(3.74f,  1.95f),  0.03f),
            ("AvenueLamp_South",  new Vector2(4.42f, -1.85f), -0.03f)
        };
        for (int i = 0; i < avenueLamps.Length; i++)
        {
            CreateInteractiveCemeteryLampPost(
                lightSys, avenueLamps[i].name, avenueLamps[i].pos, avenueLamps[i].bulbOffsetX, interactLayer);
        }

        CreateGhostRoomMarker(ghostMarkers, "CentralCrossPlaza", plazaRect);
    }

    // -----------------------------------------------------------------------
    // Sector 4: Caretaker's Bantay Shed, FuseBox, Yard & Flower Stall
    // -----------------------------------------------------------------------
    private static void BuildBantayShedAndYard(
        Transform buildingsRoot, Transform wallsRoot, Transform doorsRoot, Transform landmarksRoot,
        Transform ghostMarkers, Transform lightSys, int wallLayer, int interactLayer, int groundLayer)
    {
        Rect shedRect = new Rect(0.22f, -1.75f, 1.45f, 1.10f);
        CreateFloorArea(buildingsRoot, "Floor_BantayShed", LoadBarangaySprite("House/floor_garage_concrete"),
            shedRect, SurfaceType.Concrete, priority: 3, sortOrder: -2, groundLayer: groundLayer, walkable: true);

        Sprite wallSpr = LoadBarangaySprite("House/wall_concrete_top");
        Sprite doorClosed = LoadBarangaySprite("House/door_wood_closed");
        Sprite doorOpen = LoadBarangaySprite("House/door_wood_open");
        Sprite winSpr = LoadBarangaySprite("House/window_frame_top");
        float wt = 0.055f;

        AddWallSegment(wallsRoot, "Bantay_South", new Rect(shedRect.xMin - wt * 0.5f, shedRect.yMin - wt * 0.5f, shedRect.width + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bantay_West", new Rect(shedRect.xMin - wt * 0.5f, shedRect.yMin, wt, shedRect.height), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bantay_East", new Rect(shedRect.xMax - wt * 0.5f, shedRect.yMin, wt, shedRect.height), wallLayer, wallSpr);

        float doorX = 0.95f;
        float halfGap = 0.22f;
        AddWallSegment(wallsRoot, "Bantay_North_L", new Rect(shedRect.xMin - wt * 0.5f, shedRect.yMax - wt * 0.5f, (doorX - halfGap) - (shedRect.xMin - wt * 0.5f), wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, "Bantay_North_R", new Rect(doorX + halfGap, shedRect.yMax - wt * 0.5f, (shedRect.xMax + wt * 0.5f) - (doorX + halfGap), wt), wallLayer, wallSpr);
        CreateNetworkDoor(doorsRoot, "BantayShed_Door", new Vector2(doorX, shedRect.yMax), verticalWall: false,
            doorClosed, doorOpen, 0.42f, 0.065f, isFrontDoor: false, requiresKey: false, isHidingSpot: false,
            wallLayer: wallLayer, interactLayer: interactLayer, swingDeg: -90f, swapSprite: false);

        CreateWindowAccent(wallsRoot, "Win_BantayEast", winSpr, new Vector2(shedRect.xMax, -1.20f), vertical: true);
        CreateWindowAccent(wallsRoot, "Win_BantaySouth", winSpr, new Vector2(0.95f, shedRect.yMin), vertical: false);

        CreateEnclosedBuildingRoof(buildingsRoot, "BantayShed_Roof", LoadBarangaySprite("House/roof_main_gi"), shedRect);

        // FuseBox on inside west wall of Bantay Shed
        GameObject fbGo = new GameObject("FuseBox");
        fbGo.transform.SetParent(buildingsRoot, false);
        fbGo.transform.position = new Vector3(0.36f, -1.18f, 0f);
        fbGo.layer = interactLayer;
        Sprite fbSpr = LoadBarangaySprite("House/Furniture/furn_fusebox");
        var fbSr = fbGo.AddComponent<SpriteRenderer>();
        fbSr.sprite = fbSpr;
        fbSr.sortingLayerName = "Item";
        fbSr.sortingOrder = 1;
        if (fbSpr != null)
        {
            fbGo.transform.localScale = new Vector3(0.14f / fbSpr.bounds.size.x, 0.14f / fbSpr.bounds.size.y, 1f);
        }
        fbGo.AddComponent<BoxCollider2D>().isTrigger = true;
        fbGo.AddComponent<NetworkObject>();
        var fb = fbGo.AddComponent<FuseBox>();
        var fbSo = new SerializedObject(fb);
        var maxLightsProp = fbSo.FindProperty("maxLightsBeforeOverload");
        if (maxLightsProp != null) maxLightsProp.intValue = 8;
        fbSo.ApplyModifiedPropertiesWithoutUndo();
        fbGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Bantay Shed Desk & HideZone + Switch/SafeZone + GhostRoomMarker
        CreateSolidProp(buildingsRoot, "Bantay_Desk", LoadBarangaySprite("House/Furniture/furn_desk"),
            new Rect(1.22f, -1.60f, 0.34f, 0.20f), "Item", 0, wallLayer);
        CreateHideZone(buildingsRoot, "HideZone_BantayShedDesk", new Rect(1.18f, -1.32f, 0.32f, 0.22f));

        CreateBuildingLightingAndSwitch(lightSys, "BantayShed", shedRect, new Vector2(0.76f, -0.74f),
            interactLayer, createSafeZone: true, lightOuterRadius: 1.25f);
        CreateGhostRoomMarker(ghostMarkers, "BantayShed", shedRect);

        // Caretaker South Yard: Poso pump & Kariton handcart HideZone
        CreateSolidProp(landmarksRoot, "Caretaker_Poso", LoadCemeterySprite("cemetery_poso"),
            new Rect(0.44f, -2.36f, 0.22f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(landmarksRoot, "Caretaker_Handcart", LoadCemeterySprite("cemetery_handcart"),
            new Rect(1.28f, -2.46f, 0.38f, 0.26f), "Item", 0, wallLayer);
        CreateHideZone(landmarksRoot, "HideZone_CaretakerCart", new Rect(1.28f, -2.14f, 0.36f, 0.22f));

        // Gate Flower & Candle Stall (NW near entrance)
        CreateSolidProp(landmarksRoot, "Gate_FlowerStall", LoadCemeterySprite("cemetery_flower_stall"),
            new Rect(0.35f, 0.78f, 0.62f, 0.34f), "Item", 0, wallLayer);
        CreateHideZone(landmarksRoot, "HideZone_FlowerStall", new Rect(0.42f, 1.16f, 0.46f, 0.24f));
    }

    // -----------------------------------------------------------------------
    // Sector 5: Classic Grass Burials, Balete Grove & Outdoor West Floodlights
    // -----------------------------------------------------------------------
    private static void BuildGrassBurialsAndBalete(
        Transform landmarksRoot, Transform ghostMarkers, Transform lightSys, int wallLayer, int interactLayer)
    {
        Sprite g1 = LoadCemeterySprite("cemetery_gravestone_1");
        Sprite g2 = LoadCemeterySprite("cemetery_gravestone_2");
        Sprite lapida = LoadCemeterySprite("cemetery_lapida_flat");
        Sprite tomb = LoadCemeterySprite("cemetery_tomb_raised");
        Sprite treeSpr = LoadCemeterySprite("cemetery_balete_tree");
        Sprite leafSpr = LoadCemeterySprite("cemetery_trash_leaves");

        Vector2[] headstones =
        {
            // NW Grass Plots
            new Vector2(0.48f, 2.00f), new Vector2(1.12f, 2.00f), new Vector2(1.76f, 2.00f),
            new Vector2(0.48f, 2.82f), new Vector2(1.12f, 2.82f), new Vector2(1.76f, 2.82f),
            new Vector2(1.36f, 1.15f), new Vector2(2.30f, 1.05f), new Vector2(3.20f, 1.05f),
            new Vector2(3.25f, 2.85f),
            // SW Grass Plots
            new Vector2(2.22f, -0.78f), new Vector2(3.18f, -0.78f),
            new Vector2(2.22f, -1.60f), new Vector2(3.18f, -1.60f),
            new Vector2(2.22f, -2.55f), new Vector2(3.26f, -2.55f)
        };
        for (int i = 0; i < headstones.Length; i++)
        {
            Vector2 p = headstones[i];
            Sprite spr = (i % 2 == 0) ? g1 : g2;
            CreateSolidShadowProp(landmarksRoot, $"Gravestone_{i}", spr,
                new Rect(p.x - 0.11f, p.y - 0.11f, 0.22f, 0.22f), "Item", 0, wallLayer);
        }

        Vector2[] tombs =
        {
            new Vector2(0.80f, 2.40f), new Vector2(1.44f, 2.40f), new Vector2(2.24f, 2.82f),
            new Vector2(2.72f, -1.18f), new Vector2(2.72f, -0.78f)
        };
        for (int i = 0; i < tombs.Length; i++)
        {
            Vector2 p = tombs[i];
            CreateSolidShadowProp(landmarksRoot, $"RaisedTomb_{i}", tomb,
                new Rect(p.x - 0.14f, p.y - 0.18f, 0.28f, 0.36f), "Item", 0, wallLayer);
        }

        Vector2[] lapidas =
        {
            new Vector2(0.48f, 1.62f), new Vector2(1.12f, 1.62f), new Vector2(1.76f, 1.62f),
            new Vector2(2.76f, 1.05f), new Vector2(3.25f, 1.85f),
            new Vector2(2.22f, -1.16f), new Vector2(3.18f, -1.16f), new Vector2(3.18f, -2.02f)
        };
        for (int i = 0; i < lapidas.Length; i++)
        {
            Vector2 p = lapidas[i];
            CreateNonBlockingProp(landmarksRoot, $"Lapida_{i}", lapida,
                new Rect(p.x - 0.14f, p.y - 0.10f, 0.28f, 0.20f), "Floor", -1);
        }

        // Gnarled Balete Tree (North Grove) & Acacia Tree (South Grove)
        CreateTreeLandmark(landmarksRoot, "BaleteTree_North", treeSpr, new Vector2(2.86f, 2.26f), 0.88f, wallLayer);
        CreateHideZone(landmarksRoot, "HideZone_BaleteNorth", new Rect(2.68f, 1.80f, 0.36f, 0.24f));

        CreateTreeLandmark(landmarksRoot, "AcaciaTree_South", treeSpr, new Vector2(2.84f, -2.16f), 0.84f, wallLayer);
        CreateHideZone(landmarksRoot, "HideZone_AcaciaSouth", new Rect(2.66f, -1.86f, 0.36f, 0.24f));

        Vector2[] leafClusters =
        {
            new Vector2(2.55f, 2.00f), new Vector2(1.20f, 2.65f),
            new Vector2(2.60f, -2.00f), new Vector2(0.92f, -2.05f),
            new Vector2(3.98f, 1.45f), new Vector2(6.35f, 2.95f)
        };
        for (int i = 0; i < leafClusters.Length; i++)
        {
            Vector2 p = leafClusters[i];
            CreateNonBlockingProp(landmarksRoot, $"LeafScatter_{i}", leafSpr,
                new Rect(p.x - 0.18f, p.y - 0.18f, 0.36f, 0.36f), "Floor", -1);
        }

        CreateGhostRoomMarker(ghostMarkers, "NorthGrassBurial", new Rect(0.24f, 0.55f, 1.85f, 2.55f));
        CreateGhostRoomMarker(ghostMarkers, "BaleteGrove", new Rect(2.15f, 0.55f, 1.50f, 2.55f));
        CreateGhostRoomMarker(ghostMarkers, "SouthwestGrassPlots", new Rect(1.98f, -2.95f, 1.68f, 2.60f));
    }

    // -----------------------------------------------------------------------
    // Sector 6: Multi-Tiered Nitso Maze (NE)
    // -----------------------------------------------------------------------
    private static void BuildNitsoMaze(
        Transform compoundRoot, Transform landmarksRoot, Transform ghostMarkers, Transform lightSys,
        int wallLayer, int interactLayer, int groundLayer)
    {
        Rect nitsoGround = new Rect(4.45f, 0.48f, 3.93f, 2.80f);
        CreateFloorArea(compoundRoot, "NitsoDirtGround", LoadCemeterySprite("cemetery_dirt_alley"),
            nitsoGround, SurfaceType.Pavement, priority: 2, sortOrder: -3, groundLayer: groundLayer, walkable: true);

        Sprite nitsoSpr = LoadCemeterySprite("cemetery_nitso_block");
        (string name, Rect rect)[] blocks =
        {
            ("Nitso_S_West",   new Rect(4.80f, 0.88f, 1.30f, 0.28f)),
            ("Nitso_S_East",   new Rect(6.72f, 0.88f, 1.32f, 0.28f)),
            ("Nitso_M_West",   new Rect(4.80f, 1.70f, 0.88f, 0.28f)),
            ("Nitso_M_Center", new Rect(6.24f, 1.70f, 0.84f, 0.28f)),
            ("Nitso_M_East",   new Rect(7.62f, 1.70f, 0.48f, 0.28f)),
            ("Nitso_N_West",   new Rect(4.80f, 2.52f, 1.30f, 0.28f)),
            ("Nitso_N_East",   new Rect(6.72f, 2.52f, 1.32f, 0.28f))
        };
        foreach (var b in blocks)
        {
            CreateSolidShadowProp(landmarksRoot, b.name, nitsoSpr, b.rect, "Wall", 0, wallLayer);
        }

        CreateHideZone(landmarksRoot, "HideZone_NitsoAlcove_W", new Rect(4.46f, 1.24f, 0.30f, 0.32f));
        CreateHideZone(landmarksRoot, "HideZone_NitsoAlcove_C", new Rect(6.24f, 2.04f, 0.34f, 0.30f));
        CreateHideZone(landmarksRoot, "HideZone_NitsoAlcove_E", new Rect(7.98f, 1.24f, 0.32f, 0.32f));

        CreateGhostRoomMarker(ghostMarkers, "NitsoMaze_West", new Rect(4.50f, 0.55f, 1.90f, 2.60f));
        CreateGhostRoomMarker(ghostMarkers, "NitsoMaze_East", new Rect(6.45f, 0.55f, 1.85f, 2.60f));
    }

    // -----------------------------------------------------------------------
    // Sector 7: Three Enclosed Family Mausoleums (SE)
    // -----------------------------------------------------------------------
    private static void BuildFamilyMausoleums(
        Transform compoundRoot, Transform buildingsRoot, Transform wallsRoot, Transform doorsRoot,
        Transform ghostMarkers, Transform lightSys, int wallLayer, int interactLayer, int groundLayer)
    {
        Sprite stoneSpr = LoadCemeterySprite("cemetery_path_stone");
        CreateFloorArea(compoundRoot, "SE_Lane_North", stoneSpr, new Rect(4.36f, -0.28f, 4.02f, 0.20f),
            SurfaceType.Concrete, priority: 2, sortOrder: -3, groundLayer: groundLayer, walkable: true);
        CreateFloorArea(compoundRoot, "SE_Lane_CenterNS", stoneSpr, new Rect(6.20f, -1.78f, 0.35f, 1.62f),
            SurfaceType.Concrete, priority: 2, sortOrder: -3, groundLayer: groundLayer, walkable: true);
        CreateFloorArea(compoundRoot, "SE_Lane_MiddleEW", stoneSpr, new Rect(4.36f, -1.78f, 4.02f, 0.30f),
            SurfaceType.Concrete, priority: 2, sortOrder: -3, groundLayer: groundLayer, walkable: true);

        Sprite wallSpr = LoadBarangaySprite("House/wall_concrete_top");
        Sprite gateSpr = LoadCemeterySprite("gate_iron");
        Sprite woodClosed = LoadBarangaySprite("House/door_wood_closed");
        Sprite woodOpen = LoadBarangaySprite("House/door_wood_open");
        Sprite altarSpr = LoadCemeterySprite("cemetery_altar");
        Sprite benchSpr = LoadCemeterySprite("cemetery_bench");
        Sprite roofConc = LoadCemeterySprite("cemetery_roof_concrete");
        Sprite roofGi = LoadBarangaySprite("House/roof_main_gi");

        // 1. Mausoleum_DelosSantos (iron entry gate)
        Rect m1 = new Rect(4.55f, -1.48f, 1.65f, 1.20f);
        BuildMausoleumShell(buildingsRoot, wallsRoot, doorsRoot, "Mausoleum_DelosSantos", m1,
            northDoorX: 5.38f, doorName: "Mausoleum1_Gate",
            floorSpr: LoadCemeterySprite("cemetery_mausoleum_tile"),
            roofSpr: roofConc, wallSpr: wallSpr,
            doorClosed: gateSpr, doorOpen: gateSpr,
            wallLayer: wallLayer, interactLayer: interactLayer, groundLayer: groundLayer);
        CreateSolidProp(buildingsRoot, "Mausoleum1_Altar", altarSpr, new Rect(5.16f, -1.40f, 0.44f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(buildingsRoot, "Mausoleum1_Bench", benchSpr, new Rect(4.66f, -0.98f, 0.34f, 0.16f), "Item", 0, wallLayer);
        CreateHideZone(buildingsRoot, "HideZone_Mausoleum1", new Rect(5.76f, -1.36f, 0.34f, 0.28f));
        CreateBuildingLightingAndSwitch(lightSys, "Mausoleum1", m1, new Vector2(4.72f, -0.40f),
            interactLayer, createSafeZone: true, lightOuterRadius: 1.35f, safeZoneNameOverride: "Mausoleum_DelosSantos_SafeZone");
        CreateGhostRoomMarker(ghostMarkers, "Mausoleum_DelosSantos", m1);

        // 2. Mausoleum_Villanueva (wood door + GI roof)
        Rect m2 = new Rect(6.55f, -1.48f, 1.75f, 1.20f);
        BuildMausoleumShell(buildingsRoot, wallsRoot, doorsRoot, "Mausoleum_Villanueva", m2,
            northDoorX: 7.42f, doorName: "Mausoleum2_Door",
            floorSpr: LoadBarangaySprite("House/floor_bathroom_tile"),
            roofSpr: roofGi, wallSpr: wallSpr,
            doorClosed: woodClosed, doorOpen: woodOpen,
            wallLayer: wallLayer, interactLayer: interactLayer, groundLayer: groundLayer);
        CreateSolidProp(buildingsRoot, "Mausoleum2_Altar", altarSpr, new Rect(7.20f, -1.40f, 0.44f, 0.22f), "Item", 0, wallLayer);
        CreateSolidProp(buildingsRoot, "Mausoleum2_Bench", benchSpr, new Rect(6.66f, -0.98f, 0.34f, 0.16f), "Item", 0, wallLayer);
        CreateHideZone(buildingsRoot, "HideZone_Mausoleum2", new Rect(7.84f, -1.36f, 0.34f, 0.28f));
        CreateBuildingLightingAndSwitch(lightSys, "Mausoleum2", m2, new Vector2(6.72f, -0.40f),
            interactLayer, createSafeZone: true, lightOuterRadius: 1.35f, safeZoneNameOverride: "Mausoleum_Villanueva_SafeZone");
        CreateGhostRoomMarker(ghostMarkers, "Mausoleum_Villanueva", m2);

        // 3. Mausoleum_AbandonedCrypt (wood door + pillars + concrete roof)
        Rect m3 = new Rect(5.20f, -2.96f, 2.35f, 1.18f);
        BuildMausoleumShell(buildingsRoot, wallsRoot, doorsRoot, "Mausoleum_AbandonedCrypt", m3,
            northDoorX: 6.38f, doorName: "Mausoleum3_Door",
            floorSpr: LoadBarangaySprite("House/floor_porch_tile"),
            roofSpr: roofConc, wallSpr: wallSpr,
            doorClosed: woodClosed, doorOpen: woodOpen,
            wallLayer: wallLayer, interactLayer: interactLayer, groundLayer: groundLayer);
        CreateSolidProp(buildingsRoot, "Mausoleum3_Altar", altarSpr, new Rect(6.16f, -2.86f, 0.44f, 0.22f), "Item", 0, wallLayer);
        Sprite pillarSpr = LoadBarangaySprite("House/Furniture/furn_pillar");
        CreateSolidProp(buildingsRoot, "Mausoleum3_Pillar_W", pillarSpr, new Rect(5.52f, -2.45f, 0.20f, 0.20f), "Item", 0, wallLayer);
        CreateSolidProp(buildingsRoot, "Mausoleum3_Pillar_E", pillarSpr, new Rect(7.04f, -2.45f, 0.20f, 0.20f), "Item", 0, wallLayer);
        CreateHideZone(buildingsRoot, "HideZone_Mausoleum3", new Rect(7.10f, -2.84f, 0.34f, 0.26f));
        CreateBuildingLightingAndSwitch(lightSys, "Mausoleum3", m3, new Vector2(5.38f, -1.90f),
            interactLayer, createSafeZone: true, lightOuterRadius: 1.55f, safeZoneNameOverride: "Mausoleum_AbandonedCrypt_SafeZone");
        CreateGhostRoomMarker(ghostMarkers, "Mausoleum_AbandonedCrypt", m3);
    }

    private static void BuildMausoleumShell(
        Transform buildingsRoot, Transform wallsRoot, Transform doorsRoot, string name, Rect rect,
        float northDoorX, string doorName, Sprite floorSpr, Sprite roofSpr, Sprite wallSpr,
        Sprite doorClosed, Sprite doorOpen, int wallLayer, int interactLayer, int groundLayer)
    {
        CreateFloorArea(buildingsRoot, $"Floor_{name}", floorSpr, rect,
            SurfaceType.Concrete, priority: 3, sortOrder: -2, groundLayer: groundLayer, walkable: true);

        float wt = 0.055f;
        AddWallSegment(wallsRoot, $"{name}_South", new Rect(rect.xMin - wt * 0.5f, rect.yMin - wt * 0.5f, rect.width + wt, wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, $"{name}_West", new Rect(rect.xMin - wt * 0.5f, rect.yMin, wt, rect.height), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, $"{name}_East", new Rect(rect.xMax - wt * 0.5f, rect.yMin, wt, rect.height), wallLayer, wallSpr);

        float halfGap = 0.22f;
        AddWallSegment(wallsRoot, $"{name}_North_L", new Rect(rect.xMin - wt * 0.5f, rect.yMax - wt * 0.5f, (northDoorX - halfGap) - (rect.xMin - wt * 0.5f), wt), wallLayer, wallSpr);
        AddWallSegment(wallsRoot, $"{name}_North_R", new Rect(northDoorX + halfGap, rect.yMax - wt * 0.5f, (rect.xMax + wt * 0.5f) - (northDoorX + halfGap), wt), wallLayer, wallSpr);

        CreateNetworkDoor(doorsRoot, doorName, new Vector2(northDoorX, rect.yMax), verticalWall: false,
            doorClosed, doorOpen, 0.42f, 0.065f, isFrontDoor: false, requiresKey: false, isHidingSpot: false,
            wallLayer: wallLayer, interactLayer: interactLayer, swingDeg: -90f, swapSprite: false);

        CreateEnclosedBuildingRoof(buildingsRoot, $"{name}_Roof", roofSpr, rect);
    }

    // -----------------------------------------------------------------------
    // Sector 8: 22 Flickering Cemetery Candles & Interactive Ghost Props
    // -----------------------------------------------------------------------
    private static void BuildCandlesAndProps(
        Transform envRoot, Transform candlesRoot, Transform propsRoot, int interactLayer)
    {
        GameObject mgrGo = new GameObject("CemeteryCandleManager");
        mgrGo.transform.SetParent(envRoot, false);
        mgrGo.AddComponent<NetworkObject>();
        mgrGo.AddComponent<CemeteryCandleManager>();

        Texture2D candleTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CEMETERY_SPRITE_PREFIX}candle_sheet.png");
        Sprite candlePreview = null;
        if (candleTex != null)
        {
            candlePreview = Sprite.Create(candleTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
        }

        Vector2[] candlePositions =
        {
            // Central Cross Plaza ring (4)
            new Vector2(3.86f, 0.22f), new Vector2(4.30f, 0.22f),
            new Vector2(3.86f, -0.02f), new Vector2(4.30f, -0.02f),
            // Gate Flower Stall & Bantay Shed (3)
            new Vector2(0.54f, 0.92f), new Vector2(0.84f, 0.92f), new Vector2(1.32f, -1.52f),
            // Grass Burials & Balete Grove (6)
            new Vector2(0.48f, 1.84f), new Vector2(1.12f, 2.66f), new Vector2(1.76f, 1.84f),
            new Vector2(2.74f, 2.02f), new Vector2(2.22f, -0.94f), new Vector2(3.18f, -1.76f),
            // Nitso Maze Ledges (5)
            new Vector2(5.12f, 0.84f), new Vector2(7.05f, 0.84f),
            new Vector2(5.18f, 1.66f), new Vector2(6.62f, 1.66f), new Vector2(7.40f, 2.48f),
            // Family Mausoleums Altars (4)
            new Vector2(5.26f, -1.28f), new Vector2(5.50f, -1.28f),
            new Vector2(7.42f, -1.28f), new Vector2(6.38f, -2.74f)
        };

        for (int i = 0; i < candlePositions.Length; i++)
        {
            Vector2 p = candlePositions[i];
            GameObject cGo = new GameObject($"Candle_{i}");
            cGo.transform.SetParent(candlesRoot, false);
            cGo.transform.position = new Vector3(p.x, p.y, 0f);
            cGo.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
            cGo.layer = interactLayer;

            var sr = cGo.AddComponent<SpriteRenderer>();
            sr.sprite = candlePreview;
            sr.sortingLayerName = "Item";
            sr.sortingOrder = 3;

            var col = cGo.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;
            col.isTrigger = true;

            GameObject lGo = new GameObject($"CandleLight_{i}");
            lGo.transform.SetParent(cGo.transform, false);
            lGo.transform.localPosition = Vector3.zero;
            var light = lGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = new Color(1.0f, 0.72f, 0.34f, 1f);
            light.intensity = 0.34f;
            light.pointLightInnerRadius = 0.02f;
            light.pointLightOuterRadius = 0.38f;
            light.shadowsEnabled = false;
            AssignAllSortingLayers(light);

            var candle = cGo.AddComponent<CemeteryCandle>();
            candle.Configure(i, light, startLit: true);
            cGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }

        (string name, string display, string sheet, Vector2 pos, bool breakable, string sfx)[] props =
        {
            ("Prop_FlowerStall_Vase", "Flower Vase", "CemeteryMap/Props/prop_vase_sheet", new Vector2(0.46f, 0.86f), true, "prop_shatter_glass"),
            ("Prop_FlowerStall_CandleJar", "Vigil Jar", "CemeteryMap/Props/prop_candle_jar_sheet", new Vector2(0.76f, 0.86f), true, "prop_shatter_glass"),
            ("Prop_Bantay_Mug", "Coffee Mug", "Props/prop_cup_sheet", new Vector2(1.44f, -1.52f), true, "prop_shatter_glass"),
            ("Prop_Bantay_PaintCan", "Paint Can", "Props/prop_paint_can_sheet", new Vector2(0.46f, -1.56f), false, "prop_knock_wood"),
            ("Prop_Yard_WateringCan", "Watering Can", "CemeteryMap/Props/prop_watering_can_sheet", new Vector2(0.76f, -2.35f), false, "prop_knock_wood"),
            ("Prop_NorthGrass_Vase", "Grave Vase", "CemeteryMap/Props/prop_vase_sheet", new Vector2(1.42f, 2.02f), true, "prop_shatter_glass"),
            ("Prop_Balete_CandleJar", "Vigil Jar", "CemeteryMap/Props/prop_candle_jar_sheet", new Vector2(2.60f, 1.98f), true, "prop_shatter_glass"),
            ("Prop_Nitso_CandleJar_W", "Niche Candle Jar", "CemeteryMap/Props/prop_candle_jar_sheet", new Vector2(5.48f, 0.80f), true, "prop_shatter_glass"),
            ("Prop_Nitso_Vase_E", "Niche Vase", "CemeteryMap/Props/prop_vase_sheet", new Vector2(7.65f, 1.62f), true, "prop_shatter_glass"),
            ("Prop_Nitso_WateringCan", "Watering Can", "CemeteryMap/Props/prop_watering_can_sheet", new Vector2(6.48f, 2.46f), false, "prop_knock_wood"),
            ("Prop_Mausoleum1_Vase", "Altar Vase", "CemeteryMap/Props/prop_vase_sheet", new Vector2(5.12f, -1.24f), true, "prop_shatter_glass"),
            ("Prop_Mausoleum2_CandleJar", "Altar Candle Jar", "CemeteryMap/Props/prop_candle_jar_sheet", new Vector2(7.58f, -1.24f), true, "prop_shatter_glass"),
            ("Prop_Mausoleum2_Frame", "Family Portrait", "Props/prop_picture_frame_sheet", new Vector2(6.88f, -1.32f), true, "prop_shatter_glass"),
            ("Prop_Mausoleum3_Bottle", "Old Wine Bottle", "Props/prop_glass_bottle_sheet", new Vector2(5.96f, -2.72f), true, "prop_shatter_glass")
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

    // -----------------------------------------------------------------------
    // InvestigationZone, GhostNode Graph, WeatherManager & NavMesh Verification
    // -----------------------------------------------------------------------
    private static void BuildInvestigationZone(Transform compoundRoot)
    {
        GameObject izGo = new GameObject("CemeteryInvestigationZone");
        izGo.transform.SetParent(compoundRoot, false);
        Rect r = new Rect(0.00f, -3.10f, 8.50f, 6.50f);
        izGo.transform.position = new Vector3(r.center.x, r.center.y, 0f);

        var col = izGo.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = r.size;

        izGo.AddComponent<InvestigationZone>();
        izGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static void BuildGhostNodeGraph(Transform pathSys)
    {
        if (pathSys == null) return;

        Vector2[] pts =
        {
            /*0*/  new Vector2(0.40f,  0.12f),  // Inside FrontGate threshold
            /*1*/  new Vector2(1.25f,  0.12f),  // West Avenue
            /*2*/  new Vector2(2.60f,  0.12f),  // Mid-West Avenue
            /*3*/  new Vector2(3.70f,  0.12f),  // Cross Plaza West
            /*4*/  new Vector2(4.08f,  0.42f),  // Cross Plaza North
            /*5*/  new Vector2(4.46f,  0.12f),  // Cross Plaza East
            /*6*/  new Vector2(4.08f, -0.30f),  // Cross Plaza South
            /*7*/  new Vector2(5.40f,  0.12f),  // East Avenue Mid
            /*8*/  new Vector2(7.42f,  0.12f),  // East Avenue Far
            /*9*/  new Vector2(0.95f, -0.35f),  // Outside Bantay Shed
            /*10*/ new Vector2(0.95f, -1.15f),  // Inside Bantay Shed
            /*11*/ new Vector2(1.85f, -0.35f),  // Caretaker East Path
            /*12*/ new Vector2(1.85f, -2.15f),  // Caretaker Yard East
            /*13*/ new Vector2(0.95f, -2.15f),  // Caretaker Yard Poso/Cart
            /*14*/ new Vector2(0.75f,  1.45f),  // NW Grass South
            /*15*/ new Vector2(0.75f,  2.95f),  // NW Grass North
            /*16*/ new Vector2(2.10f,  1.50f),  // Balete West
            /*17*/ new Vector2(2.86f,  1.60f),  // Balete Grove
            /*18*/ new Vector2(3.55f,  2.55f),  // N-S Avenue North
            /*19*/ new Vector2(2.60f, -1.55f),  // SW Grass Mid
            /*20*/ new Vector2(3.55f, -1.95f),  // SW Grass East
            /*21*/ new Vector2(4.52f,  0.66f),  // Nitso Entrance
            /*22*/ new Vector2(6.38f,  0.66f),  // Nitso South Junction
            /*23*/ new Vector2(5.30f,  1.42f),  // Nitso Mid-West Alley
            /*24*/ new Vector2(7.35f,  1.42f),  // Nitso Mid-East Alley
            /*25*/ new Vector2(5.96f,  2.22f),  // Nitso North-Center Alley
            /*26*/ new Vector2(6.42f,  3.04f),  // Nitso Far North Alley
            /*27*/ new Vector2(5.38f, -0.82f),  // Inside Mausoleum DelosSantos
            /*28*/ new Vector2(6.38f, -0.20f),  // SE North Lane Mid
            /*29*/ new Vector2(7.42f, -0.82f),  // Inside Mausoleum Villanueva
            /*30*/ new Vector2(6.38f, -1.62f),  // SE Middle Lane (in front of Crypt)
            /*31*/ new Vector2(6.38f, -2.35f)   // Inside Mausoleum AbandonedCrypt
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
        Link(3, 4); Link(4, 5); Link(5, 6); Link(3, 6);
        Link(5, 7); Link(7, 8);
        Link(1, 9); Link(9, 10); Link(1, 11); Link(11, 12); Link(12, 13);
        Link(1, 14); Link(14, 15); Link(14, 16); Link(16, 17); Link(17, 18); Link(4, 18);
        Link(2, 19); Link(19, 20); Link(6, 20); Link(12, 19);
        Link(5, 21); Link(21, 22); Link(22, 23); Link(22, 24);
        Link(23, 25); Link(24, 25); Link(25, 26); Link(18, 26);
        Link(7, 27); Link(7, 28); Link(28, 8); Link(8, 29);
        Link(28, 30); Link(30, 31);
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

    private static void CreateCemeteryWeatherManager(Transform envRoot, Light2D globalLight)
    {
        GameObject wmGo = new GameObject("WeatherManager");
        wmGo.transform.SetParent(envRoot, false);
        wmGo.AddComponent<NetworkObject>();
        var wm = wmGo.AddComponent<WeatherManager>();

        SerializedObject so = new SerializedObject(wm);
        var glProp = so.FindProperty("globalLight");
        if (glProp != null) glProp.objectReferenceValue = globalLight;

        var customProp = so.FindProperty("useCustomExteriorBounds");
        if (customProp != null) customProp.boolValue = true;

        var zonesProp = so.FindProperty("customExteriorZones");
        if (zonesProp != null)
        {
            zonesProp.arraySize = 2;
            zonesProp.GetArrayElementAtIndex(0).rectValue = new Rect(-1.72f, -3.15f, 1.38f, 6.60f);
            zonesProp.GetArrayElementAtIndex(1).rectValue = new Rect(0.00f, -3.10f, 8.50f, 6.50f);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BakeAndVerifyNavMesh(Transform envRoot, Transform ghostMgr)
    {
        NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            GameObject nmGo = new GameObject("NavhMesh");
            nmGo.transform.SetParent(envRoot, false);
            surface = nmGo.AddComponent<NavMeshSurface>();
        }
        surface.transform.SetAsFirstSibling();

        BarangayMapFixer.BakeAndPersistNavMeshSurface(surface, CEMETERY_SCENE_PATH);

        // Keep dormant HuntGhost off NavMeshAgent errors and move Ghost Handler onto walkable NavMesh
        foreach (var hunter in Object.FindObjectsByType<HunterGhostBehavior>(FindObjectsInactive.Include))
        {
            if (hunter == null) continue;
            var stray = hunter.GetComponent<NavMeshAgent>();
            if (stray != null) stray.enabled = false;
        }

        if (ghostMgr != null)
        {
            GhostHandler gh = Object.FindAnyObjectByType<GhostHandler>();
            if (gh != null)
            {
                Vector3 entrancePos = new Vector3(0.40f, 0.12f, 0f);
                gh.transform.position = entrancePos;
                if (gh.eventGhost != null) gh.eventGhost.transform.localPosition = Vector3.zero;
                if (gh.hunterGhost != null) gh.hunterGhost.transform.localPosition = Vector3.zero;
            }
        }

        // Automated NavMesh verification across all 10 GhostRoomMarkers + Road non-walkable check
        Vector3 startProbe = new Vector3(0.40f, 0.12f, 0f);
        bool startOk = NavMesh.SamplePosition(startProbe, out NavMeshHit startHit, 0.35f, 1 << 0);
        bool roadWalkable = NavMesh.SamplePosition(new Vector3(-1.03f, 0.00f, 0f), out _, 0.25f, 1 << 0);
        Debug.Log($"[CemeteryMapSceneBuilder] NavMesh sanity: startWalkable={startOk}, roadWalkable={roadWalkable} (expected false)");

        GhostRoomMarker[] markers = Object.FindObjectsByType<GhostRoomMarker>(FindObjectsInactive.Include);
        NavMeshPath path = new NavMeshPath();
        foreach (GhostRoomMarker m in markers)
        {
            if (m == null) continue;
            Vector3 target = m.transform.position;
            bool hasSample = NavMesh.SamplePosition(target, out NavMeshHit destHit, 0.75f, 1 << 0);
            bool pathOk = startOk && hasSample &&
                          NavMesh.CalculatePath(startHit.position, destHit.position, 1 << 0, path) &&
                          path.status == NavMeshPathStatus.PathComplete;
            if (!pathOk)
            {
                Debug.LogError($"[CemeteryMapSceneBuilder] NavMesh path check FAILED for GhostRoomMarker '{m.name}' at {target} (sample={hasSample}, status={path.status})");
            }
            else
            {
                Debug.Log($"[CemeteryMapSceneBuilder] NavMesh path verified: Entrance -> {m.name} (corners={path.corners.Length})");
            }
        }
    }

    public static void RegisterAllScenesInBuildSettings()
    {
        var desiredPaths = new[]
        {
            "Assets/Scenes/MainMenuScene.unity",
            "Assets/Scenes/LobbyScene.unity",
            "Assets/Scenes/BarangayMap.unity",
            CEMETERY_SCENE_PATH,
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

    // -----------------------------------------------------------------------
    // Primitive Scene Helpers
    // -----------------------------------------------------------------------
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

    private static void CreateFloorArea(
        Transform parent, string name, Sprite sprite, Rect rect, SurfaceType surface,
        int priority, int sortOrder, int groundLayer, bool walkable)
    {
        GameObject go = CreateTiledSpriteRect(parent, name, sprite, rect, "Floor", sortOrder, groundLayer);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = walkable ? 0 : 1;

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = rect.size;
        var fs = go.AddComponent<FloorSurface>();
        fs.surfaceType = surface;
        fs.priority = priority;
    }

    private static void CreateEnclosedBuildingRoof(Transform parent, string name, Sprite roofSpr, Rect rect)
    {
        GameObject roofGo = CreateTiledSpriteRect(parent, name, roofSpr, rect, "Roof", 0, 0);
        roofGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var col = roofGo.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = rect.size;

        var fader = roofGo.AddComponent<RoofFader>();
        SerializedObject so = new SerializedObject(fader);
        var arr = so.FindProperty("spriteRenderers");
        arr.arraySize = 1;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = roofGo.GetComponent<SpriteRenderer>();
        var enclosed = so.FindProperty("enclosedBuilding");
        if (enclosed != null) enclosed.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateBuildingLightingAndSwitch(
        Transform lightSys, string roomName, Rect rect, Vector2 switchPos,
        int interactLayer, bool createSafeZone, float lightOuterRadius, string safeZoneNameOverride = null)
    {
        if (lightSys == null) return;

        GameObject swGo = new GameObject($"{roomName} LS");
        swGo.transform.SetParent(lightSys, false);
        swGo.transform.position = new Vector3(switchPos.x, switchPos.y, 0f);
        swGo.layer = interactLayer;

        Sprite capSpr = LoadBarangaySprite("House/wall_concrete_top");
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
        light.pointLightOuterRadius = lightOuterRadius;
        light.pointLightInnerRadius = 0.15f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 0.55f;
        light.enabled = false;
        AssignAllSortingLayers(light);

        SerializedObject swSo = new SerializedObject(hls);
        var clProp = swSo.FindProperty("controlledLights");
        clProp.arraySize = 1;
        clProp.GetArrayElementAtIndex(0).objectReferenceValue = light;

        if (createSafeZone)
        {
            string szName = string.IsNullOrEmpty(safeZoneNameOverride) ? $"{roomName}_SafeZone" : safeZoneNameOverride;
            GameObject szGo = new GameObject(szName);
            szGo.transform.SetParent(lightSys, false);
            szGo.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0f);
            var szCol = szGo.AddComponent<BoxCollider2D>();
            szCol.isTrigger = true;
            szCol.size = rect.size;
            szGo.AddComponent<SafeZone>().RefreshColliders();
            szGo.SetActive(false);

            var szProp = swSo.FindProperty("litSafeZones");
            szProp.arraySize = 1;
            szProp.GetArrayElementAtIndex(0).objectReferenceValue = szGo;
        }
        swSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateInteractiveCemeteryLampPost(
        Transform lightSys, string lampName, Vector2 pos, float bulbOffsetX, int interactLayer)
    {
        if (lightSys == null) return;

        GameObject lampGo = new GameObject(lampName);
        lampGo.transform.SetParent(lightSys, false);
        lampGo.transform.position = new Vector3(pos.x, pos.y, 0f);
        lampGo.layer = interactLayer;

        Sprite lampSpr = LoadBarangaySprite("Exterior/lamp_post");
        var lampSr = lampGo.AddComponent<SpriteRenderer>();
        lampSr.sprite = lampSpr;
        lampSr.sortingLayerName = "Item";
        lampSr.sortingOrder = 4;
        if (lampSpr != null)
        {
            lampGo.transform.localScale = new Vector3(
                0.18f / lampSpr.bounds.size.x,
                0.42f / lampSpr.bounds.size.y,
                1f);
        }

        lampGo.AddComponent<BoxCollider2D>().isTrigger = true;
        lampGo.AddComponent<NetworkObject>();
        var hls = lampGo.AddComponent<HouseLightSwitch>();
        hls.EnsureSwitchSetup();
        lampGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject lightGo = new GameObject($"CemeteryFloodLight_{lampName}");
        lightGo.transform.SetParent(lampGo.transform, false);
        lightGo.transform.position = new Vector3(pos.x + bulbOffsetX, pos.y + 0.12f, 0f);
        lightGo.transform.localScale = Vector3.one;

        var light = lightGo.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1.0f, 0.84f, 0.55f, 1f);
        light.intensity = 0.92f;
        light.pointLightInnerRadius = 0.18f;
        light.pointLightOuterRadius = 1.65f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 0.70f;
        light.enabled = false;
        AssignAllSortingLayers(light);

        GameObject szGo = new GameObject($"{lampName}_SafeZone");
        szGo.transform.SetParent(lightSys, false);
        szGo.transform.position = new Vector3(pos.x, pos.y, 0f);
        var szCol = szGo.AddComponent<BoxCollider2D>();
        szCol.isTrigger = true;
        szCol.size = new Vector2(1.35f, 1.35f);
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

    private static void CreateNetworkDoor(
        Transform parent, string name, Vector2 center, bool verticalWall, Sprite closedSpr, Sprite openSpr,
        float length, float thick, bool isFrontDoor, bool requiresKey, bool isHidingSpot,
        int wallLayer, int interactLayer, float swingDeg, bool swapSprite)
    {
        GameObject doorRoot = new GameObject(name);
        doorRoot.transform.SetParent(parent, false);
        doorRoot.transform.position = new Vector3(center.x, center.y, 0f);
        doorRoot.layer = interactLayer;
        doorRoot.AddComponent<NetworkObject>();

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
        oSr.sprite = (swapSprite && openSpr != null) ? openSpr : closedSpr;
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
        so.FindProperty("motionType").enumValueIndex = (int)DoorMotionType.ArcSwing;
        so.FindProperty("isFrontDoor").boolValue = isFrontDoor;
        so.FindProperty("requiresKey").boolValue = requiresKey;
        so.FindProperty("isHidingSpot").boolValue = isHidingSpot;
        so.FindProperty("swapSpriteWhenOpen").boolValue = swapSprite;
        so.FindProperty("disableObstacleColliderWhenOpen").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateTreeLandmark(Transform parent, string name, Sprite treeSpr, Vector2 trunkCenter, float canopySize, int wallLayer)
    {
        GameObject canopy = CreateSpriteRect(parent, name, treeSpr,
            new Rect(trunkCenter.x - canopySize * 0.5f, trunkCenter.y - canopySize * 0.5f, canopySize, canopySize),
            "Roof", 2, 0);
        canopy.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Solid trunk uses a SpriteRenderer hidden beneath the Roof canopy so 2D RenderMeshes NavMesh bakes it as Not Walkable
        Sprite trunkSpr = LoadBarangaySprite("House/wall_concrete_top");
        GameObject trunk = CreateSpriteRect(canopy.transform, $"{name}_Trunk", trunkSpr,
            new Rect(trunkCenter.x - 0.12f, trunkCenter.y - 0.12f, 0.24f, 0.24f),
            "Item", -1, wallLayer);
        var trunkSr = trunk.GetComponent<SpriteRenderer>();
        if (trunkSr != null) trunkSr.color = new Color(0.24f, 0.16f, 0.10f, 1f);

        var col = trunk.AddComponent<BoxCollider2D>();
        if (trunkSpr != null) col.size = trunkSpr.bounds.size;
        AddShadowCaster2D(trunk);
        var nmm = trunk.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;
    }

    private static void CreateHideZone(Transform parent, string name, Rect rect)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(rect.center.x, rect.center.y, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = rect.size;
        go.AddComponent<HideZone>();
    }

    private static void CreateGhostRoomMarker(Transform ghostMarkers, string name, Rect rect)
    {
        if (ghostMarkers == null) return;
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
        sr.size = rect.size;
        return go;
    }

    private static GameObject CreateNonBlockingProp(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder)
    {
        GameObject go = CreateSpriteRect(parent, name, sprite, rect, sortingLayer, sortOrder, 0);
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        return go;
    }

    private static GameObject CreateSolidProp(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int wallLayer)
    {
        GameObject go = CreateSpriteRect(parent, name, sprite, rect, sortingLayer, sortOrder, wallLayer);
        var col = go.AddComponent<BoxCollider2D>();
        if (sprite != null) col.size = sprite.bounds.size;
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;
        return go;
    }

    private static GameObject CreateSolidShadowProp(Transform parent, string name, Sprite sprite, Rect rect, string sortingLayer, int sortOrder, int wallLayer)
    {
        GameObject go = CreateSolidProp(parent, name, sprite, rect, sortingLayer, sortOrder, wallLayer);
        AddShadowCaster2D(go);
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
        col.size = rect.size;
        if (castShadows) AddShadowCaster2D(go);
        var nmm = go.AddComponent<NavMeshModifier>();
        nmm.overrideArea = true;
        nmm.area = 1;
    }

    private static void CreateWindowAccent(Transform wallsRoot, string name, Sprite windowSpr, Vector2 center, bool vertical)
    {
        GameObject go = CreateSpriteRect(wallsRoot, name, windowSpr,
            new Rect(center.x - 0.17f, center.y - 0.032f, 0.34f, 0.064f), "Wall", 2, 0);
        if (vertical) go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
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
        Sprite closedSpr = LoadBarangaySprite("Jeepney/jeepney_ramp_closed");
        Sprite openSpr = LoadBarangaySprite("Jeepney/jeepney_ramp_open");

        GameObject doorRoot = new GameObject("JeepneyRampDoor");
        doorRoot.transform.SetParent(doorsRoot, false);
        doorRoot.transform.position = new Vector3(closedCenter.x, closedCenter.y, 0f);
        doorRoot.layer = interactLayer;
        doorRoot.AddComponent<NetworkObject>();

        GameObject cRef = new GameObject("ClosedRef");
        cRef.transform.SetParent(doorRoot.transform, false);
        cRef.layer = wallLayer;
        var cSr = cRef.AddComponent<SpriteRenderer>();
        cSr.sprite = closedSpr;
        cSr.sortingLayerName = "Van";
        cSr.sortingOrder = 2;
        if (closedSpr != null)
        {
            cRef.transform.localScale = new Vector3(0.24f / closedSpr.bounds.size.x, 0.055f / closedSpr.bounds.size.y, 1f);
            cRef.AddComponent<BoxCollider2D>().size = closedSpr.bounds.size;
        }
        AddShadowCaster2D(cRef);
        cRef.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

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

    private static void StripDuplicateGameSettingsManager()
    {
        foreach (var matchMgr in Object.FindObjectsByType<GameMatchManager>(FindObjectsInactive.Include))
        {
            if (matchMgr == null) continue;
            var dupSettings = matchMgr.GetComponent<GameSettingsManager>();
            if (dupSettings != null) Object.DestroyImmediate(dupSettings);
        }
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
        for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
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

    private static Sprite LoadBarangaySprite(string relNoExt) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{BARANGAY_SPRITE_PREFIX}{relNoExt}.png");

    private static Sprite LoadCemeterySprite(string relNoExt) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{CEMETERY_SPRITE_PREFIX}{relNoExt}.png");

    private static bool FileExists(string assetPath) =>
        System.IO.File.Exists(System.IO.Path.GetFullPath(assetPath));
}
