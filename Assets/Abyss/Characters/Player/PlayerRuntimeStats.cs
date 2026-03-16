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
        _baseAttackPower = Mathf.Max(0, data.attackPower);
        _weaponAttack = 0;
        AttackPower = _baseAttackPower;
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

    // 캐릭터 기본 공격력 (CharacterData 기준, 변하지 않음)
    private int _baseAttackPower;
    // 현재 장착 무기의 공격력 (무기 교체 시 덮어씀)
    private int _weaponAttack;

    /// <summary>
    /// 무기 장착/해제 시 호출. baseAttack 기준으로 AttackPower를 재계산합니다.
    /// </summary>
    public void SetWeaponStats(int weaponAttack)
    {
        _weaponAttack = Mathf.Max(0, weaponAttack);
        AttackPower = _baseAttackPower + _weaponAttack;
        OnChanged?.Invoke();
    }
}
