using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class SpiritBoxItem : EquipmentItem
{
    [Header("Spirit Box Settings")]
    [SerializeField] private Sprite activatedSprite;
    [SerializeField] private Sprite deactivatedSprite;
    [SerializeField] private float detectionRadius = 5f;

    public float DetectionRadius => detectionRadius;

    [Header("Ghost Response Audio Slots (1 to 5)")]
    [Tooltip("Audio response when Question 1 is asked and ghost has Spirit Box evidence")]
    [SerializeField] private AudioClip ghostResponseAudio1;
    [Tooltip("Audio response when Question 2 is asked and ghost has Spirit Box evidence")]
    [SerializeField] private AudioClip ghostResponseAudio2;
    [Tooltip("Audio response when Question 3 is asked and ghost has Spirit Box evidence")]
    [SerializeField] private AudioClip ghostResponseAudio3;
    [Tooltip("Audio response when Question 4 is asked and ghost has Spirit Box evidence")]
    [SerializeField] private AudioClip ghostResponseAudio4;
    [Tooltip("Audio response when Question 5 is asked and ghost has Spirit Box evidence")]
    [SerializeField] private AudioClip ghostResponseAudio5;

    [Header("Array Audio Slot Fallback")]
    [Tooltip("Alternatively, assign all 5 clips in this array")]
    [SerializeField] private AudioClip[] ghostResponseAudios = new AudioClip[5];

    [Header("Static / Radio Sweep Sound")]
    [Tooltip("Audio clip played when radio scans or when ghost does not respond")]
    [SerializeField] private AudioClip radioStaticAudio;

    [Header("Dialogue Questions (1 to 5)")]
    [SerializeField] private string[] dialogueQuestions = new string[5]
    {
        "Where are you?",
        "How old are you?",
        "Are you close?",
        "What do you want?",
        "Give us a sign."
    };

    [Header("Ghost Response Transcripts (1 to 5)")]
    [SerializeField] private string[] ghostResponseTexts = new string[5]
    {
        "\"Behind you...\"",
        "\"Old... very old...\"",
        "\"Right here...\"",
        "\"Leave...\"",
        "\"Cold...\""
    };

    /// <summary>
    /// Global flag indicating whether the local client is currently in Spirit Box dialogue mode.
    /// Used by PlayerController and PlayerInventory to disable movement, interactions, and inventory switching.
    /// </summary>
    public static bool IsLocalDialogueActive { get; set; } = false;

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool localFallbackPower = false;

    public override bool IsPoweredOn => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        ? isPoweredOn.Value
        : localFallbackPower;

    private AudioSource audioSource;

    public bool IsHeldByLocalPlayer
    {
        get
        {
            // 1. Direct local inventory check
            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var inv = localPlayer.GetComponent<PlayerInventory>();
                if (inv != null && inv.CurrentItem == this) return true;
            }

            // 2. Check any owner PlayerController in scene
            var players = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
            foreach (var p in players)
            {
                if (p.IsOwner)
                {
                    var inv = p.GetComponent<PlayerInventory>();
                    if (inv != null && inv.CurrentItem == this) return true;
                }
            }

            // 3. Networked client ID check
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                return ownerClientId.Value == NetworkManager.Singleton.LocalClientId && isInHand.Value;
            }

            // 4. Offline / local test fallback
            return isInHand.Value;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        itemName = "Spirit Box";

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0.8f; // Spatialized world audio so nearby teammates hear responses
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 15f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.playOnAwake = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += OnPowerStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        isPoweredOn.OnValueChanged -= OnPowerStateChanged;

        if (IsHeldByLocalPlayer)
        {
            CloseLocalDialogue();
        }
    }

    private void OnDisable()
    {
        if (IsHeldByLocalPlayer)
        {
            CloseLocalDialogue();
        }
    }

    private void OnPowerStateChanged(bool oldVal, bool newVal)
    {
        UpdateVisuals();

        if (IsHeldByLocalPlayer)
        {
            if (newVal)
            {
                OpenLocalDialogue();
            }
            else
            {
                CloseLocalDialogue();
            }
        }
    }

    /// <summary>
    /// Left Click toggle when held in hand.
    /// Turns on the Spirit Box (opens dialogue), or turns it off (cancels and closes dialogue).
    /// </summary>
    public override void UsePrimary()
    {
        if (!IsPoweredOn)
        {
            // Power ON
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                TogglePowerRpc();
            }
            else
            {
                localFallbackPower = true;
                UpdateVisuals();
            }
            OpenLocalDialogue();
        }
        else
        {
            // Power OFF & Cancel
            TurnOffAndCancel();
        }
    }

    /// <summary>
    /// Turns off the Spirit Box and exits dialogue mode (called on Left Click while dialogue is active).
    /// </summary>
    public void TurnOffAndCancel()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            SetPowerRpc(false);
        }
        else
        {
            localFallbackPower = false;
            UpdateVisuals();
        }
        CloseLocalDialogue();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPowerRpc(bool state)
    {
        isPoweredOn.Value = state;
    }

    public void OpenLocalDialogue()
    {
        EnsureDialogueUI();
        if (SpiritBoxDialogueUI.Instance != null)
        {
            SpiritBoxDialogueUI.Instance.OpenDialogue(this, dialogueQuestions);
        }
        else
        {
            Debug.LogError("[SpiritBoxItem] SpiritBoxDialogueUI.Instance could not be found or initialized!");
        }
    }

    public void CloseLocalDialogue()
    {
        if (SpiritBoxDialogueUI.Instance != null && SpiritBoxDialogueUI.Instance.IsOpen)
        {
            SpiritBoxDialogueUI.Instance.CloseDialogue();
        }
        IsLocalDialogueActive = false;
    }

    private void EnsureDialogueUI()
    {
        if (SpiritBoxDialogueUI.Instance == null)
        {
            var existing = Object.FindAnyObjectByType<SpiritBoxDialogueUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
            }
            else
            {
                var uiGo = new GameObject("SpiritBoxDialogueUI");
                uiGo.AddComponent<UIDocument>();
                uiGo.AddComponent<SpiritBoxDialogueUI>();
            }
        }
    }

    /// <summary>
    /// Broadcast an inquiry question (index 0 to 4) to the ghost.
    /// </summary>
    public void AskQuestion(int questionIndex)
    {
        if (questionIndex < 0 || questionIndex >= 5) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            AskQuestionRpc(questionIndex);
        }
        else
        {
            // Offline / local editor testing
            bool ghostResponded = CheckGhostResponse();
            ReceiveGhostResponseRpc(questionIndex, ghostResponded);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AskQuestionRpc(int questionIndex)
    {
        if (!isPoweredOn.Value) return;

        bool ghostResponded = CheckGhostResponse();

        if (ghostResponded && ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 2, 5f);
        }

        ReceiveGhostResponseRpc(questionIndex, ghostResponded);
    }

    private bool CheckGhostResponse()
    {
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler == null) return false;

        // 1. Cross reference with ghost's evidence list
        if (!ghostHandler.HasEvidence(EvidenceType.SpiritBox))
        {
            return false;
        }

        // 2. Proximity check: inside ghost room OR within detection radius of the ghost
        bool inGhostRoom = (ghostHandler.ghostroomMarker != null && ghostHandler.ghostroomMarker.OverlapPoint(transform.position));
        if (inGhostRoom) return true;

        if (Vector2.Distance(transform.position, ghostHandler.transform.position) <= detectionRadius)
        {
            return true;
        }

        if (ghostHandler.eventGhost != null && Vector2.Distance(transform.position, ghostHandler.eventGhost.transform.position) <= detectionRadius)
        {
            return true;
        }

        if (ghostHandler.hunterGhost != null && Vector2.Distance(transform.position, ghostHandler.hunterGhost.transform.position) <= detectionRadius)
        {
            return true;
        }

        return false;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ReceiveGhostResponseRpc(int questionIndex, bool hasEvidence)
    {
        if (audioSource != null)
        {
            if (hasEvidence)
            {
                AudioClip clip = GetResponseClip(questionIndex);
                if (clip != null)
                {
                    audioSource.PlayOneShot(clip);
                }
                else if (radioStaticAudio != null)
                {
                    audioSource.PlayOneShot(radioStaticAudio, 0.4f);
                }
            }
            else
            {
                if (radioStaticAudio != null)
                {
                    audioSource.PlayOneShot(radioStaticAudio, 0.6f);
                }
            }
        }

        // Update local dialogue UI if held by local player
        if (IsHeldByLocalPlayer && SpiritBoxDialogueUI.Instance != null)
        {
            string question = (questionIndex >= 0 && questionIndex < dialogueQuestions.Length) ? dialogueQuestions[questionIndex] : "";
            string responseText = hasEvidence
                ? ((questionIndex >= 0 && questionIndex < ghostResponseTexts.Length) ? ghostResponseTexts[questionIndex] : "\"Yes...\"")
                : "...Nothing but radio static.";

            SpiritBoxDialogueUI.Instance.ShowResponse(question, responseText, hasEvidence);
        }
    }

    public AudioClip GetResponseClip(int index)
    {
        AudioClip clip = index switch
        {
            0 => ghostResponseAudio1,
            1 => ghostResponseAudio2,
            2 => ghostResponseAudio3,
            3 => ghostResponseAudio4,
            4 => ghostResponseAudio5,
            _ => null
        };

        if (clip != null) return clip;

        if (ghostResponseAudios != null && index >= 0 && index < ghostResponseAudios.Length)
        {
            return ghostResponseAudios[index];
        }

        return null;
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);

        // If local player no longer holds this item, close dialogue
        if (NetworkManager.Singleton != null && newOwnerId != NetworkManager.Singleton.LocalClientId)
        {
            CloseLocalDialogue();
        }
    }

    protected override void OnInHandChanged(bool inHand)
    {
        base.OnInHandChanged(inHand);
        if (!inHand && IsHeldByLocalPlayer)
        {
            CloseLocalDialogue();
        }
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        if (spriteRenderer != null)
        {
            if (IsPoweredOn && activatedSprite != null)
            {
                spriteRenderer.sprite = activatedSprite;
            }
            else if (!IsPoweredOn && deactivatedSprite != null)
            {
                spriteRenderer.sprite = deactivatedSprite;
            }
        }
    }
}
