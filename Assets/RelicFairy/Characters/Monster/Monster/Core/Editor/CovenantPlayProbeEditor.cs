using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// [실측 도구 · 플레이 중] 실제 런의 전투방에서 서약이 발동하는지 잰다.
///   1 허브 → Ch1 정상 흐름 시작 (BaseCamp_Test 플레이 중) → 대사 넘기기 → 1b 출구 열기 → 1c 출구로 이동
///   2 서약 4종 장착 + 20초 기록 (전투방 입장 직후 — 무적도 여기서 켠다) → Temp/covenant_play_probe.json
///   3 효과별 VFX 발동 기록 → Temp/covenant_vfx_probe.json · Temp/vfx_shots/*.png
///   4 속성 몸 이펙트 실측 → Temp/body_vfx_probe.json
///   5 전 조합(원인 10 × 효과 15) 발동 실측 → Temp/covenant_combo_probe.json
///   6 아이템 독 오라(VFX_PoisonAura) 근접 촬영 → Temp/vfx_shots/item_poison_aura.png
///   7 보스 기절 중 패턴 기록(보스방) → Temp/boss_stun_probe.json
/// 장착: 심장박동×저주 · 포위×잔불 · 심장박동×방전 · 심장박동×정지 (전부 실버, 복원 경로라 칸 상한 4).
/// 플레이어는 디버그 무적으로 세워 두고 입력하지 않는다 — 발동 흔적은 적에게 남은 상태로 센다.
/// </summary>
public static class CovenantPlayProbeEditor
{
    private const string Root       = "RelicFairy/Debug/서약 발동 실측/";
    private const float  Duration   = 20f;
    private const float  SampleStep = 0.25f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly string[] Pairs =
    {
        "heartbeat|curse", "besiege|ember", "heartbeat|arcflash", "heartbeat|stasis",
    };

    private static double s_end, s_next;
    private static readonly List<AssembledCovenant> s_covs = new();
    private static readonly List<BuffViewItem> s_buf = new();
    private static readonly Dictionary<string, float> s_first = new();
    private static readonly int[] s_gateOpen = new int[4];
    private static int s_samples, s_maxAlive, s_maxNear5, s_maxNear18, s_minHp = int.MaxValue;

