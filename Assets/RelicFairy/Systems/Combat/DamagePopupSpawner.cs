using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// DamagePopup 스폰 단일 진입점.
/// 첫 호출 시 Addressable 프리팹을 캐시 + 풀링.
/// </summary>
public static class DamagePopupSpawner
{
    private const string PrefabKey = "DamagePopup";
    private const int    PoolSize  = 16;

    // free 큐: 반환된(비활성) 인스턴스만 보관 → GetFromPool 은 O(1) 디큐.
    // 완료 시 DamagePopup 이 ReturnToPool 콜백으로 스스로 재입큐한다.
    private static readonly Queue<DamagePopup> _pool = new();
    private static GameObject _prefab;
    private static bool       _loading;
    private static Transform  _root;

    // 도메인 리로드 비활성(에디터) 시 정적 상태가 새 플레이세션으로 새지 않도록 초기화.
    // 미리셋 시: 2회차에 _prefab!=null 잔존 → EnsurePrefabAsync 조기탈출 → EnsureRoot 미도달,
    // _root 는 파괴(Unity-null) → CreatePooled 가 항상 null → 데미지 팝업 사망.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _pool.Clear();
        _prefab  = null;
        _root    = null;
        _loading = false;
    }

    /// <summary>임의 위치에 데미지 숫자 스폰. 인자 부족하면 무동작.</summary>
    public static void Spawn(Vector3 worldPos, float damage, bool isCrit = false)
    {
        if (damage <= 0f) return;
        SpawnAsync(worldPos, damage, isCrit).Forget();
    }

    private static async UniTaskVoid SpawnAsync(Vector3 worldPos, float damage, bool isCrit)
    {
        await EnsurePrefabAsync();
        if (_prefab == null) return;

        var popup = GetFromPool();
        if (popup == null) return;

        popup.Show(worldPos, damage, isCrit, Color.white);
    }

    private static async UniTask EnsurePrefabAsync()
    {
        if (_prefab != null) return;

        // 동시 호출 방지
        while (_loading) await UniTask.Yield();
        if (_prefab != null) return;

        _loading = true;
        try
        {
            _prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(PrefabKey);
            if (_prefab == null)
            {
                Debug.LogWarning($"[DamagePopupSpawner] '{PrefabKey}' Addressable 로드 실패");
                return;
            }

            EnsureRoot();
            for (int i = 0; i < PoolSize; i++)
                CreatePooled();
        }
        finally { _loading = false; }
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;
        var go = new GameObject("[DamagePopupRoot]");
        Object.DontDestroyOnLoad(go);
        _root = go.transform;
    }

    private static DamagePopup GetFromPool()
    {
        // free 큐에서 즉시 꺼냄(O(1)). 파괴된 잔여는 건너뜀.
        while (_pool.Count > 0)
        {
            var p = _pool.Dequeue();
            if (p != null) return p;
        }

        // 동시 표시가 풀 크기를 넘으면 1개 추가 생성(완료 시 콜백으로 free 큐에 회수됨).
        return CreatePooled(returnInstance: true);
    }

    /// <summary>풀 인스턴스 1개 생성 + 완료 콜백 연결. returnInstance=true 면 큐에 넣지 않고 즉시 반환(체크아웃).</summary>
    private static DamagePopup CreatePooled(bool returnInstance = false)
    {
        if (_prefab == null || _root == null) return null;

        var go = Object.Instantiate(_prefab, _root);
        go.SetActive(false);
        if (!go.TryGetComponent<DamagePopup>(out var p)) return null;

        p.SetReleaseCallback(static released => _pool.Enqueue(released));
        if (!returnInstance) _pool.Enqueue(p);
        return p;
    }
}
