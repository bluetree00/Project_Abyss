using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 타격감 피드백 유틸리티 (§3 BossDesign_Principles).
/// 히트스톱 · 카메라 쉐이크 · 화면 플래시를 제공한다.
/// 자동으로 DDOL BossImpactFeedbackHost를 생성하여 코루틴을 실행한다.
/// </summary>
public static class BossImpactFeedback
{
    private static BossImpactFeedbackHost _host;

    private static BossImpactFeedbackHost Host
    {
        get
        {
            if (_host != null) return _host;
            var existing = Object.FindFirstObjectByType<BossImpactFeedbackHost>();
            if (existing != null) { _host = existing; return _host; }
            var go = new GameObject("[BossImpactFeedback]");
            Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<BossImpactFeedbackHost>();
            return _host;
        }
    }

    // §3 약공격 0.08~0.12s / 강공격 0.15~0.25s / 전멸기 0.3~0.5s
    public static void TriggerHitStop(float duration)              => Host.DoHitStop(duration);

    // §3 진폭 0.08~0.2 / 지속 0.2~0.4s
    public static void TriggerCameraShake(float amplitude, float duration) => Host.DoShake(amplitude, duration);

    // §3 화면 플래시 — 피격 시 흰색 0.08s
    public static void TriggerScreenFlash(Color color, float duration) => Host.DoScreenFlash(color, duration);
}

public sealed class BossImpactFeedbackHost : MonoBehaviour
{
    private Coroutine _activeShake;
    private Vector3   _cameraBaseLocalPos;
    private Image     _flashImage;

    private void Awake()
    {
        var canvasGo = new GameObject("FlashCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasGo.AddComponent<CanvasScaler>();

        var imgGo = new GameObject("FlashImage");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _flashImage = imgGo.AddComponent<Image>();
        _flashImage.color         = Color.clear;
        _flashImage.raycastTarget = false;

        var rt = _flashImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    public void DoHitStop(float duration)
    {
        // Time.timeScale 수정 제거 — timeScale=0 은 Animator(Normal), StateTimer,
        // 패턴 타이머를 전부 동결시켜 GetHit 애니/경직이 비정상적으로 느려지는 버그를 유발.
        // 카메라 쉐이크만으로 충분한 임팩트 표현이 가능하다.
        DoShake(0.06f, duration);
    }

    public void DoShake(float amplitude, float duration)
    {
        // 진행 중인 쉐이크가 있으면 카메라 원위치 후 교체
        if (_activeShake != null)
        {
            StopCoroutine(_activeShake);
            var cam = Camera.main;
            if (cam != null) cam.transform.localPosition = _cameraBaseLocalPos;
        }
        _activeShake = StartCoroutine(ShakeRoutine(amplitude, duration));
    }

    public void DoScreenFlash(Color color, float duration)
    {
        if (_flashImage == null) return;
        StartCoroutine(FlashRoutine(color, duration));
    }

    private IEnumerator ShakeRoutine(float amplitude, float duration)
    {
        var cam = Camera.main;
        if (cam == null) { _activeShake = null; yield break; }

        _cameraBaseLocalPos = cam.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t        = 1f - elapsed / duration;
            float strength = amplitude * t;
            cam.transform.localPosition = _cameraBaseLocalPos + new Vector3(
                (Random.value * 2f - 1f) * strength,
                (Random.value * 2f - 1f) * strength,
                0f);
            yield return null;
        }

        cam.transform.localPosition = _cameraBaseLocalPos;
        _activeShake = null;
    }

    private IEnumerator FlashRoutine(Color color, float duration)
    {
        if (_flashImage == null) yield break;
        _flashImage.color = color;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float a = color.a * (1f - elapsed / duration);
            _flashImage.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        _flashImage.color = Color.clear;
    }
}
}
