using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 직사각형 경고 장판.
/// localScale = (width, 1, length) 으로 크기 지정.
/// SetFillProgress(0~1) 로 내부 채우기 알파를 제어한다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RectWarning : MonoBehaviour
{
    [SerializeField] private float borderThickness = 0.04f;

    private MeshRenderer _fillMr;
    private MaterialPropertyBlock _mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        GetComponent<MeshFilter>().mesh = BuildBorderMesh(borderThickness);

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(transform, false);
        fillGO.AddComponent<MeshFilter>().mesh = BuildFillMesh();
        _fillMr = fillGO.AddComponent<MeshRenderer>();
        _fillMr.sharedMaterial    = mr.sharedMaterial;
        _fillMr.shadowCastingMode = ShadowCastingMode.Off;
        _fillMr.receiveShadows    = false;

        _mpb = new MaterialPropertyBlock();
        SetFillProgress(0f);
    }

    public void SetFillProgress(float t)
    {
        _fillMr.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, new Color(1f, 0.1f, 0.04f, t * 0.55f));
        _fillMr.SetPropertyBlock(_mpb);
    }

    // 테두리: 4개의 얇은 쿼드 스트립 (X: -0.5~0.5, Z: 0~1)
    private static Mesh BuildBorderMesh(float thick)
    {
        var v = new Vector3[16];

        // 앞(near) 스트립
        v[0]  = new Vector3(-0.5f,        0, 0);
        v[1]  = new Vector3( 0.5f,        0, 0);
        v[2]  = new Vector3( 0.5f,        0, thick);
        v[3]  = new Vector3(-0.5f,        0, thick);
        // 뒤(far) 스트립
        v[4]  = new Vector3(-0.5f,        0, 1 - thick);
        v[5]  = new Vector3( 0.5f,        0, 1 - thick);
        v[6]  = new Vector3( 0.5f,        0, 1);
        v[7]  = new Vector3(-0.5f,        0, 1);
        // 왼쪽 스트립 (near~far 사이)
        v[8]  = new Vector3(-0.5f,        0, thick);
        v[9]  = new Vector3(-0.5f + thick,0, thick);
        v[10] = new Vector3(-0.5f + thick,0, 1 - thick);
        v[11] = new Vector3(-0.5f,        0, 1 - thick);
        // 오른쪽 스트립
        v[12] = new Vector3( 0.5f - thick,0, thick);
        v[13] = new Vector3( 0.5f,        0, thick);
        v[14] = new Vector3( 0.5f,        0, 1 - thick);
        v[15] = new Vector3( 0.5f - thick,0, 1 - thick);

        var tris = new int[24];
        for (int i = 0; i < 4; i++)
        {
            int b = i * 4, ti = i * 6;
            tris[ti]     = b;     tris[ti + 1] = b + 1; tris[ti + 2] = b + 2;
            tris[ti + 3] = b;     tris[ti + 4] = b + 2; tris[ti + 5] = b + 3;
        }

        var mesh = new Mesh { name = "RectBorder", vertices = v, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildFillMesh()
    {
        var mesh = new Mesh { name = "RectFill" };
        mesh.vertices  = new[] {
            new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0),
            new Vector3( 0.5f, 0, 1), new Vector3(-0.5f, 0, 1),
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
