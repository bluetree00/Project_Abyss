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
/// 절차적 런 진행 시퀀서. 방 풀 + RunStructureConfig로 매 클리어마다 출구 문 계획을 롤한다.
/// 고정 슬롯맵(chapter_map)을 대체. 물리 배치/연결/회전은 호출자(연결 파이프라인)가 담당한다.
/// 플레인 C# 클래스 — GameRunSession이 소유.
/// </summary>
public class RunSequencer
{
    private const float DifficultyTolerance = 0.35f;

    private enum Phase { Normal, PreBoss, Boss, Done }

    private readonly List<ZonePoolEntry>     _pool;
    private readonly RunStructureConfig      _config;
    private readonly System.Random           _rng;
    private readonly Dictionary<string, int> _cooldowns = new();

    private Phase _phase = Phase.Normal;
    private int   _visitCount;
    private int   _shopUsed;
    private int   _eventUsed;

    public int  VisitCount     => _visitCount;
    public bool InBossApproach => _phase != Phase.Normal;
    public bool IsDone         => _phase == Phase.Done;

    public RunSequencer(IEnumerable<ZonePoolEntry> pool, RunStructureConfig config, int seed)
    {
        _pool   = pool != null ? new List<ZonePoolEntry>(pool) : new List<ZonePoolEntry>();
        _config = config;
        _rng    = new System.Random(seed);
    }

    // ── Public ──────────────────────────────────────

    /// <summary>현재 방 클리어 시 출구 문 계획 산출. 보스 어프로치에선 단일 문.</summary>
    public List<DoorPlan> RollExits()
    {
        var result = new List<DoorPlan>(2);

        // 보스 피날레 게이팅
        if (_phase == Phase.Boss)
        {
            result.Add(new DoorPlan { kind = RoomPlanKind.Boss, entry = FindByKey(_config?.BossRoomKey) });
            return result;
        }
        if (_phase == Phase.PreBoss
            || (_phase == Phase.Normal && _config != null && _visitCount >= _config.BossThreshold))
        {
            _phase = Phase.PreBoss;
            result.Add(new DoorPlan { kind = RoomPlanKind.PreBoss, entry = FindByKey(_config?.PreBossRoomKey) });
            return result;
        }

        // 일반 페이즈 — 2슬롯(직진/턴). 특수방(상점/이벤트)은 한 문쌍 최대 1개.
        bool specialUsed = false;
        for (int i = 0; i < 2; i++)
        {
            var kind = RollKind(ref specialUsed);
            result.Add(new DoorPlan { kind = kind, entry = PickEntry(kind) });
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

    private RoomPlanKind RollKind(ref bool specialUsed)
    {
        if (_config == null) return RoomPlanKind.Normal;

        if (!specialUsed && _shopUsed < _config.ShopMaxPerChapter && Roll(_config.ShopChance))
        {
            _shopUsed++; specialUsed = true; return RoomPlanKind.Shop;
        }
        if (!specialUsed && _eventUsed < _config.EventMaxPerChapter && Roll(_config.EventChance))
        {
            _eventUsed++; specialUsed = true; return RoomPlanKind.Event;
        }
        if (Roll(_config.EliteChance)) return RoomPlanKind.Elite;
        return RoomPlanKind.Normal;
    }

    private ZonePoolEntry PickEntry(RoomPlanKind kind)
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

        return finalSet[_rng.Next(finalSet.Count)];
    }

    private ZonePoolEntry FindByKey(string poolKey)
    {
        if (string.IsNullOrEmpty(poolKey)) return null;
        return _pool.Find(p => string.Equals(p.pool_key, poolKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string CategoryName(RoomPlanKind kind) => kind switch
    {
        RoomPlanKind.Normal => "Normal",
        RoomPlanKind.Elite  => "Elite",
        RoomPlanKind.Shop   => "Shop",
        RoomPlanKind.Event  => "Event",
        _                   => "Normal",
    };

    private bool Roll(float chance) => _rng.NextDouble() < chance;

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
