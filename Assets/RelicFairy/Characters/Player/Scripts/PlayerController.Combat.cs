using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

// PlayerController — 피해·사망·무적 · 포이즈·날아감 · 스탠스 · 상태이상 · 저스트 회피
public sealed partial class PlayerController
{
    // ── Properties ────────────────────────────────────────────────
    /// <summary>무적 중 여부 (debugInvincible · 긴급 회피 무적 포함).</summary>
    public bool IsInvincible => debugInvincible || Time.time < _invincibleEnd || PerfectDodge.IsInvulnerable;

    /// <summary>피격 넉백으로 날아가는 중(착지 회복 포함) — 이 동안 모든 조작 불가.</summary>
    private bool IsLaunched => _locoSM?.CurrentId == LocoState.Launched;

    /// <summary>스탠스 중인가 — 포이즈 브레이크(날아감)를 막는다. 피해 자체는 그대로 받는다.</summary>
    private bool PoiseImmune => _stanceOwner != null;

    public bool IsKnockback => Status.IsKnockback;

    /// <summary>저스트 회피 슬로모 동안의 이동 배율(평소 1). DefaultMoveAbility가 최고속에 곱한다.</summary>
    public float BonusMoveSpeedMultiplier => PerfectDodge.BonusMoveMultiplier;

    /// <summary>저스트 회피 반격 창 동안의 공격 애니 속도 배수(평소 1). ActAttackState가 콤보 단계마다 곱한다.</summary>
    public float CounterAttackSpeedMultiplier => PerfectDodge.CounterSpeedMultiplier;

    /// <summary>가장 최근 저스트 회피를 일으킨 적(없으면 null). <see cref="OnPerfectDodge"/> 안에서 읽으면 이번 회피의 적이다.</summary>
    public Transform PerfectDodgeSource => PerfectDodge.LastSource;

    /// <summary>저스트 회피 발동 — 연출(회색 필터/틴트/잔상)이 구독한다. 인자는 총 지속시간(초, 실제시간).</summary>
    public event Action<float> OnPerfectDodge
    {
        add    => PerfectDodge.Triggered += value;
        remove => PerfectDodge.Triggered -= value;
    }

    /// <summary>반격 창이 닫혔다 — 연출이 '보상 끝' 신호를 낸다.</summary>
    public event Action OnCounterWindowEnded
    {
        add    => PerfectDodge.CounterWindowEnded += value;
        remove => PerfectDodge.CounterWindowEnded -= value;
    }

    /// <summary>TakeDamage 한 번의 판정 결과 — 막힌 공격까지 보이게(전투 로그·실측용, 동작에는 관여하지 않는다).</summary>
    public enum DamageOutcome { Applied, PerfectDodge, Invincible, Negated, DeathNegated }

    /// <summary>피해 판정 결과. 인자: 가해자, 들어온 피해(보정 전), 최종 피해(HP 차감량), 결과.</summary>
    public event Action<GameObject, int, int, DamageOutcome> OnDamageResolved;

