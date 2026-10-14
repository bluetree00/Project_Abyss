using System;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 정예 포이즈(경직 저항) — 10-11 일반방 긴장감 설계 §3-2 · P2-①.
///
/// 깨지기 전엔 맞아도 경직 · 넉백이 없다. 기본 6 · 타격마다 그 적중의 발동 계수만큼 깎는다(10-14 A2 — 카타나 평타 0.7 · 대검 1.6 · 대검 E/R 2.5) · 3초 동안 안 맞으면 다시 가득.
/// 깨지면 1.5초 휘청(받는 피해 +25%) 뒤 다시 가득 — 휘청 중 추가 타격은 휘청을 늘리지 않는다(스턴락 방지).
/// 공격 상태(공격 확약) 중에는 깨지지 않고 가득 찬 채 기다린다 — 공격이 끝난 뒤 첫 타격이 깬다.
/// 지속 피해(화상 · 출혈 · 장판 · 시너지)는 깎지 않는다 — 「DoT는 경직 없음」과 같은 원칙.
///
/// 정예 접두어(f9): 「강철의」 = <see cref="MaxScale"/> 2 · 「소환하는」 의식 = <see cref="Exposed"/> true 동안 포이즈 0(맞으면 바로 휘청).
/// </summary>
public sealed class MonsterPoise
{
    public const float  BaseHits       = 6f;
    public const float  RecoverDelay   = 3f;
    public const float  BreakDuration  = 1.5f;
    public const float  BreakDamageAmp = 0.25f;
    /// <summary>주 피해 밖에서 들어온 직접 타격(추가 공격 룬 · 형 · 유물 부가 피해)의 무게 = 2차 피해 발동 계수.</summary>
    public const float  RestingWeight  = ProcCoefficient.Secondary;
    public const string BreakStatusId  = "poise_break";   // 받는 피해 증폭 슬롯 · 상태 아이콘 「휘청」

    /// <summary>이번 직접 타격 한 번의 포이즈 무게 — CombatDamage가 그 적중의 발동 계수로 올리고 호출 직후 <see cref="RestingWeight"/>로 되돌린다.</summary>
    public static float PendingHitWeight = RestingWeight;

    /// <summary>
    /// 이번 직접 타격은 경직 · 넉백이 없다(화살 — 10-15 사용자 「활 같은 공격은 경직 · 넉백이 없어야」). CombatDamage가 호출 동안만 올린다.
    /// 포이즈는 깎되 이 타격으로는 깨지지 않는다(<see cref="Hit"/> canBreak false).
    /// </summary>
    public static bool PendingNoFlinch;

    private float _taken;
    private float _lastHitAt   = -999f;
    private float _brokenUntil = -999f;

    /// <summary>최대 포이즈 배율(기본 1 · 「강철의」 2).</summary>
    public float MaxScale { get; set; } = 1f;
    /// <summary>true 동안 포이즈 0 — 맞으면 바로 휘청(「소환하는」 의식).</summary>
    public bool  Exposed  { get; set; }
    public bool  IsBroken => Time.time < _brokenUntil;
    /// <summary>남은 포이즈 0 ~ 1(체력바 아래 줄). 휘청 · 노출 중엔 0.</summary>
    public float Fill01 => IsBroken || Exposed ? 0f : 1f - Mathf.Clamp01(Taken / Max);

    /// <summary>포이즈가 깨졌다(휘청 시작).</summary>
    public event Action Broken;

    private float Max   => BaseHits * Mathf.Max(0.1f, MaxScale);
    private float Taken => Time.time - _lastHitAt >= RecoverDelay ? 0f : _taken;

    /// <summary>
    /// 직접 타격 한 번. 반환 = 이번 타격으로 휘청에 들어가는가.
    /// <paramref name="canBreak"/> = false(공격 확약 중)면 가득 찬 채 기다린다.
    /// </summary>
    public bool Hit(float weight, bool canBreak)
    {
        if (IsBroken) return false;
        float now = Time.time;
        _taken = Taken + Mathf.Max(0f, weight);
        _lastHitAt = now;
        if (!Exposed && _taken < Max) return false;
        if (!canBreak) { _taken = Max; return false; }
        _taken = 0f;
        _brokenUntil = now + BreakDuration;
        Broken?.Invoke();
        return true;
    }

    /// <summary>풀 재사용 — 이전 몸의 포이즈 · 접두어 배율을 지운다.</summary>
    public void Reset()
    {
        _taken = 0f; _lastHitAt = -999f; _brokenUntil = -999f;
        MaxScale = 1f; Exposed = false;
        Broken = null;
    }
}
}
