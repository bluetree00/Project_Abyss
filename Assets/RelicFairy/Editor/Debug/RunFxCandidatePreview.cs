using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 런 공용 이펙트 후보 비교(에디터 전용) — 후보 프리팹을 레어 · 에픽 · 전설 빛깔로 나란히 띄워 찍는다.
/// 런타임과 같은 색 입히기 경로(<see cref="LichVfx.PlayEntry"/>)를 쓰므로, 찍힌 색이 곧 게임에서 보일 색이다.
/// 후보 목록: 프로젝트 Logs/reward_fx/candidates.txt — 줄마다 「에셋 경로|배율|loop 또는 once[|raw|띄울 높이]」.
/// raw = 원래 빛깔로 카메라를 보고 세운다(포탈처럼 서 있는 것 — 정면 축이 팩마다 달라 0° · 90° 두 방향).
/// 결과: Logs/reward_fx/cand_&lt;시각&gt;/&lt;번호&gt;_&lt;이름&gt;_&lt;초&gt;.png
/// </summary>
public static partial class RewardPresentationDebugMenu
{
    private const float CandidateSpacing = 3.5f;
    private static readonly float[] CandidateShots = { 0.35f, 1.0f, 2.2f };

    [MenuItem(Root + "Compare Fx Candidates (Play)")]
    public static void CompareCandidates()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RewardFx] 플레이 모드에서만."); return; }
        s_previewCts?.Cancel();
        s_previewCts?.Dispose();
        s_previewCts = new CancellationTokenSource();
        CompareAsync(s_previewCts.Token).Forget();
    }

    private static async UniTaskVoid CompareAsync(CancellationToken ct)
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "reward_fx");
        string list = Path.Combine(root, "candidates.txt");
        if (!File.Exists(list)) { Debug.LogWarning($"[RewardFx] 후보 목록 없음 — {list}"); return; }

        var player = Managers.Player?.PlayerTransform;
        var cam    = Camera.main;
        if (player == null || cam == null) { Debug.LogWarning("[RewardFx] 플레이어 · 카메라 없음"); return; }

        Vector3 ahead = cam.transform.forward; ahead.y = 0f;
        ahead = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
        Vector3 side   = Vector3.Cross(Vector3.up, ahead);
        Vector3 center = player.position + ahead * 4f;
        Color[] tints  = { RewardObjectPresenter.LightColor(ItemRarity.Rare),
                           RewardObjectPresenter.LightColor(ItemRarity.Epic),
                           RewardObjectPresenter.LightColor(ItemRarity.Legendary) };

        string dir = Path.Combine(root, $"cand_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var spawned = new List<GameObject>(3);
        int index = 0;
        try
        {
            foreach (var raw in File.ReadAllLines(list))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts  = line.Split('|');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(parts[0].Trim());
                float scale = parts.Length > 1 && float.TryParse(parts[1], out var s) ? s : 1f;
                bool  loop  = parts.Length > 2 && parts[2].Trim() == "loop";
                bool  stand = parts.Length > 3 && parts[3].Trim() == "raw";
                float lift  = parts.Length > 4 && float.TryParse(parts[4], out var h) ? h : 0f;
                index++;
                if (prefab == null) { Debug.LogWarning($"[RewardFx] 후보 {index} 없음 — {parts[0]}"); continue; }

                var entry = new LichVfxEntry { prefab = prefab, scale = 1f };
                if (stand)
                {
                    var face = Quaternion.LookRotation(-ahead, Vector3.up);
                    Vector3 c = center + ahead * 3f + Vector3.up * lift;
                    spawned.Add(LichVfx.PlayEntry(entry, c - side * CandidateSpacing, face, scale, Color.clear, loop));
                    spawned.Add(LichVfx.PlayEntry(entry, c + side * CandidateSpacing, face * Quaternion.Euler(0f, 90f, 0f), scale, Color.clear, loop));
                }
                else
                {
                    for (int i = 0; i < tints.Length; i++)
                    {
                        Vector3 p = center + side * (CandidateSpacing * (i - 1));
                        spawned.Add(LichVfx.PlayEntry(entry, p, Quaternion.identity, scale, tints[i], loop));
                    }
                }

                float t0 = Time.realtimeSinceStartup;
                foreach (float at in CandidateShots)
                    await Shot(dir, $"{index:00}_{prefab.name}_{at:0.0}", t0 + at, ct);
                Debug.Log($"[RewardFx] 후보 {index} {prefab.name} 촬영");

                for (int i = 0; i < spawned.Count; i++)
                {
                    var go = spawned[i];
                    if (go != null) LichVfx.Stop(ref go);
                }
                spawned.Clear();
                await UniTask.Delay(TimeSpan.FromSeconds(0.3), DelayType.Realtime, cancellationToken: ct);
            }
            Debug.Log($"[RewardFx] 후보 비교 완료 — {dir}");
        }
        catch (OperationCanceledException) { }
        finally
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                var go = spawned[i];
                if (go != null) LichVfx.Stop(ref go);
            }
        }
    }
}
