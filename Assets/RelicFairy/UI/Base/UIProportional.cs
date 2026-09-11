using TMPro;
using UnityEngine;

/// <summary>
/// 창을 키우면 <b>내용이 비율 그대로 따라오게</b> 만드는 공용 도구.
///
/// <para><b>무엇이 문제였나</b> — 화면들은 목업 px를 그대로 박은 <b>점 앵커</b>로 지어졌다.
/// 점 앵커는 부모의 한 점에 자식을 매다는 것이라, 부모가 커져도 자식은 원래 크기로
/// 좌상단에 붙어 있다. 그래서 배경만 넓어지고 내용은 안 따라와 자리가 통째로 비었다.</para>
///
/// <para><b>어떻게 고치나</b> — 자식을 부모에 대한 <b>비율(스트레치) 앵커</b>로 굳힌다.
/// 목업 좌표계는 창이 정확히 목업 크기이므로 그 자체가 이미 비율계다 —
/// 나누기 한 번이면 근사 없이 <b>같은 자리</b>가 나온다. 이후로는 창 rect 하나만
/// 인스펙터에서 끌어도 내용 전체가 비율을 지킨 채 따라온다.</para>
///
/// <para><b>글자는 따로 손봐야 한다</b> — 앵커는 상자를 늘릴 뿐 글꼴 크기를 바꾸지 않는다.
/// 창을 20% 키우면 상자는 20% 커지는데 글자는 그대로라 상대적으로 작아 보인다.
/// <see cref="ScaleFonts"/>가 같은 배율을 글꼴에도 먹인다.</para>
/// </summary>
public static class UIProportional
{
    /// <summary>
    /// 목업 좌상단 기준 사각형을 부모(<paramref name="pw"/>×<paramref name="ph"/>) 기준
    /// 비율 앵커로 굳힌다. 목업 Y는 아래로, uGUI 앵커 Y는 위로 자라므로 뒤집는다.
    /// </summary>
    public static void Place(RectTransform rt, float mx, float my, float mw, float mh, float pw, float ph)
    {
        if (rt == null || pw <= 0f || ph <= 0f) return;

        rt.anchorMin = new Vector2(mx / pw,        1f - (my + mh) / ph);
        rt.anchorMax = new Vector2((mx + mw) / pw, 1f - my / ph);
        // 피벗은 건드리지 않는다 — sizeDelta·anchoredPosition이 0이면 피벗과 무관하게
        // rect가 앵커 사각형과 정확히 같아진다. 기존 확대/회전 연출의 기준점을 지켜준다.
        rt.sizeDelta        = Vector2.zero;   // 크기는 이제 앵커가 정한다
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// 창을 화면(부모) 안에서 <b>최대로</b> 키운다. <paramref name="aspect"/>(가로÷세로)는 유지한다.
    /// 부모 크기를 아직 못 읽으면 <paramref name="fallbackH"/>를 쓴다.
    /// </summary>
    /// <returns>목업 기준 대비 실제 배율. 글꼴에 그대로 먹이면 된다.</returns>
    public static float FitWindow(RectTransform window, float aspect, float mockH,
                                  float margin = 0.94f, float fallbackH = 0f)
    {
        if (window == null || aspect <= 0f || mockH <= 0f) return 1f;

        float h = fallbackH > 0f ? fallbackH : mockH;
        if (window.parent is RectTransform area && area.rect.width > 1f && area.rect.height > 1f)
            h = Mathf.Min(area.rect.height * margin, area.rect.width * margin / aspect);

        window.sizeDelta = new Vector2(h * aspect, h);
        return h / mockH;
    }

    /// <summary>
    /// 창 안에 <b>목업 크기 그대로의 레이어</b>를 두고, 창이 커진 배율만큼 통째로 확대한다.
    ///
    /// <para><b>왜 필요한가</b> — 구워진 계층은 비율 앵커라 창을 따라 커지지만, <b>런타임에 만드는</b>
    /// 카드는 여전히 목업 px로 지어진다(카드 폭 산출·내부 배치·글꼴이 전부 그 좌표계에 묶여 있다).
    /// 창만 커지고 카드는 그대로면 넓어진 판에 작은 카드가 떠 있게 된다.</para>
    ///
    /// <para>레이어를 목업 크기로 고정하고 <c>localScale</c>만 올리면 <b>위치·크기·글꼴이 한 번에</b>
    /// 같은 배율로 따라온다 — 기존 절대 좌표 계산을 한 줄도 고치지 않아도 된다.
    /// 창이 목업 비율을 유지하므로 균등 배율이 정확한 답이다.</para>
    /// </summary>
    public static RectTransform EnsureScaledLayer(Transform windowRoot, string name,
                                                  float mockW, float mockH)
    {
        if (windowRoot == null || mockW <= 0f || mockH <= 0f) return null;

        var existing = windowRoot.Find(name) as RectTransform;
        var layer = existing;
        if (layer == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            layer = (RectTransform)go.transform;
            layer.SetParent(windowRoot, false);
            layer.anchorMin = layer.anchorMax = layer.pivot = new Vector2(0.5f, 0.5f);
            layer.anchoredPosition = Vector2.zero;
            // 글꼴은 이 배율이 이미 키운다 — ScaleFonts가 또 키우면 두 번 곱해진다.
            go.AddComponent<UIFontScaleExempt>();
        }
        layer.sizeDelta = new Vector2(mockW, mockH);

        float w = windowRoot is RectTransform wr ? wr.rect.width : 0f;
        float s = w > 1f ? w / mockW : 1f;
        layer.localScale = new Vector3(s, s, 1f);
        return layer;
    }

    /// <summary>
    /// 창 아래 모든 <see cref="TMP_Text"/>의 글꼴을 <paramref name="ratio"/>배로 맞춘다.
    ///
    /// <para><b>여러 번 불러도 안전하다</b> — 직전에 먹인 배율을 창에 기억시켜 두고
    /// 차이만큼만 곱한다. 그러지 않으면 화면을 열 때마다 글자가 배로 커진다.</para>
    /// </summary>
    public static void ScaleFonts(RectTransform window, float ratio)
    {
        if (window == null || ratio <= 0f) return;

        if (!window.TryGetComponent<UIFontScaleMemo>(out var memo))
            memo = window.gameObject.AddComponent<UIFontScaleMemo>();

        float step = ratio / memo.Applied;
        if (Mathf.Abs(step - 1f) < 0.001f) return;   // 이미 맞다

        var texts = window.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            var t = texts[i];
            // 배율 레이어 안은 건너뛴다 — 그 안은 localScale이 이미 같은 배율로 키우고 있다.
            if (t.GetComponentInParent<UIFontScaleExempt>(true) != null) continue;
            t.fontSize = t.fontSize * step;
            if (t.enableAutoSizing)
            {
                t.fontSizeMin = t.fontSizeMin * step;
                t.fontSizeMax = t.fontSizeMax * step;
            }
        }
        memo.Applied = ratio;
    }
}

/// <summary>
/// 이 아래의 글자는 <see cref="UIProportional.ScaleFonts"/>가 건드리지 않는다.
/// <b>배율 레이어</b>처럼 이미 <c>localScale</c>로 확대되는 구역에 붙인다 — 안 그러면 두 번 곱해진다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIFontScaleExempt : MonoBehaviour { }

/// <summary>
/// <see cref="UIProportional.ScaleFonts"/>가 <b>직전에 먹인 배율</b>을 기억한다.
/// 이게 없으면 두 번째 호출이 이미 커진 글자를 또 키운다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIFontScaleMemo : MonoBehaviour
{
    [SerializeField] private float applied = 1f;

    /// <summary>지금까지 글꼴에 먹인 누적 배율.</summary>
    public float Applied
    {
        get => applied <= 0f ? 1f : applied;
        set => applied = value;
    }
}
