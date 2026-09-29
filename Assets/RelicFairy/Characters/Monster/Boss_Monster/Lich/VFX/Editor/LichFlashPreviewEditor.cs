using System;
using Cysharp.Threading.Tasks;
using UnityEditor;
using RelicFairy.UI.Overlay;
using UnityEngine;

namespace RelicFairy.Monster.EditorTools
{
/// <summary>
/// [실측 도구 · 플레이 중] 리치 화면 플래시 세기 확인 — 리치가 쓰는 플래시 7종 + 비교 사다리를 차례로 띄우고 1 · 2프레임째를 게임 뷰로 찍는다
/// (오버레이 포함). 결과 Temp/lichflash_NN_이름_fK.png. 보스 없이 허브에서도 돈다(플래시는 FXLayer UI).
/// 09-20: FXLayer가 0×0이라 지금까지 한 번도 안 보인 채 정한 값을 화면으로 맞추기 위해.
/// 값은 호출처(LichMonster · LichSealRitual · LichFinalMagicPatternSO · LichPhase2EntryPatternSO)와 맞춰 둔다.
/// </summary>
public static class LichFlashPreviewEditor
{
    private const float Gap = 1.0f;

    private static readonly Color Gold   = new Color(1f, 0.9f, 0.6f);
    private static readonly Color Purple = new Color(0.7f, 0.4f, 1f);

    // 앞 7개 = 호출처 현행값, 뒤 = 비교용 사다리(같은 색을 다른 세기로).
    private static readonly (string name, Color color, float seconds, float peak)[] Presets =
    {
        ("parry",      Color.white, 0.12f, 0.15f),
        ("death",      Color.white, 0.25f, 0.35f),
        ("pageBreak",  Color.white, 0.25f, 0.20f),
        ("ritual",     Gold,        0.30f, 0.20f),
        ("f4Start",    Purple,      0.30f, 0.15f),
        ("f4Pull",     Gold,        0.30f, 0.25f),
        ("f4Fail",     Color.white, 0.20f, 0.25f),
        ("white0.10",  Color.white, 0.30f, 0.10f),
        ("gold0.15",   Gold,        0.30f, 0.15f),
        ("purple0.12", Purple,      0.30f, 0.12f),
    };

    [MenuItem("RelicFairy/Boss/Lich/Flash Preview Capture (Play)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[LichFlash] 플레이 모드에서만 동작한다."); return; }
        CaptureAsync().Forget();
    }

    private static async UniTaskVoid CaptureAsync()
    {
        try
        {
            var flash = Managers.UI?.GetOverlayUI<FXLayer>()?.FlashView;
            var group = flash != null ? flash.GetComponent<CanvasGroup>() : null;
            ScreenCapture.CaptureScreenshot("Temp/lichflash_00_none.png");
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true);
            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                LichCinematics.Flash(p.color, p.seconds, p.peak);
                // 에디터는 프레임이 길어 짧은 번쩍임(0.12초)은 2프레임째면 이미 끝난다 — 1 · 2프레임째를 찍고 그때의 실제 알파를 남긴다.
                for (int f = 0; f < 2; f++)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    ScreenCapture.CaptureScreenshot($"Temp/lichflash_{i + 1:00}_{p.name}_f{f + 1}.png");
                    Debug.Log($"[LichFlash] {p.name} f{f + 1} 알파 {(group != null ? group.alpha : -1f):F2} (정점 {p.peak:F2})");
                }
                await UniTask.Delay(TimeSpan.FromSeconds(Gap), ignoreTimeScale: true);
            }
            Debug.Log("[LichFlash] 완료 → Temp/lichflash_*.png");
        }
        catch (OperationCanceledException) { }
    }
}
}
