using UnityEngine;

/// <summary>
/// Plays a sliced sprite-sheet frame sequence using unscaled time so loading
/// screen animations continue smoothly even when Time.timeScale is 0.
/// </summary>
[DisallowMultipleComponent]
public class LoadingRoadSpriteAnimator : MonoBehaviour
{
    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 12f;
    [SerializeField] private bool randomizeStartFrame = false;
    [SerializeField] private int startFrameOffset = 0;

    private float frameTimer;
    private int currentFrameIndex;

    public float FramesPerSecond
    {
        get => framesPerSecond;
        set => framesPerSecond = Mathf.Max(0.1f, value);
    }

    public Sprite[] Frames => frames;

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<SpriteRenderer>();
        }

        InitializeFrameCursor();
    }

    private void OnEnable()
    {
        InitializeFrameCursor();
        ApplyCurrentFrame();
    }

    public void Configure(Sprite[] newFrames, float fps, bool randomizeStart = false, int offset = 0)
    {
        frames = newFrames;
        framesPerSecond = Mathf.Max(0.1f, fps);
        randomizeStartFrame = randomizeStart;
        startFrameOffset = offset;
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<SpriteRenderer>();
        }
        InitializeFrameCursor();
        ApplyCurrentFrame();
    }

    private void InitializeFrameCursor()
    {
        if (frames == null || frames.Length == 0)
        {
            currentFrameIndex = 0;
            return;
        }

        int initial = randomizeStartFrame ? Random.Range(0, frames.Length) : Mathf.Max(0, startFrameOffset);
        currentFrameIndex = initial % frames.Length;
        frameTimer = 0f;
    }

    private void Update()
    {
        if (frames == null || frames.Length <= 1 || targetRenderer == null)
        {
            return;
        }

        float fps = Mathf.Max(0.1f, framesPerSecond);
        float frameDuration = 1f / fps;
        frameTimer += Time.unscaledDeltaTime;

        if (frameTimer < frameDuration)
        {
            return;
        }

        int steps = Mathf.FloorToInt(frameTimer / frameDuration);
        frameTimer -= steps * frameDuration;
        currentFrameIndex = (currentFrameIndex + steps) % frames.Length;
        ApplyCurrentFrame();
    }

    private void ApplyCurrentFrame()
    {
        if (targetRenderer == null || frames == null || frames.Length == 0)
        {
            return;
        }

        Sprite next = frames[currentFrameIndex % frames.Length];
        if (next != null)
        {
            targetRenderer.sprite = next;
        }
    }
}
