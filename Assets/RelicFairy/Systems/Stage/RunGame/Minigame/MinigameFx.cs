using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 이벤트방 놀이의 피드백 공통 부품(10-01 설계 §2-2) — 소리(있는 클립을 음높이로 나눠 쓴다) · 화면 빛 · 비치명 피해 ·
/// 결말 슬로모 · 예고 장판 재질 · 고리 선. 어떤 놀이에서 불렸는지 모른다.
/// </summary>
public static class MinigameFx
{
    // ── Constants ──────────────────────────────────────────────
    public const float HurtRatio        = 0.04f;   // 맞으면 최대 체력의 4% — 이 피해로는 죽지 않는다(체력 1에서 멈춤)
    private const float FinaleSlowScale  = 0.35f;
    private const float FinaleSlowSecs   = 0.5f;
    private const float EdgePeak         = 0.12f;   // 화면 빛 상한 — 0.45 이상이면 화면이 지워진다(09-26)
    private const int   RingSegments     = 72;

    public static readonly Color HurtColor = new Color(0.85f, 0.18f, 0.15f);

    private static readonly object s_slowOwner = new object();
    private static Material s_prevCircle, s_prevArrow;
    private static int      s_guideDepth;
    private static Material s_lineMat;

    // ── 소리 ───────────────────────────────────────────────────

    public static void Sound(string key, float volume, float pitch)
        => Managers.Sound?.PlayUiAsync(key, volume, pitch).Forget();

    /// <summary>틱 — 카운트다운 · 마지막 5초 · 간발.</summary>
    public static void Tick(float pitch = 1f) => Sound(SoundKey.Sfx.UiButton, 0.6f, pitch);

    /// <summary>성공음 — 연속 성공이면 한 단계씩 높아진다(최대 8단계).</summary>
    public static void Success(int streak) => Sound(SoundKey.Sfx.ItemPickup, 0.7f, 1f + 0.06f * Mathf.Clamp(streak, 0, 8));

    /// <summary>실패 · 틀림 — 낮은 불협음.</summary>
    public static void Fail() => Sound(SoundKey.Sfx.PlayerHit, 0.7f, 0.6f);

    // ── 화면 ───────────────────────────────────────────────────

    /// <summary>화면 빛 한 번(약하게) — 입장 · 맞음 · 경고.</summary>
    public static void Flash(Color color, float seconds = 0.25f, float peak = EdgePeak)
        => LichCinematics.Flash(color, seconds, Mathf.Min(peak, EdgePeak));

    /// <summary>
    /// 결말 — 마지막 순간 0.5초(실시간) 느려진다. 시간 배율은 중재기로만 잡고, 어느 길로 빠져도 놓는다.
    /// </summary>
    public static async UniTask FinaleSlowAsync(CancellationToken ct)
    {
        TimeScaleArbiter.Acquire(s_slowOwner, FinaleSlowScale, TimeScaleArbiter.Priority.SlowMotion);
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(FinaleSlowSecs), DelayType.Realtime, cancellationToken: ct);
        }
        finally
        {
            TimeScaleArbiter.Release(s_slowOwner);
        }
    }

    // ── 피해 ───────────────────────────────────────────────────

    /// <summary>
    /// 놀이 피해 — 최대 체력의 4%, 이 피해로는 죽지 않는다(체력 1에서 멈춤). 실제로 들어갔으면 true
    /// (회피 무적 · 저스트 회피 · 막기로 무효면 false — 놀이는 「맞음」으로 세지 않는다).
    /// </summary>
    public static bool Hurt(PlayerController player)
    {
        if (player == null) return false;
        var stats = player.RuntimeStats;
        int dmg = Mathf.Min(Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * HurtRatio)), stats.Hp - 1);
        if (dmg <= 0 || player.IsInvincible) return false;

        bool applied = false;
        void Capture(GameObject _, int __, int ___, PlayerController.DamageOutcome o)
            => applied = o == PlayerController.DamageOutcome.Applied;

        player.OnDamageResolved += Capture;
        try     { player.TakeDamage(dmg, null, false, HitWeight.Light); }
        finally { player.OnDamageResolved -= Capture; }

        if (applied)
        {
            Flash(HurtColor, 0.25f);
            Sound(SoundKey.Sfx.PlayerHit, 0.8f, 0.85f);
        }
        return applied;
    }

    // ── 예고 장판 ──────────────────────────────────────────────

    /// <summary>
    /// 놀이 동안 예고 장판을 데칼(원 · 화살표 재질)로 그린다 — 재질은 런 공용 목록에서. 끝나면 <see cref="EndGuides"/>로 전의 재질로 돌린다
    /// (보스가 쓰는 재질 상태를 바꿔 두지 않게).
    /// </summary>
    public static void BeginGuides()
    {
        if (s_guideDepth++ > 0) return;
        s_prevCircle = PatternGuideHelper.CircleMaterial;
        s_prevArrow  = PatternGuideHelper.ArrowMaterial;
        if (RunFx.GuideCircle != null)
            PatternGuideHelper.SetMaterials(RunFx.GuideCircle, RunFx.GuideArrow);
    }

    public static void EndGuides()
    {
        if (s_guideDepth <= 0 || --s_guideDepth > 0) return;
        PatternGuideHelper.SetMaterials(s_prevCircle, s_prevArrow);
        s_prevCircle = s_prevArrow = null;
    }

    // ── 고리 선(조이는 고리 · 파동) ────────────────────────────

    /// <summary>바닥 고리 선 하나. <see cref="SetRing"/>으로 반경 · 틈을 바꾼다.</summary>
    public static LineRenderer CreateRing(Color color, float width)
    {
        var go = new GameObject("MinigameRing");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop          = false;
        lr.positionCount = 0;
        lr.widthMultiplier = width;
        lr.numCapVertices  = 2;
        lr.sharedMaterial  = LineMaterial();
        lr.startColor = lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        return lr;
    }

    /// <summary>고리를 다시 그린다 — 틈(<paramref name="gapDeg"/>도, 방위 <paramref name="gapYaw"/>)만 비운다. 틈 0이면 닫힌 고리.</summary>
    public static void SetRing(LineRenderer lr, Vector3 center, float radius, float gapYaw, float gapDeg)
    {
        if (lr == null) return;
        float span  = 360f - Mathf.Clamp(gapDeg, 0f, 359f);
        float start = gapYaw + gapDeg * 0.5f;
        int   n     = Mathf.Max(8, Mathf.RoundToInt(RingSegments * span / 360f));
        lr.positionCount = n + 1;
        for (int i = 0; i <= n; i++)
        {
            float yaw = start + span * i / n;
            lr.SetPosition(i, center + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * radius + Vector3.up * 0.25f);
        }
    }

    private static Material LineMaterial()
    {
        if (s_lineMat == null)
        {
            var shader = Shader.Find("Sprites/Default");
            s_lineMat = new Material(shader) { name = "MinigameRing (Runtime)" };
        }
        return s_lineMat;
    }

    // ── 방위 ───────────────────────────────────────────────────

    /// <summary>+Z 기준 시계 방향 방위(도).</summary>
    public static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
}
