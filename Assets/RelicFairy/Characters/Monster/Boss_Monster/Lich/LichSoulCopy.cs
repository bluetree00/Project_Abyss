using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// F1 「영혼 복제」의 녹화 테이프 — 플레이어의 위치 · 회전 · 애니메이터 상태 · 공격 타격 순간을 짧게 기록한다.
/// 복제체(<see cref="LichSoulCopy"/>)가 <see cref="Delay"/>초 뒤에 이 기록을 되풀이한다. 매 프레임 할당 없음(고정 링 버퍼).
/// </summary>
public sealed class LichSoulTape
{
    // ── Constants ─────────────────────────────────────────────────
    public  const float Delay       = 1.5f;
    private const int   Capacity    = 512;   // 1.5초 × 최대 340fps
    private const int   MaxLayers   = 4;
    private const int   MaxFloats   = 8;
    private const int   MaxAttacks  = 32;

    // ── Private ───────────────────────────────────────────────────
    private readonly float[]      _time   = new float[Capacity];
    private readonly Vector3[]    _pos    = new Vector3[Capacity];
    private readonly Quaternion[] _rot    = new Quaternion[Capacity];
    private readonly int[]        _state  = new int[Capacity * MaxLayers];
    private readonly float[]      _norm   = new float[Capacity * MaxLayers];
    private readonly float[]      _floats = new float[Capacity * MaxFloats];
    private readonly float[]      _attacks = new float[MaxAttacks];
    private readonly int[]        _floatHashes = new int[MaxFloats];

    private PlayerController             _player;
    private Animator                     _anim;
    private PlayerAnimationEventReceiver _receiver;
    private int     _head = -1;
    private int     _count;
    private int     _layers;
    private int     _floatCount;
    private int     _attackCount;   // 누적(링 인덱스 = 누적 % MaxAttacks)

    // ── Properties ────────────────────────────────────────────────
    public PlayerController Player      => _player;
    public Vector3          Origin      { get; private set; }
    public int              AttackCount => _attackCount;
    public int              Layers      => _layers;
    public int              FloatCount  => _floatCount;

    // ── Public Methods ────────────────────────────────────────────
    public bool Begin(PlayerController player)
    {
        End();
        if (player == null || player.Anim == null) return false;
        _player   = player;
        _anim     = player.Anim;
        _head     = -1;
        _count    = 0;
        _attackCount = 0;
        _layers   = Mathf.Min(_anim.layerCount, MaxLayers);
        _floatCount = 0;
        foreach (var p in _anim.parameters)
        {
            if (p.type != AnimatorControllerParameterType.Float || _floatCount >= MaxFloats) continue;
            _floatHashes[_floatCount++] = p.nameHash;
        }
        Origin = player.transform.position;

        _receiver = player.EventReceiver;
        if (_receiver != null) _receiver.OnHitStep += HandleHitStep;
        return true;
    }

    public void End()
    {
        if (_receiver != null) _receiver.OnHitStep -= HandleHitStep;
        _receiver = null;
        _player   = null;
        _anim     = null;
    }

    /// <summary>한 프레임 기록 — 패턴 상태의 Update에서 부른다.</summary>
    public void Record()
    {
        if (_player == null || _anim == null) return;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;

        _time[_head] = Time.time;
        _pos[_head]  = _player.transform.position;
        _rot[_head]  = _player.transform.rotation;
        for (int l = 0; l < _layers; l++)
        {
            var info = _anim.GetCurrentAnimatorStateInfo(l);
            _state[_head * MaxLayers + l] = info.fullPathHash;
            _norm [_head * MaxLayers + l] = info.normalizedTime;
        }
        for (int f = 0; f < _floatCount; f++)
            _floats[_head * MaxFloats + f] = _anim.GetFloat(_floatHashes[f]);
    }