    // ── Public Methods: 피해 · 회복 · 사망 ─────────────────────────
    /// <param name="ignorePoise">포이즈를 무시하고 확정으로 날아감을 발동한다(보스 대기술 등). 넉백 면역은 그대로 적용.</param>
    /// <param name="weight">피격 연출 등급. Auto면 최종 피해/최대 HP(8%·20%)와 ignorePoise로 판정한다.
    /// 장판 틱·DoT처럼 잦은 피해는 Light로 넘기면 같은 공격자 기준 0.3초 안 연출이 한 번으로 줄어든다.</param>
    public void TakeDamage(int dmg, GameObject attacker = null, bool ignorePoise = false, HitWeight weight = HitWeight.Auto)
    {
        int rawDmg = dmg;

        // 저스트 회피 — 회피 초반(퍼펙트 창)에 스친 공격이면 슬로모+이동보너스로 보상하고 피해는 무효.
        if (PerfectDodge.TryTriggerOnHit(attacker))
        {
            OnDamageResolved?.Invoke(attacker, rawDmg, 0, DamageOutcome.PerfectDodge);
            return;
        }

        // 긴급 회피 무적 — 발동 뒤 창이 닫힐 때까지(피한 공격은 끝날 때까지) 모든 공격을 흘린다.
        // 대시 무적만으로는 느려진 시간 속에 뒤늦게 들어오는 공격·계속되는 돌진에 맞았다(10-01).
        if (PerfectDodge.IsInvulnerable)
        {
            OnDamageResolved?.Invoke(attacker, rawDmg, 0, DamageOutcome.PerfectDodge);
            return;
        }

        if (debugInvincible || Time.time < _invincibleEnd)
        {
            OnDamageResolved?.Invoke(attacker, rawDmg, 0, DamageOutcome.Invincible);
            return;
        }

        // 유물 피해 보정 (갈라하드 방패 전방 감소 등)
        if (RelicBehavior != null)
            dmg = RelicBehavior.ModifyIncomingDamage(this, dmg, attacker);

        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;

        // 피격 전 — 무효화/감소 처리
        var pkt = new DamagePacket(dmg, attacker: attacker, target: gameObject);
        mgr?.OnPreTakeDamage(ref pkt);

        if (pkt.Negated)
        {
            FirePassive(PassiveTrigger.OnTakeDamage, new PassiveContext { damage = 0, attacker = attacker });
            OnDamageResolved?.Invoke(attacker, rawDmg, 0, DamageOutcome.Negated);
            return;
        }

        int finalDmg = Mathf.Max(0, (int)pkt.FinalDamage);

        // [서약] 들어오는 피해 변조 — 사망 체크 전(Galahad 무효 등이 치명타를 취소할 수 있도록)
        var covHandler = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
        if (covHandler != null)
        {
            float fd = covHandler.ModifyIncoming(finalDmg, new CombatContext { Target = gameObject, Damage = finalDmg });
            finalDmg = Mathf.Max(0, (int)fd);
        }

        // [방어력] 체감 감소(diminishing returns) — 피해배율 = K / (K + 방어력).
        // 가산 스택이 100%로 수렴하지 않아 상한 캡이 불필요하고, 방어력 K당 유효체력이 원 체력만큼 선형 증가한다.
        // K는 밸런스 튜닝 값(기본 방어 32 기준 감소율 약 24%).
        const float DefenseK = 100f;
        int defense = RuntimeStats.Defense;
        if (defense > 0)
            finalDmg = Mathf.Max(0, Mathf.RoundToInt(finalDmg * (DefenseK / (DefenseK + defense))));

        // [받피감소 통합 채널] 아이템/캐릭터/어둠룬 피해감소(%)를 한 곳에서 1회 적용.
        // (DamageReductionEffect.OnPreTakeDamage 제거 → 여기로 통합. DamageReduction은 Recalculate에서 Clamp01.)
        // 스탠스(스킬 시전 중 방어)는 같은 채널에 얹는다 — 따로 곱하면 감소가 두 번 적용된다.
        float dr = Mathf.Clamp01(RuntimeStats.DamageReduction + _stanceDefenseAdd);
        if (dr > 0f)
            finalDmg = Mathf.Max(0, Mathf.RoundToInt(finalDmg * (1f - dr)));

        // 실드 흡수 — HP 차감 전. 실드가 먼저 피해를 받는다.
        if (finalDmg > 0)
        {
            finalDmg = RuntimeStats.AbsorbWithShield(finalDmg);
        }

        // 사망 직전 체크
        if (RuntimeStats.Hp - finalDmg <= 0 && mgr != null)
        {
            if (mgr.OnNearDeath(out float healPct, out float invDur))
            {
                int healHp = Mathf.Max(1, (int)(RuntimeStats.MaxHp * healPct));
                RuntimeStats.SetHp(healHp);
                if (invDur > 0f)
                {
                    _invincibleEnd = Time.time + invDur;
                    ItemEffectVfxHelper.AttachLoopVfx("VFX_DeathNegateAura", transform, invDur).Forget();
                    ItemEffectVfxHelper.ShowNotice($"<color=#FF4444>사망 무효!</color> {invDur:F0}초 무적");
                }
                OnDamageResolved?.Invoke(attacker, rawDmg, 0, DamageOutcome.DeathNegated);
                return;
            }
        }

        RuntimeStats.Damage(finalDmg);
        OnDamageResolved?.Invoke(attacker, rawDmg, finalDmg, DamageOutcome.Applied);

        if (finalDmg > 0)
        {
            OnDamageTaken?.Invoke();

            // 피격 연출 — 등급(약·중·강)으로 세기를 나눈다(→ PlayerHitPresentation · HUD 비네트).
            // 모든 공격이 같은 세기면 스트레스고, 강한 공격은 맞은 순간이 읽혀야 한다.
            // 같은 공격자의 Light가 짧은 간격으로 이어지면(장판 틱·DoT) 피해만 넣고 연출은 한 번만.
            HitWeight grade = ResolveHitWeight(weight, finalDmg, ignorePoise);
            if (!IsRepeatedLightHit(grade, attacker))
            {
                SpawnHitBloodVfx();
                // 가해자→피해자 방향(가해자 미상이면 0 → 무방향 흔들림).
                Vector3 hitDir = attacker != null ? transform.position - attacker.transform.position : Vector3.zero;
                OnHitTaken?.Invoke(grade, hitDir, finalDmg);
            }

            // 룬 속성 OnDamaged 통지(어둠 게이지 등). 실제 피해가 들어갈 때만 — i-frame/회피/무효/사망무효는 위에서 이미 return.
            _runeEffects?.NotifyDamaged(finalDmg, attacker);

            // 「무결」 기행 — 이 챕터의 무피격 판정을 깬다.
            // 회피·무효는 위에서 return되지만 finalDmg가 0으로 깎이는 경로(방어력·받피감소·실드 전량 흡수)는
            // 여기까지 내려온다. 블록 밖에 두면 HP가 1도 안 깎였는데 무피격이 깨진다.
            GameRunBootstrapper.Instance?.Run?.ReportPlayerDamaged();
        }

        // [서약] 피격 통보 (실제 적용 피해량)
        covHandler?.OnTakeDamage(finalDmg);

        // 사망 판정 — 아이템(OnNearDeath) 부활 실패 후 HP 0이면 서약 사망방지 체크, 그래도 0이면 사망 처리.
        TryHandleDeath();

        // 포이즈(아머치) — 누적 임팩트가 최대치를 넘으면 날아감(LocoState.Launched) 발동.
        // 회복이 붙어 있어 연타로 맞을 때만 브레이크된다(띄엄띄엄 맞으면 회복돼 안 날아감).
        // 넉백 면역 중이면 PoiseController가 알아서 무시 → 무한 저글링 방지. 사망했으면 생략.
        //
        // 잡힌 상태(IsGrabbed)에서도 생략한다. 보스 손에 붙들린 채 내리찍히는 동안 포이즈가 터지면
        // 몸은 손바닥에 고정돼 있는데 자세만 '날아감(누움)'으로 바뀌어, 잡혀 있는데 누워 있는 그림이 된다.
        // 붙들린 동안의 연출 권한은 잡기 패턴이 가진다.
        // 스탠스 중이면 포이즈 계산 자체를 건너뛴다. <b>면역이지 무적이 아니다</b> —
        // 피해는 위에서 그대로 들어갔고, 여기서 막는 건 '날아감'뿐이다.
        // 2.75초를 제자리에서 버티는 스킬(랜슬롯 Q 등)은 이게 없으면 성립하지 않는다.
        //
        // 약(Light — 장판 틱 · DoT · 잔불)은 포이즈를 쌓지 않는다. 틱이 쌓여 1초 안에 날아가고, 내려앉으면 같은 장판에서
        // 다시 날아가는 연쇄가 생겼다(10-01 보스 감사 — 화룡 잔불 · 숲 브레스 · 가시 띠). 확정 날아감(ignorePoise)은 그대로.
        bool lightTick = weight == HitWeight.Light && !ignorePoise;
        if (!_dead && !IsGrabbed && !PoiseImmune && !lightTick && Poise != null && CharacterData != null && RuntimeStats != null)
        {
            float maxPoise = RuntimeStats.MaxPoise;
            float immunity = CharacterData.knockbackImmunity;

            bool broke = ignorePoise
                ? Poise.ForceBreak(maxPoise, immunity)
                : Poise.TakeImpact(finalDmg * CharacterData.poiseImpactPerDamage,
                                   maxPoise, CharacterData.poiseRegenDelay, immunity);

            if (broke) LaunchFrom(attacker);
        }

        // 피격 후 — 반사/방버프 등. Attacker를 채워야 DamageReflect/FireReflect가 반사 대상을 안다.
        var report = new DamageReport
        {
            DamageDealt = finalDmg,
            Attacker = attacker,
            Target = gameObject,
            HitPosition = attacker != null ? attacker.transform.position : transform.position,
        };
        mgr?.OnPostTakeDamage(report);

        FirePassive(PassiveTrigger.OnTakeDamage, new PassiveContext { damage = finalDmg, attacker = attacker });
    }

