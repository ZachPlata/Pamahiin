using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Manages local-only Ghost Spectator visual effects (desaturated pale-blue URP Volume
/// plus a 16-bit Bayer-dithered cold cyan perimeter fog overlay). Never runs when Alive or in Lobby.
/// </summary>
public class GhostSpectatorVisuals : MonoBehaviour
{
    public static GhostSpectatorVisuals Instance { get; private set; }

    private Volume spectatorVolume;
    private Canvas fogCanvas;
    private RawImage fogOverlay;
    private Material runtimeFogMaterial;
    private VolumeProfile runtimeFallbackProfile;
    private bool isActive;

    public bool IsActive => isActive;

    public static GhostSpectatorVisuals EnsureInstance()
    {
        if (Instance != null) return Instance;

        var existing = FindAnyObjectByType<GhostSpectatorVisuals>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("GhostSpectatorVisuals");
        Instance = go.AddComponent<GhostSpectatorVisuals>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildVisualRig();
        SetSpectatorVisualsActive(false);
    }

    private void OnDestroy()
    {
        if (runtimeFallbackProfile != null) Destroy(runtimeFallbackProfile);
        if (runtimeFogMaterial != null) Destroy(runtimeFogMaterial);
        if (Instance == this) Instance = null;
    }

    private void BuildVisualRig()
    {
        // 1. High-priority (95) global Volume for desaturated pale-blue ghostly vision
        GameObject volGo = new GameObject("GhostSpectatorVolume");
        volGo.transform.SetParent(transform, false);
        spectatorVolume = volGo.AddComponent<Volume>();
        spectatorVolume.isGlobal = true;
        spectatorVolume.priority = 95f;
        spectatorVolume.weight = 1f;

        VolumeProfile profile = Resources.Load<VolumeProfile>("Settings/GhostSpectatorVolumeProfile");
        if (profile == null)
        {
            profile = CreateFallbackProfile();
        }
        spectatorVolume.profile = profile;

        // 2. Non-blocking perimeter fog overlay using Pamahiin/GhostSpectatorFog shader
        GameObject canvasGo = new GameObject("GhostSpectatorFogCanvas");
        canvasGo.transform.SetParent(transform, false);
        fogCanvas = canvasGo.AddComponent<Canvas>();
        fogCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fogCanvas.sortingOrder = 5;
        canvasGo.AddComponent<CanvasScaler>();

        GameObject imgGo = new GameObject("GhostFogOverlay");
        imgGo.transform.SetParent(canvasGo.transform, false);
        fogOverlay = imgGo.AddComponent<RawImage>();
        fogOverlay.texture = Texture2D.whiteTexture;
        fogOverlay.raycastTarget = false;
        fogOverlay.color = Color.white;

        Material mat = Resources.Load<Material>("Materials/GhostSpectatorFog");
        if (mat == null)
        {
            Shader s = Shader.Find("Pamahiin/GhostSpectatorFog");
            if (s != null)
            {
                runtimeFogMaterial = new Material(s);
                mat = runtimeFogMaterial;
            }
        }
        if (mat != null)
        {
            fogOverlay.material = mat;
        }
        else
        {
            // Minimal safe tint fallback if shader could not be resolved
            fogOverlay.color = new Color(0.28f, 0.52f, 0.78f, 0.06f);
        }

        RectTransform rt = imgGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private VolumeProfile CreateFallbackProfile()
    {
        runtimeFallbackProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = runtimeFallbackProfile.Add<ColorAdjustments>(true);
        ca.saturation.Override(-35f);
        ca.colorFilter.Override(new Color(0.68f, 0.82f, 1.0f, 1f));
        ca.postExposure.Override(0.50f);

        var vig = runtimeFallbackProfile.Add<Vignette>(true);
        vig.color.Override(new Color(0.08f, 0.18f, 0.32f, 1f));
        vig.intensity.Override(0.15f);
        vig.smoothness.Override(0.75f);
        return runtimeFallbackProfile;
    }

    public void SetSpectatorVisualsActive(bool active)
    {
        isActive = active;

        if (spectatorVolume != null)
        {
            spectatorVolume.enabled = active;
        }
        if (fogCanvas != null)
        {
            fogCanvas.enabled = active;
        }

        if (active && Camera.main != null)
        {
            var camData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
            if (camData != null)
            {
                camData.renderPostProcessing = true;
            }
        }
    }
}
