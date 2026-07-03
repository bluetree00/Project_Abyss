using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 피격자(몬스터) 측 타격 연출 컴포넌트.
/// HitFeedbackService가 IHitReceiver.OnReceiveHit으로 직접 호출한다.
///
/// 블로그 ⑧ 피격자 반응(색) + ② 화면 번쩍임(PointLight) 구현.
/// 피격 애니메이션/넉백은 GetHitState가 기존 처리 — 이 클래스는 시각 펄스만 담당.
///
/// ※ MonsterBase 수정 없음 — 프리팹에 본 컴포넌트만 부착하면 동작.
/// </summary>
[DisallowMultipleComponent]
public class VictimHitFeedback : MonoBehaviour, IHitReceiver
{
    // ── Constants ─────────────────────────────────────────────────
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId  = Shader.PropertyToID("_EmissionColor");
    private static readonly int HitFlashId       = Shader.PropertyToID("_HitFlash");      // 외곽선 셰이더 구동(0~1)
    private static readonly int HitFlashColorId  = Shader.PropertyToID("_HitFlashColor"); // 외곽선 플래시 색
    private const string       AutoLightName   = "~HitFlashLight";

    // ── Serialized ────────────────────────────────────────────────
    [Header("프로필")]
    [SerializeField] private MonsterHitProfileSO _profile;

    [Header("Flash 대상 (비우면 자식 렌더러 자동 수집)")]
    [SerializeField] private Renderer[] _explicitRenderers;

    [Header("PointLight (비우면 자동 생성)")]
    [SerializeField] private Light _pulseLight;

    // 프로필 미배정 몬스터가 공유하는 런타임 기본 프로필(빨강 더블블링크 = SO 필드 기본값).
    // 자산 없이 MonsterHitProfileSO의 C# 기본 초기화값을 그대로 사용 — 프리팹 배선 불필요.
    private static MonsterHitProfileSO s_defaultProfile;

    // ── Private ───────────────────────────────────────────────────
    private Renderer[]             _renderers;
    private MaterialPropertyBlock  _mpb;

    // ③ CTS new/Dispose 대체 세대 카운터 — 히트마다 alloc 없이 이전 루틴 무효화.
    // 재시작 = ++gen, 취소(OnDisable/OnDestroy) = ++gen. 루틴은 자기 gen 불일치 시 즉시 종료.
    private int _flashGen;
    private int _lightGen;

    // 방향성 히트 리액션: 루트가 아닌 자식 비주얼만 틸트(루트=콜라이더/rb/agent/넉백 불간섭).
    // rest 로컬 트랜스폼은 Awake에서 1회 캡처 — 매틱 절대 세팅이라 누적 없음, 복원=rest 재대입.
    private Transform              _visualRoot;
    private Quaternion            _restLocalRot;
    private Vector3               _restLocalPos;
    private int                   _reactGen;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        // 프로필 미배정(자동 부착된 몬스터 등)이면 공유 기본 프로필 사용 — EnsureLight보다 먼저.
        if (_profile == null)
        {
            if (s_defaultProfile == null)
                s_defaultProfile = ScriptableObject.CreateInstance<MonsterHitProfileSO>();
            _profile = s_defaultProfile;
        }

        CollectRenderers();
        EnsureLight();
        _mpb = new MaterialPropertyBlock();

