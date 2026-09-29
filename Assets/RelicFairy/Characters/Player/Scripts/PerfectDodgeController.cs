using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 저스트 회피(퍼펙트 닷지) — 회피 초반에 공격이 스치면 슬로모 + 플레이어 이동 보너스(베요네타 Witch Time).
/// PlayerController가 소유하고 Update에서 <see cref="Tick"/>한다.
///
/// 보상(2026-09-18 설계 「저스트 회피 보상」 A+B1):
///   · 대시 게이지 환급 — 발동 즉시 대시 1회분을 돌려주고 회복 지연을 푼다(악몽 배율 미적용).
///   · 반격 창 — 슬로모 동안 모든 공격이 원인 적을 겨냥(첫 공격은 추격 돌진), 공격 애니 가속, 적중마다 게이지 환급.
///
/// 시간 배율 요청의 주인은 <b>PlayerController(Unity 객체)</b>다. TimeScaleArbiter는 파괴된 Unity 객체의 요청을
/// 스스로 치우므로, 해제 경로를 놓쳐도 시간이 느린 채로 남지 않는다 — 이 클래스(순수 C# 객체)를 주인으로 쓰면
/// 그 안전장치가 사라진다.
/// </summary>
public sealed class PerfectDodgeController
{
    // ── Constants ─────────────────────────────────────────────────
    // 발동 순간 프레임 스톱에 쓰는 시간 배율(완전 0은 물리/애니가 죽어 복귀가 튈 수 있어 아주 작은 값).
    private const float FreezeScale = 0.02f;
    private const int   SenseMax    = 8;   // windup 감지 시 훑을 적 수 상한

    // ── Private ───────────────────────────────────────────────────
    private readonly PlayerController _owner;

    // windup 감지 질의 버퍼(재사용 — 회피마다 alloc 방지)
    private readonly List<RelicFairy.Monster.MonsterBase> _senseBuf = new(SenseMax);

    private bool  _armed;             // 이번 회피에서 아직 발동하지 않았는지
    private float _end;               // 슬로모/보너스 종료 시각(unscaled) — 반격 창도 이 시각에 닫힌다
    private float _freezeEnd;         // 프레임 스톱 종료 시각(unscaled)
    private bool  _frozen;            // 지금 프레임 스톱 구간인지
    private float _scale = 0.35f;     // 프리즈 후 적용할 슬로모 배율

    // 반격 창
    private bool      _counterActive;        // 창이 열려 있는가(발동 ~ 슬로모 종료)
    private bool      _counterLungePending;  // 창의 첫 공격(긴 추격)이 아직 안 쓰였는가
    private Transform _counterSource;        // 저스트 회피를 일으킨 적(추격 대상)
    private Transform _pendingSource;        // Arm~Trigger 사이 원인 적 후보

    // ── Properties ────────────────────────────────────────────────
    /// <summary>저스트 회피 슬로모 동안의 이동 배율(평소 1). DefaultMoveAbility가 최고속에 곱한다.</summary>
    public float BonusMoveMultiplier { get; private set; } = 1f;

    /// <summary>반격 창이 열려 있는가 — 저스트 회피 발동부터 슬로모 종료까지.</summary>
    public bool IsCounterWindow => _counterActive && Time.unscaledTime < _end;

    /// <summary>반격 창 동안의 공격 애니 속도 배수(평소 1). ActAttackState가 콤보 단계마다 곱한다.</summary>
    public float CounterSpeedMultiplier
    {
        get
        {
            if (!IsCounterWindow) return 1f;
            var cd = _owner.CharacterData;
            return cd != null ? Mathf.Max(1f, cd.perfectDodgeCounterSpeed) : 1f;
        }
    }

    /// <summary>저스트 회피 발동 — 연출(회색 필터/틴트/잔상)이 구독한다. 인자는 총 지속시간(초, 실제시간).</summary>
    public event Action<float> Triggered;

    /// <summary>반격 창이 닫혔다(시간 만료·강제 종료) — 연출이 '보상 끝' 신호를 낸다.</summary>
    public event Action CounterWindowEnded;

