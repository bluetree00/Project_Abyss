using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 런타임 링(고리) 메시를 생성하는 충격파 비주얼 컴포넌트.
/// localScale.x/z로 반지름을 조절한다 (기본 단위 메시).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RingMeshWarning : MonoBehaviour
{
    [Tooltip("링 세분화 수 (높을수록 부드러움)")]
    [SerializeField] private int segments = 64;

    [Tooltip("링 두께 비율 (0~1 기준. 0.12 = 외부 반지름의 12%)")]
    [SerializeField] private float ringWidth = 0.12f;

    private void Awake()
    {
        GetComponent<MeshFilter>().mesh = BuildRingMesh(segments, ringWidth);

        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
    }

    private static Mesh BuildRingMesh(int segs, float width)
    {
        var mesh = new Mesh { name = "RingMesh" };

        int vertCount = segs * 2;
        var verts = new Vector3[vertCount];
        var uvs   = new Vector2[vertCount];
        var tris  = new int[segs * 6];

        float outerR = 1f;
        float innerR = Mathf.Max(0f, 1f - width);

        for (int i = 0; i < segs; i++)
        {
            float angle = (float)i / segs * Mathf.PI * 2f;
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);

            verts[i * 2]     = new Vector3(cos * outerR, 0f, sin * outerR);
            verts[i * 2 + 1] = new Vector3(cos * innerR, 0f, sin * innerR);

            uvs[i * 2]     = new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f);
            uvs[i * 2 + 1] = new Vector2(cos * innerR * 0.5f + 0.5f, sin * innerR * 0.5f + 0.5f);
        }

        for (int i = 0; i < segs; i++)
        {
            int next = (i + 1) % segs;
            int idx  = i * 6;

            tris[idx]     = i * 2;
            tris[idx + 1] = next * 2;
            tris[idx + 2] = i * 2 + 1;

            tris[idx + 3] = i * 2 + 1;
            tris[idx + 4] = next * 2;
            tris[idx + 5] = next * 2 + 1;
        }

        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
