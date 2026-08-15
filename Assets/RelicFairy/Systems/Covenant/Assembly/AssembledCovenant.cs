using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 조립 서약 — 원인(Cause) × 효과(Effect) 데이터구동 단일 클래스(조합마다 클래스 X).
/// CovenantId = "asm:&lt;causeId&gt;@&lt;tier&gt;|&lt;effectId&gt;@&lt;tier&gt;" 로 인코딩 → 기존 CovenantFactory/Handler/세이브 그대로 재사용.
/// (@tier 생략 시 실버로 폴백 — 구버전 id 호환)
/// 원인 트리거 override에서 조건 판정 → ApplyEffect(EffectKind별 CovenantBase 헬퍼 호출).
/// 효과 크기는 <see cref="CovenantMath"/>가 단독으로 계산한다(표시=동작 단일 소스). 티어 = 순수 파워 축.
/// </summary>
public sealed class AssembledCovenant : CovenantBase
{
    public const string Prefix = "asm:";

    // 근접 판정 상수(포위·최근접 대상 탐색)
    private const float ProximityRadius        = 5f;
    private const float ProximityCheckInterval = 0.5f;
    private const float TargetSearchRadius     = 8f;

    // 화상 틱 간격 — 다른 화상 사용처(SolarZone/FireField)와 같은 결로 맞춘다.
    private const float BurnTickInterval = 0.5f;

    // 루비 발동 VFX 키. 자산이 없으면 CovenantBase.Vfx가 조용히 무동작한다.
    private const string RubyVfxKey = "VFX/Covenant/RubyProc";

    private static readonly Collider[] _probe = new Collider[32];

    private readonly string _causeId, _effectId;
    private readonly CovenantTier _causeTier, _effectTier;
    private readonly bool   _resolved;
    private readonly CovenantPalette.CauseDef  _cause;
    private readonly CovenantPalette.EffectDef _effect;

    // 티어·계수·스케일 모드가 모두 반영된 유효 수치. 미리보기 UI가 쓰는 것과 같은 함수다.
    private float Eff      => _resolved ? CovenantMath.Effective(_effect, _effectTier, _cause, _causeTier) : 0f;
    private int   EffCount => _resolved ? CovenantMath.EffectiveCount(_effect, _effectTier, _cause, _causeTier) : 0;

    // 원인 런타임 상태
    private GameObject _streakTarget;
    private int   _streak;
    private int   _killStreak;
    private float _swapWindowEnd;
    private float _periodicTimer;
    private float _killHitWindowEnd;   // 사냥 개시
    private bool  _firstHitArmed;      // 선제
    private float _proximityTimer, _proximityCd;   // 포위
    private Vector3 _lastPos; private bool _movePrimed; private float _moveAccum;   // 행군

    // 효과 타이머(격노)
    private bool  _buffActive;
    private float _buffEnd;

    // 효과 내부 쿨다운 — 값은 팔레트(EffectDef.icd)가 쥔다. 0이면 쿨다운 없음.
    private float _icdEnd;

    // 「마지막 숨결」 남은 충전(치명 피해 생존 횟수). 생성 시 유효 횟수로 채운다.
    private int _deathSaveCharges;

    // 「박차」 중첩 상태
    private int   _momentumStacks;
    private float _momentumEnd;
    private readonly StatModifier[] _momentumMods = new StatModifier[2];

    /// <summary>
    /// 효과 적용 중 플래그. 광역 폭발이 적을 죽이면 그 처치가 다시 원인(학살·사냥 개시)을 물고
    /// 같은 효과를 재입력한다 — 한 번의 발동이 프레임 안에서 스스로를 되먹여 폭주한다.
    /// 적용 구간에 들어온 트리거는 통째로 무시해 되먹임 고리를 끊는다.
    /// </summary>
    private bool _inEffect;

    /// <param name="causePart">"causeId" 또는 "causeId@tier"</param>
    /// <param name="effectPart">"effectId" 또는 "effectId@tier"</param>
    public AssembledCovenant(string causePart, string effectPart)
    {
        (_causeId,  _causeTier)  = SplitTier(causePart);
        (_effectId, _effectTier) = SplitTier(effectPart);
        bool a = CovenantPalette.TryGetCause(_causeId, out _cause);
        bool b = CovenantPalette.TryGetEffect(_effectId, out _effect);
        _resolved = a && b;

        if (_resolved && _effect.kind == EffectKind.DeathSave)
            _deathSaveCharges = EffCount;
    }

    /// <summary>팔레트에서 원인·효과가 모두 해석됐는지. 미해결이면 껍데기라 슬롯을 차지시키지 않는다.</summary>
    public bool Resolved => _resolved;

