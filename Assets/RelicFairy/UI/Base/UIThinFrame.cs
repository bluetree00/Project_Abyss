using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>얇은 테 상태 — Idle(잉크 18%) · Active(금 55% + 눈금, 선택됨) · Ready(금 85% + 눈금, 지금 쓸 수 있음).</summary>
public enum ThinFrameState { Idle, Active, Ready }

/// <summary>
/// 톤 v2 얇은 테(10-07) — 1px 윤곽 + 모서리 눈금 + 켜짐 번쩍. 코드로 짓는다(<see cref="Create"/>).
/// Ready로 올라가는 순간 테가 시계 방향으로 그려지고(0.25초) 눈금이 돋고 안쪽이 한 번 번쩍인다(0.15초 · 30%).
/// 마름모(Q)는 Ready 동안 바깥 선이 숨쉰다(1.6초 · 28↔45%) — 화면에서 계속 움직이는 것은 이것 하나다.
/// 그 밖의 상태 바꿈은 0.18초 교차 페이드. 눌림(<see cref="PlayPress"/>) = 칸이 0.94배 → 1(0.14초).
/// 거절(<see cref="PlayReject"/>) = 재사용 대기 중에 눌렀다 — 칸이 좌우로 ±3px · 0.24초(10-08 A9, 「흔들림 없음」 원칙의 유일한 예외 · 「살짝」).
/// </summary>
public sealed class UIThinFrame : MonoBehaviour
{
    // ── Constants ──
    private const float FlashAlpha   = 0.30f;
    private const float FlashSec     = 0.15f;
    private const float TickOut      = 4f;      // 눈금은 테보다 사방 4px 바깥
    private const float OuterGap     = 8f;      // 마름모 바깥 선 간격
    private const float BreathMin    = 0.28f;
    private const float BreathMax    = 0.45f;
    private const float BreathPeriod = 1.6f;
    private const float PressScale   = 0.94f;
    private const float TickFrom     = 0.94f;   // 눈금이 돋는 출발 배율
    private const float ArtBreathMin = 0.70f;   // 장식 테 숨쉬기 바닥(테 자체가 숨쉰다)
    private const float RejectPx     = 3f;      // 거절 흔들림 폭(1080 기준 px)
    private const float RejectSec    = 0.24f;
    private const float RejectCycles = 3f;

    // ── Private ──
    private Image _outline;
    private Image _ticks;
    private Image _flash;
    private Image _outer;
    private ThinShape _shape;
    private ThinFrameState _state = ThinFrameState.Idle;
    private bool _art;          // 납품 장식 테(얇게 깎은 것)를 넘겨받은 모드 — 눈금 · 바깥 선 없음
    private bool _artBreathe;   // 그 테 자체가 Ready 동안 숨쉰다(Q)
    private bool _artKeepColor; // 그림 색을 그대로 둔다(유물칸 — 색이 컨셉) — 틴트는 흰색 밝기만
    private Sprite _idleArt;
    private Sprite _readyArt;
    private Vector3 _hostScale = Vector3.one;
    private CancellationTokenSource _cts;
    private float _rejectT0;
    private bool _rejecting;

    // ── Properties ──
    public ThinFrameState State => _state;

    // ── Lifecycle ──
    private void OnEnable()
    {
        // 꺼졌다 켜지면(HUD 숨김 등) 준비된 칸의 숨쉬기를 다시 건다.
        if (_state == ThinFrameState.Ready && Breathes && _cts == null && _outline != null) StartMotion(BreatheLoopAsync);
    }

    private void OnDisable()
    {
        CancelMotion();
        Apply(_state);   // 꺼지는 동안 연출이 멈춘 자리에 남지 않게
    }

    private void OnDestroy() => CancelMotion();

    // ── Public Methods ──

