using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// I-3 영구 빙판: 정적 위치에 VFX 재생(루프) + 범위 내 적에게 지속 피해 + 이속 -60%.
/// 빙결(freeze CC) 중인 적을 발견하면 얼음 파편 광역 폭발(공격력 × 0.5).
/// </summary>
public sealed class IceZoneField : MonoBehaviour
{
    private static readonly string[] s_hitSfx = { SoundKey.Sfx.MonsterHit1, SoundKey.Sfx.MonsterHit2, SoundKey.Sfx.MonsterHit3 };

    private ItemEffectContext _ctx;
    private float _dps;
    private float _attackPower;
    private float _radius;
    private float _lifetime;
    private float _timer;
    private float _tickTimer;
    private const float TickInterval = 0.5f;
    private const float SlowMagnitude = 0.6f;
    private const float SlowDuration = 0.7f;
    private const float ShardMult = 0.5f;
    private const float ShardCooldown = 3f;
    private const float VfxScale = 0.4f;

    private readonly List<GameObject> _buf = new(8);
    private readonly HashSet<GameObject> _hitShown = new();   // 틱 타격 이펙트를 이미 띄운 대상(빙판 하나에 한 번)
    public object Owner { get; private set; }

    public void Init(object owner, ItemEffectContext ctx, float dps, float radius, float lifetime, float attackPower)
    {
        Owner        = owner;
        _ctx         = ctx;
        _dps         = dps;
        _attackPower = attackPower;
        _radius      = radius;
        _lifetime    = lifetime;

        LegendaryRuntime.Instance?.TrackAgent(gameObject);

        var cat = LegendaryRuntime.Catalog;
        if (cat?.iceFieldVfx != null)
        {
            var vfx = Instantiate(cat.iceFieldVfx, transform.position, Quaternion.identity, transform);
            vfx.transform.localPosition = Vector3.zero;
            vfx.transform.localScale    = Vector3.one * VfxScale;
            foreach (var col in vfx.GetComponentsInChildren<Collider>())
                col.enabled = false;
            // 루프 재생
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.loop = true;
            }
        }
    }

    private void OnDestroy()
    {
        LegendaryRuntime.Instance?.RemoveAgent(gameObject);
    }

    /// <summary>지금 빙판 위 적을 빙결한다(보스 제외) — 영구 빙판의 저스트 회피 보상(10-14 R3). 얼린 수를 돌려준다.</summary>
    public int FreezeInside(float seconds)
    {
        int n = CombatQuery.GetNearbyDamageables(transform.position, _radius, _ctx?.Player?.gameObject, 10, _buf);
        int frozen = 0;
        for (int i = 0; i < n; i++)
        {
            var mb = _buf[i] != null ? _buf[i].GetComponentInParent<MonsterBase>() : null;
            if (mb == null || mb.IsDead || mb.Grade == MonsterGrade.Boss) continue;
            mb.Status.ApplyCc("freeze", seconds);
            frozen++;
        }
        return frozen;
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_lifetime > 0f && _timer >= _lifetime) { Destroy(gameObject); return; }

        _tickTimer += Time.deltaTime;
        if (_tickTimer < TickInterval) return;
        _tickTimer = 0f;

        Vector3 pos = transform.position;
        GameObject instigator = _ctx?.Player?.gameObject;
        float tickDmg = _dps * TickInterval;

        int n = CombatQuery.GetNearbyDamageables(pos, _radius, instigator, 10, _buf);
        for (int i = 0; i < n; i++)
        {
            var go = _buf[i];
            CombatQuery.DealSynergyDamage(go, tickDmg, instigator, element: RuneElement.Ice);

            // 파편이 터지는 틱엔 파편 폭발 하나로 — 같은 점 · 같은 프레임에 틱 타격 이펙트가 겹쳤다(10-11 이펙트 톤 점검)
            bool shard = false;
            var mb = go.GetComponentInParent<MonsterBase>();
            if (mb != null)
            {
                mb.Status.ApplySlow("ice_zone_slow", SlowMagnitude, SlowDuration);

                if (mb.Status.HasCc("freeze") && !mb.Status.IsOnCooldown("ice_zone_shard"))
                {
                    shard = true;
                    mb.Status.SetCooldown("ice_zone_shard", ShardCooldown);
                    float shardDmg = _attackPower * ShardMult;
                    CombatQuery.DealSynergyDamage(go, shardDmg, instigator, element: RuneElement.Ice);

                    var cat = LegendaryRuntime.Catalog;
                    // 파편 — 빙하기(SSEP IceAge · 입자 크기 모드 Hierarchy라 배율이 먹는다) 0.8은 빙판보다 컸다 → 0.5(10-11 톤 2차 V-10)
                    LegendaryRuntime.SpawnVfx(cat?.iceAoeVfx, go.transform.position, Quaternion.identity, 1.5f, 0.5f);
                }
            }
            // 틱(0.5초)마다 대상마다 뜨던 타격 이펙트는 들어온 첫 틱에 한 번만 — 피해 숫자가 틱을 알린다(10-11 톤 2차 V-10)
            if (!shard && _hitShown.Add(go))
                LegendaryRuntime.SpawnVfx(LegendaryRuntime.Catalog?.iceFieldHitVfx, go.transform.position, Quaternion.identity, 1.5f, 0.8f);
        }

        if (n > 0)
            Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }
}
