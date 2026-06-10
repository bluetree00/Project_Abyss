using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 갈라하드 Q스킬 — 성스러운 방패.
/// 전방에 방패 오브젝트를 전개해 일정 시간 유지하며:
///   1) 방패 주변 적을 방패 위치로 흡인
///   2) 흡인 범위 내 적에게 주기적 피해
///   3) 활성 중 갈라하드 전방 120° 피해를 일정 % 감소 (Galahad.IsHitFrontal 사용)
/// 지속시간 종료 시 방패 소멸 + 갈라하드의 전방 블록 플래그 해제.
/// </summary>
public class HolyShieldSkillRuntime : ISkillRuntime
{
    // ── Constants ─────────────────────────────────────────────
    private const string AnimName        = "QSkill_01"; // 컨트롤러 기존 스테이트 재사용
    private const float  AnimDuration    = 0.6f;   // 시전 모션
    private const float  ShieldDuration  = 4.0f;   // 방패 유지 시간
    private const float  ShieldForward   = 2.2f;   // 방패 전방 거리
    private const float  PullRadius      = 5.0f;   // 흡인 범위
    private const float  PullSpeed       = 4.5f;   // 흡인 속도(m/s)
    private const float  DamageRadius    = 2.5f;   // 지속 피해 범위
    private const float  DamageTickRate  = 0.5f;
    private const float  BaseTickDamage  = 18f;

    // ── Private ───────────────────────────────────────────────
    private Galahad _galahad;
    private GameObject _shieldGo;
    private HolyShieldZone _zone;
    private float _animElapsed;
    private bool  _deployed;

    public void OnEnter(SkillExecutionContext ctx)
    {
        _animElapsed = 0f;
        _deployed    = false;
        _galahad     = ctx.Controller as Galahad;

        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        ctx.Animator?.CrossFade(AnimName, 0.1f);

        // 필살기 카메라 연출 — 유물 우선, 없으면 캐릭터 데이터 폴백 (설정 있을 때만)
        var cine = ctx.Controller?.RelicClass?.QSkillCinematic ?? ctx.Controller?.CharacterData?.QSkillCinematic;
        if (cine != null)
            UltimateCinematicService.Play(cine, ctx.Controller.transform).Forget();
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _animElapsed += Time.deltaTime;

        if (!_deployed && _animElapsed >= AnimDuration * 0.5f)
        {
            DeployShield(ctx);
            _deployed = true;
        }

        if (_animElapsed >= AnimDuration)
            ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx)
    {
        ctx.SetMoveScale(1f);
        // 방패 오브젝트는 자체 수명으로 유지되므로 여기서 정리하지 않음
    }

    // ── Private Methods ───────────────────────────────────────
    private void DeployShield(SkillExecutionContext ctx)
    {
        Transform pt = ctx.PlayerTransform;
        float tickDamage = ctx.CalculateDamage(BaseTickDamage);

        // 시전자(갈라하드) 의 자식으로 부착 → 위치/방향이 자동 추종.
        _shieldGo = new GameObject("~HolyShield");
        _shieldGo.transform.SetParent(pt, worldPositionStays: false);
        _shieldGo.transform.localPosition = new Vector3(0f, 0f, ShieldForward);
        _shieldGo.transform.localRotation = Quaternion.identity;

        _zone = _shieldGo.AddComponent<HolyShieldZone>();
        _zone.Initialize(
            instigator:   ctx.Controller.gameObject,
            duration:     ShieldDuration,
            pullRadius:   PullRadius,
            pullSpeed:    PullSpeed,
            damageRadius: DamageRadius,
            tickInterval: DamageTickRate,
            tickDamage:   tickDamage);

        // 가이드 비주얼 (흡인 범위 디스크 + 피해 범위 디스크 + 실제 방패 본체) 부착
        Transform shieldBody = BuildShieldGuide(_shieldGo.transform, PullRadius, DamageRadius);

        // 타격/흡인 판정 중심을 방패 본체(시각 오브젝트)에 일치시킴
        _zone.SetHitOrigin(shieldBody);

        // 방패 활성화 동안 전방 블록 플래그 ON (유물 경로 우선, 없으면 레거시 Galahad)
        if (ctx.Controller != null && ctx.Controller.RelicBehavior is GalahadRelic gr)
            gr.SetHolyShieldActive(ShieldDuration, pt.forward);
        else if (_galahad != null)
            _galahad.SetHolyShieldActive(ShieldDuration, pt.forward);
    }

