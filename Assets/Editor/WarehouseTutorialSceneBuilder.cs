using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Netcode;

public static class WarehouseTutorialSceneBuilder
{
    private const string SCENE_PATH = "Assets/Scenes/WarehouseTutorialScene.unity";
    private const string BARANGAY_SCENE_PATH = "Assets/Scenes/BarangayMap.unity";
    private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";

    [MenuItem("Pamahiin/Ensure DevConsole In MainMenuScene")]
    public static void EnsureDevConsoleInMainMenu()
    {
        if (EditorApplication.isPlaying) return;
        Scene mainMenuScene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenuScene.unity", OpenSceneMode.Single);
        if (FindInScene<DeveloperConsoleManager>(mainMenuScene) == null)
        {
            Scene barangayScene = EditorSceneManager.OpenScene(BARANGAY_SCENE_PATH, OpenSceneMode.Additive);
            try
            {
                CloneByNameFromScene(barangayScene, mainMenuScene, "DevConsole");
            }
            finally
            {
                if (barangayScene.IsValid() && barangayScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(barangayScene, true);
                }
            }
            EditorSceneManager.SaveScene(mainMenuScene, "Assets/Scenes/MainMenuScene.unity");
            Debug.Log("[WarehouseTutorialSceneBuilder] Added DevConsole to MainMenuScene.unity");
        }
        else
        {
            Debug.Log("[WarehouseTutorialSceneBuilder] DevConsole already present in MainMenuScene.unity");
        }
    }

    [MenuItem("Pamahiin/Build Warehouse Tutorial Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[WarehouseTutorialSceneBuilder] Exit Play Mode before rebuilding the tutorial scene.");
            return;
        }

        Scene tutorialScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Scene barangayScene = EditorSceneManager.OpenScene(BARANGAY_SCENE_PATH, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(tutorialScene);

        try
        {
            BuildSceneContents(tutorialScene, barangayScene);
        }
        finally
        {
            if (barangayScene.IsValid() && barangayScene.isLoaded)
            {
                EditorSceneManager.CloseScene(barangayScene, true);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH));
        EditorSceneManager.SaveScene(tutorialScene, SCENE_PATH);
        AddSceneToBuildSettings(SCENE_PATH);
        AssetDatabase.Refresh();
        Debug.Log($"[WarehouseTutorialSceneBuilder] Built and saved {SCENE_PATH}");
    }

