using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/WeaponEffectSO")]
public class WeaponEffectSO : ScriptableObject
{
    [Header("Identification")]
    [Tooltip("Effect의 이름")]
    public string effectName;

    [Header("Prefab / Resource")]
    [Tooltip("Addressables Key 또는 Resources 경로")]
    public string prefabKey; 

    [Header("Effect Settings")]
    [Tooltip("이펙트가 자동으로 파괴될 시간 (0이면 수동 관리)")]
    public float autoDestroyTime = 3f;

    [Tooltip("Effect의 기본 스케일")]
    public Vector3 defaultScale = Vector3.one;

    [Tooltip("Effect의 기본 회전")]
    public Vector3 defaultRotation = Vector3.zero;

    [Header("Optional Parameters")]
    [Tooltip("이펙트 시작 시 사운드 재생 여부")]
    public AudioClip sfx;

    [Tooltip("Effect가 생성될 위치 오프셋 (Hand, Weapon Tip 등)")]
    public Vector3 spawnOffset = Vector3.zero;

    [Tooltip("Effect가 부모 Transform을 따라갈지 여부")]
    public bool followParent = true;

    /// <summary>
    /// Runtime에 사용할 임시 인스턴스
    /// </summary>
    [NonSerialized] public GameObject runtimeInstance;
}
