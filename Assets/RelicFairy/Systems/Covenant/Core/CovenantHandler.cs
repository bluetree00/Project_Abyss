using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 런 내 서약 목록을 관리하고 이벤트를 각 서약에 디스패치하는 오케스트레이터.
/// GameRunSession이 소유하며, PlayerController/GameRunSession 이벤트를 수신해 전달한다.
/// </summary>
public sealed class CovenantHandler
{
    // ── 상수 ────────────────────────────────────────────
    public const int MaxCovenants = 4;

    // ── 상태 ────────────────────────────────────────────
    private readonly List<CovenantBase> _covenants = new();
    private CovenantContext _ctx;
    private bool _initialized;

    // 조건부 스탯(예: 저HP 보너스) 재평가 스로틀. Tick에서 누적 → 임계 시 RefreshStats.
    // (OnChanged 구독 금지 — RefreshCovenants→Recalculate→OnChanged 무한루프 회피)
    private const float RefreshInterval = 0.2f;
    private float _refreshAccum;

    public IReadOnlyList<CovenantBase> Covenants => _covenants;

    // ── 이벤트 ──────────────────────────────────────────
    public event Action OnCovenantListChanged;

    // ── 초기화 ──────────────────────────────────────────
    public void Initialize(CovenantContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _initialized = true;

        foreach (var c in _covenants)
            c.Initialize(_ctx);
    }

    // 무기 교체 어댑터 상태 (OnWeaponSwap 디스패치용)
    private PlayerWeaponManager _weapons;
    private WeaponData _lastWeapon;
    private Action<WeaponData, GameObject> _onWeaponChanged;

    public void BindPlayer(PlayerController player)
    {
        foreach (var c in _covenants)
            c.OnBoundToPlayer(player);

        // 무기 교체 → OnWeaponSwap 디스패치 (OnWeaponChanged는 새 무기만 주므로 이전 무기 캐싱)
        UnsubscribeWeapon();
        _weapons = player != null ? player.WeaponManager : null;
        if (_weapons != null)
        {
            _lastWeapon = _weapons.CurrentWeaponData;
            _onWeaponChanged = (newData, _) =>
            {
                var prev = _lastWeapon;
                _lastWeapon = newData;
                foreach (var c in _covenants) c.OnWeaponSwap(prev, newData);
            };
            _weapons.OnWeaponChanged += _onWeaponChanged;
        }
    }

    public void Cleanup()
    {
        UnsubscribeWeapon();
        foreach (var c in _covenants)
            c.Dispose();
        _covenants.Clear();
    }

    private void UnsubscribeWeapon()
    {
        if (_weapons != null && _onWeaponChanged != null)
            _weapons.OnWeaponChanged -= _onWeaponChanged;
        _onWeaponChanged = null;
        _weapons = null;
    }

    // ── 획득 / 강화 / 진화 ──────────────────────────────
    /// <summary>새 서약 추가 (런 시작 or 분기 구간)</summary>
    public bool TryAdd(string covenantId)
    {
        if (_covenants.Count >= MaxCovenants) return false;
        if (_covenants.Any(c => c.CovenantId == covenantId)) return false;

        var covenant = CovenantFactory.Create(covenantId);
        if (covenant == null) return false;

        if (_initialized)
            covenant.Initialize(_ctx);

        _covenants.Add(covenant);
        RefreshStats();
        OnCovenantListChanged?.Invoke();
        return true;
    }

    /// <summary>서약 강화 — Basic → Enhanced (이벤트 방)</summary>
    public bool TryEnhance(string covenantId)
    {
        var covenant = Find(covenantId);
        if (covenant == null || covenant.Stage != CovenantStage.Basic) return false;

        covenant.Stage = CovenantStage.Enhanced;
        RefreshStats();
        OnCovenantListChanged?.Invoke();
        return true;
    }

    /// <summary>서약 진화 — Enhanced → Evolved (보스 처치)</summary>
    public bool TryEvolve(string covenantId)
    {
        var covenant = Find(covenantId);
        if (covenant == null || covenant.Stage != CovenantStage.Enhanced) return false;

        covenant.Stage = CovenantStage.Evolved;
        RefreshStats();
        OnCovenantListChanged?.Invoke();
        return true;
    }

