using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Characters/Character Data")]
public class CharacterData : ScriptableObject
{
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
    public float comboDuration = 3.0f;     // 콤보가 유지되는 시간

    // 대시 관련 수치
    [Header("대시 관련 수치")]
    public bool canDodge = true;          // 대시 가능 여부
    public float dashSpeed = 10f;          // 대시 속도
    public float dashDuration = 0.2f;      // 대시 지속 시간
    public float dodgeCooldown = 2f;       // 대시 쿨타임

    // 특성 관련 수치
    [Header("특성 관련 수치")]
    // 태그별 부스트 정보
    public float attackPowerBoostAmount = 10f;
    public float attackPowerBoostDuration = 5f;

    public float moveSpeedBoostAmount = 10f;
    public float moveSpeedBoostDuration = 3f;

    #region 특성 스탯 부스트 처리 메소드 모음
        
    // 스탯 부스트 처리 메서드
    public void ApplyBoostByTag(MonoBehaviour behaviour, string tag)
    {
        switch (tag)
        {
            case "APBoost":
                behaviour.StartCoroutine(DecayStat("attackPower", attackPowerBoostAmount, attackPowerBoostDuration));
                break;

            case "MSBoost":
                behaviour.StartCoroutine(DecayStat("baseMoveSpeed", moveSpeedBoostAmount, moveSpeedBoostDuration));
                break;

            default:
                Debug.LogWarning($"정의되지 않은 태그: {tag}");
                break;
        }
    }

    private System.Collections.IEnumerator DecayStat(string statName, float boostAmount, float duration)
    {
        float originalValue = GetStatValue(statName);
        if (originalValue == -1f) yield break;

        // 스탯 증가
        SetStatValue(statName, originalValue + boostAmount);

        Debug.Log($"<color=red>{statName} : {boostAmount} 만큼 증가</color>");

        // 점진적으로 감소
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float currentValue = Mathf.Lerp(originalValue + boostAmount, originalValue, elapsed / duration);
            SetStatValue(statName, currentValue);
            yield return null;
        }

        // 최종적으로 원래 값 복원
        SetStatValue(statName, originalValue);
    }

    private float GetStatValue(string statName)
    {
        // 스위치 문으로 각 스탯의 이름으로 수치를 가져오는 함수
        switch (statName)
        {
            case "baseMoveSpeed": return baseMoveSpeed;
            case "baseRunSpeed": return baseRunSpeed;
            case "maxHealth": return maxHealth;
            case "attackPower": return attackPower;
            default:
                Debug.LogWarning($"Stat {statName} not found!");
                return -1f;
        }
    }

    private void SetStatValue(string statName, float value)
    {
        //스위치 문으로 각 스탯을 관리하여 스탯값을 설정하는 함수
        switch (statName)
        {
            case "baseMoveSpeed": baseMoveSpeed = value; break;
            case "baseRunSpeed": baseRunSpeed = value; break;
            case "maxHealth": maxHealth = Mathf.RoundToInt(value); break; // Health는 반올림하여 정수로 캐스팅
            case "attackPower": attackPower = Mathf.RoundToInt(value); break; // 공격력도 반올림하여 정수로 캐스팅
            default:
                Debug.LogWarning($"Stat {statName} not found!");
                break;
        }
    }
    #endregion

    
    
}
