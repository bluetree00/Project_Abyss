using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

// PlayerController — 스킬 게이트 · 유물 · 패시브 · 무기 교체 · 애니 이벤트
public sealed partial class PlayerController
{
    // ── Public Methods: 스킬 ──────────────────────────────────────
    /// <summary>
    /// 캐릭터 고유 스킬 런타임을 반환한다.
    /// 무기 스킬보다 우선 적용. null이면 무기 스킬 또는 레거시 폴백 사용.
    /// </summary>
    public ISkillRuntime CreateCharacterSkillRuntime(SkillType slot) => RelicBehavior?.CreateSkillRuntime(this, slot);

    /// <summary>캐릭터 고유 스킬의 쿨다운(초). 0이면 무기 쿨다운 사용.</summary>
    public float GetCharacterSkillCooldown(SkillType slot) => RelicBehavior?.GetSkillCooldown(slot) ?? 0f;

    /// <summary>
    /// 해당 슬롯에 실제 발동 가능한 스킬이 있는가. <see cref="ActSkillState"/>의 해석 순서와 동일하게
    /// 유물(캐릭터) 런타임 → 무기 슬롯 순으로 확인한다.
    /// 빈 슬롯 입력이 스킬 상태로 진입해 진행 중인 모션만 끊는 것을 막고, HUD 잠금 표시의 근거로도 쓴다.
    /// </summary>
    public bool HasSkillInSlot(SkillType slot)
    {
        // 유물 제공 스킬(주로 Q) — 런타임이 만들어지면 보유로 본다.
        if (CreateCharacterSkillRuntime(slot) != null) return true;

        // 장비(무기) 제공 스킬 — ActSkillState.GetSkillSO와 동일 매핑(R은 skillQ를 쓴다).
        var wd = WeaponManager?.CurrentWeaponData;
        return slot switch
        {
            SkillType.E => wd?.skillE != null,
            SkillType.R => wd?.skillQ != null,
            _           => false,
        };
    }

    /// <summary>
    /// 유물 게이트가 열려 있는가(가웨인 정오 구간 등). <see cref="ActSkillStateBase"/>의 게이팅과 동일 조건 —
    /// <b>유물이 소유한 슬롯</b>에만 적용하고, 무기 스킬 슬롯은 항상 열린 것으로 본다.
    /// 쿨다운은 포함하지 않는다(쿨다운은 HUD에 별도 연출이 있다).
    /// </summary>
    private bool IsSkillGateOpen(SkillType slot)
    {
        if (RelicBehavior == null) return true;
        if (CreateCharacterSkillRuntime(slot) == null) return true;   // 유물 미소유 슬롯 → 게이팅 대상 아님
        return RelicBehavior.CanUseSkill(slot);
    }

    /// <summary>
    /// 스킬을 <paramref name="seconds"/>초(실시간) 봉인한다 — 보스 기믹(리치 악몽기 「뒤집힌 봉인술」 등).
    /// 이미 봉인 중이면 더 긴 쪽을 남긴다. 봉인 중엔 입력 단계에서 발동을 막는다(진행 중인 스킬은 끊지 않는다).
    /// 타이머는 실시간으로 흐르고 일시정지(TimeScaleArbiter Pause) 중엔 멈춘다.
    /// </summary>
    public void SealSkill(SkillType slot, float seconds)
    {
        if (seconds <= 0f) return;
        int i = (int)slot;
        _skillSealRemaining[i] = Mathf.Max(_skillSealRemaining[i], seconds);
        SkillSealed?.Invoke(slot, _skillSealRemaining[i]);
    }

    /// <summary>해당 슬롯이 봉인 중인가.</summary>
    public bool IsSkillSealed(SkillType slot) => _skillSealRemaining[(int)slot] > 0f;

    /// <summary>
    /// 지금 당장 발동 가능한가 = 보유 + 봉인 아님 + 유물 게이트 + 쿨다운.
    /// 입력 단계에서 이걸로 막지 않으면 스킬 상태에 <b>진입했다가 되돌아 나오면서</b>
    /// 진행 중이던 공격 모션만 끊긴다(ActSkillStateBase가 Enter에서 되돌리는 구조).
    /// </summary>
    private bool CanUseSkillNow(SkillType slot)
    {
        if (!HasSkillInSlot(slot)) return false;
        if (IsSkillSealed(slot)) return false;
        if (!IsSkillGateOpen(slot)) return false;
        return CooldownTracker == null || CooldownTracker.IsReady(slot);
    }

