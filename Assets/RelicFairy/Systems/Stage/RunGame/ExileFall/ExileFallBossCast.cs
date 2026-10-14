using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 리치 추방 추락 — 층마다 그 보스가 봉인된 채 묶여 있다가 깨어난다(10-13 사용자 「그 보스의 봉인이 깨진다는 걸 느낄 수 있는 연출」).
/// 봉인 장면이 끝난 모습(주저앉아 사슬 넷에 묶임 · 발밑 봉인진)으로 층 앞쪽에 둔다. 몸이 다가오면 버둥 → 사슬이 하나씩 끊기고 → 일어서는 동작과 함께
/// 봉인진이 깨진다(<see cref="ExileFallStage"/>의 층 깨짐 · 포효와 같은 순간).
/// <para>모델은 보스 프리팹을 Addressables로 읽어 <b>꺼진 부모 아래 복제</b>한다(Awake가 안 돈다) — 스크립트 · 판정 · 물리 · 소리 · UI를 걷고
/// 렌더러 · 애니메이터만 남긴다. AI · 체력바 · 보스 등록이 없다. 늦게 읽히면 그 층은 예전처럼 봉인진만(건너뛴다).</para>
/// 자세 이름 · 사슬 뼈 · 봉인 빛깔은 보스 장면 표(<see cref="BossStoryScenes"/> — 84)와 같은 값을 쓴다. 표는 고치지 않는다.
/// </summary>
internal sealed class ExileFallBossCast
{
    // ── Constants ─────────────────────────────────────────────────
    private const int   ChainCount        = 4;
    private const float ChainWidth        = 0.9f;    // 봉인 장면 · 묶임 사슬과 같은 굵기
    private const float AnchorRadiusScale = 0.7f;    // 묶임(BossBinding)과 같은 — 몸 가까이 감긴 모습
    private const float SealedPoseTime    = 0.98f;   // 주저앉음 끝 프레임 — 봉인 장면이 끝난 자세
    private const float StrainLead        = 0.9f;    // 층 깨짐 이만큼 전 — 버둥 · 사슬이 팽팽해진다
    private const float SnapLead          = 0.5f;    //   이만큼 전부터 사슬이 하나씩 끊긴다
    private const float SnapGap           = 0.07f;
    private const float RiseLead          = 0.2f;    //   이만큼 전 — 일어서는 동작
    private const float SnapBurstScale    = 0.2f;    // 끊김 파편(묶임 Snap과 같은 배율)

    /// <summary>
    /// 보스별 모델 주소 · 자세(보스 장면 표의 Collapse · Struggle · Break와 같은 상태 이름) · 크기.
    /// 크기 — 카메라가 몸 위 5.5 m에서 내려다보는 허공이라 제 크기면 기사가 점처럼 보였다(10-13 ②f 사진) → 환영처럼 키운다.
    /// </summary>
    private static readonly (string id, string address, string sealed_, string[] struggle, string rise, float scale)[] Casts =
    {
        (StoryProgress.ForestGuardian, "ForestGuardian/ForestGuardian", "Die",
            new[] { "GetHit_Left", "GetHit_Right", "GetHit_Back", "GetHit_Front" }, "MagicAttackS", 1.3f),
        (StoryProgress.Dragon, "DragonBoss/DragonBoss", "USleep Start",
            new[] { "UGetHit Front Left 1", "UGetHit Front Right 1", "UGetHit Back Right", "GetHit" }, "UAttackWindHighStart", 1f),
        (StoryProgress.DeathKnight, "DeathKnightBoss/DeathKnightBoss", "Die",
            new[] { "GetHit1", "GetHit2", "GetHit3", "GetHit2" }, "Attack1", 1.8f),
    };

    // 꺼진 채 복제한 모델에 남길 것 — 나머지 컴포넌트는 걷는다
    private static readonly Type[] Keep =
    {
        typeof(Transform), typeof(Animator), typeof(SkinnedMeshRenderer), typeof(MeshRenderer), typeof(MeshFilter), typeof(LODGroup),
    };

    // ── Private ───────────────────────────────────────────────────
    private readonly string  _bossId;
    private readonly Color   _sealColor;
    private readonly float   _wrapScale;
    private readonly string  _sealedState;
    private readonly string[] _struggle;
    private readonly string  _rise;
    private readonly float   _scale;
    private readonly Vector3 _feet;
    private readonly float   _breakAt;
    private readonly Vector3[] _anchors = new Vector3[ChainCount];
    private readonly LichChainLine[] _chains = new LichChainLine[ChainCount];
    private GameObject  _figure;
    private Animator    _anim;
    private Transform[] _bones;
    private int         _snapped;
    private bool        _strained, _risen;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>모델이 서 있다(늦게 읽혔거나 실패하면 false — 그 층은 봉인진만).</summary>
    public bool    Ready => _figure != null;
    public Vector3 Feet  => _feet;
    public string  BossId => _bossId;

