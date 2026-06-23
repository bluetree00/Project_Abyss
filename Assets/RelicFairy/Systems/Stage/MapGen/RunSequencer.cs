using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>출구 문이 가리키는 방의 종류.</summary>
public enum RoomPlanKind
{
    Normal,
    Elite,
    Shop,
    Event,
    PreBoss,
    Boss,
}

/// <summary>한 출구 문의 계획. 종류 + 선택된 방 템플릿(entry).</summary>
public struct DoorPlan
{
    public RoomPlanKind  kind;
    public ZonePoolEntry entry; // 카테고리/키로 선택된 풀 엔트리. 없으면 null.
}

/// <summary>
/// 런 시작 시 1회 산출하는 명시적 일정표(itinerary). visitIndex(깊이)별 출구 종류를 보유.
/// masterSeed + RunStructureConfig의 순수 함수 — 직렬화하지 않고 이어하기 시 재생성한다.
/// KIND는 경로 독립(깊이 결정적)이라 선형 배열로 표현; ENTRY(구체 템플릿)는 쿨다운으로 경로 의존이라 담지 않는다.
/// </summary>
public sealed class RunPlan
{
    /// <summary>한 깊이의 챔버 계획. visitIndex 방을 떠날 때 열리는 출구들의 종류(1~2).</summary>
    public readonly struct Chamber
    {
        public readonly int            visitIndex;
        public readonly RoomPlanKind[] exitKinds;
        public Chamber(int visitIndex, RoomPlanKind[] exitKinds)
        {
            this.visitIndex = visitIndex;
            this.exitKinds  = exitKinds;
        }
    }

    private readonly List<Chamber> _chambers = new();
    public IReadOnlyList<Chamber> Chambers => _chambers;

    internal void Add(int visitIndex, List<DoorPlan> exits)
    {
        var kinds = new RoomPlanKind[exits.Count];
        for (int i = 0; i < exits.Count; i++) kinds[i] = exits[i].kind;
        _chambers.Add(new Chamber(visitIndex, kinds));
    }
}

/// <summary>
/// 절차적 런 진행 시퀀서. 방 풀 + RunStructureConfig로 매 클리어마다 출구 문 계획을 롤한다.
/// 고정 슬롯맵(chapter_map)을 대체. 물리 배치/연결/회전은 호출자(연결 파이프라인)가 담당한다.
/// 플레인 C# 클래스 — GameRunSession이 소유.
/// </summary>
public class RunSequencer
{
    private const float DifficultyTolerance = 0.35f;

    private enum Phase { Normal, PreBoss, Boss, Done }

    private readonly List<ZonePoolEntry>     _pool;
    private readonly IRunStructure           _config;
    private readonly int                     _seed;
    private readonly Dictionary<string, int> _cooldowns = new();

    private Phase _phase = Phase.Normal;
    private int   _visitCount;
    private int   _shopUsed;
    private int   _eventUsed;

    public int  VisitCount     => _visitCount;
    public bool InBossApproach => _phase != Phase.Normal;
    public bool IsDone         => _phase == Phase.Done;

    // ── 이어하기 직렬화용 상태 노출 ──
    public int Seed      => _seed;
    public int PhaseInt  => (int)_phase;
    public int ShopUsed  => _shopUsed;
    public int EventUsed => _eventUsed;
    public IReadOnlyDictionary<string, int> Cooldowns => _cooldowns;

    /// <summary>방별 자식 시드. 같은 (마스터 시드, visitCount) → 동일 롤 → 이어하기 재현.</summary>
    public static int Combine(int seed, int visitCount) => unchecked((seed * 397) ^ visitCount);

    public RunSequencer(IEnumerable<ZonePoolEntry> pool, IRunStructure config, int seed)
    {
        _pool   = pool != null ? new List<ZonePoolEntry>(pool) : new List<ZonePoolEntry>();
        _config = config;
        _seed   = seed;
    }

    /// <summary>이어하기: 저장된 시퀀서 진행 상태를 복원한다.</summary>
    public void RestoreState(int visitCount, int phase, int shopUsed, int eventUsed,
                             IEnumerable<CooldownKV> cooldowns)
    {
        _visitCount = visitCount;
        _phase      = (Phase)phase;
        _shopUsed   = shopUsed;
        _eventUsed  = eventUsed;
        _cooldowns.Clear();
        if (cooldowns != null)
            foreach (var c in cooldowns)
                if (!string.IsNullOrEmpty(c.key)) _cooldowns[c.key] = c.turns;
    }

    // ── Public ──────────────────────────────────────

