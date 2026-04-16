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
    public WeaponElement Element;
    public bool Negated;           // true면 데미지 0으로 처리

    public DamagePacket(float damage, GameObject attacker = null, GameObject target = null, WeaponElement element = WeaponElement.None)
    {
        BaseDamage = damage;
        FinalDamage = damage;
        Attacker = attacker;
        Target = target;
        Element = element;
        Negated = false;
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
    public WeaponElement Element;
    public bool WasKill;
    public Vector3 HitPosition;
}
