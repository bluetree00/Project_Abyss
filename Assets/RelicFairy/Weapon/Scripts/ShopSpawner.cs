// ShopSpawner.cs
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 상점용 랜덤 스폰 예시
/// - allWeapons: 보여줄 수 있는 모든 WeaponSO (에디터에 등록하거나 DB에서 초기화)
/// - SpawnDisplayPrefabKey: WeaponDisplay 프리팹의 Addressables key (프리팹에는 WeaponDisplay 컴포넌트가 있어야 함)
/// </summary>
public class ShopSpawner : MonoBehaviour
{
    [Header("데이터")]
    public List<WeaponSO> allWeapons = new List<WeaponSO>(); // 에디터에서 미리 채우거나 런타임에 셋업

    [Header("프리팹 Addressable Key")]
    public string displayPrefabAddressableKey; // WeaponDisplay가 붙은 프리팹의 키

    [Header("배치")]
    public Transform slotParent;

    /// <summary>
    /// 지정 개수만큼 랜덤으로 선택해 화면에 띄움
    /// </summary>
    public async UniTask SpawnRandomShopAsync(int count)
    {
        if (string.IsNullOrEmpty(displayPrefabAddressableKey))
        {
            Debug.LogError("ShopSpawner: displayPrefabAddressableKey가 설정되지 않았습니다.");
            return;
        }

        if (allWeapons == null || allWeapons.Count == 0)
        {
            Debug.LogWarning("ShopSpawner: allWeapons가 비어있습니다.");
            return;
        }

        // 랜덤 섞기 (Fisher-Yates)
        var pool = new List<WeaponSO>(allWeapons);
        for (int i = 0; i < pool.Count; i++)
        {
            int r = UnityEngine.Random.Range(i, pool.Count);
            var tmp = pool[i]; pool[i] = pool[r]; pool[r] = tmp;
        }

        int take = Math.Min(count, pool.Count);
        for (int i = 0; i < take; i++)
        {
            var so = pool[i];

            // Addressables로 WeaponDisplay 프리팹 인스턴스 생성
            var handle = Addressables.InstantiateAsync(displayPrefabAddressableKey, slotParent);
            await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Debug.LogError($"ShopSpawner: display 프리팹 인스턴스화 실패 - {displayPrefabAddressableKey}");
                continue;
            }

            var go = handle.Result;
            // WeaponDisplay 컴포넌트 찾기
            var display = go.GetComponent<WeaponDisplay>();
            if (display == null)
            {
                Debug.LogError("ShopSpawner: 생성된 프리팹에 WeaponDisplay 컴포넌트가 없습니다.");
                // 해제(인스턴스) 처리: 호출자가 관리하지 않으면 바로 ReleaseInstance
                Addressables.ReleaseInstance(handle);
                continue;
            }

            // 생성 핸들 전달 -> WeaponDisplay가 관리하게 함
            display.AttachInstantiateHandle(handle);

            // SO로 초기화 (아이콘/텍스트 세팅)
            display.Initialize(so);

            // 클릭 콜백: 획득 동작 연결 (예시)
            display.onClicked = (weaponSo) =>
            {
                // 이 부분은 실제 게임 로직에 맞게 변경하세요 (서버검증, 결제, 인벤토리 체크 등)
                Debug.Log($"플레이어가 상점에서 {weaponSo.displayName}을(를) 클릭했습니다.");

                // WeaponData 생성 및 플레이어에 전달 예시:
                var runtime = new WeaponData(weaponSo);
               // await PlayerWeaponManager.Instance.AcquireWeaponAsync(runtime);
            };
        }
    }

    /// <summary>
    /// 모든 생성된 display를 정리(씬 전환 등에서 호출)
    /// </summary>
    public void ClearAllDisplays()
    {
        if (slotParent == null) return;
        foreach (Transform t in slotParent)
        {
            // slotParent에 있는 인스턴스들은 Addressables.InstantiateAsync로 생성되었으므로
            // Addressables.ReleaseInstance를 사용해야 안전합니다.
            var go = t.gameObject;
            var display = go.GetComponent<WeaponDisplay>();
            if (display != null)
            {
                // WeaponDisplay가 AttachInstantiateHandle로 핸들을 보관하고 있으므로
                // WeaponDisplay.OnDestroy에서 Release되도록 두는 편이 안전합니다.
                Destroy(go);
            }
            else
            {
                Destroy(go);
            }
        }
    }
}
