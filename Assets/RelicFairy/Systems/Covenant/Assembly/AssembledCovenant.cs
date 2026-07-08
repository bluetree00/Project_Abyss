using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 조립 서약 — 원인(Cause) × 효과(Effect) 데이터구동 단일 클래스(조합마다 클래스 X).
/// CovenantId = "asm:&lt;causeId&gt;@&lt;tier&gt;|&lt;effectId&gt;@&lt;tier&gt;" 로 인코딩 → 기존 CovenantFactory/Handler/세이브 그대로 재사용.
/// (@tier 생략 시 실버로 폴백 — 구버전 id 호환)
/// 원인 트리거 override에서 조건 판정 → ApplyEffect(EffectKind별 CovenantBase 헬퍼 호출).
/// 효과 크기 = magnitude(효과 티어 배율) × 원인 계수(원인 티어 배율). 티어 = 순수 파워 축.
/// </summary>
public sealed class AssembledCovenant : CovenantBase
{
    public const string Prefix = "asm:";

    // 근접 판정 상수(포위·최근접 대상 탐색)
    private const float ProximityRadius        = 5f;
    private const float ProximityCheckInterval = 0.5f;
    private const float TargetSearchRadius     = 8f;

    private static readonly Collider[] _probe = new Collider[32];

    private readonly string _causeId, _effectId;
    private readonly CovenantTier _causeTier, _effectTier;
    private readonly bool   _resolved;
    private readonly CovenantPalette.CauseDef  _cause;
    private readonly CovenantPalette.EffectDef _effect;

    // 티어 반영 유효 수치
    private float Coef => _cause.coefficient  * _causeTier.CoefficientMultiplier();
    private float Mag  => _effect.magnitude   * _effectTier.MagnitudeMultiplier();

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

    /// <param name="causePart">"causeId" 또는 "causeId@tier"</param>
    /// <param name="effectPart">"effectId" 또는 "effectId@tier"</param>
    public AssembledCovenant(string causePart, string effectPart)
    {
        (_causeId,  _causeTier)  = SplitTier(causePart);
        (_effectId, _effectTier) = SplitTier(effectPart);
        bool a = CovenantPalette.TryGetCause(_causeId, out _cause);
        bool b = CovenantPalette.TryGetEffect(_effectId, out _effect);
        _resolved = a && b;
    }

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

    // ── 원인 트리거 → ApplyEffect ─────────────────────────
    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (!_resolved) return;
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
        if (!_resolved) return;
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
        if (_resolved && _cause.trigger == CauseTriggerKind.OnWeaponSwapWindow)
            _swapWindowEnd = Time.time + _cause.thresholdF;
    }

    public override void OnRoomEnter()
    {
        if (_resolved && _cause.trigger == CauseTriggerKind.OnFirstHitInRoom) _firstHitArmed = true;
    }

    public override void OnRoomClear()
    {
        if (_resolved && _cause.trigger == CauseTriggerKind.OnRoomClear) ApplyEffect(null);
    }

    public override void OnSkillUse(SkillType skill)
    {
        if (_resolved && _cause.trigger == CauseTriggerKind.OnSkillUse) ApplyEffect(null);
    }

    public override void Tick(float deltaTime)
    {
        if (!_resolved) return;
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
    }

    // 격노: 지속 중 나가는 피해 증폭
    public override void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        if (_resolved && _buffActive && _effect.kind == EffectKind.DamageBuff)
            damage *= 1f + Mag * Coef;
    }

    private void ApplyEffect(GameObject target)
    {
        if (Ctx == null) return;
        switch (_effect.kind)
        {
            case EffectKind.AoeBurst:
            {
                Vector3 pos = target != null ? target.transform.position : PlayerPos;
                DealAoe(pos, _effect.radius, Mag * Coef);
                break;
            }
            case EffectKind.DamageBuff:
                _buffActive = true; _buffEnd = Time.time + _effect.duration;
                break;
            case EffectKind.Lifesteal:
                Ctx.Player?.Heal(Mathf.RoundToInt(Mag * Coef));
                break;
            case EffectKind.GoldBurst:
                Ctx.Session?.AddGold(Mathf.RoundToInt(Mag * Coef));
                break;
            case EffectKind.Curse:
                ApplyCurse(ResolveTarget(target));
                break;
            case EffectKind.Execute:
                TryExecute(ResolveTarget(target));
                break;
        }
    }

    // ── 효과 헬퍼 ─────────────────────────────────────────
    private void ApplyCurse(GameObject target)
    {
        if (target == null) return;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb != null) mb.ApplyDamageTakenAmp(Mag * Coef, _effect.duration, "cov_curse");
    }

    private void TryExecute(GameObject target)
    {
        if (target == null || Ctx?.Player == null) return;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;
        int max = mb.EffectiveMaxHp;
        if (max <= 0) return;
        if ((float)mb.CurrentHp / max <= Mag)   // Mag = 티어 반영 처형 임계(체력 비율)
            mb.TakeDamage(mb.CurrentHp * 10f, Ctx.Player.gameObject, 0.3f);
    }

    /// <summary>
    /// 저주·처형 대상 해석 — 대상이 살아있으면 그대로, 죽었거나(학살=시체) 없으면(주기·클리어 등)
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
