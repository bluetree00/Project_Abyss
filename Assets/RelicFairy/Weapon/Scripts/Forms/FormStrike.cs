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
            if (e == null || e.IsDead || DistToSegmentXZ(e.transform.position, from, to) > halfWidth + 0.4f) continue;
            CombatQuery.DealSynergyDamage(e, damage, p.gameObject);
            hits++;
        }
        return hits;
    }

    /// <summary>두 갈래 직선(쌍풍) — 어느 갈래에 들었든 적마다 한 번만 맞는다(넓게 — 두 번 치는 것은 되감는 바람의 몫).</summary>
    public static int Lines(PlayerController p, Vector3 from, Vector3 dirA, Vector3 dirB, float length, float halfWidth, float damage, List<MonsterBase> buf)
    {
        dirA.y = 0f; dirB.y = 0f;
        if (p == null || dirA.sqrMagnitude < 1e-4f || dirB.sqrMagnitude < 1e-4f || length <= 0f) return 0;
        Vector3 toA = from + dirA.normalized * length, toB = from + dirB.normalized * length;
        int n = CombatQuery.GetNearbyEnemies(from, length + halfWidth + 0.5f, p.gameObject, 32, buf);
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i];
            if (e == null || e.IsDead) continue;
            Vector3 at = e.transform.position;
            if (DistToSegmentXZ(at, from, toA) > halfWidth + 0.4f && DistToSegmentXZ(at, from, toB) > halfWidth + 0.4f) continue;
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

    public static void PlayVfx(string key, Vector3 at, Vector3 forward, float scale = 1f)
    {
        if (!string.IsNullOrEmpty(key)) RelicStateVfx.PlayOneShot(key, at, scale, forward);
    }

    /// <summary>
    /// 바닥에 얇은 고리를 seconds 동안 남긴다(형 색) — 남아 있는 판정 자리의 경계를 보인다(균열 · 10-12 B9).
    /// 마지막 0.3초에 옅어지며 사라진다. 방이 바뀌어 오브젝트가 사라지면 그대로 끝난다.
    /// </summary>
    /// <summary>바닥 고리(판정 반경) — 플레이어 표시 색(<see cref="SkillRangeRing.PlayerGuideColor"/>)의 얇은 선(10-15 적 예고와 구분).</summary>
    public static void GroundRing(Vector3 center, float radius, float seconds)
    {
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
