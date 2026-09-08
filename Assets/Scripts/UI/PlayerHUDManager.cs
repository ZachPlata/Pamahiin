using UnityEngine;
using UnityEngine.UIElements;

public class PlayerHUDManager : MonoBehaviour
{
    [SerializeField] private UIDocument hudDocument;
    
    private PlayerInventory localInventory; 
    private VisualElement[] slots = new VisualElement[PlayerInventory.MaxSlots];
    private Label[] slotLabels = new Label[PlayerInventory.MaxSlots];
    private VisualElement temperatureContainer;
    private Label temperatureText;

    private void OnEnable()
    {
        if (hudDocument == null || hudDocument.rootVisualElement == null) return;
        
        hudDocument.sortingOrder = 1; // Explicitly put HUD in background
        var root = hudDocument.rootVisualElement;

        for (int i = 0; i < PlayerInventory.MaxSlots; i++)
        {
            slots[i] = root.Q<VisualElement>($"Slot{i}");
            slotLabels[i] = root.Q<Label>($"ItemText{i}");
        }

        temperatureContainer = root.Q<VisualElement>("TemperatureContainer");
        temperatureText = root.Q<Label>("TemperatureText");

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

        UpdateThermometerHUD();
    }

    private void UpdateThermometerHUD()
    {
        if (localInventory == null || temperatureContainer == null || temperatureText == null) return;

        var currentItem = localInventory.CurrentItem;
        if (currentItem != null && currentItem is ThermometerItem thermometer)
        {
            if (thermometer.IsPoweredOn)
            {
                temperatureContainer.style.display = DisplayStyle.Flex;
                float temp = thermometer.DisplayedTemperature;
                temperatureText.text = $"{temp:F1}°C";
                temperatureText.style.color = temp < 0f ? new StyleColor(Color.cyan) : new StyleColor(Color.white);
            }
            else
            {
                temperatureContainer.style.display = DisplayStyle.None;
            }
        }
        else
        {
            temperatureContainer.style.display = DisplayStyle.None;
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