    /// <summary>피격 등급 — 공격 쪽이 정했으면 그대로, Auto면 ignorePoise → Heavy, 아니면 최종 피해/최대 HP로.</summary>
    private HitWeight ResolveHitWeight(HitWeight requested, int finalDmg, bool ignorePoise)
    {
        if (requested != HitWeight.Auto) return requested;
        if (ignorePoise) return HitWeight.Heavy;
        float maxHp = RuntimeStats != null ? Mathf.Max(1f, RuntimeStats.MaxHp) : 100f;
        float ratio = finalDmg / maxHp;
        return ratio >= HeavyHitHpRatio ? HitWeight.Heavy
             : ratio >= MediumHitHpRatio ? HitWeight.Medium
             : HitWeight.Light;
    }

    /// <summary>같은 공격자의 Light가 억제 창(실시간 0.3초) 안에 다시 왔는가 — 그러면 연출을 생략한다.
    /// 창은 연출을 낸 Light에서만 다시 시작하므로 틱이 이어지면 창마다 한 번씩 보인다.</summary>
    private bool IsRepeatedLightHit(HitWeight grade, GameObject attacker)
    {
        if (grade != HitWeight.Light) return false;
        float now = Time.unscaledTime;
        bool repeated = _lastLightHitAt >= 0f && attacker == _lastLightHitAttacker
                        && now - _lastLightHitAt < LightHitRepeatWindow;
        if (!repeated)
        {
            _lastLightHitAttacker = attacker;
            _lastLightHitAt       = now;
        }
        return repeated;
    }