    // ── Constructor ───────────────────────────────────────────────
    private ExileFallBossCast(string bossId, BossStoryScenes.BindingLook look, Vector3 feet, float breakAt,
                              string sealedState, string[] struggle, string rise, float scale)
    {
        _bossId = bossId; _sealColor = look.SealColor; _wrapScale = look.WrapScale * scale;
        _feet = feet; _breakAt = breakAt;
        _sealedState = sealedState; _struggle = struggle; _rise = rise; _scale = scale;
    }

    /// <summary>그 보스의 모습 크기(봉인진 · 사슬 반경도 같이 곱한다).</summary>
    public static float ScaleOf(string bossId)
    {
        foreach (var c in Casts) if (c.id == bossId) return c.scale;
        return 1f;
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>그 보스의 자리 · 값을 정한다(모델은 아직). 표에 없는 보스면 null.</summary>
    public static ExileFallBossCast Plan(string bossId, Vector3 feet, float breakAt)
    {
        if (bossId == null || !BossStoryScenes.TryGetLook(bossId, out var look)) return null;
        foreach (var c in Casts)
            if (c.id == bossId) return new ExileFallBossCast(bossId, look, feet, breakAt, c.sealed_, c.struggle, c.rise, c.scale);
        return null;
    }

    /// <summary>모델을 읽어 봉인된 자세로 세우고 사슬을 감는다. <paramref name="facing"/> = 몸이 지나가는 쪽(카메라 쪽)을 본다.</summary>
    public async UniTask SpawnAsync(Transform parent, Quaternion facing, List<Object> owned)
    {
        string address = null;
        foreach (var c in Casts) if (c.id == _bossId) address = c.address;
        var am = Managers.AddressableManager;
        if (am == null || address == null) return;
        var prefab = await am.TryLoadAssetAsync<GameObject>(address);
        if (prefab == null || parent == null) { ExileFallSequence.Note($"보스 모습 없음 — {_bossId}(주소 {address})"); return; }

        var hold = new GameObject("@ExileCastHold");
        hold.SetActive(false);   // 꺼진 부모 아래 복제 — 보스 스크립트의 Awake · OnEnable이 돌지 않는다
        try
        {
            var go = Object.Instantiate(prefab, hold.transform);
            go.name = "@ExileCast_" + _bossId;
            Strip(go);
            _anim = go.GetComponentInChildren<Animator>(true);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(_feet, facing);
            go.transform.localScale = Vector3.one * _scale;
            if (!go.activeSelf) go.SetActive(true);   // 화룡 프리팹은 루트가 꺼진 채 저장돼 있다(풀이 켠다) — ②f에서 안 보였다
            _figure = go;
            owned.Add(go);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Exile] 보스 모습을 세우지 못했다 — {_bossId} · 봉인진만: {e.Message}");
            return;
        }
        finally { Object.Destroy(hold); }

        if (_anim != null)
        {
            _anim.applyRootMotion = false;
            _anim.fireEvents      = false;   // 클립 이벤트(발소리 · 타격 프레임)를 받을 스크립트를 걷었다 — 「받는 쪽 없음」 오류를 막는다
            _anim.cullingMode     = AnimatorCullingMode.AlwaysAnimate;   // 화면 밖에서도 자세를 잡는다(층이 아래에서 다가온다)
            Play(_sealedState, SealedPoseTime);
            _anim.speed = 0f;
            _anim.Update(0f);
        }

        // 사슬 넷 — 발밑 둘레 네 곳에서 그 뼈로(묶임과 같은 차례)
        BossStoryScenes.TryGetLook(_bossId, out var look);
        _bones = BossStoryScenes.ResolveChainBones(_figure.transform, look.ChainBones, ChainCount);
        float yaw = _figure.transform.eulerAngles.y;
        for (int i = 0; i < ChainCount; i++)
        {
            _anchors[i] = _feet + Quaternion.Euler(0f, yaw + 45f + i * 90f, 0f) * Vector3.forward * (look.ChainRadius * AnchorRadiusScale * _scale);
            _chains[i]  = LichChainLine.Create(_anchors[i], Grip(i), ChainWidth * Mathf.Sqrt(_scale), _sealColor);
        }
        ExileFallSequence.Note($"보스 모습 — {_bossId} 봉인된 채(자세 {(_anim != null ? _sealedState : "없음")} · 사슬 {ChainCount})");
    }

