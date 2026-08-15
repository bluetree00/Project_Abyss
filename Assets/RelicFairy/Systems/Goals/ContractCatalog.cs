using System;
using System.Collections.Generic;

/// <summary>
/// 「계약」 정의 1건 — 런 시작에 부여되고 종료 시 자동 정산되는 목표.
///
/// 진척은 <b>런 세션이 이미 세고 있는 값</b>을 읽어서 만든다. 별도 카운터를 만들지 않는 이유는
/// 업적과 같다 — 같은 사실을 두 곳에서 세면 이어하기·저장 시점에 두 값이 어긋난다.
/// </summary>
public sealed class ContractDef
{
    public string Id      { get; }
    public string Title   { get; }
    public int    Target  { get; }
    public int    Essence { get; }

    /// <summary>현재 진척(0~Target). 세션에서 읽는다.</summary>
    private readonly Func<GameRunSession, int> _progress;

    public ContractDef(string id, string title, int target, int essence,
                       Func<GameRunSession, int> progress)
    {
        Id = id; Title = title; Target = target; Essence = essence;
        _progress = progress;
    }

    public int Progress(GameRunSession s)
        => s == null ? 0 : UnityEngine.Mathf.Clamp(_progress(s), 0, Target);

    public bool IsDone(GameRunSession s) => Progress(s) >= Target;
}

/// <summary>
/// 계약 풀. 코드 상수로 두는 이유는 <see cref="MemoryAltarCatalog"/>와 같다 —
/// 판정식이 세션 필드에 직접 걸려 있어 CSV로 뺄 수 없다(문자열로는 함수를 못 담는다).
/// </summary>
public static class ContractCatalog
{
    /// <summary>1번 슬롯 전용 — 아무리 못해도 뭔가는 달성하게 하는 보장.</summary>
    public const string ReachId = "reach_chapter";

    /// <summary>「특수방 전부」와 「특수방 0회」는 같은 런에 함께 나올 수 없다.</summary>
    private const string SpecialAllId  = "special_all";
    private const string SpecialNoneId = "special_none";

    public static readonly ContractDef Reach = new(
        ReachId, "챕터 2 도달", 2, 60,
        s => (int)s.CurrentChapter);

    /// <summary>1번 슬롯을 뺀 추첨 대상.</summary>
    public static readonly ContractDef[] Pool =
    {
        new("elite_5",     "정예 5 처치",        5, 50, s => s.EliteKillCount),
        new("enhance_6",   "무기 강화 +6 도달",  6, 50, s => s.MaxEnhanceLevel),
        new("rooms_8",     "방 8개 클리어",      8, 50, s => s.RoomClearRecords.Count),
        new("boss_1",      "보스 1회 처치",      1, 70, s => s.BossKillCount),
        new("refine_3",    "정제 3회",           3, 50, s => s.RefineUseCount),
        // 세션은 특수방 <b>방문 횟수</b>만 센다(종류는 안 센다). 「4종 전부」를 그대로 쓰려면
        // 종류 집합을 새로 추적해야 하는데, 같은 사실을 두 곳에서 세지 않는다는 원칙을 깨게 된다.
        // 대신 「3회 이용」으로 바꾼다 — 특수방을 적극적으로 찾아가게 만드는 의도는 그대로다.
        new(SpecialAllId,  "특수방 3회 이용",      3, 50, s => s.SpecialRoomVisits),
        new(SpecialNoneId, "특수방 하나도 안 쓰기", 1, 70,
            s => s.SpecialRoomVisits == 0 ? 1 : 0),
    };

    /// <summary>id로 정의를 찾는다(세이브 복원용). 없으면 null.</summary>
    public static ContractDef Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == ReachId) return Reach;
        foreach (var d in Pool) if (d.Id == id) return d;
        return null;
    }

    /// <summary>
    /// 이번 런의 계약 3개를 뽑는다.
    ///  · 1번은 <b>항상 「도달」</b> — 최소 보장.
    ///  · 「특수방 전부」와 「특수방 0회」는 서로를 배제한다(동시에 만족 불가라 하나는 반드시 죽는다).
    /// 결정적 난수를 받으므로 같은 시드는 같은 계약을 준다.
    /// </summary>
    public static List<ContractDef> Roll(System.Random rng)
    {
        var picked = new List<ContractDef>(3) { Reach };
        var bag    = new List<ContractDef>(Pool);

        while (picked.Count < 3 && bag.Count > 0)
        {
            int i = rng.Next(bag.Count);
            var d = bag[i];
            bag.RemoveAt(i);
            picked.Add(d);

            // 배타 짝을 통에서 뺀다.
            string exclude = d.Id == SpecialAllId  ? SpecialNoneId
                           : d.Id == SpecialNoneId ? SpecialAllId
                           : null;
            if (exclude != null) bag.RemoveAll(x => x.Id == exclude);
        }
        return picked;
    }
}
