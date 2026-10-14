using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정제소 뽑기 연출 v3 — 원신 기원식 「아발론의 밤」 시간표(설계 정제소·재련소_v3 1-1, 10-14). 결과 · 등급은 <see cref="RefineryService"/>가
/// 이미 정했다 — 여기는 표시만. 무대(전체 화면 캔버스 · 별 · 빛살 · 공개 카드)는 <c>UI_RefineryPanel.Wish.cs</c>가 짓는다.
///
/// <para>타임라인 v5(실시간 — 시간정지 팝업이라 unscaled · 설계 정제소_v5_긴장감 — 사용자 10-14 「짧아서 하찮고 긴장감이 없어」).
/// v4는 낙성 1.1초 · 0.9초에 등급 확정이라 기다릴 틈이 없었다. 원신 · 명조처럼 <b>「무엇이 올까」 구간</b>을 몸통으로 둔다:
/// 0~0.3 누름(글 · 열이 스미고 제단만 남음) · 0.2~1.6 충전(원석이 떠오르며 맥박이 잦아지고 보석 궤도가 빨라짐) ·
/// 1.6~1.9 방출(원석이 빛으로 터져 빛줄기가 하늘로 · 제단은 표적으로 옅게 남음) · 1.9~2.2 정적 ·
/// 2.2~4.4 낙성(먼 은백 점이 커지며 다가옴 — <b>고비 2.7 Rare · 3.25 Epic · 3.8 Legend에서 그 등급까지만 오른다</b>, 위로만) ·
/// 4.4 착지(빛무리 · 고리 두 겹 · 섬광 2.5% · <b>나온 룬의 속성 보석이 맨 위에서 멎음</b>) ·
/// 4.6~5.3 실루엣(흰 테가 숨 쉼 · 전설은 금빛 티끌이 모여듦) · 5.3~5.8 채색 · 이름 · ◆ ·
/// 입력 열림 희귀 6.1 · 영웅 6.4 · 전설 7.4(전설만 6.1~ 금빛 빛살 · 금 고리 · 불티 · 낮은 종 · 이름 금빛).</para>
///
/// <para>건너뛰기: 0.3초 뒤 아무 입력(클릭 · Space · Esc · 패드)이면 0.15초 안에 끝 모습까지 감아 간다.
/// 착지 섬광(0.2초)은 건너뛰어도 한 번은 그대로 보인다(결과를 놓치지 않게).</para>
///
/// <para>모든 모습은 시각 t 하나로 정한다(<see cref="ApplyFrame"/>) — 건너뛰기는 t를 끝까지 감을 뿐이고,
/// 끝 상태에서 [다시 정제]를 누르면 같은 시간표를 처음부터(제단 묶음 없이) 다시 돈다.</para>
/// </summary>
public sealed partial class UI_RefineryPanel
{
    // ── 타임라인(초) — v5 설계 2장 표 ──
    private const float TPress    = 0.15f;   // [정제하기] 눌림 0.94 → 1
    private const float TEdge     = 0.40f;   // 0~0.40 가장자리가 제단 화면 값에서 끝까지 짙어짐
    private const float TIdleOut  = 0.30f;   // 0~0.30 글 · 열 · 버튼이 스민다(제단은 남는다)
    private const float TCharge   = 0.20f;   // 충전 — 원석이 제단 위로 떠오르며 맥박이 잦아진다
    private const float TRelease  = 1.60f;   // 방출 — 원석이 빛으로 터져 빛줄기가 하늘로
    private const float TBurstEnd = 1.90f;   // 1.9~2.2 정적
    private const float TFall     = 2.20f;   // 낙성 시작 — 먼 은백 점
    private const float TLand     = 4.40f;   // 착지 — 속성 보석이 멎는다
    private const float TReveal   = 4.60f;   // 빛살 · 실루엣
    private const float TSilEnd   = 5.30f;   // 채색 시작(실루엣 0.7초 — 「누구지?」)
    private const float TColEnd   = 5.60f;   // 이름 · 효과 시작
    private const float TTextEnd  = 5.80f;
    private const float TGem      = 5.80f;   // 첫 ◆
    private const float TLegend   = 6.10f;   // 전설만 — 금빛 빛살 · 금 고리 · 불티 · 종 · 이름 금빛
    private const float EndRare   = 6.10f;   // 입력 열림(희귀 6.1 · 영웅 6.4 · 전설 7.4)
    private const float EndEpic   = 6.40f;
    private const float EndLegend = 7.40f;
    private const float AltarDimA = 0.35f;   // 낙성 동안 제단 — 착지 표적으로 옅게 남는다
    private const float AltarOutFrom = 4.90f, AltarOutTo = 5.40f;   // 공개가 자리 잡으면 제단이 걷힌다
    private const float OreLift   = 120f;    // 충전 때 원석이 떠오르는 높이
    private const float SpinTurns = 4f;      // 보석 룰렛 — 착지까지 도는 바퀴(+ 멎을 자리까지 나머지)
    private const float GemSpinPow = 0.7f;   // 방출 즈음 가장 빠르고 착지로 갈수록 느려진다
    private const float GemStep   = 0.12f;   // ◆ 간격
    private const float GemPop    = 0.15f;
    private const float TwinLag   = 0.10f;   // 두 장이면 높은 쪽이 늦게 뒤집힌다(마지막 강조)
    private const float SwapDur   = 0.06f;   // 고비마다 색 전환
    private const float PopDur    = 0.18f;   // 색이 오른 직후 별 머리가 한 번 부푼다
    private const float PingDur   = 0.35f;
    private const float BeatDur   = 0.18f;   // 충전 맥박 하나
    private const float ConvergeDur = 0.70f; // 전설 — 실루엣 동안 금빛 티끌이 모여든다(TReveal ~ TSilEnd)
    private const float StarFade  = 0.10f;
    private const float FlashDur  = 0.20f;
    private const float BurstDur  = 0.40f;
    private const float ShockDur  = 0.50f;
    private const float Shock2Lag = 0.10f;   // 둘째 고리 — 0.1초 늦게 작게
    private const float GoldRingDur = 0.60f; // 전설 금 고리
    private const float SparkDur  = 1.20f;   // 전설 금빛 불티
    private const float SkipAfter = 0.30f;
    private const float SkipDur   = 0.15f;

