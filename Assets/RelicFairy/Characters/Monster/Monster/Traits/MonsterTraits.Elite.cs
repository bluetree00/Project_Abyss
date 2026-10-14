using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using RelicFairy.Monster;

/// <summary>
/// 정예 접두어 행동(10-11 — 기획/RelicFairy_일반방긴장감·엘리트몬스터_진단·설계_20261011.md §3-4 · §7-2) — 1차 6종.
/// 강철(정면 막음) · 돌격(직선 돌진) · 소환(의식 → 졸개) · 박격(원 셋) · 가시(세 번 맞으면 폭발) · 복수(아군이 죽을수록 셈).
/// 공통: 예고 ≥ 0.45초 · 늘 켜진 오라 없음(행동하는 순간만 바닥 원 · 줄) · 회복 · 흡수 · 반사 없음 · 악몽은 쿨 −20%.
/// 79 포이즈(<see cref="MonsterPoise"/>, 정예만)와 잇는 곳 — 강철 최대 포이즈 2배 · 깨진 동안(휘청)은 정면 막음 풀림 · 소환 의식 중 포이즈 0(맞으면 바로 끊김).
/// </summary>
public sealed partial class MonsterTraits
{
    // ── Constants ──────────────────────────────────────────────
    private const float NightmareCooldownMul = 0.8f;
    private const int   EliteOreReward       = 1;      // 접두어 정예 처치마다 원석(RuneOre)
    private const string ExposedStatus       = "vulnerable";
    // 강철
    private const float IroncladFrontDot = 0.5f;       // cos 60° — 정면 120°
    private const float IroncladCut      = 0.4f;       // 정면 피해 −60%
    private const float IroncladMoveMul  = 0.85f;
    private const float IroncladFxGap    = 0.2f;       // 막힘 불꽃 최소 간격(연타에 불꽃이 겹치지 않게)
    private const float IroncladFxScale  = 0.18f;
    private const float IroncladPoiseScale = 2f;    // 최대 포이즈 배율(설계서 §3-4 「포이즈 2배」)
    // 돌격
    private const float ChargeEvery      = 6f;
    private const float ChargeFirst      = 2.5f;       // 나오자마자 달려들지 않게
    private const float ChargeMinDist    = 3f;
    private const float ChargeMaxDist    = 10f;
    private const float ChargeLength     = 9f;
    private const float ChargeWarn       = 0.7f;
    private const float ChargeLockAt     = 0.25f;      // 이때 방향을 굳힌다 → 돌진까지 0.45초
    private const float ChargeWidth      = 1.5f;
    private const float ChargeSpeed      = 26f;
    private const float ChargeHitPad     = 0.3f;       // 플레이어 몸 반경 몫
    private const float ChargeDmgMul     = 1.5f;
    private const float ChargeRecover    = 1.2f;
    private const float ChargeExposedAmp = 0.3f;       // 멈춘 동안 받는 피해 +30%(ApplyDamageTakenAmp 0.2 = ×1.2)
    private const float ChargeTurnSpeed  = 720f;
    private const float ChargeAnimSpeed  = 2f;         // 26 m/s 돌진 — 평소 달리기 그대로면 미끄러졌다(10-14 정예 애니 점검)
    // 소환
    private const float SummonEvery      = 8f;
    private const float SummonFirst      = 3f;
    private const float SummonRitual     = 1.2f;
    private const int   SummonPerCast    = 2;
    private const int   SummonMaxAlive   = 4;
    private const int   SummonLifeCap    = 6;          // 한 소환사가 평생 부르는 졸개 — 소환사를 살려 두고 졸개만 잡아 모으지 못하게
    private const float SummonRange      = 14f;
    private const float SummonCircle     = 1.2f;
    private const float SummonSpread     = 1.6f;
    // 박격
    private const float MortarEvery      = 5f;
    private const float MortarFirst      = 2f;
    private const float MortarMinDist    = 3f;
    private const float MortarMaxDist    = 18f;
    private const float MortarWarn       = 1.0f;
    private const float MortarRadius     = 1.5f;
    private const float MortarSpread     = 2.5f;       // 플레이어 이동 방향 앞 · 뒤
    private const float MortarDmgMul     = 1.0f;
    private const float MortarFxScale    = 1.65f;      // 폭발이 판정 원(1.5 m)을 덮게 — 0.3은 0.2 m였다(10-13 RunFx 반경 실측)
    // 가시
    private const int   ThornsMax        = 3;
    private const float ThornsStackLife  = 3f;
    private const float ThornsWarn       = 0.6f;
    private const float ThornsRadius     = 2.5f;
    private const float ThornsDmgMul     = 1.2f;
    private const float ThornsCooldown   = 3f;         // 터진 뒤 가시가 안 쌓이는 시간 — 빠른 무기에 1초마다 터지지 않게
    private const float ThornsFxScale    = 2.75f;      // 폭발이 판정 원(2.5 m)을 덮게 — 0.5는 0.4 m였다(10-13 실측)
    // 복수
    private const float AvengeRadius     = 10f;
    private const float AvengeWatch      = 12f;
    private const float AvengeScanGap    = 0.5f;
    private const int   AvengeMax        = 3;
    private const float AvengeAtkStep    = 0.15f;
    private const float AvengeMoveStep   = 0.10f;