    /// <summary>이어하기: 저장된 서약 목록(id + 단계)을 복원한다. Initialize 이후 호출.</summary>
    public void RestoreSelections(IEnumerable<CovenantSaveEntry> entries)
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.id)) continue;
            if (!TryAdd(e.id)) continue;

            var stage = (CovenantStage)e.stage;
            if (stage >= CovenantStage.Enhanced) TryEnhance(e.id);
            if (stage >= CovenantStage.Evolved)  TryEvolve(e.id);
        }
    }

    // ── 스탯 레이어 연동 ────────────────────────────────
    /// <summary>모든 서약의 StatModifier 합산 → PlayerRuntimeStats.RefreshCovenants()에서 사용</summary>
    public IEnumerable<StatModifier> GetAllModifiers()
        => _covenants.SelectMany(c => c.GetStatModifiers());

    private void RefreshStats()
    {
        if (_initialized)
            _ctx.Stats.RefreshCovenants(this);
    }

    // ── 이벤트 디스패치 ─────────────────────────────────
    public void OnRoomEnter()
    {
        foreach (var c in _covenants) c.OnRoomEnter();
    }

    public void OnRoomClear()
    {
        foreach (var c in _covenants) c.OnRoomClear();
    }

    public void OnKill(GameObject target)
    {
        foreach (var c in _covenants) c.OnKill(target);
    }

    public void OnAttackHit(GameObject target, float damage)
    {
        foreach (var c in _covenants) c.OnAttackHit(target, damage);
    }

    public void OnTakeDamage(float damage)
    {
        foreach (var c in _covenants) c.OnTakeDamage(damage);
    }

    public void OnSkillUse(SkillType skill)
    {
        foreach (var c in _covenants) c.OnSkillUse(skill);
    }

    public void Tick(float deltaTime)
    {
        foreach (var c in _covenants) c.Tick(deltaTime);

        // 조건부 스탯 주기적 재평가 — 서약 보유 시에만(0개면 불필요한 Recalculate/OnChanged 방지)
        if (_covenants.Count == 0) return;
        _refreshAccum += deltaTime;
        if (_refreshAccum >= RefreshInterval)
        {
            _refreshAccum = 0f;
            RefreshStats();
        }
    }

    // ── 피해 파이프라인 ──────────────────────────────────
    public void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        foreach (var c in _covenants) c.ModifyOutgoingDamage(ref damage, ctx);
    }

    public void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        foreach (var c in _covenants) c.ModifyIncomingDamage(ref damage, ctx);
    }

    /// <summary>통보 한 줄용 float 반환 래퍼 — 호출부: dmg = handler?.ModifyIncoming(dmg, ctx) ?? dmg;</summary>
    public float ModifyIncoming(float damage, CombatContext ctx)
    {
        ModifyIncomingDamage(ref damage, ctx);
        return damage;
    }

    /// <summary>통보 한 줄용 float 반환 래퍼 — 호출부: dmg = handler?.ModifyOutgoing(dmg, ctx) ?? dmg;</summary>
    public float ModifyOutgoing(float damage, CombatContext ctx)
    {
        ModifyOutgoingDamage(ref damage, ctx);
        return damage;
    }

    /// <summary>HP 0 시 호출. 어느 하나라도 true 반환하면 사망 방지.</summary>
    public bool TryPreventDeath()
    {
        foreach (var c in _covenants)
        {
            if (c.TryPreventDeath()) return true;
        }
        return false;
    }

    /// <summary>치명타 오버라이드 질의. 첫 응답 서약(갤러해드)의 결과를 반환. CombatCalculator.RollCrit이 호출.</summary>
    public bool TryGetCritOverride(WeaponData weapon, out bool forceCrit, out float minFloorRatio)
    {
        foreach (var c in _covenants)
        {
            if (c.TryProvideCritOverride(weapon, out forceCrit, out minFloorRatio)) return true;
        }
        forceCrit = false; minFloorRatio = 0f; return false;
    }

    // ── 메커닉 파이프라인 ────────────────────────────────
    public void ModifySkillEffect(SkillType skill, ref SkillEffectContext ctx)
    {
        foreach (var c in _covenants) c.ModifySkillEffect(skill, ref ctx);
    }

    public void ModifySkillCost(SkillType skill, ref SkillCostContext ctx)
    {
        foreach (var c in _covenants) c.OverrideSkillCost(skill, ref ctx);
    }

    // ── 헬퍼 ────────────────────────────────────────────
    public CovenantBase Find(string covenantId)
        => _covenants.Find(c => c.CovenantId == covenantId);

    public bool Has(string covenantId)
        => _covenants.Any(c => c.CovenantId == covenantId);
}
