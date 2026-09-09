using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 원거리 파츠의 <b>런타임 상태</b>. 런 스코프이며 세이브 대상이다.
///
/// 정적 정의(<see cref="WeaponPartEntry"/>)와 분리한 이유 — 파츠는 런 중에 강화되므로
/// ScriptableObject/CSV 같은 정적 자산에 상태를 담을 수 없다.
///
/// <b>내장형</b>(기획 확정 2026-08-20) — 파츠는 슬롯에 꽂는 물건이 아니라 무기에 내장된 계통이다.
/// 5종이 항상 존재하고 레벨만 다르다. Lv0 = 꺼짐, Lv1 이상 = 켜짐.
///
/// 이전의 슬롯 지급형(누적 투자 12/30/55/90으로 4칸을 여는 방식)은 폐기했다:
///  · 슬롯을 다 열면 런 수입 106 중 90이 통행세로 나가 <b>꽂은 파츠를 키울 재료가 안 남았다</b>.
///  · 슬롯 4칸에 파츠 5종이라 "하나만 버리기"였고, 그건 선택이 아니었다.
///  · 파츠는 런 스코프인데 통행세만 매 런 반복돼 성장이 아니라 절차가 됐다.
///
/// 선택은 "무엇을 고를까"가 아니라 <b>"재료를 어디에 몰아줄까"</b>다.
/// 그래서 이 클래스에는 게이트가 없다 — 있는 건 파츠별 레벨뿐이다.
/// </summary>
public sealed class RangedPartsState
{
    /// <summary>장착 1칸 — 어떤 파츠가 몇 레벨인지.</summary>
    [System.Serializable]
    public struct Equipped
    {
        public string partId;
        public int    level;
    }

    private static RangedPartsState _current;

    /// <summary>
    /// 현재 런의 파츠 상태. GameRunSession이 런 시작·복원에서 새로 만든다.
    /// 에디터에서 씬을 직접 실행하는 등 그 경로를 안 탄 경우에도 null이 되지 않게 지연 생성한다
    /// (null이면 재련소 파츠 탭이 통째로 죽어 원인을 찾기 어렵다).
    /// </summary>
    public static RangedPartsState Current
    {
        get => _current ??= new RangedPartsState();
        set => _current = value;
    }

    // ── 테스트 이월(제단 전용) ──────────────────────────────
    // 런 시작은 파츠를 비우므로(위 Current 재생성), 베이스캠프에서 맞춘 테스트 구성이
    // 던전에 들어가는 순간 사라진다. 그러면 "장착은 되는데 쏴볼 수가 없는" 도구가 된다.
    // 테스트 제단을 쓴 경우에만 채워지고, 안 썼으면 완전히 무해하다.
    private static List<Equipped> _testCarry;

    /// <summary>테스트 제단이 현재 구성을 이월 대상으로 등록한다.</summary>
    public static void SetTestCarry(IReadOnlyList<Equipped> equipped)
        => _testCarry = equipped != null && equipped.Count > 0 ? new List<Equipped>(equipped) : null;

    /// <summary>이월 해제 — 테스트를 끝낸 뒤 정상 런으로 돌아갈 때.</summary>
    public static void ClearTestCarry() => _testCarry = null;

    /// <summary>이월된 테스트 구성이 있으면 새 런 상태에 재적용한다. GameRunSession이 런 시작 직후 호출.</summary>
    public static void ApplyTestCarry()
    {
        if (_testCarry == null || _testCarry.Count == 0) return;
        Current.Restore(_testCarry);
        Debug.Log($"[파츠테스트] 테스트 구성 {_testCarry.Count}개를 새 런에 이월했습니다");
    }

    public static bool HasTestCarry => _testCarry != null && _testCarry.Count > 0;

    /// <summary>켜진 파츠만 담는다(Lv1 이상). 꺼진 파츠는 여기 없고, 목록 표시는 정적 정의가 담당한다.</summary>
    private readonly List<Equipped> _equipped = new();

    public IReadOnlyList<Equipped> Equipped_ => _equipped;
    public bool HasAny => _equipped.Count > 0;

    /// <summary>켠 파츠 수.</summary>
    public int ActiveCount => _equipped.Count;

