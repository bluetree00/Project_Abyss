using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 타격 등급 — 연출·UX 시나리오 §3 U4. 흔들림은 강·전환만(「이동 중 카메라 고정」 정책), 히트스톱은 플레이어가 맞았을 때만.
/// </summary>
public enum LichImpact
{
    Light,       // 마력탄 착탄 · 비 · 사슬 링
    Medium,      // 구체 · 쌍둥이 폭발 · 낫 베기
    Heavy,       // 낙하 강타 · 광선 시작 · 결계 파괴
    Slash,       // 낫 접촉 — 빗나가도 짧게 흔들린다(휘두름의 무게). 맞으면 히트스톱
    Transition,  // 페이지 전환 · 붕괴 · 사망
}

/// <summary>
/// 리치 패턴 공용 도구 — 판정(원·직선)·바닥 높이·예고 색·시선·타격 등급·예고 진행.
/// 판정은 수평 거리로 본다(리치 전장은 평평하고, 플레이어 피벗 높이 차로 판정이 어긋나지 않게).
/// </summary>
public static class LichPatternUtil
{
    /// <summary>
    /// 마법 예고 — 「곧 마법이 떨어진다」. (10-23 가이드 색 통일) 보라 → 공용 공격 색(<see cref="PatternGuideHelper.AttackFill"/>).
    /// 이름은 예고 호출 자리를 그대로 두려고 남겼다 — 보라 이펙트 색은 <see cref="ArcaneVfx"/>.
    /// </summary>
    public static Color Arcane => PatternGuideHelper.AttackFill;
    /// <summary>낫 예고 — 「베기가 온다」. 판정 순간엔 <see cref="Lethal"/>. (10-23 가이드 색 통일) 진홍 → 마법과 같은 공용 공격 색 — 진홍 이펙트 색은 <see cref="CrimsonVfx"/>.</summary>
    public static Color Crimson => PatternGuideHelper.AttackFill;
    /// <summary>보라 — 마법 이펙트 틴트만(옛 마법 예고 색 · 10-23). 바닥 예고에 쓰지 않는다.</summary>
    public static readonly Color ArcaneVfx = new Color(0.62f, 0.25f, 1.00f);
    /// <summary>진홍 — 낫 베기 이펙트 틴트만(옛 낫 예고 색 · 10-23). 바닥 예고에 쓰지 않는다.</summary>
    public static readonly Color CrimsonVfx = new Color(0.75f, 0.08f, 0.22f);
    /// <summary>안전(흰색).</summary>
    public static readonly Color SafeWhite = new Color(0.95f, 0.95f, 0.90f);
    /// <summary>판정 순간(빨강) — 「지금 피하라」.</summary>
    public static Color Lethal => PatternGuideHelper.Active;
    /// <summary>예고의 자리 · 방향이 굳은 뒤 판정까지 최소(공용 예고 규칙 · 10-13).</summary>
    public const float MinLead = PatternGuideHelper.MinLead;

    /// <summary>판정 직전 신호(S) 구간 밝기 배율.</summary>
    private const float SignalIntensity = 2f;

    /// <summary>패링 창 길이(초) — 낫 판정 직전. 판정 전 이만큼 먼저 <see cref="OpenParry"/>를 부른다.</summary>
    public const float ParryWindow = 0.25f;

    /// <summary>사슬 둘째 칸 피해 배율(10-22 E2 L1) — 한 사슬을 다 맞아도 HP 절반을 넘지 않게. 동시 연계(비 · 복제 동안)의 덧공격에도 쓴다.</summary>
    public const float ChainLinkDamage = 0.8f;

    // 원 깔기(PackDiscs · 10-22 E5 페이지2) — 격자점을 겨눈 자리에서 가까운 순으로(정렬 비교는 한 번만 만든다)
    private static readonly List<Vector3> s_lattice = new(64);
    private static Vector3 s_latticeOrigin;
    private static readonly System.Comparison<Vector3> s_byLatticeDistance =
        (a, b) => FlatDistance(a, s_latticeOrigin).CompareTo(FlatDistance(b, s_latticeOrigin));

