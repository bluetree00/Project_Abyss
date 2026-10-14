using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 형 게이지(10-08 · 재련소 시안 05를 명조형 HUD에 맞춤) — 근접 무기 원 위 작은 알약. 칼바람 = 마름모 5칸 · 무게 = 원 4칸(모양이 달라 흘끗 봐도 갈린다).
/// 찬 칸 = 형 색, 가득 = 알약 테가 형 색으로 물들고 은은한 빛(깜빡임 · 흔들림 없음 — 「살짝」 원칙). 칸이 찰 때마다 그 칸이 한 번 자리 잡는다.
/// 줄어듦은 까닭(<see cref="WeaponFormBase.LastReason"/>)대로 그린다(10-12 A7 · S9 — 자연 감소도 피격처럼 붉게 깜빡였다):
/// 발동 = 오른쪽 칸부터 0.2초에 비움(깜빡임 없음) · 맞음 = 0.15초 붉게 비친 뒤 꺼짐 · 시간 = 회색으로 0.4초에 스르륵 ·
/// 돌려받음(폭풍의 눈) = 비운 뒤 남은 칸에 금 테. 「다음 타에 발동」(<see cref="WeaponFormRuntime.ReadyNext"/>)이면 알약 테가 한 번 밝아지고 풀릴 때까지 밝게 남는다.
/// 원거리를 든 동안은 근접 원을 따라 작아지고 0.6으로 옅어진다. 무형검(형 없음)이면 숨긴다. 글자 없음(U5).
/// 데이터는 HudPresenter가 넣는다(WeaponFormRuntime → SetFormGauge · SetFormStacks · SetFormReadyNext · PlayFormTriggered).
/// </summary>
public sealed partial class CombatPanelView
{
    private const float FormPip = 10f, FormPipGap = 4f, FormPillPadX = 7f, FormPillH = 18f, FormPillLift = 6f;
    private const float FormReleaseSec = 0.2f, FormHurtSec = 0.15f, FormIdleSec = 0.4f, FormReadyFlashSec = 0.35f;
    private const float FormInactiveScale = 0.7f, FormInactiveAlpha = 0.6f;
    private static readonly Color FormEmptyFill = new(0f, 0f, 0f, 0.55f);
    private static readonly Color FormHurt      = new(0.95f, 0.36f, 0.32f, 1f);
    private static readonly Color FormIdleGrey  = new(0.55f, 0.55f, 0.58f, 1f);   // 시간으로 줄어드는 칸 — 회색으로 식는다

    private RectTransform _formPill;
    private System.Threading.CancellationTokenSource _formPillCts;   // 작아짐 · 옅어짐 — 새로 놓을 때 앞 것을 끊는다(무기 원과 같은 까닭 · 10-14)
    private CanvasGroup   _formPillGroup;
    private Image         _formPillGlow;
    private Image[]       _formPillLines;
    private Image[]       _formPips = System.Array.Empty<Image>();
    private Image[]       _formPipRims = System.Array.Empty<Image>();
    private WeaponFormSO  _formShown;
    private int   _formStacks, _formGen;
    private int   _formTriggeredFrame = -1;
    private int   _formRefund;          // 돌려받은 칸(왼쪽부터) — 금 테
    private bool  _formRimFull;         // 지금 그린 칸이 가득인가(테 색)
    private bool  _formReady;           // 「다음 타에 발동」
    private float _formReadyFlash;      // 켜지는 순간의 밝힘 1 → 0
    private int   _formReadyGen;

    /// <summary>근접 칸 형을 붙인다(null = 형 없음 → 숨김). 형이 바뀌면 칸 모양 · 수를 다시 짓는다.</summary>
    public void SetFormGauge(WeaponFormSO form)
    {
        if (form == null)
        {
            _formShown = null;
            if (_formPill != null) _formPill.gameObject.SetActive(false);
            return;
        }
        var host = _weaponSlotRoot.Length > 0 ? _weaponSlotRoot[0] : null;   // 근접 칸(Weapon_01)
        if (host == null) return;
        if (_formPill == null || _formPill.parent != host) BuildFormPill(host);
        if (_formShown != form || _formPips.Length != form.MaxStacks) BuildFormPips(form);
        if (_formShown != form) _formRefund = 0;
        _formShown = form;
        _formPill.gameObject.SetActive(true);
        _formGen++;
        PaintForm(_formStacks, hurt: false);
        PlaceFormPill(animate: false);
    }

