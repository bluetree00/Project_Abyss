using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DiscMeshWarning : MonoBehaviour
{
    [SerializeField] private int segments = 64;

    private void Awake()
    {
        GetComponent<MeshFilter>().mesh = BuildDisc(segments);

        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
    }

    private static Mesh BuildDisc(int segs)
    {
        var mesh = new Mesh { name = "DiscMesh" };

        var verts = new Vector3[segs + 1];
        var uvs   = new Vector2[segs + 1];
        var tris  = new int[segs * 3];

        verts[0] = Vector3.zero;
        uvs[0]   = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < segs; i++)
        {
            float angle = (float)i / segs * Mathf.PI * 2f;
            float x = Mathf.Sin(angle);
            float z = Mathf.Cos(angle);
            verts[i + 1] = new Vector3(x, 0f, z);
            uvs[i + 1]   = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);
        }

        for (int i = 0; i < segs; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % segs + 1;
        }

        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