    /// <summary>봉인 완성 이펙트(Effect_38 GloryBoundary)에서 끄는 자식 — 연기 장(흰 파편 구름) · 긴 빛기둥(늘인 입자 길이 25) · 빛가루 비 둘(초당 1000 · 500).</summary>
    private static readonly string[] SealCompleteHeavyParts =
        { "Effect_38_SmokeField", "Effect_38_ParticlesField_3", "Effect_38_ParticlesField", "Effect_38_ParticlesField_2" };

    private static readonly List<Vector2Int> s_cells = new(32);

    // ── 타격 등급 ─────────────────────────────────────────────
    // HitFeelService 진폭 → 트라우마 = 진폭 / 0.18 (0.18이면 최대).
    private const float SlashShakeAmp       = 0.05f;
    private const float SlashShakeSeconds   = 0.18f;
    private const float HeavyShakeAmp       = 0.14f;
    private const float HeavyShakeSeconds   = 0.45f;
    private const float TransitionShakeAmp  = 0.18f;
    private const float TransitionShakeSecs = 0.6f;

    /// <summary>
    /// 타격 연출 한 번 — 흔들림(강·전환) · 볼륨 펄스(강·전환) · 히트스톱(<paramref name="hitPlayer"/>일 때).
    /// Cinemachine 확장 경로(<see cref="HitFeelService"/>)라 카메라 직접 조작과 달리 실제로 보인다.
    /// </summary>
    // ── 낫 베기 이펙트 ────────────────────────────────────────
    /// <summary>
    /// Sword Slash 4 주 호의 외곽 반경(배율 1) — 생성 2.3 m → 끝 3.75 m로 퍼진다(09-19 감사).
    /// 가운데 값으로 나눠 호가 판정 가장자리 안쪽에서 시작해 살짝 넘어서며 사라지게 한다.
    /// </summary>
    public const float SlashNativeRadius = 3.4f;

    /// <summary>
    /// 노바(SlamImpact = Hit 17 nova) 호출 배율 — 보이는 반경이 <paramref name="radius"/>가 되게.
    /// 체감 반경 ≈ 1.2 m × 매핑 배율(2.0) × 호출 배율(09-19 Boss Room/11 실측 + 감사).
    /// </summary>
    public static float NovaScale(float radius) => radius / 2.4f;

    /// <summary>
    /// 쌍둥이 폭발(TwinBlast) 호출 배율 — SlamImpact와 같은 노바 프리팹이지만 매핑 배율이 ×2.5라 1.2 m × 2.5 × r/3 = r(10-08 점검).
    /// </summary>
    public static float TwinBlastScale(float radius) => radius / 3f;

    /// <summary>보라탄 착탄(BoltImpact) 호출 배율 — 퍼지는 고리 평균 반경이 <paramref name="radius"/>가 되게(감사: r/2는 1~2배로 컸다).</summary>
    public static float BoltScale(float radius) => radius / 3f;

    /// <summary>베기 이펙트 높이(바닥 위 m) — 바닥 예고와 겹쳐 보이게. 높으면 카메라 쪽으로 밀려 보인다.</summary>
    public const float SlashHeight = 0.3f;

    /// <summary>
    /// 좌우를 뒤집어 그리는 휘두름 — 이펙트가 쓸고 가는 방향을 동작과 맞춘다.
    /// 09-19 실측(Boss Room/18, 위에서 슬로모): Sword Slash 4 원본은 밝은 앞머리가 왼쪽 = 오른쪽→왼쪽으로 쓸어 보인다.
    /// 그래서 왼쪽→오른쪽 휘두름을 뒤집는다.
    /// </summary>
    private const LichSwing SlashMirroredSwing = LichSwing.LeftToRight;

