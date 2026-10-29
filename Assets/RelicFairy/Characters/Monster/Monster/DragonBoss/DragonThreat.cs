using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 화룡 위협 공용 규칙(10-14 사용자 「챕터2 보스 패턴이 위협적이지 않다 — 메테오 양 · 크기 · 범위 불규칙 · 플레이어를 노리는 메테오 · 돌진 · 근접도 위협적으로」).
///   · 플레이어 예측 — 지금 속도(평면 · 6 m/s 상한)로 몇 초 뒤 자리. 예고는 그 자리에 그리고 그때 굳힌다(예고 = 판정 · 멈추거나 꺾으면 피한다).
///   · 메테오 모양 — 반경 r칸 원, 바깥 테두리 칸 일부를 빼서 매번 모양이 다르다. 크기 셋(1 · 2 · 3) + 노림(2) · 큰 메테오(4).
///   · 피해 — 기본 한 방(공격력 × 패턴 배율, 지금 390)이 플레이어 최대 HP의 41~60%라 피해를 올리지 않고 맞을 확률로 위협을 키운다.
///     크기별 0.8 ~ 1.4배(큰 메테오가 상한 — 한 방에 죽지 않게).
/// </summary>
public static class DragonThreat
{
    public const float MaxPlayerSpeed = 6f;

    // 메테오 크기 — 반경(칸) · 뽑을 몫 · 피해 배율
    public static readonly int[]   ScatterRadius = { 1, 2, 3 };
    public static readonly float[] ScatterWeight = { 0.55f, 0.35f, 0.10f };
    public static readonly float[] ScatterDamage = { 0.8f, 1.0f, 1.2f };
    public const int   AimedRadius = 2;
    public const float AimedDamage = 1.0f;
    public const int   BigRadius   = 4;
    public const float BigDamage   = 1.4f;
    // 10-23 B4 「메테오가 위협적이지 않다」 — 노림 첫 발이 0.35초 앞이라 걷던 그대로 지나가면 착탄(예고 0.6 + 낙하 0.6초) 때 이미 원 밖이었다.
    // 0.85초 앞 = 곧장 걸으면 착탄 때 원 안(6 m/s · 반폭 2.5 m) · 멈추거나 꺾으면 피한다(예고 = 판정 · 앞섬 1.2초 그대로).
    public const float AimedLead   = 0.85f;
    public const float EdgeDrop    = 0.3f;    // 바깥 테두리 칸을 뺄 확률
    public const float MeteorReachAtOne = 4.8f; // 운석 이펙트(Meteor.prefab) 배율 1의 땅 효과 보이는 반경(충격파까지 · 10-14 실측 2.14 / 4.81 m) — 10-23부터 멀리 튀는 층만 이 값으로
    // 10-23 B4 사용자 「터지는 이펙트가 잘 느껴지게 · 범위에 맞게 크기」 — 최대 도달(4.8)로 맞추니 몸통(분화구 · 충격파 · 불 · 섬광)이 예고의 0.42~0.46배였다
    // (10-15 fx_fit ×0.31: 90% 반경 0.63~0.69 m · 판정 1.5 m). 몸통은 90% 반경(배율 1당 2.03~2.23 m) = 판정 반폭, 멀리 튀는 층은 예전처럼 최대 도달 = 판정 반폭.
    public const float MeteorCoreReachAtOne = 2.2f;
    private const float DebrisRatio = MeteorCoreReachAtOne / MeteorReachAtOne;   // 멀리 튀는 층 = 몸통 배율 × 0.46(예전 배율 그대로)
    private static readonly HashSet<string> DebrisParts = new() { "Fragments", "Sparks", "Smoke" };   // 빠른 파편(15~35 m/s) · 불티(5~14 m/s) · 퍼지는 연기
    // 10-23 B4 2차(fx_fit 사진): 보이는 불(Fire · FireMiddle — 크기 1.5)이 판정 지름의 1/3 남짓이라 터짐이 작았다 → 몸통 배율 × 1.6(보이는 반경 ≈ 판정 × 1.1~1.2 · × 2는 1.3~1.5로 넘쳤다 — 10-23 실측).
    private static readonly HashSet<string> BurstParts = new() { "Fire", "FireMiddle" };
    private const float BurstBoost = 1.6f;
    // 충격파 고리(ShockWave2 · 배율 1 반경 3 m) — 끝이 판정 반폭에 닿게(맞는 자리 테두리) = 몸통 배율 × 2.2 ÷ 3.
    private const string RingPart  = "ShockWave2";
    private const float  RingRatio = MeteorCoreReachAtOne / 3f;

