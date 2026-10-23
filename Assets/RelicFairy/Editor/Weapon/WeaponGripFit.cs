using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-19 ③ 「플레이어가 검 · 칼을 제대로 쥐고 있지 않다 — 손에 잘 잡히는 위치로」.
/// 무기 프리팹은 WeaponMount(오른손 뼈에서 약 1.5 m 떨어진 소켓) 아래에 루트 로컬값 그대로 붙는다 — 손잡이 오프셋 칸이 없고,
/// 카타나는 배율을 1 → 1.35로 바꿀 때 위치를 안 고쳐 손잡이 가운데가 새끼손가락 쪽으로 약 9 cm 밀렸다(조사_무기_96 §③).
/// 재기 = 장비 형 실측 74(플레이 중 · 무기마다 대기 자세의 주먹 구멍을 WeaponMount 기준으로 → Temp/weapon_grip74.json).
/// 맞추기 = 아래 메뉴: 손잡이 가운데가 주먹 구멍에 오도록 프리팹 루트 위치만 칼날 방향으로 옮긴다(회전 · 배율 그대로 · Root/Tip 표식은 자식이라 메시와 함께 움직인다).
/// </summary>
public static class WeaponGripFit
{
    private const string FitPath = "Temp/weapon_grip74.json";
    private const float  Bin = 0.005f;         // 손잡이 찾기 단면 칸(메시 단위 m)
    private const float  LowerShare = 0.45f;   // 아래쪽 이만큼에서 손잡이를 찾는다
    private const float  ThinRatio = 1.35f;    // 가장 가는 단면의 이 배 이하 = 손잡이
    // 메시를 못 읽을 때(읽기 꺼짐) 손잡이 가운데 = 아래 끝 + 길이 × 이 비율 — 조사_무기_96 §③ FBX 실측 Sword1_2 0.126 · Sword4_1 0.143의 가운데
    private const float  GripShare = 0.135f;

    /// <summary>마지막 <see cref="GripCenterY"/>가 메시 경계로 어림했나(꼭짓점을 못 읽음).</summary>
    public static bool LastWasEstimate { get; private set; }

    // 무기 종류 → 날밑 쪽 치우침(m) · 프리팹(같은 종류는 같은 대기 자세 · 같은 소켓을 쓴다).
    // 치우침 = 손잡이 가운데에 맞춘 뒤 넓은 옆 사진(실측 74 GR_NN_c)에서 주먹이 머리 쪽 끝에 붙고 날밑까지 8~12 cm 비어 → 날밑에서 주먹까지 약 5 cm가 되게 더 민 몫
    // (주먹 구멍 어림이 손가락 둘이라 머리 쪽으로 치우치고, 한 손 쥐기는 검지가 날밑 가까이 온다).
    private static readonly (string kind, float guardBias, string[] prefabs)[] Prefabs =
    {
        ("Nameless",   0.05f, new[] { "Assets/RelicFairy/Weapon/Nameless/Prefabs/T0_Nameless_Weapon.prefab" }),
        ("Katana",     0.06f, new[] { "Assets/RelicFairy/Weapon/Katana/Prefabs/T1_Katana_Weapon.prefab", "Assets/RelicFairy/Weapon/Katana/Prefabs/T2_Katana_Weapon.prefab", "Assets/RelicFairy/Weapon/Katana/Prefabs/T3_Katana_Weapon.prefab" }),
        ("Greatsword", 0.03f, new[] { "Assets/RelicFairy/Weapon/Greatsword/Prefabs/T1_Greatsword_Weapon.prefab", "Assets/RelicFairy/Weapon/Greatsword/Prefabs/T2_Greatsword_Weapon.prefab", "Assets/RelicFairy/Weapon/Greatsword/Prefabs/T3_Greatsword_Weapon.prefab" }),
    };

