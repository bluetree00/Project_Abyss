using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 형 발동 피해 도우미 — 직선(풍절) · 원형(내려찍기). 적중 처리 안에서 불리므로 DealSynergyDamage만 쓴다(재귀 방지).
/// 맞힌 적 수를 돌려준다.
/// </summary>
public static class FormStrike
{
    private const int   RingSegments = 48;
    private const float RingWidth = 0.08f, RingLift = 0.06f, RingAlpha = 0.55f, RingFade = 0.3f;

    /// <summary>from에서 dir로 length m, 폭 ±halfWidth 안의 살아 있는 적에게 damage.</summary>
    public static int Line(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth, float damage, List<MonsterBase> buf)
    {
        dir.y = 0f;
        if (p == null || dir.sqrMagnitude < 1e-4f || length <= 0f) return 0;
        dir.Normalize();
        Vector3 to = from + dir * length;
        int n = CombatQuery.GetNearbyEnemies((from + to) * 0.5f, length * 0.5f + halfWidth + 0.5f, p.gameObject, 32, buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i];
            if (e == null || e.IsDead || DistToSegmentXZ(e.transform.position, from, to) > halfWidth + CombatQuery.BodyReach(e)) continue;   // 몸에 닿으면(10-16)
            CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
            hits++;
        }
        return hits;
    }

    /// <summary>center 반경 radius 안의 적에게 damage. stun > 0이면 보스 제외 경직, ampPct > 0이면 경직한 적 받피증.</summary>
    public static int Radius(PlayerController p, Vector3 center, float radius, float damage, List<MonsterBase> buf,
                             float stun = 0f, float ampPct = 0f, float ampSeconds = 0f)
    {
        if (p == null || radius <= 0f) return 0;
        int n = CombatQuery.GetNearbyEnemies(center, radius, p.gameObject, 32, buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i];
            if (e == null || e.IsDead) continue;
            CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
            hits++;
            if (stun <= 0f || e.IsDead || e.Grade == MonsterGrade.Boss) continue;   // 보스는 경직 없이 피해만
            e.ApplyStun(stun);
            if (ampPct > 0f) e.ApplyDamageTakenAmp(ampPct, ampSeconds, "form_crush");
        }
        return hits;
    }

    /// <summary>날아가는 검기의 앞 끝 속도(m/초) — 그림(Hovl Slash wave)이 판정 길이를 지나는 빠르기에 맞춘다(10-23 실측 86).</summary>
    public const float WaveSpeed = 24f;

    /// <summary>
    /// 날아가는 검기(10-23 W1 「나오는 검기가 데미지를 주지 못한다」) — <paramref name="delay"/>초 뒤 검기가 나가 앞 끝이 지나가는 적을 그때 벤다.
    /// 예전엔 발동 순간 6~8 m 전부를 한꺼번에 쳐서, 검기 그림이 적을 지날 때는 숫자가 이미 지나간 뒤였다(실측 86: 모든 거리 0.00초).
    /// <paramref name="hitOnce"/>를 같은 묶음(쌍검기 갈래 등)끼리 나눠 쓰면 한 적은 한 번만 맞는다. 다 지나가면 <paramref name="done"/>(맞힌 수).
    /// </summary>
    public static void LineTravel(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth, float damage,
                                  string vfxKey, float delay = 0f, HashSet<MonsterBase> hitOnce = null, Action<int> done = null,
                                  float stripBody = 0f, float stripSeconds = 0f)
    {
        dir.y = 0f;
        if (p == null || dir.sqrMagnitude < 1e-4f || length <= 0f) { done?.Invoke(0); return; }
        LineTravelAsync(p, from, dir.normalized, length, halfWidth, damage, vfxKey, delay, hitOnce ?? new HashSet<MonsterBase>(), done,
                        stripBody, stripSeconds, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid LineTravelAsync(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth, float damage,
                                                     string vfxKey, float delay, HashSet<MonsterBase> hitOnce, Action<int> done,
                                                     float stripBody, float stripSeconds, CancellationToken ct)
    {
        try
        {
            if (delay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: ct);
            if (p == null) return;
            if (length > 0.3f) PlayWave(vfxKey, from + Vector3.up, dir, length, halfWidth + stripBody);
            if (stripSeconds > 0f && length > 0.3f) GroundStrip(from, dir, length, halfWidth + stripBody, stripSeconds);
            var buf = new List<MonsterBase>(16);
            int hits = 0;
            float t = 0f;
            while (true)
            {
                float front = Mathf.Min(length, t * WaveSpeed);
                Vector3 tip = from + dir * front;
                int n = CombatQuery.GetNearbyEnemies((from + tip) * 0.5f, front * 0.5f + halfWidth + 1.5f, p.gameObject, 32, buf);
                for (int i = 0; i < n; i++)
                {
                    var e = buf[i];
                    if (e == null || e.IsDead || hitOnce.Contains(e)) continue;
                    float reach = halfWidth + CombatQuery.BodyReach(e);
                    if (DistToSegmentXZ(e.transform.position, from, tip) > reach) continue;   // 앞 끝이 몸에 닿았나(10-16 몸에 닿으면)
                    hitOnce.Add(e);
                    CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
                    hits++;
                }
                if (front >= length) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (p == null) return;
                t += Time.deltaTime;
            }
            if (hits == 0) DiagnoseMiss(p, from, dir, length, halfWidth, t);
            done?.Invoke(hits);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>0타 진단(에디터만) — 출발 자리 · 방향 · 가장 가까운 적의 앞 · 옆 거리(10-23 실측 86: 정면 검기만 0타).</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void DiagnoseMiss(PlayerController p, Vector3 from, Vector3 dir, float length, float halfWidth, float travelSeconds)
    {
        var near = new List<MonsterBase>(8);
        int n = CombatQuery.GetNearbyEnemies(from, length + 3f, p.gameObject, 8, near);
        var sb = new System.Text.StringBuilder();
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        for (int i = 0; i < n; i++)
        {
            var e = near[i];
            if (e == null) continue;
            Vector3 d = e.transform.position - from;
            sb.Append($" [{e.name} 앞 {Vector3.Dot(d, dir):0.0} · 옆 {Vector3.Dot(d, right):0.0} · 몸 {CombatQuery.BodyReach(e):0.0}{(e.IsDead ? " · 죽음" : "")}]");
        }
        Debug.Log($"[FormStrike] 0타 — 출발 {from} · 방향 {dir} · 플레이어 정면 {p.transform.forward} · 길이 {length:0.0} · 반폭 {halfWidth:0.0} · {travelSeconds:0.00}초 ·{sb}");
    }

    /// <summary>검기가 <paramref name="from"/>에서 표적까지 날아가 닿는 순간 한 대상만 벤다(튕기는 칼끝) — <paramref name="delay"/>초 뒤 나간다.</summary>
    public static void WaveTo(PlayerController p, Vector3 from, MonsterBase target, float damage, bool crit, string vfxKey, float vfxPerMeter, float delay)
    {
        if (p == null || target == null) return;
        WaveToAsync(p, from, target, damage, crit, vfxKey, vfxPerMeter, delay, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid WaveToAsync(PlayerController p, Vector3 from, MonsterBase target, float damage, bool crit,
                                                 string vfxKey, float vfxPerMeter, float delay, CancellationToken ct)
    {
        try
        {
            if (delay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: ct);
            if (p == null || target == null || target.IsDead) return;
            Vector3 dir = target.transform.position - from;
            dir.y = 0f;
            float dist = dir.magnitude;
            PlayVfx(vfxKey, from + Vector3.up, dir, vfxPerMeter * dist);
            float travel = dist / WaveSpeed;
            if (travel > 0f) await UniTask.Delay(TimeSpan.FromSeconds(travel), cancellationToken: ct);
            if (p == null || target == null || target.IsDead) return;
            CombatQuery.DealSynergyDamage(target, damage, p.gameObject, 1f, isCrit: crit);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary><paramref name="seconds"/>초 뒤 한 번 — 분신 칼이 닿는 순간에 맞춰 피해 · 그림을 낼 때(플레이어가 사라지면 안 한다).</summary>
    public static void After(PlayerController p, float seconds, Action act)
    {
        if (p == null || act == null) return;
        if (seconds <= 0f) { act(); return; }
        AfterAsync(p, seconds, act, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid AfterAsync(PlayerController p, float seconds, Action act, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);
            if (p != null) act();
        }
        catch (OperationCanceledException) { }
    }

    public static void PlayVfx(string key, Vector3 at, Vector3 forward, float scale = 1f)
    {
        if (!string.IsNullOrEmpty(key)) RelicStateVfx.PlayOneShot(key, at, scale, forward);
    }

    /// <summary>검기 그림(vfx_form_windcut = Hovl Slash wave)이 배율 1에서 보이는 크기 — 보이는 양 90% 앞 ≈5.6 m · 옆 ≈±2.1 m(10-18 실측 71).</summary>
    public const float WaveForwardPerScale = 5.6f, WaveLateralPerScale = 2.1f;

    /// <summary>이 시간 이하로 남는 바닥 고리는 순간 가이드로 본다(<see cref="SkillRangeRing.PlayerGuides"/>가 꺼지면 안 그린다).</summary>
    private const float InstantGuideSeconds = 0.35f;

    /// <summary>
    /// 검기를 판정 모양(앞 <paramref name="length"/> m · 옆 ±<paramref name="lateral"/> m)에 앞 · 옆 따로 맞춰 띄운다(10-18 ⑯).
    /// 고르게 키우면 8 m 검기는 옆이 판정의 2배 · 2.5 m는 0.6배였다 — 보이는 가장자리의 적이 안 맞아 「판정이 거의 없다」로 읽혔다.
    /// </summary>
    public static void PlayWave(string key, Vector3 at, Vector3 forward, float length, float lateral)
    {
        if (string.IsNullOrEmpty(key) || length <= 0f || lateral <= 0f) return;
        float sz = length / WaveForwardPerScale, sx = lateral / WaveLateralPerScale;
        RelicStateVfx.PlayOneShot(key, at, new Vector3(sx, Mathf.Sqrt(sx * sz), sz), forward);
    }

    /// <summary>
    /// 바닥에 얇은 고리를 seconds 동안 남긴다(형 색) — 남아 있는 판정 자리의 경계를 보인다(균열 · 10-12 B9).
    /// 마지막 0.3초에 옅어지며 사라진다. 방이 바뀌어 오브젝트가 사라지면 그대로 끝난다.
    /// </summary>
    /// <summary>바닥 고리(판정 반경) — 플레이어 표시 색(<see cref="SkillRangeRing.PlayerGuideColor"/>)의 얇은 선(10-15 적 예고와 구분).</summary>
    public static void GroundRing(Vector3 center, float radius, float seconds)
    {
        if (seconds <= InstantGuideSeconds && !SkillRangeRing.PlayerGuides) return;   // 순간 고리(충격파 등)는 가이드라 끈다 — 남는 구역(안개 · 균열 · 지진 예고)은 그대로(10-19 ⑮)
        if (radius <= 0f || seconds <= 0f) return;
        var go = new GameObject("FormGroundRing");
        go.transform.position = center + Vector3.up * RingLift;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace     = false;
        lr.loop              = true;
        lr.positionCount     = RingSegments;
        lr.widthMultiplier   = RingWidth;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        var mat = new Material(Shader.Find("Sprites/Default"));
        lr.sharedMaterial = mat;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        Color color = SkillRangeRing.PlayerGuideColor;
        color.a = RingAlpha;
        lr.startColor = lr.endColor = color;
        FadeRingAsync(lr, mat, seconds, color, go.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 바닥에 얇은 띠(직사각형) 외곽선을 seconds 동안 — 직선 판정(참격파 · 궤적 베기)의 폭을 보인다(10-13 이펙트 = 판정).
    /// 끝은 판정처럼 반폭만큼 둥글게 넘는 몫을 포함해 길이 + 반폭 × 2로 그린다.
    /// </summary>
    public static void GroundStrip(Vector3 from, Vector3 dir, float length, float halfWidth, float seconds)
    {
        if (!SkillRangeRing.PlayerGuides) return;   // 검기 판정 띠는 가이드 — 그림이 판정 모양에 맞춰졌으니 끈다(10-19 ⑮)
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f || length <= 0f || halfWidth <= 0f || seconds <= 0f) return;
        dir.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, dir) * halfWidth;
        Vector3 a = from - dir * halfWidth, b = from + dir * (length + halfWidth);
        var go = new GameObject("FormGroundStrip");
        go.transform.position = new Vector3(0f, from.y + RingLift, 0f);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace     = true;
        lr.loop              = true;
        lr.positionCount     = 4;
        lr.widthMultiplier   = RingWidth;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;
        var mat = new Material(Shader.Find("Sprites/Default"));
        lr.sharedMaterial = mat;
        float y = from.y + RingLift;
        lr.SetPosition(0, new Vector3((a - right).x, y, (a - right).z));
        lr.SetPosition(1, new Vector3((b - right).x, y, (b - right).z));
        lr.SetPosition(2, new Vector3((b + right).x, y, (b + right).z));
        lr.SetPosition(3, new Vector3((a + right).x, y, (a + right).z));
        Color color = SkillRangeRing.PlayerGuideColor;   // 플레이어 표시 색 — 속성색(주황 · 빨강)은 적 예고와 섞였다(10-15)
        color.a = RingAlpha;
        lr.startColor = lr.endColor = color;
        FadeRingAsync(lr, mat, seconds, color, go.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid FadeRingAsync(LineRenderer lr, Material mat, float seconds, Color color, CancellationToken ct)
    {
        try
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Color c = color;
                c.a = color.a * Mathf.Clamp01((seconds - t) / RingFade);
                lr.startColor = lr.endColor = c;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (lr != null) UnityEngine.Object.Destroy(lr.gameObject);
            if (mat != null) UnityEngine.Object.Destroy(mat);
        }
    }

    public static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 P = new(p.x, p.z), A = new(a.x, a.z), B = new(b.x, b.z);
        Vector2 ab = B - A;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(P, A + ab * t);
    }
}
