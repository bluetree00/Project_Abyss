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
    private const float FadeInSeconds = 0.05f, FadeOutSeconds = 0.22f, PeakAlpha = 0.55f;
    private const int   WarmCount = 3;                           // 증강이 켜질 때 미리 만들어 둘 분신 수(애니 이벤트 안에서 만들면 DestroyImmediate가 거부된다)
    private const float AfterContact = 0.3f;                     // 칼이 닿은 뒤 이만큼 더 보이다 사라진다
    private const float WindupSeconds = 0.2f;                    // 칼이 닿기 이만큼 전부터 휘두르는 모습(10-23 W2 「분신이 나와서 휘두르는 느낌」 · 10-24 0.16 → 0.2 · 클립 앞이 모자라면 늦춘다)
    private const float FallbackLead = 0.12f, MinLead = 0.05f, MaxLead = 0.3f;
    private const float AnimSpeed = 0.9f;                        // 상태 속도(평타 · 무기 템포)에 곱
    private const string SwingState = "GroundLightAttack_03";    // 마지막 타 — 크게 휘두른다
    private const float PlayerOrigin = 0.5f;                     // 이 안에서 나가는 검기 = 플레이어가 낸 것(1.5 m는 가까운 적 자리 출발 — 세 번째 칼 · 튕김 — 을 플레이어 것으로 봐 분신이 적 너머에 섰다 · 10-24)
    private const float AheadOfPlayer = 0.6f, BehindTarget = 0.9f;
    private const float SwingFrom = 0.1f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private sealed class Ghost
    {
        public GameObject go;
        public Animator   anim;
        public Renderer[] rends;
        public float      born = -1f;
        public float      life;
        public Color      color;
        public bool       shadow;   // 지금 그림자 재질을 입었나
    }

    private static readonly Color KatanaGhost = new(1f, 0.42f, 0.4f, 0.6f);   // 카타나 = 붉은 계열(10-22 B4) — 대검 · 무형검은 회피 잔상 색(푸른)

    /// <summary>그림자 실루엣 색(10-26 무기 개편 — 그림자 파트너 · 일섬). 회피 잔상 재질은 더하기 섞기라 어두운 색이 안 보여 알파 섞기 재질로 그린다(<see cref="ShadowMaterialFrom"/>).</summary>
    public static readonly Color ShadowTint = new(0.08f, 0.04f, 0.14f, 0.55f);

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _player;
    private readonly List<Ghost> _pool = new();
    private MaterialPropertyBlock _mpb;
    private Material _material, _shadowMaterial;
    private Color _baseColor = new(0.4f, 0.7f, 1f, 0.6f);
    private int _want;   // 이만큼 될 때까지 Update에서 한 프레임에 하나씩 만든다

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

    private void Update()
    {
        // 분신은 여기서만 만든다 — 증강 발동은 애니 이벤트 안이라 DestroyImmediate(동작 벗기기)가 거부돼 충돌체 · 강체 · 스크립트가 남았다(10-24 ④)
        if (_pool.Count >= _want || _player == null || _player.Anim == null) return;
        var made = Build();
        if (made != null) _pool.Add(made);
        else _want = _pool.Count;
    }

    private void LateUpdate()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            var g = _pool[i];
            if (g.born < 0f || g.go == null) continue;
            float t = Time.time - g.born;
            if (t >= g.life)
            {
                g.born = -1f;
                g.go.SetActive(false);
                continue;
            }
            float a = t < FadeInSeconds ? t / FadeInSeconds : t > g.life - FadeOutSeconds ? (g.life - t) / FadeOutSeconds : 1f;
            SetAlpha(g, PeakAlpha * Mathf.Clamp01(a));
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
        if (_shadowMaterial != null) Destroy(_shadowMaterial);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>분신을 미리 만들어 두게 한다(증강이 켜질 때) — 실제 생성은 다음 Update부터 한 프레임에 하나.</summary>
    public static void Warm(PlayerController p)
    {
        if (p == null) return;
        if (!p.TryGetComponent<PlayerGhostStrike>(out var gs)) gs = p.gameObject.AddComponent<PlayerGhostStrike>();
        gs._want = Mathf.Max(gs._want, WarmCount);
    }

    /// <summary>분신이 <paramref name="standAt"/>에 서서 <paramref name="dir"/> 쪽으로 한 번 벤다 — 칼이 닿기까지 남은 초를 돌려준다(검기 · 피해를 그때 낸다).</summary>
    public static float Play(PlayerController p, Vector3 standAt, Vector3 dir, Color? tint = null)
    {
        if (p == null || p.Anim == null) return 0f;
        if (!p.TryGetComponent<PlayerGhostStrike>(out var gs)) gs = p.gameObject.AddComponent<PlayerGhostStrike>();
        return gs.Strike(standAt, dir, tint);
    }

    /// <summary>회피 잔상 재질을 떠 알파 섞기 · 그림자 색으로 바꾼 재질(그림자 파트너 · 그림자 분신) — 부른 쪽이 지운다.</summary>
    internal static Material ShadowMaterialFrom(Material src)
    {
        if (src == null) return null;
        var m = new Material(src) { name = "~ShadowSilhouette" };
        m.SetFloat("_Blend", 0f);       // 알파 섞기(더하기 섞기면 어두운 색이 안 보인다)
        m.SetFloat("_SrcBlend", 5f);    // SrcAlpha
        m.SetFloat("_DstBlend", 10f);   // OneMinusSrcAlpha
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, ShadowTint);
        return m;
    }

    /// <summary>
    /// 검기 한 줄(<paramref name="from"/> → <paramref name="dir"/>)을 낸 분신 — 플레이어 자리에서 나가는 검기면 몸과 겹치지 않게 조금 앞에,
    /// 적 자리에서 나가는 검기(튕기는 칼끝 등)면 그 적 바로 뒤에 서서 벤다.
    /// </summary>
    public static float ForWave(PlayerController p, Vector3 from, Vector3 dir, Color? tint = null)
    {
        if (p == null) return 0f;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return 0f;
        dir.Normalize();
        Vector3 flat = from - p.transform.position;
        flat.y = 0f;
        bool fromPlayer = flat.sqrMagnitude < PlayerOrigin * PlayerOrigin;
        Vector3 at = fromPlayer ? from + dir * AheadOfPlayer : from - dir * BehindTarget;
        at.y = p.transform.position.y;
        return Play(p, at, dir, tint);
    }

    // ── Private Methods ───────────────────────────────────────────
    private float Strike(Vector3 standAt, Vector3 dir, Color? tint)
    {
        if (_player == null) return 0f;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _player.transform.forward;
        var g = Take();
        if (g == null) return 0f;
        g.go.transform.SetPositionAndRotation(standAt, Quaternion.LookRotation(dir));
        g.go.SetActive(true);
        g.anim.runtimeAnimatorController = _player.Anim.runtimeAnimatorController;   // 지금 무기 클립(그새 무기를 바꿨을 수 있다)
        g.anim.speed = AnimSpeed;
        float lead = StartSwing(g.anim);
        g.born = Time.time;
        g.life = lead + AfterContact;
        g.color = tint ?? (MeleeAugmentCatalog.KindOf(_player.WeaponManager != null ? _player.WeaponManager.CurrentWeaponData : null) == MeleeWeaponKind.Katana
            ? KatanaGhost : _baseColor);
        UseShadow(g, tint.HasValue);
        SetAlpha(g, 0f);
        return lead;
    }

    /// <summary>
    /// 칼이 닿기 <see cref="WindupSeconds"/> 전부터 휘두른다 — 닿는 순간 = 클립의 첫 SpawnSlashEffect 이벤트(무기마다 클립이 달라 클립에서 읽는다).
    /// 예전엔 휘두름 10% 자리부터 틀어 칼이 거의 닿은 채로 나타났고, 검기는 같은 순간에 먼저 나가 「분신이 냈다」로 읽히지 않았다(10-23 실측 86).
    /// 닿는 순간까지 남은 초를 돌려준다(이벤트가 없으면 옛 자리 · 0.12초).
    /// </summary>
    private static float StartSwing(Animator a)
    {
        a.Play(SwingState, 0, 0f);
        a.Update(0f);
        var info = a.GetCurrentAnimatorClipInfo(0);
        var st = a.GetCurrentAnimatorStateInfo(0);
        float rate = a.speed * st.speed * st.speedMultiplier;
        float contact = -1f;
        if (info.Length > 0 && info[0].clip != null)
            foreach (var e in info[0].clip.events)
                if (e.functionName.StartsWith("SpawnSlashEffect") && (contact < 0f || e.time < contact)) contact = e.time;
        if (contact < 0f || rate <= 0.01f || info[0].clip.length <= 0f)
        {
            a.Play(SwingState, 0, SwingFrom);
            return FallbackLead;
        }
        float start = Mathf.Max(0f, contact - WindupSeconds * rate);
        if (start <= 0f && contact / rate < WindupSeconds)   // 클립 앞이 모자라 휘두름이 0.1초 안팎으로 잘렸다(카타나 0.09초 · 10-24) — 늦춰 0.2초 보이게
        {
            a.speed *= contact / rate / WindupSeconds;
            rate = a.speed * st.speed * st.speedMultiplier;
        }
        a.Play(SwingState, 0, start / info[0].clip.length);
        a.Update(0f);
        return Mathf.Clamp((contact - start) / rate, MinLead, MaxLead);
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
        // 빈 분신이 없다 — 다음 프레임에 하나 더 만들고(Update) 이번 갈래는 분신 없이 낸다(애니 이벤트 안에서 만들지 않는다)
        _want = Mathf.Min(PoolSize, Mathf.Max(_want, _pool.Count + 1));
        return null;
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

        var go = BuildCopy(_player, _material, "PlayerGhostStrike", out var anim, out var rends);
        return go != null ? new Ghost { go = go, anim = anim, rends = rends } : null;
    }

    /// <summary>
    /// 플레이어를 비활성 그릇 아래에서 통째로 복사(Awake가 돌지 않는다) → 동작을 벗기고 → <paramref name="material"/>을 입혀 꺼 둔 사본.
    /// 증강 분신 · 그림자 파트너(<see cref="ShadowPartner"/>)가 같이 쓴다. 애니 이벤트 안에서 부르지 않는다(DestroyImmediate가 거부된다).
    /// </summary>
    internal static GameObject BuildCopy(PlayerController p, Material material, string name, out Animator anim, out Renderer[] rends)
    {
        anim = null; rends = null;
        if (p == null || p.Anim == null || material == null) return null;
        var holder = new GameObject("[GhostStrikeHolder]");
        holder.SetActive(false);
        var go = Instantiate(p.gameObject, holder.transform);
        go.name = name;
        var cur = p.WeaponManager != null ? p.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>() : null;
        string keep = cur != null ? PathFrom(p.transform, cur.transform) : null;
        foreach (var wi in go.GetComponentsInChildren<WeaponInstance>(true))
            if (PathFrom(go.transform, wi.transform) != keep) wi.gameObject.SetActive(false);
        Strip(go);

        if (!go.TryGetComponent(out anim)) anim = go.AddComponent<Animator>();
        anim.avatar          = p.Anim.avatar;
        anim.applyRootMotion = false;
        anim.cullingMode     = AnimatorCullingMode.AlwaysAnimate;
        anim.fireEvents      = false;   // 베기 판정 · 슬래시 이펙트가 분신에서 나가지 않게

        var list = new List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is not SkinnedMeshRenderer && r is not MeshRenderer) continue;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            list.Add(r);
        }

        go.transform.SetParent(null, false);
        go.SetActive(false);
        Destroy(holder);
        rends = list.ToArray();
        return go;
    }

    /// <summary>root에서 t까지 이름 · 형제 순서 경로 — 사본에서 같은 자리(지금 든 무기)를 찾는다.</summary>
    private static string PathFrom(Transform root, Transform t)
    {
        var sb = new System.Text.StringBuilder();
        for (var x = t; x != null && x != root; x = x.parent) sb.Insert(0, "/" + x.name + "#" + x.GetSiblingIndex());
        return sb.ToString();
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

    /// <summary>그림자 색이면 알파 섞기 재질로, 아니면 회피 잔상 재질로 갈아입힌다(바뀔 때만).</summary>
    private void UseShadow(Ghost g, bool shadow)
    {
        if (g.shadow == shadow) return;
        if (shadow && _shadowMaterial == null) _shadowMaterial = ShadowMaterialFrom(_material);
        var mat = shadow ? _shadowMaterial : _material;
        if (mat == null) return;
        foreach (var r in g.rends)
        {
            if (r == null) continue;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
        g.shadow = shadow;
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
    private void OnWeaponChanged(WeaponData w, GameObject instance)
    {
        ClearPool();
        _want = Mathf.Min(_want, WarmCount);   // 새 무기 모습으로 다시 미리 만든다(Update)
    }
}
