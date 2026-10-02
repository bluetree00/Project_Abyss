using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] PlayerController 구조 개편(2026-09-17)의 동작 검증.
/// 런 플레이어가 서 있는 <b>적 없는 방</b>(시작방 등)에서 메뉴를 실행한다 → Temp/player_structure_probe.json
///
///   A 전제 — 입력 초기화 · 조작 정지 해제 · 애니 이벤트 구독
///   B 이동 배율 2채널 — 슬로우가 공격 잠금을 풀거나 덮지 않는가
///   C 조작 정지 — SetControlSuspended 동안 Update(상태이상 틱)가 멈추는가
///   D 얼음 누적 → 빙결 → 해제(루프 사운드 정리)
///   E 저스트 회피 — 회피 중 피격 → 슬로모·이동 보너스·애니 실시간 → 만료 복구
///   F 유물 외형 — 오라 부착/해제 · 로드 중 주인이 사라지면 인스턴스 반납
///   G 유물 중복 적용 무시
///   H 회전 — RequestFacing이 조준 방향에 즉시, 몸 회전에 물리 스텝 뒤 반영
///   J 재활성화 — 끈 뒤 다시 켜면 애니 이벤트 구독이 돌아오는가
///   K 보스 특전(유물 파츠) — 초행 보너스 · 저장 왕복 · 복원 · 효과 활성
///   R 원거리 — 발사 높이에서 수평으로 쏴도 키 낮은 몹(슬라임)을 맞히는가(수직 보정)
///   S 공격 전진·슬래시 — 평타의 전진이 슬래시(이펙트·판정) 전에 끝나는가 · 전역 공격 속도
///   P 저스트 회피 보상 — 게이지 환급 · 반격 창(가속·추격 1회·적중 환급) · 창 종료 복구
///   Q 반격 실동작 — 실제 입력 경로: 적을 등진 회피 → 공격 연타 → 창 안 첫 타가 돌아서 붙어 들어가고 빨라지는가
///     + 보상 연출 신호(환급·반격 공격·창 종료)와 대시 게이지(칸·번쩍임·스킨)
///   X 긴급 회피 무적 — 예고 중인 적 옆에서 회피 → 대시 무적이 끝난 뒤에도 창이 닫힐 때까지 피해·넉백 무효,
///     피한 공격의 예고가 창보다 길면 그 공격이 끝날 때까지 이어지고, 끝나면 풀린다
///   I 검증 중 콘솔 에러 0
/// 플레이어 상태를 실제로 바꾼다(유물 적용·회피 이동·HP 변화 가능) — 검증용 런에서만 쓴다.
/// </summary>
public static class PlayerStructureProbeEditor
{
    private const string MenuPath = "RelicFairy/Debug/플레이어 구조 검증 (플레이 중)";
    private const string OutPath  = "Temp/player_structure_probe.json";
    private const string AuraKey  = "VFX_DeathNegateAura";   // Addressables에 등록된 루프 VFX(유물 데이터엔 오라 키가 없다)
    private const string GawainPath   = "Assets/RelicFairy/Systems/Relic/Data/RelicClass_Gawain.asset";
    private const string LancelotPath = "Assets/RelicFairy/Systems/Relic/Data/RelicClass_Lancelot.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string RangedTargetKey = "Slime/Slime";     // 원거리 과녁(실전 몬스터 그대로)
    private const string ArrowKey        = "Basic_Arrow_01";  // 활 평타 발사체(BowAbility payloadKey)

    private struct Step
    {
        public string      Name;
        public Func<float> Wait;   // 이전 단계 뒤 대기(실시간 초) — 앞 단계 결과로 정해지는 경우가 있어 지연 평가
        public Action      Run;
    }

    private static readonly List<Step>   s_steps   = new();
    private static readonly List<string> s_results = new();
    private static readonly List<string> s_errors  = new();
    private static readonly List<string> s_warnings = new();
    private static int    s_index, s_pass, s_fail, s_perfectDodgeCount, s_hpBefore, s_auraBaseline;
    private static double s_nextAt;
    private static string s_abort;
    private static float  s_timeScaleBefore, s_perfectDodgeTotal, s_facingYaw;
    private static PlayerController s_player;
    private static PlayerStatusEffects s_status;
    private static RelicAppearance s_orphanAppearance;
    private static Quaternion s_facingTarget;
    private static RelicFairy.Monster.MonsterBase s_rangedTarget;
    private static int  s_rangedHp0;
    private static bool s_rangedFired;
    private static CounterRun s_counterRun;
    private static Vector3    s_home;       // 검증 시작 자리 — 앞 단계(추격·회피)로 밀려나 발판 끝에서 떨어지지 않게 되돌아올 곳
    private static Quaternion s_homeRot;
    private static SlashRun   s_slashRun;

    /// <summary>S 시나리오 측정값.</summary>
    private sealed class SlashRun
    {
        public bool   Done;
        public float  SlashNorm = -1f, AnimSpeed, MoveBeforeSlash, MoveAfterSlash, PlannedStep;
        public string Weapon;
        public string Note;
    }

    /// <summary>Q 시나리오 측정값(실시간 초 · 미터).</summary>
    private sealed class CounterRun
    {
        public bool   Done, Triggered, AttackInWindow;
        public float  DodgeEnd = -1f, AttackStart = -1f;
        public float  CounterAnimSpeed, NormalAnimSpeed;
        public float  DistBefore, DistAfter, Travel, PlannedStep, FacingAngle;
        public int    WindowHits, AllHits, TargetHpBefore = -1, TargetHpAfter = -1;
        public bool   TargetImmune;
        public int    RefundEvents, StrikeEvents, FirstStrikes, WindowEndEvents;
        public float  RefundTotal, FirstStrikeDist;
        public bool   GaugeFlashed, GaugeSkinned;
        public float  SlashTfDist = -1f, SlashRbDist = -1f;   // 반격 첫 슬래시 순간 과녁까지(보간 transform / 물리 위치)
        public float  GaugeStep = -1f;
        public bool   CounterLungeFlag;
        public float  LungeTargetDist;
        public string Note;
    }

