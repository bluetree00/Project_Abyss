using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보상 오브젝트 등급 연출 미리보기(에디터 전용) — 플레이어 앞 4 m에 그 등급의 예고 → 등장 → 놓여 있는 모습을 세우고 화면을 찍는다.
/// 결과: 프로젝트 Logs/reward_fx/&lt;시각&gt;_&lt;등급&gt;/ (예고 중간 · 등장 직후 · 0.6초 뒤 · 놓여 있는 모습).
/// 전투 · 굴림 · 출구 보류와 무관한 <b>보여 주기 확인용</b>이다 — 실제 방 클리어 확인은 자동 런으로 한다.
/// </summary>
public static partial class RewardPresentationDebugMenu
{
    private const float PreviewAhead = 4f;
    private const float PreviewLift  = 0.6f;    // RoomClearGate.effectHeightOffset과 같은 값
    private const float PreviewStay  = 3.5f;    // 놓여 있는 모습을 보는 시간
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static CancellationTokenSource s_previewCts;

    [MenuItem(Root + "Preview Common (Play)")]    public static void PreviewCommon()    => Preview(ItemRarity.Common);
    [MenuItem(Root + "Preview Rare (Play)")]      public static void PreviewRare()      => Preview(ItemRarity.Rare);
    [MenuItem(Root + "Preview Epic (Play)")]      public static void PreviewEpic()      => Preview(ItemRarity.Epic);
    [MenuItem(Root + "Preview Legendary (Play)")] public static void PreviewLegendary() => Preview(ItemRarity.Legendary);

    private static void Preview(ItemRarity rarity)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RewardFx] 플레이 모드에서만."); return; }
        s_previewCts?.Cancel();
        s_previewCts?.Dispose();
        s_previewCts = new CancellationTokenSource();
        PreviewAsync(rarity, s_previewCts.Token).Forget();
    }

    private static async UniTaskVoid PreviewAsync(ItemRarity rarity, CancellationToken ct)
    {
        GameObject reward = null;
        try
        {
            var player = Managers.Player?.PlayerTransform;
            var cam    = Camera.main;
            if (player == null || cam == null) { Debug.LogWarning("[RewardFx] 플레이어 · 카메라 없음"); return; }

            Vector3 ahead = cam.transform.forward;
            ahead.y = 0f;
            ahead   = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
            float   floorY = player.position.y;
            Vector3 spot   = player.position + ahead * PreviewAhead + Vector3.up * PreviewLift;

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "reward_fx", $"{DateTime.Now:MMdd_HHmmss}_{rarity}");
            Directory.CreateDirectory(dir);
            var   spec = RewardPresentation.World(rarity);
            float t0   = Time.realtimeSinceStartup;

            ShotAt(dir, "1_foretell", t0 + spec.Foretell * 0.7f, ct).Forget();
            var aura = await RewardObjectPresenter.ForetellAsync(spot, floorY, rarity, ct);
            float arrived = Time.realtimeSinceStartup;
            Debug.Log($"[RewardFx] {rarity} 예고 {arrived - t0:0.00}초(표 {spec.Foretell:0.00}) · 빛 {(aura != null ? "있음" : "없음")} · 목록 {(RunFx.IsReady ? "있음" : "없음")}");

            reward = SpawnLook(spot);
            RewardObjectPresenter.Arrive(reward, aura, floorY, rarity);

            await Shot(dir, "2_arrive", arrived + 0.12f, ct);
            await Shot(dir, "3_arrive_0.6", arrived + 0.6f, ct);
            await Shot(dir, "4_idle", arrived + PreviewStay - 0.5f, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(0.5), DelayType.Realtime, cancellationToken: ct);
            Debug.Log($"[RewardFx] 미리보기 완료 — {dir} · 시간 배율 {Time.timeScale:0.00}");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (reward != null) UnityEngine.Object.Destroy(reward);   // 사라지면 빛도 스스로 흩어진다
        }
    }

    /// <summary>실제 보상 오브젝트와 같은 모양(트리거 없이) — 런 부트스트래퍼가 든 프리팹을 빌린다. 없으면 빈 오브젝트.</summary>
    private static GameObject SpawnLook(Vector3 spot)
    {
        var boot   = GameRunBootstrapper.Instance;
        var prefab = boot != null
            ? typeof(GameRunBootstrapper).GetField("clearEndEffect2Prefab", Inst)?.GetValue(boot) as GameObject
            : null;
        var go = prefab != null ? UnityEngine.Object.Instantiate(prefab, spot, Quaternion.identity)
                                : new GameObject("RewardPreview");
        go.transform.position = spot;
        return go;
    }

    private static async UniTaskVoid ShotAt(string dir, string name, float realtime, CancellationToken ct)
    {
        try { await Shot(dir, name, realtime, ct); }
        catch (OperationCanceledException) { }
    }

    private static async UniTask Shot(string dir, string name, float realtime, CancellationToken ct)
    {
        while (Time.realtimeSinceStartup < realtime) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null || !Application.isPlaying) return;
        await UniTask.WaitForEndOfFrame(runner, ct);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }
}
