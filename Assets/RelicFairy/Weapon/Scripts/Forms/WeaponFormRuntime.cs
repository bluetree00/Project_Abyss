using System;
using UnityEngine;

/// <summary>
/// 플레이어에 붙는 「형」 구동기. 근접 칸(슬롯 0) 무기의 형을 만들고 플레이어 사건을 넘긴다.
/// 넘기는 사건: 평타 · 스킬 적중(HitFeedbackService.OnHit) · 피격 · 회피.
/// 쌓는 것은 근접 칸을 들고 있을 때만이다. 원거리를 들면 멈추고(줄지도 · 잃지도 않는다), 쌓인 수는 유지한다.
/// 「한 번의 휘두름」 = 슬래시 이벤트(SpawnSlashEffectN) 하나. 마지막 타 여부도 그 이벤트 때의 콤보 단계로 정한다
/// (적중 간격 0.12초로 세던 것은 슬래시 판정이 0.5초 남아 늦게 맞은 적을 새 휘두름으로 셌다 — 10-07 검토 M5).
/// HUD(Part B) · 연출이 읽는 창구: <see cref="Of"/> · <see cref="Current"/> · <see cref="Changed"/>.
/// </summary>
public sealed class WeaponFormRuntime : MonoBehaviour
{
    private const float SwingGap = 0.12f;   // 슬래시 이벤트를 못 받을 때(받는 쪽 없음)만 쓰는 옛 기준
    // 번호가 그대로인데 앞 적중과 이만큼 떨어졌으면 슬래시보다 먼저 맞은 새 휘두름 — 2초 쉬고 친 첫 타는 피해가 슬래시 이벤트보다 먼저 왔다
    // (10-15 실측 59). 판정 수명(0.5초)보다 짧게. 근접 강화 증강(MeleeAugmentEffect.EarlyHitGap)과 같은 값.
    private const float EarlyHitGap = 0.45f;
    private const float GlowMax  = 0.6f;    // 가득일 때 형 색 × 이 값 — 「은은하게」
    private static readonly string[] TriggerVariants = { "twin_wind", "return_wind", "fissure", "aftershock" };   // 발동과 함께 나가는 변주
    private static readonly int EmissiveColorId = Shader.PropertyToID("_Emissive_Color");   // 툰 셰이더
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");    // URP Lit — 카타나 · 대검 T1~T3 재질(검토 I2: 툰 칸만 칠해 빛이 안 났다)

    private PlayerController    _player;
    private PlayerWeaponManager _wm;
    private WeaponFormBase      _form;
    private WeaponData          _formWeapon;
    private float               _lastMeleeHitAt = -999f;
    private PlayerAnimationEventReceiver _receiver;
    private int                 _swingSerial, _countedSwing = -1, _swingStep;
    private PlayerWeaponTrailVfx  _trail;
    private Transform             _tip, _root;
    private Renderer[]            _blade = Array.Empty<Renderer>();
    private readonly System.Collections.Generic.List<(Renderer rd, int index, int prop, Color baseColor)> _glowSlots = new();
    private MaterialPropertyBlock _block;
    private Color                 _formGlow, _extraGlow;   // 형 빛 · 다른 연출이 더하는 빛(대검 R 속성 검 · 10-26)
    private bool                  _trailWanted, _trailOn;
    private readonly System.Collections.Generic.HashSet<string> _announced = new();
    private bool                  _readyNext;
    private float                 _lastSlashAt = -999f;
    private const float           SameSwingGap = 0.5f;   // 같은 단계 슬래시가 이 안에 또 오면 같은 휘두름

    /// <summary>형이 새로 만들어짐(무기 교체 · 진화) — HUD가 다시 붙는다.</summary>
    public event Action Changed;
    /// <summary>가득 찬 채 콤보가 마지막 타 바로 앞 휘두름까지 왔다 — 다음 타에 발동한다(올라설 때 한 번 · HUD 형 게이지 테가 밝아진다 · 10-12 S8 · A7).</summary>
    public event Action ReadyNext;

    public WeaponFormBase Current => _form;
    /// <summary>지금 「다음 타에 발동」 상태인가 — HUD가 붙을 때 읽는다.</summary>
    public bool IsReadyNext => _readyNext;
    /// <summary>휘두름 일련번호(슬래시 이벤트마다 +1 · 받는 쪽이 없으면 −1) — 근접 강화 증강이 「휘두름당 한 번」을 형과 같은 기준으로 센다(10-15).</summary>
    public int SwingSerial => _receiver != null ? _swingSerial : -1;
    /// <summary>그 휘두름의 콤보 단계(슬래시가 나올 때) — 「마지막 타」 · 「n번째 타」 증강이 읽는다(10-15).</summary>
    public int SwingStep => _swingStep;

