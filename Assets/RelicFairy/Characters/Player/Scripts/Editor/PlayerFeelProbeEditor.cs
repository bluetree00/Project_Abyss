using System;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// [실측 도구 · 플레이 중] 플레이어 연출 기준(09-25)이 실제로 지켜지는지 잰다. 테스트 허브(베이스캠프 훈련 인형)에서 돈다.
///
///  A. 흔들림 겹침 — 보스 강공(0.14/0.45초) 도중 약한 흔들림(0.022/0.1초)이 와도 보스 흔들림이 끝까지 가는가.
///     (고치기 전엔 약한 쪽이 감쇠를 덮어써 0.12초 만에 꺼졌다.)
///  B. 주는 쪽 단계 — 기본 공격 / 스킬 / 스킬 막타를 인형에 한 방씩 넣고 흔들림 진폭·막타 신호를 잰다.
///     기준: 기본 ≤ 0.012 · 스킬 ≤ 0.016 · 막타 = 0.022, 막타만 가장자리 금빛.
///  C. 막타 신호가 화면에 보이는가 — 신호 전·후 화면을 게임 안에서 찍어 모서리·가운데 색 변화를 잰다.
///
/// 결과: Temp/player_feel_probe.txt · 캡처 Temp/player_feel_before.png · player_feel_after.png
/// MCP 스크린샷은 강제 임포트로 도메인 리로드를 부르므로 쓰지 않는다(09-22).
/// </summary>
public static class PlayerFeelProbeEditor
{
    private const string OutPath = "Temp/player_feel_probe.txt";
    private const string Title   = "플레이어연출";
    private const float  AmpPerTrauma = 0.18f;   // HitFeelService.TraumaPerAmplitude = 1/0.18

