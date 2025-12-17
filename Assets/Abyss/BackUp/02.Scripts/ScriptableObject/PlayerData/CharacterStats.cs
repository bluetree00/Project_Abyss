[System.Serializable]
public class CharacterStats
{
    public float baseMoveSpeed;
    public float baseRunSpeed;
    public int maxHealth;
    public int attackPower;
    public int attackComboStep = 0;       // 공격 스택 단계
    public float comboTimer = 0.0f;       // 콤보 유지 시간
    public float comboDuration = 3.0f;     // 콤보가 유지되는 시간
    public bool canDodge = true;          // 대시 가능 여부
    public float dashSpeed = 10f;          // 대시 속도
    public float dashDuration = 0.2f;      // 대시 지속 시간
    public float dodgeCooldown = 2f;       // 대시 쿨타임
}
