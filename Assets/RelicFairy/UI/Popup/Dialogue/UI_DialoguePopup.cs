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
    // 대사 중 이동·시간 흐름 차단(보스전 포함). timeScale=0이라 내부 연출은 unscaled로 동작해야 함.
    public override bool BlocksGameplay => true;

    /// <summary>
    /// 라인이 넘어갈 때 그 라인의 일러스트 키를 알린다(빈 키 포함).
    /// 구독자가 있으면 팝업은 자체 슬롯에 삽화를 그리지 않고 외부 프레젠터
    /// (예: 전체화면 <see cref="IntroPageBook"/>)에 표시를 위임한다.
    /// </summary>
    public static event Action<string> OnLineIllustration;

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
    [SerializeField] private string godName     = "???";
    [SerializeField] private string shadowName  = "그림자";
    [SerializeField] private string lichName    = "리치";
    [SerializeField] private string mordredName = "모르드레드";
    [SerializeField] private string arthurName  = "아서왕";
    [SerializeField] private string knightName  = "기사";

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
            // 외부 프레젠터가 삽화를 맡으면 팝업은 로드하지 않는다(중복 로드/표시 방지).
            if (OnLineIllustration == null)
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
        // 매니저 참조를 1회 캡처해 release 람다에 가둔다. AddressableManager는 순수 C# 객체라
        // Managers.Instance(MonoBehaviour)가 teardown으로 먼저 파괴돼도 이 참조는 살아있어
        // OnDestroy/finally의 ReleaseIllustrations가 정적 getter(null)를 거치지 않고 안전하게 해제한다.
        var addressables = Managers.AddressableManager;
        if (addressables == null) return;

        var keys = lines
            .Select(l => l.illustrationKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct();

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();

            var sprite = await addressables.TryLoadAssetAsync<Sprite>(key);
            if (sprite != null)
            {
                _illustCache[key] = sprite;
                var k = key;
                _releaseActions.Add(() => addressables.ReleaseAsset<Sprite>(k));
                continue;
            }

            // PNG가 Texture2D 주 타입으로 등록된 경우 폴백
            var tex = await addressables.TryLoadAssetAsync<Texture2D>(key);
            if (tex != null)
            {
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
                _illustCache[key] = sprite;
                var k = key;
                _releaseActions.Add(() => addressables.ReleaseAsset<Texture2D>(k));
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
        // 외부 프레젠터(전체화면 페이지 등)가 있으면 표시를 넘기고 자체 슬롯은 비운다.
        if (OnLineIllustration != null)
        {
            OnLineIllustration(key);
            if (portrait != null)
            {
                portrait.sprite = null;
                portrait.color  = new Color(1f, 1f, 1f, 0f);
            }
            return;
        }

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

        // 전체 텍스트를 한 번에 넣고 maxVisibleCharacters로 한 글자씩 드러낸다.
        // text[..i]로 자르면 <color=...> 같은 리치텍스트 태그가 중간에 잘려 깨지므로 이 방식을 쓴다.
        bodyText.text = text;
        bodyText.ForceMeshUpdate();
        int total = bodyText.textInfo.characterCount; // 태그 제외한 보이는 글자 수
        bodyText.maxVisibleCharacters = 0;

        try
        {
            for (int i = 0; i <= total; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (_skipRequested)
                {
                    bodyText.maxVisibleCharacters = total;
                    break;
                }

                bodyText.maxVisibleCharacters = i;
                // timeScale=0(BlocksGameplay) 중에도 진행되도록 unscaled.
                await UniTask.Delay(TimeSpan.FromSeconds(charDelay), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 팝업 파괴 시 정상 취소 — 무시
        }
        finally
        {
            if (bodyText != null) bodyText.maxVisibleCharacters = int.MaxValue; // 남은 글자 모두 표시
            _isTyping = false;
        }
    }

    private void SetSpeakerName(DialogueSpeaker speaker)
    {
        if (speakerNameText == null) return;
        speakerNameText.text = speaker switch
        {
            DialogueSpeaker.God     => godName,
            DialogueSpeaker.Shadow  => shadowName,
            DialogueSpeaker.Lich    => lichName,
            DialogueSpeaker.Mordred => mordredName,
            DialogueSpeaker.Arthur  => arthurName,
            DialogueSpeaker.Knight  => knightName,
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
