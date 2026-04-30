using Abyss.Monster;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Worm monster override that keeps the mesh underground while patrolling,
/// chasing, and winding up attacks, then emerges only for the actual attack.
/// </summary>
[CreateAssetMenu(fileName = "WormPatrolOverride", menuName = "Lee/Monster/Override/WormPatrolOverride")]
public class WormPatrolOverrideSO : MonsterStateOverrideSO
{
    [Tooltip("How far the visual root is moved downward while burrowed.")]
    public float burrowDepth = 0.6f;

    [Tooltip("Hide worm renderers completely while burrowed.")]
    public bool hideRenderersWhileBurrowed = true;

    [Tooltip("Optional effect played when the worm burrows.")]
    public GameObject burrowEffectPrefab;

    [Tooltip("Optional looping effect while the worm moves underground. Falls back to burrowEffectPrefab when empty.")]
    public GameObject undergroundMoveEffectPrefab;

    [Tooltip("Seconds between underground move effect pulses while the worm is buried and moving.")]
    public float undergroundMoveEffectInterval = 0.35f;

    [Tooltip("Optional effect played when the worm emerges.")]
    public GameObject emergeEffectPrefab;

    public override void RegisterOverrides(MonsterFSM fsm, MonsterBase monster)
    {
        var burrowController = new BurrowVisualController(monster, this);
        monster.RegisterOnEnabledCallback(burrowController.ResetToSurface);

        fsm.RegisterAs<PatrolState>(new WormUndergroundPatrolState(burrowController));
        fsm.RegisterAs<ChaseState>(new WormUndergroundChaseState(burrowController));
        fsm.RegisterAs<AttackReadyState>(new WormUndergroundAttackReadyState(burrowController));
        fsm.RegisterAs<AttackState>(new WormSurfaceAttackState(burrowController));
    }

    private sealed class BurrowVisualController
    {
        private readonly MonsterBase _monster;
        private readonly Transform _monsterRoot;
        private readonly WormPatrolOverrideSO _so;

        private Transform _visualRoot;
        private Transform _effectPoolRoot;
        private Renderer[] _renderers;
        private Vector3 _originalLocalPos;
        private bool _isBurrowed;
        private float _moveEffectTimer;
        private readonly Dictionary<int, WormEffectPool> _effectPools = new();

        public BurrowVisualController(MonsterBase monster, WormPatrolOverrideSO so)
        {
            _monster = monster;
            _monsterRoot = monster.transform;
            _so = so;
        }

        public void Burrow(MonsterContext ctx)
        {
            CacheVisualRoot();
            if (_isBurrowed || _visualRoot == null) return;

            _visualRoot.localPosition = _originalLocalPos + Vector3.down * _so.burrowDepth;
            SetRenderersEnabled(!_so.hideRenderersWhileBurrowed);
            _isBurrowed = true;
            _moveEffectTimer = 0f;
            _monster.HideWorldHPBar();
            SpawnEffect(_so.burrowEffectPrefab, ctx.Transform.position);
        }

        public bool IsBurrowed => _isBurrowed;

        public void Emerge(MonsterContext ctx)
        {
            CacheVisualRoot();
            if (!_isBurrowed || _visualRoot == null) return;

            _visualRoot.localPosition = _originalLocalPos;
            SetRenderersEnabled(true);
            _isBurrowed = false;
            _moveEffectTimer = 0f;
            _monster.ShowWorldHPBar();
            SpawnEffect(_so.emergeEffectPrefab, ctx.Transform.position);
        }

        public void ResetToSurface()
        {
            CacheVisualRoot();
            if (_visualRoot != null)
                _visualRoot.localPosition = _originalLocalPos;

            SetRenderersEnabled(true);
            _isBurrowed = false;
            _moveEffectTimer = 0f;
            RecycleEffectPools();
            _monster.ShowWorldHPBar();
        }

        public void TickUnderground(MonsterContext ctx)
        {
            if (!_isBurrowed) return;

            var effectPrefab = _so.undergroundMoveEffectPrefab != null
                ? _so.undergroundMoveEffectPrefab
                : _so.burrowEffectPrefab;
            if (effectPrefab == null) return;

            var agentVelocity = ctx.Agent != null ? ctx.Agent.velocity.sqrMagnitude : 0f;
            var desiredVelocity = ctx.Agent != null ? ctx.Agent.desiredVelocity.sqrMagnitude : 0f;
            if (agentVelocity < 0.01f && desiredVelocity < 0.01f) return;

            _moveEffectTimer -= Time.deltaTime;
            if (_moveEffectTimer > 0f) return;

            _moveEffectTimer = Mathf.Max(0.05f, _so.undergroundMoveEffectInterval);
            SpawnEffect(effectPrefab, ctx.Transform.position);
        }

        private void CacheVisualRoot()
        {
            if (_visualRoot != null) return;

            _visualRoot = FindVisualRoot(_monsterRoot);
            _originalLocalPos = _visualRoot != null ? _visualRoot.localPosition : Vector3.zero;
            _renderers = _visualRoot != null
                ? _visualRoot.GetComponentsInChildren<Renderer>(true)
                : _monsterRoot.GetComponentsInChildren<Renderer>(true);
        }

        private static Transform FindVisualRoot(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.GetComponentInChildren<Renderer>(true) != null)
                    return child;
            }

