using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponAbilitySO (Steps Only)")]
public class WeaponAbilitySO : ScriptableObject
{
    [Header("Ability Steps")]
    [Tooltip("애니메이션 이벤트의 stepIndex에 매칭되는 수치 세트")]
    public List<AbilityStep> steps = new List<AbilityStep>();

    /// <summary>
    /// 해당 stepIndex에 매칭되는 스텝 리스트 반환
    /// </summary>
    public List<AbilityStep> GetSteps(int stepIndex)
    {
        if (steps == null || steps.Count == 0) return new List<AbilityStep>();
        return steps.Where(s => s.stepIndex == stepIndex).OrderBy(s => s.order).ToList();
    }

    public void ResetOneShots()
    {
        if (steps == null) return;
        foreach (var s in steps) s.triggeredThisActivation = false;
    }

    [Serializable]
    public class AbilityStep
    {
        [Header("Step Identity")]
        [Tooltip("애니메이션 이벤트에서 전달되는 인덱스")]
        public int stepIndex = 0;

        [Tooltip("동일 stepIndex에서 실행 순서")]
        public int order = 0;

        [Space(5)]
        [Header("Damage & Impact Values")]
        [Tooltip("기본 피해량")]
        public float baseDamage = 0f;

        [Tooltip("넉백 강도 배수")]
        public float knockbackMultiplier = 1f;

        [Tooltip("스킬 사용 시 자기 자신 이동 벡터 (루트모션 보정용)")]
        public Vector3 selfMovement = Vector3.zero;

        [Space(5)]
        [Header("Effect / Visual Multipliers")]
        [Tooltip("이펙트 크기 배수")]
        public float effectScaleMultiplier = 1f;

        [Tooltip("이펙트 지속 시간 배수")]
        public float effectLifeMultiplier = 1f;

        [Space(3)]
        [Header("Collider Multipliers")]
        [Tooltip("콜라이더 크기 배수")]
        public float colliderSizeMultiplier = 1f;

        [Tooltip("콜라이더 지속 시간 배수")]
        public float colliderDurationMultiplier = 1f;

        [Space(5)]
        [Header("Payload & Meta")]
        [Tooltip("이펙트 / 콜라이더 Lookup 키 값")]
        public string payloadKey;

        [Tooltip("해당 스텝 지속 시간")]
        public float duration = 0.2f;

        [Tooltip("FixedUpdate에서 실행할지 여부")]
        public bool executeInFixedUpdate = false;

        [Tooltip("한번만 실행되는 스텝 여부")]
        public bool oneShot = false;

        [NonSerialized] public bool triggeredThisActivation = false;
    }
}
