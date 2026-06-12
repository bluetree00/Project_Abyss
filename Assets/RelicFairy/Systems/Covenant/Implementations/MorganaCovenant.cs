using UnityEngine;

/// <summary>
/// 모르가나의 서약 — 변신의 마법으로 이전 무기의 잔상을 남긴다 (행동 조건형: 잔상)
///
/// [선택]   무기 교체 직후 N초간, 공격 시 이전 무기 잔상이 ×mult로 추가 타격.
/// [강화]   지속 연장 + 배율 상승 + 잔상이 다른 방향에서 타격(범위↑).
/// [각성]   잔상 2개(이전+그 이전 무기) ×mult1/×mult2. 연속 교체 시 창 갱신.
///
/// 데이터 인덱스: [0]지속초 [1]잔상배율 [2]잔상수 [3]반경 [4]2번째 잔상배율
/// P1 구현: 잔상 피해는 가산형 AoE(현재 공격력 기반). PDF의 "이전 무기 잔상"의 정확한
///          이전-무기 스탯 반영·방향 분리 타격은 P2 공격 변환 파이프라인에서 충실화 예정.
/// </summary>
public sealed class MorganaCovenant : CovenantBase
{
    private const int V_DURATION = 0;
    private const int V_MULT1    = 1;
    private const int V_COUNT    = 2;
    private const int V_RADIUS   = 3;
    private const int V_MULT2    = 4;

    public override string CovenantId => CovenantFactory.Morgana;
    public override CovenantCategory Category => CovenantCategory.ActionConditional;

    public override string DisplayName         => "모르가나의 서약";
    public override string LoreText            => "모르가나 — 변신의 마법으로 이전 무기의 잔상을 남겼다";
    public override string BasicDescription    => "무기 교체 직후 5초간, 공격 시 이전 무기의 잔상이 공격력 ×40%로 추가 타격.";
    public override string EnhancedDescription => "잔상 지속 7초로 연장. 잔상 공격력 ×60%로 상승, 적중 범위 확대.";
    public override string EvolvedDescription  => "교체 시 잔상이 2개로 증가(×60%/×40%). 연속 교체할수록 잔상이 쌓인다.";

    // ── 런타임 상태 ──────────────────────────────────────
    private float _windowEnd;

    private float Duration => V(V_DURATION, 5f);
    private float Mult1    => V(V_MULT1, 0.40f);
    private int   Count    => Mathf.Max(1, VI(V_COUNT, 1));
    private float Radius   => V(V_RADIUS, 1f);
    private float Mult2    => V(V_MULT2, 0f);

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnWeaponSwap(WeaponData prev, WeaponData next)
    {
        // 교체 시 잔상 창 개시/갱신. 첫 무기 장착(prev=null)은 무시.
        if (prev == null) return;
        _windowEnd = Time.time + Duration;
    }

    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (target == null || Time.time >= _windowEnd) return;

        // 잔상 추가 타격(가산). DealAoe는 ColliderInstance를 안 거쳐 OnAttackHit 재귀 없음.
        Vector3 at = target.transform.position;
        DealAoe(at, Radius, Mult1);
        Vfx("VFX_FireExplosion", at); // 임시 VFX (전용 잔상 VFX 대기)

        // 각성: 두 번째 잔상
        if (Count >= 2 && Mult2 > 0f)
            DealAoe(at, Radius, Mult2);
    }
}
