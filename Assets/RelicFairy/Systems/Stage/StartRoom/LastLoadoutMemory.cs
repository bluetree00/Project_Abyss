using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 지난 세팅 유지(10-29 사용자 「원거리나 파츠 유물등을 선택하지 않더라도 이전의 세팅을 유지 · 변경을 하고싶다면 할수있도록」).
/// 성문을 지날 때 원거리 · 유물을 슬롯 메타(<see cref="UserGameData.lastLoadout"/> · 로컬 파일만)에 적고,
/// 거점에 돌아오면 <b>비어 있는 칸만</b> 그대로 쥐여 준다 — 처음 한 번(기록 없음)만 공방 · 유물 제단을 들른다.
/// 무형검은 지금처럼 <see cref="BaseCampBootstrapper"/>가 준다. 시작 파츠는 제단 해금이 정한다(GameRunSession).
/// 해금이 빠진 원거리(석궁)는 그 칸만 비운다 — 바꾸기는 공방 · 유물 제단에서 지금처럼, 다시 고른 것이 다음 성문에서 새 기록이 된다.
/// </summary>
public static class LastLoadoutMemory
{
    // ── Constants ─────────────────────────────────────────────
    private const char Sep = '|';

    // ── Static ────────────────────────────────────────────────
    /// <summary>이번 거점 도착에서 지난 세팅을 쥐여 줬는가 — 도착 알림 한 줄.</summary>
    public static bool RestoredThisVisit { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => RestoredThisVisit = false;

    // ── Public Methods ────────────────────────────────────────

    /// <summary>성문 통과 — 지금 들고 가는 원거리 · 유물을 적는다(런이 어떻게 끝나든 다음 귀환에 쓴다).</summary>
    public static void Capture(PlayerLoadout lo)
    {
        var gd = BackendGameData.Instance;
        if (gd?.Data == null || lo == null) return;
        gd.Data.lastLoadout = (lo.WeaponSlot1 != null ? lo.WeaponSlot1.name : "") + Sep + (lo.Relic != null ? lo.Relic.name : "");
        gd.SaveAsync().Forget();
    }

    /// <summary>
    /// 거점 도착(플레이어를 만들기 전) — 비어 있는 칸을 지난 세팅으로 채운다.
    /// <paramref name="rangedAllowed"/>: 공방이 내놓는 원거리인가(석궁 해금 등) — 아니면 원거리를 채우지 않는다.
    /// </summary>
    public static async UniTask RestoreAsync(PlayerLoadout lo, Func<WeaponSO, bool> rangedAllowed, CancellationToken ct)
    {
        RestoredThisVisit = false;
        string raw = BackendGameData.Instance?.Data?.lastLoadout;
        if (lo == null || string.IsNullOrEmpty(raw)) return;

        var p = raw.Split(Sep);
        string ranged = p.Length > 0 ? p[0] : "";
        string relic  = p.Length > 1 ? p[1] : "";
        bool any = false;

        if (lo.WeaponSlot1 == null && !string.IsNullOrEmpty(ranged))
        {
            var w = await Managers.AddressableManager.TryLoadAssetAsync<WeaponSO>(ranged);
            ct.ThrowIfCancellationRequested();
            if (w != null && (rangedAllowed == null || rangedAllowed(w))) { lo.SetWeaponSlot1(w); any = true; }
        }

        if (lo.Relic == null && !string.IsNullOrEmpty(relic))
        {
            var r = await Managers.AddressableManager.TryLoadAssetAsync<RelicClassSO>(relic);
            ct.ThrowIfCancellationRequested();
            if (r != null) { lo.SetRelic(r); any = true; }
        }

        RestoredThisVisit = any;
        if (any) Debug.Log($"[LastLoadout] 지난 세팅 복원 — {Describe(lo)}");
    }

    /// <summary>「석궁 · 가웨인」 — 도착 알림 · 로그.</summary>
    public static string Describe(PlayerLoadout lo)
    {
        if (lo == null) return "";
        string ranged = lo.WeaponSlot1 != null ? lo.WeaponSlot1.displayName : "";
        string relic  = lo.Relic != null ? lo.Relic.DisplayName : "";
        return ranged.Length > 0 && relic.Length > 0 ? $"{ranged} · {relic}" : ranged + relic;
    }
}
