using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 런타임 부채꼴(Fan) 메시를 생성하는 경고 장판 컴포넌트.
/// Awake에서 arcDegrees 각도의 반원형 메시를 MeshFilter에 할당한다.
/// 반지름은 localScale.x / localScale.z 로 조절한다 (기본 단위 메시).
/// 바닥 밀착: Y=0.02 오프셋, 그림자 없음, 뎁스 쓰기 없음.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FanMeshWarning : MonoBehaviour
{
    [Tooltip("부채꼴 각도 (180 = 반원)")]
    [SerializeField] private float arcDegrees = 180f;

    [Tooltip("메시 세분화 수 (높을수록 부드러움)")]
    [SerializeField] private int segments = 32;

    private void Awake()
    {
        GetComponent<MeshFilter>().mesh = BuildFanMesh(arcDegrees, segments);

        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.renderingLayerMask = 1;
    }

    private static Mesh BuildFanMesh(float arcDeg, int segs)
    {
        var mesh = new Mesh { name = "FanMesh" };

        int vertCount = segs + 2;          // 중심 + 호 꼭짓점들
        var verts = new Vector3[vertCount];
        var uvs   = new Vector2[vertCount];
        var tris  = new int[segs * 3];

        // 중심 꼭짓점
        verts[0] = Vector3.zero;
        uvs[0]   = new Vector2(0.5f, 0.5f);

        float halfArc = arcDeg * 0.5f;

        for (int i = 0; i <= segs; i++)
        {
            float t     = (float)i / segs;
            float angle = Mathf.Lerp(-halfArc, halfArc, t) * Mathf.Deg2Rad;
            float x     = Mathf.Sin(angle);
            float z     = Mathf.Cos(angle);

            verts[i + 1] = new Vector3(x, 0f, z);
            uvs[i + 1]   = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);
        }

        for (int i = 0; i < segs; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }

        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
