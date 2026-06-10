using UnityEngine;

/// <summary>
/// Q스킬 필살기 카메라 연출 설정. 원신/명조류 ultimate burst 시퀀스.
/// 어지러움 방지를 위해 카메라는 회전 없이 짧은 오프셋 + Cinemachine smooth blend 만 사용.
/// </summary>
[CreateAssetMenu(menuName = "Characters/Ultimate Cinematic Config")]
public class UltimateCinematicConfig : ScriptableObject
{
    [Header("타이밍 (real-time 초)")]
    [Tooltip("전체 연출 길이. 0.7s 권장 (짧고 임팩트).")]
    [Range(0.3f, 2f)] public float duration = 0.7f;

    [Tooltip("연출 동안 적용할 timeScale. 0.15 권장 (강한 슬로모).")]
    [Range(0.05f, 1f)] public float timeScale = 0.15f;

    [Header("카메라 (actor 기준 로컬 오프셋)")]
    [Tooltip("연출 카메라 위치 오프셋 — actor 의 right/up/forward 기준. 어지러움 방지: 작게 유지.")]
    public Vector3 cameraOffset = new Vector3(1.5f, 1.8f, -2.5f);

    [Tooltip("LookAt 타겟 오프셋 (actor 위치 + 이 값). 보통 (0,1,0) = 머리 높이.")]
    public Vector3 cameraLookOffset = new Vector3(0f, 1f, 0f);

    [Tooltip("연출 FOV. 0 이면 현재 FOV 유지 (어지러움 방지 권장).")]
    [Range(0f, 90f)] public float cameraFov = 0f;

    [Header("Letterbox (상하 검정 바)")]
    public bool useLetterbox = true;
    [Tooltip("화면 높이의 비율 (0.12 = 12%).")]
    [Range(0f, 0.3f)] public float letterboxHeightNorm = 0.12f;

    [Header("배경 Dim")]
    public bool useDim = true;
    [Range(0f, 1f)] public float dimAlpha = 0.4f;

    [Header("스킬명 텍스트")]
    [Tooltip("표시할 스킬 이름 (비우면 표시 안 함).")]
    public string skillName = "";
    public Color skillNameColor = new Color(1f, 0.92f, 0.55f, 1f);
    [Range(20, 120)] public int skillNameFontSize = 64;
}
