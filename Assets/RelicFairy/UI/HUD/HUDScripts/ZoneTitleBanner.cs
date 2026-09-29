using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 위쪽 가운데의 <b>구역 이름</b> — 처음 들어간 구역에서 한 번, 제목 + 한 줄 설명(베이스캠프 「장비 공방」 등).
/// 월드의 <see cref="ZoneSign"/>가 <see cref="ZoneSign.TitleRequested"/>로 알린다(처음 들어간 구역만 쏜다 — 기록은 ZoneSign 몫).
/// 그 뒤로는 들어설 때마다 <see cref="ZoneSign.ArrivalRequested"/> → <see cref="ShowSubtle"/>: 제목만 작고 옅게, 느린 페이드(09-29 사용자).
/// 조작을 막지 않는다(입력·시간 정지 없음 · 레이캐스터 없음). 표시 중에 새 요청이 오면 그 자리에서 교체한다.
///
/// <para>HUD 계층에 붙이지 않는다 — 시작방 대화 중엔 HUD 전체가 숨는데(HudBootstrapper.SetStartRoomSuppressed)
/// 구역 이름은 그때도 보여야 한다. 자기 캔버스(1920×1080 · Match 0.5)를 앱 시작 때 한 번 세운다.
/// 정렬 150 = HUD(100) 위 · 장면 UI(300)·팝업(400)·화면 전환(900) 아래.</para>
/// </summary>
public sealed class ZoneTitleBanner : MonoBehaviour
{
    // ── Constants ──
    private const int   SortingOrder = 150;
    private const float TopRatio     = 0.20f;   // 화면 위에서 1/5 지점
    private const float FadeInSec    = 0.35f;
    private const float HoldSec      = 2.0f;
    private const float FadeOutSec   = 0.6f;
    private const float RiseY        = 10f;     // 나타날 때 아래에서 살짝 떠오른다
    private const float TitleSize    = 52f;
    private const float DescSize     = 24f;
    // 도착 표시(들어설 때마다) — 은은하게: 작은 제목 한 줄 · 옅은 받침 · 끝까지 다 밝히지 않음 · 느린 페이드
    private const float SubtleTitleSize = 34f;
    private const float SubtleFadeIn    = 0.6f;
    private const float SubtleHold      = 1.1f;
    private const float SubtleFadeOut   = 1.0f;
    private const float SubtlePeak      = 0.85f;

    private static readonly Color TitleInk = new(0.97f, 0.93f, 0.84f, 1f);
    private static readonly Color DescInk  = new(0.82f, 0.85f, 0.92f, 1f);
    private static readonly Color RuleInk  = new(0.91f, 0.73f, 0.33f, 0.80f);
    private static readonly Color BackInk  = new(0.02f, 0.02f, 0.04f, 0.75f);   // 글자 자리 전부 이 값(가장자리 함수 수정 뒤 — 0.85는 무거웠다, 09-28 3차)
    private static readonly Color SubtleBackInk = new(0.02f, 0.02f, 0.04f, 0.4f);

    // ── Private ──
    private CanvasGroup   _group;
    private RectTransform _block;
    private TMP_Text      _title;
    private TMP_Text      _desc;
    private Image         _back;
    private Image         _rule;
    private bool          _fullShowing;   // 처음 배너가 떠 있는 동안 — 도착 표시가 끼어들지 않는다
    private int           _fullSeq;       // 배너가 배너를 교체하면 앞 것의 끝 처리가 늦게 돌아 표시를 끄지 않게
    private CancellationTokenSource _cts;