    private static (string id, CovenantTier tier) SplitTier(string part)
    {
        if (string.IsNullOrEmpty(part)) return (part, CovenantTier.Silver);
        int at = part.IndexOf('@');
        return at < 0
            ? (part, CovenantTier.Silver)
            : (part.Substring(0, at), CovenantTierUtil.Parse(part.Substring(at + 1)));
    }

    public static string MakeId(string causeId, CovenantTier causeTier, string effectId, CovenantTier effectTier)
        => Prefix + causeId + "@" + causeTier.Code() + "|" + effectId + "@" + effectTier.Code();

    public override string CovenantId       => MakeId(_causeId, _causeTier, _effectId, _effectTier);
    public override CovenantCategory Category => _resolved ? _cause.category : base.Category;
    public override string DisplayName      => _resolved
        ? _cause.name + "[" + _causeTier.DisplayName() + "] × " + _effect.name + "[" + _effectTier.DisplayName() + "]"
        : CovenantId;
    public override string BasicDescription => _resolved ? _cause.desc + " → " + _effect.desc : string.Empty;

    // 원인/결과를 따로 노출 → HUD가 한 줄로 이어붙이지 않고 줄을 나눠 보여준다.
    public override string CauseText  => _resolved ? _cause.desc  : null;
    public override string EffectText => _resolved ? _effect.desc : null;

