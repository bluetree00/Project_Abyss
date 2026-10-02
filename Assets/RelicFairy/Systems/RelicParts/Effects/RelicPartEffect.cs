using UnityEngine;

/// <summary>
/// <see cref="IRelicPartEffect"/> 기본 구현 — 모든 훅이 빈 가상 메서드(스켈레톤).
///
/// effect_key마다 이 클래스를 상속해 필요한 훅만 override 한다(룬의 RuneEffect와 같은 패턴).
/// 아직 구현하지 않은 key는 이 클래스 그대로 등록돼 <b>아무 일도 하지 않지만</b>,
/// 드래프트·획득·저장·해제 배관은 정상 동작한다(연결만 보장).
/// </summary>
public class RelicPartEffect : IRelicPartEffect
{
    public string EffectKey { get; }

    /// <summary>등급 — 기본 흐릿. 허브가 OnAcquire 전에 <see cref="SetGrade"/>로 정한다.</summary>
    public RelicMemoryGrade Grade { get; private set; } = RelicMemoryGrade.Faint;

    /// <summary>② 확장 줄이 드러났는가(선명 이상).</summary>
    protected bool Line2 => Grade >= RelicMemoryGrade.Clear;
    /// <summary>③ 변주 줄이 드러났는가(찬란).</summary>
    protected bool Line3 => Grade >= RelicMemoryGrade.Radiant;

    public RelicPartEffect(string effectKey) => EffectKey = effectKey;

    public void SetGrade(RelicMemoryGrade grade, PlayerController player)
    {
        if (grade < RelicMemoryGrade.Faint) grade = RelicMemoryGrade.Faint;
        if (grade == Grade) return;
        var before = Grade;
        Grade = grade;
        OnGradeChanged(before, player);
    }

    /// <summary>등급이 바뀌었다(「선명하게」) — 새로 열린 줄을 켠다. 획득 전 초기 설정에서도 불릴 수 있다(player 무관하게 안전해야).</summary>
    protected virtual void OnGradeChanged(RelicMemoryGrade before, PlayerController player) { }

    public virtual void OnAcquire(PlayerController player) { }
    public virtual void OnHit(in HitInfo hit, PlayerController player) { }
    public virtual void OnCrit(in HitInfo hit, PlayerController player) { }
    public virtual void OnDamaged(in HitInfo hit, PlayerController player) { }
    public virtual void OnSkillUsed(PlayerController player) { }
    public virtual void OnKill(GameObject deadEnemy, PlayerController player) { }
    public virtual void Tick(float dt, PlayerController player) { }
    public virtual void OnRemove(PlayerController player) { }
}
