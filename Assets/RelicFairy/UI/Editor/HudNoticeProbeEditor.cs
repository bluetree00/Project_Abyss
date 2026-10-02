#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 「패시브 텍스트의 가시성」 실측 — 왼쪽 발동 알림 세 줄을 띄운다(실제 게임 문구 형식).
/// 이어서 「10-02 HUD 겹침 덤프만 (런 중)」을 누르면 Temp/ui_shots/Hud_Now.png · Temp/hud_overlap_dump_Now.tsv로 겹침을 잰다.
/// </summary>
public static class HudNoticeProbeEditor
{
    [MenuItem("RelicFairy/UI/10-02 발동 알림 세 줄 띄우기 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        ItemEffectVfxHelper.ShowNotice("<color=#FFDD55>추가 타격</color> 220 → Orc");
        ItemEffectVfxHelper.ShowNotice("<color=#FFB466>강화재료 +2</color>  (재련소 연료)");
        ItemEffectVfxHelper.ShowNotice("<color=#E05A4F>질주 계열</color> 각인 2단계 — 이동 속도 +10%");
        Debug.Log("[HudNotice] 세 줄 띄움");
    }
}
#endif
