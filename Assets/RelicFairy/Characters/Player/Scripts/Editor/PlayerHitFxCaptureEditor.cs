using System;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 피격 등급 연출 캡처 — 피격 전 1장 + 약·중·강을 한 번씩 맞고 2프레임 뒤(번쩍임·비네트가
/// 가장 진한 순간)를 게임 뷰로 찍는다(HUD 오버레이 포함). 결과 Temp/hitfx_0_none.png · hitfx_1_Light.png ….
/// 등급 사이엔 HP를 되돌리고 1.2초 쉰다. 공격자는 플레이어 정면 2m의 빈 오브젝트(방향형 흔들림용).
/// </summary>
public static class PlayerHitFxCaptureEditor
{
    private const float Gap = 1.2f;

    [MenuItem("RelicFairy/Debug/피격 등급 연출 캡처 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[HitFxCapture] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[HitFxCapture] 런 플레이어가 없다."); return; }
        CaptureAsync(p).Forget();
    }

    private static async UniTaskVoid CaptureAsync(PlayerController p)
    {
        var attacker = new GameObject("~HitFxCaptureAttacker");
        attacker.transform.position = p.transform.position + p.transform.forward * 2f;
        int hp0 = p.RuntimeStats.Hp;
        try
        {
            ScreenCapture.CaptureScreenshot("Temp/hitfx_0_none.png");
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true);

            // 진단 — 비네트·줌을 그리는 FXLayer와 그 위 HitFxPresenter(구독자)가 실제로 켜져 있는가.
            var fx        = Managers.UI?.GetOverlayUI<RelicFairy.UI.Overlay.FXLayer>();
            var presenter = fx != null ? fx.GetComponentInChildren<HitFxPresenter>(true) : null;
            var vignette  = fx != null ? fx.GetComponentInChildren<RelicFairy.UI.Overlay.DamageVignetteView>(true) : null;
            var vImage    = vignette != null ? vignette.GetComponent<UnityEngine.UI.Image>() : null;
            Debug.Log($"[HitFxCapture] FXLayer {(fx != null ? (fx.gameObject.activeInHierarchy ? "켜짐" : "꺼짐") : "없음")} · " +
                      $"HitFxPresenter {(presenter != null ? (presenter.isActiveAndEnabled ? "켜짐" : "꺼짐") : "없음")} · " +
                      $"비네트 이미지 {(vImage != null ? (vImage.isActiveAndEnabled ? "켜짐" : "꺼짐") : "없음")} · " +
                      $"FXLayer 크기 {(fx != null ? ((RectTransform)fx.transform).rect.size.ToString() : "-")} · " +
                      $"FXLayer 스케일 {(fx != null ? fx.transform.localScale.ToString() : "-")} · " +
                      $"비네트 크기 {(vImage != null ? vImage.rectTransform.rect.size.ToString() : "-")}");

            int i = 1;
            foreach (var w in new[] { HitWeight.Light, HitWeight.Medium, HitWeight.Heavy })
            {
                p.TakeDamage(1, attacker, false, w);
                // 비네트는 지속시간의 20% 지점이 정점 — 12프레임 동안 알파 최대치를 재고, 5프레임째를 찍는다.
                float maxA = 0f;
                for (int f = 0; f < 12; f++)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    if (vImage != null) maxA = Mathf.Max(maxA, vImage.color.a);
                    if (f == 4) ScreenCapture.CaptureScreenshot($"Temp/hitfx_{i}_{w}.png");
                }
                Debug.Log($"[HitFxCapture] {w}: 비네트 알파 최대 {maxA:F2}");
                i++;
                await UniTask.Delay(TimeSpan.FromSeconds(Gap), ignoreTimeScale: true);
                p.RuntimeStats.SetHp(hp0);
            }
            // 빈사 경고(LowHpVolumeService) — 후처리라 UI 비네트와 달리 HUD 아래에 깔린다.
            // 꺼진 화면을 바로 앞에서 한 장 찍어 둔다(같은 장면 A/B 대조용 — 조명이 변하는 씬이라 이전 캡처와는 비교가 안 된다).
            ScreenCapture.CaptureScreenshot("Temp/hitfx_lowhp_off.png");
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), ignoreTimeScale: true);

            foreach (float hpRatio in new[] { 0.25f, 0.12f, 0.02f })
            {
                p.RuntimeStats.SetHp(Mathf.Max(1, Mathf.RoundToInt(p.RuntimeStats.MaxHp * hpRatio)));
                await UniTask.Delay(TimeSpan.FromSeconds(1.2f), ignoreTimeScale: true);
                Debug.Log($"[HitFxCapture] 빈사 경고 체력 {hpRatio:P0} · 세기 {LowHpVolumeService.CurrentIntensity:F3}");
                ScreenCapture.CaptureScreenshot($"Temp/hitfx_lowhp_{Mathf.RoundToInt(hpRatio * 100f)}.png");
                await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true);
            }
            p.RuntimeStats.SetHp(hp0);

            // 속도선(집중선) 세기 대조 — 강 피격 0.18과 저스트 회피 반격 대시 0.6을 같은 장면에서 찍는다.
            // 0.6은 빛살이 흰 고리로만 보이던 시절에 정한 값이라 화면에서 확인된 적이 없다(09-21).
            if (fx != null)
            {
                foreach (var (label, intensity, duration) in new[] { ("강피격", 0.18f, 0.16f), ("저스트회피", 0.6f, 0.22f) })
                {
                    fx.ZoomIn(intensity, duration);
                    for (int f = 0; f < 3; f++) await UniTask.Yield(PlayerLoopTiming.Update);
                    ScreenCapture.CaptureScreenshot($"Temp/hitfx_zoom_{label}.png");
                    await UniTask.Delay(TimeSpan.FromSeconds(Gap), ignoreTimeScale: true);
                }
                Debug.Log("[HitFxCapture] 속도선 대조 → Temp/hitfx_zoom_강피격.png · hitfx_zoom_저스트회피.png");
            }

            Debug.Log("[HitFxCapture] 완료 → Temp/hitfx_*.png · hitfx_lowhp_*.png(빈사 경고) · hitfx_zoom_*.png(속도선)");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (attacker != null) UnityEngine.Object.Destroy(attacker);
        }
    }
}
