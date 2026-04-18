using UnityEngine;

namespace Abyss.Monster
{
public sealed class DragonSummonEgg : MonoBehaviour
{
    private Vector3 _targetPos;
    private float   _dropDuration;
    private float   _crackDelay;

    private GameObject           _miniDragonPrefab;
    private float                _miniScale;
    private int                  _miniHp;
    private float                _miniSpeed;
    private float                _miniBreathRange;
    private float                _miniBreathCooldown;
    private int                  _miniBreathDamage;
    private float                _miniBreathSpeed;
    private GameObject           _miniBreathPrefab;
    private Color                _miniColor;
    private Transform            _playerTarget;
    private PlayerStatusEffectSO _statusEffect;
    private int                  _orbitIndex;

    private Vector3  _startPos;
    private float    _timer;
    private bool     _cracked;
    private Animator _animator;

    private static readonly int EggCrackHash = Animator.StringToHash("EggCracking");

    public System.Action<DragonMiniDragon> OnMiniDragonSpawned;

    public void Init(
        Vector3 targetPos, float dropDuration, float crackDelay,
        Color eggColor,
        GameObject miniDragonPrefab, float miniScale,
        int miniHp, float miniSpeed,
        float miniBreathRange, float miniBreathCooldown,
        int miniBreathDamage, float miniBreathSpeed,
        GameObject miniBreathPrefab, Color miniColor,
        Transform playerTarget,
        int orbitIndex = 0,
        PlayerStatusEffectSO statusEffect = null)
    {
        _targetPos          = targetPos;
        _dropDuration       = Mathf.Max(dropDuration, 0.1f);
        _crackDelay         = crackDelay;
        _miniDragonPrefab   = miniDragonPrefab;
        _miniScale          = Mathf.Max(miniScale, 0.05f);
        _miniHp             = miniHp;
        _miniSpeed          = miniSpeed;
        _miniBreathRange    = miniBreathRange;
        _miniBreathCooldown = miniBreathCooldown;
        _miniBreathDamage   = miniBreathDamage;
        _miniBreathSpeed    = miniBreathSpeed;
        _miniBreathPrefab   = miniBreathPrefab;
        _miniColor          = miniColor;
        _playerTarget       = playerTarget;
        _orbitIndex         = orbitIndex;
        _statusEffect       = statusEffect;
        _startPos           = transform.position;
        _timer              = 0f;
        _cracked            = false;

        _animator = GetComponentInChildren<Animator>(true);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))    mat.SetColor("_BaseColor",    eggColor);
                if (mat.HasProperty("_Color"))        mat.SetColor("_Color",        eggColor);
                if (mat.HasProperty("_TintColor"))    mat.SetColor("_TintColor",    eggColor);
                if (mat.HasProperty("_MainColor"))    mat.SetColor("_MainColor",    eggColor);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", eggColor * 0.4f);
                    mat.EnableKeyword("_EMISSION");
                }
            }
        }
    }

    private void Update()
    {
        _timer += Time.deltaTime;

        if (_timer <= _dropDuration)
        {
            float t = _timer / _dropDuration;
            transform.position = Vector3.Lerp(_startPos, _targetPos, t);
            return;
        }

        if (!_cracked && _timer >= _dropDuration + _crackDelay)
        {
            _cracked = true;
            Crack();
        }
    }

    private void Crack()
    {
        if (_animator != null && _animator.HasState(0, EggCrackHash))
            _animator.CrossFade("EggCracking", 0.1f, 0, 0f);
        SpawnMiniDragon();
        Destroy(gameObject, 4f);
    }

    private void SpawnMiniDragon()
    {
        if (_miniDragonPrefab == null) return;

        var go = Instantiate(_miniDragonPrefab, _targetPos, Quaternion.identity);
        go.transform.localScale = Vector3.one * _miniScale;

        var mini = go.GetComponent<DragonMiniDragon>() ?? go.AddComponent<DragonMiniDragon>();
        mini.Init(
            hp:             _miniHp,
            speed:          _miniSpeed,
            breathRange:    _miniBreathRange,
            breathCooldown: _miniBreathCooldown,
            breathDamage:   _miniBreathDamage,
            breathSpeed:    _miniBreathSpeed,
            breathPrefab:   _miniBreathPrefab,
            tintColor:      _miniColor,
            playerTarget:   _playerTarget,
            statusEffect:   _statusEffect,
            orbitIndex:     _orbitIndex);

        OnMiniDragonSpawned?.Invoke(mini);
    }
}
}
