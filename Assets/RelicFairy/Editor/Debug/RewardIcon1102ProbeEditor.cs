#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-02 Q9 「룬 보상 위에도 아이콘」 실측(테스트 허브 런 중) — 결과 Temp/reward_icon1102.txt · 사진 Temp/ui_shots/RI1102_*.png · 로그 「[RI1102] 끝」.
/// 실제 보상 프리팹(EndEffect2 · 0.3배)을 플레이어 앞 6 m에 세우고 Rare 룬 3장 고르기로 초기화 → 표시판 높이 · 크기 · 그림 · 글 · 줍기 반경(월드) · [F] 범위 진입을 잰다.
/// </summary>
public static class RewardIcon1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 보상 구슬 아이콘 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RI1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 보상 구슬 아이콘\n");
        Directory.CreateDirectory("Temp/ui_shots");
        GameObject orb = null;
        PlayerController p = null;
        Vector3 home = Vector3.zero;
        try
        {
            var run = GameRunBootstrapper.Instance?.Run;
            p = run?.Player;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Systems/Stage/UseEffect/EndEffect2.prefab");
            var pool = ItemSORegistry.GetByRarity(ItemRarity.Rare);
            if (run == null || p == null || prefab == null || pool == null || pool.Count < 3)
            { sb.AppendLine($"런 {run != null} · 플레이어 {p != null} · 프리팹 {prefab != null} · Rare 풀 {pool?.Count ?? 0}"); return; }
            home = p.transform.position;

            var rewards = new List<(RuntimeItemData, ItemSO)> { (null, pool[0]), (null, pool[1]), (null, pool[2]) };
            Vector3 fw = p.transform.forward; fw.y = 0f; fw.Normalize();
            orb = Object.Instantiate(prefab, home + fw * 6f + Vector3.up * 0.6f, Quaternion.identity);
            var trig = orb.AddComponent<ClearRewardTrigger>();
            trig.Initialize(run, rewards, isBossRoom: false, isChoice: true, choiceRounds: 1);
            await UniTask.Delay(1000, ignoreTimeScale: true);

            var col = orb.GetComponent<SphereCollider>();
            sb.AppendLine($"보상 물체 배율 {orb.transform.lossyScale.x:0.00} · 줍기 반경(월드) {col.radius * orb.transform.lossyScale.x:0.00} m");
            var ind = orb.transform.Find("WorldIndicator") as RectTransform;
            if (ind == null) { sb.AppendLine("표시판 없음"); return; }
            float scale = ind.lossyScale.x;
            sb.AppendLine($"표시판 — 구슬 중심 위 {ind.position.y - orb.transform.position.y:0.00} m · 판 {ind.rect.width * scale:0.00} × {ind.rect.height * scale:0.00} m · 정렬 {ind.GetComponent<Canvas>().sortingOrder}");
            var icon = ind.GetComponentInChildren<Image>(true) != null ? ind.Find("IconBG/Icon")?.GetComponent<Image>() : null;
            if (icon != null)
                sb.AppendLine($"  아이콘 {(icon.gameObject.activeInHierarchy ? "보임" : "숨김")} · 그림 {(icon.sprite != null ? icon.sprite.name : "없음")} · 크기 {((RectTransform)icon.transform.parent).rect.width * scale:0.00} m · 첫 후보 그림 {(pool[0].icon != null ? pool[0].icon.name : "없음")}");
            var txt = ind.GetComponentInChildren<TextMeshProUGUI>(true);
            sb.AppendLine($"  글 「{txt?.text}」 · 글 높이 {(txt != null ? txt.fontSize * scale : 0f):0.00} m");
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/RI1102_far.png");
            await UniTask.Delay(300, ignoreTimeScale: true);

            foreach (float d in new[] { 5.0f, 4.0f, 2.0f })
            {
                Place(p, orb.transform.position - fw * d - Vector3.up * 0.6f);
                await UniTask.Delay(400, ignoreTimeScale: true);
                bool inRange = typeof(ClearRewardTrigger).GetField("_playerInRange", Inst)?.GetValue(trig) is bool b && b;
                sb.AppendLine($"  {d:0.0} m 앞 — [F] 범위 {(inRange ? "안" : "밖")} · 글 「{txt?.text}」");
                if (d < 2.5f) ScreenCapture.CaptureScreenshot("Temp/ui_shots/RI1102_near.png");
            }
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (orb != null) Object.Destroy(orb);
            if (p != null) Place(p, home);
            File.WriteAllText("Temp/reward_icon1102.txt", sb.ToString());
            Debug.Log("[RI1102] 끝 → Temp/reward_icon1102.txt");
        }
    }

    private static void Place(PlayerController p, Vector3 at)
    {
        p.transform.position = at;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }
}
#endif