    private static readonly BindingFlags Static   = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/플레이어 연출 실측 — 흔들림·타격 단계·막타 신호 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[플레이어연출] 플레이 모드에서만 동작한다."); return; }
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        // 대상: 베이스캠프 훈련 인형이 있으면 인형, 없으면(런 전투방) 살아 있는 일반 몬스터 하나.
        MonsterBase dummy = UnityEngine.Object.FindFirstObjectByType<TrainingDummyMonster>();
        if (dummy == null)
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (!mb.IsDead && mb.isActiveAndEnabled && mb.Grade != MonsterGrade.Boss) { dummy = mb; break; }
        if (player == null) { Debug.LogWarning("[플레이어연출] 플레이어가 없다."); return; }
        RunAsync(player, dummy, player.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController player, MonsterBase dummy, CancellationToken ct)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin(OutPath, Title);
        try
        {
            HitFeelService.CameraShake(0f, 0.1f);   // 흔들림 확장을 활성 카메라에 붙여 둔다
            var ext = typeof(HitFeelService).GetField("_shakeExt", Static)?.GetValue(null) as CameraShakeExtension;
            var traumaField = typeof(CameraShakeExtension).GetField("_trauma", Instance);
            if (ext == null || traumaField == null) { sb.AppendLine("⚠ 흔들림 확장을 못 찾았다 — 중단"); return; }
            float Trauma() => (float)traumaField.GetValue(ext);

            // ── A. 흔들림 겹침 ─────────────────────────────────────
            sb.AppendLine("A. 흔들림 겹침 (보스 강공 0.14/0.45초가 끝까지 가는가)");
            float solo = await MeasureShakeAsync(Trauma, withWeakAfter: false, ct);
            float mixed = await MeasureShakeAsync(Trauma, withWeakAfter: true, ct);
            // 강공만 걸어도 1.5초 안에 안 꺼지면 흔들림 확장이 붙은 카메라가 안 도는 것이다(09-25 베이스캠프에서 실제로 그랬다) — 판정 무효.
            bool live = solo < 1.4f;
            sb.AppendLine($"  강공만: {solo:F2}초 · 강공 0.05초 뒤 약한 흔들림: {mixed:F2}초 " +
                          (!live ? "⚠ 무효(흔들림이 줄지 않음 — 카메라 비활성)" : Mathf.Abs(mixed - solo) < 0.05f ? "✅ 같다(약한 쪽이 묻힘)" : "❌ 잘렸다"));

            // ── B. 주는 쪽 단계 ────────────────────────────────────
            sb.AppendLine("B. 주는 쪽 단계 (대상에 한 방씩)");
            // 전투방 몬스터는 등장 중 무적이고 체력이 낮다 — 무적이 풀리길 기다리고 체력을 크게 잡는다(인형은 원래 불사).
            if (dummy != null && dummy is not TrainingDummyMonster)
            {
                float w0 = Time.realtimeSinceStartup;
                while (dummy.IsDamageImmuneNow && Time.realtimeSinceStartup - w0 < 8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                var rt = typeof(MonsterBase).GetField("_runtime", Instance)?.GetValue(dummy) as MonsterRuntimeData;
                if (rt != null) rt.CurrentHp = 50000;
            }
            if (dummy == null) sb.AppendLine("  ⚠ 대상(훈련 인형·몬스터)이 없다 — 건너뜀");
            else
            {
                sb.AppendLine($"  대상: {dummy.name}");
                await TierHitAsync(sb, player, dummy, Trauma, WeaponActionType.GroundLight, false, "기본 공격", HitFeelService.DealtShakeBasic, ct);
                await TierHitAsync(sb, player, dummy, Trauma, WeaponActionType.ESkill,      false, "스킬",     HitFeelService.DealtShakeSkill, ct);
                await TierHitAsync(sb, player, dummy, Trauma, WeaponActionType.RSkill,      true,  "스킬 막타", HitFeelService.DealtShakeFinisher, ct);
            }

            // ── C. 막타 신호 가시성 ─────────────────────────────────
            sb.AppendLine("C. 막타 신호가 화면에 보이는가 (HUD 아래 가장자리 금빛)");
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.Realtime, cancellationToken: ct);
            var before = await CaptureAsync(player, ct);
            FinisherEdgeService.Pulse();
            await UniTask.Delay(TimeSpan.FromSeconds(0.05f), DelayType.Realtime, cancellationToken: ct);
            float alphaAtShot = FinisherEdgeService.CurrentAlpha;
            var after = await CaptureAsync(player, ct);
            if (before == null || after == null) sb.AppendLine("  ⚠ 캡처 실패");
            else
            {
                System.IO.File.WriteAllBytes("Temp/player_feel_before.png", before.EncodeToPNG());
                System.IO.File.WriteAllBytes("Temp/player_feel_after.png",  after.EncodeToPNG());
                sb.AppendLine($"  찍힌 순간 신호 불투명도 {alphaAtShot:F3}");
                AppendPatch(sb, "왼쪽 위 모서리",  before, after, 0.00f, 0.94f);
                AppendPatch(sb, "오른쪽 위 모서리", before, after, 0.94f, 0.94f);
                AppendPatch(sb, "왼쪽 아래 모서리", before, after, 0.00f, 0.00f);
                AppendPatch(sb, "오른쪽 아래 모서리", before, after, 0.94f, 0.00f);
                AppendPatch(sb, "왼쪽 가장자리 가운데", before, after, 0.00f, 0.47f);
                AppendPatch(sb, "화면 가운데",      before, after, 0.47f, 0.47f);
                UnityEngine.Object.Destroy(before);
                UnityEngine.Object.Destroy(after);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }

        string text = sb.ToString();
        ProbeOutput.Write(OutPath, Title, text);
        Debug.Log("[플레이어연출] 결과\n" + text);
    }

