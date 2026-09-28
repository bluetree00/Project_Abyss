using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 씨앗 폭우(FL3)가 남기는 가시덤불 — 들어가 있으면 <c>tickSeconds</c>마다 약한 피해.
/// 패턴 상태보다 오래 남으므로(8초) 스스로 틱을 돌고, 수명이 다하거나 보스가 죽거나 사라지면 없어진다.
/// 바닥 원(가시 색) + 선택 이펙트(파티클은 수명 동안 계속 돌게 루프로 바꾼다 — BossStageHazard와 같은 방식).
/// </summary>
public sealed class FGThornBush : MonoBehaviour
{
    // ── Private ────────────────────────────────────────────────
    private MonsterBase _boss;
    private float       _radius;
    private float       _life;
    private float       _tickSeconds;
    private float       _tick;
    private int         _damage;

    // ── Public Methods ─────────────────────────────────────────
    /// <param name="damagePerTick">틱당 피해(시전 시점의 보스 공격력 × 배율).</param>
    public static FGThornBush Spawn(MonsterBase boss, Vector3 pos, float radius, float lifetime, float tickSeconds,
                                    int damagePerTick, Color color, GameObject vfxPrefab, float vfxScale)
    {
        var go   = new GameObject("~FG_ThornBush");
        go.transform.position = pos;
        var bush = go.AddComponent<FGThornBush>();
        bush._boss        = boss;
        bush._radius      = Mathf.Max(0.1f, radius);
        bush._life        = Mathf.Max(0.1f, lifetime);
        bush._tickSeconds = Mathf.Max(0.1f, tickSeconds);
        bush._tick        = bush._tickSeconds;   // 착지 피해 직후 곧바로 두 번 맞지 않게 한 틱 쉬고 시작
        bush._damage      = Mathf.Max(1, damagePerTick);

        var decal = PatternGuideHelper.Disc(pos, bush._radius, color);
        PatternGuideHelper.SetFlow(decal, color);
        PatternGuideHelper.SetProgress(decal, 1f);
        decal.transform.SetParent(go.transform, true);

        if (vfxPrefab != null)
        {
            var fx = Instantiate(vfxPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), go.transform);
            fx.transform.localScale = Vector3.one * vfxScale;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.loop = true;   // 한 번 터지고 끝나는 이펙트도 덤불이 있는 동안 계속
                if (!ps.isPlaying) ps.Play();
            }
            foreach (var col in fx.GetComponentsInChildren<Collider>()) col.enabled = false;   // 시각 전용
            // 메시 가시(spike_mesh — 원래 흰 원뿔)는 덤불 색으로(무대 가시 띠와 같게, 09-28 화면 검증)
            var meshes = fx.GetComponentsInChildren<MeshRenderer>();
            if (meshes.Length > 0)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
                foreach (var mr in meshes) mr.SetPropertyBlock(mpb);
            }
        }
        return bush;
    }

    // ── Lifecycle ──────────────────────────────────────────────
    private void Update()
    {
        if (_boss == null || _boss.IsDead || !_boss.isActiveAndEnabled) { Destroy(gameObject); return; }

        float dt = Time.deltaTime;
        _life -= dt;
        if (_life <= 0f) { Destroy(gameObject); return; }

        _tick -= dt;
        if (_tick > 0f) return;
        _tick = _tickSeconds;

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return;
        Vector3 d = player.transform.position - transform.position;
        if (d.x * d.x + d.z * d.z > _radius * _radius) return;
        player.TakeDamage(_damage, _boss.gameObject, false, HitWeight.Light);   // 장판 틱 — 약
    }
}
}
