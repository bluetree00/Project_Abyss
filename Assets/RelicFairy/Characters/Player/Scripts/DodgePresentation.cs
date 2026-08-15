using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using INab.Common;

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
    private const float  DashTrailFadeIn  = 0.02f;
    private const float  DashTrailFadeOut = 0.1f;

    // ── Private (refs, Awake 캐싱) ─────────────────────────────────
    private PlayerController     _controller;
    private CharacterData        _data;
    private SkinnedMeshRenderer[] _renderers;
    private TrailRenderer        _trail;             // 폴백 — dashTrailVfxPrefab 미할당 시 사용
    private WeaponTrailEffect    _dashTrailVfx;       // INab 대시 트레일 — dashTrailVfxPrefab 할당 시 우선
    private MaterialPropertyBlock _mpb;

    // ── Private (틴트) ────────────────────────────────────────────
    private bool _tintActive;

    // ── Private (저스트 회피 연출) ─────────────────────────────────
    // 공유 GameVolumeProfile은 전 챕터 공통이라 절대 건드리지 않는다.
    // 대신 런타임 전용 Volume을 우선순위 최상으로 띄웠다 걷어내는 방식으로 화면 채도를 뺀다.
    private Volume          _pdVolume;
    private VolumeProfile   _pdProfile;
    private ColorAdjustments _pdColor;
    private Vignette        _pdVignette;
    private bool  _perfectActive;          // 저스트 연출 진행 중 — i-frame OFF가 틴트/잔상을 걷어가지 않도록 보호
    private int   _perfectGen;             // 중첩 발동 시 이전 연출 무효화
    private float _ghostIntervalOverride = -1f;  // >0이면 이 간격으로 잔상 스폰
    private bool  _ghostUnscaled;                // 잔상 간격을 실제시간(슬로모 무시)으로 셀지

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
        _mpb        = new MaterialPropertyBlock();
        SetupDashTrail();   // INab 프리팹 할당 시 INab 트레일, 아니면 TrailRenderer 폴백

        // 트레일은 회피 중에만 켠다 — 시작 상태는 항상 OFF로 강제(인스펙터에서 켜둔 상태 방어).
        HardOffTrail();
    }

    private void OnEnable()
    {
        if (_controller == null) return;
        _controller.OnDodgeIFrame += HandleDodgeIFrame;
        _controller.OnDodgeStart  += HandleDodgeStart;
        _controller.OnDodgeEnd    += HandleDodgeEnd;
        _controller.OnPerfectDodge += HandlePerfectDodge;
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnDodgeIFrame -= HandleDodgeIFrame;
            _controller.OnDodgeStart  -= HandleDodgeStart;
            _controller.OnDodgeEnd    -= HandleDodgeEnd;
            _controller.OnPerfectDodge -= HandlePerfectDodge;
        }

        // 저스트 연출이 걸린 채 비활성화되면 화면이 회색으로 남는다 → 반드시 원복.
        EndPerfectFx();

        // 비활성/풀 반환 시 시각 잔류 0 보장 — 틴트 원복 + 스폰 중지 + 트레일 OFF.
        ClearIFrameTint();
        StopGhostSpawn();
        HardOffTrail();
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
            // 저스트 연출 중이면 i-frame이 끝나도 틴트/잔상을 걷지 않는다(연출이 자기 수명까지 소유).
            if (_perfectActive) return;
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

    // ── 저스트 회피 연출 ───────────────────────────────────────────
    // 레퍼런스: 베요네타 Witch Time — "세계는 변하고 플레이어는 안 변한다".
    // 화면 채도를 빼 세계를 회색으로 만들고(공유 프로파일은 건드리지 않고 런타임 Volume으로),
    // 플레이어에게만 발광 틴트를 입혀 회색 속에서 혼자 빛나게 한다. 잔상으로 속도 대비를 강조.
    private void HandlePerfectDodge(float duration)
    {
        if (_data == null || duration <= 0f) return;

        _perfectActive = true;
        int gen = ++_perfectGen;

        ApplyPerfectTint();

        // 잔상 — 실제시간 간격으로 촘촘히(슬로모라도 실제 화면에선 촘촘하게 보이도록).
        if (_data.perfectDodgeGhostInterval > 0f)
        {
            StopGhostSpawn();   // i-frame 잔상 루프를 접고 저스트 설정으로 다시 시작
            _ghostIntervalOverride = _data.perfectDodgeGhostInterval;
            _ghostUnscaled         = true;
            StartGhostSpawn();
        }

        PerfectFxAsync(gen, duration).Forget();
    }

    private async UniTaskVoid PerfectFxAsync(int gen, float duration)
    {
        var token = destroyCancellationToken;
        try
        {
            EnsurePerfectVolume();

            _pdColor.saturation.value   = _data.perfectDodgeSaturation;
            _pdVignette.intensity.value = _data.perfectDodgeVignette;

            float fadeIn  = Mathf.Max(0.01f, _data.perfectDodgeFadeIn);
            float fadeOut = Mathf.Max(0.01f, _data.perfectDodgeFadeOut);
            float hold    = Mathf.Max(0f, duration - fadeIn - fadeOut);

            // 모든 타이밍은 unscaled — 슬로모 중이라 스케일 시간을 쓰면 연출이 늘어져 버린다.
            float t = 0f;
            while (t < fadeIn && gen == _perfectGen)
            {
                t += Time.unscaledDeltaTime;
                _pdVolume.weight = Mathf.Clamp01(t / fadeIn);
                await UniTask.Yield(token);
            }
            if (gen != _perfectGen) return;
            _pdVolume.weight = 1f;

            float h = 0f;
            while (h < hold && gen == _perfectGen)
            {
                h += Time.unscaledDeltaTime;
                await UniTask.Yield(token);
            }
            if (gen != _perfectGen) return;

            t = 0f;
            while (t < fadeOut && gen == _perfectGen)
            {
                t += Time.unscaledDeltaTime;
                _pdVolume.weight = 1f - Mathf.Clamp01(t / fadeOut);
                await UniTask.Yield(token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (gen == _perfectGen) EndPerfectFx();
        }
    }

    // 연출 종료 — 화면/틴트/잔상 전부 원복. 중단(비활성/파괴/중첩 발동) 경로에서도 호출된다.
    private void EndPerfectFx()
    {
        if (!_perfectActive) return;
        _perfectActive = false;
        _perfectGen++;

        if (_pdVolume != null) _pdVolume.weight = 0f;

        _ghostIntervalOverride = -1f;
        _ghostUnscaled         = false;

        StopGhostSpawn();
        ClearIFrameTint();
    }

    // 회색 필터용 런타임 Volume. 공유 GameVolumeProfile 대신 최상위 우선순위로 덮었다 걷는다.
    private void EnsurePerfectVolume()
    {
        if (_pdVolume != null) return;

        var go = new GameObject("~PerfectDodgeVolume") { hideFlags = HideFlags.HideAndDontSave };
        _pdVolume = go.AddComponent<Volume>();
        _pdVolume.isGlobal = true;
        _pdVolume.priority = 1000f;   // 씬/챕터 볼륨보다 확실히 위
        _pdVolume.weight   = 0f;

        _pdProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _pdVolume.sharedProfile = _pdProfile;

        _pdColor = _pdProfile.Add<ColorAdjustments>(true);
        _pdColor.saturation.overrideState = true;

        _pdVignette = _pdProfile.Add<Vignette>(true);
        _pdVignette.intensity.overrideState = true;
        _pdVignette.color.overrideState     = true;
        _pdVignette.color.value             = Color.black;
    }

    // 저스트 전용 틴트 — i-frame 틴트와 같은 MPB 경로를 쓰되 색/발광을 더 강하게(회색 대비용).
    private void ApplyPerfectTint()
    {
        if (_data == null) return;
        EnsureRenderers();
        if (_renderers == null || _renderers.Length == 0) return;

        Color tint = _data.perfectDodgeTint;
        if (tint.a <= 0.001f) return;

        float strength = Mathf.Clamp01(tint.a);
        Color tintRgb  = new Color(tint.r, tint.g, tint.b, 1f);
        Color baseC    = Color.Lerp(Color.white, tintRgb, strength);
        baseC.a = 1f;
        Color emis = tintRgb * (_data.perfectDodgeTintEmission * strength);

        _mpb.Clear();
        _mpb.SetColor(BaseColorId, baseC);
        _mpb.SetColor(EmissionColorId, emis);
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].SetPropertyBlock(_mpb);

        _tintActive = true;
    }

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

                // 저스트 회피 중엔 간격을 덮어쓰고 실제시간으로 센다 —
                // 슬로모라 스케일 시간으로 세면 잔상이 뚝뚝 끊겨 속도 대비가 죽는다.
                float interval = _ghostIntervalOverride > 0f ? _ghostIntervalOverride : _data.dodgeGhostInterval;
                interval = Mathf.Max(0.01f, interval);

                await UniTask.Delay(TimeSpan.FromSeconds(interval),
                    _ghostUnscaled ? DelayType.UnscaledDeltaTime : DelayType.DeltaTime,
                    cancellationToken: token);
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
    /// <summary>
    /// 회피 출발 지점의 먼지. 부모 없음 → 대시로 멀어져도 출발한 자리에 남는다.
    ///
    /// 회피는 전투 중 가장 자주 눌리는 키라 매번 Instantiate/Destroy하면 할당과 히칭이 누적된다
    /// (잔상은 공들여 풀링해 놓고 먼지만 새고 있었다). 프리팹이 CharacterData의 직접 참조라
    /// Addressables 키가 없으므로 <see cref="ObjectPoolerManager.SpawnFromPrefab"/>로 인스턴스ID 키잉해 푼다.
    /// </summary>
    private void SpawnDust()
    {
        if (_data == null || _data.dodgeDustVfxPrefab == null) return;

        Vector3 pos = transform.position + Vector3.up * _data.dodgeDustHeightOffset;
        Quaternion rot = _data.dodgeDustVfxPrefab.transform.rotation;

        var pooler = Managers.ObjectPooler;
        if (pooler == null)
        {
            // 풀러가 없는 컨텍스트(부트 이전·씬 정리 중) — 연출을 통째로 죽이지 않고 예전 방식으로 폴백.
            var fallback = Instantiate(_data.dodgeDustVfxPrefab, pos, rot);
            Destroy(fallback, DustLifetime(fallback));
            return;
        }

        var go = pooler.SpawnFromPrefab(_data.dodgeDustVfxPrefab, ObjectPoolerManager.PoolType.Effect, pos, rot);
        if (go == null) return;

        // 파티클 Clear+Play·스케일 원복·수명 후 자동 반환을 PooledOneShotVfx가 전담한다
        // (풀 재사용 시 이전 회피의 파티클이 남아 보이는 것을 막는 안전장치가 그 안에 있다).
        if (!go.TryGetComponent<PooledOneShotVfx>(out var vfx)) vfx = go.AddComponent<PooledOneShotVfx>();
        vfx.Play(DustLifetime(go));
    }

    /// <summary>파티클이 완전히 사그라들 때까지의 시간. 시스템이 없으면 보수적으로 2초.</summary>
    private static float DustLifetime(GameObject go)
    {
        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        return ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 2f;
    }

    // ── 트레일 ────────────────────────────────────────────────────
    /// <summary>
    /// 자식에 TrailRenderer가 없을 때 CharacterData 설정으로 대시 트레일을 런타임 생성한다.
    /// dashTrailMaterial 미할당 시 null 반환(트레일 스킵).
    /// </summary>
    private TrailRenderer CreateDashTrail()
    {
        if (_data == null || _data.dashTrailMaterial == null) return null;

        var go = new GameObject("~DashTrail");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = _data.dashTrailLocalOffset;

        var tr = go.AddComponent<TrailRenderer>();
        tr.material = _data.dashTrailMaterial;
        tr.time = _data.dashTrailTime;
        tr.startWidth = _data.dashTrailWidth;
        tr.endWidth = 0f;
        tr.minVertexDistance = 0.05f;
        tr.autodestruct = false;
        tr.emitting = false;
        tr.alignment = LineAlignment.View;
        tr.textureMode = LineTextureMode.Stretch;
        tr.numCapVertices = 2;
        tr.numCornerVertices = 2;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;

        Color c = _data.dashTrailColor;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;

        return tr;
    }

    // INab 프리팹이 있으면 몸 상/하 앵커로 INab 트레일을 구성(우선), 없으면 기존 TrailRenderer 폴백.
    private void SetupDashTrail()
    {
        if (_data != null && _data.dashTrailVfxPrefab != null)
        {
            var rig = new GameObject("~DashTrailRig");
            rig.transform.SetParent(transform, false);

            var tip = new GameObject("Tip").transform;
            tip.SetParent(rig.transform, false);
            tip.localPosition = new Vector3(0f, _data.dashTrailUpperY, 0f);

            var bottom = new GameObject("Bottom").transform;
            bottom.SetParent(rig.transform, false);
            bottom.localPosition = new Vector3(0f, _data.dashTrailLowerY, 0f);

            _dashTrailVfx = rig.AddComponent<WeaponTrailEffect>();
            _dashTrailVfx.trailUsageType      = WeaponTrailEffect.TrailUsageType.Manual;
            _dashTrailVfx.useEvents           = false;
            _dashTrailVfx.enableGizmos        = false;
            _dashTrailVfx.lineTipTransform    = tip;
            _dashTrailVfx.lineBottomTransform = bottom;
            _dashTrailVfx.SetNewTrailPrefab(_data.dashTrailVfxPrefab);   // 안전 컨텍스트(Awake)에서 인스턴스화
            return;
        }

        // 폴백: 기존 자식 TrailRenderer 탐색 → 없으면 CharacterData 머티리얼로 생성
        _trail = GetComponentInChildren<TrailRenderer>(true);
        if (_trail == null) _trail = CreateDashTrail();
    }

    private void EnableTrail()
    {
        if (_dashTrailVfx != null) { _dashTrailVfx.StartTrail(DashTrailFadeIn); return; }
        if (_trail == null) return;
        _trail.Clear();          // 직전 위치에서 이어지는 잔상 줄 방지
        _trail.emitting = true;
        _trail.enabled  = true;
    }

    // 회피 종료 — 부드럽게 페이드아웃(게임플레이 활성 컨텍스트).
    private void DisableTrail()
    {
        if (_dashTrailVfx != null) { _dashTrailVfx.StopTrail(DashTrailFadeOut); return; }
        if (_trail == null) return;
        _trail.emitting = false;
        _trail.enabled  = false;
    }

    // 즉시 OFF(코루틴 없음) — Awake 초기화/OnDisable에서 'inactive GameObject' 경고 방지.
    private void HardOffTrail()
    {
        if (_dashTrailVfx != null)
        {
            _dashTrailVfx.SetProperty_EffectActive(false);
            _dashTrailVfx.SetProperty_EffectAlive(0f);
            return;
        }
        if (_trail == null) return;
        _trail.emitting = false;
        _trail.enabled  = false;
    }
}
