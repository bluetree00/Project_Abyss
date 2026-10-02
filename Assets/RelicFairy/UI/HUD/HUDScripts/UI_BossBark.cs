using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
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
        [SerializeField] private float _patternFontSize  = 44f;
        [SerializeField] private float _patternDuration  = 1.5f;
        [SerializeField] private float _patternOffsetY   = 200f;

        [Header("Bark")]
        [SerializeField] private float _barkFontSize     = 28f;
        [SerializeField] private float _barkDuration     = 3.5f;
        [SerializeField] private float _barkOffsetY      = -220f;

        [Header("PhaseAnnounce")]
        [SerializeField] private float _phaseFontSize    = 64f;
        [SerializeField] private float _phaseDuration    = 4.0f;
        [SerializeField] private float _phaseOffsetY     = 0f;

        [Header("MerlinNarration")]
        [SerializeField] private float _merlinFontSize   = 34f;
        [SerializeField] private float _merlinDuration   = 5.0f;
        [SerializeField] private float _merlinOffsetY    = -280f;

        [Header("BossIntro")]
        [SerializeField] private float _introFontSize    = 56f;
        [SerializeField] private float _introDuration    = 4.0f;
        [SerializeField] private float _introOffsetY     = -160f;

        [Header("Fade")]
        [SerializeField] private float _fadeInDuration   = 0.15f;
        [SerializeField] private float _fadeOutDuration  = 0.45f;

        private readonly Queue<(string text, BossBarkType type, DialogueSpeaker speaker, UniTaskCompletionSource tcs)> _queue = new();
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
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            _cts?.Cancel();
            _cts?.Dispose();
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
                Instance._queue.Clear();
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
                _queue.Clear();
                InterruptCurrent();
            }

            _queue.Enqueue((text, type, speaker, tcs));

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

                    var (text, type, speaker, tcs) = _queue.Dequeue();

                    _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

                    try
                    {
                        await DisplayOne(text, type, speaker, _cts.Token);
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

        private async UniTask DisplayOne(string text, BossBarkType type, DialogueSpeaker speaker, CancellationToken ct)
        {
            ApplyLayout(text, type);
            ApplySpeaker(speaker);

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
            float hideAt = Time.unscaledTime + holdDuration;
            while (Time.unscaledTime < hideAt
                   && !(_dismissRequested && Time.unscaledTime - shownAt >= _dismissMinShown))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await FadeTo(0f, _fadeOutDuration, ct);
        }

        private void ApplyLayout(string text, BossBarkType type)
        {
            if (_label != null)
                _label.text = text;

            float fontSize, offsetY;

            switch (type)
            {
                case BossBarkType.PatternAnnounce:
                    fontSize = _patternFontSize;
                    offsetY  = _patternOffsetY;
                    break;
                case BossBarkType.PhaseAnnounce:
                    fontSize = _phaseFontSize;
                    offsetY  = _phaseOffsetY;
                    break;
                case BossBarkType.MerlinNarration:
                    fontSize = _merlinFontSize;
                    offsetY  = _merlinOffsetY;
                    break;
                case BossBarkType.BossIntro:
                    fontSize = _introFontSize;
                    offsetY  = _introOffsetY;
                    break;
                default:
                    fontSize = _barkFontSize;
                    offsetY  = _barkOffsetY;
                    break;
            }

            if (_label != null)
                _label.fontSize = fontSize;

            if (_container != null)
                _container.anchoredPosition = new Vector2(0f, offsetY);
        }

        /// <summary>화자줄 표시/숨김. None이면 라벨·구분선을 끄고 기존 레이아웃 그대로 둔다.</summary>
        private void ApplySpeaker(DialogueSpeaker speaker)
        {
            bool has = speaker != DialogueSpeaker.None;

            if (_speakerLabel != null)
            {
                _speakerLabel.gameObject.SetActive(has);
                if (has)
                {
                    _speakerLabel.text  = SpeakerName(speaker);
                    _speakerLabel.color = SpeakerColor(speaker);
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
            DialogueSpeaker.DeathKnight    => "죽음의 기사",
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
