using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// P-3 사방 독 화살. LightSwordAgent와 동일한 VFX 구조: transform 자식 배치.
/// 이동/충돌/관통은 LegendaryProjectileBase가 처리.
/// </summary>
public sealed class GrassArrowAgent : LegendaryProjectileBase
{
    // ── Static ──────────────────────────────────────────────────────────
    private static readonly string[] s_hitSfx =
        { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private static readonly List<GrassArrowAgent> s_active = new();

    /// <summary>활성화된 모든 화살을 즉시 제거한다 (룸 전환·비활성화 시 호출).</summary>
    public static void ClearAll()
    {
        for (int i = s_active.Count - 1; i >= 0; i--)
        {
            if (s_active[i] != null)
                Destroy(s_active[i].gameObject);
        }
        s_active.Clear();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────
    private void OnEnable()  => s_active.Add(this);
    private void OnDisable() => s_active.Remove(this);

    // ── LegendaryProjectileBase overrides ────────────────────────────────
    protected override void TryNavMeshBounce() { }  // Wall 콜라이더만으로 소멸 판정

    protected override bool TryBounce()
    {
        float lookAhead = _speed * Time.deltaTime + 0.2f;
        if (!Physics.Raycast(transform.position, _dir, out var hit, lookAhead, WallMask))
            return false;
        if (Mathf.Abs(hit.normal.y) > 0.7f) return false;
        Expire();
        return true;
    }

    protected override void Move()
    {
        base.Move();
        if (_dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_dir);
    }

    protected override void OnSpawnVfx()
    {
        if (_dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(_dir);

        // TrailRenderer로 직접 독 화살 시각화 (외부 에셋 의존 없음)
        var trail = gameObject.AddComponent<TrailRenderer>();
        trail.time       = 0.25f;
        trail.startWidth = 0.18f;
        trail.endWidth   = 0f;
        trail.minVertexDistance = 0.05f;
        trail.startColor = new Color(0.15f, 0.85f, 0.1f, 0.95f);
        trail.endColor   = new Color(0.15f, 0.85f, 0.1f, 0f);
        trail.material   = new Material(Shader.Find("Sprites/Default"));
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows    = false;
    }

    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }

    protected override void OnSpawnHitVfx(Vector3 pos)
    {
        var cat = LegendaryRuntime.Catalog;
        if (cat?.grassHitVfx != null)
            LegendaryRuntime.SpawnVfx(cat.grassHitVfx, pos, Quaternion.identity, 1.5f);
    }
}
