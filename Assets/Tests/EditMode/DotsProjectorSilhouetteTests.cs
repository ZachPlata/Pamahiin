using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class DotsProjectorSilhouetteTests
{
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private readonly List<GameObject> createdObjects = new List<GameObject>();

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

    [Test]
    public void DotsWalkSpriteSheetAndAnimationAssets_AreConfiguredProperly()
    {
        Assert.IsFalse(File.Exists("Assets/Sprites/CHARACTERS/Dots Silhouette.png"),
            "Legacy Assets/Sprites/CHARACTERS/Dots Silhouette.png should have been deleted.");
        Assert.IsFalse(File.Exists("Assets/Sprites/CHARACTERS/Dots Silhouette.png.meta"),
            "Legacy Dots Silhouette.png.meta should have been deleted.");

        Sprite[] rawSprites = Resources.LoadAll<Sprite>("Sprites/CHARACTERS/ghost_dots_walk_sheet");
        Assert.IsNotNull(rawSprites);
        Assert.AreEqual(8, rawSprites.Length, "ghost_dots_walk_sheet must slice into 8 frames.");

        Sprite[] sorted = rawSprites
            .OrderBy(s => ParseTrailingIndex(s.name))
            .ThenBy(s => s.name, StringComparer.Ordinal)
            .ToArray();

        for (int i = 0; i < 8; i++)
        {
            Sprite frame = sorted[i];
            Assert.AreEqual($"ghost_dots_walk_sheet_{i}", frame.name);
            Assert.AreEqual(64f, frame.rect.width, 0.01f);
            Assert.AreEqual(64f, frame.rect.height, 0.01f);
            Assert.AreEqual(100f, frame.pixelsPerUnit, 0.01f);

            Vector2 normalizedPivot = new Vector2(frame.pivot.x / frame.rect.width, frame.pivot.y / frame.rect.height);
            Assert.AreEqual(0.5f, normalizedPivot.x, 0.02f);
            Assert.AreEqual(0.5f, normalizedPivot.y, 0.02f);
        }

        Texture2D tex = sorted[0].texture;
        Assert.IsNotNull(tex);
        Assert.AreEqual(512, tex.width);
        Assert.AreEqual(64, tex.height);
        Assert.AreEqual(FilterMode.Point, tex.filterMode);

        // Verify frame 0 silhouette contains transparent gaps and strictly green laser pixels.
        Rect r0 = sorted[0].rect;
        Color32[] pixels = tex.GetPixels32();
        int nonTransparent = 0;
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int y = (int)r0.y; y < (int)(r0.y + r0.height); y++)
        {
            for (int x = (int)r0.x; x < (int)(r0.x + r0.width); x++)
            {
                Color32 c = pixels[y * tex.width + x];
                if (c.a == 0) continue;

                nonTransparent++;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                Assert.Greater(c.g, c.r, $"Expected green laser dot at ({x},{y}), got {c}");
                Assert.GreaterOrEqual(c.g, c.b, $"Expected green laser dot at ({x},{y}), got {c}");
            }
        }

        Assert.Greater(nonTransparent, 200, "Frame 0 must contain visible silhouette dots.");
        Assert.Less(nonTransparent, 64 * 64, "Frame 0 must contain transparent gaps between dots.");
        Assert.Greater(maxY - minY, 20, "Silhouette should span upright (+Y) body height.");

        RuntimeAnimatorController controller = Resources.Load<RuntimeAnimatorController>("Animations/DotsSilhouetteAnim");
        Assert.IsNotNull(controller, "DotsSilhouetteAnim.controller must be loadable from Resources.");

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Animations/DotsSilhouetteWalk.anim");
        Assert.IsNotNull(clip, "DotsSilhouetteWalk.anim must exist.");
        Assert.AreEqual(10f, clip.frameRate, 0.01f);
        Assert.IsTrue(clip.isLooping);

        EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        Assert.GreaterOrEqual(bindings.Length, 1);
        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
        Assert.GreaterOrEqual(keys.Length, 8);
    }

    [Test]
    public void DotsProjectorPrefab_HasDisabledSilhouetteVisualAndCalibratedCone()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DotsProjector.prefab");
        Assert.IsNotNull(prefab, "DotsProjector.prefab must exist.");

        Assert.AreEqual(0, prefab.GetComponentsInChildren<CircleCollider2D>(true).Length,
            "DotsProjector prefab should not contain legacy CircleCollider2D.");

        Type dotsType = ResolveRuntimeType("DotsProjectorItem");
        Component dotsComp = prefab.GetComponent(dotsType);
        Assert.IsNotNull(dotsComp, "DotsProjectorItem must exist on prefab root.");

        float projectionRadius = (float)dotsType.GetField("projectionRadius", InstanceFlags).GetValue(dotsComp);
        Assert.AreEqual(0.75f, projectionRadius, 0.01f);

        SpriteRenderer silhouetteRenderer = (SpriteRenderer)dotsType.GetProperty("SilhouetteRenderer", InstanceFlags).GetValue(dotsComp);
        Assert.IsNotNull(silhouetteRenderer, "silhouetteRenderer reference must be wired on prefab.");
        Assert.AreEqual("DotsSilhouetteVisual", silhouetteRenderer.gameObject.name);
        Assert.IsFalse(silhouetteRenderer.gameObject.activeSelf, "DotsSilhouetteVisual must start inactive.");
        Assert.AreEqual("DOTSLayer", silhouetteRenderer.sortingLayerName);
        Assert.AreEqual(5, silhouetteRenderer.sortingOrder);
        Assert.IsNotNull(silhouetteRenderer.sharedMaterial);
        Assert.IsTrue(silhouetteRenderer.sharedMaterial.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0,
            $"Expected Unlit sprite material, got {silhouetteRenderer.sharedMaterial.shader.name}");

        Animator silhouetteAnimator = (Animator)dotsType.GetProperty("SilhouetteAnimator", InstanceFlags).GetValue(dotsComp);
        Assert.IsNotNull(silhouetteAnimator, "silhouetteAnimator reference must be wired on prefab.");
        Assert.IsNotNull(silhouetteAnimator.runtimeAnimatorController);
        Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate, silhouetteAnimator.cullingMode);

        Component dotsLight = (Component)dotsType.GetProperty("DotsLight", InstanceFlags).GetValue(dotsComp);
        Assert.IsNotNull(dotsLight, "dotsLight reference must be wired on prefab.");
        Assert.AreEqual(1f, dotsLight.transform.localScale.x, 0.001f);
        Assert.AreEqual(1f, dotsLight.transform.localScale.y, 0.001f);
        Assert.AreEqual(1f, dotsLight.transform.localScale.z, 0.001f);

        PropertyInfo outerRadiusProp = dotsLight.GetType().GetProperty("pointLightOuterRadius", InstanceFlags);
        Assert.IsNotNull(outerRadiusProp);
        float outerRadius = (float)outerRadiusProp.GetValue(dotsLight);
        Assert.AreEqual(0.75f, outerRadius, 0.01f);
    }

    [Test]
    public void ComputeCompensatedLocalScale_AndWalkRotationAngle_WorkOverTinyRootScale()
    {
        Type dotsType = ResolveRuntimeType("DotsProjectorItem");
        MethodInfo scaleMethod = dotsType.GetMethod("ComputeCompensatedLocalScale", StaticFlags);
        MethodInfo angleMethod = dotsType.GetMethod("ComputeWalkRotationAngle", StaticFlags);
        Assert.IsNotNull(scaleMethod);
        Assert.IsNotNull(angleMethod);

        Vector3 parentLossy = new Vector3(0.0065f, 0.0065f, 0.0065f);
        Vector3 localScale = (Vector3)scaleMethod.Invoke(null, new object[] { parentLossy, 0.28f });
        Assert.AreEqual(0.28f, localScale.x * parentLossy.x, 0.001f);
        Assert.AreEqual(0.28f, localScale.y * parentLossy.y, 0.001f);
        Assert.AreEqual(0.28f, localScale.z * parentLossy.z, 0.001f);

        Vector3 negativeScale = (Vector3)scaleMethod.Invoke(null, new object[] { new Vector3(-0.0065f, 0.0065f, 0f), 0.28f });
        Assert.IsFalse(float.IsNaN(negativeScale.x) || float.IsInfinity(negativeScale.x));
        Assert.AreEqual(0.28f, negativeScale.x * 0.0065f, 0.001f);

        float northAngle = (float)angleMethod.Invoke(null, new object[] { Vector2.up });
        float eastAngle = (float)angleMethod.Invoke(null, new object[] { Vector2.right });
        float westAngle = (float)angleMethod.Invoke(null, new object[] { Vector2.left });
        float southAngle = (float)angleMethod.Invoke(null, new object[] { Vector2.down });

        Assert.AreEqual(0f, Mathf.DeltaAngle(0f, northAngle), 0.01f);
        Assert.AreEqual(0f, Mathf.DeltaAngle(-90f, eastAngle), 0.01f);
        Assert.AreEqual(0f, Mathf.DeltaAngle(90f, westAngle), 0.01f);
        Assert.AreEqual(0f, Mathf.DeltaAngle(180f, southAngle), 0.01f);
    }

    [Test]
    public void TryComputeConstrainedWalkSegment_ClipsAgainstWallsAndRoomBounds()
    {
        int wallLayer = LayerMask.NameToLayer("Walls");
        Assert.GreaterOrEqual(wallLayer, 0, "Walls layer must exist in TagManager.");

        // Wall right at x = 0.50 blocking the forward beam from (0, 0) aiming +X
        GameObject wallGo = CreateTestObject("TestWall");
        wallGo.layer = wallLayer;
        wallGo.transform.position = new Vector3(0.55f, 0f, 0f);
        BoxCollider2D wallCol = wallGo.AddComponent<BoxCollider2D>();
        wallCol.size = new Vector2(0.10f, 2f); // left edge at x = 0.50

        // Side wall at y = 0.18
        GameObject sideWallGo = CreateTestObject("TestSideWall");
        sideWallGo.layer = wallLayer;
        sideWallGo.transform.position = new Vector3(0.25f, 0.23f, 0f);
        BoxCollider2D sideWallCol = sideWallGo.AddComponent<BoxCollider2D>();
        sideWallCol.size = new Vector2(1f, 0.10f); // bottom edge at y = 0.18

        // Room bounds centered around origin
        GameObject roomGo = CreateTestObject("TestRoom");
        roomGo.transform.position = new Vector3(0.15f, 0f, 0f);
        BoxCollider2D roomCol = roomGo.AddComponent<BoxCollider2D>();
        roomCol.isTrigger = true;
        roomCol.size = new Vector2(0.70f, 0.36f); // x in [-0.20, 0.50], y in [-0.18, 0.18]

        Physics2D.SyncTransforms();

        Type dotsType = ResolveRuntimeType("DotsProjectorItem");
        MethodInfo segmentMethod = dotsType.GetMethod("TryComputeConstrainedWalkSegment", StaticFlags);
        Assert.IsNotNull(segmentMethod);

        object[] args =
        {
            Vector2.zero,
            Vector2.right,
            0.75f,
            30f,
            roomCol,
            false,
            1 << wallLayer,
            0.65f,
            0f,
            1,
            Vector2.zero,
            Vector2.zero
        };

        bool ok = (bool)segmentMethod.Invoke(null, args);
        Assert.IsTrue(ok, "Should find a valid clipped walk segment inside room and short of walls.");

        Vector2 startPos = (Vector2)args[10];
        Vector2 endPos = (Vector2)args[11];

        Assert.Less(startPos.x, 0.50f, "Start X must stop short of front wall.");
        Assert.Less(endPos.x, 0.50f, "End X must stop short of front wall.");
        Assert.Less(startPos.y, 0.18f, "Start Y must stop short of top wall.");
        Assert.Less(endPos.y, 0.18f, "End Y must stop short of top wall.");
        Assert.IsTrue(roomCol.OverlapPoint(startPos), "Start point must remain inside room bounds.");
        Assert.IsTrue(roomCol.OverlapPoint(endPos), "End point must remain inside room bounds.");
        Assert.GreaterOrEqual(Vector2.Distance(startPos, endPos), 0.08f);

        // Pressed flush against wall at x = 0.04 should return false cleanly
        wallGo.transform.position = new Vector3(0.09f, 0f, 0f); // left edge at 0.04
        Physics2D.SyncTransforms();

        object[] blockedArgs =
        {
            Vector2.zero,
            Vector2.right,
            0.75f,
            30f,
            roomCol,
            true,
            1 << wallLayer,
            0.5f,
            0f,
            1,
            Vector2.zero,
            Vector2.zero
        };

        bool blockedOk = (bool)segmentMethod.Invoke(null, blockedArgs);
        Assert.IsFalse(blockedOk, "Projector flush against a wall (< minimum clearance) should not project into the wall.");
    }

    [Test]
    public void DotsProjectorItem_CancellationGuards_ImmediatelyHideVisualOnPowerOffAndStow()
    {
        Type dotsType = ResolveRuntimeType("DotsProjectorItem");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DotsProjector.prefab");
        Assert.IsNotNull(prefab);

        GameObject dotsGo = UnityEngine.Object.Instantiate(prefab);
        createdObjects.Add(dotsGo);
        dotsGo.transform.position = Vector3.zero;
        Physics2D.SyncTransforms();

        Component dots = dotsGo.GetComponent(dotsType);
        Assert.IsNotNull(dots);
        dotsType.GetMethod("EnsureSilhouetteVisualInitialized", InstanceFlags).Invoke(dots, null);

        SetNetworkVarValue(dots, "isPoweredOn", true);
        SetNetworkVarValue(dots, "isPlaced", true);

        object[] trigArgs = { null, true, Vector2.zero, 1.8f };
        MethodInfo trigMethod = dotsType.GetMethod("TriggerDotsProjectionOnServer", InstanceFlags);
        bool triggered = (bool)trigMethod.Invoke(dots, trigArgs);
        Assert.IsTrue(triggered, "Powered placed projector should start silhouette projection.");

        SpriteRenderer sr = (SpriteRenderer)dotsType.GetProperty("SilhouetteRenderer", InstanceFlags).GetValue(dots);
        bool isProjecting = (bool)dotsType.GetProperty("IsProjectingSilhouette", InstanceFlags).GetValue(dots);
        Assert.IsTrue(isProjecting);
        Assert.IsTrue(sr.gameObject.activeSelf);

        // Guard A: Power off hides visual immediately
        SetNetworkVarValue(dots, "isPoweredOn", false);
        dotsType.GetMethod("UpdateVisuals", InstanceFlags).Invoke(dots, null);
        Assert.IsFalse((bool)dotsType.GetProperty("IsProjectingSilhouette", InstanceFlags).GetValue(dots));
        Assert.IsFalse(sr.gameObject.activeSelf);

        // Re-trigger while held in hand by an owner, then stow in pocket (isInHand = false while owned)
        SetNetworkVarValue(dots, "isPoweredOn", true);
        SetNetworkVarValue(dots, "isPlaced", false);
        SetNetworkVarValue(dots, "ownerClientId", 1UL);
        SetNetworkVarValue(dots, "isInHand", true);

        Assert.IsTrue((bool)trigMethod.Invoke(dots, new object[] { null, true, Vector2.zero, 1.8f }));
        Assert.IsTrue(sr.gameObject.activeSelf);

        // Guard B: Stowed in inventory immediately cancels projection
        SetNetworkVarValue(dots, "isInHand", false);
        dotsType.GetMethod("UpdateVisuals", InstanceFlags).Invoke(dots, null);
        Assert.IsFalse((bool)dotsType.GetProperty("IsProjectingSilhouette", InstanceFlags).GetValue(dots));
        Assert.IsFalse(sr.gameObject.activeSelf);
    }

    [Test]
    public void EventGhostBehavior_DotsManifestation_NeverRevealsOrMovesEventGhost_AndSandboxWorks()
    {
        Type ghostHandlerType = ResolveRuntimeType("GhostHandler");
        Type eventGhostType = ResolveRuntimeType("EventGhostBehavior");
        Type dotsType = ResolveRuntimeType("DotsProjectorItem");
        Type eventEnumType = eventGhostType.GetNestedType("GhostEventType", BindingFlags.Public);
        Assert.IsNotNull(ghostHandlerType);
        Assert.IsNotNull(eventGhostType);
        Assert.IsNotNull(eventEnumType);

        // Setup GhostHandler without DOTS evidence
        GameObject handlerGo = CreateTestObject("TestGhostHandler");
        Component ghostHandler = handlerGo.AddComponent(ghostHandlerType);

        GameObject roomGo = CreateTestObject("TestGhostRoom");
        roomGo.transform.position = new Vector3(-20f, -20f, 0f);
        BoxCollider2D roomCol = roomGo.AddComponent<BoxCollider2D>();
        roomCol.isTrigger = true;
        roomCol.size = new Vector2(2f, 2f);
        ghostHandlerType.GetField("ghostroomMarker", InstanceFlags).SetValue(ghostHandler, roomCol);

        // Setup EventGhost at (10, 10) with hidden SpriteRenderer
        GameObject ghostGo = CreateTestObject("TestEventGhost");
        Vector3 initialGhostPos = new Vector3(10f, 10f, 0f);
        ghostGo.transform.position = initialGhostPos;
        SpriteRenderer ghostSr = ghostGo.AddComponent<SpriteRenderer>();
        ghostSr.enabled = false;
        Component eventGhost = ghostGo.AddComponent(eventGhostType);
        eventGhostType.GetField("ghostHandler", InstanceFlags).SetValue(eventGhost, ghostHandler);

        // Setup powered DOTS projector OUTSIDE the ghost room (at 2, 3)
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DotsProjector.prefab");
        GameObject dotsGo = UnityEngine.Object.Instantiate(prefab);
        createdObjects.Add(dotsGo);
        dotsGo.transform.position = new Vector3(2f, 3f, 0f);
        Physics2D.SyncTransforms();

        Component dots = dotsGo.GetComponent(dotsType);
        dotsType.GetMethod("EnsureSilhouetteVisualInitialized", InstanceFlags).Invoke(dots, null);
        SetNetworkVarValue(dots, "isPoweredOn", true);
        SetNetworkVarValue(dots, "isPlaced", true);

        object dotsEventValue = Enum.Parse(eventEnumType, "DotsManifestation");

        // Natural room selector should NOT pick an out-of-room projector
        MethodInfo selectMethod = eventGhostType.GetMethod("SelectDotsProjectorForEvent", InstanceFlags);
        object[] naturalArgs = { false, new Vector2(10f, 10f), 5f, false };
        object naturalMatch = selectMethod.Invoke(eventGhost, naturalArgs);
        Assert.IsNull(naturalMatch, "Natural gameplay should not trigger DOTS outside the ghost room.");

        // ForceDotsBtn sandbox mode (ExecuteForcedEvent) SHOULD trigger on powered projector regardless of evidence or room
        MethodInfo forcedMethod = eventGhostType.GetMethod("ExecuteForcedEvent", InstanceFlags);
        forcedMethod.Invoke(eventGhost, new[] { dotsEventValue });

        SpriteRenderer dotsSr = (SpriteRenderer)dotsType.GetProperty("SilhouetteRenderer", InstanceFlags).GetValue(dots);
        Assert.IsTrue((bool)dotsType.GetProperty("IsProjectingSilhouette", InstanceFlags).GetValue(dots),
            "Sandbox ForceDotsBtn should start DOTS silhouette on powered projector anywhere.");
        Assert.IsTrue(dotsSr.gameObject.activeSelf);

        // EventGhost must stay invisible and unmoved!
        Assert.IsFalse(ghostSr.enabled, "EventGhost SpriteRenderer must stay disabled during DOTS manifestation.");
        Assert.IsFalse((bool)GetNetworkVarValue(eventGhost, "isVisiblyManifested"), "EventGhost isVisiblyManifested must stay false.");
        Assert.AreEqual(initialGhostPos, ghostGo.transform.position, "EventGhost must not teleport during DOTS.");

        // Canceling on hunt/exorcism must immediately hide the active silhouette
        eventGhostType.GetMethod("CancelActiveDotsProjectionOnServer", InstanceFlags).Invoke(eventGhost, null);
        Assert.IsFalse((bool)dotsType.GetProperty("IsProjectingSilhouette", InstanceFlags).GetValue(dots));
        Assert.IsFalse(dotsSr.gameObject.activeSelf);
    }

    private GameObject CreateTestObject(string name)
    {
        GameObject go = new GameObject(name);
        createdObjects.Add(go);
        return go;
    }

    private static Type ResolveRuntimeType(string typeName)
    {
        Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
            ?? Assembly.Load("Assembly-CSharp");

        Type resolved = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
        Assert.IsNotNull(resolved, $"Could not resolve runtime type '{typeName}'.");
        return resolved;
    }

    private static void SetNetworkVarValue(object target, string fieldName, object value)
    {
        FieldInfo field = FindFieldInHierarchy(target.GetType(), fieldName);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
        object netVar = field.GetValue(target);
        Assert.IsNotNull(netVar, $"Field '{fieldName}' was null.");
        PropertyInfo valProp = netVar.GetType().GetProperty("Value", InstanceFlags);
        Assert.IsNotNull(valProp);
        valProp.SetValue(netVar, value);
    }

    private static object GetNetworkVarValue(object target, string fieldName)
    {
        FieldInfo field = FindFieldInHierarchy(target.GetType(), fieldName);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
        object netVar = field.GetValue(target);
        Assert.IsNotNull(netVar);
        return netVar.GetType().GetProperty("Value", InstanceFlags).GetValue(netVar);
    }

    private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(fieldName, InstanceFlags);
            if (f != null) return f;
        }
        return null;
    }

    private static int ParseTrailingIndex(string spriteName)
    {
        int underscore = spriteName.LastIndexOf('_');
        return underscore >= 0 && int.TryParse(spriteName.Substring(underscore + 1), out int idx) ? idx : int.MaxValue;
    }
}
