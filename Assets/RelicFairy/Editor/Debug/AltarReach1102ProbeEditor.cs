#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q28 사용자 「기억의 제단을 좀 더 잘 열 수 있게」 실측(거점, 플레이 중) — 결과 Temp/altar_reach1102.txt · 사진 Temp/ui_shots/AR1102_*.png · 로그 「[AR1102] 끝」.
/// 제단 둘레 16방향마다 8 m 밖에서 제단 쪽으로 플레이어 몸(캡슐)을 밀어 「막히는 거리」를 재고, 그 자리에 세워 [F] 범위(_playerInRange)에 드는지 본다.
/// 귀환 자리(BaseCampBootstrapper 기본 스폰)에서 제단까지 거리도 적는다.
/// </summary>
public static class AltarReach1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int   Dirs  = 16;
    private const float Start = 8f;
    private const float Lift  = 0.35f;   // 계단 · 문턱 한 칸은 넘는다고 본다

    [MenuItem("RelicFairy/Debug/11-02 기억의 제단 다가가기 실측 (거점, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[AR1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 기억의 제단 다가가기\n");
        Directory.CreateDirectory("Temp/ui_shots");
        PlayerController p = null;
        Vector3 home = Vector3.zero;
        try
        {
            await CloseDialoguesAsync(sb);
            var altar = Object.FindFirstObjectByType<WorldAwakeningAltar>();
            p = Object.FindFirstObjectByType<PlayerController>();
            if (altar == null || p == null) { sb.AppendLine($"제단 {altar != null} · 플레이어 {p != null}"); return; }
            home = p.transform.position;
            var trig = altar.GetComponent<SphereCollider>();
            var cap  = p.GetComponent<CapsuleCollider>();
            Vector3 c = trig.transform.TransformPoint(trig.center);
            float   r = trig.radius * Mathf.Max(trig.transform.lossyScale.x, trig.transform.lossyScale.y, trig.transform.lossyScale.z);
            float capR = cap != null ? cap.radius * p.transform.lossyScale.x : 0.4f;
            float capH = cap != null ? cap.height * p.transform.lossyScale.y : 1.8f;
            float floorY = FloorY(c + Vector3.up * 2f, p.transform.position.y);
            sb.AppendLine($"[F] 범위 공 — 중심 {c} · 반경 {r:0.00} m · 바닥 높이 {floorY:0.00} · 플레이어 캡슐 반경 {capR:0.00} 높이 {capH:0.00}");
            var bc = Object.FindFirstObjectByType<BaseCampBootstrapper>();
            var sp = bc != null ? bc.GetType().GetField("returnSpawnPoint", Inst)?.GetValue(bc) as Transform : null;
            if (sp != null) sb.AppendLine($"귀환 자리 {sp.position} — 제단 중심까지 수평 {Flat(sp.position - c):0.00} m");
            sb.AppendLine($"지금 플레이어 자리 {home} — 수평 {Flat(home - c):0.00} m · 범위 안 {InRange(altar)}");

            int ok = 0;
            for (int i = 0; i < Dirs; i++)
            {
                float a = 360f / Dirs * i;
                var talk = Object.FindFirstObjectByType<UI_DialoguePopup>();
                if (talk != null && talk.isActiveAndEnabled) await CloseDialoguesAsync(sb);   // 거점 대사가 늦게 떠 시간이 멈추면 트리거가 안 돈다
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Vector3 from = new Vector3(c.x, 0f, c.z) + dir * Start;
                from.y = FloorY(from + Vector3.up * 3f, floorY);
                Vector3 b0 = from + Vector3.up * (Lift + capR), b1 = from + Vector3.up * Mathf.Max(Lift + capR, capH - capR);
                float stand = 0f;
                string by = "막힘 없음";
                if (Physics.CapsuleCast(b0, b1, capR * 0.95f, -dir, out var hit, Start, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerController>() == null)
                {
                    stand = Start - hit.distance;
                    by = hit.collider.name;
                }
                Vector3 at = new Vector3(c.x, 0f, c.z) + dir * (stand + 0.05f);
                at.y = FloorY(at + Vector3.up * 3f, from.y);
                Place(p, new Vector3(c.x, 0f, c.z) + dir * 14f + Vector3.up * from.y);
                await UniTask.Delay(250, ignoreTimeScale: true);
                Place(p, at);
                await UniTask.Delay(300, ignoreTimeScale: true);
                bool inside = InRange(altar);
                if (inside) ok++;
                // 바깥(6 m)에서 0.25 m씩 다가가며 [F]가 처음 뜨는 거리 — 막힌 곳 안쪽으로는 안 들어간다
                float firstAt = -1f;
                for (float dd = 6f; dd >= Mathf.Max(stand, 0.05f); dd -= 0.25f)
                {
                    Vector3 q = new Vector3(c.x, 0f, c.z) + dir * dd;
                    q.y = FloorY(q + Vector3.up * 3f, from.y);
                    Place(p, q);
                    await UniTask.Delay(120, ignoreTimeScale: true);
                    if (InRange(altar)) { firstAt = dd; break; }
                }
                Place(p, new Vector3(c.x, 0f, c.z) + dir * 14f + Vector3.up * from.y);
                await UniTask.Delay(150, ignoreTimeScale: true);
                sb.AppendLine($"  {a,5:0}° 막히는 곳 {stand:0.00} m({by}) · 바닥 {at.y:0.00} · [F] {(inside ? "뜸" : "안 뜸")} · 다가갈 때 처음 뜨는 거리 {(firstAt > 0f ? firstAt.ToString("0.00") + " m" : "—")}");
                if (i == 0) ScreenCapture.CaptureScreenshot("Temp/ui_shots/AR1102_front.png");
            }
            sb.AppendLine($"→ 16방향 중 [F] 뜸 {ok}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (p != null) Place(p, home);
            File.WriteAllText("Temp/altar_reach1102.txt", sb.ToString());
            Debug.Log("[AR1102] 끝 → Temp/altar_reach1102.txt");
        }
    }

    /// <summary>대사창이 떠 있으면(시간 정지 · 입력 막힘) 끝까지 넘긴다 — 11-02 첫 실측이 멀린 대사 중에 재어 무효였다.</summary>
    private static async UniTask CloseDialoguesAsync(StringBuilder sb)
    {
        int n = 0;
        float quiet = 0f;
        for (float t = 0f; t < 20f && quiet < 1.5f; t += 0.15f)
        {
            var popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
            if (popup == null || !popup.isActiveAndEnabled) quiet += 0.15f;
            else
            {
                quiet = 0f;
                (new SerializedObject(popup).FindProperty("advanceButton").objectReferenceValue as UnityEngine.UI.Button)?.onClick.Invoke();
                n++;
            }
            await UniTask.Delay(150, ignoreTimeScale: true);
        }
        sb.AppendLine($"대사 넘김 {n} · 시간 배율 {Time.timeScale:0.00} · 입력 막힘 {UIInputGate.Blocked}");
    }

    private static bool InRange(WorldAwakeningAltar a) => typeof(WorldAwakeningAltar).GetField("_playerInRange", Inst)?.GetValue(a) is bool b && b;

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    private static float FloorY(Vector3 above, float fallback)
        => Physics.Raycast(above, Vector3.down, out var h, 8f, ~0, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<PlayerController>() == null ? h.point.y : fallback;

    private static void Place(PlayerController p, Vector3 at)
    {
        p.transform.position = at;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
}
#endif