    /// <summary>HUD 재화 칸 확인용 — 런에 골드를 준다(골드 칸은 처음 얻을 때 나타난다).</summary>
    [MenuItem("RelicFairy/Debug/골드 1250 지급 (플레이 중 · HUD 확인)")]
    private static void GiveGold()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (!Application.isPlaying || run == null) { Debug.LogWarning("[PlayerProbe] 런 플레이 중에만 동작한다."); return; }
        run.AddGold(1250);
        Debug.Log("[PlayerProbe] 골드 +1250");
    }

    /// <summary>원거리 수직 보정 판단 로그 토글 — 실제 공격 루프(다른 실측 도구 포함)에서 발사마다 무엇을 골랐는지 본다.</summary>
    [MenuItem("RelicFairy/Debug/원거리 수직 보정 로그 켜기·끄기 (플레이 중)")]
    private static void TogglePitchLog()
    {
        CombatSpawner.LogPitch = !CombatSpawner.LogPitch;
        Debug.Log($"[PlayerProbe] 원거리 수직 보정 로그 {(CombatSpawner.LogPitch ? "켬" : "끔")}");
    }

    /// <summary>
    /// 근접 무기 지상 평타 클립의 실제 길이와 이벤트 시각을 덤프한다(편집 모드). 결과 Temp/attack_clip_events.txt.
    /// FBX 임포터의 이벤트 시각이 비율인지 초인지 — 이벤트 주입 도구가 어느 쪽으로 넣었는지 — 를 런타임 클립으로 확인한다.
    /// 런타임 AnimationEvent.time은 항상 초다.
    /// </summary>
    [MenuItem("RelicFairy/Debug/공격 클립 이벤트 시각 덤프")]
    private static void DumpAttackClipEvents()
    {
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        var sb = new StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponAnimationSetSO"))
        {
            var set = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (set == null) continue;
            sb.AppendLine($"== {set.name}  lightAttackAnimSpeed={set.lightAttackAnimSpeed}");
            foreach (var m in set.GetMappings(WeaponAnimGroup.Ground, WeaponActionType.GroundLight))
            {
                var clip = FindAddressableClip(settings, m.addressableKey);
                if (clip == null) { sb.AppendLine($"  [{m.comboIndex}] {m.addressableKey}: 클립 없음"); continue; }
                sb.AppendLine($"  [{m.comboIndex}] {m.addressableKey} '{clip.name}' 길이 {clip.length:F3}s {clip.frameRate}fps · 전진 {m.attackStepStartNorm:F2}-{m.attackStepEndNorm:F2} · 끝 {m.attackEndAt:F2}");
                foreach (var e in clip.events)
                    sb.AppendLine($"      {e.time,7:F3}s = {e.time / clip.length:F3}  {e.functionName}");
            }
        }
        File.WriteAllText("Temp/attack_clip_events.txt", sb.ToString());
        Debug.Log("[PlayerProbe] 공격 클립 이벤트 덤프 → Temp/attack_clip_events.txt");
    }

    private static AnimationClip FindAddressableClip(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings, string key)
    {
        if (settings == null || string.IsNullOrEmpty(key)) return null;
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries)
            {
                if (e.address != key) continue;
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(e.guid)))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            }
        }
        return null;
    }

    /// <summary>
    /// 플레이어 상태 스냅샷(읽기 전용) — 공격·시간 고착 진단용. 0.5초 간격 3회 찍어 애니·시간이 흐르는지 본다.
    /// 결과 Temp/player_state_snapshot.txt. 상태를 바꾸지 않는다.
    /// </summary>
    [MenuItem("RelicFairy/Debug/플레이어 상태 스냅샷 (플레이 중)")]
    private static void SnapshotPlayerState()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[PlayerProbe] 플레이 모드에서만 동작한다."); return; }
        var p = FindRunPlayer();
        if (p == null) { Debug.LogWarning("[PlayerProbe] 런 플레이어가 없다."); return; }
        SnapshotAsync(p).Forget();
    }

    private static async UniTaskVoid SnapshotAsync(PlayerController p)
    {
        var sb = new StringBuilder();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true);
                if (p == null) { sb.AppendLine("플레이어 사라짐"); break; }
                AppendSnapshot(sb, p, i);
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        File.WriteAllText("Temp/player_state_snapshot.txt", sb.ToString());
        Debug.Log("[PlayerProbe] 상태 스냅샷 → Temp/player_state_snapshot.txt");
    }

    private static void AppendSnapshot(StringBuilder sb, PlayerController p, int index)
    {
        var act    = GetField<LayerStateMachine<ActState>>(p, "_actSM");
        var anim   = p.Anim;
        var status = (PlayerStatusEffects)typeof(PlayerController).GetProperty("Status", Inst).GetValue(p);

        sb.AppendLine($"── #{index}  unscaled {Time.unscaledTime:F2}s · frame {Time.frameCount}");
        sb.AppendLine($"  timeScale {Time.timeScale:F3} · 보유자 {TimeScaleArbiter.DescribeHolders()}");
        sb.AppendLine($"  loco {p.LocoSM.CurrentId} · act {act.CurrentId} · MoveScale {p.MoveScale:F2} · SlowScale {status.SlowScale:F2} · 빙결 {status.IsFrozen}");
        sb.AppendLine($"  입력 준비 {GetField<bool>(p, "_inputInitialized")} · 조작 정지 {GetField<bool>(p, "_controlSuspended")} · 외부 입력 차단 {GetField<bool>(p, "_inputDisabledExternally")} · 입력 버퍼 {p.InputBuffer.Count}");
        sb.AppendLine($"  콤보 공격중 {p.Combo.IsAttacking} · 단계 {p.Combo.CurrentComboStep} · 창 {p.Combo.ComboWindowOpen} · 다음 예약 {p.Combo.NextComboQueued}");
        sb.AppendLine($"  무기 {p.WeaponManager?.CurrentWeaponData?.weaponType.ToString() ?? "없음"} · 반격 창 x{p.CounterAttackSpeedMultiplier:F2}");
        if (anim != null)
        {
            var cur = anim.GetCurrentAnimatorStateInfo(0);
            sb.AppendLine($"  애니 speed {anim.speed:F2} · update {anim.updateMode} · 전환 중 {anim.IsInTransition(0)} · 현재 해시 {cur.shortNameHash} · t {cur.normalizedTime:F3} · 길이 {cur.length:F2}s");
            if (anim.IsInTransition(0))
            {
                var nxt = anim.GetNextAnimatorStateInfo(0);
                sb.AppendLine($"    다음 해시 {nxt.shortNameHash} · t {nxt.normalizedTime:F3}");
            }
        }
        if (act.CurrentId == ActState.Attack)
        {
            sb.AppendLine($"  공격 상태 해시 {GetStateField<int>(act, "_currentStateHash")} · 경과 {GetStateField<float>(act, "_stateElapsed"):F2}s · 회수 {GetStateField<bool>(act, "_inRecovery")} · " +
                          $"창 열림 {GetStateField<bool>(act, "_comboWindowOpened")} · 끝 발동 {GetStateField<bool>(act, "_attackEndFired")} · " +
                          $"open/close/end {GetStateField<float>(act, "_comboOpen"):F2}/{GetStateField<float>(act, "_comboClose"):F2}/{GetStateField<float>(act, "_attackEnd"):F2}");
        }
    }

    [MenuItem(MenuPath)]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[PlayerProbe] 플레이 모드에서만 동작한다."); return; }
        if (s_steps.Count > 0) { Debug.LogWarning("[PlayerProbe] 이미 실행 중이다."); return; }

        s_player = FindRunPlayer();
        if (s_player != null) { s_home = s_player.transform.position; s_homeRot = s_player.transform.rotation; }
        if (s_player == null) { Debug.LogWarning("[PlayerProbe] 런 플레이어가 없다 — 런을 시작한 뒤 실행한다."); return; }

        s_results.Clear(); s_errors.Clear(); s_warnings.Clear();
        s_pass = s_fail = s_index = 0;
        s_abort = null;
        BuildSteps();

        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
        s_nextAt = EditorApplication.timeSinceStartup + s_steps[0].Wait();
        Debug.Log($"[PlayerProbe] 시작 — {s_steps.Count}단계");
    }

    // ── 단계 구성 ─────────────────────────────────────────────────
    private static void BuildSteps()
    {
        var p = s_player;
        s_steps.Clear();

        Add("A 전제", 0f, () =>
        {
            // 전제가 깨지면 이후 결과는 전부 무의미하다 — 기록만 남기고 중단한다.
            // (플레이 중 리컴파일이 끼면 직렬화되지 않는 필드가 비고 에디터가 일시정지된다)
            int failBefore = s_fail;
            Check("A0 에디터 일시정지 아님", !EditorApplication.isPaused);
            Check("A1 입력 초기화 완료", GetField<bool>(p, "_inputInitialized"));
            Check("A2 조작 정지 해제(등장 연출 종료)", !GetField<bool>(p, "_controlSuspended"));
            Check("A3 애니 이벤트 구독", GetField<bool>(p, "_aeSubscribed"));
            Check("A4 상태머신·입력 버퍼(리로드로 비지 않음)", p.LocoSM != null && p.InputBuffer != null && p.Combo != null);
            // 첫 전환이 enum 기본값이라 무시되면 상태 객체가 비어 Update가 안 돈다(09-19 수정).
            var actSm = GetField<LayerStateMachine<ActState>>(p, "_actSM");
            Check("A4b 두 상태머신 모두 현재 상태가 있음",
                  GetField<object>(p.LocoSM, "_current") != null && actSm != null && GetField<object>(actSm, "_current") != null,
                  $"loco {p.LocoSM.CurrentId} · act {actSm?.CurrentId}");
            Check("A5 리시버·손 소켓", p.EventReceiver != null && p.HandTransform != null);
            Check("A6 시간 정지 아님", Time.timeScale > 0.5f, $"timeScale={Time.timeScale:F2} 보유자={TimeScaleArbiter.DescribeHolders()}");
            s_status = (PlayerStatusEffects)typeof(PlayerController).GetProperty("Status", Inst).GetValue(p);
            Check("A7 상태이상 객체", s_status != null);
            if (s_fail > failBefore) s_abort = "전제 실패";
        });

        Add("B 이동 배율", 0.1f, () =>
        {
            p.SetMoveScale(0f);
            p.ApplySlow(0.5f, 0.3f);
            Check("B1 잠금 중 슬로우 → 잠금 유지(0)", Approx(p.MoveScale, 0f), $"MoveScale={p.MoveScale:F2}");
        });
        Add("B 이동 배율", 0.7f, () =>
        {
            Check("B2 슬로우 만료가 잠금을 풀지 않음(0)", Approx(p.MoveScale, 0f), $"MoveScale={p.MoveScale:F2}");
            Check("B3 슬로우 채널 복구(1)", Approx(s_status.SlowScale, 1f), $"SlowScale={s_status.SlowScale:F2}");
            p.SetMoveScale(1f);
            Check("B4 잠금 해제 → 1", Approx(p.MoveScale, 1f), $"MoveScale={p.MoveScale:F2}");

            p.ApplySlow(0.5f, 5f);
            p.SetMoveScale(0f);
            p.SetMoveScale(1f);
            Check("B5 공격 종료가 슬로우를 지우지 않음(0.5)", Approx(p.MoveScale, 0.5f), $"MoveScale={p.MoveScale:F2}");
            p.ClearSlow();
            Check("B6 ClearSlow → 1", Approx(p.MoveScale, 1f), $"MoveScale={p.MoveScale:F2}");
        });

        Add("C 조작 정지", 0.1f, () =>
        {
            p.SetControlSuspended(true);
            p.ApplySlow(0.5f, 0.2f);
        });
        Add("C 조작 정지", 0.6f, () =>
        {
            Check("C1 정지 중엔 상태이상 타이머가 흐르지 않음", Approx(s_status.SlowScale, 0.5f), $"SlowScale={s_status.SlowScale:F2}");
            p.SetControlSuspended(false);
        });
        Add("C 조작 정지", 0.6f, () =>
            Check("C2 정지 해제 후 틱 재개(슬로우 만료)", Approx(s_status.SlowScale, 1f), $"SlowScale={s_status.SlowScale:F2}"));

        Add("D 빙결", 0.1f, () =>
        {
            int r = p.AddIceStack(2f);
            Check("D1 얼음 1회 → 1단계", r == 1 && !s_status.IsFrozen, $"ret={r} frozen={s_status.IsFrozen}");
        });
        Add("D 빙결", 0.1f, () =>
        {
            int r = p.AddIceStack(2f);
            Check("D2 창 안 2회 → 빙결", r == 2 && s_status.IsFrozen, $"ret={r} frozen={s_status.IsFrozen}");
        });
        Add("D 빙결", 0.3f, () =>
            Check("D3 빙결 중 이동 입력 0", s_status.IsFrozen && p.MoveDirection == Vector3.zero, $"moveDir={p.MoveDirection}"));
        Add("D 빙결", 1.2f, () =>
        {
            Check("D4 빙결 해제(1초)", !s_status.IsFrozen);
            Check("D5 루프 사운드 정리", GetField<AudioSource>(s_status, "_freezeLoopAudioSource") == null);
        });

        Add("E 저스트 회피", 0.3f, () =>
        {
            var cd = p.CharacterData;
            s_perfectDodgeTotal = cd != null ? Mathf.Max(0f, cd.perfectDodgeFreeze) + Mathf.Max(0f, cd.perfectDodgeDuration) : 1.27f;
            s_timeScaleBefore   = Time.timeScale;
            s_hpBefore          = p.RuntimeStats.Hp;
            s_perfectDodgeCount = 0;
            p.OnPerfectDodge += OnPerfectDodge;

            p.LocoSM.Change(LocoState.Dodge);
            Check("E1 회피 상태 진입", p.LocoSM.CurrentId == LocoState.Dodge, $"loco={p.LocoSM.CurrentId}");
            p.TakeDamage(1);

            Check("E2 발동 이벤트 1회", s_perfectDodgeCount == 1, $"count={s_perfectDodgeCount}");
            Check("E3 피해 무효", p.RuntimeStats.Hp == s_hpBefore, $"hp {s_hpBefore}→{p.RuntimeStats.Hp}");
            Check("E4 시간 배율 주인 = PlayerController", TimeScaleArbiter.IsHeldBy(p));
            Check("E5 이동 보너스 > 1", p.BonusMoveSpeedMultiplier > 1f, $"x{p.BonusMoveSpeedMultiplier:F2}");
            Check("E6 플레이어 애니 실시간", p.Anim != null && p.Anim.updateMode == AnimatorUpdateMode.UnscaledTime);
        });
        Add("E 저스트 회피", 0.15f, () =>
            Check("E7 세계 시간 느려짐", Time.timeScale < s_timeScaleBefore, $"timeScale={Time.timeScale:F2}"));
        // 만료 대기 — 데이터의 프리즈+지속시간(실시간) + 여유
        Add("E 저스트 회피", () => s_perfectDodgeTotal + 0.4f, () =>
        {
            p.OnPerfectDodge -= OnPerfectDodge;
            Check("E8 만료 → 시간 배율 해제", !TimeScaleArbiter.IsHeldBy(p));
            Check("E9 시간 복구", Approx(Time.timeScale, s_timeScaleBefore), $"timeScale={Time.timeScale:F2}");
            Check("E10 이동 보너스 복구", Approx(p.BonusMoveSpeedMultiplier, 1f), $"x{p.BonusMoveSpeedMultiplier:F2}");
            Check("E11 애니 시간 복구", p.Anim != null && p.Anim.updateMode == AnimatorUpdateMode.Normal);
        });

        Add("F 유물 외형", 0.3f, () =>
        {
            s_auraBaseline = CountAuraObjects();
            var app = GetField<RelicAppearance>(p, "_relicAppearance");
            app.SpawnAuraAsync(AuraKey, "", p.transform).Forget();

            // 로드 중 주인이 사라지는 경우 — 인스턴스가 씬에 남으면 안 된다.
            var root = new GameObject("PlayerProbe_AuraRoot");
            s_orphanAppearance = new RelicAppearance();
            s_orphanAppearance.SpawnAuraAsync(AuraKey, "", root.transform).Forget();
            UnityEngine.Object.Destroy(root);
        });
        Add("F 유물 외형", 2.5f, () =>
        {
            var app  = GetField<RelicAppearance>(p, "_relicAppearance");
            var inst = GetField<GameObject>(app, "_auraInstance");
            Check("F1 오라 부착(플레이어 자식)", inst != null && inst.transform.parent == p.transform, inst != null ? inst.name : "null");
            Check("F2 주인 소멸 시 인스턴스 미보관", GetField<GameObject>(s_orphanAppearance, "_auraInstance") == null);
            Check("F3 떠도는 오라 없음(+1만)", CountAuraObjects() == s_auraBaseline + 1, $"{s_auraBaseline}→{CountAuraObjects()}");
            app.ReleaseAura();
        });
        Add("F 유물 외형", 0.5f, () =>
        {
            var app = GetField<RelicAppearance>(p, "_relicAppearance");
            Check("F4 해제 후 참조 비움", GetField<GameObject>(app, "_auraInstance") == null);
            Check("F5 해제 후 씬에서 제거", CountAuraObjects() == s_auraBaseline, $"{s_auraBaseline}→{CountAuraObjects()}");
        });

        Add("G 유물 중복 적용", 0.1f, () =>
        {
            var gawain   = AssetDatabase.LoadAssetAtPath<RelicClassSO>(GawainPath);
            var lancelot = AssetDatabase.LoadAssetAtPath<RelicClassSO>(LancelotPath);
            if (p.RelicClass == null)
            {
                p.SetRelicAndApply(gawain);
                Check("G1 첫 적용", p.RelicClass == gawain && p.RelicBehavior != null, p.RelicClass != null ? p.RelicClass.name : "null");
            }
            var applied  = p.RelicClass;
            var behavior = p.RelicBehavior;
            int warnBefore = s_warnings.Count;
            p.SetRelicAndApply(applied == gawain ? lancelot : gawain);
            Check("G2 다른 유물 재적용 무시", p.RelicClass == applied && ReferenceEquals(p.RelicBehavior, behavior));
            bool warned = false;
            for (int i = warnBefore; i < s_warnings.Count; i++)
                warned |= s_warnings[i].Contains("적용 요청 무시");
            Check("G3 무시 경고 로그", warned);
        });

        Add("H 회전", 2.0f, () =>
        {
            s_facingYaw    = Mathf.Repeat(p.transform.eulerAngles.y + 90f, 360f);
            s_facingTarget = Quaternion.Euler(0f, s_facingYaw, 0f);
            p.RequestFacing(s_facingTarget);
            Check("H1 조준 방향 즉시 반영", Vector3.Angle(p.AimForward, s_facingTarget * Vector3.forward) < 1f,
                  $"angle={Vector3.Angle(p.AimForward, s_facingTarget * Vector3.forward):F1}");
        });
        Add("H 회전", 0.3f, () =>
        {
            float diff = Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.y, s_facingYaw));
            Check("H2 몸 회전 반영(물리 스텝 후)", diff < 2f, $"diff={diff:F1}°");
        });

        Add("J 재활성화", 0.1f, () =>
        {
            p.enabled = false;
            Check("J1 비활성화 → 애니 이벤트 구독 해제", !GetField<bool>(p, "_aeSubscribed"));
            p.enabled = true;
            Check("J2 재활성화 → 구독 복구", GetField<bool>(p, "_aeSubscribed"));
        });

        Add("K 보스 특전(유물 파츠)", 0.2f, () =>
        {
            var loadout = AppBootstrapper.Instance?.Loadout;
            var mgr     = Managers.RelicParts;
            if (loadout == null || mgr == null || p.RelicClass == null)
            {
                Check("K0 전제(로드아웃·파츠 차트·유물)", false, $"loadout={loadout != null} chart={mgr != null} relic={p.RelicClass != null}");
                return;
            }

            string relicId = p.RelicClass.Id.ToString().ToLower();
            var tier1 = mgr.GetDraftPool(relicId, 1, loadout.RelicPartIds);
            var tier3 = mgr.GetDraftPool(relicId, 3, loadout.RelicPartIds);
            Check("K1 유물별 후보 풀(기능·코어)", tier1.Count > 0 && tier3.Count > 0, $"tier1={tier1.Count} tier3={tier3.Count}");
            if (tier1.Count == 0 || tier3.Count == 0) return;

            var effectPart = tier1.Find(e => e.part_kind != "core");
            var corePart   = tier3.Find(e => e.part_kind == "core");
            Check("K2 기능·코어 파츠 존재", effectPart != null && corePart != null);
            if (effectPart == null || corePart == null) return;

            // 초행 보너스는 코어에만 붙어야 한다(기능 파츠 획득은 카운터를 올리지 않는다).
            int before = FirstRunService.RunCorePartFirsts;
            loadout.AddRelicPart(effectPart.part_id);
            int afterEffect = FirstRunService.RunCorePartFirsts;
            loadout.AddRelicPart(corePart.part_id);
            int afterCore = FirstRunService.RunCorePartFirsts;

            Check("K3 기능 파츠는 초행 보너스 없음", afterEffect == before, $"{before}→{afterEffect} ({effectPart.part_id})");
            // 코어는 이 계정에서 이미 처음을 밟았을 수 있다 — 그 경우 증가하지 않는 것이 정상이라 '감소 없음'만 본다.
            Check("K4 코어 파츠만 초행 후보", afterCore >= afterEffect, $"{afterEffect}→{afterCore} ({corePart.part_id})");
            Check("K5 획득 목록 반영", loadout.HasRelicPart(effectPart.part_id) && loadout.HasRelicPart(corePart.part_id));

            // 저장 왕복 — 새 필드가 JSON을 타고 살아 돌아오는가.
            var save = new RunSaveData { relicPartIds = string.Join(",", loadout.RelicPartIds) };
            var round = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(save));
            Check("K6 저장 왕복(파츠 id)", round != null && round.relicPartIds == save.relicPartIds, round?.relicPartIds ?? "null");

            // 복원 — 유물 설정이 파츠를 비운 뒤 저장분이 그대로 되살아나는가.
            var restored = new PlayerLoadout();
            restored.SetRelic(p.RelicClass);
            int beforeRestore = FirstRunService.RunCorePartFirsts;
            restored.RestoreRelicParts(round.relicPartIds);
            Check("K7 복원 후 파츠 일치", restored.RelicPartIds.Count == loadout.RelicPartIds.Count
                                       && restored.HasRelicPart(effectPart.part_id)
                                       && restored.HasRelicPart(corePart.part_id),
                  $"{restored.RelicPartIds.Count}개");
            Check("K8 복원은 초행 보너스를 다시 주지 않음", FirstRunService.RunCorePartFirsts == beforeRestore);

            // 효과 활성 — 획득한 파츠가 실제 효과 인스턴스로 붙는가.
            p.RuneEffects.Parts.SyncFromLoadout(loadout.RelicPartIds);
            var entry = mgr.GetById(corePart.part_id);
            Check("K9 파츠 효과 활성화", entry != null && p.RuneEffects.Parts.IsActive(entry.effect_key), entry?.effect_key ?? "null");
        });

        // 원거리 — 실전 조건(발사 높이 루트+1m에서 수평 발사)으로 키 낮은 몹(슬라임)을 맞히는가.
        // 09-18 이전엔 수평 발사가 슬라임 머리 위로 지나가 활·석궁 피해가 0이었다(CombatSpawner 수직 보정으로 수정).
        Add("R 원거리", 0.2f, () =>
        {
            s_rangedTarget = null;
            s_rangedFired  = false;
            SpawnRangedTargetAsync(p).Forget();
        });
        Add("R 원거리", 1.5f, () =>
        {
            Check("R1 과녁(슬라임) 스폰", s_rangedTarget != null, s_rangedTarget != null ? $"{s_rangedTarget.name} HP {s_rangedTarget.CurrentHp}" : "null");
            if (s_rangedTarget == null) return;
            s_rangedHp0 = s_rangedTarget.CurrentHp;
            FireArrowAtTargetAsync(p).Forget();
        });
        Add("R 원거리", 1.2f, () =>
        {
            if (s_rangedTarget == null) return;
            int drop = s_rangedHp0 - s_rangedTarget.CurrentHp;
            // 과녁 HP가 낮으면 1에서 멈추므로(HpFloorMin1) '1을 넘는 피해가 들어갔는가'로 본다.
            Check("R2 수평 발사 → 낮은 몹 명중", drop > 1, $"HP {s_rangedHp0}→{s_rangedTarget.CurrentHp} (-{drop}) · 발사 {s_rangedFired}");
            Managers.ObjectPooler?.Despawn(s_rangedTarget.gameObject);
            s_rangedTarget = null;
        });

        // 평타 한 번 — 전진이 슬래시 전에 끝나야 한다(슬래시·판정은 월드에 남는다). 적이 없어 기본 전진만 한다.
        Add("S 공격 전진·슬래시", 0.5f, () =>
        {
            s_slashRun = new SlashRun();
            RunSlashScenarioAsync(p, s_slashRun).Forget();
        });
        Add("S 공격 전진·슬래시", 2.5f, () =>
        {
            var r = s_slashRun;
            Check("S1 평타·슬래시 이벤트 발생", r.Done, r.Note ?? "");
            Check("S2 슬래시 뒤 전진 없음(≤10cm)", r.MoveAfterSlash <= 0.1f,
                  $"슬래시 전 {r.MoveBeforeSlash:F2}m · 뒤 {r.MoveAfterSlash:F2}m · 슬래시 시각 {r.SlashNorm:F2} · 무기 {r.Weapon} (프레임 추적 Temp/slash_trace.txt)");
            Check("S4 계획한 전진을 다 감(±15%)", r.PlannedStep > 0f && Mathf.Abs(r.MoveBeforeSlash - r.PlannedStep) <= r.PlannedStep * 0.15f,
                  $"이동 {r.MoveBeforeSlash:F2}m / 계획 {r.PlannedStep:F2}m");
            Check("S3 전역 공격 속도 1.0", Approx(p.GlobalAttackAnimSpeedScale, 1f),
                  $"전역 x{p.GlobalAttackAnimSpeedScale:F2} · anim.speed {r.AnimSpeed:F2}");
        });

        // 저스트 회피 보상(09-18 설계 A+B1) — 원인 적이 있어야 추격 대상을 확인할 수 있어 과녁을 세운다.
        Add("P 저스트 회피 보상", 0.3f, () =>
        {
            s_rangedTarget = null;
            SpawnRangedTargetAsync(p).Forget();
        });
        Add("P 저스트 회피 보상", 1.5f, () =>
        {
            Check("P0 원인 적 스폰", s_rangedTarget != null);
            if (s_rangedTarget == null) return;

            var   cd  = p.CharacterData;
            float max = p.RuntimeStats.MaxStamina;
            p.Stamina.TryConsume(max * 0.75f, max, 0.5f);   // 게이지를 1/4로 — 환급이 보이게
            float before = p.Stamina.Current;
            s_perfectDodgeTotal = Mathf.Max(0f, cd.perfectDodgeFreeze) + Mathf.Max(0f, cd.perfectDodgeDuration);

            // 회피 진입(공격 예고 감지) 또는 회피 중 피격 — 어느 쪽이든 원인은 과녁이다.
            p.LocoSM.Change(LocoState.Dodge);
            p.TakeDamage(1, s_rangedTarget.gameObject);
            float after = p.Stamina.Current;
            Check("P1 게이지 환급(대시 1회분)", Approx(after - before, cd.perfectDodgeStaminaRefund),
                  $"{before:F1}→{after:F1} (+{after - before:F1})");
            Check("P2 반격 창 가속", Approx(p.CounterAttackSpeedMultiplier, cd.perfectDodgeCounterSpeed),
                  $"x{p.CounterAttackSpeedMultiplier:F2}");

            bool got1 = p.TryGetCounterTarget(out var target1, out bool isFirst1);
            bool got2 = p.TryGetCounterTarget(out var target2, out bool isFirst2);
            Check("P3 창 안 공격은 원인 적 고정 · 긴 추격은 첫 공격만",
                  got1 && got2 && target1 == s_rangedTarget.transform && target2 == target1 && isFirst1 && !isFirst2,
                  $"1타 {got1}/{isFirst1} · 2타 {got2}/{isFirst2}");

            float hitBefore = p.Stamina.Current;
            RaisePlayerHit(p);
            Check("P4 창 안 적중 → 게이지 환급", Approx(p.Stamina.Current - hitBefore, cd.perfectDodgeCounterHitRefund),
                  $"+{p.Stamina.Current - hitBefore:F1}");
        });
        Add("P 저스트 회피 보상", () => s_perfectDodgeTotal + 0.4f, () =>
        {
            Check("P5 창 종료 → 가속 해제", Approx(p.CounterAttackSpeedMultiplier, 1f), $"x{p.CounterAttackSpeedMultiplier:F2}");
            float hitBefore = p.Stamina.Current;
            RaisePlayerHit(p);
            // 같은 프레임이라 자연 회복분이 끼지 않는다 — 환급이 없어야 한다.
            Check("P6 창 밖 적중은 환급 없음", p.Stamina.Current - hitBefore < 0.5f, $"+{p.Stamina.Current - hitBefore:F2}");
            if (s_rangedTarget != null) { Managers.ObjectPooler?.Despawn(s_rangedTarget.gameObject); s_rangedTarget = null; }
        });

        // 실제 입력 경로 — 적을 등지고 뒤로 빠지는 회피(실전에서 흔한 모양) 뒤 공격을 연타한다.
        // 창 안 첫 타는 몸을 돌려 원인 적에게 붙어 들어가고, 창 밖 공격보다 빨라야 한다.
        Add("Q 반격 실동작", 0.3f, () =>
        {
            s_rangedTarget = null;
            s_counterRun   = new CounterRun();
            SpawnRangedTargetAsync(p).Forget();
        });
        Add("Q 반격 실동작", 1.2f, () =>
        {
            Check("Q0 원인 적 스폰", s_rangedTarget != null);
            if (s_rangedTarget != null) RunCounterScenarioAsync(p, s_counterRun).Forget();
        });
        Add("Q 반격 실동작", 5.0f, () =>
        {
            var q  = s_counterRun;
            var cd = p.CharacterData;
            Check("Q1 시나리오 완료", q.Done, q.Note ?? "");
            Check("Q2 저스트 회피 발동", q.Triggered);
            Check("Q3 회피 뒤 첫 공격이 창 안에서 시작", q.AttackInWindow,
                  $"회피 끝 {q.DodgeEnd:F2}s · 공격 시작 {q.AttackStart:F2}s · 창 {s_perfectDodgeTotal:F2}s (발동 기준 실시간)");
            float ratio = q.NormalAnimSpeed > 0f ? q.CounterAnimSpeed / q.NormalAnimSpeed : 0f;
            Check("Q4 창 안 공격 가속(창 밖 대비)", Approx(ratio, cd.perfectDodgeCounterSpeed),
                  $"anim.speed {q.CounterAnimSpeed:F2} vs {q.NormalAnimSpeed:F2} (x{ratio:F2})");
            Check("Q5 원인 적을 향해 돌아섬", q.FacingAngle < 25f, $"{q.FacingAngle:F0}°");
            Check("Q6 원인 적에게 붙어 들어감", q.DistBefore - q.DistAfter > 1f,
                  $"거리 {q.DistBefore:F1}→{q.DistAfter:F1}m · 이동 {q.Travel:F2}m / 계획 {q.PlannedStep:F2}m · 추격 {q.CounterLungeFlag}·대상거리 {q.LungeTargetDist:F1}m");
            if (s_rangedTarget != null) q.TargetHpAfter = s_rangedTarget.CurrentHp;
            Check("Q7 창 안 실제 적중 통지(플레이어 발)", q.WindowHits > 0,
                  $"창 안 {q.WindowHits}회 · 전체 {q.AllHits}회 · 과녁 HP {q.TargetHpBefore}→{q.TargetHpAfter} · 무적 {q.TargetImmune} · " +
                  $"슬래시 순간 과녁까지 transform {q.SlashTfDist:F2}m / 물리 {q.SlashRbDist:F2}m");
            Check("Q8 계획한 추격을 다 감(≥85%)", q.PlannedStep > 0f && q.Travel >= q.PlannedStep * 0.85f,
                  $"이동 {q.Travel:F2}m / 계획 {q.PlannedStep:F2}m");
            Check("Q9 연출 신호 — 발동 환급 이벤트(+25)", q.RefundEvents >= 1 && q.RefundTotal >= cd.perfectDodgeStaminaRefund - 0.01f,
                  $"{q.RefundEvents}회 · 합 +{q.RefundTotal:F1}");
            Check("Q10 연출 신호 — 반격 공격(첫 타 1회·추격 거리)", q.FirstStrikes == 1 && q.FirstStrikeDist >= cd.perfectDodgeDashTrailMinDistance,
                  $"반격 공격 {q.StrikeEvents}회 · 첫 타 {q.FirstStrikes}회 · 추격 {q.FirstStrikeDist:F1}m");
            Check("Q11 연출 신호 — 창 종료 1회", q.WindowEndEvents == 1, $"{q.WindowEndEvents}회");
            Check("Q12 대시 게이지 — 환급 번쩍임 시작", q.GaugeFlashed);
            // 기대 칸 간격 = 실제 대시 비용 ÷ 최대치 — 악몽 규칙(뿌리의 속박 ×1.4)·최대치 보너스가 걸리면 4칸이 아니다.
            float dashCost     = cd.dodgeStaminaCost * NightmareRules.DodgeStaminaMultiplier;
            float expectedStep = dashCost / p.RuntimeStats.MaxStamina;
            Check("Q13 대시 게이지 — 대시 1회 = 1칸", Approx(q.GaugeStep, expectedStep),
                  $"칸 간격 {q.GaugeStep:F3} / 기대 {expectedStep:F3} (비용 {dashCost:0.#} · 최대 {p.RuntimeStats.MaxStamina:0.#})");
            Check("Q14 대시 게이지 — 체력바 파생 스킨 적용", q.GaugeSkinned);
            if (s_rangedTarget != null) { Managers.ObjectPooler?.Despawn(s_rangedTarget.gameObject); s_rangedTarget = null; }
        });

        // 피격 등급 — 명시 등급·Auto 판정·같은 공격자 Light 0.3초 중복 억제(피해는 들어가고 연출 신호만 생략).
        var hitGrades = new List<HitWeight>();
        Action<HitWeight, Vector3, int> onHitTaken = (w, _, _) => hitGrades.Add(w);
        GameObject probeAttackerA = null, probeAttackerB = null;
        int hpBeforeW = 0;
        Add("W 피격 등급", 0.3f, () =>
        {
            probeAttackerA = new GameObject("~ProbeAttackerA");
            probeAttackerB = new GameObject("~ProbeAttackerB");
            probeAttackerA.transform.position = p.transform.position + p.transform.forward * 2f;
            probeAttackerB.transform.position = p.transform.position - p.transform.forward * 2f;
            hpBeforeW = p.RuntimeStats.Hp;
            p.OnHitTaken += onHitTaken;
            p.TakeDamage(1, probeAttackerA, false, HitWeight.Light);
            int hpAfterFirst = p.RuntimeStats.Hp;
            p.TakeDamage(1, probeAttackerA, false, HitWeight.Light);    // 0.3초 안 같은 공격자 → 연출 생략
            Check("W0 억제된 Light도 피해는 들어감", p.RuntimeStats.Hp < hpAfterFirst || hpAfterFirst == hpBeforeW,
                  $"hp {hpBeforeW}→{hpAfterFirst}→{p.RuntimeStats.Hp}");
            p.TakeDamage(1, probeAttackerA, false, HitWeight.Heavy);
            p.TakeDamage(1, probeAttackerA, false, HitWeight.Medium);
            p.TakeDamage(1, probeAttackerB);                            // Auto · 소량 → Light(다른 공격자라 억제 안 됨)
        });
        Add("W 피격 등급", 0.5f, () =>
        {
            p.OnHitTaken -= onHitTaken;
            string got = string.Join(",", hitGrades);
            Check("W1 등급 신호 = Light,Heavy,Medium,Light(두 번째 Light는 억제)", got == "Light,Heavy,Medium,Light", got);
            p.RuntimeStats.SetHp(hpBeforeW);
            if (probeAttackerA != null) UnityEngine.Object.Destroy(probeAttackerA);
            if (probeAttackerB != null) UnityEngine.Object.Destroy(probeAttackerB);
        });

        // 스킬 봉인 — 보스 기믹 API(리치 악몽기 「뒤집힌 봉인술」). 걸면 봉인·이벤트, 실시간이 지나면 풀리고 이벤트.
        int sealEvents = 0, unsealEvents = 0;
        Action<SkillType, float> onSeal   = (_, _) => sealEvents++;
        Action<SkillType>        onUnseal = _ => unsealEvents++;
        Add("V 스킬 봉인", 0.2f, () =>
        {
            p.SkillSealed   += onSeal;
            p.SkillUnsealed += onUnseal;
            p.SealSkill(SkillType.E, 0.5f);
            Check("V1 봉인 → E만 봉인 상태", p.IsSkillSealed(SkillType.E) && !p.IsSkillSealed(SkillType.R), $"봉인 이벤트 {sealEvents}회");
        });
        Add("V 스킬 봉인", 0.9f, () =>
        {
            Check("V2 0.5초 뒤 풀림 · 봉인 이벤트 1 · 해제 이벤트 1",
                  !p.IsSkillSealed(SkillType.E) && sealEvents == 1 && unsealEvents == 1, $"봉인 {sealEvents} · 해제 {unsealEvents}");
            p.SkillSealed   -= onSeal;
            p.SkillUnsealed -= onUnseal;
        });

        // 긴급 회피 무적(10-01) — 예전엔 대시 무적(대시 길이)이 느려진 시간 안에서 금방 끝나 뒤늦은 공격·돌진에 맞았다.
        // 과녁은 기절시켜 AI가 예고 표시를 건드리지 않게 하고, 예고의 시작·끝을 이쪽이 쥔다.
        var dodgeOutcomes = new List<PlayerController.DamageOutcome>();
        Action<GameObject, int, int, PlayerController.DamageOutcome> onResolved = (_, _, _, o) => dodgeOutcomes.Add(o);
        float dodgeAt = 0f;
        int   hpBeforeX = 0;
        Add("X 긴급 회피 무적", 0.3f, () =>
        {
            // 앞 단계(Q 추격 등)가 몸을 수 m씩 밀어 둔다 — 시작 자리로 되돌려야 회피 4m가 발판 끝을 넘지 않는다
            // (10-01 첫 실측: 떨어져 낙하 복구 무적이 X8을 가렸다).
            p.transform.SetPositionAndRotation(s_home, s_homeRot);
            if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = s_home; rb.rotation = s_homeRot; rb.linearVelocity = Vector3.zero; }
            p.RequestFacing(s_homeRot);   // 유휴 분기가 예전 방향으로 되돌리지 않게 facing 시스템에도 알린다
            s_rangedTarget = null;
            SpawnRangedTargetAsync(p, 2.5f).Forget();   // 예고 감지 반경(4m) 안
        });
        Add("X 긴급 회피 무적", 1.5f, () =>
        {
            Check("X0 원인 적 스폰", s_rangedTarget != null);
            if (s_rangedTarget == null) return;
            var cd = p.CharacterData;
            s_perfectDodgeTotal = Mathf.Max(0f, cd.perfectDodgeFreeze) + Mathf.Max(0f, cd.perfectDodgeDuration);
            s_rangedTarget.ApplyStun(30f);
            s_rangedTarget.BeginAttackTelegraph();
            s_perfectDodgeCount = 0;
            p.OnPerfectDodge   += OnPerfectDodge;
            p.OnDamageResolved += onResolved;
            hpBeforeX = p.RuntimeStats.Hp;
            p.LocoSM.Change(LocoState.Dodge);            // 회피 진입 → 예고 감지로 긴급 회피
            dodgeAt = Time.unscaledTime;
            Check("X1 예고 감지로 발동", s_perfectDodgeCount == 1,
                  $"{s_perfectDodgeCount}회 · 과녁까지 {FlatDist(p.transform.position, s_rangedTarget.transform.position):F1}m");
        });
        Add("X 긴급 회피 무적", 0.9f, () =>
        {
            if (s_rangedTarget == null) return;
            float iframeEnd = GetField<float>(p, "_invincibleEnd");
            Check("X2 대시 무적은 이미 끝남(검사 전제)", Time.time >= iframeEnd, $"time {Time.time:F2} · 대시 무적 끝 {iframeEnd:F2}");
            dodgeOutcomes.Clear();
            p.TakeDamage(50, s_rangedTarget.gameObject);
            Check("X3 창 안 피해 무효(긴급 회피 무적)",
                  dodgeOutcomes.Count == 1 && dodgeOutcomes[0] == PlayerController.DamageOutcome.PerfectDodge && p.RuntimeStats.Hp == hpBeforeX,
                  $"{string.Join(",", dodgeOutcomes)} · hp {hpBeforeX}→{p.RuntimeStats.Hp}");
            float kbBefore = GetField<float>(s_status, "_knockbackTimer");
            p.ApplyKnockback(Vector3.forward * 30f, 1f);
            float kbAfter = GetField<float>(s_status, "_knockbackTimer");
            Check("X4 창 안 넉백 무시", Approx(kbAfter, kbBefore), $"넉백 타이머 {kbBefore:F2}→{kbAfter:F2}");
        });
        // 창은 닫혔지만 피한 공격이 아직 예고 중 — 그 공격이 끝날 때까지 무적
        Add("X 긴급 회피 무적", () => Mathf.Max(0.1f, dodgeAt + s_perfectDodgeTotal + 0.3f - Time.unscaledTime), () =>
        {
            if (s_rangedTarget == null) return;
            Check("X5 반격 창 닫힘(검사 전제)", Approx(p.CounterAttackSpeedMultiplier, 1f), $"x{p.CounterAttackSpeedMultiplier:F2}");
            dodgeOutcomes.Clear();
            p.TakeDamage(50, s_rangedTarget.gameObject);
            Check("X6 창 뒤 · 피한 공격 예고 중 → 무효", dodgeOutcomes.Count == 1 && dodgeOutcomes[0] == PlayerController.DamageOutcome.PerfectDodge,
                  string.Join(",", dodgeOutcomes));
            s_rangedTarget.EndAttackTelegraph();          // 예고 끝 = 피해가 들어오는 프레임(AttackState 순서)
            dodgeOutcomes.Clear();
            p.TakeDamage(50, s_rangedTarget.gameObject);
            Check("X7 예고가 끝나는 프레임의 피해도 무효", dodgeOutcomes.Count == 1 && dodgeOutcomes[0] == PlayerController.DamageOutcome.PerfectDodge,
                  $"{string.Join(",", dodgeOutcomes)} · hp {hpBeforeX}→{p.RuntimeStats.Hp}");
        });
        Add("X 긴급 회피 무적", 0.6f, () =>
        {
            if (s_rangedTarget != null)
            {
                dodgeOutcomes.Clear();
                p.TakeDamage(1, s_rangedTarget.gameObject);
                bool blocked = dodgeOutcomes.Count == 1 && (dodgeOutcomes[0] == PlayerController.DamageOutcome.PerfectDodge
                                                            || dodgeOutcomes[0] == PlayerController.DamageOutcome.Invincible);
                Check("X8 피한 공격이 끝나면 무적 해제", dodgeOutcomes.Count == 1 && !blocked, string.Join(",", dodgeOutcomes));
                float kbBefore = GetField<float>(s_status, "_knockbackTimer");
                p.ApplyKnockback(Vector3.zero, 0.2f);
                Check("X9 무적이 풀리면 넉백이 다시 들어감", GetField<float>(s_status, "_knockbackTimer") > kbBefore + 0.1f,
                      $"넉백 타이머 {kbBefore:F2}→{GetField<float>(s_status, "_knockbackTimer"):F2}");
                Managers.ObjectPooler?.Despawn(s_rangedTarget.gameObject);
                s_rangedTarget = null;
            }
            p.OnPerfectDodge   -= OnPerfectDodge;
            p.OnDamageResolved -= onResolved;
            if (hpBeforeX > 0) p.RuntimeStats.SetHp(hpBeforeX);
        });

        Add("I 콘솔", 0.5f, () =>
            Check("I1 검증 중 에러·예외 0", s_errors.Count == 0, s_errors.Count > 0 ? s_errors[0] : ""));
    }

    private static void Add(string name, float wait, Action run) => Add(name, () => wait, run);

    private static void Add(string name, Func<float> wait, Action run)
        => s_steps.Add(new Step { Name = name, Wait = wait, Run = run });

    // ── 실행 루프 ─────────────────────────────────────────────────
    private static void Tick()
    {
        if (!Application.isPlaying || s_player == null)
        {
            Finish("중단 — 플레이 종료 또는 플레이어 소멸");
            return;
        }
        if (EditorApplication.timeSinceStartup < s_nextAt) return;

        var step = s_steps[s_index];
        try
        {
            step.Run();
        }
        catch (Exception e)
        {
            Check($"{step.Name} 실행", false, e.GetType().Name + ": " + e.Message);
        }

        s_index++;
        if (s_abort != null) { Finish("중단 — " + s_abort); return; }
        if (s_index >= s_steps.Count) { Finish("완료"); return; }

        s_nextAt = EditorApplication.timeSinceStartup + s_steps[s_index].Wait();
    }

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;

        if (s_player != null)
        {
            s_player.OnPerfectDodge -= OnPerfectDodge;
            s_player.SetControlSuspended(false);
            s_player.SetMoveScale(1f);
            s_player.ClearSlow();
        }

        var sb = new StringBuilder();
        sb.Append("{\n  \"reason\": \"").Append(Escape(reason)).Append("\",\n");
        sb.Append("  \"pass\": ").Append(s_pass).Append(",\n  \"fail\": ").Append(s_fail).Append(",\n");
        sb.Append("  \"results\": [\n");
        for (int i = 0; i < s_results.Count; i++)
            sb.Append("    \"").Append(Escape(s_results[i])).Append(i + 1 < s_results.Count ? "\",\n" : "\"\n");
        sb.Append("  ],\n  \"errors\": [\n");
        for (int i = 0; i < s_errors.Count; i++)
            sb.Append("    \"").Append(Escape(s_errors[i])).Append(i + 1 < s_errors.Count ? "\",\n" : "\"\n");
        sb.Append("  ]\n}\n");
        File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));

        Debug.Log($"[PlayerProbe] {reason} — PASS {s_pass} / FAIL {s_fail} → {OutPath}");
        s_steps.Clear();
        s_player = null;
        s_status = null;
        s_orphanAppearance = null;
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────
    private static PlayerController FindRunPlayer()
    {
        foreach (var pc in UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (!pc.IsShadowClone) return pc;
        return null;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        string line = $"{(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}";
        s_results.Add(line);
        if (ok) Debug.Log("[PlayerProbe] " + line);
        else    Debug.LogWarning("[PlayerProbe] " + line);
    }

    private static T GetField<T>(object target, string name)
    {
        var f = target.GetType().GetField(name, Inst);
        if (f == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)f.GetValue(target);
    }

    private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.01f;

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    /// <summary>수평 거리(m). <see cref="Flat"/>은 방향을 정규화해 돌려주므로 거리에 쓰면 늘 1이 나온다.</summary>
    private static float FlatDist(Vector3 a, Vector3 b) { Vector3 d = a - b; d.y = 0f; return d.magnitude; }

    /// <summary>원거리 과녁 — 플레이어 정면(벽이 있으면 그 앞) 2~<paramref name="dist"/>m에 세운다.</summary>
    private static async UniTaskVoid SpawnRangedTargetAsync(PlayerController p, float dist = 5f)
    {
        try
        {
            Vector3 fwd    = Flat(p.transform.forward);
            Vector3 origin = p.transform.position + Vector3.up;
            if (Physics.Raycast(origin, fwd, out var wall, dist, ~0, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(2f, wall.distance - 1f);
            Vector3 pos = p.transform.position + fwd * dist;

            var mb = await Managers.ObjectPooler.SpawnAsync<RelicFairy.Monster.MonsterBase>(
                RangedTargetKey, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.LookRotation(-fwd));
            if (mb == null) return;
            mb.name        = "@PlayerProbeTarget";
            mb.HpFloorMin1 = true;   // 측정 중 죽지 않게
            s_rangedTarget = mb;
        }
        catch (Exception e) { Debug.LogWarning("[PlayerProbe] 과녁 스폰 예외: " + e.Message); }
    }

    /// <summary>
    /// 평타와 같은 기하로 쏜다 — 발사점 = 플레이어 루트 + 정면 1m + 위 1m, 방향은 과녁 쪽 <b>수평</b>(y=0).
    /// 실제 발사 퍼널(CombatSpawner)을 지나므로 퍼널의 수직 보정이 켜져 있어야 낮은 몹에 맞는다.
    /// </summary>
    private static async UniTaskVoid FireArrowAtTargetAsync(PlayerController p)
    {
        try
        {
            Vector3 flat    = Flat(s_rangedTarget.transform.position - p.transform.position);
            Vector3 firePos = p.transform.position + flat + Vector3.up;

            var go = await Managers.ObjectPooler.SpawnAsync(ArrowKey, ObjectPoolerManager.PoolType.Effect,
                                                             firePos, Quaternion.LookRotation(flat));
            if (go == null || !go.TryGetComponent<BasicArrow>(out var arrow)) return;

            var req = ProjectileRequest.Create(ArrowKey, firePos, flat, 50f, p.gameObject, 0);
            CombatSpawner.SpawnProjectile(ref req, arrow);
            s_rangedFired = true;
        }
        catch (Exception e) { Debug.LogWarning("[PlayerProbe] 화살 발사 예외: " + e.Message); }
    }

    private static int CountAuraObjects()
    {
        // VFX 계층의 맨 위만 센다(자식 파티클 이름도 같은 접두어일 수 있다).
        int n = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith(AuraKey, StringComparison.Ordinal)) continue;
            if (t.parent != null && t.parent.name.StartsWith(AuraKey, StringComparison.Ordinal)) continue;
            n++;
        }
        return n;
    }

    private static void OnPerfectDodge(float _) => s_perfectDodgeCount++;

    /// <summary>
    /// Q 시나리오 — 적을 등지고 회피 → 회피 중 피격으로 저스트 회피 → 공격 연타(실제 입력 버퍼).
    /// 첫 공격이 시작되는 순간과 그 뒤를 재고, 창이 닫힌 뒤 한 번 더 쳐서 창 밖 속도를 잰다.
    /// </summary>
    private static async UniTaskVoid RunCounterScenarioAsync(PlayerController p, CounterRun q)
    {
        void OnRefund(float amount) { q.RefundEvents++; q.RefundTotal += amount; }
        void OnStrike(Vector3 from, Vector3 to, bool isFirst)
        {
            q.StrikeEvents++;
            if (!isFirst) return;
            q.FirstStrikes++;
            q.FirstStrikeDist = FlatDist(to, from);
        }
        void OnWindowEnded() => q.WindowEndEvents++;
        void OnSlash(int _)
        {
            if (q.SlashRbDist >= 0f || s_rangedTarget == null || q.AttackStart < 0f) return;
            q.SlashTfDist = FlatDist(s_rangedTarget.transform.position, p.transform.position);
            q.SlashRbDist = FlatDist(s_rangedTarget.transform.position, p.Rigid.position);
        }

        void OnHit(HitInfo info)
        {
            if (info.Attacker != p.gameObject) return;
            q.AllHits++;
            if (p.CounterAttackSpeedMultiplier > 1f) q.WindowHits++;
        }

        HitFeedbackService.OnHit += OnHit;
        p.Stamina.Refunded     += OnRefund;
        p.OnCounterStrike      += OnStrike;
        p.OnCounterWindowEnded += OnWindowEnded;
        p.EventReceiver.OnEffectStep += OnSlash;
        var gauge = p.GetComponent<StaminaBarView>();
        try
        {
            var target = s_rangedTarget.transform;
            var act    = GetField<LayerStateMachine<ActState>>(p, "_actSM");

            // 몸 회전은 물리 스텝에 걸쳐 돈다(H2와 같은 대기) — 돌기 전에 회피하면 적 쪽으로 대시한다.
            p.RequestFacing(Quaternion.LookRotation(Flat(p.transform.position - target.position)));
            await UniTask.Delay(TimeSpan.FromSeconds(0.3f), ignoreTimeScale: true);

            float stMax = p.RuntimeStats.MaxStamina;
            p.Stamina.TryConsume(stMax * 0.5f, stMax, 0.5f);   // 실제 플레이처럼 대시가 게이지를 먼저 쓴 상태

            float t0 = Time.unscaledTime;
            p.LocoSM.Change(LocoState.Dodge);
            p.TakeDamage(1, target.gameObject);
            q.Triggered = p.CounterAttackSpeedMultiplier > 1f;
            q.TargetHpBefore = s_rangedTarget.CurrentHp;
            q.TargetImmune   = s_rangedTarget.IsDamageImmuneNow;
            if (gauge != null)
            {
                q.GaugeFlashed = GetField<float>(gauge, "_gainStart") >= 0f;
                q.GaugeSkinned = GetField<bool>(gauge, "_useSprites");
            }

            // 회피 중 연타 — 버퍼 유효시간(0.18초)보다 촘촘히 넣는다. 공격이 시작되면 멈춘다.
            Vector3 posAtStart = p.Rigid.position;
            while (Time.unscaledTime - t0 < 2f)
            {
                if (q.DodgeEnd < 0f && p.LocoSM.CurrentId != LocoState.Dodge) q.DodgeEnd = Time.unscaledTime - t0;
                if (act.CurrentId == ActState.Attack) break;
                posAtStart   = p.Rigid.position;
                q.DistBefore = FlatDist(target.position, posAtStart);
                p.InputBuffer.Push(Command.Light);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            if (act.CurrentId != ActState.Attack) { q.Note = "공격이 시작되지 않음"; return; }

            q.AttackStart      = Time.unscaledTime - t0;
            q.AttackInWindow   = p.CounterAttackSpeedMultiplier > 1f;
            q.CounterAnimSpeed = p.Anim.speed;
            q.PlannedStep      = GetStateField<float>(act, "_effectiveStepDistance");
            q.CounterLungeFlag = GetStateField<bool>(act, "_counterLunge");
            q.LungeTargetDist  = GetStateField<float>(act, "_lungeTargetDist");
            p.InputBuffer.Clear();   // 연타로 남은 입력이 2타를 이어 붙이면 첫 타 추격이 도중에 끊긴다 — 첫 타만 잰다

            // 추격 추적(진단) — 0.25초 동안 프레임마다 전진 진행·물리 위치를 남긴다 → Temp/counter_trace.txt
            var   trace = new StringBuilder();
            float tA    = Time.unscaledTime;
            while (Time.unscaledTime - tA < 0.25f)
            {
                if (act.CurrentId == ActState.Attack)
                    trace.AppendLine($"{Time.unscaledTime - tA:F3}s fixed={Time.fixedTime:F3} ts={Time.timeScale:F2} " +
                                     $"lastNT={GetStateField<float>(act, "_stepLastNT"):F3} eff={GetStateField<float>(act, "_effectiveStepDistance"):F2} " +
                                     $"dist={FlatDist(target.position, p.Rigid.position):F2} " +
                                     $"yawErr={Vector3.Angle(Flat(p.transform.forward), Flat(target.position - p.Rigid.position)):F0}");
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            File.WriteAllText("Temp/counter_trace.txt", trace.ToString());
            Vector3 rbPos = p.Rigid.position;
            q.DistAfter   = FlatDist(target.position, rbPos);
            q.Travel      = FlatDist(rbPos, posAtStart);
            q.FacingAngle = Vector3.Angle(Flat(p.transform.forward), Flat(target.position - rbPos));

            // 창이 닫히고 공격이 끝난 뒤 한 번 더 — 창 밖 속도 기준값.
            while (Time.unscaledTime - t0 < s_perfectDodgeTotal + 0.2f || act.CurrentId != ActState.None)
            {
                if (Time.unscaledTime - t0 > 4f) { q.Note = "창 밖 공격 대기 시간 초과"; return; }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            float t1 = Time.unscaledTime;
            while (act.CurrentId != ActState.Attack && Time.unscaledTime - t1 < 1f)
            {
                p.InputBuffer.Push(Command.Light);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            if (act.CurrentId != ActState.Attack) { q.Note = "창 밖 공격이 시작되지 않음"; return; }
            q.NormalAnimSpeed = p.Anim.speed;
            q.Done = true;
        }
        catch (Exception e) { q.Note = "예외: " + e.Message; }
        finally
        {
            HitFeedbackService.OnHit -= OnHit;
            p.Stamina.Refunded     -= OnRefund;
            p.OnCounterStrike      -= OnStrike;
            p.OnCounterWindowEnded -= OnWindowEnded;
            p.EventReceiver.OnEffectStep -= OnSlash;
            if (gauge != null) q.GaugeStep = GetField<float>(gauge, "_dividerStep");
        }
    }

    /// <summary>
    /// S 시나리오 — 멈춰 선 채 평타 1회. 첫 슬래시 이벤트 순간의 위치를 기준으로 그 전/후 이동을 잰다.
    /// 입력은 1번만 넣고 버퍼를 비운다(남은 입력이 콤보 2타를 이어 붙이면 측정이 섞인다).
    /// </summary>
    private static async UniTaskVoid RunSlashScenarioAsync(PlayerController p, SlashRun r)
    {
        // 위치는 물리 위치(rb.position) — transform은 보간돼 한 스텝 늦게 따라와 '슬래시 뒤 이동'으로 잘못 잡힌다.
        var     act      = GetField<LayerStateMachine<ActState>>(p, "_actSM");
        bool    slashed  = false;
        Vector3 slashPos = default;
        void OnEffectStep(int _)
        {
            if (slashed) return;
            slashed       = true;
            slashPos      = p.Rigid.position;
            r.PlannedStep = GetStateField<float>(act, "_effectiveStepDistance");
        }

        p.EventReceiver.OnEffectStep += OnEffectStep;
        try
        {
            float t0 = Time.unscaledTime;
            while (act.CurrentId != ActState.None && Time.unscaledTime - t0 < 2f)
                await UniTask.Yield(PlayerLoopTiming.Update);

            Vector3 start = p.Rigid.position;
            p.InputBuffer.Clear();
            p.InputBuffer.Push(Command.Light);
            while (act.CurrentId != ActState.Attack && Time.unscaledTime - t0 < 3f)
                await UniTask.Yield(PlayerLoopTiming.Update);
            if (act.CurrentId != ActState.Attack) { r.Note = "공격이 시작되지 않음"; return; }
            p.InputBuffer.Clear();
            r.AnimSpeed = p.Anim.speed;
            r.SlashNorm = GetStateField<float>(act, "_slashNorm");
            r.Weapon    = p.WeaponManager?.CurrentWeaponData?.weaponType.ToString() ?? "없음";

            // 프레임 추적 — 슬래시 뒤 이동이 전진 끝물인지, 남은 속도(미끄러짐)인지, 다음 상태의 이동인지 가른다.
            var trace = new StringBuilder();
            trace.AppendLine($"무기 {r.Weapon} · 슬래시 시각 {r.SlashNorm:F3} · anim.speed {r.AnimSpeed:F2} · 계획 전진 {GetStateField<float>(act, "_effectiveStepDistance"):F2}m");
            float t1 = Time.unscaledTime;
            float slashAt = -1f;
            while (Time.unscaledTime - t1 < 1.5f)
            {
                if (slashed && slashAt < 0f) slashAt = Time.unscaledTime;
                if (slashAt >= 0f && Time.unscaledTime - slashAt > 0.3f) break;
                var st = p.Anim.GetCurrentAnimatorStateInfo(0);
                trace.AppendLine($"{Time.unscaledTime - t1:F3}s f{Time.frameCount} act {act.CurrentId} loco {p.LocoSM.CurrentId} · nt {st.normalizedTime:F3} · " +
                                 $"이동 {FlatDist(p.Rigid.position, start):F3}m · 수평속도 {new Vector2(p.Rigid.linearVelocity.x, p.Rigid.linearVelocity.z).magnitude:F2}m/s" +
                                 (slashed ? " · 슬래시 후" : ""));
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            File.WriteAllText("Temp/slash_trace.txt", trace.ToString());
            if (!slashed) { r.Note = "슬래시 이벤트가 오지 않음"; return; }
            r.MoveBeforeSlash = FlatDist(slashPos, start);
            r.MoveAfterSlash  = FlatDist(p.Rigid.position, slashPos);
            r.Done = true;
        }
        catch (Exception e) { r.Note = "예외: " + e.Message; }
        finally { p.EventReceiver.OnEffectStep -= OnEffectStep; }
    }

    /// <summary>행동 상태머신의 현재 상태 객체에서 비공개 필드를 읽는다(측정 전용).</summary>
    private static T GetStateField<T>(LayerStateMachine<ActState> sm, string name)
    {
        var state = GetField<object>(sm, "_current");
        return state != null ? GetField<T>(state, name) : default;
    }

    /// <summary>플레이어가 과녁을 한 대 친 것으로 타격 통지를 낸다(실제 경로 HitFeedbackService.OnHit).</summary>
    private static void RaisePlayerHit(PlayerController p)
    {
        if (s_rangedTarget == null) return;
        HitFeedbackService.RaiseHit(new HitInfo(p.gameObject, s_rangedTarget.gameObject,
            s_rangedTarget.transform.position, Vector3.forward, 10f, false, WeaponActionType.GroundLight));
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (condition.StartsWith("[PlayerProbe]", StringComparison.Ordinal)) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            s_errors.Add($"{type}: {condition}");
        else if (type == LogType.Warning)
            s_warnings.Add(condition);
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
