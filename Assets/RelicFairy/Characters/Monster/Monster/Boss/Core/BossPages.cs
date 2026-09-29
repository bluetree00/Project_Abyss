using System;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 2페이지(해방 페이지) 공용 부품 — 숲의 수호자 · 화룡 · 죽음의 기사가 들고 있는 일반 객체(09-28 설계 확정 §2).
///
/// 해방기부터(<see cref="StoryProgress.IsLiberated"/>) 켜진다 — 봉인이 풀려야 두 번째 힘이 열린다(09-29 시기별 페이지). 켜지면
///   · 총 체력 = 기존 × (1 + <see cref="Page2Share"/>) — 두 줄 체력바. 1페이지 = 기존 체력 · 2페이지 = 기존 × 0.4.
///   · 1페이지 동안 체력은 2페이지 몫 아래로 내려가지 않는다(<see cref="HpFloor"/>) → 전환 패턴이 무적으로 연출 → 2페이지.
///   · 보스가 스스로 읽는 체력 비율(<see cref="PhaseRatio"/>)은 1페이지에선 1페이지 기준(1→0)이라
///     기존 페이즈 · 소환 · 광폭 경계가 코드 수정 없이 1페이지 안에서 그대로 돈다. 2페이지에선 기존 체력 기준(0.4→0).
/// 꺼져 있으면(봉인기) 모든 값이 지금 전투와 같다.
/// 리치는 자체 페이지 구현을 그대로 쓴다(이 부품으로 옮기지 않음 — D4).
/// </summary>
public sealed class BossPages
{
    // ── Constants ──────────────────────────────────────────────
    public const float DefaultPage2Share = 0.4f;   // 2페이지 체력 = 기존 × 0.4 (D1 더하기)
    public const float SignatureAt       = 0.5f;   // 2페이지 체력 이 비율에서 간판 패턴 한 번
    public const float LateBreakScale    = 0.75f;  // 간판 뒤(후반) 패턴 사이 쉬는 시간 배율 — 2페이지 구성 §9
    public const float NightmareBreakScale = 0.8f; // 악몽 모드 — 패턴 사이 쉬는 시간 배율(시기별 페이지 §2-1, 강화 1차)

    // ── Private ────────────────────────────────────────────────
    private readonly MonsterBase _boss;
    private readonly float[]     _markers;
    private int  _page    = 1;
    private int  _hudPage = 1;
    private bool _transitioning;
    private bool _signatureDone;
    private int  _page2Patterns;   // 2페이지에서 시작한 패턴 수 — 개막 판정

    // ── Properties ─────────────────────────────────────────────
    /// <summary>이번 전투에 2페이지가 있는가(해방기부터).</summary>
    public bool  Enabled       { get; }
    /// <summary>악몽 모드 전투 — 쉬는 시간이 짧다.</summary>
    public bool  NightmareMode { get; }
    public float Page2Share    { get; }
    public int   Page          => _page;
    public bool  IsPage2       => Enabled && _page >= 2;
    public bool  Transitioning => _transitioning;
    public bool  SignatureDone => _signatureDone;
    /// <summary>개막 — 전환이 끝났고 2페이지에서 아직 패턴을 하나도 안 썼다(§9: 목표를 보여 주는 패턴을 먼저).</summary>
    public bool  OpenerDue     => IsPage2 && !_transitioning && _page2Patterns == 0;
    /// <summary>후반 — 간판을 쓴 뒤(§9: 쉬는 시간 −25% · 컨셉 연계기).</summary>
    public bool  IsLate        => IsPage2 && _signatureDone;
    /// <summary>러너가 패턴 사이 쉬는 시간에 곱하는 배율 — 후반 ×0.75, 악몽 모드 ×0.8(곱).</summary>
    public float BreakScale    => (IsLate ? LateBreakScale : 1f) * (NightmareMode ? NightmareBreakScale : 1f);

    /// <summary>총 체력 배율 — MonsterBase.BossHpScale로 넘긴다.</summary>
    public float HpScale => Enabled ? 1f + Page2Share : 1f;

    /// <summary>2페이지 몫 체력(HP 수치).</summary>
    public int Page2Hp => Enabled ? Mathf.CeilToInt(_boss.EffectiveMaxHp * Page2Share / (1f + Page2Share)) : 0;

    /// <summary>1페이지(= 기존 전투) 체력.</summary>
    public int Page1Hp => _boss.EffectiveMaxHp - Page2Hp;

    // IBossHudSource 위임용
    public float[] HudPageMarkers => _markers;
    public int     HudPage        => _hudPage;
    public event Action<int, float> HudPageRefill;
    public event Action             HudPageMarkersChanged;

    // ── Constructor ────────────────────────────────────────────
    public BossPages(MonsterBase boss, bool enabled, float page2Share = DefaultPage2Share)
    {
        _boss      = boss;
        Enabled    = enabled && boss != null;
        NightmareMode = Enabled && StoryProgress.IsNightmareMode;
        Page2Share = Mathf.Max(0.01f, page2Share);
        _markers   = Enabled ? new[] { Page2Share / (1f + Page2Share) } : null;
    }

