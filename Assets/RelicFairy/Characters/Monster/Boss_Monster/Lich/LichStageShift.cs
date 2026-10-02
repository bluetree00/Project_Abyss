using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RelicFairy.Monster
{
/// <summary>
/// 2페이즈 전환 무대 변화(09-19 사용자 지시 — 「스테이지가 극적으로 변하는 연출 · 환경은 넓게 쓰되 제한은 두고 · 웅장하게」).
/// 제단의 네 귀퉁이가 영구히 무너져 심연으로 떨어지고(넓이는 대부분 남는다),
/// 그 빈자리에서 거대한 석상이 솟아올라 제단을 에워싼다. 석상은 전투가 끝날 때까지 남는다.
/// 전환 컷신(<see cref="LichPhase2EntryState"/>)이 부른다.
/// </summary>
public static class LichStageShift
{
    // ── Constants ─────────────────────────────────────────────
    private const int   CornerSize     = 3;      // 귀퉁이마다 3×3칸 — 천공의 대제단은 귀퉁이가 이미 깎여 있어 실제로는 칸 6개씩
    private const float CollapseWarn   = 1.1f;   // 붉게 흔들린 뒤 떨어진다
    private const float RiseSeconds    = 2.6f;
    private const float RiseDepth      = 45f;    // 이만큼 아래 심연에서 솟는다
    private const float OutwardPush    = 4f;     // 석상은 귀퉁이 중심에서 바깥으로 조금 — 제단 가장자리를 가리지 않게
    private const float DustScale      = 3f;
    private const float LightRange     = 40f;    // 석상 앞을 비추는 진홍 빛 — 어두운 보스방에서 윤곽이 보이게(09-19 실측: 발치만 비쳐 안 보였다)
    private const float LightIntensity = 30f;
    private const float LightHeight    = 11f;    // 석상 가슴~두건 높이(m) — ×8 석상은 두건이 어두웠다(09-19)
    private const float LightForward   = 5f;     // 석상 앞(제단 쪽)으로
    private const float VanishSeconds  = 1.5f;   // 전투가 끝나면 디졸브로 스러진다(10-03 — 툭 꺼졌다)
    private static readonly Color LightColor = new(1f, 0.25f, 0.3f);

    private static readonly List<GameObject> s_monuments = new();
    private static readonly List<Vector2Int> s_cells     = new();
    private static CancellationTokenSource   s_cts;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_monuments.Clear();
        s_cts = null;
    }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>
    /// 무대 변화 시작 — 귀퉁이 붕괴 + 석상 상승. 플레이어가 귀퉁이에 서 있으면 안쪽으로 옮긴다.
    /// 리치가 그 제단 위에 있을 때만(테스트 씬에 아레나가 여럿이어도 엉뚱한 곳이 무너지지 않게). 무너뜨린 칸 수.
    /// </summary>
    public static int Begin(Transform lich, Transform player)
    {
        var grid = ArenaTileGrid.Active;
        if (grid == null || lich == null || !grid.TryGetCell(lich.position, out _)) return 0;
        if (!grid.TryGetWorldCenter(out var center)) return 0;

        grid.CornerCells(CornerSize, s_cells);
        MovePlayerOff(grid, player, center);
        int collapsed = grid.CollapseCells(s_cells, CollapseWarn, ignoreCap: true);

        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();

        // 귀퉁이 블록 중심 — 칸이 없어도 격자 기하로(이 제단은 귀퉁이 칸이 원래 없다).
        float lo = (CornerSize - 1) * 0.5f;
        float hi = grid.GridSize - 1 - lo;
        Vector2[] corners = { new(lo, lo), new(hi, lo), new(lo, hi), new(hi, hi) };
        foreach (var c in corners)
        {
            if (!grid.TryGetGridPoint(c.x, c.y, out var at)) continue;
            Vector3 out_ = at - center;
            out_.y = 0f;
            if (out_.sqrMagnitude > 0.01f) at += out_.normalized * OutwardPush;
            LichVfx.Play(LichVfxSlot.PhaseBurst, at + Vector3.up, Quaternion.identity, 1.6f);
            RiseAsync(at, center, s_cts.Token).Forget();
        }

        LichSfx.Play(LichSfxSlot.Collapse, center);
        return collapsed;
    }

    /// <summary>석상을 걷는다 — 전투가 끝나거나 리치가 꺼질 때. 디졸브로 스러진 뒤 파괴된다.</summary>
    public static void Clear()
    {
        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = null;
        for (int i = 0; i < s_monuments.Count; i++)
            LichPatternUtil.DissolveAndDestroy(s_monuments[i], s_monuments[i], VanishSeconds);
        s_monuments.Clear();
    }

    // ── Private Methods ───────────────────────────────────────
    private static async UniTaskVoid RiseAsync(Vector3 at, Vector3 center, CancellationToken ct)
    {
        Vector3 face = center - at;
        face.y = 0f;
        var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized) : Quaternion.identity;
        var go  = LichVfx.InstantiateModel(LichVfxSlot.MonumentModel, at - Vector3.up * RiseDepth, rot);
        if (go == null)
        {
            Debug.LogWarning("[LichStageShift] 석상 칸(MonumentModel)이 비었다 — 이펙트 목록 매핑을 확인할 것");
            return;
        }
        s_monuments.Add(go);

        // 아래에서 올려 비추는 진홍 빛 — 석상이 솟는 동안 함께 올라간다.
        var lightGo = new GameObject("MonumentLight");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, LightHeight, LightForward) / Mathf.Max(0.01f, go.transform.localScale.y);
        var light = lightGo.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = LightColor;
        light.range     = LightRange;
        light.intensity = LightIntensity;
        light.shadows   = LightShadows.None;

        LichVfx.Play(LichVfxSlot.SummonGround, at, Quaternion.identity, DustScale);
        LichPatternUtil.Impact(LichImpact.Heavy);

        Vector3 from = go.transform.position;
        Vector3 to   = from + Vector3.up * RiseDepth;
        float   t    = 0f;
        try
        {
            while (t < RiseSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / RiseSeconds);
                float e = 1f - (1f - k) * (1f - k) * (1f - k);   // 솟구치다 끝에서 천천히 멈춘다
                if (go == null) return;
                go.transform.position = Vector3.LerpUnclamped(from, to, e);
                await UniTask.Yield(ct);
            }
            if (go != null) go.transform.position = to;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>플레이어가 무너질 귀퉁이 위에 있으면 제단 안쪽(중심 방향 한 칸 반)으로 옮긴다 — 컷신 중 낙하 방지.</summary>
    private static void MovePlayerOff(ArenaTileGrid grid, Transform player, Vector3 center)
    {
        if (player == null || !grid.TryGetCell(player.position, out var cell) || !s_cells.Contains(cell)) return;
        Vector3 inward = center - player.position;
        inward.y = 0f;
        Vector3 dest = player.position + inward.normalized * (grid.CellSize * CornerSize * 1.2f);
        player.position = dest;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = dest;
            rb.linearVelocity = Vector3.zero;
        }
    }
}
}
