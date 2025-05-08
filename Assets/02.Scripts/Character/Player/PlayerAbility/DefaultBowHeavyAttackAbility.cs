using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(menuName = "Weapon/Abilities/HeavyAttack")]
public class DefaultBowHeavyAttackAbility : HeavyAttackAbilitySO
{
    [Header("차지 공격 설정")]
    public float minChargeTime = 1f;
    public float maxChargeTime = 3f;
    public GameObject chargeEffectPrefab;
    public GameObject chargedAttackEffectPrefab;

    private GameObject chargeEffectInstance;

    public override void HeavyAttackStartCharging(CharacterController character)
    {
        Debug.Log("차지 시작");

        // 이펙트 생성
        if (chargeEffectPrefab != null)
        {
            chargeEffectInstance = Instantiate(chargeEffectPrefab, character.transform.position + Vector3.up, Quaternion.identity);
            chargeEffectInstance.transform.SetParent(character.transform); // 캐릭터에 따라다니게
        }

        // 애니메이션 트리거나 사운드 처리 가능
       // character.Animator?.SetTrigger("ChargeStart");
    }

    public override void HeavyAttackUpdateCharging(CharacterController character, float chargeTime)
    {
        // 이펙트 밝기 조절 또는 게이지 UI 업데이트 등
        if (chargeEffectInstance != null)
        {
            float intensity = Mathf.Clamp01(chargeTime / maxChargeTime);
            chargeEffectInstance.transform.localScale = Vector3.one * (1f + intensity);
        }

        // 차지 시간에 따라 애니메이션 블렌드 등도 가능
    }

    public override void HeavyAttackReleaseChargedAttack(CharacterController character, float chargeTime)
    {
        float clampedCharge = Mathf.Clamp(chargeTime, minChargeTime, maxChargeTime);
        Debug.Log($"차지 완료! 차지 시간: {clampedCharge}");

        // 이펙트 제거
        if (chargeEffectInstance != null)
            Destroy(chargeEffectInstance);

        // 차지 강공격 실행
        if (chargedAttackEffectPrefab != null)
        {
            Vector3 spawnPos = character.transform.position + character.transform.forward * 1.5f;
            Quaternion spawnRot = character.transform.rotation;
            Instantiate(chargedAttackEffectPrefab, spawnPos, spawnRot);
        }

        // 공격 애니메이션
       // character.Animator?.SetTrigger("HeavyAttack");

        // 데미지 계산 등은 상태에서 할 수도 있고 여기서 직접 할 수도 있음
    }

    public override void HeavyAttackCancelCharging(CharacterController character)
    {
        Debug.Log("차지 취소");

        if (chargeEffectInstance != null)
            Destroy(chargeEffectInstance);

       // character.Animator?.SetTrigger("CancelCharge");
    }
}

