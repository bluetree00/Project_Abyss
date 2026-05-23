using UnityEngine;

/// <summary>
/// 유물 1 — 성배의 수호자 (갈라하드)
/// 스타일: 방어형 / 초보 친화 / 진입장벽 낮음
///
/// 패시브:
///   아버지의 죄  — 피격 시 신성 게이지 +15
///   성스러운 방패 — (STUB) 블록 성공 시 피해 반사
/// Q스킬:
///   성배의 빛 — 전방 범위 신성 폭발. 게이지 30+ 시 강화.
/// 고유 메커닉:
///   신성 게이지 — 0~100. 만충 시 5초 공격력 2.5× + 피해감소 30%.
/// </summary>
public class Galahad : PlayerController
{
    private HolyGauge _holyGauge;

    // ── 초기화 ───────────────────────────────────────────────────────────────

    protected override void InitLayerFSMs()
    {
        RegisterDefaultFSMs();
    }

    protected override bool IsInAttackOrSkillState() =>
        base.IsInAttackOrSkillState()       ||
        actSM.CurrentId == ActState.Charge  ||
        actSM.CurrentId == ActState.HeavyAttack;

    protected override void InitPassives()
    {
        _holyGauge = gameObject.AddComponent<HolyGauge>();
        _holyGauge.Initialize(RuntimeStats);

        RegisterPassive(new GalahadFathersGuiltPassive(_holyGauge));
        RegisterPassive(new GalahadHolyShieldPassive());
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }

    // ── 캐릭터 고유 스킬 ─────────────────────────────────────────────────────

    public override ISkillRuntime CreateCharacterSkillRuntime(SkillType slot)
    {
        if (slot == SkillType.Q) return new HolyLightSkillRuntime(_holyGauge);
        return null;
    }

    public override float GetCharacterSkillCooldown(SkillType slot)
    {
        if (slot == SkillType.Q) return 18f;
        return 0f;
    }

    // ── 공개 API ─────────────────────────────────────────────────────────────

    /// <summary>HUD나 UI에서 신성 게이지 상태에 접근할 때 사용.</summary>
    public HolyGauge HolyGauge => _holyGauge;
}