    [MenuItem("RelicFairy/Weapon/검 손잡이 — 주먹 구멍에 맞추기 (10-19 ③, 실측 74 결과로)")]
    private static void Apply()
    {
        if (Application.isPlaying) { Debug.LogWarning("[GripFit] 편집 모드에서만."); return; }
        if (!File.Exists(FitPath)) { Debug.LogWarning($"[GripFit] {FitPath} 없음 — 장비 형 실측 74를 먼저 돌린다."); return; }
        var fist = ParseFit(File.ReadAllText(FitPath));
        var sb = new StringBuilder("[GripFit] 손잡이 맞추기\n");
        foreach (var (kind, guardBias, paths) in Prefabs)
        {
            if (!fist.TryGetValue(kind, out var target)) { sb.AppendLine($"  {kind}: 실측 값 없음 — 건너뜀"); continue; }
            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var mf = LargestMesh(root);
                    if (mf == null) { sb.AppendLine($"  {Path.GetFileName(path)}: 메시 없음"); continue; }
                    float gripY = GripCenterY(mf.sharedMesh, out float g0, out float g1);
                    Vector3 gripInRoot = root.transform.InverseTransformPoint(mf.transform.TransformPoint(new Vector3(0f, gripY, 0f)));
                    var t = root.transform;
                    Vector3 old = t.localPosition;
                    // 칼날 방향으로만 옮긴다 — 옆은 실측 사진에서 손잡이가 주먹을 지나가고(실측 74), 주먹 구멍 어림(손가락 둘)의 옆 오차가 더 크다
                    Vector3 gripNow = old + t.localRotation * Vector3.Scale(t.localScale, gripInRoot);
                    Vector3 axis = (t.localRotation * Vector3.up).normalized;
                    t.localPosition = old + axis * (Vector3.Dot(target - gripNow, axis) - guardBias);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    sb.AppendLine($"  {Path.GetFileName(path)}: {mf.sharedMesh.name} 손잡이 y {g0:0.000}~{g1:0.000}(가운데 {gripY:0.000}{(LastWasEstimate ? " · 경계로 어림" : "")}) · 루트 {old.ToString("F3")} → {t.localPosition.ToString("F3")} (옮김 {(t.localPosition - old).magnitude * 100f:0.0} cm)");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>가장 큰 메시(칼 몸) — 표식 · 트레일 자식에는 메시가 없다.</summary>
    public static MeshFilter LargestMesh(GameObject root)
        => root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null)
               .OrderByDescending(m => m.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();

    /// <summary>
    /// 손잡이 가운데(메시 로컬 y) — 칼은 y축으로 눕혀 만들어졌다(아래 = 손잡이 끝). 아래쪽 45%를 0.5 cm 단면으로 잘라
    /// 가장 가는 단면의 1.35배 이하가 가장 길게 이어진 구간을 손잡이로 본다(아래 끝 장식 · 위 날밑은 두껍다).
    /// </summary>
    public static float GripCenterY(Mesh mesh, out float from, out float to)
    {
        var v = ReadVertices(mesh);
        LastWasEstimate = v.Length == 0;
        if (LastWasEstimate)
        {
            var b = mesh.bounds;   // 경계는 읽기 꺼져도 읽힌다
            float len = b.size.y, mid = b.min.y + len * GripShare;
            from = mid - len * 0.05f; to = mid + len * 0.05f;
            return mid;
        }
        float yMin = v.Min(p => p.y), yMax = v.Max(p => p.y);
        int bins = Mathf.Max(4, Mathf.CeilToInt((yMax - yMin) * LowerShare / Bin));
        var xMin = new float[bins]; var xMax = new float[bins]; var zMin = new float[bins]; var zMax = new float[bins]; var n = new int[bins];
        for (int i = 0; i < bins; i++) { xMin[i] = zMin[i] = float.MaxValue; xMax[i] = zMax[i] = float.MinValue; }
        foreach (var p in v)
        {
            int i = Mathf.FloorToInt((p.y - yMin) / Bin);
            if (i < 0 || i >= bins) continue;
            n[i]++;
            xMin[i] = Mathf.Min(xMin[i], p.x); xMax[i] = Mathf.Max(xMax[i], p.x);
            zMin[i] = Mathf.Min(zMin[i], p.z); zMax[i] = Mathf.Max(zMax[i], p.z);
        }
        var width = new float[bins];
        float thin = float.MaxValue;
        for (int i = 0; i < bins; i++)
        {
            width[i] = n[i] > 0 ? Mathf.Max(xMax[i] - xMin[i], zMax[i] - zMin[i]) : -1f;
            if (n[i] > 0) thin = Mathf.Min(thin, width[i]);
        }
        int bestA = 0, bestB = -1, a = -1;
        for (int i = 0; i <= bins; i++)
        {
            bool ok = i < bins && (width[i] < 0f || width[i] <= thin * ThinRatio);   // 빈 칸은 이어진 것으로 본다
            if (ok) { if (a < 0) a = i; continue; }
            if (a >= 0 && i - 1 - a > bestB - bestA) { bestA = a; bestB = i - 1; }
            a = -1;
        }
        from = yMin + bestA * Bin;
        to   = yMin + (bestB + 1) * Bin;
        return (from + to) * 0.5f;
    }

    /// <summary>꼭짓점 — 읽기 꺼진 메시(무기 FBX 대부분)는 비운다(플레이 중 읽으면 예외 · 실측 74 1 · 2회차) → 경계로 어림.</summary>
    private static Vector3[] ReadVertices(Mesh mesh)
    {
        if (!mesh.isReadable) return System.Array.Empty<Vector3>();
        return mesh.vertices;
    }

    /// <summary>
    /// 주먹 구멍 가운데 — 검지 · 가운데 · 약지마다 세 마디(MCP · PIP · DIP)를 지나는 원의 중심(손가락이 손잡이를 감싼다)을 평균한다.
    /// 원이 이상하면(반지름 1~7 cm 밖 = 손가락이 펴짐) 그 손가락은 뺀다. 하나도 없으면 마디 아홉의 평균.
    /// </summary>
    public static Vector3 FistCenter(Animator anim, out string note)
    {
        var fingers = new[]
        {
            (HumanBodyBones.RightIndexProximal,  HumanBodyBones.RightIndexIntermediate,  HumanBodyBones.RightIndexDistal),
            (HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal),
            (HumanBodyBones.RightRingProximal,   HumanBodyBones.RightRingIntermediate,   HumanBodyBones.RightRingDistal),
        };
        Vector3 sum = Vector3.zero, all = Vector3.zero;
        int used = 0, pts = 0;
        var radii = new List<string>();
        foreach (var (pa, pb, pc) in fingers)
        {
            var A = anim.GetBoneTransform(pa); var B = anim.GetBoneTransform(pb); var C = anim.GetBoneTransform(pc);
            if (A == null || B == null || C == null) { radii.Add("뼈 없음"); continue; }
            all += A.position + B.position + C.position; pts += 3;
            Vector3 a = A.position - C.position, b = B.position - C.position, axb = Vector3.Cross(a, b);
            if (axb.sqrMagnitude < 1e-10f) { radii.Add("곧음"); continue; }
            Vector3 o = C.position + Vector3.Cross(a.sqrMagnitude * b - b.sqrMagnitude * a, axb) / (2f * axb.sqrMagnitude);
            float r = (o - A.position).magnitude;
            radii.Add($"{r * 100f:0.0}");
            if (r < 0.01f || r > 0.07f) continue;
            sum += o; used++;
        }
        note = $"마디 원 반지름 cm [{string.Join(", ", radii)}] · 쓴 손가락 {used}";
        if (used > 0) return sum / used;
        return pts > 0 ? all / pts : anim.GetBoneTransform(HumanBodyBones.RightHand).position;
    }

    private static Dictionary<string, Vector3> ParseFit(string json)
    {
        // {"Nameless":[x,y,z],...} — JsonUtility는 사전을 못 읽어 직접 푼다
        var res = new Dictionary<string, Vector3>();
        foreach (var part in json.Trim('{', '}', ' ', '\n', '\r').Split(']'))
        {
            int q1 = part.IndexOf('"'), q2 = q1 >= 0 ? part.IndexOf('"', q1 + 1) : -1, br = part.IndexOf('[');
            if (q1 < 0 || q2 < 0 || br < 0) continue;
            var nums = part.Substring(br + 1).Split(',').Select(s => float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (nums.Length == 3) res[part.Substring(q1 + 1, q2 - q1 - 1)] = new Vector3(nums[0], nums[1], nums[2]);
        }
        return res;
    }
}
