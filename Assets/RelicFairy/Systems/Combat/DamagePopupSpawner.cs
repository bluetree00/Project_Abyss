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

    // 연타(랜슬롯 심판 9타 등)가 다수 적에게 동시에 꽂히면 16개로는 즉시 고갈된다.
    private const int    PoolSize  = 48;

    // ── 영수증 캐스케이드 ──────────────────────────────────────────
    // 같은 대상에 이 시간(초) 안에 다시 꽂히면 '연타'로 보고 순번을 올려 위로 쌓는다.
    // 지나면 순번을 0으로 리셋 — 새 공격은 다시 바닥부터 찍힌다.
    private const float CascadeWindow  = 0.7f;
    private const int   CascadeMaxStep = 7;   // 그 이상은 화면 밖으로 나가므로 되감는다

    /// <summary>
    /// 순번 1칸당 스폰을 미루는 시간(초). 분열·다단은 <b>같은 프레임</b>에 수십 발이 꽂히는데,
    /// 그걸 동시에 찍으면 위로 쌓여도 '기둥이 흘러 올라가는' 게 아니라 벽처럼 한꺼번에 박힌다.
    /// 순번만큼 늦춰야 아래에서 위로 흐르며 읽힌다. 순번 0(=서로 다른 대상)은 지연이 없다.
    /// </summary>
    private const float CascadeStagger = 0.04f;

    private struct Cascade { public int index; public float lastTime; }
    private static readonly Dictionary<int, Cascade> _cascades = new();

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
        _cascades.Clear();
        _prefab  = null;
        _root    = null;
        _loading = false;
    }

    /// <summary>
    /// 임의 위치에 데미지 숫자 스폰. 인자 부족하면 무동작.
    /// targetId: 같은 대상에 꽂힌 연타를 위로 쌓기 위한 식별자(보통 GetInstanceID()). 0이면 캐스케이드 없음.
    /// kind: 피해 출처 — 색으로 구분된다(일반/시너지/DoT).
    /// </summary>
    public static void Spawn(Vector3 worldPos, float damage, bool isCrit = false, int targetId = 0,
                             DamageKind kind = DamageKind.Normal, RuneElement? element = null)
    {
        if (damage <= 0f) return;
        // 순번은 '지금' 확정한다 — 지연 뒤에 뽑으면 같은 프레임의 연타가 서로 순번을 덮어쓴다.
        SpawnAsync(worldPos, damage, isCrit, NextCascadeIndex(targetId), kind, element).Forget();
    }

    /// <summary>대상별 연타 순번. 창(CascadeWindow) 안에 다시 맞으면 +1, 지나면 0으로 리셋.</summary>
    private static int NextCascadeIndex(int targetId)
    {
        if (targetId == 0) return 0;

        float now = Time.time;
        int index = 0;

        if (_cascades.TryGetValue(targetId, out var c) && now - c.lastTime <= CascadeWindow)
            index = (c.index + 1) % (CascadeMaxStep + 1);

        _cascades[targetId] = new Cascade { index = index, lastTime = now };
        return index;
    }

    private static async UniTaskVoid SpawnAsync(Vector3 worldPos, float damage, bool isCrit, int cascadeIndex,
                                                DamageKind kind, RuneElement? element)
    {
        await EnsurePrefabAsync();
        if (_prefab == null) return;

        // 순번만큼 늦게 찍는다. 지연 뒤에 풀에서 꺼내므로 대기 중 인스턴스를 붙잡고 있지 않는다.
        if (cascadeIndex > 0)
            await UniTask.Delay(System.TimeSpan.FromSeconds(CascadeStagger * cascadeIndex),
                                DelayType.DeltaTime);

        var popup = GetFromPool();
        if (popup == null) return;

        popup.Show(worldPos, damage, isCrit, kind, cascadeIndex, element);
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
