using UnityEngine;

/// <summary>
/// 재련소 대장장이의 망치질 — 혼자 일할 때(<see cref="ServiceNpcReactor.Reaction.Work"/>) 망치가 모루에 닿는 순간
/// 불꽃이 튀고 모루 위 달군 쇠가 잠깐 밝아진다. 모루 · 불꽃 · 열기 빛은 NPC 프리팹 안에 있다(몸만 돌고 모루는 제자리).
/// 애니메이션 이벤트를 쓰지 않는다 — FBX 가져오기 설정(.meta)을 건드릴 수 없어서 닿는 순간을 초로 맞춘다.
/// </summary>
[RequireComponent(typeof(ServiceNpcReactor))]
public sealed class SmithForgeFx : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    // 프리팹 규약 — 루트 자식 「Anvil」 밑에 불꽃 「Sparks」(ParticleSystem) · 열기 빛 「Heat」(Light)
    private const string SparksPath = "Anvil/Sparks";
    private const string HeatPath   = "Anvil/Heat";

    // ── [SerializeField] ────────────────────────────────────
    [Tooltip("몸짓 시작 → 망치가 모루에 닿는 순간(초)")]
    [SerializeField] private float strikeDelay = 0.35f;
    [SerializeField] private float heatFlash   = 2.2f;   // 닿는 순간 열기 빛 배수
    [SerializeField] private float heatFade    = 0.5f;   // 원래 밝기로 돌아오는 시간(초)

    // ── Private ─────────────────────────────────────────────
    private ParticleSystem    sparks;
    private Light             heat;
    private ServiceNpcReactor _reactor;
    private float _strikeAt = -1f;
    private float _flashT   = -1f;
    private float _baseHeat;

    // ── Lifecycle ───────────────────────────────────────────
    private void Awake()
    {
        TryGetComponent(out _reactor);
        var s = transform.Find(SparksPath);
        if (s != null) s.TryGetComponent(out sparks);
        var h = transform.Find(HeatPath);
        if (h != null) h.TryGetComponent(out heat);
        if (heat != null) _baseHeat = heat.intensity;
    }

    private void OnEnable()
    {
        if (_reactor != null) _reactor.Reacted += HandleReacted;
    }

    private void Update()
    {
        if (_strikeAt >= 0f && Time.time >= _strikeAt)
        {
            _strikeAt = -1f;
            _flashT   = 0f;
            if (sparks != null) sparks.Play(true);
        }

        if (_flashT < 0f || heat == null) return;
        _flashT += Time.deltaTime;
        float k = Mathf.Clamp01(_flashT / heatFade);
        heat.intensity = _baseHeat * Mathf.Lerp(heatFlash, 1f, k);
        if (k >= 1f) _flashT = -1f;
    }

    private void OnDisable()
    {
        if (_reactor != null) _reactor.Reacted -= HandleReacted;
    }

    // ── Event Handlers ──────────────────────────────────────
    private void HandleReacted(ServiceNpcReactor.Reaction r)
    {
        if (r == ServiceNpcReactor.Reaction.Work) _strikeAt = Time.time + strikeDelay;
    }
}
