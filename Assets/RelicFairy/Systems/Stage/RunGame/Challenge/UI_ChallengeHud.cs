using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이벤트 전투 챌린지 HUD(소형) — 목표 + 실시간 상태(타이머/피격수) + 잠정 등급. 런타임 생성(ScreenSpaceOverlay).
/// CombatChallengeOverlay가 생성/갱신/종료. 프리팹/Addressable 불필요(코드 절차 생성, OnboardingGuideArrow 패턴).
/// </summary>
public sealed class UI_ChallengeHud : MonoBehaviour
{
    /// <summary>띠 위끝 하한(1080 기준 px) — 오른쪽 위 재화 줄(위 74 · 높이 40) 아래. 위에 두면 띠 오른쪽 끝이 재화 줄과 겹쳤다(10-01 f5 전주기 시뮬).</summary>
    private const float MinTop = 124f;

    private TextMeshProUGUI _objective;
    private TextMeshProUGUI _status;

    public static UI_ChallengeHud Create() => Create(0f);

    /// <summary><paramref name="topOffset"/>만큼 아래에 — 보스 체력바(상단)와 겹치지 않게(리치 F4).</summary>
    public static UI_ChallengeHud Create(float topOffset)
    {
        var canvasGO = new GameObject("ChallengeHudCanvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.HudIndicator;

        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var hud = canvasGO.AddComponent<UI_ChallengeHud>();

        // 월드 위에 글자만 떠 있었다 — 좌우가 흐린 어두운 띠를 깐다(보스 대사 띠와 같은 결, 09-28 UI 톤 통일).
        var band = new GameObject("Band", typeof(RectTransform)).AddComponent<Image>();
        band.transform.SetParent(canvasGO.transform, false);
        var brt = band.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
        brt.pivot     = new Vector2(0.5f, 1f);
        float top = Mathf.Max(30f + topOffset, MinTop);
        brt.anchoredPosition = new Vector2(0f, -top);
        brt.sizeDelta        = new Vector2(1300f, 110f);
        band.sprite        = UITheme.SoftBand;
        band.color         = new Color(0.02f, 0.02f, 0.04f, 0.72f);
        band.raycastTarget = false;

        hud._objective = hud.MakeText(canvasGO.transform, new Vector2(0f, -top - 14f), 30f, UIPalette.Gold);
        hud._status    = hud.MakeText(canvasGO.transform, new Vector2(0f, -top - 52f), 25f, UITheme.Ink);
        return hud;
    }

    public void SetObjective(string s) { if (_objective) _objective.text = s; }
    public void SetStatus(string s)    { if (_status) _status.text = s; }

    public void Close()
    {
        if (this != null && gameObject != null) Destroy(gameObject);
    }

    private TextMeshProUGUI MakeText(Transform parent, Vector2 anchoredPos, float size, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(900f, 44f);

        var t = go.AddComponent<TextMeshProUGUI>();
        var f = TMP_Settings.defaultFontAsset;
        if (f != null) t.font = f;
        t.fontSize          = size;
        t.alignment         = TextAlignmentOptions.Center;
        t.color             = color;
        t.raycastTarget     = false;
        t.textWrappingMode  = TextWrappingModes.NoWrap;
        TMPOutlineHelper.ApplySoftShadow(t);   // 두꺼운 검정 테두리 대신 부드러운 그림자(글자 정본 09-27)
        return t;
    }
}