    /// <summary>쌓인 수만 넣는다(까닭 모름 — 다시 붙기 · 실측 도구). 줄었으면 예전 판정(같은 프레임의 발동 알림)으로 가른다.</summary>
    public void SetFormStacks(int stacks) => SetFormStacks(stacks, WeaponFormBase.StackReason.Gain, 0);

    /// <summary>
    /// 쌓인 수가 바뀌었다 — 오른 칸은 자리 잡고, 줄면 까닭대로 그린다(<paramref name="reason"/> = LastReason · <paramref name="refund"/> = LastRefund).
    /// 돌려받은 칸 표시는 돌려받음에서 새로 · 맞음 · 발동에서 지움 · 오름 · 시간에서는 남은 칸까지만.
    /// </summary>
    public void SetFormStacks(int stacks, WeaponFormBase.StackReason reason, int refund)
    {
        int before = _formStacks;
        _formStacks = Mathf.Max(0, stacks);
        _formRefund = reason switch
        {
            WeaponFormBase.StackReason.Refund  => Mathf.Min(refund, _formStacks),
            WeaponFormBase.StackReason.Damaged => 0,
            WeaponFormBase.StackReason.Trigger => 0,
            _                                  => Mathf.Min(_formRefund, _formStacks),
        };
        if (_formShown == null || _formPill == null) return;
        _formGen++;
        if (_formStacks < before && isActiveAndEnabled)
        {
            EmptyFormAsync(_formGen, before, _formStacks, reason).Forget();
            return;
        }
        PaintForm(_formStacks, hurt: false);
        if (_formStacks > before && _formStacks > 0 && isActiveAndEnabled) SettlePipAsync(_formGen, _formStacks - 1).Forget();
    }

    /// <summary>발동(풍절 · 내려찍기 · 변주) — 까닭 없이 줄어든 경우(예전 경로)에만 쓰인다: 같은 프레임이면 발동으로 비운다.</summary>
    public void PlayFormTriggered() => _formTriggeredFrame = Time.frameCount;

    /// <summary>「다음 타에 발동」 — 켜지는 순간(<paramref name="flash"/>) 테가 한 번 밝아졌다 가라앉고, 풀릴 때까지 조금 밝게 남는다.</summary>
    public void SetFormReadyNext(bool on, bool flash)
    {
        _formReady = on;
        _formReadyGen++;
        _formReadyFlash = 0f;
        if (on && flash && _formPill != null && isActiveAndEnabled) FlashFormRimAsync(_formReadyGen).Forget();
        PaintFormRim();
    }

    private void BuildFormPill(RectTransform host)
    {
        if (_formPill != null) Destroy(_formPill.gameObject);
        var go = new GameObject("FormGauge", typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement));
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        _formPill = (RectTransform)go.transform;
        _formPill.SetParent(host, false);
        _formPill.anchorMin = _formPill.anchorMax = new Vector2(0.5f, 1f);
        _formPill.pivot = new Vector2(0.5f, 0f);
        _formPill.anchoredPosition = new Vector2(0f, FormPillLift);
        _formPillGroup = go.GetComponent<CanvasGroup>();
        _formPillGroup.blocksRaycasts = false;
        _formPillGroup.interactable   = false;

        _formPillGlow = ShopUIStyle.MakeImage(_formPill, "Glow", Color.clear);
        _formPillGlow.sprite = UI_RuneSelectPopup.SoftDot;
        var grt = _formPillGlow.rectTransform;
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(-16f, -12f); grt.offsetMax = new Vector2(16f, 12f);

