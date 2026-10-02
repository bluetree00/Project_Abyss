#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중] 랜슬롯 Q(심판의 일격) 전용 검 표시 — 장착 무기마다(무형검 · 카타나 T1~3 · 대검 T1~3 · 석궁 · 활)
/// Q 시전 0.9초 지점에 손 주변에 무엇이 그려지는지(활성 렌더러 · 부모 · 위치 · 크기)와 가까운 화면을 남긴다.
/// 사용자 10-03: 「무형검 이외의 검들도 랜슬롯 Q 때 검이 제대로 나와야 한다」.
/// 결과: Temp/lancelot_qsword.txt · Temp/lancelot_qsword_&lt;무기&gt;.png
/// </summary>
public static class LancelotQSwordProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/lancelot_qsword.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Weapons =
    {
        "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T2_Katana.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T3_Katana.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T2_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T3_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset",
        "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset",
    };
    private static bool s_armed;
    private static bool s_fxMode;   // 메뉴 11 — 트레일 · 칼날 이펙트 후보 비교

    private const string INab1 = "Assets/RelicFairy/_Imported/EffectSource/INab Studio 1/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/";
    private const string INab  = "Assets/RelicFairy/_Imported/EffectSource/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/";
    private static readonly (string trail, string vfx)[] FxCandidates =
    {
        (INab1 + "Blood 1.prefab",  "vfx_lancelot_frenzy"),
        (INab  + "Dark 1.prefab",   "vfx_lancelot_cursed"),
        (INab  + "Fire 3.prefab",   "vfx_lancelot_frenzy"),
        (INab  + "Magic 2.prefab",  "vfx_lancelot_cursed"),
        (INab  + "Stylized 2.prefab", null),
    };

    [MenuItem("RelicFairy/Debug/유물 성장 v2/11 랜슬롯 Q 검 트레일 · 이펙트 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginFx() { s_fxMode = true; Begin(); }

    [MenuItem("RelicFairy/Debug/유물 성장 v2/9 랜슬롯 Q 전용 검 표시 실측 (테스트 허브, 플레이 중)")]
    private static void BeginShow() { s_fxMode = false; Begin(); }

    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[QSword] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[QSword] 테스트 허브에서 시작한다."); return; }
        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == "Lancelot");
        if (idx < 0) { Debug.LogWarning("[QSword] 허브 유물 목록에 랜슬롯이 없다."); return; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player == null) return;
            if (s_fxMode) RunFxAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
            else RunAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("랜슬롯 Q 전용 검 표시 실측\n");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);   // 몹이 끼어들어 피격 · 넉백으로 Q가 끊기지 않게
            await Wait(2f, ct);
            var relic = p.RelicBehavior as LancelotMadnessRelic;
            if (relic == null) { sb.AppendLine("랜슬롯 유물이 아니다"); return; }

            foreach (var path in Weapons)
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);   // 방 웨이브가 다시 나온다
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"■ {key}: 장착 실패"); continue; }
                await Wait(1.5f, ct);
                var equipped = p.WeaponManager.CurrentWeaponInstance;
                var before = Visible(p);

                // 광란 → Q
                relic.Madness.SetStacks(relic.Madness.MaxStacks);
                if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
                await Wait(0.2f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.Q);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.QSkill);
                await Wait(0.9f, ct);

                var during = Visible(p);
                var bone = FindDeep(p.transform, "hand_r");
                sb.AppendLine($"■ {key} — 장착 무기 {(equipped != null ? equipped.name : "없음")} · Q 중 장착 무기 활성 {equipped != null && equipped.activeInHierarchy}");
                foreach (var r in during)
                {
                    bool isNew = !before.Contains(r);
                    var b = r.bounds;
                    sb.AppendLine($"   {(isNew ? "＋" : " ")} {HierPath(r.transform, p.transform)} · 부모 {r.transform.parent?.name} · 크기 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} · 무기 소켓에서 {Vector3.Distance(b.center, Hand(p)):0.00} m · 손뼈에서 {(bone != null ? Vector3.Distance(b.center, bone.position) : -1f):0.00} m · 원점→손뼈 {(bone != null ? Vector3.Distance(r.transform.position, bone.position) : -1f):0.00} m");
                }
                foreach (var r in before)
                    if (!during.Contains(r)) sb.AppendLine($"   － {HierPath(r.transform, p.transform)} (Q 중 숨음)");
                CloseUp(p, $"Temp/lancelot_qsword_{key}_a.png");
                await Wait(1.6f, ct);   // 마무리 자세(2.4초 막타 + 포즈 홀드)
                CloseUp(p, $"Temp/lancelot_qsword_{key}_b.png");

                await Wait(2.4f, ct);   // Q 끝(3.6초 + 막타 시간 정지 0.45초 실시간)까지
                bool restored = equipped == null || equipped.activeInHierarchy;
                var left = new List<string>();
                foreach (var r in Visible(p)) if (!before.Contains(r)) left.Add(HierPath(r.transform, p.transform));
                sb.AppendLine($"   Q 끝 — 장착 무기 복구 {restored} · Q 뒤 새로 보이는 렌더러 {left.Count}{(left.Count > 0 ? " — " + string.Join(" · ", left) : "")}");
                relic.Madness.SetStacks(0);
                typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(relic.Madness, Time.time - 0.01f);
                await Wait(0.5f, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[QSword] 끝 → {OutPath}");
        }
    }

    /// <summary>후보마다 Q를 쓰고, 전용 검이 뜨면 그 후보의 트레일 · 칼날 이펙트를 붙여 연타 중 두 장을 찍는다.</summary>
    private static async UniTaskVoid RunFxAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("랜슬롯 Q 검 트레일 · 이펙트 후보\n");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            await Wait(2f, ct);
            var relic = p.RelicBehavior as LancelotMadnessRelic;
            var trailVfx = p.GetComponent<PlayerWeaponTrailVfx>();
            if (relic == null || trailVfx == null) { sb.AppendLine($"랜슬롯 {relic != null} · 트레일 구동기 {trailVfx != null}"); return; }
            for (int i = 0; i < FxCandidates.Length; i++)
            {
                var (trailPath, vfxKey) = FxCandidates[i];
                foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                relic.Madness.SetStacks(relic.Madness.MaxStacks);
                if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
                await Wait(0.3f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.Q);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.QSkill);
                await Wait(0.45f, ct);
                var sword = FindDeep(p.transform, "Lancelot_QSword(Clone)");
                var tip = sword != null ? FindDeep(sword, "QSwordTip") : null;
                var root = sword != null ? FindDeep(sword, "QSwordRoot") : null;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(trailPath);
                GameObject vfx = null;
                if (tip != null && root != null && prefab != null) trailVfx.BeginOverride(tip, root, prefab);
                if (tip != null && root != null && !string.IsNullOrEmpty(vfxKey))
                {
                    vfx = await Managers.AddressableManager.InstantiateAsync(vfxKey, tip.parent);
                    if (vfx != null)
                    {
                        vfx.transform.localPosition = (tip.localPosition + root.localPosition) * 0.5f;
                        vfx.transform.localScale = Vector3.one * tip.localPosition.magnitude * 0.35f;
                    }
                }
                string name = System.IO.Path.GetFileNameWithoutExtension(trailPath).Replace(" ", "");
                sb.AppendLine($"#{i} 트레일 {name}(로드 {prefab != null}) · 이펙트 {vfxKey ?? "없음"}(생성 {vfx != null}) · 앵커 {tip != null && root != null}");
                await Wait(0.35f, ct);
                CloseUp(p, $"Temp/qsword_fx_{i}_{name}_a.png");
                await Wait(0.5f, ct);
                CloseUp(p, $"Temp/qsword_fx_{i}_{name}_b.png");
                await Wait(2.6f, ct);
                trailVfx.EndOverride();
                if (vfx != null) Managers.AddressableManager.ReleaseInstance(vfx);
                relic.Madness.SetStacks(0);
                typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(relic.Madness, Time.time - 0.01f);
                await Wait(0.8f, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText("Temp/qsword_fx.txt", sb.ToString());
            Debug.Log("[QSword] 끝 → Temp/qsword_fx.txt");
        }
    }

    // 손(오른손 무기 소켓) 반경 1.6 m 안에서 플레이어 계층 아래 켜진 렌더러 — 몸(SkinnedMesh)은 뺀다
    private static HashSet<Renderer> Visible(PlayerController p)
    {
        var set = new HashSet<Renderer>();
        Vector3 hand = Hand(p);
        foreach (var r in p.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled || r is SkinnedMeshRenderer || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (Vector3.Distance(r.bounds.center, hand) > 1.6f) continue;
            set.Add(r);
        }
        return set;
    }

    private static Vector3 Hand(PlayerController p) => p.HandTransform != null ? p.HandTransform.position : p.transform.position + Vector3.up;

    private static string HierPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts.Count > 4 ? parts.GetRange(parts.Count - 4, 4) : parts);
    }

    /// <summary>플레이어 오른쪽 앞 2.6 m에서 손을 보는 화면 한 장(임시 카메라 → RenderTexture → PNG).</summary>
    private static void CloseUp(PlayerController p, string path)
    {
        var go = new GameObject("QSwordProbeCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            Vector3 chest = p.transform.position + Vector3.up * 1.1f;
            Vector3 eye = p.transform.position + p.transform.forward * 2.6f + p.transform.right * 0.8f + Vector3.up * 1.5f;
            go.transform.position = eye;
            go.transform.LookAt(chest);
            cam.fieldOfView = 45f;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }
        finally { UnityEngine.Object.Destroy(go); }
    }

    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        string key = System.IO.Path.GetFileNameWithoutExtension(soPath);
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        // 슬롯 0에 넣고 그 슬롯으로 바꾼다 — 빈 슬롯 자동 배정은 두 번째부터 다른 슬롯을 덮어 현재 무기가 안 바뀌었다(1회차).
        // 슬롯에 인스턴스가 있으면 장착이 데이터만 바꾸고 모델은 그대로 둔다 — 먼저 해제한다(게임 내부 DestroySlotInstance와 같은 길).
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f)
        {
            var inst = wm.CurrentWeaponInstance;
            if (inst != null && inst.name.StartsWith(key) && inst.activeInHierarchy) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
