using UnityEngine;

/// <summary>
/// 몬스터의 플레이어 감지·추격 포기 설정 SO.
/// </summary>
[CreateAssetMenu(fileName = "MonsterDetection", menuName = "Lee/Monster/DetectionSO")]
public class MonsterDetectionSO : ScriptableObject
{
    [Tooltip("플레이어 인식 거리 (m)")]
    public float detectionRange    = 5f;

    [Tooltip("이 거리를 초과하면 추격 포기 후 배회로 복귀 (m). detectionRange 보다 크게 설정.")]
    public float chaseGiveUpRange  = 8f;
}
