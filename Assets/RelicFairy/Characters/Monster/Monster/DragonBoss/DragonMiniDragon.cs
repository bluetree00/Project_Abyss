using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// Mini dragon summoned during boss summon pattern.
/// Chase → in breath range: attack ready (Idle) → fire breath (U Attack Fire Ball).
/// Periodically side-steps left/right.
/// </summary>
public sealed class DragonMiniDragon : MonoBehaviour, IDamageable, IKillable
{
    private int                  _maxHp;
    private int                  _currentHp;
    private float                _breathRange;
    private float                _breathCooldown;
    private int                  _breathDamage;
    private float                _breathSpeed;
    private GameObject           _breathPrefab;
    private PlayerStatusEffectSO _statusEffect;
    private Transform            _playerTarget;
    private int                  _orbitIndex;
    private float                _speedVariance;

    private NavMeshAgent _agent;
    private Animator     _animator;
    private Transform    _mouthBone;
    private float        _breathTimer;

    // 사이드스텝
    private float _sideStepTimer;
    private float _nextSideStepTime;
    private float _sideStepActiveTimer;
    private bool  _isSideStepping;
    private bool  _wasInBreathRange;

    // 브레스 발사 애니메이션 제어
    private bool  _isFiring;
    private float _fireMinTimer; // CrossFade 시작 최소 대기시간

    // 브레스 예고(10-08 점검)
    private bool       _telegraphing;
    private float      _telegraphTimer;
    private Vector3    _aimPoint;
    private GameObject _breathGuide;
    private Color      _tint;
    private const float FireMinGuard = 0.25f;

    public bool IsDead { get; private set; }
    public System.Action<DragonMiniDragon> OnDied;

    private const float FaceRotateSpeed  = 480f;
    private const float SideStepMin      = 3f;
    private const float SideStepMax      = 7f;
    private const float SideStepDuration = 0.65f;
    private const float SideStepDist     = 2.5f;
    private const float AppearSeconds    = 0.5f;   // 디졸브 등장 · 퇴장(10-03 개선 3-5 — 예전엔 뿅 생기고 그냥 파괴)
    private const float DisappearSeconds = 0.8f;
    private const float DeathLinger      = 3f;     // 사망 애니 뒤 사라지기까지(퇴장 디졸브 포함)
    private const float BreathTelegraph  = 0.6f;   // 브레스 예고 — 바닥 화살표가 다 차면 그 줄로 쏜다(10-08 점검: 예고 없이 바로 쐈다)
    private const float BreathGuideWidth = 1.6f;   // 탄 판정(반경 0.4) + 플레이어 몸

    private static readonly int WalkChaseHash     = Animator.StringToHash("WalkChase");
    private static readonly int AttackReadyHash   = Animator.StringToHash("AttackReady");
    private static readonly int FireBallHash      = Animator.StringToHash("U Attack Fire Ball");
    private static readonly int DeathHash         = Animator.StringToHash("Death");
    private static readonly int DodgeLeftHash     = Animator.StringToHash("UGround Dodge Left");
    private static readonly int DodgeRightHash    = Animator.StringToHash("UGround Dodge Right");

    // ── 초기화 ──────────────────────────────────────────────────────────────

    public void Init(
        int hp, float speed,
        float breathRange, float breathCooldown,
        int breathDamage, float breathSpeed,
        GameObject breathPrefab,
        Color tintColor,
        Transform playerTarget,
        PlayerStatusEffectSO statusEffect = null,
        int orbitIndex = 0)
    {
        _maxHp          = hp;
        _currentHp      = hp;
        _breathRange    = breathRange;
        _breathCooldown = breathCooldown;
        _breathDamage   = breathDamage;
        _breathSpeed    = breathSpeed;
        _breathPrefab   = breathPrefab;
        _statusEffect   = statusEffect;
        _playerTarget   = playerTarget;
        _orbitIndex     = orbitIndex;
        _speedVariance  = Random.Range(0.85f, 1.2f);

        // 세 마리 브레스 타이밍 엇갈리기
        _breathTimer      = (breathCooldown / 3f) * orbitIndex
                          + Random.Range(0f, breathCooldown * 0.3f);
        _nextSideStepTime = Random.Range(SideStepMin, SideStepMax);
        _sideStepTimer    = Random.Range(0f, SideStepMin);

        _tint = tintColor;
        ApplyTint(tintColor);

        if (_agent != null)
        {
            _agent.speed            = speed * _speedVariance;
            _agent.stoppingDistance = 0.5f;
            _agent.radius           = 0.25f;
            _agent.height           = 0.55f;
            _agent.acceleration     = 30f;
            _agent.angularSpeed     = 720f;
            _agent.autoBraking      = true;
        }

        // 기본 상태(Idle_Normal)는 모션 없음 → 스폰 직후 WalkChase로 즉시 전환해 메쉬 정지 방지
        if (_animator != null && _animator.HasState(0, WalkChaseHash))
            _animator.Play("WalkChase", 0, 0f);

        DissolveEffect.PlayAppear(gameObject, AppearSeconds);   // 틴트(ApplyTint)를 입힌 뒤
    }

