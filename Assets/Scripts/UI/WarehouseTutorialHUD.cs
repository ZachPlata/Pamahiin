using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Interactive checklist HUD for WarehouseTutorialScene.
/// Displays current Bay (1-5), step checkboxes, keybind badges, hints,
/// and the final training completion summary modal.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class WarehouseTutorialHUD : MonoBehaviour
{
    private UIDocument uiDocument;
    private Label lblProgress;
    private Label lblBayTitle;
    private VisualElement progressFill;
    private VisualElement stepsContainer;
    private Label lblHint;
    private VisualElement completionModal;

    private WarehouseTutorialManager boundManager;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        uiDocument.sortingOrder = 4;

        BuildUI(uiDocument.rootVisualElement);
        TryBindManager();
        RefreshUI();
    }

    private void OnDisable()
    {
        if (boundManager != null)
        {
            boundManager.OnTutorialStateChanged -= RefreshUI;
            boundManager.OnTutorialHint -= HandleHint;
            boundManager.OnTutorialCompleted -= ShowCompletionModal;
            boundManager = null;
        }
    }

    private void Update()
    {
        if (boundManager == null && WarehouseTutorialManager.Instance != null)
        {
            TryBindManager();
            RefreshUI();
        }
    }

    private void TryBindManager()
    {
        if (boundManager != null || WarehouseTutorialManager.Instance == null) return;
        boundManager = WarehouseTutorialManager.Instance;
        boundManager.OnTutorialStateChanged += RefreshUI;
        boundManager.OnTutorialHint += HandleHint;
        boundManager.OnTutorialCompleted += ShowCompletionModal;
    }

    private void BuildUI(VisualElement root)
    {
        if (root == null) return;
        root.Clear();
        root.pickingMode = PickingMode.Ignore;

        // Top-left checklist card
        var card = new VisualElement();
        card.name = "tutorial-checklist-card";
        card.pickingMode = PickingMode.Ignore;
        card.style.position = Position.Absolute;
        card.style.top = 18;
        card.style.left = 18;
        card.style.width = 360;
        card.style.paddingTop = 12;
        card.style.paddingBottom = 12;
        card.style.paddingLeft = 14;
        card.style.paddingRight = 14;
        card.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.86f);
        card.style.borderTopWidth = 1;
        card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1;
        card.style.borderRightWidth = 1;
        var borderColor = new Color(0.76f, 0.64f, 0.42f, 0.55f);
        card.style.borderTopColor = borderColor;
        card.style.borderBottomColor = borderColor;
        card.style.borderLeftColor = borderColor;
        card.style.borderRightColor = borderColor;
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;

        // Header row
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.justifyContent = Justify.SpaceBetween;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 6;

        var headerTitle = new Label("WAREHOUSE TUTORIAL");
        headerTitle.style.color = new Color(0.92f, 0.80f, 0.54f, 1f);
        headerTitle.style.fontSize = 11;
        headerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;

        lblProgress = new Label("BAY 1 / 5");
        lblProgress.style.color = new Color(0.85f, 0.88f, 0.92f, 1f);
        lblProgress.style.fontSize = 11;
        lblProgress.style.unityFontStyleAndWeight = FontStyle.Bold;

        headerRow.Add(headerTitle);
        headerRow.Add(lblProgress);
        card.Add(headerRow);

        // Progress bar
        var barBg = new VisualElement();
        barBg.style.height = 5;
        barBg.style.backgroundColor = new Color(0.18f, 0.20f, 0.24f, 1f);
        barBg.style.borderTopLeftRadius = 3;
        barBg.style.borderTopRightRadius = 3;
        barBg.style.borderBottomLeftRadius = 3;
        barBg.style.borderBottomRightRadius = 3;
        barBg.style.marginBottom = 8;

        progressFill = new VisualElement();
        progressFill.style.height = 5;
        progressFill.style.width = Length.Percent(20);
        progressFill.style.backgroundColor = new Color(0.82f, 0.65f, 0.34f, 1f);
        progressFill.style.borderTopLeftRadius = 3;
        progressFill.style.borderTopRightRadius = 3;
        progressFill.style.borderBottomLeftRadius = 3;
        progressFill.style.borderBottomRightRadius = 3;
        barBg.Add(progressFill);
        card.Add(barBg);

        // Bay title
        lblBayTitle = new Label("Bay 1 — Controls & Channel Separation");
        lblBayTitle.style.color = Color.white;
        lblBayTitle.style.fontSize = 14;
        lblBayTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        lblBayTitle.style.marginBottom = 8;
        card.Add(lblBayTitle);

        // Steps container
        stepsContainer = new VisualElement();
        stepsContainer.style.marginBottom = 8;
        card.Add(stepsContainer);

        // Hint label
        lblHint = new Label("");
        lblHint.style.color = new Color(0.75f, 0.82f, 0.88f, 0.95f);
        lblHint.style.fontSize = 11;
        lblHint.style.whiteSpace = WhiteSpace.Normal;
        lblHint.style.paddingTop = 6;
        lblHint.style.borderTopWidth = 1;
        lblHint.style.borderTopColor = new Color(1f, 1f, 1f, 0.12f);
        card.Add(lblHint);

        root.Add(card);

        // Completion modal (hidden initially)
        completionModal = new VisualElement();
        completionModal.name = "tutorial-completion-modal";
        completionModal.style.position = Position.Absolute;
        completionModal.style.left = 0;
        completionModal.style.right = 0;
        completionModal.style.top = 0;
        completionModal.style.bottom = 0;
        completionModal.style.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 0.85f);
        completionModal.style.alignItems = Align.Center;
        completionModal.style.justifyContent = Justify.Center;
        completionModal.style.display = DisplayStyle.None;

        var modalBox = new VisualElement();
        modalBox.style.width = 440;
        modalBox.style.paddingTop = 24;
        modalBox.style.paddingBottom = 24;
        modalBox.style.paddingLeft = 24;
        modalBox.style.paddingRight = 24;
        modalBox.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.98f);
        modalBox.style.borderTopWidth = 2;
        modalBox.style.borderBottomWidth = 2;
        modalBox.style.borderLeftWidth = 2;
        modalBox.style.borderRightWidth = 2;
        modalBox.style.borderTopColor = borderColor;
        modalBox.style.borderBottomColor = borderColor;
        modalBox.style.borderLeftColor = borderColor;
        modalBox.style.borderRightColor = borderColor;
        modalBox.style.borderTopLeftRadius = 8;
        modalBox.style.borderTopRightRadius = 8;
        modalBox.style.borderBottomLeftRadius = 8;
        modalBox.style.borderBottomRightRadius = 8;
        modalBox.style.alignItems = Align.Center;

        var modalTitle = new Label("WAREHOUSE TRAINING COMPLETE");
        modalTitle.style.color = new Color(0.95f, 0.82f, 0.50f, 1f);
        modalTitle.style.fontSize = 20;
        modalTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        modalTitle.style.marginBottom = 10;

        var modalBody = new Label("You have mastered equipment handling, power & sanity management, evidence gathering, and hunt survival. Contracts and Multiplayer are now unlocked.");
        modalBody.style.color = new Color(0.88f, 0.90f, 0.94f, 1f);
        modalBody.style.fontSize = 13;
        modalBody.style.whiteSpace = WhiteSpace.Normal;
        modalBody.style.unityTextAlign = TextAnchor.MiddleCenter;
        modalBody.style.marginBottom = 18;

        var btnLobby = new Button(() => WarehouseTutorialManager.Instance?.CompleteTutorial())
        {
            text = "Enter Safe House Lobby"
        };
        btnLobby.style.width = 240;
        btnLobby.style.height = 38;
        btnLobby.style.marginBottom = 8;

        var btnMenu = new Button(() =>
        {
            ProgressionAndLoadoutManager.MarkTutorialCompleted();
            if (NetworkController.Instance != null)
                NetworkController.Instance.DisconnectAndReturnToMenu();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME);
        })
        {
            text = "Return to Main Menu"
        };
        btnMenu.style.width = 240;
        btnMenu.style.height = 34;

        modalBox.Add(modalTitle);
        modalBox.Add(modalBody);
        modalBox.Add(btnLobby);
        modalBox.Add(btnMenu);
        completionModal.Add(modalBox);
        root.Add(completionModal);
    }

    private void HandleHint(string message)
    {
        if (lblHint != null)
        {
            lblHint.text = message;
        }
    }

    private void ShowCompletionModal()
    {
        if (completionModal != null)
        {
            completionModal.style.display = DisplayStyle.Flex;
        }
    }

    public void RefreshUI()
    {
        if (boundManager == null || stepsContainer == null) return;

        TutorialBay bay = boundManager.CurrentBay;
        int bayNum = Mathf.Clamp((int)bay, 1, 5);
        if (bay == TutorialBay.Completed) bayNum = 5;

        if (lblProgress != null) lblProgress.text = $"BAY {bayNum} / 5";
        if (progressFill != null)
        {
            float pct = bay == TutorialBay.Completed ? 100f : (bayNum / 5f) * 100f;
            progressFill.style.width = Length.Percent(pct);
        }
        if (lblBayTitle != null) lblBayTitle.text = boundManager.GetBayTitle(bay);

        stepsContainer.Clear();
        IReadOnlyList<(string stepId, string label, string keyBadge)> steps = boundManager.GetStepsForBay(bay);
        foreach (var (stepId, label, keyBadge) in steps)
        {
            bool done = boundManager.IsStepCompleted(stepId);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var left = new Label($"{(done ? "[✓]" : "[ ]")} {label}");
            left.style.fontSize = 12;
            left.style.color = done ? new Color(0.55f, 0.92f, 0.62f, 1f) : new Color(0.92f, 0.92f, 0.94f, 1f);

            var badge = new Label(keyBadge);
            badge.style.fontSize = 10;
            badge.style.color = new Color(0.95f, 0.84f, 0.56f, 1f);
            badge.style.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 0.95f);
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;

            row.Add(left);
            row.Add(badge);
            stepsContainer.Add(row);
        }

        if (bay == TutorialBay.Completed)
        {
            ShowCompletionModal();
        }
    }
}