    public void Heal(int amount)
    {
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        mgr?.ModifyHeal(ref amount);
        RuntimeStats.Heal(amount);
    }

    /// <summary>
    /// HP가 0 이하면 서약 사망방지 체크 후 사망 처리. TakeDamage 외 경로(아이템 자해 등)로
    /// HP가 소진됐을 때 호출해 "다음 피격까지 생존" 버그를 막는다.
    /// </summary>
    public void NotifyHpDepleted() => TryHandleDeath();

    /// <summary>외부에서 일시 무적 상태로 설정. 기존 무적이 남아있으면 더 긴 쪽을 유지.
    /// 사용처: 낙사 리스폰(FallRecoveryController), 부활 아이템 등.</summary>
    public void SetInvincible(float duration)
    {
        if (duration <= 0f) return;
        _invincibleEnd = Mathf.Max(_invincibleEnd, Time.time + duration);
    }

    /// <summary>잡기 패턴이 붙들기 시작/해제 시 호출. 이동 잠금(SetMoveScale)과 같은 수명으로 다뤄야 한다.</summary>
    public void SetGrabbed(bool grabbed) => IsGrabbed = grabbed;

    /// <summary>분신으로 마킹. Awake 전에 호출해야 카메라 등록이 방지된다.</summary>
    public void SetAsShadowClone() => IsShadowClone = true;

    /// <summary>번개 그로기: duration초 동안 비네트로 시야를 좁힌다.</summary>
    public void ApplyThunderGroggy(float duration)
        => RelicFairy.Monster.ThunderGroggyVignetteView.Trigger(duration);