    /// <summary>host 위(마지막 형제)에 테를 짓는다. size = 테 크기(1080 기준 px), host 가운데 맞춤 · 레이아웃 무시.</summary>
    public static UIThinFrame Create(RectTransform host, ThinShape shape, Vector2 size)
    {
        var go = new GameObject("ThinFrame", typeof(RectTransform), typeof(LayoutElement));
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.SetParent(host, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        rt.SetAsLastSibling();

        var f = go.AddComponent<UIThinFrame>();
        f._shape     = shape;
        f._hostScale = host.localScale;
        f._flash     = f.Layer("Flash", UIThinShapes.Fill(shape, size.x, size.y), size);
        f._outline   = f.Layer("Outline", UIThinShapes.Outline(shape, size.x, size.y), size);
        f._outline.type          = Image.Type.Filled;
        f._outline.fillMethod    = Image.FillMethod.Radial360;
        f._outline.fillOrigin    = (int)Image.Origin360.Top;
        f._outline.fillClockwise = true;
        if (shape == ThinShape.Square)
        {
            var ts = size + Vector2.one * (TickOut * 2f);
            f._ticks = f.Layer("Ticks", UIThinShapes.Ticks(ts.x, ts.y), ts);
        }
        else
        {
            var os = size + Vector2.one * (OuterGap * 2f);
            f._outer = f.Layer("Outer", UIThinShapes.Outline(shape, os.x, os.y), os);
        }
        f.Apply(ThinFrameState.Idle);
        return f;
    }

    /// <summary>
    /// 이미 놓인 테 Image(납품 장식 테를 얇게 깎은 것)를 넘겨받아 같은 상태 · 연출을 건다 — 사용자 「테두리도 조금은 사용하되 얇게」(10-08).
    /// 그림이 모서리 장식을 가져 눈금은 없다. Ready = readyArt(없으면 idleArt)로 바꿔 시계 방향으로 그려짐 · 색은 무채색 그림에 틴트.
    /// <paramref name="breathe"/>면 Ready 동안 그 테가 숨쉰다(Q 유물칸). 테 Image의 크기 · 자리는 그대로 둔다.
    /// </summary>
    public static UIThinFrame Adopt(Image frame, Sprite idleArt, Sprite readyArt, bool breathe, bool keepColor = false)
    {
        if (frame == null || idleArt == null) return null;
        if (!frame.TryGetComponent<UIThinFrame>(out var f)) f = frame.gameObject.AddComponent<UIThinFrame>();
        f._art        = true;
        f._artBreathe = breathe;
        f._artKeepColor = keepColor;
        f._idleArt    = idleArt;
        f._readyArt   = readyArt != null ? readyArt : idleArt;
        f._hostScale  = frame.transform.parent != null ? frame.transform.parent.localScale : Vector3.one;
        f._outline    = frame;
        frame.raycastTarget = false;
        frame.type          = Image.Type.Filled;
        frame.fillMethod    = Image.FillMethod.Radial360;
        frame.fillOrigin    = (int)Image.Origin360.Top;
        frame.fillClockwise = true;
        f.Apply(ThinFrameState.Idle);
        return f;
    }

    /// <summary>상태를 바꾼다. animate면 Ready로 오를 때 켜짐 연출, 그 밖엔 0.18초 교차 페이드.</summary>
    public void SetState(ThinFrameState next, bool animate)
    {
        if (next == _state) return;
        var prev = _state;
        _state = next;
        CancelMotion();
        if (!animate || !isActiveAndEnabled)
        {
            Apply(next);
            if (next == ThinFrameState.Ready && Breathes && isActiveAndEnabled) StartMotion(BreatheLoopAsync);
            return;
        }
        if (next == ThinFrameState.Ready && prev != ThinFrameState.Ready) StartMotion(ReadyAsync);
        else                                                               StartMotion(CrossfadeAsync);
    }

    /// <summary>그림을 바꾼 뒤(<see cref="Adopt"/>를 다시 부른 뒤) 지금 상태를 연출 없이 다시 칠한다 — Ready면 숨쉬기도 다시.</summary>
    public void Reapply()
    {
        CancelMotion();
        Apply(_state);
        if (_state == ThinFrameState.Ready && Breathes && isActiveAndEnabled) StartMotion(BreatheLoopAsync);
    }

    /// <summary>눌림 — 칸(host)이 0.94배로 줄었다가 0.14초에 돌아온다(스킬을 쓴 순간).</summary>
    public void PlayPress()
    {
        if (!isActiveAndEnabled) return;
        PressAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>거절 — 칸(host)이 좌우로 살짝 흔들리고 멎는다. 흔들리는 중에 또 누르면 처음부터(자리는 처음 것 그대로).</summary>
    public void PlayReject()
    {
        if (!isActiveAndEnabled) return;
        _rejectT0 = Time.unscaledTime;
        if (_rejecting) return;
        RejectAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── Private Methods ──

    private Image Layer(string name, Sprite sprite, Vector2 size)
    {
        var img = ShopUIStyle.MakeImage(transform, name, Color.clear);
        img.sprite = sprite;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        return img;
    }

    private void StartMotion(Func<CancellationToken, UniTask> body)
    {
        _cts = new CancellationTokenSource();
        RunMotionAsync(body, _cts.Token).Forget();
    }

    private static async UniTaskVoid RunMotionAsync(Func<CancellationToken, UniTask> body, CancellationToken ct)
    {
        try { await body(ct); }
        catch (OperationCanceledException) { }
    }

    private void CancelMotion()
    {
        if (_cts == null) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    private bool Breathes => _outer != null || _artBreathe;

    private void Targets(ThinFrameState s, out Color outline, out Color ticks, out Color outer)
    {
        if (_art)
        {
            // 얇게 깎은 장식 테 — 1px 직선 테보다 그림이 성기므로 한 단 밝게. 색을 살리는 그림(유물칸)은 흰 틴트로 밝기만.
            if (_artKeepColor)
            {
                outline = s switch
                {
                    ThinFrameState.Ready  => Color.white,
                    ThinFrameState.Active => new Color(1f, 1f, 1f, 0.85f),
                    _                     => new Color(1f, 1f, 1f, 0.6f),
                };
                ticks = Color.clear;
                outer = Color.clear;
                return;
            }
            outline = s switch
            {
                ThinFrameState.Ready  => UITheme.Thin.WithA(UITheme.Thin.Gold, 0.95f),
                ThinFrameState.Active => UITheme.Thin.WithA(UITheme.Thin.Gold, 0.75f),
                _                     => UITheme.Thin.InkA(0.55f),
            };
            ticks = Color.clear;
            outer = Color.clear;
            return;
        }
        outline = s switch
        {
            ThinFrameState.Ready  => UITheme.Thin.LineReady,
            ThinFrameState.Active => UITheme.Thin.LineActive,
            _                     => UITheme.Thin.LineIdle,
        };
        ticks = s == ThinFrameState.Idle ? Color.clear : UITheme.Thin.TickGold;
        outer = s == ThinFrameState.Ready ? UITheme.Thin.WithA(UITheme.Thin.Gold, BreathMin) : Color.clear;
    }

    private void Apply(ThinFrameState s)
    {
        if (_outline == null) return;
        Targets(s, out var o, out var t, out var u);
        if (_art) _outline.sprite = s == ThinFrameState.Ready ? _readyArt : _idleArt;
        _outline.fillAmount = 1f;
        _outline.color = o;
        if (_ticks != null) { _ticks.color = t; _ticks.rectTransform.localScale = Vector3.one; }
        if (_outer != null) _outer.color = u;
        if (_flash != null) _flash.color = Color.clear;
    }

    private async UniTask ReadyAsync(CancellationToken ct)
    {
        if (_art) _outline.sprite = _readyArt;
        Targets(ThinFrameState.Ready, out var readyColor, out _, out _);
        _outline.color = readyColor;
        _outline.fillAmount = 0f;
        if (_ticks != null) { _ticks.color = Color.clear; _ticks.rectTransform.localScale = Vector3.one * TickFrom; }
        if (_outer != null) _outer.color = Color.clear;

        await UIMotion.Tween(UIMotion.Trace, k =>
        {
            _outline.fillAmount = k;
            // 장식 테는 안쪽 번쩍 층이 없다 — 테 자체가 흰빛으로 그려져 금빛으로 가라앉는다(준비 반짝임, 10-08 A9).
            if (_flash == null) _outline.color = Color.Lerp(new Color(1f, 1f, 1f, readyColor.a), readyColor, k);
            float flash = 1f - Mathf.Clamp01(k * UIMotion.Trace / FlashSec);
            if (_flash != null) _flash.color = UITheme.Thin.WithA(UITheme.Thin.Gold, FlashAlpha * flash);
            if (_ticks != null)
            {
                _ticks.color = UITheme.Thin.WithA(UITheme.Thin.Gold, UITheme.Thin.TickGold.a * k);
                _ticks.rectTransform.localScale = Vector3.one * Mathf.Lerp(TickFrom, 1f, k);
            }
        }, ct);
        Apply(ThinFrameState.Ready);
        if (Breathes) await BreatheLoopAsync(ct);
    }

    private async UniTask CrossfadeAsync(CancellationToken ct)
    {
        Color o0 = _outline.color;
        Color t0 = _ticks != null ? _ticks.color : Color.clear;
        Color u0 = _outer != null ? _outer.color : Color.clear;
        Targets(_state, out var o1, out var t1, out var u1);
        if (_art) _outline.sprite = _state == ThinFrameState.Ready ? _readyArt : _idleArt;   // 그림은 바로 바꾸고 색만 옮긴다
        _outline.fillAmount = 1f;
        if (_flash != null) _flash.color = Color.clear;
        await UIMotion.Tween(UIMotion.Swap, k =>
        {
            _outline.color = Color.Lerp(o0, o1, k);
            if (_ticks != null) _ticks.color = Color.Lerp(t0, t1, k);
            if (_outer != null) _outer.color = Color.Lerp(u0, u1, k);
        }, ct);
        Apply(_state);
        if (_state == ThinFrameState.Ready && Breathes) await BreatheLoopAsync(ct);
    }

    private UniTask BreatheLoopAsync(CancellationToken ct)
        => _outer != null
            ? UIMotion.Breathe(BreathPeriod, k => _outer.color = UITheme.Thin.WithA(UITheme.Thin.Gold, Mathf.Lerp(BreathMin, BreathMax, k)), ct)
            : UIMotion.Breathe(BreathPeriod, k =>
                  {
                      var baseColor = _artKeepColor ? Color.white : UITheme.Thin.Gold;
                      _outline.color = UITheme.Thin.WithA(baseColor, Mathf.Lerp(ArtBreathMin, _artKeepColor ? 1f : 0.95f, k));
                  }, ct);

    private async UniTaskVoid PressAsync(CancellationToken ct)
    {
        var host = transform.parent as RectTransform;
        if (host == null) return;
        Vector3 baseScale = _hostScale;
        try
        {
            await UIMotion.Tween(UIMotion.Quick, k => host.localScale = baseScale * Mathf.Lerp(PressScale, 1f, k), ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (host != null) host.localScale = baseScale;
        }
    }

    private async UniTaskVoid RejectAsync(CancellationToken ct)
    {
        var host = transform.parent as RectTransform;
        if (host == null) return;
        _rejecting = true;
        Vector2 basePos = host.anchoredPosition;
        try
        {
            while (true)
            {
                float k = (Time.unscaledTime - _rejectT0) / RejectSec;
                if (k >= 1f) break;
                float dx = RejectPx * (1f - k) * Mathf.Sin(k * RejectCycles * Mathf.PI * 2f);
                host.anchoredPosition = basePos + new Vector2(dx, 0f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (host != null) host.anchoredPosition = basePos;
            _rejecting = false;
        }
    }
}
