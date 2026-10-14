using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RelicFairy.UI
{
    public enum BossBarkType
    {
        /// <summary>패턴 예고 — 화면 상단 중앙, 큰 텍스트, 1.5초</summary>
        PatternAnnounce,
        /// <summary>보스 대사/일반 연출 — 화면 하단 중앙, 소형 텍스트, 3.5초</summary>
        Bark,
        /// <summary>페이즈 전환 — 화면 정중앙, 최대 텍스트, 4초</summary>
        PhaseAnnounce,
        /// <summary>멀린 내레이션 — 화면 하단 중앙, 중형 텍스트, 5초. 보스 bark와 구분되는 서사 대사.</summary>
        MerlinNarration,
        /// <summary>보스 등장 이름 — 화면 하단 중앙, 대형 텍스트, 4초. 카메라 팬과 함께 등장.</summary>
        BossIntro,
    }

    /// <summary>
    /// 비차단 실시간 보스 자막. 중앙 화면에 검은 배경 + 텍스트.
    /// 플레이어 입력 없이 자동 페이드아웃. 패턴 SO나 LichMonster에서 직접 호출.
    /// 사용: UI_BossBark.Show("텍스트", BossBarkType.PatternAnnounce);
    /// </summary>
    public sealed class UI_BossBark : MonoBehaviour
    {
        /// <summary>걷기 요청(<see cref="Dismiss"/>)이 와도 막 뜬 줄은 이만큼은 읽히게 둔다(초).</summary>
        private const float DismissMinShown = 2f;

        // 글자 단계(10-11 톤 v2 · 비교 리서치 A §2-4) — 보스 이름 20 < 대사 · 멀린 24 < 기술 이름 32 < 페이즈 · 등장 48, 화자 줄 17.
        // 서체는 전부 Noto Medium(DNF Bold였다 — 화면에서 가장 굵은 글자가 이것들이었다). 크기는 코드가 정한다:
        // @UIRoot 프리팹에 옛 값(_barkFontSize 32 · _patternFontSize 44 · 64 · 34 · 56)이 직렬화돼 있어 필드 기본값을 바꿔도
        // 프리팹 값이 이겼다 → 직렬화 필드를 걷고 상수로 둔다(프리팹에 남은 옛 키는 더 읽히지 않는다).
        private const float PatternFontSize = UITheme.Thin.H2;      // 32
        private const float BarkFontSize    = UITheme.Thin.Title;   // 24
        private const float PhaseFontSize   = UITheme.Thin.H1;      // 48
        private const float MerlinFontSize  = UITheme.Thin.Title;   // 24
        private const float IntroFontSize   = UITheme.Thin.H1;      // 48
        private const float SpeakerFontSize = UITheme.Thin.Body;    // 17
        // 한 줄 최대 폭(1080 기준 px) — 글 칸(1152)을 꽉 채운 한 줄은 눈이 너무 멀리 간다. 넘으면 두 줄로 접힌다
        // (기술 이름 32 ≈ 한글 32자 · 대사 24 ≈ 43자). 칸 높이(두 줄)도 넘치는 긴 줄만 이 비율까지 글자를 줄인다.
        private const float MaxLineWidth  = 960f;
        private const float ShrinkToRatio = 0.75f;
        private const float PortraitSize  = 112f;   // 보스 초상 원(10-13 힉스필드 · 사용자 「보스들도 대사가 있으니 초상화가 하나씩」)
        private const float PortraitGap   = 28f;
        private const float SpeakerAboveText = 12f;    // 화자줄 아래 끝 — 글 위 끝에서(프리팹 값)
        private const float DividerAboveText = 5f;     // 밑줄 — 글 위 끝에서(프리팹 값)
        private const float DividerWidth     = 240f;   // 밑줄 최대 폭(프리팹 값) — 글이 더 짧으면 글 폭까지

        public static UI_BossBark Instance { get; private set; }

        [Header("References")]
        [SerializeField] private CanvasGroup   _canvasGroup;
        [SerializeField] private Image         _background;
        [SerializeField] private TMP_Text      _label;
        [SerializeField] private RectTransform _container;

        [Header("Speaker (선택) — 화자가 없으면 통째로 숨겨져 기존 연출과 동일하게 동작")]
        [SerializeField] private TMP_Text _speakerLabel;
        [SerializeField] private Image    _speakerDivider;

        [Header("Speaker Colors")]
        [SerializeField] private Color _lichColor    = new Color(0.66f, 0.33f, 0.97f); // 아케인 보라
        [SerializeField] private Color _mordredColor = new Color(0.88f, 0.27f, 0.25f); // 타락 적
        [SerializeField] private Color _knightColor  = new Color(0.85f, 0.84f, 0.80f); // 기사 회백
        [SerializeField] private Color _merlinColor  = new Color(0.31f, 0.66f, 0.88f); // 멀린 청

        [Header("PatternAnnounce")]
        [SerializeField] private float _patternDuration  = 1.5f;
        [SerializeField] private float _patternOffsetY   = 200f;

        [Header("Bark")]
        [SerializeField] private float _barkDuration     = 3.5f;
        [SerializeField] private float _barkOffsetY      = -220f;

        [Header("PhaseAnnounce")]
        [SerializeField] private float _phaseDuration    = 4.0f;
        [SerializeField] private float _phaseOffsetY     = 0f;

        [Header("MerlinNarration")]
        [SerializeField] private float _merlinDuration   = 5.0f;
        [SerializeField] private float _merlinOffsetY    = -280f;

        [Header("BossIntro")]
        [SerializeField] private float _introDuration    = 4.0f;
        [SerializeField] private float _introOffsetY     = -160f;

        [Header("Fade")]
        [SerializeField] private float _fadeInDuration   = 0.15f;
        [SerializeField] private float _fadeOutDuration  = 0.45f;

        // scene = 줄을 건 순간의 활성 씬 — 씬이 바뀌면 그 씬의 줄은 버린다(DDOL @UIRoot라 줄이 다음 씬까지 따라왔다:
        // 10-09 전주기 시뮬 · 프롤로그 보스 패턴 예고가 거점 첫 대사 위에 떴다).
        private readonly Queue<(string text, BossBarkType type, DialogueSpeaker speaker, UniTaskCompletionSource tcs, int scene)> _queue = new();
        private RectTransform _portraitGroup;
        private Image         _portraitImage;
        private readonly Dictionary<string, Sprite> _portraitCache = new();
        private int           _portraitReq;
        private CancellationTokenSource _cts;
        private bool _showing;
        private bool  _dismissRequested;   // 장면이 끝났다 — 지금 줄을 일찍 내린다
        private float _dismissMinShown;

        private void Awake()
        {
            Instance = this;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            // 반투명 검은 네모 띠(웹 자막처럼 보였다) → 좌우 끝이 흐려지는 띠. 글 폭(1152)이 흐림 구간에 덜 걸리게
            // 양옆으로 240씩 넓힌다(09-28 UI 톤 통일). 글자는 부드러운 그림자(글자 정본).
            if (_background != null)
            {
                _background.sprite = UITheme.SoftBand;
                _background.type   = Image.Type.Simple;
                _background.color  = new Color(0.02f, 0.02f, 0.04f, 0.80f);
                _background.rectTransform.sizeDelta += new Vector2(480f, 24f);
            }
            if (_label != null) TMPOutlineHelper.ApplySoftShadow(_label);
            if (_speakerLabel != null) TMPOutlineHelper.ApplySoftShadow(_speakerLabel);
            // 화자 밑줄 2px → 1px — 톤 v2 선은 평소 1 · 준비됨 2 · 특수 3, 이 밑줄은 평소 선이다(10-11).
            if (_speakerDivider != null)
            {
                var d = _speakerDivider.rectTransform;
                d.sizeDelta = new Vector2(d.sizeDelta.x, UITheme.Thin.Stroke);
            }
            BuildPortrait();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            _cts?.Cancel();
            _cts?.Dispose();
            var addressables = Managers.AddressableManager;
            if (addressables != null)
                foreach (var key in _portraitCache.Keys) addressables.ReleaseAsset<Sprite>(key);
            _portraitCache.Clear();
        }

        // ─── Public API ───────────────────────────────────────────────

        /// <summary>자막이 떠 있거나 대기 중인가 — 보스 끝 장면 대사가 끝나길 기다리는 쪽이 읽는다(10-02 되찾은 기억 연출).</summary>
        public static bool IsShowing => Instance != null && Instance._showing;

        /// <summary>speaker를 넘기면 본문 위에 화자줄이 붙는다. 생략하면 기존 동작 그대로.</summary>
        public static void Show(string text, BossBarkType type = BossBarkType.Bark,
                               DialogueSpeaker speaker = DialogueSpeaker.None)
        {
            if (Instance == null) return;
            Instance.Enqueue(text, type, speaker, null);
        }

        /// <summary>
        /// 대사 CSV(DIALOGUE_DATA)의 한 시퀀스를 자막으로 순서대로 띄운다. 시퀀스가 없으면 false.
        /// 멀린·그림자 줄은 내레이션, 그 밖의 화자는 첫 줄만 <paramref name="firstLineType"/>이고 나머지는 일반 바크 —
        /// PhaseAnnounce는 큐를 비우므로 연달아 쓰면 앞줄이 지워진다.
        /// </summary>
        public static bool ShowDialogue(string sequenceKey, BossBarkType firstLineType = BossBarkType.Bark)
        {
            if (Instance == null || string.IsNullOrEmpty(sequenceKey)) return false;
            var lines = Managers.DialogueData?.GetLines(sequenceKey);
            if (lines == null || lines.Length == 0) return false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line == null || string.IsNullOrEmpty(line.text)) continue;
                Instance.Enqueue(line.text, TypeFor(line.speaker, i == 0 ? firstLineType : BossBarkType.Bark), line.speaker, null);
            }
            return true;
        }

        /// <summary>
        /// 대사 CSV 시퀀스의 한 줄만 띄운다. <paramref name="interrupt"/>면 대기열을 비우고 바로 띄운다 —
        /// 컷신처럼 줄마다 시각이 정해져 있을 때(앞 줄이 길어 뒤로 밀리지 않게).
        /// </summary>
        public static bool ShowDialogueLine(string sequenceKey, int index,
                                            BossBarkType bossLineType = BossBarkType.Bark, bool interrupt = false)
        {
            if (Instance == null || string.IsNullOrEmpty(sequenceKey)) return false;
            var lines = Managers.DialogueData?.GetLines(sequenceKey);
            if (lines == null || index < 0 || index >= lines.Length) return false;

            var line = lines[index];
            if (line == null || string.IsNullOrEmpty(line.text)) return false;

            if (interrupt)
            {
                Instance.DropQueued();   // 기다리던 쪽도 풀어 준다(Clear는 영영 안 풀렸다)
                Instance.InterruptCurrent();
            }
            Instance.Enqueue(line.text, TypeFor(line.speaker, bossLineType), line.speaker, null);
            return true;
        }

        /// <summary>멀린·그림자 줄은 내레이션 자막, 그 밖의 화자는 지정한 종류.</summary>
        private static BossBarkType TypeFor(DialogueSpeaker speaker, BossBarkType otherType)
            => speaker == DialogueSpeaker.Merlin || speaker == DialogueSpeaker.Shadow
                ? BossBarkType.MerlinNarration
                : otherType;

        /// <summary>표시 후 페이드아웃까지 완료될 때까지 대기한다. (보스 등장 연출의 카메라 단계 동기화용)</summary>
        public static UniTask ShowAndWaitAsync(string text, BossBarkType type = BossBarkType.Bark,
                                               DialogueSpeaker speaker = DialogueSpeaker.None)
        {
            if (Instance == null) return UniTask.CompletedTask;
            var tcs = new UniTaskCompletionSource();
            Instance.Enqueue(text, type, speaker, tcs);
            return tcs.Task;
        }

        /// <summary>
        /// 대기열을 비우고 지금 줄을 끊고 바로 띄운다 — 카운터처럼 최신 값만 의미 있는 알림용
        /// (10-01 f5 전주기 시뮬: 봉인석 점화 2/4 · 3/4가 줄을 서서 붕괴 컷신 위에 늦게 나왔다).
        /// </summary>
        public static void ShowLatest(string text, BossBarkType type = BossBarkType.Bark,
                                      DialogueSpeaker speaker = DialogueSpeaker.None)
        {
            if (Instance == null) return;
            Instance.DropQueued();
            Instance.InterruptCurrent();
            Instance.Enqueue(text, type, speaker, null);
        }

        /// <summary>
        /// 대기열을 비우고 지금 자막을 페이드아웃시킨다 — 장면이 끝나면 그 장면의 대사도 같이 걷는다
        /// (10-01 f5 전주기 시뮬: 봉인 장면 대사가 6초 넘게 남아 포탈 화면까지 따라왔다). 막 뜬 줄은 <paramref name="minShown"/>초는 보여 준다.
        /// </summary>
        public static void Dismiss(float minShown = DismissMinShown)
        {
            if (Instance == null) return;
            Instance.DropQueued();
            if (!Instance._showing) return;
            Instance._dismissRequested = true;
            Instance._dismissMinShown  = minShown;
        }

        // ─── Internal ─────────────────────────────────────────────────

        private void Enqueue(string text, BossBarkType type, DialogueSpeaker speaker, UniTaskCompletionSource tcs)
        {
            // PhaseAnnounce는 큐를 비우고 즉시 표시 (페이즈 전환은 최우선)
            // MerlinNarration은 큐 유지 — 서사 대사는 순서대로 출력
            if (type == BossBarkType.PhaseAnnounce)
            {
                DropQueued();
                InterruptCurrent();
            }

            _queue.Enqueue((text, type, speaker, tcs, SceneManager.GetActiveScene().handle));

            if (!_showing)
                RunQueue(destroyCancellationToken).Forget();
        }

        /// <summary>
        /// 표시 중인 자막만 취소한다. <b>_showing/_cts는 건드리지 않는다</b> —
        /// 여기서 _showing을 내리면 뒤이은 Enqueue가 RunQueue를 하나 더 띄우고,
        /// 먼저 돌던 루프의 finally가 새 루프의 _cts를 지워 이후 취소가 먹지 않았다
        /// (그 상태로 페이드 도중 취소되면 알파가 중간값으로 굳어 자막이 화면에 남는다).
        /// 취소된 루프는 자기 while로 돌아와 새로 들어온 항목을 그대로 이어 처리한다.
        /// </summary>
        private void InterruptCurrent() => _cts?.Cancel();

        /// <summary>대기 중인 줄을 버린다 — 기다리던 쪽(<see cref="ShowAndWaitAsync"/>)은 끝난 것으로 풀어 준다.</summary>
        private void DropQueued()
        {
            while (_queue.Count > 0)
                _queue.Dequeue().tcs?.TrySetResult();
        }

        private async UniTaskVoid RunQueue(CancellationToken lifetimeToken)
        {
            _showing = true;
            try
            {
                while (_queue.Count > 0)
                {
                    if (lifetimeToken.IsCancellationRequested) return;

                    var (text, type, speaker, tcs, scene) = _queue.Dequeue();
                    if (scene != SceneManager.GetActiveScene().handle) { tcs?.TrySetResult(); continue; }

                    _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

                    try
                    {
                        await DisplayOne(text, type, speaker, scene, _cts.Token);
                    }
                    catch (System.OperationCanceledException) { }
                    finally
                    {
                        _cts?.Dispose();
                        _cts = null;
                        tcs?.TrySetResult();
                    }
                }
            }
            finally
            {
                _showing = false;
                // 페이드 도중 취소로 빠져나오면 알파가 중간값이다 — 이어받을 자막이 없으면 확실히 숨긴다.
                if (_canvasGroup != null && _queue.Count == 0) _canvasGroup.alpha = 0f;
            }
        }

        private async UniTask DisplayOne(string text, BossBarkType type, DialogueSpeaker speaker, int scene, CancellationToken ct)
        {
            ApplyLayout(text, type);
            ApplySpeaker(speaker);
            ApplyPortrait(speaker);

            float holdDuration = type switch
            {
                BossBarkType.PatternAnnounce => _patternDuration,
                BossBarkType.PhaseAnnounce   => _phaseDuration,
                BossBarkType.MerlinNarration => _merlinDuration,
                BossBarkType.BossIntro       => _introDuration,
                _                            => _barkDuration,
            };

            _dismissRequested = false;
            float shownAt = Time.unscaledTime;
            await FadeTo(1f, _fadeInDuration, ct);
            // unscaled 필수 — 스케일드면 timeScale=0(일시정지·차단 팝업) 동안 유지 시간이 흐르지 않아
            // 자막이 화면에 굳는다. 페이드(FadeTo)도 이미 unscaled다. 걷기 요청이 오면 최소 표시 시간만 채우고 내린다.
            // 씬이 바뀌면 바로 내린다.
            float hideAt = Time.unscaledTime + holdDuration;
            while (Time.unscaledTime < hideAt
                   && !(_dismissRequested && Time.unscaledTime - shownAt >= _dismissMinShown)
                   && scene == SceneManager.GetActiveScene().handle)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await FadeTo(0f, _fadeOutDuration, ct);
        }

        private void ApplyLayout(string text, BossBarkType type)
        {
            float fontSize, offsetY;

            switch (type)
            {
                case BossBarkType.PatternAnnounce:
                    fontSize = PatternFontSize;
                    offsetY  = _patternOffsetY;
                    break;
                case BossBarkType.PhaseAnnounce:
                    fontSize = PhaseFontSize;
                    offsetY  = _phaseOffsetY;
                    break;
                case BossBarkType.MerlinNarration:
                    fontSize = MerlinFontSize;
                    offsetY  = _merlinOffsetY;
                    break;
                case BossBarkType.BossIntro:
                    fontSize = IntroFontSize;
                    offsetY  = _introOffsetY;
                    break;
                default:
                    fontSize = BarkFontSize;
                    offsetY  = _barkOffsetY;
                    break;
            }

            if (_label != null)
            {
                // 띄울 때마다 바른다 — @UIRoot는 앱 부트 때 생겨 Awake엔 톤 v2 글꼴(Addressables)이 아직 없다.
                UITheme.StyleThinText(_label, UIWeight.Medium, fontSize, UITheme.Thin.Ink, shadow: true);
                _label.enableAutoSizing = true;
                _label.fontSizeMax      = fontSize;
                _label.fontSizeMin      = Mathf.Round(fontSize * ShrinkToRatio);
                float side = Mathf.Max(0f, (_label.rectTransform.rect.width - MaxLineWidth) * 0.5f);
                _label.margin = new Vector4(side, 0f, side, 0f);
                _label.text   = text;
            }

            if (_container != null)
                _container.anchoredPosition = new Vector2(0f, offsetY);
        }

        /// <summary>화자줄 표시/숨김. None이면 라벨·구분선을 끄고 기존 레이아웃 그대로 둔다.</summary>
        private void ApplySpeaker(DialogueSpeaker speaker)
        {
            bool has = speaker != DialogueSpeaker.None;
            if (has) PlaceSpeaker();

            if (_speakerLabel != null)
            {
                _speakerLabel.gameObject.SetActive(has);
                if (has)
                {
                    UITheme.StyleThinText(_speakerLabel, UIWeight.Medium, SpeakerFontSize, SpeakerColor(speaker), shadow: true);
                    _speakerLabel.text = SpeakerName(speaker);
                }
            }

            if (_speakerDivider != null)
            {
                _speakerDivider.gameObject.SetActive(has);
                if (has)
                {
                    var c = SpeakerColor(speaker);
                    c.a = 0.5f;
                    _speakerDivider.color = c;
                }
            }
        }

        /// <summary>
        /// 화자줄 · 밑줄을 그린 글의 왼쪽 끝 · 바로 위에 붙인다. 프리팹 자리는 글 칸(1152)의 왼쪽 위 끝이라, 글이 가운데 960 안으로
        /// 좁혀진 뒤(10-11 톤 v2) 짧은 줄이면 화자 이름이 글에서 멀리 떨어져 보였다(10-14 cf 봉인 장면 사진 「???」).
        /// 화자줄 · 밑줄의 앵커는 그릇(_container) 왼쪽 위 · 피벗 아래 — 프리팹의 글 위 12 · 5 간격은 그대로 둔다.
        /// </summary>
        private void PlaceSpeaker()
        {
            if (_label == null || _container == null) return;
            var (textLeft, textTop, width) = TextBox();
            float left = textLeft + _container.rect.width * 0.5f;    // 그릇 왼쪽 끝에서 글 왼쪽 끝까지
            float top  = textTop - _container.rect.height * 0.5f;    // 그릇 위 끝에서 글 위 끝까지(아래로 음수)
            if (_speakerLabel != null)
                _speakerLabel.rectTransform.anchoredPosition = new Vector2(left, top + SpeakerAboveText);
            if (_speakerDivider != null)
            {
                var d = _speakerDivider.rectTransform;
                d.anchoredPosition = new Vector2(left, top + DividerAboveText);
                d.sizeDelta        = new Vector2(Mathf.Min(DividerWidth, width), d.sizeDelta.y);
            }
        }

        /// <summary>
        /// 그린 글의 왼쪽 끝 x · 위 끝 y · 폭 — 그릇 가운데 기준. 줄바꿈 · 자동 크기 · 글 칸 여백을 다 거친 실제 자리
        /// (GetPreferredValues는 여백(양옆 96)까지 폭에 넣어 화자줄 · 초상이 96px 왼쪽으로 밀렸다, 10-14 실측).
        /// </summary>
        private (float left, float top, float width) TextBox()
        {
            _label.ForceMeshUpdate();
            var b  = _label.textBounds;
            var lt = _container.InverseTransformPoint(_label.rectTransform.TransformPoint(new Vector3(b.min.x, b.max.y, 0f)));
            return (lt.x, lt.y, b.size.x);
        }

        /// <summary>초상 원 — 먹색 원판(마스크) 안에 반신 그림을 얼굴이 가운데 오게 · 바깥에 얇은 금 고리.</summary>
        private void BuildPortrait()
        {
            if (_container == null) return;
            var group = new GameObject("BossPortrait", typeof(RectTransform));
            _portraitGroup = (RectTransform)group.transform;
            _portraitGroup.SetParent(_container, false);
            _portraitGroup.anchorMin = _portraitGroup.anchorMax = new Vector2(0.5f, 0.5f);
            _portraitGroup.sizeDelta = new Vector2(PortraitSize, PortraitSize);

            var disc = new GameObject("Disc", typeof(RectTransform), typeof(Image), typeof(Mask));
            var dr   = (RectTransform)disc.transform;
            dr.SetParent(_portraitGroup, false);
            dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            var discImage = disc.GetComponent<Image>();
            discImage.sprite        = UIProceduralSprites.Circle(128);
            discImage.color         = new Color(0.03f, 0.03f, 0.05f, 1f);   // 그림 바탕과 같은 먹색
            discImage.raycastTarget = false;

            // 2:3 반신 그림 — 위쪽 1/4쯤의 얼굴이 원 가운데 오게 위를 맞춰 넘치게 둔다
            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            var fr   = (RectTransform)face.transform;
            fr.SetParent(dr, false);
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f);
            fr.pivot     = new Vector2(0.5f, 1f);
            fr.sizeDelta = new Vector2(PortraitSize * 1.4f, PortraitSize * 2.1f);
            fr.anchoredPosition = new Vector2(0f, PortraitSize * 0.5f + 4f);
            _portraitImage = face.GetComponent<Image>();
            _portraitImage.preserveAspect = true;
            _portraitImage.raycastTarget  = false;

            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var rr   = (RectTransform)ring.transform;
            rr.SetParent(_portraitGroup, false);
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(PortraitSize + 6f, PortraitSize + 6f);
            var ringImage = ring.GetComponent<Image>();
            ringImage.sprite        = UIProceduralSprites.Ring(0.03f, 128);
            ringImage.color         = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.85f);
            ringImage.raycastTarget = false;

            group.SetActive(false);
        }

        /// <summary>화자에 초상이 있으면 글 왼쪽에 띄운다(처음엔 불러온 뒤). 없으면 숨김.</summary>
        private void ApplyPortrait(DialogueSpeaker speaker)
        {
            if (_portraitGroup == null) return;
            int    req = ++_portraitReq;
            string key = PortraitKey(speaker);
            if (key == null) { _portraitGroup.gameObject.SetActive(false); return; }

            float left = _label != null ? TextBox().left : -MaxLineWidth * 0.5f;
            float y    = _label != null ? _container.InverseTransformPoint(_label.rectTransform.TransformPoint(_label.rectTransform.rect.center)).y : 0f;
            _portraitGroup.anchoredPosition = new Vector2(left - PortraitSize * 0.5f - PortraitGap, y);

            if (_portraitCache.TryGetValue(key, out var cached)) { SetPortrait(cached); return; }
            _portraitGroup.gameObject.SetActive(false);
            LoadPortraitAsync(key, req).Forget();
        }

        private async UniTaskVoid LoadPortraitAsync(string key, int req)
        {
            var addressables = Managers.AddressableManager;
            if (addressables == null) return;
            var sprite = await addressables.TryLoadAssetAsync<Sprite>(key);
            if (sprite == null) return;
            if (this == null || _portraitCache.ContainsKey(key)) { addressables.ReleaseAsset<Sprite>(key); if (this == null) return; }
            else _portraitCache[key] = sprite;
            if (req == _portraitReq) SetPortrait(_portraitCache[key]);
        }

        private void SetPortrait(Sprite sprite)
        {
            _portraitImage.sprite = sprite;
            _portraitGroup.gameObject.SetActive(true);
        }

        /// <summary>보스 초상 주소. 프롤로그 모르드레드(타락 전 서임 대사)는 타락한 그림을 쓰지 않는다 — 없음.</summary>
        private static string PortraitKey(DialogueSpeaker speaker) => speaker switch
        {
            DialogueSpeaker.ForestGuardian => "Portrait_Boss_ForestGuardian",
            DialogueSpeaker.Dragon         => "Portrait_Boss_Dragon",
            DialogueSpeaker.DeathKnight    => "Portrait_Boss_Mordred",
            DialogueSpeaker.Lich           => "Lich_Portrait",
            _                              => null,
        };

        private static string SpeakerName(DialogueSpeaker speaker) => speaker switch
        {
            DialogueSpeaker.Lich    => "리치",
            DialogueSpeaker.Mordred => "모르드레드",
            DialogueSpeaker.Knight  => "기사",
            // 대사창(UI_DialoguePopup)과 같은 공개 규칙 — 리치가 이름을 부른 뒤 멀린, 엔딩 뒤 모르가나.
            DialogueSpeaker.Merlin  => StoryProgress.IsMerlinNamed ? "멀린" : "???",
            DialogueSpeaker.Shadow  => StoryProgress.HasEnded ? "모르가나" : "그림자",
            DialogueSpeaker.Arthur  => "아서왕",
            DialogueSpeaker.ForestGuardian => "숲의 수호자",
            DialogueSpeaker.Dragon  => "화룡",
            DialogueSpeaker.DeathKnight    => "모르드레드",   // 인트로 모르드레드와 같은 인물(10-13 이름 통일 — 옛 「죽음의 기사」)
            _                       => string.Empty,
        };

        private Color SpeakerColor(DialogueSpeaker speaker) => speaker switch
        {
            DialogueSpeaker.Lich    => _lichColor,
            DialogueSpeaker.Mordred => _mordredColor,
            DialogueSpeaker.Knight  => _knightColor,
            _                       => _merlinColor,
        };

        private async UniTask FadeTo(float target, float duration, CancellationToken ct)
        {
            if (_canvasGroup == null) return;

            float start = _canvasGroup.alpha;
            float t = 0f;
            float dur = Mathf.Max(0.0001f, duration);

            while (t < 1f)
            {
                ct.ThrowIfCancellationRequested();
                t += Time.unscaledDeltaTime / dur;
                _canvasGroup.alpha = Mathf.Lerp(start, target, t);
                await UniTask.Yield(ct);
            }

            _canvasGroup.alpha = target;
        }
    }
}
