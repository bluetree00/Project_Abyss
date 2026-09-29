using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEngine;

/// <summary>
/// 봉인 섬 넷 — 이야기 진행이 화면이 아니라 <b>공간에</b> 보이게 한다.
/// 09-29 사용자: 「봉인석을 맵 외곽의 잘 보이는 곳에 크고 웅장하게 — 봉인 지점을 알려주게」(2차 개편).
///   광장보다 낮게 떠 있는 섬(가까이·얕게 Ch1 → 멀리·깊게 Ch4) 위에서 봉인 수정을 기둥 넷이 빛 사슬로 붙잡는다.
///   성문 오른쪽 봉인 전망대의 비석 넷이 각 섬을 향해 서서, 가까이 가면 보스 이름과 상태를 말한다(이름표 규칙 <see cref="BaseCampLabelRule"/>).
///   게임 카메라는 발 +4.8 m 위를 못 잡는다(09-28 실측) — 그래서 섬은 광장보다 낮고, 수정 꼭대기가 광장 +3 m를 넘지 않는다.
/// 상태는 <see cref="ResolveState"/> 한 곳에서만 정한다(순환 개정 §5-5, 7a 09-29).
/// 새로 봉인하고 처음 돌아오면 그 섬에 한 번만 불이 들어오고 멀린이 한 줄 — 입력·카메라는 건드리지 않는다(매 판 연출 규칙).
/// 섬 구성(바위·받침·기둥·수정·광원)은 프리팹 BaseCamp_Sanctum/Wing에 있고, 이 컴포넌트는 이름으로 찾아 상태만 입힌다.
/// </summary>
public sealed class BaseCampSealShrine : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const int    IslandCount   = 4;
    private const string SeenKeyPrefix = "basecamp.sealLit.";   // StoryProgress 본 것 기록 — 섬 점등 연출은 보스당 한 번
    private const float  BrokenTilt    = 18f;                   // 깨진 수정이 기우는 각도
    private const float  IgniteSeconds = 1.6f;
    private const float  ChainWidth    = 0.22f;
    private const float  TetherWidth   = 0.09f;
    private const float  RingWidth     = 0.35f;
    private const float  PlaqueLabelY  = 1.9f;

    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private enum SealState { Unsealed, Sealed, Broken, Slain }

    // ── Serialized ────────────────────────────────────────────────
    [Header("보스 (Ch1~4 순)")]
    [SerializeField] private string[] bossNames  = { "숲의 수호자", "화룡", "죽음의 기사", "리치" };
    [SerializeField] private Color[]  bossColors =
    {
        new(0.35f, 0.95f, 0.45f), new(1f, 0.45f, 0.18f), new(0.62f, 0.8f, 1f), new(0.72f, 0.42f, 1f),
    };

    [Header("상태 색")]
    [SerializeField] private Color sealedGold   = new(1f, 0.82f, 0.45f);
    [SerializeField] private Color brokenViolet = new(0.62f, 0.35f, 1f);
    [SerializeField] private Color slainRed     = new(0.95f, 0.18f, 0.15f);

    [Header("재질")]
    [Tooltip("봉인 수정(발광 Lit) — 섬마다 색은 MaterialPropertyBlock으로 입힌다")]
    [SerializeField] private Material crystalMaterial;
    [Tooltip("빛 사슬·비석 줄(Hovl Laser2)")]
    [SerializeField] private Material beamMaterial;
    [Tooltip("안개·불티 입자(Hovl Glow1cg)")]
    [SerializeField] private Material moteMaterial;
    [SerializeField] private TMP_FontAsset labelFont;

    [Header("멀린")]
    [Tooltip("새로 봉인하고 처음 돌아왔을 때 한 줄 — {0} = 보스 이름")]
    [SerializeField, TextArea] private string newSealLine = "{0}의 봉인이 섰다. 저 수정이 빛나는 한, 그것은 다시 깨어나지 못한다.";

    // ── Private ───────────────────────────────────────────────────
    private sealed class Island
    {
        public Transform      Crystal;
        public Renderer[]     CrystalRenderers;
        public Quaternion     CrystalRest;
        public Light          Light;
        public float          LightMax;
        public Vector3[]      PillarTops;
        public LineRenderer[] Chains;
        public LineRenderer[] Rings;
        public Transform      Plaque;
        public LineRenderer   Tether;
        public ParticleSystem Mist;
        public TextMeshPro    Label;
        public SealState      State;
    }

    private Island[] _islands;
    private MaterialPropertyBlock _mpb;
    private Transform _camTransform;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _islands = new Island[IslandCount];
        var terrace = transform.parent != null ? transform.parent.Find("SealTerrace") : null;
        for (int i = 0; i < IslandCount; i++)
            _islands[i] = BindIsland(i, transform.Find($"Island_{i + 1}"), terrace != null ? terrace.Find($"Plaque_{i + 1}") : null);
    }

    private void Start() => BuildWhenReadyAsync(destroyCancellationToken).Forget();

    private void LateUpdate()
    {
        if (_camTransform == null)
        {
            var gcc = GameCameraController.Instance;
            if (gcc == null) return;
            _camTransform = gcc.transform;
        }
        for (int i = 0; i < IslandCount; i++)
        {
            var label = _islands[i].Label;
            if (label == null) continue;
            bool show = BaseCampLabelRule.IsShown(_islands[i].Plaque);
            if (label.enabled != show) label.enabled = show;
            if (show) label.transform.rotation = _camTransform.rotation;
        }
    }

    private void OnDestroy()
    {
        if (_islands == null) return;
        foreach (var isl in _islands)
            if (isl.Plaque != null) BaseCampLabelRule.Unregister(isl.Plaque);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>디버그 — 섬 하나(또는 -1 = 전부)의 겉모습만 바꾼다(저장 없음). state: 0 풀림 · 1 봉인 · 2 깨짐 · 3 처치.</summary>
    public void DebugPreview(int island, int state)
    {
        for (int i = 0; i < IslandCount; i++)
            if (island < 0 || island == i) Apply(i, (SealState)Mathf.Clamp(state, 0, 3), 1f);
    }

    /// <summary>디버그 — 섬 하나의 점등 연출을 다시 튼다(저장 없음).</summary>
    public void DebugIgnite(int island) => IgniteAsync(Mathf.Clamp(island, 0, IslandCount - 1), false, false, destroyCancellationToken).Forget();

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>계정 기록(자동 로그인·세이브 로드)이 준비된 뒤에 상태를 입힌다 — 씬 직접 실행에서도 상태가 맞게.</summary>
    private async UniTaskVoid BuildWhenReadyAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        StoryProgress.EnsureConsistency();

        var log = new System.Text.StringBuilder("[SealShrine] ");
        int newlySealed = -1;
        for (int i = 0; i < IslandCount; i++)
        {
            string bossId = StoryProgress.BossIdForChapter((ChapterId)(i + 1));
            var state = ResolveState(bossId);
            bool ignite = state == SealState.Sealed && !StoryProgress.IsSeen(SeenKeyPrefix + bossId);
            // 새로 봉인한 섬은 풀린 모습으로 두었다가 소환·시작 대사가 끝난 뒤 불을 켠다
            Apply(i, ignite ? SealState.Unsealed : state, 1f);
            if (ignite && newlySealed < 0) newlySealed = i;
            log.Append(bossId).Append('=').Append(state).Append(ignite ? "(점등)" : "").Append(' ');
        }
        Debug.Log(log.ToString(), this);

        if (newlySealed < 0) return;
        int last = -1;   // 여러 섬이 한꺼번에 새로 켜지면(업데이트 전 봉인분) 멀린은 마지막 하나만 말한다 — 한 순간에 하나만
        for (int i = newlySealed; i < IslandCount; i++)
            if (IsNewSeal(i)) last = i;
        for (int i = newlySealed; i < IslandCount; i++)
            if (IsNewSeal(i)) await IgniteAsync(i, true, i == last, ct);
    }

    /// <summary>
    /// 섬 상태 — 순환 개정 §5-5(7a 09-29): 풀림 = 해방 전·봉인 전 · 봉인 = 해방 전·봉인함 · 깨짐 = 해방 뒤·다시 못 잡음 · 처치 = 해방 뒤·다시 잡음.
    /// 「해방됨」은 지금 <see cref="StoryProgress.IsNightmare"/>(= 리치 봉인 깨짐)로 읽는다 — StoryProgress.IsLiberated가 생기면 이 줄만 바꾼다.
    /// ⑤ 악몽 표식(nmkill.&lt;boss&gt;)은 그 기록이 생기면 여기서 가른다.
    /// </summary>
    private static SealState ResolveState(string bossId)
    {
        bool liberated = StoryProgress.IsNightmare;
        if (liberated) return StoryProgress.IsKilled(bossId) ? SealState.Slain : SealState.Broken;
        return StoryProgress.IsSealed(bossId) ? SealState.Sealed : SealState.Unsealed;
    }

    /// <summary>새로 봉인한 섬 점등 — 광원·수정 빛이 오르고 사슬이 뻗는다. 입력·카메라는 그대로.</summary>
    private static bool IsNewSeal(int i)
    {
        string bossId = StoryProgress.BossIdForChapter((ChapterId)(i + 1));
        return ResolveState(bossId) == SealState.Sealed && !StoryProgress.IsSeen(SeenKeyPrefix + bossId);
    }

    private async UniTask IgniteAsync(int i, bool record, bool speak, CancellationToken ct)
    {
        try
        {
            if (record)
            {
                await UniTask.WaitUntil(() => BaseCampBootstrapper.Instance == null || BaseCampBootstrapper.Instance.IsReady, cancellationToken: ct);
                await UniTask.Delay(TimeSpan.FromSeconds(0.8f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            Apply(i, SealState.Unsealed, 1f);
            float t0 = Time.unscaledTime;
            while (true)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0) / IgniteSeconds);
                Apply(i, SealState.Sealed, Mathf.SmoothStep(0f, 1f, k));
                if (k >= 1f) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            var isl = _islands[i];
            if (isl.Mist != null) isl.Mist.Emit(40);   // 불이 들어오는 순간 한 번 피어오름

            if (!record) return;
            string bossId = StoryProgress.BossIdForChapter((ChapterId)(i + 1));
            StoryProgress.MarkSeen(SeenKeyPrefix + bossId);
            if (speak && !string.IsNullOrEmpty(newSealLine))
                RelicFairy.UI.UI_BossBark.Show(string.Format(newSealLine, BossName(i)), RelicFairy.UI.BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
            Debug.Log($"[SealShrine] 점등 — {bossId}", this);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>겉모습 적용 — k(0~1)는 봉인 점등 중간값(1 = 완전).</summary>
    private void Apply(int i, SealState state, float k)
    {
        var isl = _islands[i];
        if (isl == null) return;
        isl.State = state;
        Color boss = BossColor(i);

        Color baseCol, emission, mist, light;
        float lightK, mistRate, chainK;
        switch (state)
        {
            case SealState.Sealed:
                baseCol  = Color.Lerp(boss * 0.35f, Color.Lerp(boss, sealedGold, 0.35f), k);
                emission = Color.Lerp(boss * 0.25f, boss * 1.6f + sealedGold * 1.2f, k);
                mist     = Color.Lerp(boss, sealedGold, k);
                light    = Color.Lerp(boss, sealedGold, 0.5f);
                lightK   = Mathf.Lerp(0.25f, 1f, k);
                mistRate = 5f;
                chainK   = k;
                break;
            case SealState.Broken:
                baseCol  = brokenViolet * 0.5f;
                emission = brokenViolet * 0.9f;
                mist     = brokenViolet;
                light    = brokenViolet;
                lightK   = 0.55f;
                mistRate = 7f;
                chainK   = 0f;
                break;
            case SealState.Slain:
                baseCol  = slainRed * 0.45f;
                emission = slainRed * 1.1f;
                mist     = slainRed;
                light    = slainRed;
                lightK   = 0.45f;
                mistRate = 3f;
                chainK   = 0f;
                break;
            default:   // 풀림 — 수정은 어둡고 보스 색 안개가 섬에서 피어오른다: 「저기 아직 무언가 있다」
                baseCol  = boss * 0.3f;
                emission = boss * 0.25f;
                mist     = boss;
                light    = boss;
                lightK   = 0.25f;
                mistRate = 10f;
                chainK   = 0f;
                break;
        }

        if (isl.CrystalRenderers != null)
        {
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, baseCol);
            _mpb.SetColor(EmissionColorId, emission);
            foreach (var r in isl.CrystalRenderers) r.SetPropertyBlock(_mpb);
        }
        if (isl.Crystal != null)
        {
            bool tilted = state == SealState.Broken || state == SealState.Slain;
            isl.Crystal.localRotation = tilted ? isl.CrystalRest * Quaternion.Euler(BrokenTilt, 0f, BrokenTilt * 0.5f) : isl.CrystalRest;
        }
        if (isl.Light != null)
        {
            isl.Light.color = light;
            isl.Light.intensity = isl.LightMax * lightK;
        }
        BaseCampParticles.Retint(isl.Mist, mist, mistRate);

        if (isl.Chains != null)
        {
            Color chainCol = sealedGold * 1.4f;
            chainCol.a = chainK;
            foreach (var c in isl.Chains)
            {
                if (c == null) continue;
                c.enabled = chainK > 0.01f;
                c.widthMultiplier = ChainWidth * chainK;
                c.startColor = c.endColor = chainCol;
            }
        }
        if (isl.Rings != null)
        {
            // 봉인진 — 봉인이면 금빛 가득, 풀림이면 보스 색 흐리게, 깨짐·처치는 그 색
            Color ringCol = state == SealState.Sealed ? Color.Lerp(boss, sealedGold, k) * Mathf.Lerp(0.6f, 1.6f, k) : light * 0.9f;
            ringCol.a = state == SealState.Unsealed ? 0.45f : 0.9f;
            foreach (var ring in isl.Rings)
                if (ring != null) ring.startColor = ring.endColor = ringCol;
        }
        if (isl.Tether != null)
        {
            isl.Tether.enabled = state == SealState.Sealed && k > 0.99f;
            Color tc = sealedGold;
            tc.a = 0.8f;
            isl.Tether.startColor = isl.Tether.endColor = tc;
        }
        if (isl.Label != null) isl.Label.text = ComposeLabel(i, state);
    }

    private Island BindIsland(int i, Transform root, Transform plaque)
    {
        var isl = new Island();
        if (root == null)
        {
            Debug.LogWarning($"[SealShrine] Island_{i + 1} 없음 — 프리팹 Wing/SealIslands 구성을 확인", this);
            return isl;
        }

        isl.Crystal = root.Find("CrystalPivot/Crystal");
        if (isl.Crystal != null)
        {
            isl.CrystalRest = isl.Crystal.localRotation;
            isl.CrystalRenderers = isl.Crystal.GetComponentsInChildren<Renderer>(true);
            if (crystalMaterial != null)
                foreach (var r in isl.CrystalRenderers) r.sharedMaterial = crystalMaterial;
        }

        var lightTf = root.Find("Light");
        if (lightTf != null && lightTf.TryGetComponent(out isl.Light)) isl.LightMax = isl.Light.intensity;

        // 기둥 꼭대기 → 수정 가운데: 빛 사슬(수정은 제자리에서 돌기만 해서 끝점이 고정이다)
        Vector3 core = isl.Crystal != null ? isl.Crystal.position : root.position;
        isl.PillarTops = new Vector3[4];
        isl.Chains = new LineRenderer[4];
        for (int p = 0; p < 4; p++)
        {
            var pillar = root.Find($"Pillar_{p + 1}");
            if (pillar == null) continue;
            var rend = pillar.GetComponentInChildren<Renderer>();
            Vector3 top = rend != null ? new Vector3(pillar.position.x, rend.bounds.max.y, pillar.position.z) : pillar.position;
            isl.PillarTops[p] = top;
            var lr = BaseCampParticles.Beam($"~SealChain_{p + 1}", root, beamMaterial, ChainWidth);
            lr.SetPosition(0, top);
            lr.SetPosition(1, core);
            isl.Chains[p] = lr;
        }

        // 봉인진 고리 둘 — 받침 윗면에(내려다보는 카메라에 섬이 무엇인지 읽히게)
        var dais = root.Find("Dais");
        var daisRend = dais != null ? dais.GetComponentInChildren<Renderer>() : null;
        if (daisRend != null)
        {
            Vector3 c = daisRend.bounds.center;
            c.y = daisRend.bounds.max.y + 0.06f;
            float r = Mathf.Min(daisRend.bounds.extents.x, daisRend.bounds.extents.z);
            isl.Rings = new[]
            {
                BaseCampParticles.Ring("~SealRing_Outer", root, c, r * 0.82f, beamMaterial, RingWidth),
                BaseCampParticles.Ring("~SealRing_Inner", root, c, r * 0.5f, beamMaterial, RingWidth * 0.7f),
            };
        }

        isl.Mist = BaseCampParticles.Motes("~SealMist", root, root.InverseTransformPoint(core) + Vector3.down * 1.5f,
                                           new Vector3(5f, 1f, 5f), 8f, 4.5f, 1.2f, BossColor(i),
                                           new Vector3(-0.15f, 0.5f, -0.15f), new Vector3(0.15f, 1.2f, 0.15f), moteMaterial);

        isl.Plaque = plaque;
        if (plaque != null)
        {
            isl.Tether = BaseCampParticles.Beam("~SealTether", root, beamMaterial, TetherWidth);
            isl.Tether.SetPosition(0, core);
            isl.Tether.SetPosition(1, plaque.position + Vector3.up * 0.9f);
            isl.Label = CreateLabel(plaque);
            BaseCampLabelRule.Register(plaque);
        }
        return isl;
    }

    private TextMeshPro CreateLabel(Transform plaque)
    {
        var go = new GameObject("SealPlaqueLabel");
        go.transform.SetParent(transform, false);
        go.transform.position = plaque.position + Vector3.up * PlaqueLabelY;
        var t = go.AddComponent<TextMeshPro>();
        if (labelFont != null) t.font = labelFont;
        // 프리팹 루트가 1.25배라 글자도 커진다 — 씬에 있는 스테이션 이름표(3)와 같은 크기로 보이게 나눈다
        t.fontSize = 3f / Mathf.Max(0.01f, t.transform.lossyScale.x);
        t.alignment = TextAlignmentOptions.Center;
        t.color = sealedGold;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(t);
        t.enabled = false;   // 가까이 간 곳 하나만 — LateUpdate가 켠다
        return t;
    }

    private string ComposeLabel(int i, SealState state)
    {
        string line = state switch
        {
            SealState.Sealed => "봉인됨",
            SealState.Broken => "봉인이 깨졌다",
            SealState.Slain  => "쓰러뜨렸다",
            _                => "아직 봉인되지 않았다",
        };
        return $"{BossName(i)}\n<size=60%>{line}</size>";
    }

    private string BossName(int i) => bossNames != null && i < bossNames.Length ? bossNames[i] : $"Ch{i + 1}";

    private Color BossColor(int i) => bossColors != null && i < bossColors.Length ? bossColors[i] : Color.white;
}
