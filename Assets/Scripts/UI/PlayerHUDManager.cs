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

        var pauseButton = root.Q<Button>("PauseButton");
        if (pauseButton != null)
        {
            pauseButton.clicked += OnPauseClicked;
        }

        FindLocalPlayer();
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene")
        {
            if (hudDocument != null && hudDocument.rootVisualElement != null)
            {
                hudDocument.rootVisualElement.style.display = DisplayStyle.None;
            }
            return;
        }
        else if (hudDocument != null && hudDocument.rootVisualElement != null)
        {
            hudDocument.rootVisualElement.style.display = DisplayStyle.Flex;
        }

        if (localInventory == null)
        {
            FindLocalPlayer();
        }

        UpdateDynamicHUD();
    }

    private void UpdateDynamicHUD()
    {
        if (localInventory == null) return;

        for (int i = 0; i < PlayerInventory.MaxSlots; i++)
        {
            var item = localInventory.slots[i];
            
            // Power Indicator logic
            if (item != null && (item is FlashlightItem || item is VideoCameraItem || item is DotsProjectorItem || item is SpiritBoxItem || item is ThermometerItem))
            {
                if (powerIndicators[i] != null)
                {
                    powerIndicators[i].style.display = DisplayStyle.Flex;
                    powerIndicators[i].style.backgroundColor = item.IsPoweredOn ? new StyleColor(Color.green) : new StyleColor(Color.red);
                }
            }
            else
            {
                if (powerIndicators[i] != null) powerIndicators[i].style.display = DisplayStyle.None;
            }

            // EMF Reader logic
            if (item != null && item is EMFReaderItem emfReader)
            {
                if (emfContainers[i] != null) emfContainers[i].style.display = DisplayStyle.Flex;
                
                int emfLevel = emfReader.CurrentEmfLevel;
                Color[] colors = new Color[] { Color.green, Color.green, Color.yellow, new Color(1f, 0.5f, 0f), Color.red };
                
                for (int j = 0; j < 5; j++)
                {
                    if (emfDots[i, j] != null)
                    {
                        if (j < emfLevel)
                        {
                            emfDots[i, j].style.backgroundColor = new StyleColor(colors[j]);
                        }
                        else
                        {
                            emfDots[i, j].style.backgroundColor = new StyleColor(Color.gray);
                        }
                    }
                }
            }
            else
            {
                if (emfContainers[i] != null) emfContainers[i].style.display = DisplayStyle.None;
            }

            // Thermometer logic
            if (item != null && item is ThermometerItem thermometer && thermometer.IsPoweredOn)
            {
                if (temperatureContainers[i] != null) temperatureContainers[i].style.display = DisplayStyle.Flex;
                if (temperatureTexts[i] != null)
                {
                    float temp = thermometer.DisplayedTemperature;
                    temperatureTexts[i].text = $"{temp:F1}°C";
                    temperatureTexts[i].style.color = temp < 0f ? new StyleColor(Color.cyan) : new StyleColor(Color.white);
                }
            }
            else
            {
                if (temperatureContainers[i] != null) temperatureContainers[i].style.display = DisplayStyle.None;
            }
        }
    }

    private void FindLocalPlayer()
    {
        var player = Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (player != null)
        {
            localInventory = player.GetComponent<PlayerInventory>();
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
}
