#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 슬롯 삭제 뒤 인트로 확인(사용자 「삭제해도 인트로를 건너뛴다」) — 사용자 슬롯(0~2)은 안 건드리고 가상 슬롯 99로 돈다.
/// 프롤로그를 끝낸 슬롯 → 슬롯 삭제(ResetSlot) → 로비 카드 새로고침(RepairHubRecord) 순서를 재현해:
/// 인트로 완료 플래그가 지워지는가 · 지운 슬롯에 허브 기록이 되살아나지 않는가(되살아나면 이어하기 → 거점으로 가서 인트로를 건너뛴다).
/// 결과: Temp/slot_delete_intro_probe.txt · 로그 「[SlotDeleteIntro] 끝」. 플레이 중(로비 · 허브 어디서나).
/// </summary>
public static class SlotDeleteIntroProbeEditor
{
    private const int TestSlot = 99;

    [MenuItem("RelicFairy/Debug/10-14 슬롯 삭제 뒤 인트로 확인 (플레이 중 · 가상 슬롯 99)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[SlotDeleteIntro] 플레이 모드에서"); return; }
        var sb = new StringBuilder("슬롯 삭제 뒤 인트로 확인(가상 슬롯 99)\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var rpm = RunProgressManager.Instance;
        try
        {
            if (rpm == null) { sb.AppendLine("런 진행 관리자 없음"); return; }
            if (rpm.HasLocalRun(TestSlot)) { sb.AppendLine("가상 슬롯 99에 기록이 이미 있음 — 멈춤"); return; }

            // 프롤로그를 끝낸 슬롯(이어하기면 거점으로 가는 상태)
            PlayerPrefs.SetInt("game_intro_done_v1_slot" + TestSlot, 1);
            Check("준비: 프롤로그 완료로 표시", IntroCompletionTracker.IsCompletedForSlot(TestSlot));

            // 슬롯 삭제 → 로비 카드 새로고침
            rpm.ResetSlot(TestSlot);
            Check("삭제 뒤 인트로 완료 플래그 지워짐", !IntroCompletionTracker.IsCompletedForSlot(TestSlot));
            rpm.RepairHubRecord(TestSlot);
            bool revived = rpm.HasLocalRun(TestSlot);
            Check("카드 새로고침 뒤 지운 슬롯이 되살아나지 않음(= 새 게임 → 인트로부터)", !revived);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            // 뒷정리 — 가상 슬롯의 흔적을 남기지 않는다
            if (rpm != null) rpm.ResetSlot(TestSlot);
            PlayerPrefs.DeleteKey("game_intro_done_v1_slot" + TestSlot);
            PlayerPrefs.Save();
            sb.AppendLine($"뒷정리 뒤 가상 슬롯 기록 남음: {(rpm != null && rpm.HasLocalRun(TestSlot))}");
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/slot_delete_intro_probe.txt", sb.ToString());
            Debug.Log("[SlotDeleteIntro] 끝\n" + sb);
        }
    }
}
#endif
