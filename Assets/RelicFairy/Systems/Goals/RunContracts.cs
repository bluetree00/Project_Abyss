using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이번 런의 계약 3개. <b>런 스코프</b>이며 세이브 대상이다(이어하기 시 같은 계약을 이어간다).
///
/// 업적과 달리 <b>수령 버튼이 없다</b> — 런 종료 시 달성분을 자동 정산한다.
/// 매 런 생기는 목표를 수령까지 요구하면 피곤하고, 죽은 직후에 버튼을 누르게 만드는 것도 어색하다.
/// 「죽어도 헛되지 않았다」는 <b>실패한 런에서도 정산된다</b>는 사실이 만든다.
/// </summary>
public sealed class RunContracts
{
    private static RunContracts _current;

    /// <summary>현재 런의 계약. 런 시작에서 새로 만들고, 이어하기에서 복원한다.</summary>
    public static RunContracts Current
    {
        get => _current ??= new RunContracts();
        set => _current = value;
    }

    private readonly List<ContractDef> _defs = new();

    /// <summary>부여된 계약 3개. 아직 롤을 안 했으면 비어 있다.</summary>
    public IReadOnlyList<ContractDef> Defs => _defs;

    public bool HasAny => _defs.Count > 0;

    /// <summary>세이브용 — 계약 id를 쉼표로 이은 문자열.</summary>
    public string ToSaveString() => string.Join(",", _defs.ConvertAll(d => d.Id));

    /// <summary>
    /// 런 시작 시 3개를 뽑는다. 이미 있으면 <b>다시 뽑지 않는다</b> —
    /// 챕터 전환 등으로 이 경로가 다시 불려도 계약이 바뀌면 안 된다.
    /// </summary>
    public void RollIfEmpty(System.Random rng)
    {
        if (_defs.Count > 0) return;
        _defs.AddRange(ContractCatalog.Roll(rng));
        Debug.Log($"[계약] 부여 — {string.Join(" · ", _defs.ConvertAll(d => d.Title))}");
    }

    /// <summary>이어하기 복원. 알 수 없는 id는 버린다(정의가 바뀐 세이브 방어).</summary>
    public void Restore(string saved)
    {
        _defs.Clear();
        if (string.IsNullOrEmpty(saved)) return;

        foreach (var id in saved.Split(','))
        {
            var d = ContractCatalog.Find(id.Trim());
            if (d != null) _defs.Add(d);
        }
    }

    // ── 조회 ────────────────────────────────────────────────

    public int DoneCount(GameRunSession s)
    {
        int n = 0;
        for (int i = 0; i < _defs.Count; i++)
            if (_defs[i].IsDone(s)) n++;
        return n;
    }

    /// <summary>지금 정산하면 받을 정수. 달성분만 센다 — 실패한 런에서도 이만큼은 남는다.</summary>
    public int EarnedEssence(GameRunSession s)
    {
        int sum = 0;
        for (int i = 0; i < _defs.Count; i++)
            if (_defs[i].IsDone(s)) sum += _defs[i].Essence;
        return sum;
    }
}
