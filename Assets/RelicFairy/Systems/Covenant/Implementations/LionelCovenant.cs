using UnityEngine;

/// <summary>
/// 라이오넬의 서약 — 멈추지 않는 연속 공세 (행동 조건형)
///
/// [선택]   같은 적 N회 연속 적중 시 광역 충격파 발동(반경 r). 이후 카운터 초기화.
/// [강화]   연속 카운터 단축 + 충격파 반경 확대.
/// [각성]   충격파 후 카운터가 0이 아닌 restart부터 재시작 → 더 잦은 발동.
///
/// 데이터 인덱스: [0]연속수 [1]반경 [2]재시작카운터 [3]충격파 피해배율
/// P1 구현: 충격파는 가산형 AoE(DealAoe). PDF의 "다음 공격이 광역 충격파로 전환"은
///          P2 공격 변환 파이프라인(AttackRequest)에서 충실화 예정.
/// </summary>
public sealed class LionelCovenant : CovenantBase
{
    private const int V_STREAK   = 0;
    private const int V_RADIUS   = 1;
    private const int V_RESTART  = 2;
    private const int V_SHOCKMUL = 3;

    public override string CovenantId => CovenantFactory.Lionel;
    public override CovenantCategory Category => CovenantCategory.ActionConditional;

    public override string DisplayName         => "라이오넬의 서약";
    public override string LoreText            => "라이오넬 — 멈추지 않는 연속 공세로 전장을 압도했다";
    public override string BasicDescription    => "같은 적을 5회 연속 적중 시 다음 공격이 광역 충격파로 전환(반경 2m). 카운터 초기화.";
    public override string EnhancedDescription => "연속 적중 카운터 4회로 단축. 충격파 반경 3m로 확대.";
    public override string EvolvedDescription  => "충격파 발동 시 카운터가 2부터 재시작 — 충격파가 더 자주 발동되는 리듬.";

    // ── 런타임 상태 ──────────────────────────────────────
    private GameObject _lastTarget;
    private int        _streak;

    private int   StreakNeeded => Mathf.Max(1, VI(V_STREAK, 5));
    private float Radius       => V(V_RADIUS, 2f);
    private int   RestartValue => VI(V_RESTART, 0);
    private float ShockMult    => V(V_SHOCKMUL, 1f);

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (target == null) return;

        // 대상이 바뀌면 연속 카운터를 1부터 다시 시작
        if (target != _lastTarget)
        {
            _lastTarget = target;
            _streak = 1;
            return;
        }

        _streak++;
        if (_streak < StreakNeeded) return;

        // 충격파 발동 — DealAoe는 ColliderInstance를 거치지 않아 OnAttackHit 재귀 없음.
        Vector3 at = target.transform.position;
        DealAoe(at, Radius, ShockMult);
        Vfx("VFX_FireExplosion", at); // 임시 VFX (전용 충격파 VFX 대기)

        _streak = RestartValue;
    }

    public override void OnRoomEnter()
    {
        // 방 이동 시 대상/카운터 리셋 (이전 방의 적은 사라짐)
        _lastTarget = null;
        _streak = 0;
    }
}
