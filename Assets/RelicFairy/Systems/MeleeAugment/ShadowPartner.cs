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
/// 플레이어를 통째로 떠 동작을 벗긴 사본(<see cref="PlayerGhostStrike.BuildCopy"/>)에 알파 섞기 그림자 재질을 입힌다.
/// 시각은 게임 시간이라 멈춘 화면(룬판 · ESC)에서는 따라 하기도 멈춘다. 맞을 것이 없는 방(대기방 · 상점)에서는 숨긴다(전설 고리와 같은 규칙).
/// 분신 베기(10-30 사용자 「분신이 타격하는 게 제대로 보여야 하고 데미지도 제대로 체크」): 플레이어 평타 베기마다(<see cref="NoteSlash"/>) 분신마다 칼이 닿는 순간
/// 그 분신 자리에서 플레이어와 같은 베기를 어두운 보라로 내고(판정 없는 보이기용 사본), 플레이어 베기와 같은 판정 모양 안의 적을 벤다(그림 = 판정 · <see cref="Strike"/>).
/// 피해 = 바탕 × <see cref="Percent"/>% — 바탕은 플레이어가 막 때린 적이면 그 피해(<see cref="NoteHit"/> · 치명 · 룬 포함), 아니면 그 휘두름의 피해(치명 · 방어 전).
/// 예전엔 플레이어가 맞힌 적에게만 피해를 따라 주고 분신 자리와 상관없었다(분신이 안 닿아도 맞고 · 분신 칼 앞 적은 안 맞음).
/// 11-05 사용자 「공격은 자기 방향의 상대에게」 — 분신마다 자기 앞(플레이어가 보는 쪽 콘)의 가장 가까운 적을 노려(<see cref="PickAim"/>) 그쪽을 보고 벤다(이동은 그대로 플레이어 기준).
/// 스킬(E · R)도 분신마다 자기 앞 적을 벤다(<see cref="EchoSkillHit"/> · 증강이 부른다) · 검풍은 분신마다 따로 낮은 확률로 쏜다(<see cref="CloneShots"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class ShadowPartner : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    public const int MaxClones = 3;
    private const int   Samples = 48, MaxLayers = 4, MaxFloats = 16;
    private const float IdleCheckInterval = 0.5f, IdleRadius = 25f, Alpha = 0.45f;
    private const float AimDistance = 2.2f, TurnSpeed = 900f;   // 타격 지점(평타 판정 가운데) · 그쪽으로 도는 빠르기(°/초)
    private const float SlashLife = 0.6f, HitFresh = 0.5f;        // 베기 사본 수명 · 플레이어가 막 때린 피해를 그 적의 바탕으로 보는 시간
    private const int   MaxSlashFx = 6, MaxHitNotes = 64;
    private const float AimRange = 3.5f, AimHalfAngle = 60f, AimHold = 0.6f;   // 분신이 노릴 적 — 자기 자리에서 앞 콘 · 노린 적을 붙잡는 시간(11-05 #17)
    private const string SkillEchoKey = "SlashAttack";                         // 스킬 따라 베기 그림(카타나 베기 · 보라로 칠한 사본)
    // 분신 자리(플레이어 회전 기준) · 늦음 — 셋 다 타격 지점까지 2 m 안(카타나 평타가 닿는 거리)
    private static readonly Vector3[] Offsets = { new(-1.1f, 0f, 0.3f), new(1.1f, 0f, 0.3f), new(-1.5f, 0f, 1.7f) };
    private static readonly float[]   Delays  = { 0.12f, 0.16f, 0.2f };
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly List<GameObject> s_idleBuf = new(1);
    private static readonly Collider[] s_overlap = new Collider[64];
    private static readonly List<RelicFairy.Monster.MonsterBase> s_struck = new(16);
    private static readonly List<GameObject> s_aimBuf = new(8);
    // 분신 베기 색(알파 섞기 · 재질 기본색 ×4라 낮게) — 보라 그림자. 10-26 값(0.09 · 0.07 · 0.14)은 어두운 방에서 안 보였다(10-30)
    private static readonly Color SlashBright = new(0.2f, 0.11f, 0.38f), SlashDeep = new(0.05f, 0.025f, 0.1f);
    private const float SlashAlpha = 0.85f;
