using System;
using UnityEngine;

[Serializable]
public sealed class PlayerRuntimeStats
{
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public int AttackPower { get; private set; }

    public event Action OnChanged;

    public void InitializeFrom(CharacterData data)
    {
        if (data == null)
        {
            Debug.LogError("[PlayerRuntimeStats] CharacterData is null.");
            return;
        }

        // SO는 템플릿. 런타임 값은 여기로 복사.
        MaxHp = Mathf.Max(1, data.maxHealth);

        // 런 시작 시 풀피로 시작하는 정책
        Hp = MaxHp;

        // SO 내부 totalAttackPower를 쓰려면 Initialize()로 계산되어 있어야 함
        // (단, SO 자체 값을 바꾸지 않도록 주의)
        AttackPower = Mathf.Max(0, data.attackPower);
        OnChanged?.Invoke();
    }

    public void SetHp(int hp)
    {
        hp = Mathf.Clamp(hp, 0, MaxHp);
        if (Hp == hp) return;
        Hp = hp;
        OnChanged?.Invoke();
    }

    public void Damage(int amount)
    {
        if (amount <= 0) return;
        SetHp(Hp - amount);
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        SetHp(Hp + amount);
    }

    public void SetAttackPower(int attackPower)
    {
        attackPower = Mathf.Max(0, attackPower);
        if (AttackPower == attackPower) return;
        AttackPower = attackPower;
        OnChanged?.Invoke();
    }

    // (선택) 무기/버프를 런타임에서 반영하고 싶다면 이렇게 누적 방식으로 가는 걸 추천
    private int _attackBonus;
    public void AddAttackBonus(int bonus)
    {
        if (bonus == 0) return;
        _attackBonus += bonus;
        AttackPower = Mathf.Max(0, AttackPower + bonus);
        OnChanged?.Invoke();
    }
}