    /// <summary><paramref name="t"/> 시각(이전 중 가장 가까운) 기록의 인덱스. 없으면 −1.</summary>
    public int Find(float t)
    {
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - i + Capacity) % Capacity;
            if (_time[idx] <= t) return idx;
        }
        return _count > 0 ? (_head - _count + 1 + Capacity) % Capacity : -1;
    }

    public Vector3    PositionAt(int idx) => _pos[idx];
    public Quaternion RotationAt(int idx) => _rot[idx];
    public int   StateAt(int idx, int layer)  => _state[idx * MaxLayers + layer];
    public float NormAt(int idx, int layer)   => _norm[idx * MaxLayers + layer];
    public float FloatAt(int idx, int f)      => _floats[idx * MaxFloats + f];
    public int   FloatHash(int f)             => _floatHashes[f];

    /// <summary>누적 k번째 공격(타격 프레임) 시각.</summary>
    public float AttackTime(int k) => _attacks[k % MaxAttacks];

    // ── Event Handlers ────────────────────────────────────────────
    private void HandleHitStep(int step)
    {
        _attacks[_attackCount % MaxAttacks] = Time.time;
        _attackCount++;
    }
}

/// <summary>
/// F1 「영혼 복제」 복제체 — 플레이어 모습의 보라 그림자. 코어 모서리에 서서 <see cref="LichSoulTape.Delay"/>초 전
/// 플레이어의 움직임을 제 자리 기준으로 되풀이하고, 그때 플레이어가 휘두른 박자에 맞춰 <b>지금의 플레이어</b>를 벤다
/// (보라 예고선이 <see cref="AttackWarn"/>초 선행). 두 번 맞으면 흩어진다 · 수명 <see cref="Lifetime"/>초.
/// 외형: 플레이어를 비활성 상태로 통째로 복사한 뒤 스크립트 · 충돌체를 벗긴다(Awake가 한 번도 돌지 않는다) —
/// 입력 · 카메라 · 매니저와 무관하다(전설 분신 기능은 PlayerController를 남기므로 재사용하지 않았다).
/// </summary>
public sealed class LichSoulCopy : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────────
    private const string HitLayerName = "MonsterHit";
    private const float  Lifetime     = 10f;
    private const int    HitsToBreak  = 2;
    private const float  HitInterval  = 0.15f;
    private const float  AttackWarn   = 0.5f;   // 0.4 → 0.5(10-02 — 짧은 예고 늘리기)
    private const float  AttackGap    = 0.55f;   // 콤보 연타는 이 간격 안이면 한 번으로
    private const float  SlashLength  = 7f;
    private const float  SlashWidth   = 2.4f;
    private const float  SlashSpeed   = 45f;
    private const float  SlashDamage  = 0.8f;
    private const float  TurnSpeed    = 540f;
    private const float  FadeSeconds  = 0.35f;   // 떠오르고 흐려진다(10-03) — 반투명 그림자라 디졸브 대신 알파(MaterialFade)

    private static readonly int   BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int   ColorId     = Shader.PropertyToID("_Color");
    private static readonly Color GhostColor  = new(0.70f, 0.35f, 1.00f, 0.85f);   // 0.55는 희미한 연기로만 보였다(09-19 실측)
    private static readonly List<LichSoulCopy> s_live = new();
    private static Material s_ghostMaterial;   // 플레이어 회피 잔상 머티리얼(반투명)을 보라로 — 처음 쓸 때 한 번

    // ── Private ───────────────────────────────────────────────────
    private MonsterContext _ctx;
    private LichSoulTape   _tape;
    private Animator       _anim;
    private Vector3        _anchor;
    private Vector3        _center;
    private float          _coreRadius;
    private float          _born;
    private int            _hits;
    private float          _lastHitTime = float.NegativeInfinity;
    private int            _nextAttack;
    private float          _lastStrike = float.NegativeInfinity;
    private GameObject     _telegraph;
    private Vector3        _telegraphDir;
    private float          _strikeAt = -1f;
    private GameObject     _aura;
    private bool           _gone;
    private MaterialFade.FadeInHandle _fadeIn;   // 페이드가 렌더러마다 복제한 재질 — 파괴 때 거둔다

    // ── Properties ────────────────────────────────────────────────
    public static IReadOnlyList<LichSoulCopy> Live => s_live;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_live.Clear();
        s_ghostMaterial = null;
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (_gone) return;
        if (Time.time - _born >= Lifetime || _tape?.Player == null) { Vanish(); return; }

        float t   = Time.time - LichSoulTape.Delay;
        int   idx = _tape.Find(t);
        if (idx >= 0) Replay(idx);
        TickAttack(t);
    }

    private void OnDestroy()
    {
        s_live.Remove(this);
        PatternGuideHelper.SafeDestroy(ref _telegraph);
        LichVfx.Stop(ref _aura);
        // 복제 재질은 오브젝트와 함께 사라지지 않는다 — 직접 거둔다(10-03)
        if (_fadeIn != null)
            foreach (var m in _fadeIn.Mats) if (m != null) Destroy(m);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>플레이어 모습을 떠 <paramref name="anchor"/>에 세운다. 플레이어 모델을 못 찾으면 null.</summary>
    public static LichSoulCopy Create(MonsterContext ctx, LichSoulTape tape, Vector3 anchor, Vector3 center, float coreRadius)
    {
        var player = tape?.Player;
        if (player == null || player.Anim == null) return null;

        // 비활성 그릇 아래에서 통째로 복사(메시가 가리키는 뼈대까지 복사본으로 이어진다) → Awake가 돌지 않는다.
        // 동작을 벗긴 뒤 그릇에서 꺼낸다.
        var holder = new GameObject("[SoulCopyHolder]");
        holder.SetActive(false);
        var go = Instantiate(player.gameObject, holder.transform);
        go.name = "LichSoulCopy";
        Strip(go);
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col    = go.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 1.9f;
        col.center = Vector3.up * 0.95f;

        if (!go.TryGetComponent<Animator>(out var anim)) anim = go.AddComponent<Animator>();
        anim.avatar                    = player.Anim.avatar;
        anim.runtimeAnimatorController = player.Anim.runtimeAnimatorController;
        anim.applyRootMotion           = false;
        anim.cullingMode               = AnimatorCullingMode.AlwaysAnimate;
        anim.fireEvents                = false;   // 애니 이벤트 수신자가 없다 — 경고 방지
        anim.speed                     = 0f;      // 상태 · 진행도는 테이프가 정한다

        var copyC = go.AddComponent<LichSoulCopy>();
        copyC._ctx        = ctx;
        copyC._tape       = tape;
        copyC._anim       = anim;
        copyC._anchor     = anchor;
        copyC._center     = center;
        copyC._coreRadius = coreRadius;
        copyC._born       = Time.time;
        copyC._nextAttack = tape.AttackCount;
        copyC.RewindAttacks();
        copyC.Tint(player);
        copyC._fadeIn = MaterialFade.BeginFadeIn(go);   // 그려지기 전에 투명으로 — 툭 나타나지 않게(10-03)
        go.transform.SetParent(null, false);
        go.transform.SetPositionAndRotation(anchor, player.transform.rotation);
        Destroy(holder);

        copyC._aura = LichVfx.PlayLoop(LichVfxSlot.BossAura, anchor + Vector3.up * 0.05f, Quaternion.identity, 0.45f, go.transform);
        // 잔상(PhantomShow)은 카드가 제단 전체로 흩어져 화면을 덮는다(09-19 실측) — 나타남 연기로.
        LichVfx.Play(LichVfxSlot.TeleportAppear, anchor, Quaternion.identity, 0.7f);
        s_live.Add(copyC);
        MaterialFade.FadeInAsync(copyC._fadeIn, FadeSeconds, copyC.destroyCancellationToken).Forget();
        return copyC;
    }

    /// <summary>모두 흩어지게 한다 — 패턴 종료 · 페이지 전환 · 전투 종료.</summary>
    public static void ClearAll()
    {
        for (int i = s_live.Count - 1; i >= 0; i--)
            if (s_live[i] != null) s_live[i].Vanish();
        s_live.Clear();
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (_gone) return;
        if (Time.time - _lastHitTime < HitInterval) return;
        _lastHitTime = Time.time;
        _hits++;
        Vector3 at = transform.position + Vector3.up * 1f;
        LichVfx.Play(LichVfxSlot.BoltImpact, at, Quaternion.identity, 0.35f);
        LichSfx.Play(LichSfxSlot.SealStoneHit, at, 0.7f);
        if (_hits >= HitsToBreak) Vanish();
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>
    /// 복사본에서 동작을 벗긴다 — 스크립트(의존 순서) → 관절 → 충돌체 → 강체 · 소리 · 빛 · 카메라.
    /// 메시 외 렌더러(입자 · 궤적)는 끈다. 레이어는 기본으로(플레이어 전용 렌더 · 충돌 규칙에서 빠진다).
    /// </summary>
    private static void Strip(GameObject root)
    {
        foreach (var c in root.GetComponentsInChildren<Canvas>(true))
            if (c != null) DestroyImmediate(c.gameObject);
        foreach (var c in root.GetComponentsInChildren<Camera>(true))
            if (c != null) DestroyImmediate(c.gameObject);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) StripScripts(t.gameObject);
        foreach (var j in root.GetComponentsInChildren<Joint>(true))     DestroyImmediate(j);
        foreach (var c in root.GetComponentsInChildren<Collider>(true))  DestroyImmediate(c);
        foreach (var r in root.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
        foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        foreach (var l in root.GetComponentsInChildren<Light>(true))     DestroyImmediate(l);
        foreach (var l in root.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(l);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r is not SkinnedMeshRenderer && r is not MeshRenderer) r.enabled = false;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
    }

    /// <summary>한 오브젝트의 스크립트를 의존(RequireComponent) 순서대로 지운다 — 남이 필요로 하는 것은 나중에.</summary>
    private static void StripScripts(GameObject go)
    {
        var mbs = new List<MonoBehaviour>(go.GetComponents<MonoBehaviour>());
        for (int pass = 0; pass < 8 && mbs.Count > 0; pass++)
        {
            for (int i = mbs.Count - 1; i >= 0; i--)
            {
                if (mbs[i] == null) { mbs.RemoveAt(i); continue; }
                if (IsRequiredByOther(mbs[i], mbs)) continue;
                DestroyImmediate(mbs[i]);
                mbs.RemoveAt(i);
            }
        }
    }

    private static bool IsRequiredByOther(MonoBehaviour target, List<MonoBehaviour> all)
    {
        var type = target.GetType();
        foreach (var other in all)
        {
            if (other == null || other == target) continue;
            foreach (RequireComponent rc in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (Requires(rc.m_Type0, type) || Requires(rc.m_Type1, type) || Requires(rc.m_Type2, type))
                    return true;
            }
        }
        return false;
    }

    private static bool Requires(System.Type required, System.Type type) => required != null && required.IsAssignableFrom(type);

    /// <summary>
    /// 반투명 보라 그림자 — 플레이어 회피 잔상 머티리얼(CharacterData.dodgeGhostMaterial)을 보라로 입혀 모든 메시에 씌운다.
    /// 잔상 머티리얼이 없으면 원래 머티리얼에 색만 입힌다(툰 셰이더는 거의 안 먹는다 — 09-19 실측).
    /// </summary>
    private void Tint(PlayerController player)
    {
        if (s_ghostMaterial == null && player.CharacterData != null && player.CharacterData.dodgeGhostMaterial != null)
        {
            s_ghostMaterial = new Material(player.CharacterData.dodgeGhostMaterial) { name = "~LichSoulCopyGhost" };
            if (s_ghostMaterial.HasProperty(BaseColorId)) s_ghostMaterial.SetColor(BaseColorId, GhostColor);
            if (s_ghostMaterial.HasProperty(ColorId))     s_ghostMaterial.SetColor(ColorId, GhostColor);
        }

        var mpb = s_ghostMaterial == null ? new MaterialPropertyBlock() : null;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is not SkinnedMeshRenderer && r is not MeshRenderer) continue;
            if (s_ghostMaterial != null)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = s_ghostMaterial;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                continue;
            }
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, GhostColor);
            r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>테이프 안(지금부터 Delay초 전 이후)의 공격부터 되풀이한다 — 예고 구간에 막 태어나도 박자를 놓치지 않게.</summary>
    private void RewindAttacks()
    {
        float from = Time.time - LichSoulTape.Delay;
        while (_nextAttack > 0 && _nextAttack > _tape.AttackCount - 8 && _tape.AttackTime(_nextAttack - 1) > from)
            _nextAttack--;
    }

    /// <summary>기록 한 칸을 제 자리 기준으로 재현 — 위치는 원점 대비 이동량, 몸은 지금의 플레이어를 향한다.</summary>
    private void Replay(int idx)
    {
        Vector3 p = _anchor + (_tape.PositionAt(idx) - _tape.Origin);
        Vector3 flat = p - _center;
        flat.y = 0f;
        if (flat.magnitude > _coreRadius) p = _center + flat.normalized * _coreRadius + Vector3.up * (p.y - _center.y);
        transform.position = p;

        var target = _tape.Player.transform.position - p;
        target.y = 0f;
        if (target.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(target), TurnSpeed * Time.deltaTime);

        for (int l = 0; l < _tape.Layers; l++)
            _anim.Play(_tape.StateAt(idx, l), l, _tape.NormAt(idx, l));
        for (int f = 0; f < _tape.FloatCount; f++)
            _anim.SetFloat(_tape.FloatHash(f), _tape.FloatAt(idx, f));
    }

    /// <summary>
    /// 되풀이 공격 — 플레이어가 휘두른 타격 순간(기록 시각 ta)에 맞춰, ta+Delay−Warn에 보라 예고선, ta+Delay에 벤다.
    /// 방향은 예고가 뜨는 순간의 플레이어 쪽으로 고정.
    /// </summary>
    private void TickAttack(float replayTime)
    {
        if (_strikeAt > 0f)
        {
            float k = 1f - (_strikeAt - Time.time) / AttackWarn;
            PatternGuideHelper.SetProgress(_telegraph, k);
            if (Time.time < _strikeAt) return;
            Strike();
            return;
        }

        while (_nextAttack < _tape.AttackCount)
        {
            float ta = _tape.AttackTime(_nextAttack);
            if (ta - AttackWarn > replayTime) return;   // 아직 예고 전
            _nextAttack++;
            float strikeAt = ta + LichSoulTape.Delay;
            if (strikeAt - _lastStrike < AttackGap || strikeAt < Time.time) continue;
            BeginTelegraph(strikeAt);
            return;
        }
    }

    private void BeginTelegraph(float strikeAt)
    {
        Vector3 origin = FloorPos();
        Vector3 dir = _tape.Player.transform.position - origin;
        dir.y = 0f;
        _telegraphDir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        _telegraph = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Beam(origin, _telegraphDir, SlashLength, SlashWidth, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
        _strikeAt = strikeAt;
    }

    private void Strike()
    {
        PatternGuideHelper.SafeDestroy(ref _telegraph);
        _strikeAt   = -1f;
        _lastStrike = Time.time;
        Vector3 origin = FloorPos();
        LichHazards.BeamFront(_ctx, origin, _telegraphDir, SlashLength, SlashWidth * 0.5f, SlashSpeed, SlashDamage, 0.5f);
        LichPatternUtil.SlashVfx(origin, _telegraphDir, SlashLength * 0.6f, LichSwing.RightToLeft, LichPatternUtil.Arcane);
        LichSfx.Play(LichSfxSlot.ScytheSwing, origin, 0.6f);
    }

    private Vector3 FloorPos()
    {
        var p = transform.position;
        return new Vector3(p.x, _center.y, p.z);
    }

    private void Vanish()
    {
        if (_gone) return;
        _gone = true;
        s_live.Remove(this);
        PatternGuideHelper.SafeDestroy(ref _telegraph);   // 예고는 바로 — 흐려지는 동안 베지 않는다
        LichVfx.Stop(ref _aura);   // 붙은 오라는 먼저 풀로 — 페이드가 자식 입자를 끄면 풀 인스턴스가 꺼진 채 돌아간다
        LichVfx.Play(LichVfxSlot.TeleportVanish, transform.position + Vector3.up * 0.9f, Quaternion.identity, 0.6f);
        FadeOutAndDestroyAsync(destroyCancellationToken).Forget();
    }

    /// <summary>흐려지며 사라진다(10-03).</summary>
    private async UniTaskVoid FadeOutAndDestroyAsync(CancellationToken ct)
    {
        await MaterialFade.FadeOutAsync(gameObject, FadeSeconds, ct);   // 취소는 안에서 삼킨다
        if (this != null) Destroy(gameObject);
    }
}
}
