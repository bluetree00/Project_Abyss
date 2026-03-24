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
        [Range(-1f, 1f)] public float attackEndAt      = -1f;
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
