#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-24 빌드 전 확인 — 세이브는 건드리지 않는다(런 사본 값만 · 제단 데이터 읽기만).
/// <list type="bullet">
/// <item>전투 시계(U5) — 전투방에서 싸우는 동안 오르나 · 시간이 멈추면(고르기 창) 멈추나 · 방 기준이 한 번 더해지나 · 보스 뒤 띠 알림 · 결과창 「전투 m:ss · 등급」.</item>
/// <item>전설 증강(W7) — 카드 한 장만 전설 색 · 「전설 · 공용」 · 단계 없음 · 제단 「증강의 계시」 노드 자리 · 글.</item>
/// <item>첫 안내 순서(N10) — 유물을 장비 · 파츠보다 먼저 고르면 그 단계를 건너뛰고 성문까지 가나.</item>
/// </list>
/// 결과 Temp/fb1024_*.txt · Temp/ui_shots/FB24_*.png · 로그 「[Fb24] 끝」.
/// </summary>
public static class Fb1024ProbeEditor
{
    private const string Tag = "[Fb24]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-24 전투 시계 · 등급 확인 (런 중, 전투방)")]
    private static void RunClock()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        ClockAsync().Forget();
    }

    [MenuItem("RelicFairy/Debug/10-24 전설 증강 카드 · 증강의 계시 · 첫 안내 순서 확인 (플레이 중)")]
    private static void RunLegend()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        LegendAsync().Forget();
    }

    // ── 전투 시계 ──
    private static async UniTaskVoid ClockAsync()
    {
        var sb = new StringBuilder("10-24 전투 시계(U5)\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {ok} — {what}"); }
        try
        {
            var run = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.Run : null;
            var stats = run?.RunStats;
            if (stats == null) { sb.AppendLine("런이 없다"); return; }
            sb.AppendLine($"방 {run.CurrentRoomKind} · 챕터 {(int)run.CurrentChapter} · 시기 {run.StartEra} · 깊이 {run.AbyssDepth} · 방 순번 {run.RoomEnterSerial}");
            Check("시계가 떠 있다(RunCombatClock)", Object.FindFirstObjectByType<RunCombatClock>() != null);

            float c0 = stats.CombatSeconds, b0 = stats.BaseSeconds;
            await UniTask.Delay(3000, DelayType.Realtime);
            float c1 = stats.CombatSeconds, b1 = stats.BaseSeconds;
            float expectBase = RunTimeGrade.RoomBase(run.CurrentRoomKind, (int)run.CurrentChapter, run.StartEra, run.AbyssDepth);
            sb.AppendLine($"    3초 싸움: 전투 {c0:0.00} → {c1:0.00}(+{c1 - c0:0.00}) · 기준 {b0:0.0} → {b1:0.0}(이 방 기준 {expectBase:0.0}) · timeScale {Time.timeScale:0.00}");
            Check("싸우는 동안 오른다(3초 → +2.0 ~ 3.3 · 경직 슬로 몫)", c1 - c0 > 2.0f && c1 - c0 < 3.3f);
            Check("이 방 기준이 한 번만 더해졌다", b1 > 0f && Mathf.Abs(b1 - b0) < 0.01f || Mathf.Abs(b1 - b0 - expectBase) < 0.01f);

            float keep = Time.timeScale;
            Time.timeScale = 0f;   // 고르기 창 · 대사 · 일시정지와 같은 상태
            float c2 = stats.CombatSeconds;
            await UniTask.Delay(1500, DelayType.Realtime);
            float c3 = stats.CombatSeconds;
            Time.timeScale = keep;
            Check($"시간이 멈추면 멈춘다({c2:0.00} → {c3:0.00})", Mathf.Abs(c3 - c2) < 0.001f);

            // 보스 뒤 띠 알림 — 보스방 클리어 신호만 흉내(보스 정산 훅은 부르지 않는다)
            var clock = Object.FindFirstObjectByType<RunCombatClock>();
            typeof(RunCombatClock).GetMethod("HandleBossRoomCleared", Any)?.Invoke(clock, new object[] { Vector3.zero });
            await UniTask.Delay(2300, DelayType.Realtime);
            var banner = Object.FindFirstObjectByType<ZoneTitleBanner>();
            var title = banner != null ? typeof(ZoneTitleBanner).GetField("_title", Any)?.GetValue(banner) as TMP_Text : null;
            var desc  = banner != null ? typeof(ZoneTitleBanner).GetField("_desc", Any)?.GetValue(banner) as TMP_Text : null;
            sb.AppendLine($"    띠 알림 「{title?.text}」 · 「{desc?.text}」");
            Check("보스 뒤 띠 알림 「… — 전투 m:ss · 등급」", title != null && title.text.Contains("전투") && title.text.Contains("<color="));
            await Shot("FB24_1_banner");

            // 결과창 — 마친 챕터 하나(지금 값으로 닫음) + 지금 챕터(완주로) → 「전투 m:ss · 등급」 줄
            stats.ReportChapterEnd((int)run.CurrentChapter, run.PlaySeconds);
            Check("챕터를 닫으면 지금 값이 비워진다", stats.CombatSeconds == 0f && stats.BaseSeconds == 0f);
            bool known = stats.TryChapterClockAt(stats.ChapterEndCount - 1, out float cc, out float cb);
            Check($"닫은 챕터 값이 남는다(전투 {cc:0.0} · 기준 {cb:0.0} · {RunTimeGrade.Letter(cc, cb)})", known && cc > 0f && cb > 0f);
            await UniTask.Delay(1500, DelayType.Realtime);   // 지금 챕터 몫도 조금 쌓이게
            var json = stats.ToJson();
            var back = RunStats.FromJson(json);
            Check("이어하기 저장 · 되살리기에서 값이 남는다", back.TryChapterClockAt(back.ChapterEndCount - 1, out float bc, out _) && Mathf.Abs(bc - cc) < 0.01f);

            var snap = RunResultProvider.Capture(run, AppBootstrapper.Instance?.Loadout, cleared: true);
            foreach (var g in snap.Segments) sb.AppendLine($"    띠 {g.Label} · {g.Seconds}s · 전투 {g.CombatSeconds}s · 등급 「{g.Grade}」 · 끝 {g.Done}");
            Check("결과창 마친 챕터에 전투 시간 · 등급", snap.Segments.Count > 0 && snap.Segments[0].CombatSeconds > 0 && !string.IsNullOrEmpty(snap.Segments[0].Grade));
            Directory.CreateDirectory("Temp/ui_shots");
            await RunResultProbeEditor.ShowAndShoot(snap, "FB24_2_result", sb, new[] { 2.6f });
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/fb1024_clock.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    // ── 전설 증강 · 제단 노드 · 첫 안내 ──
    private static async UniTaskVoid LegendAsync()
    {
        var sb = new StringBuilder("10-24 전설 증강(W7) · 증강의 계시 · 첫 안내 순서(N10)\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {ok} — {what}"); }
        try
        {
            // ① 제단 노드
            var node = MemoryAltarCatalog.Get(MemoryAltarCatalog.AugmentRevelation);
            Check($"제단 노드 「{node?.DisplayName}」 · {node?.Branch} · 단계 {node?.MaxLevel} · 값 {node?.CostForLevel(1, false)}/{node?.CostForLevel(2, false)}/{node?.CostForLevel(3, false)}",
                  node != null && node.Branch == AltarBranch.Weapon && node.MaxLevel == 3);
            for (int lv = 0; lv <= 3; lv++) sb.AppendLine($"    단계 {lv}: {MemoryAltarCatalog.RevelationText(lv)}");
            var place = MemoryAltarLayout.Compute(MemoryAltarCatalog.All);
            float minGap = float.MaxValue; string near = null;
            if (node != null && place.TryGetValue(node.Id, out var me))
                foreach (var kv in place)
                {
                    if (kv.Key == node.Id) continue;
                    float gap = Vector2.Distance(kv.Value.Position, me.Position) - (kv.Value.Diameter + me.Diameter) * 0.5f;
                    if (gap < minGap) { minGap = gap; near = kv.Key; }
                }
            Check($"노드 자리가 이웃과 안 겹친다(가장 가까운 {near} 사이 {minGap:0}px · 22 이상)", minGap >= 22f);
            Check($"지금 확률 읽기 — 화면마다 {MeleeAugmentDraft.LegendChance(false) * 100f:0.#}% · 런마다 {MeleeAugmentDraft.LegendChancePerRun() * 100f:0}%", MeleeAugmentDraft.LegendChance(false) > 0f);

            // ② 전설 카드 한 장 + 금 카드 한 장
            MeleeAugmentDef legend = null, gold = null;
            foreach (var d in MeleeAugmentCatalog.All)
            {
                if (d.Weapon != MeleeWeaponKind.None) continue;
                if (legend == null && d.IsLegend) legend = d;
                if (gold == null && d.Tier == AugmentTier.Gold) gold = d;
            }
            if (legend != null && gold != null)
            {
                var offer = new MeleeAugmentOffer { Level = 3, Tier = AugmentTier.Gold };
                offer.Cards = new[] { new MeleeAugmentCard(legend.Id, 1), new MeleeAugmentCard(gold.Id, 1) };
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_MeleeAugmentPopup>();
                if (popup != null)
                {
                    popup.Show(offer);
                    await UniTask.Delay(2200, DelayType.Realtime);
                    var cards = typeof(UI_MeleeAugmentPopup).GetField("_cards", Any)?.GetValue(popup) as System.Array;
                    var c0 = cards?.GetValue(0); var c1 = cards?.GetValue(1);
                    string Txt(object card, string f) => (card?.GetType().GetField(f, Any)?.GetValue(card) as TMP_Text)?.text;
                    var tier = typeof(UI_MeleeAugmentPopup).GetField("_tier", Any)?.GetValue(popup) as TMP_Text;
                    sb.AppendLine($"    머리 「{tier?.text}」 · 카드1 「{Txt(c0, "Chip")}」 「{Txt(c0, "Name")}」 「{Txt(c0, "Rank")}」 · 카드2 「{Txt(c1, "Chip")}」 「{Txt(c1, "Rank")}」");
                    Check("전설 카드 — 「전설 · 공용」 · 단계 없음(처음부터 완성)", Txt(c0, "Chip") == "전설 · 공용" && Txt(c0, "Rank") == "처음부터 완성");
                    Check("다른 카드는 화면 등급(금) 그대로", Txt(c1, "Chip") == "금 · 공용");
                    Check("머리에 「전설 한 장」", tier != null && tier.text.Contains("전설"));
                    await Shot("FB24_3_legend_card");
                    popup.ClosePopupUI();
                    await UniTask.Delay(400, DelayType.Realtime);
                }
                else sb.AppendLine("    증강 창을 못 열었다");
            }
            else sb.AppendLine($"    공용 전설 · 금 증강을 못 찾았다(전설 {legend?.Id} · 금 {gold?.Id})");

            // ③ 첫 안내 순서 — 이 씬의 온보딩 감독을 메모리에서만 첫 판처럼 돌린다(완료 기록은 되돌린다)
            var dir = Object.FindFirstObjectByType<BaseCampOnboardingDirector>(FindObjectsInactive.Include);
            if (dir != null)
            {
                string key = "basecamp_onboarding_done_v1_slot" + (RunProgressManager.Instance?.ActiveSlotIndex ?? 0);
                int keepDone = PlayerPrefs.GetInt(key, 0);
                var t = typeof(BaseCampOnboardingDirector);
                t.GetField("_active", Any)?.SetValue(dir, true);
                t.GetField("_swordPhase", Any)?.SetValue(dir, false);
                if (t.GetField("_cts", Any)?.GetValue(dir) == null) t.GetField("_cts", Any)?.SetValue(dir, new System.Threading.CancellationTokenSource());
                var steps = t.GetField("steps", Any)?.GetValue(dir) as System.Array;
                var enter = t.GetMethod("EnterStep", Any);
                var report = t.GetMethod("HandleReport", Any);
                enter?.Invoke(dir, new object[] { 0, true });
                report?.Invoke(dir, new object[] { "Relic", "Lancelot", 1 });   // 장비 단계에서 유물을 먼저
                int afterRelic = (int)t.GetField("_stepIndex", Any).GetValue(dir);
                report?.Invoke(dir, new object[] { "Equip", "Bow", 1 });
                int afterEquip = (int)t.GetField("_stepIndex", Any).GetValue(dir);
                report?.Invoke(dir, new object[] { "Parts", "Pierce", 1 });
                int afterParts = (int)t.GetField("_stepIndex", Any).GetValue(dir);
                var arrow = t.GetField("guideArrow", Any)?.GetValue(dir) as OnboardingGuideArrow;
                var target = arrow != null ? typeof(OnboardingGuideArrow).GetField("_target", Any)?.GetValue(arrow) as Transform : null;
                var gate = t.GetField("gateTarget", Any)?.GetValue(dir) as Transform;
                sb.AppendLine($"    단계 {steps?.Length}개 · 유물 먼저 → {afterRelic} · 장비 → {afterEquip} · 파츠 → {afterParts} · 화살표 대상 {(target != null ? target.name : "없음")}(성문 {(gate != null ? gate.name : "없음")})");
                Check("유물을 먼저 골라도 장비 → 파츠 뒤 유물 단계를 건너뛰고 성문으로", steps != null && afterParts == steps.Length && target == gate);
                t.GetField("_active", Any)?.SetValue(dir, false);
                arrow?.Clear();
                ZoneSign.SetObjective(null);
                PlayerPrefs.SetInt(key, keepDone); PlayerPrefs.Save();
                BaseCampOnboardingDirector_ResetInProgress();
            }
            else sb.AppendLine("    이 씬에 온보딩 감독이 없다 — 첫 안내 순서는 건너뜀");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/fb1024_legend.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static void BaseCampOnboardingDirector_ResetInProgress()
        => typeof(BaseCampOnboardingDirector).GetProperty("InProgress", Any)?.SetValue(null, false);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
