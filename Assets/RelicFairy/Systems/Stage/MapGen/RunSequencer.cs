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
    Crucible,   // 재련소(무기 강화/승급 + 도박) — PRD 확률 등장, 챕터당 1회
    Refinery,   // 정제소(룬 지급/룬판) — PRD 확률 등장, 챕터당 1회
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

    // ── 특수방 등장 확률(PRD) ────────────────────────────────
    // 재련소·정제소는 런 구조 데이터(CSV/SO)에 확률 컬럼이 없다 — 여기 상수가 튜닝 노브다.
    // 상점·이벤트는 기존 config의 ShopChance/EventChance를 baseline으로 그대로 쓴다.
    private const float CrucibleBaseChance = 0.10f;
    private const float RefineryBaseChance = 0.10f;

    /// <summary>한 방에 특수방이 뜰 확률의 상한. 오래 못 만난 타입이 쌓이면 PRD 확률 합이 1을 넘는데,
    /// 상한이 없으면 후반이 특수방으로만 채워져 전투가 사라진다.</summary>
    private const float SpecialSlotCap = 0.55f;

    /// <summary>PRD 대상 특수방 4종. 등장 순서 편향을 막기 위해 매 롤마다 섞어 쓴다.</summary>
    private static readonly RoomPlanKind[] SpecialKinds =
    {
        RoomPlanKind.Shop, RoomPlanKind.Event, RoomPlanKind.Crucible, RoomPlanKind.Refinery,
    };

    private enum Phase { Normal, PreBoss, Boss, Done }

    private readonly List<ZonePoolEntry>     _pool;
    private readonly IRunStructure           _config;
    private readonly int                     _seed;
    private readonly int                     _bossThresholdOverride; // >0이면 테스트용으로 config 값을 대체
    private readonly Dictionary<string, int> _cooldowns = new();

    private Phase _phase = Phase.Normal;
    private int   _visitCount;
    private int   _shopUsed;
    private int   _eventUsed;
    private int   _crucibleUsed;   // 챕터당 1
    private int   _refineryUsed;   // 챕터당 1

    // PRD(의사난수분포) 미출현 누적 — 해당 특수방을 '방문하지 않은' 방 수.
    // 확률 = baseline × (miss+1) 이므로, 안 만나거나 그냥 지나칠수록 다음 방에서 뜰 확률이 올라간다.
    private int   _shopMiss, _eventMiss, _crucibleMiss, _refineryMiss;

    private RoomPlanKind _lastCommittedKind = RoomPlanKind.Normal; // 직전 진입 방(같은 특수방 연속 방지)

    // 특수방 후보 셔플용 재사용 버퍼(롤마다 new 방지)
    private readonly List<RoomPlanKind> _specialBuffer = new(4);

    public int  VisitCount     => _visitCount;
    public bool InBossApproach => _phase != Phase.Normal;
    public bool IsDone         => _phase == Phase.Done;

    // ── 이어하기 직렬화용 상태 노출 ──
    public int Seed          => _seed;
    public int PhaseInt      => (int)_phase;
    public int ShopUsed      => _shopUsed;
    public int EventUsed     => _eventUsed;
    public int CrucibleUsed  => _crucibleUsed;
    public int RefineryUsed  => _refineryUsed;
    public int ShopMiss      => _shopMiss;
    public int EventMiss     => _eventMiss;
    public int CrucibleMiss  => _crucibleMiss;
    public int RefineryMiss  => _refineryMiss;
    public IReadOnlyDictionary<string, int> Cooldowns => _cooldowns;

    /// <summary>방별 자식 시드. 같은 (마스터 시드, visitCount) → 동일 롤 → 이어하기 재현.</summary>
    public static int Combine(int seed, int visitCount) => unchecked((seed * 397) ^ visitCount);

    /// <param name="bossThresholdOverride">0이면 무시(정상). 1 이상이면 config의 BossThreshold 대신 이 값으로
    /// 보스 어프로치를 게이팅한다 — 보스 전방/보스방을 빨리 보기 위한 <b>테스트 전용</b> 값이다.</param>
    public RunSequencer(IEnumerable<ZonePoolEntry> pool, IRunStructure config, int seed,
                        int bossThresholdOverride = 0)
    {
        _pool   = pool != null ? new List<ZonePoolEntry>(pool) : new List<ZonePoolEntry>();
        _config = config;
        _seed   = seed;
        _bossThresholdOverride = bossThresholdOverride;
    }

    /// <summary>실효 보스 임계값. 테스트 오버라이드가 있으면 그것을, 없으면 런 구조(CSV/SO) 값을 쓴다.</summary>
    private int EffectiveBossThreshold
        => _bossThresholdOverride > 0 ? _bossThresholdOverride : (_config?.BossThreshold ?? int.MaxValue);

    /// <summary>이어하기: 저장된 시퀀서 진행 상태를 복원한다.
    /// 재련/정제 캡과 PRD 미출현 누적은 구 세이브에 없으므로 기본값 0(=만량·미출현 없음)으로 폴백한다.</summary>
    public void RestoreState(int visitCount, int phase, int shopUsed, int eventUsed,
                             IEnumerable<CooldownKV> cooldowns,
                             int crucibleUsed = 0, int refineryUsed = 0,
                             int shopMiss = 0, int eventMiss = 0,
                             int crucibleMiss = 0, int refineryMiss = 0)
    {
        _visitCount   = visitCount;
        _phase        = (Phase)phase;
        _shopUsed     = shopUsed;
        _eventUsed    = eventUsed;
        _crucibleUsed = crucibleUsed;
        _refineryUsed = refineryUsed;
        _shopMiss     = shopMiss;
        _eventMiss    = eventMiss;
        _crucibleMiss = crucibleMiss;
        _refineryMiss = refineryMiss;
        _cooldowns.Clear();
        if (cooldowns != null)
            foreach (var c in cooldowns)
                if (!string.IsNullOrEmpty(c.key)) _cooldowns[c.key] = c.turns;
    }

    // ── Public ──────────────────────────────────────

    /// <summary>런 시작 시 1회 호출. 마스터 시드+config로 보스 도달까지의 출구 종류를 산출한다.
    /// 라이브 상태를 건드리지 않도록 동일 (pool/config/seed) 클론에서 RollExits 로직을 그대로 재생한다.
    /// ⚠️ 특수방이 PRD 확률 + <b>방문 이력</b>에 의존하도록 바뀐 뒤로 이것은 <b>확정 일정표가 아니라 예보</b>다 —
    /// "매번 첫 문으로 직진한다"는 가정의 한 가지 시나리오일 뿐이고, 실제 플레이는 방문에 따라 갈라진다.</summary>
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
            || (_phase == Phase.Normal && _config != null && _visitCount >= EffectiveBossThreshold))
        {
            _phase = Phase.PreBoss;
            result.Add(new DoorPlan { kind = RoomPlanKind.PreBoss, entry = BossEntry(_config?.PreBossRoomKey, RoomPlanKind.PreBoss) });
            return result;
        }

        // 방별 자식 RNG — 같은 (시드, visitCount)면 동일 출구 (이어하기 재현)
        var rng = new System.Random(Combine(_seed, _visitCount));

        // 특수방 타이밍은 <b>고정 마일스톤이 아니라 PRD 확률</b>이 정한다(§방구조 개편).
        // 예외는 하나뿐 — 보스까지 남은 방이 모자라면 아직 못 만난 특수방을 강제 배치한다(하드 피티).
        RoomPlanKind? forced = PendingPitySpecial(rng);

        // 일반 페이즈 — 2슬롯(직진/턴). 특수방(상점/이벤트/재련소/정제소)은 한 문쌍 최대 1개.
        // 피티 강제는 '문 하나'에만 적용한다 — 두 문을 같은 종류로 채우면
        // "상점 | 상점"처럼 중복 선택지가 되어 고르는 의미가 사라진다.
        bool specialUsed = false;
        for (int i = 0; i < 2; i++)
        {
            RoomPlanKind kind;
            if (forced.HasValue && i == 0)
            {
                kind = forced.Value;
                if (IsSpecialKind(kind)) specialUsed = true;   // 나머지 문은 특수방 금지
            }
            else kind = RollKind(rng, ref specialUsed);

            // 같은 특수 종류가 두 문에 겹치면 강등(중복 선택지 방지)
            if (i > 0 && IsSpecialKind(kind) && result[0].kind == kind) kind = RoomPlanKind.Normal;

            // 다른 문과 같은 방 템플릿도 배제
            string exclude = i > 0 ? result[0].entry?.pool_key : null;
            result.Add(MakeDoor(rng, kind, exclude));
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

        // 특수방 캡은 <b>실제로 들어갔을 때만</b> 소모된다. 문에 떴는데 안 고르면 사라지지 않고
        // 오히려 PRD 누적이 올라 다음 방에서 다시 뜰 확률이 높아진다.
        NoteSpecialVisit(chosen.kind);

        _lastCommittedKind = chosen.kind;   // 같은 특수방 연속 배치 방지용

        if (chosen.kind == RoomPlanKind.PreBoss) _phase = Phase.Boss;
        else if (chosen.kind == RoomPlanKind.Boss) _phase = Phase.Done;
    }

    // ── Private ─────────────────────────────────────

    /// <summary>종류에 맞는 방 템플릿을 골라 DoorPlan을 만든다. 템플릿이 풀에 없으면 Normal로 안전 폴백
    /// (게이트가 빈 목적지를 가리켜 소프트락 나는 것을 방지). kind/entry를 함께 강등해 라벨·색도 일치.</summary>
    private DoorPlan MakeDoor(System.Random rng, RoomPlanKind kind, string excludeKey = null)
    {
        var entry = PickEntry(rng, kind, excludeKey);
        if (entry == null && kind != RoomPlanKind.Normal)
        {
            Debug.LogWarning($"[RunSequencer] '{kind}' 방 템플릿 없음 — Normal 폴백 (visit={_visitCount + 1}). 템플릿 추가 필요.");
            kind  = RoomPlanKind.Normal;
            entry = PickEntry(rng, kind, excludeKey);
        }
        return new DoorPlan { kind = kind, entry = entry };
    }

    /// <summary>한 문쌍에 1개만 허용되는 특수방 종류(상점/이벤트/재련소/정제소).</summary>
    private static bool IsSpecialKind(RoomPlanKind k) =>
        k == RoomPlanKind.Shop || k == RoomPlanKind.Event ||
        k == RoomPlanKind.Crucible || k == RoomPlanKind.Refinery;

    /// <summary>챕터 캡 잔여 여부(= 아직 방문하지 않았는가). 재련소/정제소는 챕터당 1개.</summary>
    private bool CanUseSpecial(RoomPlanKind k) => k switch
    {
        RoomPlanKind.Shop     => _config == null || _shopUsed  < _config.ShopMaxPerChapter,
        RoomPlanKind.Event    => _config == null || _eventUsed < _config.EventMaxPerChapter,
        RoomPlanKind.Crucible => _crucibleUsed < 1,
        RoomPlanKind.Refinery => _refineryUsed < 1,
        _                     => true,
    };

    /// <summary>
    /// 방 진입 확정 시 특수방 상태 갱신. 들어간 종류는 캡을 소모하고 누적을 리셋,
    /// 들어가지 않은 (캡이 남은) 종류는 누적 +1 → 다음 방 등장 확률이 올라간다.
    /// </summary>
    private void NoteSpecialVisit(RoomPlanKind visited)
    {
        for (int i = 0; i < SpecialKinds.Length; i++)
        {
            var k = SpecialKinds[i];
            if (k == visited)
            {
                IncUsed(k);
                SetMiss(k, 0);
            }
            else if (CanUseSpecial(k)) SetMiss(k, MissOf(k) + 1);
        }
    }

    private void IncUsed(RoomPlanKind k)
    {
        if (k == RoomPlanKind.Shop)          _shopUsed++;
        else if (k == RoomPlanKind.Event)    _eventUsed++;
        else if (k == RoomPlanKind.Crucible) _crucibleUsed++;
        else if (k == RoomPlanKind.Refinery) _refineryUsed++;
    }

    private int MissOf(RoomPlanKind k) => k switch
    {
        RoomPlanKind.Shop     => _shopMiss,
        RoomPlanKind.Event    => _eventMiss,
        RoomPlanKind.Crucible => _crucibleMiss,
        RoomPlanKind.Refinery => _refineryMiss,
        _                     => 0,
    };

    private void SetMiss(RoomPlanKind k, int v)
    {
        if (k == RoomPlanKind.Shop)          _shopMiss     = v;
        else if (k == RoomPlanKind.Event)    _eventMiss    = v;
        else if (k == RoomPlanKind.Crucible) _crucibleMiss = v;
        else if (k == RoomPlanKind.Refinery) _refineryMiss = v;
    }

    /// <summary>특수방 기본 등장 확률(PRD 상수 C). 상점·이벤트는 런 구조 데이터, 재련·정제는 코드 상수.</summary>
    private float BaseChance(RoomPlanKind k) => k switch
    {
        RoomPlanKind.Shop     => _config?.ShopChance  ?? 0f,
        RoomPlanKind.Event    => _config?.EventChance ?? 0f,
        RoomPlanKind.Crucible => CrucibleBaseChance,
        RoomPlanKind.Refinery => RefineryBaseChance,
        _                     => 0f,
    };

    /// <summary>PRD 확률 — P(N) = C × N (N = 미출현 누적 + 1). 안 만날수록 선형으로 올라 결국 확정에 가까워진다.</summary>
    private float SpecialChance(RoomPlanKind k)
    {
        float c = BaseChance(k);
        return c <= 0f ? 0f : Mathf.Clamp01(c * (MissOf(k) + 1));
    }

    /// <summary>
    /// 하드 피티 — 보스까지 남은 방이 '아직 못 만난 특수방 수' 이하로 줄면 그 중 하나를 강제 배치한다.
    /// 순수 확률만 두면 "챕터 내내 상점 0회" 같은 불운이 나오므로 상한을 건다(가장 오래 기다린 종류 우선).
    /// </summary>
    private RoomPlanKind? PendingPitySpecial(System.Random rng)
    {
        if (_config == null) return null;

        int roomsLeft = EffectiveBossThreshold - _visitCount;
        if (roomsLeft <= 0) return null;

        // 가장 오래 기다린(miss 최대) 종류들을 모은다.
        _specialBuffer.Clear();
        int pending = 0, bestMiss = -1;
        for (int i = 0; i < SpecialKinds.Length; i++)
        {
            var k = SpecialKinds[i];
            if (!CanUseSpecial(k) || BaseChance(k) <= 0f) continue;
            pending++;

            int m = MissOf(k);
            if (m > bestMiss) { bestMiss = m; _specialBuffer.Clear(); _specialBuffer.Add(k); }
            else if (m == bestMiss) _specialBuffer.Add(k);
        }

        if (pending == 0 || roomsLeft > pending || _specialBuffer.Count == 0) return null;

        // ⚠️ 동률일 때 무작위로 고른다. 배열 순서대로 뽑으면 miss가 같은 초반에 항상 상점만 강제돼
        //    재련·정제가 뒤로 밀리고 노출이 극단적으로 치우친다(시뮬레이션에서 상점 독점 확인).
        return _specialBuffer[rng.Next(_specialBuffer.Count)];
    }

    /// <summary>보스/보스전방 템플릿 해석: 지정 pool_key 우선, 비었거나 못 찾으면 카테고리 첫 항목으로 폴백.
    /// 풀마다 Boss/PreBoss가 정확히 1개라 카테고리 폴백이 결정적 — 챕터별 키 없이 단일 config 공유 가능.</summary>
    private ZonePoolEntry BossEntry(string poolKey, RoomPlanKind kind)
        => FindByKey(poolKey) ?? FirstByCategory(CategoryName(kind));

    private ZonePoolEntry FirstByCategory(string category)
        => _pool.Find(p => string.Equals(p.category, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 문 하나의 종류를 굴린다. <b>"특수 슬롯이 열리는가"와 "어느 종류인가"를 분리</b>한다 —
    /// 열림 확률은 후보 PRD 확률의 합(상한 <see cref="SpecialSlotCap"/>), 종류는 그 확률로 가중 추첨.
    /// 순차 판정으로 하면 baseline이 높은 상점이 슬롯을 독점해 재련·정제가 거의 안 뜬다.
    /// <b>여기서는 캡을 소모하지 않는다</b> — 소모는 실제 진입 시(<see cref="NoteSpecialVisit"/>).
    /// </summary>
    private RoomPlanKind RollKind(System.Random rng, ref bool specialUsed)
    {
        if (_config == null) return RoomPlanKind.Normal;

        if (!specialUsed)
        {
            _specialBuffer.Clear();
            float total = 0f;
            for (int i = 0; i < SpecialKinds.Length; i++)
            {
                var k = SpecialKinds[i];
                // 캡이 남고, 직전에 들어간 종류가 아니어야 한다("상점 → 상점" 연속 방지).
                if (!CanUseSpecial(k) || _lastCommittedKind == k) continue;
                float c = SpecialChance(k);
                if (c <= 0f) continue;
                _specialBuffer.Add(k);
                total += c;
            }

            if (_specialBuffer.Count > 0 && Roll(rng, Mathf.Min(SpecialSlotCap, total)))
            {
                float r = (float)rng.NextDouble() * total;
                for (int i = 0; i < _specialBuffer.Count; i++)
                {
                    r -= SpecialChance(_specialBuffer[i]);
                    if (r > 0f && i < _specialBuffer.Count - 1) continue;
                    specialUsed = true;
                    return _specialBuffer[i];
                }
            }
        }

        if (Roll(rng, _config.EliteChance)) return RoomPlanKind.Elite;
        return RoomPlanKind.Normal;
    }

    private ZonePoolEntry PickEntry(System.Random rng, RoomPlanKind kind, string excludeKey = null)
    {
        string category = CategoryName(kind);
        var byCategory = _pool.FindAll(p => string.Equals(p.category, category, StringComparison.OrdinalIgnoreCase));
        if (byCategory.Count == 0) return null;

        // 같은 문쌍의 다른 문과 동일 템플릿 배제(같은 방이 두 선택지로 뜨는 것 방지).
        // 후보가 그것뿐이면 배제하지 않는다(선택지 소멸 방지).
        if (!string.IsNullOrEmpty(excludeKey) && byCategory.Count > 1)
        {
            var deduped = byCategory.FindAll(p => !string.Equals(p.pool_key, excludeKey, StringComparison.OrdinalIgnoreCase));
            if (deduped.Count > 0) byCategory = deduped;
        }

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
        RoomPlanKind.Crucible => "Crucible",
        RoomPlanKind.Refinery => "Refinery",
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
