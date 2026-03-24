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
        _weaponAttack    = 0;
        _itemAttackBonus = 0;
        _roomAttackBuff  = 0;
        HeavyChargeThreshold = Mathf.Max(0f, data.heavyAttackChargeThreshold);

        // 패시브 초기 적용
        ApplyPassive(data.passive);   // RecalculateAttack + OnChanged 포함
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

    // ── 스탯 레이어 ──────────────────────────────────────────────────────────
    // 최종 스탯 = Base (CharacterData)
    //           + PassiveBonus  (캐릭터 패시브, 런 시작 시 1회)
    //           + WeaponBonus   (장착 무기)
    //           + ItemBonus     (아이템 누적, 런 내 영구)
    //           + RoomBuff      (일시적, 방 단위)

    private int _baseAttackPower;   // CharacterData 원본
    private int _passiveBonus;      // PassiveSO 적용분
    private int _weaponAttack;      // 현재 장착 무기
    private int _itemAttackBonus;   // RunItemInventory 누적분
    private int _roomAttackBuff;    // RoomBuffHandler 일시 버프

    // ── 무기 ─────────────────────────────────────────────────────────────────
    /// <summary>무기 장착/해제 시 호출.</summary>
    public void SetWeaponStats(int weaponAttack)
    {
        _weaponAttack = Mathf.Max(0, weaponAttack);
        RecalculateAttack();
    }

    // ── 패시브 ───────────────────────────────────────────────────────────────
    /// <summary>런 시작 시 PassiveSO 적용. null이면 0으로 초기화.</summary>
    public void ApplyPassive(PassiveSO passive)
    {
        _passiveBonus = 0;
        if (passive != null)
            foreach (var mod in passive.baseModifiers)
                if (mod.Type == StatType.AttackPower) _passiveBonus += (int)mod.Value;
        RecalculateAttack();
    }

    // ── 아이템 누적 ──────────────────────────────────────────────────────────
    /// <summary>RunItemInventory.OnInventoryChanged 이벤트에 연결.</summary>
    public void RefreshItemBonuses(RunItemInventory inventory)
    {
        _itemAttackBonus = inventory != null ? (int)inventory.GetTotal(StatType.AttackPower) : 0;
        RecalculateAttack();
    }

    // ── 방 버프 ──────────────────────────────────────────────────────────────
    /// <summary>RoomBuffHandler.OnBuffsChanged 이벤트에 연결.</summary>
    public void RefreshRoomBuffs(RoomBuffHandler handler)
    {
        _roomAttackBuff = handler != null ? (int)handler.GetTotal(StatType.AttackPower) : 0;
        RecalculateAttack();
    }

    // ── 내부 재계산 ──────────────────────────────────────────────────────────
    private void RecalculateAttack()
    {
        AttackPower = Mathf.Max(0, _baseAttackPower + _passiveBonus + _weaponAttack
                                   + _itemAttackBonus + _roomAttackBuff);
        OnChanged?.Invoke();
    }
}