    // ── 모습 ──
    private const float IdleGemA  = 0.45f;   // 대기 보석 — 누를 수 없는 장식이 강조 1순위였다(C §2)
    private const float IdleGemS  = 0.85f;
    // v4 — UI는 선형 색공간에서 섞여 낮은 알파도 화면에선 훨씬 밝다: 섬광 22%도 전설이면 화면 전체가 금빛 판이었다(10-14 사진 L_1.86).
    // 섬광 · 고리 · 빛살을 그만큼 낮춘다(눈에 보이는 세기는 v3의 절반 아래). 거의 검은 밤하늘 위라 7%도 화면 전체가 올리브 빛이었다(2차 사진 L_1.86) —
    // 화면 전체 섬광은 2.5%(옅은 따뜻한 번짐)만 남기고 「쿵」은 가운데 빛무리 · 고리가 맡는다.
    private const float FlashA    = 0.025f;
    private const float BurstA    = 0.55f;
    private const float BurstTo   = 950f;
    private const float ShockA    = 0.55f;
    private const float Shock2A   = 0.35f;
    private const float RaysA     = 0.26f;
    private const float GoldRaysA = 0.32f;
    private const float GlowA     = 0.45f;
    private const float RaysSpin  = 12f;     // 초당 도 — 전설 금빛 빛살, 천천히
    private const float RaysDrift = 4f;      // 초당 도 — 등급 색 빛살(모든 등급, 금빛과 반대로)
    private const float StarPow   = 1.8f;    // 낙하 가속(위치 = u^1.8) — 멀리서 천천히, 가까이 빠르게
    private const int   HaloHueGap = 24;     // 청록 후광 픽셀 판정 — G · B가 R보다 이만큼 크면 후광

    private static readonly Color Silver = new(0.86f, 0.90f, 1f, 1f);

    /// <summary>오르는 고비 — k번째(1 Rare · 2 Epic · 3 Legend)가 되는 시각. 나온 등급까지만 오르고 거기서 멈춘다(내려가는 가짜 없음).</summary>
    private static readonly float[] Checkpoints = { 2.70f, 3.25f, 3.80f };
    /// <summary>충전 맥박 — 점점 잦아진다.</summary>
    private static readonly float[] Beats = { 0.55f, 0.95f, 1.25f, 1.45f };

    // ── Static ──
    private static Sprite s_frameNoHalo, s_frameNoHaloSrc;

    // ── Private ──
    private GachaPlan _plan;
    private bool    _skipRequested;     // 실측 도구가 「아무 입력」을 대신 넣는 자리
    private float   _gachaT0 = -1f, _gachaEndAt = -1f, _flashAt = -1f, _inputOpenAt = -1f;   // 실시간 기록(실측 도구가 읽는다)
    private CancellationTokenSource _ambientCts;

    /// <summary>한 번 돌리기의 그림 계획 — 등급 · 색 · 두 장 · 소리 단계.</summary>
    private struct GachaPlan
    {
        public int   Tier;              // 낙성 색 등급 = 두 장이면 높은 쪽(1 Rare · 2 Epic · 3 Legend)
        public int   MainTier, AltTier; // 공개 카드 등급(Main = 높은 쪽 · 오른쪽)
        public float End;               // 입력이 열리는 시각
        public Color Grade;
        public bool  Twin, Replay;      // Replay = 끝 상태에서 [다시 정제] — 제단 묶음이 이미 걷혀 있다
        public RuntimeItemData Rune, Alt;
        public int   Lit;               // 울린 ◆ 차임 수
        public int   Cue;               // 울린 소리 단계
        public int   Beat, Up;          // 울린 맥박 수 · 오른 고비 수
        public int   ElemIdx;           // 나온 룬(높은 쪽) 속성의 ElementDef.Order 순번 — 보석 룰렛이 멎을 자리(-1 = 없음)
        public float SpinFrom, SpinTotal;   // 보석 룰렛 — 출발 각(제단 화면에서 돌던 자리) · 착지까지 돌 양
    }

    // ── 대기 ──

    /// <summary>
    /// 대기 화면에 필요한 지연 요소 — 결과 틀(후광 걷은 사본, C §2 「결과 틀 후광 0」).
    /// 결과 틀 아트에 구워진 청록 후광 · 바깥 선을 걷는다.
    /// </summary>
    private void EnsureIdleParts()
    {
        if (_altarRing == null) return;
        // 플레이 중 재굽기가 런타임 사본(저장 안 됨)을 담아 가면 프리팹 테가 빈칸이 된다 — 그땐 스킨 원본에서 다시 만든다.
        var src = _altarRing.sprite != null ? _altarRing.sprite : UISkin.Refinery?.slotFrame;
        var noHalo = FrameWithoutHalo(src);
        if (noHalo != null) _altarRing.sprite = noHalo;
    }

