#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-07 런 결과창 실측. ① 미리 보기(플레이 중 아무 씬) — 가짜 사본 셋(가득 · 빈 · 긴 이름)을 띄워 등장 0.3 · 2.0초를 찍는다.
/// ② 실전(B5에서 추가) — 런 중 실제로 쓰러뜨려 진짜 사본을 찍는다. 결과: Temp/ui_shots/RR_*.png · 로그 「[RunResult] 끝」.
/// </summary>
public static class RunResultProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-07 런 결과창 미리 보기 (플레이 중)")]
    private static void Preview()
    {
        if (!EditorApplication.isPlaying) return;
        PreviewAsync().Forget();
    }

    private static async UniTaskVoid PreviewAsync()
    {
        var sb = new StringBuilder("런 결과창 미리 보기\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            await ShowAndShoot(Full(), "RR_full", sb, new[] { 0.3f, 2.0f });
            await ShowAndShoot(new RunResultSnapshot { ChapterNumber = 1, PlaySeconds = 42 }, "RR_empty", sb, new[] { 2.0f });
            var longName = Full();
            longName.Weapon.Name = new string('긴', 20);
            longName.KilledBy = "아주아주 긴 이름을 가진 정예 몬스터";
            await ShowAndShoot(longName, "RR_long", sb, new[] { 2.0f });
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/run_result_probe.txt", sb.ToString());
            Debug.Log("[RunResult] 끝\n" + sb);
        }
    }

    /// <summary>
    /// 실전(런 중, 전투방) — 가까운 적에게 출처가 다른 피해 몇 갈래를 넣고(C1 뒤 비중 확인), 사망을 불러 진짜 결과창을 찍는다.
    /// <paramref name="withPopup"/>면 시간을 멈추는 팝업(정제소)을 연 채로 쓰러뜨린다(Review Focus 2) · <paramref name="cleared"/>면 완주 경로(HandleRunClear).
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-07 런 결과창 실전 (런 중, 전투방)")]
    private static void Live() { if (EditorApplication.isPlaying) LiveAsync(false, false).Forget(); }

    [MenuItem("RelicFairy/Debug/10-07 런 결과창 실전 — 팝업 연 채로 (런 중)")]
    private static void LiveWithPopup() { if (EditorApplication.isPlaying) LiveAsync(true, false).Forget(); }

    [MenuItem("RelicFairy/Debug/10-07 런 결과창 실전 — 완주 (런 중)")]
    private static void LiveClear() { if (EditorApplication.isPlaying) LiveAsync(false, true).Forget(); }

    private static async UniTaskVoid LiveAsync(bool withPopup, bool cleared)
    {
        var sb = new StringBuilder($"런 결과창 실전 (팝업 {withPopup} · 완주 {cleared})\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            var boot = GameRunBootstrapper.Instance;
            var player = boot?.Run?.Player;
            if (player == null) { sb.AppendLine("런 없음"); return; }
            HitNearest(player, sb);
            // 시간을 멈추는 팝업(BlocksGameplay) — 정제소 창. 결과창이 그 위에 뜨고 timeScale 0에서도 돌아야 한다.
            if (withPopup) await Managers.UI.ShowPopupUIAndGetAsync<UI_RefineryPanel>();
            sb.AppendLine($"  timeScale {Time.timeScale}");
            if (cleared) boot.HandleRunClear();
            else         boot.HandlePlayerDeath();
            float t0 = Time.unscaledTime;
            while (RunResultView.Active == null && Time.unscaledTime - t0 < 12f) await UniTask.Yield();
            if (RunResultView.Active == null) { sb.AppendLine("12초 안에 결과창이 안 떴다"); return; }
            float shown = Time.unscaledTime;
            foreach (float at in new[] { 0.3f, 2.0f })
            {
                float wait = shown + at - Time.unscaledTime;
                if (wait > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
                ScreenCapture.CaptureScreenshot($"Temp/ui_shots/RR_live{(withPopup ? "_popup" : "")}{(cleared ? "_clear" : "")}_{at:0.0}.png");
            }
            var s = RunResultView.LastShown;
            sb.AppendLine($"  {RunResultPresenter.Title(s)} · {RunResultPresenter.Subtitle(s)} · {RunResultPresenter.FormatTime(s.PlaySeconds)}");
            sb.AppendLine($"  정수 {s.Essence} (계약 {s.EssenceFromContracts} · 초행 {s.EssenceFromFirstRun}) · 골드 {s.Gold} · 재료 {s.EnhanceMaterial} · 원석 {s.RuneOre}");
            sb.AppendLine($"  무기 {RunResultPresenter.WeaponTitle(s.Weapon)} · 유물 {s.RelicName} · 룬 {s.RuneCount} · 서약 {s.Covenants.Count} · 띠 {s.Segments.Count}칸");
            for (int i = 0; i < RunStats.SourceCount; i++) sb.AppendLine($"  피해 {(DamageSource)i} {s.Damage[i]}");
            var rpm = RunProgressManager.Instance;
            bool saveLeft = rpm != null && rpm.HasLocalRun(rpm.ActiveSlotIndex);
            sb.AppendLine($"  CHECK C1 결과창 동안 런 세이브 지워짐: {!saveLeft}");
            sb.AppendLine($"  CHECK I2 가장 큰 한 방 < 1,000,000 (실제로 깎인 체력): {s.BiggestHit < 1000000} ({s.BiggestHit:N0})");
            sb.AppendLine($"  CHECK M3 챕터 이름 있음: {!string.IsNullOrEmpty(s.ChapterName)} ({s.ChapterName})");
            sb.AppendLine($"  CHECK M7 시작 지급 재료는 얻은 양이 아님: {s.EnhanceMaterial == 0} ({s.EnhanceMaterial})");
            // Space로 넘어가는지 — 1초 지난 뒤라 Continue와 같다(실키 입력은 사람이 확인)
            RunResultView.Active?.Continue();
            float tc = Time.unscaledTime;
            while (RunResultView.Active != null && Time.unscaledTime - tc < 3f) await UniTask.Yield();
            sb.AppendLine($"  닫힘 {RunResultView.Active == null}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/run_result_live.txt", sb.ToString());
            Debug.Log("[RunResult] 끝\n" + sb);
        }
    }

    private static void HitNearest(PlayerController player, StringBuilder sb)
    {
        RelicFairy.Monster.MonsterBase best = null;
        float bd = float.MaxValue;
        foreach (var mb in Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb.IsDead) continue;
            float d = (mb.transform.position - player.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = mb; }
        }
        if (best == null) { sb.AppendLine("  적 없음 — 피해 생략"); return; }
        best.HpFloorMin1 = true;
        var go = best.gameObject;
        CombatDamage.Deal(new CombatDamage.Request { Target = go, Owner = player.gameObject, BaseDamage = 120, ActionType = WeaponActionType.GroundLight });
        CombatDamage.Deal(new CombatDamage.Request { Target = go, Owner = player.gameObject, BaseDamage = 200, ActionType = WeaponActionType.ESkill });
        CombatDamage.Deal(new CombatDamage.Request { Target = go, Owner = player.gameObject, BaseDamage = 300, ActionType = WeaponActionType.QSkill });
        best.TakeSynergyDamage(80, player.gameObject, 1f, false, DamageKind.Synergy);
        best.TakeSynergyDamage(40, player.gameObject, 1f, false, DamageKind.Dot);
        using (DamageSourceScope.Enter(DamageSource.Covenant))
            best.TakeSynergyDamage(150, player.gameObject, 1f, false, DamageKind.Synergy);
        // I2 — 하한에 막힌 큰 한 방: 실제로 깎인 체력만 기록돼야 한다(감산 전 수치 1,000,000이면 실패).
        best.TakeSynergyDamage(1000000f, player.gameObject, 1f, false, DamageKind.Synergy);
        sb.AppendLine($"  최대 체력 {best.EffectiveMaxHp}");
        best.HpFloorMin1 = false;
        sb.AppendLine($"  피해 넣음 → {best.name}");
    }

    internal static async UniTask ShowAndShoot(RunResultSnapshot snap, string name, StringBuilder sb, float[] shots)
    {
        var task = RunResultView.ShowAsync(snap, default);
        float t0 = Time.unscaledTime;
        foreach (float at in shots)
        {
            float wait = t0 + at - Time.unscaledTime;
            if (wait > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}_{at:0.0}.png");
            await UniTask.Yield();
        }
        sb.AppendLine($"  {name}: 떠 있음 {RunResultView.Active != null}");
        RunResultView.Active?.Continue();
        await task;
        await UniTask.Yield();   // Destroy는 프레임 끝에 일어난다 — 한 프레임 뒤에 잰다
        sb.AppendLine($"  {name}: 닫힘 {RunResultView.Active == null}");
    }

    private static RunResultSnapshot Full()
    {
        var s = new RunResultSnapshot
        {
            Cleared = false, ChapterNumber = 3, ChapterName = "성채", RoomClears = 23, Era = StoryEra.Liberated,
            KilledBy = "죽음의 기사", PlaySeconds = 2832,
            Essence = 128, EssenceFromContracts = 30, ContractsDone = 2, EssenceFromFirstRun = 12,
            Gold = 1240, EnhanceMaterial = 38, RuneOre = 21,
            Weapon = new RunResultSnapshot.WeaponLine { Name = "무명의 형상", Enhance = 7, FormName = "쌍풍", FormTier = 2 },
            SubWeapon = new RunResultSnapshot.WeaponLine { Name = "석궁" },
            RelicName = "가웨인", MemFaint = 2, MemClear = 3, MemRadiant = 1,
            RuneCount = 8, CovenantFires = 38,
            Kills = 214, EliteKills = 9, BossKills = 2, FlawlessChapters = 1, BiggestHit = 3412, BiggestHitSource = DamageSource.Covenant,
        };
        s.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch1 숲", Seconds = 665, Done = true });
        s.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch2 동굴", Seconds = 880, Done = true });
        s.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch3 성채", Seconds = 1287, Done = true });
        s.Segments.Add(new RunResultSnapshot.Segment { Label = "Ch4 왕좌", Seconds = 0, Done = false });
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 12; x++)
                s.Board.Add(new RunResultSnapshot.RuneCell { X = x, Y = y, Element = (x + y) % 4 == 0 ? "FIRE" : (x * y) % 7 == 1 ? "LIGHT" : null });
        s.BoardFilled = 23; s.BoardTotal = 132;
        s.Synergies.Add(("FIRE", 3)); s.Synergies.Add(("LIGHT", 2));
        s.Covenants.Add(new RunResultSnapshot.CovenantLine
        {
            Name = "연격 → 초신성 → 잔불", Cause = "같은 적을 3연타할 때마다",
            Tiers = new[] { CovenantTier.Gold, CovenantTier.Gold, CovenantTier.Ruby },
        });
        long[] d = { 4100, 2200, 1200, 1100, 900, 500 };
        for (int i = 0; i < d.Length; i++) s.Damage[i] = d[i];
        return s;
    }
}
#endif
