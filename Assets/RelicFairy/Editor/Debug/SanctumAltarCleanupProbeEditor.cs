#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 성소 서약 제단 뒷정리 확인(5회차 재확인 — 안 쓴 성소 제단이 같은 앵커에 나중에 지어진 리치 아레나에 「[F] 서약 조립」으로 남았다).
/// 임시 방 루트 아래에서 CV 토큰 처리기(CovenantAltarHandler)를 돌려 제단을 세우고 → 제단이 방 루트에 묶였는지 → 방 루트를 지우면 제단도 사라지는지.
/// 결과: Temp/sanctum_altar_probe.txt · 로그 「[SanctumAltar] 끝」. 플레이 중(테스트 허브 — 런이 없어 첫 서약 안내는 안 뜬다).
/// </summary>
public static class SanctumAltarCleanupProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-14 성소 서약 제단 뒷정리 확인 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[SanctumAltar] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("성소 서약 제단 뒷정리 확인\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        GameObject room = null;
        try
        {
            room = new GameObject("@SanctumProbeRoom");
            room.transform.position = new Vector3(0f, 0f, 5000f);
            var ctx = new TokenContext
            {
                RawToken = "CV",
                Cell     = new Vector2Int(7, 9),
                WorldPos = new Vector3(3f, 0f, 5004f),
                BaseY    = 0f,
                Parent   = room.transform,
            };
            new CovenantAltarHandler().Execute(ctx);
            await UniTask.DelayFrame(2);

            var altar = GameObject.Find("CovenantAltar_7_9");
            Check("제단이 세워짐", altar != null);
            Check($"제단이 방 루트에 묶임(부모 = {(altar != null && altar.transform.parent != null ? altar.transform.parent.name : "없음")})",
                  altar != null && altar.transform.parent == room.transform);
            Check("자리는 토큰 자리 그대로", altar != null && Vector3.Distance(altar.transform.position, new Vector3(3f, 0f, 5004f)) < 0.01f);

            Object.Destroy(room);
            room = null;
            await UniTask.DelayFrame(2);
            Check("방을 지우면 제단도 사라짐(같은 앵커의 다음 방에 안 남음)", GameObject.Find("CovenantAltar_7_9") == null);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (room != null) Object.Destroy(room);
            var left = GameObject.Find("CovenantAltar_7_9");
            if (left != null) Object.Destroy(left);   // 고치기 전(루트로 남는 판)에도 흔적을 남기지 않는다
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/sanctum_altar_probe.txt", sb.ToString());
            Debug.Log("[SanctumAltar] 끝\n" + sb);
        }
    }
}
#endif
