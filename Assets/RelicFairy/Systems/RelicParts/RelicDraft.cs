using System.Collections.Generic;

/// <summary>카드 종류 — 채우기 사슬(새 조각 → 선명하게 → 메아리 → 잔향) 순서와 같다.</summary>
public enum RelicDraftCardKind
{
    NewFragment = 0,   // 새 기억 조각(등급을 굴린다)
    Sharpen     = 1,   // 선명하게 — 가진 조각의 다음 줄을 연다
    Echo        = 2,   // 메아리 — 공명 칸 하나(가웨인 시간대 · 랜슬롯 빈 받침)
    Residue     = 3,   // 잔향 — 정수(모든 것이 찬 끝의 끝)
}

/// <summary>드래프트 카드 한 장.</summary>
public sealed class RelicDraftCard
{
    public RelicDraftCardKind Kind;
    public RelicPartEntry     Entry;            // NewFragment · Sharpen
    public RelicMemoryGrade   Grade;            // NewFragment = 굴린 등급 · Sharpen = 오를 등급
    public string             Anchor;           // Echo = 시간대/계단 · 그 밖엔 Entry.anchor
    public int                ResidueEssence;   // Residue

    public string PartId => Entry != null ? Entry.part_id : null;
}

/// <summary>
/// 한 번 굴린 드래프트 — d6 「기억의 빛」(TopGrade로 빛 세기)과 「되살아난 기억」 창이 <b>같은 객체</b>를 쓴다.
/// 다시 굴리면 빛과 카드가 어긋나므로, 다시 떠올리기(제단 노드)만이 새 드래프트를 만든다.
/// </summary>
public sealed class RelicDraft
{
    public string RelicId;
    public readonly List<RelicDraftCard> Cards = new(3);
    public RelicMemoryGrade TopGrade = RelicMemoryGrade.Faint;
    public bool PityApplied;
    public bool Redrawn;

    public bool HasRadiant
    {
        get
        {
            for (int i = 0; i < Cards.Count; i++)
                if ((Cards[i].Kind == RelicDraftCardKind.NewFragment || Cards[i].Kind == RelicDraftCardKind.Sharpen)
                    && Cards[i].Grade == RelicMemoryGrade.Radiant) return true;
            return false;
        }
    }
}