    /// <summary>
    /// 대기 화면 — v4 전체 화면 제단(<see cref="ShowIdleStage"/>) + 잔잔한 움직임. 숨긴 옛 창의 값도 그대로 맞춰 둔다.
    /// 열 때 · 공개를 닫을 때 부른다(실측 도구는 촬영 사이에도).
    /// </summary>
    private void ApplyIdleLook()
    {
        _plan = default;
        _pendingRune = null;
        StopAmbient();
        ShowIdleStage();
        for (int i = 0; i < _gems.Count; i++)
        {
            var j = _gems[i].jewel;
            if (j == null) continue;
            j.transform.localScale = Vector3.one * IdleGemS;
            var c = j.color;
            j.color = new Color(c.r, c.g, c.b, IdleGemA);
        }
        if (_revealFlash != null) _revealFlash.color = Color.clear;
        if (_revealRays  != null) _revealRays.gameObject.SetActive(false);
        if (_resultBox   != null) _resultBox.gameObject.SetActive(false);
        if (_spinBtnImg  != null) _spinBtnImg.transform.localScale = Vector3.one;
        if (_hint != null) _hint.text = IdleHint;
        if (_iHint != null) _iHint.text = "";   // 제단 화면 아래 줄은 거절 사유만(설명은 머리 줄 · 오른쪽 열)
        RefreshInfo();
        Refresh();
        StartAmbient(0f);   // 별 반짝임 · 제단이 도는 몫
    }

    // ── 뽑기 ──

    /// <summary>
    /// 뽑기 연출 한 번. outcome의 등급 · 룬으로 그린다(두 장이면 Rune = 높은 쪽 · Alt = 다른 쪽, 낙성 색은 높은 쪽).
    /// 끝 모습(공개 카드 · ◆ · 전설 빛살)을 남기고 돌아온다 — 입력을 여는 것은 부르는 쪽(<see cref="EnterEndState"/>).
    /// 실측 도구는 서비스를 거치지 않고 등급을 넣은 outcome으로 이것만 부른다(게임 로직 · 원석 · 보관함 무관).
    /// </summary>
    private async UniTask PlayGachaAsync(RefineryOutcome outcome, CancellationToken ct)
    {
        EnsureWish();
        StopAmbient();
        _plan = MakePlan(outcome, _wIdle.alpha < 0.5f);   // 제단 묶음이 걷혀 있으면 끝 상태에서 [다시 정제]
        // 보석 룰렛 — 제단 화면에서 돌던 각에서 이어 돌아 착지 순간 나온 룬 속성 보석이 맨 위(90°)에 선다.
        _plan.SpinFrom  = AltarSpin * Time.unscaledTime;
        float toTop     = _plan.ElemIdx >= 0 ? 90f - GemAngles[_plan.ElemIdx] - _plan.SpinFrom : 0f;
        _plan.SpinTotal = Mathf.Repeat(toTop, 360f) + 360f * SpinTurns;
        PrepareWish();

        _skipRequested = false;
        _gachaEndAt    = -1f;
        _flashAt       = -1f;
        _inputOpenAt   = -1f;
        float t0 = Time.unscaledTime;
        _gachaT0 = t0;
        float skipAt = -1f, skipFrom = 0f;
        ApplyFrame(0f);

        while (true)
        {
            float now = Time.unscaledTime;
            float t   = now - t0;
            // 건너뛰기 — 0.3초 뒤 아무 입력. [정제하기]를 누른 그 클릭은 0.3초 문턱이 거른다.
            if (skipAt < 0f && t >= SkipAfter && t < _plan.End && (_skipRequested || Input.anyKeyDown))
            {
                skipAt   = now;
                skipFrom = t;
            }
            if (skipAt >= 0f) t = Mathf.Lerp(skipFrom, _plan.End, (now - skipAt) / SkipDur);
            t = Mathf.Min(t, _plan.End);

            // 착지 섬광은 실시간 0.2초 — 감아 가는 중에 착지를 지나도 한 번은 그대로 보인다.
            if (t >= TLand && _flashAt < 0f) _flashAt = now;
            ApplyFrame(t);
            ApplyFlash(_flashAt < 0f ? -1f : now - _flashAt);
            FireCues(t, skipAt >= 0f);

            if (t >= _plan.End)
            {
                if (_gachaEndAt < 0f) _gachaEndAt = now;
                if (now - _flashAt >= FlashDur) break;
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        ApplyFlash(-1f);
        StartAmbient(_plan.End);   // 끝 상태에서도 별이 반짝이고 전설 빛살이 계속 돈다
    }

    private static float EndOf(int tier) => tier >= 3 ? EndLegend : tier == 2 ? EndEpic : EndRare;

    private static GachaPlan MakePlan(RefineryOutcome o, bool replay)
    {
        var rune = o.Rune;
        var alt  = o.Alt;
        int main = Mathf.Clamp(RarityTier(o.Rarity), 1, 3);
        int other = alt != null ? Mathf.Clamp(RarityTier(alt.rarity), 1, 3) : 0;
        // 높은 쪽이 Main(오른쪽 · 늦게 뒤집힘) — 서비스는 좋은 쪽을 Rune으로 주지만 순서를 믿지 않는다.
        if (other > main) { (rune, alt) = (alt, rune); (main, other) = (other, main); }
        return new GachaPlan
        {
            Tier     = main,
            MainTier = main,
            AltTier  = other,
            End      = EndOf(main),
            Grade    = GradeColor(main),
            Twin     = alt != null,
            Replay   = replay,
            Rune     = rune,
            Alt      = alt,
            ElemIdx  = ElementIndex(rune),
        };
    }

    private static int ElementIndex(RuntimeItemData rune)
    {
        if (rune == null || string.IsNullOrEmpty(rune.element)) return -1;
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++) if (order[i] == rune.element) return i;
        return -1;
    }

    /// <summary>시각 t의 모습 — 모든 요소를 t 하나로 정한다(건너뛰기는 t를 끝까지 감을 뿐).</summary>
    private void ApplyFrame(float t)
    {
        // 누름 — [정제하기]가 0.94 → 1(0.15초)하며 제단 묶음이 스민다. [다시 정제]는 끝 버튼이 바로 걷히므로 없음.
        if (!_plan.Replay && _iSpin != null)
            _iSpin.transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, UIMotion.EaseOut(t / TPress));
        _wIdle.alpha = _plan.Replay ? 0f : 1f - UIMotion.EaseOut(Mathf.InverseLerp(0f, TIdleOut, t));

        // 누름 — 가장자리가 제단 화면 값에서 끝까지 짙어진다(1.15배에서 제자리로 좁혀 오며)
        float edge = _plan.Replay ? 1f : UIMotion.EaseOut(Mathf.InverseLerp(0f, TEdge, t));
        SetA(_wEdge, Color.black, Mathf.Lerp(EdgeIdleA, 1f, edge));
        _wEdge.rectTransform.localScale = Vector3.one * Mathf.Lerp(EdgeIdleS, 1f, edge);

        // 「아발론의 밤」은 제단 화면부터 덮여 있다(v4)
        _wNight.alpha = 1f;
        ApplyAmbient(t);

        ApplyAltar(t);
        ApplyOre(t);
        ApplyStar(t);
        ApplyLanding(t);

        // 공개 — 등급 색 빛살 배경(전설은 금빛 빛살이 한 겹 더 · 천천히 돈다)
        SetA(_wRays, _plan.Grade, RaysA * UIMotion.EaseOut(Mathf.InverseLerp(TReveal, TReveal + 0.3f, t)));
        if (_plan.Tier >= 3)
            SetA(_wRaysGold, UITheme.Thin.Gold, GoldRaysA * UIMotion.EaseOut(Mathf.InverseLerp(TLegend, TLegend + 0.6f, t)));
        else SetA(_wRaysGold, UITheme.Thin.Gold, 0f);

        ApplyLegend(t);

        ApplyCard(_wMain, t, _plan.Twin ? TwinLag : 0f);
        if (_plan.Twin) ApplyCard(_wAlt, t, 0f);

        // 건너뛰기 안내(오른쪽 위) — 0.3초부터 끝 직전까지. 두 장 안내는 이름과 함께 떠오른다.
        float skip = Mathf.InverseLerp(SkipAfter, SkipAfter + 0.15f, t) * (1f - Mathf.InverseLerp(_plan.End - 0.2f, _plan.End, t));
        _wSkip.alpha = skip;
        if (_plan.Twin) _wCaption.alpha = Mathf.InverseLerp(TColEnd, TTextEnd, t);
    }

