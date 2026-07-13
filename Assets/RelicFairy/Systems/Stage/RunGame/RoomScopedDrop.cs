using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 스코프 드랍 마커 — 골드 코인 · 클리어 보상 · 버린 아이템/무기 등.
///
/// 이 오브젝트들은 부모 없이(씬 루트) 스폰되므로 방 GameObject를 파괴해도 살아남아
/// <b>다음 방에 이전 방의 흔적으로 떠다니는</b> 문제가 있었다.
/// 스폰 시 이 컴포넌트를 붙여두면 방 전환에서 <see cref="ClearAll"/> 한 번으로 일괄 정리된다.
///
/// 정적 레지스트리(OnEnable/OnDisable 자동 등록)라 FindObjectsOfType 없이 O(1)로 추적한다.
/// </summary>
public sealed class RoomScopedDrop : MonoBehaviour
{
    private static readonly List<RoomScopedDrop> _all = new();

    private void OnEnable()  => _all.Add(this);
    private void OnDisable() => _all.Remove(this);

    /// <summary>대상에 마커를 붙인다(중복 부착 방지).</summary>
    public static void Mark(GameObject go)
    {
        if (go == null) return;
        if (!go.TryGetComponent<RoomScopedDrop>(out _))
            go.AddComponent<RoomScopedDrop>();
    }

    /// <summary>남아있는 방 스코프 드랍을 전부 제거. 방 전환(이전 방 파괴) 시 호출.</summary>
    public static void ClearAll()
    {
        if (_all.Count == 0) return;

        int n = _all.Count;
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            var d = _all[i];
            if (d != null) Destroy(d.gameObject);
        }
        _all.Clear();

        Debug.Log($"[RoomScopedDrop] 이전 방 드랍 {n}개 정리");
    }
}
