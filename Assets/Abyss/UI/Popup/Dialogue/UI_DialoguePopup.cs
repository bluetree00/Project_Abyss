using System;
using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] private Image portrait;

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

    [Header("Illustration")]
    [SerializeField, Min(0f)] private float illustFadeIn = 0.25f;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private bool _isTyping;
    private bool _skipRequested;
    private UniTaskCompletionSource _advanceTcs;

    private readonly Dictionary<string, Sprite> _illustCache = new();
    private readonly List<Action> _releaseActions = new();

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
        ReleaseIllustrations();
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

        if (portrait != null) portrait.color = new Color(1f, 1f, 1f, 0f);

        try
        {
            await PreloadIllustrationsAsync(lines, ct);
            for (int i = 0; i < lines.Length; i++)
                await ShowLineAsync(lines[i], ct);
        }
        catch (OperationCanceledException) { return; }
        finally { ReleaseIllustrations(); }

        ClosePopupUI();
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>시퀀스 시작 시 등장하는 고유 키를 한 번 순회해 일괄 로드.</summary>
    private async UniTask PreloadIllustrationsAsync(DialogueLine[] lines, CancellationToken ct)
    {
        var keys = lines
            .Select(l => l.illustrationKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct();

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();

            var sprite = await Managers.AddressableManager.TryLoadAssetAsync<Sprite>(key);
            if (sprite != null)
            {
                _illustCache[key] = sprite;
                var k = key;
                _releaseActions.Add(() => Managers.AddressableManager.ReleaseAsset<Sprite>(k));
                continue;
            }

            // PNG가 Texture2D 주 타입으로 등록된 경우 폴백
            var tex = await Managers.AddressableManager.TryLoadAssetAsync<Texture2D>(key);
            if (tex != null)
            {
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
                _illustCache[key] = sprite;
                var k = key;
                _releaseActions.Add(() => Managers.AddressableManager.ReleaseAsset<Texture2D>(k));
            }
            else
            {
                Debug.LogWarning($"[UI_DialoguePopup] 일러스트 키 없음: {key}");
            }
        }
    }

    private void ReleaseIllustrations()
    {
        foreach (var release in _releaseActions)
            release();
        _releaseActions.Clear();
        _illustCache.Clear();
    }

    private async UniTask ShowLineAsync(DialogueLine line, CancellationToken ct)
    {
        SetSpeakerName(line.speaker);
        ApplyIllustration(line.illustrationKey, ct);

        if (bodyText != null)
            bodyText.text = string.Empty;

        _isTyping = true;
        _skipRequested = false;
        _advanceTcs = new UniTaskCompletionSource();

        TypewriterAsync(line.text, ct).Forget();

        await _advanceTcs.Task.AttachExternalCancellation(ct);
    }

    private void ApplyIllustration(string key, CancellationToken ct)
    {
        if (portrait == null) return;
        if (string.IsNullOrEmpty(key))
        {
            portrait.sprite = null;
            portrait.color = new Color(1f, 1f, 1f, 0f);
            return;
        }
        if (_illustCache.TryGetValue(key, out var sprite))
        {
            portrait.sprite = sprite;
            FadeIllustrationAsync(ct).Forget();
        }
    }

    private async UniTaskVoid FadeIllustrationAsync(CancellationToken ct)
    {
        if (portrait == null) return;

        float elapsed = 0f;
        float startAlpha = portrait.color.a;

        try
        {
            while (elapsed < illustFadeIn)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / illustFadeIn);
                portrait.color = new Color(1f, 1f, 1f, Mathf.Lerp(startAlpha, 1f, t));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { return; }

        portrait.color = Color.white;
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
