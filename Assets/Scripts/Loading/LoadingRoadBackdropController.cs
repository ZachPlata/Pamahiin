using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the static-Jeepney vertical driving loading backdrop:
/// - Keeps the Jeepney and vertical road static in world space.
/// - Translates roadside waving trees strictly from top to bottom and wraps them seamlessly.
/// - Ensures full-bleed roadside ground coverage with zero dead space across aspect ratios.
/// - Supports both standalone playback of LoadingScene and RenderTexture overlay compositing in LoadingScreenManager.
/// </summary>
[DisallowMultipleComponent]
public class LoadingRoadBackdropController : MonoBehaviour
{
    public static LoadingRoadBackdropController ActiveInstance { get; private set; }
    public static bool OverlayModeRequested { get; set; }

    [Header("Scene References")]
    [SerializeField] private Camera backdropCamera;
    [SerializeField] private AudioListener backdropAudioListener;
    [SerializeField] private Transform jeepneyRoot;
    [SerializeField] private Transform roadsideGroundBackdrop;
    [SerializeField] private Transform[] scrollingTrees = Array.Empty<Transform>();

    [Header("Top-to-Bottom Tree Scroll")]
    [SerializeField] private float scrollSpeedUnitsPerSecond = 6.5f;
    [SerializeField] private float treeWrapTopY = 9.5f;
    [SerializeField] private float treeWrapBottomY = -9.5f;

    private Vector3 initialJeepneyPosition;
    private bool hasInitialJeepneyPosition;

    public Camera BackdropCamera => backdropCamera;
    public Transform JeepneyRoot => jeepneyRoot;
    public Transform RoadsideGroundBackdrop => roadsideGroundBackdrop;
    public int TreeCount => scrollingTrees != null ? scrollingTrees.Length : 0;

    public float ScrollSpeedUnitsPerSecond
    {
        get => scrollSpeedUnitsPerSecond;
        set => scrollSpeedUnitsPerSecond = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        ActiveInstance = this;

        if (backdropCamera == null)
        {
            backdropCamera = GetComponentInChildren<Camera>(true);
        }

        if (backdropAudioListener == null && backdropCamera != null)
        {
            backdropAudioListener = backdropCamera.GetComponent<AudioListener>();
        }

        if (jeepneyRoot != null)
        {
            initialJeepneyPosition = jeepneyRoot.localPosition;
            hasInitialJeepneyPosition = true;
        }

        if (OverlayModeRequested || SceneManager.sceneCount > 1)
        {
            DisableConflictingAudioListenerAndMainTag();
        }
    }

    private void Start()
    {
        EnsureBackdropFillsCameraView();
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }
    }

    public void Configure(
        Camera cam,
        AudioListener listener,
        Transform jeepney,
        Transform groundBackdrop,
        Transform[] trees,
        float speedUnitsPerSec,
        float wrapTopY,
        float wrapBottomY)
    {
        backdropCamera = cam;
        backdropAudioListener = listener;
        jeepneyRoot = jeepney;
        roadsideGroundBackdrop = groundBackdrop;
        scrollingTrees = trees ?? Array.Empty<Transform>();
        scrollSpeedUnitsPerSecond = Mathf.Max(0f, speedUnitsPerSec);
        treeWrapTopY = wrapTopY;
        treeWrapBottomY = wrapBottomY;

        if (jeepneyRoot != null)
        {
            initialJeepneyPosition = jeepneyRoot.localPosition;
            hasInitialJeepneyPosition = true;
        }

        EnsureBackdropFillsCameraView();
    }

    public void ConfigureForOverlayRender(RenderTexture targetTexture)
    {
        DisableConflictingAudioListenerAndMainTag();

        if (backdropCamera != null)
        {
            backdropCamera.targetTexture = targetTexture;
            backdropCamera.enabled = targetTexture != null;
        }

        EnsureBackdropFillsCameraView();
    }

    public void RestoreStandaloneCamera()
    {
        if (backdropCamera != null)
        {
            backdropCamera.targetTexture = null;
            backdropCamera.enabled = true;
        }
    }

    private void DisableConflictingAudioListenerAndMainTag()
    {
        if (backdropAudioListener != null)
        {
            backdropAudioListener.enabled = false;
        }

        if (backdropCamera != null)
        {
            backdropCamera.tag = "Untagged";
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f)
        {
            StepScrollingTrees(dt);
        }

        if (hasInitialJeepneyPosition && jeepneyRoot != null)
        {
            jeepneyRoot.localPosition = initialJeepneyPosition;
        }

        EnsureBackdropFillsCameraView();
    }

    /// <summary>
    /// Moves roadside trees strictly downward (top to bottom) and wraps them back above the top bound.
    /// Everything else in the scene (road and Jeepney) stays stationary.
    /// </summary>
    public void StepScrollingTrees(float unscaledDeltaTime)
    {
        if (scrollingTrees == null || scrollingTrees.Length == 0 || unscaledDeltaTime <= 0f)
        {
            return;
        }

        float deltaY = scrollSpeedUnitsPerSecond * unscaledDeltaTime;
        float wrapSpan = Mathf.Max(1f, treeWrapTopY - treeWrapBottomY);

        for (int i = 0; i < scrollingTrees.Length; i++)
        {
            Transform tree = scrollingTrees[i];
            if (tree == null) continue;

            Vector3 pos = tree.localPosition;
            pos.y -= deltaY;
            while (pos.y < treeWrapBottomY)
            {
                pos.y += wrapSpan;
            }
            tree.localPosition = pos;
        }
    }

    public void EnsureBackdropFillsCameraView()
    {
        if (roadsideGroundBackdrop == null || backdropCamera == null)
        {
            return;
        }

        float orthoHeight = Mathf.Max(1f, backdropCamera.orthographicSize * 2f);
        float aspect = backdropCamera.targetTexture != null && backdropCamera.targetTexture.height > 0
            ? (float)backdropCamera.targetTexture.width / backdropCamera.targetTexture.height
            : Mathf.Max(0.5f, backdropCamera.aspect);
        float orthoWidth = orthoHeight * aspect;

        float requiredWidth = Mathf.Max(28f, orthoWidth * 1.35f);
        float requiredHeight = Mathf.Max(20f, orthoHeight * 1.35f);

        Vector3 currentScale = roadsideGroundBackdrop.localScale;
        SpriteRenderer sr = roadsideGroundBackdrop.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            Vector2 spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x > 0.01f && spriteSize.y > 0.01f)
            {
                float targetScaleX = Mathf.Max(currentScale.x, requiredWidth / spriteSize.x);
                float targetScaleY = Mathf.Max(currentScale.y, requiredHeight / spriteSize.y);
                roadsideGroundBackdrop.localScale = new Vector3(targetScaleX, targetScaleY, 1f);
            }
        }
    }
}
