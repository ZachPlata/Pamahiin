using System.IO;
using NavMeshPlus.Components;
using NavMeshPlus.Extensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Non-destructive editor repair utility for BarangayMap.
/// Preserves all manually placed objects and custom light switch scales while updating
/// switch colliders/indicator lights and baking + persisting the 2D NavMesh asset.
/// </summary>
[InitializeOnLoad]
public static class BarangayMapFixer
{
    public const string BARANGAY_SCENE_PATH = "Assets/Scenes/BarangayMap.unity";
    private static bool s_RepairQueued;

    static BarangayMapFixer()
    {
        EditorSceneManager.sceneOpened += (_, _) => QueueAutoRepairIfNeeded();
        QueueAutoRepairIfNeeded();
    }

    private static void QueueAutoRepairIfNeeded()
    {
        if (s_RepairQueued || Application.isBatchMode) return;
        s_RepairQueued = true;
        EditorApplication.delayCall += TryAutoRepairOnEditorIdle;
    }

    private static void TryAutoRepairOnEditorIdle()
    {
        s_RepairQueued = false;

        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            QueueAutoRepairIfNeeded();
            return;
        }

        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid() || !active.isLoaded || active.path != BARANGAY_SCENE_PATH)
        {
            return;
        }

        if (!DoesActiveSceneNeedRepair())
        {
            return;
        }

        Debug.Log("[BarangayMapFixer] Detected unbaked 2D NavMesh / legacy CircleCollider2D switches on BarangayMap; auto-repairing...");
        RepairCurrentBarangayScene(saveScene: true);
    }

    private static bool DoesActiveSceneNeedRepair()
    {
        NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null || surface.GetComponent<CollectSources2d>() == null)
        {
            return true;
        }

        string dataPath = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "";
        if (string.IsNullOrEmpty(dataPath) || !File.Exists(dataPath) || new FileInfo(dataPath).Length < 7000)
        {
            return true;
        }

        HouseLightSwitch[] switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Include);
        for (int i = 0; i < switches.Length; i++)
        {
            HouseLightSwitch sw = switches[i];
            if (sw == null) continue;
            if (sw.GetComponent<CircleCollider2D>() != null || sw.GetComponent<BoxCollider2D>() == null)
            {
                return true;
            }
            if (sw.transform.Find("SwitchIndicatorLight") == null)
            {
                return true;
            }
        }

        return false;
    }

    [MenuItem("Pamahiin/Repair BarangayMap (LightSwitches & Bake NavMesh)")]
    public static void RepairInteractive()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path != BARANGAY_SCENE_PATH)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            EditorSceneManager.OpenScene(BARANGAY_SCENE_PATH, OpenSceneMode.Single);
        }

        RepairCurrentBarangayScene(saveScene: true);
    }

    public static void RunBatchmodeRepair()
    {
        EditorSceneManager.OpenScene(BARANGAY_SCENE_PATH, OpenSceneMode.Single);
        bool ok = RepairCurrentBarangayScene(saveScene: true);
        if (!ok)
        {
            Debug.LogError("[BarangayMapFixer] Batchmode repair reported NavMesh bake issues.");
        }
    }

    public static bool RepairCurrentBarangayScene(bool saveScene)
    {
        Scene scene = SceneManager.GetActiveScene();

        // 1. Light switches: preserve exact manual scales, swap CircleCollider2D -> BoxCollider2D, ensure indicator Light2D.
        HouseLightSwitch[] switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Include);
        for (int i = 0; i < switches.Length; i++)
        {
            HouseLightSwitch sw = switches[i];
            if (sw == null) continue;
            sw.EnsureSwitchSetup();
            EditorUtility.SetDirty(sw);
            EditorUtility.SetDirty(sw.gameObject);
        }
        Debug.Log($"[BarangayMapFixer] Updated {switches.Length} light switch(es) preserving manual scales.");

        // 1b. Keep exterior zones (Road, FrontPorch, JeepneyBayWalkable) strictly Not Walkable (area = 1)
        string[] exteriorNonWalkableNames = { "BarangayRoadTile", "FrontPorch", "JeepneyBayWalkable" };
        foreach (string extName in exteriorNonWalkableNames)
        {
            GameObject extGo = GameObject.Find(extName);
            if (extGo == null) continue;
            NavMeshModifier mod = extGo.GetComponent<NavMeshModifier>() ?? extGo.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = 1;
            EditorUtility.SetDirty(mod);
            EditorUtility.SetDirty(extGo);
        }

        // 2. NavhMesh surface: ensure CollectSources2d + BuildNavMesh + save persistent NavMesh .asset.
        NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            GameObject env = GameObject.Find("Environment");
            GameObject nmGo = new GameObject("NavhMesh");
            if (env != null) nmGo.transform.SetParent(env.transform, false);
            surface = nmGo.AddComponent<NavMeshSurface>();
        }

        // Put NavhMesh near top of Environment so NavMeshSurface.OnEnable runs before Ghost Manager.
        if (surface.transform.parent != null)
        {
            surface.transform.SetAsFirstSibling();
        }

        BakeAndPersistNavMeshSurface(surface, string.IsNullOrEmpty(scene.path) ? BARANGAY_SCENE_PATH : scene.path);

        // Keep any NavMeshLink aligned with surface agentTypeID.
        NavMeshLink[] links = Object.FindObjectsByType<NavMeshLink>(FindObjectsInactive.Include);
        for (int i = 0; i < links.Length; i++)
        {
            if (links[i] == null) continue;
            links[i].agentTypeID = surface.agentTypeID;
            EditorUtility.SetDirty(links[i]);
        }

        // 3. Verify triangulation and sample exterior vs interior spots (walkable mask = 1 << 0).
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        int triCount = tri.indices != null ? tri.indices.Length / 3 : 0;
        bool porchWalk = NavMesh.SamplePosition(new Vector3(-0.10f, 0.06f, 0f), out _, 0.20f, 1 << 0);
        bool roadWalk = NavMesh.SamplePosition(new Vector3(-1.03f, 0.00f, 0f), out _, 0.25f, 1 << 0);
        bool bayWalk = NavMesh.SamplePosition(new Vector3(-0.82f, 0.00f, 0f), out _, 0.25f, 1 << 0);
        bool foyerWalk = NavMesh.SamplePosition(new Vector3(0.45f, 0.06f, 0f), out _, 0.25f, 1 << 0);
        bool basementWalk = NavMesh.SamplePosition(new Vector3(2.40f, -4.95f, 0f), out _, 0.25f, 1 << 0);
        Debug.Log($"[BarangayMapFixer] NavMesh baked: vertices={tri.vertices?.Length ?? 0}, triangles={triCount}, asset={AssetDatabase.GetAssetPath(surface.navMeshData)} | Walkable checks: porch={porchWalk}, road={roadWalk}, jeepBay={bayWalk}, foyer={foyerWalk}, basement={basementWalk}");

        // 4. Keep dormant HuntGhost off NavMeshAgent errors if a stale component exists in scene.
        HunterGhostBehavior[] hunters = Object.FindObjectsByType<HunterGhostBehavior>(FindObjectsInactive.Include);
        for (int i = 0; i < hunters.Length; i++)
        {
            if (hunters[i] == null) continue;
            NavMeshAgent strayAgent = hunters[i].GetComponent<NavMeshAgent>();
            if (strayAgent != null)
            {
                strayAgent.enabled = false;
                EditorUtility.SetDirty(strayAgent);
            }
        }

        // Move Ghost Handler root and ghost children to a valid walkable position if currently sitting off-NavMesh.
        GhostHandler ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null)
        {
            bool rootWalkable = NavMesh.SamplePosition(ghostHandler.transform.position, out _, 0.25f, 1 << 0);
            bool eventWalkable = ghostHandler.eventGhost == null || NavMesh.SamplePosition(ghostHandler.eventGhost.transform.position, out _, 0.25f, 1 << 0);
            bool hunterWalkable = ghostHandler.hunterGhost == null || NavMesh.SamplePosition(ghostHandler.hunterGhost.transform.position, out _, 0.25f, 1 << 0);

            if (!rootWalkable || !eventWalkable || !hunterWalkable)
            {
                Vector3 probePos = ghostHandler.transform.position;
                GhostRoomMarker[] markers = Object.FindObjectsByType<GhostRoomMarker>(FindObjectsInactive.Include);
                if (markers.Length > 0 && markers[0] != null)
                {
                    probePos = markers[0].transform.position;
                }
                if (NavMesh.SamplePosition(probePos, out NavMeshHit walkHit, 5f, 1 << 0))
                {
                    Vector3 walkPos = new Vector3(walkHit.position.x, walkHit.position.y, ghostHandler.transform.position.z);
                    if (!rootWalkable)
                    {
                        ghostHandler.transform.position = walkPos;
                        EditorUtility.SetDirty(ghostHandler.transform);
                    }
                    foreach (Transform ghostChild in new[] { ghostHandler.eventGhost?.transform, ghostHandler.hunterGhost?.transform })
                    {
                        if (ghostChild == null) continue;
                        if (!NavMesh.SamplePosition(ghostChild.position, out _, 0.25f, 1 << 0))
                        {
                            ghostChild.localPosition = Vector3.zero;
                            EditorUtility.SetDirty(ghostChild);
                        }
                    }
                    Debug.Log($"[BarangayMapFixer] Aligned Ghost Handler & ghost children onto walkable NavMesh point {walkHit.position}.");
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene && !string.IsNullOrEmpty(scene.path))
        {
            EditorSceneManager.SaveScene(scene);
        }

        if (!Application.isBatchMode && surface != null)
        {
            Selection.activeGameObject = surface.gameObject;
            SceneView.RepaintAll();
        }

        return triCount > 0;
    }

    /// <summary>
    /// Attaches CollectSources2d if missing, configures 2D baking parameters, synchronously
    /// builds the NavMesh, and persists the resulting NavMeshData asset alongside the scene.
    /// </summary>
    public static void BakeAndPersistNavMeshSurface(NavMeshSurface surface, string scenePath)
    {
        if (surface == null) return;

        CollectSources2d col2d = surface.GetComponent<CollectSources2d>();
        if (col2d == null)
        {
            col2d = surface.gameObject.AddComponent<CollectSources2d>();
        }
        col2d.overrideByGrid = false;
        col2d.compressBounds = false;
        col2d.overrideVector = Vector3.one;

        // Guarantee CollectSources2d is hooked into surface.NavMeshExtensions before synchronous BuildNavMesh.
        bool registered = false;
        for (int i = 0; i < surface.NavMeshExtensions.Count; i++)
        {
            if (surface.NavMeshExtensions[i] == col2d)
            {
                registered = true;
                break;
            }
        }
        if (!registered)
        {
            surface.NavMeshExtensions.Add(col2d, col2d.Order);
        }

        surface.transform.position = Vector3.zero;
        surface.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        surface.transform.localScale = Vector3.one;
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = ~0;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.01f;
        surface.defaultArea = 1; // Not Walkable by default; only interior rooms override to area 0

        surface.BuildNavMesh();

        if (surface.navMeshData != null && !string.IsNullOrEmpty(scenePath))
        {
            string sceneDir = Path.GetDirectoryName(scenePath)?.Replace("\\", "/");
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (!string.IsNullOrEmpty(sceneDir) && !string.IsNullOrEmpty(sceneName))
            {
                string targetFolder = $"{sceneDir}/{sceneName}";
                if (!AssetDatabase.IsValidFolder(targetFolder))
                {
                    AssetDatabase.CreateFolder(sceneDir, sceneName);
                }

                string assetPath = $"{targetFolder}/NavMesh-{surface.name}.asset";
                string duplicateAssetPath = $"{targetFolder}/NavMesh-{surface.name} 1.asset";
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(duplicateAssetPath) != null)
                {
                    AssetDatabase.DeleteAsset(duplicateAssetPath);
                }
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
                AssetDatabase.SaveAssets();
            }
        }

        surface.RemoveData();
        surface.AddData();
        EditorUtility.SetDirty(col2d);
        EditorUtility.SetDirty(surface);
        if (surface.navMeshData != null)
        {
            EditorUtility.SetDirty(surface.navMeshData);
        }
    }
}
