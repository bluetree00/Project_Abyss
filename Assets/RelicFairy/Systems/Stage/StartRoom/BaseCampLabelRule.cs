using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 베이스캠프 월드 글자 규칙 「조용히 · 하나만」(2차 개편 09-29) — 스테이션 이름표는 <b>가까이 간 곳 하나만</b> 보인다.
/// 광장에 서면 스테이션 이름표 6장 + 구역 이름판 8장이 한꺼번에 떠 과했다(실측 09-29, 문턱 한 화면 최대 15개).
/// 멀리서 갈 곳을 알리는 일은 온보딩 목표 이름판(<see cref="ZoneSign"/>)과 물건의 모양·빛이 맡는다.
/// 이름표를 가진 쪽이 <see cref="Register"/>하고, 매 프레임 <see cref="IsShown"/>으로 켜고 끈다(가장 가까운 하나를 프레임당 한 번 고른다).
/// </summary>
public static class BaseCampLabelRule
{
    // ── Constants ─────────────────────────────────────────────
    private const float NearRadius = 9f;   // 월드 m — 구역 반경(7~8)보다 조금 넓게: 다가가는 중에 미리 뜬다

    // ── Static ────────────────────────────────────────────────
    private static readonly List<Transform> s_anchors = new();
    private static int       s_frame = -1;
    private static Transform s_nearest;

    // 도메인 리로드가 꺼져 있으면 정적 목록이 이전 플레이에서 남는다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_anchors.Clear();
        s_frame   = -1;
        s_nearest = null;
    }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>이름표 기준점(보통 스테이션 자신)을 등록한다.</summary>
    public static void Register(Transform anchor)
    {
        if (anchor != null && !s_anchors.Contains(anchor)) s_anchors.Add(anchor);
    }

    public static void Unregister(Transform anchor) => s_anchors.Remove(anchor);

    /// <summary>이 이름표를 지금 보일까 — 플레이어에게 가장 가까운(반경 안) 하나만. 전경 연출·구역 배너 동안은 전부 숨긴다.</summary>
    public static bool IsShown(Transform anchor)
    {
        if (ZoneSign.LabelsHidden || ZoneSign.BannerShowing) return false;
        if (s_frame != Time.frameCount)
        {
            s_frame   = Time.frameCount;
            s_nearest = FindNearest();
        }
        return anchor == s_nearest;
    }

    // ── Private Methods ───────────────────────────────────────

    private static Transform FindNearest()
    {
        var player = Managers.Player?.PlayerTransform;
        if (player == null) return null;

        Vector3   p      = player.position;
        Transform best   = null;
        float     bestSq = NearRadius * NearRadius;
        for (int i = s_anchors.Count - 1; i >= 0; i--)
        {
            var a = s_anchors[i];
            if (a == null) { s_anchors.RemoveAt(i); continue; }
            if (!a.gameObject.activeInHierarchy) continue;
            float sq = (a.position - p).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = a; }
        }
        return best;
    }
}
