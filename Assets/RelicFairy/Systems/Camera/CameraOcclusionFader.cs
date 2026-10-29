using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라~플레이어 사이 오브젝트를 반투명하게 페이드.
///
/// 최적화:
///   - SphereCastNonAlloc + 고정 버퍼 → 매 프레임 GC 제로
///   - MaterialPropertyBlock 캐시 → 프레임당 1개 재사용
///   - toRemove List 재사용 → 힙 할당 없음
///   - 렌더러 캐시 (콜라이더 → Renderer[]) → GetComponentsInChildren 반복 방지
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraOcclusionFader : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────────────
    private const int MaxHits = 32;
    private const float NearMinHeight = 1.5f;    // 근거리 판정은 이만큼 높은 구조물만(촛대 · 잔해 · 보상 오브젝트 같은 작은 것은 빼고)
    private const float NearHoldSeconds = 0.35f; // 근거리로 흐린 것은 이만큼 붙잡는다 — 캡슐 경계에서 들락날락 깜박이지 않게
    // 머리 위 가림(10-25 사용자 「카메라를 늘리면 천장 쪽 오브젝트도 카메라를 사이에 두면 투명 처리」) — 천장 타일은 Ground 층이고
    // 촛대 · 장식 · 문 상인방은 콜라이더가 없거나 마스크 밖이라 구 판정(SphereCast)에 안 걸린다. 방이 드러날 때 그 방을 둘로 모은다:
    //  · 천장(Ground 층 · 높이 있는 것 · MapBuilder 천장 조각 이름) — 카메라가 천장 아랫면 근처 · 위로 올라가면 방 천장을 통째로 걷는다(그림자는 남김).
    //    이름까지 보는 까닭(10-26): 손으로 지은 방(리치 대기방 Plaza_Boss_Ch4 — 출구가 12 m 위 계단 꼭대기)의 윗층 바닥 · 계단참도 Ground 층 ·
    //    바닥 + 3 m 위라 천장으로 잡혀 통째로 숨었다(사용자 「대기방 바닥들이 날아갔다」).
    //    시선이 닿는 칸만 흐리면 화면이 천장 윗면으로 덮이고 플레이어 자리만 구멍으로 뚫렸다(Ch4 최대 거리 실측).
    //  · 떠 있는 것(아래 끝이 바닥 + OverheadClear 위 — 벽에 걸린 것 · 천장에 달린 것 · 상인방) — 카메라 → 플레이어 선분이 지나면 흐린다.
    //    판정은 렌더러 자기 좌표의 상자 — 월드 축 상자는 돌려 놓은 장식에서 실제보다 커 헛걸린다. 벽(Wall 층)은 구 판정 몫이라 뺀다.
    private const float OverheadClear    = 1f;     // 바닥에 선 것(바닥 · 기둥 · 석상 · 가구 · 벽 아랫단)은 빼고 — 구 판정 몫
    private const float OverheadMaxSize  = 40f;    // 방 전체를 감싸는 껍데기(탑 벽 · 하늘 돔)는 빼고
    private const float CeilingClear     = 3f;     // Ground 층 중 아래 끝이 바닥 + 이만큼 위 = 천장(아래는 바닥 타일)
    private const float CeilingRestoreExtra = 0.5f;   // 10-29 되돌림 여유
    private const float CeilingCamMargin = 0.5f;   // 카메라가 천장 아랫면 − 이만큼 위면 걷는다(가까운 면에 잘리는 것까지)
    private const int   GroundLayer      = 3;      // MapBuilder.BuildCeiling · 복도 천장 층
    // MapBuilder가 천장 조각에 붙이는 이름 앞머리 — 방 천장 · 문 복도 · 끝 챔버 · 챕터 계단 회랑(계단 바닥 「Stair_F_」 · 앞면 「Stair_R_」은 아님)
    private static readonly string[] CeilingNames = { "Ceiling_", "Corridor_C_", "Chamber_C_", "Stair_C_" };
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapID   = Shader.PropertyToID("_BaseMap");
    private static readonly int BumpMapID   = Shader.PropertyToID("_BumpMap");

    // 원본 → 페이드용 URP Lit로 옮길 때 찾아볼 프로퍼티 이름들(앞에서부터 먼저 맞는 것 1개).
    //
    // 이 프로젝트의 석재·목재는 대부분 커스텀 ShaderGraph라 URP 표준 이름을 쓰지 않는다:
    //   · Gothic_Interior(봉인 석문 4종) = _BaseColorMap / _BaseColor
    //   · Leartes S_Masking(숲 봉인문 뿌리) = _Base, 색 프로퍼티 없음
    //   · Gothic_Interior 셰이더그래프(S_OpaqueORM · WorldAlign · TransparentORM — 방 벽 · 철창 · 나무문) = Texture2D_2A1A8040(「Material 01 Base Map」) /
    //     Texture2D_7EA392D1(「Material 01 Normal」) — 10-09 전주기 시뮬
    // _BaseMap만 보면 전부 실패해 텍스처 없는 흰 판으로 페이드된다.
    private static readonly string[] TextureProps = { "_BaseMap", "_BaseColorMap", "_MainTex", "_Base", "_Albedo", "Texture2D_2A1A8040" };
    private static readonly string[] ColorProps   = { "_BaseColor", "_Color", "_BaseTintColor", "_MainColor" };
    private static readonly string[] NormalProps  = { "_BumpMap", "_NormalMap", "_Normal", "Texture2D_7EA392D1" };

    // 지금 켜진 페이더 — 방 밖 구조물(출구 문틀 · 챕터 계단 회랑)이 생길 때 그쪽에서 알린다(10-26)
    private static CameraOcclusionFader s_active;

    // ── SerializeField ────────────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private LayerMask occlusionMask = ~0;
    [SerializeField] private float     castRadius    = 0.3f;
    [Tooltip("플레이어 둘레 — 카메라에서 플레이어 좌우 · 앞뒤 바닥으로 이만큼(m) 떨어진 자리로도 구 판정을 쏜다(카메라 쪽은 좁고 플레이어 쪽은 넓은 원뿔). 0 = 가운데 하나만")]
    [SerializeField] private float     aroundRadius  = 2.5f;
    [Tooltip("카메라 앞 근거리 판정 — 카메라에서 플레이어 쪽으로 이 거리(m)까지(플레이어까지의 절반을 넘지 않음). 0 = 끔")]
    [SerializeField] private float     nearDistance  = 0f;
    [Tooltip("카메라 앞 근거리 판정 반경(m) — 카메라→플레이어 선 위가 아니어도 카메라 바로 옆 큰 구조물(회랑 기둥)을 잡는다")]
    [SerializeField] private float     nearRadius    = 2.5f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float hiddenAlpha = 0.15f;
    [Tooltip("카메라 앞 근거리로 흐린 것의 알파 — 화면 가장자리를 크게 덮는 구조물이라 더 옅게(밝게 떠서 눈을 끌지 않게)")]
    [SerializeField, Range(0f, 1f)] private float nearHiddenAlpha = 0.06f;
    [SerializeField]                private float fadeSpeed    = 8f;

    // ── Private fields ────────────────────────────────────────────────────
    private Transform _playerTransform;

    // NonAlloc 물리 버퍼 — 고정 크기, 재사용
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[MaxHits];
    private readonly Collider[]   _nearBuffer = new Collider[MaxHits];
    private readonly Dictionary<Renderer, float> _nearHoldUntil = new();

    // 콜라이더 → 렌더러 배열 캐시 (GetComponentsInChildren 반복 방지)
    private readonly Dictionary<Collider, Renderer[]> _rendererCache = new();

    // 페이드 상태 테이블
    private readonly Dictionary<Renderer, FadeState> _fading = new();

    // 머리 위 가림 후보 — 방이 드러날 때 모은 고정 렌더러 · 월드 상자 · 자기 좌표 상자 · 월드 → 자기 좌표(고정 지형이라 한 번만 잰다)
    private readonly List<Renderer>  _overhead = new();
    private readonly List<Bounds>    _overheadWorld = new();
    private readonly List<Bounds>    _overheadLocal = new();
    private readonly List<Matrix4x4> _overheadToLocal = new();

    // 지금 방 천장 — 걷을 때 원래 그림자 설정을 되돌리려고 같이 둔다
    private readonly List<Renderer> _ceiling = new();
    private readonly List<UnityEngine.Rendering.ShadowCastingMode> _ceilingShadow = new();
    private float _ceilingMinY = float.MaxValue;
    private bool  _ceilingHidden;
    private float _areaFloorY;   // 지금 모은 곳의 바닥 높이 — 뒤에 생기는 문틀을 같은 기준으로 가른다
    private int   _wallLayer = -1;

    // 이번 프레임 히트 렌더러
    private readonly HashSet<Renderer> _hitThisFrame = new();

    // UpdateFading 내 재사용 리스트
    private readonly List<Renderer> _toRemove = new();

    // 재사용 MaterialPropertyBlock (프레임당 1개) — Awake에서 초기화
    private MaterialPropertyBlock _mpb;

    // 페이드용 투명 셰이더 (URP Lit). 원본이 Opaque 전용 ShaderGraph라도 확실히 반투명 처리하기 위해
    // 원본 클론 대신 이 셰이더로 스왑한다. Awake에서 1회 캐싱.
    private Shader _fadeShader;

    private class FadeState
    {
        public Material[] originals;
        public Material[] faded;
        public float      currentAlpha;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _fadeShader = Shader.Find("Universal Render Pipeline/Lit");
        _wallLayer = LayerMask.NameToLayer("Wall");
    }

    private void OnEnable()
    {
        // 디졸브가 원본을 기억할 때 이 페이더가 씌운 임시 재질 대신 진짜 원본을 받게 한다(10-01)
        DissolveEffect.OriginalMaterialsResolver = ResolveOriginals;
        RunFlowController.RoomRevealed += CollectOverhead;
        s_active = this;
    }

    private void OnDisable()
    {
        if (DissolveEffect.OriginalMaterialsResolver == (System.Func<Renderer, Material[]>)ResolveOriginals)
            DissolveEffect.OriginalMaterialsResolver = null;
        RunFlowController.RoomRevealed -= CollectOverhead;
        SetCeilingHidden(false);
        if (s_active == this) s_active = null;
    }

    private void Start()
    {
        // 플레이어 (재)스폰 시마다 타깃 갱신 — 허브 유물 재스폰·전투 존 재스폰에서 이전 타깃이 파괴되므로.
        if (Managers.Player != null) Managers.Player.OnPlayerSpawned += SetTarget;
        SubscribePlayer();
    }

    private void OnDestroy()
    {
        if (Managers.Player != null) Managers.Player.OnPlayerSpawned -= SetTarget;
        RestoreAll();
    }

    private void LateUpdate()
    {
        if (_playerTransform == null) return;
        _hitThisFrame.Clear();
        DetectOccluders();
        UpdateFading();
    }

    // ── Public ────────────────────────────────────────────────────────────

    public void SetTarget(Transform player) => _playerTransform = player;

    /// <summary>
    /// 방이 드러난 뒤 생긴 고정 구조물을 떠 있는 것 후보에 보탠다 — 출구 문틀(기둥 · 상인방 · 화로, 10-26 사용자 「문 위 천장이 안 흐려진다」).
    /// 문틀은 콜라이더를 지워 구 판정에 안 걸리고, 방 밖(RunFlowController 아래)에 서서 방을 모을 때도 빠졌다. Wall 층이어도 받는다.
    /// </summary>
    public static void AddOverheadArea(GameObject root)
    {
        if (s_active == null || root == null) return;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            s_active.AddOverheadCandidate(r, s_active._areaFloorY, allowWall: true);
    }

    /// <summary>
    /// 방 밖의 서는 곳을 지금 방처럼 다시 모은다 — 챕터 계단 회랑(10-26 사용자 「다음 챕터로 넘어가는 통로 천장이 안 걷힌다」).
    /// 계단이라 바닥이 7 m까지 오르지만 천장은 이름(<see cref="CeilingNames"/>)으로 가려 계단 바닥은 걷히지 않는다.
    /// </summary>
    public static void CollectArea(GameObject root)
    {
        if (s_active != null) s_active.CollectRoot(root, null);
    }

    // ── Private ───────────────────────────────────────────────────────────

    private void SubscribePlayer()
    {
        if (GameRunBootstrapper.Instance?.Run?.Player != null)
        {
            _playerTransform = GameRunBootstrapper.Instance.Run.Player.transform;
            return;
        }
        if (Managers.Player?.PlayerTransform != null)
        {
            _playerTransform = Managers.Player.PlayerTransform;
            return;
        }
        WaitForPlayerAsync().Forget();
    }

    private async Cysharp.Threading.Tasks.UniTaskVoid WaitForPlayerAsync()
    {
        var ct = destroyCancellationToken;
        await Cysharp.Threading.Tasks.UniTask.WaitUntil(
            () => GameRunBootstrapper.Instance?.Run?.Player != null
               || Managers.Player?.PlayerTransform != null,
            cancellationToken: ct);
        if (GameRunBootstrapper.Instance?.Run?.Player != null)
            _playerTransform = GameRunBootstrapper.Instance.Run.Player.transform;
        else if (Managers.Player?.PlayerTransform != null)
            _playerTransform = Managers.Player.PlayerTransform;
    }

    private void DetectOccluders()
    {
        var   camPos    = transform.position;
        var   playerPos = _playerTransform.position + Vector3.up * 1f;
        var   dir       = playerPos - camPos;
        float dist      = dir.magnitude;
        if (dist < 0.1f) return;

        CastTo(camPos, playerPos);
        // 둘레 — 화면 좌우(가슴 높이) · 위아래(먼 쪽 · 가까운 쪽 바닥). 가운데 한 줄만 보면 벽에 붙었을 때 플레이어 자리만 뚫려
        // 둘레가 안 보였다(10-26 사용자 「벽에 붙으면 플레이어만 보여서 주변이 안 보여 — 지원 범위를 늘려야」)
        if (aroundRadius > 0f)
        {
            Vector3 right = transform.right; right.y = 0f;
            if (right.sqrMagnitude > 1e-4f)
            {
                right.Normalize();
                Vector3 fwd  = Vector3.Cross(right, Vector3.up);   // 카메라가 보는 쪽(땅 위)
                Vector3 feet = _playerTransform.position + Vector3.up * 0.3f;
                CastTo(camPos, playerPos + right * aroundRadius, true);
                CastTo(camPos, playerPos - right * aroundRadius, true);
                CastTo(camPos, feet + fwd * aroundRadius, true);
                CastTo(camPos, feet - fwd * (aroundRadius * 0.6f), true);
            }
        }

        DetectOverhead(camPos, playerPos);

        // 카메라 앞 근거리 — 카메라 바로 옆 큰 구조물은 플레이어와 한 줄이 아니어도 화면 한쪽을 통째로 가린다
        // (10-01 f5 · 09-25 G21: 기사 아레나 가장자리에서 회랑 기둥이 화면 35~40%). 플레이어까지의 절반에서 멈춰 바닥은 안 닿는다.
        if (nearDistance > 0f)
        {
            float   reach = Mathf.Min(nearDistance, dist * 0.5f);
            Vector3 end   = camPos + dir / dist * reach;
            int near = Physics.OverlapCapsuleNonAlloc(camPos, end, nearRadius, _nearBuffer, occlusionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < near; i++)
            {
                var col = _nearBuffer[i];
                // 움직이는 것(리지드바디) · 낮은 것은 빼고 큰 고정 구조물(기둥 · 벽)만
                if (col == null || col.attachedRigidbody != null || col.bounds.size.y < NearMinHeight) continue;
                MarkOccluder(col, true);
            }
        }
    }

    /// <summary>
    /// 방이 드러날 때 — 그 방 천장과 떠 있는 고정 렌더러를 모은다(몬스터 · 투사체는 나중에 생겨 안 들어온다).
    /// 바닥 기준은 방 뿌리 높이(MapBuilder가 바닥 · 천장을 방 기준 높이로 짓는다). 문 상인방은 방 밖(StartRoomGate)에 붙어 따로 모은다.
    /// </summary>
    private void CollectOverhead(GameObject room) => CollectRoot(room, FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None));

    private void CollectRoot(GameObject room, StartRoomGate[] gates)
    {
        SetCeilingHidden(false);   // 앞 방 천장은 되돌린다
        _ceiling.Clear();
        _ceilingShadow.Clear();
        _ceilingMinY = float.MaxValue;
        _overhead.Clear();
        _overheadWorld.Clear();
        _overheadLocal.Clear();
        _overheadToLocal.Clear();
        if (room == null) return;

        float floorY = room.transform.position.y;
        _areaFloorY = floorY;
        foreach (var r in room.GetComponentsInChildren<MeshRenderer>())
            AddOverheadCandidate(r, floorY, room.transform);
        if (gates == null) return;
        foreach (var gate in gates)
            foreach (var r in gate.GetComponentsInChildren<MeshRenderer>())
                AddOverheadCandidate(r, gate.transform.position.y, gate.transform);
    }

    private void AddOverheadCandidate(MeshRenderer r, float floorY, Transform root = null, bool allowWall = false)
    {
        // 10-29 꺼진 렌더러도 모은다 — 들어설 때 디졸브 파도가 아직 안 닿은 먼 통로 천장이 빠졌다(걷기는 forceRenderingOff라 enabled와 무관)
        if (r == null) return;
        if (_ceiling.Contains(r) || _overhead.Contains(r)) return;   // 10-29 문틀을 다시 보탤 때 겹치지 않게
        var b = r.bounds;
        if (b.size.x > OverheadMaxSize || b.size.y > OverheadMaxSize || b.size.z > OverheadMaxSize) return;
        int layer = r.gameObject.layer;
        if (layer == GroundLayer)
        {
            if (b.min.y < floorY + CeilingClear || !HasCeilingName(r.transform, root)) return;   // 바닥 타일 · 윗층 바닥 · 계단참
            _ceiling.Add(r);
            _ceilingShadow.Add(r.shadowCastingMode);
            _ceilingMinY = Mathf.Min(_ceilingMinY, b.min.y);
            return;
        }
        if ((layer == _wallLayer && !allowWall) || b.min.y < floorY + OverheadClear) return;   // 벽 = 구 판정 몫 · 바닥에 선 것
        _overhead.Add(r);
        _overheadWorld.Add(b);
        _overheadLocal.Add(r.localBounds);
        _overheadToLocal.Add(r.transform.worldToLocalMatrix);
    }

    /// <summary>렌더러 · 그 부모(뿌리 앞까지) 중 하나가 MapBuilder 천장 조각 이름인가 — 렌더러가 조각 프리팹 안쪽에 있어도 조각 이름을 본다.</summary>
    private static bool HasCeilingName(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            foreach (var prefix in CeilingNames)
                if (t.name.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>방 천장을 걷거나(그림자만 남김) 되돌린다 — 상태가 바뀔 때만 손댄다.</summary>
    private void SetCeilingHidden(bool hide)
    {
        if (hide == _ceilingHidden) return;
        _ceilingHidden = hide;
        for (int i = 0; i < _ceiling.Count; i++)
        {
            var r = _ceiling[i];
            if (r == null) continue;
            var original = _ceilingShadow[i];
            if (original == UnityEngine.Rendering.ShadowCastingMode.Off) r.forceRenderingOff = hide;   // 그림자도 안 드리우던 것은 그냥 끈다
            else r.shadowCastingMode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : original;
        }
    }

    /// <summary>
    /// 천장 — 카메라가 천장 아랫면 근처 · 위면 방 천장을 통째로 걷는다.
    /// 떠 있는 것 — 카메라 → 플레이어 선분이 지나는 것을 흐린다(선분 상자와 겹치는 것만 자기 좌표 상자로 다시 본다 · 콜라이더 · 층 무관).
    /// </summary>
    private void DetectOverhead(Vector3 camPos, Vector3 target)
    {
        // 10-29 걷은 뒤엔 0.5 m 더 내려와야 되돌린다 — 궤도 높이가 천장 아랫면 근처인 팔레트에서 경계에 깜박였다
        float margin = _ceilingHidden ? CeilingCamMargin + CeilingRestoreExtra : CeilingCamMargin;
        SetCeilingHidden(_ceiling.Count > 0 && camPos.y > _ceilingMinY - margin);

        int n = _overhead.Count;
        if (n == 0) return;
        float   minY   = _playerTransform.position.y + OverheadClear;
        Vector3 segMin = Vector3.Min(camPos, target), segMax = Vector3.Max(camPos, target);
        for (int i = 0; i < n; i++)
        {
            var wb = _overheadWorld[i];
            if (wb.min.y < minY
                || wb.max.x < segMin.x || wb.min.x > segMax.x
                || wb.max.y < segMin.y || wb.min.y > segMax.y
                || wb.max.z < segMin.z || wb.min.z > segMax.z) continue;
            var   m     = _overheadToLocal[i];
            var   from  = m.MultiplyPoint3x4(camPos);
            var   along = m.MultiplyVector(target - camPos);   // 자기 좌표에서의 선분(크기 = 그 좌표의 선분 길이)
            float len   = along.magnitude;
            if (len < 1e-4f || !_overheadLocal[i].IntersectRay(new Ray(from, along / len), out float t) || t > len) continue;
            var r = _overhead[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || DissolveEffect.IsDissolving(r)) continue;
            _hitThisFrame.Add(r);
            if (!_fading.ContainsKey(r))
                BeginFade(r);
        }
    }

    /// <summary>
    /// 카메라 → 한 자리 구 판정 — 걸린 콜라이더를 흐린다(NonAlloc · _hitBuffer 재사용, GC 제로).
    /// 둘레 판정(<paramref name="around"/>)은 근거리 판정과 같이 큰 고정 구조물만 — 움직이는 것(몬스터 · 투사체) · 낮은 소품은 빼서 깜박이지 않게.
    /// </summary>
    private void CastTo(Vector3 from, Vector3 to, bool around = false)
    {
        var   d   = to - from;
        float len = d.magnitude;
        if (len < 0.1f) return;
        int count = Physics.SphereCastNonAlloc(from, castRadius, d / len, _hitBuffer, len, occlusionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var col = _hitBuffer[i].collider;
            if (around && (col == null || col.attachedRigidbody != null || col.bounds.size.y < NearMinHeight)) continue;
            MarkOccluder(col);
        }
    }

    private void MarkOccluder(Collider col, bool near = false)
    {
        if (col == null) return;
        if (col.gameObject == _playerTransform.gameObject) return;

        // 렌더러 캐시 조회 (최초 1회만 GetComponentsInChildren 호출)
        if (!_rendererCache.TryGetValue(col, out var renderers))
        {
            renderers = col.GetComponentsInChildren<Renderer>();
            _rendererCache[col] = renderers;
        }

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // 꺼진 렌더러(방을 짓는 동안 숨긴 블록) · 디졸브 중인 렌더러는 잡지 않는다 — 그 순간의 재질(디졸브 임시 재질)을
            // 원본으로 기억하거나, 곧 시작할 입장 디졸브가 이 페이더의 임시 재질을 원본으로 기억하면 되돌릴 때 빈 재질(마젠타)이 된다.
            // (10-01 Ch1 상점·정제소 — 입장 연출이 없어 벽이 숨김 · 디졸브 중일 때 카메라와 플레이어 사이에 들었다)
            if (!r.enabled || DissolveEffect.IsDissolving(r)) continue;
            _hitThisFrame.Add(r);
            if (near) _nearHoldUntil[r] = Time.unscaledTime + NearHoldSeconds;
            if (!_fading.ContainsKey(r))
                BeginFade(r);
        }
    }

    private void UpdateFading()
    {
        _toRemove.Clear();

        foreach (var kv in _fading)
        {
            var r = kv.Key;
            if (r == null) { _toRemove.Add(r); continue; }

            var   data       = kv.Value;

            // 디졸브가 이 렌더러를 잡아 갔다 — 디졸브는 우리 원본(ResolveOriginals)을 기억했으니 되돌리지 않고 손을 뗀다
            if (DissolveEffect.IsDissolving(r))
            {
                foreach (var m in data.faded) if (m != null) Object.Destroy(m);
                _toRemove.Add(r);
                continue;
            }

            bool  shouldHide = _hitThisFrame.Contains(r)
                            || (_nearHoldUntil.TryGetValue(r, out float holdUntil) && Time.unscaledTime < holdUntil);
            float target     = shouldHide ? (_nearHoldUntil.ContainsKey(r) ? nearHiddenAlpha : hiddenAlpha) : 1f;
            data.currentAlpha = Mathf.MoveTowards(data.currentAlpha, target, fadeSpeed * Time.deltaTime);

            ApplyAlpha(r, data.faded, data.currentAlpha);

            if (!shouldHide && Mathf.Approximately(data.currentAlpha, 1f))
            {
                RestoreRenderer(r, data);
                _toRemove.Add(r);
            }
        }

        for (int i = 0; i < _toRemove.Count; i++)
        {
            _fading.Remove(_toRemove[i]);
            _nearHoldUntil.Remove(_toRemove[i]);
        }
    }

    private void BeginFade(Renderer r)
    {
        var origMats  = r.sharedMaterials;
        var fadedMats = new Material[origMats.Length];
        for (int i = 0; i < origMats.Length; i++)
        {
            var orig = origMats[i];

            // 원본을 클론해 _Surface만 토글하면 Opaque 전용 ShaderGraph(예: BaseCamp 석재
            // S_OpaqueORMWorldAlign)는 투명 패스가 컴파일돼 있지 않아 무시된다(불투명 그대로).
            // → URP Lit(투명 패스 내장)로 스왑하고 베이스맵/색/노멀만 복사해 어떤 셰이더든 확실히 반투명화.
            Material copy;
            if (_fadeShader != null)
            {
                copy = new Material(_fadeShader);
                if (orig != null)
                {
                    CopyFirstTexture(orig, copy, TextureProps, BaseMapID);
                    CopyFirstTexture(orig, copy, NormalProps,  BumpMapID);
                    CopyFirstColor(orig, copy, ColorProps, BaseColorID);
                }
            }
            else
            {
                // URP Lit 미발견 시 기존 경로 폴백(원본 클론 + _Surface 토글)
                copy = orig != null ? new Material(orig) : null;
            }

            if (copy != null) MakeTransparent(copy);
            fadedMats[i] = copy;
        }
        r.materials = fadedMats;

        _fading[r] = new FadeState
        {
            originals    = origMats,
            faded        = fadedMats,
            currentAlpha = 1f,
        };
    }

    /// <summary>후보 중 원본이 가진 첫 텍스처를 페이드 머티리얼의 dstId로 옮긴다. 없으면 아무것도 안 한다.</summary>
    private static void CopyFirstTexture(Material orig, Material copy, string[] candidates, int dstId)
    {
        if (!copy.HasProperty(dstId)) return;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!orig.HasProperty(candidates[i])) continue;
            var tex = orig.GetTexture(candidates[i]);
            if (tex == null) continue;
            copy.SetTexture(dstId, tex);
            return;
        }
    }

    /// <summary>후보 중 원본이 가진 첫 색을 옮긴다. 알파는 페이드가 매 프레임 덮어쓰므로 무시한다.</summary>
    private static void CopyFirstColor(Material orig, Material copy, string[] candidates, int dstId)
    {
        if (!copy.HasProperty(dstId)) return;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!orig.HasProperty(candidates[i])) continue;
            copy.SetColor(dstId, orig.GetColor(candidates[i]));
            return;
        }
    }

    // _mpb 재사용 — new MaterialPropertyBlock() 없음
    private void ApplyAlpha(Renderer r, Material[] mats, float alpha)
    {
        foreach (var m in mats)
        {
            if (m == null) continue;
            // _BaseColor 없는 커스텀 ShaderGraph(S_Masking 등) 스킵 — 매 프레임 오류 방지
            if (!m.HasProperty(BaseColorID)) continue;
            var c = m.GetColor(BaseColorID);
            c.a   = alpha;
            m.SetColor(BaseColorID, c);
        }
        // SRP Batcher와 호환되는 방식으로 MPB 적용
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetFloat("_Surface", 1f);
        r.SetPropertyBlock(_mpb);
    }

    private static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface",   1f);
        m.SetFloat("_Blend",     0f);
        m.SetFloat("_AlphaClip", 0f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite",   0);

        // 새 URP Lit의 기본 매끄러움 0.5가 직사광을 되비춰 흐린 기둥이 번들거렸다(10-01 f5) — 거칠게.
        // 키워드(정반사 · 환경 반사 끄기)는 쓰지 않는다: 새 변형이라 에디터에선 컴파일 동안 청록 대체 셰이더로 뜨고, 빌드에선 걸러질 수 있다.
        m.SetFloat("_Smoothness", 0f);
    }

    /// <summary>디졸브가 묻는다 — 이 페이더가 임시 재질을 씌운 렌더러면 그 원본, 아니면 null.</summary>
    private Material[] ResolveOriginals(Renderer r)
        => r != null && _fading.TryGetValue(r, out var data) ? data.originals : null;

    private static void RestoreRenderer(Renderer r, FadeState data)
    {
        if (r != null) r.sharedMaterials = data.originals;
        foreach (var m in data.faded)
            if (m != null) Object.Destroy(m);
    }

    private void RestoreAll()
    {
        foreach (var kv in _fading)
            RestoreRenderer(kv.Key, kv.Value);
        _fading.Clear();
        _rendererCache.Clear();
        SetCeilingHidden(false);
    }
}