    public static WeaponFormRuntime Of(PlayerController p)
        => p != null && p.TryGetComponent(out WeaponFormRuntime r) ? r : null;

    private bool MeleeActive => _wm != null && _wm.CurrentSlotIndex == PlayerWeaponManager.Slot0;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        TryGetComponent(out _trail);
        _receiver = GetComponentInChildren<PlayerAnimationEventReceiver>(true);
        _block = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        HitFeedbackService.OnHit += HandleHit;
        if (_player != null)
        {
            _player.OnDamageTaken += HandleDamaged;
            _player.OnDodgeStart  += HandleDodgeStart;
            _player.OnDodgeEnd    += HandleDodgeEnd;
        }
        if (_receiver != null) _receiver.OnEffectStepEarly += HandleEffectStep;   // 베기 판정 · 피해보다 먼저 센다(10-24 ①)
        BindWeaponManager();
        SyncCurrentWeapon();   // 장착이 구동기보다 먼저 끝났거나 다시 켜졌다 — 칼끝 · 칼날 캐싱부터(검토 M6)
    }

    private void Start()
    {
        BindWeaponManager();   // 무기 관리자는 플레이어 Start 뒤에 생길 수 있다 — 한 번 더 시도
        SyncCurrentWeapon();
    }

    private void Update()
    {
        if (_form != null) { _form.Holding = MeleeActive; _form.Tick(Time.deltaTime); }   // 원거리를 든 동안은 쉬는 시간을 세지 않는다(검토 M4) — 여진 · 균열 · 되감는 바람 타이머는 흐른다
        // 트레일 교체는 애니메이션 이벤트 안에서 하면 안 된다(PlayerWeaponTrailVfx 주석) — 적중 사건은 표시만, 적용은 여기서
        if (_trail == null) return;
        var full = _form != null ? _form.Form.FullTrailPrefab : null;
        // 다른 주인(랜슬롯 Q 검)이 덮어쓰면 우리 것은 꺼진 것으로 본다 — 끝나면 다시 건다(검토 I4)
        if (_trailOn && (!_trail.IsOverriding || _trail.OverridePrefab != full)) _trailOn = false;
        if (_trailWanted == _trailOn) return;
        if (_trailWanted)
        {
            if (_trail.IsOverriding || _tip == null || _root == null || full == null) return;   // 남이 쓰는 중이면 기다린다
            _trail.BeginOverride(_tip, _root, full);
            _trailOn = true;
        }
        else
        {
            if (full != null && _trail.OverridePrefab == full) _trail.EndOverride();   // 남의 트레일은 끄지 않는다
            _trailOn = false;
        }
    }

    private void OnDisable()
    {
        HitFeedbackService.OnHit -= HandleHit;
        if (_player != null)
        {
            _player.OnDamageTaken -= HandleDamaged;
            _player.OnDodgeStart  -= HandleDodgeStart;
            _player.OnDodgeEnd    -= HandleDodgeEnd;
        }
        if (_receiver != null) _receiver.OnEffectStepEarly -= HandleEffectStep;
        if (_wm != null)
        {
            _wm.OnWeaponChanged           -= HandleWeaponChanged;
            _wm.OnEquippedWeaponRefreshed -= HandleWeaponRefreshed;
            _wm = null;
        }
        DisposeForm();
    }

    /// <summary>칼날 발광에 더할 색 — 대검 R 속성 검 동안 속성 색(10-26). 형 빛과 같은 칸에 더해 서로 덮어쓰지 않는다. clear면 뺀다.</summary>
    public void SetExtraGlow(Color c)
    {
        _extraGlow = c;
        PaintGlow();
    }

    /// <summary>근접 칸 무기로 형을 다시 만든다(쌓인 수는 0부터). 같은 무기 · 같은 형이면 그대로 둔다.</summary>
    public void Rebuild()
    {
        BindWeaponManager();
        var w0 = _wm != null ? _wm.Weapon0Data : null;
        if (_form != null && w0 != null && ReferenceEquals(_formWeapon, w0) && w0.HasForm) return;
        DisposeForm();
        _form = WeaponFormBase.Create(_player, w0);
        _formWeapon = _form != null ? w0 : null;
        if (_form != null)
        {
            _form.StacksChanged += HandleStacksChanged;
            _form.Triggered     += HandleTriggered;
        }
        HandleStacksChanged(0, _form?.MaxStacks ?? 1);
        Changed?.Invoke();
    }

    private void SyncCurrentWeapon()
    {
        if (_wm != null && _wm.CurrentWeaponInstance != null) HandleWeaponChanged(_wm.CurrentWeaponData, _wm.CurrentWeaponInstance);
        else Rebuild();
    }

    private void BindWeaponManager()
    {
        if (_wm != null || _player == null || _player.WeaponManager == null) return;
        _wm = _player.WeaponManager;
        _wm.OnWeaponChanged           += HandleWeaponChanged;
        _wm.OnEquippedWeaponRefreshed += HandleWeaponRefreshed;
    }

    private void DisposeForm()
    {
        if (_form != null)
        {
            _form.StacksChanged -= HandleStacksChanged;
            _form.Triggered     -= HandleTriggered;
        }
        _trailWanted = false;
        _form?.Dispose();
        _form = null;
        _formWeapon = null;
    }

    // ── Event Handlers ───────────────────────────────────────────
    private void HandleHit(HitInfo info)
    {
        if (_form == null || info.Attacker != gameObject || info.Target == null || !MeleeActive) return;
        if (info.ActionType == WeaponActionType.GroundLight)
        {
            int last = Mathf.Max(1, _formWeapon.groundEndCount) - 1;
            bool newSwing, finisher;
            if (_receiver != null)
            {
                bool early = _swingSerial <= _countedSwing && Time.time - _lastMeleeHitAt > EarlyHitGap;   // 슬래시보다 먼저 맞은 새 휘두름
                newSwing = _swingSerial > _countedSwing || early;   // 이 슬래시로는 처음 맞았다
                if (newSwing) _countedSwing = early ? _swingSerial + 1 : _swingSerial;   // 먼저 맞았으면 곧 올 슬래시 번호도 이 휘두름 것
                // 슬래시가 나올 때의 단계 — 판정이 남아 있는 동안 단계가 넘어가도 그대로. 슬래시보다 먼저 맞은 타는 지금 단계
                finisher = (_swingSerial < _countedSwing ? _player.Combo.CurrentComboStep : _swingStep) >= last;
            }
            else
            {
                newSwing = Time.time - _lastMeleeHitAt > SwingGap;
                finisher = _player.Combo.CurrentComboStep >= last;
            }
            _lastMeleeHitAt = Time.time;
            _form.OnMeleeHit(info, newSwing, finisher);
        }
        else if (info.ActionType == WeaponActionType.ESkill || info.ActionType == WeaponActionType.RSkill)
        {
            _form.OnSkillHit(info);
        }
    }

    private void HandleEffectStep(int _)
    {
        // 한 동작에 슬래시 이벤트가 둘인 클립(무형검 3타 SpawnSlashEffect2 · 3) — 같은 콤보 단계 · 짧은 간격이면 같은 휘두름(10-24 ②)
        int step = _player != null ? _player.Combo.CurrentComboStep : 0;
        bool sameSwing = _swingSerial > 0 && step == _swingStep && Time.time - _lastSlashAt < SameSwingGap;
        _lastSlashAt = Time.time;
        if (sameSwing) return;
        _swingSerial++;
        _swingStep = step;
        if (MeleeActive) _form?.OnSwing(_swingStep);
        CheckReadyNext();
    }

    /// <summary>가득 · 근접 · 마지막 타 바로 앞 휘두름 — 셋이 다 맞을 때 「다음 타 발동」(올라설 때만 사건). 휘두름 · 쌓임이 바뀔 때 다시 본다.</summary>
    private void CheckReadyNext()
    {
        int last = Mathf.Max(1, _formWeapon != null ? _formWeapon.groundEndCount : 1) - 1;
        bool ready = MeleeActive && _form != null && _form.IsFull && last > 0 && _swingStep == last - 1;
        if (ready && !_readyNext) ReadyNext?.Invoke();
        _readyNext = ready;
    }

    private void HandleDamaged()    { if (MeleeActive) _form?.OnDamaged(); }   // 원거리를 든 동안은 잃지 않는다(검토 M4)
    private void HandleDodgeStart() { if (MeleeActive) _form?.OnDodgeStart(); }
    private void HandleDodgeEnd()   { if (MeleeActive) _form?.OnDodgeEnd(); }
    private void HandleWeaponChanged(WeaponData _, GameObject instance)
    {
        // 무기 모델의 칼끝 · 칼밑 · 렌더러는 여기서 한 번만 캐싱한다(Update에서 찾지 않는다)
        _tip = _root = null;
        _blade = Array.Empty<Renderer>();
        if (instance != null && instance.TryGetComponent(out WeaponInstance wi))
        {
            _tip  = wi.tipPoint;
            _root = wi.rootPoint;
        }
        if (instance != null) _blade = instance.GetComponentsInChildren<Renderer>(true);
        CacheGlowSlots();
        Rebuild();
        HandleStacksChanged(_form?.Stacks ?? 0, _form?.MaxStacks ?? 1);   // 근접으로 돌아오면 쌓인 만큼 다시 칠한다
    }

    private void HandleStacksChanged(int stacks, int max)
    {
        CheckReadyNext();
        _trailWanted = _form != null && stacks >= max && MeleeActive;
        float k = _form != null && MeleeActive && max > 0 ? (float)stacks / max : 0f;   // 원거리를 들면 그 무기는 칠하지 않는다
        _formGlow = _form != null ? _form.Form.Tint * (k * GlowMax) : Color.clear;
        PaintGlow();
    }

    private void PaintGlow()
    {
        Color c = _formGlow + _extraGlow;
        for (int i = 0; i < _glowSlots.Count; i++)
        {
            var (rd, index, prop, baseColor) = _glowSlots[i];
            if (rd == null) continue;
            rd.GetPropertyBlock(_block, index);
            _block.SetColor(prop, baseColor + c);   // 원래 발광(룬 등)에 더한다 — 0이면 원래 그대로
            rd.SetPropertyBlock(_block, index);
        }
    }

    /// <summary>
    /// 칼날 재질마다 칠할 칸을 찾는다(무기 교체 때 한 번). 툰 = _Emissive_Color, URP Lit = _EmissionColor(재질 사본에 _EMISSION을 켠다 — 공유 재질은 건드리지 않는다).
    /// </summary>
    private void CacheGlowSlots()
    {
        _glowSlots.Clear();
        for (int i = 0; i < _blade.Length; i++)
        {
            var rd = _blade[i];
            if (rd == null || rd is ParticleSystemRenderer || rd is TrailRenderer || rd is LineRenderer) continue;
            var shared = rd.sharedMaterials;
            Material[] inst = null;
            for (int m = 0; m < shared.Length; m++)
            {
                var mat = shared[m];
                if (mat == null) continue;
                if (mat.HasProperty(EmissiveColorId)) { _glowSlots.Add((rd, m, EmissiveColorId, mat.GetColor(EmissiveColorId))); continue; }
                if (!mat.HasProperty(EmissionColorId)) continue;
                bool had = mat.IsKeywordEnabled("_EMISSION");
                Color baseColor = had ? mat.GetColor(EmissionColorId) : Color.black;
                if (!had)
                {
                    inst ??= rd.materials;   // 이 렌더러의 재질 사본(무기 인스턴스와 함께 사라진다)
                    inst[m].EnableKeyword("_EMISSION");
                    inst[m].SetColor(EmissionColorId, Color.black);
                }
                _glowSlots.Add((rd, m, EmissionColorId, baseColor));
            }
        }
    }

    private void HandleTriggered(string name)
    {
        if (_form == null) return;
        Managers.Sound?.PlayEvent(_form.Form.FormId == "weight" ? SoundEvent.FormWeight : SoundEvent.FormWindblade);   // 발동마다 소리(10-12 S7)
        if (_announced.Add(name))   // 이 런에서 처음 터질 때만 이름을 띄운다
        {
            RelicFloatText.Show(transform.position, name, _form.Form.Tint);
            WeaponFormHints.TryShowOnce(_form.Form);
        }
        if (name != _form.Form.TriggerName) return;   // 변주 이름은 본 발동(풍절 · 내려찍기) 때만 — 무풍이 먼저 터져도 쌍풍 이름이 앞서 뜨지 않게
        for (int i = 0; i < TriggerVariants.Length; i++)   // 발동과 함께 나가는 변주는 첫 발동 1회 이름(10-12 S7)
        {
            string id = TriggerVariants[i];
            if (_formWeapon == null || !_formWeapon.HasFormVariant(id) || !_form.Form.TryGetVariant(id, out var v) || !_announced.Add(id)) continue;
            RelicFloatText.Show(transform.position + Vector3.up * 0.35f, v.displayName, _form.Form.Tint);
        }
    }
    private void HandleWeaponRefreshed(WeaponData _) => Rebuild();
}
