using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 보스 경고 장판 — 부채꼴(扇形) 메시.
/// DiscMeshWarning의 섹터 버전. angle로 호 범위를 지정한다.
/// 메시는 Z+ 방향을 중심으로 펼쳐지므로 프리팹을 보스 회전에 맞춰 스폰하면 된다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SectorMeshWarning : MonoBehaviour
{
    [SerializeField] private float angle    = 120f;   // 부채꼴 각도 (도)
    [SerializeField] private int   segments = 48;

    private void Awake()
    {
        GetComponent<MeshFilter>().mesh = BuildSector(angle, segments);

        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
    }

    private static Mesh BuildSector(float angleDeg, int segs)
    {
        var mesh = new Mesh { name = "SectorMesh" };

        // vertices: 중심(0) + 호 위의 점(segs+1)
        var verts = new Vector3[segs + 2];
        var uvs   = new Vector2[segs + 2];
        var tris  = new int[segs * 3];

        float halfRad = angleDeg * 0.5f * Mathf.Deg2Rad;

        verts[0] = Vector3.zero;
        uvs[0]   = new Vector2(0.5f, 0.5f);

        for (int i = 0; i <= segs; i++)
        {
            float t     = (float)i / segs;
            float a     = Mathf.Lerp(-halfRad, halfRad, t);
            float x     = Mathf.Sin(a);
            float z     = Mathf.Cos(a);
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
