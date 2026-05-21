using System;
using UnityEngine;

/// <summary>
/// 서약 하나의 표시 데이터 + 스테이지별 수치 배열.
/// 각 구현체는 private const int 인덱스로 배열을 참조한다.
/// </summary>
[CreateAssetMenu(fileName = "CovenantData", menuName = "RelicFairy/Covenant/Covenant Data")]
public sealed class CovenantDataSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] public string covenantId;

    [Header("Display")]
    [SerializeField] public string displayName;
    [SerializeField, TextArea(2, 3)] public string loreText;
    [SerializeField] public Sprite icon;

    [Header("Descriptions")]
    [SerializeField, TextArea(2, 4)] public string basicDescription;
    [SerializeField, TextArea(2, 4)] public string enhancedDescription;
    [SerializeField, TextArea(2, 4)] public string evolvedDescription;

    [Header("Values — Basic")]
    [SerializeField] public float[] basicValues = Array.Empty<float>();

    [Header("Values — Enhanced")]
    [SerializeField] public float[] enhancedValues = Array.Empty<float>();

    [Header("Values — Evolved")]
    [SerializeField] public float[] evolvedValues = Array.Empty<float>();

    /// <summary>
    /// 스테이지별 float 수치 반환. 배열 범위 밖이거나 배열이 없으면 fallback 반환.
    /// </summary>
    public float Get(CovenantStage stage, int index, float fallback = 0f)
    {
        var arr = stage switch
        {
            CovenantStage.Enhanced => enhancedValues,
            CovenantStage.Evolved  => evolvedValues,
            _                      => basicValues,
        };
        return arr != null && (uint)index < (uint)arr.Length ? arr[index] : fallback;
    }

    public int GetInt(CovenantStage stage, int index, int fallback = 0)
        => Mathf.RoundToInt(Get(stage, index, fallback));
}
