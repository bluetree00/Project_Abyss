using Cinemachine;
using UnityEngine;

/// <summary>
/// 플레이어 시점 사용자 조정(<see cref="CameraUserSettings"/>)을 게임플레이 FreeLook에 얹는 확장.
/// Body 단계(궤도가 카메라 자리를 정한 직후)에서 플레이어 기준 오프셋의 위쪽 성분에 높이 배율, 전체에 거리 배율을 곱한다 —
/// 다음 Aim 단계가 그 자리에서 다시 플레이어를 본다. 궤도 값을 쓰는 곳(구역 · 보스 · 아레나 39곳)을 건드리지 않고 마지막에 한 번만 조정한다.
/// 두 배율이 1이면 아무것도 하지 않는다(기존 거동과 같다). <see cref="GameCameraController"/>가 FreeLook을 찾을 때 붙인다.
/// </summary>
[AddComponentMenu("")]
public sealed class CameraUserFraming : CinemachineExtension
{
    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
        ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body) return;
        float dist = CameraUserSettings.Distance, height = CameraUserSettings.Height;
        if (Mathf.Approximately(dist, 1f) && Mathf.Approximately(height, 1f)) return;
        var follow = vcam.Follow;
        if (follow == null) return;

        Vector3 pivot = follow.position;
        Vector3 off   = state.RawPosition - pivot;
        off.y *= height;
        state.RawPosition = pivot + off * dist;
    }
}
