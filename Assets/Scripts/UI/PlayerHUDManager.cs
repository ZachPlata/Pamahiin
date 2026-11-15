using UnityEngine;
using UnityEngine.UIElements;

public class PlayerHUDManager : MonoBehaviour
{
    [SerializeField] private UIDocument hudDocument;
    
    private PlayerInventory localInventory; 
    private VisualElement[] slots = new VisualElement[PlayerInventory.MaxSlots];
    private Label[] slotLabels = new Label[PlayerInventory.MaxSlots];
    private VisualElement[] powerIndicators = new VisualElement[PlayerInventory.MaxSlots];
    private VisualElement[] emfContainers = new VisualElement[PlayerInventory.MaxSlots];
    private VisualElement[,] emfDots = new VisualElement[PlayerInventory.MaxSlots, 5];
    private VisualElement[] temperatureContainers = new VisualElement[PlayerInventory.MaxSlots];
    private Label[] temperatureTexts = new Label[PlayerInventory.MaxSlots];

    // Camera Night Vision Viewfinder elements
    private VisualElement nvOverlay;
    private VisualElement nvRecDot;
    private Label nvTimer;
    private Label nvTimestamp;
    private float nvRecBlinkTimer = 0f;
    private bool nvRecBlinkState = true;

    // Death / End Game Screen elements
    private VisualElement deathOverlay;
    private Button btnEndGame;
    private PlayerController localPlayerController;

    // Dirty-state caches so we don't mutate UI Toolkit styles/text every frame unnecessarily
    private int lastSortingOrder = -1;
    private bool? lastHudRootVisible;
    private bool? lastDeadState;
    private bool? lastHoldingNvCam;
    private int lastNvClockSecond = -1;
    private readonly bool?[] lastPowerVisible = new bool?[PlayerInventory.MaxSlots];
    private readonly bool?[] lastPowerOn = new bool?[PlayerInventory.MaxSlots];
    private readonly bool?[] lastEmfVisible = new bool?[PlayerInventory.MaxSlots];
    private readonly int[] lastEmfLevel = new int[] { -1, -1, -1, -1 };
    private readonly bool?[] lastTempVisible = new bool?[PlayerInventory.MaxSlots];
    private readonly int[] lastTempTenths = new int[] { int.MinValue, int.MinValue, int.MinValue, int.MinValue };

    private static readonly Color[] EmfColors = { Color.green, Color.green, Color.yellow, new Color(1f, 0.5f, 0f), Color.red };
    private static readonly StyleColor PowerOnStyle = new StyleColor(Color.green);
    private static readonly StyleColor PowerOffStyle = new StyleColor(Color.red);
    private static readonly StyleColor EmfOffStyle = new StyleColor(Color.gray);
    private static readonly StyleColor TempColdStyle = new StyleColor(Color.cyan);
    private static readonly StyleColor TempNormalStyle = new StyleColor(Color.white);

    private void OnEnable()
    {
        if (hudDocument == null || hudDocument.rootVisualElement == null) return;
        
        hudDocument.sortingOrder = 1; // Explicitly put HUD in background
        var root = hudDocument.rootVisualElement;

        for (int i = 0; i < PlayerInventory.MaxSlots; i++)
        {
            slots[i] = root.Q<VisualElement>($"Slot{i}");
            slotLabels[i] = root.Q<Label>($"ItemText{i}");
            powerIndicators[i] = root.Q<VisualElement>($"PowerIndicator{i}");
            emfContainers[i] = root.Q<VisualElement>($"EmfContainer{i}");
            for (int j = 0; j < 5; j++)
            {
                emfDots[i, j] = root.Q<VisualElement>($"EmfDot{i}_{j}");
            }
            temperatureContainers[i] = root.Q<VisualElement>($"TemperatureContainer{i}");
            temperatureTexts[i] = root.Q<Label>($"TemperatureText{i}");
        }

        nvOverlay = root.Q<VisualElement>("NightVisionOverlay");
        nvRecDot = root.Q<VisualElement>("NvRecDot");
        nvTimer = root.Q<Label>("NvTimer");
        nvTimestamp = root.Q<Label>("NvTimestamp");

        var pauseButton = root.Q<Button>("PauseButton");
        if (pauseButton != null)
        {
            pauseButton.clicked += OnPauseClicked;
        }

        deathOverlay = root.Q<VisualElement>("DeathOverlay");
        if (deathOverlay != null)
        {
            deathOverlay.AddToClassList("death-overlay-hidden");
            deathOverlay.style.display = DisplayStyle.None;
        }

        btnEndGame = root.Q<Button>("BtnEndGame");
        if (btnEndGame != null)
        {
            btnEndGame.clicked += OnEndGameClicked;
        }

        var tutorialMgr = GetComponent<TutorialTooltipManager>();
        if (tutorialMgr == null) tutorialMgr = gameObject.AddComponent<TutorialTooltipManager>();
        tutorialMgr.BindUI(root);

        FindLocalPlayer();
    }

