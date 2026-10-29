using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 그림자 파트너(10-26 무기 개편 · 카타나 증강) — 곁에 어두운 실루엣이 붙어 플레이어 동작을 조금 늦게 그대로 따라 한다(동작을 따라 하는 분신).
/// 재련할수록 분신이 는다(<see cref="Count"/> 1 → 2 → 3 · 10-26 사용자 「추가 재련 시 분신을 추가」). 자리는 모두 플레이어가 노리는 타격 지점(앞 <see cref="AimDistance"/> m)에
/// 칼이 닿는 곳 — 왼쪽 옆 · 오른쪽 옆 · 왼쪽 앞(10-26 「분신의 위치가 기본 공격 타격을 유효하게 때리는 위치」). 공격 · 스킬 중에는 그 지점을 보고 벤다.
/// 분신마다 늦음이 조금씩 달라(<see cref="DelayOf"/>) 잇달아 베는 잔상처럼 보인다.
/// 피해는 증강(<see cref="ShadowPartnerAugment"/>)이 적중마다 분신마다 같은 늦음으로 따로 준다 — 여기는 모습만.
/// 플레이어를 통째로 떠 동작을 벗긴 사본(<see cref="PlayerGhostStrike.BuildCopy"/>)에 알파 섞기 그림자 재질을 입힌다.
/// 시각은 게임 시간이라 멈춘 화면(룬판 · ESC)에서는 따라 하기도 멈춘다. 맞을 것이 없는 방(대기방 · 상점)에서는 숨긴다(전설 고리와 같은 규칙).
/// 베기 메아리(10-26 사용자 「개선 적용」 — 분신 피해가 숫자로만 보였다): 분신의 피해가 들어가는 순간 그 분신 자리에서 플레이어와 같은 베기를
/// 어둡고 옅게 한 번(<see cref="EchoSlash"/> · 베기마다 분신당 한 번 · 맞힌 분신만 — 그림 = 판정). 판정 없는 보이기용 사본이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShadowPartner : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    public const int MaxClones = 3;
    private const int   Samples = 48, MaxLayers = 4, MaxFloats = 16;
    private const float IdleCheckInterval = 0.5f, IdleRadius = 25f, Alpha = 0.45f;
    private const float AimDistance = 2.2f, TurnSpeed = 900f;   // 타격 지점(평타 판정 가운데) · 그쪽으로 도는 빠르기(°/초)
    private const float SlashLife = 0.6f, SlashFresh = 0.5f;      // 베기 메아리 수명 · 플레이어 베기가 이보다 오래되면 메아리 없음
    private const int   MaxSlashFx = 6;
    // 분신 자리(플레이어 회전 기준) · 늦음 — 셋 다 타격 지점까지 2 m 안(카타나 평타가 닿는 거리)
    private static readonly Vector3[] Offsets = { new(-1.1f, 0f, 0.3f), new(1.1f, 0f, 0.3f), new(-1.5f, 0f, 1.7f) };
    private static readonly float[]   Delays  = { 0.12f, 0.16f, 0.2f };
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly List<GameObject> s_idleBuf = new(1);
    // 베기 메아리 색(알파 섞기 · 재질 기본색 ×4라 낮게) — 어두운 보라 그림자
    private static readonly Color SlashBright = new(0.09f, 0.07f, 0.14f), SlashDeep = new(0.02f, 0.015f, 0.035f);
    private const float SlashAlpha = 0.7f;
#if UNITY_EDITOR
    /// <summary>실측 99 F-4 — 베기 메아리 색 후보(투명도 0이면 기본).</summary>
    public static Color ProbeSlashBright, ProbeSlashDeep;
    public static float ProbeSlashAlpha;