    public PerfectDodgeController(PlayerController owner) => _owner = owner;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 회피 진입 시 호출(LocoDodgeState.Enter). 퍼펙트 판정을 장전하고, <b>이미 날아오는 공격이 있으면 즉시 발동</b>한다.
    ///
    /// '맞으면 발동'만으로는 거의 안 터진다 — 몹은 피해를 적용하는 순간에 거리를 <b>다시</b> 재는데
    /// (MonsterBase.DealDamageToPlayer), 대시로 4m를 빠져나가면 그 검사에서 걸러져 TakeDamage 자체가
    /// 호출되지 않는다. 즉 "회피에 성공하면 판정이 안 온다"는 모순이 생긴다.
    /// 그래서 <b>적의 공격 windup(예고) 중에 회피를 시작했는가</b>로 잡는다. 이게 긴급회피의 실제 정의다.
    /// </summary>
    public void Arm()
    {
        _armed = true;
        if (SensesIncomingAttack()) Trigger("공격 예고 회피", _pendingSource);
    }

    /// <summary>
    /// <b>대시(회피) 중에 공격을 맞았는가</b>를 판정. TakeDamage 맨 앞에서 호출.
    /// 대시 전 구간이 무적이라 피해는 어차피 0이지만, 회피로 파고들어 실제로 접촉한 경우를 여기서 잡는다.
    /// (대부분의 저스트 회피는 위 windup 감지로 먼저 발동한다.)
    /// </summary>
    /// <param name="attacker">공격한 적 — 반격 창의 추격 대상이 된다(없으면 추격 없음).</param>
    public bool TryTriggerOnHit(GameObject attacker)
    {
        if (!_armed) return false;
        var loco = _owner.LocoSM;
        if (loco == null || loco.CurrentId != LocoState.Dodge) return false;

        Trigger("대시 중 피격", attacker != null ? attacker.transform : null);
        return true;
    }

    /// <summary>
    /// 반격 창 안의 공격이 겨냥할 원인 적. 창이 닫혔거나 대상이 죽었거나 사라졌으면 false.
    /// <paramref name="isFirst"/>는 창의 첫 공격일 때만 true(1회 소비) — 첫 공격만 먼 거리를 추격한다.
    /// </summary>
    public bool TryGetCounterTarget(out Transform target, out bool isFirst)
    {
        target  = null;
        isFirst = false;
        if (!IsCounterWindow) return false;

        if (_counterSource == null || !_counterSource.gameObject.activeInHierarchy) return false;
        // 투사체 등 몬스터가 아닌 원인은 쫓지 않는다 — 몬스터 본체일 때만 추격.
        var mb = _counterSource.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb == null || mb.CurrentHp <= 0) return false;

