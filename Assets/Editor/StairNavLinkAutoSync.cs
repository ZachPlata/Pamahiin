using NavMeshPlus.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Keeps "StairNavMeshLink" (NavMeshLink) endpoints locked to the "ArrivalFromBasement" (Main Floor landing)
/// and "ArrivalFromMainFloor" (Basement landing) markers in real time while editing.
/// Move either marker in the Scene view / Inspector and the link line follows immediately.
/// </summary>
[InitializeOnLoad]
public static class StairNavLinkAutoSync
{
    public const string LinkName = "StairNavMeshLink";
    public const string StartMarkerName = "ArrivalFromBasement";   // Main Floor landing
    public const string EndMarkerName = "ArrivalFromMainFloor";    // Basement landing

    private const string PrefKey = "StairNavLinkAutoSync.Enabled";

    private static NavMeshLink link;
    private static Transform startMarker;
    private static Transform endMarker;
    private static double nextLookupTime;

    static StairNavLinkAutoSync()
    {
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Stairs/Auto-Sync NavMesh Link")]
    private static void ToggleAutoSync()
    {
        EditorPrefs.SetBool(PrefKey, !EditorPrefs.GetBool(PrefKey, true));
    }

    [MenuItem("Tools/Stairs/Auto-Sync NavMesh Link", true)]
    private static bool ToggleAutoSyncValidate()
    {
        Menu.SetChecked("Tools/Stairs/Auto-Sync NavMesh Link", EditorPrefs.GetBool(PrefKey, true));
        return true;
    }

    [MenuItem("Tools/Stairs/Sync NavMesh Link Now")]
    public static void SyncNow()
    {
        ResolveReferences(force: true);
        Apply();
    }

    private static void Tick()
    {
        if (!EditorPrefs.GetBool(PrefKey, true)) return;

        ResolveReferences(force: false);
        Apply();
    }

    private static void ResolveReferences(bool force)
    {
        bool missing = link == null || startMarker == null || endMarker == null;
        if (!force && !missing) return;
        if (!force && EditorApplication.timeSinceStartup < nextLookupTime) return;
        nextLookupTime = EditorApplication.timeSinceStartup + 1.0;

        if (link == null)
        {
            foreach (var l in Object.FindObjectsByType<NavMeshLink>(FindObjectsInactive.Include))
            {
                if (l != null && l.gameObject.name == LinkName)
                {
                    link = l;
                    break;
                }
            }
        }

        if (startMarker == null) startMarker = FindTransform(StartMarkerName);
        if (endMarker == null) endMarker = FindTransform(EndMarkerName);
    }

    private static Transform FindTransform(string objectName)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t != null && t.name == objectName && t.gameObject.scene.IsValid()) return t;
        }
        return null;
    }

    private static void Apply()
    {
        if (link == null || startMarker == null || endMarker == null) return;

        Vector3 newStart = link.transform.InverseTransformPoint(startMarker.position);
        Vector3 newEnd = link.transform.InverseTransformPoint(endMarker.position);
        newStart.z = link.startPoint.z;
        newEnd.z = link.endPoint.z;

        bool startChanged = (newStart - link.startPoint).sqrMagnitude > 1e-8f;
        bool endChanged = (newEnd - link.endPoint).sqrMagnitude > 1e-8f;
        if (!startChanged && !endChanged) return;

        if (!Application.isPlaying)
        {
            Undo.RecordObject(link, "Sync Stair NavMesh Link");
        }

        if (startChanged) link.startPoint = newStart;
        if (endChanged) link.endPoint = newEnd;

        EditorUtility.SetDirty(link);
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(link.gameObject.scene);
        }
        SceneView.RepaintAll();
    }
}
