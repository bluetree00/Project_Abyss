#if UNITY_EDITOR
using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 B1 「여전히 챕터3 보스 진입 시 못 움직임」 — 보스방에 막 들어선 자리 점검.
/// 플레이어 발 · 발밑 바닥(위 5 m에서 내려 쏨) · 몸과 겹친 콜라이더 · 가장 가까운 내비 · Rigidbody 상태를 남기고,
/// 0.5초 동안 앞으로 5 m/s를 넣어 실제로 몇 m 움직였는지 잰다. 결과 로그 「[EntryStand] …」.
/// </summary>
public static class BossEntryStandProbeEditor
{
    private const float PushSpeed   = 5f;
    private const float PushSeconds = 0.5f;

    [MenuItem("RelicFairy/Debug/10-23 B1 보스방 입장 자리 점검 (Play)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[EntryStand] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    /// <summary>
    /// 지금 씬의 LODGroup 자식(LOD1 이상) 중 로컬 TRS가 아이덴티티가 아닌 것 — 원본 프리팹은 LOD 자식이 전부 아이덴티티다(LOD 자식 어긋남 함정).
    /// 콜라이더가 달린 것은 보이지 않는 벽이 된다. 결과 로그 「[LodSkew] …」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-23 B1 LOD 자식 어긋남 찾기 (Play)")]
    private static void ScanLodSkew()
    {
        int skewed = 0, withCollider = 0;
        foreach (var g in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            foreach (Transform child in g.transform)
            {
                if (!child.name.Contains("_LOD") || child.name.EndsWith("_LOD0")) continue;
                bool identity = child.localPosition.sqrMagnitude < 1e-4f && Quaternion.Angle(child.localRotation, Quaternion.identity) < 0.5f
                                && (child.localScale - Vector3.one).sqrMagnitude < 1e-4f;
                if (identity) continue;
                skewed++;
                var col = child.GetComponent<Collider>();
                if (col == null || !col.enabled) continue;
                withCollider++;
                if (withCollider <= 40)
                    Debug.Log($"[LodSkew] {PathOf(child)} · 로컬 위치 {child.localPosition} · 회전 {child.localEulerAngles} · 크기 {child.localScale} · 콜라이더 중심 {col.bounds.center}");
            }
        }
        Debug.Log($"[LodSkew] 끝 — 어긋난 LOD 자식 {skewed} · 그중 콜라이더 있음 {withCollider}");
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        for (int i = 0; i < 5 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    private static async UniTaskVoid RunAsync()
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) { Debug.LogWarning("[EntryStand] 플레이어 없음"); return; }
        Vector3 p = player.transform.position;
        int mask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
        string floor = Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 30f, mask, QueryTriggerInteraction.Ignore)
            ? $"{hit.collider.name} 윗면 y {hit.point.y:0.00}(발보다 {hit.point.y - p.y:+0.00;-0.00} m)"
            : "없음";
        string overlap = "?";
        if (player.TryGetComponent<CapsuleCollider>(out var cap))
        {
            Vector3 c = cap.transform.TransformPoint(cap.center);
            float half = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
            var hits = Physics.OverlapCapsule(c + Vector3.down * half, c + Vector3.up * half, cap.radius * 0.95f, mask, QueryTriggerInteraction.Ignore)
                .Where(h => !h.transform.IsChildOf(player.transform))
                .Select(h => $"{PathOf(h.transform)}[{h.GetType().Name}{(h is MeshCollider mc && mc.convex ? " 볼록" : "")} · 범위 {h.bounds.min:F1}~{h.bounds.max:F1}]")
                .Take(6).ToArray();
            overlap = hits.Length == 0 ? "없음" : string.Join(", ", hits);
        }
        string nav = UnityEngine.AI.NavMesh.SamplePosition(p, out var nh, 4f, UnityEngine.AI.NavMesh.AllAreas)
            ? $"{nh.position} (거리 {Vector3.Distance(p, nh.position):0.00} m)" : "4 m 안 없음";
        player.TryGetComponent<Rigidbody>(out var rb);
        var room = UnityEngine.Object.FindFirstObjectByType<BossRoomController>();
        string trigger = room != null && room.TryGetComponent<Collider>(out var tc) ? $"{tc.bounds.min:F1}~{tc.bounds.max:F1}" : "없음";
        Debug.Log($"[EntryStand] 발 {p} · 발밑 {floor} · 몸과 겹침 {overlap} · 내비 {nav} · 보스방 트리거 {trigger} · " +
                  (rb != null ? $"rb kinematic {rb.isKinematic} · constraints {rb.constraints}" : "rb 없음"));

        if (rb == null) return;
        Vector3 start = rb.position;
        Vector3 dir   = player.transform.forward; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        float t0 = Time.time;
        while (Time.time - t0 < PushSeconds)
        {
            rb.linearVelocity = new Vector3(dir.x * PushSpeed, rb.linearVelocity.y, dir.z * PushSpeed);
            await UniTask.WaitForFixedUpdate();
        }
        Vector3 moved = rb.position - start;
        Debug.Log($"[EntryStand] {PushSeconds}초 밀기 → 수평 {new Vector2(moved.x, moved.z).magnitude:0.00} m · 수직 {moved.y:+0.00;-0.00} m (기대 ≈ {PushSpeed * PushSeconds:0.0} m)");
    }
}
#endif