        _visualRoot = ResolveVisualRoot();
        if (_visualRoot != null)
        {
            _restLocalRot = _visualRoot.localRotation;
            _restLocalPos = _visualRoot.localPosition;
        }
    }

    private void OnDisable()
    {
        CancelAll();
        RestoreMaterials();
        RestoreVisualRoot();   // 풀 반환 시 틸트/오프셋 잔류 0 보장 (루틴 finally가 비동기라도 동기 복원)
        if (_pulseLight != null)
        {
            _pulseLight.enabled = false;
            _pulseLight.intensity = 0f;
        }
    }

    private void OnDestroy() => CancelAll();

    // ── Public Methods (IHitReceiver) ─────────────────────────────
    public void OnReceiveHit(in HitInfo info)
    {
        if (_profile == null) return;

        Color flashColor = _profile.FlashColor;
        Color lightColor = _profile.LightColor;

        PlayFlash(flashColor);
        if (_profile.UsePointLight) PlayLightPulse(lightColor);
        if (_profile.UseHitReaction) PlayHitReaction(info);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void CollectRenderers()
    {
        if (_explicitRenderers != null && _explicitRenderers.Length > 0)
        {
            _renderers = _explicitRenderers;
            return;
        }

        var buffer = new List<Renderer>();
        var all = GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in all)
        {
            // SkinnedMesh / Mesh 만 채택. Trail/Line/Particle/Canvas 등은 배제.
            if (r is SkinnedMeshRenderer || r is MeshRenderer)
                buffer.Add(r);
        }
        _renderers = buffer.ToArray();
    }

    private void EnsureLight()
    {
        if (_pulseLight != null) return;
        if (_profile == null || !_profile.UsePointLight) return;

        var lightGo = new GameObject(AutoLightName);
        lightGo.transform.SetParent(transform, worldPositionStays: false);
        lightGo.transform.localPosition = _profile.LightLocalOffset;

        _pulseLight = lightGo.AddComponent<Light>();
        _pulseLight.type      = LightType.Point;
        _pulseLight.range     = _profile.LightRange;
        _pulseLight.intensity = 0f;
        _pulseLight.enabled   = false;
    }

    private void PlayFlash(Color flashColor)
    {
        int gen = ++_flashGen;
        _ = FlashRoutineAsync(flashColor, gen);
    }

    private void PlayLightPulse(Color lightColor)
    {
        if (_pulseLight == null) return;

        int gen = ++_lightGen;
        _ = LightPulseRoutineAsync(lightColor, gen);
    }

    private void PlayHitReaction(in HitInfo info)
    {
        if (_visualRoot == null) return;

        // 맞은 방향(공격자→피격자) 수평화. 수직 성분만이면 방향감 없음 → 스킵.
        Vector3 dir = info.AttackDirection;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        float mult = info.IsCritical ? _profile.CritReactionMultiplier : 1f;

        int gen = ++_reactGen;
        _ = HitReactionRoutineAsync(dir, mult, gen);
    }

    private async UniTaskVoid FlashRoutineAsync(Color flashColor, int gen)
    {
        float duration = _profile.FlashDuration;
        if (duration <= 0f || _renderers == null || _renderers.Length == 0) return;

        float boost  = _profile.EmissionBoost;
        var   curve  = _profile.FlashCurve;
        int   pulses = Mathf.Max(1, _profile.FlashPulses);
        bool  outlineFlash = _profile.UseOutlineFlash;

        Color outlineColor = flashColor;
        outlineColor.a     = 1f; // 가림 아웃라인이 플래시 중 또렷하게 보이도록 불투명 빨강

        float t = 0f;
        while (t < duration)
        {
            if (gen != _flashGen) return; // 새 플래시/취소 → 이전 루틴 무효 (복원은 새 루틴/OnDisable이 담당)

            float n = Mathf.Clamp01(t / duration);
            // 지속시간을 pulses등분해 커브를 반복 → 더블(멀티) 블링크. pulses=1이면 기존 단발.
            float phase = n * pulses;
            float local = phase - Mathf.Floor(phase);
            float w     = curve != null ? curve.Evaluate(local) : 1f - local;

            Color tint     = flashColor;
            tint.a         = 1f;
            Color emission = flashColor * (boost * w);

            _mpb.SetColor(BaseColorId,     Color.Lerp(Color.white, tint, w));
            _mpb.SetColor(EmissionColorId, emission);

            // 외곽선 플래시: 같은 렌더러 MPB를 Render Objects 아웃라인 패스가 함께 읽음.
            if (outlineFlash)
            {
                _mpb.SetFloat(HitFlashId,      w);
                _mpb.SetColor(HitFlashColorId, outlineColor);
            }

            ApplyPropertyBlock(_mpb);

            t += Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (gen == _flashGen) RestoreMaterials(); // 자연 종료만 복원 (중첩 시 새 루틴이 소유)
    }

    private async UniTaskVoid LightPulseRoutineAsync(Color lightColor, int gen)
    {
        float duration = _profile.LightDuration;
        if (duration <= 0f || _pulseLight == null) return;

        float peak  = _profile.LightIntensity;
        var   curve = _profile.LightCurve;

        _pulseLight.color   = lightColor;
        _pulseLight.range   = _profile.LightRange;
        _pulseLight.enabled = true;

        float t = 0f;
        while (t < duration)
        {
            if (gen != _lightGen) return; // 새 펄스/취소 → 이전 루틴 무효 (새 루틴이 라이트 소유)

            float n = Mathf.Clamp01(t / duration);
            float w = curve != null ? curve.Evaluate(n) : 1f - n;
            _pulseLight.intensity = peak * w;

            t += Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (gen == _lightGen && _pulseLight != null) // 자연 종료만 소등
        {
            _pulseLight.intensity = 0f;
            _pulseLight.enabled   = false;
        }
    }

    private async UniTaskVoid HitReactionRoutineAsync(Vector3 dirWorld, float mult, int gen)
    {
        float duration = _profile.FlinchDuration;
        if (duration <= 0f || _visualRoot == null) return;

        float angle = _profile.FlinchTiltAngle  * mult;
        float back  = _profile.FlinchBackOffset * mult;
        var   curve = _profile.FlinchCurve;

        // 틸트축(맞은 방향으로 상단이 기울도록) + 플린치 오프셋 방향을 부모 로컬 공간으로 1회 변환.
        // → 리액션 중 몸(루트)이 회전해도 몸 기준 일관, localRotation/Position 절대 세팅이라 복원 단순.
        var parent = _visualRoot.parent;
        Vector3 tiltAxis = Vector3.Cross(Vector3.up, dirWorld);
        if (parent != null)
        {
            tiltAxis = parent.InverseTransformDirection(tiltAxis);
            dirWorld = parent.InverseTransformDirection(dirWorld);
        }
        if (tiltAxis.sqrMagnitude > 0.0001f) tiltAxis.Normalize();

        float t = 0f;
        while (t < duration)
        {
            if (gen != _reactGen) return; // 새 리액션/취소 → 이전 루틴 무효 (절대 세팅이라 새 루틴이 덮어씀)

            float n   = Mathf.Clamp01(t / duration);
            float env = curve != null ? curve.Evaluate(n) : 1f - n;

            _visualRoot.localRotation = Quaternion.AngleAxis(angle * env, tiltAxis) * _restLocalRot;
            _visualRoot.localPosition = _restLocalPos + dirWorld * (back * env);

            t += Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        if (gen == _reactGen) RestoreVisualRoot(); // 자연 종료만 복원
    }

    /// <summary>비주얼 자식만 틸트 대상으로 해석. Animator 트랜스폼(루트≠) 우선, 없으면 렌더러의 루트 직속 자식.
    /// 단일 메시가 루트에만 있으면 null(틸트 스킵 — 루트 회전=물리/NavMesh 파손 방지).</summary>
    private Transform ResolveVisualRoot()
    {
        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.transform != transform) return anim.transform;

        if (_renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                var t = r.transform;
                while (t != null && t.parent != transform) t = t.parent;
                if (t != null && t != transform) return t;
            }
        }
        return null;
    }

    private void RestoreVisualRoot()
    {
        if (_visualRoot == null) return;
        _visualRoot.localRotation = _restLocalRot;
        _visualRoot.localPosition = _restLocalPos;
    }

    private void ApplyPropertyBlock(MaterialPropertyBlock mpb)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(mpb);
        }
    }

    private void RestoreMaterials()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(null);
        }
    }

    private void CancelAll()
    {
        // 세대 bump로 실행 중 루틴을 무효화 — 다음 yield에서 자기 gen 불일치 감지 후 종료.
        // 동기 복원(RestoreMaterials/RestoreVisualRoot/라이트 소등)은 OnDisable이 별도 수행.
        _flashGen++;
        _lightGen++;
        _reactGen++;
    }
}
