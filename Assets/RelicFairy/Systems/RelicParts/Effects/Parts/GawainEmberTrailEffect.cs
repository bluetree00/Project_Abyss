using UnityEngine;

/// <summary>
/// gawain_ember_trail(잔염의 검흔) — 태양의 검흔이 사라진 자리에 불씨 지대가 잠깐 남아 밟는 적에게 화상을 준다.
///
/// 근접 타격(Q 스킬 제외) 자리에 짧은 수명의 불씨 지대를 남긴다(쿨다운으로 스폰 빈도 제한).
/// 트레일 궤적 전체가 아니라 타격 지점 기준의 근사 — 밟는 적에게 화상을 주는 동작은 동일하다.
/// </summary>
public sealed class GawainEmberTrailEffect : RelicPartEffect
{
    private const float SpawnCd     = 0.4f;
    private const float FieldRadius = 1.5f;
    private const float FieldLife   = 2.5f;
    private const float DpsRatio    = 0.10f;   // 기본 화상보다 약간 낮게 — 잦은 소형 패치라 누적 고려
    private const float BurnDur     = 1.5f;

    private float _cd;

    public GawainEmberTrailEffect() : base("gawain_ember_trail") { }

    public override void Tick(float dt, PlayerController player)
    {
        if (_cd > 0f) _cd -= dt;
    }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (_cd > 0f || hit.ActionType == WeaponActionType.QSkill) return;   // 근접 검흔만
        _cd = SpawnCd;

        float dps = player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) * DpsRatio;
        FireField.SpawnAt(player.gameObject, hit.HitPoint, FieldRadius, FieldLife, dps, BurnDur);
    }
}