    private static void BuildSceneContents(Scene tutorialScene, Scene barangayScene)
    {
        // 1. Camera & Global Light
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 0.42f;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraFollow>();

        var globalLightGo = new GameObject("Global Light 2D");
        var globalLight = globalLightGo.AddComponent<Light2D>();
        globalLight.lightType = Light2D.LightType.Global;
        globalLight.intensity = 0.25f;
        globalLight.color = new Color(0.75f, 0.80f, 0.90f, 1f);

        Sprite whiteSprite = CreateWhiteSquareSprite();
        Sprite doorClosedSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/BarangayMap/House/door_wood_closed.png");
        Sprite doorOpenSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/BarangayMap/House/door_wood_open.png");
        Sprite ghostIdleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/ghost_idle_0.png");
        Sprite ghostHuntSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/ghost_hunt_0.png");
        Sprite chalkDiagramSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/EQUIPMENTS/chalk diagram.png");

        // 2. Clone shared UI & Managers from BarangayMap.unity
        string[] sharedObjectsToClone =
        {
            "HUD",
            "Journal UI",
            "PauseMenu UI",
            "SpiritBox UI",
            "Exorcism UI",
            "EndGame UI",
            "DevConsole",
            "ParanormalManager"
        };
        foreach (string objName in sharedObjectsToClone)
        {
            CloneByNameFromScene(barangayScene, tutorialScene, objName);
        }

        // Find template NetworkDoor & HouseLightSwitch & HideZone from BarangayMap
        NetworkDoor templateDoor = FindDoorTemplate(barangayScene);
        HouseLightSwitch templateSwitch = FindInScene<HouseLightSwitch>(barangayScene);
        HideZone templateHideZone = FindInScene<HideZone>(barangayScene);

        // 3. Build 5 sequential bays along +X (centers at 0, 1.2, 2.4, 3.6, 4.8)
        var bayResetPoints = new Transform[5];
        var bayExitDoors = new NetworkDoor[4];

        string[] bayNames =
        {
            "Bay1_Controls",
            "Bay2_PowerSanity",
            "Bay3_Evidence",
            "Bay4_Survival",
            "Bay5_Extraction"
        };

        for (int i = 0; i < 5; i++)
        {
            float cx = i * 1.2f;
            var bayRoot = new GameObject(bayNames[i]);
            bayRoot.transform.position = new Vector3(cx, 0f, 0f);

            // Floor (Bay 5 extended slightly east to frame the Jeepney extraction dock)
            var floor = new GameObject("Floor");
            floor.transform.SetParent(bayRoot.transform, false);
            floor.transform.localPosition = i == 4 ? new Vector3(0.25f, 0f, 0f) : Vector3.zero;
            floor.transform.localScale = i == 4 ? new Vector3(1.65f, 1.10f, 1f) : new Vector3(1.15f, 0.70f, 1f);
            var floorSr = floor.AddComponent<SpriteRenderer>();
            floorSr.sprite = whiteSprite;
            floorSr.color = new Color(0.14f, 0.15f, 0.17f, 1f);
            floorSr.sortingOrder = -10;

            float wallHalfW = i == 4 ? 1.70f : 1.20f;
            float wallCenterX = i == 4 ? 0.25f : 0f;
            float wallY = i == 4 ? 0.56f : 0.36f;
            CreateWall(bayRoot.transform, "Wall_North", new Vector3(wallCenterX, wallY, 0f), new Vector3(wallHalfW, 0.05f, 1f), whiteSprite);
            CreateWall(bayRoot.transform, "Wall_South", new Vector3(wallCenterX, -wallY, 0f), new Vector3(wallHalfW, 0.05f, 1f), whiteSprite);

            if (i == 0)
            {
                CreateWall(bayRoot.transform, "Wall_West", new Vector3(-0.56f, 0f, 0f), new Vector3(0.05f, 0.75f, 1f), whiteSprite);
            }
            if (i == 4)
            {
                CreateWall(bayRoot.transform, "Wall_East", new Vector3(1.08f, 0f, 0f), new Vector3(0.05f, 1.15f, 1f), whiteSprite);
            }

            var spawnPt = new GameObject($"Bay{i + 1}_SpawnPoint");
            spawnPt.transform.SetParent(bayRoot.transform, false);
            spawnPt.transform.localPosition = new Vector3(-0.35f, 0f, 0f);
            bayResetPoints[i] = spawnPt.transform;

            if (i != 1) // Bay 2 light is controlled by the cloned BarangayMap LightSwitch + FuseBox
            {
                var bayLightGo = new GameObject($"Bay{i + 1}_SpotLight");
                bayLightGo.transform.SetParent(bayRoot.transform, false);
                var bayLight = bayLightGo.AddComponent<Light2D>();
                bayLight.lightType = Light2D.LightType.Point;
                bayLight.pointLightOuterRadius = 0.80f;
                bayLight.intensity = 0.85f;
                bayLight.color = new Color(1f, 0.94f, 0.82f, 1f);
            }

            if (i < 4)
            {
                float doorX = cx + 0.60f;
                CreateWall(bayRoot.transform, $"Divider_{i + 1}_Top", new Vector3(0.60f, 0.23f, 0f), new Vector3(0.05f, 0.28f, 1f), whiteSprite);
                CreateWall(bayRoot.transform, $"Divider_{i + 1}_Bottom", new Vector3(0.60f, -0.23f, 0f), new Vector3(0.05f, 0.28f, 1f), whiteSprite);

                bayExitDoors[i] = CreateTutorialDoor(
                    templateDoor,
                    tutorialScene,
                    $"Bay{i + 1}_ExitDoor",
                    new Vector3(doorX, 0f, 0f),
                    doorClosedSprite,
                    doorOpenSprite,
                    whiteSprite);
            }
        }

        // 4. Bay 1 Equipment
        InstantiatePrefab("Assets/Prefabs/FlashLightItem.prefab", new Vector3(-0.10f, 0.12f, 0f));

        // 5. Bay 2 — Cloned FuseBox & HouseLightSwitch + Light2D + SafeZone from BarangayMap
        var fuseGo = CloneByNameFromScene(barangayScene, tutorialScene, "FuseBox");
        FuseBox fuseBox = null;
        if (fuseGo != null)
        {
            fuseGo.transform.SetParent(null, true);
            fuseGo.transform.position = new Vector3(1.05f, 0.29f, 0f);
            fuseBox = fuseGo.GetComponent<FuseBox>();
        }

        // Bay 2 Room Light2D + SafeZone controlled by the cloned HouseLightSwitch
        var bay2LightGo = new GameObject("Bay2_RoomLight2D");
        bay2LightGo.transform.position = new Vector3(1.20f, 0f, 0f);
        var bay2RoomLight = bay2LightGo.AddComponent<Light2D>();
        bay2RoomLight.lightType = Light2D.LightType.Point;
        bay2RoomLight.pointLightOuterRadius = 0.85f;
        bay2RoomLight.intensity = 0.95f;
        bay2RoomLight.color = new Color(1f, 0.94f, 0.80f, 1f);
        bay2RoomLight.enabled = false;

        var bay2SafeZoneGo = new GameObject("Bay2_SafeZone");
        bay2SafeZoneGo.transform.position = new Vector3(1.20f, 0f, 0f);
        var safeCol = bay2SafeZoneGo.AddComponent<BoxCollider2D>();
        safeCol.isTrigger = true;
        safeCol.size = new Vector2(1.05f, 0.65f);
        bay2SafeZoneGo.AddComponent<SafeZone>();
        bay2SafeZoneGo.SetActive(false);

        HouseLightSwitch lightSwitch = null;
        if (templateSwitch != null)
        {
            var switchGo = UnityEngine.Object.Instantiate(templateSwitch.gameObject);
            switchGo.name = "Bay2_LightSwitch";
            SceneManager.MoveGameObjectToScene(switchGo, tutorialScene);
            switchGo.transform.SetParent(null, true);
            switchGo.transform.position = new Vector3(1.35f, 0.29f, 0f);
            lightSwitch = switchGo.GetComponent<HouseLightSwitch>();

            var switchSo = new SerializedObject(lightSwitch);
            var lightsProp = switchSo.FindProperty("controlledLights");
            lightsProp.ClearArray();
            lightsProp.arraySize = 1;
            lightsProp.GetArrayElementAtIndex(0).objectReferenceValue = bay2RoomLight;

            var zonesProp = switchSo.FindProperty("litSafeZones");
            zonesProp.ClearArray();
            zonesProp.arraySize = 1;
            zonesProp.GetArrayElementAtIndex(0).objectReferenceValue = bay2SafeZoneGo;
            switchSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // 6. Bay 3 — Ghost Room Marker, Real GhostHandler, Visible Ghost Orbs, D.O.T.S. Example & Evidence Tools
        var roomMarkerGo = new GameObject("Bay3_GhostRoomMarker");
        roomMarkerGo.transform.position = new Vector3(2.40f, 0f, 0f);
        var roomCol = roomMarkerGo.AddComponent<BoxCollider2D>();
        roomCol.isTrigger = true;
        roomCol.size = new Vector2(1.05f, 0.65f);
        roomMarkerGo.AddComponent<GhostRoomMarker>();

        var bay3EmfAnchor = new GameObject("Bay3_EMFAnchor");
        bay3EmfAnchor.transform.position = new Vector3(2.40f, -0.15f, 0f);

        var bay3GhostAnchor = new GameObject("Bay3_GhostIdleAnchor");
        bay3GhostAnchor.transform.position = new Vector3(2.65f, -0.10f, 0f);

        var ghostHandlerGo = InstantiatePrefab("Assets/Prefabs/Ghost Handler.prefab", new Vector3(2.40f, 0f, 0f));
        GhostHandler ghostHandler = ghostHandlerGo != null ? ghostHandlerGo.GetComponent<GhostHandler>() : null;
        if (ghostHandler != null)
        {
            ghostHandler.ghostroomMarker = roomCol;
            ghostHandler.ghostName = "Multo";
            ghostHandler.currentEvidence.Clear();
            ghostHandler.currentEvidence.Add(EvidenceType.Crucifix);
            ghostHandler.currentEvidence.Add(EvidenceType.EMF);
            ghostHandler.currentEvidence.Add(EvidenceType.Freezing);
            ghostHandler.currentEvidence.Add(EvidenceType.DOTS);
            ghostHandler.currentEvidence.Add(EvidenceType.GhostWriting);
            ghostHandler.currentEvidence.Add(EvidenceType.SpiritBox);
            ghostHandler.currentEvidence.Add(EvidenceType.GhostOrbs);
        }

        InstantiatePrefab("Assets/Prefabs/EMFReaderItem.prefab", new Vector3(2.05f, 0.16f, 0f));
        InstantiatePrefab("Assets/Prefabs/ThermometerItem.prefab", new Vector3(2.16f, 0.16f, 0f));
        InstantiatePrefab("Assets/Prefabs/SpiritBoxItem.prefab", new Vector3(2.27f, 0.16f, 0f));
        InstantiatePrefab("Assets/Prefabs/GhostWritingBookItem.prefab", new Vector3(2.38f, 0.16f, 0f));
        InstantiatePrefab("Assets/Prefabs/VideoCameraItem.prefab", new Vector3(2.49f, 0.16f, 0f));
        var dotsGo = InstantiatePrefab("Assets/Prefabs/DotsProjector.prefab", new Vector3(2.58f, 0.05f, 0f));
        DotsProjectorItem exampleDots = dotsGo != null ? dotsGo.GetComponent<DotsProjectorItem>() : null;

        var orbsGo = InstantiatePrefab("Assets/Prefabs/Ghost Orbs.prefab", new Vector3(2.40f, -0.04f, 0f));
        GhostOrbSystem orbSys = orbsGo != null ? orbsGo.GetComponent<GhostOrbSystem>() : null;

        // 7. Bay 4 — Cloned HideZone, Crucifix, Sanity Pills, Scripted Ghost with Real Sprites, Guided Exorcism Circle
        InstantiatePrefab("Assets/Prefabs/Crucifix.prefab", new Vector3(3.30f, 0.14f, 0f));
        InstantiatePrefab("Assets/Prefabs/SanityPillsItem.prefab", new Vector3(3.85f, 0.14f, 0f));
        InstantiatePrefab("Assets/Prefabs/RitualChalkItem.prefab", new Vector3(3.85f, -0.14f, 0f));

        HideZone hideZone = null;
        if (templateHideZone != null)
        {
            var hideGo = UnityEngine.Object.Instantiate(templateHideZone.gameObject);
            hideGo.name = "Bay4_HideZone";
            SceneManager.MoveGameObjectToScene(hideGo, tutorialScene);
            hideGo.transform.SetParent(null, true);
            hideGo.transform.position = new Vector3(3.55f, -0.20f, 0f);
            hideZone = hideGo.GetComponent<HideZone>();
        }
        else
        {
            var hideGo = new GameObject("Bay4_HideZone");
            hideGo.transform.position = new Vector3(3.55f, -0.20f, 0f);
            hideGo.AddComponent<BoxCollider2D>().isTrigger = true;
            hideZone = hideGo.AddComponent<HideZone>();
        }

        var exorcismGo = new GameObject("Bay4_ExorcismCircle");
        exorcismGo.transform.position = new Vector3(3.60f, 0.04f, 0f);
        var exoSr = exorcismGo.AddComponent<SpriteRenderer>();
        exoSr.sprite = chalkDiagramSprite != null ? chalkDiagramSprite : whiteSprite;
        exoSr.color = new Color(0.95f, 0.85f, 0.65f, 0.9f);
        exoSr.sortingOrder = -2;
        if (exoSr.sprite != null && exoSr.sprite.bounds.size.x > 0.01f)
        {
            float es = 0.22f / exoSr.sprite.bounds.size.x;
            exorcismGo.transform.localScale = new Vector3(es, es, 1f);
        }
        var exoCol = exorcismGo.AddComponent<BoxCollider2D>();
        exoCol.isTrigger = true;
        var exoScript = exorcismGo.AddComponent<TutorialExorcismSpotInteractable>();
        var exoSo = new SerializedObject(exoScript);
        exoSo.FindProperty("circleRenderer").objectReferenceValue = exoSr;
        exoSo.ApplyModifiedPropertiesWithoutUndo();

        var ghostGo = new GameObject("TutorialGhost");
        ghostGo.transform.position = new Vector3(3.98f, 0.16f, 0f);
        var ghostSr = ghostGo.AddComponent<SpriteRenderer>();
        ghostSr.sprite = ghostIdleSprite != null ? ghostIdleSprite : whiteSprite;
        ghostSr.color = new Color(0.85f, 0.95f, 1f, 0.78f);
        ghostSr.sortingOrder = 5;
        if (ghostIdleSprite != null && ghostIdleSprite.bounds.size.y > 0.01f)
        {
            float gs = 0.12f / ghostIdleSprite.bounds.size.y;
            ghostGo.transform.localScale = new Vector3(gs, gs, 1f);
        }
        else
        {
            ghostGo.transform.localScale = new Vector3(0.08f, 0.08f, 1f);
        }
        var tutorialGhost = ghostGo.AddComponent<TutorialGhostController>();
        var ghostSo = new SerializedObject(tutorialGhost);
        ghostSo.FindProperty("ghostRenderer").objectReferenceValue = ghostSr;
        ghostSo.FindProperty("idleSprite").objectReferenceValue = ghostIdleSprite;
        ghostSo.FindProperty("huntSprite").objectReferenceValue = ghostHuntSprite;
        ghostSo.ApplyModifiedPropertiesWithoutUndo();

        // 8. Bay 5 — Real Cloned Jeepney & EndGameButton (TruckEndGameButton) from BarangayMap
        var jeepneyGo = CloneByNameFromScene(barangayScene, tutorialScene, "Jeepney");
        if (jeepneyGo != null)
        {
            jeepneyGo.transform.SetParent(null, true);
            // Offset so JeepneyBody (-0.82, 0.28) lands cleanly at Bay 5 right side (5.05, 0.18) with rear ramp facing Bay 5 floor
            jeepneyGo.transform.position = new Vector3(5.87f, -0.10f, 0f);
        }

        // Ensure EndGameButton (TruckEndGameButton) is present inside tutorialScene
        TruckEndGameButton existingEndBtn = jeepneyGo != null ? jeepneyGo.GetComponentInChildren<TruckEndGameButton>(true) : null;
        if (existingEndBtn == null)
        {
            var endBtnClone = CloneByNameFromScene(barangayScene, tutorialScene, "EndGameButton");
            if (endBtnClone != null)
            {
                endBtnClone.transform.SetParent(null, true);
                endBtnClone.transform.position = new Vector3(5.05f, 0.08f, 0f);
            }
            else
            {
                var consoleGo = new GameObject("Bay5_ExitConsole");
                consoleGo.transform.position = new Vector3(5.05f, 0f, 0f);
                consoleGo.AddComponent<BoxCollider2D>().isTrigger = true;
                consoleGo.AddComponent<TruckEndGameButton>();
            }
        }
        else
        {
            existingEndBtn.transform.position = new Vector3(5.05f, 0.08f, 0f);
        }

        // 9. WarehouseTutorialManager + Wiring
        var mgrGo = new GameObject("WarehouseTutorialManager");
        var mgr = mgrGo.AddComponent<WarehouseTutorialManager>();
        var so = new SerializedObject(mgr);

        var doorsProp = so.FindProperty("bayExitDoors");
        doorsProp.arraySize = 4;
        for (int i = 0; i < 4; i++)
        {
            doorsProp.GetArrayElementAtIndex(i).objectReferenceValue = bayExitDoors[i];
        }

        var spawnsProp = so.FindProperty("bayResetPoints");
        spawnsProp.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            spawnsProp.GetArrayElementAtIndex(i).objectReferenceValue = bayResetPoints[i];
        }

        so.FindProperty("tutorialGhost").objectReferenceValue = tutorialGhost;
        if (ghostHandler != null) so.FindProperty("tutorialGhostHandler").objectReferenceValue = ghostHandler;
        so.FindProperty("bay3RoomCollider").objectReferenceValue = roomCol;
        so.FindProperty("bay3EmfAnchor").objectReferenceValue = bay3EmfAnchor.transform;
        so.FindProperty("bay3GhostIdleAnchor").objectReferenceValue = bay3GhostAnchor.transform;
        if (exampleDots != null) so.FindProperty("bay3ExampleDots").objectReferenceValue = exampleDots;
        if (fuseBox != null) so.FindProperty("bay2FuseBox").objectReferenceValue = fuseBox;
        if (lightSwitch != null) so.FindProperty("bay2LightSwitch").objectReferenceValue = lightSwitch;
        if (hideZone != null) so.FindProperty("bay4HideZone").objectReferenceValue = hideZone;
        if (orbSys != null) so.FindProperty("bay3GhostOrbs").objectReferenceValue = orbSys;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 10. WarehouseTutorialHUD
        var hudGo = new GameObject("WarehouseTutorialHUD");
        var uiDoc = hudGo.AddComponent<UIDocument>();
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH);
        if (panelSettings != null)
        {
            uiDoc.panelSettings = panelSettings;
        }
        hudGo.AddComponent<WarehouseTutorialHUD>();
    }

    private static NetworkDoor CreateTutorialDoor(
        NetworkDoor templateDoor,
        Scene tutorialScene,
        string name,
        Vector3 position,
        Sprite doorClosedSprite,
        Sprite doorOpenSprite,
        Sprite fallbackSprite)
    {
        if (templateDoor != null)
        {
            var clone = UnityEngine.Object.Instantiate(templateDoor.gameObject);
            clone.name = name;
            SceneManager.MoveGameObjectToScene(clone, tutorialScene);
            clone.transform.SetParent(null, true);
            clone.transform.position = position;

            var netDoor = clone.GetComponent<NetworkDoor>();
            var doorSo = new SerializedObject(netDoor);
            var closedRef = doorSo.FindProperty("closedDoorRef").objectReferenceValue as SpriteRenderer;
            if (closedRef != null && closedRef.bounds.size.x > closedRef.bounds.size.y)
            {
                // Rotate horizontal BarangayMap door so it sits vertically across the bay divider
                clone.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
            return netDoor;
        }

        // Fallback using BarangayMap sprites
        var doorGo = new GameObject(name);
        doorGo.transform.position = position;
        doorGo.AddComponent<NetworkObject>();
        var fallbackDoor = doorGo.AddComponent<NetworkDoor>();

        var closedGo = new GameObject("ClosedRef");
        closedGo.transform.SetParent(doorGo.transform, false);
        closedGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        var closedSr = closedGo.AddComponent<SpriteRenderer>();
        closedSr.sprite = doorClosedSprite != null ? doorClosedSprite : fallbackSprite;
        closedSr.sortingOrder = 2;
        closedGo.AddComponent<BoxCollider2D>();

        var openGo = new GameObject("OpenRef");
        openGo.transform.SetParent(doorGo.transform, false);
        openGo.transform.localPosition = new Vector3(-0.08f, 0.08f, 0f);
        var openSr = openGo.AddComponent<SpriteRenderer>();
        openSr.sprite = doorOpenSprite != null ? doorOpenSprite : fallbackSprite;
        openSr.sortingOrder = 2;

        if (closedSr.sprite != null && closedSr.sprite.bounds.size.x > 0.01f)
        {
            float s = 0.18f / closedSr.sprite.bounds.size.x;
            closedGo.transform.localScale = new Vector3(s, s, 1f);
            openGo.transform.localScale = new Vector3(s, s, 1f);
        }

        var so = new SerializedObject(fallbackDoor);
        so.FindProperty("closedDoorRef").objectReferenceValue = closedSr;
        so.FindProperty("openDoorRef").objectReferenceValue = openSr;
        so.ApplyModifiedPropertiesWithoutUndo();
        return fallbackDoor;
    }

    private static GameObject CloneByNameFromScene(Scene sourceScene, Scene destScene, string objectName)
    {
        foreach (var root in sourceScene.GetRootGameObjects())
        {
            var found = FindChildRecursive(root.transform, objectName);
            if (found != null)
            {
                var clone = UnityEngine.Object.Instantiate(found.gameObject);
                clone.name = objectName;
                SceneManager.MoveGameObjectToScene(clone, destScene);
                return clone;
            }
        }
        Debug.LogWarning($"[WarehouseTutorialSceneBuilder] Could not find '{objectName}' in {sourceScene.name}");
        return null;
    }

    private static Transform FindChildRecursive(Transform parent, string targetName)
    {
        if (parent.name == targetName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var hit = FindChildRecursive(parent.GetChild(i), targetName);
            if (hit != null) return hit;
        }
        return null;
    }

    private static NetworkDoor FindDoorTemplate(Scene sourceScene)
    {
        foreach (var root in sourceScene.GetRootGameObjects())
        {
            foreach (var door in root.GetComponentsInChildren<NetworkDoor>(true))
            {
                if (door != null && !door.IsFrontDoor && !door.IsHidingSpot && door.name != "JeepneyRampDoor")
                {
                    var so = new SerializedObject(door);
                    if (so.FindProperty("closedDoorRef").objectReferenceValue != null &&
                        so.FindProperty("openDoorRef").objectReferenceValue != null)
                    {
                        return door;
                    }
                }
            }
        }
        return null;
    }

    private static T FindInScene<T>(Scene sourceScene) where T : Component
    {
        foreach (var root in sourceScene.GetRootGameObjects())
        {
            var found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static void CreateWall(Transform parent, string name, Vector3 localPos, Vector3 scale, Sprite sprite)
    {
        var wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = scale;
        var sr = wall.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(0.08f, 0.09f, 0.11f, 1f);
        sr.sortingOrder = 0;
        wall.AddComponent<BoxCollider2D>();
    }

    private static GameObject InstantiatePrefab(string assetPath, Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) return null;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance != null)
        {
            instance.transform.position = position;
        }
        return instance;
    }

    private static Sprite CreateWhiteSquareSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
        {
            if (s.path == scenePath)
            {
                s.enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }
        }
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
