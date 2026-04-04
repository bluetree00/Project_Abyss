using System;
using UnityEngine;

namespace Abyss.Monster
{
public class DragonBossBlackboard
{
    public enum DragonElement { Ice, Thunder, Fire }

    private Func<float> _hpRatioGetter;

    public Vector3 SpawnPosition { get; set; }
    public float SpawnY { get; set; }

    public DragonElement CurrentElement
    {
        get
        {
            float r = _hpRatioGetter != null ? _hpRatioGetter() : 1f;
            if (r > 0.7f) return DragonElement.Ice;
            if (r > 0.4f) return DragonElement.Thunder;
            return DragonElement.Fire;
        }
    }

    private bool _summon80Used;
    private bool _summon50Used;
    private bool _summon10Used;

    public int ActiveMiniDragonCount = 0;
    public bool ThunderShieldActive = false;
    public string LastPatternTag = "";
    public float NormalModeTimer = 0f;

    public void Init(Func<float> hpRatioGetter) => _hpRatioGetter = hpRatioGetter;

    public bool IsSummonTriggered(float threshold)
    {
        if (threshold >= 0.79f) return _summon80Used;
        if (threshold >= 0.49f) return _summon50Used;
        return _summon10Used;
    }

    public void MarkSummonUsed(float threshold)
    {
        if (threshold >= 0.79f) _summon80Used = true;
        else if (threshold >= 0.49f) _summon50Used = true;
        else _summon10Used = true;
    }

    public void Reset()
    {
        _summon80Used = false;
        _summon50Used = false;
        _summon10Used = false;
        ActiveMiniDragonCount = 0;
        ThunderShieldActive = false;
        LastPatternTag = "";
        NormalModeTimer = 0f;
    }
}
}
