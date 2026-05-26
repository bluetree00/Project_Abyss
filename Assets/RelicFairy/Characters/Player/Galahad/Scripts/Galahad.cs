/// <summary>
/// 유물 1 — 성배의 수호자 (갈라하드)
/// 스타일: 방어형 / 초보 친화 / 진입장벽 낮음
///
/// 패시브:
///   빛의 반사 — 피격 시 10% 피해 경감 + 방어력 비례 반사 피해 (공격자에게)
/// Q스킬:
///   성스러운 방패 — 전방 피해 완전 차단 가드. 블록 성공 시 반사 발동.
/// </summary>
public class Galahad : PlayerController
{
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
        RegisterPassive(new GalahadLightReflectionPassive());
    }

    protected override void RouteInputsToLayers()
    {
        DefaultRouteInputsToLayers();
    }

    // ── 캐릭터 고유 스킬 ─────────────────────────────────────────────────────

    public override ISkillRuntime CreateCharacterSkillRuntime(SkillType slot)
    {
        if (slot == SkillType.Q) return new HolyShieldSkillRuntime();
        return null;
    }

    public override float GetCharacterSkillCooldown(SkillType slot)
    {
        if (slot == SkillType.Q) return 18f;
        return 0f;
    }
}
