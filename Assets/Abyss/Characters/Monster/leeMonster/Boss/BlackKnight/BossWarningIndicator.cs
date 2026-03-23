using UnityEngine;

/// <summary>
/// BlackKnight 보스 공격 경고 장판.
/// 보스 프리팹에 부착하며, 각 공격 상태에서 ShowCircle / ShowCharge 를 호출한다.
///
///  ShowCircle  — SpinSlash(회전), OverheadSlash(내려찍기) 용 원형 장판
///  ShowCharge  — ChargeAttack(돌진) 용 화살표 장판
/// </summary>
public class BossWarningIndicator : UnityEngine.MonoBehaviour
{
    private const int CircleSegments = 48;

    // ── 원형 경고 ─────────────────────────────────────────
    private UnityEngine.LineRenderer _circleOutline;
    private UnityEngine.GameObject   _circleFillGO;
    private UnityEngine.Material     _circleFillMat;

    private bool                   _circleActive;
    private float                  _circleTimer;
    private float                  _circleDuration;
    private UnityEngine.Transform  _circleFollow;
    private float                  _circleRadius;
    private float                  _circleColorFloor; // 0=흐리게 시작, 1=즉시 최대 밝기

    // ── 돌진 경고 ─────────────────────────────────────────
    private UnityEngine.LineRenderer _chargeOutline;

    private bool  _chargeActive;
    private float _chargeTimer;
    private float _chargeDuration;

    // ── 색상 상수 ─────────────────────────────────────────
    private static readonly UnityEngine.Color OutlineFaint  = new UnityEngine.Color(1f, 0.55f, 0f, 0.40f); // 주황
    private static readonly UnityEngine.Color OutlineBright = new UnityEngine.Color(1f, 0.00f, 0f, 1.00f); // 빨강
    private static readonly UnityEngine.Color FillFaint     = new UnityEngine.Color(1f, 0.45f, 0f, 0.10f); // 주황 반투명
    private static readonly UnityEngine.Color FillBright    = new UnityEngine.Color(1f, 0.00f, 0f, 0.32f); // 빨강 반투명

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Unity 생명주기
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Awake()
    {
        // 원형 아웃라인
        _circleOutline = CreateLineRenderer("WarnCircle_Outline");
        _circleOutline.positionCount   = CircleSegments + 1;
        _circleOutline.widthMultiplier = 0.15f;

        // 원형 필 (디스크 메시)
        _circleFillGO = new UnityEngine.GameObject("WarnCircle_Fill");
        _circleFillGO.transform.SetParent(transform);
        var mf = _circleFillGO.AddComponent<UnityEngine.MeshFilter>();
        var mr = _circleFillGO.AddComponent<UnityEngine.MeshRenderer>();
        mf.mesh        = GenerateDiscMesh(1f, CircleSegments);
        _circleFillMat = CreateFillMaterial();
        mr.material    = _circleFillMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        // 돌진 아웃라인 (6점 화살표)
        _chargeOutline = CreateLineRenderer("WarnCharge_Outline");
        _chargeOutline.positionCount   = 6;
        _chargeOutline.widthMultiplier = 0.12f;

        SetLRActive(_circleOutline, false);
        _circleFillGO.SetActive(false);
        SetLRActive(_chargeOutline, false);
    }

    private void OnDestroy()
    {
        if (_circleFillMat != null) Destroy(_circleFillMat);
    }

    private void Update()
    {
        float dt = UnityEngine.Time.deltaTime;

        if (_circleActive)
        {
            _circleTimer -= dt;
            UpdateCircleVisuals();
            if (_circleTimer <= 0f) HideCircle();
        }

        if (_chargeActive)
        {
            _chargeTimer -= dt;
            ApplyChargeColor(1f - UnityEngine.Mathf.Clamp01(_chargeTimer / _chargeDuration));
            if (_chargeTimer <= 0f) HideCharge();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Public API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>원형 경고 장판 표시. follow 트랜스폼 중심으로 이동한다.</summary>
    /// <param name="colorFloor">색상 밝기 시작값. 0=흐리게→점점 밝아짐, 1=처음부터 최대 밝기.</param>
    public void ShowCircle(UnityEngine.Transform follow, float radius, float duration,
                           float colorFloor = 0f)
    {
        _circleFollow     = follow;
        _circleRadius     = radius;
        _circleDuration   = duration;
        _circleTimer      = duration;
        _circleColorFloor = UnityEngine.Mathf.Clamp01(colorFloor);
        _circleActive     = true;
        SetLRActive(_circleOutline, true);
        _circleFillGO.SetActive(true);
    }

    /// <summary>원형 경고 즉시 숨기기.</summary>
    public void HideCircle()
    {
        _circleActive = false;
        SetLRActive(_circleOutline, false);
        _circleFillGO.SetActive(false);
    }

    /// <summary>표시 중인 원형 장판의 반지름을 실시간 변경.</summary>
    public void UpdateCircleRadius(float radius) => _circleRadius = radius;

    /// <summary>돌진 경로 화살표 경고 표시.</summary>
    public void ShowCharge(UnityEngine.Vector3 start, UnityEngine.Vector3 direction,
                           float length, float width, float duration)
    {
        _chargeDuration = duration;
        _chargeTimer    = duration;
        _chargeActive   = true;
        SetLRActive(_chargeOutline, true);
        SetChargeShape(start, direction, length, width);
        ApplyChargeColor(0f);
    }

    /// <summary>돌진 경고 즉시 숨기기.</summary>
    public void HideCharge()
    {
        _chargeActive = false;
        SetLRActive(_chargeOutline, false);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 비주얼 갱신
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void UpdateCircleVisuals()
    {
        if (_circleFollow == null) return;

        float timerT = 1f - UnityEngine.Mathf.Clamp01(_circleTimer / _circleDuration);
        float t      = UnityEngine.Mathf.Max(_circleColorFloor, timerT); // floor 이하로 떨어지지 않음
        float pulse  = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (5f + t * 12f)) + 1f) * 0.5f;
        float blend  = UnityEngine.Mathf.Clamp01(t * 0.7f + pulse * 0.3f);

        var outC = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);
        _circleOutline.startColor = outC;
        _circleOutline.endColor   = outC;

        _circleFillMat.color = UnityEngine.Color.Lerp(FillFaint, FillBright, blend);

        // 보스 위치 추적
        var center = _circleFollow.position;
        center.y += 0.05f;

        for (int i = 0; i <= CircleSegments; i++)
        {
            float a = i * UnityEngine.Mathf.PI * 2f / CircleSegments;
            _circleOutline.SetPosition(i, center + new UnityEngine.Vector3(
                UnityEngine.Mathf.Cos(a) * _circleRadius, 0f,
                UnityEngine.Mathf.Sin(a) * _circleRadius));
        }

        // 디스크 메시는 XZ 평면 — rotation identity 로 지면에 평행
        _circleFillGO.transform.position   = center;
        _circleFillGO.transform.rotation   = UnityEngine.Quaternion.identity;
        _circleFillGO.transform.localScale =
            new UnityEngine.Vector3(_circleRadius, 1f, _circleRadius);
    }