    /// <summary>봉인 타이머 진행 — 실시간, 일시정지 중엔 멈춘다. 풀리는 순간 SkillUnsealed.</summary>
    private void TickSkillSeals()
    {
        if (TimeScaleArbiter.HasRequestAtOrAbove(TimeScaleArbiter.Priority.Pause)) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < _skillSealRemaining.Length; i++)
        {
            if (_skillSealRemaining[i] <= 0f) continue;
            _skillSealRemaining[i] = Mathf.Max(0f, _skillSealRemaining[i] - dt);
            if (_skillSealRemaining[i] <= 0f) SkillUnsealed?.Invoke((SkillType)i);
        }
    }

    /// <summary>콤보가 최대 스텝까지 완료됐을 때 ActAttackState에서 호출된다.</summary>
    public void NotifyComboFinished(int finalStep)
    {
        FirePassive(PassiveTrigger.OnComboFinish,
            new PassiveContext { comboStep = finalStep });
    }

    // ── Public Methods: 유물 · 패시브 ─────────────────────────────
    /// <summary>
    /// 런타임에 선택된 유물을 주입·적용한다. 스폰(InitAsync) 이후 호출.
    /// relicClass가 SerializeField라 Instantiate 후엔 Awake가 이미 지나므로, 이 주입점으로 적용한다.
    /// null이면 무동작(CombatGirl 기본 몸 유지).
    /// </summary>
    public void SetRelicAndApply(RelicClassSO relic)
    {
        if (relic == null) return;

        // 유물은 한 번만 적용된다. 이미 적용된 뒤 필드만 바꾸면 RelicClass와 실제 적용된 유물이 어긋난다.
        if (_relicApplied)
        {
            if (relic != relicClass)
                Debug.LogWarning($"[PlayerController] 이미 유물 {relicClass?.Id} 적용됨 — {relic.Id} 적용 요청 무시");
            return;
        }

        relicClass = relic;
        ApplyRelic();
    }

    /// <summary>유물 행동 객체가 패시브를 등록할 때 쓰는 진입점.</summary>
    public void RegisterRelicPassive(ICharacterPassive passive) => _passives.Register(passive);

    /// <summary>
    /// 트리거 조건이 맞는 패시브를 모두 실행한다.
    /// 상태 클래스 및 외부에서 호출 가능.
    /// </summary>
    public void FirePassive(PassiveTrigger trigger, in PassiveContext ctx) => _passives.Fire(this, trigger, ctx);

    // ── Private Methods: 공격 · 스킬 판정 ─────────────────────────
    // 무기 장착 여부 검사 — 날아가는 중(피격 넉백)엔 어떤 공격도 불가.
    private bool CanAttack()
    {
        return WeaponManager != null && WeaponManager.HasWeapon && !IsLaunched;
    }

    /// <summary>공격·스킬 상태 여부 판별 (Safe_OnAttackAnimationEnd 내부 사용).</summary>
    private bool IsInAttackOrSkillState() =>
        _actSM.CurrentId == ActState.Attack      ||
        _actSM.CurrentId == ActState.AttackReady ||
        _actSM.CurrentId == ActState.QSkill      ||
        _actSM.CurrentId == ActState.ESkill      ||
        _actSM.CurrentId == ActState.RSkill;

    // ── Private Methods: 유물 적용 ────────────────────────────────
    /// <summary>
    /// 선택된 유물(relicClass)을 적용 — 행동 OnAttach(코드 패시브/메커닉)
    /// + 유물 스탯(유물 스탯 필드 + 데이터 패시브 PassiveSO의 스탯 보정을 공통 베이스 위 가산)
    /// + 고유스킬 클립 오버라이드 + 외형(오라). relicClass 없으면 무동작.
    /// _relicApplied 가드로 중복 적용을 방지한다(패시브/스탯 이중 적용 차단).
    /// </summary>
    private void ApplyRelic()
    {
        if (relicClass == null || _relicApplied) return;
        _relicApplied = true;

        RelicBehavior = RelicRegistry.Create(relicClass.Id);
        RelicBehavior?.OnAttach(this);

        // 유물 스탯 — 유물 char_id 행(서버)으로 전체 교체(유물이 곧 캐릭터).
        // 서버 데이터/행 없으면 유물 StatModifier 가산으로 폴백.
        string relicCharId = relicClass.Id.ToString().ToLower(); // Gawain → "gawain"
        if (!PlayerDataResolver.TryApplyServerStats(relicCharId, RuntimeStats, ref characterData))
        {
            var relicMods = new List<StatModifier>();
            if (relicClass.Stats != null) relicMods.AddRange(relicClass.Stats);
            if (relicClass.Passives != null)
                foreach (var p in relicClass.Passives)
                    if (p != null && p.baseModifiers != null) relicMods.AddRange(p.baseModifiers);
            RuntimeStats?.ApplyRelicStats(relicMods);
        }

        _relicAppearance.ApplyQAnimationAsync(relicClass, _animSvc, this).Forget();

        // 외형 — 현재는 오라 VFX만(키 있을 때). 추후 모델/애니메이터 변형은 이 지점에서 확장.
        if (!string.IsNullOrEmpty(relicClass.AuraVfxKey))
            _relicAppearance.SpawnAuraAsync(relicClass.AuraVfxKey, relicClass.AuraSocket, transform).Forget();

        Debug.Log($"[PlayerController] 유물 적용: {relicClass.Id} (char_id={relicCharId}, passives={relicClass.Passives?.Length ?? 0})");
    }

    // ── Private Methods: 무기 입력 정책 · 애니 이벤트 구독 ─────────
    private void AssignAttackPolicyForWeapon(WeaponData wd)
    {
        if (wd == null)
        {
            _attackPolicy = null;
            return;
        }

        // [강공격 봉인] 차지 임계값(holdThreshold)·단계 수(chargeStages)를 넘기던 인자는 사라졌다.
        // 두 정책은 현재 "누르고 떼면 약공격 1회"로 동일하게 동작한다 — 무기별 입력 차이가
        // 다시 생기면 이 switch가 그대로 분기점이 된다.
        switch (wd.weaponType)
        {
            case WeaponType.Bow:
            case WeaponType.Crossbow:
                _attackPolicy = new BowAttackPolicy();
                break;

            default:
                _attackPolicy = new SwordAttackPolicy();
                break;
        }
    }

    private void SubscribeToAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || _aeSubscribed) return;

        // OnAttackEnd: ActAttackState는 normalizedTime 폴링으로 자체 처리 → 스킬 상태 종료용으로만 받는다.
        // 콤보 창 열고 닫기도 ActAttackState가 폴링으로 처리한다.
        // 아래는 PlayerController가 직접 처리해야 하는 시각적/전역 이벤트만 유지.
        receiver.OnAttackEnd  += Safe_OnAttackAnimationEnd;
        receiver.OnHitStep    += Safe_OnHitStep;
        receiver.OnEffectStep += Safe_OnEffectStep;

        _aeSubscribed = true;
    }

    private void UnsubscribeFromAnimationReceiver(PlayerAnimationEventReceiver receiver)
    {
        if (receiver == null || !_aeSubscribed) return;

        receiver.OnAttackEnd  -= Safe_OnAttackAnimationEnd;
        receiver.OnHitStep    -= Safe_OnHitStep;
        receiver.OnEffectStep -= Safe_OnEffectStep;

        _aeSubscribed = false;
    }

    // ── Event Handlers: 무기 교체 ─────────────────────────────────
    private void OnWeaponChangedApplyAnimation(WeaponData newWeapon, GameObject weaponInstance)
    {
        // 이전 무기 오버라이드 원복 (로코모션 포함) — 무기 교체/해제 시 이전 클립이 남지 않도록.
        _animSvc?.ResetOverrides();

        // null 무기면 정책 제거
        if (newWeapon == null)
        {
            _attackPolicy = null;
            Debug.Log("[PlayerController] 무기 해제 - 공격 불가 상태로 전환");
            return;
        }

        if (_animSvc == null || newWeapon.animationSet == null || !Managers.AnimationResources.IsInitialized)
        {
            AssignAttackPolicyForWeapon(newWeapon);
            return;
        }

        var animSet = newWeapon.animationSet;
        foreach (var mapping in animSet.GetAllMappings())
        {
            var clip = Managers.AnimationResources.GetClip(mapping.addressableKey);
            if (clip == null) continue; // 어드레서블 미등록 — AnimationResourceManager 가 이미 경고했다.

            // Override 키는 컨트롤러 상태가 물고 있는 '원본 클립 이름'이다. baseClipName(=상태 이름)과
            // 다르거나 그런 상태가 없으면 조용히 실패해 무기 클립이 영영 적용되지 않는다.
            // 지금까지 이 실패가 묻혀 있었으므로 반드시 드러낸다.
            if (!_animSvc.Override(mapping.baseClipName, clip))
                Debug.LogWarning($"[PlayerController] 애니 오버라이드 실패 — 무기 '{newWeapon.weaponSOKey}' " +
                                 $"키 '{mapping.baseClipName}' (addressable '{mapping.addressableKey}'). " +
                                 $"컨트롤러에 그 이름의 원본 클립이 없다.");
        }

        AssignAttackPolicyForWeapon(newWeapon);
    }

    private void OnWeaponChangedApplyStats(WeaponData newWeapon, GameObject _)
    {
        if (newWeapon == null)
        {
            RuntimeStats.SetWeaponStats(0, 0, 0);
            RuntimeStats.SetWeaponMastery(0f, 0f);
            return;
        }

        var kind = newWeapon.weaponType.GetAttackStatKind();
        // 절삭하지 않고 소수 그대로 넘긴다 — 강화 배율이 여기서 잘리면 강화가 공격력에 안 닿는다.
        float melee  = kind == AttackStatKind.Melee  ? newWeapon.baseAttack : 0f;
        float ranged = kind == AttackStatKind.Ranged ? newWeapon.baseAttack : 0f;
        RuntimeStats.SetWeaponStats(melee, ranged, newWeapon.baseDefense);

        // 진화 후 추가 강화 구간(마스터리) → 스킬 확장. 테이블 미로드면 0(무보정).
        var table   = WeaponEnhanceService.Table;
        int mastery = WeaponEnhanceService.MasteryLevel(newWeapon, table);
        RuntimeStats.SetWeaponMastery(
            table != null ? table.MasterySkillDamage(mastery) : 0f,
            table != null ? table.MasterySkillCdr(mastery)    : 0f);
    }

    /// <summary>강화/승급으로 장착 무기 스탯만 갱신됐을 때 — 교체 없이 데미지 스탯만 재적용.</summary>
    private void OnEquippedWeaponRefreshedApplyStats(WeaponData weapon)
        => OnWeaponChangedApplyStats(weapon, null);

    // ── Event Handlers: 애니메이션 이벤트 ─────────────────────────
    /// <summary>
    /// AE_AttackEnd 애니메이션 이벤트 수신 — 스킬 상태 종료 전용.
    /// ActState.Attack은 상태가 자체 처리하므로 스킵한다.
    /// </summary>
    private void Safe_OnAttackAnimationEnd()
    {
        // Attack은 State가 자체적으로 종료를 처리한다
        if (_actSM.CurrentId == ActState.Attack)
            return;

        Combo.SetAttacking(false);

        if (IsInAttackOrSkillState())
            _actSM.Change(ActState.None);
    }

    private void Safe_OnHitStep(int stepIndex)
    {
        if (stepIndex < 0 || !Combo.IsAttacking) return;
        FirePassive(PassiveTrigger.OnAttackHit,
            new PassiveContext { comboStep = stepIndex });
    }

    private void Safe_OnEffectStep(int step)
    {
        if (EffectHandler != null && WeaponManager.HasWeapon)
            EffectHandler.PlayEffect(CurrentAttackTypeForEffect, Combo.CurrentComboStep, step, ActiveExecution).Forget();
    }
}