    private void Awake()
    {
        if (!TryGetComponent(out _agent))
            _agent = gameObject.AddComponent<NavMeshAgent>();
        _animator  = GetComponentInChildren<Animator>(true);
        _mouthBone = FindDeepChild("Jaw");

        int hitLayer = LayerMask.NameToLayer("MonsterHit");
        if (hitLayer >= 0)
            foreach (var col in GetComponentsInChildren<Collider>(true))
                col.gameObject.layer = hitLayer;
    }

    // ── Update ──────────────────────────────────────────────────────────────

    private void Update()
    {
        if (IsDead || _playerTarget == null) return;

        _breathTimer   += Time.deltaTime;
        _sideStepTimer += Time.deltaTime;

        float distToPlayer = Vector3.Distance(transform.position, _playerTarget.position);
        bool  inBreathRange = distToPlayer <= _breathRange;

        // 추적→공격 전환 시 사이드스텝 타이머 리셋: 공격 먼저 실행되도록
        if (inBreathRange && !_wasInBreathRange)
        {
            _sideStepTimer    = 0f;
            _nextSideStepTime = Random.Range(SideStepMin, SideStepMax);
        }
        _wasInBreathRange = inBreathRange;

        UpdateSideStep();
        if (_isSideStepping) return;

        if (inBreathRange || _telegraphing)   // 예고를 깔았으면 사거리를 벗어나도 그 줄로 쏜다(10-08 점검)
        {
            StopAndFace();
            UpdateFiring();
        }
        else
        {
            ChasePlayer();
        }
    }

    private void OnDestroy() => CancelBreathTelegraph();

    /// <summary>보스 사망 시 소환수를 즉시 처리한다. 사망 애니메이션 없이 OnDied만 발행하고 디졸브로 사라진다(10-03).</summary>
    public void ForceKill()
    {
        if (IsDead) return;
        IsDead = true;
        if (_agent != null) _agent.enabled = false;
        CancelBreathTelegraph();
        OnDied?.Invoke(this);
        VanishAsync(0f).Forget();
    }

