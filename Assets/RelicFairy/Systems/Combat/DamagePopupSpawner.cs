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

    // ── 영수증 줄 ────────────────────────────────────────────────
    // 같은 대상(× 주/추가)의 새 숫자는 맨 아래에 찍히고, 떠 있는 앞 숫자들이 한 칸씩 밀려 올라간다(10-23).
    // 예전엔 순번만큼 위에 늦춰 찍었다 — 늦게 찍힌 숫자가 떠오르던 앞 숫자를 뚫고 지나가 줄이 엉켰다.
    // 떠 있는 팝업 목록 — 같은 줄을 찾아 민다(풀 크기 48 안팎이라 훑어도 싸다).
    private static readonly List<DamagePopup> _live = new();

    // ── 다단히트 숫자 합산 ────────────────────────────────────────
    // 분열 투사체 20발처럼 <b>같은 순간에</b> 꽂히는 피해는 숫자를 따로 띄워봐야 겹쳐서 못 읽는다.
    // 같은 대상에 창(MergeWindow) 안으로 다시 꽂히면 이미 떠 있는 팝업의 값을 올린다.
    //
    // 창은 <b>눈이 숫자를 분리하지 못하는 구간</b>에만 걸어야 한다 — 사람이 두세 자리를 읽는 데
    // 0.25초쯤 걸리므로, 그보다 느리게 들어오는 타격은 각자 보여주는 게 맞다.
    // 그래서 0.12초로 잡는다: 동시 타격은 합치고, 읽을 수 있는 간격의 연타(랜슬롯 Q 0.17초)는 건드리지 않는다.
    private const float MergeWindow = 0.12f;

    // 추가 피해 이어 찍힘(11-02 Q29) — 같은 프레임 · 같은 대상의 추가 피해 순번
    private const float BonusStagger = 0.05f;
    private static int _bonusFrame = -1, _bonusTarget, _bonusSeq;

    private struct Merge { public DamagePopup popup; public float total; public float lastTime; public bool crit; }
    private static readonly Dictionary<int, Merge> _merges = new();

    /// <summary>
    /// 합산 항목이 이만큼 쌓이면 만료분을 걷어낸다. 한 번 맞고 다시 안 맞는 몹은
    /// 스스로 지워질 계기가 없어(다음 타가 와야 만료 판정이 돈다) 런 내내 남는다.
    /// </summary>
    private const int MergePruneAt = 64;
    private static readonly List<int> _pruneBuf = new();

    // ── 누적(영수증 합계 · 10-31) ─────────────────────────────────
    // 사용자 「누적 데미지가 계속 올라오는 느낌」 — 정예 · 보스(· 허수아비)에 창(ChainWindow) 안으로 이어 맞힌 피해를 모아
    // 줄 왼쪽 하나의 「누적」 숫자로 세어 올린다. 주 · 추가 피해 모두 더한다. 두 번째 타부터 뜬다(한 방은 그 숫자가 곧 합계).
    // 창을 넘기면 흐려지고 다음 타부터 새로 센다. 일반 몹은 금방 쓰러지고 여럿이 맞으면 화면이 누적으로 덮여 뺀다.
    private const float ChainWindow = 1.2f;

    private struct Chain { public DamagePopup popup; public float total; public float lastTime; public int hits; public bool pending; }
    private static readonly Dictionary<int, Chain> _chains = new();

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
        _live.Clear();
        _merges.Clear();
        _chains.Clear();
        _bonusFrame = -1;
        _prefab  = null;
        _root    = null;
        _loading = false;
    }

    /// <summary>
    /// 임의 위치에 데미지 숫자 스폰. 인자 부족하면 무동작.
    /// targetId: 같은 대상에 꽂힌 연타를 한 줄로 쌓기 위한 식별자(보통 GetInstanceID()). 0이면 줄 없음.
    /// kind: 피해 출처 — 색으로 구분된다(일반/시너지/DoT).
    /// showTotal: 이어 맞힌 피해를 「누적」으로 세어 올린다(정예 · 보스 · 허수아비). targetId가 있어야 한다.
    /// </summary>
    public static void Spawn(Vector3 worldPos, float damage, bool isCrit = false, int targetId = 0,
                             DamageKind kind = DamageKind.Normal, RuneElement? element = null,
                             bool merge = true, bool showTotal = false, BonusSource source = default)
    {
        if (damage <= 0f) return;
        if (GameRunBootstrapper.Instance?.Run?.InCutscene == true) return;   // 컷신 동안 떠오르는 글은 숨긴다(10-01 f5)

        if (showTotal && targetId != 0) AddToChain(worldPos, damage, targetId);

        // 같은 대상에 연달아 꽂히면 이미 떠 있는 숫자를 키운다 — 새 팝업을 만들지 않는다.
        if (merge && targetId != 0 && TryMerge(MergeKey(targetId, kind, source), damage, isCrit, kind, element)) return;

        SpawnAsync(worldPos, damage, isCrit, kind, element, targetId, source, BonusDelay(kind, targetId)).Forget();
    }

    /// <summary>
    /// 추가 피해(Bonus)는 주 숫자 뒤에 이어 찍힌다 — 같은 프레임 · 같은 대상의 n번째 추가 피해는 n × <see cref="BonusStagger"/>초 늦게(11-02 Q29 「피해량이 많이 보이게」).
    /// </summary>
    private static float BonusDelay(DamageKind kind, int targetId)
    {
        if (kind != DamageKind.Bonus) return 0f;
        if (Time.frameCount != _bonusFrame || targetId != _bonusTarget) { _bonusFrame = Time.frameCount; _bonusTarget = targetId; _bonusSeq = 0; }
        return BonusStagger * ++_bonusSeq;
    }

    /// <summary>
    /// 창 안의 기존 팝업에 피해를 더한다. 성공하면 true(새 스폰 불필요).
    /// 팝업이 이미 수명을 다했거나(풀 반환) 창을 넘겼으면 false — 그때는 새로 띄운다.
    /// </summary>
    private static bool TryMerge(int targetId, float damage, bool isCrit,
                                 DamageKind kind, RuneElement? element)
    {
        if (!_merges.TryGetValue(targetId, out var m)) return false;

        if (m.popup == null || !m.popup.IsShowing || Time.time - m.lastTime > MergeWindow)
        {
            _merges.Remove(targetId);
            return false;
        }

        m.total   += damage;
        m.lastTime = Time.time;
        m.crit    |= isCrit;                 // 한 번이라도 크리가 섞이면 합산 숫자를 크리로 승격
        _merges[targetId] = m;

        // 값만 올리고 수명을 되감는다 — 쌓이는 동안 숫자가 사라지면 안 된다.
        m.popup.Accumulate(m.total, m.crit, kind, element);
        return true;
    }

    private static async UniTaskVoid SpawnAsync(Vector3 worldPos, float damage, bool isCrit,
                                                DamageKind kind, RuneElement? element, int targetId = 0,
                                                BonusSource source = default, float delay = 0f)
    {
        await EnsurePrefabAsync();
        if (_prefab == null) return;

        var popup = GetFromPool();
        if (popup == null) return;

        // 같은 줄의 앞 숫자들을 한 칸씩 민다 — 새 숫자는 맨 아래.
        int column = targetId != 0 ? ColumnKey(targetId, kind) : 0;
        if (column != 0)
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null && _live[i].IsShowing && _live[i].Column == column) _live[i].Bump();

        popup.Show(worldPos, damage, isCrit, kind, element, column, source, delay);
        _live.Add(popup);

        // 다음 타가 이 팝업에 얹힐 수 있게 등록한다.
        if (targetId != 0)
        {
            if (_merges.Count >= MergePruneAt) PruneMerges();
            _merges[MergeKey(targetId, kind, source)] = new Merge { popup = popup, total = damage, lastTime = Time.time, crit = isCrit };
        }
    }

    /// <summary>
    /// 이어 맞힘에 피해를 더한다 — 창을 넘겼으면 앞 누적을 흐리고 새로 센다. 두 번째 타에서 누적 숫자를 띄우고(첫 타 값부터 세어 올림),
    /// 그 뒤로는 떠 있는 누적의 목표만 올린다.
    /// </summary>
    private static void AddToChain(Vector3 worldPos, float damage, int targetId)
    {
        float now = Time.time;
        _chains.TryGetValue(targetId, out var c);
        if (c.hits > 0 && now - c.lastTime > ChainWindow)
        {
            if (Owns(c.popup, targetId)) c.popup.Retire();
            c = default;
        }

        float before = c.total;
        c.total   += damage;
        c.hits    += 1;
        c.lastTime = now;

        bool spawn = false;
        if (Owns(c.popup, targetId)) c.popup.AddTotal(c.total, c.hits);
        else if (c.hits >= 2 && !c.pending)
        {
            c.popup   = null;
            c.pending = true;
            spawn     = true;
        }

        if (_chains.Count >= MergePruneAt) PruneChains();
        _chains[targetId] = c;
        // 사전에 먼저 써 둔 뒤 띄운다 — 프리팹이 이미 있으면 SpawnTotalAsync가 동기로 이어져 「띄우는 중」을 읽는다.
        if (spawn) SpawnTotalAsync(worldPos, before, targetId).Forget();
    }

    /// <summary>그 팝업이 아직 이 대상의 누적으로 떠 있는가 — 풀에서 다른 숫자로 다시 쓰였으면 아니다.</summary>
    private static bool Owns(DamagePopup popup, int targetId)
        => popup != null && popup.IsShowing && popup.TotalOwner == targetId;

    private static async UniTaskVoid SpawnTotalAsync(Vector3 worldPos, float from, int targetId)
    {
        await EnsurePrefabAsync();
        if (!_chains.TryGetValue(targetId, out var c) || !c.pending) return;

        c.pending = false;
        var popup = _prefab != null ? GetFromPool() : null;
        if (popup != null)
        {
            popup.ShowTotal(worldPos, from, c.total, c.hits, targetId);
            _live.Add(popup);
        }
        c.popup = popup;
        _chains[targetId] = c;
    }

    /// <summary>창을 넘긴 이어 맞힘 제거(띄우는 중인 것은 남긴다).</summary>
    private static void PruneChains()
    {
        float now = Time.time;
        _pruneBuf.Clear();
        foreach (var kv in _chains)
            if (!kv.Value.pending && now - kv.Value.lastTime > ChainWindow) _pruneBuf.Add(kv.Key);

        for (int i = 0; i < _pruneBuf.Count; i++) _chains.Remove(_pruneBuf[i]);
        _pruneBuf.Clear();
    }

    /// <summary>
    /// 합산 열쇠 — 주 피해와 추가 피해(시너지 · DoT)는 따로 합친다. 같은 순간의 추가타가 주 피해 숫자에 섞이면
    /// 색만 바뀌고 따로 맞았는지 보이지 않았다(10-19 ⑭). 보수(~)로 대상 ID와 겹치지 않는 둘째 칸을 만든다.
    /// 추가 피해(Bonus)는 출처마다 따로 합친다(룬 몫 · 증강 몫이 한 숫자로 섞이지 않게 · 11-02 Q29).
    /// </summary>
    private static int MergeKey(int targetId, DamageKind kind, BonusSource source = default)
        => kind == DamageKind.Bonus ? unchecked(~targetId * 397 ^ ((int)source + 11)) : ColumnKey(targetId, kind);

    /// <summary>줄 열쇠 — 주 피해는 가운데 줄, 추가 피해(시너지 · DoT · 보너스)는 모두 오른쪽 한 줄에 쌓인다.</summary>
    private static int ColumnKey(int targetId, DamageKind kind) => kind == DamageKind.Normal ? targetId : ~targetId;

    /// <summary>창을 넘긴 합산 항목 제거. 순회 중 삭제를 피해 키를 모았다가 지운다.</summary>
    private static void PruneMerges()
    {
        float now = Time.time;
        _pruneBuf.Clear();
        foreach (var kv in _merges)
            if (now - kv.Value.lastTime > MergeWindow) _pruneBuf.Add(kv.Key);

        for (int i = 0; i < _pruneBuf.Count; i++) _merges.Remove(_pruneBuf[i]);
        _pruneBuf.Clear();
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

        p.SetReleaseCallback(static released => { _live.Remove(released); _pool.Enqueue(released); });
        if (!returnInstance) _pool.Enqueue(p);
        return p;
    }
}