    private void ApplyChargeColor(float t)
    {
        float pulse = (UnityEngine.Mathf.Sin(UnityEngine.Time.time * (8f + t * 14f)) + 1f) * 0.5f;
        float blend = UnityEngine.Mathf.Clamp01(t * 0.7f + pulse * 0.3f);
        var c = UnityEngine.Color.Lerp(OutlineFaint, OutlineBright, blend);
        _chargeOutline.startColor = c;
        _chargeOutline.endColor   = c;
    }

    /// <summary>화살표 모양 — 뒤가 넓고 앞이 뾰족.</summary>
    private void SetChargeShape(UnityEngine.Vector3 start, UnityEngine.Vector3 direction,
                                float length, float width)
    {
        var right  = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, direction).normalized
                     * (width * 0.5f);
        var midPos = start + direction * (length * 0.82f);
        var tip    = start + direction * length;
        float y    = start.y + 0.05f;

        _chargeOutline.SetPosition(0, WithY(start  - right, y));
        _chargeOutline.SetPosition(1, WithY(midPos - right, y));
        _chargeOutline.SetPosition(2, WithY(tip,            y)); // 뾰족한 끝
        _chargeOutline.SetPosition(3, WithY(midPos + right, y));
        _chargeOutline.SetPosition(4, WithY(start  + right, y));
        _chargeOutline.SetPosition(5, WithY(start  - right, y)); // 닫기
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 리소스 생성
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private UnityEngine.LineRenderer CreateLineRenderer(string goName)
    {
        var go = new UnityEngine.GameObject(goName);
        go.transform.SetParent(transform);
        var lr = go.AddComponent<UnityEngine.LineRenderer>();
        lr.useWorldSpace     = true;
        lr.loop              = false;
        lr.numCapVertices    = 4;
        lr.material          = CreateLineMaterial();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        return lr;
    }

    private static UnityEngine.Material CreateLineMaterial()
    {
        string[] candidates = { "Sprites/Default", "Unlit/Transparent", "UI/Default" };
        foreach (string sn in candidates)
        {
            var s = UnityEngine.Shader.Find(sn);
            if (s != null) return new UnityEngine.Material(s);
        }
        return new UnityEngine.Material(UnityEngine.Shader.Find("Standard"));
    }

    private static UnityEngine.Material CreateFillMaterial()
    {
        string[] candidates = { "Sprites/Default", "Unlit/Transparent", "UI/Default" };
        foreach (string sn in candidates)
        {
            var s = UnityEngine.Shader.Find(sn);
            if (s != null) return new UnityEngine.Material(s) { color = FillFaint };
        }
        return new UnityEngine.Material(UnityEngine.Shader.Find("Standard"));
    }

    /// <summary>XZ 평면 디스크 메시 (radius=1 기준, localScale 로 크기 조정).</summary>
    private static UnityEngine.Mesh GenerateDiscMesh(float radius, int segments)
    {
        var mesh  = new UnityEngine.Mesh { name = "WarnDisc" };
        var verts = new UnityEngine.Vector3[segments + 1];
        var tris  = new int[segments * 3];

        verts[0] = UnityEngine.Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float a = i * UnityEngine.Mathf.PI * 2f / segments;
            verts[i + 1] = new UnityEngine.Vector3(
                UnityEngine.Mathf.Cos(a) * radius, 0f,
                UnityEngine.Mathf.Sin(a) * radius);
        }
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % segments + 1;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void SetLRActive(UnityEngine.LineRenderer lr, bool active) =>
        lr.gameObject.SetActive(active);

    private static UnityEngine.Vector3 WithY(UnityEngine.Vector3 v, float y) =>
        new UnityEngine.Vector3(v.x, y, v.z);
}
