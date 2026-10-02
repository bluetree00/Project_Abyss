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
            BaseCampParticles.Retint(_motes, idleColor, 1.5f);
        }
        else
        {
            if (_light != null) _light.color = readyColor;
            BaseCampParticles.Retint(_motes, readyColor, 9f);
        }
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

            var next = MemoryAltarService.GetNextGoal();   // 열 수 있는 게 있으면 가장 싼 차례 칸이 곧 열 수 있는 칸이다
            if (next == null || next.Value.Node == null) return;
            // 새 봉인 한 줄(준비 뒤 0.8초 + 점등 1.6초)이 먼저 지나가게 — 한 순간에 하나만(원칙 5)
            await UniTask.Delay(TimeSpan.FromSeconds(4f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            RelicFairy.UI.UI_BossBark.Show(string.Format(affordableLine, next.Value.Node.DisplayName),
                                           RelicFairy.UI.BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
            Debug.Log($"[MemoryAltarCrystal] 열 수 있는 것 {before} → {_affordable} — 멀린 한 줄", this);
        }
        catch (OperationCanceledException) { }
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
