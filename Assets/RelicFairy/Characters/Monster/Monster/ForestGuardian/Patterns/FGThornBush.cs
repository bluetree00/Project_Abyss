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
    // ── Constants ──────────────────────────────────────────────
    private const float AppearSeconds = 0.4f;   // 가시가 디졸브로 돋는 시간(10-03)
    private const float VanishSeconds = 0.6f;   // 가시가 디졸브로 꺼지는 시간(10-03)

    // ── Private ────────────────────────────────────────────────
    private MonsterBase _boss;
    private float       _radius;
    private float       _life;
    private float       _tickSeconds;
    private float       _tick;
    private int         _damage;
    private GameObject  _decal;       // 바닥 원 — 반투명이라 디졸브하지 않는다
    private GameObject  _fx;          // 가시 메시 — 디졸브 대상(루트째 디졸브하면 바닥 원까지 불투명으로 깨진다)
    private float       _appearEnd;   // 등장 디졸브가 끝나는 시각 — 그 전에 꺼지면 겹치지 않게 바로 없앤다
    private bool        _vanishing;

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
        bush._decal = decal;

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
            // 가시가 땅에서 디졸브로 돋는다(10-03) — 루트(~FG_ThornBush)가 아니라 fx만: 부모·자식 동시 디졸브 금지
            bush._fx        = fx;
            bush._appearEnd = Time.time + AppearSeconds;
            DissolveEffect.PlayAppear(fx, AppearSeconds);
        }
        return bush;
    }

    /// <summary>
    /// 덤불을 거둔다 — 틱 피해와 바닥 원은 곧바로 멈추고, 가시는 디졸브로 꺼진 뒤 사라진다(10-03).
    /// 퇴장 디졸브는 원본 재질을 되돌리지 않으므로 끝나자마자 Destroy한다. 등장 디졸브가 아직 돌면 겹치지 않게 바로 없앤다.
    /// </summary>
    public void Vanish()
    {
        if (_vanishing) return;
        _vanishing = true;
        enabled    = false;   // 틱 정지
        if (_decal != null) Destroy(_decal);
        if (_fx == null || Time.time < _appearEnd) { Destroy(gameObject); return; }
        DissolveEffect.PlayDisappear(_fx, VanishSeconds, () => { if (this != null) Destroy(gameObject); });
    }

    // ── Lifecycle ──────────────────────────────────────────────
    private void Update()
    {
        if (_boss == null || _boss.IsDead || !_boss.isActiveAndEnabled) { Vanish(); return; }

        float dt = Time.deltaTime;
        _life -= dt;
        if (_life <= 0f) { Vanish(); return; }

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
