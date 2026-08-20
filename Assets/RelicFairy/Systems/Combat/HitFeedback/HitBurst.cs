using UnityEngine;

/// <summary>
/// 한 프레임에 발생한 타격들을 <b>하나로 합친</b> 화면 연출 페이로드.
///
/// 개별 타격(<see cref="HitInfo"/>)과 역할이 다르다.
///  · <see cref="HitInfo"/>  — "누가 누구를 어디서 때렸나". 착탄 지점마다 나야 하는 국소 연출용.
///  · <see cref="HitBurst"/> — "이번 순간 화면이 얼마나 흔들려야 하나". 공격 1회당 한 번이어야 하는 글로벌 연출용.
///
/// 분열 20발 + 관통 + 폭발이 붙은 원거리 공격은 몇 프레임 안에 타격 수십 건을 만든다.
/// 그 전부가 각자 셰이크·플래시를 걸면 화면이 통째로 터지므로, 글로벌 채널은 이 구조체로 합산해 1회만 쓴다.
/// </summary>
public readonly struct HitBurst
{
    /// <summary>합산된 타격 수. 1이면 단타와 완전히 동일하게 동작한다.</summary>
    public readonly int Count;
    /// <summary>가장 센 한 방. 셰이크·히트스톱의 세기는 합계가 아니라 이 값이 정한다.</summary>
    public readonly float MaxDamage;
    /// <summary>합계 피해. 연출 세기엔 쓰지 않고 정보용(디버그·후속 튜닝).</summary>
    public readonly float TotalDamage;
    /// <summary>하나라도 치명타였는가. 크리 전용 연출(줌인·강펄스)의 게이트.</summary>
    public readonly bool AnyCritical;
    /// <summary>공격 방향 평균(정규화). 사방에서 맞으면 상쇄돼 0에 가까워지고, 그때는 무방향 셰이크가 옳다.</summary>
    public readonly Vector3 Direction;
    /// <summary>가장 센 타격의 착탄 지점.</summary>
    public readonly Vector3 HitPoint;
    /// <summary>가장 센 타격의 무기 — 손맛 프로필(WeaponFeelTable) 조회용.</summary>
    public readonly WeaponType WeaponType;
    /// <summary>가장 센 타격의 공격자.</summary>
    public readonly GameObject Attacker;

    public HitBurst(int count, float maxDamage, float totalDamage, bool anyCritical,
                    Vector3 direction, Vector3 hitPoint, WeaponType weaponType, GameObject attacker)
    {
        Count       = count;
        MaxDamage   = maxDamage;
        TotalDamage = totalDamage;
        AnyCritical = anyCritical;
        Direction   = direction;
        HitPoint    = hitPoint;
        WeaponType  = weaponType;
        Attacker    = attacker;
    }
}