    /// <summary>별 반짝임 · 빛살 회전(등급 색은 천천히 · 전설 금빛은 반대로) · 제단 화면 움직임 — 끝 상태 · 제단 화면에서도 계속 도는 몫
    /// (<see cref="AmbientAsync"/>가 t를 이어 준다).</summary>
    private void ApplyAmbient(float t)
    {
        if (_wStars != null)      _wStars.color      = UITheme.Thin.WithA(StarTint, 0.85f + 0.15f * Mathf.Sin(t * 1.9f));
        if (_wStarsFaint != null) _wStarsFaint.color = UITheme.Thin.WithA(StarTint, 0.55f + 0.25f * Mathf.Sin(t * 1.3f + 1.7f));
        if (_wRays != null && _plan.Tier > 0)
            _wRays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, RaysDrift * Mathf.Max(0f, t - TReveal));
        if (_wRaysGold != null && _plan.Tier >= 3)
            _wRaysGold.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -RaysSpin * Mathf.Max(0f, t - TLegend));
        ApplyIdleMotion(Time.unscaledTime);
    }

    /// <summary>
    /// 제단 — 충전 때 밝아지고(빛무리 · 빛살 · 안 고리가 맥박마다) 보석 여섯이 빨라졌다가 낙성 동안 느려져 착지 순간 나온 룬의 속성 보석이
    /// 맨 위에서 멎는다(룰렛 — 등급은 별이, 속성은 보석이 알려 주는 두 겹의 「무엇이 올까」). 방출 뒤엔 착지 표적으로 옅게 남고,
    /// 착지 때 한 번 밝아졌다가 공개가 자리 잡으면 걷힌다. [다시 정제]는 0.25초에 다시 나타난다.
    /// </summary>
    private void ApplyAltar(float t)
    {
        if (_wAltar == null) return;
        float a = _plan.Replay ? UIMotion.EaseOut(Mathf.InverseLerp(0f, 0.25f, t)) : 1f;
        a *= Mathf.Lerp(1f, AltarDimA, UIMotion.EaseOut(Mathf.InverseLerp(TRelease, TBurstEnd, t)));
        a  = Mathf.Lerp(a, 0.85f, Mathf.InverseLerp(TLand, TLand + 0.1f, t));
        a *= 1f - UIMotion.EaseOut(Mathf.InverseLerp(AltarOutFrom, AltarOutTo, t));
        _wAltar.alpha = a;

        float charge = Mathf.InverseLerp(TCharge, TRelease, t) * (1f - Mathf.InverseLerp(TRelease, TBurstEnd, t));
        float beat   = BeatPulse(t);
        _iHalo.color   = UITheme.Thin.WithA(AltarHaloC, 0.20f + 0.15f * charge + 0.12f * beat);
        _iRays.color   = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.10f + 0.08f * charge + 0.06f * beat);
        _iRingIn.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.30f + 0.35f * charge + 0.25f * beat);

        float sweep = GemSweep(t);
        _iRays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, sweep * 0.15f);
        _iDash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -sweep * 0.35f);

        float stop = Mathf.InverseLerp(TLand, TLand + 0.15f, t);
        for (int i = 0; i < _iGems.Length; i++)
        {
            float ang = (GemAngles[i < GemAngles.Length ? i : 0] + sweep) * Mathf.Deg2Rad;
            _iGems[i].anchoredPosition = new Vector2(OrbitR * Mathf.Cos(ang), AltarY + OrbitR * Mathf.Sin(ang));
            bool chosen = i == _plan.ElemIdx;
            float s = chosen ? 1f + 0.4f * UIMotion.EaseOut(stop) : 1f;
            _iGems[i].localScale = new Vector3(s, s, 1f);
            var c = _iGemBase[i];
            _iGemImgs[i].color = UITheme.Thin.WithA(c, c.a * (chosen ? 1f : Mathf.Lerp(1f, 0.4f, stop)));
        }
        if (_plan.ElemIdx >= 0)
            SetA(_iGemGlow, ElementDef.IdColor(ElementDef.Order[_plan.ElemIdx], UITheme.Thin.Ink), 0.55f * stop);
        else SetA(_iGemGlow, Color.white, 0f);
    }

    /// <summary>보석 룰렛 각 — 출발 각(SpinFrom)에서 착지(TLand)에 정확히 SpinTotal만큼 돈다. 속도 0에서 출발해 방출 즈음 가장 빠르고 0으로 멎는다.</summary>
    private float GemSweep(float t)
    {
        float u = Mathf.Clamp01(t / TLand);
        float w = Mathf.Pow(u, GemSpinPow);
        return _plan.SpinFrom + _plan.SpinTotal * (w * w * (3f - 2f * w));
    }

    /// <summary>충전 맥박 — 맥박 시각마다 1 → 0(0.18초).</summary>
    private static float BeatPulse(float t)
    {
        float p = 0f;
        for (int i = 0; i < Beats.Length; i++)
        {
            float k = (t - Beats[i]) / BeatDur;
            if (k >= 0f && k < 1f) p = Mathf.Max(p, (1f - k) * (1f - k));
        }
        return p;
    }

    /// <summary>
    /// 충전 · 방출 — 원석이 제단 가운데(제단 화면에 떠 있던 자리 · 크기)에서 120px 떠오르며 맥박마다 부풀고(점점 잦아짐),
    /// 1.6초에 빛으로 터져 가는 빛줄기가 하늘로 솟는다(바친 것이 하늘로 간다 — 별은 그 답으로 내려온다).
    /// 처음부터 보인다(제단 쪽 원석을 이어받는다 — <see cref="PrepareWish"/>). [다시 정제]는 0.1초에 나타난다.
    /// </summary>
    private void ApplyOre(float t)
    {
        float fadeIn = _plan.Replay ? Mathf.InverseLerp(0f, 0.1f, t) : 1f;
        float lift   = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TCharge, TRelease, t));
        float beat   = BeatPulse(t);
        float burst  = Mathf.InverseLerp(TRelease, TBurstEnd, t);
        var   p      = new Vector2(LandX, OreFromY + OreLift * lift);
        if (t < TBurstEnd)
        {
            float s = Mathf.Lerp(1f, 0.8f, lift) * (1f + 0.12f * beat) * Mathf.Lerp(1f, 1.7f, UIMotion.EaseOut(burst));
            _wOre.rectTransform.anchoredPosition     = p;
            _wOre.rectTransform.localScale           = Vector3.one * s;
            _wOreGlow.rectTransform.anchoredPosition = p;
            _wOreGlow.rectTransform.localScale       = Vector3.one * (1f + 0.35f * beat + 1.6f * UIMotion.EaseOut(burst));
            SetA(_wOre, Silver, _wOre.sprite != null ? fadeIn * (1f - burst) : 0f);   // 아이콘이 없으면(스킨 미로드) 빛무리만
            SetA(_wOreGlow, Silver, fadeIn * (0.15f + 0.15f * lift + 0.15f * beat) * (1f - burst * burst));   // 0.7은 흰 덩어리였다(R_0.20)
        }
        else { SetA(_wOre, Silver, 0f); SetA(_wOreGlow, Silver, 0f); }

        // 빛줄기 — 방출 순간 원석 자리에서 화면 위로(0.25초에 뻗고 0.35초에 진다)
        float db = t - TRelease;
        if (db >= 0f && db < 0.6f)
        {
            float fade = Mathf.InverseLerp(0.25f, 0.6f, db);
            var br = _wBeam.rectTransform;
            br.anchoredPosition = new Vector2(LandX, OreFromY + OreLift);
            br.sizeDelta        = new Vector2(BeamW * (1f - 0.5f * fade), BeamLen * UIMotion.EaseOut(Mathf.InverseLerp(0f, 0.25f, db)));
            SetA(_wBeam, Silver, 0.8f * (1f - fade));
        }
        else SetA(_wBeam, Silver, 0f);
    }

    /// <summary>
    /// 낙성 — 오른쪽 위 먼 은백 점이 커지며 가속해 제단(착지 = 공개 자리)으로 떨어진다. 꼬리 · 머리 · 빛무리 색 = <see cref="TailColor"/>,
    /// 고비마다(나온 등급까지만) 한 단계 오르며 머리가 부풀고 고리가 퍼지고 꼬리가 굵어진다.
    /// </summary>
    private void ApplyStar(float t)
    {
        bool on = t >= TFall && t < TLand + StarFade;
        SetStarVisible(on);
        if (!on) return;

        float u      = Mathf.InverseLerp(TFall, TLand, t);
        float after  = Mathf.InverseLerp(TLand, TLand + StarFade, t);   // 착지 뒤 사라짐
        float vis    = Mathf.InverseLerp(TFall, TFall + 0.3f, t) * (1f - after);   // 먼 점이 천천히 밝아진다
        Vector2 p    = Vector2.LerpUnclamped(StarFrom, Land, Mathf.Pow(u, StarPow));
        Color c      = TailColor(t);
        int   stage  = StageAt(t);
        float near   = Mathf.Lerp(0.3f, 1f, u);                       // 멀리서 작게 → 가까이 크게
        float heavy  = 1f + 0.3f * Mathf.Max(0, stage - 1);           // 오를수록 굵어진다
        float pop    = 1f + 0.45f * TintPop(t);
        float length = Mathf.Lerp(StarTailMin * 0.3f, StarTailMax, u) * (1f - after);

        var hr = _wHead.rectTransform;
        hr.anchoredPosition = p;
        hr.localScale       = Vector3.one * (pop * near * (0.85f + 0.15f * heavy));
        _wHead.color = UITheme.Thin.WithA(c, vis);
        _wCore.color = new Color(1f, 1f, 1f, 0.95f * vis);

        var tr = _wTail.rectTransform;
        tr.anchoredPosition = p;
        tr.sizeDelta        = new Vector2(StarTailW * pop * near * heavy, length);
        _wTail.color = UITheme.Thin.WithA(c, 0.9f * vis);

        var gr = _wTailGlow.rectTransform;
        gr.anchoredPosition = p;
        gr.sizeDelta        = new Vector2(StarGlowW * pop * near * heavy, length * 0.9f);
        _wTailGlow.color = UITheme.Thin.WithA(c, 0.28f * vis);

        float dp = stage > 0 ? t - Checkpoints[stage - 1] : -1f;
        if (dp >= 0f && dp < PingDur)
        {
            float k = dp / PingDur;
            _wPing.rectTransform.anchoredPosition = p;
            _wPing.rectTransform.sizeDelta        = Vector2.one * Mathf.Lerp(60f, 180f + 60f * stage, UIMotion.EaseOut(k));
            SetA(_wPing, c, 0.55f * (1f - k) * vis);
        }
        else SetA(_wPing, c, 0f);
    }

    /// <summary>
    /// 꼬리 색 — 첫 고비(2.7초) 전엔 은백(등급 미정), 고비마다 한 단계씩 오른다(Rare → Epic → Legend, 0.06초 전환) — 나온 등급에서 멈춘다.
    /// 오르기만 하므로 <b>위로만</b>(내려가는 가짜 연출 없음). 두 장이면 높은 쪽 등급.
    /// </summary>
    private Color TailColor(float t)
    {
        Color c = Silver;
        for (int k = 1; k <= _plan.Tier && k <= Checkpoints.Length; k++)
            c = Color.Lerp(c, GradeColor(k), Mathf.Clamp01((t - Checkpoints[k - 1]) / SwapDur));
        return c;
    }

    /// <summary>지금까지 오른 단계(0 = 은백) — 나온 등급을 넘지 않는다.</summary>
    private int StageAt(float t)
    {
        int s = 0;
        for (int k = 1; k <= _plan.Tier && k <= Checkpoints.Length; k++) if (t >= Checkpoints[k - 1]) s = k;
        return s;
    }

    /// <summary>색이 오른 직후 0.18초 동안 1 → 0 — 머리가 한 번 부푼다.</summary>
    private float TintPop(float t)
    {
        int s = StageAt(t);
        if (s == 0) return 0f;
        float dt = t - Checkpoints[s - 1];
        return dt < PopDur ? 1f - dt / PopDur : 0f;
    }

    /// <summary>착지 — 등급 색 빛무리가 터지고 고리가 두 겹(둘째는 0.1초 늦게 작게) 퍼진다(흔들림 없음). 전체 섬광은 실시간 <see cref="ApplyFlash"/>.</summary>
    private void ApplyLanding(float t)
    {
        float dl = t - TLand;
        if (dl >= 0f && dl < BurstDur)
        {
            float k = dl / BurstDur;
            _wBurst.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(500f, BurstTo, UIMotion.EaseOut(k));
            SetA(_wBurst, _plan.Grade, BurstA * (1f - k) * (1f - k));
        }
        else SetA(_wBurst, _plan.Grade, 0f);

        if (dl >= 0f && dl < ShockDur)
        {
            float k = dl / ShockDur;
            _wShock.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(200f, 1200f, UIMotion.EaseOut(k));
            SetA(_wShock, _plan.Grade, ShockA * (1f - k));
        }
        else SetA(_wShock, _plan.Grade, 0f);

        float d2 = dl - Shock2Lag;
        if (d2 >= 0f && d2 < ShockDur)
        {
            float k = d2 / ShockDur;
            _wShock2.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(140f, 800f, UIMotion.EaseOut(k));
            SetA(_wShock2, _plan.Grade, Shock2A * (1f - k));
        }
        else SetA(_wShock2, _plan.Grade, 0f);
    }

    /// <summary>
    /// 전설만 한 박자 더(원신 5★ 컷) — 실루엣 동안(4.6~5.3) 금빛 티끌 16개가 사방에서 실루엣으로 모여들고,
    /// 6.1초에 금 고리가 퍼지며 같은 티끌이 사방으로 흩어진다(1.2초 · 끝 7.4초 전에 다 진다). 방향 · 거리는 번호 해시라 매번 같은 모양이다.
    /// </summary>
    private void ApplyLegend(float t)
    {
        float dl = _plan.Tier >= 3 ? t - TLegend : -1f;
        float dc = _plan.Tier >= 3 ? t - TReveal : -1f;
        if (dl >= 0f && dl < GoldRingDur)
        {
            float k = dl / GoldRingDur;
            _wGoldRing.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(320f, 820f, UIMotion.EaseOut(k));
            SetA(_wGoldRing, UITheme.Thin.Gold, 0.65f * (1f - k));
        }
        else SetA(_wGoldRing, UITheme.Thin.Gold, 0f);

        for (int i = 0; i < _wSparks.Length; i++)
        {
            var s = _wSparks[i];
            if (dc >= 0f && dc < ConvergeDur)
            {
                float kc = dc / ConvergeDur;
                float ac = (i * 360f / _wSparks.Length + 23f * Hash(i, 2, 7)) * Mathf.Deg2Rad;
                float fc = Mathf.Lerp(420f + 140f * Hash(i, 3, 7), 30f, kc * kc);
                var   rc = s.rectTransform;
                rc.anchoredPosition = Land + new Vector2(Mathf.Cos(ac), Mathf.Sin(ac)) * fc;
                rc.sizeDelta        = Vector2.one * Mathf.Lerp(8f, 16f, kc);
                SetA(s, UITheme.Thin.Gold, 0.85f * Mathf.Sin(Mathf.PI * kc));
                continue;
            }
            if (dl < 0f || dl >= SparkDur) { SetA(s, UITheme.Thin.Gold, 0f); continue; }
            float k   = dl / SparkDur;
            float ang = (i * 360f / _wSparks.Length + 14f * Hash(i, 0, 7)) * Mathf.Deg2Rad;
            float far = Mathf.Lerp(80f, 300f + 160f * Hash(i, 1, 7), UIMotion.EaseOut(k));
            var   rt  = s.rectTransform;
            rt.anchoredPosition = Land + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * far;
            rt.sizeDelta        = Vector2.one * Mathf.Lerp(20f, 8f, k);
            SetA(s, UITheme.Thin.Gold, 0.95f * (1f - k) * (1f - k));
        }
    }

    /// <summary>착지 섬광 — 등급 색 화면 전체 2.5%가 0.2초에 걷힌다(흔들림 없음). dt &lt; 0이면 끈다.</summary>
    private void ApplyFlash(float dt)
    {
        if (_wFlash == null) return;
        float a = dt < 0f || dt >= FlashDur ? 0f : FlashA * (1f - dt / FlashDur);
        SetA(_wFlash, _plan.Grade, a);
    }

    /// <summary>
    /// 공개 카드 한 장 — 실루엣(검게 · 흰 테가 숨 쉼) 0.7초 → 채색 0.3초(조금 크게 떠서 제자리로) → 이름 · 속성 · 효과 0.2초 →
    /// ◆가 0.12초 간격으로 하나씩 켜진다. 전설은 6.1초부터 이름이 금빛으로. delay = 두 장일 때 높은 쪽이 늦는 몫.
    /// </summary>
    private void ApplyCard(WishCard c, float t, float delay)
    {
        if (c == null) return;
        float r0 = TReveal + delay;
        bool show = t >= r0;
        if (c.Root.gameObject.activeSelf != show) c.Root.gameObject.SetActive(show);
        if (!show) return;

        Color g   = GradeColor(c.Tier);
        float a   = Mathf.InverseLerp(r0, r0 + 0.08f, t);
        float col = UIMotion.EaseOut(Mathf.InverseLerp(TSilEnd + delay, TColEnd + delay, t));
        float s   = Mathf.Lerp(1.12f, 1f, UIMotion.EaseOut(Mathf.InverseLerp(r0, TColEnd + delay, t)));

        c.Icon.color = new Color(Mathf.Lerp(Shade.r, 1f, col), Mathf.Lerp(Shade.g, 1f, col), Mathf.Lerp(Shade.b, 1f, col), a);
        c.Icon.rectTransform.localScale = new Vector3(s, s, 1f);
        c.Rim.color = new Color(1f, 1f, 1f, a * (1f - col) * (0.7f + 0.3f * Mathf.Cos((t - r0) * 9f)));   // 「누구지?」 — 테가 숨 쉰다
        c.Rim.rectTransform.localScale  = new Vector3(s, s, 1f);
        SetA(c.Glow, g, GlowA * UIMotion.EaseOut(Mathf.InverseLerp(r0, r0 + 0.3f, t)));

        float text = UIMotion.EaseOut(Mathf.InverseLerp(TColEnd + delay, TTextEnd + delay, t));
        c.TextGroup.alpha = text;
        c.Text.anchoredPosition = c.TextPos + new Vector2(0f, Mathf.Lerp(-12f, 0f, text));   // 그림 아래에서 살짝 떠오른다
        c.Name.color = c.Tier >= 3
            ? Color.Lerp(UITheme.Thin.Ink, UITheme.Thin.Gold, Mathf.InverseLerp(TLegend, TLegend + 0.4f, t))
            : UITheme.Thin.Ink;

        for (int i = 0; i < c.Gems.Length; i++)
        {
            var gem = c.Gems[i];
            if (gem == null || i >= c.Tier) continue;
            float dt = t - (TGem + delay + i * GemStep);
            bool lit = dt >= 0f;
            gem.color = lit ? g : UITheme.Thin.InkA(0.16f);
            float k = lit ? Mathf.Lerp(1.6f, 1f, UIMotion.EaseOut(dt / GemPop)) : 1f;
            gem.rectTransform.localScale = new Vector3(k, k, 1f);
        }
    }

    /// <summary>
    /// 소리 — 누름(집광) · 충전 맥박(낮게 · 점점 높게) · 오르는 고비마다 한 번(높이 = 그 단계) · 착지 · ◆ 차임(하나씩) + 전설 종.
    /// 정제소가 쓰던 키만 쓴다(새 에셋 없음). 건너뛰며 감는 중엔 맥박 · 고비 · 차임을 뺀다(착지 · 전설 종은 한 번 울린다).
    /// </summary>
    private void FireCues(float t, bool skipping)
    {
        if (_plan.Cue == 0) { _plan.Cue = 1; ShopUIStyle.PlaySfx("refine_spin"); }
        while (_plan.Beat < Beats.Length && t >= Beats[_plan.Beat])
        {
            if (!skipping) PlayCue("refine_lit", 0.55f + 0.05f * _plan.Beat);
            _plan.Beat++;
        }
        int stage = StageAt(t);
        while (_plan.Up < stage)
        {
            _plan.Up++;
            if (!skipping) PlayCue("refine_lit", _plan.Up >= 3 ? 1.04f : _plan.Up == 2 ? 0.92f : 0.82f);
        }
        if (_plan.Cue == 1 && t >= TLand)
        {
            _plan.Cue = 2;
            ShopUIStyle.PlaySfx("refine_reveal");
        }

        // ◆ — 높은 쪽 카드의 등급 수만큼, 켜질 때마다 차임 한 번(조금씩 높게)
        float delay = _plan.Twin ? TwinLag : 0f;
        int lit = 0;
        for (int i = 0; i < _plan.MainTier; i++) if (t >= TGem + delay + i * GemStep) lit = i + 1;
        if (lit != _plan.Lit)
        {
            if (!skipping) for (int i = _plan.Lit; i < lit; i++) PlayCue("refine_lit", 1f + 0.06f * i);
            _plan.Lit = lit;
        }

        if (_plan.Cue == 2 && _plan.Tier >= 3 && t >= TLegend) { _plan.Cue = 3; ShopUIStyle.PlaySfx("refine_legend"); }
    }

    /// <summary>뜻 키를 피치를 곱해 낸다(UI 채널) — 표에 없으면 공용 길(<see cref="ShopUIStyle.PlaySfx"/>)로.</summary>
    private static void PlayCue(string key, float pitch)
    {
        var sound = Managers.Sound;
        if (sound != null && sound.PlayUiEvent(key, pitch)) return;
        ShopUIStyle.PlaySfx(key);
    }

    // ── 끝 상태 ──

    /// <summary>끝 상태의 잔잔한 움직임(별 반짝임 · 전설 빛살 회전)을 켠다 — 다음 정제 · 공개 닫기 · 파괴 때 멈춘다.</summary>
    private void StartAmbient(float from)
    {
        StopAmbient();
        _ambientCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        AmbientAsync(from, _ambientCts.Token).Forget();
    }

    private void StopAmbient()
    {
        if (_ambientCts == null) return;
        _ambientCts.Cancel();
        _ambientCts.Dispose();
        _ambientCts = null;
    }

    private async UniTaskVoid AmbientAsync(float from, CancellationToken ct)
    {
        float t0 = Time.unscaledTime;
        try
        {
            while (true)
            {
                ApplyAmbient(from + Time.unscaledTime - t0);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── 코드로 만든 사본 · 작은 도우미 ──

    /// <summary>
    /// 결과 틀 아트(중앙 최종룬)에서 <b>구워진 청록 후광 · 바깥 선을 걷어 낸</b> 사본 — 대기 화면 결과 틀 후광 0(C §2).
    /// 후광은 한 색(177,250,255)이라 청록 쪽으로 기운 픽셀만 지운다. 붉은 장식 테(유물 콘셉트)는 그대로 남는다. 한 번 만들어 캐시한다.
    /// </summary>
    private static Sprite FrameWithoutHalo(Sprite src)
    {
        if (src == null || src == s_frameNoHalo) return src;
        if (s_frameNoHalo != null && s_frameNoHaloSrc == src) return s_frameNoHalo;
        var sprite = CopySprite(src, "NoHalo",
            c => c.g > c.r + HaloHueGap && c.b > c.r + HaloHueGap ? new Color32(0, 0, 0, 0) : c);
        if (sprite == null) return src;
        s_frameNoHalo    = sprite;
        s_frameNoHaloSrc = src;
        return sprite;
    }

    /// <summary>
    /// 스프라이트의 픽셀을 바꾼 사본 — 읽기 불가 텍스처라 GPU로 복사해 읽는다(<see cref="UISpriteMaster"/>와 같은 방식).
    /// 모양(피벗 · 9-slice 경계 · 단위당 픽셀)은 원본 그대로. 저장되지 않는 런타임 텍스처다.
    /// </summary>
    private static Sprite CopySprite(Sprite src, string suffix, Func<Color32, Color32> map)
    {
        var tex = src != null ? src.texture : null;
        if (tex == null) return null;

        Rect r;
        try { r = src.textureRect; }   // 촘촘히 묶인(tight) 아틀라스면 예외 — 그땐 rect
        catch { r = src.rect; }
        int x = Mathf.FloorToInt(r.x), y = Mathf.FloorToInt(r.y);
        int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));

        // sRGB 렌더 타깃 — 선형 색공간에서도 읽어 온 바이트가 원본과 같게.
        var rt   = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Texture2D copy;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(x, y, w, h), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        var px = copy.GetPixels32();
        for (int i = 0; i < px.Length; i++) px[i] = map(px[i]);
        copy.SetPixels32(px);
        copy.wrapMode   = TextureWrapMode.Clamp;
        copy.filterMode = FilterMode.Bilinear;
        copy.name       = $"{src.name} ({suffix})";
        copy.hideFlags  = HideFlags.HideAndDontSave;
        copy.Apply(false, true);

        var pivot  = new Vector2(src.pivot.x / src.rect.width, src.pivot.y / src.rect.height);
        var sprite = Sprite.Create(copy, new Rect(0f, 0f, w, h), pivot, src.pixelsPerUnit, 0, SpriteMeshType.FullRect, src.border);
        sprite.name      = copy.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// <summary>색 · 알파를 함께 주고, 알파가 0이면 그리지 않는다(화면을 덮는 큰 투명 그림의 채우기 비용을 아낀다).</summary>
    private static void SetA(Image img, Color c, float a)
    {
        if (img == null) return;
        img.color = UITheme.Thin.WithA(c, a);
        bool on = a > 0.001f;
        if (img.enabled != on) img.enabled = on;
    }

    private void SetStarVisible(bool on)
    {
        if (_wHead == null || _wHead.enabled == on) return;
        _wHead.enabled     = on;
        _wCore.enabled     = on;
        _wTail.enabled     = on;
        _wTailGlow.enabled = on;
        if (!on) SetA(_wPing, Color.white, 0f);
    }

    private static ItemRarity TierRarity(int tier) => tier switch
    {
        3 => ItemRarity.Legendary,
        2 => ItemRarity.Epic,
        _ => ItemRarity.Rare,
    };

    private static Color GradeColor(int tier) => ShopUIStyle.Rarity(TierRarity(tier));

    // 등급 이름은 영문(10-10 용어집 · 확률판과 같은 말 — 「Legendary」는 좁은 칸에서 넘쳐 Legend)
    private static string GradeName(int tier) => tier switch
    {
        3 => "Legend",
        2 => "Epic",
        _ => "Rare",
    };
}
