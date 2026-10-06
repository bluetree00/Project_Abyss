using System;
using UnityEngine;

/// <summary>
/// 진화 무기 「형」의 공통 뼈대 — 쌓기 · 사건 · 생성. 형마다 하위 클래스(칼바람 · 무게)가 규칙을 채운다.
/// WeaponFormRuntime이 플레이어 사건을 넘기고 Tick으로 시간을 흘린다. MonoBehaviour가 아니다.
/// 단련 단계 · 변주는 WeaponData를 매번 읽는다 — 재련소에서 벼려도 다시 만들 필요가 없다.
/// </summary>
public abstract class WeaponFormBase
{
    protected readonly PlayerController Player;
    protected readonly WeaponData       Weapon;
    protected readonly WeaponFormSO     Def;
    private int _stacks;

    /// <summary>(지금, 최대) — HUD 게이지(Part B) · 연출이 듣는다.</summary>
    public event Action<int, int> StacksChanged;
    /// <summary>발동 이름(풍절 · 내려찍기 · 변주 이름).</summary>
    public event Action<string> Triggered;

    protected WeaponFormBase(PlayerController player, WeaponData weapon)
    {
        Player = player;
        Weapon = weapon;
        Def    = weapon.form;
    }

    public WeaponFormSO Form      => Def;
    public int          Stacks    => _stacks;
    public int          MaxStacks => Def.MaxStacks;
    public bool         IsFull    => _stacks >= Def.MaxStacks;
    public int          Tier      => Weapon.formTier;

    /// <summary>무기의 형 정의로 만든다. 형이 없거나 모르는 id면 null.</summary>
    public static WeaponFormBase Create(PlayerController player, WeaponData weapon)
    {
        if (player == null || weapon == null || !weapon.HasForm) return null;
        return weapon.form.FormId switch
        {
            "windblade" => new WindbladeForm(player, weapon),
            "weight"    => new WeightForm(player, weapon),
            _           => null,
        };
    }

    /// <summary>근접 평타 적중. newSwing = 그 휘두름의 첫 적중, finisherStep = 콤보 마지막 타.</summary>
    public virtual void OnMeleeHit(in HitInfo hit, bool newSwing, bool finisherStep) { }
    public virtual void OnDamaged() { }
    public virtual void OnDodgeStart() { }
    public virtual void OnDodgeEnd() { }
    public virtual void OnSkillHit(in HitInfo hit) { }
    public virtual void Tick(float dt) { }
    public virtual void Dispose() { }

    protected bool  Has(string variantId) => Weapon.HasFormVariant(variantId);
    protected float Atk => Player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);

    protected void SetStacks(int v)
    {
        v = Mathf.Clamp(v, 0, Def.MaxStacks);
        if (v == _stacks) return;
        _stacks = v;
        StacksChanged?.Invoke(_stacks, Def.MaxStacks);
    }

    protected void RaiseTriggered(string name) => Triggered?.Invoke(name);
}