    private void Update()
    {
        bool isMainMenu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene";
        if (hudDocument != null && hudDocument.rootVisualElement != null && lastHudRootVisible != !isMainMenu)
        {
            lastHudRootVisible = !isMainMenu;
            hudDocument.rootVisualElement.style.display = isMainMenu ? DisplayStyle.None : DisplayStyle.Flex;
        }
        if (isMainMenu) return;

        if (localInventory == null || localPlayerController == null)
        {
            FindLocalPlayer();
        }

        if (deathOverlay != null)
        {
            bool isDead = localPlayerController != null && !localPlayerController.IsAlive;
            if (lastDeadState != isDead)
            {
                lastDeadState = isDead;
                int desiredOrder = isDead ? 100 : 1;
                if (hudDocument != null && lastSortingOrder != desiredOrder)
                {
                    lastSortingOrder = desiredOrder;
                    hudDocument.sortingOrder = desiredOrder;
                }

                if (isDead)
                {
                    deathOverlay.RemoveFromClassList("death-overlay-hidden");
                    deathOverlay.style.display = DisplayStyle.Flex;
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                    UnityEngine.Cursor.visible = true;
                }
                else
                {
                    deathOverlay.AddToClassList("death-overlay-hidden");
                    deathOverlay.style.display = DisplayStyle.None;
                }
            }
        }

        UpdateDynamicHUD();
    }

    private void UpdateDynamicHUD()
    {
        if (localInventory == null) return;

        for (int i = 0; i < PlayerInventory.MaxSlots; i++)
        {
            var item = localInventory.slots[i];

            // Power Indicator logic (update style only when visibility or power changes)
            bool showPower = item != null && (item is FlashlightItem || item is VideoCameraItem || item is DotsProjectorItem || item is SpiritBoxItem || item is ThermometerItem);
            if (powerIndicators[i] != null)
            {
                if (lastPowerVisible[i] != showPower)
                {
                    lastPowerVisible[i] = showPower;
                    powerIndicators[i].style.display = showPower ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!showPower) lastPowerOn[i] = null;
                }

                if (showPower)
                {
                    bool isOn = item.IsPoweredOn;
                    if (lastPowerOn[i] != isOn)
                    {
                        lastPowerOn[i] = isOn;
                        powerIndicators[i].style.backgroundColor = isOn ? PowerOnStyle : PowerOffStyle;
                    }
                }
            }

            // EMF Reader logic (update only when container visibility or EMF level changes)
            EMFReaderItem emfReader = item as EMFReaderItem;
            bool showEmf = emfReader != null;
            if (emfContainers[i] != null && lastEmfVisible[i] != showEmf)
            {
                lastEmfVisible[i] = showEmf;
                emfContainers[i].style.display = showEmf ? DisplayStyle.Flex : DisplayStyle.None;
                if (!showEmf) lastEmfLevel[i] = -1;
            }

            if (showEmf)
            {
                int emfLevel = emfReader.CurrentEmfLevel;
                if (lastEmfLevel[i] != emfLevel)
                {
                    lastEmfLevel[i] = emfLevel;
                    for (int j = 0; j < 5; j++)
                    {
                        if (emfDots[i, j] != null)
                        {
                            emfDots[i, j].style.backgroundColor = j < emfLevel ? new StyleColor(EmfColors[j]) : EmfOffStyle;
                        }
                    }
                }
            }

            // Thermometer logic (update only when visibility or rounded tenth-degree value changes)
            ThermometerItem thermometer = item as ThermometerItem;
            bool showTemp = thermometer != null && thermometer.IsPoweredOn;
            if (temperatureContainers[i] != null && lastTempVisible[i] != showTemp)
            {
                lastTempVisible[i] = showTemp;
                temperatureContainers[i].style.display = showTemp ? DisplayStyle.Flex : DisplayStyle.None;
                if (!showTemp) lastTempTenths[i] = int.MinValue;
            }

            if (showTemp && temperatureTexts[i] != null)
            {
                float temp = thermometer.DisplayedTemperature;
                int tenths = Mathf.RoundToInt(temp * 10f);
                if (lastTempTenths[i] != tenths)
                {
                    lastTempTenths[i] = tenths;
                    temperatureTexts[i].text = $"{(tenths * 0.1f):F1}°C";
                    temperatureTexts[i].style.color = tenths < 0 ? TempColdStyle : TempNormalStyle;
                }
            }
        }

