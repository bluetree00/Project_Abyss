using UnityEngine;

/// <summary>
/// 트리스탄의 서약 — 멈추지 않는 전투의 리듬 (행동 조건형: 이동 전투)
///
/// [선택]   이동 중 공격 시 추가 전방 투사 피해(투사체 변환 근사).
/// [강화]   튕김(P2) — 현재는 배율 유지.
/// [각성]   이동거리 비례 크기 증가(P2) — 현재는 배율 유지.
///
/// 데이터 인덱스: [0]투사 피해배율 [1]튕김(P2 미적용) [2]최대크기(P2 미적용)
/// ⚠️ P1 근사: 이동 중 공격에 전방 광역 추가 피해. 실제 근접→투사체 변환·튕김·크기는 P2 seam.
/// </summary>
public sealed class TristanCovenant : CovenantBase
{
    private const int V_MULT = 0;
    private const float MinMoveSqr = 0.25f; // 0.5 m/s 이상이면 이동 중으로 판정

    public override string CovenantId => CovenantFactory.Tristan;
    public override CovenantCategory Category => CovenantCategory.ActionConditional;

    public override string DisplayName         => "트리스탄의 서약";
    public override string LoreText            => "트리스탄 — 멈추지 않는 전투의 리듬으로 싸웠다";
    public override string BasicDescription    => "이동 중 공격 시 추가 전방 투사 피해(투사체 변환).";
    public override string EnhancedDescription => "투사체가 적을 튕겨 추가로 적중.";
    public override string EvolvedDescription  => "이동 거리에 비례해 투사체 크기 증가(최대 3배).";

    private float Mult => V(V_MULT, 1f);

    private bool IsMoving()
    {
        var r = Ctx?.Player?.Rigid;
        if (r == null) return false;
        var v = r.linearVelocity; v.y = 0f;
        return v.sqrMagnitude > MinMoveSqr;
    }

    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (target == null || !IsMoving()) return;
        // 이동 중 공격 → 전방 추가 투사 피해(근사)
        Vector3 at = target.transform.position;
        DealAoe(at, 1.5f, Mult);
        Vfx("VFX_FireExplosion", at);
    }
}
