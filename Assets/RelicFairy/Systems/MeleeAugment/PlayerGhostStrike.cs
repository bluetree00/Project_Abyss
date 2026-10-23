using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 무형검 증강 「추가 공격」의 그림 — 플레이어와 같은 모습의 반투명 분신이 그 자리에서 한 번 베고 사라진다(10-22 B6 사용자
/// 「무형검 추가 효과 발동 때 플레이어와 같은 모습의 투명 캐릭터로 그 추가 공격을 실제로 보여 주면 좋은 연출」).
/// 증강 검기마다 하나(쌍검기면 셋 · 튕기는 칼끝이면 갈래마다) — 사용자 「추가 검기들을 사방에 날리는 것도 복수의 투명 플레이어로」.
/// 진화한 카타나 · 대검(무형검 증강 = 환영 몫)도 같다 — 등 뒤 환영 검은 떠 있는 표시로만 남는다.
///
/// 분신 = 플레이어를 통째로 떠 동작(스크립트 · 충돌체 · 강체 · 소리 · 빛)을 벗기고 회피 잔상 재질을 입힌 것(리치 「영혼 복제」와 같은 방식).
/// 애니 이벤트는 끈다(베기 판정 · 이펙트가 분신에서 나가지 않게) — 피해와 검기는 증강이 따로 낸다. 처음 쓸 때 만들고 다시 쓴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerGhostStrike : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const int PoolSize = 6;                              // 여러 갈래가 한꺼번에(쌍검기 3갈래 · 튕기는 칼끝)
    private const float LifeSeconds = 0.45f, FadeInSeconds = 0.08f, PeakAlpha = 0.55f;
    private const float AnimSpeed = 0.9f;                        // 상태 속도(평타 · 무기 템포)에 곱 — 0.45초 안에 한 번 베는 모습
    private const string SwingState = "GroundLightAttack_03";    // 마지막 타 — 크게 휘두른다
    private const float PlayerOrigin = 1.5f;                     // 이 안에서 나가는 검기 = 플레이어가 낸 것
    private const float AheadOfPlayer = 0.6f, BehindTarget = 0.9f;
    private const float SwingFrom = 0.1f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private sealed class Ghost
    {
        public GameObject go;
        public Animator   anim;
        public Renderer[] rends;
        public float      born = -1f;
        public Color      color;
    }

    private static readonly Color KatanaGhost = new(1f, 0.42f, 0.4f, 0.6f);   // 카타나 = 붉은 계열(10-22 B4) — 대검 · 무형검은 회피 잔상 색(푸른)

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _player;
    private readonly List<Ghost> _pool = new();
    private MaterialPropertyBlock _mpb;
    private Material _material;
    private Color _baseColor = new(0.4f, 0.7f, 1f, 0.6f);

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        TryGetComponent(out _player);
        _mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged += OnWeaponChanged;
    }

    private void LateUpdate()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            var g = _pool[i];
            if (g.born < 0f || g.go == null) continue;
            float t = Time.time - g.born;
            if (t >= LifeSeconds)
            {
                g.born = -1f;
                g.go.SetActive(false);
                continue;
            }
            float a = t < FadeInSeconds ? t / FadeInSeconds : 1f - (t - FadeInSeconds) / (LifeSeconds - FadeInSeconds);
            SetAlpha(g, PeakAlpha * a);
        }
    }

    private void OnDisable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged -= OnWeaponChanged;
    }

    private void OnDestroy()
    {
        ClearPool();
        if (_material != null) Destroy(_material);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>분신이 <paramref name="standAt"/>에 서서 <paramref name="dir"/> 쪽으로 한 번 벤다.</summary>
    public static void Play(PlayerController p, Vector3 standAt, Vector3 dir)
    {
        if (p == null || p.Anim == null) return;
        if (!p.TryGetComponent<PlayerGhostStrike>(out var gs)) gs = p.gameObject.AddComponent<PlayerGhostStrike>();
        gs.Strike(standAt, dir);
    }

    /// <summary>
    /// 검기 한 줄(<paramref name="from"/> → <paramref name="dir"/>)을 낸 분신 — 플레이어 자리에서 나가는 검기면 몸과 겹치지 않게 조금 앞에,
    /// 적 자리에서 나가는 검기(튕기는 칼끝 등)면 그 적 바로 뒤에 서서 벤다.
    /// </summary>
    public static void ForWave(PlayerController p, Vector3 from, Vector3 dir)
    {
        if (p == null) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        Vector3 flat = from - p.transform.position;
        flat.y = 0f;
        bool fromPlayer = flat.sqrMagnitude < PlayerOrigin * PlayerOrigin;
        Vector3 at = fromPlayer ? from + dir * AheadOfPlayer : from - dir * BehindTarget;
        at.y = p.transform.position.y;
        Play(p, at, dir);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Strike(Vector3 standAt, Vector3 dir)
    {
        if (_player == null) return;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _player.transform.forward;
        var g = Take();
        if (g == null) return;
        g.go.transform.SetPositionAndRotation(standAt, Quaternion.LookRotation(dir));
        g.go.SetActive(true);
        g.anim.runtimeAnimatorController = _player.Anim.runtimeAnimatorController;   // 지금 무기 클립(그새 무기를 바꿨을 수 있다)
        g.anim.speed = AnimSpeed;
        g.anim.Play(SwingState, 0, SwingFrom);
        g.born = Time.time;
        g.color = MeleeAugmentCatalog.KindOf(_player.WeaponManager != null ? _player.WeaponManager.CurrentWeaponData : null) == MeleeWeaponKind.Katana
            ? KatanaGhost : _baseColor;
        SetAlpha(g, 0f);
    }

    private Ghost Take()
    {
        foreach (var g in _pool)
            if (g.born < 0f && g.go != null) return g;
        if (_pool.Count >= PoolSize)
        {
            // 다 쓰는 중이면 가장 먼저 나간 것을 다시 쓴다
            Ghost oldest = _pool[0];
            foreach (var g in _pool) if (g.born < oldest.born) oldest = g;
            return oldest.go != null ? oldest : null;
        }
        var made = Build();
        if (made != null) _pool.Add(made);
        return made;
    }

    /// <summary>비활성 그릇 아래에서 통째로 복사(Awake가 돌지 않는다) → 동작을 벗기고 → 잔상 재질을 입혀 꺼 둔다.</summary>
    private Ghost Build()
    {
        var src = _player.CharacterData != null ? _player.CharacterData.dodgeGhostMaterial : null;
        if (src == null) return null;
        if (_material == null)
        {
            _material = new Material(src) { name = "~PlayerGhostStrike" };
            if (_material.HasProperty(BaseColorId)) _baseColor = _material.GetColor(BaseColorId);
        }

        var holder = new GameObject("[GhostStrikeHolder]");
        holder.SetActive(false);
        var go = Instantiate(_player.gameObject, holder.transform);
        go.name = "PlayerGhostStrike";
        Strip(go);

        if (!go.TryGetComponent<Animator>(out var anim)) anim = go.AddComponent<Animator>();
        anim.avatar          = _player.Anim.avatar;
        anim.applyRootMotion = false;
        anim.cullingMode     = AnimatorCullingMode.AlwaysAnimate;
        anim.fireEvents      = false;   // 베기 판정 · 슬래시 이펙트가 분신에서 나가지 않게

        var rends = new List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is not SkinnedMeshRenderer && r is not MeshRenderer) continue;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = _material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            rends.Add(r);
        }

        go.transform.SetParent(null, false);
        go.SetActive(false);
        Destroy(holder);
        return new Ghost { go = go, anim = anim, rends = rends.ToArray() };
    }

    /// <summary>복사본에서 동작을 벗긴다(리치 영혼 복제와 같은 순서) — 메시 외 렌더러 · 비주얼 그래프는 끈다.</summary>
    private static void Strip(GameObject root)
    {
        foreach (var c in root.GetComponentsInChildren<Canvas>(true))
            if (c != null) DestroyImmediate(c.gameObject);
        foreach (var c in root.GetComponentsInChildren<Camera>(true))
            if (c != null) DestroyImmediate(c.gameObject);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) StripScripts(t.gameObject);
        foreach (var j in root.GetComponentsInChildren<Joint>(true))         DestroyImmediate(j);
        foreach (var c in root.GetComponentsInChildren<Collider>(true))      DestroyImmediate(c);
        foreach (var r in root.GetComponentsInChildren<Rigidbody>(true))     DestroyImmediate(r);
        foreach (var a in root.GetComponentsInChildren<AudioSource>(true))   DestroyImmediate(a);
        foreach (var l in root.GetComponentsInChildren<Light>(true))         DestroyImmediate(l);
        foreach (var l in root.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(l);
        foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
            if (b is not Animator) b.enabled = false;                          // 비주얼 그래프 등 — 스크립트가 아닌 동작
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
                if (Requires(rc.m_Type0, type) || Requires(rc.m_Type1, type) || Requires(rc.m_Type2, type))
                    return true;
        }
        return false;
    }

    private static bool Requires(System.Type required, System.Type type) => required != null && required.IsAssignableFrom(type);

    private void ClearPool()
    {
        foreach (var g in _pool)
            if (g.go != null) Destroy(g.go);
        _pool.Clear();
    }

    private void SetAlpha(Ghost g, float a)
    {
        var c = g.color;
        c.a = Mathf.Clamp01(a);
        _mpb.SetColor(BaseColorId, c);
        foreach (var r in g.rends)
            if (r != null) r.SetPropertyBlock(_mpb);
    }

    // ── Event Handlers ────────────────────────────────────────────
    /// <summary>무기가 바뀌면 분신을 다시 뜬다 — 떠 둔 분신은 그때 든 무기를 쥐고 있다(무형검 → 카타나 진화 등).</summary>
    private void OnWeaponChanged(WeaponData w, GameObject instance) => ClearPool();
}