    private static readonly Color IroncladSpark = new(0.78f, 0.84f, 0.92f, 1f);
    private static readonly Color ThornsColor   = new(1f, 0.38f, 0.32f, 1f);
    private static readonly Color MortarBurst   = new(1f, 0.6f, 0.3f, 1f);

    // ── Private ────────────────────────────────────────────────
    private bool  _elite;
    private float _nextAbilityAt;
    private float _ironFxAt;
    private Func<Vector3, UniTask<MonsterBase>> _summon;
    private readonly List<MonsterBase> _minions = new(SummonMaxAlive);
    private int   _summonedTotal;
    private bool  _isMinion;
    private int   _thorns;
    private float _thornsExpireAt;
    private float _thornsReadyAt;
    private int   _avenge;
    private float _avengeScanAt;
    private readonly HashSet<MonsterBase> _watched = new();
    private Vector3 _playerLast;
    private Vector3 _playerVel;
    private EliteChargeState _chargeState;
    private EliteSummonState _summonState;

    // ── Properties ─────────────────────────────────────────────
    public bool HasElitePrefix => _elite && !_reverted;

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>소환 졸개 — 골드를 떨구지 않는다(<see cref="DieState"/>). 소환사가 죽으면 같이 쓰러진다.</summary>
    public static bool IsDropless(MonsterBase m)
        => m != null && m.TryGetComponent<MonsterTraits>(out var t) && t._isMinion && !t._reverted;

    /// <summary>머리 위 이름 앞에 접두어(영문 · 분류색)를 붙인다 — <see cref="MonsterBase.DisplayName"/>이 부른다.</summary>
    public string DecorateName(string baseName)
    {
        if (_reverted) return baseName;
        foreach (var k in _kinds)
        {
            string label = MonsterTraitTable.PrefixLabel(k);
            if (label != null) return $"{label} {baseName}";
        }
        return baseName;
    }

    // ── Private Methods ────────────────────────────────────────

    private static void MakeMinion(MonsterBase child)
    {
        if (child == null) return;
        RemoveStale(child);
        var t = child.gameObject.AddComponent<MonsterTraits>();
        t._monster  = child;
        t._isMinion = true;
    }

    private void InitElite()
    {
        _elite = false;
        foreach (var k in _kinds)
            if (MonsterTraitTable.Of(k).elitePrefix) { _elite = true; break; }
        if (!_elite) return;

        float first = Has(MonsterTraitKind.Charger) ? ChargeFirst : Has(MonsterTraitKind.Summoner) ? SummonFirst : MortarFirst;
        _nextAbilityAt = Time.time + first;
        _avengeScanAt  = Time.time;
        _playerLast    = PlayerPos();
        _monster.OnDied += HandleDiedElite;
        if (Has(MonsterTraitKind.Ironclad) && _monster.Poise != null) _monster.Poise.MaxScale = IroncladPoiseScale;
    }

    private void RevertElite()
    {
        if (_monster != null)
        {
            _monster.OnDied -= HandleDiedElite;
            if (_avenge > 0) _monster.SetTraitAttackMul(1f);
            // 풀에 돌아가면 OnEnable이 포이즈를 되돌리지만, 같은 몸에 특성을 다시 붙일 때(RemoveStale)는 여기서 되돌린다. Revert 중이라 Has()가 아닌 목록으로 본다
            if (_monster.Poise != null && _kinds.Contains(MonsterTraitKind.Ironclad)) _monster.Poise.MaxScale = 1f;
        }
        foreach (var m in _watched) if (m != null) m.OnDied -= HandleAllyDied;
        _watched.Clear();
        _minions.Clear();
        _avenge  = 0;
        _thorns  = 0;
        _elite   = false;
        _isMinion = false;
    }

