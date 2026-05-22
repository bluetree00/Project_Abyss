using TMPro;
using UnityEngine;

/// <summary>
/// TMP_Text 컴포넌트에 가독성용 검정 테두리(Outline)를 일괄 적용하는 헬퍼.
/// TMP의 outline은 fontMaterial 프로퍼티를 통해 제어된다. fontMaterial 접근 시
/// Unity가 material을 자동 인스턴스화하므로 다른 TMP 컴포넌트와 공유되지 않는다.
/// </summary>
public static class TMPOutlineHelper
{
    private static readonly Color DefaultOutlineColor = new(0f, 0f, 0f, 1f);
    // 너무 크면 작은 글자가 검정으로 덮임. 가독성 있게 0.1~0.15 권장.
    private const float DefaultWidth    = 0.12f;
    private const float DefaultSoftness = 0.05f;

    /// <summary>TMP_Text 에 검정 테두리 기본값으로 적용.</summary>
    public static void ApplyDefault(TMP_Text text)
    {
        Apply(text, DefaultOutlineColor, DefaultWidth, DefaultSoftness);
    }

    /// <summary>outlineColor/width/softness 커스텀 적용. width 0~1, softness 0~1.</summary>
    public static void Apply(TMP_Text text, Color outlineColor, float width, float softness = 0f)
    {
        if (text == null) return;
        if (text.font == null || text.font.material == null) return;

        // fontMaterial 접근 시 Unity가 자동 복제 → 다른 TMP에 영향 없음
        var mat = text.fontMaterial;
        if (mat == null) return;

        mat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(width));
        mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, Mathf.Clamp01(softness));

        // Underlay(그림자)도 함께 넣고 싶으면 추가 가능하지만 일단 테두리만.
        text.UpdateMeshPadding();
    }
}
