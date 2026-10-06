using System;
using UnityEngine;

/// <summary>
/// 플레이어에 붙는 「형」 구동기. 근접 칸(슬롯 0) 무기의 형을 만들고 플레이어 사건을 넘긴다.
/// 넘기는 사건: 평타 · 스킬 적중(HitFeedbackService.OnHit) · 피격 · 회피.
/// 쌓는 것은 근접 칸을 들고 있을 때만이다. 원거리를 들면 멈추고, 쌓인 수는 유지한다.
/// HUD(Part B) · 연출이 읽는 창구: <see cref="Of"/> · <see cref="Current"/> · <see cref="Changed"/>.
/// </summary>
public sealed class WeaponFormRuntime : MonoBehaviour
{
    private const float SwingGap = 0.12f;   // 같은 휘두름의 여러 적중을 한 번으로 센다(LancelotMemoryHub와 같은 값)
    private const float GlowMax  = 0.6f;    // 가득일 때 형 색 × 이 값 — 「은은하게」
    private static readonly int EmissiveColorId = Shader.PropertyToID("_Emissive_Color");

    private PlayerController    _player;
    private PlayerWeaponManager _wm;
    private WeaponFormBase      _form;
    private WeaponData          _formWeapon;
    private float               _lastMeleeHitAt = -999f;
    private PlayerWeaponTrailVfx  _trail;
    private Transform             _tip, _root;
    private Renderer[]            _blade = Array.Empty<Renderer>();
    private MaterialPropertyBlock _block;
    private bool                  _trailWanted, _trailOn;
    private readonly System.Collections.Generic.HashSet<string> _announced = new();

    /// <summary>형이 새로 만들어짐(무기 교체 · 진화) — HUD가 다시 붙는다.</summary>
    public event Action Changed;

    public WeaponFormBase Current => _form;

    public static WeaponFormRuntime Of(PlayerController p)
        => p != null && p.TryGetComponent(out WeaponFormRuntime r) ? r : null;

    private bool MeleeActive => _wm != null && _wm.CurrentSlotIndex == PlayerWeaponManager.Slot0;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        TryGetComponent(out _trail);
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
        BindWeaponManager();
    }

    private void Start()
    {
        BindWeaponManager();   // 무기 관리자는 플레이어 Start 뒤에 생길 수 있다 — 한 번 더 시도
        Rebuild();
    }

    private void Update()
    {
        _form?.Tick(Time.deltaTime);
        // 트레일 교체는 애니메이션 이벤트 안에서 하면 안 된다(PlayerWeaponTrailVfx 주석) — 적중 사건은 표시만, 적용은 여기서
        if (_trailWanted == _trailOn || _trail == null) return;
        if (_trailWanted && _tip != null && _root != null && _form?.Form.FullTrailPrefab != null)
            _trail.BeginOverride(_tip, _root, _form.Form.FullTrailPrefab);
        else
            _trail.EndOverride();
        _trailOn = _trailWanted;
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
        if (_wm != null)
        {
            _wm.OnWeaponChanged           -= HandleWeaponChanged;
            _wm.OnEquippedWeaponRefreshed -= HandleWeaponRefreshed;
            _wm = null;
        }
        DisposeForm();
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
            bool newSwing = Time.time - _lastMeleeHitAt > SwingGap;
            _lastMeleeHitAt = Time.time;
            int last = Mathf.Max(1, _formWeapon.groundEndCount) - 1;   // 적중 순간엔 아직 다음 단계로 안 넘어갔다(ActAttackState.OnAttackEnd)
            _form.OnMeleeHit(info, newSwing, _player.Combo.CurrentComboStep >= last);
        }
        else if (info.ActionType == WeaponActionType.ESkill || info.ActionType == WeaponActionType.RSkill)
        {
            _form.OnSkillHit(info);
        }
    }

    private void HandleDamaged()    => _form?.OnDamaged();
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
        Rebuild();
        HandleStacksChanged(_form?.Stacks ?? 0, _form?.MaxStacks ?? 1);   // 근접으로 돌아오면 쌓인 만큼 다시 칠한다
    }

    private void HandleStacksChanged(int stacks, int max)
    {
        _trailWanted = _form != null && stacks >= max && MeleeActive;
        if (_form == null) return;
        float k = MeleeActive && max > 0 ? (float)stacks / max : 0f;   // 원거리를 들면 그 무기는 칠하지 않는다
        Color c = _form.Form.Tint * (k * GlowMax);
        for (int i = 0; i < _blade.Length; i++)
        {
            var rd = _blade[i];
            if (rd == null || rd.sharedMaterial == null || !rd.sharedMaterial.HasProperty(EmissiveColorId)) continue;
            rd.GetPropertyBlock(_block);
            _block.SetColor(EmissiveColorId, c);
            rd.SetPropertyBlock(_block);
        }
    }

    private void HandleTriggered(string name)
    {
        if (_form == null || !_announced.Add(name)) return;   // 이 런에서 처음 터질 때만 이름을 띄운다
        RelicFloatText.Show(transform.position, name, _form.Form.Tint);
        WeaponFormHints.TryShowOnce(_form.Form);
    }
    private void HandleWeaponRefreshed(WeaponData _) => Rebuild();
}
