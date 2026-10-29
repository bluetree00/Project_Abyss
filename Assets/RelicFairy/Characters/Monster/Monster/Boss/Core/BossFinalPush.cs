using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 해방 보스 「한 번 더」(10-29 설계 §6-2 F2 · 2a) — 2페이지 마지막 <see cref="HpShare"/>에서 패턴이 끝나면,
/// 봉인기엔 사슬이 막고 있던 그 보스의 해방기 전용 기술(<see cref="BossPatternSO.liberatedOnly"/> — 숲 잡아 던지기 · 화룡 휩쓸기 · 기사 무너지는 원탁)을
/// 곧바로 한 번 더 잇는다. 연계 칸이 아니라 온전한 패턴으로 낸다(예고 · 판정 그대로) · 끝나면 큰 틈(<see cref="OpeningBreak"/>) — 피하면 칠 시간이 생긴다.
/// 실행기(<see cref="BossPatternRunner"/> · <see cref="DKComboRunner"/>)가 패턴이 끝날 때 <see cref="TryTake"/>로 묻는다. 해방기 전용 기술이 없는 보스(리치)는 만들지 않는다.
/// </summary>
public sealed class BossFinalPush
{
    // ── Constants ──
    public const float HpShare      = 0.25f;   // 2페이지 체력 이 비율 아래
    public const float Cooldown     = 12f;     // 다시 잇기까지(초) — 마지막 구간이 길어져도 매번은 아니게
    public const float LinkRest     = 0.5f;    // 앞 패턴 끝 → 「한 번 더」 시작
    public const float OpeningBreak = 2.6f;    // 「한 번 더」 끝 → 다음 패턴까지 최소(큰 틈)

    // ── Private ──
    private readonly MonsterBase _boss;
    private readonly IPagedBoss  _paged;
    private float _lastAt = float.NegativeInfinity;

    // ── Properties ──
    public BossPatternSO Pattern { get; }

    private BossFinalPush(MonsterBase boss, IPagedBoss paged, BossPatternSO pattern)
    {
        _boss   = boss;
        _paged  = paged;
        Pattern = pattern;
    }

    // ── Public Methods ──
    /// <summary>설정 항목 · 덧붙인 풀에서 처음 만난 해방기 전용 기술로 만든다 — 2페이지 보스가 아니거나 그런 기술이 없으면 null.</summary>
    public static BossFinalPush Create(MonsterBase boss, BossConfigSO config, params IEnumerable<BossPatternSO>[] pools)
    {
        if (boss is not IPagedBoss paged) return null;
        if (config?.patternEntries != null)
            foreach (var entry in config.patternEntries)
                if (entry?.patterns != null)
                    foreach (var p in entry.patterns)
                        if (p != null && p.liberatedOnly) return Made(boss, paged, p);
        foreach (var pool in pools)
            if (pool != null)
                foreach (var p in pool)
                    if (p != null && p.liberatedOnly) return Made(boss, paged, p);
        return null;
    }

    private static BossFinalPush Made(MonsterBase boss, IPagedBoss paged, BossPatternSO p)
    {
        Debug.Log($"[FinalPush] {boss.name} 준비 — {p.name}");
        return new BossFinalPush(boss, paged, p);
    }

    /// <summary>방금 끝난 패턴 뒤에 「한 번 더」를 이을 때인가 — 맞으면 쿨을 잰다. 같은 기술 뒤엔 잇지 않는다.</summary>
    public bool TryTake(BossPatternSO justEnded, BossPatternContext ctx)
    {
        var pages = _paged.Pages;
        if (pages == null || !pages.IsPage2 || pages.Transitioning || justEnded == Pattern) return false;
        float ratio = pages.Page2Ratio(_boss.CurrentHp);
        if (ratio > HpShare || Time.time - _lastAt < Cooldown) return false;
        if (Pattern.EraLocked || Pattern.GetRuntimeState() == null || !Pattern.CanFollowUp(ctx)) return false;
        _lastAt = Time.time;
        Debug.Log($"[FinalPush] {_boss.name} 「한 번 더」 — {Pattern.name} · 2페이지 {ratio:P0}");
        return true;
    }

    /// <summary>「한 번 더」가 끝났다 — 실행기가 큰 틈을 건다.</summary>
    public void NoteEnded() => Debug.Log($"[FinalPush] {_boss.name} 끝 — 큰 틈 {OpeningBreak:0.0}초");

    /// <summary>보스 풀 재사용.</summary>
    public void Reset() => _lastAt = float.NegativeInfinity;
}
}