#endif

    private struct Pose { public float t; public Vector3 pos; public Quaternion rot; public bool combat; }
    private struct LayerPose { public int hash; public float time; public float weight; }

    private sealed class SlashFx
    {
        public string key;
        public GameObject go;
        public ParticleSystem[] ps;
        public float hideAt = -1f;
    }

    private sealed class Clone
    {
        public GameObject go;
        public Animator   anim;
        public Renderer[] rends;
    }

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _player;
    private readonly Clone[] _clones = new Clone[MaxClones];
    private Material _material;
    private MaterialPropertyBlock _mpb;
    private Pose[] _poses;
    private LayerPose[] _layers;
    private float[] _floats;          // 칸마다 애니 float 값(이동 블렌드 MoveX · MoveY 등 — 상태만 따라 하면 달릴 때 선 자세로 미끄러졌다 · 10-26 검토)
    private int[] _floatHashes;
    private int _head = -1, _count, _layerCount, _floatCount;
    private float _idleCheck;
    private bool _enemiesNear, _shown;
    private int _shownCount;
    private readonly List<SlashFx> _slashFx = new();
    private string _slashKey;
    private Vector3 _slashPos;          // 플레이어 기준(회전만 · 크기 무관)
    private Quaternion _slashRot;
    private float _slashScale, _slashAt = -999f;
    private int _echoed, _loadingSlash;   // 이번 베기에서 메아리를 낸 분신(비트) · 불러오는 중인 사본 수

    // ── Properties ────────────────────────────────────────────────
    /// <summary>증강이 켜져 있고 그 카타나를 쥐었다 — 증강이 틱마다 넣는다.</summary>
    public bool Wanted { get; set; }
    /// <summary>분신 수(1~3) — 증강이 재련 횟수로 정해 틱마다 넣는다.</summary>
    public int Count { get; set; } = 1;
    /// <summary>지금 실루엣이 보이나.</summary>
    public bool Shown => _shown;
    /// <summary>지금 보이는 분신 수(실측).</summary>
    public int ShownCount => _shown ? _shownCount : 0;
    /// <summary>지금 보이는 베기 메아리 수(실측).</summary>
    public int EchoVisible { get { int n = 0; foreach (var f in _slashFx) if (f.go != null && f.go.activeSelf) n++; return n; } }
    /// <summary>베기 메아리 사본에 켜진 충돌체 수 — 0이어야(판정 없는 보이기용 · 실측).</summary>
    public int EchoColliders { get { int n = 0; foreach (var f in _slashFx) if (f.go != null) foreach (var c in f.go.GetComponentsInChildren<Collider>(true)) if (c.enabled) n++; return n; } }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        TryGetComponent(out _player);
        _mpb    = new MaterialPropertyBlock();
        _poses  = new Pose[Samples];
        _layers = new LayerPose[Samples * MaxLayers];
        _floats = new float[Samples * MaxFloats];
        _floatHashes = new int[MaxFloats];
    }

    private void OnEnable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged += OnWeaponChanged;
    }

    private void LateUpdate()
    {
        if (_player == null || _player.Anim == null) return;
        TickSlashFx();
        bool want = Wanted && EnemiesNear();
        int count = Mathf.Clamp(Count, 1, MaxClones);
        if (want) BuildNext(count);   // 애니 이벤트 밖(LateUpdate)에서만 · 한 프레임에 하나씩 — 사본 벗기기(DestroyImmediate)가 거부되지 않게
        int built = 0;
        for (int i = 0; i < count; i++) if (_clones[i] != null) built++;
        if (built == 0) return;
        if (want != _shown || built != _shownCount)
        {
            _shown = want;
            _shownCount = built;
            for (int i = 0; i < MaxClones; i++)
                if (_clones[i] != null) _clones[i].go.SetActive(want && i < count);
            if (!want) _count = 0;
        }
        if (!_shown) return;
        Record();
        for (int i = 0; i < count; i++) if (_clones[i] != null) Apply(_clones[i], i);
    }

    private void OnDisable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged -= OnWeaponChanged;
    }

    private void OnDestroy()
    {
        DestroyClones();
        if (_material != null) Destroy(_material);
        foreach (var f in _slashFx) if (f.go != null) Destroy(f.go);
        _slashFx.Clear();
    }

    // ── Public Methods ────────────────────────────────────────────
    public static ShadowPartner Ensure(PlayerController p)
    {
        if (p == null) return null;
        if (!p.TryGetComponent<ShadowPartner>(out var sp)) sp = p.gameObject.AddComponent<ShadowPartner>();
        return sp;
    }

    /// <summary><paramref name="index"/>번째 분신의 늦음(초) — 증강이 그 분신의 칼이 닿을 때 피해를 준다.</summary>
    public static float DelayOf(int index) => Delays[Mathf.Clamp(index, 0, MaxClones - 1)];

    /// <summary>플레이어 평타 베기가 났다(WeaponEffectHandler) — 분신이 맞힐 때 같은 베기를 낼 수 있게 플레이어 기준 자리를 적어 둔다.</summary>
    public static void NoteSlash(PlayerController p, string key, Vector3 pos, Quaternion rot, float scale)
    {
        if (p == null || string.IsNullOrEmpty(key) || !p.TryGetComponent<ShadowPartner>(out var sp) || !sp._shown) return;
        var tf = p.transform;
        var inv = Quaternion.Inverse(tf.rotation);
        sp._slashKey   = key;
        sp._slashPos   = inv * (pos - tf.position);
        sp._slashRot   = inv * rot;
        sp._slashScale = scale;
        sp._slashAt    = Time.time;
        sp._echoed     = 0;
    }

    /// <summary>
    /// <paramref name="slot"/>번째 분신의 피해가 들어갔다(증강) — 그 분신 자리에서 마지막 플레이어 베기를 어둡고 옅게 한 번.
    /// 베기마다 분신당 한 번(여러 적을 맞혀도) · 플레이어 베기가 <see cref="SlashFresh"/>초보다 오래됐으면 없음.
    /// </summary>
    public void EchoSlash(int slot)
    {
        if (!_shown || slot < 0 || slot >= MaxClones || _clones[slot] == null || _slashKey == null) return;
        if (Time.time - _slashAt > SlashFresh || (_echoed & (1 << slot)) != 0) return;
        _echoed |= 1 << slot;
        var ctf = _clones[slot].go.transform;
        Vector3 pos = ctf.position + ctf.rotation * _slashPos;
        Quaternion rot = ctf.rotation * _slashRot;
        int shown = ShowOrLoad(_slashKey, pos, rot, _slashScale);
        if (shown == 1) Log($"베기 메아리 — 분신 {slot + 1}");
        else if (shown == 2) Log($"베기 메아리 — 분신 {slot + 1}(사본 만듦)");
    }

    /// <summary>
    /// 그 자리에서 베기 하나를 어두운 보라로 한 번(판정 없는 보이기용 사본 · 베기 메아리와 같은 모습 · 같은 사본 풀) —
    /// 그림자 걸음(전설 어둠 단일) 분신의 칼이 닿을 때(10-29 사용자 「분신 베기 이펙트 넣자」). 이 증강이 없어도 쓴다.
    /// </summary>
    public static void PlayDarkSlash(PlayerController p, string key, Vector3 pos, Quaternion rot, float scale)
    {
        if (string.IsNullOrEmpty(key)) return;
        var sp = Ensure(p);
        if (sp != null && sp.ShowOrLoad(key, pos, rot, scale) == 2) Log("어두운 베기(사본 만듦)");
    }

    // ── Private Methods ───────────────────────────────────────────
    private bool EnemiesNear()
    {
        _idleCheck -= Time.unscaledDeltaTime;
        if (_idleCheck > 0f) return _enemiesNear;
        _idleCheck = IdleCheckInterval;
        _enemiesNear = CombatQuery.GetNearbyDamageables(_player.transform.position, IdleRadius, _player.gameObject, 1, s_idleBuf) > 0;
        return _enemiesNear;
    }

    /// <summary>아직 없는 분신을 하나 만든다(한 프레임에 하나).</summary>
    private void BuildNext(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_clones[i] != null) continue;
            var src = _player.CharacterData != null ? _player.CharacterData.dodgeGhostMaterial : null;
            if (src == null) return;
            if (_material == null) _material = PlayerGhostStrike.ShadowMaterialFrom(src);
            var go = PlayerGhostStrike.BuildCopy(_player, _material, "ShadowPartner" + i, out var anim, out var rends);
            if (go == null) return;
            anim.runtimeAnimatorController = _player.Anim.runtimeAnimatorController;   // 지금 무기 클립
            if (_floatCount == 0)
                foreach (var prm in _player.Anim.parameters)
                    if (prm.type == AnimatorControllerParameterType.Float && _floatCount < MaxFloats) _floatHashes[_floatCount++] = prm.nameHash;
            var c = PlayerGhostStrike.ShadowTint;
            c.a = Alpha;
            _mpb.SetColor(BaseColorId, c);
            foreach (var r in rends) if (r != null) r.SetPropertyBlock(_mpb);
            go.SetActive(false);   // 다음 LateUpdate가 켠다(보이는 수가 바뀌었다)
            _clones[i] = new Clone { go = go, anim = anim, rends = rends };
            return;
        }
    }

    private void DestroyClones()
    {
        for (int i = 0; i < MaxClones; i++)
        {
            if (_clones[i] != null && _clones[i].go != null) Destroy(_clones[i].go);
            _clones[i] = null;
        }
        _shown = false;
        _shownCount = 0;
        _floatCount = 0;
        _count = 0;   // 옛 자세(무기 바꾸기 전 · 옛 애니 상태)를 버린다(10-26 검토 I4)
    }

    /// <summary>쉬는 사본이 있으면 그 자리에 보인다(1) · 없으면 새로 만든다(2 · 사본 수 상한 <see cref="MaxSlashFx"/>) · 상한이면 0.</summary>
    private int ShowOrLoad(string key, Vector3 pos, Quaternion rot, float scale)
    {
        foreach (var f in _slashFx)
            if (f.key == key && f.go != null && !f.go.activeSelf) { ShowSlash(f, pos, rot, scale); return 1; }
        if (_slashFx.Count + _loadingSlash >= MaxSlashFx) return 0;
        LoadSlashAsync(key, pos, rot, scale, this.GetCancellationTokenOnDestroy()).Forget();
        return 2;
    }

    /// <summary>베기 사본을 만든다 — 무기 페이로드를 겸하는 프리팹이라 판정(충돌체 · 풀 회수 · 판정체)을 끄고 보이기만 · 어두운 색으로 한 번 칠한다.</summary>
    private async UniTaskVoid LoadSlashAsync(string key, Vector3 pos, Quaternion rot, float scale, CancellationToken ct)
    {
        _loadingSlash++;
        GameObject go = null;
        try
        {
            go = await Managers.AddressableManager.InstantiateAsync(key, null, true);
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { if (go != null) Destroy(go); return; }
        catch (Exception) { return; }   // 키 부재 등 — 메아리만 빠진다
        finally { _loadingSlash--; }
        if (go == null) return;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var b in go.GetComponentsInChildren<EffectBehaviour>(true)) b.enabled = false;
        foreach (var c in go.GetComponentsInChildren<ColliderInstance>(true)) c.enabled = false;
        Color bright = SlashBright, deep = SlashDeep;
        float alpha = SlashAlpha;
#if UNITY_EDITOR
        if (ProbeSlashAlpha > 0f) { bright = ProbeSlashBright; deep = ProbeSlashDeep; alpha = ProbeSlashAlpha; }
#endif
        ElementSlashTint.Paint(go, bright, deep, alpha);
        var f = new SlashFx { key = key, go = go, ps = go.GetComponentsInChildren<ParticleSystem>(true) };
        _slashFx.Add(f);
        ShowSlash(f, pos, rot, scale);
    }

    private void ShowSlash(SlashFx f, Vector3 pos, Quaternion rot, float scale)
    {
        f.go.transform.SetPositionAndRotation(pos, rot);
        f.go.transform.localScale = Vector3.one * scale;
        f.go.SetActive(true);
        foreach (var s in f.ps) { s.Clear(false); s.Play(false); }
        f.hideAt = Time.time + SlashLife;
    }

    /// <summary>수명이 다한 베기 사본을 끈다(게임 시간 — 멈춘 화면에선 그대로).</summary>
    private void TickSlashFx()
    {
        for (int i = 0; i < _slashFx.Count; i++)
        {
            var f = _slashFx[i];
            if (f.go == null || f.hideAt < 0f || Time.time < f.hideAt) continue;
            f.go.SetActive(false);
            f.hideAt = -1f;
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void Log(string msg) => Debug.Log("[ShadowPartner] " + msg);

    /// <summary>지금 자리 · 회전 · 애니 층마다 상태를 적는다(게임 시각이 그대로면 마지막 칸을 덮는다 — 멈춘 화면).</summary>
    private void Record()
    {
        float now = Time.time;
        bool same = _count > 0 && _poses[_head].t == now;   // 게임 시각이 그대로(멈춘 화면) — Approximately는 시간이 길어지면 프레임을 합쳤다
        if (!same)
        {
            _head = (_head + 1) % Samples;
            _count = Mathf.Min(_count + 1, Samples);
        }
        var tf = _player.transform;
        _poses[_head] = new Pose { t = now, pos = tf.position, rot = tf.rotation, combat = _player.IsInAttackAction || _player.IsInSkillAction };
        var a = _player.Anim;
        _layerCount = Mathf.Min(a.layerCount, MaxLayers);
        for (int l = 0; l < _layerCount; l++)
        {
            var info = a.GetCurrentAnimatorStateInfo(l);
            _layers[_head * MaxLayers + l] = new LayerPose { hash = info.fullPathHash, time = info.normalizedTime, weight = l == 0 ? 1f : a.GetLayerWeight(l) };
        }
        for (int i = 0; i < _floatCount; i++) _floats[_head * MaxFloats + i] = a.GetFloat(_floatHashes[i]);
    }

    /// <summary>그 분신의 늦음만큼 앞 칸(없으면 가장 오래된 칸)의 자리 · 동작을 입힌다.</summary>
    private void Apply(Clone c, int slot)
    {
        float want = Time.time - Delays[slot];
        int idx = _head;
        for (int i = 0; i < _count; i++)
        {
            int k = (_head - i + Samples) % Samples;
            idx = k;
            if (_poses[k].t <= want) break;
        }
        var pose = _poses[idx];
        Vector3 pos = pose.pos + pose.rot * Offsets[slot];
        Quaternion rot = pose.rot;
        if (pose.combat)
        {
            Vector3 look = pose.pos + pose.rot * Vector3.forward * AimDistance - pos;   // 플레이어가 노리는 타격 지점을 본다
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(look);
        }
        c.go.transform.SetPositionAndRotation(pos, Quaternion.RotateTowards(c.go.transform.rotation, rot, TurnSpeed * Time.deltaTime));
        for (int i = 0; i < _floatCount; i++) c.anim.SetFloat(_floatHashes[i], _floats[idx * MaxFloats + i]);
        for (int l = 0; l < _layerCount; l++)
        {
            var lp = _layers[idx * MaxLayers + l];
            if (l > 0) c.anim.SetLayerWeight(l, lp.weight);
            c.anim.Play(lp.hash, l, lp.time);
        }
        c.anim.Update(0f);
    }

    // ── Event Handlers ────────────────────────────────────────────
    /// <summary>무기가 바뀌면 사본을 다시 뜬다 — 떠 둔 사본은 그때 든 무기를 쥐고 있다.</summary>
    private void OnWeaponChanged(WeaponData w, GameObject instance) => DestroyClones();
}
