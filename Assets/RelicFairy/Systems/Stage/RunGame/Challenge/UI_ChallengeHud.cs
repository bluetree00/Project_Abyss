using TMPro;
using UnityEngine;

/// <summary>
/// 이벤트 전투 챌린지 HUD(소형) — 목표 + 실시간 상태(타이머/피격수) + 잠정 등급. 런타임 생성(ScreenSpaceOverlay).
/// CombatChallengeOverlay가 생성/갱신/종료. 프리팹/Addressable 불필요(코드 절차 생성, OnboardingGuideArrow 패턴).
/// </summary>
public sealed class UI_ChallengeHud : MonoBehaviour
{
    private TextMeshProUGUI _objective;
    private TextMeshProUGUI _status;

    public static UI_ChallengeHud Create()
    {
        var canvasGO = new GameObject("ChallengeHudCanvas", typeof(Canvas));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;

        var hud = canvasGO.AddComponent<UI_ChallengeHud>();
        hud._objective = hud.MakeText(canvasGO.transform, new Vector2(0f, -44f), 30f, new Color(1f, 0.9f, 0.5f));
        hud._status    = hud.MakeText(canvasGO.transform, new Vector2(0f, -82f), 25f, Color.white);
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
        TMPOutlineHelper.ApplyDefault(t);
        return t;
    }
}
