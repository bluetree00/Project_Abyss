using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 비주얼노벨 스타일 대화 팝업.
/// UIManager.ShowPopupUIAndGetAsync&lt;UI_DialoguePopup&gt;() 로 로드 후
/// ShowAsync(sequence) 로 재생. 전 라인 완료 시 자동 닫힘.
/// </summary>
public class UI_DialoguePopup : UI_Popup
{
    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("Portrait")]
    [SerializeField] private Image portrait;  // 좌측 고정 일러스트 — 라인마다 Addressables로 교체

    [Header("Text Box")]
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Header("Advance")]
    [SerializeField] private Button advanceButton;

    [Header("Speaker Names")]
    [SerializeField] private string godName    = "???";
    [SerializeField] private string shadowName = "그림자";

    [Header("Typewriter")]
    [SerializeField, Min(0.01f)] private float charDelay = 0.03f;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private bool _isTyping;
    private bool _skipRequested;
    private UniTaskCompletionSource _advanceTcs;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();
        if (advanceButton != null)
            advanceButton.onClick.AddListener(OnAdvanceClicked);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(0))
            OnAdvanceClicked();
    }

    private void OnDestroy()
    {
        if (advanceButton != null)
            advanceButton.onClick.RemoveListener(OnAdvanceClicked);
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>SO 기반 재생. 모든 라인이 끝나면 자동으로 닫는다.</summary>
    public UniTask ShowAsync(DialogueSequenceSO sequence)
        => ShowAsync(sequence?.Lines);

    /// <summary>라인 배열 직접 재생 (서버 CSV 로드 경로). 모든 라인이 끝나면 자동으로 닫는다.</summary>
    public async UniTask ShowAsync(DialogueLine[] lines)
    {
        if (lines == null || lines.Length == 0)
        {
            ClosePopupUI();
            return;
        }

        var ct = this.GetCancellationTokenOnDestroy();

        for (int i = 0; i < lines.Length; i++)
        {
            try
            {
                await ShowLineAsync(lines[i], ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        ClosePopupUI();
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────

    private async UniTask ShowLineAsync(DialogueLine line, CancellationToken ct)
    {
        SetSpeakerName(line.speaker);
        await LoadIllustrationAsync(line.illustrationKey, ct);

        if (bodyText != null)
            bodyText.text = string.Empty;

        _isTyping = true;
        _skipRequested = false;
        _advanceTcs = new UniTaskCompletionSource();

        TypewriterAsync(line.text, ct).Forget();

        await _advanceTcs.Task;
    }

    private async UniTask LoadIllustrationAsync(string key, CancellationToken ct)
    {
        if (portrait == null || string.IsNullOrEmpty(key)) return;
        try
        {
            var sprite = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(key);
            ct.ThrowIfCancellationRequested();
            if (sprite != null)
                portrait.sprite = sprite;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UI_DialoguePopup] 일러스트 로드 실패: {key} — {e.Message}");
        }
    }

    private async UniTaskVoid TypewriterAsync(string text, CancellationToken ct)
    {
        if (bodyText == null) { _isTyping = false; return; }

        try
        {
            for (int i = 0; i < text.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (_skipRequested)
                {
                    bodyText.text = text;
                    break;
                }

                bodyText.text = text[..(i + 1)];
                await UniTask.Delay(TimeSpan.FromSeconds(charDelay), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 팝업 파괴 시 정상 취소 — 무시
        }
        finally
        {
            _isTyping = false;
        }
    }

    private void SetSpeakerName(DialogueSpeaker speaker)
    {
        if (speakerNameText == null) return;
        speakerNameText.text = speaker switch
        {
            DialogueSpeaker.God    => godName,
            DialogueSpeaker.Shadow => shadowName,
            _                      => string.Empty,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnAdvanceClicked()
    {
        if (_advanceTcs == null) return;

        if (_isTyping)
        {
            _skipRequested = true;
            return;
        }

        _advanceTcs.TrySetResult();
    }
}
