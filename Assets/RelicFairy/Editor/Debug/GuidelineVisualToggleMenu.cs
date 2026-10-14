#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 개발용 가이드라인 표시(<see cref="GuidelineVisual"/> — 시너지 구체 · 「CRIT」 · 연쇄 선 · 장판 원반) 켜기 · 끄기.
/// 10-11 이펙트 톤 2차(D-1)부터 에디터 · 개발 빌드에서도 기본 OFF다 — 효과 판정을 눈으로 볼 때만 켠다.
/// </summary>
public static class GuidelineVisualToggleMenu
{
    [MenuItem("RelicFairy/Debug/가이드라인 표시 켜기·끄기 (플레이 중)")]
    private static void Toggle()
    {
        GuidelineVisual.Toggle();
        Debug.Log($"[GuidelineVisual] 개발용 가이드라인 표시 {(GuidelineVisual.Enabled ? "켬" : "끔")}");
    }
}
#endif
