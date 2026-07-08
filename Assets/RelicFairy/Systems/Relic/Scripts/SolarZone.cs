using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 작열 지대 — 가웨인 낙일(태양 강림) 착탄 지점에 남는 지속 화염 장판.
/// 수명 동안 주기 틱으로 반경 내 적에게 화염 피해(방어 일부 무시) + 태양 화상 부여.
/// 룬 필드 시스템 부재 → 자체 경량 구현(소수 인스턴스, 수명 후 Destroy).
/// </summary>
public sealed class SolarZone : MonoBehaviour
{
    private float      _radius, _tickInterval, _tickDamage, _burnDps;
    private GameObject _instigator;
    private float      _life, _tickAccum;
    private readonly List<MonsterBase> _buffer = new(16);

    public static SolarZone Spawn(Vector3 pos, float radius, float duration,
                                  float tickDamage, float burnDps, float tickInterval, GameObject instigator)
    {
        var go = new GameObject("SolarZone") { transform = { position = pos } };
        var z = go.AddComponent<SolarZone>();
        z._radius = radius; z._life = duration; z._tickDamage = tickDamage;
        z._burnDps = burnDps; z._tickInterval = Mathf.Max(0.1f, tickInterval); z._instigator = instigator;
        return z;
    }

    private void Update()
    {
        _life -= Time.deltaTime;
        _tickAccum += Time.deltaTime;
        if (_tickAccum >= _tickInterval)
        {
            _tickAccum -= _tickInterval;
            CombatQuery.GetNearbyEnemies(transform.position, _radius, _instigator, 32, _buffer);
            foreach (var mb in _buffer)
            {
                if (mb == null) continue;
                if (_tickDamage > 0f) CombatQuery.DealSynergyDamage(mb, _tickDamage, _instigator, 0.5f);
                if (_burnDps > 0f)
                    MonsterBurnHandler.Apply(mb.gameObject, _burnDps, _tickInterval * 2f, _tickInterval, _instigator);
            }
        }
        if (_life <= 0f) Destroy(gameObject);
    }
}
