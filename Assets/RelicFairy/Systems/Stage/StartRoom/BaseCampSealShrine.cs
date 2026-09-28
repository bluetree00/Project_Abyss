using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 거점 봉인석 넷 — 이야기 진행이 화면이 아니라 <b>공간에</b> 보이게 한다.
///   · 봉인기: 봉인한 보스의 돌이 금빛으로 켜진다(아직이면 빈 받침).
///   · 붕괴 뒤(악몽기): 넷 모두 깨진 모습(보라, 기울어짐) → 처치한 보스는 붉은 표식.
/// 순서는 이 오브젝트의 오른쪽(+X)으로 숲 · 불 · 검 · 성소(Ch1~4).
///
/// 거점은 복귀마다 새로 로드되므로 시작할 때 한 번만 상태를 읽는다. 아트가 오기 전까지 프리미티브(판정 없음).
/// 설계: 기획 「최종장이후_사이클시나리오」 v2 §3-1 · §10 S5.
/// </summary>
public sealed class BaseCampSealShrine : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const int   StoneCount     = 4;
    private const float PedestalRadius = 0.55f;
    private const float CrystalWidth   = 0.42f;
    private const float BrokenTilt     = 28f;     // 깨진 돌이 기우는 각도
    private const float BrokenShrink   = 0.55f;   // 깨진 돌 높이 비율

    private static readonly Color PedestalColor = new Color(0.16f, 0.16f, 0.20f);
    private static readonly Color EmptyColor    = new Color(0.30f, 0.30f, 0.34f);
    private static readonly Color SlainColor    = new Color(0.95f, 0.15f, 0.15f);

    private enum StoneState { Empty, Lit, Broken, Slain }

    // ── Serialized ────────────────────────────────────────────────
    [Tooltip("돌 사이 간격(m)")]
    [SerializeField, Min(0.5f)] private float spacing = 2.4f;
    [Tooltip("받침 높이(m)")]
    [SerializeField, Min(0.1f)] private float pedestalHeight = 0.9f;
    [Tooltip("봉인석(결정) 높이(m)")]
    [SerializeField, Min(0.2f)] private float crystalHeight = 1.6f;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Start() => BuildWhenReadyAsync(destroyCancellationToken).Forget();

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>계정 기록(자동 로그인·세이브 로드)이 준비된 뒤에 돌을 세운다 — 씬 직접 실행에서도 상태가 맞게.</summary>
    private async UniTaskVoid BuildWhenReadyAsync(CancellationToken ct)
    {
        try
        {
            await UniTask.WaitUntil(() => AppBootstrapper.Instance == null || AppBootstrapper.Instance.IsReady,
                                    cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        StoryProgress.EnsureConsistency();

        var log = new System.Text.StringBuilder("[SealShrine] ");
        for (int i = 0; i < StoneCount; i++)
        {
            string bossId = StoryProgress.BossIdForChapter((ChapterId)(i + 1));
            var state = ResolveState(bossId);
            BuildStone(i, state);
            log.Append(bossId).Append('=').Append(state).Append(' ');
        }
        Debug.Log(log.ToString(), this);
    }

    private static StoneState ResolveState(string bossId)
    {
        if (StoryProgress.IsNightmare)
            return StoryProgress.IsKilled(bossId) ? StoneState.Slain : StoneState.Broken;
        return StoryProgress.IsSealed(bossId) ? StoneState.Lit : StoneState.Empty;
    }

    private void BuildStone(int index, StoneState state)
    {
        float   offset = (index - (StoneCount - 1) * 0.5f) * spacing;
        Vector3 basePos = transform.TransformPoint(new Vector3(offset, 0f, 0f));

        var pedestal = PatternGuideHelper.Pillar(basePos, PedestalRadius, pedestalHeight, PedestalColor);
        pedestal.name = $"SealStone_{index + 1}_Pedestal";
        pedestal.transform.SetParent(transform, true);

        Vector3 bottom = basePos + Vector3.up * pedestalHeight;
        Vector3 top;
        Color   color;
        switch (state)
        {
            case StoneState.Lit:
                top   = bottom + Vector3.up * crystalHeight;
                color = PatternGuideHelper.PlayerSeal;
                break;
            case StoneState.Broken:
                top   = bottom + Quaternion.AngleAxis(BrokenTilt, transform.forward) * Vector3.up * (crystalHeight * BrokenShrink);
                color = PatternGuideHelper.Reversed;
                break;
            case StoneState.Slain:
                top   = bottom + Quaternion.AngleAxis(BrokenTilt, transform.forward) * Vector3.up * (crystalHeight * BrokenShrink);
                color = SlainColor;
                break;
            default:
                // 빈 받침 — 낮은 흐린 돌만 둔다(봉인하면 여기에 불이 들어온다)
                top   = bottom + Vector3.up * (crystalHeight * 0.25f);
                color = EmptyColor;
                break;
        }

        var crystal = PatternGuideHelper.Link(bottom, top, CrystalWidth, color);
        crystal.name = $"SealStone_{index + 1}_{state}";
        crystal.transform.SetParent(transform, true);
    }
}
