using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponAnimationSetSO")]
public class WeaponAnimationSetSO : ScriptableObject
{

    [Serializable]
    public class ClipMapping
    {
        public string baseClipName;      // Animator Controller 상태 이름
        public string addressableKey;    // Addressables Key
        public WeaponActionType actionType;
        public int comboIndex = 0;       // 콤보 인덱스

        [Header("낙하 공격")]
        [Tooltip("체크 시 이 공격은 낙하 공격으로 동작합니다.")]
        public bool isPlunge = false;
        [Tooltip("낙하 속도. 0이면 기본값(ActPlungeState.DefaultPlungeSpeed) 사용")]
        public float plungeFallSpeed = 0f;
        [Range(0f, 1f), Tooltip("하강 시작 normalizedTime. 이 시점 전까지는 공중에 정지 (0 = 즉시 하강)")]
        public float plungeDescendAt = 0f;

        [Header("콤보 타이밍 Override (0~1 normalized, -1 = AnimSet 기본값 사용)")]
        [Range(-1f, 1f)] public float comboWindowOpen  = -1f;
        [Range(-1f, 1f)] public float comboWindowClose = -1f;
        [Tooltip("다음 콤보가 발사되는 '체인 지점'. 회수 동작의 끝이 아니다 — 회수는 recoveryEndAt 로 따로 지정한다.")]
        [Range(-1f, 1f)] public float attackEndAt      = -1f;
        [Tooltip("회수(recovery) 종료 시점. attackEndAt 보다 뒤여야 유효. 후속 입력이 없을 때만 여기까지 클립을 마저 재생한다. " +
                 "어떤 입력이든 들어오면 즉시 취소되므로 콤보 반응성은 변하지 않는다. -1 = 회수 없음(종전 동작).")]
        [Range(-1f, 1f)] public float recoveryEndAt    = -1f;

        [Header("공격 중 전진 (Lunge Step)")]
        [Tooltip("이 콤보 단계에서 전진할 거리(m). 0 이면 전진 없음.")]
        [Range(0f, 4f)] public float attackStepDistance  = 0f;
        [Tooltip("전진 시작 normalizedTime. 회전 보정 직후가 적절(0.10).")]
        [Range(0f, 1f)] public float attackStepStartNorm = 0.10f;
        [Tooltip("전진 종료 normalizedTime. 실제 HitStep 이전이 적절(0.40).")]
        [Range(0f, 1f)] public float attackStepEndNorm   = 0.40f;
        [Tooltip("유도(Aim Assist) 회전이 목표에 정렬 완료된 순간 추가로 전진할 거리(m). 0 이면 보너스 없음. 정면 적/벽 앞에서는 캡되어 멈춤(관통 안 함).")]
        [Range(0f, 3f)] public float aimCompleteStepBonus = 0f;
        [Tooltip("유도 돌진 최대 거리(m). >0 이면 정면(콘+이 사거리) 안의 적을 향해 이 거리까지 전진(적 앞에서 정지). base(attackStepDistance)와 분리된 '전진 지능' 사거리. 0 이면 기존(base+1.0) 부스트 사용.")]
        [Range(0f, 6f)] public float lungeMaxRange = 0f;

        [Header("공격 중 입력 반영")]
        [Tooltip("공격 중 이동 입력을 어느 정도 반영할지 (0=완전 정지, 1=평소). 무기/콤보별 기동성 조절.")]
        [Range(0f, 1f)] public float moveInputScale = 0f;

        [Header("공격 시작 시 유도 보정 (Aim Assist)")]
        [Tooltip("체크 시 마우스 방향 콘 안에서 가장 근접한 적 쪽으로 살짝 회전 보정.")]
        public bool useAimAssist = true;
        [Tooltip("적 탐색 거리(m). 무기 사거리 ±α 권장.")]
        [Range(0f, 15f)] public float aimAssistRadius = 6f;
        [Tooltip("적 탐색 콘 반각(도). 좁을수록 의도 존중.")]
        [Range(0f, 90f)] public float aimAssistConeHalfAngle = 40f;
        [Tooltip("마우스 방향과 적 방향의 블렌드 비율 (0=마우스, 1=적). 1.0 = 콘 안 적을 정면으로 풀스냅 (lerp 와 결합 시 부드러우면서도 정면 정렬).")]
        [Range(0f, 1f)] public float aimAssistStrength = 1.0f;
        [Tooltip("회전이 도달까지 걸리는 시간(초). 0이면 즉시 snap, >0이면 lerp. 0.08~0.12 권장.")]
        [Range(0f, 0.3f)] public float aimRotationDuration = 0.10f;
    }

    [Serializable]
    public class AnimGroupMapping
    {
        public WeaponAnimGroup groupType;
        public List<ClipMapping> clipMappings = new List<ClipMapping>();

        // 그룹 안에서 액션 타입별 조회
        public IEnumerable<ClipMapping> GetMappingsForAction(WeaponActionType action)
        {
            foreach (var m in clipMappings)
                if (m.actionType == action) yield return m;
        }
    }

    [Header("가드 차지 시스템")]
    [Tooltip("체크 시 강공격 차지 중 피격을 Guard Accept로 처리합니다 (Greatsword 전용).")]
    public bool useGuardCharge = false;

    [Header("애니메이션 속도 배율 (Animator.speed = 이 값 × 아이템 공격속도)")]
    [Range(0.1f, 3f)] public float lightAttackAnimSpeed  = 1.0f;
    [Range(0.1f, 3f)] public float heavyAttackAnimSpeed  = 1.0f;

    [Header("콤보 타이밍 기본값 (ClipMapping override가 -1일 때 사용)")]
    [Range(0f, 1f)] public float defaultComboWindowOpen  = 0.25f;
    [Range(0f, 1f)] public float defaultComboWindowClose = 0.75f;
    [Range(0f, 1f)] public float defaultAttackEndAt      = 0.85f;

    // Ground / Air 그룹 리스트
    public List<AnimGroupMapping> animGroups = new List<AnimGroupMapping>();

    // 그룹과 액션으로 조회
    public IEnumerable<ClipMapping> GetMappings(WeaponAnimGroup group, WeaponActionType action)
    {
        var grp = animGroups.Find(g => g.groupType == group);
        if (grp != null)
            return grp.GetMappingsForAction(action);
        return new List<ClipMapping>();
    }

    // 모든 매핑 반환 (모든 그룹 포함)
    public IEnumerable<ClipMapping> GetAllMappings()
    {
        foreach (var g in animGroups)
            foreach (var m in g.clipMappings)
                yield return m;
    }
}