    // 10-19 사용자 「맨 처음 쓰던 메테오 리소스가 좋았다 — 키워서 더 높은 곳에서 떨어져 웅장하게」(10-18 몸통/착탄 분리는 걷음):
    // Meteor.prefab 하나 — 떨어지는 불덩이(뿌리 입자)와 꼬리는 크게(배율만큼 더 높은 곳에서 · 0.4초 그대로),
    // 땅 효과 자식만 판정 크기(MeteorScale)로 되돌려 「예고 = 판정」을 지킨다.
    public const float BodyScale    = 1.6f;   // 흩뿌림 불덩이(예전 판정 크기 0.31~0.94)
    public const float BigBodyScale = 2.4f;   // 노림 · 큰 메테오
    private static readonly HashSet<string> TrailParts = new() { "SubFire", "SubSmoke", "SubSparks" };   // 뿌리(불덩이)의 꼬리 — 땅 효과가 아니다
    private const float SkyPartY      = 1f;     // 로컬 높이가 이보다 높은 자식 = 하늘 부분(출발 섬광 Glow · Flare — 출발점 (0, 7, −4))
    private const float NativeFallTime = 0.4f;  // Meteor.prefab 불덩이 입자 수명(배율과 무관)
    private static readonly List<ParticleSystem> s_meteorPs = new();

    /// <summary>플레이어 평면 속도(m/s, 6 상한) — 몸의 Rigidbody 기준. 없으면 0.</summary>
    public static Vector3 PlayerVelocity(MonsterContext ctx)
    {
        var t = ctx?.Runtime?.PlayerTarget;
        if (t == null || !t.TryGetComponent<Rigidbody>(out var rb)) return Vector3.zero;
        Vector3 v = rb.linearVelocity; v.y = 0f;
        return v.sqrMagnitude > MaxPlayerSpeed * MaxPlayerSpeed ? v.normalized * MaxPlayerSpeed : v;
    }

    /// <summary>플레이어의 <paramref name="seconds"/>초 뒤 자리(지금 속도 그대로) — 높이는 지금 그대로.</summary>
    public static Vector3 PredictPlayer(MonsterContext ctx, float seconds)
    {
        var t = ctx?.Runtime?.PlayerTarget;
        if (t == null) return ctx != null ? ctx.Transform.position : Vector3.zero;
        return t.position + PlayerVelocity(ctx) * Mathf.Max(0f, seconds);
    }

    /// <summary>흩뿌림 크기 뽑기 — 반경(칸)과 몫 순번(피해 배율 칸).</summary>
    public static int PickScatterRadius(out int tier)
    {
        float v = Random.value;
        for (tier = 0; tier < ScatterRadius.Length - 1; tier++)
        {
            if (v < ScatterWeight[tier]) break;
            v -= ScatterWeight[tier];
        }
        return ScatterRadius[tier];
    }