        target               = mb.transform;
        isFirst              = _counterLungePending;
        _counterLungePending = false;
        return true;
    }

    /// <summary>
    /// 플레이어 타격 통지(HitFeedbackService.OnHit). 반격 창 안의 적중마다 대시 게이지를 조금 돌려준다 —
    /// 반격이 곧 다음 회피의 자원이 되게 한다.
    /// </summary>
    public void NotifyPlayerHit(in HitInfo info)
    {
        if (!IsCounterWindow) return;
        if (info.Attacker != _owner.gameObject || info.Damage <= 0f) return;

        var cd = _owner.CharacterData;
        if (cd != null) RefundStamina(cd.perfectDodgeCounterHitRefund);
    }

    /// <summary>프리즈→슬로모 전환 및 만료 처리. Update에서 unscaled 시간으로 구동.</summary>
    public void Tick()
    {
        // 반격 창은 시간 배율 보유 여부와 무관하게 종료 시각에 닫는다(다른 경로가 먼저 해제해도 창이 남지 않게).
        if (_counterActive && Time.unscaledTime >= _end) EndCounterWindow();

        if (!TimeScaleArbiter.IsHeldBy(_owner)) return;

        // 프레임 스톱 종료 → 같은 owner로 슬로모 배율로 덮어쓴다.
        if (_frozen && Time.unscaledTime >= _freezeEnd)
        {
            _frozen = false;
            TimeScaleArbiter.Acquire(_owner, _scale, TimeScaleArbiter.Priority.SlowMotion);
        }

        if (Time.unscaledTime < _end) return;

        TimeScaleArbiter.Release(_owner);
        BonusMoveMultiplier = 1f;
        _frozen = false;
        if (_owner.Anim != null) _owner.Anim.updateMode = AnimatorUpdateMode.Normal;
    }

    /// <summary>슬로모를 강제 종료한다(사망/씬 전환 시 시간이 느린 채로 남지 않도록).</summary>
    public void Clear()
    {
        TimeScaleArbiter.Release(_owner);
        BonusMoveMultiplier = 1f;
        _armed  = false;
        _frozen = false;
        EndCounterWindow();
        if (_owner.Anim != null) _owner.Anim.updateMode = AnimatorUpdateMode.Normal;
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>회피 반경 안에 공격 windup 중인 적이 있는가(= 지금 회피하면 아슬하게 피하는 것). 찾은 적을 원인 후보로 남긴다.</summary>
    private bool SensesIncomingAttack()
    {
        _pendingSource = null;
        var cd = _owner.CharacterData;
        float r = cd != null ? cd.perfectDodgeSenseRadius : 4f;
        if (r <= 0f) return false;

        int n = CombatQuery.GetNearbyEnemies(_owner.transform.position, r, _owner.gameObject, SenseMax, _senseBuf);
        for (int i = 0; i < n; i++)
        {
            var mb = _senseBuf[i];
            if (mb != null && mb.IsTelegraphingAttack)
            {
                _pendingSource = mb.transform;
                return true;
            }
        }
        return false;
    }

    /// <summary>저스트 회피 발동 본체 — 슬로모 + 이동 보너스 + 보상(게이지 환급·반격 창) + 연출 신호. 회피 1회당 1발.</summary>
    private void Trigger(string trigger, Transform source)
    {
        if (!_armed) return;
        _armed = false;

        var cd = _owner.CharacterData;
        float scale    = cd != null ? Mathf.Clamp(cd.perfectDodgeTimeScale, 0.05f, 1f) : 0.35f;
        float duration = cd != null ? Mathf.Max(0f, cd.perfectDodgeDuration)   : 1.2f;
        float boost    = cd != null ? Mathf.Max(1f, cd.perfectDodgeSpeedBoost) : 1.3f;
        float freeze   = cd != null ? Mathf.Max(0f, cd.perfectDodgeFreeze)     : 0.07f;
        if (duration <= 0f) return;

        _scale = scale;

        // 시간은 TimeScaleArbiter가 단일 소유 — 직접 Time.timeScale을 만지지 않는다.
        // 프레임 스톱과 슬로모는 우선순위가 달라(HitStop 10 < SlowMotion 100) 겹쳐 걸 수 없으므로,
        // 같은 owner로 '프리즈 → 슬로모' 순차 덮어쓰기를 한다.
        _frozen = freeze > 0f;
        TimeScaleArbiter.Acquire(_owner,
            _frozen ? FreezeScale : scale,
            TimeScaleArbiter.Priority.SlowMotion);

        _freezeEnd = Time.unscaledTime + freeze;
        _end       = Time.unscaledTime + freeze + duration;

        // 세계는 느려지는데 플레이어는 빨라야 한다.
        // 물리는 스케일된 시간으로 적분되므로, 시간배율의 역수(1/scale)만큼 되돌리고 그 위에 부스트를 얹는다.
        BonusMoveMultiplier = (1f / scale) * boost;

        // 세계만 느려지고 플레이어는 정상 속도로 움직여야 한다(Witch Time의 핵심).
        // Time.timeScale은 전역이라 Animator까지 같이 느려진다 → 플레이어 Animator만 실제시간으로 돌린다.
        // 이게 없으면 "슬로모 애니로 빠르게 미끄러지고 공격도 느리게 나가는" 반쪽짜리가 된다.
        if (_owner.Anim != null) _owner.Anim.updateMode = AnimatorUpdateMode.UnscaledTime;

        // 보상 ① 대시 게이지 환급 — 위험(공격 예고 반경 안으로 파고듦)에 맞는 대가. 악몽 배율은 걸지 않는다.
        if (cd != null) RefundStamina(cd.perfectDodgeStaminaRefund);

        // 보상 ② 반격 창 — 슬로모가 끝날 때까지. 첫 공격은 원인 적에게 추격 돌진한다.
        _counterActive       = true;
        _counterLungePending = true;
        _counterSource       = source;

        // 발동 임팩트 — 짧은 카메라 펀치
        HitFeelService.CameraShake(0.1f, 0.12f);

        Triggered?.Invoke(freeze + duration);
        Debug.Log($"[저스트회피] 발동 ({trigger}) | 슬로모 {scale:F2}배 {duration:F1}초 · 이동 {BonusMoveMultiplier:F1}배 · 추격 대상 {(source != null ? source.name : "없음")}");
    }

    private void RefundStamina(float amount)
    {
        if (amount <= 0f) return;
        var stats = _owner.RuntimeStats;
        _owner.Stamina?.Refund(amount, stats != null ? stats.MaxStamina : 100f);
    }

    private void EndCounterWindow()
    {
        bool wasActive       = _counterActive;
        _counterActive       = false;
        _counterLungePending = false;
        _counterSource       = null;
        if (wasActive) CounterWindowEnded?.Invoke();
    }
}