        var bg = ShopUIStyle.MakeImage(_formPill, "Fill", UITheme.Thin.Backdrop);
        ShopUIStyle.Stretch(bg.rectTransform);
        var box = UIThinBox.Create(_formPill, "Line", Vector2.zero, Vector2.one, 0f, UITheme.Thin.LineGold, Color.clear, Color.clear);
        box.SetSiblingIndex(2);   // 빛 · 바탕 위, 칸 아래
        _formPillLines = box.GetComponentsInChildren<Image>(true);
    }

    private void BuildFormPips(WeaponFormSO form)
    {
        foreach (var p in _formPipRims) if (p != null) Destroy(p.transform.parent.gameObject);
        int n = form.MaxStacks;
        bool round = form.FormId == "weight";
        _formPips    = new Image[n];
        _formPipRims = new Image[n];
        float w = FormPillPadX * 2f + n * FormPip + (n - 1) * FormPipGap;
        _formPill.sizeDelta = new Vector2(w, FormPillH);
        for (int i = 0; i < n; i++)
        {
            var cell = new GameObject($"Pip{i}", typeof(RectTransform));
            var rt = (RectTransform)cell.transform;
            rt.SetParent(_formPill, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * FormPip;
            rt.anchoredPosition = new Vector2(FormPillPadX + FormPip * 0.5f + i * (FormPip + FormPipGap), 0f);
            var fill = ShopUIStyle.MakeImage(rt, "Fill", FormEmptyFill);
            fill.sprite = round ? UIThinShapes.Disc(FormPip) : UIThinShapes.Fill(ThinShape.Diamond, FormPip, FormPip);
            ShopUIStyle.Stretch(fill.rectTransform);
            var rim = ShopUIStyle.MakeImage(rt, "Rim", UITheme.Thin.InkA(0.4f));
            rim.sprite = round ? UIThinShapes.Ring(FormPip, 1f) : UIThinShapes.Outline(ThinShape.Diamond, FormPip, FormPip);
            ShopUIStyle.Stretch(rim.rectTransform);
            _formPips[i]    = fill;
            _formPipRims[i] = rim;
        }
    }

    /// <summary>칸 · 테를 칠한다. <paramref name="refundRims"/> = 돌려받은 칸에 금 테(비우는 동안엔 끄고 다 비운 뒤 켠다).</summary>
    private void PaintForm(int stacks, bool hurt, bool refundRims = true)
    {
        if (_formShown == null) return;
        var tint = _formShown.Tint;
        bool full = stacks >= _formPips.Length && _formPips.Length > 0;
        int refund = refundRims ? Mathf.Min(_formRefund, stacks) : 0;
        for (int i = 0; i < _formPips.Length; i++)
        {
            bool on = i < stacks;
            _formPips[i].color    = hurt ? FormHurt : on ? tint : FormEmptyFill;
            _formPipRims[i].color = hurt ? FormHurt
                                  : i < refund ? UITheme.Thin.Gold
                                  : on ? UITheme.Thin.WithA(Color.Lerp(tint, Color.white, full ? 0.55f : 0.25f), 1f) : UITheme.Thin.InkA(0.4f);
            _formPips[i].rectTransform.parent.localScale = Vector3.one;
        }
        _formRimFull = full && !hurt;
        PaintFormRim();
    }

    /// <summary>시간으로 줄어듦 — 꺼질 칸(to ~ from-1)이 형 색 → 회색 → 빈 칸으로 식는다(k 0 → 1). 깜빡임 없음.</summary>
    private void PaintFormFade(int to, int from, float k)
    {
        PaintForm(to, hurt: false);
        var fill = k < 0.5f ? Color.Lerp(_formShown.Tint, FormIdleGrey, k * 2f) : Color.Lerp(FormIdleGrey, FormEmptyFill, k * 2f - 1f);
        var rim  = Color.Lerp(FormIdleGrey, UITheme.Thin.InkA(0.4f), k);
        for (int i = Mathf.Max(0, to); i < from && i < _formPips.Length; i++)
        {
            _formPips[i].color    = fill;
            _formPipRims[i].color = rim;
        }
    }

    /// <summary>알약 테 · 빛 — 가득 = 형 색, 「다음 타에 발동」 = 흰빛을 조금 더 섞어 밝게(켜지는 순간 한 번 더 밝았다 가라앉는다).</summary>
    private void PaintFormRim()
    {
        if (_formShown == null || _formPillLines == null || _formPillGlow == null) return;
        var tint = _formShown.Tint;
        bool ready = _formReady && _formRimFull;
        float lift = ready ? 0.3f + 0.4f * _formReadyFlash : 0f;
        var line = _formRimFull ? UITheme.Thin.WithA(Color.Lerp(tint, Color.white, lift), ready ? 1f : 0.9f) : UITheme.Thin.LineGold;
        foreach (var l in _formPillLines) if (l != null) l.color = line;
        float glow = _formRimFull ? 0.28f + (ready ? 0.1f + 0.22f * _formReadyFlash : 0f) : 0f;
        _formPillGlow.color = UITheme.Thin.WithA(tint, glow);
    }

    private async UniTaskVoid FlashFormRimAsync(int gen)
    {
        try
        {
            await UIMotion.Tween(FormReadyFlashSec, k =>
            {
                if (gen != _formReadyGen) return;
                _formReadyFlash = 1f - k;
                PaintFormRim();
            }, destroyCancellationToken);
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>방금 찬 칸 — 1.3배에서 0.14초에 자리 잡는다(튕김 없이).</summary>
    private async UniTaskVoid SettlePipAsync(int gen, int index)
    {
        if (index < 0 || index >= _formPips.Length) return;
        var rt = _formPips[index].rectTransform.parent;
        try
        {
            await UIMotion.Tween(UIMotion.Quick, k => { if (gen == _formGen) rt.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, k); },
                                 destroyCancellationToken);
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>
    /// 줄어듦 — 까닭대로: 발동 · 돌려받음 = 오른쪽 칸부터 0.2초에 차례로 끔(돌려받음은 다 끈 뒤 남은 칸에 금 테가 자리 잡는다) ·
    /// 맞음 = 0.15초 붉게 비친 뒤 끔 · 시간 = 회색으로 0.4초에 식음.
    /// 까닭을 모르면(Gain — 사건 밖에서 줄인 경우) 예전 판정: 형은 칸을 먼저 줄이고 발동 알림을 뒤에 낸다(WindbladeForm.Release · 같은 호출 안) —
    /// 이 프레임 끝(PostLateUpdate)까지 기다려 프레임 번호로 가른다(발동 = 비움 · 아니면 맞음).
    /// </summary>
    private async UniTaskVoid EmptyFormAsync(int gen, int from, int to, WeaponFormBase.StackReason reason)
    {
        var ct = destroyCancellationToken;
        try
        {
            PaintForm(from, hurt: false);
            if (reason == WeaponFormBase.StackReason.Gain)
            {
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                if (gen != _formGen) return;
                bool release = _formTriggeredFrame >= 0 && Time.frameCount - _formTriggeredFrame <= 1;
                reason = release ? WeaponFormBase.StackReason.Trigger : WeaponFormBase.StackReason.Damaged;
            }
            _formTriggeredFrame = -1;
            // 경과 시간으로 칠한다 — 프레임이 길어도(느린 기기 · 에디터) 0.2초 · 0.15초 · 0.4초를 지킨다
            float t0 = Time.unscaledTime;
            switch (reason)
            {
                case WeaponFormBase.StackReason.Trigger:
                case WeaponFormBase.StackReason.Refund:
                {
                    int n = Mathf.Max(1, from - to);
                    while (true)
                    {
                        if (gen != _formGen) return;
                        float k = Mathf.Clamp01((Time.unscaledTime - t0) / FormReleaseSec);
                        PaintForm(from - Mathf.Max(1, Mathf.CeilToInt(k * n)), hurt: false, refundRims: false);   // 오른쪽 칸부터 — 첫 칸은 바로 꺼진다
                        if (k >= 1f) break;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    break;
                }
                case WeaponFormBase.StackReason.Idle:
                    while (true)
                    {
                        if (gen != _formGen) return;
                        float k = Mathf.Clamp01((Time.unscaledTime - t0) / FormIdleSec);
                        PaintFormFade(to, from, k);
                        if (k >= 1f) break;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    break;
                default:   // 맞음
                    PaintForm(from, hurt: true);
                    while (Time.unscaledTime - t0 < FormHurtSec) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    break;
            }
        }
        catch (System.OperationCanceledException) { return; }
        if (gen != _formGen) return;
        PaintForm(to, hurt: false);
        if (reason == WeaponFormBase.StackReason.Refund)
            for (int i = 0; i < _formRefund; i++) SettlePipAsync(gen, i).Forget();   // 돌려받은 칸이 금 테와 함께 자리 잡는다
    }

    /// <summary>원거리를 든 동안 — 근접 원을 따라 작아지고 0.6으로 옅어진다(PlaceWeapons가 부른다).</summary>
    private void PlaceFormPill(bool animate)
    {
        if (_formPill == null || !_formPill.gameObject.activeSelf) return;
        bool melee = _activeWeaponIndex <= 0;
        float s = melee ? 1f : FormInactiveScale, a = melee ? 1f : FormInactiveAlpha;
        if (_formPillCts != null) { _formPillCts.Cancel(); _formPillCts.Dispose(); _formPillCts = null; }
        if (!animate || !isActiveAndEnabled)
        {
            _formPill.localScale = Vector3.one * s;
            _formPillGroup.alpha = a;
            return;
        }
        float s0 = _formPill.localScale.x, a0 = _formPillGroup.alpha;
        _formPillCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        UIMotion.Tween(UIMotion.Swap, k =>
        {
            if (_formPill == null) return;
            _formPill.localScale = Vector3.one * Mathf.Lerp(s0, s, k);
            _formPillGroup.alpha = Mathf.Lerp(a0, a, k);
        }, _formPillCts.Token).SuppressCancellationThrow().Forget();
    }
}
