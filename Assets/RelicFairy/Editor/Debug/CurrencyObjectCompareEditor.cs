#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-12 재화 오브젝트 비교 촬영 — 사용자 「재화의 오브젝트가 너무 안 좋아」.
/// 테스트 허브에서 보스 대기방으로 들어가, 플레이어 앞 바닥에 재화 후보를 나란히 세우고 <b>게임 카메라 그대로</b> 7장을 찍는다.
///   1 지금(임시 구체 · 정육면체 · 원석/강화재료는 월드 물체 없음) · 2 권장 4종 · 3 골드 후보 · 4 정수 후보 · 5 강화재료 · 원석 후보
///   6 보스 처치 밀도(금화 36 · 정수 8) · 7 실제 드롭 경로(SpawnDrops — 목록이 적용됐는지 · 나타나기 디졸브 뒤 모습)
/// 후보는 <see cref="CurrencyVisualSetup.Specs"/>를 메모리에서만 세운다 — 플레이 중 Assets에 쓰지 않는다.
/// 인수 Temp/currency_cmp_args.txt 1줄 = 챕터(기본 1 · 어두운 바닥은 3).
/// 결과: Temp/ui_shots/CURRENCY_*.png · Temp/ui_shots/CURRENCY_report.txt · 로그 「[CurrencyCmp] 끝」.
/// </summary>
public static class CurrencyObjectCompareEditor
{
    private const BindingFlags Inst       = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const string ArgsPath = "Temp/currency_cmp_args.txt";
    private const string Dir      = "Temp/ui_shots";
    private const float  Ahead    = 2.6f;    // 플레이어 앞(카메라 반대쪽) — 화면에서 머리 위, HUD와 안 겹친다
    private const float  Spacing  = 1.1f;
    private const float  GoldRestY    = 0.05f;   // GoldCoinPickup 착지 높이(가운데)
    private const float  EssenceRestY = 0.32f;   // EssenceShardPickup 새 착지 높이
    private const float  EssenceOldY  = 0.25f;   // 예전 착지 높이(임시 정육면체)

    private static bool s_running;

