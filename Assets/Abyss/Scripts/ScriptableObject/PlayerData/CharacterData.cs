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
    public float comboDuration = 3.0f;

    [Header("강공격 관련 수치")]
    public float heavyAttackChargeThreshold = 1.5f;
    public float heavyAttackReleaseTime = 0.4f;

    // 대시 관련 수치
    [Header("대시 관련 수치")]
    public float dashSpeed = 10f;
    public float dashDuration = 0.2f;
    public float dodgeCooldown = 2f;

    [Header("점프 및 중력 설정")]
    public float jumpForce = 7f;
    public float gravity = -30f;
    public float fallMultiplier = 2f;

    [Header("지면 체크 및 착지 관련")]
    public float groundCheckDistance = 1f;
    public float hardLandingTimeThreshold = 0.8f;
    public LayerMask groundLayer;

    [Header("물리 이동 관련")]
    public float airControlMultiplier = 0.5f;
    public float groundDrag = 4f;
    public float airDrag = 0.5f;

    //캐릭터 클래스
    [Header("캐릭터 클래스")]
    public Define.CharacterClass conClass;

    // 캐릭터 고유 패시브
    [Header("패시브")]
    public PassiveSO passive;

    // 총 공격력 반환 (EffectData 호환)
    public int GetTotalAttackPower() => attackPower;

    // PlayerController.InitCharacterDataAsync에서 호출
    public void Initialize() { }
}