    /// <summary>
    /// 낫 베기 한 번 — 휘두름 방향에 맞춘 이펙트를 판정 크기(<paramref name="radius"/>)로 바닥 가까이에 그린다.
    /// 내려찍기는 호를 세워 앞으로 떨어지게 그린다.
    /// </summary>
    public static void SlashVfx(Vector3 floorOrigin, Vector3 dir, float radius, LichSwing swing, Color? tint = null)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion rot = Quaternion.LookRotation(dir.normalized);
        if (swing == LichSwing.Overhead)        rot *= Quaternion.Euler(0f, 0f, 90f);
        else if (swing == SlashMirroredSwing)   rot *= Quaternion.Euler(0f, 0f, 180f);

        Vector3 pos   = floorOrigin + Vector3.up * (swing == LichSwing.Overhead ? radius * 0.5f : SlashHeight);
        float   scale = radius / SlashNativeRadius;
        if (tint.HasValue) LichVfx.PlayTinted(LichVfxSlot.ScytheSlash, pos, rot, scale, tint.Value);
        else               LichVfx.Play(LichVfxSlot.ScytheSlash, pos, rot, scale);
    }

    /// <summary>낫 휘두름 — <paramref name="contactIn"/>초 뒤 판정 순간에 동작의 접촉 프레임을 맞춘다.</summary>
    public static void Swing(MonsterContext ctx, LichSwing swing, float contactIn, float holdSeconds = 0.07f)
        => (ctx.Monster as LichMonster)?.PlaySwing(swing, contactIn, holdSeconds);

    /// <summary>몸 방향을 쥔다(판정 방향 = 몸 방향). 패턴 잠금이 풀리면 자동으로 놓인다.</summary>
    /// <summary>시전 박자 — 충전 자세로 버티다 <paramref name="releaseIn"/>초 뒤 판정 순간에 내뻗는다(CrossFade 대신).</summary>
    public static void CastBeat(MonsterContext ctx, LichCast cast, float releaseIn, float snapSpeed = 2f, float holdSeconds = 0.1f)
        => Lich(ctx)?.PlayCastBeat(cast, releaseIn, snapSpeed, holdSeconds);

    public static void HoldFacing(MonsterContext ctx, float yaw, float turnDegPerSec = 720f)
        => Mover(ctx)?.HoldFacing(yaw, turnDegPerSec);

    /// <summary>표적 쪽으로 초당 <paramref name="turnDegPerSec"/>로 돈다(즉시 회전 대신 · 10-18 실측 마법탄마다 162°/프레임). 판정이 몸 방향이 아닌 패턴에만.</summary>
    public static void FaceToward(MonsterContext ctx, Vector3 target, float turnDegPerSec = 720f)
    {
        Vector3 dir = target - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.001f) return;
        if (Mover(ctx) == null) { FaceInstant(ctx, target); return; }
        HoldFacing(ctx, Quaternion.LookRotation(dir).eulerAngles.y, turnDegPerSec);
    }

    /// <summary>
    /// 봉인 완성(금빛 고리 · <see cref="LichVfxSlot.SealComplete"/>) — 고리 넷만 남기고 나머지 자식(<see cref="SealCompleteHeavyParts"/>)은 끈다
    /// (10-14 전주기 5회차 재확인: 사슬 구를 뺀 뒤에도 흰 빛줄기 여섯 갈래 · 파편 구름이 화면 절반 이상을 2~3초 덮었다 · 이펙트 절제.
    /// 허브 비교 촬영 — 연기 · 빛기둥을 꺼도 빛가루 비가 고리보다 넓게 흰 줄로 내렸다. 「감긴다」는 금빛 번쩍 · 봉인석 사슬 번쩍이 알린다).
    /// 칸 프리팹은 이 칸만 쓴다 — 풀로 돌아간 인스턴스가 꺼진 채 남아도 된다.
    /// </summary>
    public static GameObject PlaySealComplete(Vector3 at)
    {
        var go = LichVfx.Play(LichVfxSlot.SealComplete, at, Quaternion.identity);
        if (go == null) return null;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (System.Array.IndexOf(SealCompleteHeavyParts, t.name) >= 0) t.gameObject.SetActive(false);
        return go;
    }

    public static void Impact(LichImpact grade, bool hitPlayer = false)
    {
        LichPatternProbe.Strike(grade.ToString(), hitPlayer);
        switch (grade)
        {
            case LichImpact.Light:
                if (hitPlayer) HitFeelService.HitStop(0.1f, 0.05f);
                break;
            case LichImpact.Medium:
                if (hitPlayer) HitFeelService.HitStop(0.08f, 0.08f);
                break;
            case LichImpact.Slash:
                HitFeelService.CameraShake(SlashShakeAmp, SlashShakeSeconds);
                if (hitPlayer) HitFeelService.HitStop(0.06f, 0.1f);
                break;
            case LichImpact.Heavy:
                HitFeelService.CameraShake(HeavyShakeAmp, HeavyShakeSeconds);
                VolumePulseService.Pulse(0.3f, 0.2f);
                if (hitPlayer) HitFeelService.HitStop(0.05f, 0.12f);
                break;
            case LichImpact.Transition:
                HitFeelService.CameraShake(TransitionShakeAmp, TransitionShakeSecs);
                VolumePulseService.Pulse(0.5f, 0.35f);
                break;
        }
    }

    // ── 예고 진행 ─────────────────────────────────────────────

    /// <summary>
    /// 예고 한 프레임 — <paramref name="elapsed"/>/<paramref name="openSeconds"/>만큼 데칼을 채우고,
    /// 끝나기 <paramref name="signalSeconds"/> 전부터 빨강 + 밝게(신호). 신호로 넘어간 프레임에 true.
    /// </summary>
    public static bool TickTelegraph(GameObject guide, float elapsed, float openSeconds, float signalSeconds, ref bool signaled)
    {
        if (guide == null) return false;
        float open = Mathf.Max(0.01f, openSeconds);
        PatternGuideHelper.SetProgress(guide, elapsed / open);
        if (signaled || elapsed < open - signalSeconds) return false;

        signaled = true;
        LichPatternProbe.Signal();
        PatternGuideHelper.SetColor(guide, Lethal);
        PatternGuideHelper.SetFlow(guide, Lethal);
        PatternGuideHelper.SetIntensity(guide, SignalIntensity);
        return true;
    }

    /// <summary>예고 데칼을 차오르는 면으로 준비한다(색 = 테두리·채움 같은 색).</summary>
    public static GameObject PrepareTelegraph(GameObject guide, Color color)
    {
        LichPatternProbe.Telegraph();
        PatternGuideHelper.SetFlow(guide, color);
        PatternGuideHelper.SetProgress(guide, 0f);
        return guide;
    }

    public static LichMonster Lich(MonsterContext ctx) => ctx.Monster as LichMonster;

    /// <summary>
    /// 공격 뒤 회복 시간 — 2·3페이지 공격 속도 배율(<see cref="BossAttackBlackboard.AttackSpeedMult"/>, 전환 패턴이 1.2를 건다)만큼 짧게(10-08 사용자 결정).
    /// 예고 · 판정 시간은 그대로 둔다 — 피할 수 있는 정도는 같고, 공격을 더 촘촘히 잇는다.
    /// </summary>
    public static float Recovery(MonsterContext ctx, float seconds)
    {
        float mult = (Lich(ctx)?.LichBB?.AttackSpeedMult ?? 1f) * LichMonster.AssaultRecoverySpeed;   // 10-26 「몰아치게」 ÷1.25
        return mult > 0f ? seconds / mult : seconds;
    }

    /// <summary>
    /// 디졸브로 스러진 뒤 <paramref name="root"/>를 파괴한다(10-03 — 리치가 쓰는 물체가 툭 꺼지지 않게). 디졸브는 <paramref name="visual"/>(메시)에만 —
    /// 부모와 자식을 겹쳐 디졸브하면 마젠타, 투명 가이드 · 입자는 대상이 아니다. 메시가 없거나 앱 종료 중(매니저 해제 — 디졸브 재질을 못 부른다)이면 바로 파괴.
    /// </summary>
    public static void DissolveAndDestroy(GameObject visual, GameObject root, float seconds)
    {
        if (root == null) return;
        if (visual == null || Managers.Instance == null)
        {
            Object.Destroy(root);
            return;
        }
        DissolveEffect.PlayDisappear(visual, seconds, () => { if (root != null) Object.Destroy(root); });
    }

    /// <summary>
    /// 바닥을 부순다(1페이지 지형 — 연출·UX 시나리오 §12-4). <paramref name="center"/> 수평 반경 안 칸이 붉게 흔들리다 가라앉는다.
    /// <paramref name="downSeconds"/>가 음수면 복구 패턴(<see cref="RestoreFloor"/>)까지 구멍으로 남아 누적된다(상한은 아레나가 정한다).
    /// <paramref name="keepCenterCell"/>면 중심이 선 칸은 남긴다(리치가 선 자리). 붕괴형 아레나가 아니면 아무 일도 없다. 실제로 부순 칸 수.
    /// </summary>
    public static int BreakFloor(Vector3 center, float radius, float warnSeconds, float downSeconds = -1f, bool keepCenterCell = false)
    {
        var grid = ArenaTileGrid.Active;
        if (grid == null || radius <= 0f) return 0;

        grid.CellsInRadius(center, radius, s_cells);
        if (keepCenterCell && grid.TryGetCell(center, out var own)) s_cells.Remove(own);
        int n = grid.BreakCells(s_cells, warnSeconds, downSeconds);
        if (n > 0) LichSfx.Play(LichSfxSlot.Collapse, center, 0.6f);
        return n;
    }

    /// <summary>
    /// 부서진 바닥을 되살린다 — 복구 패턴(M8 원소 재편 시작 · T1 전환). <paramref name="center"/>에서 가까운 칸부터
    /// <paramref name="stagger"/>초 간격으로 떠오른다(칸마다 청록 빛 · 소리는 아레나 이벤트로). 되살린 칸 수.
    /// </summary>
    public static int RestoreFloor(Vector3 center, float stagger = 0.06f)
    {
        var grid = ArenaTileGrid.Active;
        if (grid == null) return 0;
        int n = grid.RestoreBroken(center, stagger);
        if (n > 0) LichSfx.Play(LichSfxSlot.CastCharge, center, 0.7f);
        return n;
    }

    /// <summary>낫 판정 직전 — 패링 창을 연다(낫 섬광 · 예고음).</summary>
    public static void OpenParry(MonsterContext ctx) => Lich(ctx)?.OpenParryWindow(ParryWindow + 0.05f);

    /// <summary>
    /// 낫 판정 순간 — 튕겨냈으면 휘청을 시작하고 그 시간(초)을 돌려준다. 0이면 튕겨지지 않았다 → 평소대로 판정.
    /// </summary>
    public static float ConsumeParry(MonsterContext ctx)
    {
        var lich = Lich(ctx);
        return lich != null && lich.ConsumeParried() ? lich.BeginParryStagger() : 0f;
    }

    public static LichMovementController Mover(MonsterContext ctx) => Lich(ctx)?.MovementController;

    /// <summary>바닥 높이(리치 이동 컨트롤러가 잰 값, 없으면 리치 높이).</summary>
    public static float FloorY(MonsterContext ctx)
    {
        var mc = Mover(ctx);
        return mc != null ? mc.FloorY : ctx.Transform.position.y;
    }

    public static Vector3 OnFloor(MonsterContext ctx, Vector3 p)
    {
        p.y = FloorY(ctx);
        return p;
    }

    /// <summary>플레이어 발밑(바닥 높이). 플레이어가 없으면 리치 앞 8 m.</summary>
    public static Vector3 PlayerFloorPos(MonsterContext ctx)
    {
        var t = ctx.Runtime.PlayerTarget;
        Vector3 p = t != null ? t.position : ctx.Transform.position + ctx.Transform.forward * 8f;
        return OnFloor(ctx, p);
    }

    public static void FaceInstant(MonsterContext ctx, Vector3 target)
    {
        Vector3 dir = target - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>
    /// 바닥 원 가이드 크기 조절 — 데칼(Quad, x·y가 넓이)과 원기둥 폴백(x·z가 넓이) 모두에서 넓이 축만 곱한다.
    /// </summary>
    public static Vector3 ScaleFlat(Vector3 baseScale, float k)
        => new Vector3(baseScale.x * k,
                       baseScale.y > 0.5f ? baseScale.y * k : baseScale.y,
                       baseScale.z > 0.5f ? baseScale.z * k : baseScale.z);

    /// <summary><paramref name="from"/>에서 <paramref name="dir"/>로 갈 때 제단 가장자리까지의 수평 거리.</summary>
    public static float DistanceToArenaEdge(MonsterContext ctx, Vector3 from, Vector3 dir)
    {
        var mc = Mover(ctx);
        if (mc == null) return 999f;

        Vector3 p = from - mc.ArenaCenter;
        p.y   = 0f;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return 0f;
        dir.Normalize();

        float r    = mc.ArenaRadius;
        float b    = Vector3.Dot(p, dir);
        float disc = b * b - (p.sqrMagnitude - r * r);
        return disc < 0f ? 0f : Mathf.Max(0f, -b + Mathf.Sqrt(disc));
    }

    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// 반경 <paramref name="radius"/> 원을 빽빽이 깐다(10-22 E5 페이지2) — 흔든 육각 격자: 격자 한 점 = <paramref name="aim"/>(없으면 <paramref name="area"/>),
    /// 격자 방향 · 간격은 시전마다 무작위, 원마다 조금씩 흔들어 이웃 원 가장자리 사이 빈틈이 <paramref name="gap"/>(최소 · 최대) 안에서 매번 다르다.
    /// 빈자리를 무작위로 찾으면(원소 일제 발동 방식) 지름 5 m 넘는 빈자리가 남았다(오프라인 실측) — 격자는 가장 큰 빈자리도 지름 2.5~2.8 m.
    /// <paramref name="area"/> 둘레 <paramref name="areaRadius"/> 안 격자점을 겨눈 자리에서 가까운 순으로 <paramref name="maxCount"/>개까지 ·
    /// <paramref name="blocked"/>가 true인 자리(제단 밖 · 안전 칸)는 비운다. <paramref name="discs"/>에 이미 든 원(떨어지는 중인 것)과는 최소 빈틈을 지킨다.
    /// </summary>
    public static void PackDiscs(List<Vector3> discs, Vector3 area, float areaRadius, float radius, Vector2 gap, int maxCount,
                                 Vector3? aim, System.Predicate<Vector3> blocked = null)
    {
        float   g      = Random.Range(Mathf.Lerp(gap.x, gap.y, 0.3f), Mathf.Lerp(gap.x, gap.y, 0.7f));   // 이번 시전 간격
        float   jitter = Mathf.Min(g - gap.x, gap.y - g) * 0.5f;                                       // 원마다 흔들림 — 이웃 틈이 gap 안에 남는다
        float   step   = radius * 2f + g;
        Vector3 origin = aim ?? area;
        float   yaw    = Random.Range(0f, 60f);
        Vector3 u      = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * step;
        Vector3 v      = Quaternion.Euler(0f, yaw + 60f, 0f) * Vector3.forward * step;
        int     n      = Mathf.CeilToInt((areaRadius + FlatDistance(origin, area)) / (step * 0.866f)) + 1;

        s_lattice.Clear();
        for (int a = -n; a <= n; a++)
        for (int b = -n; b <= n; b++)
        {
            Vector3 p = origin + u * a + v * b;
            if (FlatDistance(p, area) <= areaRadius) s_lattice.Add(p);
        }
        s_latticeOrigin = origin;
        s_lattice.Sort(s_byLatticeDistance);

        int first = discs.Count;
        for (int i = 0; i < s_lattice.Count && discs.Count - first < maxCount; i++)
        {
            Vector3 p = s_lattice[i];
            if (!aim.HasValue || FlatDistance(p, origin) > 0.01f)   // 겨눈 자리는 그대로
            {
                Vector2 o = Random.insideUnitCircle * jitter;
                p += new Vector3(o.x, 0f, o.y);
            }
            if (blocked != null && blocked(p)) continue;
            bool crowded = false;
            for (int k = 0; k < first && !crowded; k++) crowded = FlatDistance(p, discs[k]) < radius * 2f + gap.x;
            if (!crowded) discs.Add(p);
        }
    }

    /// <summary>피해 — 공격력 × 배율. 사슬 둘째 칸(<see cref="LichMonster.IsChainLink"/>)이 도는 동안은 × <see cref="ChainLinkDamage"/>(10-22 E2 L1).</summary>
    public static int Damage(MonsterContext ctx, float multiplier)
    {
        if (Lich(ctx)?.IsChainLink == true) multiplier *= ChainLinkDamage;
        return Mathf.Max(1, Mathf.RoundToInt((ctx.Config?.stat?.attackPower ?? 0f) * multiplier));
    }

    /// <summary>원 판정 — 맞았으면 true.</summary>
    public static bool HitCircle(MonsterContext ctx, Vector3 center, float radius, float damageMult,
                                 float knockbackMult = 0f, bool ignorePoise = false)
        => HitCircle(ctx, center, radius, damageMult, out _, knockbackMult, ignorePoise);

    /// <summary>
    /// 원 판정 — 맞았으면 true. <paramref name="contact"/> = 무적이 아니던 플레이어에게 닿았다(무효화 · 저스트 회피 · 부활로 피해가 0이어도).
    /// 닿을 때까지 매 프레임 판정하는 투사체 · 광선은 contact로 「쓰였다」를 정한다 — 피해 0이라고 다시 판정하면 무효화 확률을
    /// 매 프레임 다시 굴리고 피격 패시브가 매 프레임 터졌다(10-08 점검). 결박 · 추격 · 히트스톱은 반환값(실제 피해)으로 건다.
    /// </summary>
    public static bool HitCircle(MonsterContext ctx, Vector3 center, float radius, float damageMult, out bool contact,
                                 float knockbackMult = 0f, bool ignorePoise = false)
    {
        contact = false;
        var target = ctx.Runtime.PlayerTarget;
        if (target == null || FlatDistance(center, target.position) > radius) return false;
        LichPatternProbe.Inside();
        return Apply(ctx, center, damageMult, knockbackMult, ignorePoise, out contact);
    }

    /// <summary>
    /// 직선 판정 — <paramref name="origin"/>에서 <paramref name="dir"/>로 <paramref name="length"/>까지, 폭 2×<paramref name="halfWidth"/>.
    /// </summary>
    public static bool HitBeam(MonsterContext ctx, Vector3 origin, Vector3 dir, float length, float halfWidth,
                               float damageMult, float knockbackMult = 0f)
        => HitBeam(ctx, origin, dir, length, halfWidth, damageMult, out _, knockbackMult);

    /// <summary>
    /// 직선 판정 — 반환값 · <paramref name="contact"/>는 원 판정(HitCircle)과 같다(실제 피해 · 닿음).
    /// 넉백은 광선 옆으로(축에서 플레이어 쪽, <paramref name="pushSide"/>가 있으면 그쪽) — 광선을 따라 밀면 그 안에 남았다(10-08 점검).
    /// </summary>
    public static bool HitBeam(MonsterContext ctx, Vector3 origin, Vector3 dir, float length, float halfWidth,
                               float damageMult, out bool contact, float knockbackMult = 0f, Vector3 pushSide = default)
    {
        contact = false;
        var target = ctx.Runtime.PlayerTarget;
        if (target == null) return false;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        Vector3 to    = target.position - origin;
        to.y          = 0f;
        float   along = Vector3.Dot(to, dir);
        if (along < 0f || along > length) return false;
        Vector3 side  = to - dir * along;
        if (side.magnitude > halfWidth) return false;

        LichPatternProbe.Inside();
        if (pushSide.sqrMagnitude > 0.0001f)  side = pushSide;
        else if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(Vector3.up, dir);   // 축 위 — 어느 옆이든
        side.y = 0f;
        return Apply(ctx, target.position - side.normalized, damageMult, knockbackMult, false, out contact);
    }

    /// <summary>모양 판정을 패턴이 직접 한 뒤 피해만 넣는다(부채꼴 · 쐐기 등). 실제로 맞았을 때만 true.</summary>
    public static bool HitPlayer(MonsterContext ctx, Vector3 from, float damageMult, float knockbackMult = 0f)
        => HitPlayer(ctx, from, damageMult, out _, knockbackMult);

    /// <summary>모양 판정 뒤 피해만 — <paramref name="contact"/>는 닿음(무효화 · 저스트 회피로 흘렸어도, 10-08 점검).</summary>
    public static bool HitPlayer(MonsterContext ctx, Vector3 from, float damageMult, out bool contact, float knockbackMult = 0f)
    {
        contact = false;
        if (ctx.Runtime.PlayerTarget == null) return false;
        LichPatternProbe.Inside();
        return Apply(ctx, from, damageMult, knockbackMult, false, out contact);
    }

    /// <summary>
    /// 피해를 넣고 <b>실제로 맞았을 때만</b> true — 무적 · 저스트 회피 · 무효화로 흘려보냈으면 false이고 넉백도 없다.
    /// (true를 받아 히트스톱 · 사슬 결박 · 추격 폭발을 거는 패턴이 많다 — 회피 성공이 맞은 것처럼 처리되던 문제, 09-19 감사)
    /// </summary>
    /// <summary>
    /// 플레이어 피격 연출 등급 — 피해 배율로 매긴다(패턴마다 따로 적지 않게). 포이즈 무시 또는 ×1.8 이상 = 강 · ×1.0 이상 = 중 · 그 아래(틱 · 여운 장판 · 약한 탄) = 약.
    /// 연출은 플레이어 쪽이 등급대로(약한 공격까지 같은 강도면 스트레스 — 09-20 사용자 지시).
    /// </summary>
    private static HitWeight WeightOf(float damageMult, bool ignorePoise)
        => ignorePoise || damageMult >= 1.8f ? HitWeight.Heavy
         : damageMult >= 1.0f                ? HitWeight.Medium
         :                                     HitWeight.Light;

    private static bool Apply(MonsterContext ctx, Vector3 from, float damageMult, float knockbackMult, bool ignorePoise,
                              out bool contact)
    {
        contact = false;
        var player = ctx.Runtime.CachedPlayer;
        if (player == null && ctx.Runtime.PlayerTarget != null)
            ctx.Runtime.PlayerTarget.TryGetComponent(out player);
        if (player == null) return false;

        var  stats  = player.RuntimeStats;
        bool dodged = player.IsInvincible;
        int  hp     = stats != null ? stats.Hp : 0;
        int  shield = stats != null ? stats.Shield : 0;

        player.TakeDamage(Damage(ctx, damageMult), ctx.Monster.gameObject, ignorePoise, WeightOf(damageMult, ignorePoise));

        // 무적이 아니던 플레이어에게 닿았다 — 무효화 · 저스트 회피 · 부활로 HP가 안 줄었어도 이 판정은 쓰였다(10-08 점검).
        contact = !dodged;
        bool landed = !dodged && stats != null && (stats.Hp < hp || stats.Shield < shield);
        if (!landed) return false;

        if (knockbackMult > 0f)
        {
            Vector3 dir = player.transform.position - from;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
            dir.y = 0.3f;
            player.ApplyKnockback(dir.normalized * (ctx.Config.stat.knockbackForce * knockbackMult));
        }
        return true;
    }
}
}
