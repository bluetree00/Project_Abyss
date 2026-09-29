using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// [실측 도구] 서약 대상 탐색(OverlapSphereNonAlloc · 버퍼 32 · 레이어 필터 없음)이 바닥 콜라이더가 깔린 방에서
/// 몬스터를 잡는지 잰다. 방은 1m 칸마다 바닥 블록(BoxCollider, Ground 레이어)을 하나씩 깐다(MapBuilder).
///
/// 한 번의 동기 호출 안에서 y=-5000에 HideAndDontSave 오브젝트를 만들고 재고 지운다 —
/// 플레이 모드·씬 저장 없이 공유 에디터의 다른 작업과 겹치지 않는다.
/// 결과: Temp/covenant_query_probe.json
/// </summary>
public static class CovenantQueryProbeEditor
{
    private const float BaseY = -5000f;

    [MenuItem("RelicFairy/Debug/서약 탐색 버퍼 실측")]
    private static void Run()
    {
        var sb = new StringBuilder();
        sb.Append("{\"cases\":[");
        bool first = true;

        int[] floorRadii = { 0, 2, 3, 4, 6 };   // 바닥을 깔 반경(칸) — 0이면 바닥 없음
        foreach (int fr in floorRadii)
        foreach (bool monsterFirst in new[] { true, false })
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Measure(fr, monsterFirst));
        }

        sb.Append("]}");
        string path = Path.Combine("Temp", "covenant_query_probe.json");
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[CovenantQueryProbe] " + sb);
    }

    /// <summary>
    /// 실제 서약 코드(<see cref="AssembledCovenant"/>의 private 탐색 함수)를 같은 바닥 위에서 부른다.
    /// 몬스터는 실제 MonsterBase 파생(SlimeMonster) — 편집 모드라 Awake/OnEnable은 돌지 않고 IsDead=false로 읽힌다.
    /// </summary>
    [MenuItem("RelicFairy/Debug/서약 탐색 실측 (실제 코드)")]
    private static void RunRealCode()
    {
        var root = new GameObject("~CovenantQueryProbeReal") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, BaseY, 0f);
        var sb = new StringBuilder();
        try
        {
            int ground     = LayerMask.NameToLayer("Ground");
            int monsterHit = LayerMask.NameToLayer("MonsterHit");
            Vector3 center = new Vector3(0f, BaseY + 0.5f, 0f);

            int floors = 0;
            const int r = 6;
            for (int x = -r; x <= r; x++)
            for (int z = -r; z <= r; z++)
            {
                if (x * x + z * z > r * r) continue;
                var f = new GameObject("floor") { hideFlags = HideFlags.HideAndDontSave };
                f.transform.SetParent(root.transform, false);
                f.transform.position = new Vector3(x, BaseY, z);
                f.AddComponent<BoxCollider>();
                if (ground >= 0) f.layer = ground;
                floors++;
            }

            var monsters = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                var m = MakeMonster(root.transform, center + new Vector3(0f, 0f, i - 1f), monsterHit);
                m.AddComponent<SlimeMonster>();
                monsters.Add(m);
            }
            Physics.SyncTransforms();

            var cov  = new AssembledCovenant("streak", "stasis");
            var type = typeof(AssembledCovenant);
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;

            var collect = type.GetMethod("CollectLiveEnemies", inst);
            var nearest = type.GetMethod("NearestLiveEnemy", inst);
            var scratch = (IList)type.GetField("_areaScratch", inst).GetValue(cov);

            collect.Invoke(cov, new object[] { center, 5f, null });
            int collected = scratch.Count;
            scratch.Clear();

            var near = nearest.Invoke(cov, new object[] { center, 8f, null }) as GameObject;

            sb.Append("{\"floors\":").Append(floors)
              .Append(",\"monsters\":").Append(monsters.Count)
              .Append(",\"collect_r5\":").Append(collected)
              .Append(",\"nearest_r8\":\"").Append(near != null ? near.name : "null").Append("\"}");
        }
        catch (System.Exception e)
        {
            sb.Append("{\"error\":\"").Append(e.GetType().Name).Append(": ")
              .Append((e.InnerException ?? e).Message.Replace("\"", "'")).Append("\"}");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Physics.SyncTransforms();
        }

        File.WriteAllText(Path.Combine("Temp", "covenant_query_probe_real.json"), sb.ToString());
        Debug.Log("[CovenantQueryProbe] 실제 코드 " + sb);
    }

    private static string Measure(int floorRadius, bool monsterFirst)
    {
        var root = new GameObject("~CovenantQueryProbe") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, BaseY, 0f);
        try
        {
            int ground     = LayerMask.NameToLayer("Ground");
            int monsterHit = LayerMask.NameToLayer("MonsterHit");
            Vector3 center = new Vector3(0f, BaseY + 0.5f, 0f);   // 바닥 윗면 = 플레이어 발

            GameObject monster = null;
            if (monsterFirst) monster = MakeMonster(root.transform, center, monsterHit);

            int floors = 0;
            for (int x = -floorRadius; x <= floorRadius; x++)
            for (int z = -floorRadius; z <= floorRadius; z++)
            {
                if (floorRadius == 0) continue;
                if (x * x + z * z > floorRadius * floorRadius) continue;
                var f = new GameObject("floor") { hideFlags = HideFlags.HideAndDontSave };
                f.transform.SetParent(root.transform, false);
                f.transform.position = new Vector3(x, BaseY, z);
                f.AddComponent<BoxCollider>();   // 1m 큐브 — 윗면 y = BaseY + 0.5
                if (ground >= 0) f.layer = ground;
                floors++;
            }

            if (!monsterFirst) monster = MakeMonster(root.transform, center, monsterHit);
            Physics.SyncTransforms();

            var probe32 = new Collider[32];

            // ① 서약과 같은 호출(레이어 인자 생략) — 반경 5 / 8
            int n5 = Physics.OverlapSphereNonAlloc(center, 5f, probe32);
            bool found5 = Contains(probe32, n5, monster);
            int n8 = Physics.OverlapSphereNonAlloc(center, 8f, probe32);
            bool found8 = Contains(probe32, n8, monster);

            // ② 할당형(초신성 DealAoe) — 잘리지 않는다
            var all = Physics.OverlapSphere(center, 5f);
            bool foundAlloc = System.Array.IndexOf(all, monster.GetComponent<Collider>()) >= 0;

            // ③ 프로젝트 표준(CombatQuery) — MonsterHit 마스크 · 트리거 포함
            int nMask = Physics.OverlapSphereNonAlloc(center, 5f, probe32, MonsterBase.HitLayerMask,
                                                      QueryTriggerInteraction.Collide);
            bool foundMask = Contains(probe32, nMask, monster);

            return "{\"floorRadius\":" + floorRadius
                 + ",\"floors\":" + floors
                 + ",\"monsterFirst\":" + (monsterFirst ? "true" : "false")
                 + ",\"allInR5\":" + all.Length
                 + ",\"nonAlloc32_r5\":" + n5 + ",\"found_r5\":" + (found5 ? "true" : "false")
                 + ",\"nonAlloc32_r8\":" + n8 + ",\"found_r8\":" + (found8 ? "true" : "false")
                 + ",\"alloc_found\":" + (foundAlloc ? "true" : "false")
                 + ",\"mask_n\":" + nMask + ",\"mask_found\":" + (foundMask ? "true" : "false")
                 + "}";
        }
        finally
        {
            Object.DestroyImmediate(root);
            Physics.SyncTransforms();
        }
    }

    /// <summary>실제 몬스터 프리팹과 같은 구성: 루트에 캡슐 1개 + 키네마틱 Rigidbody, 3m 앞.</summary>
    private static GameObject MakeMonster(Transform parent, Vector3 center, int layer)
    {
        var m = new GameObject("monster") { hideFlags = HideFlags.HideAndDontSave };
        m.transform.SetParent(parent, false);
        m.transform.position = center + new Vector3(3f, 1f, 0f);
        var cap = m.AddComponent<CapsuleCollider>();
        cap.height = 2f;
        cap.radius = 0.5f;
        var rb = m.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;
        if (layer >= 0) m.layer = layer;
        return m;
    }

    private static bool Contains(IReadOnlyList<Collider> buf, int n, GameObject go)
    {
        for (int i = 0; i < n; i++)
            if (buf[i] != null && buf[i].gameObject == go) return true;
        return false;
    }
}
