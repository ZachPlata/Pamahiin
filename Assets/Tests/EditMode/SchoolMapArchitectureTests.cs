using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SchoolMapArchitectureTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

    private static Assembly runtimeAssembly;
    private static Assembly editorAssembly;
    private static Assembly navMeshAssembly;

    private static Assembly RuntimeAssembly => runtimeAssembly ??= Assembly.Load("Assembly-CSharp");
    private static Assembly EditorAssembly => editorAssembly ??= Assembly.Load("Assembly-CSharp-Editor");
    private static Assembly NavMeshAssembly => navMeshAssembly ??= Assembly.Load("NavMeshPlus");

    private static Type GetRuntimeType(string name)
    {
        Type t = RuntimeAssembly.GetType(name, throwOnError: true);
        Assert.IsNotNull(t, $"Missing runtime type {name}");
        return t;
    }

    private static Type GetEditorType(string name)
    {
        Type t = EditorAssembly.GetType(name, throwOnError: true);
        Assert.IsNotNull(t, $"Missing editor type {name}");
        return t;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
        createdObjects.Clear();
    }

    private GameObject CreateZoneObject(string name, int floorIndex, bool interior, Rect bounds)
    {
        Type fzType = GetRuntimeType("FloorZone");
        GameObject go = new GameObject(name);
        createdObjects.Add(go);

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.offset = bounds.center;
        col.size = bounds.size;

        Component fz = go.AddComponent(fzType);
        MethodInfo cfg = fzType.GetMethod("Configure", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(cfg);
        cfg.Invoke(fz, new object[] { floorIndex, interior, $"Floor {floorIndex}" });
        return go;
    }

    [Test]
    public void FloorZone_GetFloorIndex_DistinguishesExteriorAndFloors1To3()
    {
        Type fzType = GetRuntimeType("FloorZone");
        MethodInfo getFloor = fzType.GetMethod("GetFloorIndex", BindingFlags.Public | BindingFlags.Static);
        MethodInfo tryGet = fzType.GetMethod("TryGetFloorIndex", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(getFloor);
        Assert.IsNotNull(tryGet);

        CreateZoneObject("FZ1", 1, true, new Rect(-6.5f, -4.25f, 13f, 8.5f));
        CreateZoneObject("FZ2", 2, true, new Rect(-6.5f, -40.25f, 13f, 8.5f));
        CreateZoneObject("FZ3", 3, true, new Rect(-6.5f, -76.25f, 13f, 8.5f));

        Assert.AreEqual(1, (int)getFloor.Invoke(null, new object[] { new Vector3(0f, 0f, 0f) }));
        Assert.AreEqual(2, (int)getFloor.Invoke(null, new object[] { new Vector3(0f, -36f, 0f) }));
        Assert.AreEqual(3, (int)getFloor.Invoke(null, new object[] { new Vector3(0f, -72f, 0f) }));
        Assert.AreEqual(0, (int)getFloor.Invoke(null, new object[] { new Vector3(0f, 6.0f, 0f) }));

        object[] args = { new Vector3(0f, -36f, 0f), 0 };
        Assert.IsTrue((bool)tryGet.Invoke(null, args));
        Assert.AreEqual(2, (int)args[1]);
    }

    [Test]
    public void GameAudioManager_IsSameFloor_ConsultsFloorZonesAndPreservesBasementFallback()
    {
        Type audioType = GetRuntimeType("GameAudioManager");
        MethodInfo isSameFloor = audioType.GetMethod("IsSameFloor", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(isSameFloor);

        // No FloorZones -> legacy BarangayMap BASEMENT_Y_THRESHOLD (-3.2f) fallback
        Assert.IsTrue((bool)isSameFloor.Invoke(null, new object[] { new Vector3(1f, 0f, 0f), new Vector3(2f, -1f, 0f) }));
        Assert.IsFalse((bool)isSameFloor.Invoke(null, new object[] { new Vector3(1f, 0f, 0f), new Vector3(2f, -5f, 0f) }));

        // With FloorZones -> F1 (0) and F1 courtyard (-4) both valid cleanly
        CreateZoneObject("FZ1", 1, true, new Rect(-6.5f, -2.25f, 13f, 4.5f));
        CreateZoneObject("FZ2", 2, true, new Rect(-6.5f, -38.25f, 13f, 4.5f));

        Assert.IsTrue((bool)isSameFloor.Invoke(null, new object[] { new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 0f) }));
        Assert.IsFalse((bool)isSameFloor.Invoke(null, new object[] { new Vector3(0f, 0f, 0f), new Vector3(0f, -36f, 0f) }));
        Assert.IsFalse((bool)isSameFloor.Invoke(null, new object[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 5.5f, 0f) }));
    }

    [Test]
    public void WeatherManager_IsPositionIndoors_HandlesMultiFloorZonesAndLegacyBasement()
    {
        Type wmType = GetRuntimeType("WeatherManager");
        MethodInfo isIndoors = wmType.GetMethod("IsPositionIndoors", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(isIndoors);

        GameObject wmGo = new GameObject("TempWeatherManager");
        createdObjects.Add(wmGo);
        Component wm = wmGo.AddComponent(wmType);

        // Without FloorZones -> y < -3.2f is legacy Barangay basement indoors
        Assert.IsTrue((bool)isIndoors.Invoke(wm, new object[] { new Vector3(2f, -5f, 0f) }));
        Assert.IsFalse((bool)isIndoors.Invoke(wm, new object[] { new Vector3(0f, 0f, 0f) }));

        // With School FloorZones -> F2 (-36) indoors, F1 interior (0) indoors, South Courtyard (0, -3.5) outdoors!
        CreateZoneObject("FZ1_Bridge", 1, true, new Rect(-3.5f, -2.25f, 7f, 4.5f));
        CreateZoneObject("FZ2_Bridge", 2, true, new Rect(-3.5f, -38.25f, 7f, 4.5f));

        Assert.IsTrue((bool)isIndoors.Invoke(wm, new object[] { new Vector3(0f, 0f, 0f) }));
        Assert.IsTrue((bool)isIndoors.Invoke(wm, new object[] { new Vector3(0f, -36f, 0f) }));
        Assert.IsFalse((bool)isIndoors.Invoke(wm, new object[] { new Vector3(0f, -3.5f, 0f) }));
    }

    [Test]
    public void ProgressionAndLoadoutManager_RegistersSchoolMap()
    {
        Type mgrType = GetRuntimeType("ProgressionAndLoadoutManager");
        FieldInfo mapsField = mgrType.GetField("AvailableMaps", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo setMap = mgrType.GetMethod("SetMapIndex", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo getScene = mgrType.GetMethod("GetSelectedSceneName", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo isGameplay = mgrType.GetMethod("IsGameplayMapScene", BindingFlags.Public | BindingFlags.Static);

        GameObject go = new GameObject("TempLoadoutMgr");
        createdObjects.Add(go);
        Component mgr = go.AddComponent(mgrType);

        string[] maps = (string[])mapsField.GetValue(mgr);
        int schoolIdx = Array.IndexOf(maps, "San Nicolas High School (3 Floors)");
        Assert.GreaterOrEqual(schoolIdx, 0, "AvailableMaps must register San Nicolas High School (3 Floors).");

        setMap.Invoke(mgr, new object[] { schoolIdx });
        Assert.AreEqual("SchoolMap", (string)getScene.Invoke(mgr, null));
        Assert.IsTrue((bool)isGameplay.Invoke(null, new object[] { "SchoolMap" }));
        Assert.IsTrue((bool)isGameplay.Invoke(null, new object[] { "BarangayMap" }));
    }

    [Test]
    public void SchoolMapLayout_HasRequiredRoomsAndCourtyardSeparation()
    {
        Type layoutType = GetEditorType("SchoolMapLayout");
        MethodInfo buildRooms = layoutType.GetMethod("BuildAllRooms", BindingFlags.Public | BindingFlags.Static);
        MethodInfo buildDoors = layoutType.GetMethod("BuildAllDoors", BindingFlags.Public | BindingFlags.Static);
        MethodInfo getWings = layoutType.GetMethod("GetFloorWingBounds", BindingFlags.Public | BindingFlags.Static);

        var rooms = (IList)buildRooms.Invoke(null, null);
        Assert.GreaterOrEqual(rooms.Count, 40);

        // Courtyard pockets on F1 must remain exterior
        Rect[] f1Wings = (Rect[])getWings.Invoke(null, new object[] { 1 });
        Vector2 northCourtyard = new Vector2(0f, 3.25f);
        Vector2 southLawn = new Vector2(0f, -3.25f);
        foreach (Rect wing in f1Wings)
        {
            Assert.IsFalse(wing.Contains(northCourtyard));
            Assert.IsFalse(wing.Contains(southLawn));
        }

        // Verify all 4 Entry doors are marked IsFrontDoor
        var doors = (IList)buildDoors.Invoke(null, null);
        Type doorType = doors[0].GetType();
        FieldInfo isFrontField = doorType.GetField("IsFrontDoor");
        int frontCount = 0;
        foreach (object d in doors)
        {
            if ((bool)isFrontField.GetValue(d)) frontCount++;
        }
        Assert.AreEqual(4, frontCount);
    }

    [Test]
    public void SchoolMapScene_WhenPresent_PassesFullMultiFloorVerification()
    {
        const string scenePath = "Assets/Scenes/SchoolMap.unity";
        if (!File.Exists(scenePath))
        {
            Assert.Ignore("SchoolMap.unity has not been built yet; run Pamahiin/Build SchoolMap Scene first.");
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            Physics2D.SyncTransforms();
            GameObject[] roots = scene.GetRootGameObjects();

            Type stairType = GetRuntimeType("StairTeleporter");
            Type floorZoneType = GetRuntimeType("FloorZone");
            Type roofType = GetRuntimeType("RoofFader");
            Type doorType = GetRuntimeType("NetworkDoor");
            Type fuseType = GetRuntimeType("FuseBox");
            Type hideType = GetRuntimeType("HideZone");
            Type markerType = GetRuntimeType("GhostRoomMarker");
            Type burialType = GetRuntimeType("BurialMapStage");
            Type navLinkType = NavMeshAssembly.GetType("NavMeshPlus.Components.NavMeshLink", true);

            var allStairs = CollectInScene(roots, stairType);
            var stairs = new List<Component>();
            foreach (Component s in allStairs)
            {
                if (s != null && s.gameObject.name != "MidStairTeleporterNode")
                {
                    stairs.Add(s);
                }
            }
            Assert.AreEqual(16, stairs.Count, "Expected 16 primary StairTeleporter endpoints (8 bidirectional F1<->F2<->F3 pairs).");

            PropertyInfo destProp = stairType.GetProperty("Destination", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getArrPos = stairType.GetMethod("GetArrivalPosition", BindingFlags.Public | BindingFlags.Instance);
            foreach (Component st in stairs)
            {
                Component dest = (Component)destProp.GetValue(st);
                Assert.IsNotNull(dest, $"StairTeleporter {st.name} has null Destination.");
                Assert.AreEqual(st, destProp.GetValue(dest), $"StairTeleporter {st.name} <-> {dest.name} must be bidirectional.");

                Vector3 arrPos = (Vector3)getArrPos.Invoke(dest, null);
                BoxCollider2D trig = dest.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(trig);
                Vector3 closest = trig.bounds.ClosestPoint(arrPos);
                float clearance = Vector2.Distance(arrPos, closest);
                Assert.GreaterOrEqual(clearance, 0.35f, $"Arrival offset for {dest.name} is too close to trigger ({clearance:F3}u).");
            }

            Assert.AreEqual(8, CollectInScene(roots, navLinkType).Count, "Expected 8 bidirectional NavMeshLinks.");
            Assert.AreEqual(3, CollectInScene(roots, floorZoneType).Count, "Expected 3 FloorZones (F1, F2, F3).");
            Assert.GreaterOrEqual(CollectInScene(roots, roofType).Count, 3, "Expected F1-F3 RoofFaders.");
            Assert.AreEqual(1, CollectInScene(roots, fuseType).Count, "Expected master FuseBox in F1 Workshop.");
            Assert.GreaterOrEqual(CollectInScene(roots, hideType).Count, 24, "Expected HideZones across stalls/lockers/closets.");
            Assert.GreaterOrEqual(CollectInScene(roots, markerType).Count, 35, "Expected >= 35 GhostRoomMarkers.");

            var doors = CollectInScene(roots, doorType);
            PropertyInfo isFrontProp = doorType.GetProperty("IsFrontDoor");
            int frontDoors = 0;
            foreach (Component d in doors)
            {
                if ((bool)isFrontProp.GetValue(d)) frontDoors++;
            }
            Assert.AreEqual(4, frontDoors, "Expected all 4 exterior entry doors marked IsFrontDoor.");

            var burials = CollectInScene(roots, burialType);
            Assert.AreEqual(1, burials.Count);
            Assert.Greater(Vector2.Distance(burials[0].transform.position, Vector2.zero), 20f);

            bool registeredInBuild = false;
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s != null && s.enabled && s.path == scenePath)
                {
                    registeredInBuild = true;
                    break;
                }
            }
            Assert.IsTrue(registeredInBuild, "SchoolMap.unity must be registered in EditorBuildSettings.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static List<Component> CollectInScene(GameObject[] roots, Type t)
    {
        var result = new List<Component>();
        foreach (GameObject r in roots)
        {
            if (r == null) continue;
            result.AddRange(r.GetComponentsInChildren(t, true));
        }
        return result;
    }
}