    /// <summary>강공 흔들림을 걸고(선택적으로 0.05초 뒤 약한 흔들림) 세기가 0.02 아래로 떨어질 때까지 걸린 실시간.</summary>
    private static async UniTask<float> MeasureShakeAsync(Func<float> trauma, bool withWeakAfter, CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f), DelayType.Realtime, cancellationToken: ct);   // 이전 흔들림 정리
        float t0 = Time.realtimeSinceStartup;
        HitFeelService.CameraShake(0.14f, 0.45f);
        bool weakSent = !withWeakAfter;
        while (Time.realtimeSinceStartup - t0 < 1.5f)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            float el = Time.realtimeSinceStartup - t0;
            if (!weakSent && el >= 0.05f) { HitFeelService.CameraShake(0.022f, 0.10f); weakSent = true; }
            if (el > 0.06f && trauma() < 0.02f) return el;
        }
        return 1.5f;
    }

    private static async UniTask TierHitAsync(StringBuilder sb, PlayerController player, MonsterBase dummy,
                                              Func<float> trauma, WeaponActionType action, bool finisher,
                                              string label, float cap, CancellationToken ct)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f), DelayType.Realtime, cancellationToken: ct);
        DealtHitTier seenTier = DealtHitTier.Basic;
        bool seenCrit = false;
        void OnBurst(HitBurst b) { seenTier = b.Tier; seenCrit = b.AnyCritical; }
        GlobalFeelCoalescer.OnBurst += OnBurst;
        try
        {
            CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = dummy.gameObject,
                BaseDamage          = 10f,
                Owner               = player.gameObject,
                ActionType          = action,
                IsFinisher          = finisher,
                KnockbackMultiplier = 0f,
                HitPoint            = dummy.transform.position + Vector3.up,
                SourcePosition      = player.transform.position,
                SkipHitVfx          = true,
            });
            float peakTrauma = 0f, peakEdge = 0f;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.25f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                peakTrauma = Mathf.Max(peakTrauma, trauma());
                peakEdge   = Mathf.Max(peakEdge, FinisherEdgeService.CurrentAlpha);
            }
            float amp = peakTrauma * AmpPerTrauma;
            bool ampOk  = finisher ? Mathf.Abs(amp - cap) < 0.002f : amp <= cap + 0.0005f;
            bool edgeOk = finisher ? peakEdge > 0.05f : peakEdge < 0.001f;
            sb.AppendLine($"  {label,-6} 단계 {seenTier}{(seenCrit ? "(치명)" : "")} · 흔들림 진폭 {amp:F4} (상한 {cap:F3}) {(ampOk ? "✅" : "❌")}" +
                          $" · 가장자리 신호 {peakEdge:F3} {(edgeOk ? "✅" : "❌")}");
        }
        finally { GlobalFeelCoalescer.OnBurst -= OnBurst; }
    }

    private static async UniTask<Texture2D> CaptureAsync(MonoBehaviour runner, CancellationToken ct)
    {
        await UniTask.WaitForEndOfFrame(runner, ct);
        return ScreenCapture.CaptureScreenshotAsTexture();
    }

    /// <summary>화면 비율 좌표(x0,y0)에서 가로·세로 6% 조각의 평균색 변화.</summary>
    private static void AppendPatch(StringBuilder sb, string name, Texture2D a, Texture2D b, float x0, float y0)
    {
        Color ca = Mean(a, x0, y0), cb = Mean(b, x0, y0);
        sb.AppendLine($"  {name,-10} 전 ({ca.r * 255:F0},{ca.g * 255:F0},{ca.b * 255:F0}) → 후 ({cb.r * 255:F0},{cb.g * 255:F0},{cb.b * 255:F0})" +
                      $"  변화 R{(cb.r - ca.r) * 255:+0;-0} G{(cb.g - ca.g) * 255:+0;-0} B{(cb.b - ca.b) * 255:+0;-0}");
    }

    private static Color Mean(Texture2D t, float x0, float y0)
    {
        int w = Mathf.Max(1, (int)(t.width * 0.06f)), h = Mathf.Max(1, (int)(t.height * 0.06f));
        int sx = (int)(t.width * x0), sy = (int)(t.height * y0);
        Color sum = Color.clear; int n = 0;
        for (int y = sy; y < Mathf.Min(t.height, sy + h); y += 2)
        for (int x = sx; x < Mathf.Min(t.width, sx + w); x += 2) { sum += t.GetPixel(x, y); n++; }
        return n > 0 ? sum / n : Color.clear;
    }
}
