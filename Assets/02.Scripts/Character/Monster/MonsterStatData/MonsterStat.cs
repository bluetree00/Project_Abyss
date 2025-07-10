using System;
using UnityEngine;

[Serializable]
public class MonsterStat
{
    // 🟩 필수 기본 스탯
    public int MaxHp;
    public int Attack;
    public float MoveSpeed;

    // 🟦 선택 스탯 (nullable 또는 기본값 처리)
    public float? LifeSteal;       // 흡혈 (일부 몬스터만 사용)
    public float? SkillResist;     // 스킬 저항
    public float? CriticalChance;  // 치명타 확률
    public float? Armor;           // 방어력

    // 🟨 기타 메타 정보 (서버 전용 키 등)
    public string MonsterId;
    public int StatVersion;

    // ✅ 유효성 검사 메서드
    public bool IsValid()
    {
        return MaxHp > 0 && Attack >= 0 && MoveSpeed > 0;
    }

    // ✅ 디버그용 로그 출력
    public void Print()
    {
        Debug.Log($"[MonsterStat] ID: {MonsterId}, HP: {MaxHp}, ATK: {Attack}, SPD: {MoveSpeed}, LS: {LifeSteal ?? 0}");
    }
}
