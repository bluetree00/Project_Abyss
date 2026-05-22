using UnityEngine;

/// <summary>
/// 발로르의 서약 — 사악한 눈의 거인왕과 맺은 파괴 계약
///
/// [Basic]    8초마다 전방 관통 시선 발동 (고정 피해)
/// [Enhanced] 쿨타임 5초, 피해 +50%
/// [Evolved]  시선 적중 적에게 저주 부여 → 저주 적 처치 시 연쇄 폭발로 주변 전파
/// </summary>
public sealed class BalorCovenant : CovenantBase
{
    private const int V_COOLDOWN_MAX  = 0;
    private const int V_DAMAGE_BONUS  = 1;

    public override string CovenantId => CovenantFactory.Balor;

    // ── 런타임 상태 ──────────────────────────────────────
    private float _eyeCooldown;

    private float CooldownMax => V(V_COOLDOWN_MAX, 8f);
    private float DamageBonus => V(V_DAMAGE_BONUS, 1.0f);
    private bool  IsEvolved   => Stage == CovenantStage.Evolved;

    // ── 이벤트 ──────────────────────────────────────────
    public override void Tick(float deltaTime)
    {
        _eyeCooldown -= deltaTime;
        if (_eyeCooldown > 0f) return;

        _eyeCooldown = CooldownMax;
        FireEyeBeam();
    }

    // ── 내부 ────────────────────────────────────────────
    private void FireEyeBeam()
    {
        // TODO: 플레이어 전방 방향으로 관통 레이캐스트 또는 라인 피해 판정
        // 적중한 각 적에게 고정 피해 * DamageBonus 적용
        // IsEvolved: 적중 적에게 "발로르의 저주" 마커 부여
    }

    public override void OnKill(GameObject target)
    {
        if (!IsEvolved) return;

        // TODO: target이 저주 마커를 보유하면 주변 범위 폭발 발동 (연쇄 전파)
    }
}
