using UnityEngine;

/// <summary>
/// 룸 클리어 후 등장하는 포탈에 부착되는 트리거.
/// 플레이어가 트리거에 진입하면 StageMap 씬으로 복귀한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoomExitTrigger : MonoBehaviour
{
    private bool _triggered;

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (!IsPlayer(other)) return;

        _triggered = true;

        var app = AppBootstrapper.Instance;
        if (app == null)
        {
            Debug.LogError("[RoomExit] AppBootstrapper 없음 — 씬 전환 실패");
            return;
        }

        Debug.Log("[RoomExit] 포탈 진입 — StageMap 복귀");
        app.RequestLoad(Define.Scene.StageMap);
    }

    private static bool IsPlayer(Collider col)
    {
        if (col == null) return false;
        if (col.CompareTag("Player")) return true;
        return col.GetComponentInParent<PlayerController>() != null;
    }
}