    /// <summary>
    /// 매 프레임 — 층 깨짐(<c>breakAt</c>) 전 0.9초 버둥 · 0.5초부터 사슬이 하나씩 끊김 · 0.2초 일어섬.
    /// 깨짐 순간(봉인진 터짐 · 포효)은 무대가 맡는다.
    /// </summary>
    public void Tick(float fallTime)
    {
        if (_figure == null) return;
        for (int i = 0; i < ChainCount; i++)
            if (_chains[i] != null) _chains[i].SetEnds(_anchors[i], Grip(i));   // 버둥 · 일어섬을 따라간다

        if (!_strained && fallTime >= _breakAt - StrainLead)
        {
            _strained = true;
            if (_anim != null) _anim.speed = 1f;
            Play(_struggle.Length > 0 ? _struggle[0] : null, 0f);
            foreach (var c in _chains) if (c != null) { c.Tension(1f); c.Flash(0.4f); }
            ExileFallSequence.Note($"보스 모습 — {_bossId} 버둥");
        }
        while (_snapped < ChainCount && fallTime >= _breakAt - SnapLead + _snapped * SnapGap)
        {
            Snap(_snapped);
            if (_snapped + 1 < _struggle.Length) Play(_struggle[_snapped + 1], 0f);
            _snapped++;
        }
        if (!_risen && fallTime >= _breakAt - RiseLead)
        {
            _risen = true;
            Play(_rise, 0f);
            ExileFallSequence.Note($"보스 모습 — {_bossId} 일어선다");
        }
    }

    /// <summary>사슬을 거둔다(무대가 지워질 때). 모델은 무대 소유 목록이 지운다.</summary>
    public void Dispose()
    {
        for (int i = 0; i < ChainCount; i++)
        {
            if (_chains[i] != null) _chains[i].Dispose();
            _chains[i] = null;
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Snap(int i)
    {
        if (_chains[i] == null) return;
        Vector3 mid = Vector3.Lerp(_anchors[i], Grip(i), 0.5f);
        LichVfx.PlayTinted(LichVfxSlot.BossSealBurst, mid, Quaternion.identity, _wrapScale * SnapBurstScale, _sealColor);
        _chains[i].Dispose();
        _chains[i] = null;
    }

    private Vector3 Grip(int i)
        => _bones != null && _bones[i] != null ? _bones[i].position : _feet + Vector3.up * (1.5f * _scale);

    private void Play(string state, float normalizedTime)
    {
        if (_anim == null || string.IsNullOrEmpty(state)) return;
        int hash = Animator.StringToHash(state);
        if (_anim.HasState(0, hash)) _anim.Play(hash, 0, normalizedTime);
    }

    /// <summary>
    /// 그림만 남긴다 — 스크립트(다른 스크립트가 요구하지 않는 것부터 차례로) → 효과 입자(끄기) · 캔버스(끄기) → 나머지 컴포넌트.
    /// 꺼진 부모 아래에서 하므로 지워지는 스크립트의 OnDisable · OnDestroy만 돈다(Awake를 안 거쳐 대개 비어 있다).
    /// </summary>
    private static void Strip(GameObject root)
    {
        var scripts = new List<MonoBehaviour>(root.GetComponentsInChildren<MonoBehaviour>(true));
        for (int guard = 0; scripts.Count > 0 && guard < 16; guard++)
        {
            for (int i = scripts.Count - 1; i >= 0; i--)
            {
                var mb = scripts[i];
                if (mb == null) { scripts.RemoveAt(i); continue; }
                if (IsRequiredByOther(mb, scripts)) continue;
                Object.DestroyImmediate(mb);
                scripts.RemoveAt(i);
            }
        }
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var em = ps.emission; em.enabled = false;
            if (ps.TryGetComponent<ParticleSystemRenderer>(out var r)) r.enabled = false;
        }
        foreach (var cv in root.GetComponentsInChildren<Canvas>(true)) cv.gameObject.SetActive(false);
        // 판정 · 이동 · 소리 · 관절 → 강체는 맨 뒤(관절이 강체를 붙잡는다)
        foreach (var comp in root.GetComponentsInChildren<Component>(true))
            if (IsStrippable(comp) && !(comp is Rigidbody)) Object.DestroyImmediate(comp);
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(rb);
    }

    private static bool IsStrippable(Component comp)
    {
        if (comp == null || comp is ParticleSystem || comp is ParticleSystemRenderer || comp is Canvas || comp is CanvasRenderer) return false;
        return !(comp is Light) && !IsKept(comp);   // 눈 · 몸의 빛은 남긴다
    }

    private static bool IsKept(Component c)
    {
        var t = c.GetType();
        foreach (var k in Keep) if (k.IsAssignableFrom(t)) return true;
        return false;
    }

    /// <summary>같은 오브젝트의 다른 남은 스크립트가 [RequireComponent]로 이 스크립트 형을 붙잡고 있는가.</summary>
    private static bool IsRequiredByOther(MonoBehaviour mb, List<MonoBehaviour> remaining)
    {
        var type = mb.GetType();
        foreach (var other in remaining)
        {
            if (other == null || other == mb || other.gameObject != mb.gameObject) continue;
            foreach (RequireComponent rc in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if ((rc.m_Type0 != null && rc.m_Type0.IsAssignableFrom(type)) ||
                    (rc.m_Type1 != null && rc.m_Type1.IsAssignableFrom(type)) ||
                    (rc.m_Type2 != null && rc.m_Type2.IsAssignableFrom(type)))
                    return true;
        }
        return false;
    }
}
