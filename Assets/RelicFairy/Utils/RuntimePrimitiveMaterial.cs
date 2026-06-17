using UnityEngine;

/// <summary>
/// 런타임 <see cref="GameObject.CreatePrimitive"/> 의 기본 머티리얼은 빌드에서 빌트인 셰이더가
/// 스트립되어 마젠타(핑크)로 렌더된다(에디터에선 정상이라 발견이 늦다).
/// URP/Lit 머티리얼을 명시적으로 할당해 에디터/빌드 렌더를 일치시킨다.
/// </summary>
public static class RuntimePrimitiveMaterial
{
    private static Shader _urpLit;

    private static Shader UrpLit
    {
        get
        {
            if (_urpLit == null)
                _urpLit = Shader.Find("Universal Render Pipeline/Lit");
            return _urpLit;
        }
    }

    /// <summary>프리미티브 Renderer 에 URP/Lit 인스턴스 머티리얼을 할당하고 색을 적용한다.</summary>
    public static void Apply(Renderer renderer, Color color)
    {
        if (renderer == null) return;

        var shader = UrpLit;
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            renderer.material = mat;
        }
        else
        {
            // 폴백 — URP/Lit 미발견 시 기존 머티리얼 색만 변경(에디터 동작 보존).
            renderer.material.color = color;
        }
    }
}