    /// <summary>이번 전투에 2페이지를 여는가 — 해방기부터(봉인기 1줄 → 해방기 · 악몽 모드 2줄).</summary>
    public static bool ResolveEnabled() => StoryProgress.IsLiberated;

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// 보스가 페이즈 · 원소 · 광폭 경계에 쓰는 체력 비율. <paramref name="configMaxHp"/> = config 최대 체력(기존 공식).
    /// 꺼져 있으면 기존 공식 그대로(현재 / config 최대).
    /// </summary>
    public float PhaseRatio(int currentHp, int configMaxHp)
    {
        if (!Enabled) return configMaxHp > 0 ? (float)currentHp / configMaxHp : 1f;
        float baseMax = Mathf.Max(1, Page1Hp);
        return _page <= 1
            ? Mathf.Clamp01((currentHp - Page2Hp) / baseMax)   // 1페이지: 1 → 0
            : Mathf.Max(0f, currentHp / baseMax);               // 2페이지: 0.4 → 0 (1페이지 조건이 다시 켜지지 않게)
    }

    /// <summary>2페이지 안에서의 체력 비율(1 → 0). 간판 · 2페이지 전용 경계에 쓴다.</summary>
    public float Page2Ratio(int currentHp)
        => Page2Hp > 0 ? Mathf.Clamp01((float)currentHp / Page2Hp) : 0f;

    /// <summary>MonsterBase.DamageHpFloor에 덮어씌운다 — 1페이지에선 2페이지 몫 아래로 안 깎인다.</summary>
    public int HpFloor(int baseFloor)
        => Enabled && _page <= 1 ? Mathf.Max(baseFloor, Page2Hp) : baseFloor;

    /// <summary>전환 패턴을 쓸 때인가 — 1페이지 체력이 다 깎였다.</summary>
    public bool TransitionDue(int currentHp)
        => Enabled && _page <= 1 && !_transitioning && currentHp <= Page2Hp;

    /// <summary>간판 패턴을 쓸 때인가 — 2페이지 체력 50% 이하 · 아직 안 씀.</summary>
    public bool SignatureDue(int currentHp)
        => IsPage2 && !_transitioning && !_signatureDone && Page2Ratio(currentHp) <= SignatureAt;

    public void MarkSignatureDone() => _signatureDone = true;

    /// <summary>러너가 패턴을 시작할 때 부른다 — 2페이지에서 쓴 패턴 수(개막 판정).</summary>
    public void NotePatternStarted()
    {
        if (IsPage2 && !_transitioning) _page2Patterns++;
    }

    /// <summary>전환 시작(전환 패턴 Enter).</summary>
    public void BeginTransition() => _transitioning = true;

    /// <summary>전환 연출이 바를 채우기 시작한다 — HUD가 두 번째 줄을 <paramref name="seconds"/> 동안 차오르게 그린다.</summary>
    public void BeginRefill(float seconds)
    {
        _hudPage = 2;
        HudPageRefill?.Invoke(2, seconds);
    }

    /// <summary>전환 끝 — 2페이지.</summary>
    public void CompleteTransition()
    {
        _transitioning = false;
        _page = 2;
        if (_hudPage < 2)
        {
            _hudPage = 2;
            HudPageMarkersChanged?.Invoke();
        }
    }

    /// <summary>전환이 끊겼을 때(보스 비활성 등) — 다시 시도할 수 있게.</summary>
    public void AbortTransition() => _transitioning = false;
}

/// <summary>2페이지 보스 — 전환 패턴이 부르는 훅.</summary>
public interface IPagedBoss
{
    BossPages Pages { get; }

    /// <summary>이야기 보스 id(<see cref="StoryProgress"/> 키) — 시나리오 장면(<see cref="BossStoryScenes"/>)이 본다.</summary>
    string StoryBossId { get; }

    /// <summary>전환 전경 순간 — 무대를 영구히 바꾼다(<paramref name="seconds"/> = 자라나는 시간).</summary>
    void OnPageStageChange(float seconds);

    /// <summary>전환 끝 — 2페이지 진입(버프 · 수동 공격 정리 등).</summary>
    void OnPage2Entered();

    /// <summary>전환 · 간판 패턴이 끝난 뒤 돌아갈 상태로 보낸다(보스마다 추격/대기 상태가 다르다).</summary>
    void ReturnToCombat();
}

/// <summary>페이지 조건 — <see cref="BossConditionKey.Page_TransitionDue"/> 등. 보스 조건 조립기의 기본 분기에서 만든다.</summary>
public sealed class BossPageCondition : ICondition
{
    private readonly Func<bool> _eval;
    public BossPageCondition(Func<bool> eval) { _eval = eval; }
    public bool Evaluate(BossPatternContext ctx) => _eval != null && _eval();

    /// <summary>페이지 키면 조건을 만든다. 페이지 키가 아니면 false.</summary>
    public static bool TryBuild(BossConditionKey key, MonsterBase boss, Func<BossPages> pages, out ICondition condition)
    {
        condition = key switch
        {
            BossConditionKey.Page_1            => new BossPageCondition(() => pages()?.IsPage2 != true),
            BossConditionKey.Page_2            => new BossPageCondition(() => pages()?.IsPage2 == true),
            BossConditionKey.Page_TransitionDue=> new BossPageCondition(() => pages() is { } p && boss != null && p.TransitionDue(boss.CurrentHp)),
            BossConditionKey.Page_SignatureDue => new BossPageCondition(() => pages() is { } p && boss != null && p.SignatureDue(boss.CurrentHp)),
            BossConditionKey.Page_Opener       => new BossPageCondition(() => pages()?.OpenerDue == true),
            BossConditionKey.Page_Late         => new BossPageCondition(() => pages()?.IsLate == true),
            _ => null,
        };
        return condition != null;
    }
}
}
