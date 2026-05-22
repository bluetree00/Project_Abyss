/// <summary>
/// 런 중 적용된 시너지 효과 1건의 기록.
/// GameRunSession이 source of truth로 보유하며,
/// 씬 전환 시 PlayerRuntimeStats에 재적용하는 데 사용한다.
/// </summary>
[System.Serializable]
public sealed class SynergyRecord
{
    public string gridId;
    public string effectType;   // MeleeAttack, RangedAttack, AttackPower, Defense, MaxHp, ...
    public string trigger;      // "Always", "OnHit", "OnLowHp"
    public float  value;
    public float  value2;       // OnLowHp threshold 등
    public int    maxStack;     // OnHit 용
    public float  duration;     // OnHit 스택 지속 시간
}