    private static Transform BuildShieldGuide(Transform parent, float pullRadius, float damageRadius)
    {
        // ── Pull radius disc (외곽 바닥 표시, 옅은 청백색) ──
        var pullDisc = CreatePrimitive(PrimitiveType.Cylinder, "Guide_PullArea",
            new Color(0.5f, 0.85f, 1f, 0.18f));
        pullDisc.transform.SetParent(parent, false);
        pullDisc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        pullDisc.transform.localScale    = new Vector3(pullRadius * 2f, 0.01f, pullRadius * 2f);

        // ── Damage radius disc (내부 바닥 표시, 진한 황금색) ──
        var dmgDisc = CreatePrimitive(PrimitiveType.Cylinder, "Guide_DamageArea",
            new Color(1f, 0.85f, 0.3f, 0.35f));
        dmgDisc.transform.SetParent(parent, false);
        dmgDisc.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        dmgDisc.transform.localScale    = new Vector3(damageRadius * 2f, 0.01f, damageRadius * 2f);

        // ── 실제 방패 본체 (수직 디스크, 캐릭터 정면 향함) ──
        // Cylinder 의 Y축이 기본 위 방향 → X축으로 90° 회전 시 축이 Z(전방) 와 일치 → 디스크 면이 전방 향함.
        var shieldBody = CreatePrimitive(PrimitiveType.Cylinder, "ShieldBody",
            new Color(1f, 0.92f, 0.55f, 0.55f));
        shieldBody.transform.SetParent(parent, false);
        shieldBody.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        shieldBody.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shieldBody.transform.localScale    = new Vector3(damageRadius * 2f, 0.08f, damageRadius * 2f);

        return shieldBody.transform;
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (go.TryGetComponent<Collider>(out var col)) Object.Destroy(col);

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (shader != null && shader.name.Contains("Universal"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.SetFloat("_ZWrite",  0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }
        go.GetComponent<MeshRenderer>().material = mat;
        return go;
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// HolyShieldZone — 전개된 방패가 흡인 + 지속 피해를 관리
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
public sealed class HolyShieldZone : MonoBehaviour
{
    private GameObject _instigator;
    private Transform  _hitOrigin;       // 타격/흡인 판정 중심 — 방패 본체(ShieldBody)
    private float _remaining;
    private float _pullRadius;
    private float _pullSpeed;
    private float _damageRadius;
    private float _tickInterval;
    private float _tickDamage;
    private float _tickAccum;

    private static readonly Collider[] _buffer = new Collider[32];
    private readonly HashSet<GameObject> _tickHit = new();

    /// <summary>타격/흡인 판정의 중심점이 될 트랜스폼을 설정.
    /// 미설정 시 zone 자체 transform 사용.</summary>
    public void SetHitOrigin(Transform origin) => _hitOrigin = origin;

    private Vector3 HitCenter => _hitOrigin != null ? _hitOrigin.position : transform.position;

    public void Initialize(GameObject instigator, float duration,
        float pullRadius, float pullSpeed,
        float damageRadius, float tickInterval, float tickDamage)
    {
        _instigator   = instigator;
        _remaining    = duration;
        _pullRadius   = pullRadius;
        _pullSpeed    = pullSpeed;
        _damageRadius = damageRadius;
        _tickInterval = tickInterval;
        _tickDamage   = tickDamage;
    }

    private void Update()
    {
        _remaining -= Time.deltaTime;
        _tickAccum += Time.deltaTime;

        PullEnemies();

        if (_tickAccum >= _tickInterval)
        {
            ApplyTickDamage();
            _tickAccum -= _tickInterval;
        }

        if (_remaining <= 0f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 흡인 중 자동 갱신을 멈췄던 NavMeshAgent 들 복원
        foreach (var a in _suspendedAgents)
        {
            if (a == null) continue;
            a.updatePosition = true;
            a.updateRotation = true;
        }
        _suspendedAgents.Clear();
    }

    private readonly HashSet<NavMeshAgent> _suspendedAgents = new();

    private bool IsInstigatorHierarchy(Collider col)
    {
        // 콜라이더가 시전자(플레이어) 자신 또는 그 자식 GameObject 인지 판정.
        // 콜라이더가 루트가 아닌 자식 GO 에 붙어 있는 경우(갈라하드 프리팹 구조)를 위해 IsChildOf 사용.
        if (_instigator == null) return false;
        return col.transform == _instigator.transform || col.transform.IsChildOf(_instigator.transform);
    }

    private void PullEnemies()
    {
        // 같은 루트(예: 몬스터 본+콜라이더 다중 GO)에서 중복 처리되지 않게 추적
        _pulledThisFrame.Clear();
        Vector3 center = HitCenter;
        int count = Physics.OverlapSphereNonAlloc(center, _pullRadius, _buffer);
        for (int i = 0; i < count; i++)
        {
            var col = _buffer[i];
            if (col == null) continue;
            if (IsInstigatorHierarchy(col)) continue;

            // IDamageable 은 보통 몬스터 루트(MonsterBase) 에 부착됨.
            // 콜라이더가 자식 GO 에 있는 경우도 처리하기 위해 GetComponentInParent 폴백 사용.
            var dmg = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (dmg == null) continue;

            // IDamageable 의 실제 호스트 GameObject 를 기준으로 1회만 끌어당김
            var host = (dmg as Component)?.transform;
            if (host == null) continue;
            if (!_pulledThisFrame.Add(host)) continue;

            Vector3 toShield = center - host.position;
            toShield.y = 0f;
            float dist = toShield.magnitude;
            if (dist < 0.3f) continue;

            Vector3 step = toShield.normalized * Mathf.Min(_pullSpeed * Time.deltaTime, dist - 0.3f);

            // NavMeshAgent 가 있는 몬스터:
            //   agent.Move()/Warp() 은 다음 프레임에 AI 가 자기 도착지로 재이동시켜 상쇄됨.
            //   해결: updatePosition/Rotation 을 꺼서 AI 자동 갱신을 잠시 중단하고 직접 transform 제어.
            //   Zone 파괴 시 OnDestroy 에서 복원.
            var agent = host.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                if (_suspendedAgents.Add(agent))
                {
                    agent.updatePosition = false;
                    agent.updateRotation = false;
                }
                Vector3 nextPos = host.position + step;
                host.position      = nextPos;
                agent.nextPosition = nextPos; // NavMesh 논리 위치 동기화 (path 계산 정합성 유지)
                continue;
            }

            // Rigidbody (kinematic 포함): MovePosition 으로 물리 동기화 유지
            var rb = host.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.MovePosition(host.position + step);
                continue;
            }

            host.position += step;
        }
    }

    private readonly HashSet<Transform> _pulledThisFrame = new();

    private void ApplyTickDamage()
    {
        _tickHit.Clear();
        int count = Physics.OverlapSphereNonAlloc(HitCenter, _damageRadius, _buffer);
        for (int i = 0; i < count; i++)
        {
            var col = _buffer[i];
            if (col == null) continue;
            if (IsInstigatorHierarchy(col)) continue;

            var d = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            if (d == null) continue;

            var hostGo = (d as Component)?.gameObject;
            if (hostGo == null || _tickHit.Contains(hostGo)) continue;

            d.TakeDamage(_tickDamage, _instigator, knockbackMultiplier: 0f);
            _tickHit.Add(hostGo);
            // 팝업은 대상측(MonsterBase.TakeDamage) 자체 처리
        }
    }
}
