using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Pamahiin.EditModeTests
{
    public class CemeteryMapTests
    {
        private const string CemeteryScenePath = "Assets/Scenes/CemeteryMap.unity";
        private static Assembly s_GameAssembly;

        private static Assembly GameAssembly
        {
            get
            {
                if (s_GameAssembly == null)
                {
                    s_GameAssembly = Assembly.Load("Assembly-CSharp");
                }
                return s_GameAssembly;
            }
        }

        private static Type ResolveGameType(string typeName)
        {
            Type t = GameAssembly.GetType(typeName)
                ?? GameAssembly.GetTypes().FirstOrDefault(x => string.Equals(x.Name, typeName, StringComparison.Ordinal));
            Assert.IsNotNull(t, $"Could not resolve runtime type '{typeName}' from Assembly-CSharp.");
            return t;
        }

        private static Type ResolveNavMeshSurfaceType()
        {
            Assembly navAsm = Assembly.Load("NavMeshPlus");
            Type t = navAsm.GetType("NavMeshPlus.Components.NavMeshSurface")
                ?? navAsm.GetTypes().FirstOrDefault(x => string.Equals(x.Name, "NavMeshSurface", StringComparison.Ordinal));
            Assert.IsNotNull(t, "Could not resolve NavMeshSurface from NavMeshPlus assembly.");
            return t;
        }

        [Test]
        public void ProgressionAndLoadoutManager_RegistersCemeteryMap_AndRetiresLegacyPrototype()
        {
            Type progType = ResolveGameType("ProgressionAndLoadoutManager");
            var go = new GameObject("TestProgressionManager");
            try
            {
                Component mgr = go.AddComponent(progType);
                string[] maps = (string[])progType.GetField("AvailableMaps").GetValue(mgr);
                Assert.IsNotNull(maps);
                Assert.AreEqual(2, maps.Length);
                Assert.IsTrue(maps.Any(m => m.Contains("Barangay San Nicolas")));
                Assert.IsTrue(maps.Any(m => m.Contains("Kampo Santo ng San Nicolas")));
                Assert.IsFalse(maps.Any(m => m.Contains("Abandoned House (Original Prototype)")));

                MethodInfo setMapIndex = progType.GetMethod("SetMapIndex");
                MethodInfo getSceneName = progType.GetMethod("GetSelectedSceneName");
                MethodInfo isGameplay = progType.GetMethod("IsGameplayMapScene", BindingFlags.Public | BindingFlags.Static);

                setMapIndex.Invoke(mgr, new object[] { 0 });
                Assert.AreEqual("BarangayMap", (string)getSceneName.Invoke(mgr, null));

                setMapIndex.Invoke(mgr, new object[] { 1 });
                Assert.AreEqual("CemeteryMap", (string)getSceneName.Invoke(mgr, null));

                Assert.IsTrue((bool)isGameplay.Invoke(null, new object[] { "BarangayMap" }));
                Assert.IsTrue((bool)isGameplay.Invoke(null, new object[] { "CemeteryMap" }));
                Assert.IsFalse((bool)isGameplay.Invoke(null, new object[] { "GameMapScene" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EditorBuildSettings_ContainsEnabledCemeteryMapScene()
        {
            EditorBuildSettingsScene entry = EditorBuildSettings.scenes
                .FirstOrDefault(s => string.Equals(s.path, CemeteryScenePath, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(entry, $"Expected '{CemeteryScenePath}' in EditorBuildSettings.scenes.");
            Assert.IsTrue(entry.enabled, $"Expected '{CemeteryScenePath}' to be enabled in EditorBuildSettings.");
        }

        [Test]
        public void CemeteryMapScene_HasRequiredGameplaySystems_Doors_Roofs_AndValidNavMesh()
        {
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.GetFullPath(CemeteryScenePath)),
                $"Missing '{CemeteryScenePath}'. Run 'Pamahiin/Build CemeteryMap Scene' first.");

            Scene originalScene = SceneManager.GetActiveScene();
            string originalPath = originalScene.path;
            EditorSceneManager.OpenScene(CemeteryScenePath, OpenSceneMode.Single);

            try
            {
                Type doorType = ResolveGameType("NetworkDoor");
                Type fuseType = ResolveGameType("FuseBox");
                Type switchType = ResolveGameType("HouseLightSwitch");
                Type roofType = ResolveGameType("RoofFader");
                Type markerType = ResolveGameType("GhostRoomMarker");
                Type izType = ResolveGameType("InvestigationZone");
                Type weatherType = ResolveGameType("WeatherManager");
                Type candleMgrType = ResolveGameType("CemeteryCandleManager");
                Type candleType = ResolveGameType("CemeteryCandle");
                Type playerSpawnType = ResolveGameType("PlayerSpawnPoint");
                Type equipSpawnType = ResolveGameType("EquipmentSpawnPoint");
                Type endBtnType = ResolveGameType("TruckEndGameButton");
                Type burialType = ResolveGameType("BurialMapStage");
                Type navSurfaceType = ResolveNavMeshSurfaceType();

                // 1. FrontGate single-sprite NetworkDoor validation
                GameObject frontGateGo = GameObject.Find("FrontGate");
                Assert.IsNotNull(frontGateGo, "Missing FrontGate GameObject in CemeteryMap.");
                Component frontDoor = frontGateGo.GetComponent(doorType);
                Assert.IsNotNull(frontDoor, "FrontGate must have NetworkDoor component.");
                Assert.IsTrue((bool)doorType.GetProperty("IsFrontDoor").GetValue(frontDoor), "FrontGate must have isFrontDoor = true.");
                Assert.IsTrue((bool)doorType.GetProperty("RequiresKey").GetValue(frontDoor), "FrontGate must have requiresKey = true.");

                FieldInfo closedField = doorType.GetField("closedDoorRef", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo openField = doorType.GetField("openDoorRef", BindingFlags.Instance | BindingFlags.NonPublic);
                SpriteRenderer closedSr = closedField?.GetValue(frontDoor) as SpriteRenderer;
                SpriteRenderer openSr = openField?.GetValue(frontDoor) as SpriteRenderer;
                Assert.IsNotNull(closedSr, "FrontGate closedDoorRef is null.");
                Assert.IsNotNull(openSr, "FrontGate openDoorRef is null.");
                Assert.IsNotNull(closedSr.sprite, "FrontGate closedDoorRef sprite is null.");
                Assert.AreSame(closedSr.sprite, openSr.sprite, "FrontGate must share the single gate_iron sprite on ClosedRef and OpenRef.");

                // 2. Core scene objects & gameplay managers
                Assert.GreaterOrEqual(UnityEngine.Object.FindObjectsByType(fuseType, FindObjectsInactive.Include).Length, 1, "Missing FuseBox.");
                Assert.GreaterOrEqual(UnityEngine.Object.FindObjectsByType(switchType, FindObjectsInactive.Include).Length, 5, "Expected at least 5 HouseLightSwitches.");

                // Interactive inner avenue lamp post (wired to HouseLightSwitch + average-sized SafeZone)
                GameObject avenueLampGo = GameObject.Find("AvenueLamp_West_N");
                Assert.IsNotNull(avenueLampGo, "Missing interactive AvenueLamp_West_N GameObject in CemeteryMap.");
                Component avenueSwitch = avenueLampGo.GetComponent(switchType);
                Assert.IsNotNull(avenueSwitch, "AvenueLamp_West_N must have HouseLightSwitch connected to FuseBox.");
                var litZonesField = switchType.GetField("litSafeZones", BindingFlags.Instance | BindingFlags.NonPublic);
                var litZones = litZonesField?.GetValue(avenueSwitch) as System.Collections.IList;
                Assert.IsNotNull(litZones);
                Assert.AreEqual(1, litZones.Count, "AvenueLamp_West_N must link to 1 SafeZone.");
                GameObject avenueSafeZoneGo = litZones[0] as GameObject;
                Assert.IsNotNull(avenueSafeZoneGo);
                Assert.AreEqual("AvenueLamp_West_N_SafeZone", avenueSafeZoneGo.name);

                // West Road main road lamp posts must stay disconnected from FuseBox and SafeZones
                GameObject westRoadLampGo = GameObject.Find("LampPost_0");
                Assert.IsNotNull(westRoadLampGo, "Missing West Road LampPost_0 GameObject.");
                Assert.IsNull(westRoadLampGo.GetComponent(switchType), "West Road LampPost_0 must NOT have HouseLightSwitch.");
                Assert.IsNull(westRoadLampGo.GetComponentInChildren(ResolveGameType("SafeZone"), true), "West Road LampPost_0 must NOT have SafeZone.");

                UnityEngine.Object[] allRoofs = UnityEngine.Object.FindObjectsByType(roofType, FindObjectsInactive.Include);
                PropertyInfo enclosedProp = roofType.GetProperty("IsEnclosedBuilding");
                int enclosedCount = allRoofs.Count(r => (bool)enclosedProp.GetValue(r));
                Assert.GreaterOrEqual(enclosedCount, 4, "Expected at least 4 enclosed RoofFader buildings (Bantay Shed + 3 Mausoleums).");

                Assert.AreEqual(10, UnityEngine.Object.FindObjectsByType(markerType, FindObjectsInactive.Include).Length, "Expected 10 GhostRoomMarkers.");
                Assert.GreaterOrEqual(UnityEngine.Object.FindObjectsByType(izType, FindObjectsInactive.Include).Length, 1, "Missing InvestigationZone.");
                Assert.GreaterOrEqual(UnityEngine.Object.FindObjectsByType(candleMgrType, FindObjectsInactive.Include).Length, 1, "Missing CemeteryCandleManager.");
                Assert.GreaterOrEqual(UnityEngine.Object.FindObjectsByType(candleType, FindObjectsInactive.Include).Length, 20, "Expected at least 20 CemeteryCandles.");

                UnityEngine.Object weatherMgr = UnityEngine.Object.FindAnyObjectByType(weatherType, FindObjectsInactive.Include);
                Assert.IsNotNull(weatherMgr, "Missing WeatherManager.");
                Assert.IsTrue((bool)weatherType.GetProperty("UseCustomExteriorBounds").GetValue(weatherMgr),
                    "WeatherManager.UseCustomExteriorBounds must be enabled in CemeteryMap.");

                Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType(playerSpawnType, FindObjectsInactive.Include), "Missing PlayerSpawnPoint.");
                Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType(equipSpawnType, FindObjectsInactive.Include), "Missing EquipmentSpawnPoint.");
                Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType(endBtnType, FindObjectsInactive.Include), "Missing TruckEndGameButton.");
                Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType(burialType, FindObjectsInactive.Include), "Missing BurialMapStage.");

                // 3. NavMeshSurface + NavMesh path checks
                UnityEngine.Object surfaceObj = UnityEngine.Object.FindAnyObjectByType(navSurfaceType, FindObjectsInactive.Include);
                Assert.IsNotNull(surfaceObj, "Missing NavMeshSurface.");
                NavMeshData navData = navSurfaceType.GetProperty("navMeshData")?.GetValue(surfaceObj) as NavMeshData;
                Assert.IsNotNull(navData, "NavMeshSurface.navMeshData must be baked and assigned.");

                const int walkableMask = 1 << 0;
                Assert.IsFalse(NavMesh.SamplePosition(new Vector3(-1.03f, 0.00f, 0f), out _, 0.25f, walkableMask),
                    "West road outside FrontGate must NOT be walkable.");

                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0.40f, 0.12f, 0f), out NavMeshHit startHit, 0.35f, walkableMask),
                    "Entrance Plaza (0.40, 0.12) must be on walkable NavMesh.");

                Vector3[] requiredDestinations =
                {
                    new Vector3(0.95f, -1.20f, 0f), // Bantay Shed interior
                    new Vector3(5.30f,  1.42f, 0f), // Nitso Maze mid-west alley
                    new Vector3(7.35f,  2.22f, 0f), // Nitso Maze north-east alley
                    new Vector3(5.38f, -0.88f, 0f), // Mausoleum_DelosSantos interior
                    new Vector3(7.42f, -0.88f, 0f), // Mausoleum_Villanueva interior
                    new Vector3(6.38f, -2.35f, 0f)  // Mausoleum_AbandonedCrypt interior
                };

                NavMeshPath path = new NavMeshPath();
                foreach (Vector3 dest in requiredDestinations)
                {
                    Assert.IsTrue(NavMesh.SamplePosition(dest, out NavMeshHit destHit, 0.45f, walkableMask),
                        $"Destination {dest} is not on walkable NavMesh.");
                    Assert.IsTrue(NavMesh.CalculatePath(startHit.position, destHit.position, walkableMask, path),
                        $"NavMesh.CalculatePath failed to {dest}.");
                    Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status,
                        $"NavMesh path to {dest} was incomplete ({path.status}).");
                }

                // 4. Single-level CemeteryMap must never trigger false basement audio muffling
                Type audioType = ResolveGameType("GameAudioManager");
                audioType.GetMethod("RefreshSceneBasementFlag", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                Assert.IsFalse((bool)audioType.GetProperty("HasSceneBasement", BindingFlags.Public | BindingFlags.Static).GetValue(null));
                Assert.IsFalse((bool)audioType.GetMethod("IsBasementPosition", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { new Vector3(2.0f, -4.5f, 0f) }));
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalPath) && System.IO.File.Exists(System.IO.Path.GetFullPath(originalPath)))
                {
                    EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
                }
            }
        }

        [Test]
        public void JournalUI_Stylesheet_HasCemeteryBlueprint_AndPassesThemeRules()
        {
            string ussPath = System.IO.Path.GetFullPath("Assets/UI/Journal/JournalUI.uss");
            Assert.IsTrue(System.IO.File.Exists(ussPath));
            string content = System.IO.File.ReadAllText(ussPath);
            string[] lines = System.IO.File.ReadAllLines(ussPath);

            Assert.IsTrue(content.Contains(".blueprint-cemetery"), "JournalUI.uss missing .blueprint-cemetery rule.");
            Assert.IsTrue(content.Contains("blueprint_cemetery.png"), "JournalUI.uss .blueprint-cemetery must reference blueprint_cemetery.png.");
            Assert.IsTrue(content.Contains("PamahiinTheme.uss"), "JournalUI.uss must @import PamahiinTheme.uss.");
            Assert.IsFalse(content.Contains("VT323"));
            Assert.IsFalse(content.Contains("box-shadow"));

            var radiusRx = new System.Text.RegularExpressions.Regex(@"border(?:-\w+)*-radius\s*:\s*([^;]+);");
            var scaleRx = new System.Text.RegularExpressions.Regex(@"(^|\s|,)scale(\s*:|\s*,|\s*;)");
            var fontStyleRx = new System.Text.RegularExpressions.Regex(@"-unity-font-style\s*:\s*([^;]+);");
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;
                var rm = radiusRx.Match(trimmed);
                if (rm.Success)
                {
                    string val = rm.Groups[1].Value.Trim();
                    Assert.IsTrue(val == "0" || val == "0px", $"Line {i + 1}: non-zero radius '{val}'.");
                }
                Assert.IsFalse(scaleRx.IsMatch(trimmed), $"Line {i + 1}: scale not allowed.");
                var fm = fontStyleRx.Match(trimmed);
                if (fm.Success)
                {
                    Assert.AreEqual("normal", fm.Groups[1].Value.Trim(), $"Line {i + 1}: font style must be normal.");
                }
            }
        }

        [Test]
        public void GameMatchManager_AssignsKsnCasePrefix_WhenCemeterySelected()
        {
            Type progType = ResolveGameType("ProgressionAndLoadoutManager");
            Type matchType = ResolveGameType("GameMatchManager");

            var progGo = new GameObject("TestProgForMatch");
            var matchGo = new GameObject("TestMatchManager");
            try
            {
                Component prog = progGo.AddComponent(progType);
                progType.GetMethod("SetMapIndex").Invoke(prog, new object[] { 1 });

                Component match = matchGo.AddComponent(matchType);
                matchType.GetMethod("EnsureGhostContractProfileAssigned", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(match, null);

                string caseId = (string)matchType.GetProperty("AssignedCaseId").GetValue(match);
                Assert.IsTrue(caseId.StartsWith("KSN-"), $"Expected KSN-#### case ID when CemeteryMap selected, got '{caseId}'.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(matchGo);
                UnityEngine.Object.DestroyImmediate(progGo);
            }
        }
    }
}
