using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 그래픽에 가로(좌→우) 정점 컬러 그라디언트를 입힌다 — 셰이더/머티리얼 없이 정점색만 곱한다.
/// 게이지 fill에 얹으면 "끝으로 갈수록 진하고 밝은 붉은색" 같은 표현이 된다.
/// Filled/Horizontal Image에 쓰면 보이는 채움 구간 폭으로 정규화되어 선단이 항상 오른쪽(밝은) 색이 된다.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIHorizontalGradient : BaseMeshEffect
{
    [SerializeField] private Color left  = new Color(0.5f, 0.06f, 0.05f, 1f);
    [SerializeField] private Color right = new Color(1f, 0.28f, 0.18f, 1f);

    public void SetColors(Color l, Color r)
    {
        left = l; right = r;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        float min = float.MaxValue, max = float.MinValue;
        var v = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            if (v.position.x < min) min = v.position.x;
            if (v.position.x > max) max = v.position.x;
        }

        float range = Mathf.Max(0.0001f, max - min);
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float t = (v.position.x - min) / range;
            v.color = Color.Lerp(left, right, t) * v.color;   // 기존 정점색(틴트)과 곱
            vh.SetUIVertex(v, i);
        }
    }
}