    /// <summary>반경 r칸 원 — 바깥 테두리 칸을 <paramref name="drop"/> 확률로 뺀다. 방 안쪽 칸만.</summary>
    public static List<Vector2Int> Footprint(Vector2Int c, int r, float drop)
    {
        var cells = new List<Vector2Int>((2 * r + 1) * (2 * r + 1));
        float outer = (r + 0.4f) * (r + 0.4f), inner = (r - 0.6f) * (r - 0.6f);
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 > outer) continue;
            if (d2 > inner && d2 > 0 && Random.value < drop) continue;
            int x = c.x + dx, z = c.y + dz;
            if (DragonBossRoomContext.IsInterior(x, z)) cells.Add(new Vector2Int(x, z));
        }
        return cells;
    }

    /// <summary>운석 땅 효과 몸통 배율 — 90% 반경이 판정 반폭(r + 0.5칸)에 맞게(반경 1칸 0.68 · 2칸 1.14 · 3칸 1.59 · 4칸 2.05 · 10-23 B4).
    /// 멀리 튀는 층(파편 · 불티 · 연기)은 <see cref="SpawnMeteor"/>가 × <see cref="DebrisRatio"/>(예전 배율 0.31 ~ 0.94 그대로).</summary>
    public static float MeteorScale(int r) => (r + 0.5f) * DragonBossRoomContext.CellSize / MeteorCoreReachAtOne;

    /// <summary>
    /// 운석 하나를 착탄 자리에 띄운다 — 불덩이(뿌리 입자)와 꼬리는 <paramref name="bodyScale"/>(클수록 더 높은 곳에서 떨어진다),
    /// 땅 효과 자식(분화구 · 충격파 · 불 · 섬광)은 <paramref name="groundScale"/>(판정 크기 · 90% 반경), 멀리 튀는 파편 · 불티 · 연기는 그 × <see cref="DebrisRatio"/>.
    /// <paramref name="uniform"/> = 한 배율 그대로(공중 물기 화염구 — 운석이 바꿔 둔 자식 배율만 되돌린다). 풀 인스턴스라 매번 다시 맞춘다.
    /// </summary>
    public static GameObject SpawnMeteor(GameObject prefab, Vector3 land, float bodyScale, float groundScale,
                                         float fallSeconds = NativeFallTime, bool fromCameraFar = true, bool uniform = false)
    {
        if (prefab == null) return null;
        // 출발점(로컬 −Z 위)이 카메라 먼 쪽 = 화면 위에서 들어오게(identity면 늘 월드 −Z에서 왔다 · 10-19 01 조사)
        Quaternion rot = Quaternion.identity;
        var cam = fromCameraFar ? Camera.main : null;
        if (cam != null) { Vector3 f = cam.transform.forward; f.y = 0f; if (f.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(-f.normalized); }
        var go = BossEffectPool.Spawn(prefab, land, rot);
        if (go == null) return null;
        go.transform.localScale = Vector3.one * bodyScale;
        float k = groundScale / Mathf.Max(0.01f, bodyScale);
        foreach (Transform c in go.transform)
            c.localScale = uniform || TrailParts.Contains(c.name) || c.localPosition.y > SkyPartY ? Vector3.one
                         : Vector3.one * (DebrisParts.Contains(c.name) ? k * DebrisRatio
                                        : BurstParts.Contains(c.name) ? k * BurstBoost
                                        : c.name == RingPart ? k * RingRatio : k);
        // 보이는 착탄 = 피해 순간 — 불덩이 수명 0.4초를 낙하 시간(ImpactDelay)에 맞춰 재생 속도를 늦춘다(땅 효과도 그만큼 무겁게)
        float speed = NativeFallTime / Mathf.Max(NativeFallTime, fallSeconds);
        go.GetComponentsInChildren(true, s_meteorPs);
        foreach (var ps in s_meteorPs) { var m = ps.main; m.simulationSpeed = speed; }
        s_meteorPs.Clear();
        // 굴절층(HS_Distortion) 끔 — 화염구 비만 껐고 수동 운석 · 휩쓸기 운석은 켜진 채라 버스트마다 화면이 일그러졌다(10-11 이펙트 톤 2차 · 10-23 B4 한곳으로)
        BossPageTransitionState.ToneRoarVfx(go, Color.clear);
        return go;
    }

    // 운석끼리 겹쳐 같은 순간 여러 번 맞으면(맞은 뒤 무적이 없다) 2초에 1994 — 최대 HP를 넘었다(10-14 실측). 운석 피해는 이만큼 사이를 둔다
    public const float MeteorHitGap = 0.6f;
    private static float s_meteorHitAt = -99f;

    /// <summary>운석 한 대를 넣어도 되는가 — 직전 운석 피해에서 <see cref="MeteorHitGap"/>초가 지났으면 true(그리고 지금을 적는다).</summary>
    public static bool TryMeteorHit()
    {
        if (Time.time - s_meteorHitAt < MeteorHitGap) return false;
        s_meteorHitAt = Time.time;
        return true;
    }

    // ── 착탄 소리(10-23 B4 사용자 「메테오 낙하 충격 사운드 — 과하지 않게 조금씩」) ──
    // 버스트는 0.09초마다 한 발 — 다 내면 소리가 뭉개진다. 0.18초에 한 번(노림 · 큰 운석은 간격을 안 기다린다) · 피치 0.9~1.1(큰 것 0.8~0.88) ·
    // 플레이어에서 4 m 안 그대로 → 28 m 0(선형). 리스너(카메라)가 높이 떠 있어 「플레이어 ↔ 착탄」 거리 · 방향 그대로 리스너 둘레에 놓는다.
    public const float ImpactSfxGap   = 0.18f;
    private const float ImpactSfxNear = 4f;
    private const float ImpactSfxFar  = 28f;
    private static float s_impactSfxAt = -99f;

    /// <summary>운석 착탄 소리 — <paramref name="halfWidth"/> = 판정 반폭(m, 1.5 ~ 4.5) · <paramref name="heavy"/> = 노림 · 큰 운석(간격을 안 기다린다).</summary>
    public static void PlayImpactSfx(AudioClip clip, float volume, Vector3 land, Transform player, float halfWidth, bool heavy)
    {
        if (clip == null || volume <= 0f) return;
        if (!heavy && Time.time - s_impactSfxAt < ImpactSfxGap) return;
        Vector3 rel = player != null ? land - player.position : Vector3.zero;
        rel.y = 0f;
        if (rel.sqrMagnitude > ImpactSfxFar * ImpactSfxFar) return;   // 안 들릴 만큼 먼 것은 간격을 쓰지 않는다
        s_impactSfxAt = Time.time;
        bool  big   = halfWidth >= BigRadius + 0.5f;
        float vol   = volume * Mathf.Lerp(0.8f, 1.15f, Mathf.InverseLerp(1.5f, 4.5f, halfWidth));
        float pitch = big ? Random.Range(0.8f, 0.88f) : Random.Range(0.9f, 1.1f);
        var   cam   = Camera.main;
        Vector3 at  = cam != null && player != null ? cam.transform.position + rel : land;
        Managers.Sound?.PlayEffectAt(clip, at, vol, pitch, ImpactSfxNear, ImpactSfxFar, AudioRolloffMode.Linear);
    }

    // ── 떨어지기 시작 소리(10-23 B4) — FireRain.mp3(2.9초)가 운석마다(버스트 초당 최대 11회) 간격 없이 겹쳐 새 착탄 소리를 덮었다 →
    //    0.6초에 한 번 · 볼륨 0.6(예전 1). 수동 운석 · 화염구 비 · 휩쓸기 운석이 한 시계를 함께 쓴다.
    public const float LaunchSfxGap    = 0.6f;
    public const float LaunchSfxVolume = 0.6f;
    private static float s_launchSfxAt = -99f;

    /// <summary>운석이 떨어지기 시작하는 소리 — <see cref="LaunchSfxGap"/>초에 한 번만(나머지는 건너뛴다) · 착탄 자리에서(예전과 같은 3D 기본 거리).</summary>
    public static void PlayLaunchSfx(AudioClip clip, Vector3 land)
    {
        if (clip == null || Time.time - s_launchSfxAt < LaunchSfxGap) return;
        s_launchSfxAt = Time.time;
        Managers.Sound?.PlayEffectAt(clip, land, LaunchSfxVolume);
    }

    // ── 메테오 바닥 예고 알파(10-26 사용자 「카메라가 확장되었을 때 맵에 무작위로 떨어지는 메테오의 판정 가이드라인이 안 보인다」) ──
    // 예전엔 예고 내내 0 → 목표로 곧게 올라 흩뿌림(1.0초)은 0.3초에 0.15 — 붉은 바닥에서 앞 몇백 ms가 안 보여 앞섬을 깎았다.
    // 처음부터 목표의 60% · 예고 35% 지점에 다 찬다. 공중 선회 탑다운(높이 약 40 m · FOV 78)에선 1 m 칸이 화면 20 px 안팎이고
    // 바닥이 안개색 45~55%로 붉어져(10-26 실측 · 칸/바닥 차이 0.3~0.5) × 1.5(0.85 상한). 흩뿌림 · 화염구 비 · 휩쓸기 운석이 함께 쓴다.
    private const float WarnStartShare   = 0.6f;
    private const float WarnRampShare    = 0.35f;
    private const float TopDownWarnBoost = 1.5f;
    private const float TopDownWarnMax   = 0.85f;

    /// <summary>메테오 바닥 예고 칸 알파 — <paramref name="target"/> = 다 찬 알파 · <paramref name="t"/> = 예고 진행(0 ~ 1).</summary>
    public static float WarnAlpha(float target, float t)
    {
        float a = target * Mathf.Lerp(WarnStartShare, 1f, Mathf.Clamp01(t / WarnRampShare));
        var cam = GameCameraController.Instance;
        return cam != null && cam.IsDragonTopDownActive ? Mathf.Max(a, Mathf.Min(a * TopDownWarnBoost, TopDownWarnMax)) : a;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_meteorHitAt = -99f; s_impactSfxAt = -99f; s_launchSfxAt = -99f; }

    public static Vector2Int ClampInterior(Vector2Int c)
        => new(Mathf.Clamp(c.x, 1, DragonBossRoomContext.Width - 2), Mathf.Clamp(c.y, 1, DragonBossRoomContext.Height - 2));
}
}
