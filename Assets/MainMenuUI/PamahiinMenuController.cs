using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(PanelRenderer))]
public class PamahiinMenuController : MonoBehaviour
{
    private PanelRenderer panelRenderer;
    private Label titleLabel;
    private VisualElement mist1, mist2;
    private Button hostButton, joinButton;
    private float timeElapsed;
    private int uiVersion = -1;

    void OnEnable()
    {
        panelRenderer = GetComponent<PanelRenderer>();
        
        // Using a lambda expression avoids method-group conversion errors entirely
        panelRenderer.RegisterUIReloadCallback((renderer, root, version) =>
        {
            if (version == uiVersion) return;
            uiVersion = version;

            titleLabel = root.Q<Label>("title");
            mist1 = root.Q<VisualElement>("mist1");
            mist2 = root.Q<VisualElement>("mist2");

            hostButton = root.Q<Button>("start-btn");
            joinButton = root.Q<Button>("settings-btn");

            if (hostButton != null) hostButton.clicked += StartHostGame;
            if (joinButton != null) joinButton.clicked += StartClientGame;
        });
    }

    void OnDisable()
    {
        if (hostButton != null) hostButton.clicked -= StartHostGame;
        if (joinButton != null) joinButton.clicked -= StartClientGame;
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;

        // Subtle eerie horror title flicker effect
        if (titleLabel != null)
        {
            float flicker = 1f;
            float wave = timeElapsed % 6f;
            if (wave > 4.5f && wave < 4.7f) flicker = 0.4f;
            else if (wave > 5.1f && wave < 5.25f) flicker = 0.7f;

            titleLabel.style.color = new Color(230f / 255f, 50f / 255f, 60f / 255f, flicker);
        }

        // Slow atmospheric mist drift
        if (mist1 != null)
            mist1.style.translate = new StyleTranslate(new Translate(Mathf.Sin(timeElapsed * 0.4f) * 12f, 0));
        if (mist2 != null)
            mist2.style.translate = new StyleTranslate(new Translate(Mathf.Cos(timeElapsed * 0.3f) * 15f, 0));
    }

    private void StartHostGame()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
            panelRenderer.enabled = false; // Hides the menu cleanly to enter the game loop
        }
    }

    private void StartClientGame()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartClient();
            panelRenderer.enabled = false;
        }
    }
}