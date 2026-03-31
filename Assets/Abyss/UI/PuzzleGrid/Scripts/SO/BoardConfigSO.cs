using UnityEngine;

[CreateAssetMenu(menuName = "PuzzleGrid/Board/Board Config SO")]
public class BoardConfigSO : ScriptableObject
{
    [Tooltip("true면 시작 시 선택 화면을 보여준다. false면 initialGridAsset으로 바로 진입.")]
    public bool startInSelectionMode = true;

    [Tooltip("그리드와 셰이프에 동시 적용되는 균일 스케일.")]
    public float gameplayUniformScale = 1f;
}