    // ── Public Methods: 회피 연출 신호 ────────────────────────────
    public void RaiseDodgeIFrame(bool active) => OnDodgeIFrame?.Invoke(active);
    public void RaiseDodgeStart() => OnDodgeStart?.Invoke();
    public void RaiseDodgeEnd()   => OnDodgeEnd?.Invoke();
    public void RaiseCounterStrike(Vector3 from, Vector3 to, bool isFirst) => OnCounterStrike?.Invoke(from, to, isFirst);

    // ── Public Methods: 날아감 · 행동 취소 ─────────────────────────
    /// <summary>진행 중인 공격/스킬을 즉시 취소한다(날아감 진입 등).</summary>
    public void CancelActions()
    {
        Combo?.Reset();
        if (_actSM != null && _actSM.CurrentId != ActState.None)
            _actSM.Change(ActState.None);
    }

    /// <summary>날아감 방향(수평 정규화)을 소비한다. 미설정이면 등 뒤(-forward).</summary>
    public Vector3 ConsumeLaunchDirection()
    {
        Vector3 d = _pendingLaunchDir;
        _pendingLaunchDir = Vector3.zero;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : -transform.forward;
    }

    // ── Public Methods: 스킬 스탠스 ───────────────────────────────
    /// <summary>
    /// 스탠스 시작. owner는 해제 권한을 가진 주체(보통 스킬 런타임 자신)다.
    /// 이미 다른 스탠스가 켜져 있으면 <b>덮어쓰지 않는다</b> — 먼저 켠 쪽이 끝까지 소유한다.
    /// </summary>
    public void BeginStance(object owner, float defenseAdd)
    {
        if (owner == null || _stanceOwner != null) return;
        _stanceOwner      = owner;
        _stanceDefenseAdd = Mathf.Clamp01(defenseAdd);
    }

    /// <summary>스탠스 해제. 자기가 켠 것만 끌 수 있다(중단·사망 경로에서도 반드시 호출).</summary>
    public void EndStance(object owner)
    {
        if (owner == null || !ReferenceEquals(_stanceOwner, owner)) return;
        _stanceOwner      = null;
        _stanceDefenseAdd = 0f;
    }

    // ── Public Methods: 상태이상 (PlayerStatusEffects 위임) ────────
    /// <summary>이동 속도를 scale 배율로 duration초 동안 감소시킨다. 종료 시 자동으로 복구.</summary>
    public void ApplySlow(float scale, float duration) => Status.ApplySlow(scale, duration);

    /// <summary>슬로우 상태를 즉시 해제하고 부착된 VFX를 제거한다.</summary>
    public void ClearSlow() => Status.ClearSlow();

    /// <summary>
    /// 얼음 공격 1회 처리. 반환값: 1=1단계(화면이펙트), 2=빙결 발동, 0=이미 빙결 중(연장만).
    /// </summary>
    public int AddIceStack(float fullDuration) => Status.AddIceStack(fullDuration);

    /// <summary>외부 힘(넉백)을 가하고 일정 시간 동안 수평 이동 잠금을 스킵한다. 무적 중이면 무시한다.</summary>
    public void ApplyKnockback(Vector3 force, float duration = 0.3f)
    {
        // 무적 = 피했다(리치·화룡 패턴의 dodged 판정과 같은 뜻). 몬스터 공격은 TakeDamage 뒤 결과와 무관하게 넉백을 부르므로,
        // 여기서 거르지 않으면 피해는 막고도 옆으로 떠밀려 「회피했는데 맞았다」로 읽힌다(10-01).
        if (IsInvincible) return;
        Status.ApplyKnockback(force, duration);
    }

    // ── Public Methods: 저스트 회피 (PerfectDodgeController 위임) ──
    /// <summary>회피 진입 시 호출(LocoDodgeState.Enter). 퍼펙트 판정을 장전한다.</summary>
    public void ArmPerfectDodge() => PerfectDodge.Arm();

    /// <summary>반격 창 안의 공격이 겨냥할 원인 적(첫 공격 여부 포함). ActAttackState가 콤보 단계 시작에 묻는다.</summary>
    public bool TryGetCounterTarget(out Transform target, out bool isFirst) => PerfectDodge.TryGetCounterTarget(out target, out isFirst);

