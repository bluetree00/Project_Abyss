using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진화 무기의 고유 능력 「형(形)」 정의(정적 데이터). 무형검에는 없고 진화하면 깨어난다(기획 장비개선 §4).
/// 쌓기 · 발동 수치와 단련 변주(Ⅱ · Ⅲ 각 2택)를 담는다. 런타임 상태(단련 단계 · 고른 변주)는 WeaponData가 갖는다.
/// </summary>
[CreateAssetMenu(fileName = "WeaponForm", menuName = "Game/Weapon/WeaponFormSO")]
public class WeaponFormSO : ScriptableObject
{
    /// <summary>단련 변주 하나 — 단계(2 · 3)마다 두 개 중 하나를 고른다.</summary>
    [Serializable]
    public struct VariantDef
    {
        public string id;                 // 세이브 · 코드 분기 키(예: twin_wind)
        public int    tier;               // 2 또는 3
        public string displayName;        // 쌍풍
        public string traitTag;           // 성격 칩(넓게)
        [TextArea] public string line;    // 한 줄 설명
    }

    [Header("이름")]
    [SerializeField, Tooltip("코드 분기 키 — windblade(칼바람) / weight(무게)")] private string formId;
    [SerializeField] private string displayName;
    [SerializeField, Tooltip("발동 이름 — 풍절 / 내려찍기")] private string triggerName;
    [SerializeField, TextArea] private string line;
    [SerializeField, Tooltip("형 색 — 칼바람 강철빛 / 무게 잉걸빛(재련소 FastTint · HeavyTint)")] private Color tint = Color.white;

    [Header("쌓기 · 발동 (D5 시작값 — 실측 뒤 조정)")]
    [SerializeField] private int   maxStacks = 5;
    [SerializeField, Tooltip("발동 피해 = 근접 공격력 × 이 값")] private float triggerAtkRatio = 1.5f;
    [SerializeField, Tooltip("풍절 길이 · 내려찍기 반경(m)")] private float triggerRange = 8f;
    [SerializeField, Tooltip("이 시간 동안 못 쌓으면 줄어든다(칼바람) / 비운다(무게)")] private float idleSeconds = 3f;

    [Header("단련")]
    [SerializeField, Tooltip("단련 비용(강화재료) — [0] = Ⅱ, [1] = Ⅲ")] private int[] temperCosts = { 12, 20 };
    [SerializeField] private VariantDef[] variants = Array.Empty<VariantDef>();

    [Header("연출 — 비우면 없음")]
    [SerializeField, Tooltip("발동 이펙트 Addressables 키")] private string triggerVfxKey;
    [SerializeField, Tooltip("발동 이펙트 크기(배) — 게임 카메라 높이에서 판정 범위와 맞춘다")] private float triggerVfxScale = 1f;
    [SerializeField, Tooltip("가득 찼을 때 칼날 트레일(INab Weapon Trail 프리팹)")] private GameObject fullTrailPrefab;

    public string     FormId          => formId;
    public string     DisplayName     => displayName;
    public string     TriggerName     => triggerName;
    public string     Line            => line;
    public Color      Tint            => tint;
    public int        MaxStacks       => Mathf.Max(1, maxStacks);
    public float      TriggerAtkRatio => triggerAtkRatio;
    public float      TriggerRange    => triggerRange;
    public float      IdleSeconds     => idleSeconds;
    public string     TriggerVfxKey   => triggerVfxKey;
    public float      TriggerVfxScale => triggerVfxScale > 0f ? triggerVfxScale : 1f;
    public GameObject FullTrailPrefab => fullTrailPrefab;

    /// <summary>그 단계로 벼리는 비용(2 → Ⅱ, 3 → Ⅲ). 범위 밖이면 0.</summary>
    public int TemperCost(int toTier)
    {
        int i = toTier - 2;
        return temperCosts != null && i >= 0 && i < temperCosts.Length ? temperCosts[i] : 0;
    }

    /// <summary>그 단계의 변주들(보통 2개)을 into에 채운다.</summary>
    public void GetVariants(int tier, List<VariantDef> into)
    {
        into.Clear();
        if (variants == null) return;
        for (int i = 0; i < variants.Length; i++) if (variants[i].tier == tier) into.Add(variants[i]);
    }

    public bool TryGetVariant(string id, out VariantDef v)
    {
        v = default;
        if (string.IsNullOrEmpty(id) || variants == null) return false;
        for (int i = 0; i < variants.Length; i++)
            if (variants[i].id == id) { v = variants[i]; return true; }
        return false;
    }
}