    private void TickElite()
    {
        if (!_elite) return;
        TrackPlayer();
        if (_thorns > 0 && Time.time >= _thornsExpireAt) { _thorns = 0; RebuildBadges(); PushBadges(); }
        if (Has(MonsterTraitKind.Avenger) && Time.time >= _avengeScanAt) ScanAllies();
        if (Time.time < _nextAbilityAt || _monster.IsCcActive) return;

        if      (Has(MonsterTraitKind.Charger))  TryStartCharge();
        else if (Has(MonsterTraitKind.Summoner)) TryStartSummon();
        else if (Has(MonsterTraitKind.Mortar))   TryFireMortar();
    }

    /// <summary>받는 피해 — 강철(정면 −60%) · 가시(직접 타격마다 1). 직접 타격 = 플레이어 무기 · 투사체(<see cref="MonsterBase.TakeDamage"/>).</summary>
    private float ModifyIncomingElite(float actual, GameObject instigator, bool direct)
    {
        if (!_elite || !direct) return actual;
        if (instigator != null && Has(MonsterTraitKind.Ironclad))
        {
            Vector3 to = instigator.transform.position - _monster.transform.position;
            to.y = 0f;
            bool broken = _monster.Poise != null && _monster.Poise.IsBroken;   // 포이즈를 깨면 휘청 1.5초 동안 막음이 풀린다
            if (!broken && to.sqrMagnitude > 0.0001f && Vector3.Dot(_monster.transform.forward, to.normalized) >= IroncladFrontDot)
            {
                actual *= IroncladCut;
                IroncladBlockFx(to.normalized);
            }
        }
        if (Has(MonsterTraitKind.Thorns)) AddThorn();
        return actual;
    }

    private float EliteSpeedMul()
        => (Has(MonsterTraitKind.Ironclad) ? IroncladMoveMul : 1f) * (1f + AvengeMoveStep * _avenge);

    private Color EliteRing(MonsterTraitKind k, Color ring)
        => k == MonsterTraitKind.Avenger ? Color.Lerp(ring * 0.55f, ring, (float)_avenge / AvengeMax) : ring;

    // 가시 수 — 배지 옆에 하나씩(1~3)
    private void AppendEliteBadges()
    {
        for (int i = 0; i < _thorns; i++) _badges.Add(new MonsterTraitBadge("bleed", ThornsColor));
    }

    private static float Cooldown(float seconds)
        => StoryProgress.Era == StoryEra.NightmareMode ? seconds * NightmareCooldownMul : seconds;

    private int AttackDamage(float mul)
        => Mathf.Max(1, Mathf.RoundToInt(_monster.EffectiveAttackPower * _monster.AttackMultiplierNow * mul));

    private static Vector3 PlayerPos()
    {
        var pt = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        return pt != null ? pt.position : Vector3.zero;
    }

    private static Transform PlayerTransform() => Managers.Player != null ? Managers.Player.PlayerTransform : null;

    private static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    // 추적(또는 공격 준비) 중일 때만 행동을 시작한다 — 공격 · 경직 · 특수 상태를 끊지 않게. 등록 키로 묻는다(패턴 몬스터의 오버라이드 상태도 맞게 — 79 IsInState)
    private bool InStates(bool allowAttackReady)
    {
        if (_monster.IsInSpecialState) return false;
        return _monster.IsInState<ChaseState>() || (allowAttackReady && _monster.IsInState<AttackReadyState>());
    }

    // 거리 기술(박격 · 소환 의식)은 휘두르는 중 · 경직 중 · 특수 상태만 아니면 — 추적 · 공격 준비만 보면 순찰로 돌아간 정예가 한 번도 못 했다(10-12 실측)
    private bool NotBusy()
        => !_monster.IsInSpecialState && !_monster.IsInState<AttackState>() && !_monster.IsInState<GetHitState>();

    private void TrackPlayer()
    {
        Vector3 p = PlayerPos();
        float dt = Time.deltaTime;
        if (dt > 0f) _playerVel = Vector3.Lerp(_playerVel, (p - _playerLast) / dt, 0.3f);
        _playerLast = p;
    }

