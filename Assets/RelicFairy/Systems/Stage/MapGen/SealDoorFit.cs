using UnityEngine;

/// <summary>
/// 봉인 문 세우기(10-05) — 런타임(<see cref="RunFlowController"/>)과 비교 도구가 같은 규칙을 쓴다.
/// 예전엔 개구부에 가로 · 세로를 따로 맞춰 늘려(깊이는 그대로) 누운 뿌리 · 옆으로 놓인 문짝이 얇은 판으로 늘어났다
/// (사용자 10-02 「얇은 판이 움직이는 느낌」 · 실측 배율 (2.39, 10.45, 1) · Gothic 문은 두께를 40배로 늘렸다).
/// ① 문짝 면이 개구부를 보게 세운다(메시가 옆(Z)으로 넓으면 90° 돌림) ② 높이에 맞춰 <b>균등 배율</b>(깊이도 같이)
/// ③ 한 짝이 개구부 폭의 <see cref="PairBelow"/>보다 좁으면 거울 짝을 붙여 쌍문으로. 전체를 개구부 중앙(컨테이너 원점)에 맞춘다.
/// </summary>
public static class SealDoorFit
{
    /// <summary>한 짝 폭이 개구부 폭의 이 비율보다 좁으면 쌍문으로 세운다.</summary>
    public const float PairBelow = 0.6f;
    /// <summary>문 평면의 깊이 자리(컨테이너 로컬 z) — 예전 석문과 같은 자리.</summary>
    public const float PlaneZ = -0.25f;
    /// <summary>젖힘 문이 다 열렸을 때의 각도(도) — 문짝이 통로 벽에 붙듯 바깥으로 젖혀진다.</summary>
    public const float SwingAngle = 95f;

    private static readonly Vector3[] s_corner = new Vector3[8];

    /// <summary>
    /// <paramref name="container"/>(배율 1 · 개구부 중앙이 원점 · 자식 없음) 아래에 <paramref name="prefab"/>을 세운다.
    /// <paramref name="local"/> = 세운 문 전체의 컨테이너 로컬 바운즈, <paramref name="leaves"/> = 문짝 수(1 · 2).
    /// 메시 렌더러가 없는 프리팹이면 false — 아무것도 남기지 않는다(부르는 쪽이 옛 규약으로 폴백).
    /// </summary>
    public static bool Place(Transform container, GameObject prefab, float ow, float oh, out Bounds local, out int leaves)
    {
        local  = default;
        leaves = 0;
        var leaf = Object.Instantiate(prefab, container);
        leaf.name = "Leaf";
        var lt = leaf.transform;
        Vector3 baseScale = prefab.transform.localScale;
        lt.localPosition = Vector3.zero;
        lt.localRotation = Quaternion.identity;
        lt.localScale    = baseScale;
        if (!Measure(container, container, out var b) || b.size.y < 0.01f || Mathf.Max(b.size.x, b.size.z) < 0.01f)
        {
            Kill(leaf);
            return false;
        }

        if (b.size.z > b.size.x)   // 문짝이 옆으로 누워 있다 — 면이 개구부를 보게 돌린다
        {
            lt.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Measure(container, container, out b);
        }
        lt.localScale = baseScale * (oh / Mathf.Max(0.01f, b.size.y));
        Measure(container, container, out b);
        leaves = 1;

        if (b.size.x < ow * PairBelow)
        {
            // 쌍문 — 왼짝의 오른쪽 끝을 원점에, 오른짝은 X 거울 부모 아래 같은 자리(거울이라 원점 오른쪽에 맞붙는다)
            lt.localPosition += new Vector3(-b.max.x, 0f, 0f);
            var mirror = new GameObject("Leaf_R").transform;
            mirror.SetParent(container, false);
            mirror.localScale = new Vector3(-1f, 1f, 1f);
            var right = Object.Instantiate(leaf, mirror);
            right.name = "Leaf";
            right.transform.localPosition = lt.localPosition;
            right.transform.localRotation = lt.localRotation;
            right.transform.localScale    = lt.localScale;
            leaves = 2;
        }

        // 가로 · 세로 중심을 개구부 중앙에, 문 평면을 PlaneZ에
        Measure(container, container, out b);
        var shift = new Vector3(-b.center.x, -b.center.y, PlaneZ - b.center.z);
        for (int i = 0; i < container.childCount; i++) container.GetChild(i).localPosition += shift;
        Measure(container, container, out local);
        return true;
    }

    /// <summary><see cref="Place"/>로 세운 문인가(문짝 「Leaf」 · 「Leaf_R」이 있다). 옛 규약 폴백 문엔 경첩을 달지 않는다.</summary>
    public static bool IsFitted(Transform door)
        => door != null && (door.Find("Leaf") != null || door.Find("Leaf_R") != null);

    /// <summary>
    /// 젖힘 문(10-06) — 문짝마다 바깥 모서리에 경첩을 세우고 문짝을 그 아래로 옮긴다. 쌍문 = 양끝, 한 짝 = 왼끝.
    /// <see cref="Place"/> 뒤에 부른다. 돌려주는 경첩을 <see cref="SetSwing"/>로 돌린다.
    /// </summary>
    public static Transform[] BuildHinges(Transform container)
    {
        int n = container.childCount;
        var leaves = new Transform[n];
        for (int i = 0; i < n; i++) leaves[i] = container.GetChild(i);
        var hinges = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            Measure(leaves[i], container, out var b);
            bool right = n > 1 && b.center.x > 0f;
            var h = new GameObject(right ? "Hinge_R" : "Hinge_L").transform;
            h.SetParent(container, false);
            h.localPosition = new Vector3(right ? b.max.x : b.min.x, 0f, b.center.z);
            leaves[i].SetParent(h, true);
            hinges[i] = h;
        }
        return hinges;
    }

    /// <summary>젖힘 정도 <paramref name="k"/>(0 = 닫힘 · 1 = 다 열림). <paramref name="outward"/> = 바깥이 컨테이너 로컬 +z면 +1, −z면 −1.</summary>
    public static void SetSwing(Transform[] hinges, float outward, float k)
    {
        if (hinges == null) return;
        foreach (var h in hinges)
        {
            if (h == null) continue;
            // Y축 +각은 +x 끝을 −z로 돌린다 — 왼 경첩(문짝이 +x로 뻗음)은 −, 오른 경첩(−x로 뻗음)은 +를 줘야 둘 다 바깥으로 젖혀진다
            float side = h.localPosition.x > 0f ? 1f : -1f;
            h.localRotation = Quaternion.Euler(0f, side * outward * SwingAngle * k, 0f);
        }
    }

    /// <summary><paramref name="root"/> 아래 메시들의 바운즈를 <paramref name="space"/> 로컬로 — 꼭짓점 변환이라 회전 · 거울에도 정확하다.</summary>
    private static bool Measure(Transform root, Transform space, out Bounds b)
    {
        b = default;
        bool has = false;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var mb = mesh.bounds;
            var t  = mf.transform;
            int n = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                s_corner[n++] = mb.center + Vector3.Scale(mb.extents, new Vector3(sx, sy, sz));
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = space.InverseTransformPoint(t.TransformPoint(s_corner[i]));
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
            }
        }
        return has;
    }

    private static void Kill(Object o)
    {
        if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
    }
}
