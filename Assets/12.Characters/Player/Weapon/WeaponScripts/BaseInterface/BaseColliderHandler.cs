using System.Collections;
using UnityEngine;

/// <summary>
/// BaseColliderHandler: WeaponColliderPackageSO 구조에 맞춰 콜라이더를 생성/세팅/비활성화 처리
/// - effectIndex는 기본적으로 comboIndex-1을 사용 (0-based)
/// - 실제 프로젝트에서는 Instantiate 대신 풀링을 권장
/// </summary>
public class BaseColliderHandler : IColliderHandler
{
    /// <summary>
    /// ability: 실행중인 어빌리티
    /// comboIndex: 콤보 인덱스 (1..N). 기본적으로 effectIndex = comboIndex - 1 사용
    /// owner: 소유자(플레이어) Transform (attach 기준)
    /// colliderPackage: WeaponColliderPackageSO (장비의 콜라이더 패키지)
    /// </summary>
    public void ExecuteColliders(WeaponAbilitySO ability, int comboIndex, Transform owner, WeaponColliderPackageSO colliderPackage)
    {
   
        //어빌리티 정보로 콜라이더 정보를 가져와서 생성
    }


}