    // ── Private Methods ───────────────────────────────────────────
    private void TryHandleDeath()
    {
        if (RuntimeStats.Hp > 0 || _dead) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.CovenantHandler != null && run.CovenantHandler.TryPreventDeath())
        {
            RuntimeStats.SetHp(Mathf.Max(1, RuntimeStats.Hp)); // 서약 사망방지 → 사망 취소
            _invincibleEnd = Time.time + 1f;
        }
        else if (run != null && run.TryConsumeMetaRevive())
        {
            // 기억의 제단 「부활 1회」 — 런당 1회, 체력 절반으로 일어난다.
            // 서약 사망방지보다 <b>뒤에</b> 둔다: 서약은 런 안에서 얻은 것이라 먼저 소모돼야
            // 영구 해금이 런 자원을 대신 태우는 일이 없다.
            RuntimeStats.SetHp(Mathf.Max(1, RuntimeStats.MaxHp / 2));
            _invincibleEnd = Time.time + 2f;
            Debug.Log("[MemoryAltar] 부활 1회 소모 — 체력 절반으로 복귀");
        }
        else
        {
            _dead = true;
            SetInputEnabled(false);
            PerfectDodge.Clear();   // 사망했는데 슬로모가 남아 시간이 느린 채로 진행되는 것 방지
            if (IntroBootstrapper.Instance != null)
                IntroBootstrapper.Instance.HandleIntroDeath();
            else
                GameRunBootstrapper.Instance?.HandlePlayerDeath();
        }
    }

    /// <summary>
    /// 피격 넉백 — 날아감(LocoState.Launched) 진입. attacker 반대방향으로 띄운다.
    /// 사망 중이거나 이미 날아가는 중이면 무시한다.
    /// </summary>
    private void LaunchFrom(GameObject attacker)
    {
        if (_dead || _locoSM == null) return;
        if (_locoSM.CurrentId == LocoState.Launched) return;

        Vector3 dir = attacker != null
            ? (transform.position - attacker.transform.position)
            : -transform.forward;
        dir.y = 0f;
        _pendingLaunchDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;

        _locoSM.Change(LocoState.Launched);
    }

    /// <summary>
    /// 피격 혈흔 — 플레이어에 부모로 붙어 함께 움직인다.
    ///
    /// 피격은 연속으로 들어오므로 매번 Instantiate/Destroy하면 할당이 누적된다. 프리팹이 인스펙터
    /// 직접 참조(Addressables 키 없음)라 <see cref="ObjectPoolerManager.SpawnFromPrefab"/>으로 푼다.
    /// </summary>
    private void SpawnHitBloodVfx()
    {
        if (_hitBloodVfxPrefab == null) return;

        Vector3 pos = transform.position + Vector3.up * _hitBloodVfxHeightOffset;
        Quaternion rot = _hitBloodVfxPrefab.transform.rotation;

        var pooler = Managers.ObjectPooler;
        GameObject go = pooler != null
            ? pooler.SpawnFromPrefab(_hitBloodVfxPrefab, ObjectPoolerManager.PoolType.Effect, pos, rot)
            : Instantiate(_hitBloodVfxPrefab, pos, rot);
        if (go == null) return;

        // 부모 → 스케일 순서. 먼저 붙이지 않으면 SetParent가 월드 스케일을 보존하려고 localScale을 되돌린다.
        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.localScale = Vector3.one * _hitBloodVfxScale;

        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 3f;

        if (pooler == null) { Destroy(go, lifetime); return; }

        // 스케일은 위에서 절대값으로 직접 넣었으므로 스케일 미적용 오버로드를 쓴다(곱연산 누적 방지).
        if (!go.TryGetComponent<PooledOneShotVfx>(out var vfx)) vfx = go.AddComponent<PooledOneShotVfx>();
        vfx.Play(lifetime);
    }

    // ── Event Handlers ────────────────────────────────────────────
    /// <summary>타격 통지(OnEnable 구독) — 저스트 회피 반격 창의 적중 환급으로 넘긴다.</summary>
    private void HandleHitFeedback(HitInfo info) => PerfectDodge.NotifyPlayerHit(info);
}
