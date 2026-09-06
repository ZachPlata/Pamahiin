using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(UIDocument))]
public class ExorcismManager : NetworkBehaviour
{
    public static ExorcismManager Instance { get; private set; }

    private UIDocument uiDocument;
    private VisualElement exorcismOverlay;
    private VisualElement sweetSpot;
    private VisualElement cursor;
    private Label lblScore;

    [Header("Minigame Settings")]
    [SerializeField] private float cursorSpeed = 300f;
    [SerializeField] private float barWidth = 500f;
    [SerializeField] private float sweetSpotWidth = 80f;

    private int successfulChants = 0;
    private const int ChantsRequired = 3;
    
    private bool isMinigameActive = false;
    private float cursorPosition = 0f;
    private float cursorDirection = 1f;
    
    private float currentSweetSpotLeft = 0f;
    private float currentSweetSpotRight = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        var root = uiDocument.rootVisualElement;
        exorcismOverlay = root.Q<VisualElement>("exorcism-overlay");
        sweetSpot = root.Q<VisualElement>("sweet-spot");
        cursor = root.Q<VisualElement>("cursor");
        lblScore = root.Q<Label>("lbl-score");
    }

    public void StartExorcism()
    {
        if (IsServer)
        {
            StartExorcismClientRpc();
            
            // Force start the permanent hunt!
            var ghost = Object.FindAnyObjectByType<GhostController>();
            if (ghost != null)
            {
                ghost.ForceStartHunt();
            }
        }
        else
        {
            StartExorcismServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartExorcismServerRpc()
    {
        StartExorcism();
    }

    [ClientRpc]
    private void StartExorcismClientRpc()
    {
        successfulChants = 0;
        UpdateScoreLabel();
        isMinigameActive = true;
        exorcismOverlay.RemoveFromClassList("hidden");
        RandomizeSweetSpot();
    }

    private void Update()
    {
        if (!isMinigameActive) return;

        // Move cursor back and forth
        cursorPosition += cursorSpeed * cursorDirection * Time.deltaTime;
        if (cursorPosition >= barWidth || cursorPosition <= 0f)
        {
            cursorDirection *= -1f;
            cursorPosition = Mathf.Clamp(cursorPosition, 0f, barWidth);
        }

        cursor.style.left = cursorPosition;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            CheckSkillCheck();
        }
    }

    private void CheckSkillCheck()
    {
        // Hit detection
        if (cursorPosition >= currentSweetSpotLeft && cursorPosition <= currentSweetSpotRight)
        {
            // Success
            successfulChants++;
            UpdateScoreLabel();
            
            if (successfulChants >= ChantsRequired)
            {
                EndExorcism(true);
            }
            else
            {
                // Speed up and randomize again
                cursorSpeed += 100f;
                RandomizeSweetSpot();
            }
        }
        else
        {
            // Failed a check! You could add penalties here (like ghost gets faster or spawns closer)
            Debug.Log("Failed skill check! Ghost gets angrier!");
        }
    }

    private void RandomizeSweetSpot()
    {
        float maxLeft = barWidth - sweetSpotWidth;
        currentSweetSpotLeft = Random.Range(0f, maxLeft);
        currentSweetSpotRight = currentSweetSpotLeft + sweetSpotWidth;

        sweetSpot.style.left = currentSweetSpotLeft;
    }

    private void UpdateScoreLabel()
    {
        lblScore.text = $"{successfulChants} / {ChantsRequired} Successful Chants";
    }

    private void EndExorcism(bool success)
    {
        isMinigameActive = false;
        exorcismOverlay.AddToClassList("hidden");

        if (success && IsServer)
        {
            var ghost = Object.FindAnyObjectByType<GhostController>();
            if (ghost != null)
            {
                ghost.NetworkObject.Despawn(true); // Ghost is banished!
            }

            if (GameMatchManager.Instance != null)
            {
                GameMatchManager.Instance.LockInDeduction(JournalController.LastLockedInGuess); // Auto-end match
            }
        }
        else if (success)
        {
            EndExorcismServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void EndExorcismServerRpc()
    {
        EndExorcism(true);
    }
}