    /// <summary>파츠 레벨 합 — 원거리 스킬 단계(<see cref="SkillTierResolver"/>)의 진행도.</summary>
    public int TotalLevel
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < _equipped.Count; i++) sum += _equipped[i].level;
            return sum;
        }
    }

    /// <summary>
    /// 지금까지 파츠에 부은 강화재료 총량. 레벨에서 역산하므로 따로 들고 다니지 않는다
    /// (별도 필드로 두면 세이브·복원에서 레벨과 어긋날 수 있다).
    /// </summary>
    public int TotalSpent
    {
        get
        {
            var data = Managers.WeaponParts;
            if (data == null) return 0;

            int sum = 0;
            for (int i = 0; i < _equipped.Count; i++)
            {
                var def = data.GetById(_equipped[i].partId);
                if (def == null) continue;
                // Lv N = CostAt(0) + CostAt(1) + ... + CostAt(N-1) 을 이미 지불한 상태.
                for (int lv = 0; lv < _equipped[i].level; lv++) sum += def.CostAt(lv);
            }
            return sum;
        }
    }

    // ── 상태 변경 ───────────────────────────────────────────

    /// <summary>파츠를 켠다(Lv0 → Lv1). 이미 켜져 있으면 아무것도 하지 않는다.</summary>
    public bool Equip(string partId, int level = 1)
    {
        if (string.IsNullOrEmpty(partId)) return false;
        if (IndexOf(partId) >= 0) return false;

        _equipped.Add(new Equipped { partId = partId, level = Mathf.Max(1, level) });
        return true;
    }

    public bool Unequip(string partId)
    {
        int i = IndexOf(partId);
        if (i < 0) return false;
        _equipped.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// 파츠 레벨 상승. <b>꺼진 파츠(Lv0)면 켜는 것부터</b> 처리한다 —
    /// 내장형에는 "장착"이라는 별도 단계가 없어, 첫 강화가 곧 활성화다.
    /// 상한은 정의의 max_level.
    /// </summary>
    public bool LevelUp(string partId, int delta = 1)
    {
        int i = IndexOf(partId);
        if (i < 0) return Equip(partId, delta);

        var def = Managers.WeaponParts?.GetById(partId);
        int max = def != null && def.max_level > 0 ? def.max_level : int.MaxValue;

        var e = _equipped[i];
        e.level = Mathf.Clamp(e.level + delta, 1, max);
        _equipped[i] = e;
        return true;
    }

    public int LevelOf(string partId)
    {
        int i = IndexOf(partId);
        return i < 0 ? 0 : _equipped[i].level;
    }

    /// <summary>이어하기 복원.</summary>
    public void Restore(IEnumerable<Equipped> equipped)
    {
        _equipped.Clear();
        if (equipped == null) return;

        foreach (var e in equipped)
            if (!string.IsNullOrEmpty(e.partId) && e.level > 0) _equipped.Add(e);
    }

    // ── 발사 요청에 반영 ────────────────────────────────────

    /// <summary>
    /// 장착 파츠 효과를 요청에 누적한다. <see cref="CombatSpawner"/>의 ②단계에서만 불린다.
    /// 종류별로 <b>서로 다른 요청 필드</b>를 건드리므로 파츠끼리 축이 겹치지 않는다.
    /// </summary>
    public void Apply(ref ProjectileRequest req)
    {
        var data = Managers.WeaponParts;
        if (data == null) return;

        for (int i = 0; i < _equipped.Count; i++)
        {
            var def = data.GetById(_equipped[i].partId);
            if (def == null) continue;

            float v = def.ValueAt(_equipped[i].level);
            switch (def.Kind)
            {
                case RangedPartKind.Split:
                    req.count += Mathf.RoundToInt(v);
                    break;

                case RangedPartKind.Pierce:
                    req.pierce += Mathf.RoundToInt(v);
                    break;

                case RangedPartKind.Explode:
                    req.explodeRadius += v;
                    // 폭발 피해는 본체 피해의 일부 — 반경만 키우면 "커지는데 안 아픈" 파츠가 된다.
                    if (req.explodeDamageRatio <= 0f) req.explodeDamageRatio = ExplodeDamageRatio;
                    break;

                case RangedPartKind.Homing:
                    req.homingStrength += v;
                    // 관통이 함께 있으면 되돌아와 재타격한다 — 유도 파츠의 정체성.
                    req.returnOnPierce = true;
                    break;

                case RangedPartKind.Power:
                    // 크기와 피해가 함께 오른다 — 수치 상승에 시각적 근거를 준다.
                    req.sizeMult   += v;
                    req.damageMult += v;
                    break;
            }
        }
    }

    /// <summary>폭발 피해 = 본체 피해 × 이 비율.</summary>
    private const float ExplodeDamageRatio = 0.5f;

    private int IndexOf(string partId)
    {
        for (int i = 0; i < _equipped.Count; i++)
            if (_equipped[i].partId == partId) return i;
        return -1;
    }
}
