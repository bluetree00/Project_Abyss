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

    // 처형 「표식」 탐색 반경 — 경계 원인(개선·선제)은 방 단위 사건이라 근접 반경으로는 짚을 게 없다.
    private const float ExecuteMarkRadius      = 12f;

    // 루비 발동 VFX 키. 자산이 없으면 CovenantBase.Vfx가 조용히 무동작한다.
    private const string RubyVfxKey = "VFX/Covenant/RubyProc";

    private static readonly Collider[] _probe = new Collider[32];

    // 광역 효과가 훑어 담는 대상 목록. 인스턴스별로 따로 갖는다 — 정적으로 두면 광역 피해가
    // 다른 서약의 원인을 물었을 때 그 서약이 같은 리스트를 비워버려 진행 중인 순회가 사라진다.
    private readonly List<MonsterBase> _areaScratch = new();

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

    // 「결계」 — 발동 시점에 굳힌 받피 감소량과 만료 시각.
    // 감소량을 매 피격마다 다시 세지 않는 이유: 그러면 결계의 두께가 맞는 순간에만 얇아졌다 두꺼워졌다 해
    // 플레이어가 "지금 얼마나 단단한가"를 알 수 없다. 두께는 걸리는 순간 정해진다.
    private float _wardReduction;
    private float _wardEnd;

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

    // ── 상태 통화 노출(시너지 힌트) ──────────────────────
    // UI가 "지금 가진 서약이 이 조합과 물리는가"를 판정하려면 보유 서약 쪽 통화·역할을 읽을 수 있어야 한다.
    /// <summary>이 서약이 다루는 상태 통화.</summary>
    public StatusCurrency Status => _resolved ? _effect.status : StatusCurrency.None;
    /// <summary>그 통화를 거는가(Apply) 먹는가(Consume).</summary>
    public StatusRole Role => _resolved ? _effect.role : StatusRole.None;
    /// <summary>효과 표시명(힌트 문구가 "무엇과 물리는지" 이름으로 짚어준다).</summary>
    public string EffectName => _resolved ? _effect.name : null;

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
                    int near = CountNearbyEnemies(ProximityRadius);

                    // 「격노」 위험 변형(④) — 포위가 풀리면 증폭도 그 자리에서 꺼진다.
                    // 1.25배를 그냥 주는 게 아니라 "둘러싸여 있는 동안만"이라는 대가를 붙이는 쪽이다.
                    // 여기 얹는 이유: 인접 수는 이 분기가 이미 세고 있다(매 프레임 OverlapSphere를 새로 돌 이유가 없다).
                    if (_buffActive && _effect.kind == EffectKind.DamageBuff && near < _cause.thresholdInt)
                        _buffActive = false;

                    if (Time.time >= _proximityCd && near >= _cause.thresholdInt)
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
            damage *= 1f + FuryAmp;
    }

    /// <summary>「결계」 — 지속 중 받는 피해 감소. 체력을 되돌리는 게 아니라 애초에 덜 맞는다.</summary>
    public override void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        if (_resolved && _effect.kind == EffectKind.Ward && Time.time < _wardEnd)
            damage *= 1f - _wardReduction;
    }

    /// <summary>「격노」 증폭량 — 위험 원인(포위)이면 1.25배, 대신 포위가 풀리는 순간 꺼진다(④).</summary>
    private float FuryAmp
        => Eff * (_cause.cls == CauseClass.Danger ? CovenantMath.FuryDangerBonus : 1f);

    // ── 死훅 (스탯 / 사망방지 / 버프창) ──────────────────
    /// <summary>「박차」 중첩을 스탯 레이어에 얹는다. CovenantHandler가 0.2초마다 폴링해 재적용한다.</summary>
    public override IEnumerable<StatModifier> GetStatModifiers()
    {
        if (!_resolved || _effect.kind != EffectKind.StatBuff || _momentumStacks <= 0)
            return Array.Empty<StatModifier>();

        MomentumRates(out float move, out float atk);
        _momentumMods[0] = new StatModifier(StatType.MoveSpeed,   move);
        _momentumMods[1] = new StatModifier(StatType.AttackSpeed, atk);
        return _momentumMods;
    }

    /// <summary>
    /// 「박차」 원인 형상 변형(③). 같은 중첩량을 어디에 싣느냐가 달라진다 —
    /// 기동으로 쌓은 박차는 <b>달리기</b>가 되고, 처치로 쌓은 박차는 <b>휘두르기</b>가 된다.
    /// (기동은 상한이 높고, 처치는 지속이 두 배 — <see cref="MomentumDuration"/>.)
    /// </summary>
    private void MomentumRates(out float move, out float atk)
    {
        float per = Eff * _momentumStacks;
        switch (_cause.cls)
        {
            case CauseClass.Mobility:
                move = per; atk = per * CovenantMath.MomentumOffAxisRatio; break;
            case CauseClass.Kill:
                atk = per; move = per * CovenantMath.MomentumOffAxisRatio; break;
            default:
                move = per; atk = per * CovenantMath.MomentumAtkSpeedRatio; break;
        }
    }

    private float MomentumDuration
        => _effect.duration * (_cause.cls == CauseClass.Kill ? CovenantMath.MomentumKillDurationMult : 1f);

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
                label:       $"{_effect.name} 피해 +{FuryAmp * 100f:0}%",
                stacks:      1,
                remaining01: Remaining01(_buffEnd, _effect.duration),
                remainText:  string.Empty,
                source:      BuffSource.Relic,
                isDebuff:    false);
            return true;
        }

        if (_resolved && _momentumStacks > 0 && _effect.kind == EffectKind.StatBuff)
        {
            MomentumRates(out float move, out float atk);
            item = new BuffViewItem(
                iconKey:     "speed",
                label:       $"{_effect.name} 이속 +{move * 100f:0}% · 공속 +{atk * 100f:0}%",
                stacks:      _momentumStacks,
                remaining01: Remaining01(_momentumEnd, MomentumDuration),
                remainText:  string.Empty,
                source:      BuffSource.Relic,
                isDebuff:    false);
            return true;
        }

        if (_resolved && _effect.kind == EffectKind.Ward && Time.time < _wardEnd)
        {
            item = new BuffViewItem(
                iconKey:     "def",
                label:       $"{_effect.name} 받는 피해 -{_wardReduction * 100f:0}%",
                stacks:      1,
                remaining01: Remaining01(_wardEnd, _effect.duration),
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
                DealAoe(pos, SupernovaRadius(pos), Eff);
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

            // 처형은 대상 해석을 스스로 한다 — 경계 원인이면 '때린 적'이 아니라 방의 최약체를 짚기 때문에
            // 여기서 미리 풀어 주면 그 탐색이 통째로 버려진다.
            case EffectKind.Execute:
                return TryExecute(target);

            case EffectKind.Burn:
                return ApplyBurn(ResolveTarget(target));

            case EffectKind.BleedStack:
                return ApplyBleed(ResolveTarget(target));

            case EffectKind.Detonate:
                return Detonate(target);

            case EffectKind.Harvest:
                return Harvest(ResolveTarget(target));

            case EffectKind.Arcflash:
                return Arcflash(target);

            case EffectKind.Stasis:
                return Stasis(target);

            case EffectKind.Ward:
                return Ward();

            case EffectKind.Invincible:
                if (Ctx.Player == null) return false;
                Ctx.Player.SetInvincible(Eff);
                return true;

            case EffectKind.StatBuff:
                _momentumStacks = Mathf.Min(_momentumStacks + 1, CovenantMath.MomentumStackCap(_cause.cls));
                _momentumEnd    = Time.time + MomentumDuration;
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
    // 통화를 걸고·키우고·먹는 일은 전부 <see cref="CovenantStatus"/>(환전소)를 지난다.
    // 여기서 채널을 직접 부르면 "무엇이 통화인가"를 아는 곳이 효과 수만큼 늘어난다.

    private bool ApplyCurse(GameObject target)
    {
        if (Live(target) == null) return false;
        CovenantStatus.Apply(target, StatusCurrency.Vulnerable, Eff, _effect.duration, Ctx?.Player?.gameObject);
        return true;
    }

    /// <summary>화상 부여 — dps = 유효 공격력 × 유효 수치.</summary>
    private bool ApplyBurn(GameObject target)
    {
        if (target == null || Ctx?.Player == null || Ctx.Stats == null) return false;

        float dps = EffectiveAttack() * Eff;
        if (dps <= 0f) return false;

        CovenantStatus.Apply(target, StatusCurrency.Burn, dps, _effect.duration, Ctx.Player.gameObject);
        return true;
    }

    /// <summary>출혈 부여 — dps/스택 = 유효 공격력 × 유효 수치. 같은 대상에 다시 걸면 dps가 누적된다.</summary>
    private bool ApplyBleed(GameObject target)
    {
        if (target == null || Ctx?.Player == null || Ctx.Stats == null) return false;

        float dps = EffectiveAttack() * Eff;
        if (dps <= 0f) return false;

        CovenantStatus.Amplify(target, StatusCurrency.Bleed, dps, _effect.duration, Ctx.Player.gameObject);
        return true;
    }

    // ── 감전 계열(C3) ─────────────────────────────────────
    /// <summary>
    /// 방전 — 대상과 인근에 감전 1스택씩. 원인 형상 분기:
    ///  • 스킬 원인 → <b>순차 체인</b>. 한 발이 적을 타고 넘어가는 그림이라 대상 수가 한 명 더 붙되,
    ///    다음 적이 <see cref="CovenantMath.ArcflashChainHop"/> 안에 없으면 거기서 끊긴다(뭉친 무리에 강하다).
    ///    체인이 뻗는 범위 자체는 두 방식 모두 발동 지점 반경 안이다 — 한 발이 방을 가로지르지 않게.
    ///  • 그 외 → <b>방사형</b>. 발동 지점에서 가까운 순으로 반경 안을 채운다(퍼진 무리에 고르다).
    /// 감전의 세기는 늘 1스택 고정 — 커지는 건 '몇에게 거는가'다.
    /// </summary>
    private bool Arcflash(GameObject target)
    {
        if (Ctx?.Player == null) return false;

        var primary = Live(ResolveTarget(target));
        if (primary == null) return false;

        var inst = Ctx.Player.gameObject;
        Shock(primary, inst);

        bool  chain = _cause.cls == CauseClass.Skill;
        int   extra = EffCount + (chain ? CovenantMath.ArcflashChainBonus : 0);
        float radius = CovenantMath.EffectiveRadius(_effect);

        // 훑기를 먼저 끝낸다 — _probe는 서약 인스턴스끼리 공유하는 정적 버퍼다(CollectLiveEnemies 주석 참조).
        CollectLiveEnemies(primary.transform.position, radius, primary);

        Vector3 from = primary.transform.position;
        for (int i = 0; i < extra; i++)
        {
            var next = TakeNearest(from, chain ? CovenantMath.ArcflashChainHop : radius);
            if (next == null) break;
            Shock(next, inst);
            if (chain) from = next.transform.position;   // 체인만 발판을 옮긴다
        }
        _areaScratch.Clear();
        return true;
    }

    private static void Shock(MonsterBase mb, GameObject instigator)
        => CovenantStatus.Amplify(mb.gameObject, StatusCurrency.Shock, 0f, 0f, instigator);

    /// <summary>
    /// 정지 — 반경 안에 쌓인 감전을 전부 걷어 그 스택 수만큼 오래 멈춰 세운다.
    /// 걷기와 걸기를 두 바퀴로 나눈 이유: 한 바퀴에서 걷고 바로 기절시키면 그 기절이 다른 서약의 원인을
    /// 물고 돌아와 순회 중인 목록을 흔든다. 먼저 다 걷고, 총량이 정해진 뒤에 건다.
    /// 보스는 지속을 깎는다 — 감전만 쌓아 두면 페이즈가 통째로 건너뛰어진다.
    /// </summary>
    private bool Stasis(GameObject target)
    {
        if (Ctx?.Player == null) return false;

        var    origin = Live(ResolveTarget(target));
        Vector3 center = origin != null ? origin.transform.position : PlayerPos;
        float   radius = CovenantMath.EffectiveRadius(_effect);

        CollectLiveEnemies(center, radius, null);
        if (_areaScratch.Count == 0) return false;

        int stacks = 0;
        for (int i = 0; i < _areaScratch.Count; i++)
        {
            var mb = _areaScratch[i];
            if (mb != null && !mb.IsDead)
                stacks += Mathf.RoundToInt(
                    CovenantStatus.Consume(mb.gameObject, StatusCurrency.Shock, 1f, Ctx.Player.gameObject, false));
        }
        if (stacks <= 0) { _areaScratch.Clear(); return false; }

        float duration = Mathf.Min(Eff * stacks, CovenantMath.StasisStunCap);
        GuidelineVisual.AoeBurst(center, radius, GuidelineVisual.ToastKind.Covenant);

        for (int i = 0; i < _areaScratch.Count; i++)
        {
            var mb = _areaScratch[i];
            if (mb == null || mb.IsDead) continue;
            mb.ApplyStun(mb.Grade == MonsterGrade.Boss ? duration * CovenantMath.StasisBossMult : duration);
        }
        _areaScratch.Clear();
        return true;
    }

    // ── 결계(비-흡혈 방어) ────────────────────────────────
    /// <summary>
    /// 결계 — 잠시 받는 피해가 줄어든다. 주변에 상태가 걸린 적이 많을수록 두꺼워지되,
    /// <b>적에게서 아무것도 가져오지 않는다</b>(상태를 읽기만 하고 걷어가지 않는다).
    /// 그래서 소모형이 아니고, 통화를 걸어 줄 서약이 없어도 기본 두께만큼은 혼자 선다 —
    /// 드래프트의 "생존 카드 한 장 보장"을 조건부가 아닌 카드로 채울 수 있는 이유다.
    /// </summary>
    private bool Ward()
    {
        if (Ctx?.Player == null) return false;

        int steeped = CountSteeped(PlayerPos, CovenantMath.EffectiveRadius(_effect));
        _wardReduction = Mathf.Min(Eff + CovenantMath.WardPerSteepedEnemy * steeped,
                                   CovenantMath.WardReductionCap);
        _wardEnd = Time.time + _effect.duration;
        return true;
    }

    /// <summary>「초신성」 반경(B7) — 반경 안에 절여진 적 1체당 넓어진다. 상태를 깔아 둔 판일수록 폭발이 크다.</summary>
    private float SupernovaRadius(Vector3 center)
    {
        float baseRadius = CovenantMath.EffectiveRadius(_effect);
        return Mathf.Min(
            baseRadius + CovenantMath.SupernovaRadiusPerSteeped * CountSteeped(center, baseRadius),
            CovenantMath.AoeRadiusCap);
    }

    /// <summary>반경 내에서 상태 통화가 하나라도 걸린 적의 수.</summary>
    private int CountSteeped(Vector3 center, float radius)
    {
        CollectLiveEnemies(center, radius, null);
        int n = 0;
        for (int i = 0; i < _areaScratch.Count; i++)
            if (CovenantStatus.HasAny(_areaScratch[i])) n++;
        _areaScratch.Clear();
        return n;
    }

    /// <summary>_areaScratch에서 from에 가장 가까운 대상을 <b>꺼내</b> 반환(maxDist 초과면 null). 같은 적을 두 번 잡지 않는다.</summary>
    private MonsterBase TakeNearest(Vector3 from, float maxDist)
    {
        int best = -1;
        float bestSq = maxDist * maxDist;
        for (int i = 0; i < _areaScratch.Count; i++)
        {
            var mb = _areaScratch[i];
            if (mb == null || mb.IsDead) continue;
            float sq = (mb.transform.position - from).sqrMagnitude;
            if (sq <= bestSq) { bestSq = sq; best = i; }
        }
        if (best < 0) return null;

        var picked = _areaScratch[best];
        _areaScratch.RemoveAt(best);
        return picked;
    }

    // ── 소모형(기폭·수확·정지) ────────────────────────────
    // 셋 다 상태를 <b>부여하지 않는다</b>. 먹을 게 없으면 false를 돌려 쿨다운도 연출도 소모하지 않는다 —
    // "터뜨릴 게 없는데 쿨만 돈다"가 되면 걸기-터뜨리기의 순서가 플레이어에게 보이지 않는다.

    /// <summary>
    /// 기폭 — 원인 형상 분기(①). 가리킬 대상이 있는 원인은 그 적 하나를 터뜨리고 주변에 파편을 흩는다.
    /// 대상이 없는 원인(주기·클리어·포위 …)은 애초에 "이 적"이 없으므로 주변에 걸린 상태를 통째로 분산 기폭한다.
    /// </summary>
    private bool Detonate(GameObject target)
    {
        if (Ctx?.Player == null) return false;

        if (_cause.targeted)
        {
            var mb = Live(ResolveTarget(target));
            return mb != null && DetonateOne(mb, spread: true);
        }
        return DetonateArea();
    }

    /// <summary>한 대상의 화상·출혈을 소모해 즉시 피해로 바꾼다. spread=true면 회수한 가치에 비례해 주변에 파편.</summary>
    private bool DetonateOne(MonsterBase mb, bool spread)
    {
        float total = ConsumeDots(mb.gameObject, CovenantMath.DetonateFraction, asDamage: true);
        if (total <= 0f) return false;

        if (spread) SpreadBurst(mb, total * Eff);
        return true;
    }

    /// <summary>발동 지점 주변의 모든 적을 각자 기폭(분산). 파편은 없다 — 이미 광역이다.</summary>
    private bool DetonateArea()
    {
        float radius = CovenantMath.EffectiveRadius(_effect);
        CollectLiveEnemies(PlayerPos, radius, null);
        if (_areaScratch.Count == 0) return false;

        GuidelineVisual.AoeBurst(PlayerPos, radius, GuidelineVisual.ToastKind.Covenant);

        bool any = false;
        for (int i = 0; i < _areaScratch.Count; i++)
        {
            var mb = _areaScratch[i];
            if (mb != null && !mb.IsDead) any |= DetonateOne(mb, spread: false);
        }
        _areaScratch.Clear();
        return any;
    }

    /// <summary>터뜨린 가치의 일부를 주변 적에게 파편 피해로 흩는다(기폭 원점 제외).</summary>
    private void SpreadBurst(MonsterBase origin, float amount)
    {
        if (amount <= 0f) return;

        float radius = CovenantMath.EffectiveRadius(_effect);
        Vector3 center = origin.transform.position;
        CollectLiveEnemies(center, radius, origin);
        if (_areaScratch.Count == 0) return;

        GuidelineVisual.AoeBurst(center, radius, GuidelineVisual.ToastKind.Covenant);
        for (int i = 0; i < _areaScratch.Count; i++)
            _areaScratch[i]?.TakeSynergyDamage(amount, Ctx.Player.gameObject, 1f, false, DamageKind.Synergy);
        _areaScratch.Clear();
    }

    /// <summary>
    /// 수확 — 걸린 화상·출혈을 걷어 스킬 재촉(쿨감)과 금으로 바꾼다.
    /// 체력에는 손대지 않는다 — 걷은 것이 회복으로 돌아오면 상태를 거두는 서약이 곧 흡혈이 된다.
    /// </summary>
    private bool Harvest(GameObject target)
    {
        var mb = Live(target);
        if (mb == null) return false;

        float value = ConsumeDots(mb.gameObject, CovenantMath.HarvestFraction, asDamage: false);
        if (value <= 0f) return false;

        Ctx.Player?.CooldownTracker?.ReduceAllCooldowns(Eff);
        Ctx.Session?.AddGold(CovenantMath.HarvestGold);
        return true;
    }

    /// <summary>화상+출혈 잔량을 fraction만큼 걷고 그 가치를 합산해 반환. asDamage=true면 걷은 만큼 즉시 피해.</summary>
    private float ConsumeDots(GameObject go, float fraction, bool asDamage)
    {
        var inst = Ctx?.Player != null ? Ctx.Player.gameObject : null;
        return CovenantStatus.Consume(go, StatusCurrency.Burn,  fraction, inst, asDamage)
             + CovenantStatus.Consume(go, StatusCurrency.Bleed, fraction, inst, asDamage);
    }

    /// <summary>
    /// 처형 — 상태에 절여진 저체력 대상을 즉사시킨다.
    ///
    /// 임계는 두 축으로 커진다:
    ///  • B4 — 대상에게 걸린 상태 통화 <b>종수</b> 1종당 ×1.5. 처형은 "약한 적"이 아니라 "절여진 적"을 벤다.
    ///  • ⑤ — 경계 원인(개선·선제)이면 ×2. 대신 '지금 때린 적'이 아니라 방에서 가장 약한 적을 짚는다(표식).
    /// 배수는 <see cref="CovenantMath.ExecuteThresholdCap"/>로 가둔다 — 임계가 1을 넘으면 체력과 무관한 즉사가 된다.
    ///
    /// 보스는 즉사시키지 않는다(페이즈·연출이 통째로 건너뛰어진다). 베어내는 양은 <b>기본 임계</b>로 계산한다 —
    /// 통화 배수까지 얹으면 한 번의 발동이 최대 체력의 절반을 넘게 깎는다.
    /// </summary>
    private bool TryExecute(GameObject target)
    {
        if (Ctx?.Player == null) return false;

        bool boundary = _cause.cls == CauseClass.Boundary;
        var  mb = boundary ? LowestHpEnemy(PlayerPos, ExecuteMarkRadius) : Live(ResolveTarget(target));
        if (mb == null) return false;

        int max = mb.EffectiveMaxHp;
        if (max <= 0) return false;

        float baseT    = Eff;
        float threshold = Mathf.Min(
            baseT * Mathf.Pow(CovenantMath.ExecuteStatusMult, CovenantStatus.CountKinds(mb.gameObject))
                  * (boundary ? CovenantMath.ExecuteBoundaryMult : 1f),
            CovenantMath.ExecuteThresholdCap);

        if ((float)mb.CurrentHp / max > threshold) return false;

        // 전염이 먼저다 — 대상을 먼저 죽이면 화상 핸들러가 함께 사라져 옮길 불이 남지 않는다.
        if (mb.Grade != MonsterGrade.Boss) SpreadBurnFrom(mb);

        if (mb.Grade == MonsterGrade.Boss)
            mb.TakeSynergyDamage(max * baseT, Ctx.Player.gameObject, 1f, false, DamageKind.Synergy);
        else
            mb.TakeDamage(mb.CurrentHp * 10f, Ctx.Player.gameObject, 0.3f);
        return true;
    }

    /// <summary>처형 성공 시 화상을 가장 가까운 다른 적에게 옮긴다(B4). 화상이 없으면 무동작.</summary>
    private void SpreadBurnFrom(MonsterBase from)
    {
        var next = NearestLiveEnemy(from.transform.position, TargetSearchRadius, from);
        if (next != null)
            MonsterBurnHandler.SpreadTo(from.gameObject, next, Ctx.Player.gameObject);
    }

    /// <summary>반경 내에서 체력 비율이 가장 낮은 살아있는 적(표식 대상).</summary>
    private MonsterBase LowestHpEnemy(Vector3 center, float radius)
    {
        CollectLiveEnemies(center, radius, null);
        MonsterBase best = null;
        float bestRatio = float.MaxValue;
        for (int i = 0; i < _areaScratch.Count; i++)
        {
            var mb = _areaScratch[i];
            int max = mb != null ? mb.EffectiveMaxHp : 0;
            if (max <= 0) continue;
            float ratio = (float)mb.CurrentHp / max;
            if (ratio < bestRatio) { bestRatio = ratio; best = mb; }
        }
        _areaScratch.Clear();
        return best;
    }

    /// <summary>
    /// 반경 내 살아있는 적을 <see cref="_areaScratch"/>에 모은다(except 제외).
    /// 피해를 넣기 <b>전에</b> 훑기를 끝내는 게 요점이다 — <see cref="_probe"/>는 서약 인스턴스끼리 공유하는
    /// 정적 버퍼라, 피해가 적을 죽여 다른 서약의 원인이 발동하면 순회 도중에 통째로 덮어써진다.
    /// </summary>
    private void CollectLiveEnemies(Vector3 center, float radius, MonsterBase except)
    {
        _areaScratch.Clear();
        int n = Physics.OverlapSphereNonAlloc(center, radius, _probe);
        for (int i = 0; i < n; i++)
        {
            var col = _probe[i];
            if (col == null) continue;
            if (!col.TryGetComponent<MonsterBase>(out var mb) || mb.IsDead || mb == except) continue;
            _areaScratch.Add(mb);
        }
    }

    /// <summary>살아있는 몬스터면 그 MonsterBase, 아니면 null.</summary>
    private static MonsterBase Live(GameObject go)
    {
        if (go == null) return null;
        var mb = go.GetComponentInParent<MonsterBase>();
        return mb != null && !mb.IsDead ? mb : null;
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

    private GameObject NearestLiveEnemy(Vector3 center, float radius, MonsterBase except = null)
    {
        int n = Physics.OverlapSphereNonAlloc(center, radius, _probe);
        GameObject best = null; float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var col = _probe[i];
            if (col == null) continue;
            if (!col.TryGetComponent<MonsterBase>(out var mb) || mb.IsDead || mb == except) continue;
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
