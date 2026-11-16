using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Pamahiin.EditModeTests
{
    public class PlaytesterFeedbackUxTests
    {
        private const string PauseMenuUxmlPath = "Assets/UI/PauseMenu/PauseMenu.uxml";
        private const string MainMenuUxmlPath = "Assets/UI/MainMenu/MainMenu.uxml";
        private const string HudUxmlPath = "Assets/UI/HUD/HUD.uxml";
        private const string ExorcismUxmlPath = "Assets/UI/Exorcism/ExorcismUI.uxml";

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

        private GameObject Track(GameObject go)
        {
            spawnedObjects.Add(go);
            return go;
        }

        private static PanelSettings ResolvePanelSettings()
        {
            Type utilType = ResolveRuntimeType("PamahiinUIThemeUtility");
            MethodInfo resolvePs = utilType.GetMethod("ResolveRuntimePanelSettings", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(resolvePs);
            var ps = resolvePs.Invoke(null, new object[] { null }) as PanelSettings;
            Assert.IsNotNull(ps);
            return ps;
        }

        private UIDocument CreateTestUiDocument(string name, string uxmlAssetPath)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlAssetPath);
            Assert.IsNotNull(tree, $"Missing VisualTreeAsset at '{uxmlAssetPath}'.");

            var go = Track(new GameObject(name));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ResolvePanelSettings();
            doc.visualTreeAsset = tree;
            return doc;
        }

        [TearDown]
        public void TearDown()
        {
            Type spiritBoxType = ResolveRuntimeType("SpiritBoxItem");
            PropertyInfo dialogueActiveProp = spiritBoxType.GetProperty("IsLocalDialogueActive", BindingFlags.Public | BindingFlags.Static);
            dialogueActiveProp?.SetValue(null, false);

            for (int i = spawnedObjects.Count - 1; i >= 0; i--)
            {
                if (spawnedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(spawnedObjects[i]);
                }
            }
            spawnedObjects.Clear();
        }

        [Test]
        public void PauseMenu_HelpAndControlsPanel_ShowsVerifiedControls_AndNavigatesCleanly()
        {
            var doc = CreateTestUiDocument("TestPauseMenu", PauseMenuUxmlPath);
            Type pauseType = ResolveRuntimeType("PauseMenuController");
            Component pause = doc.gameObject.AddComponent(pauseType);

            pauseType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            pauseType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);

            var root = doc.rootVisualElement;
            Assert.IsNotNull(root);

            var panelMain = root.Q<VisualElement>("panel-main");
            var panelSettings = root.Q<VisualElement>("panel-settings");
            var panelControls = root.Q<VisualElement>("panel-controls");
            var panelPlayers = root.Q<VisualElement>("panel-players");
            var btnControls = root.Q<Button>("btn-controls");
            var btnControlsBack = root.Q<Button>("btn-controls-back");

            Assert.IsNotNull(panelMain);
            Assert.IsNotNull(panelSettings);
            Assert.IsNotNull(panelControls);
            Assert.IsNotNull(panelPlayers);
            Assert.IsNotNull(btnControls);
            Assert.IsNotNull(btnControlsBack);

            Assert.IsFalse(panelMain.ClassListContains("hidden"));
            Assert.IsTrue(panelControls.ClassListContains("hidden"));

            pauseType.GetMethod("TogglePause", BindingFlags.Public | BindingFlags.Instance)?.Invoke(pause, null);
            Assert.IsTrue((bool)pauseType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));

            pauseType.GetMethod("ShowControls", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            Assert.IsTrue(panelMain.ClassListContains("hidden"));
            Assert.IsFalse(panelControls.ClassListContains("hidden"));

            var allLabels = panelControls.Query<Label>().ToList();
            string combinedText = string.Join("\n", allLabels.ConvertAll(l => l.text));
            Assert.IsTrue(combinedText.Contains("WASD"), "Help & Controls must document WASD.");
            Assert.IsTrue(combinedText.Contains("Left Shift"), "Help & Controls must document Left Shift.");
            Assert.IsTrue(combinedText.Contains("E / L-Click"), "Help & Controls must document E / L-Click.");
            Assert.IsTrue(combinedText.Contains("R-Click"), "Help & Controls must document R-Click placement.");
            Assert.IsTrue(combinedText.Contains("1-3 / Scroll / Click"), "Help & Controls must document hotbar selection including Click.");
            Assert.IsFalse(combinedText.Contains("T / V"), "Unbound T / V voice bind should not appear.");

            MethodInfo handleEscape = pauseType.GetMethod("HandleEscapePressed", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(handleEscape);

            // Esc from Help & Controls returns to Main Pause Panel without closing Pause Menu
            handleEscape.Invoke(pause, null);
            Assert.IsTrue((bool)pauseType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            Assert.IsFalse(panelMain.ClassListContains("hidden"));
            Assert.IsTrue(panelControls.ClassListContains("hidden"));

            // Esc from Settings and Players also returns to Main Pause Panel
            pauseType.GetMethod("ShowSettings", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            Assert.IsFalse(panelSettings.ClassListContains("hidden"));
            handleEscape.Invoke(pause, null);
            Assert.IsFalse(panelMain.ClassListContains("hidden"));
            Assert.IsTrue(panelSettings.ClassListContains("hidden"));

            pauseType.GetMethod("ShowPlayers", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            Assert.IsFalse(panelPlayers.ClassListContains("hidden"));
            handleEscape.Invoke(pause, null);
            Assert.IsFalse(panelMain.ClassListContains("hidden"));
            Assert.IsTrue(panelPlayers.ClassListContains("hidden"));

            // Esc from Main Pause Panel closes the Pause Menu
            handleEscape.Invoke(pause, null);
            Assert.IsFalse((bool)pauseType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
        }

        [Test]
        public void MainMenu_HelpView_MatchesVerifiedControls()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath);
            Assert.IsNotNull(tree);
            VisualElement root = tree.CloneTree();

            var helpView = root.Q<VisualElement>("help-view");
            Assert.IsNotNull(helpView);

            var allLabels = helpView.Query<Label>().ToList();
            string combinedText = string.Join("\n", allLabels.ConvertAll(l => l.text));

            Assert.IsTrue(combinedText.Contains("Crouch"));
            Assert.IsTrue(combinedText.Contains("Place Equipment on Floor"));
            Assert.IsTrue(combinedText.Contains("1-3 / Scroll / Click"));
            Assert.IsFalse(combinedText.Contains("Toggle Flashlight"), "Stale F - Toggle Flashlight label should be removed.");
            Assert.IsFalse(combinedText.Contains("Q / E"), "Stale Q / E label should be removed.");
        }

        [Test]
        public void PlayerHUDManager_HotbarPointerDown_SwitchesSlots_AndRespectsInteractionBlock()
        {
            var hudDoc = CreateTestUiDocument("TestHUD", HudUxmlPath);
            Type hudType = ResolveRuntimeType("PlayerHUDManager");
            Component hud = hudDoc.gameObject.AddComponent(hudType);
            hudType.GetField("hudDocument", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(hud, hudDoc);
            hudType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(hud, null);

            GameObject playerGo = Track(new GameObject("TestPlayer_Hud"));
            playerGo.AddComponent<Rigidbody2D>();
            Type playerType = ResolveRuntimeType("PlayerController");
            Component player = playerGo.AddComponent(playerType);
            Type invType = ResolveRuntimeType("PlayerInventory");
            Component inv = playerGo.AddComponent(invType);

            hudType.GetField("localPlayerController", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(hud, player);
            hudType.GetField("localInventory", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(hud, inv);

            FieldInfo currentSlotField = invType.GetField("currentSlotIndex", BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(0, (int)currentSlotField.GetValue(inv));

            MethodInfo onSlotPointerDown = hudType.GetMethod("OnSlotPointerDown", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onSlotPointerDown);

            using (var rightClick = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1 }))
            {
                onSlotPointerDown.Invoke(hud, new object[] { rightClick, 2 });
                Assert.AreEqual(0, (int)currentSlotField.GetValue(inv), "Right-click must not switch hotbar slots.");
            }

            using (var leftClick = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
            {
                onSlotPointerDown.Invoke(hud, new object[] { leftClick, 2 });
                Assert.AreEqual(2, (int)currentSlotField.GetValue(inv), "Left-click on Slot2 should select slot 2.");
                Assert.IsTrue((bool)hudType.GetProperty("WasHotbarClickedThisFrame", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            }

            // Block interactions via PauseMenuController.IsOpen and ensure clicking Slot1 is ignored
            var pauseDoc = CreateTestUiDocument("TestPauseBlocker", PauseMenuUxmlPath);
            Type pauseType = ResolveRuntimeType("PauseMenuController");
            Component pause = pauseDoc.gameObject.AddComponent(pauseType);
            pauseType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            pauseType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            pauseType.GetMethod("TogglePause", BindingFlags.Public | BindingFlags.Instance)?.Invoke(pause, null);
            Assert.IsTrue((bool)playerType.GetProperty("IsInteractionBlocked", BindingFlags.Public | BindingFlags.Static).GetValue(null));

            using (var blockedClick = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
            {
                onSlotPointerDown.Invoke(hud, new object[] { blockedClick, 1 });
                Assert.AreEqual(2, (int)currentSlotField.GetValue(inv), "Hotbar click must be ignored while interaction is blocked.");
            }
        }

        [Test]
        public void PlayerController_MovementAndInteractionBlocked_CoversJournalAndExorcismUiPanelsOnly()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            PropertyInfo moveBlockedProp = playerType.GetProperty("IsMovementBlocked", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo interactBlockedProp = playerType.GetProperty("IsInteractionBlocked", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(moveBlockedProp);
            Assert.IsNotNull(interactBlockedProp);

            Assert.IsFalse((bool)moveBlockedProp.GetValue(null));
            Assert.IsFalse((bool)interactBlockedProp.GetValue(null));

            // 1. JournalController.IsOpen must block BOTH movement and interaction
            Type journalType = ResolveRuntimeType("JournalController");
            var journalGo = Track(new GameObject("TestJournal"));
            Component journal = journalGo.AddComponent(journalType);
            journalType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, journal);
            journalType.GetField("isJournalOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(journal, true);

            Assert.IsTrue((bool)journalType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "Opening Journal must block WASD movement.");
            Assert.IsTrue((bool)interactBlockedProp.GetValue(null), "Opening Journal must block interactions.");

            journalType.GetField("isJournalOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(journal, false);
            journalType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);
            Assert.IsFalse((bool)moveBlockedProp.GetValue(null));

            // 2. ExorcismManager.IsUIOpen must block during ghostSelectPopup and minigame overlay, NOT during candle lighting
            var exorcismDoc = CreateTestUiDocument("TestExorcism", ExorcismUxmlPath);
            Type exorcismType = ResolveRuntimeType("ExorcismManager");
            Component exorcism = exorcismDoc.gameObject.AddComponent(exorcismType);
            exorcismType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(exorcism, null);
            exorcismType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(exorcism, null);

            var ghostSelectPopup = exorcismDoc.rootVisualElement.Q<VisualElement>("ghost-select-popup");
            Assert.IsNotNull(ghostSelectPopup);

            // Ghost selection popup visible -> blocked
            exorcismType.GetField("isExorcismActive", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(exorcism, true);
            ghostSelectPopup.RemoveFromClassList("hidden");
            Assert.IsTrue((bool)exorcismType.GetProperty("IsUIOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "Exorcism ghost selection popup must block movement.");

            // Candle-lighting phase (popup hidden, minigame not yet active) -> movement allowed
            ghostSelectPopup.AddToClassList("hidden");
            Assert.IsFalse((bool)exorcismType.GetProperty("IsUIOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            Assert.IsFalse((bool)moveBlockedProp.GetValue(null), "Candle lighting phase should not lock movement.");

            // Skill check minigame active -> blocked (prevents A/E/G keys from walking/interacting/dropping)
            exorcismType.GetField("isMinigameActive", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(exorcism, true);
            Assert.IsTrue((bool)exorcismType.GetProperty("IsUIOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "Exorcism minigame overlay must block movement.");
            Assert.IsTrue((bool)interactBlockedProp.GetValue(null), "Exorcism minigame overlay must block interaction.");

            exorcismType.GetField("isMinigameActive", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(exorcism, false);
            exorcismType.GetField("isExorcismActive", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(exorcism, false);
            Assert.IsFalse((bool)moveBlockedProp.GetValue(null));

            // 3. Remaining modal overlays covered by IsMovementBlocked
            // Spirit Box dialogue
            Type spiritBoxType = ResolveRuntimeType("SpiritBoxItem");
            PropertyInfo dialogueActiveProp = spiritBoxType.GetProperty("IsLocalDialogueActive", BindingFlags.Public | BindingFlags.Static);
            dialogueActiveProp.SetValue(null, true);
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "SpiritBox dialogue must block movement.");
            dialogueActiveProp.SetValue(null, false);

            // Dev Console
            Type consoleType = ResolveRuntimeType("DeveloperConsoleManager");
            var consoleGo = Track(new GameObject("TestConsole"));
            Component console = consoleGo.AddComponent(consoleType);
            consoleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, console);
            consoleType.GetField("isConsoleOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(console, true);
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "Developer console must block movement.");
            consoleType.GetField("isConsoleOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(console, false);
            consoleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);

            // Truck Dashboard
            Type dashType = ResolveRuntimeType("TruckDashboardController");
            var dashGo = Track(new GameObject("TestDash"));
            Component dash = dashGo.AddComponent(dashType);
            dashType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, dash);
            dashType.GetField("isDashboardOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(dash, true);
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "Truck dashboard must block movement.");
            dashType.GetField("isDashboardOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(dash, false);
            dashType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);

            // End Game Reveal
            Type endGameType = ResolveRuntimeType("EndGameRevealController");
            var endGameGo = Track(new GameObject("TestEndGame"));
            Component endGame = endGameGo.AddComponent(endGameType);
            endGameType.GetField("activeInstance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, endGame);
            endGameType.GetField("isOverlayVisible", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(endGame, true);
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "End Game reveal overlay must block movement.");
            endGameType.GetField("isOverlayVisible", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(endGame, false);
            endGameType.GetField("activeInstance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);

            // Loading Screen
            Type loadingType = ResolveRuntimeType("LoadingScreenManager");
            var loadingGo = Track(new GameObject("TestLoading"));
            Component loading = loadingGo.AddComponent(loadingType);
            loadingType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, loading);
            loadingType.GetField("isVisible", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(loading, true);
            Assert.IsTrue((bool)moveBlockedProp.GetValue(null), "Loading screen must block movement.");
            loadingType.GetField("isVisible", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(loading, false);
            loadingType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);
        }

        [Test]
        public void PauseMenu_HostDisconnectedModal_ShowsAndBlocksInteraction()
        {
            var doc = CreateTestUiDocument("TestHostDisconnectPauseMenu", PauseMenuUxmlPath);
            Type pauseType = ResolveRuntimeType("PauseMenuController");
            Component pause = doc.gameObject.AddComponent(pauseType);

            pauseType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            pauseType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);

            var root = doc.rootVisualElement;
            var hostOverlay = root.Q<VisualElement>("host-disconnect-overlay");
            var msgLabel = root.Q<Label>("lbl-host-disconnect-msg");
            var menuButton = root.Q<Button>("btn-host-disconnect-menu");

            Assert.IsNotNull(hostOverlay);
            Assert.IsNotNull(msgLabel);
            Assert.IsNotNull(menuButton);
            Assert.IsTrue(hostOverlay.ClassListContains("hidden"));

            pauseType.GetMethod("ShowHostDisconnected", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(pause, new object[] { "Host closed the lobby." });

            Assert.IsFalse(hostOverlay.ClassListContains("hidden"));
            Assert.AreEqual("Host closed the lobby.", msgLabel.text);
            Assert.AreEqual(60, doc.sortingOrder);
            Assert.IsTrue((bool)pauseType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));

            Type playerType = ResolveRuntimeType("PlayerController");
            Assert.IsTrue((bool)playerType.GetProperty("IsMovementBlocked", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            Assert.IsTrue((bool)playerType.GetProperty("IsInteractionBlocked", BindingFlags.Public | BindingFlags.Static).GetValue(null));

            // Esc must not dismiss the Host Disconnected modal
            pauseType.GetMethod("HandleEscapePressed", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
            Assert.IsFalse(hostOverlay.ClassListContains("hidden"));
            Assert.IsTrue((bool)pauseType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static).GetValue(null));
        }

        [Test]
        public void NetworkDoor_CabinetDoor_BlocksWhenClosed_AndAllowsPassThroughWhenOpen()
        {
            Texture2D tex = new Texture2D(32, 16);
            Sprite closedSprite = Sprite.Create(tex, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 64f);
            Sprite openSprite = Sprite.Create(tex, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 64f);

            var doorGo = Track(new GameObject("Boys_Cabinet_Door"));
            var closedRefGo = new GameObject("ClosedRef");
            closedRefGo.transform.SetParent(doorGo.transform, false);
            var closedSr = closedRefGo.AddComponent<SpriteRenderer>();
            closedSr.sprite = closedSprite;
            var closedCol = closedRefGo.AddComponent<BoxCollider2D>();
            closedCol.size = new Vector2(0.5f, 0.25f);

            var openRefGo = new GameObject("OpenRef");
            openRefGo.transform.SetParent(doorGo.transform, false);
            var openSr = openRefGo.AddComponent<SpriteRenderer>();
            openSr.sprite = openSprite;

            Type doorType = ResolveRuntimeType("NetworkDoor");
            Component door = doorGo.AddComponent(doorType);

            doorType.GetField("closedDoorRef", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(door, closedSr);
            doorType.GetField("openDoorRef", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(door, openSr);
            doorType.GetField("swapSpriteWhenOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(door, true);
            doorType.GetField("disableObstacleColliderWhenOpen", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(door, true);
            doorType.GetField("isHidingSpot", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(door, true);

            doorType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);

            var runtimeCol = doorType.GetField("runtimeCollider", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(door) as Collider2D;
            var runtimeSr = doorType.GetField("runtimeRenderer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(door) as SpriteRenderer;
            Assert.IsNotNull(runtimeCol);
            Assert.IsNotNull(runtimeSr);

            // Closed initially -> solid collider enabled, closed sprite shown
            Assert.IsTrue(runtimeCol.enabled, "Closed cabinet door must block player passage.");
            Assert.AreEqual(closedSprite, runtimeSr.sprite);

            // Open cabinet door -> collider disabled immediately so player can pass through
            MethodInfo setDoorOpen = doorType.GetMethod("SetDoorOpen", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(setDoorOpen);
            setDoorOpen.Invoke(door, new object[] { true, false });
            Assert.IsFalse(runtimeCol.enabled, "Open cabinet door must allow player passage.");
            Assert.AreEqual(openSprite, runtimeSr.sprite);

            // Close cabinet door again -> collider re-enabled immediately so player cannot pass through
            setDoorOpen.Invoke(door, new object[] { false, false });
            Assert.IsTrue(runtimeCol.enabled, "Re-closed cabinet door must block player passage.");
            Assert.AreEqual(closedSprite, runtimeSr.sprite);

            // Verify a non-cabinet room door named with "Closet" is NOT auto-flagged as isHidingSpot
            var roomDoorGo = Track(new GameObject("Door_MasterCloset"));
            Component roomDoor = roomDoorGo.AddComponent(doorType);
            doorType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(roomDoor, null);
            Assert.IsFalse((bool)doorType.GetProperty("IsHidingSpot", BindingFlags.Public | BindingFlags.Instance).GetValue(roomDoor),
                "Swinging room door 'Door_MasterCloset' should not be auto-marked as isHidingSpot.");

            UnityEngine.Object.DestroyImmediate(closedSprite);
            UnityEngine.Object.DestroyImmediate(openSprite);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        [Test]
        public void HunterGhostBehavior_DoesNotOpenDistantDoorsOrChainOpenNonPartnerDoors()
        {
            Type hunterType = ResolveRuntimeType("HunterGhostBehavior");
            Type doorType = ResolveRuntimeType("NetworkDoor");

            var ghostGo = Track(new GameObject("TestHunterGhost"));
            var ghost = ghostGo.AddComponent(hunterType);

            // Simulate stale large values serialized on an older prefab/scene before Awake() clamps them
            hunterType.GetField("doorOpenReach", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(ghost, 2.0f);
            hunterType.GetField("openedDoorHideCheckRadius", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(ghost, 1.3f);
            hunterType.GetField("hidingDoorStandOffDistance", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(ghost, 0.6f);

            hunterType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ghost, null);

            float clampedCabinetReach = (float)hunterType.GetField("doorOpenReach", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ghost);
            float clampedInteriorReach = (float)hunterType.GetField("interiorDoorOpenReach", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ghost);
            float clampedHideBehindRadius = (float)hunterType.GetField("openedDoorHideCheckRadius", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ghost);

            Assert.LessOrEqual(clampedCabinetReach, 0.32f);
            Assert.LessOrEqual(clampedInteriorReach, 0.30f);
            Assert.LessOrEqual(clampedHideBehindRadius, 0.25f);

            var createdDoors = new List<Component>();
            Component CreateDoor(string name, Vector3 pos, bool hidingSpot)
            {
                var go = Track(new GameObject(name));
                go.transform.position = pos;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.25f, 0.08f);
                var d = go.AddComponent(doorType);
                doorType.GetField("isHidingSpot", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(d, hidingSpot);
                doorType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(d, null);
                doorType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(d, null);
                createdDoors.Add(d);
                return d;
            }

            try
            {
                var targetCabinet = CreateDoor("Cabinet_Target", Vector3.zero, true);
                var partnerCabinet = CreateDoor("Cabinet_Partner", new Vector3(0.22f, 0f, 0f), true);
                var nearbyNormalDoor = CreateDoor("Door_NearbyHall", new Vector3(0.25f, 0f, 0f), false);
                var distantCabinet = CreateDoor("Cabinet_Distant", new Vector3(0.55f, 0f, 0f), true);

                PropertyInfo isOpenProp = doorType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(isOpenProp);

                // OpenDoorAndConnected should open only targetCabinet and partnerCabinet (<= 0.28f & isHidingSpot)
                hunterType.GetMethod("OpenDoorAndConnected", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(ghost, new object[] { targetCabinet });

                Assert.IsTrue((bool)isOpenProp.GetValue(targetCabinet), "Target cabinet should open.");
                Assert.IsTrue((bool)isOpenProp.GetValue(partnerCabinet), "Adjacent partner cabinet door (<= 0.28f) should open.");
                Assert.IsFalse((bool)isOpenProp.GetValue(nearbyNormalDoor), "Adjacent non-hiding interior door must NOT chain-open.");
                Assert.IsFalse((bool)isOpenProp.GetValue(distantCabinet), "Distant cabinet (> 0.28f) must NOT chain-open.");

                // Reset partner & check distant CheckDoorObstructions / CheckCabinetDoorInteractions does not open doors from 0.8f away
                var farRoomDoor = CreateDoor("Door_FarRoom", new Vector3(0.8f, 0f, 0f), false);
                var farClosedCabinet = CreateDoor("Cabinet_FarClosed", new Vector3(0.8f, 0.2f, 0f), true);
                ghostGo.transform.position = new Vector3(-0.2f, 0f, 0f);
                hunterType.GetField("targetedHidingDoor", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(ghost, farClosedCabinet);
                Physics2D.SyncTransforms();

                hunterType.GetMethod("CheckDoorObstructions", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ghost, null);
                hunterType.GetMethod("CheckCabinetDoorInteractions", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ghost, null);

                Assert.IsFalse((bool)isOpenProp.GetValue(farRoomDoor), "Ghost must NOT open distant closed interior doors.");
                Assert.IsFalse((bool)isOpenProp.GetValue(farClosedCabinet), "Ghost must NOT open distant targeted cabinet doors.");
            }
            finally
            {
                MethodInfo onDisable = doorType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < createdDoors.Count; i++)
                {
                    if (createdDoors[i] != null) onDisable?.Invoke(createdDoors[i], null);
                }
            }
        }

        [Test]
        public void TruckGearSpawner_ZeroLoadout_SpawnsNoEquipmentInTruck()
        {
            Type loadoutType = ResolveRuntimeType("ProgressionAndLoadoutManager");
            Type spawnerType = ResolveRuntimeType("TruckGearSpawner");
            Type eqType = ResolveRuntimeType("EquipmentItem");

            var loadoutGo = Track(new GameObject("TestLoadoutMgr"));
            Component loadoutMgr = loadoutGo.AddComponent(loadoutType);
            loadoutType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, loadoutMgr);

            var countsField = loadoutType.GetField("loadoutCounts", BindingFlags.NonPublic | BindingFlags.Instance);
            var countsDict = countsField?.GetValue(loadoutMgr) as Dictionary<string, int>;
            Assert.IsNotNull(countsDict);
            countsDict.Clear();

            var catalogField = loadoutType.GetField("Catalog", BindingFlags.Public | BindingFlags.Instance);
            var catalog = catalogField?.GetValue(loadoutMgr) as System.Collections.IList;
            Assert.IsNotNull(catalog);
            foreach (var entry in catalog)
            {
                string id = entry.GetType().GetField("id").GetValue(entry) as string;
                countsDict[id] = 0;
            }

            var spawnerGo = Track(new GameObject("TestTruckGearSpawner"));
            Component spawner = spawnerGo.AddComponent(spawnerType);
            spawnerType.GetField("hasSpawnedLoadout", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(spawner, false);

            int beforeCount = UnityEngine.Object.FindObjectsByType(eqType, FindObjectsInactive.Include).Length;
            spawnerType.GetMethod("SpawnSelectedLoadoutInVan", BindingFlags.Public | BindingFlags.Instance)?.Invoke(spawner, null);
            int afterCount = UnityEngine.Object.FindObjectsByType(eqType, FindObjectsInactive.Include).Length;

            Assert.AreEqual(beforeCount, afterCount, "TruckGearSpawner must not spawn fallback gear when lobby loadout has 0 items.");
            Assert.IsTrue((bool)spawnerType.GetProperty("HasSpawnedLoadout", BindingFlags.Public | BindingFlags.Instance).GetValue(spawner));

            loadoutType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);
        }
    }
}