    // ── 강철 ───────────────────────────────────────────────
    private void IroncladBlockFx(Vector3 toAttacker)
    {
        if (Time.time < _ironFxAt) return;
        _ironFxAt = Time.time + IroncladFxGap;
        Vector3 at = _monster.transform.position + toAttacker * 0.7f + Vector3.up * 1f;
        RunFx.Play(RunFxSlot.Burst, at, IroncladFxScale, IroncladSpark);   // 작은 한 번짜리 불꽃 — 정면에 막혔다
        Debug.Log($"[ElitePrefix] 강철 정면 막음 t={Time.time:F2}");
    }

    // ── 돌격 ───────────────────────────────────────────────
    private void TryStartCharge()
    {
        Transform pt = PlayerTransform();
        if (pt == null || !InStates(allowAttackReady: false)) return;
        float d = FlatDist(pt.position, _monster.transform.position);
        if (d < ChargeMinDist || d > ChargeMaxDist) return;
        _nextAbilityAt = Time.time + Cooldown(ChargeEvery);
        _chargeState ??= new EliteChargeState(this);
        _monster.ChangeState(_chargeState);
    }

    /// <summary>돌진 — 0.25초 따라 돌다가 방향을 굳히고 줄 예고(공용 예고 · 0.45초 뒤 돌진) → 직선 9 m(벽에서 자름 = 예고 길이) → 1.2초 멈춤(받는 피해 +30%). 시작하면 끊기지 않는다.</summary>
    private sealed class EliteChargeState : SpecialStateBase
    {
        private enum Phase { Warn, Dash, Recover }

        private readonly MonsterTraits _owner;
        private Phase      _phase;
        private float      _t;
        private Vector3    _dir;
        private float      _len;
        private float      _moved;
        private bool       _locked;
        private bool       _hit;
        private MonsterTelegraph _tele;

        public EliteChargeState(MonsterTraits owner) { _owner = owner; }

        public override SpecialStateConstraint Constraints =>
            SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;

        public override void Enter(MonsterContext ctx)
        {
            _phase = Phase.Warn; _t = 0f; _moved = 0f; _locked = false; _hit = false;
            StopAgent(ctx);
            Aim(ctx);
            PlayState(ctx, ctx.Animation.attackReadyStateName);
            Debug.Log($"[ElitePrefix] 돌격 예고 시작 t={Time.time:F2} 길이 {_len:F1} m — {ctx.Monster.name}");
        }

        public override void Update(MonsterContext ctx)
        {
            _t += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Warn:
                    if (!_locked)
                    {
                        Aim(ctx);
                        ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation,
                            Quaternion.LookRotation(_dir), ChargeTurnSpeed * Time.deltaTime);
                        if (_t >= ChargeLockAt)
                        {
                            _locked = true;
                            ctx.Transform.rotation = Quaternion.LookRotation(_dir);
                            _tele = MonsterTelegraph.Line(ctx.Transform.position, _dir, _len + ChargeWidth * 0.5f, ChargeWidth, ChargeWarn - _t,   // 끝에서도 몸 반폭만큼 닿는다
                                                          MonsterTelegraph.Heavy, face: true);
                            Debug.Log($"[ElitePrefix] 돌격 방향 굳힘 t={Time.time:F2}(돌진까지 {ChargeWarn - ChargeLockAt:F2}초)");
                        }
                    }
                    if (_t >= ChargeWarn)
                    {
                        _phase = Phase.Dash; _t = 0f;
                        PlayState(ctx, ctx.Animation.chaseStateName);
                        if (ctx.Animator != null) ctx.Animator.speed = ChargeAnimSpeed;
                        Debug.Log($"[ElitePrefix] 돌격 판정 시작 t={Time.time:F2}");
                    }
                    break;

                case Phase.Dash:
                    float step = Mathf.Min(ChargeSpeed * Time.deltaTime, _len - _moved);
                    Vector3 before = ctx.Transform.position;
                    if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.Move(_dir * step);
                    float moved = FlatDist(ctx.Transform.position, before);
                    _moved += moved;
                    TryHit(ctx);
                    if (_moved >= _len - 0.05f || moved < step * 0.3f) BeginRecover(ctx);
                    break;

