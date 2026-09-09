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

    // ── 스킨 장식 크기 (원본 비율 유지) ──
    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("Portrait")]
    [SerializeField] private Image portrait;

    [Header("Text Box")]
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [Tooltip("이름 옆 역할 태그(참고 이미지의 「작가」). 비어 있으면 숨긴다. 프리팹에 없으면 런타임에 만든다.")]
    [SerializeField] private TextMeshProUGUI roleTagText;

    [Header("Advance")]
    [SerializeField] private Button advanceButton;

    [Header("Speaker Names")]
    [Tooltip("멀린 — 정체를 감추는 동안 \"???\"로 표기.")]
    [SerializeField] private string merlinName  = "???";
    [SerializeField] private string shadowName  = "그림자";
    [SerializeField] private string lichName    = "리치";
    [SerializeField] private string mordredName = "모르드레드";
    [SerializeField] private string arthurName  = "아서왕";
    [SerializeField] private string knightName  = "기사";
    [SerializeField] private string forestGuardianName = "숲의 수호자";
    [SerializeField] private string dragonName  = "화룡";
    [SerializeField] private string deathKnightName = "죽음의 기사";

    [Header("Speaker Titles — 이름 옆 역할 태그")]
    [Tooltip("DialogueSpeaker 순서(None·Merlin·Shadow·Lich·Mordred·Arthur·Knight·ForestGuardian·Dragon·DeathKnight). "
             + "기획 문구가 오기 전까지 비워 둔다 — 빈 칸은 태그를 숨긴다.")]
    [SerializeField] private string[] speakerTitles = new string[10];

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

    // 이미지형 띠(참고 이미지 스타일) — 본문은 한 페이지에 두 줄까지, 진행 표식은 타자가 끝난 뒤 점멸.
    private const int   MaxLinesPerPage = 2;
    private const float HintBlinkPeriod = 0.8f;
    private static readonly Color BandColor = new(0f, 0f, 0f, 0.65f);   // 암막 0.4 위에서 띠가 읽히는 값(실측 09-09)
    private static readonly Color RoleTagColor = new(0.72f, 0.74f, 0.78f, 1f);
    private TextMeshProUGUI _advanceHint;

    private readonly Dictionary<string, Sprite> _illustCache = new();
    private readonly List<Action> _releaseActions = new();

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    public override void Init()
    {
        // 열리는 순간부터 초상 슬롯은 투명 — 대사가 붙기 전 프리팹 기본(흰색·스프라이트 없음)이 흰 네모로 뜬다.
        if (portrait != null) portrait.color = new Color(1f, 1f, 1f, 0f);

        base.Init();
        if (advanceButton != null)
            advanceButton.onClick.AddListener(OnAdvanceClicked);

        ApplySkin();
        EnsureImageStyleNodes();
    }

    /// <summary>
    /// 띠 바탕을 입힌다. 이미지형(2026-09-09): 액자·모서리·장식·이름 판은 프리팹에서 걷었고,
    /// 남은 것은 <b>반투명 검은 띠 하나</b>다. 디자이너 띠 아트(<see cref="DialogueSkinSO.band"/>)가 있으면 그것을,
    /// 없으면 코드가 만든 둥근 소프트 판을 9-slice로 깐다 — 아트 없이도 참고 이미지 모습이 나온다.
    /// </summary>
    private void ApplySkin()
    {
        if (bodyText == null) return;
        if (!(bodyText.transform.parent is RectTransform box) || !box.TryGetComponent<Image>(out var band)) return;

        var art = UISkin.Dialogue?.band;
        if (art != null)
        {
            ShopUIStyle.Skin(band, art, sliced: true);   // 색은 아트가 갖는다
            return;
        }
        band.sprite = GetBandSprite();
        band.type   = Image.Type.Sliced;
        band.color  = BandColor;
    }

    /// <summary>
    /// 이미지형 띠에 필요한 노드 둘을 프리팹에 없으면 런타임에 만든다 —
    /// 이름 옆 <b>역할 태그</b>(이름 폭에 따라 자리가 바뀌므로 코드가 놓는다)와 띠 우하단 <b>진행 표식</b>(▼).
    /// </summary>
    private void EnsureImageStyleNodes()
    {
        if (speakerNameText != null && roleTagText == null)
        {
            roleTagText = MakeLabel(speakerNameText.rectTransform, "RoleTag", 15f, RoleTagColor,
                                    TextAlignmentOptions.MidlineLeft);
            var rt = roleTagText.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(240f, 24f);
            roleTagText.gameObject.SetActive(false);
        }

        if (_advanceHint == null && bodyText != null && bodyText.transform.parent is RectTransform box)
        {
            _advanceHint = MakeLabel(box, "AdvanceHint", 12f, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center);
            var rt = _advanceHint.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-18f, 14f);
            rt.sizeDelta = new Vector2(24f, 20f);
            _advanceHint.text = "▼";   // 글리프 화이트리스트 안의 기호
            _advanceHint.gameObject.SetActive(false);
        }
    }

    private TextMeshProUGUI MakeLabel(RectTransform parent, string name, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (bodyText != null && bodyText.font != null) t.font = bodyText.font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    /// <summary>띠 바탕 — 공용 생성기(둥근 사각 반경 28 · 소프트 14 · 9-slice 42). 아트가 오면 <see cref="DialogueSkinSO.band"/>로 갈아끼운다.</summary>
    private static Sprite GetBandSprite() => UIProceduralSprites.RoundedRect(radius: 28f, feather: 14f);

    private void Update()
    {
        // 마우스 클릭은 <b>AdvanceButton</b>(상자를 덮는 투명 캐처)이 받는다.
        // 여기서 GetMouseButtonDown을 또 보면 누를 때(Update)와 뗄 때(onClick) 두 번 불려
        // 한 번 클릭에 두 줄이 넘어가고, UI 가림 판정도 건너뛰어 다른 창 위를 눌러도 넘어간다.
        if (Input.GetKeyDown(KeyCode.F))
            OnAdvanceClicked();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();   // 차단 잠금 누수 방지(UI_Popup)
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
        // 해제 전에 슬롯을 비운다 — 파괴된 스프라이트를 물고 있는 Image는 흰 사각형으로 그려진다.
        if (portrait != null)
        {
            portrait.sprite = null;
            portrait.color  = new Color(1f, 1f, 1f, 0f);
        }
        foreach (var release in _releaseActions)
            release();
        _releaseActions.Clear();
        _illustCache.Clear();
    }

    private async UniTask ShowLineAsync(DialogueLine line, CancellationToken ct)
    {
        SetSpeakerName(line.speaker);
        ApplyIllustration(line.illustrationKey, ct);

        // 띠에는 두 줄까지만 — 넘치는 문장은 같은 화자의 다음 페이지로 나눠 넘긴다.
        var pages = SplitPages(line.text);
        for (int p = 0; p < pages.Count; p++)
        {
            if (bodyText != null)
                bodyText.text = string.Empty;

            _isTyping = true;
            _skipRequested = false;
            _advanceTcs = new UniTaskCompletionSource();

            TypewriterAsync(pages[p], ct).Forget();
            BlinkHintAsync(_advanceTcs, ct).Forget();

            await _advanceTcs.Task.AttachExternalCancellation(ct);
        }
    }

    /// <summary>
    /// 본문을 <see cref="MaxLinesPerPage"/>줄 단위로 나눈다. 실제 폰트·폭으로 한 번 재서 3줄째 첫 글자에서 자른다.
    /// (리치텍스트 태그가 페이지 경계를 걸치는 문장은 드물어 따로 닫지 않는다.)
    /// </summary>
    private List<string> SplitPages(string text)
    {
        var pages = new List<string>();
        if (bodyText == null || string.IsNullOrEmpty(text)) { pages.Add(text ?? string.Empty); return pages; }

        string rest = text;
        for (int guard = 0; guard < 16 && !string.IsNullOrEmpty(rest); guard++)
        {
            bodyText.text = rest;
            bodyText.maxVisibleCharacters = int.MaxValue;
            bodyText.ForceMeshUpdate();
            var info = bodyText.textInfo;
            if (info.lineCount <= MaxLinesPerPage) { pages.Add(rest); break; }

            int cutChar = info.lineInfo[MaxLinesPerPage].firstCharacterIndex;
            int srcIdx  = cutChar > 0 && cutChar < info.characterCount ? info.characterInfo[cutChar].index : -1;
            if (srcIdx <= 0 || srcIdx >= rest.Length) { pages.Add(rest); break; }

            pages.Add(rest.Substring(0, srcIdx).TrimEnd());
            rest = rest.Substring(srcIdx).TrimStart();
        }
        bodyText.text = string.Empty;
        return pages;
    }

    /// <summary>타자가 끝난 뒤 띠 우하단 ▼가 0.8초 주기로 숨 쉰다 — 넘길 수 있다는 신호. 넘기면 꺼진다.</summary>
    private async UniTaskVoid BlinkHintAsync(UniTaskCompletionSource page, CancellationToken ct)
    {
        if (_advanceHint == null) return;
        try
        {
            while (_isTyping && page == _advanceTcs)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (page != _advanceTcs) return;

            _advanceHint.gameObject.SetActive(true);
            float t = 0f;
            while (page == _advanceTcs && page.Task.Status == UniTaskStatus.Pending)
            {
                t += Time.unscaledDeltaTime;
                _advanceHint.alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / HintBlinkPeriod));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_advanceHint != null) _advanceHint.gameObject.SetActive(false);
        }
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
        else
        {
            // 키는 있는데 못 불러온 줄 — 이전 대사의 초상(해제돼 파괴된 스프라이트)이 남으면 흰 네모가 뜬다.
            portrait.sprite = null;
            portrait.color  = new Color(1f, 1f, 1f, 0f);
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
            DialogueSpeaker.Merlin  => merlinName,
            DialogueSpeaker.Shadow  => shadowName,
            DialogueSpeaker.Lich    => lichName,
            DialogueSpeaker.Mordred => mordredName,
            DialogueSpeaker.Arthur  => arthurName,
            DialogueSpeaker.Knight  => knightName,
            DialogueSpeaker.ForestGuardian => forestGuardianName,
            DialogueSpeaker.Dragon  => dragonName,
            DialogueSpeaker.DeathKnight    => deathKnightName,
            _                      => string.Empty,
        };
        ApplyRoleTag(speaker);
    }

    /// <summary>이름 옆 역할 태그 — 이름 폭을 재서 오른쪽 8px, 기준선보다 6px 위에 놓는다. 문구가 없으면 숨긴다.</summary>
    private void ApplyRoleTag(DialogueSpeaker speaker)
    {
        if (roleTagText == null || speakerNameText == null) return;

        int idx = (int)speaker;
        string title = speakerTitles != null && idx >= 0 && idx < speakerTitles.Length ? speakerTitles[idx] : null;
        bool show = !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(speakerNameText.text);
        roleTagText.gameObject.SetActive(show);
        if (!show) return;

        roleTagText.text = title;
        speakerNameText.ForceMeshUpdate();
        float half = speakerNameText.preferredWidth * 0.5f;
        roleTagText.rectTransform.anchoredPosition = new Vector2(half + 8f, 6f);
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