    /// <summary>런 시작 시 1회 호출. 마스터 시드+config로 보스 도달까지 전 깊이의 출구 종류를 미리 산출한다.
    /// 라이브 상태를 건드리지 않도록 동일 (pool/config/seed) 클론을 만들어 기존 RollExits 로직을 그대로 재생한다 —
    /// 단일 결정 로직(RollExits)을 공유하므로 라이브 진행과 종류가 구조적으로 일치한다(드리프트 불가).
    /// KIND는 경로 독립(깊이 결정적)이라 어느 문을 커밋해도 다음 깊이 종류가 동일하다.</summary>
    public RunPlan BuildPlan()
    {
        var plan = new RunPlan();
        var sim  = new RunSequencer(_pool, _config, _seed);
        int guard = 0;
        while (!sim.IsDone && guard++ < 256)
        {
            var exits = sim.RollExits();
            if (exits.Count == 0) break;
            plan.Add(sim.VisitCount, exits);
            sim.CommitEntry(exits[0]); // 깊이 진행용 — 종류는 선택 문과 무관(경로 독립)
        }
        return plan;
    }

    /// <summary>현재 방 클리어 시 출구 문 계획 산출. 보스 어프로치에선 단일 문.</summary>
    public List<DoorPlan> RollExits()
    {
        var result = new List<DoorPlan>(2);

        // 런 종료(보스 처치 등) — 더 이상 출구 없음
        if (_phase == Phase.Done)
            return result;

        // 보스 피날레 게이팅 — 확정 마일스톤(visitCount>=BossThreshold). 키 비면 카테고리로 폴백.
        if (_phase == Phase.Boss)
        {
            result.Add(new DoorPlan { kind = RoomPlanKind.Boss, entry = BossEntry(_config?.BossRoomKey, RoomPlanKind.Boss) });
            return result;
        }
        if (_phase == Phase.PreBoss
            || (_phase == Phase.Normal && _config != null && _visitCount >= _config.BossThreshold))
        {
            _phase = Phase.PreBoss;
            result.Add(new DoorPlan { kind = RoomPlanKind.PreBoss, entry = BossEntry(_config?.PreBossRoomKey, RoomPlanKind.PreBoss) });
            return result;
        }

        // 방별 자식 RNG — 같은 (시드, visitCount)면 동일 출구 (이어하기 재현)
        var rng = new System.Random(Combine(_seed, _visitCount));

        // 확정 마일스톤: 진입할 방 순번(_visitCount+1)에 강제 종류가 있으면 확률형을 덮어쓴다.
        // Boss/PreBoss는 BossThreshold 게이팅이 담당하므로 마일스톤 종류로 와도 무시(null 처리).
        RoomPlanKind? forced = _config?.GetMilestoneKind(_visitCount + 1);
        if (forced == RoomPlanKind.Boss || forced == RoomPlanKind.PreBoss) forced = null;
        if (forced.HasValue) NoteForcedSpecial(forced.Value); // 상점/이벤트 캡 카운터에 반영(확률형과 합산)

        // 일반 페이즈 — 2슬롯(직진/턴). 특수방(상점/이벤트)은 한 문쌍 최대 1개.
        // 마일스톤 강제 시 두 출구 모두 해당 종류로 확정(선택지가 아니라 보장된 배치).
        bool specialUsed = false;
        for (int i = 0; i < 2; i++)
        {
            var kind = forced ?? RollKind(rng, ref specialUsed);
            result.Add(MakeDoor(rng, kind));
        }
        return result;
    }

    /// <summary>문 통과로 다음 방 진입 확정 시 호출 — visitCount 증가, 쿨다운 갱신, 페이즈 전이.</summary>
    public void CommitEntry(DoorPlan chosen)
    {
        _visitCount++;
        TickCooldowns();

        if (chosen.entry != null && !string.IsNullOrEmpty(chosen.entry.pool_key))
            _cooldowns[chosen.entry.pool_key] = CooldownFor(chosen.kind);

        if (chosen.kind == RoomPlanKind.PreBoss) _phase = Phase.Boss;
        else if (chosen.kind == RoomPlanKind.Boss) _phase = Phase.Done;
    }

    // ── Private ─────────────────────────────────────

