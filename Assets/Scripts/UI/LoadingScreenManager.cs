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
    private Coroutine timedIntermissionCoroutine;

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
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Rebind project PanelSettings if the initial instance initialized BeforeSceneLoad
        if (resolvedAssetPanelSettings == null)
        {
            EnsureUIReady();
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

        loadingOverlay.RemoveFromClassList("hidden");
        loadingOverlay.AddToClassList("loading-visible");
        loadingOverlay.pickingMode = PickingMode.Position;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        fadeCoroutine = StartCoroutine(FadeOverlayRoutine(1f, fadeInDuration, hideAtEnd: false));
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
    /// Smoothly fades out and hides the intermission screen.
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
        }
        fadeCoroutine = StartCoroutine(FadeOverlayRoutine(0f, Mathf.Max(0.05f, fadeDuration), hideAtEnd: true));
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

    private IEnumerator FadeOverlayRoutine(float targetAlpha, float duration, bool hideAtEnd)
    {
        if (loadingOverlay == null)
        {
            fadeCoroutine = null;
            yield break;
        }

        float startAlpha = loadingOverlay.resolvedStyle.opacity;
        if (float.IsNaN(startAlpha))
        {
            startAlpha = hideAtEnd ? 1f : 0f;
        }

        if (duration <= 0.01f)
        {
            loadingOverlay.style.opacity = targetAlpha;
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                loadingOverlay.style.opacity = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }
            loadingOverlay.style.opacity = targetAlpha;
        }

        if (hideAtEnd)
        {
            isVisible = false;
            isFadingOut = false;
            targetProgress = 0f;
            displayedProgress = 0f;
            lastAppliedProgress = -1f;
            loadingOverlay.RemoveFromClassList("loading-visible");
            loadingOverlay.AddToClassList("hidden");
            loadingOverlay.pickingMode = PickingMode.Ignore;
        }

        fadeCoroutine = null;
    }
}
