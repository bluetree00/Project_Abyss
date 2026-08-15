using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 원거리 슬롯에 장착된 파츠의 <b>런타임 상태</b>. 런 스코프이며 세이브 대상이다.
///
/// 정적 정의(<see cref="WeaponPartEntry"/>)와 분리한 이유 — 파츠는 런 중에 장착·강화되므로
/// ScriptableObject/CSV 같은 정적 자산에 상태를 담을 수 없다.
///
/// 슬롯 해금은 <b>원거리 무기에 누적 투자한 강화재료</b>에 종속된다(기획 확정 2026-08-13).
///
/// 강화 <i>레벨</i>이 아니라 <i>투자액</i>을 쓰는 이유:
///  · 레벨 축은 간격을 벌릴 수 없다 — 기대 비용이 +4=10, +6=49, +7=145, +9=2358로 폭발해
///    "+3마다 한 칸" 같은 설계가 성립하지 않는다. 1레벨 간격으로 욱여넣으면
///    "강화 한 번 = 파츠 하나"로 읽혀 마일스톤이 마일스톤답지 않다.
///  · 투자액 축은 임계를 자유롭게 벌릴 수 있고, 그 사이에 강화 시도가 여러 번 들어간다.
///  · <b>실패해도 진척이 쌓인다</b>. 강화 실패는 재료와 레벨을 함께 앗아가지만, 파츠 게이지는
///    올라간다 — 도박 구간의 좌절을 완충한다.
///  · 레벨 하락으로 슬롯이 닫혀 장착 파츠가 소실되던 문제가 구조적으로 사라진다(투자는 되돌아가지 않는다).
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

    /// <summary>
    /// 슬롯이 열리는 누적 투자액(강화재료). 런 총 수입 ~106 기준 11% / 28% / 52% / 85%.
    /// 초기 세팅은 가볍게 — 조일 때는 이 배열만 올리면 된다.
    /// </summary>
    private static readonly int[] TierThresholds = { 12, 30, 55, 90 };

    /// <summary>슬롯 상한 — UI·밸런스 한계. 임계 개수와 같아야 한다.</summary>
    public  const int MaxSlots        = 4;

    /// <summary>i번째 슬롯이 열리는 누적 투자액. 범위 밖이면 0(UI 표기용).</summary>
    public static int ThresholdAt(int slotIndex)
        => slotIndex >= 0 && slotIndex < TierThresholds.Length ? TierThresholds[slotIndex] : 0;

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
    private static int _testCarryInvested;

    /// <summary>테스트 제단이 현재 구성을 이월 대상으로 등록한다.</summary>
    public static void SetTestCarry(IReadOnlyList<Equipped> equipped, int invested)
    {
        _testCarry = equipped != null && equipped.Count > 0 ? new List<Equipped>(equipped) : null;
        _testCarryInvested = invested;
    }

    /// <summary>이월 해제 — 테스트를 끝낸 뒤 정상 런으로 돌아갈 때.</summary>
    public static void ClearTestCarry() => _testCarry = null;

    /// <summary>이월된 테스트 구성이 있으면 새 런 상태에 재적용한다. GameRunSession이 런 시작 직후 호출.</summary>
    public static void ApplyTestCarry()
    {
        if (_testCarry == null || _testCarry.Count == 0) return;
        Current.Restore(_testCarry, _testCarryInvested, _testCarry.Count);
        Debug.Log($"[파츠테스트] 테스트 구성 {_testCarry.Count}개를 새 런에 이월했습니다");
    }

    public static bool HasTestCarry => _testCarry != null && _testCarry.Count > 0;

    private readonly List<Equipped> _equipped = new();
    private int _invested;
    private int _grantedTier;

    public IReadOnlyList<Equipped> Equipped_ => _equipped;
    public bool HasAny => _equipped.Count > 0;

    /// <summary>원거리 무기 강화에 지금까지 부은 강화재료 총량(실패분 포함).</summary>
    public int Invested => _invested;

    /// <summary>파츠를 이미 지급받은 티어 수. 미수령 티어를 세는 기준.</summary>
    public int GrantedTier => _grantedTier;

    /// <summary>투자액으로 도달한 티어 수 = 해금 슬롯 수. 0이면 파츠를 하나도 쓸 수 없다.</summary>
    public int UnlockedSlots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < TierThresholds.Length; i++)
                if (_invested >= TierThresholds[i]) n++;
            return Mathf.Min(n, MaxSlots);
        }
    }

    /// <summary>아직 파츠를 고르지 않은 티어 수. 0보다 크면 재련소가 선택을 제시한다.</summary>
    public int PendingGrants => Mathf.Max(0, UnlockedSlots - _grantedTier);

    /// <summary>다음 티어 임계액. 이미 전부 열었으면 0.</summary>
    public int NextThreshold
    {
        get
        {
            int n = UnlockedSlots;
            return n < TierThresholds.Length ? TierThresholds[n] : 0;
        }
    }

    /// <summary>현재 티어 구간의 시작액 — 진척 게이지의 0점.</summary>
    public int CurrentTierFloor
    {
        get
        {
            int n = UnlockedSlots;
            return n <= 0 ? 0 : TierThresholds[n - 1];
        }
    }

    /// <summary>다음 티어까지의 진척(0~1). 전부 열었으면 1.</summary>
    public float ProgressToNext
    {
        get
        {
            int next = NextThreshold;
            if (next <= 0) return 1f;
            int floor = CurrentTierFloor;
            return Mathf.Clamp01((float)(_invested - floor) / Mathf.Max(1, next - floor));
        }
    }

    // ── 상태 변경 ───────────────────────────────────────────

    /// <summary>
    /// 원거리 무기 강화에 재료를 썼음을 기록한다. <b>실패해도 부른다</b> —
    /// 투자는 결과와 무관하게 쌓이는 게 이 축의 요점이다. 재련소가 강화 확정 직후 호출.
    /// </summary>
    public void AddInvestment(int material)
    {
        if (material <= 0) return;
        _invested += material;
    }

    /// <summary>티어 파츠 지급을 확정한다(선택 완료). 중복 지급 방지용 이력.</summary>
    public void ConsumeGrant() => _grantedTier = Mathf.Min(_grantedTier + 1, MaxSlots);

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
    public void Restore(IEnumerable<Equipped> equipped, int invested, int grantedTier)
    {
        _equipped.Clear();
        if (equipped != null)
            foreach (var e in equipped)
                if (!string.IsNullOrEmpty(e.partId)) _equipped.Add(e);

        _invested    = Mathf.Max(0, invested);
        // 지급 이력은 최소한 장착 수만큼은 되어야 한다 — 안 그러면 복원 후 이미 받은 티어를 또 준다.
        _grantedTier = Mathf.Clamp(Mathf.Max(grantedTier, _equipped.Count), 0, MaxSlots);
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
