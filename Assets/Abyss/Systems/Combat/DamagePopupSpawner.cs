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

    private static readonly Queue<DamagePopup> _pool = new();
    private static GameObject _prefab;
    private static bool       _loading;
    private static Transform  _root;

    private static readonly Dictionary<ElementType, Color> _elementColors = new()
    {
        { ElementType.None,      Color.white },
        { ElementType.Lightning, new(1.00f, 0.92f, 0.23f, 1f) },
        { ElementType.Water,     new(0.13f, 0.59f, 0.95f, 1f) },
        { ElementType.Fire,      new(0.96f, 0.26f, 0.21f, 1f) },
        { ElementType.Grass,     new(0.30f, 0.69f, 0.31f, 1f) },
        { ElementType.Earth,     new(0.55f, 0.43f, 0.39f, 1f) },
    };

    /// <summary>임의 위치에 데미지 숫자 스폰. 인자 부족하면 무동작.</summary>
    public static void Spawn(Vector3 worldPos, float damage, bool isCrit = false, ElementType element = ElementType.None)
    {
        if (damage <= 0f) return;
        SpawnAsync(worldPos, damage, isCrit, element).Forget();
    }

    private static async UniTaskVoid SpawnAsync(Vector3 worldPos, float damage, bool isCrit, ElementType element)
    {
        await EnsurePrefabAsync();
        if (_prefab == null) return;

        var popup = GetFromPool();
        if (popup == null) return;

        Color color = element.IsValid() && _elementColors.TryGetValue(element, out var c) ? c : Color.white;
        popup.Show(worldPos, damage, isCrit, color);
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
            {
                var go = Object.Instantiate(_prefab, _root);
                go.SetActive(false);
                if (go.TryGetComponent<DamagePopup>(out var p))
                    _pool.Enqueue(p);
            }
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
        // 여유 있는 인스턴스 찾기
        for (int i = 0; i < _pool.Count; i++)
        {
            var p = _pool.Dequeue();
            _pool.Enqueue(p);
            if (p != null && !p.gameObject.activeSelf) return p;
        }

        // 모두 사용 중이면 새로 생성
        if (_prefab != null && _root != null)
        {
            var go = Object.Instantiate(_prefab, _root);
            go.SetActive(false);
            if (go.TryGetComponent<DamagePopup>(out var p))
            {
                _pool.Enqueue(p);
                return p;
            }
        }
        return null;
    }
}
