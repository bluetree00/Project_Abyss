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
            LegendaryRuntime.SpawnVfx(LegendaryRuntime.Catalog?.iceFieldHitVfx, go.transform.position, Quaternion.identity, 1.5f, 0.8f);

            var mb = go.GetComponentInParent<MonsterBase>();
            if (mb == null) continue;

            mb.Status.ApplySlow("ice_zone_slow", SlowMagnitude, SlowDuration);

            if (mb.Status.HasCc("freeze") && !mb.Status.IsOnCooldown("ice_zone_shard"))
            {
                mb.Status.SetCooldown("ice_zone_shard", ShardCooldown);
                float shardDmg = _attackPower * ShardMult;
                CombatQuery.DealSynergyDamage(go, shardDmg, instigator, element: RuneElement.Ice);

                var cat = LegendaryRuntime.Catalog;
                LegendaryRuntime.SpawnVfx(cat?.iceAoeVfx, go.transform.position, Quaternion.identity, 1.5f, 0.8f);
            }
        }

        if (n > 0)
            Managers.Sound?.PlayEffectAsync(s_hitSfx[Random.Range(0, s_hitSfx.Length)]).Forget();
    }
}
