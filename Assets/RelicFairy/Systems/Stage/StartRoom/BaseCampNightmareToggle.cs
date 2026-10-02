using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 악몽 모드 갈림길 — 엔딩 뒤 출발 게이트 곁에 드러나는 상호작용 하나(순환 개정 v3 D3 · UX 정본 f5 10-01).
///   · [F] → 두 장 선택 창: 「해방된 세계로」(지금 그대로) / 「악몽으로 들어간다」(악몽 규칙 · 보스 악몽 강화 · 정수 배율).
///   · 고른 값은 <see cref="StoryProgress.SetNightmareMode"/>로 기억한다 — 다음 내려감부터. 거점에서만 바꾼다(런 중엔 이 오브젝트가 없다).
///   · 켜져 있으면 게이트가 황혼 보라~진홍으로 물들고 이름판에 「악몽」 한 줄(<see cref="BaseCampFxDirector.ApplyNightmareLook"/>).
/// 받침 · 수정 · 빛은 <see cref="BaseCampFxDirector"/>가 세워 넘긴다(해금 전엔 아예 만들지 않는다).
/// 창은 코드로 짠다(심연 깊이 갈림길과 같은 방식) — 시간을 멈추고 플레이어 입력을 막는다. 바깥을 누르거나 [F]로 닫는다
/// ([Esc]는 ESC 메뉴가 쥐고 있어 쓰지 않는다).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class BaseCampNightmareToggle : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const float LabelAboveCrystal  = 0.75f;
    private const float PromptAboveCrystal = 1.15f;
    private const float SpinDegPerSec      = 16f;
    private const float BobAmplitude       = 0.08f;
    private const float BobSpeed           = 1.3f;
    private const float CardW              = 360f;
    private const float CardH              = 210f;
    private const int   Undecided          = -2;
    private const int   Closed             = -1;
    private const int   PickLiberated      = 0;
    private const int   PickNightmare      = 1;

    // ── Private ───────────────────────────────────────────────
    private BaseCampFxDirector _director;
    private Transform   _crystal;
    private Vector3     _crystalBase;
    private Color       _accent;
    private PlayerController _player;
    private Transform   _cam;
    private TextMeshPro _label;
    private GameObject  _prompt;
    private bool        _busy;
    private bool        _ready;   // 드러남 연출이 끝났다 — 그 전엔 [F]를 받지 않는다
    private int         _picked;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake() => GetComponent<SphereCollider>().isTrigger = true;

    private void Update()
    {
        if (_crystal != null)
        {
            _crystal.Rotate(0f, SpinDegPerSec * Time.deltaTime, 0f, Space.World);
            _crystal.localPosition = _crystalBase + Vector3.up * (Mathf.Sin(Time.time * BobSpeed) * BobAmplitude);
        }
        Billboard();
        if (!_ready || _busy || _player == null || UIInputGate.Blocked) return;
        if (Input.GetKeyDown(KeyCode.F)) OpenAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy() => BaseCampLabelRule.Unregister(transform);

    private void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p == null) return;
        _player = p;
        ShowPrompt(_ready && !_busy);
    }

    private void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerController>();
        if (p != null && p == _player) { _player = null; ShowPrompt(false); }
    }

    // ── Public Methods ────────────────────────────────────────

    /// <param name="crystalHeight">받침 위 수정 높이(로컬 m) — 이름표·[F] 안내를 그 위에 띄운다.</param>
    public void Setup(BaseCampFxDirector director, Transform crystal, Color accent, float crystalHeight)
    {
        _director    = director;
        _crystal     = crystal;
        _crystalBase = crystal != null ? crystal.localPosition : Vector3.zero;
        _accent      = accent;
        CreateTexts(crystalHeight);
        // 「가까운 곳 하나만」 이름표 규칙에 든다 — 곁에 오면 바로 옆 유물 성소 이름표 대신 이 이름표가 뜬다
        // (빠져 있을 땐 유물 성소 이름표가 갈림길 위에 떠 받침이 「유물 성소」로 읽혔다, 10-01 f5 검토).
        BaseCampLabelRule.Register(transform);
    }

    /// <summary>드러남 연출이 끝났다 — 이제 [F]를 받는다.</summary>
    public void MarkReady()
    {
        _ready = true;
        ShowPrompt(_player != null && !_busy);
    }

    // ── Private Methods ───────────────────────────────────────

    private async UniTaskVoid OpenAsync(CancellationToken ct)
    {
        _busy = true;
        ShowPrompt(false);
        try
        {
            bool wasOn = StoryProgress.IsNightmareMode;
            int pick = await ShowChoiceAsync(wasOn, _player, ct);
            if (pick == Closed || (pick == PickNightmare) == wasOn) return;

            bool on = pick == PickNightmare;
            StoryProgress.SetNightmareMode(on);
            _director?.ApplyNightmareLook(StoryProgress.IsNightmareMode, animate: true);
            ZoneSign.RequestTitle(on ? "악몽으로 들어간다" : "해방된 세계로",
                                  on ? "다음 내려감부터 쓰러뜨린 것들의 악몽이 깨어난다" : "다음 내려감은 지금 그대로");
            Debug.Log($"[NightmareToggle] {(on ? "악몽 모드 켬" : "악몽 모드 끔")} — 지금 시기 {StoryProgress.Era}");
        }
        catch (OperationCanceledException) { }
        finally
        {
            _busy = false;
            ShowPrompt(_ready && _player != null);
        }
    }

    /// <summary>두 장 선택 창 — 고른 쪽(0 해방 · 1 악몽) 또는 닫음(-1). 지금 상태 카드에 「지금」 표시.</summary>
    private async UniTask<int> ShowChoiceAsync(bool currentOn, PlayerController player, CancellationToken ct)
    {
        _picked = Undecided;
        var go = new GameObject("@NightmareChoice", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UISortingOrder.RunChoice;
        // 프로젝트 캔버스 기준 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        var group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;

        TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);
        if (player != null) player.SetInputEnabled(false);   // 카드를 누르는 클릭이 공격으로 새지 않게
        try
        {
            // 바탕 막 — 바깥을 누르면 그대로 두고 닫는다
            var veil = NewRect(go.transform, "Veil", Vector2.zero, Vector2.zero);
            veil.anchorMin = Vector2.zero; veil.anchorMax = Vector2.one;
            veil.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            var veilBtn = veil.gameObject.AddComponent<Button>();
            veilBtn.transition = Selectable.Transition.None;
            veilBtn.onClick.AddListener(() => _picked = Closed);

            NewText(go.transform, "어느 세계로 내려가는가", 34f, UITheme.Gold, new Vector2(0f, 220f), new Vector2(900f, 46f));
            NewText(go.transform, "고른 것은 기억된다 · 거점에서만 바꿀 수 있다", 20f, UITheme.Mute, new Vector2(0f, 168f), new Vector2(900f, 30f));

            NewCard(go.transform, new Vector2(-(CardW * 0.5f + 20f), -10f), "해방된 세계로", "지금 그대로 — 해방기 규칙",
                    UITheme.Ink, !currentOn, () => _picked = PickLiberated);
            NewCard(go.transform, new Vector2(CardW * 0.5f + 20f, -10f), "악몽으로 들어간다",
                    $"챕터마다 악몽 규칙 · 보스 악몽 강화\n정수 ×{NightmareRules.ModeEssenceMultiplier:0.0#}",
                    _accent, currentOn, () => _picked = PickNightmare);

            NewText(go.transform, "[F] 또는 바깥을 누르면 닫는다", 18f, UITheme.Mute, new Vector2(0f, -170f), new Vector2(900f, 28f));

            FadeAsync(group, 1f, UIFader.OpenSec, ct).Forget();
            // 창을 연 [F]가 같은 프레임에 닫기로 읽히지 않게 한 프레임 넘긴다
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            while (_picked == Undecided)
            {
                if (Input.GetKeyDown(KeyCode.F)) _picked = Closed;
                else await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            group.blocksRaycasts = false;
            await FadeAsync(group, 0f, UIFader.CloseSec, ct);
            return _picked;
        }
        finally
        {
            TimeScaleArbiter.Release(this);
            if (player != null) player.SetInputEnabled(true);
            if (go != null) Destroy(go);
        }
    }

    /// <summary>선택 카드 — 제목 + 설명, 지금 상태면 위에 「지금」과 진한 테두리.</summary>
    private static void NewCard(Transform parent, Vector2 pos, string title, string desc, Color accent, bool current, Action onClick)
    {
        var rt = NewRect(parent, $"Card_{title}", new Vector2(CardW, CardH), pos);
        var img = rt.gameObject.AddComponent<Image>();
        UITheme.StylePanel(img, UITheme.Band, new Color(accent.r, accent.g, accent.b, current ? 0.9f : 0.45f));

        NewText(rt, title, 28f, accent, new Vector2(0f, 46f), new Vector2(CardW - 30f, 40f));
        var d = NewText(rt, desc, 17f, UITheme.Mute, new Vector2(0f, -30f), new Vector2(CardW - 40f, 80f));
        d.textWrappingMode = TextWrappingModes.Normal;
        if (current) NewText(rt, "· 지금 ·", 18f, accent, new Vector2(0f, CardH * 0.5f + 18f), new Vector2(200f, 28f));

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition    = Selectable.Transition.ColorTint;
        btn.targetGraphic = img;
        var cb = btn.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f); cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick?.Invoke());
        rt.gameObject.AddComponent<UIButtonFeedback>();   // UI_Popup을 거치지 않으니 팝업과 같은 손맛을 직접 건다
    }

    private static RectTransform NewRect(Transform parent, string name, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box)
    {
        var rt = NewRect(parent, "Text", box, pos);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text             = text;
        t.fontSize         = size;
        t.color            = color;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        TMPOutlineHelper.ApplySoftShadow(t);   // 기본 글꼴이 이미 굵다 — 가짜 굵게 금지(글자 정본)
        return t;
    }

    private static async UniTask FadeAsync(CanvasGroup g, float to, float seconds, CancellationToken ct)
    {
        float from = g.alpha;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (g == null) return;
            g.alpha = Mathf.Lerp(from, to, t / seconds);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (g != null) g.alpha = to;
    }

    // ── 월드 이름표 · [F] 안내(파츠 공방과 같은 결) ──

    private void CreateTexts(float crystalHeight)
    {
        var labelGo = new GameObject("NightmareLabel");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = Vector3.up * (crystalHeight + LabelAboveCrystal);
        _label = labelGo.AddComponent<TextMeshPro>();
        _label.text             = "악몽의 갈림길";
        _label.fontSize         = 3f;
        _label.alignment        = TextAlignmentOptions.Center;
        _label.color            = Color.Lerp(_accent, Color.white, 0.35f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.sortingOrder     = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(_label);

        _prompt = new GameObject("InteractPrompt");
        _prompt.transform.SetParent(transform, false);
        _prompt.transform.localPosition = Vector3.up * (crystalHeight + PromptAboveCrystal);
        var tmp = _prompt.AddComponent<TextMeshPro>();
        tmp.text             = $"<color={UIPalette.GoldHex}>[F]</color> 세계 고르기";
        tmp.fontSize         = 4f;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.color            = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder     = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplySoftShadow(tmp);
        _prompt.SetActive(false);
    }

    private void Billboard()
    {
        if (_cam == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _cam = cam.transform;
        }
        if (_label != null)
        {
            _label.transform.rotation = _cam.rotation;
            bool show = BaseCampLabelRule.IsShown(transform);
            if (_label.enabled != show) _label.enabled = show;
        }
        if (_prompt != null && _prompt.activeSelf) _prompt.transform.rotation = _cam.rotation;
    }

    private void ShowPrompt(bool on)
    {
        if (_prompt != null && _prompt.activeSelf != on) _prompt.SetActive(on);
    }
}
