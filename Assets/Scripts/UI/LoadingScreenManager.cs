using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Persistent UI Toolkit overlay that displays synchronized horror dossier intermission
/// screens during Lobby → Map travel and End Game → Ghost Reveal transitions.
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance { get; private set; }
    public static bool IsLoadingVisible => Instance != null && Instance.IsVisible;

    private const string UXML_RESOURCE_PATH = "UI/LoadingScreen";
    private const string LOADING_SCENE_NAME = "LoadingScene";
    private const int OVERLAY_SORT_ORDER = 100;

    private static readonly string[] FieldTips =
    {
        "Verify all three evidence traces in your Field Journal before initiating an exorcism.",
        "Ritual Chalk must be placed inside the active Ghost Room to manifest the summoning circle.",
        "Keep a Crucifix close—failing an exorcism enrages the entity and bleeds the house in crimson light.",
        "Use the Spirit Box with room lights off to provoke vocal answers from responsive entities.",
        "Return to the Jeepney and press the End Game button once your investigation is complete."
    };

    private UIDocument uiDocument;
    private PanelSettings fallbackPanelSettings;
    private PanelSettings resolvedAssetPanelSettings;

    private VisualElement loadingOverlay;
    private VisualElement loadingSceneBackdrop;
    private VisualElement loadingBlackFade;
    private Label lblTag;
    private Label lblPercent;
    private Label lblTitle;
    private Label lblSubtitle;
    private VisualElement progressFill;
    private Label lblStatus;
    private Label lblTip;

    private bool isVisible = false;
    private bool isFadingOut = false;
    private float targetProgress = 0f;
    private float displayedProgress = 0f;
    private float lastAppliedProgress = -1f;
    private int tipIndex = 0;

    private Coroutine fadeCoroutine;
    private Coroutine blackFadeCoroutine;
    private Coroutine timedIntermissionCoroutine;
    private RenderTexture backdropRenderTexture;
    private bool isLoadingBackdropScene = false;

    public bool IsVisible => isVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        EnsureInstance();
    }

    public static LoadingScreenManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        var existing = FindAnyObjectByType<LoadingScreenManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Instance = existing;
            DontDestroyOnLoad(existing.gameObject);
            return Instance;
        }

        var go = new GameObject("LoadingScreenManager");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LoadingScreenManager>();
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
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureUIReady();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
        if (fallbackPanelSettings != null)
        {
            Destroy(fallbackPanelSettings);
            fallbackPanelSettings = null;
        }
        if (backdropRenderTexture != null)
        {
            backdropRenderTexture.Release();
            Destroy(backdropRenderTexture);
            backdropRenderTexture = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Rebind project PanelSettings if the initial instance initialized BeforeSceneLoad
        if (resolvedAssetPanelSettings == null)
        {
            EnsureUIReady();
        }

        if (string.Equals(scene.name, LOADING_SCENE_NAME, StringComparison.Ordinal))
        {
            isLoadingBackdropScene = false;
            if (isVisible)
            {
                EnsureBackdropRenderTexture();
                if (LoadingRoadBackdropController.ActiveInstance != null)
                {
                    LoadingRoadBackdropController.ActiveInstance.ConfigureForOverlayRender(backdropRenderTexture);
                }
            }
            else if (SceneManager.sceneCount > 1)
            {
                SceneManager.UnloadSceneAsync(scene);
            }
            return;
        }

        if (isVisible && !isFadingOut)
        {
            EnsureBackdropSceneActive();
        }
    }

    private bool EnsureUIReady()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }
        }

        uiDocument.sortingOrder = OVERLAY_SORT_ORDER;

        PanelSettings targetSettings = ResolvePanelSettings();
        if (targetSettings != null && uiDocument.panelSettings != targetSettings)
        {
            uiDocument.panelSettings = targetSettings;
            loadingOverlay = null;
        }

        if (uiDocument.visualTreeAsset == null)
        {
            var tree = Resources.Load<VisualTreeAsset>(UXML_RESOURCE_PATH);
            if (tree != null)
            {
                uiDocument.visualTreeAsset = tree;
                loadingOverlay = null;
            }
        }

        if (loadingOverlay == null && uiDocument.rootVisualElement != null)
        {
            BindVisualElements(uiDocument.rootVisualElement);
        }

        return loadingOverlay != null;
    }

    private PanelSettings ResolvePanelSettings()
    {
        if (resolvedAssetPanelSettings != null && resolvedAssetPanelSettings.themeStyleSheet != null)
        {
            return resolvedAssetPanelSettings;
        }

        PanelSettings current = uiDocument != null ? uiDocument.panelSettings : null;
        resolvedAssetPanelSettings = PamahiinUIThemeUtility.ResolveRuntimePanelSettings(current);
        return resolvedAssetPanelSettings;
    }

    private void BindVisualElements(VisualElement root)
    {
        loadingOverlay = root.Q<VisualElement>("loading-overlay");
        loadingSceneBackdrop = root.Q<VisualElement>("loading-scene-backdrop");
        loadingBlackFade = root.Q<VisualElement>("loading-black-fade");
        lblTag = root.Q<Label>("lbl-loading-tag");
        lblPercent = root.Q<Label>("lbl-loading-percent");
        lblTitle = root.Q<Label>("lbl-loading-title");
        lblSubtitle = root.Q<Label>("lbl-loading-subtitle");
        progressFill = root.Q<VisualElement>("loading-progress-fill");
        lblStatus = root.Q<Label>("lbl-loading-status");
        lblTip = root.Q<Label>("lbl-loading-tip");

        if (loadingOverlay != null && !isVisible)
        {
            loadingOverlay.AddToClassList("hidden");
            loadingOverlay.RemoveFromClassList("loading-visible");
            loadingOverlay.style.opacity = 0f;
            loadingOverlay.pickingMode = PickingMode.Ignore;
        }

        if (loadingBlackFade != null && blackFadeCoroutine == null)
        {
            loadingBlackFade.AddToClassList("hidden");
            loadingBlackFade.style.opacity = 0f;
            loadingBlackFade.pickingMode = PickingMode.Ignore;
        }
    }

    private void Update()
    {
        if (!isVisible || loadingOverlay == null) return;

        displayedProgress = Mathf.MoveTowards(displayedProgress, targetProgress, Time.unscaledDeltaTime * 2.4f);
        if (!Mathf.Approximately(displayedProgress, lastAppliedProgress))
        {
            lastAppliedProgress = displayedProgress;
            ApplyProgressVisuals(displayedProgress);
        }
    }

    private void ApplyProgressVisuals(float normalized)
    {
        float clamped = Mathf.Clamp01(normalized);
        if (progressFill != null)
        {
            progressFill.style.width = Length.Percent(clamped * 100f);
        }
        if (lblPercent != null)
        {
            lblPercent.text = $"{Mathf.RoundToInt(clamped * 100f)}%";
        }
    }

    private string PickFieldTip(string explicitTip)
    {
        if (!string.IsNullOrEmpty(explicitTip)) return explicitTip;
        string tip = FieldTips[tipIndex % FieldTips.Length];
        tipIndex = (tipIndex + 1) % FieldTips.Length;
        return tip;
    }

    /// <summary>
    /// Displays the persistent intermission screen immediately with smooth fade-in.
    /// </summary>
    public void ShowIntermission(string title, string subtitle, string tip = null, string tag = "PAMAHIIN // FIELD DISPATCH")
    {
        if (timedIntermissionCoroutine != null)
        {
            StopCoroutine(timedIntermissionCoroutine);
            timedIntermissionCoroutine = null;
        }

        ShowIntermissionInternal(title, subtitle, tip, tag, 0.22f);
    }

    private void ShowIntermissionInternal(string title, string subtitle, string tip, string tag, float fadeInDuration)
    {
        if (!EnsureUIReady()) return;

        isVisible = true;
        isFadingOut = false;
        targetProgress = 0.08f;
        displayedProgress = 0.04f;
        lastAppliedProgress = displayedProgress;

        if (lblTag != null) lblTag.text = string.IsNullOrEmpty(tag) ? "PAMAHIIN // FIELD DISPATCH" : tag;
        if (lblTitle != null) lblTitle.text = string.IsNullOrEmpty(title) ? "LOADING..." : title;
        if (lblSubtitle != null) lblSubtitle.text = subtitle ?? string.Empty;
        if (lblStatus != null) lblStatus.text = "Initializing...";
        if (lblTip != null) lblTip.text = PickFieldTip(tip);
        ApplyProgressVisuals(displayedProgress);

        EnsureBackdropSceneActive();

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        if (blackFadeCoroutine != null)
        {
            StopCoroutine(blackFadeCoroutine);
            blackFadeCoroutine = null;
        }

        if (loadingBlackFade != null)
        {
            float toBlackDuration = Mathf.Clamp(fadeInDuration, 0.12f, 0.28f);
            float fromBlackDuration = Mathf.Clamp(fadeInDuration + 0.04f, 0.16f, 0.32f);
            fadeCoroutine = StartCoroutine(ShowIntermissionWithBlackFadeRoutine(toBlackDuration, fromBlackDuration));
        }
        else
        {
            loadingOverlay.RemoveFromClassList("hidden");
            loadingOverlay.AddToClassList("loading-visible");
            loadingOverlay.pickingMode = PickingMode.Position;
            fadeCoroutine = StartCoroutine(FadeOverlayRoutine(1f, fadeInDuration, hideAtEnd: false));
        }
    }

    /// <summary>
    /// Updates the progress bar target and optional status line while visible.
    /// </summary>
    public void SetProgress(float normalized01, string statusText = null)
    {
        targetProgress = Mathf.Clamp01(Mathf.Max(targetProgress, normalized01));
        if (statusText != null && lblStatus != null)
        {
            lblStatus.text = statusText;
        }
    }

    /// <summary>
    /// Smoothly fades out and hides the intermission screen via a black veil transition.
    /// </summary>
    public void HideIntermission(float fadeDuration = 0.35f)
    {
        if (timedIntermissionCoroutine != null)
        {
            StopCoroutine(timedIntermissionCoroutine);
            timedIntermissionCoroutine = null;
        }

        if (!isVisible || isFadingOut || loadingOverlay == null)
        {
            if (loadingOverlay == null) isVisible = false;
            return;
        }

        isFadingOut = true;
        targetProgress = 1f;
        displayedProgress = 1f;
        lastAppliedProgress = 1f;
        ApplyProgressVisuals(1f);

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        if (blackFadeCoroutine != null)
        {
            StopCoroutine(blackFadeCoroutine);
            blackFadeCoroutine = null;
        }

        if (loadingBlackFade != null)
        {
            float toBlackDuration = Mathf.Clamp(fadeDuration * 0.65f, 0.06f, 0.28f);
            float fromBlackDuration = Mathf.Clamp(fadeDuration, 0.08f, 0.38f);
            fadeCoroutine = StartCoroutine(HideIntermissionWithBlackFadeRoutine(toBlackDuration, fromBlackDuration));
        }
        else
        {
            fadeCoroutine = StartCoroutine(FadeOverlayRoutine(0f, Mathf.Max(0.05f, fadeDuration), hideAtEnd: true));
        }
    }

    /// <summary>
    /// Fades the standalone black veil to opaque black (used for non-truck scene/host transitions).
    /// </summary>
    public void FadeToBlack(float duration = 0.18f)
    {
        if (!EnsureUIReady() || loadingBlackFade == null) return;

        if (blackFadeCoroutine != null)
        {
            StopCoroutine(blackFadeCoroutine);
        }
        blackFadeCoroutine = StartCoroutine(AnimateBlackFadeRoutine(1f, Mathf.Max(0.01f, duration), hideWhenZero: false));
    }

    /// <summary>
    /// Fades the standalone black veil back to transparent.
    /// </summary>
    public void FadeFromBlack(float duration = 0.24f)
    {
        if (!EnsureUIReady() || loadingBlackFade == null) return;

        if (blackFadeCoroutine != null)
        {
            StopCoroutine(blackFadeCoroutine);
        }
        blackFadeCoroutine = StartCoroutine(AnimateBlackFadeRoutine(0f, Mathf.Max(0.01f, duration), hideWhenZero: true));
    }

    /// <summary>
    /// Runs an intermission for at least minSeconds and until readyCondition returns true,
    /// then invokes onComplete (keeping the overlay visible through the callback so UI switches underneath cleanly).
    /// </summary>
    public void RunTimedIntermission(
        string title,
        string subtitle,
        float minSeconds,
        Func<bool> readyCondition,
        Action onComplete,
        string tip = null,
        string tag = "CASE DEBRIEFING")
    {
        if (timedIntermissionCoroutine != null)
        {
            StopCoroutine(timedIntermissionCoroutine);
        }
        timedIntermissionCoroutine = StartCoroutine(RunTimedIntermissionRoutine(title, subtitle, minSeconds, readyCondition, onComplete, tip, tag));
    }

    private IEnumerator RunTimedIntermissionRoutine(
        string title,
        string subtitle,
        float minSeconds,
        Func<bool> readyCondition,
        Action onComplete,
        string tip,
        string tag)
    {
        ShowIntermissionInternal(title, subtitle, tip, tag, 0.25f);

        float elapsed = 0f;
        float minDuration = Mathf.Max(0.4f, minSeconds);
        float maxWait = Mathf.Max(minDuration + 4.0f, 6.0f);

        while (elapsed < minDuration || (readyCondition != null && !readyCondition() && elapsed < maxWait))
        {
            elapsed += Time.unscaledDeltaTime;
            float norm = Mathf.Clamp01(elapsed / minDuration);
            float barProgress = Mathf.Lerp(0.12f, readyCondition == null || readyCondition() ? 0.96f : 0.84f, norm);
            SetProgress(barProgress, elapsed < minDuration * 0.55f ? "Encrypting field logs..." : "Finalizing dossier...");
            yield return null;
        }

        SetProgress(1f, "Complete.");
        yield return new WaitForSecondsRealtime(0.12f);

        timedIntermissionCoroutine = null;
        onComplete?.Invoke();
    }

    private IEnumerator ShowIntermissionWithBlackFadeRoutine(float toBlackDuration, float fromBlackDuration)
    {
        if (loadingBlackFade != null)
        {
            loadingBlackFade.RemoveFromClassList("hidden");
            loadingBlackFade.pickingMode = PickingMode.Position;
            yield return LerpElementAlphaRoutine(loadingBlackFade, 1f, toBlackDuration, 0f);
        }

        // Let the additive truck backdrop finish initializing behind pure black (capped so it never stalls).
        float backdropWait = 0f;
        while (isLoadingBackdropScene && backdropWait < 0.15f)
        {
            backdropWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (loadingOverlay != null)
        {
            loadingOverlay.RemoveFromClassList("hidden");
            loadingOverlay.AddToClassList("loading-visible");
            loadingOverlay.style.opacity = 1f;
            loadingOverlay.pickingMode = PickingMode.Position;
        }

        if (loadingBlackFade != null)
        {
            yield return LerpElementAlphaRoutine(loadingBlackFade, 0f, fromBlackDuration, 1f);
            loadingBlackFade.AddToClassList("hidden");
            loadingBlackFade.pickingMode = PickingMode.Ignore;
        }

        fadeCoroutine = null;
    }

    private IEnumerator HideIntermissionWithBlackFadeRoutine(float toBlackDuration, float fromBlackDuration)
    {
        if (loadingBlackFade != null)
        {
            loadingBlackFade.RemoveFromClassList("hidden");
            loadingBlackFade.pickingMode = PickingMode.Position;
            yield return LerpElementAlphaRoutine(loadingBlackFade, 1f, toBlackDuration, 0f);
        }

        ResetAndHideLoadingOverlay();

        if (loadingBlackFade != null)
        {
            yield return LerpElementAlphaRoutine(loadingBlackFade, 0f, fromBlackDuration, 1f);
            loadingBlackFade.AddToClassList("hidden");
            loadingBlackFade.pickingMode = PickingMode.Ignore;
        }

        isVisible = false;
        isFadingOut = false;
        fadeCoroutine = null;
    }

    private IEnumerator AnimateBlackFadeRoutine(float targetAlpha, float duration, bool hideWhenZero)
    {
        if (loadingBlackFade == null)
        {
            blackFadeCoroutine = null;
            yield break;
        }

        loadingBlackFade.RemoveFromClassList("hidden");
        loadingBlackFade.pickingMode = targetAlpha > 0.01f ? PickingMode.Position : PickingMode.Ignore;

        yield return LerpElementAlphaRoutine(loadingBlackFade, targetAlpha, duration, targetAlpha > 0.5f ? 0f : 1f);

        if (hideWhenZero && targetAlpha <= 0.01f)
        {
            loadingBlackFade.AddToClassList("hidden");
            loadingBlackFade.pickingMode = PickingMode.Ignore;
        }

        blackFadeCoroutine = null;
    }

    private static IEnumerator LerpElementAlphaRoutine(VisualElement element, float targetAlpha, float duration, float fallbackStartAlpha)
    {
        if (element == null) yield break;

        float startAlpha = element.resolvedStyle.opacity;
        if (float.IsNaN(startAlpha))
        {
            startAlpha = fallbackStartAlpha;
        }

        if (duration <= 0.01f)
        {
            element.style.opacity = targetAlpha;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            element.style.opacity = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }
        element.style.opacity = targetAlpha;
    }

    private void ResetAndHideLoadingOverlay()
    {
        targetProgress = 0f;
        displayedProgress = 0f;
        lastAppliedProgress = -1f;
        if (loadingOverlay != null)
        {
            loadingOverlay.style.opacity = 0f;
            loadingOverlay.RemoveFromClassList("loading-visible");
            loadingOverlay.AddToClassList("hidden");
            loadingOverlay.pickingMode = PickingMode.Ignore;
        }
        ReleaseBackdropScene();
    }

    private IEnumerator FadeOverlayRoutine(float targetAlpha, float duration, bool hideAtEnd)
    {
        if (loadingOverlay == null)
        {
            fadeCoroutine = null;
            yield break;
        }

        yield return LerpElementAlphaRoutine(loadingOverlay, targetAlpha, duration, hideAtEnd ? 1f : 0f);

        if (hideAtEnd)
        {
            isVisible = false;
            isFadingOut = false;
            ResetAndHideLoadingOverlay();
        }

        fadeCoroutine = null;
    }

    private void EnsureBackdropRenderTexture()
    {
        float aspect = Screen.height > 0 ? Mathf.Max(0.5f, (float)Screen.width / Screen.height) : (16f / 9f);
        int targetW = 1280;
        int targetH = Mathf.Clamp(Mathf.RoundToInt(targetW / aspect), 480, 1280);

        if (backdropRenderTexture == null || backdropRenderTexture.width != targetW || backdropRenderTexture.height != targetH)
        {
            if (backdropRenderTexture != null)
            {
                backdropRenderTexture.Release();
                Destroy(backdropRenderTexture);
            }

            backdropRenderTexture = new RenderTexture(targetW, targetH, 16, RenderTextureFormat.ARGB32)
            {
                name = "LoadingSceneBackdropRT",
                filterMode = FilterMode.Point,
                useMipMap = false
            };
            backdropRenderTexture.Create();
        }

        if (loadingSceneBackdrop != null)
        {
            loadingSceneBackdrop.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(backdropRenderTexture));
        }
    }

    private void EnsureBackdropSceneActive()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureBackdropRenderTexture();

        if (LoadingRoadBackdropController.ActiveInstance != null)
        {
            LoadingRoadBackdropController.OverlayModeRequested = true;
            LoadingRoadBackdropController.ActiveInstance.ConfigureForOverlayRender(backdropRenderTexture);
            return;
        }

        Scene loadedBackdrop = SceneManager.GetSceneByName(LOADING_SCENE_NAME);
        if (!loadedBackdrop.isLoaded && !isLoadingBackdropScene && Application.CanStreamedLevelBeLoaded(LOADING_SCENE_NAME))
        {
            LoadingRoadBackdropController.OverlayModeRequested = true;
            isLoadingBackdropScene = true;
            SceneManager.LoadSceneAsync(LOADING_SCENE_NAME, LoadSceneMode.Additive);
        }
    }

    private void ReleaseBackdropScene()
    {
        LoadingRoadBackdropController.OverlayModeRequested = false;
        isLoadingBackdropScene = false;

        if (loadingSceneBackdrop != null)
        {
            loadingSceneBackdrop.style.backgroundImage = StyleKeyword.None;
        }

        if (LoadingRoadBackdropController.ActiveInstance != null)
        {
            LoadingRoadBackdropController.ActiveInstance.ConfigureForOverlayRender(null);
        }

        if (!Application.isPlaying)
        {
            return;
        }

        Scene loadedBackdrop = SceneManager.GetSceneByName(LOADING_SCENE_NAME);
        if (loadedBackdrop.isLoaded && SceneManager.sceneCount > 1)
        {
            SceneManager.UnloadSceneAsync(loadedBackdrop);
        }
    }
}