                case Phase.Recover:
                    if (_t >= ChargeRecover) ctx.Monster.ChangeState<ChaseState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_tele != null) _tele.Cancel();   // 돌진 전에 끊기면 헛예고를 지운다
            _tele = null;
            if (ctx.Animator != null) ctx.Animator.speed = 1f;
            ResumeAgent(ctx);
        }

        private void Aim(MonsterContext ctx)
        {
            Vector3 from = ctx.Transform.position;
            Vector3 to   = PlayerPos() - from;
            to.y = 0f;
            _dir = to.sqrMagnitude > 0.01f ? to.normalized : ctx.Transform.forward;
            _len = ChargeLength;
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            {
                var filter = new NavMeshQueryFilter { agentTypeID = ctx.Agent.agentTypeID, areaMask = ctx.Agent.areaMask };
                if (NavMesh.Raycast(from, from + _dir * ChargeLength, out var hit, filter))
                    _len = Mathf.Max(1f, hit.distance - 0.3f);   // 벽 · 장애물에서 자른다 — 예고 줄 = 실제 돌진 길이
            }
        }

        private void BeginRecover(MonsterContext ctx)
        {
            _phase = Phase.Recover; _t = 0f;
            if (ctx.Animator != null) ctx.Animator.speed = 1f;
            int dizzy = MonsterAnimStates.Of(ctx).Dizzy;   // 멈춘 틈 = 취약 — 어지러워하는 동작으로 읽히게(예전엔 평상 대기 · 10-14 정예 애니 점검)
            if (dizzy != 0 && ctx.Animator != null) ctx.Animator.CrossFade(dizzy, 0.1f);
            else PlayState(ctx, ctx.Animation.idleStateName);
            ctx.Monster.ApplyDamageTakenAmp(ChargeExposedAmp, ChargeRecover, ExposedStatus);   // 멈춘 틈 — 체력바 아래 「취약」 아이콘
        }

        private void TryHit(MonsterContext ctx)
        {
            if (_hit) return;
            Transform pt = PlayerTransform();
            if (pt == null || FlatDist(pt.position, ctx.Transform.position) > ChargeWidth * 0.5f + ChargeHitPad) return;
            if (!pt.TryGetComponent<PlayerController>(out var pc)) return;
            _hit = true;
            pc.TakeDamage(_owner.AttackDamage(ChargeDmgMul), ctx.Monster.gameObject, false, HitWeight.Heavy);
            Debug.Log($"[ElitePrefix] 돌격 명중 t={Time.time:F2}");
        }
    }

    // ── 소환 ───────────────────────────────────────────────
    private void TryStartSummon()
    {
        if (_summon == null || _summonedTotal >= SummonLifeCap) { _nextAbilityAt = float.MaxValue; return; }
        _minions.RemoveAll(m => m == null || m.IsDead || !m.isActiveAndEnabled);
        if (_minions.Count + SummonPerCast > SummonMaxAlive || !MonsterBudget.CanSpawn) { _nextAbilityAt = Time.time + 1f; return; }
        Transform pt = PlayerTransform();
        if (pt == null || !NotBusy()) return;
        if (FlatDist(pt.position, _monster.transform.position) > SummonRange) return;
        _nextAbilityAt = Time.time + Cooldown(SummonEvery);
        _summonState ??= new EliteSummonState(this);
        _monster.ChangeState(_summonState);
    }

    private void SummonNow()
    {
        Vector3 c = _monster.transform.position, side = _monster.transform.right * SummonSpread;
        _summonedTotal += SummonPerCast;
        for (int i = 0; i < SummonPerCast; i++)
            SummonOneAsync(i == 0 ? c + side : c - side).Forget();
        Debug.Log($"[ElitePrefix] 소환 완료 t={Time.time:F2} — 졸개 {SummonPerCast}(누적 {_summonedTotal}/{SummonLifeCap})");
    }

    private async UniTaskVoid SummonOneAsync(Vector3 at)
    {
        MonsterBase child = await _summon(at);
        if (child == null) return;
        if (this == null || _reverted || _monster == null || _monster.IsDead)
        {
            if (!child.IsDead) child.ChangeState<DieState>();   // 소환사가 그새 쓰러졌다 — 졸개도 같이
            return;
        }
        MakeMinion(child);
        _minions.Add(child);
    }

    /// <summary>의식 1.2초 — 발밑 얇은 보라 원이 차오른다. 의식 중엔 포이즈 0이라 맞으면 휘청으로 끊긴다(포이즈 없는 등급은 지금 경직으로).</summary>
    private sealed class EliteSummonState : SpecialStateBase
    {
        private readonly MonsterTraits _owner;
        private float      _t;
        private bool       _done;
        private GameObject _circle;

        public EliteSummonState(MonsterTraits owner) { _owner = owner; }

        public override SpecialStateConstraint Constraints => SpecialStateConstraint.None;

        public override void Enter(MonsterContext ctx)
        {
            _t = 0f; _done = false;
            StopAgent(ctx);
            _circle = SpawnDisc(ctx.Transform.position, SummonCircle, PatternGuideHelper.Summon);
            int taunt = MonsterAnimStates.Of(ctx).Taunt;   // 부르는 동작(팩의 도발) — 전투 대기 그대로라 의식으로 안 읽혔다(10-14 정예 애니 점검)
            if (taunt != 0 && ctx.Animator != null) ctx.Animator.CrossFade(taunt, 0.1f);
            else PlayState(ctx, ctx.Animation.attackReadyStateName);
            if (ctx.Monster.Poise != null) ctx.Monster.Poise.Exposed = true;   // 의식 중 포이즈 0 — 맞으면 바로 휘청 · 끊김
            Debug.Log($"[ElitePrefix] 소환 의식 시작 t={Time.time:F2} — {ctx.Monster.name}");
        }

        public override void Update(MonsterContext ctx)
        {
            _t += Time.deltaTime;
            PatternGuideHelper.SetProgress(_circle, _t / SummonRitual);
            if (_t < SummonRitual) return;
            _done = true;
            _owner.SummonNow();
            ctx.Monster.ChangeState<ChaseState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            PatternGuideHelper.SafeDestroy(ref _circle);
            ResumeAgent(ctx);
            if (ctx.Monster.Poise != null) ctx.Monster.Poise.Exposed = false;
            if (!_done) Debug.Log($"[ElitePrefix] 소환 의식 끊김 t={Time.time:F2}");
        }
    }

    // ── 박격 ───────────────────────────────────────────────
    private void TryFireMortar()
    {
        Transform pt = PlayerTransform();
        if (pt == null || !NotBusy()) return;
        Vector3 me = _monster.transform.position;
        float d = FlatDist(pt.position, me);
        if (d < MortarMinDist || d > MortarMaxDist) return;
        _nextAbilityAt = Time.time + Cooldown(MortarEvery);

        Vector3 fwd = _playerVel; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.25f) { fwd = pt.position - me; fwd.y = 0f; }
        fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
        MonsterAttackGlint.Play(_monster, null);   // 쏘는 쪽 — 일반 몬스터 공격 반짝임과 같은 말
        MortarVolleyAsync(pt.position, fwd, AttackDamage(MortarDmgMul), _monster.gameObject).Forget();
    }

    private static async UniTaskVoid MortarVolleyAsync(Vector3 center, Vector3 fwd, int dmg, GameObject src)
    {
        var spots = new[] { center, center + fwd * MortarSpread, center - fwd * MortarSpread };
        var teles = new MonsterTelegraph[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            teles[i] = MonsterTelegraph.Circle(spots[i], MortarRadius, MortarWarn, MonsterTelegraph.Heavy, face: true);
            RoomScopedDrop.Mark(teles[i].gameObject);   // 방을 나가면 예고째 거둔다 — 그러면 판정도 없다
        }
        Debug.Log($"[ElitePrefix] 박격 예고 시작 t={Time.time:F2}");
        if (!await WaitTelegraph(teles[0], MortarWarn)) return;
        bool hit = false;
        for (int i = 0; i < spots.Length; i++)
        {
            RunFx.Play(RunFxSlot.Burst, spots[i], MortarFxScale, MortarBurst);
            if (!hit) hit = HurtPlayerWithin(spots[i], MortarRadius, dmg, HitWeight.Medium, src);   // 원이 겹쳐도 한 번만
        }
        Debug.Log($"[ElitePrefix] 박격 판정 t={Time.time:F2}{(hit ? " — 명중" : "")}");
    }

    // ── 가시 ───────────────────────────────────────────────
    private void AddThorn()
    {
        if (Time.time < _thornsReadyAt) return;
        _thorns++;
        _thornsExpireAt = Time.time + ThornsStackLife;
        if (_thorns >= ThornsMax)
        {
            _thorns = 0;
            _thornsReadyAt = Time.time + ThornsWarn + ThornsCooldown;
            ThornsBurstAsync(_monster.transform.position, AttackDamage(ThornsDmgMul), _monster.gameObject).Forget();
        }
        RebuildBadges();
        PushBadges();
    }

    private static async UniTaskVoid ThornsBurstAsync(Vector3 at, int dmg, GameObject src)
    {
        var tele = MonsterTelegraph.Circle(at, ThornsRadius, ThornsWarn, MonsterTelegraph.Heavy, face: true);
        RoomScopedDrop.Mark(tele.gameObject);
        Debug.Log($"[ElitePrefix] 가시 예고 시작 t={Time.time:F2}");
        if (!await WaitTelegraph(tele, ThornsWarn)) return;
        RunFx.Play(RunFxSlot.Burst, at, ThornsFxScale, ThornsColor);
        bool hit = HurtPlayerWithin(at, ThornsRadius, dmg, HitWeight.Medium, src);
        Debug.Log($"[ElitePrefix] 가시 판정 t={Time.time:F2}{(hit ? " — 명중" : "")}");
    }

    // ── 복수 ───────────────────────────────────────────────
    private void ScanAllies()
    {
        _avengeScanAt = Time.time + AvengeScanGap;
        _watched.RemoveWhere(m => m == null || !m.isActiveAndEnabled);
        MonsterBase.CopyActive(s_scan);
        Vector3 me = _monster.transform.position;
        float r2 = AvengeWatch * AvengeWatch;
        foreach (var m in s_scan)
        {
            if (m == null || m == _monster || m.IsDead || m.Grade == MonsterGrade.Boss) continue;
            if ((m.transform.position - me).sqrMagnitude > r2) continue;
            if (_watched.Add(m)) m.OnDied += HandleAllyDied;
        }
    }

    private void HandleAllyDied(MonsterBase m)
    {
        _watched.Remove(m);
        if (m != null) m.OnDied -= HandleAllyDied;
        if (_reverted || _monster == null || _monster.IsDead || m == null || _avenge >= AvengeMax) return;
        if (FlatDist(m.transform.position, _monster.transform.position) > AvengeRadius) return;
        _avenge++;
        _monster.SetTraitAttackMul(1f + AvengeAtkStep * _avenge);
        RebuildBadges();
        PushBadges();
        Debug.Log($"[ElitePrefix] 복수 {_avenge}중첩 — 공격 ×{1f + AvengeAtkStep * _avenge:F2}");
    }

    // ── 처치 — 원석 · 졸개 정리 ───────────────────────────
    private void HandleDiedElite(MonsterBase m)
    {
        foreach (var mn in _minions)
            if (mn != null && !mn.IsDead && mn.isActiveAndEnabled) mn.ChangeState<DieState>();
        _minions.Clear();
        var bank = GameRunBootstrapper.Instance?.Run?.FuelBank;
        if (bank == null) return;
        bank.Add(FuelKind.RuneOre, EliteOreReward);
        ItemEffectVfxHelper.ShowNotice($"<color={UIPalette.RuneOreHex}>원석 +{EliteOreReward}</color>  (정예)");
    }

    // ── 공용 ───────────────────────────────────────────────
    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || !ctx.Agent.isActiveAndEnabled || !ctx.Agent.isOnNavMesh) return;
        ctx.Agent.isStopped = true;
        ctx.Agent.ResetPath();
        ctx.Agent.velocity = Vector3.zero;   // 남은 추적 속도로 예고 자세가 미끄러졌다(10-14 실측 2.5~3.5 m/s)
    }

    private static void ResumeAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || !ctx.Agent.isActiveAndEnabled || !ctx.Agent.isOnNavMesh) return;
        ctx.Agent.isStopped = false;
        ctx.Agent.Warp(ctx.Transform.position);
    }

    private static void PlayState(MonsterContext ctx, string state)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(state)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(state))) return;
        ctx.Animator.CrossFade(state, 0.1f);
    }

    /// <summary>예고가 피해 순간까지 남아 있으면 true — 그새 방을 떠나 거둬졌으면 false(판정 없음). 예고는 판정 0.1초 뒤 스스로 사라진다.</summary>
    private static async UniTask<bool> WaitTelegraph(MonsterTelegraph tele, float seconds)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: tele.GetCancellationTokenOnDestroy());
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }
}