    [MenuItem(Root + "1 허브 → Ch1 정상 흐름 시작 (플레이 중)")]
    private static void StartCh1()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[CovenantPlayProbe] 플레이 모드에서만"); return; }
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[CovenantPlayProbe] 테스트 허브가 아니다"); return; }
        var t = typeof(TestHubLauncher);
        t.GetField("_chapter", Inst).SetValue(launcher, 1);
        t.GetField("_bossApproach", Inst).SetValue(launcher, false);
        Debug.Log("[CovenantPlayProbe] Ch1 정상 흐름 시작 → " + launcher.TryLaunch());
    }

    /// <summary>대기방 출구는 서약 제단 완료로 열린다 — 제단 완료 통지를 그대로 흉내 내 연출·통과 조건을 세운다.</summary>
    [MenuItem(Root + "1b 대기방 출구 열기 (플레이 중)")]
    private static void OpenStartGate()
    {
        if (!Application.isPlaying) return;
        foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
        {
            if ((int)typeof(StartRoomGate).GetField("_fromZoneIndex", Inst).GetValue(gate) != -1) continue;
            typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst).Invoke(gate, null);
            Debug.Log("[CovenantPlayProbe] 대기방 출구 열기 요청");
            return;
        }
        Debug.LogWarning("[CovenantPlayProbe] 대기방 출구가 없다");
    }

    /// <summary>열린 대기방 출구 트리거 안으로 플레이어를 옮긴다(걸어 들어간 것과 같다).</summary>
    [MenuItem(Root + "1c 대기방 출구로 이동 (플레이 중)")]
    private static void EnterStartGate()
    {
        if (!Application.isPlaying) return;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
        {
            if ((int)typeof(StartRoomGate).GetField("_fromZoneIndex", Inst).GetValue(gate) != -1) continue;
            if (!gate.TryGetComponent<Collider>(out var col)) continue;
            var b   = col.bounds;
            var pos = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
            player.transform.position = pos;
            if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
            Debug.Log("[CovenantPlayProbe] 대기방 출구로 이동 " + pos);
            return;
        }
    }

    /// <summary>열린 대사창을 끝까지 넘긴다(한 번에 최대 30줄).</summary>
    [MenuItem(Root + "대사 전부 넘기기 (플레이 중)")]
    private static void SkipDialogue()
    {
        if (!Application.isPlaying) return;
        int n = 0;
        for (; n < 30; n++)
        {
            var popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
            if (popup == null || !popup.isActiveAndEnabled) break;
            var button = new SerializedObject(popup).FindProperty("advanceButton").objectReferenceValue as UnityEngine.UI.Button;
            if (button == null) break;
            button.onClick.Invoke();
        }
        Debug.Log("[CovenantPlayProbe] 대사 넘김 " + n);
    }

    [MenuItem(Root + "2 서약 장착 + 20초 기록 (전투방에서)")]
    private static void Equip()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[CovenantPlayProbe] 플레이 모드에서만"); return; }
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.Player == null || run.CovenantHandler == null) { Debug.LogWarning("[CovenantPlayProbe] 런이 없다"); return; }

        typeof(PlayerController).GetField("debugInvincible", Inst).SetValue(run.Player, true);

        var tryAdd = typeof(CovenantHandler).GetMethod("TryAdd", Inst, null,
                                                       new[] { typeof(string), typeof(bool) }, null);
        s_covs.Clear();
        foreach (var pair in Pairs)
        {
            var parts = pair.Split('|');
            string id = AssembledCovenant.MakeId(parts[0], CovenantTier.Silver, parts[1], CovenantTier.Silver);
            bool ok = (bool)tryAdd.Invoke(run.CovenantHandler, new object[] { id, true });
            Debug.Log($"[CovenantPlayProbe] 장착 {id} → {ok}");
        }
        foreach (var c in run.CovenantHandler.Covenants)
            if (c is AssembledCovenant a) s_covs.Add(a);

        s_first.Clear();
        System.Array.Clear(s_gateOpen, 0, s_gateOpen.Length);
        s_samples = s_maxAlive = s_maxNear5 = s_maxNear18 = 0;
        s_minHp = int.MaxValue;
        s_end  = EditorApplication.timeSinceStartup + Duration;
        s_next = 0;
        EditorApplication.update -= Sample;
        EditorApplication.update += Sample;
    }

    // ── 3 효과별 VFX ─────────────────────────────────────
    // 효과 하나를 0.6초(실시간)에 하나씩 직접 발동하고(원인 무관), 그 직후 ElementVfxPlayer 풀에 새로 켜진 이펙트 이름을 기록한다.
    // 발동 0.35초 뒤 게임 화면을 찍는다(Temp/vfx_shots) — 파티클이 막 생겨난 순간은 거의 비어 보인다.
    // 수확·기폭은 먹을 화상·출혈이 같은 대상에 있어야 하므로 걸기 바로 뒤에 둔다.
    private static readonly string[] VfxSteps =
    {
        "ember", "hemorrhage", "harvest", "ember", "hemorrhage", "arcflash", "detonate", "arcflash",
        "execute", "stasis", "supernova", "bloodmark", "aegis", "ward", "lastbreath", "ruby",
    };
    private const double VfxStepGap = 0.6, VfxShotDelay = 0.35;
    private static int s_vfxStep;
    private static double s_vfxNext, s_vfxShotAt;
    private static string s_vfxShotName;
    private static readonly StringBuilder s_vfxLog = new();

    [MenuItem(Root + "3 효과별 VFX 발동 기록 (전투방에서)")]
    private static void VfxRun()
    {
        if (!Application.isPlaying) return;
        s_vfxStep     = 0;
        s_vfxNext     = 0;
        s_vfxShotName = null;
        s_vfxLog.Clear().Append('[');
        Directory.CreateDirectory(Path.Combine("Temp", "vfx_shots"));
        EditorApplication.update -= VfxTick;
        EditorApplication.update += VfxTick;
    }

    private static void VfxTick()
    {
        if (!Application.isPlaying) { EditorApplication.update -= VfxTick; return; }
        double now = EditorApplication.timeSinceStartup;
        if (s_vfxShotName != null && now >= s_vfxShotAt)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine("Temp", "vfx_shots", s_vfxShotName));
            s_vfxShotName = null;
        }
        if (now < s_vfxNext) return;
        s_vfxNext = now + VfxStepGap;

        if (s_vfxStep >= VfxSteps.Length)
        {
            EditorApplication.update -= VfxTick;
            s_vfxLog.Append(']');
            File.WriteAllText(Path.Combine("Temp", "covenant_vfx_probe.json"), s_vfxLog.ToString());
            Debug.Log("[CovenantPlayProbe] VFX " + s_vfxLog);
            return;
        }

        string effect = VfxSteps[s_vfxStep++];
        s_vfxShotName = $"{s_vfxStep:00}_{effect}.png";
        s_vfxShotAt   = now + VfxShotDelay;
        var run    = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        var target = NearestMonster(player);
        var before = ActiveVfxNames();
        int beamsBefore = ActiveBeams();
        string note = "";

        if (player != null && target != null)
        {
            var ctx = (CovenantContext)typeof(CovenantHandler).GetField("_ctx", Inst).GetValue(run.CovenantHandler);
            bool ruby = effect == "ruby";
            // 루비는 아직 연출 스로틀(효과별 0.4초)에 걸리지 않은 효과(저주)에 얹어 본다.
            string id = AssembledCovenant.MakeId("streak", CovenantTier.Silver, ruby ? "curse" : effect,
                                                 ruby ? CovenantTier.Ruby : CovenantTier.Silver);
            var cov = (AssembledCovenant)CovenantFactory.Create(id);
            cov.Initialize(ctx);

            if (effect == "execute")
            {
                // 처형 임계(걸린 통화 종수만큼 커진다) 아래로 체력을 내린다.
                for (int k = 0; k < 6 && target.CurrentHp > target.EffectiveMaxHp * 0.3f; k++)
                    target.TakeSynergyDamage(target.EffectiveMaxHp * 0.3f, player.gameObject, 1f, false, DamageKind.Synergy);
                note = "hp=" + target.CurrentHp + "/" + target.EffectiveMaxHp;
            }

            if (effect == "lastbreath") note += " prevent=" + cov.TryPreventDeath();
            else typeof(AssembledCovenant).GetMethod("ApplyEffect", Inst).Invoke(cov, new object[] { target.gameObject });
        }
        else note = "no player/target";

        var after = ActiveVfxNames();
        after.ExceptWith(before);
        if (s_vfxLog.Length > 1) s_vfxLog.Append(',');
        s_vfxLog.Append("{\"effect\":\"").Append(effect).Append("\",\"new\":\"")
                .Append(string.Join("|", after)).Append("\",\"beams\":").Append(ActiveBeams() - beamsBefore)
                .Append(",\"note\":\"").Append(note).Append("\"}");
    }

    // ── 4 속성 몸 이펙트 ─────────────────────────────────
    // 상태이상 몸 이펙트(ElementVfxRegistry.statusBody)를 적에게 붙이고 0.8초 뒤 살아 있는 파티클 수를 세고 근접 촬영한다.
    // INab 캐릭터 이펙트(VFX Graph)는 대상 메시를 구워 넣고 재생 이벤트를 보내야 도는 구조라, 위치만 옮겨 띄우면 비어 있을 수 있다.
    private static readonly RuneElement[] BodyElements =
    {
        RuneElement.Fire, RuneElement.Ice, RuneElement.Electric, RuneElement.Grass, RuneElement.Light, RuneElement.Dark,
    };
    private const double BodyMeasureDelay = 0.8, BodyStepGap = 1.2;
    private static int s_bodyStep;
    private static double s_bodyNext, s_bodyMeasureAt;
    private static int s_bodyHandle;
    private static MonsterBase s_bodyTarget;
    private static readonly StringBuilder s_bodyLog = new();

    [MenuItem(Root + "4 속성 몸 이펙트 실측 (전투방에서)")]
    private static void BodyRun()
    {
        if (!Application.isPlaying) return;
        s_bodyStep = 0;
        s_bodyNext = 0;
        s_bodyMeasureAt = double.MaxValue;
        s_bodyLog.Clear().Append('[');
        Directory.CreateDirectory(Path.Combine("Temp", "vfx_shots"));
        EditorApplication.update -= BodyTick;
        EditorApplication.update += BodyTick;
    }

    private static void BodyTick()
    {
        if (!Application.isPlaying) { EditorApplication.update -= BodyTick; return; }
        double now = EditorApplication.timeSinceStartup;

        if (now >= s_bodyMeasureAt)
        {
            s_bodyMeasureAt = double.MaxValue;
            MeasureBody(BodyElements[s_bodyStep - 1]);
        }
        if (now < s_bodyNext) return;
        s_bodyNext = now + BodyStepGap;

        if (s_bodyStep >= BodyElements.Length)
        {
            EditorApplication.update -= BodyTick;
            s_bodyLog.Append(']');
            File.WriteAllText(Path.Combine("Temp", "body_vfx_probe.json"), s_bodyLog.ToString());
            Debug.Log("[CovenantPlayProbe] 몸 이펙트 " + s_bodyLog);
            return;
        }

        var element = BodyElements[s_bodyStep++];
        s_bodyTarget = NearestMonster(GameRunBootstrapper.Instance?.Run?.Player);
        s_bodyHandle = 0;
        if (s_bodyTarget != null)
            s_bodyHandle = ElementVfxPlayer.AttachStatus(element, s_bodyTarget.transform, 3f);
        s_bodyMeasureAt = now + BodyMeasureDelay;
    }

    private static void MeasureBody(RuneElement element)
    {
        string prefab = "", kind = "none";
        int alive = -1;
        var inst = typeof(ElementVfxPlayer).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        if (inst != null && s_bodyHandle != 0)
        {
            var handles = (System.Collections.IDictionary)typeof(ElementVfxPlayer).GetField("_handles", Inst).GetValue(inst);
            var it = handles.Contains(s_bodyHandle) ? handles[s_bodyHandle] : null;
            var go = it != null ? (GameObject)it.GetType().GetField("go").GetValue(it) : null;
            if (go != null)
            {
                prefab = go.name;
                var vfx = go.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
                if (vfx != null) { kind = "vfxgraph"; alive = vfx.aliveParticleCount; }
                else
                {
                    kind  = "particles";
                    alive = 0;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) alive += ps.particleCount;
                }
            }
        }

        string shot = $"body_{element}.png";
        if (s_bodyTarget != null) CaptureCloseUp(s_bodyTarget.transform, Path.Combine("Temp", "vfx_shots", shot));

        if (s_bodyLog.Length > 1) s_bodyLog.Append(',');
        s_bodyLog.Append("{\"element\":\"").Append(element).Append("\",\"prefab\":\"").Append(prefab)
                 .Append("\",\"kind\":\"").Append(kind).Append("\",\"alive\":").Append(alive)
                 .Append(",\"target\":\"").Append(s_bodyTarget != null ? s_bodyTarget.name : "null")
                 .Append("\",\"shot\":\"").Append(shot).Append("\"}");
    }

    /// <summary>
    /// 대상 옆 3m에서 임시 카메라로 찍는다(게임 카메라는 멀어서 몸 이펙트를 가늠하기 어렵다).
    /// 플레이어→대상 방향의 옆에서 찍어 플레이어가 대상을 가리지 않게 하고, 머리 위 HP바(UI 레이어)는 뺀다.
    /// </summary>
    private static void CaptureCloseUp(Transform target, string path)
    {
        var go  = new GameObject("~ProbeCloseUpCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt  = new RenderTexture(512, 512, 24);
        try
        {
            Vector3 focus  = target.position + Vector3.up * 0.9f;
            var player     = GameRunBootstrapper.Instance?.Run?.Player;
            Vector3 toward = player != null ? target.position - player.transform.position : Vector3.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f) toward = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toward.normalized);
            go.transform.position = focus + side * 3f + Vector3.up * 1.0f;
            go.transform.LookAt(focus);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) cam.cullingMask = ~(1 << ui);
            cam.fieldOfView     = 40f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
            cam.targetTexture   = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }

    // ── 5 전 조합 발동 ───────────────────────────────────
    // 원인 10 × 효과 15 = 150칸을 한 프레임에 하나씩 새로 만들어, 원인의 조건을 그대로 걸고 효과가 발동했는지 본다.
    // 발동 판정 = CovenantFxService의 연출 기록(효과가 실제로 들어간 뒤에만 불린다). 연출 스로틀은 매번 비운다.
    // 몬스터는 HP 하한 1로 세워 둔다(광역 피해로 방이 비지 않게). 소모형·처형은 먹을 것을 미리 걸어 둔다.
    private static readonly string[] AllCauses =
        { "streak", "slaughter", "swap", "clear", "heartbeat", "concerto", "hunt", "opener", "besiege", "march" };
    private static readonly string[] AllEffects =
        { "supernova", "fury", "curse", "execute", "ember", "hemorrhage", "detonate", "harvest",
          "arcflash", "stasis", "bloodmark", "aegis", "lastbreath", "ward", "momentum" };
    private static int s_comboI;
    private static string[] s_comboCauses = AllCauses;
    private static readonly StringBuilder s_comboLog = new();

    [MenuItem(Root + "5 전 조합 발동 실측 (전투방에서)")]
    private static void ComboRun() => StartCombo(AllCauses);

    /// <summary>포위 행만 다시 잰다 — 포위는 5m 안 적 3마리가 조건이라, 재기 직전에 가까운 적 3마리를 플레이어 곁으로 옮긴다.</summary>
    [MenuItem(Root + "5b 포위 행만 재실측 (전투방에서)")]
    private static void ComboBesiegeRun() => StartCombo(new[] { "besiege" });

    private static void StartCombo(string[] causes)
    {
        if (!Application.isPlaying) return;
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.Player == null) { Debug.LogWarning("[CovenantPlayProbe] 런이 없다"); return; }
        typeof(PlayerController).GetField("debugInvincible", Inst).SetValue(run.Player, true);
        s_comboI = 0;
        s_comboCauses = causes;
        s_comboLog.Clear().Append('[');
        EditorApplication.update -= ComboTick;
        EditorApplication.update += ComboTick;
    }

    private static void ComboTick()
    {
        if (!Application.isPlaying) { EditorApplication.update -= ComboTick; return; }
        int total = s_comboCauses.Length * AllEffects.Length;
        if (s_comboI >= total)
        {
            EditorApplication.update -= ComboTick;
            s_comboLog.Append(']');
            File.WriteAllText(Path.Combine("Temp", "covenant_combo_probe.json"), s_comboLog.ToString());
            Debug.Log("[CovenantPlayProbe] 전 조합 완료 " + total);
            return;
        }

        string cause  = s_comboCauses[s_comboI / AllEffects.Length];
        string effect = AllEffects[s_comboI % AllEffects.Length];
        s_comboI++;

        var run    = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None)) mb.HpFloorMin1 = true;
        var target = NearestMonster(player);

        string fired = "no-target";
        int near5 = 0;
        if (player != null && target != null)
        {
            Vector3 p = player.transform.position;
            foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (!mb.IsDead && Vector3.Distance(mb.transform.position, p) <= 5f) near5++;

            if (cause == "besiege") GatherAround(player, 3);
            Prepare(effect, player);

            var ctx = (CovenantContext)typeof(CovenantHandler).GetField("_ctx", Inst).GetValue(run.CovenantHandler);
            var cov = (AssembledCovenant)CovenantFactory.Create(
                AssembledCovenant.MakeId(cause, CovenantTier.Silver, effect, CovenantTier.Silver));
            cov.Initialize(ctx);
            ClearFxThrottle();

            try
            {
                Trigger(cov, cause, target.gameObject);
                if (effect == "lastbreath") cov.TryPreventDeath();   // 충전형 — 원인과 무관하게 치명 피해에서 발동
                fired = FxPlayed(effect) ? "yes" : "no";
            }
            catch (System.Exception e)
            {
                fired = "error:" + (e.InnerException ?? e).GetType().Name;
            }
        }

        if (s_comboLog.Length > 1) s_comboLog.Append(',');
        s_comboLog.Append("{\"cause\":\"").Append(cause).Append("\",\"effect\":\"").Append(effect)
                  .Append("\",\"banned\":").Append(CovenantPalette.IsBannedPair(cause, effect) ? "true" : "false")
                  .Append(",\"fired\":\"").Append(fired).Append("\",\"near5\":").Append(near5).Append('}');
    }

    /// <summary>원인 조건을 실제 훅 순서대로 건다.</summary>
    private static void Trigger(AssembledCovenant cov, string cause, GameObject target)
    {
        switch (cause)
        {
            case "streak":    for (int i = 0; i < 3; i++) cov.OnAttackHit(target, 10f); break;
            case "slaughter": for (int i = 0; i < 5; i++) cov.OnKill(target); break;
            case "swap":      cov.OnWeaponSwap(null, null); cov.OnAttackHit(target, 10f); break;
            case "clear":     cov.OnRoomClear(); break;
            case "heartbeat": for (int i = 0; i < 8; i++) cov.Tick(0.6f); break;   // 게이트 판정 + 4초 주기
            case "concerto":  cov.OnSkillUse(SkillType.Q); break;
            case "hunt":      cov.OnKill(target); cov.OnAttackHit(target, 10f); break;
            case "opener":    cov.OnRoomEnter(); cov.OnAttackHit(target, 10f); break;
            case "besiege":   cov.Tick(0.6f); break;                                  // 5m 안 3마리 이상
            case "march":
            {
                // 12m를 걷는 대신 누적을 문턱 바로 아래로 두고 한 걸음만 옮긴 것처럼 만든다.
                var t = typeof(AssembledCovenant);
                var player = GameRunBootstrapper.Instance.Run.Player;
                t.GetField("_movePrimed", Inst).SetValue(cov, true);
                t.GetField("_moveAccum", Inst).SetValue(cov, 11.8f);
                t.GetField("_lastPos", Inst).SetValue(cov, player.transform.position + new Vector3(0.5f, 0f, 0f));
                cov.Tick(0.6f);
                break;
            }
        }
    }

    /// <summary>가까운 적 count마리를 플레이어 둘레 1.5m로 옮긴다(NavMeshAgent는 Warp).</summary>
    private static void GatherAround(PlayerController player, int count)
    {
        var list = new List<MonsterBase>();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead) list.Add(mb);
        Vector3 p = player.transform.position;
        list.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        for (int i = 0; i < count && i < list.Count; i++)
        {
            float ang = i * Mathf.PI * 2f / count;
            Vector3 to = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.5f;
            if (list[i].TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.isOnNavMesh) agent.Warp(to);
            else list[i].transform.position = to;
        }
        Physics.SyncTransforms();
    }

    /// <summary>소모형(기폭·수확·정지)과 처형이 먹을 것을 주변 적에게 미리 건다.</summary>
    private static void Prepare(string effect, PlayerController player)
    {
        if (effect != "detonate" && effect != "harvest" && effect != "stasis" && effect != "execute") return;
        Vector3 p = player.transform.position;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead || Vector3.Distance(mb.transform.position, p) > 12f) continue;
            var go = mb.gameObject;
            switch (effect)
            {
                case "detonate":
                case "harvest":
                    CovenantStatus.Apply(go, StatusCurrency.Burn, 5f, 6f, player.gameObject);
                    CovenantStatus.Amplify(go, StatusCurrency.Bleed, 5f, 6f, player.gameObject);
                    break;
                case "stasis":
                    CovenantStatus.Amplify(go, StatusCurrency.Shock, 0f, 0f, player.gameObject);
                    break;
                case "execute":
                    if (mb.CurrentHp > 1) mb.TakeSynergyDamage(mb.CurrentHp, player.gameObject, 1f, false, DamageKind.Synergy);
                    break;
            }
        }
    }

    private static void ClearFxThrottle()
    {
        var t = typeof(CovenantFxService);
        ((System.Collections.IDictionary)t.GetField("_lastPlay", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        t.GetField("_lastFrame", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, -1);
    }

    private static bool FxPlayed(string effectId)
        => ((System.Collections.IDictionary)typeof(CovenantFxService)
               .GetField("_lastPlay", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Contains(effectId);

    // ── 6 아이템 독 오라 ─────────────────────────────────
    // 아이템 VFX_PoisonAura(Hovl Poison aura)는 URP 미리보기에서 분홍(셰이더 깨짐)으로 나왔다. 게임 화면에서도 그런지 찍는다.
    private static double s_itemShotAt;
    private static Transform s_itemTarget;

    [MenuItem(Root + "6 아이템 독 오라 촬영 (전투방에서)")]
    private static void ItemAuraRun()
    {
        if (!Application.isPlaying) return;
        var target = NearestMonster(GameRunBootstrapper.Instance?.Run?.Player);
        if (target == null) return;
        s_itemTarget = target.transform;
        ItemEffectVfxHelper.AttachLoopVfx("VFX_PoisonAura", s_itemTarget, 3f).Forget();
        s_itemShotAt = EditorApplication.timeSinceStartup + 0.8;
        Directory.CreateDirectory(Path.Combine("Temp", "vfx_shots"));
        EditorApplication.update -= ItemAuraTick;
        EditorApplication.update += ItemAuraTick;
    }

    private static void ItemAuraTick()
    {
        if (!Application.isPlaying) { EditorApplication.update -= ItemAuraTick; return; }
        if (EditorApplication.timeSinceStartup < s_itemShotAt) return;
        EditorApplication.update -= ItemAuraTick;
        if (s_itemTarget != null) CaptureCloseUp(s_itemTarget, Path.Combine("Temp", "vfx_shots", "item_poison_aura.png"));
        Debug.Log("[CovenantPlayProbe] 아이템 독 오라 촬영 완료");
    }

    // ── 7 보스 기절 중 패턴 ─────────────────────────────
    // 보스가 깨어나 패턴 밖(대기)에 있을 때까지 기다렸다가(최대 60초), 6초 동안 기절 상태로 유지하며
    // (0.4초마다 1초 기절 재부여) FSM 상태가 패턴 상태로 바뀌는지 기록한다. 보스방에 들어서자마자 실행한다.
    private const double BossStunSeconds = 6.0, BossWaitSeconds = 60.0;
    private static double s_bossEnd, s_bossNextStun, s_bossWaitUntil;
    private static bool s_bossArmed;
    private static string s_bossStartState;
    private static MonsterBase s_boss;
    private static readonly List<string> s_bossStates = new();
    private static int s_bossSamples, s_bossSpecialSamples, s_bossCcSamples;

    [MenuItem(Root + "7 보스 기절 중 패턴 기록 (보스방에서)")]
    private static void BossStunRun()
    {
        if (!Application.isPlaying) return;
        s_boss = null;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead && mb.Grade == MonsterGrade.Boss) { s_boss = mb; break; }
        if (s_boss == null) { Debug.LogWarning("[CovenantPlayProbe] 보스가 없다"); return; }

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player != null) typeof(PlayerController).GetField("debugInvincible", Inst).SetValue(player, true);
        s_boss.HpFloorMin1 = true;
        s_bossStates.Clear();
        s_bossSamples = s_bossSpecialSamples = s_bossCcSamples = 0;
        s_bossArmed     = false;
        s_bossWaitUntil = EditorApplication.timeSinceStartup + BossWaitSeconds;
        s_bossNextStun  = 0;
        EditorApplication.update -= BossStunTick;
        EditorApplication.update += BossStunTick;
    }

    private static void BossStunTick()
    {
        if (!Application.isPlaying || s_boss == null) { EditorApplication.update -= BossStunTick; return; }
        double now = EditorApplication.timeSinceStartup;
        var fsm  = typeof(MonsterBase).GetField("_fsm", Inst).GetValue(s_boss);
        var type = fsm?.GetType().GetProperty("CurrentType")?.GetValue(fsm) as System.Type;
        string name = type != null ? type.Name : "null";

        if (!s_bossArmed)
        {
            // 등장 연출(휴면) 중이거나 패턴 중이면 기다린다 — 패턴 중에 기절시키면 그 상태로 멈춰 있을 뿐이라 판단이 안 된다.
            var dormant  = s_boss.GetType().GetField("_dormantState", Inst)?.GetValue(s_boss);
            bool asleep  = dormant != null && (bool)(dormant.GetType().GetProperty("IsActive")?.GetValue(dormant) ?? false);
            if ((asleep || s_boss.IsInSpecialState) && now < s_bossWaitUntil) return;
            s_bossArmed      = true;
            s_bossStartState = name + (asleep ? "(휴면)" : s_boss.IsInSpecialState ? "(패턴 중)" : "");
            s_bossEnd        = now + BossStunSeconds;

            // 플레이어를 보스 2.5m 앞에 세운다 — 패턴 발동 거리 안이어야 "기절 중에 패턴이 시작되는가"를 가를 수 있다.
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player != null)
            {
                Vector3 bp  = s_boss.transform.position;
                Vector3 dir = player.transform.position - bp;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : s_boss.transform.forward;
                Vector3 to = bp + dir * 2.5f;
                player.transform.position = to;
                if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; }
            }
        }

        if (now >= s_bossNextStun)
        {
            s_boss.ApplyStun(1f);
            s_bossNextStun = now + 0.4;
        }
        if (s_bossStates.Count == 0 || s_bossStates[s_bossStates.Count - 1] != name) s_bossStates.Add(name);
        s_bossSamples++;
        if (s_boss.IsInSpecialState) s_bossSpecialSamples++;
        if (s_boss.Status.IsCcActive) s_bossCcSamples++;

        if (now < s_bossEnd) return;
        EditorApplication.update -= BossStunTick;
        string json = "{\"boss\":\"" + s_boss.name + "\",\"startState\":\"" + s_bossStartState + "\",\"samples\":" + s_bossSamples
                    + ",\"ccSamples\":" + s_bossCcSamples + ",\"specialStateSamples\":" + s_bossSpecialSamples
                    + ",\"states\":\"" + string.Join(" > ", s_bossStates) + "\"}";
        File.WriteAllText(Path.Combine("Temp", "boss_stun_probe.json"), json);
        Debug.Log("[CovenantPlayProbe] 보스 기절 " + json);
    }

    [MenuItem(Root + "7a 허브 → Ch1 보스 대기방 시작 (플레이 중)")]
    private static void StartCh1Boss()
    {
        if (!Application.isPlaying) return;
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[CovenantPlayProbe] 테스트 허브가 아니다"); return; }
        var t = typeof(TestHubLauncher);
        t.GetField("_chapter", Inst).SetValue(launcher, 1);
        t.GetField("_bossApproach", Inst).SetValue(launcher, true);
        Debug.Log("[CovenantPlayProbe] Ch1 보스 대기방 시작 → " + launcher.TryLaunch());
    }

    private static MonsterBase NearestMonster(PlayerController player)
    {
        if (player == null) return null;
        MonsterBase best = null;
        float bestSq = float.MaxValue;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead || mb.CurrentHp <= 0) continue;
            float sq = (mb.transform.position - player.transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = mb; }
        }
        return best;
    }

    /// <summary>ElementVfxPlayer 풀에서 지금 켜져 있는 이펙트 이름(인스턴스 id 포함).</summary>
    private static HashSet<string> ActiveVfxNames()
    {
        var set  = new HashSet<string>();
        var inst = typeof(ElementVfxPlayer).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        if (inst == null) return set;
        var list = (System.Collections.IList)typeof(ElementVfxPlayer).GetField("_active", Inst).GetValue(inst);
        foreach (var it in list)
        {
            var go = (GameObject)it.GetType().GetField("go").GetValue(it);
            if (go != null && go.activeSelf) set.Add(go.name + "#" + go.GetInstanceID());
        }
        return set;
    }

    private static int ActiveBeams()
    {
        var inst = typeof(ElementVfxPlayer).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        if (inst == null) return 0;
        return ((System.Collections.IList)typeof(ElementVfxPlayer).GetField("_activeBeams", Inst).GetValue(inst)).Count;
    }

    private static void Sample()
    {
        if (!Application.isPlaying) { EditorApplication.update -= Sample; return; }
        double now = EditorApplication.timeSinceStartup;
        if (now < s_next) return;
        s_next = now + SampleStep;

        var run = GameRunBootstrapper.Instance?.Run;
        var player = run?.Player;
        if (player == null) return;
        Vector3 p = player.transform.position;
        float t = (float)(Duration - (s_end - now));
        s_samples++;

        int alive = 0, near5 = 0, near18 = 0;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead || mb.CurrentHp <= 0) continue;
            alive++;
            float d = Vector3.Distance(mb.transform.position, p);
            if (d <= 5f) near5++;
            if (d <= 18f) near18++;

            s_buf.Clear();
            mb.CollectStatuses(s_buf);
            foreach (var it in s_buf) Mark("status:" + it.Label, t);
        }
        s_maxAlive  = Mathf.Max(s_maxAlive, alive);
        s_maxNear5  = Mathf.Max(s_maxNear5, near5);
        s_maxNear18 = Mathf.Max(s_maxNear18, near18);
        s_minHp     = Mathf.Min(s_minHp, player.RuntimeStats != null ? player.RuntimeStats.Hp : 0);

        var type = typeof(AssembledCovenant);
        for (int i = 0; i < s_covs.Count && i < s_gateOpen.Length; i++)
        {
            var c = s_covs[i];
            if ((float)type.GetField("_gateOpenUntil", Inst).GetValue(c) > Time.time) s_gateOpen[i]++;
            if ((float)type.GetField("_icdEnd", Inst).GetValue(c) > Time.time) Mark("icd:" + c.EffectId, t);
        }

        if (now >= s_end)
        {
            EditorApplication.update -= Sample;
            Write();
        }
    }

    private static void Mark(string key, float t)
    {
        if (!s_first.ContainsKey(key)) s_first[key] = t;
    }

    private static void Write()
    {
        var sb = new StringBuilder();
        sb.Append("{\"samples\":").Append(s_samples)
          .Append(",\"maxAlive\":").Append(s_maxAlive)
          .Append(",\"maxNear5\":").Append(s_maxNear5)
          .Append(",\"maxNear18\":").Append(s_maxNear18)
          .Append(",\"minPlayerHp\":").Append(s_minHp)
          .Append(",\"covenants\":[");
        for (int i = 0; i < s_covs.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":\"").Append(s_covs[i].CovenantId).Append("\",\"gateOpenSamples\":")
              .Append(i < s_gateOpen.Length ? s_gateOpen[i] : -1).Append('}');
        }
        sb.Append("],\"firstSeen\":{");
        bool first = true;
        foreach (var kv in s_first)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value.ToString("0.00"));
        }
        sb.Append("}}");
        File.WriteAllText(Path.Combine("Temp", "covenant_play_probe.json"), sb.ToString());
        Debug.Log("[CovenantPlayProbe] " + sb);
    }
}