    [MenuItem("RelicFairy/Debug/10-12 재화 오브젝트 비교 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CurrencyCmp] 플레이 모드(테스트 허브)에서"); return; }
        if (s_running) { Debug.LogWarning("[CurrencyCmp] 이미 찍는 중"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_running = true;
        int ch = 1;
        if (File.Exists(ArgsPath) && int.TryParse(File.ReadAllText(ArgsPath).Trim(), out var c)) ch = Mathf.Clamp(c, 1, 4);
        var sb  = new StringBuilder($"재화 오브젝트 비교 · Ch{ch} 대기방 · 게임 카메라\n");
        var own = new List<Object>();   // 이번 장면에 세운 것(오브젝트 · 메모리 재질) — 장마다 치운다
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(ch);
                typeof(TestHubLauncher).GetField("_bossApproach", Inst)?.SetValue(launcher, true);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            await UniTask.Delay(3000, DelayType.Realtime);   // 방 등장 연출 · 카메라 자리 잡기

            var cam    = Camera.main;
            var player = Managers.Player?.PlayerTransform;
            if (cam == null || player == null) { sb.AppendLine("카메라 · 플레이어 없음 — 테스트 허브에서 실행"); return; }

            await CurrencyVisuals.LoadAsync();
            sb.AppendLine($"재화 목록(Run/Drops/CurrencyVisualSet): {(CurrencyVisuals.IsReady ? "있음 — 권장 4종은 실제 프리팹" : "없음 — 만들기 메뉴 전 · 권장 4종도 후보 표로 메모리에서 세움")}");
            Directory.CreateDirectory(Dir);

            Vector3 fwd   = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.transform.right,   Vector3.up).normalized;
            Vector3 center = player.position + fwd * Ahead;

            // 1 — 지금
            sb.AppendLine("\n[1] 지금");
            Cluster(Placeholder(typeof(GoldCoinPickup)), 3, Slot(center, right, 0, 4), GoldRestY, "GOLD now", own, sb, sphere: true);
            Cluster(Placeholder(typeof(EssenceShardPickup)), 2, Slot(center, right, 1, 4), EssenceOldY, "ESSENCE now", own, sb, keepRotation: true);
            Label(Slot(center, right, 2, 4), "ORE: text only", own, cam);
            Label(Slot(center, right, 3, 4), "ENHANCE: text only", own, cam);
            await Shot("CURRENCY_1_before", own, cam);

            // 2 — 권장 4종(런타임 프리팹이 있으면 그것)
            sb.AppendLine("\n[2] 권장 4종");
            var kinds = new[] { CurrencyVisualKind.Gold, CurrencyVisualKind.Essence, CurrencyVisualKind.EnhanceMaterial, CurrencyVisualKind.RuneOre };
            int[] counts = { 3, 2, 2, 2 };
            for (int i = 0; i < kinds.Length; i++)
            {
                var pick = PickOf(kinds[i]);
                GameObject proto = CurrencyVisuals.TryCreate(kinds[i]);
                string info = proto != null ? "런타임 프리팹" : null;
                if (proto == null) proto = Build(pick, own, out info);
                Cluster(proto, counts[i], Slot(center, right, i, 4), RestY(kinds[i]), kinds[i].ToString().ToUpperInvariant(), own, sb,
                        info: $"{pick.id} · {info}");
            }
            await Shot("CURRENCY_2_after", own, cam);

            // 3~5 — 재화별 후보
            sb.AppendLine("\n[3] 골드 후보");
            Row(new GameObject[] { Placeholder(typeof(GoldCoinPickup)) }, new[] { "G0 now" }, center, right, 0, 3, GoldRestY, own, sb);
            CandRow(CurrencyVisualKind.Gold, center, right, 1, 3, own, sb);
            await Shot("CURRENCY_3_gold", own, cam);

            sb.AppendLine("\n[4] 정수 후보");
            Row(new GameObject[] { Placeholder(typeof(EssenceShardPickup)) }, new[] { "E0 now" }, center, right, 0, 4, EssenceOldY, own, sb, keepRotation: true);
            CandRow(CurrencyVisualKind.Essence, center, right, 1, 4, own, sb);
            await Shot("CURRENCY_4_essence", own, cam);

            sb.AppendLine("\n[5] 강화재료 · 원석 후보");
            int col = CandRow(CurrencyVisualKind.EnhanceMaterial, center, right, 0, 4, own, sb);
            CandRow(CurrencyVisualKind.RuneOre, center, right, col, 4, own, sb);
            await Shot("CURRENCY_5_enhance_ore", own, cam);

            // 6 — 보스 처치 밀도(DieState 보스 배율 5 × 5~8닢 ≈ 25~40 · 보스 정수 132 → 조각 8)
            sb.AppendLine("\n[6] 보스 처치 밀도 — 금화 36 · 정수 8 (반경 0.35~1.3 m, SpawnDrops와 같은 흩뿌림)");
            var gProto = CurrencyVisuals.TryCreate(CurrencyVisualKind.Gold) ?? Build(PickOf(CurrencyVisualKind.Gold), own, out _);
            var eProto = CurrencyVisuals.TryCreate(CurrencyVisualKind.Essence) ?? Build(PickOf(CurrencyVisualKind.Essence), own, out _);
            Scatter(gProto, 36, center, GoldRestY, own);
            Scatter(eProto, 8, center, EssenceRestY, own);
            await Shot("CURRENCY_6_boss_density", own, cam);

            // 7 — 실제 드롭 경로(자석 범위 밖에서 — 줍지 않는다)
            sb.AppendLine("\n[7] 실제 드롭 경로 — GoldCoinPickup.SpawnDrops(6) · EssenceShardPickup.SpawnDrops(30 → 2조각), 1.2초 뒤");
            Vector3 dropAt = player.position + right * 5.2f + fwd * 1.5f;
            GoldCoinPickup.SpawnDrops(dropAt, 6, 1);
            EssenceShardPickup.SpawnDrops(dropAt + right * 0.01f, 30);
            await UniTask.Delay(1200, DelayType.Realtime);
            int arts = 0, total = 0;
            foreach (var g in Object.FindObjectsByType<GoldCoinPickup>(FindObjectsSortMode.None))
            {
                total++;
                if (g.GetComponent<MeshFilter>() == null) arts++;   // 임시 구체는 뿌리에 메시가 있다 · 프리팹은 Body 아래에
                own.Add(g.gameObject);
            }
            foreach (var e in Object.FindObjectsByType<EssenceShardPickup>(FindObjectsSortMode.None))
            {
                total++;
                if (e.GetComponent<MeshFilter>() == null) arts++;
                own.Add(e.gameObject);
            }
            sb.AppendLine($"  떨어진 재화 {total}개 · 새 모양 {arts}개 · 임시 도형 {total - arts}개");
            await Shot("CURRENCY_7_runtime_drop", own, cam);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Clear(own);
            File.WriteAllText($"{Dir}/CURRENCY_report.txt", sb.ToString());
            Debug.Log("[CurrencyCmp] 끝\n" + sb);
            s_running = false;
        }
    }

    // ── 세우기 ─────────────────────────────────────────────

    private static CurrencyVisualSetup.Spec PickOf(CurrencyVisualKind kind)
    {
        foreach (var s in CurrencyVisualSetup.Specs) if (s.kind == kind && s.pick) return s;
        return default;
    }

    private static float RestY(CurrencyVisualKind kind) => kind switch
    {
        CurrencyVisualKind.Gold    => GoldRestY,
        CurrencyVisualKind.Essence => EssenceRestY,
        _                          => -1f,   // 월드 드롭이 아직 없다 — 바닥에 놓는다
    };

    private static GameObject Build(in CurrencyVisualSetup.Spec s, List<Object> own, out string info)
    {
        info = null;
        var mat = CurrencyVisualSetup.MakeMaterial(s);
        if (mat != null && string.IsNullOrEmpty(s.existingMat)) own.Add(mat);
        var go = CurrencyVisualSetup.BuildVisual(s, mat, null, out info);
        if (go == null) info = $"모델 없음 — {s.mesh}";
        return go;
    }

    private static GameObject Placeholder(System.Type pickup)
        => pickup.GetMethod("CreatePlaceholder", PrivStatic)?.Invoke(null, null) as GameObject;

    /// <summary>재화 하나의 후보를 칸 <paramref name="from"/>부터 차례로 세운다. 다음 빈 칸을 돌려준다.</summary>
    private static int CandRow(CurrencyVisualKind kind, Vector3 center, Vector3 right, int from, int slots, List<Object> own, StringBuilder sb)
    {
        int i = from;
        foreach (var s in CurrencyVisualSetup.Specs)
        {
            if (s.kind != kind) continue;
            var go = Build(s, own, out string info);
            string label = s.pick ? s.id + " *" : s.id;
            if (go == null) { sb.AppendLine($"  {label}: {info}"); i++; continue; }
            Place(go, Slot(center, right, i, slots), RestY(kind), own, keepRotation: false);
            Label(Slot(center, right, i, slots), label, own, Camera.main);
            sb.AppendLine($"  {label}: {info} · {s.note}");
            i++;
        }
        return i;
    }

    private static void Row(GameObject[] gos, string[] labels, Vector3 center, Vector3 right, int from, int slots, float restY,
                            List<Object> own, StringBuilder sb, bool keepRotation = false)
    {
        for (int i = 0; i < gos.Length; i++)
        {
            if (gos[i] == null) continue;
            Place(gos[i], Slot(center, right, from + i, slots), restY, own, keepRotation);
            Label(Slot(center, right, from + i, slots), labels[i], own, Camera.main);
            sb.AppendLine($"  {labels[i]}: 지금 임시 도형(Primitive)");
        }
    }

    /// <summary>한 칸에 같은 것 n개(첫 개가 원형, 나머지는 복제) — 실제 드롭처럼 몇 개가 모여 있는 모습.</summary>
    private static void Cluster(GameObject proto, int n, Vector3 at, float restY, string label, List<Object> own, StringBuilder sb,
                                bool sphere = false, bool keepRotation = false, string info = null)
    {
        if (proto == null) { sb.AppendLine($"  {label}: 없음"); return; }
        for (int k = 0; k < n; k++)
        {
            var go = k == 0 ? proto : Object.Instantiate(proto);
            float a = k * 2.4f;
            Vector3 p = at + (k == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.28f);
            Place(go, p, restY, own, keepRotation);
        }
        Label(at, label, own, Camera.main);
        sb.AppendLine($"  {label} ×{n}{(info != null ? " · " + info : "")}{(sphere ? " · 구체 중심이 바닥 +0.05 m(반지름 0.175) — 아래 0.125 m(높이의 36%)가 바닥에 묻힌다" : "")}");
    }

    private static void Scatter(GameObject proto, int n, Vector3 center, float restY, List<Object> own)
    {
        if (proto == null) return;
        var rng = new System.Random(12);
        for (int k = 0; k < n; k++)
        {
            var go = k == 0 ? proto : Object.Instantiate(proto);
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float r   = Mathf.Lerp(0.35f, 1.3f, (float)rng.NextDouble());
            Place(go, center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r), restY, own, keepRotation: false);
        }
    }

    /// <summary>
    /// 바닥 위에 놓는다. <paramref name="restY"/> ≥ 0이면 가운데를 바닥 + restY에(런타임 착지 높이와 같게), 음수면 밑면을 바닥에.
    /// 금화는 누운 채 아무 방향, 정수 캡슐은 런타임처럼 12° 기울인다.
    /// </summary>
    private static void Place(GameObject go, Vector3 at, float restY, List<Object> own, bool keepRotation)
    {
        own.Add(go);
        float floor = FloorY(at);
        if (!keepRotation)
        {
            float yaw = Random.Range(0f, 360f);
            go.transform.rotation = Mathf.Approximately(restY, EssenceRestY) ? Quaternion.Euler(0f, yaw, 12f) : Quaternion.Euler(0f, yaw, 0f);
        }
        if (restY >= 0f)
        {
            go.transform.position = new Vector3(at.x, floor + restY, at.z);
            return;
        }
        go.transform.position = new Vector3(at.x, floor, at.z);
        if (CurrencyVisualSetup.TryBounds(go, out var b))
            go.transform.position += Vector3.up * (floor - b.min.y + 0.01f);
    }

    private static Vector3 Slot(Vector3 center, Vector3 right, int i, int n) => center + right * ((i - (n - 1) * 0.5f) * Spacing);

    private static float FloorY(Vector3 at)
    {
        int mask = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI", "TransparentFX");
        return Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 5f, mask, QueryTriggerInteraction.Ignore)
            ? hit.point.y
            : Managers.Player?.PlayerTransform != null ? Managers.Player.PlayerTransform.position.y : at.y;
    }

    private static void Label(Vector3 at, string text, List<Object> own, Camera cam)
    {
        var go = new GameObject("~CurrencyLabel");
        own.Add(go);
        go.transform.position = new Vector3(at.x, FloorY(at) + 0.95f, at.z);
        if (cam != null) go.transform.rotation = cam.transform.rotation;
        var t = go.AddComponent<TextMeshPro>();
        t.text = text;
        t.fontSize = 2.6f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(t);
    }

    // ── 찍기 · 치우기 ──────────────────────────────────────

    private static async UniTask Shot(string name, List<Object> own, Camera cam)
    {
        foreach (var o in own)
            if (o is GameObject g && g.name == "~CurrencyLabel" && cam != null) g.transform.rotation = cam.transform.rotation;
        await UniTask.Delay(400, DelayType.Realtime);
        ScreenCapture.CaptureScreenshot($"{Dir}/{name}.png");
        await UniTask.Delay(600, DelayType.Realtime);
        Clear(own);
        await UniTask.Delay(200, DelayType.Realtime);
    }

    private static void Clear(List<Object> own)
    {
        foreach (var o in own)
            if (o != null) Object.Destroy(o);
        own.Clear();
    }
}
#endif
