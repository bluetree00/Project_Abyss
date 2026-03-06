using System;
using UnityEngine;

[Serializable]
public sealed class PlayerRuntimeStats
{
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public int AttackPower { get; private set; }
    public float HeavyChargeThreshold { get; private set; }

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
        Hp = MaxHp;
        AttackPower = Mathf.Max(0, data.attackPower);
        HeavyChargeThreshold = Mathf.Max(0f, data.heavyAttackChargeThreshold);
        OnChanged?.Invoke();
    }

    public void SetHeavyChargeThreshold(float value)
    {
        value = Mathf.Max(0f, value);
        if (Mathf.Approximately(HeavyChargeThreshold, value)) return;
        HeavyChargeThreshold = value;
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
