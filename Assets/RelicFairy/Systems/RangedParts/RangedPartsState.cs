using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 원거리 슬롯에 장착된 파츠의 <b>런타임 상태</b>. 런 스코프이며 세이브 대상이다.
///
/// 정적 정의(<see cref="WeaponPartEntry"/>)와 분리한 이유 — 파츠는 런 중에 장착·강화되므로
/// ScriptableObject/CSV 같은 정적 자산에 상태를 담을 수 없다.
///
/// 슬롯 해금은 <b>원거리 무기 강화 레벨</b>에 종속된다(기획 확정): 강화할수록 슬롯이 하나씩 열려
/// 원거리에도 무기 강화 동기가 생긴다.
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

    /// <summary>무기 강화 레벨이 이 값을 넘을 때마다 파츠 슬롯이 1개 열린다.</summary>
    private const int SlotUnlockEvery = 3;
    /// <summary>슬롯 상한 — UI·밸런스 한계.</summary>
    public  const int MaxSlots        = 4;

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
    private static int _testCarryEnhanceLevel;

    /// <summary>테스트 제단이 현재 구성을 이월 대상으로 등록한다.</summary>
    public static void SetTestCarry(IReadOnlyList<Equipped> equipped, int weaponEnhanceLevel)
    {
        _testCarry = equipped != null && equipped.Count > 0 ? new List<Equipped>(equipped) : null;
        _testCarryEnhanceLevel = weaponEnhanceLevel;
    }

    /// <summary>이월 해제 — 테스트를 끝낸 뒤 정상 런으로 돌아갈 때.</summary>
    public static void ClearTestCarry() => _testCarry = null;

    /// <summary>이월된 테스트 구성이 있으면 새 런 상태에 재적용한다. GameRunSession이 런 시작 직후 호출.</summary>
    public static void ApplyTestCarry()
    {
        if (_testCarry == null || _testCarry.Count == 0) return;
        Current.Restore(_testCarry, _testCarryEnhanceLevel);
        Debug.Log($"[파츠테스트] 테스트 구성 {_testCarry.Count}개를 새 런에 이월했습니다");
    }

    public static bool HasTestCarry => _testCarry != null && _testCarry.Count > 0;

    private readonly List<Equipped> _equipped = new();
    private int _weaponEnhanceLevel;

    public IReadOnlyList<Equipped> Equipped_ => _equipped;
    public bool HasAny => _equipped.Count > 0;

    /// <summary>무기 강화 레벨로 결정되는 해금 슬롯 수.</summary>
    public int UnlockedSlots => Mathf.Clamp(1 + _weaponEnhanceLevel / SlotUnlockEvery, 1, MaxSlots);

    /// <summary>세이브용 — 슬롯 해금 근거가 되는 원거리 무기 강화 레벨.</summary>
    public int WeaponEnhanceLevel => _weaponEnhanceLevel;

    // ── 상태 변경 ───────────────────────────────────────────

    /// <summary>원거리 무기 강화 레벨 반영(슬롯 해금 갱신). 재련소 강화 후 호출.</summary>
    public void SetWeaponEnhanceLevel(int level)
    {
        _weaponEnhanceLevel = Mathf.Max(0, level);
        // 해금이 줄어드는 경우는 없지만, 방어적으로 초과분은 잘라낸다.
        while (_equipped.Count > UnlockedSlots) _equipped.RemoveAt(_equipped.Count - 1);
    }

    /// <summary>파츠 장착. 슬롯이 남아 있고 같은 파츠를 중복 장착하지 않을 때만 성공.</summary>
    public bool Equip(string partId, int level = 1)
    {
        if (string.IsNullOrEmpty(partId)) return false;
        if (_equipped.Count >= UnlockedSlots) return false;
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

    /// <summary>파츠 레벨 상승(재련소 강화 성공). 상한은 정의의 max_level.</summary>
    public bool LevelUp(string partId, int delta = 1)
    {
        int i = IndexOf(partId);
        if (i < 0) return false;

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
    public void Restore(IEnumerable<Equipped> equipped, int weaponEnhanceLevel)
    {
        _equipped.Clear();
        if (equipped != null)
            foreach (var e in equipped)
                if (!string.IsNullOrEmpty(e.partId)) _equipped.Add(e);

        _weaponEnhanceLevel = Mathf.Max(0, weaponEnhanceLevel);
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
