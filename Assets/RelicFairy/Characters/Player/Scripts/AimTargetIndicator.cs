using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 자동추적(에임어시스트) 대상 몬스터를 머리 위 화살표로 실시간 표시한다.
/// 플레이어에 런타임 자동 부착(DodgePresentation/PlayerWeaponTrailVfx와 동일 패턴).
///
/// 타겟은 PlayerController.ComputeMouseAimAssistRotation(부작용 없는 순수 계산)을
/// 저주기로 재사용해 얻는다 = "지금 공격하면 런지가 붙을 대상"(마우스 조준 콘 기준).
///
/// 비주얼은 절차적 메시(모던한 얇은 아래방향 셰브론 ﹀) + URP Unlit — 신규 에셋 불필요.
/// 대상 몬스터의 콜라이더 상단에 떠서 카메라를 향해 빌보드 + 미세 바운스.
/// </summary>
[DisallowMultipleComponent]
public sealed class AimTargetIndicator : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    // 무기 실제값을 못 읽을 때의 폴백 = 에임어시스트 실측 하한(공격 코드와 동일).
    private const float TrackRadius        = 7f;    // ActAttackState.LungeTrackMinRadius (반경 floor)
    private const float ConeHalfAngleDeg   = 40f;   // WeaponAnimationSetSO 기본 aimAssistConeHalfAngle
    private const float PollInterval       = 0.06f; // 타겟 재탐색 주기(초)
    private const float HeightOffset       = 0.85f; // 콜라이더 상단으로부터 화살표 높이(m)
    private const float BobAmplitude       = 0.1f;
    private const float BobSpeed           = 4.5f;
    private const float ArrowWorldScale    = 0.5f;

    // ── Serialized ────────────────────────────────────────────────
    [Header("화살표 색상 (타격 빨강/외곽선 파랑과 구분되는 주목색)")]
    [SerializeField] private Color _arrowColor = new(1f, 0.82f, 0.15f, 1f); // 골드

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _pc;
    private Camera           _cam;
    private GameObject       _arrowGo;
    private Transform        _arrowTf;
    private Transform        _target;
    private Collider         _targetCollider;
    private float            _pollTimer;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _pc  = GetComponent<PlayerController>();
        _cam = Camera.main;
        BuildArrow();
        HideArrow();
    }

    private void OnDisable() => HideArrow();

    private void OnDestroy()
    {
        if (_arrowGo != null) Destroy(_arrowGo);
    }

    private void LateUpdate()
    {
        if (_pc == null || _arrowTf == null) return;

        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer <= 0f)
        {
            _pollTimer = PollInterval;
            RefreshTarget();
        }

        // 대상이 사라졌거나(풀 반환/사망) 비활성이면 숨김.
        if (_target == null || !_target.gameObject.activeInHierarchy)
        {
            if (_arrowGo.activeSelf) HideArrow();
            return;
        }

        if (!_arrowGo.activeSelf) _arrowGo.SetActive(true);
        PositionArrow();
    }

    // ── Private Methods ───────────────────────────────────────────
    private void RefreshTarget()
    {
        ResolveAimParams(out float radius, out float cone);

        // 부작용 없는 순수 계산(RequestFacing 미호출) — out으로 콘 안 최적 타겟만 취한다.
        _pc.ComputeMouseAimAssistRotation(radius, cone, 1f, out Transform enemy, out _);

        if (enemy == _target) return;
        _target         = enemy;
        _targetCollider = enemy != null ? enemy.GetComponentInChildren<Collider>() : null;
    }

    /// <summary>현재 무기의 실제 에임어시스트 반경/콘을 읽어 실제 공격 탐지와 일치시킨다.
    /// animationSet은 CSV 주입이 반영된 런타임 클론이라 실제 사용값과 동일.
    /// useAimAssist 매핑 중 최대 반경(공격 하한 7m로 floor)을 채택 — 어떤 공격이든 잠글 대상을 놓치지 않는다.
    /// 무기/매핑이 없으면 하한 기본값(7m/40°)으로 폴백. 저주기(0.06s) 호출 + List 구조체 열거자라 GC 없음.</summary>
    private void ResolveAimParams(out float radius, out float cone)
    {
        radius = TrackRadius;
        cone   = ConeHalfAngleDeg;

        var wd = _pc.WeaponManager != null ? _pc.WeaponManager.CurrentWeaponData : null;
        var animSet = wd != null ? wd.animationSet : null;
        if (animSet == null || animSet.animGroups == null) return;

        float maxRadius = 0f, coneAtMax = 0f;
        foreach (var grp in animSet.animGroups)
        {
            if (grp?.clipMappings == null) continue;
            foreach (var m in grp.clipMappings)
            {
                if (m == null || !m.useAimAssist) continue;
                if (m.aimAssistRadius > maxRadius)
                {
                    maxRadius = m.aimAssistRadius;
                    coneAtMax = m.aimAssistConeHalfAngle;
                }
            }
        }

        if (maxRadius > 0f)
        {
            radius = Mathf.Max(maxRadius, TrackRadius);
            if (coneAtMax > 0f) cone = coneAtMax;
        }
    }

    private void PositionArrow()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        Vector3 basePos = _target.position;
        float topY = _targetCollider != null ? _targetCollider.bounds.max.y : basePos.y + 2f;

        // Time.unscaledTime로 시간정지(히트스톱/일시정지) 중에도 바운스가 자연스럽게 이어짐.
        float bob = Mathf.Sin(Time.unscaledTime * BobSpeed) * BobAmplitude;
        _arrowTf.position = new Vector3(basePos.x, topY + HeightOffset + bob, basePos.z);
        _arrowTf.rotation = _cam.transform.rotation; // 카메라 빌보드
    }

    private void HideArrow()
    {
        _target         = null;
        _targetCollider = null;
        if (_arrowGo != null && _arrowGo.activeSelf) _arrowGo.SetActive(false);
    }

    private void BuildArrow()
    {
        _arrowGo = new GameObject("~AimTargetArrow");
        _arrowTf = _arrowGo.transform;
        _arrowTf.localScale = Vector3.one * ArrowWorldScale;

        var mf = _arrowGo.AddComponent<MeshFilter>();
        mf.sharedMesh = CreateDownArrowMesh();

        var mr = _arrowGo.AddComponent<MeshRenderer>();
        mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.material             = CreateArrowMaterial();
    }

    private Material CreateArrowMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _arrowColor);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", _arrowColor);
        if (mat.HasProperty("_Cull"))      mat.SetFloat("_Cull", 0f); // 양면 렌더 → 빌보드 뒤집힘 무관
        return mat;
    }

    /// <summary>모던한 얇은 아래방향 셰브론(﹀) 메시. 두 개의 가는 스트로크가 하단 팁에서 만난다.
    /// 로컬 XY 평면, +Z 정면. 양면 재질(_Cull=0)로 렌더 → winding 무관.</summary>
    private static Mesh CreateDownArrowMesh()
    {
        var verts = new List<Vector3>(8);
        var tris  = new List<int>(12);

        Vector2 tip   = new(0f, -0.3f);
        Vector2 left  = new(-0.5f, 0.18f);
        Vector2 right = new(0.5f, 0.18f);
        const float halfThick = 0.055f; // 얇은 스트로크 두께의 절반

        AddStroke(verts, tris, tip, left,  halfThick);
        AddStroke(verts, tris, tip, right, halfThick);

        var mesh = new Mesh { name = "AimTargetChevron" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>두 점 A→B를 잇는 얇은 사각 스트로크(quad)를 메시 버퍼에 추가한다.</summary>
    private static void AddStroke(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b, float halfThick)
    {
        Vector2 dir  = (b - a).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x) * halfThick;

        int i0 = verts.Count;
        verts.Add(new Vector3(a.x + perp.x, a.y + perp.y, 0f));
        verts.Add(new Vector3(a.x - perp.x, a.y - perp.y, 0f));
        verts.Add(new Vector3(b.x - perp.x, b.y - perp.y, 0f));
        verts.Add(new Vector3(b.x + perp.x, b.y + perp.y, 0f));

        tris.Add(i0);     tris.Add(i0 + 2); tris.Add(i0 + 1);
        tris.Add(i0);     tris.Add(i0 + 3); tris.Add(i0 + 2);
    }
}
