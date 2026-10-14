using UnityEngine;

/// <summary>
/// 데미지 정보 패킷. Pre 단계에서 ref로 전달하여 효과가 수정 가능.
/// </summary>
public struct DamagePacket
{
    public float BaseDamage;
    public float FinalDamage;
    public GameObject Attacker;
    public GameObject Target;
    public bool Negated;           // true면 데미지 0으로 처리
    public bool IsCrit;            // 크리티컬 발생 여부

    public DamagePacket(float damage, GameObject attacker = null, GameObject target = null)
    {
        BaseDamage = damage;
        FinalDamage = damage;
        Attacker = attacker;
        Target = target;
        Negated = false;
        IsCrit = false;
    }
}

/// <summary>
/// 데미지 적중 확정 후 결과. Post 단계에서 읽기 전용으로 전달.
/// </summary>
public struct DamageReport
{
    public float DamageDealt;
    public GameObject Attacker;
    public GameObject Target;
    public bool WasKill;
    public bool IsCrit;            // 치명타 여부 (룬 OnCrit 라우팅에 사용)
    public Vector3 HitPosition;
    public WeaponActionType ActionType;   // 공격 종류(스킬 특화 파츠가 심판의 일격 등 식별에 사용)
    public bool IsRanged;          // 원거리 명중(화살 · 볼트) — 사격 계열 발동이 센다
    public float ProcCoef;         // 발동 계수(10-14 A1) — 확률 · 상태 지속 · 포이즈에 곱한다. 0 = 옛 경로(1로 본다)
    public bool IsHeavy;           // 강타(발동 계수 ≥ 문턱 · 10-14 A2) — 강타 발동 룬이 센다
}
