using UnityEngine;



[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    // 캐릭터 기본 정보
    [Header("캐릭터 기본 정보")]
    public string characterName;

    // 캐릭터 기본 스탯
    [Header("캐릭터 기본 스탯")]
    public float baseMoveSpeed;
    public float baseRunSpeed;
    public int maxHealth;
    public int attackPower;

    // 공격, 콤보 관련 수치
    [Header("공격, 콤보 관련 수치")]
    public int attackComboStep = 0;       // 공격 스택 단계
    public float comboTimer = 0.0f;       // 콤보 유지 시간
    public float comboDuration = 3.0f;   // 콤보가 유지되는 시간

    // 대시 관련 수치
    [Header("대시 관련 수치")]
    public bool canDodge = true;          // 대시 가능 여부
    public float dashSpeed = 10f;         // 대시 속도
    public float dashDuration = 0.2f;     // 대시 지속 시간
    public float dodgeCooldown = 2f;     // 대시 쿨타임

    // 특성 관련 수치
    [Header("특성 관련 수치")]
    public float attackPowerBoostAmount = 10f;
    public float attackPowerBoostDuration = 10f;
    public float moveSpeedBoostAmount = 10f;
    public float moveSpeedBoostDuration = 3f;

       [Header("점프 및 중력 설정")]
    public float jumpForce = 7f;
    public float gravity = -30f;
    public float fallMultiplier = 2f;

    [Header("지면 체크 및 착지 관련")]
    public float groundCheckDistance = 0.3f;
    public float hardLandingTimeThreshold = 0.8f;
    public LayerMask groundLayer;

    [Header("물리 이동 관련")]
    public float airControlMultiplier = 0.5f;
    public float groundDrag = 4f;
    public float airDrag = 0.5f;

    // 초기 스탯 수치
    private float initialBaseMoveSpeed;
    private float initialBaseRunSpeed;
    private int initialMaxHealth;
    private int initialAttackPower;

    // 무기 관련
    [Header("무기 관련")]
    public WeaponData equippedWeapon;
    
    //캐릭터 클래스
    [Header("캐릭터 클래스")]
    public Define.CharacterClass conClass;

    // 총 데미지 계산
    private int totalAttackPower;

    // 총 공격력 가져오기
    public int GetTotalAttackPower()
    {
        return totalAttackPower;
    }

    // 총 공격력 업데이트
    private void UpdateTotalAttackPower()
    {
        totalAttackPower = attackPower;

        if (equippedWeapon != null)
        {
            totalAttackPower += equippedWeapon.CalculateEffectiveAttackPower();
        }
    }

    // 초기화 메서드
    public void Initialize()
    {
        initialBaseMoveSpeed = baseMoveSpeed;
        initialBaseRunSpeed = baseRunSpeed;
        initialMaxHealth = maxHealth;
        initialAttackPower = attackPower;

        UpdateTotalAttackPower(); // 초기화 시 총 공격력 계산
    }

    // 부스트 적용 메서드
    public void ApplyAttackBoost(float boostAmount, float duration)
    {
        attackPower += Mathf.RoundToInt(boostAmount);
        Debug.Log($"공격력이 {boostAmount}만큼 증가했습니다! 지속 시간: {duration}초");

        UpdateTotalAttackPower(); // 부스트 후 총 공격력 재계산
    }

    public void ApplyMoveSpeedBoost(float boostAmount, float duration)
    {
        baseMoveSpeed += boostAmount;
        Debug.Log($"이동 속도가 {boostAmount}만큼 증가했습니다! 지속 시간: {duration}초");
    }

    // 무기 장착 메서드
    public void EquipWeapon(WeaponData newWeapon)
    {
        if (newWeapon == equippedWeapon)
        {
            Debug.Log("같은 무기가 이미 장착되어 있습니다.");
            return;
        }

        equippedWeapon = newWeapon;

        if (newWeapon != null)
        {
            Debug.Log($"{characterName}이(가) {newWeapon.weaponName}을(를) 장착했습니다.");
        }
        else
        {
            Debug.Log($"{characterName}이(가) 무기를 해제했습니다.");
        }

        UpdateTotalAttackPower(); // 무기 장착 또는 해제 시 총 공격력 업데이트
    }

    // 무기 해제 메서드
    public void UnequipWeapon()
    {
        if (equippedWeapon != null)
        {
            Debug.Log($"{characterName}이(가) {equippedWeapon.weaponName}을(를) 해제했습니다.");
            equippedWeapon = null;

            UpdateTotalAttackPower(); // 무기 해제 시 총 공격력 업데이트
        }
        else
        {
            Debug.Log("장착된 무기가 없습니다.");
        }
    }

    //무기 효과를 적용하는 메서드
    public void ApplyWeaponEffectsToDamage(ref float damage)
    {
        if (equippedWeapon != null)
        {
            equippedWeapon.ApplyWeaponEffects(ref damage);
        }
    }
}