    // ── Static ──
    /// <summary>앱 시작 때 한 번 세운다(씬을 넘어 산다).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<ZoneTitleBanner>() != null) return;
        var go = new GameObject("ZoneTitleBanner", typeof(RectTransform));
        DontDestroyOnLoad(go);
        go.AddComponent<ZoneTitleBanner>();
    }

    // ── Lifecycle ──
    private void Awake()     => Build();
    private void OnEnable()
    {
        ZoneSign.TitleRequested   += Show;
        ZoneSign.ArrivalRequested += ShowSubtle;
    }

    private void OnDisable()
    {
        ZoneSign.TitleRequested   -= Show;
        ZoneSign.ArrivalRequested -= ShowSubtle;
        Cancel();
    }
    private void OnDestroy() => Cancel();

    // ── Public Methods ──

    /// <summary>제목과 한 줄 설명을 띄운다. 표시 중이면 지금 밝기에서 이어 새 글자로 바꾼다.</summary>
    public void Show(string title, string description)
    {
        if (string.IsNullOrEmpty(title)) return;
        Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        PlayAsync(title, description, _cts.Token).Forget();
    }

    /// <summary>도착 표시 — 제목만 작고 옅게. 처음 배너가 떠 있으면 방해하지 않는다.</summary>
    public void ShowSubtle(string title)
    {
        if (string.IsNullOrEmpty(title) || _fullShowing) return;
        Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        PlaySubtleAsync(title, _cts.Token).Forget();
    }

    // ── Private Methods ──

    private async UniTaskVoid PlayAsync(string title, string description, CancellationToken ct)
    {
        ApplyStyle(subtle: false);
        _title.text = title;
        _desc.text  = description ?? string.Empty;
        _desc.gameObject.SetActive(!string.IsNullOrEmpty(description));

        _fullShowing = true;
        int seq = ++_fullSeq;
        try
        {
            float from = _group.alpha;   // 교체면 지금 밝기에서 이어 간다(깜빡이지 않게)
            await FadeAsync(from, 1f, FadeInSec * (1f - from), rise: true, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(HoldSec), ignoreTimeScale: true, cancellationToken: ct);
            await FadeAsync(1f, 0f, FadeOutSec, rise: false, ct);
        }
        catch (OperationCanceledException) { }
        finally { if (seq == _fullSeq) _fullShowing = false; }
    }

    private async UniTaskVoid PlaySubtleAsync(string title, CancellationToken ct)
    {
        ApplyStyle(subtle: true);
        _title.text = title;
        _desc.gameObject.SetActive(false);
        try
        {
            float from = Mathf.Min(_group.alpha, SubtlePeak);
            await FadeAsync(from, SubtlePeak, SubtleFadeIn * (1f - from / SubtlePeak), rise: true, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(SubtleHold), ignoreTimeScale: true, cancellationToken: ct);
            await FadeAsync(SubtlePeak, 0f, SubtleFadeOut, rise: false, ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>처음 배너(큰 제목 · 금빛 선 · 짙은 받침) ↔ 도착 표시(작은 제목만 · 옅은 받침).</summary>
    private void ApplyStyle(bool subtle)
    {
        _title.fontSize         = subtle ? SubtleTitleSize : TitleSize;
        _title.characterSpacing = subtle ? 4f : 6f;
        _rule.gameObject.SetActive(!subtle);
        _back.color = subtle ? SubtleBackInk : BackInk;
    }

    private async UniTask FadeAsync(float from, float to, float dur, bool rise, CancellationToken ct)
    {
        if (dur > 0f)
        {
            for (float t = 0f; t < dur; )
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                _group.alpha = Mathf.Lerp(from, to, k);
                if (rise) _block.anchoredPosition = new Vector2(0f, -RiseY * (1f - k) * (1f - k));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        _group.alpha = to;
        if (rise) _block.anchoredPosition = Vector2.zero;
    }

    private void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>자기 캔버스 + 글자 뒤 어두운 받침 · 제목 · 금빛 가는 선 · 설명. 레이캐스터를 달지 않아 클릭을 받지 않는다.</summary>
    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha          = 0f;
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        var holder = new GameObject("Anchor", typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(transform, false);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 1f - TopRatio);
        holder.sizeDelta = new Vector2(1200f, 140f);

        _block = new GameObject("Block", typeof(RectTransform)).GetComponent<RectTransform>();
        _block.SetParent(holder, false);
        _block.anchorMin = Vector2.zero; _block.anchorMax = Vector2.one;
        _block.offsetMin = _block.offsetMax = Vector2.zero;

        // 글자 뒤 어두운 받침 — 카메라가 향한 목표(소환 빛기둥 · 무형검 제단 마법진)가 늘 화면 위 가운데라 흰 글자가 묻혔다(09-28 ae 실측).
        // 가운데는 고르게 어둡고 가장자리만 흐려지는 타원이라 판 모양이 드러나지 않는다(먼저 붙여 글자 뒤에 그린다).
        var back = new GameObject("Backing", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        back.rectTransform.SetParent(_block, false);
        back.rectTransform.anchorMin = back.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        back.rectTransform.anchoredPosition = new Vector2(0f, -6f);   // 제목(+26)과 설명(-40) 사이
        back.rectTransform.sizeDelta        = new Vector2(1100f, 260f);   // 부제 줄까지 고른 구간 안
        back.sprite = UI_RuneSelectPopup.MakeProcSprite("ZoneTitle_Backing", 128, 64, (u, v) =>
        {
            // 반지름 0.45까지는 1, 1에서 0 — Mathf.SmoothStep(a, b, t)는 a→b 보간이라 가장자리 함수로 못 쓴다(09-28 3차).
            float d = new Vector2((u - 0.5f) * 2f, (v - 0.5f) * 2f).magnitude;
            float t = Mathf.Clamp01((d - 0.45f) / 0.55f);
            return 1f - t * t * (3f - 2f * t);
        });
        back.color         = BackInk;
        back.raycastTarget = false;
        _back = back;

        _title = MakeText(_block, "Title", TitleSize, TitleInk, 26f, 70f);
        _title.characterSpacing = 6f;

        // 제목과 설명 사이의 금빛 가는 선 — 양끝이 흐려지는 부드러운 점을 가로로 늘린다.
        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        rule.rectTransform.SetParent(_block, false);
        rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rule.rectTransform.anchoredPosition = new Vector2(0f, -14f);
        rule.rectTransform.sizeDelta        = new Vector2(420f, 6f);
        rule.sprite        = UI_RuneSelectPopup.SoftDot;
        rule.color         = RuleInk;
        rule.raycastTarget = false;
        _rule = rule;

        _desc = MakeText(_block, "Desc", DescSize, DescInk, -40f, 34f);
    }

    private static TMP_Text MakeText(RectTransform parent, string name, float size, Color color, float y, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize         = size;
        t.fontStyle        = FontStyles.Normal;   // 기본 폰트가 이미 굵다 — 가짜 굵게 금지(글자 정본)
        t.color            = color;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 0.5f); rt.anchorMax = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta        = new Vector2(0f, height);
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }
}
