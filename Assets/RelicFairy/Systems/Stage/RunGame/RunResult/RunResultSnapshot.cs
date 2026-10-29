using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 결과창이 그리는 값 — 런이 끝나는 순간 떠 둔 사본(10-07). 끝난 뒤 로드아웃 · 룬판 · 서약이 지워져도 남는다.
/// 빌드 · 집계는 <see cref="RunResultProvider.Capture"/>(EndRun 앞), 정산 값은 <see cref="RunResultProvider.Settle"/>(EndRun 뒤)이 채운다.
/// </summary>
public sealed class RunResultSnapshot
{
    // ── 머리 ──
    public bool Cleared;
    public int ChapterNumber;
    public string ChapterName = string.Empty;
    public int RoomClears;
    public StoryEra Era;
    public int AbyssDepth;
    public string KilledBy = string.Empty;
    public int PlaySeconds;
    public List<Segment> Segments = new();

    // ── 재화 ──
    public int Essence;
    public int EssenceFromContracts;
    public int ContractsDone;
    public int EssenceFromFirstRun;
    public int Gold;
    public int EnhanceMaterial;
    public int RuneOre;

    // ── 빌드 ──
    public WeaponLine Weapon;          // 든 무기(없으면 null)
    public WeaponLine SubWeapon;       // 다른 칸(없으면 null)
    public string RelicName = string.Empty;
    public Sprite RelicIcon;
    public int MemFaint, MemClear, MemRadiant;
    public List<RuneCell> Board = new();
    public int BoardFilled, BoardTotal, RuneCount;
    public List<(string elementId, int tier)> Synergies = new();
    public List<CovenantLine> Covenants = new();
    public int CovenantFires;

    // ── 전투 ──
    public int Kills, EliteKills, BossKills, FlawlessChapters;
    public int BiggestHit;
    public DamageSource BiggestHitSource;
    public long[] Damage = new long[RunStats.SourceCount];

    public struct Segment
    {
        public string Label;
        public int Seconds;
        public bool Done;   // 마친 챕터 + 쓰러진(끝난) 챕터 = true · 못 간 챕터 = false
        public int CombatSeconds;   // 챕터 전투 시계(10-24 U5) — -1 = 모름(옛 세이브) · 못 간 챕터
        public string Grade;        // 등급 글자(색 입힘) — 마친 챕터만 · 쓰러진 챕터 · 모르면 빈 글
    }

    public sealed class WeaponLine
    {
        public string Name = string.Empty;
        public Sprite Icon;
        public int Enhance;
        public string FormName = string.Empty;
        public int FormTier;
        public bool Ranged;
        public string Build = string.Empty;   // 근접 = 증강 「이름 단계」 · 원거리 = 켠 파츠 「이름 Lv」(10-22)
    }

    public struct RuneCell
    {
        public int X, Y;
        public string Element;   // 놓인 칸의 속성 id · 빈 칸은 null
        public char Zone;        // 존 코드(빈 칸 색)
    }

    public struct CovenantLine
    {
        public string Name;                 // 「연격 → 초신성 → 잔불」
        public string Cause;                // 「같은 적을 3연타할 때마다」
        public CovenantTier[] Tiers;        // 이름 조각마다(원인 → 결과 차례)
    }
}
