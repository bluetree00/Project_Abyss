using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 몬스터 HP 바 오브젝트 풀 매니저.
/// 프리팹은 Addressables에 "LeeHPBar" 키로 등록.
/// </summary>
public class LeeMonsterHPBarManager
{
    private const string AddressableKey = "LeeHPBar";

    private readonly Queue<LeeMonsterHPBar> _pool = new();
    private GameObject _prefab;      // 첫 로드 후 캐싱
    private Transform  _poolRoot;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공개 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>몬스터에게 HP 바를 할당하고 반환한다.</summary>
    /// <param name="headBone">Head 본 Transform. null이면 콜라이더 상단 기준 폴백.</param>
    public async UniTask<LeeMonsterHPBar> RequestHPBarAsync(MonoBehaviour monster, int currentHp, int maxHp, Transform headBone, float headOffset = 0.1f)
    {
        var bar = await GetFromPoolAsync();
        if (bar == null)
        {
            Debug.LogError($"[LeeMonsterHPBarManager] HP 바 프리팹 로드 실패. Addressables에 '{AddressableKey}' 키로 등록됐는지 확인하세요.");
            return null;
        }

        bar.Link(monster, currentHp, maxHp, headBone, headOffset);
        return bar;
    }

    /// <summary>HP 바를 몬스터와 분리하고 풀에 반환한다.</summary>
    public void ReturnHPBar(LeeMonsterHPBar bar)
    {
        if (bar == null) return;
        bar.Unlink();
        bar.transform.SetParent(GetPoolRoot(), false);
        _pool.Enqueue(bar);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTask<LeeMonsterHPBar> GetFromPoolAsync()
    {
        while (_pool.Count > 0)
        {
            var bar = _pool.Dequeue();
            if (bar != null) return bar;
        }

        return await CreateNewBarAsync();
    }

    private async UniTask<LeeMonsterHPBar> CreateNewBarAsync()
    {
        // 프리팹 캐싱 (최초 1회만 Addressables 로드)
        if (_prefab == null)
            _prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(AddressableKey);

        if (_prefab == null) return null;

        GameObject go = Object.Instantiate(_prefab, GetPoolRoot());
        go.name = "LeeMonsterHPBar";

        var canvas = go.GetOrAddComponent<Canvas>();
        canvas.renderMode  = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        return go.GetOrAddComponent<LeeMonsterHPBar>();
    }

    private Transform GetPoolRoot()
    {
        if (_poolRoot != null) return _poolRoot;

        GameObject poolGo = new GameObject("@MonsterHPBarPool");
        Object.DontDestroyOnLoad(poolGo);
        _poolRoot = poolGo.transform;
        return _poolRoot;
    }
}
