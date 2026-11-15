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
    [Tooltip("Audio clip played on loop when radio is powered on.")]
    [SerializeField] private AudioClip radioStaticAudio;
    [Tooltip("Volume level for the looping radio static sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float staticAudioVolume = 0.5f;

    [SerializeField] private AudioSource staticAudioSource;

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

    public override bool IsHeldByLocalPlayer
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

        // Ghost voice response audio source
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        ConfigureAudioSource(audioSource);

        // Dedicated looping static audio source
        if (staticAudioSource == null)
        {
            var sources = GetComponents<AudioSource>();
            if (sources.Length > 1)
            {
                staticAudioSource = sources[1];
            }
            else
            {
                staticAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        ConfigureAudioSource(staticAudioSource);
        staticAudioSource.loop = true;
        staticAudioSource.playOnAwake = false;
        staticAudioSource.volume = staticAudioVolume;

        if (radioStaticAudio == null) radioStaticAudio = GameAudioManager.GetClip("radio_static");
        if (ghostResponseAudio1 == null) ghostResponseAudio1 = GameAudioManager.GetSpiritBoxVoiceClip(1);
        if (ghostResponseAudio2 == null) ghostResponseAudio2 = GameAudioManager.GetSpiritBoxVoiceClip(2);
        if (ghostResponseAudio3 == null) ghostResponseAudio3 = GameAudioManager.GetSpiritBoxVoiceClip(3);
        if (ghostResponseAudio4 == null) ghostResponseAudio4 = GameAudioManager.GetSpiritBoxVoiceClip(4);
        if (ghostResponseAudio5 == null) ghostResponseAudio5 = GameAudioManager.GetSpiritBoxVoiceClip(5);
    }

    private void ConfigureAudioSource(AudioSource src)
    {
        GameAudioManager.ConfigureProximityAudioSource(src, 0.45f, 2.8f, 1.0f);
        src.playOnAwake = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += OnPowerStateChanged;
        UpdateStaticAudio();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        isPoweredOn.OnValueChanged -= OnPowerStateChanged;
        UpdateStaticAudio();

        if (IsHeldByLocalPlayer)
        {
            CloseLocalDialogue();
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        UpdateStaticAudio();

        if (IsHeldByLocalPlayer)
        {
            CloseLocalDialogue();
        }
    }

    protected override void Update()
    {
        base.Update();
        if (staticAudioSource != null && IsPoweredOn && staticAudioSource.isPlaying)
        {
            float floorMult = GameAudioManager.GetFloorAttenuation(transform.position, 0f);
            staticAudioSource.volume = GameAudioManager.GetEffectiveSfxVolume(staticAudioVolume * floorMult);
        }
    }

    private void UpdateStaticAudio()
    {
        if (staticAudioSource == null) return;

        bool shouldPlay = IsPoweredOn && radioStaticAudio != null && !IsHeldByDeadPlayer();
        if (shouldPlay)
        {
            if (staticAudioSource.clip != radioStaticAudio)
            {
                staticAudioSource.clip = radioStaticAudio;
            }
            staticAudioSource.loop = true;
            float floorMult = GameAudioManager.GetFloorAttenuation(transform.position, 0f);
            staticAudioSource.volume = GameAudioManager.GetEffectiveSfxVolume(staticAudioVolume * floorMult);
            if (!staticAudioSource.isPlaying)
            {
                staticAudioSource.Play();
            }
        }
        else
        {
            if (staticAudioSource.isPlaying)
            {
                staticAudioSource.Stop();
            }
        }
    }

    /// <summary>
    /// Validates whether the Spirit Box is currently inside the active Ghost Room bounds.
    /// </summary>
    public bool IsInsideGhostRoom()
    {
        return GhostRoomMarker.IsInsideGhostRoom(transform.position);
    }

    private void OnPowerStateChanged(bool oldVal, bool newVal)
    {
        UpdateVisuals();
        UpdateStaticAudio();

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
    /// Can be toggled anywhere; ghost responses still require being in the ghost room.
    /// </summary>
    public override void UsePrimary()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        if (!IsPoweredOn)
        {
            // Power ON (allowed anywhere)
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
            {
                TogglePowerRpc();
            }
            else
            {
                localFallbackPower = true;
                UpdateVisuals();
                UpdateStaticAudio();
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
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            SetPowerRpc(false);
        }
        else
        {
            localFallbackPower = false;
            UpdateVisuals();
        }
        UpdateStaticAudio();
        CloseLocalDialogue();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc(RpcParams rpcParams = default)
    {
        if (!IsPlayerAliveAndPresent(rpcParams.Receive.SenderClientId)) return;
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPowerRpc(bool state, RpcParams rpcParams = default)
    {
        if (!IsPlayerAliveAndPresent(rpcParams.Receive.SenderClientId)) return;
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
                var doc = uiGo.AddComponent<UIDocument>();
                doc.panelSettings = PamahiinUIThemeUtility.ResolveRuntimePanelSettings(doc.panelSettings);
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

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
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
    private void AskQuestionRpc(int questionIndex, RpcParams rpcParams = default)
    {
        if (!IsPlayerAliveAndPresent(rpcParams.Receive.SenderClientId)) return;
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

        // 2. Responses only come when inside the activated GhostRoomMarker bounds
        if (!IsInsideGhostRoom())
        {
            return false;
        }

        return true;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ReceiveGhostResponseRpc(int questionIndex, bool hasEvidence)
    {
        if (audioSource != null && hasEvidence)
        {
            AudioClip clip = GetResponseClip(questionIndex);
            if (clip != null)
            {
                float floorMult = GameAudioManager.GetFloorAttenuation(transform.position, 0f);
                if (floorMult > 0.01f)
                {
                    audioSource.PlayOneShot(clip, GameAudioManager.GetEffectiveSfxVolume(floorMult));
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
        UpdateStaticAudio();
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