#if UNITY_EDITOR
    /// <summary>실측 99 F-4 — 베기 메아리 색 후보(투명도 0이면 기본).</summary>
    public static Color ProbeSlashBright, ProbeSlashDeep;
    public static float ProbeSlashAlpha;
    /// <summary>실측 99 O — 분신 베기가 적을 벴다(분신 칸 · 적 · 피해).</summary>
    public static Action<int, RelicFairy.Monster.MonsterBase, float> ProbeStruck;
#endif

    /// <summary>플레이어 평타 베기 한 번 — 플레이어 기준 그림 자리 · 판정 모양 · 피해. 분신이 자기 자리에서 같은 베기를 낸다.</summary>
    private readonly struct SlashNote
    {
        public readonly string Key;
        public readonly Vector3 Pos;            // 그림(플레이어 기준 — 회전만)
        public readonly Quaternion Rot;
        public readonly float Scale;
        public readonly bool Box;               // 판정 = 상자(아니면 구 · 반경 = Half.x)
        public readonly Vector3 Center;         // 판정 가운데(플레이어 기준)
        public readonly Quaternion ShapeRot;
        public readonly Vector3 Half;
        public readonly float Damage;           // 그 휘두름의 피해(치명 · 방어 전) — 플레이어가 안 때린 적의 바탕

        public SlashNote(string key, Vector3 pos, Quaternion rot, float scale, bool box, Vector3 center, Quaternion shapeRot, Vector3 half, float damage)
        {
            Key = key; Pos = pos; Rot = rot; Scale = scale; Box = box; Center = center; ShapeRot = shapeRot; Half = half; Damage = damage;
        }
    }

    private struct Pose { public float t; public Vector3 pos; public Quaternion rot; public bool combat; }
    private struct LayerPose { public int hash; public float time; public float weight; }

    private sealed class SlashFx
    {
        public string key;
        public GameObject go;
        public ParticleSystem[] ps;
        public ParticleSystem.MinMaxGradient[] baseColors;   // 칠한 뒤 시작 색 — 보스 예고 중 옅게 했다가 되돌린다(10-30)
        public bool dimmed;
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
    private int _loadingSlash;          // 불러오는 중인 사본 수
    private readonly Dictionary<int, (float dmg, float at)> _hitDmg = new();   // 플레이어가 막 때린 적 → 그 피해(분신 피해의 바탕)
    private readonly RelicFairy.Monster.MonsterBase[] _aim = new RelicFairy.Monster.MonsterBase[MaxClones];   // 분신마다 노리는 적(11-05 #17)
    private readonly float[] _aimUntil = new float[MaxClones];

    // ── Properties ────────────────────────────────────────────────
    /// <summary>증강이 켜져 있고 그 카타나를 쥐었다 — 증강이 틱마다 넣는다.</summary>
    public bool Wanted { get; set; }
    /// <summary>분신 수(1~3) — 증강이 재련 횟수로 정해 틱마다 넣는다.</summary>
    public int Count { get; set; } = 1;
    /// <summary>지금 실루엣이 보이나.</summary>
    public bool Shown => _shown;
    /// <summary>지금 보이는 분신 수(실측).</summary>
    public int ShownCount => _shown ? _shownCount : 0;
    /// <summary>분신 베기 한 번의 피해(바탕의 %) — 증강이 틱마다 넣는다(재련할수록 자람).</summary>
    public float Percent { get; set; }
    /// <summary>분신 베기 수(실측 — 맞힌 적이 없어도 센다).</summary>
    public int StrikeCount { get; private set; }
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

    /// <summary>
    /// 검풍(카타나 증강)을 그림자마다 따로 날릴 자리(11-05 사용자 「검풍은 그림자마다 따로」) — 보이는 분신마다 칼끝 자리(<paramref name="reach"/> m) ·
    /// 방향(그 분신이 노리는 적 쪽 · 없으면 분신 정면) · 그 분신의 칼이 닿기까지 남은 초(늦음). 그림자가 안 보이면 0.
    /// </summary>
    public static int CloneShots(PlayerController p, float reach, List<(Vector3 from, Vector3 dir, float lead)> buf)
    {
        buf.Clear();
        if (p == null || !p.TryGetComponent<ShadowPartner>(out var sp) || !sp._shown) return 0;
        int count = Mathf.Clamp(sp.Count, 1, MaxClones);
        Vector3 fwd = p.transform.forward;
        for (int i = 0; i < count; i++)
        {
            var c = sp._clones[i];
            if (c == null || !c.go.activeSelf) continue;
            var aim = sp.AimOf(i);
            if (aim == null) aim = sp.PickAim(i, fwd);
            Vector3 cpos = c.go.transform.position;
            Vector3 dir = aim != null ? aim.transform.position - cpos : c.go.transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : fwd;
            Vector3 from = cpos + dir * reach;
            from.y = p.transform.position.y;
            buf.Add((from, dir, Delays[i]));
        }
        return buf.Count;
    }

    /// <summary>
    /// 스킬(E · R) 적중 한 번을 분신마다 따라 벤다(11-05 사용자 「같은 스킬을 쓴다 · 공격은 자기 방향의 상대에게」) — 그 분신의 칼이 닿을 때
    /// 자기 앞 적(플레이어가 맞힌 <paramref name="playerTarget"/>이 그 앞이면 그 적)에게 <paramref name="dmg"/>. <paramref name="showSlash"/>면 그 분신 자리에 보라 베기를 보인다(시전마다 한 번).
    /// </summary>
    public static void EchoSkillHit(PlayerController p, GameObject playerTarget, float dmg, bool showSlash)
    {
        if (p == null || dmg <= 0f || !p.TryGetComponent<ShadowPartner>(out var sp) || !sp._shown) return;
        int count = Mathf.Clamp(sp.Count, 1, MaxClones);
        Vector3 fwd = p.transform.forward;
        for (int i = 0; i < count; i++)
        {
            if (sp._clones[i] == null || !sp._clones[i].go.activeSelf) continue;
            var aim = sp.PickAim(i, fwd, playerTarget);
            if (aim == null) continue;
            int slot = i;
            FormStrike.After(p, Delays[slot], () => { if (sp != null) sp.SkillStrike(slot, aim, dmg, showSlash); });   // 그 분신의 칼이 닿을 때
        }
    }

    /// <summary>
    /// 플레이어 평타 베기가 판정을 마쳤다(WeaponEffectHandler) — 보이는 분신마다 그 늦음 뒤 자기 자리에서 같은 베기 · 같은 판정을 낸다(<see cref="Strike"/>).
    /// <paramref name="shape"/> = 그 베기의 판정 콜라이더(상자 · 구만 · 크기 배율이 먹은 뒤) · <paramref name="damage"/> = 그 휘두름의 피해(치명 · 방어 전).
    /// </summary>
    public static void NoteSlash(PlayerController p, string key, Transform slash, Collider shape, float damage)
    {
        if (p == null || slash == null || string.IsNullOrEmpty(key) || !p.TryGetComponent<ShadowPartner>(out var sp) || !sp._shown) return;
        var tf = p.transform;
        var inv = Quaternion.Inverse(tf.rotation);
        Vector3 center, half;
        Quaternion shapeRot;
        if (shape is BoxCollider box)
        {
            var t = box.transform;
            Vector3 s = t.lossyScale;
            center   = t.TransformPoint(box.center);
            half     = new Vector3(box.size.x * 0.5f * Mathf.Abs(s.x), box.size.y * 0.5f * Mathf.Abs(s.y), box.size.z * 0.5f * Mathf.Abs(s.z));
            shapeRot = inv * t.rotation;
        }
        else if (shape is SphereCollider sphere)
        {
            var t = sphere.transform;
            Vector3 s = t.lossyScale;
            center   = t.TransformPoint(sphere.center);
            half     = Vector3.one * sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            shapeRot = Quaternion.identity;
        }
        else return;   // 판정 모양을 모르면 분신 베기 없음(그림만 내면 그림 ≠ 판정)
        var note = new SlashNote(key, inv * (slash.position - tf.position), inv * slash.rotation, slash.lossyScale.x,
                                 shape is BoxCollider, inv * (center - tf.position), shapeRot, half, damage);
        int count = Mathf.Clamp(sp.Count, 1, MaxClones);
        for (int i = 0; i < count; i++)
        {
            if (sp._clones[i] == null) continue;
            sp.PickAim(i, tf.forward);   // 자기 앞 적을 노린다(11-05 #17) — 없으면 플레이어 타격 지점
            int slot = i;
            FormStrike.After(p, Delays[slot], () => { if (sp != null) sp.Strike(slot, note); });   // 그 분신의 칼이 닿을 때(분신마다 늦음이 다르다)
        }
    }

    /// <summary>플레이어 평타가 그 적에게 준 피해(증강 · 적중마다) — 곧 올 분신 베기가 그 적에게 같은 바탕으로 벤다.</summary>
    public void NoteHit(RelicFairy.Monster.MonsterBase target, float dealt)
    {
        if (target == null || dealt <= 0f) return;
        if (_hitDmg.Count >= MaxHitNotes) _hitDmg.Clear();
        _hitDmg[target.GetInstanceID()] = (dealt, Time.time);
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

    /// <summary>
    /// 분신 <paramref name="slot"/>이 노릴 적 — 분신 자리에서 <paramref name="fwd"/>(플레이어가 보는 쪽) 콘 안의 가장 가까운 산 몬스터(11-05 #17).
    /// <paramref name="prefer"/>가 그 콘 안이면 그 적. 노린 적은 <see cref="AimHold"/>초 붙잡는다(분신이 그쪽을 보고 벤다).
    /// </summary>
    private RelicFairy.Monster.MonsterBase PickAim(int slot, Vector3 fwd, GameObject prefer = null)
    {
        var c = _clones[slot];
        RelicFairy.Monster.MonsterBase pick = null;
        if (c != null && c.go.activeSelf)
        {
            CombatQuery.GetDamageablesInCone(c.go.transform.position, fwd, AimRange, AimHalfAngle, _player.gameObject, 8, s_aimBuf, showGuide: false);
            for (int i = 0; i < s_aimBuf.Count; i++)
            {
                if (!s_aimBuf[i].TryGetComponent<RelicFairy.Monster.MonsterBase>(out var mb) || mb.IsDead) continue;
                if (s_aimBuf[i] == prefer) { pick = mb; break; }
                if (pick == null) pick = mb;   // 가까운 순
            }
        }
        _aim[slot] = pick;
        _aimUntil[slot] = pick != null ? Time.time + AimHold : 0f;
        return pick;
    }

    /// <summary>분신이 지금 노리는 적(붙잡는 시간 안 · 살아 있을 때) — 없으면 null.</summary>
    private RelicFairy.Monster.MonsterBase AimOf(int slot)
    {
        var a = _aim[slot];
        return a != null && !a.IsDead && Time.time < _aimUntil[slot] ? a : null;
    }

    /// <summary>스킬 따라 베기 — 그 분신이 노린 적 쪽을 보고(보라 베기 · 시전마다 한 번) 피해(추가 피해라 적중 효과는 붙지 않는다 · D5).</summary>
    private void SkillStrike(int slot, RelicFairy.Monster.MonsterBase aim, float dmg, bool showSlash)
    {
        if (!_shown || _player == null || _clones[slot] == null || !_clones[slot].go.activeSelf) return;
        if (aim == null || aim.IsDead || aim.IsMeleeImmuneNow) return;
        var ctf = _clones[slot].go.transform;
        Vector3 to = aim.transform.position - ctf.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f) ctf.rotation = Quaternion.LookRotation(to);
        if (showSlash) ShowOrLoad(SkillEchoKey, ctf.position + ctf.forward * 1.2f + Vector3.up, ctf.rotation * Quaternion.Euler(0f, 0f, 8f), 1f);
        CombatQuery.DealSynergyDamage(aim, dmg, _player.gameObject);
#if UNITY_EDITOR
        ProbeStruck?.Invoke(slot, aim, dmg);
#endif
        Log($"분신 {slot + 1} 스킬 따라 베기 — {dmg:0.0}");
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

    /// <summary>
    /// <paramref name="slot"/>번째 분신의 칼이 닿았다 — 그 분신 자리에서 플레이어와 같은 베기를 보라로 내고(그림), 같은 판정 모양 안의 적을 벤다(그림 = 판정).
    /// 피해 = (플레이어가 막 때린 적이면 그 피해 · 아니면 그 휘두름의 피해) × <see cref="Percent"/>%. 추가 피해라 적중 효과(룬 · 유물 발동)는 붙지 않는다(D5).
    /// </summary>
    private void Strike(int slot, SlashNote n)
    {
        if (!_shown || _player == null || slot >= MaxClones || _clones[slot] == null || !_clones[slot].go.activeSelf) return;
        var ctf = _clones[slot].go.transform;
        Vector3 cpos = ctf.position;
        var aim = AimOf(slot);
        if (aim != null)   // 자기 앞 적 쪽으로 돌아 벤다 — 그림과 판정이 같은 회전(11-05 #17)
        {
            Vector3 to = aim.transform.position - cpos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) ctf.rotation = Quaternion.LookRotation(to);
        }
        Quaternion crot = ctf.rotation;
        ShowOrLoad(n.Key, cpos + crot * n.Pos, crot * n.Rot, n.Scale);
        StrikeCount++;
        Vector3 c = cpos + crot * n.Center;
        int hits = n.Box
            ? Physics.OverlapBoxNonAlloc(c, n.Half, s_overlap, crot * n.ShapeRot, RelicFairy.Monster.MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide)
            : Physics.OverlapSphereNonAlloc(c, n.Half.x, s_overlap, RelicFairy.Monster.MonsterBase.HitLayerMask, QueryTriggerInteraction.Collide);
        s_struck.Clear();
        for (int i = 0; i < hits; i++)
        {
            var mb = s_overlap[i] != null ? s_overlap[i].GetComponentInParent<RelicFairy.Monster.MonsterBase>() : null;
            if (mb == null || mb.IsDead || mb.IsMeleeImmuneNow || s_struck.Contains(mb)) continue;
            s_struck.Add(mb);
        }
        float now = Time.time;
        for (int i = 0; i < s_struck.Count; i++)
        {
            var mb = s_struck[i];
            float basis = _hitDmg.TryGetValue(mb.GetInstanceID(), out var h) && now - h.at <= HitFresh ? h.dmg : n.Damage;
            float dmg = basis * Percent * 0.01f;
            if (dmg <= 0f) continue;
            CombatQuery.DealSynergyDamage(mb, dmg, _player.gameObject);
#if UNITY_EDITOR
            ProbeStruck?.Invoke(slot, mb, dmg);
#endif
        }
        Log($"분신 {slot + 1} 베기 — 맞힘 {s_struck.Count}");
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
        PlayerFxCalm.LowerSorting(go);   // 보스 예고 아래로(10-30)
        var f = new SlashFx { key = key, go = go, ps = go.GetComponentsInChildren<ParticleSystem>(true) };
        f.baseColors = new ParticleSystem.MinMaxGradient[f.ps.Length];
        for (int i = 0; i < f.ps.Length; i++) f.baseColors[i] = f.ps[i].main.startColor;
        _slashFx.Add(f);
        ShowSlash(f, pos, rot, scale);
    }

    private void ShowSlash(SlashFx f, Vector3 pos, Quaternion rot, float scale)
    {
        f.go.transform.SetPositionAndRotation(pos, rot);
        f.go.transform.localScale = Vector3.one * scale;
        bool dim = PlayerFxCalm.Active;   // 보스 예고가 떠 있으면 옅게(10-30) — 바뀔 때만 다시 칠한다
        if (dim != f.dimmed && f.baseColors != null)
        {
            f.dimmed = dim;
            for (int i = 0; i < f.ps.Length; i++)
            {
                var main = f.ps[i].main;
                main.startColor = dim ? PlayerFxCalm.ScaleAlpha(f.baseColors[i], PlayerFxCalm.DimAlpha) : f.baseColors[i];
            }
        }
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
            var aim = AimOf(slot);   // 자기 앞 적을 노렸으면 그 적(11-05 #17) · 아니면 플레이어가 노리는 타격 지점
            Vector3 look = (aim != null ? aim.transform.position : pose.pos + pose.rot * Vector3.forward * AimDistance) - pos;
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