    /// <summary>종류에 맞는 방 템플릿을 골라 DoorPlan을 만든다. 템플릿이 풀에 없으면 Normal로 안전 폴백
    /// (게이트가 빈 목적지를 가리켜 소프트락 나는 것을 방지). kind/entry를 함께 강등해 라벨·색도 일치.</summary>
    private DoorPlan MakeDoor(System.Random rng, RoomPlanKind kind)
    {
        var entry = PickEntry(rng, kind);
        if (entry == null && kind != RoomPlanKind.Normal)
        {
            Debug.LogWarning($"[RunSequencer] '{kind}' 방 템플릿 없음 — Normal 폴백 (visit={_visitCount + 1}). 템플릿 추가 필요.");
            kind  = RoomPlanKind.Normal;
            entry = PickEntry(rng, kind);
        }
        return new DoorPlan { kind = kind, entry = entry };
    }

    /// <summary>강제(마일스톤) 상점/이벤트도 챕터 캡 카운터에 반영 — 확률형 추가 발생을 억제한다.</summary>
    private void NoteForcedSpecial(RoomPlanKind kind)
    {
        if (kind == RoomPlanKind.Shop)       _shopUsed++;
        else if (kind == RoomPlanKind.Event) _eventUsed++;
    }

    /// <summary>보스/보스전방 템플릿 해석: 지정 pool_key 우선, 비었거나 못 찾으면 카테고리 첫 항목으로 폴백.
    /// 풀마다 Boss/PreBoss가 정확히 1개라 카테고리 폴백이 결정적 — 챕터별 키 없이 단일 config 공유 가능.</summary>
    private ZonePoolEntry BossEntry(string poolKey, RoomPlanKind kind)
        => FindByKey(poolKey) ?? FirstByCategory(CategoryName(kind));

    private ZonePoolEntry FirstByCategory(string category)
        => _pool.Find(p => string.Equals(p.category, category, StringComparison.OrdinalIgnoreCase));

    private RoomPlanKind RollKind(System.Random rng, ref bool specialUsed)
    {
        if (_config == null) return RoomPlanKind.Normal;

        if (!specialUsed && _shopUsed < _config.ShopMaxPerChapter && Roll(rng, _config.ShopChance))
        {
            _shopUsed++; specialUsed = true; return RoomPlanKind.Shop;
        }
        if (!specialUsed && _eventUsed < _config.EventMaxPerChapter && Roll(rng, _config.EventChance))
        {
            _eventUsed++; specialUsed = true; return RoomPlanKind.Event;
        }
        if (Roll(rng, _config.EliteChance)) return RoomPlanKind.Elite;
        return RoomPlanKind.Normal;
    }

    private ZonePoolEntry PickEntry(System.Random rng, RoomPlanKind kind)
    {
        string category = CategoryName(kind);
        var byCategory = _pool.FindAll(p => string.Equals(p.category, category, StringComparison.OrdinalIgnoreCase));
        if (byCategory.Count == 0) return null;

        // 쿨다운 미적용 우선
        var available = byCategory.FindAll(p => !IsOnCooldown(p.pool_key));
        if (available.Count == 0) available = byCategory;

        // 난이도 윈도로 추가 좁히기 (충족 후보 없으면 무시)
        float target = _config != null ? _config.DifficultyAt(_visitCount) : 0f;
        var windowed = available.FindAll(p => Mathf.Abs(p.difficulty_scale - target) <= DifficultyTolerance);
        var finalSet = windowed.Count > 0 ? windowed : available;

        return finalSet[rng.Next(finalSet.Count)];
    }

    private ZonePoolEntry FindByKey(string poolKey)
    {
        if (string.IsNullOrEmpty(poolKey)) return null;
        return _pool.Find(p => string.Equals(p.pool_key, poolKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string CategoryName(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Normal  => "Normal",
        RoomPlanKind.Elite   => "Elite",
        RoomPlanKind.Shop    => "Shop",
        RoomPlanKind.Event   => "Event",
        RoomPlanKind.PreBoss => "PreBoss",
        RoomPlanKind.Boss    => "Boss",
        _                    => "Normal",
    };

    private static bool Roll(System.Random rng, float chance) => rng.NextDouble() < chance;

    private bool IsOnCooldown(string key)
        => !string.IsNullOrEmpty(key) && _cooldowns.TryGetValue(key, out int cd) && cd > 0;

    private int CooldownFor(RoomPlanKind kind)
    {
        // PickWithCooldown 방식: 같은 카테고리 풀 크기의 40%, 최소 2턴
        string cat = CategoryName(kind);
        int catCount = _pool.FindAll(p => string.Equals(p.category, cat, StringComparison.OrdinalIgnoreCase)).Count;
        return Math.Max(2, (int)(catCount * 0.4f));
    }

    private void TickCooldowns()
    {
        var keys = new List<string>(_cooldowns.Keys);
        foreach (var k in keys)
            if (_cooldowns[k] > 0) _cooldowns[k]--;
    }
}
