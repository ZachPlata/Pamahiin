using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlayerDeathSpectatorTests
{
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private static Assembly runtimeAssembly;

    private static Assembly RuntimeAssembly
    {
        get
        {
            if (runtimeAssembly == null)
            {
                runtimeAssembly = Assembly.Load("Assembly-CSharp");
            }
            return runtimeAssembly;
        }
    }

    private static Type ResolveRuntimeType(string typeName)
    {
        Type t = RuntimeAssembly.GetType(typeName, throwOnError: true);
        Assert.IsNotNull(t, $"Expected runtime type '{typeName}' in Assembly-CSharp.");
        return t;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            if (spawnedObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(spawnedObjects[i]);
            }
        }
        spawnedObjects.Clear();
    }

    private GameObject Track(GameObject go)
    {
        spawnedObjects.Add(go);
        return go;
    }

    [Test]
    public void LayerConfiguration_GhostSpectatorLayer11_MatchesSpec()
    {
        int layer = LayerMask.NameToLayer("GhostSpectator");
        Assert.AreEqual(11, layer, "Layer 11 should be named GhostSpectator.");
        Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(layer, layer), "Dead spectators should ignore each other.");
        Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(layer, 9), "GhostSpectator should ignore GhostLayer (9).");
        Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(layer, 10), "GhostSpectator should ignore GhostProp (10).");
        Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(layer, 7), "GhostSpectator should still collide with Walls (7).");
        Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(layer, 8), "GhostSpectator should still overlap Interactable stairs/triggers (8).");
        Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(layer, 0), "GhostSpectator should still overlap Default roof/zone triggers (0).");
    }

    [Test]
    public void InventoryTightDrop_DropsWithinBodyRadiusAndClampsAtWalls()
    {
        Type invType = ResolveRuntimeType("PlayerInventory");
        int maxSlots = (int)invType.GetField("MaxSlots", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        MethodInfo computeDrop = invType.GetMethod("ComputeTightDropPosition", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(computeDrop);

        Vector3 origin = Vector3.zero;
        for (int slot = 0; slot < maxSlots; slot++)
        {
            Vector3 freePos = (Vector3)computeDrop.Invoke(null, new object[] { origin, slot, 0 });
            float dist = Vector3.Distance(origin, freePos);
            Assert.Greater(dist, 0.05f, $"Slot {slot} drop should step slightly away from center.");
            Assert.LessOrEqual(dist, 0.11f, $"Slot {slot} drop should remain tightly within 0.10u of death body.");
        }

        // Verify wall raycast clamping blocks dropping on the far side of a Wall (Layer 7)
        Vector3 freeSlot0 = (Vector3)computeDrop.Invoke(null, new object[] { origin, 0, 0 });
        Vector2 dir = ((Vector2)(freeSlot0 - origin)).normalized;

        GameObject wallGo = Track(new GameObject("TestWall_Drop"));
        wallGo.layer = 7;
        wallGo.transform.position = (Vector3)(dir * 0.04f);
        var box = wallGo.AddComponent<BoxCollider2D>();
        box.size = new Vector2(0.02f, 0.4f);
        Physics2D.SyncTransforms();

        Vector3 clampedPos = (Vector3)computeDrop.Invoke(null, new object[] { origin, 0, 1 << 7 });
        Assert.Less(Vector3.Distance(origin, clampedPos), Vector3.Distance(origin, freeSlot0),
            "Tight drop should clamp before intersecting a Layer 7 wall collider.");
    }

    [Test]
    public void CorpseDrag_WallClampStopsAtWallsAndAllowsOpenSpace()
    {
        Type corpseType = ResolveRuntimeType("PlayerCorpse");
        MethodInfo clampMethod = corpseType.GetMethod("ClampDragTargetAgainstWalls", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(clampMethod);

        Vector2 start = Vector2.zero;
        Vector2 desired = new Vector2(1.0f, 0f);

        Vector2 openResult = (Vector2)clampMethod.Invoke(null, new object[] { start, desired, 0 });
        Assert.AreEqual(desired.x, openResult.x, 0.001f);
        Assert.AreEqual(desired.y, openResult.y, 0.001f);

        GameObject wallGo = Track(new GameObject("TestWall_Drag"));
        wallGo.layer = 7;
        wallGo.transform.position = new Vector3(0.4f, 0f, 0f);
        var box = wallGo.AddComponent<BoxCollider2D>();
        box.size = new Vector2(0.05f, 1.0f);
        Physics2D.SyncTransforms();

        Vector2 clampedResult = (Vector2)clampMethod.Invoke(null, new object[] { start, desired, 1 << 7 });
        Assert.Less(clampedResult.x, 0.4f, "Corpse drag target must clamp ahead of wall collider.");
    }

    [Test]
    public void SpectatorEquipmentWhitelist_PermitsOnGroundGearAndBlocksPillsChalkAndPlaced()
    {
        Type playerType = ResolveRuntimeType("PlayerController");
        MethodInfo allowMethod = playerType.GetMethod("IsEquipmentAllowedForGhostSpectator", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(allowMethod);

        Type equipBaseType = ResolveRuntimeType("EquipmentItem");
        PropertyInfo isOnGroundProp = equipBaseType.GetProperty("IsOnGround", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(isOnGroundProp);

        GameObject flashGo = Track(new GameObject("TestFlashlight"));
        Component flashlight = flashGo.AddComponent(ResolveRuntimeType("FlashlightItem"));
        Assert.IsTrue((bool)isOnGroundProp.GetValue(flashlight), "Unplaced, unowned FlashlightItem should be IsOnGround.");
        Assert.IsTrue((bool)allowMethod.Invoke(null, new object[] { flashlight }),
            "Dead spectators can pick up/drop ground non-consumable equipment like FlashlightItem.");

        GameObject pillsGo = Track(new GameObject("TestPills"));
        Component pills = pillsGo.AddComponent(ResolveRuntimeType("SanityPillsItem"));
        Assert.IsFalse((bool)allowMethod.Invoke(null, new object[] { pills }),
            "Dead spectators must NOT pick up or use SanityPillsItem.");

        GameObject chalkGo = Track(new GameObject("TestChalk"));
        Component chalk = chalkGo.AddComponent(ResolveRuntimeType("RitualChalkItem"));
        Assert.IsFalse((bool)allowMethod.Invoke(null, new object[] { chalk }),
            "Dead spectators must NOT pick up or use RitualChalkItem.");

        Assert.IsFalse((bool)allowMethod.Invoke(null, new object[] { null }),
            "Null equipment must return false.");
    }

    [Test]
    public void TeamWipeGraceDuration_MatchesDecisionA2()
    {
        Type matchType = ResolveRuntimeType("GameMatchManager");
        MethodInfo graceMethod = matchType.GetMethod("GetTeamWipeGraceDuration", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(graceMethod);

        Assert.AreEqual(3f, (float)graceMethod.Invoke(null, new object[] { true }), 0.001f,
            "Multiplayer team wipe grace should be 3s.");
        Assert.AreEqual(15f, (float)graceMethod.Invoke(null, new object[] { false }), 0.001f,
            "Solo / Editor test team wipe grace should be 15s.");
    }

    [Test]
    public void DeceasedPayoutMultiplier_AppliesFiftyPercentPenaltyCleanly()
    {
        const string keyInit = "Pamahiin_Currency_Initialized_v2";
        const string keyCurr = "Pamahiin_Currency_v2";
        const string keyDiff = "Pamahiin_Difficulty_v1";

        bool hadInit = PlayerPrefs.HasKey(keyInit);
        int prevInit = PlayerPrefs.GetInt(keyInit, 0);
        bool hadCurr = PlayerPrefs.HasKey(keyCurr);
        int prevCurr = PlayerPrefs.GetInt(keyCurr, 0);
        bool hadDiff = PlayerPrefs.HasKey(keyDiff);
        int prevDiff = PlayerPrefs.GetInt(keyDiff, 0);

        Type mgrType = ResolveRuntimeType("ProgressionAndLoadoutManager");
        Type diffEnum = ResolveRuntimeType("ContractDifficulty");
        Type breakdownType = ResolveRuntimeType("MatchPayoutBreakdown");

        var mgrGo = Track(new GameObject("TestProgressionMgr"));
        Component mgr = mgrGo.AddComponent(mgrType);
        MethodInfo setDiff = mgrType.GetMethod("SetDifficulty", BindingFlags.Public | BindingFlags.Instance);
        setDiff.Invoke(mgr, new object[] { Enum.Parse(diffEnum, "Amateur") });

        MethodInfo calcMethod = mgrType.GetMethod("CalculateAndAwardMatchPayout", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(calcMethod);

        try
        {
            object aliveBreakdown = calcMethod.Invoke(mgr, new object[] { 2, true, false, false });
            object deadBreakdown = calcMethod.Invoke(mgr, new object[] { 2, true, false, true });

            FieldInfo isDeceasedField = breakdownType.GetField("isDeceased");
            FieldInfo survMultField = breakdownType.GetField("survivalMultiplier");
            FieldInfo totalField = breakdownType.GetField("totalEarned");

            // Base = 2*35 + 100 = 170; Amateur = 1.0x -> Alive = 170, Deceased = 85
            Assert.IsFalse((bool)isDeceasedField.GetValue(aliveBreakdown));
            Assert.AreEqual(1.0f, (float)survMultField.GetValue(aliveBreakdown), 0.001f);
            Assert.AreEqual(170, (int)totalField.GetValue(aliveBreakdown));

            Assert.IsTrue((bool)isDeceasedField.GetValue(deadBreakdown));
            Assert.AreEqual(0.5f, (float)survMultField.GetValue(deadBreakdown), 0.001f);
            Assert.AreEqual(85, (int)totalField.GetValue(deadBreakdown));
        }
        finally
        {
            if (hadInit) PlayerPrefs.SetInt(keyInit, prevInit); else PlayerPrefs.DeleteKey(keyInit);
            if (hadCurr) PlayerPrefs.SetInt(keyCurr, prevCurr); else PlayerPrefs.DeleteKey(keyCurr);
            if (hadDiff) PlayerPrefs.SetInt(keyDiff, prevDiff); else PlayerPrefs.DeleteKey(keyDiff);
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void BurialMapStage_BuildStageHierarchyAndEnsure_SupportsPitAndLunge()
    {
        Type stageType = ResolveRuntimeType("BurialMapStage");
        bool existedInScene = UnityEngine.Object.FindObjectsByType(stageType, FindObjectsInactive.Include).Length > 0;

        MethodInfo ensureMethod = stageType.GetMethod("EnsureAndGet", BindingFlags.Public | BindingFlags.Static);
        Component ensuredStage = (Component)ensureMethod.Invoke(null, null);
        Assert.IsNotNull(ensuredStage);
        if (!existedInScene)
        {
            Track(ensuredStage.gameObject);
        }

        // Also test building a fresh isolated stage hierarchy directly
        GameObject tempGo = Track(new GameObject("TestBurialMapStage"));
        Component stage = tempGo.AddComponent(stageType);
        MethodInfo buildHierarchy = stageType.GetMethod("BuildStageHierarchy", BindingFlags.Public | BindingFlags.Static);
        buildHierarchy.Invoke(null, new object[] { tempGo });

        Assert.AreEqual(new Vector3(25f, 25f, 0f), stage.transform.position);
        Assert.IsNotNull(stage.transform.Find("BurialStageAnchor"));
        Assert.IsNotNull(stage.transform.Find("PitFloor"));
        Assert.IsNotNull(stage.transform.Find("BurialGhost"));

        MethodInfo setActive = stageType.GetMethod("SetStageActive", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo setLunge = stageType.GetMethod("SetLungeProgress", BindingFlags.Public | BindingFlags.Instance);
        SpriteRenderer ghostSr = stage.transform.Find("BurialGhost").GetComponent<SpriteRenderer>();

        setActive.Invoke(stage, new object[] { true });
        Assert.IsTrue(ghostSr.enabled, "BurialGhost renderer should enable when stage is active.");

        setLunge.Invoke(stage, new object[] { 0.5f });
        setActive.Invoke(stage, new object[] { false });
        Assert.IsFalse(ghostSr.enabled, "BurialGhost renderer should disable when stage is deactivated.");
    }

    [Test]
    public void SlicedSpriteSheets_AndAnimationAssets_ExistWithExpectedCounts()
    {
        Sprite[] handSprites = Resources.LoadAll<Sprite>("Sprites/UI/death_ghost_hand_sheet");
        Assert.AreEqual(20, handSprites.Length, "death_ghost_hand_sheet should be sliced into 20 sprites.");

        Sprite[] jumpscareSprites = Resources.LoadAll<Sprite>("Sprites/UI/burial_jumpscare_sheet");
        Assert.AreEqual(12, jumpscareSprites.Length, "burial_jumpscare_sheet should be sliced into 12 sprites.");

        Sprite[] chokeSprites = Resources.LoadAll<Sprite>("Sprites/CHARACTERS/player_death_choke_sheet");
        Assert.AreEqual(12, chokeSprites.Length, "player_death_choke_sheet should be sliced into 12 sprites.");

        Sprite[] ragdollSprites = Resources.LoadAll<Sprite>("Sprites/CHARACTERS/player_corpse_ragdoll_sheet");
        Assert.AreEqual(6, ragdollSprites.Length, "player_corpse_ragdoll_sheet should be sliced into 6 ragdoll part sprites.");

        var partNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Sprite s in ragdollSprites)
        {
            partNames.Add(s.name);
        }
        Assert.IsTrue(partNames.Contains("Corpse_Torso"));
        Assert.IsTrue(partNames.Contains("Corpse_Head"));
        Assert.IsTrue(partNames.Contains("Corpse_LeftArm"));
        Assert.IsTrue(partNames.Contains("Corpse_RightArm"));
        Assert.IsTrue(partNames.Contains("Corpse_LeftLeg"));
        Assert.IsTrue(partNames.Contains("Corpse_RightLeg"));

        Assert.IsNotNull(Resources.Load<RuntimeAnimatorController>("Animations/CorpseAnim"),
            "CorpseAnim.controller should exist in Resources/Animations.");
        Assert.IsNotNull(Resources.Load<RuntimeAnimatorController>("Animations/DeathOverlayAnim"),
            "DeathOverlayAnim.controller should exist in Resources/Animations.");

        Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Animations/CorpseChoke.anim"));
        Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Animations/DeathGhostHands.anim"));
        Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Animations/BurialJumpscare.anim"));
    }

    [Test]
    public void PlayerCorpsePrefab_HasCalibratedScaleDOTSLayerPolygonAndSixPartRagdoll()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerCorpse.prefab");
        Assert.IsNotNull(prefab, "PlayerCorpse.prefab must exist.");

        Assert.IsNull(prefab.GetComponent<BoxCollider2D>(), "Legacy BoxCollider2D should be removed from PlayerCorpse.prefab.");
        PolygonCollider2D poly = prefab.GetComponent<PolygonCollider2D>();
        Assert.IsNotNull(poly, "PlayerCorpse.prefab must have a PolygonCollider2D trigger.");
        Assert.IsTrue(poly.isTrigger, "PlayerCorpse PolygonCollider2D must be a trigger.");

        SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
        Assert.IsNotNull(sr);
        Assert.AreEqual("DOTSLayer", sr.sortingLayerName);
        Assert.AreEqual(-1, sr.sortingOrder);

        Animator anim = prefab.GetComponent<Animator>();
        Assert.IsNotNull(anim);
        Assert.IsNotNull(anim.runtimeAnimatorController, "PlayerCorpse Animator should have CorpseAnim assigned.");

        GameObject instance = Track(UnityEngine.Object.Instantiate(prefab));
        Physics2D.SyncTransforms();
        PolygonCollider2D instPoly = instance.GetComponent<PolygonCollider2D>();
        Vector3 boundsSize = instPoly.bounds.size;
        Assert.GreaterOrEqual(boundsSize.x, 0.07f);
        Assert.LessOrEqual(boundsSize.x, 0.11f);
        Assert.GreaterOrEqual(boundsSize.y, 0.11f);
        Assert.LessOrEqual(boundsSize.y, 0.15f);

        Type ragdollType = ResolveRuntimeType("CorpseRagdollController");
        Component ragdoll = instance.GetComponent(ragdollType);
        Assert.IsNotNull(ragdoll, "PlayerCorpse should have CorpseRagdollController.");

        ragdollType.GetMethod("ActivateRagdoll", BindingFlags.Public | BindingFlags.Instance)
            .Invoke(ragdoll, new object[] { true });

        Rigidbody2D torso = (Rigidbody2D)ragdollType.GetProperty("TorsoBody").GetValue(ragdoll);
        Assert.IsNotNull(torso);
        Assert.AreEqual(RigidbodyType2D.Kinematic, torso.bodyType);

        var joints = (IReadOnlyList<HingeJoint2D>)ragdollType.GetProperty("LimbJoints").GetValue(ragdoll);
        var bodies = (IReadOnlyList<Rigidbody2D>)ragdollType.GetProperty("LimbBodies").GetValue(ragdoll);
        var renderers = (IReadOnlyList<SpriteRenderer>)ragdollType.GetProperty("AllPartRenderers").GetValue(ragdoll);

        Assert.AreEqual(5, joints.Count, "Expected 5 articulated HingeJoint2D limbs attached to Torso.");
        Assert.AreEqual(5, bodies.Count, "Expected 5 Dynamic Rigidbody2D limbs.");
        Assert.AreEqual(6, renderers.Count, "Expected 6 SpriteRenderers across Torso + 5 limbs.");

        foreach (SpriteRenderer partSr in renderers)
        {
            Assert.AreEqual("DOTSLayer", partSr.sortingLayerName);
            Assert.AreEqual(-1, partSr.sortingOrder);
        }

        foreach (HingeJoint2D j in joints)
        {
            Assert.AreEqual(torso, j.connectedBody);
            Assert.IsTrue(j.useLimits, "Ragdoll HingeJoint2D should enforce anatomical angle limits.");
        }

        MethodInfo setDragging = ragdollType.GetMethod("SetDragging", BindingFlags.Public | BindingFlags.Instance);
        setDragging.Invoke(ragdoll, new object[] { true });
        Assert.AreEqual(5.5f, bodies[0].linearDamping, 0.01f);
        Assert.AreEqual(3.5f, bodies[0].angularDamping, 0.01f);

        setDragging.Invoke(ragdoll, new object[] { false });
        Assert.AreEqual(14f, bodies[0].linearDamping, 0.01f);
        Assert.AreEqual(10f, bodies[0].angularDamping, 0.01f);
    }

    [Test]
    public void DeathSequenceController_ConfiguresImageAspectRatioFitterAndAnimator()
    {
        Type seqType = ResolveRuntimeType("DeathSequenceController");
        GameObject go = Track(new GameObject("TestDeathSequence"));
        Component seq = go.AddComponent(seqType);

        seqType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(seq, null);

        object overlayImg = seqType.GetProperty("SheetOverlayImage").GetValue(seq);
        Assert.IsNotNull(overlayImg);
        Assert.AreEqual("Image", overlayImg.GetType().Name, "SheetOverlay should use UnityEngine.UI.Image, not RawImage.");

        object fitter = seqType.GetProperty("SheetAspectFitter").GetValue(seq);
        Assert.IsNotNull(fitter);
        Assert.AreEqual("EnvelopeParent", fitter.GetType().GetProperty("aspectMode").GetValue(fitter).ToString());
        Assert.AreEqual(16f / 9f, (float)fitter.GetType().GetProperty("aspectRatio").GetValue(fitter), 0.001f);

        Animator anim = (Animator)seqType.GetProperty("SheetAnimator").GetValue(seq);
        Assert.IsNotNull(anim);
        Assert.IsNotNull(anim.runtimeAnimatorController, "SheetOverlay Animator should load DeathOverlayAnim.");

        Type rawImageType = Type.GetType("UnityEngine.UI.RawImage, UnityEngine.UI");
        if (rawImageType != null)
        {
            Assert.IsNull(go.GetComponentInChildren(rawImageType, true), "No RawImage should remain under DeathSequenceController.");
        }
    }

    [Test]
    public void GhostSpectatorOverlays_KeepCenterTransparentAndLightVignette()
    {
        Material fogMat = Resources.Load<Material>("Materials/GhostSpectatorFog");
        Assert.IsNotNull(fogMat, "GhostSpectatorFog.mat must exist in Resources/Materials.");
        Assert.LessOrEqual(fogMat.GetColor("_Color").a, 0.12f, "Center tint alpha should stay subtle.");
        Assert.LessOrEqual(fogMat.GetFloat("_Intensity"), 0.60f, "Fog overlay intensity should not obscure center playfield.");

        Type profileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
        Assert.IsNotNull(profileType);
        UnityEngine.Object profile = Resources.Load("Settings/GhostSpectatorVolumeProfile", profileType);
        Assert.IsNotNull(profile, "GhostSpectatorVolumeProfile must exist in Resources/Settings.");

        var components = (System.Collections.IList)profileType.GetField("components").GetValue(profile);
        object vignetteComp = null;
        foreach (object comp in components)
        {
            if (comp != null && comp.GetType().Name == "Vignette")
            {
                vignetteComp = comp;
                break;
            }
        }
        Assert.IsNotNull(vignetteComp, "GhostSpectatorVolumeProfile should contain a Vignette override.");
        object intensityParam = vignetteComp.GetType().GetField("intensity").GetValue(vignetteComp);
        float vignetteIntensity = (float)intensityParam.GetType().GetProperty("value").GetValue(intensityParam);
        Assert.LessOrEqual(vignetteIntensity, 0.20f, "Spectator post-processing vignette should remain lightweight.");
    }

    [Test]
    public void SafeZone_IsInSafeZone_ClearsStickyStateWhenSteppingOutsideBounds()
    {
        Type zoneType = ResolveRuntimeType("SafeZone");
        Type playerType = ResolveRuntimeType("PlayerController");

        GameObject zoneGo = Track(new GameObject("JeepneySafeZone_Test"));
        zoneGo.transform.position = Vector3.zero;
        BoxCollider2D zoneCol = zoneGo.AddComponent<BoxCollider2D>();
        zoneCol.size = new Vector2(2f, 2f);
        zoneCol.isTrigger = true;
        Component zone = zoneGo.AddComponent(zoneType);
        zoneType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(zone, null);
        zoneType.GetMethod("RefreshColliders", BindingFlags.Public | BindingFlags.Instance)?.Invoke(zone, null);

        GameObject playerGo = Track(new GameObject("SafeZonePlayer_Test"));
        Component player = playerGo.AddComponent(playerType);
        PropertyInfo isInSafeZoneProp = playerType.GetProperty("IsInSafeZone", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo activeZonesField = playerType.GetField("activeSafeZones", BindingFlags.NonPublic | BindingFlags.Instance);

        playerGo.transform.position = Vector3.zero;
        Physics2D.SyncTransforms();
        Assert.IsTrue((bool)isInSafeZoneProp.GetValue(player), "Player inside collider bounds should report IsInSafeZone == true.");

        // Simulate sticky trigger cache where Exit wasn't fired, then step 0.05u outside [-1..1] bounds.
        var activeSet = activeZonesField.GetValue(player);
        activeSet.GetType().GetMethod("Add").Invoke(activeSet, new object[] { zone });

        playerGo.transform.position = new Vector3(1.05f, 0f, 0f);
        Physics2D.SyncTransforms();
        Assert.IsFalse((bool)isInSafeZoneProp.GetValue(player), "Player outside SafeZone bounds must NOT remain latched in safe zone.");
        Assert.AreEqual(0, (int)activeSet.GetType().GetProperty("Count").GetValue(activeSet),
            "Non-overlapping SafeZone entry should be evicted from activeSafeZones.");
    }

    [Test]
    public void HunterGhostBehavior_AndPlayerInventory_DetectRemoteClientActiveFlashlightWithoutLocalSlotCache()
    {
        Type playerType = ResolveRuntimeType("PlayerController");
        Type invType = ResolveRuntimeType("PlayerInventory");
        Type equipType = ResolveRuntimeType("EquipmentItem");
        Type flashlightType = ResolveRuntimeType("FlashlightItem");
        Type hunterType = ResolveRuntimeType("HunterGhostBehavior");

        GameObject playerGo = Track(new GameObject("RemotePlayer_Test"));
        playerGo.AddComponent<Unity.Netcode.NetworkObject>();
        Component player = playerGo.AddComponent(playerType);
        playerGo.AddComponent(invType);

        GameObject flashGo = Track(new GameObject("RemoteFlashlight_Test"));
        flashGo.AddComponent<Unity.Netcode.NetworkObject>();
        Component flashlight = flashGo.AddComponent(flashlightType);

        // Ensure AllEquipment contains this item in EditMode even if Awake order varies.
        var allEquipList = (System.Collections.IList)equipType.GetField("AllEquipment", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        if (!allEquipList.Contains(flashlight))
        {
            allEquipList.Add(flashlight);
        }

        try
        {
            var ownerVar = (Unity.Netcode.NetworkVariable<ulong>)equipType
                .GetField("ownerClientId", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(flashlight);
            var inHandVar = (Unity.Netcode.NetworkVariable<bool>)equipType
                .GetField("isInHand", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(flashlight);
            var placedVar = (Unity.Netcode.NetworkVariable<bool>)equipType
                .GetField("isPlaced", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(flashlight);
            var lightOnVar = (Unity.Netcode.NetworkVariable<bool>)flashlightType
                .GetField("isLightOn", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(flashlight);

            ulong targetClientId = ((Unity.Netcode.NetworkBehaviour)player).OwnerClientId;
            ownerVar.Value = targetClientId + 77UL; // owned by another player
            inHandVar.Value = true;
            placedVar.Value = false;
            lightOnVar.Value = true;

            MethodInfo hasActiveFlash = invType.GetMethod("HasActiveHeldFlashlight", BindingFlags.Public | BindingFlags.Static);
            GameObject hunterGo = Track(new GameObject("Hunter_Test"));
            Component hunter = hunterGo.AddComponent(hunterType);
            MethodInfo isEquipActive = hunterType.GetMethod("IsPlayerEquipmentActive", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsFalse((bool)hasActiveFlash.Invoke(null, new object[] { player }));
            Assert.IsFalse((bool)isEquipActive.Invoke(hunter, new object[] { player }));

            // Reassign holder to player without setting local PlayerInventory CurrentItem slot cache.
            ownerVar.Value = targetClientId;
            Assert.IsTrue((bool)hasActiveFlash.Invoke(null, new object[] { player }),
                "HasActiveHeldFlashlight must fall back to EquipmentItem.AllEquipment when local slot cache is empty.");
            Assert.IsTrue((bool)isEquipActive.Invoke(hunter, new object[] { player }),
                "HunterGhostBehavior.IsPlayerEquipmentActive must detect remote held active flashlight.");

            // Stowed active flashlight (isInHand = false, isLightOn = true) still counts for dark protection and hunter detection.
            inHandVar.Value = false;
            Assert.IsTrue((bool)hasActiveFlash.Invoke(null, new object[] { player }));
            Assert.IsTrue((bool)isEquipActive.Invoke(hunter, new object[] { player }));

            // Turning off the stowed flashlight stops dark protection and hunter detection.
            lightOnVar.Value = false;
            Assert.IsFalse((bool)hasActiveFlash.Invoke(null, new object[] { player }));
            Assert.IsFalse((bool)isEquipActive.Invoke(hunter, new object[] { player }));
        }
        finally
        {
            allEquipList.Remove(flashlight);
        }
    }
}