        // Handheld Camera Night Vision Viewfinder logic
        bool isHoldingCamWithNV = false;
        if (localInventory.currentSlotIndex >= 0 && localInventory.currentSlotIndex < PlayerInventory.MaxSlots)
        {
            var heldItem = localInventory.slots[localInventory.currentSlotIndex];
            if (heldItem is VideoCameraItem cam && cam.IsPoweredOn)
            {
                isHoldingCamWithNV = true;
            }
        }

        if (nvOverlay != null)
        {
            if (lastHoldingNvCam != isHoldingCamWithNV)
            {
                lastHoldingNvCam = isHoldingCamWithNV;
                if (isHoldingCamWithNV) nvOverlay.RemoveFromClassList("nv-overlay-hidden");
                else nvOverlay.AddToClassList("nv-overlay-hidden");
            }

            if (isHoldingCamWithNV)
            {
                // Blink REC indicator
                nvRecBlinkTimer += Time.deltaTime;
                if (nvRecBlinkTimer >= 0.5f)
                {
                    nvRecBlinkTimer = 0f;
                    nvRecBlinkState = !nvRecBlinkState;
                    if (nvRecDot != null)
                    {
                        if (nvRecBlinkState) nvRecDot.RemoveFromClassList("nv-rec-dot-dim");
                        else nvRecDot.AddToClassList("nv-rec-dot-dim");
                    }
                }

                // Update timer & clock text once per second instead of allocating strings every frame
                int totalSeconds = Mathf.FloorToInt(Time.timeSinceLevelLoad);
                if (totalSeconds != lastNvClockSecond)
                {
                    lastNvClockSecond = totalSeconds;
                    if (nvTimer != null)
                    {
                        int hours = totalSeconds / 3600;
                        int minutes = (totalSeconds % 3600) / 60;
                        int seconds = totalSeconds % 60;
                        nvTimer.text = $"{hours:00}:{minutes:00}:{seconds:00}";
                    }
                    if (nvTimestamp != null)
                    {
                        nvTimestamp.text = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
                    }
                }
            }
        }
    }

    private void FindLocalPlayer()
    {
        PlayerController pc = PlayerController.LocalInstance;
        if (pc == null && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            var playerObj = Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (playerObj != null) pc = playerObj.GetComponent<PlayerController>();
        }
        if (pc == null && PlayerController.AllPlayers.Count > 0)
        {
            pc = PlayerController.AllPlayers[0];
        }

        if (pc != null)
        {
            localPlayerController = pc;
            localInventory = pc.GetComponent<PlayerInventory>();
            if (localInventory != null)
            {
                localInventory.OnSlotChanged -= HandleSlotChanged;
                localInventory.OnInventoryUpdated -= HandleInventoryUpdated;
                localInventory.OnSlotChanged += HandleSlotChanged;
                localInventory.OnInventoryUpdated += HandleInventoryUpdated;
                RefreshHUD();
            }
        }
    }

    private void OnDisable()
    {
        if (localInventory != null)
        {
            localInventory.OnSlotChanged -= HandleSlotChanged;
            localInventory.OnInventoryUpdated -= HandleInventoryUpdated;
        }
        
        if (hudDocument != null && hudDocument.rootVisualElement != null)
        {
            var pauseButton = hudDocument.rootVisualElement.Q<Button>("PauseButton");
            if (pauseButton != null)
            {
                pauseButton.clicked -= OnPauseClicked;
            }
        }

        if (btnEndGame != null)
        {
            btnEndGame.clicked -= OnEndGameClicked;
        }
    }

    private void HandleSlotChanged(int newSlotIndex)
    {
        RefreshHUD();
    }

    private void HandleInventoryUpdated()
    {
        RefreshHUD();
    }

    private void RefreshHUD()
    {
        if (localInventory == null) return;

        for (int i = 0; i < PlayerInventory.MaxSlots; i++)
        {
            if (slots[i] == null || slotLabels[i] == null) continue;

            if (i == localInventory.currentSlotIndex)
            {
                slots[i].AddToClassList("inventory-slot-active");
            }
            else
            {
                slots[i].RemoveFromClassList("inventory-slot-active");
            }

            var item = localInventory.slots[i];
            if (item != null)
            {
                slotLabels[i].text = item.ItemName;
            }
            else
            {
                slotLabels[i].text = "Empty";
            }
        }
    }

    private void OnPauseClicked()
    {
        var pauseMenu = FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        if (pauseMenu != null)
        {
            pauseMenu.TogglePause();
        }
    }

    private void OnEndGameClicked()
    {
        GameSettingsManager.Instance?.SaveAll();

        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.DisconnectAndReturnToMenu();
        }
        else if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
    }
}
