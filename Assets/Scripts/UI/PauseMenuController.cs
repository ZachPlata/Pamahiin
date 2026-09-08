using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

[RequireComponent(typeof(UIDocument))]
public class PauseMenuController : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement pauseOverlay;
    
    private bool isPaused = false;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 6;
        var root = uiDocument.rootVisualElement;
        
        pauseOverlay = root.Q<VisualElement>("pause-overlay");

        root.Q<Button>("btn-resume").clicked += TogglePause;
        root.Q<Button>("btn-quit").clicked += OnQuitClicked;
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        isPaused = !isPaused;

        if (isPaused)
        {
            pauseOverlay.RemoveFromClassList("hidden");
        }
        else
        {
            pauseOverlay.AddToClassList("hidden");
        }
    }

    private void OnQuitClicked()
    {
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.DisconnectAndReturnToMenu();
        }
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
    }
}