    // ── 원인 트리거 → ApplyEffect ─────────────────────────
    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (!_resolved || _inEffect) return;
        switch (_cause.trigger)
        {
            case CauseTriggerKind.OnHitStreakSameTarget:
                if (target == _streakTarget) _streak++;
                else { _streakTarget = target; _streak = 1; }
                if (_streak >= _cause.thresholdInt) { _streak = 0; ApplyEffect(target); }
                break;
            case CauseTriggerKind.OnWeaponSwapWindow:
                if (Time.time <= _swapWindowEnd) { _swapWindowEnd = 0f; ApplyEffect(target); }
                break;
            case CauseTriggerKind.OnKillThenHitWindow:
                if (Time.time <= _killHitWindowEnd) { _killHitWindowEnd = 0f; ApplyEffect(target); }
                break;
            case CauseTriggerKind.OnFirstHitInRoom:
                if (_firstHitArmed) { _firstHitArmed = false; ApplyEffect(target); }
                break;
        }
    }

    public override void OnKill(GameObject target)
    {
        if (!_resolved || _inEffect) return;
        switch (_cause.trigger)
        {
            case CauseTriggerKind.OnKillStreak:
                _killStreak++;
                if (_killStreak >= _cause.thresholdInt) { _killStreak = 0; ApplyEffect(target); }
                break;
            case CauseTriggerKind.OnKillThenHitWindow:
                _killHitWindowEnd = Time.time + _cause.thresholdF;
                break;
        }
    }

    public override void OnWeaponSwap(WeaponData prev, WeaponData next)
    {
        if (_resolved && !_inEffect && _cause.trigger == CauseTriggerKind.OnWeaponSwapWindow)
            _swapWindowEnd = Time.time + _cause.thresholdF;
    }

    public override void OnRoomEnter()
    {
        if (_resolved && _cause.trigger == CauseTriggerKind.OnFirstHitInRoom) _firstHitArmed = true;
    }

    public override void OnRoomClear()
    {
        if (_resolved && !_inEffect && _cause.trigger == CauseTriggerKind.OnRoomClear) ApplyEffect(null);
    }

    public override void OnSkillUse(SkillType skill)
    {
        if (_resolved && !_inEffect && _cause.trigger == CauseTriggerKind.OnSkillUse) ApplyEffect(null);
    }

    public override void Tick(float deltaTime)
    {
        if (!_resolved || _inEffect) return;
        switch (_cause.trigger)
        {
            case CauseTriggerKind.Periodic:
                _periodicTimer += deltaTime;
                if (_periodicTimer >= _cause.thresholdF) { _periodicTimer = 0f; ApplyEffect(null); }
                break;
            case CauseTriggerKind.OnProximity:
                _proximityTimer += deltaTime;
                if (_proximityTimer >= ProximityCheckInterval)
                {
                    _proximityTimer = 0f;
                    if (Time.time >= _proximityCd && CountNearbyEnemies(ProximityRadius) >= _cause.thresholdInt)
                    {
                        _proximityCd = Time.time + _cause.thresholdF;
                        ApplyEffect(null);
                    }
                }
                break;
            case CauseTriggerKind.OnMoveDistance:
                if (Ctx?.Player != null)
                {
                    Vector3 p = PlayerPos;
                    if (!_movePrimed) { _lastPos = p; _movePrimed = true; }
                    else
                    {
                        _moveAccum += Vector3.Distance(p, _lastPos);
                        _lastPos = p;
                        if (_moveAccum >= _cause.thresholdF) { _moveAccum = 0f; ApplyEffect(null); }
                    }
                }
                break;
        }

        if (_buffActive && Time.time >= _buffEnd) _buffActive = false;

        // 「박차」 만료 — 스탯 레이어에 즉시 반영해야 중첩이 끝난 뒤에도 속도가 남아 있지 않다.
        if (_momentumStacks > 0 && Time.time >= _momentumEnd)
        {
            _momentumStacks = 0;
            RefreshStats();
        }
    }

    // 격노: 지속 중 나가는 피해 증폭
    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (_resolved && _buffActive && _effect.kind == EffectKind.DamageBuff)
            damage *= 1f + Eff;
    }

    // ── 死훅 (스탯 / 사망방지 / 버프창) ──────────────────
    /// <summary>「박차」 중첩을 스탯 레이어에 얹는다. CovenantHandler가 0.2초마다 폴링해 재적용한다.</summary>
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        if (!_resolved || _effect.kind != EffectKind.StatBuff || _momentumStacks <= 0)
            return Array.Empty<StatModifier>();

        float per = Eff * _momentumStacks;
        _momentumMods[0] = new StatModifier(StatType.MoveSpeed,   per);
        _momentumMods[1] = new StatModifier(StatType.AttackSpeed, per * CovenantMath.MomentumAtkSpeedRatio);
        return _momentumMods;
    }

    /// <summary>「마지막 숨결」 — 충전이 남아 있으면 사망을 취소한다(HP 복원·무적은 PlayerController가 처리).</summary>
    public override bool TryPreventDeath()
    {
        if (!_resolved || _effect.kind != EffectKind.DeathSave || _deathSaveCharges <= 0) return false;

        _deathSaveCharges--;
        CovenantFxService.Play(_effectTier, _effectId, Vector3.zero);
        return true;
    }

    /// <summary>일시 발동/지속 상태만 버프창에 노출(상시 보유 목록은 CovenantPanelView 담당).</summary>
    public override bool TryGetBuffView(out BuffViewItem item)
    {
        if (_resolved && _buffActive && _effect.kind == EffectKind.DamageBuff)
        {
            item = new BuffViewItem(
                iconKey:     "dmg",
                label:       $"{_effect.name} 피해 +{Eff * 100f:0}%",
                stacks:      1,
                remaining01: Remaining01(_buffEnd, _effect.duration),
                remainText:  string.Empty,
                source:      BuffSource.Relic,
                isDebuff:    false);
            return true;
        }

        if (_resolved && _momentumStacks > 0 && _effect.kind == EffectKind.StatBuff)
        {
            item = new BuffViewItem(
                iconKey:     "speed",
                label:       $"{_effect.name} 이속 +{Eff * _momentumStacks * 100f:0}%",
                stacks:      _momentumStacks,
                remaining01: Remaining01(_momentumEnd, _effect.duration),
                remainText:  string.Empty,
                source:      BuffSource.Relic,
                isDebuff:    false);
            return true;
        }

        item = default;
        return false;
    }

    private static float Remaining01(float end, float duration)
        => duration > 0f ? Mathf.Clamp01((end - Time.time) / duration) : -1f;

    // ── 효과 적용 ─────────────────────────────────────────
    private void ApplyEffect(GameObject target)
    {
        if (Ctx == null || _inEffect) return;
        if (_effect.icd > 0f && Time.time < _icdEnd) return;

        _inEffect = true;
        try
        {
            if (!Fire(target)) return;   // 헛방(대상 없음 등)은 쿨다운도 연출도 소모하지 않는다

            if (_effect.icd > 0f) _icdEnd = Time.time + _effect.icd;
            PlayTierFx(target);
        }
        finally
        {
            _inEffect = false;
        }
    }

    /// <summary>실제 효과 1회. 반환=발동했는지(false면 쿨다운/연출 미소모).</summary>
    private bool Fire(GameObject target)
    {
        switch (_effect.kind)
        {
            case EffectKind.AoeBurst:
            {
                Vector3 pos = target != null ? target.transform.position : PlayerPos;
                DealAoe(pos, CovenantMath.EffectiveRadius(_effect), Eff);
                return true;
            }
            case EffectKind.DamageBuff:
                _buffActive = true; _buffEnd = Time.time + _effect.duration;
                return true;

            case EffectKind.Shield:
                if (Ctx.Player?.RuntimeStats == null) return false;
                Ctx.Player.RuntimeStats.AddShield(Eff);
                return true;

            case EffectKind.GoldBurst:
                Ctx.Session?.AddGold(Mathf.RoundToInt(Eff));
                return true;

            case EffectKind.Curse:
                return ApplyCurse(ResolveTarget(target));

            case EffectKind.Execute:
                return TryExecute(ResolveTarget(target));

            case EffectKind.Burn:
                return ApplyBurn(ResolveTarget(target));

            case EffectKind.Invincible:
                if (Ctx.Player == null) return false;
                Ctx.Player.SetInvincible(Eff);
                return true;

            case EffectKind.StatBuff:
                _momentumStacks = Mathf.Min(_momentumStacks + 1, CovenantMath.MomentumMaxStacks);
                _momentumEnd    = Time.time + _effect.duration;
                RefreshStats();
                return true;

            // 「마지막 숨결」은 상시 대기하는 충전이라 원인 발동으로는 아무 일도 하지 않는다
            // (원인 계수는 생성 시 충전 횟수로 이미 환산됐다).
            case EffectKind.DeathSave:
            default:
                return false;
        }
    }

    /// <summary>티어 도파민 연출 — 루비만 VFX까지 얹는다(Vfx는 CovenantBase 헬퍼 재사용).</summary>
    private void PlayTierFx(GameObject target)
    {
        Vector3 pos = target != null ? target.transform.position : PlayerPos;
        Vector3 dir = pos - PlayerPos;

        if (CovenantFxService.Play(_effectTier, _effectId, dir) && _effectTier == CovenantTier.Ruby)
            Vfx(RubyVfxKey, pos);
    }

    // ── 효과 헬퍼 ─────────────────────────────────────────
    private bool ApplyCurse(GameObject target)
    {
        if (target == null) return false;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null) return false;
        mb.ApplyDamageTakenAmp(Eff, _effect.duration, "cov_curse");
        return true;
    }

    /// <summary>화상 부여 — dps = 유효 공격력 × 유효 수치.</summary>
    private bool ApplyBurn(GameObject target)
    {
        if (target == null || Ctx?.Player == null || Ctx.Stats == null) return false;

        float dps = EffectiveAttack() * Eff;
        if (dps <= 0f) return false;

        MonsterBurnHandler.Apply(target, dps, _effect.duration, BurnTickInterval, Ctx.Player.gameObject);
        return true;
    }

    /// <summary>
    /// 처형 — 임계 이하 대상 즉사. 보스는 즉사시키지 않는다(페이즈·연출이 통째로 건너뛰어진다):
    /// 대신 최대 체력의 임계 비율만큼을 피해로 넣어 "크게 베어낸다"로 번역한다.
    /// </summary>
    private bool TryExecute(GameObject target)
    {
        if (target == null || Ctx?.Player == null) return false;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return false;

        int max = mb.EffectiveMaxHp;
        if (max <= 0) return false;

        float threshold = Eff;   // 계수까지 반영된 임계(구현상 magnitude만 보던 결함 수정)
        if ((float)mb.CurrentHp / max > threshold) return false;

        if (mb.Grade == MonsterGrade.Boss)
            mb.TakeSynergyDamage(max * threshold, Ctx.Player.gameObject, 1f, false, DamageKind.Synergy);
        else
            mb.TakeDamage(mb.CurrentHp * 10f, Ctx.Player.gameObject, 0.3f);
        return true;
    }

    /// <summary>현재 무기 계통 기준 유효 공격력(DealAoe와 같은 해석).</summary>
    private float EffectiveAttack()
    {
        var weaponData = Ctx.Player.WeaponManager?.CurrentWeaponData;
        var kind = weaponData != null ? weaponData.weaponType.GetAttackStatKind() : AttackStatKind.Melee;
        return Ctx.Stats.GetEffectiveAttack(kind);
    }

    /// <summary>
    /// 저주·처형·화상 대상 해석 — 대상이 살아있으면 그대로, 죽었거나(학살=시체) 없으면(주기·클리어 등)
    /// 발동 지점 근처의 살아있는 적을 잡는다(시체 헛방 방지).
    /// </summary>
    private GameObject ResolveTarget(GameObject target)
    {
        if (target != null)
        {
            var mb = target.GetComponentInParent<MonsterBase>();
            if (mb != null && !mb.IsDead) return target;
            return NearestLiveEnemy(target.transform.position, TargetSearchRadius);
        }
        return NearestLiveEnemy(PlayerPos, TargetSearchRadius);
    }

    private GameObject NearestLiveEnemy(Vector3 center, float radius)
    {
        int n = Physics.OverlapSphereNonAlloc(center, radius, _probe);
        GameObject best = null; float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var col = _probe[i];
            if (col == null) continue;
            if (!col.TryGetComponent<MonsterBase>(out var mb) || mb.IsDead) continue;
            float sq = (col.transform.position - center).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = col.gameObject; }
        }
        return best;
    }

    private int CountNearbyEnemies(float radius)
    {
        if (Ctx?.Player == null) return 0;
        int n = Physics.OverlapSphereNonAlloc(PlayerPos, radius, _probe);
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            var col = _probe[i];
            if (col == null) continue;
            if (col.TryGetComponent<MonsterBase>(out var mb) && !mb.IsDead) count++;
        }
        return count;
    }
}
