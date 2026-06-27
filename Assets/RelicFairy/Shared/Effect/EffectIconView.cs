using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 효과 아이콘 표시용 최소 글루(C단계). IconKey를 스프라이트로 해석해
/// 효과 1줄(TMP) 좌측에 작은 Image 아이콘을 붙이고 텍스트를 들여쓴다.
///
/// 본격적인 "효과 1줄 공통 위젯"은 D단계에서 만든다 — 여기서는 기존 동적 TMP 라인에
/// 아이콘만 비침습적으로 얹는 역할만 한다.
/// </summary>
public static class EffectIconView
{
    /// <summary>
    /// row(TMP가 본체인 GameObject) 좌측에 size×size 아이콘 Image를 자식으로 추가하고
    /// text의 좌측 margin을 늘려 겹치지 않게 한다.
    /// </summary>
    public static Image Attach(Transform row, string iconKey, TMP_Text text, float size = 16f)
    {
        if (row == null) return null;

        var sprite = EffectIconRegistry.GetSprite(iconKey);
        if (sprite == null) return null;

        var iconGO = new GameObject("EffectIcon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(row, false);

        var rt = iconGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot     = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(0f, 0f);

        var img = iconGO.GetComponent<Image>();
        img.sprite        = sprite;          // 플레이스홀더는 이미 색이 입혀져 있어 흰색 틴트 유지
        img.preserveAspect = true;
        img.raycastTarget = false;

        // 텍스트를 아이콘 폭 + 여백만큼 들여쓰기(레이아웃 구조 변경 없이).
        if (text != null)
            text.margin = new Vector4(size + 4f, text.margin.y, text.margin.z, text.margin.w);

        return img;
    }
}
