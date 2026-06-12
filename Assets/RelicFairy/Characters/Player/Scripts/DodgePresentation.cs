using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 회피(닷지) 시각 연출 — 잔상(afterimage) / i-frame 색 틴트 / 대시 먼지·트레일.
/// 설계: docs/reviews/dodge-feel-design.md §5-2.
///
/// PlayerController가 런타임에 자동 부착한다(프리팹/씬 수동 배선 불가 환경 대응).
/// 모든 시각 자원 참조는 CharacterData의 optional 필드에서 읽으며, 미할당 시 해당 효과만 무동작한다
/// (참조 0 → NRE 0 → 현행 회피 동작 회귀 0).
///
/// 정적(static) 상태를 보유하지 않는다 — 잔상 풀/세대 카운터는 모두 인스턴스 범위라
/// 플레이어 인스턴스와 함께 재생성된다. 따라서 도메인 리로드 OFF 2회차에도 잔류 버그가 없어
/// 별도 ResetStatics가 필요 없다(DissolveEffect/HitFeelService의 static 풀과 다른 점).
///
/// 신호 짝(설계 §5-2): OnDodgeIFrame(true)=잔상 스폰 시작 + 틴트 적용,
/// OnDodgeIFrame(false)=잔상 스폰 중지 + 틴트 즉시 원복. 회피 시작=먼지 1회+트레일 ON, 종료=트레일 OFF.
/// </summary>
[DisallowMultipleComponent]
public class DodgePresentation : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string GhostObjectName = "~DodgeGhost";

    // ── Private (refs, Awake 캐싱) ─────────────────────────────────
    private PlayerController     _controller;
    private CharacterData        _data;
    private SkinnedMeshRenderer[] _renderers;
    private TrailRenderer        _trail;             // 있으면 사용, 없으면 트레일 스킵
    private MaterialPropertyBlock _mpb;

    // ── Private (틴트) ────────────────────────────────────────────
    private bool _tintActive;

    // ── Private (잔상 풀 — 인스턴스 범위) ──────────────────────────
    private sealed class Ghost
    {
        public GameObject go;
        public MeshRenderer renderer;
        public Mesh mesh;
        public MaterialPropertyBlock mpb;
    }

    private readonly Stack<Ghost> _ghostPool = new();
    private readonly List<Ghost>  _allGhosts = new();   // 풀+활성 전체 — OnDestroy에서 Mesh/GO 파괴
    private bool _ghostSpawnActive;
    private int  _ghostGen;   // 스폰 루프 무효화용 세대 카운터(토글/중단 안전)

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _data       = _controller != null ? _controller.CharacterData : null;
        _renderers  = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _trail      = GetComponentInChildren<TrailRenderer>(true);
        _mpb        = new MaterialPropertyBlock();

        // 트레일은 회피 중에만 켠다 — 시작 상태는 항상 OFF로 강제(인스펙터에서 켜둔 상태 방어).
        DisableTrail();
    }

    private void OnEnable()
    {
        if (_controller == null) return;
        _controller.OnDodgeIFrame += HandleDodgeIFrame;
        _controller.OnDodgeStart  += HandleDodgeStart;
        _controller.OnDodgeEnd    += HandleDodgeEnd;
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnDodgeIFrame -= HandleDodgeIFrame;
            _controller.OnDodgeStart  -= HandleDodgeStart;
            _controller.OnDodgeEnd    -= HandleDodgeEnd;
        }

        // 비활성/풀 반환 시 시각 잔류 0 보장 — 틴트 원복 + 스폰 중지 + 트레일 OFF.
        ClearIFrameTint();
        StopGhostSpawn();
        DisableTrail();
    }

    private void OnDestroy()
    {
        _ghostSpawnActive = false;
        for (int i = 0; i < _allGhosts.Count; i++)
        {
            var g = _allGhosts[i];
            if (g == null) continue;
            if (g.mesh != null) Destroy(g.mesh);
            if (g.go   != null) Destroy(g.go);
        }
        _allGhosts.Clear();
        _ghostPool.Clear();
    }

    // ── Event Handlers ────────────────────────────────────────────
    // OnDodgeIFrame ON → 잔상 스폰 시작 + 틴트, OFF → 스폰 중지 + 틴트 원복 (1:1, 중복 토글 안전).
    private void HandleDodgeIFrame(bool active)
    {
        if (active)
        {
            ApplyIFrameTint();
            StartGhostSpawn();
        }
        else
        {
            ClearIFrameTint();
            StopGhostSpawn();
        }
    }

    private void HandleDodgeStart()
    {
        SpawnDust();
        EnableTrail();
    }

    private void HandleDodgeEnd() => DisableTrail();

    // 플레이어 비주얼이 한 프레임 늦게(유물 외형 등) 붙는 경우 대비 — 비어 있으면 1회 재수집.
    private void EnsureRenderers()
    {
        if (_renderers != null && _renderers.Length > 0) return;
        _renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    // ── i-frame 색 틴트 (MPB) ─────────────────────────────────────
    // 색 미설정(a≤0)이면 무동작. 플레이어 렌더러에는 다른 MPB writer가 없어(피격 플래시는 HUD 비네트)
    // 단독 소유 — OFF 시 SetPropertyBlock(null)로 머티리얼 기본값 복원(VictimHitFeedback와 동일 패턴).
    private void ApplyIFrameTint()
    {
        if (_data == null) return;
        EnsureRenderers();
        if (_renderers == null || _renderers.Length == 0) return;
        Color tint = _data.dodgeIFrameTint;
        if (tint.a <= 0.001f) return;   // 색 미설정 → 틴트 스킵

        float strength = Mathf.Clamp01(tint.a);
        Color tintRgb  = new Color(tint.r, tint.g, tint.b, 1f);
        Color baseC    = Color.Lerp(Color.white, tintRgb, strength);
        baseC.a = 1f;   // 불투명 유지(베이스 알파로 인한 의도치 않은 반투명 방지)
        Color emis = tintRgb * (_data.dodgeIFrameTintEmission * strength);

        _mpb.Clear();
        _mpb.SetColor(BaseColorId, baseC);
        _mpb.SetColor(EmissionColorId, emis);
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].SetPropertyBlock(_mpb);

        _tintActive = true;
    }

    private void ClearIFrameTint()
    {
        if (!_tintActive || _renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
        _tintActive = false;
    }

    // ── 잔상(afterimage) ──────────────────────────────────────────
    private void StartGhostSpawn()
    {
        if (_data == null || _data.dodgeGhostMaterial == null) return;   // 머티리얼 미할당 → 잔상 스킵
        EnsureRenderers();
        if (_renderers == null || _renderers.Length == 0) return;

        _ghostSpawnActive = true;
        int gen = ++_ghostGen;
        GhostSpawnLoopAsync(gen).Forget();
    }

    // 스폰만 중지 — 이미 떠 있는 잔상은 각자 수명까지 페이드 후 풀로 반환된다(신호가 무적보다 오래 남지 않게 스폰은 즉시 멈춤).
    private void StopGhostSpawn()
    {
        _ghostSpawnActive = false;
        _ghostGen++;   // 진행 중 스폰 루프를 다음 yield에서 무효화
    }

    private async UniTaskVoid GhostSpawnLoopAsync(int gen)
    {
        var token = destroyCancellationToken;
        try
        {
            while (_ghostSpawnActive && gen == _ghostGen)
            {
                SpawnGhostSnapshot();
                float interval = Mathf.Max(0.01f, _data.dodgeGhostInterval);
                await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SpawnGhostSnapshot()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var smr = _renderers[i];
            if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;

            var g = RentGhost();
            if (g == null) continue;

            smr.BakeMesh(g.mesh);   // 풀 Mesh 재사용(매프레임 alloc 최소화)
            var st = smr.transform;
            g.go.transform.SetPositionAndRotation(st.position, st.rotation);
            g.go.transform.localScale = st.lossyScale;   // BakeMesh(mesh)는 스케일 미포함 → lossyScale로 보정

            GhostFadeAsync(g).Forget();
        }
    }

    // 잔상 1개의 알파 페이드아웃 후 풀 반환. 후속 회피와 독립(자기 수명까지 진행).
    private async UniTaskVoid GhostFadeAsync(Ghost g)
    {
        var token = destroyCancellationToken;
        float life = Mathf.Max(0.01f, _data.dodgeGhostLifetime);
        Color c    = _data.dodgeGhostColor;
        float startA = c.a;

        try
        {
            float t = 0f;
            while (t < life)
            {
                c.a = startA * (1f - t / life);
                g.mpb.SetColor(BaseColorId, c);
                g.renderer.SetPropertyBlock(g.mpb);
                t += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        catch (OperationCanceledException) { return; }   // 파괴 → OnDestroy가 일괄 정리

        ReturnGhost(g);
    }

    private Ghost RentGhost()
    {
        Ghost g = null;
        // 풀에 잔류한 파괴된(씬 언로드) 인스턴스(Unity-null)는 버린다.
        while (_ghostPool.Count > 0)
        {
            var cand = _ghostPool.Pop();
            if (cand != null && cand.go != null) { g = cand; break; }
        }
        if (g == null) g = CreateGhost();
        if (g == null) return null;
        g.go.SetActive(true);
        return g;
    }

    private Ghost CreateGhost()
    {
        if (_data == null || _data.dodgeGhostMaterial == null) return null;

        var go = new GameObject(GhostObjectName);   // 부모 없음 — 스폰 시점 위치에 고정(플레이어 따라가지 않음)
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial      = _data.dodgeGhostMaterial;
        mr.shadowCastingMode   = ShadowCastingMode.Off;
        mr.receiveShadows      = false;

        var mesh = new Mesh { name = "~DodgeGhostMesh" };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;

        var g = new Ghost { go = go, renderer = mr, mesh = mesh, mpb = new MaterialPropertyBlock() };
        _allGhosts.Add(g);
        return g;
    }

    private void ReturnGhost(Ghost g)
    {
        if (g == null) return;
        if (g.go != null) g.go.SetActive(false);
        _ghostPool.Push(g);
    }

    // ── 먼지 VFX (1회 스폰) ───────────────────────────────────────
    // 프리팹 미할당 → 스킵. 풀 미등록 일회성 VFX이므로 플레이어 자체 VFX(SpawnHitBloodVfx)와 동일하게
    // Instantiate + 파티클 수명 후 Destroy. 부모 없음 → 회피 출발 지점에 고정(대시로 멀어져도 그 자리).
    private void SpawnDust()
    {
        if (_data == null || _data.dodgeDustVfxPrefab == null) return;

        Vector3 pos = transform.position + Vector3.up * _data.dodgeDustHeightOffset;
        var go = Instantiate(_data.dodgeDustVfxPrefab, pos, _data.dodgeDustVfxPrefab.transform.rotation);

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float life = ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 2f;
        Destroy(go, life);
    }

    // ── 트레일 ────────────────────────────────────────────────────
    private void EnableTrail()
    {
        if (_trail == null) return;
        _trail.Clear();          // 직전 위치에서 이어지는 잔상 줄 방지
        _trail.emitting = true;
        _trail.enabled  = true;
    }

    private void DisableTrail()
    {
        if (_trail == null) return;
        _trail.emitting = false;
        _trail.enabled  = false;
    }
}
