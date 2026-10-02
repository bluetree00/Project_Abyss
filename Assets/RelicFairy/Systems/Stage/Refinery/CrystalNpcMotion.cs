using UnityEngine;

/// <summary>
/// 정제소 「응축 성소」의 수정 정령 몸짓 — 뼈대가 없는 몸이라 애니메이터 대신 코드로 뜨고 · 돌고 · 조각을 모은다.
/// <see cref="ServiceNpcReactor"/>의 반응을 받는다:
///  알아봄 = 빛이 오르고 조각이 빨라진다 · 말 걸기 = 한 번 고동 · 거래 뒤 = 조각이 심장으로 응축했다가 빛과 함께 풀린다(정제 그 자체)
///  시큰둥 = 빛이 잠깐 가라앉는다 · 떠남 = 천천히 원래 박자로.
/// 흔들림은 「살짝 느껴질 정도」가 상한 — 크기 변화 없이 거리 · 빛 · 속도만 움직인다.
/// </summary>
[RequireComponent(typeof(ServiceNpcReactor))]
public sealed class CrystalNpcMotion : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    // 프리팹 규약 — 몸은 「Body」 밑에 심장 「Core」 · 조각 묶음 「Shards」(자식 전부) · 빛 「Glow」(Light)
    private const string CorePath   = "Body/Core";
    private const string ShardsPath = "Body/Shards";
    private const string GlowPath   = "Body/Glow";

    // ── [SerializeField] ────────────────────────────────────
    [Header("떠 있기")]
    [SerializeField] private float bobHeight = 0.10f;
    [SerializeField] private float bobSpeed  = 1.1f;
    [SerializeField] private float spinSpeed = 16f;    // °/s

    [Header("조각 궤도")]
    [SerializeField] private float orbitRadius = 0.7f;
    [SerializeField] private float orbitSpeed  = 38f;  // °/s
    [SerializeField] private float orbitWobble = 0.08f;

    [Header("응축(거래 뒤)")]
    [SerializeField] private float condenseSeconds = 0.4f;
    [SerializeField] private float releaseSeconds  = 0.9f;
    [SerializeField] private float flashMultiplier = 2.6f;

    // ── Private ─────────────────────────────────────────────
    private Transform   core;
    private Transform[] shards;
    private Light       glow;
    private ServiceNpcReactor _reactor;
    private Vector3 _coreBase;
    private float   _baseIntensity;
    private float   _phase;
    private float   _speedMul = 1f;
    private float   _glowMul  = 1f;
    private float   _radiusMul = 1f;
    private float   _condenseT = -1f;   // 0 이상이면 응축 진행 중(초)
    private float   _pulseT    = -1f;   // 0 이상이면 고동 진행 중(초)
    private float   _dimT      = -1f;   // 0 이상이면 가라앉음 진행 중(초)

    // ── Lifecycle ───────────────────────────────────────────
    private void Awake()
    {
        TryGetComponent(out _reactor);
        core = transform.Find(CorePath);
        var group = transform.Find(ShardsPath);
        shards = new Transform[group != null ? group.childCount : 0];
        for (int i = 0; i < shards.Length; i++) shards[i] = group.GetChild(i);
        var g = transform.Find(GlowPath);
        if (g != null) g.TryGetComponent(out glow);
        if (core != null) _coreBase = core.localPosition;
        if (glow != null) _baseIntensity = glow.intensity;
        _phase = Random.value * 360f;
    }

    private void OnEnable()
    {
        if (_reactor != null) _reactor.Reacted += HandleReacted;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float t  = Time.time;

        // 가까이 있으면 박자가 빨라지고 빛이 오른다 — 천천히 따라간다.
        bool attentive = _reactor != null && (_reactor.IsNear || _reactor.IsTalking);
        _speedMul = Mathf.MoveTowards(_speedMul, attentive ? 1.9f : 1f, dt * 1.5f);
        float glowTarget = attentive ? 1.45f : 1f;

        if (core != null)
        {
            core.localPosition = _coreBase + Vector3.up * (Mathf.Sin(t * bobSpeed) * bobHeight);
            core.Rotate(0f, spinSpeed * _speedMul * dt, 0f, Space.Self);
        }

        // 응축 — 조각이 심장으로 모였다가(0.4초) 섬광과 함께 살짝 넘쳐 풀린다(0.9초).
        _radiusMul = 1f;
        float flash = 0f;
        if (_condenseT >= 0f)
        {
            _condenseT += dt;
            if (_condenseT < condenseSeconds)
            {
                float k = _condenseT / condenseSeconds;
                _radiusMul = Mathf.Lerp(1f, 0.12f, k * k);
                _speedMul  = Mathf.Lerp(_speedMul, 4f, k);
            }
            else
            {
                float k = Mathf.Clamp01((_condenseT - condenseSeconds) / releaseSeconds);
                _radiusMul = Mathf.Lerp(0.12f, 1f, 1f - (1f - k) * (1f - k)) + Mathf.Sin(k * Mathf.PI) * 0.12f;
                flash = (1f - k) * (flashMultiplier - 1f);
                if (k >= 1f) _condenseT = -1f;
            }
        }

        if (_pulseT >= 0f)
        {
            _pulseT += dt;
            flash = Mathf.Max(flash, Mathf.Sin(Mathf.Clamp01(_pulseT / 0.5f) * Mathf.PI) * 0.6f);
            if (_pulseT >= 0.5f) _pulseT = -1f;
        }

        if (_dimT >= 0f)
        {
            _dimT += dt;
            glowTarget *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(_dimT / 1.2f));
            if (_dimT >= 1.2f) _dimT = -1f;
        }

        _glowMul = Mathf.MoveTowards(_glowMul, glowTarget, dt * 1.2f);
        if (glow != null) glow.intensity = _baseIntensity * (_glowMul + flash);

        OrbitShards(dt, t);
    }

    private void OnDisable()
    {
        if (_reactor != null) _reactor.Reacted -= HandleReacted;
    }

    // ── Private Methods ─────────────────────────────────────
    private void OrbitShards(float dt, float t)
    {
        if (shards == null || shards.Length == 0) return;
        _phase += orbitSpeed * _speedMul * dt;
        Vector3 center = core != null ? core.localPosition : Vector3.zero;
        float step = 360f / shards.Length;
        for (int i = 0; i < shards.Length; i++)
        {
            var s = shards[i];
            if (s == null) continue;
            float a = (_phase + step * i) * Mathf.Deg2Rad;
            float y = Mathf.Sin(t * 1.7f + i * 1.3f) * orbitWobble;
            s.localPosition = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (orbitRadius * _radiusMul) + Vector3.up * y;
            s.Rotate(25f * dt, 40f * dt, 0f, Space.Self);
        }
    }

    // ── Event Handlers ──────────────────────────────────────
    private void HandleReacted(ServiceNpcReactor.Reaction r)
    {
        switch (r)
        {
            case ServiceNpcReactor.Reaction.Notice:
            case ServiceNpcReactor.Reaction.Talk:
                _pulseT = 0f;
                break;
            case ServiceNpcReactor.Reaction.Thanks:
                _condenseT = 0f;
                break;
            case ServiceNpcReactor.Reaction.Shrug:
                _dimT = 0f;
                break;
        }
    }
}
