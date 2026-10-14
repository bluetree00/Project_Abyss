using System;
using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>피해 출처(결과창 「피해 비중」). 저장 값이라 번호를 바꾸지 않는다.</summary>
public enum DamageSource { Basic = 0, Skill = 1, RelicQ = 2, Covenant = 3, Rune = 4, Dot = 5 }

/// <summary>
/// 런 결과창 집계(10-07) — 세션에 <b>없던</b> 값만 센다: 챕터 끝 시각 · 플레이어를 마지막으로 때린 상대 · 연료 획득량 ·
/// 서약 발동 수 · 출처별 피해와 가장 큰 한 방. 처치 · 방 · 정수 · 골드는 <see cref="GameRunSession"/> · <see cref="RunDelta"/>에 이미 있다.
/// 값은 순수 데이터라 이어하기 세이브에 JSON 한 줄로 실린다(<c>RunSaveData.runStatsJson</c>).
/// </summary>
[Serializable]
public sealed class RunStats
{
    // ── Constants ──
    public const int SourceCount = 6;

    // ── Serialized ──
    [SerializeField] private List<int> chapterIds = new();          // 마친 챕터 번호(차례대로 — 심연 루프면 1이 다시 나온다)
    [SerializeField] private List<int> chapterEndSeconds = new();   // 그 챕터를 마친 순간의 PlaySeconds
    [SerializeField] private string lastHitBy = string.Empty;
    [SerializeField] private int enhanceMaterialGained;
    [SerializeField] private int runeOreGained;
    [SerializeField] private int covenantFires;
    [SerializeField] private long[] damageBySource = new long[SourceCount];
    [SerializeField] private int biggestHit;
    [SerializeField] private int biggestHitSource;
    [SerializeField] private int midBossSeed;                       // 정예 보스 번갈이 시드(10-14) — 런에 하나 · 0 = 아직 안 정함(구 세이브 포함)

    // ── Properties ──
    public int ChapterEndCount => chapterEndSeconds.Count;
    public string LastHitBy => lastHitBy;
    public int EnhanceMaterialGained => enhanceMaterialGained;
    public int RuneOreGained => runeOreGained;
    public int CovenantFires => covenantFires;
    public int BiggestHit => biggestHit;
    public DamageSource BiggestHitSource => (DamageSource)biggestHitSource;

    /// <summary>
    /// 정예 보스 번갈이(<see cref="RunSequencer.MidBossScheduled"/>) 시드 — 런에 하나. 처음 읽을 때 정하고 이어하기 세이브에 같이 실린다.
    /// 방 시드는 챕터 씬마다 새로 뽑혀(<c>RunFlowController.StartRunAsync</c>) 그걸 쓰면 「직전 챕터에 없었으면 확정」이 끊겼다(10-14 5회차).
    /// </summary>
    public int MidBossSeed
    {
        get
        {
            if (midBossSeed == 0) midBossSeed = Environment.TickCount | 1;
            return midBossSeed;
        }
    }

    public long TotalDamage
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < damageBySource.Length; i++) sum += damageBySource[i];
            return sum;
        }
    }

    // ── Public Methods ──
    public (int chapter, int seconds) ChapterEndAt(int i) => (chapterIds[i], chapterEndSeconds[i]);
    public long DamageOf(DamageSource s) => damageBySource[(int)s];

    /// <summary>출처 비중(0~1). 총합이 0이면 0.</summary>
    public float ShareOf(DamageSource s)
    {
        long total = TotalDamage;
        return total > 0 ? (float)damageBySource[(int)s] / total : 0f;
    }

    public void ReportChapterEnd(int chapter, int playSeconds)
    {
        chapterIds.Add(chapter);
        chapterEndSeconds.Add(Mathf.Max(0, playSeconds));
    }

    /// <summary>빈 이름(투사체 · 함정 등 몬스터가 아닌 것)은 덮어쓰지 않는다 — 마지막으로 이름 있는 상대가 남는다.</summary>
    public void ReportHitBy(string name)
    {
        if (!string.IsNullOrEmpty(name)) lastHitBy = name;
    }

    /// <summary>들어온 양만 센다(쓴 것 · 음수는 무시).</summary>
    public void ReportFuelGained(FuelKind kind, int amount)
    {
        if (amount <= 0) return;
        if (kind == FuelKind.EnhanceMaterial) enhanceMaterialGained += amount;
        else if (kind == FuelKind.RuneOre)    runeOreGained         += amount;
    }

    public void ReportCovenantFire() => covenantFires++;

    public void RecordDamage(DamageSource source, int amount)
    {
        if (amount <= 0) return;
        damageBySource[(int)source] += amount;
        if (amount > biggestHit)
        {
            biggestHit = amount;
            biggestHitSource = (int)source;
        }
    }

    public string ToJson() => JsonUtility.ToJson(this);

    /// <summary>세이브 JSON에서 되살린다. 비었거나 깨졌으면 새 집계(구 세이브 호환 · 예외 없음).</summary>
    public static RunStats FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return new RunStats();
        RunStats s;
        try { s = JsonUtility.FromJson<RunStats>(json); }
        catch (Exception) { return new RunStats(); }
        if (s == null) return new RunStats();
        if (s.damageBySource == null || s.damageBySource.Length != SourceCount) Array.Resize(ref s.damageBySource, SourceCount);
        s.chapterIds ??= new List<int>();
        s.chapterEndSeconds ??= new List<int>();
        if (s.chapterIds.Count != s.chapterEndSeconds.Count) { s.chapterIds.Clear(); s.chapterEndSeconds.Clear(); }
        s.lastHitBy ??= string.Empty;
        return s;
    }

    /// <summary>플레이어를 때린 상대 이름 — 보스면 보스 이름(한글), 아니면 몬스터 이름. 몬스터가 아니면 빈 문자열.</summary>
    public static string NameOf(GameObject attacker)
    {
        if (attacker == null) return string.Empty;
        var mb = attacker.GetComponentInParent<MonsterBase>();
        if (mb == null) return string.Empty;
        return mb.Grade == MonsterGrade.Boss && !string.IsNullOrEmpty(mb.BossName) ? mb.BossName : mb.DisplayName;
    }
}