            return root;
        }

        private void SpawnEffect(GameObject prefab, Vector3 position)
        {
            if (prefab == null) return;

            var pool = GetOrCreateEffectPool(prefab);
            var obj = pool.Get(position, Quaternion.identity);
            var lifetime = GetEffectLifetime(obj);

            var returner = obj.GetComponent<WormPooledEffectReturner>();
            if (returner == null)
                returner = obj.AddComponent<WormPooledEffectReturner>();

            returner.ScheduleReturn(pool, lifetime);
        }

        private void SetRenderersEnabled(bool enabled)
        {
            if (_renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        private WormEffectPool GetOrCreateEffectPool(GameObject prefab)
        {
            int key = prefab.GetInstanceID();
            if (_effectPools.TryGetValue(key, out var existingPool))
                return existingPool;

            if (_effectPoolRoot == null)
            {
                var root = new GameObject("[WormEffectPools]");
                _effectPoolRoot = root.transform;
                _effectPoolRoot.SetParent(_monsterRoot, false);
            }

            var container = new GameObject($"[{prefab.name}Pool]");
            container.transform.SetParent(_effectPoolRoot, false);

            var pool = new WormEffectPool(prefab, 4, container.transform);
            _effectPools[key] = pool;
            return pool;
        }

        private void RecycleEffectPools()
        {
            foreach (var pool in _effectPools.Values)
                pool?.RecycleAll();
        }

        private static float GetEffectLifetime(GameObject obj)
        {
            var particleSystems = obj.GetComponentsInChildren<ParticleSystem>(true);
            float lifetime = 0f;

            for (int i = 0; i < particleSystems.Length; i++)
            {
                var ps = particleSystems[i];
                if (ps == null)
                    continue;

                var main = ps.main;
                lifetime = Mathf.Max(
                    lifetime,
                    main.duration + main.startLifetimeMultiplier + 0.35f);
            }

            return lifetime > 0f ? lifetime : 3f;
        }
    }

    private class WormUndergroundPatrolState : PatrolState
    {
        private readonly BurrowVisualController _burrowController;

        public WormUndergroundPatrolState(BurrowVisualController burrowController)
        {
            _burrowController = burrowController;
        }

        public override void Enter(MonsterContext ctx)
        {
            base.Enter(ctx);
            _burrowController.Burrow(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _burrowController.TickUnderground(ctx);
            base.Update(ctx);
        }
    }

    private class WormUndergroundChaseState : ChaseState
    {
        private readonly BurrowVisualController _burrowController;

        public WormUndergroundChaseState(BurrowVisualController burrowController)
        {
            _burrowController = burrowController;
        }

        public override void Enter(MonsterContext ctx)
        {
            base.Enter(ctx);
            _burrowController.Burrow(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _burrowController.TickUnderground(ctx);
            base.Update(ctx);
        }
    }

    private class WormUndergroundAttackReadyState : AttackReadyState
    {
        private readonly BurrowVisualController _burrowController;

        public WormUndergroundAttackReadyState(BurrowVisualController burrowController)
        {
            _burrowController = burrowController;
        }

        public override void Enter(MonsterContext ctx)
        {
            base.Enter(ctx);
            // Preserve current surfaced state during chained melee attacks.
            // Only keep the worm buried here when it is approaching from underground.
            if (_burrowController.IsBurrowed)
                _burrowController.Burrow(ctx);
        }

        public override void Update(MonsterContext ctx)
        {
            _burrowController.TickUnderground(ctx);
            base.Update(ctx);
        }
    }

    private class WormSurfaceAttackState : AttackState
    {
        private readonly BurrowVisualController _burrowController;

        public WormSurfaceAttackState(BurrowVisualController burrowController)
        {
            _burrowController = burrowController;
        }

        public override void Enter(MonsterContext ctx)
        {
            _burrowController.Emerge(ctx);
            base.Enter(ctx);
        }

        public override void Exit(MonsterContext ctx)
        {
            _burrowController.Burrow(ctx);
            base.Exit(ctx);
        }
    }
}

internal sealed class WormPooledEffectReturner : MonoBehaviour
{
    private WormEffectPool _pool;
    private int _playVersion;

    public void ScheduleReturn(WormEffectPool pool, float lifetime)
    {
        _pool = pool;
        _playVersion++;
        ReturnAfterDelayAsync(_playVersion, Mathf.Max(0.05f, lifetime)).Forget();
    }

    private async UniTaskVoid ReturnAfterDelayAsync(int version, float lifetime)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(lifetime));

        if (this == null || version != _playVersion || _pool == null || !gameObject.activeSelf)
            return;

        _pool.Return(gameObject);
    }

    private void OnDisable()
    {
        _playVersion++;
    }

    private void OnDestroy()
    {
        _playVersion++;
    }
}

internal sealed class WormEffectPool
{
    private readonly GameObject _prefab;
    private readonly Transform _container;
    private readonly Queue<GameObject> _idle = new();
    private readonly HashSet<GameObject> _active = new();

    public WormEffectPool(GameObject prefab, int initialSize, Transform container)
    {
        _prefab = prefab;
        _container = container;

        for (int i = 0; i < initialSize; i++)
            _idle.Enqueue(CreateInstance());
    }

    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        var obj = _idle.Count > 0 ? _idle.Dequeue() : CreateInstance();
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        _active.Add(obj);
        return obj;
    }

    public void Return(GameObject obj)
    {
        if (obj == null || !_active.Remove(obj))
            return;

        obj.SetActive(false);
        if (_container != null)
            obj.transform.SetParent(_container, false);
        _idle.Enqueue(obj);
    }

    public void RecycleAll()
    {
        var snapshot = new List<GameObject>(_active);
        for (int i = 0; i < snapshot.Count; i++)
            Return(snapshot[i]);
    }

    private GameObject CreateInstance()
    {
        var obj = UnityEngine.Object.Instantiate(_prefab, _container);
        obj.SetActive(false);
        return obj;
    }
}
