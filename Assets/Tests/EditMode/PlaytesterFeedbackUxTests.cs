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
        private const string SpiritBoxDialogueUxmlPath = "Assets/UI/SpiritBox/SpiritBoxDialogue.uxml";

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
            Assert.IsTrue(combinedText.Contains("Use Held Item / Pick Up Equipment"), "Help & Controls must document L-Click use/pickup.");
            Assert.IsTrue(combinedText.Contains("Hold L-Click"), "Help & Controls must document Hold L-Click.");
            Assert.IsTrue(combinedText.Contains("Drag Item"), "Help & Controls must document Drag Item.");
            Assert.IsTrue(combinedText.Contains("Interact (Doors, Switches, etc.)"), "Help & Controls must document E environment interact.");
            Assert.IsTrue(combinedText.Contains("Toggle Flashlight"), "Help & Controls must document F flashlight toggle.");
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
            Assert.IsTrue(combinedText.Contains("Use Held Item / Pick Up Equipment"));
            Assert.IsTrue(combinedText.Contains("Hold L-Click"));
            Assert.IsTrue(combinedText.Contains("Interact (Doors, Switches, etc.)"));
            Assert.IsTrue(combinedText.Contains("Toggle Flashlight"));
            Assert.IsTrue(combinedText.Contains("Place Equipment on Floor"));
            Assert.IsTrue(combinedText.Contains("1-3 / Scroll / Click"));
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

        [Test]
        public void GameplayTuningPlan_ReachCircleKillDistanceAndExorcismHitTolerance_AreConfiguredCorrectly()
        {
            // 1. HunterGhostBehavior killDistance & agentStoppingDistance defaults and Awake clamping
            Type hunterType = ResolveRuntimeType("HunterGhostBehavior");
            var ghostGo = Track(new GameObject("TestHunterKillRange"));
            Component ghost = ghostGo.AddComponent(hunterType);

            FieldInfo killDistField = hunterType.GetField("killDistance", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo stopDistField = hunterType.GetField("agentStoppingDistance", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(killDistField);
            Assert.IsNotNull(stopDistField);
            Assert.AreEqual(0.15f, (float)killDistField.GetValue(ghost), 0.001f);
            Assert.AreEqual(0.05f, (float)stopDistField.GetValue(ghost), 0.001f);

            // Simulate stale larger serialized scene values before Awake() clamps them
            killDistField.SetValue(ghost, 0.3f);
            stopDistField.SetValue(ghost, 0.15f);
            hunterType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ghost, null);
            Assert.AreEqual(0.15f, (float)killDistField.GetValue(ghost), 0.001f);
            Assert.AreEqual(0.05f, (float)stopDistField.GetValue(ghost), 0.001f);

            // 2. ExorcismManager hitTolerance matches yellow bar angular half-width per difficulty
            Type loadoutType = ResolveRuntimeType("ProgressionAndLoadoutManager");
            Type difficultyType = ResolveRuntimeType("ContractDifficulty");
            Type exorcismType = ResolveRuntimeType("ExorcismManager");

            var loadoutGo = Track(new GameObject("TestDifficultyMgr"));
            Component loadoutMgr = loadoutGo.AddComponent(loadoutType);
            PropertyInfo loadoutInstanceProp = loadoutType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo selectedDiffProp = loadoutType.GetProperty("SelectedDifficulty", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo setDiffMethod = selectedDiffProp?.GetSetMethod(true);
            Assert.IsNotNull(setDiffMethod);

            var exorcismDoc = CreateTestUiDocument("TestExorcismTuning", ExorcismUxmlPath);
            Component exorcism = exorcismDoc.gameObject.AddComponent(exorcismType);
            exorcismType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(exorcism, null);
            exorcismType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(exorcism, null);

            MethodInfo applySettingsMethod = exorcismType.GetMethod("ApplyDifficultyMinigameSettings", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo hitToleranceField = exorcismType.GetField("hitTolerance", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(applySettingsMethod);
            Assert.IsNotNull(hitToleranceField);

            object prevLoadoutInstance = loadoutInstanceProp.GetValue(null);
            loadoutInstanceProp.SetValue(null, loadoutMgr);
            try
            {
                var cases = new (string name, float expectedTolerance)[]
                {
                    ("Amateur", 12f),
                    ("Intermediate", 8.5f),
                    ("Professional", 6f),
                    ("Nightmare", 4.2f)
                };

                foreach (var c in cases)
                {
                    object diffVal = Enum.Parse(difficultyType, c.name);
                    setDiffMethod.Invoke(loadoutMgr, new object[] { diffVal });
                    applySettingsMethod.Invoke(exorcism, null);
                    float actual = (float)hitToleranceField.GetValue(exorcism);
                    Assert.AreEqual(c.expectedTolerance, actual, 0.01f, $"Difficulty {c.name} hitTolerance mismatch.");
                }
            }
            finally
            {
                loadoutInstanceProp.SetValue(null, prevLoadoutInstance);
            }

            // 3. PlayerController ReachGuideCircle creation, scale compensation, 1.1s hold-then-fade, and accessibility toggle
            Type playerType = ResolveRuntimeType("PlayerController");
            Type settingsType = ResolveRuntimeType("GameSettingsManager");

            const string reachKey = "reach_indicator_enabled";
            bool hadReachPref = PlayerPrefs.HasKey(reachKey);
            int prevReachPref = PlayerPrefs.GetInt(reachKey, 1);

            var settingsGo = Track(new GameObject("TestSettingsMgr_Reach"));
            Component settingsMgr = settingsGo.AddComponent(settingsType);
            settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, settingsMgr);

            try
            {
                PlayerPrefs.DeleteKey(reachKey);
                settingsType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(settingsMgr, null);

                PropertyInfo reachProp = settingsType.GetProperty("ReachIndicatorEnabled", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo setReachMethod = settingsType.GetMethod("SetReachIndicatorEnabled", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsTrue((bool)reachProp.GetValue(settingsMgr), "ReachIndicatorEnabled must default to true (ON).");

                var playerGo = Track(new GameObject("TestPlayerReachGuide"));
                playerGo.transform.localScale = new Vector3(0.009549775f, -0.009720439f, -0.033174798f);
                Component player = playerGo.AddComponent(playerType);
                playerType.GetField("maxReachRadius", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, 0.15f);

                playerType.GetMethod("EnsureReachGuideRenderer", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);

                Transform ringTransform = playerGo.transform.Find("ReachGuideCircle");
                Assert.IsNotNull(ringTransform, "ReachGuideCircle child should be created.");
                LineRenderer ring = ringTransform.GetComponent<LineRenderer>();
                Assert.IsNotNull(ring);
                Assert.IsTrue(ring.loop);
                Assert.IsFalse(ring.useWorldSpace);
                Assert.AreEqual(48, ring.positionCount);
                Assert.AreEqual(0.002f, ring.startWidth, 0.0001f);
                Assert.AreEqual(0.002f, ring.endWidth, 0.0001f);
                Assert.IsFalse(ring.enabled, "ReachGuideCircle should start hidden until triggered.");
                Assert.AreEqual(0.15f, Vector3.Distance(playerGo.transform.position, ringTransform.TransformPoint(ring.GetPosition(0))), 0.002f);

                MethodInfo triggerRing = playerType.GetMethod("TriggerReachGuide", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo stepRing = playerType.GetMethod("StepReachGuideCircle", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(triggerRing);
                Assert.IsNotNull(stepRing);

                // Trigger while ON -> visible thin semi-transparent gray ring, holds for 1.1s then fades
                triggerRing.Invoke(player, null);
                Assert.IsTrue(ring.enabled, "ReachGuideCircle must render when triggered while ReachIndicatorEnabled is ON.");
                Assert.AreEqual(0.75f, ring.startColor.r, 0.02f);
                Assert.AreEqual(0.75f, ring.startColor.g, 0.02f);
                Assert.AreEqual(0.75f, ring.startColor.b, 0.02f);
                Assert.AreEqual(0.25f, ring.startColor.a, 0.02f);

                // Still visible during the 1.1s hold window
                stepRing.Invoke(player, new object[] { 0.6f });
                Assert.IsTrue(ring.enabled, "ReachGuideCircle must stay visible during the 1.1s hold window.");
                Assert.AreEqual(0.25f, ring.startColor.a, 0.02f);

                // Past 1.1s hold -> begins fading and eventually disables
                stepRing.Invoke(player, new object[] { 0.6f });
                stepRing.Invoke(player, new object[] { 0.5f });
                Assert.IsFalse(ring.enabled, "ReachGuideCircle must fade out and disable after 1.1s.");

                // When toggled OFF, triggering does NOT enable the circle
                setReachMethod.Invoke(settingsMgr, new object[] { false });
                triggerRing.Invoke(player, null);
                Assert.IsFalse(ring.enabled, "ReachGuideCircle must stay disabled when ReachIndicatorEnabled is OFF.");

                playerType.GetMethod("HideReachGuideImmediate", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                Assert.IsFalse(ring.enabled);
            }
            finally
            {
                if (hadReachPref) PlayerPrefs.SetInt(reachKey, prevReachPref);
                else PlayerPrefs.DeleteKey(reachKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void PlayerInventory_ToggleFlashlight_TogglesStowedFlashlightAndKeepsBeamActive()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            Type invType = ResolveRuntimeType("PlayerInventory");
            Type equipType = ResolveRuntimeType("EquipmentItem");
            Type flashlightType = ResolveRuntimeType("FlashlightItem");
            Type emfType = ResolveRuntimeType("EMFReaderItem");

            GameObject playerGo = Track(new GameObject("TestPlayer_FlashlightHotkey"));
            playerGo.AddComponent<Unity.Netcode.NetworkObject>();
            playerGo.AddComponent<Rigidbody2D>();
            Component player = playerGo.AddComponent(playerType);
            Component inv = playerGo.AddComponent(invType);

            GameObject emfGo = Track(new GameObject("TestActiveEmf"));
            emfGo.AddComponent<Unity.Netcode.NetworkObject>();
            Component emf = emfGo.AddComponent(emfType);

            GameObject flashGo = Track(new GameObject("TestStowedFlashlight"));
            flashGo.AddComponent<Unity.Netcode.NetworkObject>();
            SpriteRenderer flashSr = flashGo.AddComponent<SpriteRenderer>();
            FieldInfo spotlightField = flashlightType.GetField("spotlight", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(spotlightField);
            GameObject lightGo = new GameObject("Spotlight");
            lightGo.transform.SetParent(flashGo.transform, false);
            var spotlight = lightGo.AddComponent(spotlightField.FieldType) as Behaviour;
            Assert.IsNotNull(spotlight);
            Component flashlight = flashGo.AddComponent(flashlightType);

            playerType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            invType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(inv, null);
            emfType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(emf, null);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);

            try
            {
                ulong clientId = ((Unity.Netcode.NetworkBehaviour)player).OwnerClientId;
                var ownerVar = (Unity.Netcode.NetworkVariable<ulong>)equipType
                    .GetField("ownerClientId", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(flashlight);
                ownerVar.Value = clientId;

                Array slots = invType.GetField("slots", BindingFlags.Public | BindingFlags.Instance).GetValue(inv) as Array;
                Assert.IsNotNull(slots);
                slots.SetValue(emf, 0);
                slots.SetValue(flashlight, 1);
                invType.GetField("currentSlotIndex", BindingFlags.Public | BindingFlags.Instance).SetValue(inv, 0);

                equipType.GetMethod("ServerSetInHand", BindingFlags.Public | BindingFlags.Instance).Invoke(flashlight, new object[] { false });

                MethodInfo toggleFlash = invType.GetMethod("ToggleFlashlight", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo hasActiveFlash = invType.GetMethod("HasActiveHeldFlashlight", BindingFlags.Public | BindingFlags.Static);
                MethodInfo hasActiveEquip = invType.GetMethod("HasActiveHeldEquipment", BindingFlags.Public | BindingFlags.Static);
                PropertyInfo isLightOnProp = flashlightType.GetProperty("IsLightOn", BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(toggleFlash);
                Assert.IsFalse((bool)isLightOnProp.GetValue(flashlight));
                Assert.IsFalse(spotlight.enabled, "Flashlight beam should start disabled before toggling on.");
                Assert.IsFalse(flashSr.enabled, "Stowed flashlight sprite must stay hidden initially.");

                // Toggle ON while holding EMF in slot 0
                bool toggledOn = (bool)toggleFlash.Invoke(inv, null);
                Assert.IsTrue(toggledOn, "ToggleFlashlight should find and toggle stowed flashlight in slot 1.");
                Assert.IsTrue((bool)isLightOnProp.GetValue(flashlight));
                Assert.IsTrue(spotlight.enabled, "Stowed active flashlight beam must remain enabled.");
                Assert.IsFalse(flashSr.enabled, "Stowed flashlight sprite must stay hidden.");
                Assert.IsTrue((bool)hasActiveFlash.Invoke(null, new object[] { player }), "Stowed active flashlight must satisfy HasActiveHeldFlashlight.");
                Assert.IsTrue((bool)hasActiveEquip.Invoke(null, new object[] { player }), "Stowed active flashlight must satisfy HasActiveHeldEquipment.");

                // Toggle OFF
                bool toggledOff = (bool)toggleFlash.Invoke(inv, null);
                Assert.IsTrue(toggledOff);
                Assert.IsFalse((bool)isLightOnProp.GetValue(flashlight));
                Assert.IsFalse(spotlight.enabled);
                Assert.IsFalse((bool)hasActiveFlash.Invoke(null, new object[] { player }));
            }
            finally
            {
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
                emfType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(emf, null);
                playerType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            }
        }

        [Test]
        public void PlayerController_HoverSeparation_SplitsEnvironmentAndEquipmentChannels()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            Type doorType = ResolveRuntimeType("NetworkDoor");
            Type flashlightType = ResolveRuntimeType("FlashlightItem");

            Texture2D tex = new Texture2D(16, 16);
            Sprite itemSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 64f);

            GameObject playerGo = Track(new GameObject("TestPlayer_HoverChannels"));
            playerGo.transform.position = Vector3.zero;
            playerGo.AddComponent<Rigidbody2D>();
            Component player = playerGo.AddComponent(playerType);
            playerType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetField("maxReachRadius", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, 0.5f);
            playerType.GetField("mousePosition", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, new Vector2(0.1f, 0f));

            GameObject doorGo = Track(new GameObject("TestHoverDoor"));
            doorGo.transform.position = new Vector3(0.1f, 0f, 0f);
            var doorCol = doorGo.AddComponent<BoxCollider2D>();
            doorCol.isTrigger = true;
            doorCol.size = new Vector2(0.2f, 0.2f);
            Component door = doorGo.AddComponent(doorType);
            doorType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);
            doorType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);

            GameObject flashGo = Track(new GameObject("TestHoverFlashlight"));
            flashGo.transform.position = new Vector3(0.1f, 0f, 0f);
            var flashSr = flashGo.AddComponent<SpriteRenderer>();
            flashSr.sprite = itemSprite;
            Component flashlight = flashGo.AddComponent(flashlightType);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);

            try
            {
                Physics2D.SyncTransforms();

                MethodInfo calcEnv = playerType.GetMethod("CalculateHoveredEnvironment", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo calcEquip = playerType.GetMethod("CalculateHoveredEquipment", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(calcEnv);
                Assert.IsNotNull(calcEquip);

                object hoveredEnv = calcEnv.Invoke(player, null);
                object hoveredEquip = calcEquip.Invoke(player, null);

                Assert.AreSame(door, hoveredEnv, "Environment channel [E] must resolve NetworkDoor and ignore ground FlashlightItem.");
                Assert.AreSame(flashlight, hoveredEquip, "Equipment channel [L-Click] must resolve ground FlashlightItem and ignore NetworkDoor.");
            }
            finally
            {
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
                doorType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);
                playerType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                UnityEngine.Object.DestroyImmediate(itemSprite);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void SpiritBoxDialogueUI_ClickInsideSubmitsOption_ClickOutsideCloses_AndFooterMatches()
        {
            var sbDoc = CreateTestUiDocument("TestSpiritBoxDialogue", SpiritBoxDialogueUxmlPath);
            Type sbUiType = ResolveRuntimeType("SpiritBoxDialogueUI");
            Type sbItemType = ResolveRuntimeType("SpiritBoxItem");

            var footerLabel = sbDoc.rootVisualElement.Q<Label>(className: "sb-footer-text");
            Assert.IsNotNull(footerLabel);
            Assert.IsTrue(footerLabel.text.Contains("[1-5 / LMB] Ask Question"), "Footer must document LMB option ask.");
            Assert.IsTrue(footerLabel.text.Contains("[CLICK OUTSIDE / ESC] Turn Off & Close"), "Footer must document click-outside/ESC close.");

            Component sbUi = sbDoc.gameObject.AddComponent(sbUiType);
            sbUiType.GetField("uiDocument", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(sbUi, sbDoc);
            sbUiType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(sbUi, null);
            sbUiType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(sbUi, null);

            GameObject sbGo = Track(new GameObject("TestSpiritBoxItem"));
            Component sbItem = sbGo.AddComponent(sbItemType);

            PropertyInfo instanceProp = sbUiType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo isOpenProp = sbUiType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo openMethod = sbUiType.GetMethod("OpenDialogue", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo onButtonClicked = sbUiType.GetMethod("OnButtonClicked", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo tryBackdrop = sbUiType.GetMethod("TryHandleBackdropClick", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo highlightedField = sbUiType.GetField("highlightedIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo transmittingField = sbUiType.GetField("isTransmitting", BindingFlags.NonPublic | BindingFlags.Instance);

            try
            {
                openMethod.Invoke(sbUi, new object[] { sbItem, null });
                Assert.IsTrue((bool)isOpenProp.GetValue(sbUi));

                // Bypass the 0.25s open debounce
                sbUiType.GetField("openedFrame", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(sbUi, -1);
                sbUiType.GetField("openTime", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(sbUi, Time.unscaledTime - 1f);

                // Clicking option button 2 asks question and keeps Spirit Box open
                onButtonClicked.Invoke(sbUi, new object[] { 2 });
                Assert.IsTrue((bool)isOpenProp.GetValue(sbUi), "Clicking an option inside SpiritBoxPanel must not close dialogue.");
                Assert.AreEqual(2, (int)highlightedField.GetValue(sbUi));
                transmittingField.SetValue(sbUi, false);

                // Clicking inside SpiritBoxPanel does not trigger backdrop close
                var option0 = sbDoc.rootVisualElement.Q<Button>("Option0");
                var overlay = sbDoc.rootVisualElement.Q<VisualElement>("SpiritBoxOverlay");
                Assert.IsNotNull(option0);
                Assert.IsNotNull(overlay);

                bool closedByInside = (bool)tryBackdrop.Invoke(sbUi, new object[] { option0, Vector2.zero, 0 });
                Assert.IsFalse(closedByInside, "Click inside panel must not close Spirit Box dialogue.");
                Assert.IsTrue((bool)isOpenProp.GetValue(sbUi));

                // Clicking backdrop outside SpiritBoxPanel closes & turns off Spirit Box
                bool closedByOutside = (bool)tryBackdrop.Invoke(sbUi, new object[] { overlay, Vector2.zero, 0 });
                Assert.IsTrue(closedByOutside, "Click outside panel on overlay must close Spirit Box dialogue.");
                Assert.IsFalse((bool)isOpenProp.GetValue(sbUi));
            }
            finally
            {
                instanceProp?.SetValue(null, null);
            }
        }

        [Test]
        public void PlayerController_ExactHoverDetection_RequiresCursorDirectlyOnItemAndTightSanityPillsCollider()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            Type flashlightType = ResolveRuntimeType("FlashlightItem");
            Type pillsType = ResolveRuntimeType("SanityPillsItem");

            Texture2D tex = new Texture2D(16, 16);
            Sprite itemSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 500f); // 0.032 x 0.032 world units

            GameObject playerGo = Track(new GameObject("TestPlayer_TightHover"));
            playerGo.transform.position = Vector3.zero;
            playerGo.AddComponent<Rigidbody2D>();
            Component player = playerGo.AddComponent(playerType);
            playerType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetField("maxReachRadius", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, 0.5f);

            GameObject flashGo = Track(new GameObject("TestTightHoverFlashlight"));
            flashGo.transform.position = new Vector3(0.10f, 0f, 0f);
            var flashSr = flashGo.AddComponent<SpriteRenderer>();
            flashSr.sprite = itemSprite;
            Component flashlight = flashGo.AddComponent(flashlightType);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);

            GameObject pillsGo = Track(new GameObject("TestTightHoverSanityPills"));
            pillsGo.transform.position = new Vector3(0.25f, 0f, 0f);
            var pillsSr = pillsGo.AddComponent<SpriteRenderer>();
            pillsSr.sprite = itemSprite;
            Component pills = pillsGo.AddComponent(pillsType);
            AudioClip dummyPillsClip = AudioClip.Create("DummyPillsClip", 64, 1, 44100, false);
            pillsType.GetField("consumeAudioClip", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(pills, dummyPillsClip);
            pillsType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pills, null);
            pillsType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pills, null);

            try
            {
                Assert.IsNull(pillsGo.GetComponent<CircleCollider2D>(), "SanityPillsItem must not use an oversized CircleCollider2D.");
                Assert.IsNotNull(pillsGo.GetComponent<PolygonCollider2D>(), "SanityPillsItem should use sprite-fitted PolygonCollider2D.");

                Physics2D.SyncTransforms();
                FieldInfo mouseField = playerType.GetField("mousePosition", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo calcEquip = playerType.GetMethod("CalculateHoveredEquipment", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(mouseField);
                Assert.IsNotNull(calcEquip);

                // 0.04f away from Flashlight -> must NOT hover
                mouseField.SetValue(player, new Vector2(0.10f, 0.04f));
                Assert.IsNull(calcEquip.Invoke(player, null), "Cursor 0.04f away must not glow/hover Flashlight.");

                // Directly on Flashlight -> hovers
                mouseField.SetValue(player, new Vector2(0.10f, 0f));
                Assert.AreSame(flashlight, calcEquip.Invoke(player, null), "Cursor directly on Flashlight must hover it.");

                // 0.04f away from SanityPills -> must NOT hover
                mouseField.SetValue(player, new Vector2(0.25f, 0.04f));
                Assert.IsNull(calcEquip.Invoke(player, null), "Cursor 0.04f away must not glow/hover SanityPills.");

                // Directly on SanityPills -> hovers
                mouseField.SetValue(player, new Vector2(0.25f, 0f));
                Assert.AreSame(pills, calcEquip.Invoke(player, null), "Cursor directly on SanityPills must hover it.");
            }
            finally
            {
                pillsType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pills, null);
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
                playerType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                UnityEngine.Object.DestroyImmediate(dummyPillsClip);
                UnityEngine.Object.DestroyImmediate(itemSprite);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void PlayerInventory_DropCurrentItem_DropsDirectlyUnderneathPlayer()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            Type invType = ResolveRuntimeType("PlayerInventory");
            Type equipType = ResolveRuntimeType("EquipmentItem");
            Type flashlightType = ResolveRuntimeType("FlashlightItem");

            GameObject playerGo = Track(new GameObject("TestPlayer_UnderDrop"));
            playerGo.AddComponent<Unity.Netcode.NetworkObject>();
            playerGo.AddComponent<Rigidbody2D>();
            Component player = playerGo.AddComponent(playerType);
            Component inv = playerGo.AddComponent(invType);

            GameObject flashGo = Track(new GameObject("TestFlashlight_UnderDrop"));
            flashGo.AddComponent<Unity.Netcode.NetworkObject>();
            flashGo.AddComponent<SpriteRenderer>();
            Component flashlight = flashGo.AddComponent(flashlightType);

            playerType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            invType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(inv, null);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);

            try
            {
                ulong clientId = ((Unity.Netcode.NetworkBehaviour)player).OwnerClientId;
                var ownerVar = (Unity.Netcode.NetworkVariable<ulong>)equipType
                    .GetField("ownerClientId", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(flashlight);
                ownerVar.Value = clientId;

                Array slots = invType.GetField("slots", BindingFlags.Public | BindingFlags.Instance).GetValue(inv) as Array;
                Assert.IsNotNull(slots);
                slots.SetValue(flashlight, 0);
                invType.GetField("currentSlotIndex", BindingFlags.Public | BindingFlags.Instance).SetValue(inv, 0);

                Vector3 playerPos = new Vector3(1.25f, -0.75f, 0.5f);
                invType.GetMethod("DropCurrentItem", BindingFlags.Public | BindingFlags.Instance)?.Invoke(inv, new object[] { playerPos });

                Assert.IsNull(invType.GetProperty("CurrentItem", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inv));
                Assert.AreEqual(1.25f, flashGo.transform.position.x, 0.0001f);
                Assert.AreEqual(-0.75f, flashGo.transform.position.y, 0.0001f);
                Assert.AreEqual(0f, flashGo.transform.position.z, 0.0001f);
            }
            finally
            {
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
                playerType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            }
        }

        [Test]
        public void ReachCircleSetting_PersistsAndBindsInMainMenuAndPauseMenu()
        {
            Type settingsType = ResolveRuntimeType("GameSettingsManager");
            Type pauseType = ResolveRuntimeType("PauseMenuController");
            Type mainType = ResolveRuntimeType("MainMenuController");

            const string reachKey = "reach_indicator_enabled";
            bool hadReachPref = PlayerPrefs.HasKey(reachKey);
            int prevReachPref = PlayerPrefs.GetInt(reachKey, 1);

            var settingsGo = Track(new GameObject("TestSettingsMgr_UI"));
            Component settingsMgr = settingsGo.AddComponent(settingsType);
            settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, settingsMgr);

            try
            {
                PlayerPrefs.DeleteKey(reachKey);
                settingsType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(settingsMgr, null);
                PropertyInfo reachProp = settingsType.GetProperty("ReachIndicatorEnabled", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsTrue((bool)reachProp.GetValue(settingsMgr), "ReachIndicatorEnabled must default to true (ON).");

                // PauseMenu binding
                var pauseDoc = CreateTestUiDocument("TestPauseMenu_Reach", PauseMenuUxmlPath);
                Component pause = pauseDoc.gameObject.AddComponent(pauseType);
                pauseType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);
                pauseType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pause, null);

                var pauseToggle = pauseDoc.rootVisualElement.Q<Toggle>("toggle-reach-indicator");
                Assert.IsNotNull(pauseToggle, "PauseMenu.uxml must expose toggle-reach-indicator.");
                Assert.IsTrue(pauseToggle.value);

                pauseToggle.value = false;
                Assert.IsFalse((bool)reachProp.GetValue(settingsMgr), "Toggling PauseMenu reach toggle OFF must update GameSettingsManager.");
                Assert.AreEqual(0, PlayerPrefs.GetInt(reachKey, 1));

                // MainMenu binding
                var mainDoc = CreateTestUiDocument("TestMainMenu_Reach", MainMenuUxmlPath);
                Component main = mainDoc.gameObject.AddComponent(mainType);
                mainType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(main, null);
                mainType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(main, null);

                var mainToggle = mainDoc.rootVisualElement.Q<Toggle>("toggle-reach-indicator");
                Assert.IsNotNull(mainToggle, "MainMenu.uxml must expose toggle-reach-indicator.");
                Assert.IsFalse(mainToggle.value, "MainMenu must sync toggle-reach-indicator from GameSettingsManager.");

                mainToggle.value = true;
                Assert.IsTrue((bool)reachProp.GetValue(settingsMgr), "Toggling MainMenu reach toggle ON must update GameSettingsManager.");
                Assert.AreEqual(1, PlayerPrefs.GetInt(reachKey, 0));
            }
            finally
            {
                if (hadReachPref) PlayerPrefs.SetInt(reachKey, prevReachPref);
                else PlayerPrefs.DeleteKey(reachKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void PlayerSpeechBubble_CannotReachThis_TriggersOnOutOfReachClickInteractAndPlacement()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            Type invType = ResolveRuntimeType("PlayerInventory");
            Type settingsType = ResolveRuntimeType("GameSettingsManager");
            Type flashlightType = ResolveRuntimeType("FlashlightItem");
            Type doorType = ResolveRuntimeType("NetworkDoor");
            Type indicatorType = ResolveRuntimeType("ItemHoverIndicator");

            const string reachKey = "reach_indicator_enabled";
            bool hadReachPref = PlayerPrefs.HasKey(reachKey);
            int prevReachPref = PlayerPrefs.GetInt(reachKey, 1);

            Texture2D tex = new Texture2D(16, 16);
            Sprite itemSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 500f);

            var settingsGo = Track(new GameObject("TestSettingsMgr_Bubble"));
            Component settingsMgr = settingsGo.AddComponent(settingsType);
            settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, settingsMgr);

            var indicatorGo = Track(new GameObject("TestItemHoverIndicator_Bubble"));
            Component indicator = indicatorGo.AddComponent(indicatorType);
            indicatorType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, indicator);

            GameObject playerGo = Track(new GameObject("TestPlayer_SpeechBubble"));
            playerGo.transform.position = Vector3.zero;
            playerGo.AddComponent<Rigidbody2D>();
            Component player = playerGo.AddComponent(playerType);
            Component inv = playerGo.AddComponent(invType);

            GameObject farFlashGo = Track(new GameObject("FarFlashlight"));
            farFlashGo.transform.position = new Vector3(0.40f, 0f, 0f);
            var farSr = farFlashGo.AddComponent<SpriteRenderer>();
            farSr.sprite = itemSprite;
            Component farFlash = farFlashGo.AddComponent(flashlightType);

            GameObject heldFlashGo = Track(new GameObject("HeldFlashlight"));
            heldFlashGo.AddComponent<Unity.Netcode.NetworkObject>();
            heldFlashGo.AddComponent<SpriteRenderer>();
            Component heldFlash = heldFlashGo.AddComponent(flashlightType);

            GameObject farDoorGo = Track(new GameObject("FarDoor"));
            farDoorGo.transform.position = new Vector3(0.50f, 0f, 0f);
            var doorCol = farDoorGo.AddComponent<BoxCollider2D>();
            doorCol.isTrigger = true;
            doorCol.size = new Vector2(0.10f, 0.10f);
            Component farDoor = farDoorGo.AddComponent(doorType);

            settingsType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(settingsMgr, null);
            playerType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            invType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(inv, null);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(farFlash, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(farFlash, null);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(heldFlash, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(heldFlash, null);
            doorType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(farDoor, null);
            doorType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(farDoor, null);

            try
            {
                playerType.GetField("maxReachRadius", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, 0.15f);
                Physics2D.SyncTransforms();

                FieldInfo mouseField = playerType.GetField("mousePosition", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo handleLeftClick = playerType.GetMethod("HandleLeftClickDown", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo tryInteract = playerType.GetMethod("TryInteract", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo tryPlace = playerType.GetMethod("TryPlaceHeldItemAtMouse", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo setReachMethod = settingsType.GetMethod("SetReachIndicatorEnabled", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo hideBubble = indicatorType.GetMethod("HideSpeechBubble", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo isVisibleProp = indicatorType.GetProperty("IsSpeechBubbleVisible", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo textProp = indicatorType.GetProperty("CurrentSpeechBubbleText", BindingFlags.Public | BindingFlags.Instance);

                // Ensure reach circle renderer is initialized
                playerType.GetMethod("EnsureReachGuideRenderer", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                LineRenderer ring = playerGo.transform.Find("ReachGuideCircle").GetComponent<LineRenderer>();

                // --- Reach Circle Setting OFF ---
                setReachMethod.Invoke(settingsMgr, new object[] { false });

                // 1. L-Click on out-of-reach ground item triggers "I can't reach this!" WITHOUT showing reach circle
                mouseField.SetValue(player, new Vector2(0.40f, 0f));
                handleLeftClick.Invoke(player, null);
                Assert.IsTrue((bool)isVisibleProp.GetValue(indicator));
                Assert.AreEqual("I can't reach this!", (string)textProp.GetValue(indicator));
                Assert.IsFalse(ring.enabled, "Reach circle must stay hidden when accessibility option is OFF.");

                // 2. [E] on out-of-reach door ALSO triggers "I can't reach this!" (while keeping reach circle hidden when OFF)
                hideBubble.Invoke(indicator, null);
                mouseField.SetValue(player, new Vector2(0.50f, 0f));
                tryInteract.Invoke(player, null);
                Assert.IsTrue((bool)isVisibleProp.GetValue(indicator), "Pressing [E] on an out-of-reach door must show 'I can't reach this!'.");
                Assert.AreEqual("I can't reach this!", (string)textProp.GetValue(indicator));
                Assert.IsFalse(ring.enabled, "Reach circle must stay hidden when accessibility option is OFF.");

                // 3. R-Click placement outside maxReachRadius triggers "I can't reach this!" WITHOUT showing reach circle
                hideBubble.Invoke(indicator, null);
                Array slots = invType.GetField("slots", BindingFlags.Public | BindingFlags.Instance).GetValue(inv) as Array;
                slots.SetValue(heldFlash, 0);
                invType.GetField("currentSlotIndex", BindingFlags.Public | BindingFlags.Instance).SetValue(inv, 0);

                mouseField.SetValue(player, new Vector2(0.35f, 0f));
                bool placedOutOfReach = (bool)tryPlace.Invoke(player, null);
                Assert.IsFalse(placedOutOfReach);
                Assert.IsTrue((bool)isVisibleProp.GetValue(indicator));
                Assert.AreEqual("I can't reach this!", (string)textProp.GetValue(indicator));
                Assert.IsFalse(ring.enabled, "Reach circle must stay hidden when accessibility option is OFF.");

                // --- Reach Circle Setting ON ---
                setReachMethod.Invoke(settingsMgr, new object[] { true });

                // 4. L-Click on out-of-reach ground item triggers "I can't reach this!" AND shows reach circle
                hideBubble.Invoke(indicator, null);
                mouseField.SetValue(player, new Vector2(0.40f, 0f));
                handleLeftClick.Invoke(player, null);
                Assert.IsTrue((bool)isVisibleProp.GetValue(indicator));
                Assert.AreEqual("I can't reach this!", (string)textProp.GetValue(indicator));
                Assert.IsTrue(ring.enabled, "Reach circle must appear when accessibility option is ON.");

                // 5. R-Click placement behind a wall inside maxReachRadius triggers "I can't reach this!" AND shows reach circle
                playerType.GetMethod("HideReachGuideImmediate", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                int wallsLayer = LayerMask.NameToLayer("Walls");
                if (wallsLayer >= 0)
                {
                    hideBubble.Invoke(indicator, null);
                    GameObject wallGo = Track(new GameObject("TestWall_PlacementBlock"));
                    wallGo.layer = wallsLayer;
                    wallGo.transform.position = new Vector3(0.05f, 0f, 0f);
                    var wallCol = wallGo.AddComponent<BoxCollider2D>();
                    wallCol.size = new Vector2(0.02f, 0.25f);
                    Physics2D.SyncTransforms();

                    mouseField.SetValue(player, new Vector2(0.10f, 0f));
                    bool placedBehindWall = (bool)tryPlace.Invoke(player, null);
                    Assert.IsFalse(placedBehindWall);
                    Assert.IsTrue((bool)isVisibleProp.GetValue(indicator));
                    Assert.AreEqual("I can't reach this!", (string)textProp.GetValue(indicator));
                    Assert.IsTrue(ring.enabled, "Reach circle must appear on wall-blocked R-Click when accessibility option is ON.");
                }
            }
            finally
            {
                if (hadReachPref) PlayerPrefs.SetInt(reachKey, prevReachPref);
                else PlayerPrefs.DeleteKey(reachKey);
                PlayerPrefs.Save();

                indicatorType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);
                doorType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(farDoor, null);
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(heldFlash, null);
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(farFlash, null);
                playerType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                UnityEngine.Object.DestroyImmediate(itemSprite);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void PlayerController_LeftClickAndE_WorkWithReachCircleOffAndAcrossChannels()
        {
            Type playerType = ResolveRuntimeType("PlayerController");
            Type settingsType = ResolveRuntimeType("GameSettingsManager");
            Type doorType = ResolveRuntimeType("NetworkDoor");
            Type flashlightType = ResolveRuntimeType("FlashlightItem");

            const string reachKey = "reach_indicator_enabled";
            bool hadReachPref = PlayerPrefs.HasKey(reachKey);
            int prevReachPref = PlayerPrefs.GetInt(reachKey, 1);

            Texture2D tex = new Texture2D(16, 16);
            Sprite itemSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 500f);

            GameObject settingsGo = Track(new GameObject("TestSettings_ControlsFix"));
            Component settingsMgr = settingsGo.AddComponent(settingsType);
            settingsType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(settingsMgr, null);

            GameObject playerGo = Track(new GameObject("TestPlayer_ControlsFix"));
            playerGo.transform.position = Vector3.zero;
            playerGo.AddComponent<Rigidbody2D>();
            Component player = playerGo.AddComponent(playerType);
            playerType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
            playerType.GetField("maxReachRadius", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, 0.5f);

            GameObject flashGo = Track(new GameObject("TestControls_Flashlight"));
            flashGo.transform.position = new Vector3(0.10f, 0f, 0f);
            var flashSr = flashGo.AddComponent<SpriteRenderer>();
            flashSr.sprite = itemSprite;
            Component flashlight = flashGo.AddComponent(flashlightType);
            flashlightType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
            flashlightType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);

            GameObject doorGo = Track(new GameObject("TestControls_Door"));
            doorGo.transform.position = new Vector3(0.20f, 0f, 0f);
            var doorCol = doorGo.AddComponent<BoxCollider2D>();
            doorCol.isTrigger = true;
            doorCol.size = new Vector2(0.1f, 0.1f);
            Component door = doorGo.AddComponent(doorType);
            doorType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);
            doorType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);

            try
            {
                Physics2D.SyncTransforms();

                MethodInfo setReachMethod = settingsType.GetMethod("SetReachIndicatorEnabled", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo stepReach = playerType.GetMethod("StepReachGuideCircle", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo handleLeftClick = playerType.GetMethod("HandleLeftClickDown", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo tryInteract = playerType.GetMethod("TryInteract", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo hoveredEqField = playerType.GetField("currentlyHoveredEquipment", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo hoveredEnvField = playerType.GetField("currentlyHoveredEnvironment", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo pendingField = playerType.GetField("pendingLeftClickTarget", BindingFlags.NonPublic | BindingFlags.Instance);

                // 1. Turning Reach Circle OFF must NOT wipe pendingLeftClickTarget during StepReachGuideCircle
                setReachMethod.Invoke(settingsMgr, new object[] { false });
                hoveredEqField.SetValue(player, flashlight);
                hoveredEnvField.SetValue(player, null);
                handleLeftClick.Invoke(player, null);
                Assert.AreSame(flashlight, pendingField.GetValue(player), "L-Click down on draggable equipment should set pendingLeftClickTarget.");
                stepReach.Invoke(player, new object[] { 0.016f });
                Assert.AreSame(flashlight, pendingField.GetValue(player), "StepReachGuideCircle must not clear pendingLeftClickTarget when Reach Circle is OFF.");

                // 2. [L-Click] on hovered environment (when no equipment is hovered) must NOT open doors or set pendingLeftClickTarget
                hoveredEqField.SetValue(player, null);
                hoveredEnvField.SetValue(player, door);
                handleLeftClick.Invoke(player, null);
                Assert.IsNull(pendingField.GetValue(player), "L-Click must not target or drag environment objects.");

                // 3. [E] must only interact with environment, not equipment
                hoveredEnvField.SetValue(player, null);
                hoveredEqField.SetValue(player, flashlight);
                Assert.DoesNotThrow(() => tryInteract.Invoke(player, null));

                // 4. LobbyBriefingTableInteractable behaves as environment and shows green hover indicator
                Type briefingType = ResolveRuntimeType("LobbyBriefingTableInteractable");
                GameObject deskGo = Track(new GameObject("TestLobbyDesk"));
                deskGo.transform.position = new Vector3(0.15f, 0f, 0f);
                var deskCol = deskGo.AddComponent<BoxCollider2D>();
                deskCol.isTrigger = true;
                deskCol.size = new Vector2(0.1f, 0.1f);
                Component desk = deskGo.AddComponent(briefingType);
                Physics2D.SyncTransforms();

                playerType.GetField("mousePosition", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(player, new Vector2(0.15f, 0f));
                MethodInfo calcEnv = playerType.GetMethod("CalculateHoveredEnvironment", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo calcEquip = playerType.GetMethod("CalculateHoveredEquipment", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.AreSame(desk, calcEnv.Invoke(player, null), "Lobby desk must resolve on the environment channel.");
                Assert.IsNull(calcEquip.Invoke(player, null), "Lobby desk must not resolve on the equipment channel.");

                hoveredEnvField.SetValue(player, desk);
                hoveredEqField.SetValue(player, null);
                playerType.GetMethod("UpdateItemHoverIndicator", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                Type indicatorType = ResolveRuntimeType("ItemHoverIndicator");
                object indicator = indicatorType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Assert.IsNotNull(indicator);
                Assert.AreSame(deskGo, indicatorType.GetField("currentTarget", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(indicator),
                    "Hovering the lobby desk must activate the green ItemHoverIndicator glow.");
            }
            finally
            {
                if (hadReachPref) PlayerPrefs.SetInt(reachKey, prevReachPref);
                else PlayerPrefs.DeleteKey(reachKey);
                PlayerPrefs.Save();

                doorType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(door, null);
                flashlightType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flashlight, null);
                playerType.GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);
                UnityEngine.Object.DestroyImmediate(itemSprite);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