    // ── IDamageable ──────────────────────────────────────────────────────────

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (IsDead) return;
        _currentHp -= Mathf.RoundToInt(amount);
        DamagePopupSpawner.Spawn(transform.position + Vector3.up * 1.2f, amount, isCrit, GetInstanceID());
        if (_currentHp <= 0) Die();
    }

    // ── 사이드스텝 ────────────────────────────────────────────────────────────

    private void UpdateSideStep()
    {
        if (_isSideStepping)
        {
            FaceTarget();
            _sideStepActiveTimer += Time.deltaTime;
            if (_sideStepActiveTimer >= SideStepDuration)
            {
                _isSideStepping   = false;
                _sideStepTimer    = 0f;
                _nextSideStepTime = Random.Range(SideStepMin, SideStepMax);
                if (_agent != null) _agent.updateRotation = true;
            }
            return;
        }

        if (_sideStepTimer >= _nextSideStepTime && !_isFiring && !_telegraphing)   // 예고 중엔 자리를 지킨다(예고 줄이 어긋난다)
            BeginSideStep();
    }

    private void BeginSideStep()
    {
        _isSideStepping      = true;
        _sideStepActiveTimer = 0f;

        if (_agent != null) _agent.updateRotation = false;

        bool    goRight  = Random.value > 0.5f;
        Vector3 toPlayer = (_playerTarget.position - transform.position).normalized;
        Vector3 right    = Vector3.Cross(Vector3.up, toPlayer).normalized;
        Vector3 target   = transform.position + right * (goRight ? 1f : -1f) * SideStepDist;

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(target);
        }

        int    hash = goRight ? DodgeRightHash : DodgeLeftHash;
        string name = goRight ? "UGround Dodge Right" : "UGround Dodge Left";
        if (_animator != null && _animator.HasState(0, hash))
            _animator.CrossFade(name, 0.1f);
    }

    // ── 이동 / 공격 ──────────────────────────────────────────────────────────

    private void ChasePlayer()
    {
        _isFiring = false;
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(_playerTarget.position);
        }
        if (_animator == null || _animator.IsInTransition(0)) return;
        if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash != WalkChaseHash)
            _animator.CrossFade("WalkChase", 0.15f);
    }

    private void StopAndFace()
    {
        if (_agent != null && _agent.isOnNavMesh && !_agent.isStopped)
            _agent.isStopped = true;
        FaceTarget();

        // 발사 애니메이션 중에는 덮지 않음
        if (_isFiring) return;
        if (_animator == null || _animator.IsInTransition(0)) return;
        if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash != AttackReadyHash)
            _animator.CrossFade("AttackReady", 0.15f);
    }

    private void UpdateFiring()
    {
        if (_isFiring)
        {
            _fireMinTimer -= Time.deltaTime;
            // CrossFade 안착 후 normalizedTime으로 첫 1회 완료 감지
            if (_fireMinTimer <= 0f && _animator != null && !_animator.IsInTransition(0))
            {
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == FireBallHash && info.normalizedTime >= 0.85f)
                    _isFiring = false;
            }
            return;
        }

        if (_telegraphing)
        {
            _telegraphTimer += Time.deltaTime;
            PatternGuideHelper.SetProgress(_breathGuide, _telegraphTimer / BreathTelegraph);
            if (_telegraphTimer < BreathTelegraph) return;

            _telegraphing = false;
            _isFiring     = true;
            _fireMinTimer = FireMinGuard;

            if (_animator != null && _animator.HasState(0, FireBallHash))
                _animator.CrossFade("U Attack Fire Ball", 0.1f);

            FireBreath();
            return;
        }

        if (_breathTimer < _breathCooldown) return;

        _breathTimer = 0f;
        BeginBreathTelegraph();
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    private void Die()
    {
        IsDead = true;
        if (_agent != null) _agent.enabled = false;
        if (_animator != null && _animator.HasState(0, DeathHash))
            _animator.CrossFade("Death", 0.15f);
        CancelBreathTelegraph();
        OnDied?.Invoke(this);
        VanishAsync(DeathLinger - DisappearSeconds).Forget();
    }

    /// <summary><paramref name="delay"/>초 뒤 디졸브로 사라진 다음 파괴한다(퇴장은 재질을 되돌리지 않으므로 파괴 직전에만).</summary>
    private async UniTaskVoid VanishAsync(float delay)
    {
        var ct = destroyCancellationToken;
        try
        {
            if (delay > 0f)
                await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: ct);
            await DissolveEffect.PlayDisappearAsync(gameObject, DisappearSeconds, ct);
        }
        catch (System.OperationCanceledException) { return; }
        if (this != null) Destroy(gameObject);
    }

    private void FaceTarget()
    {
        Vector3 dir = (_telegraphing ? _aimPoint : _playerTarget.position) - transform.position;   // 예고 중엔 겨눈 자리를 본다
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(dir),
                FaceRotateSpeed * Time.deltaTime);
    }

    /// <summary>쏠 자리를 정하고 바닥에 예고 화살표(속성 색)를 깐다 — 다 차면 그 자리로 쏜다(10-08 점검).</summary>
    private void BeginBreathTelegraph()
    {
        _telegraphing   = true;
        _telegraphTimer = 0f;
        _aimPoint       = _playerTarget.position + Vector3.up * 1f;

        Vector3 mouth  = MouthPos();
        Vector3 flat   = _aimPoint - mouth;
        Vector3 origin = new Vector3(mouth.x, transform.position.y, mouth.z);
        Color   c      = new Color(_tint.r, _tint.g, _tint.b, 0.85f);
        PatternGuideHelper.SafeDestroy(ref _breathGuide);
        _breathGuide = PatternGuideHelper.Prepare(
            PatternGuideHelper.Beam(origin, flat, _breathRange * 1.5f, BreathGuideWidth, c), c);
    }

    private void CancelBreathTelegraph()
    {
        _telegraphing = false;
        PatternGuideHelper.SafeDestroy(ref _breathGuide);
    }

    private Vector3 MouthPos() => _mouthBone != null
        ? _mouthBone.position
        : transform.position + Vector3.up * (transform.localScale.y * 1.8f);

    private void FireBreath()
    {
        if (_breathPrefab == null) { CancelBreathTelegraph(); return; }

        Vector3 spawnPos = MouthPos();
        Vector3 dir = (_aimPoint - spawnPos).normalized;   // 예고 때 겨눈 자리로

        var go   = Instantiate(_breathPrefab, spawnPos, Quaternion.LookRotation(dir));
        var shot = go.AddComponent<DragonBreathShot>();
        PatternGuideHelper.Arm(_breathGuide);   // 날아가는 동안 판정 색 — 탄이 맞거나 사거리 끝에 닿으면 탄이 치운다
        shot.Init(dir, _breathSpeed, _breathRange * 1.5f, _breathDamage, _statusEffect, gameObject, _breathGuide);
        _breathGuide = null;
    }

    private Transform FindDeepChild(string boneName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    private void ApplyTint(Color tint)
    {
        // 베이스 텍스처/색상은 유지하고 속성 색상을 발광(Emission)으로만 포인트
        var block = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", tint * 0.45f);
            r.SetPropertyBlock(block);

            // Emission 키워드는 머티리얼에 직접 활성화 (MaterialPropertyBlock은 키워드 제어 불가)
            foreach (var mat in r.sharedMaterials)
                mat?.EnableKeyword("_EMISSION");
        }
    }
}
}
