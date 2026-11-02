using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 기억 성소의 수정 — 기억의 제단 다섯 갈래가 <b>공간에</b> 보인다(2차 개편 09-29, 봉인 섬과 같은 원리).
///   · 둘레 작은 수정 5 = 갈래(룬·서약·장비·여정·원거리). 밝기 = 그 갈래에서 연 칸의 비율(0 어둠 · 일부 흐린 빛 · 전부 금빛).
///   · 지금 정수로 열 수 있는 칸이 있으면 가운데 큰 수정이 금빛으로 맥동하고 빛 입자가 오른다(유도설계 A-2 「다음 해금을 제단 밖으로」).
///     없으면 잔잔한 푸른빛.
///   · 복귀 판에 「열 수 있는 수」가 지난번보다 늘었을 때만 멀린이 한 줄(슬롯별 기록 — 매번 말하지 않는다, 원칙 2).
/// 제단 값은 0.5초마다 다시 읽는다(제단 창에서 사면 바로 바뀐다). 수정 구성은 프리팹 BaseCamp_Sanctum/Wing/MemorySanctum에서 이름으로 찾는다.
/// </summary>
public sealed class MemoryAltarCrystal : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float  PollInterval   = 0.5f;
    private const float  PulseSpeed     = 2.2f;
    private const string AffordableKey  = "basecamp_altar_affordable_v1_slot";
    // 10-30 바닥 빛 길 — 귀환 자리(광장) → 제단 다리. 열 수 있을 때만 · 제단에 한 번 닿으면 그 방문엔 끈다
    private const string TrailPath      = "Markers/AltarTrail";
    private const float  TrailSpacing   = 2.4f;    // 빛 점 사이(m)
    private const float  TrailStepSec   = 0.11f;   // 점 하나씩 켜지는 간격 — 물결이 제단 쪽으로 흐른다
    private const float  TrailWaveSec   = 3.2f;    // 물결 한 번
    private const float  TrailReachDist = 7f;      // 이만큼 다가오면 길을 끈다

    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly AltarBranch[] Branches =
        { AltarBranch.Rune, AltarBranch.Covenant, AltarBranch.Weapon, AltarBranch.Journey, AltarBranch.Memory };   // 10-02 재설계 — 수정 5개 = 바깥 갈래 4 + 가운데 기억

    // ── Serialized ────────────────────────────────────────────────
    [Header("재질")]
    [Tooltip("수정(발광 Lit) — 색은 MaterialPropertyBlock으로")]
    [SerializeField] private Material crystalMaterial;
    [Tooltip("오르는 빛 입자(Hovl Glow1cg)")]
    [SerializeField] private Material moteMaterial;

    [Header("색")]
    [SerializeField] private Color idleColor   = new(0.45f, 0.7f, 1f);
    [SerializeField] private Color readyColor  = new(1f, 0.8f, 0.4f);
    [SerializeField] private Color branchDark  = new(0.18f, 0.22f, 0.32f);
    [SerializeField] private Color branchLit   = new(0.55f, 0.75f, 1f);
    [SerializeField] private Color branchFull  = new(1f, 0.82f, 0.45f);

    [Header("멀린")]
    [Tooltip("열 수 있는 것이 늘었을 때 한 줄 — {0} = 가장 싼 열 수 있는 칸 이름")]
    [SerializeField, TextArea] private string affordableLine = "정수가 모였다 — 기억의 제단에서 「{0}」부터 열 수 있다.";

    // ── Private ───────────────────────────────────────────────────
    private Renderer[] _core;
    private Renderer[][] _branches;
    private Light _light;
    private float _lightMax;
    private ParticleSystem _motes;
    private MaterialPropertyBlock _mpb;
    private int _affordable = -1;
    private bool _debugForceReady;
    private Vector3[] _trail;
    private ParticleSystem _trailPs;
    private float _trailWaveStart;
    private int _trailEmitted;
    private bool _trailReached;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _core = Bind(transform.Find("CorePivot/Core"));
        _branches = new Renderer[Branches.Length][];
        for (int i = 0; i < Branches.Length; i++) _branches[i] = Bind(transform.Find($"Branch_{i + 1}/Shard"));
        var lightTf = transform.Find("Light");
        if (lightTf != null && lightTf.TryGetComponent(out _light)) _lightMax = _light.intensity;
        var core = transform.Find("CorePivot");
        _motes = BaseCampParticles.Motes("~MemoryMotes", transform, core != null ? core.localPosition : Vector3.up * 2f,
                                         new Vector3(1.6f, 0.4f, 1.6f), 0f, 3f, 0.5f, readyColor,
                                         new Vector3(-0.1f, 0.6f, -0.1f), new Vector3(0.1f, 1.3f, 0.1f), moteMaterial);
        BuildTrail();
    }

    private void Start()
    {
        PollAsync(destroyCancellationToken).Forget();
        AnnounceIfMoreAsync(destroyCancellationToken).Forget();
    }

    private void Update()
    {
        // 열 수 있을 때만 맥동 — 값 계산만 하고 할당은 없다
        if (_core == null || !Ready) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseSpeed);
        SetColor(_core, readyColor * 0.9f, readyColor * Mathf.Lerp(1.2f, 2.6f, pulse));
        if (_light != null) _light.intensity = _lightMax * Mathf.Lerp(0.8f, 1.3f, pulse);
        TickTrail();
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>디버그 — 「열 수 있음」 모습을 강제로 켜고 끈다(저장 없음).</summary>
    public void DebugForceReady(bool on)
    {
        _debugForceReady = on;
        _affordable = -1;
        Refresh();
    }

    // ── Private Methods ───────────────────────────────────────────
    private bool Ready => _debugForceReady || _affordable > 0;

    private async UniTaskVoid PollAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady, cancellationToken: ct);
            while (true)
            {
                Refresh();
                await UniTask.Delay(TimeSpan.FromSeconds(PollInterval), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Refresh()
    {
        int affordable = MemoryAltarService.AffordableCount();
        bool changed = affordable != _affordable;
        _affordable = affordable;

        for (int i = 0; i < Branches.Length; i++)
        {
            var (unlocked, total) = MemoryAltarService.BranchProgress(Branches[i]);
            float ratio = total > 0 ? (float)unlocked / total : 0f;
            Color c = ratio >= 1f ? branchFull : Color.Lerp(branchDark, branchLit, ratio);
            SetColor(_branches[i], c * 0.8f, c * Mathf.Lerp(0.2f, 1.6f, ratio));
        }

        if (!changed) return;
        if (!Ready)
        {
            SetColor(_core, idleColor * 0.6f, idleColor * 0.9f);
            if (_light != null) { _light.color = idleColor; _light.intensity = _lightMax * 0.6f; }
            BaseCampParticles.Retint(_motes, idleColor, 0f);   // 열 것이 없으면 입자 0 — 아무 일 없는 상태가 가장 조용하게(10-11 톤 2차 V-13)
        }
        else
        {
            if (_light != null) _light.color = readyColor;
            BaseCampParticles.Retint(_motes, readyColor, 9f);
        }
    }

    /// <summary>슬롯 초기화(새 게임 · 삭제) — 「지난번 열 수 있던 수」 기록도 지운다(10-09 출시 점검: 새 슬롯에 남아 첫 한 줄이 안 나왔다).</summary>
    public static void ClearForSlot(int slot)
    {
        PlayerPrefs.DeleteKey(AffordableKey + slot);
        PlayerPrefs.Save();
    }

    /// <summary>복귀 판 — 열 수 있는 수가 지난번보다 늘었으면 멀린 한 줄. 첫 판 온보딩 중에는 말하지 않는다.</summary>
    private async UniTaskVoid AnnounceIfMoreAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => (BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance.IsReady) && _affordable >= 0,
                                    cancellationToken: ct);
            if (BaseCampOnboardingDirector.InProgress) return;

            string key = AffordableKey + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
            int before = PlayerPrefs.GetInt(key, 0);
            PlayerPrefs.SetInt(key, _affordable);
            PlayerPrefs.Save();
            if (_affordable <= before || string.IsNullOrEmpty(affordableLine)) return;

            var next = MemoryAltarService.GetNextGoal();   // 해방기엔 해방 대비 칸이 먼저(10-22 D5) — 지금 열 수 있을 때만 그 칸을 부른다
            if (next == null || !next.Value.CanBuy)
                next = MemoryAltarService.GetNextGoal(preferLiberation: false);   // 열 수 있는 게 있으면 가장 싼 차례 칸이 곧 열 수 있는 칸이다
            if (next == null || next.Value.Node == null) return;
            // 새 봉인 한 줄(준비 뒤 0.8초 + 점등 1.6초)이 먼저 지나가게 — 한 순간에 하나만(원칙 5)
            await UniTask.Delay(TimeSpan.FromSeconds(4f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            RelicFairy.UI.UI_BossBark.Show(Loc.F(affordableLine, Loc.T(next.Value.Node.DisplayName)),   // 11-02 Q11
                                           RelicFairy.UI.BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
            Debug.Log($"[MemoryAltarCrystal] 열 수 있는 것 {before} → {_affordable} — 멀린 한 줄", this);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 10-30 사용자 「귀환은 예전처럼 입구 방위 · 제단으로 이끄는 레벨디자인」 — 광장 귀환 자리에서 제단이 화면 오른쪽 밖(58°)이라,
    /// 발밑에서 제단 다리까지 바닥에 낮은 금빛 점이 차례로 켜졌다 꺼진다(꺾임점 = 프리팹 Markers/AltarTrail 자식 순서). 점은 바닥에 붙인다.
    /// </summary>
    private void BuildTrail()
    {
        Transform root = null;
        for (var t = transform.parent; t != null && root == null; t = t.parent) root = t.Find(TrailPath);   // 성소 프리팹이 씬에서 다른 부모 밑에 있어도
        if (root == null || root.childCount < 2) { Debug.LogWarning($"[MemoryAltarCrystal] 바닥 빛 길 꺾임점 없음({TrailPath})", this); return; }
        var pts = new System.Collections.Generic.List<Vector3>();
        for (int i = 1; i < root.childCount; i++)
        {
            Vector3 a = root.GetChild(i - 1).position, b = root.GetChild(i).position;
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / TrailSpacing));
            for (int k = i == 1 ? 0 : 1; k <= n; k++) pts.Add(Snap(Vector3.Lerp(a, b, (float)k / n)));
        }
        _trail = pts.ToArray();
        _trailPs = BaseCampParticles.Motes("~AltarTrail", transform, Vector3.zero, Vector3.zero, 0f, 1.3f, 0.75f, readyColor,
                                           new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0.25f, 0f), moteMaterial);
    }

    private static Vector3 Snap(Vector3 p)
        => Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * 0.08f : p;

    private void TickTrail()
    {
        if (_trail == null || _trailPs == null || _trailReached) return;
        var player = Managers.Player?.PlayerTransform;
        if (player != null && (player.position - _trail[^1]).sqrMagnitude < TrailReachDist * TrailReachDist) { _trailReached = true; return; }   // 길 끝(제단 다리)에 닿으면
        float t = Time.unscaledTime - _trailWaveStart;
        int upTo = Mathf.Min(_trail.Length, (int)(t / TrailStepSec) + 1);
        var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (; _trailEmitted < upTo; _trailEmitted++)
        {
            ep.position = _trail[_trailEmitted];
            _trailPs.Emit(ep, 1);
        }
        if (t >= TrailWaveSec) { _trailWaveStart = Time.unscaledTime; _trailEmitted = 0; }
    }

    private Renderer[] Bind(Transform t)
    {
        if (t == null) return null;
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (crystalMaterial != null)
            foreach (var r in rs) r.sharedMaterial = crystalMaterial;
        return rs;
    }

    private void SetColor(Renderer[] rs, Color baseCol, Color emission)
    {
        if (rs == null) return;
        _mpb.Clear();
        _mpb.SetColor(BaseColorId, baseCol);
        _mpb.SetColor(EmissionColorId, emission);
        foreach (var r in rs) r.SetPropertyBlock(_mpb);
    }
}
