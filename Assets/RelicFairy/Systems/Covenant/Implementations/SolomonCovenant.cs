using UnityEngine;

/// <summary>
/// 솔로몬의 서약 — 72개 봉인의 왕과 맺은 소환 계약
///
/// [Basic]    처치 시 25% 확률로 유령 소환 (10초, 자동 공격)
/// [Enhanced] 확률 45%, 유령 최대 2기 동시
/// [Evolved]  소환 유령이 처치한 적도 유령으로 소환 (연쇄, 최대 5기)
/// </summary>
public sealed class SolomonCovenant : CovenantBase
{
    private const int V_SUMMON_CHANCE   = 0;
    private const int V_MAX_GHOSTS      = 1;
    private const int V_GHOST_DURATION  = 2;

    public override string CovenantId => CovenantFactory.Solomon;

    // ── 표시 데이터 ──────────────────────────────────────
    public override string DisplayName         => "솔로몬의 서약";
    public override string LoreText            => "72개 봉인의 왕과 맺은 소환 계약";
    public override string BasicDescription    => "처치 시 25% 확률로 유령 소환 (10초, 자동 공격)";
    public override string EnhancedDescription => "확률 45%, 유령 최대 2기 동시";
    public override string EvolvedDescription  => "소환 유령이 처치한 적도 유령으로 소환 (연쇄, 최대 5기)";

    // ── 런타임 상태 ──────────────────────────────────────
    private int _activeGhostCount;

    private float SummonChance   => V(V_SUMMON_CHANCE,  0.25f);
    private int   MaxGhosts      => VI(V_MAX_GHOSTS,    1);
    private float GhostDuration  => V(V_GHOST_DURATION, 10f);
    private bool  IsEvolved      => Stage == CovenantStage.Evolved;

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnKill(GameObject target)
    {
        if (_activeGhostCount >= MaxGhosts) return;
        if (Random.value > SummonChance) return;

        SummonGhost(target.transform.position, isChained: false);
    }

    // ── 내부 ────────────────────────────────────────────
    private void SummonGhost(Vector3 position, bool isChained)
    {
        if (_activeGhostCount >= MaxGhosts) return;

        _activeGhostCount++;

        // TODO: Addressable로 유령 프리팹 스폰 (GhostDuration 초 뒤 자동 소멸)
        // IsEvolved: 유령이 처치한 적 위치에 SummonGhost(pos, isChained: true) 재귀 호출
    }

    public void OnGhostExpired() => _activeGhostCount = Mathf.Max(0, _activeGhostCount - 1);
}
