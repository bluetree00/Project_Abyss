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
        public static UI_BossBark Instance { get; private set; }

        [Header("References")]
        [SerializeField] private CanvasGroup   _canvasGroup;
        [SerializeField] private Image         _background;
        [SerializeField] private TMP_Text      _label;
        [SerializeField] private RectTransform _container;

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

        private readonly Queue<(string text, BossBarkType type)> _queue = new();
        private CancellationTokenSource _cts;
        private bool _showing;

        private void Awake()
        {
            Instance = this;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            _cts?.Cancel();
            _cts?.Dispose();
        }

        // ─── Public API ───────────────────────────────────────────────

        public static void Show(string text, BossBarkType type = BossBarkType.Bark)
        {
            if (Instance == null) return;
            Instance.Enqueue(text, type);
        }

        // ─── Internal ─────────────────────────────────────────────────

        private void Enqueue(string text, BossBarkType type)
        {
            // PhaseAnnounce는 큐를 비우고 즉시 표시 (페이즈 전환은 최우선)
            // MerlinNarration은 큐 유지 — 서사 대사는 순서대로 출력
            if (type == BossBarkType.PhaseAnnounce)
            {
                _queue.Clear();
                InterruptCurrent();
            }

            _queue.Enqueue((text, type));

            if (!_showing)
                RunQueue(destroyCancellationToken).Forget();
        }

        private void InterruptCurrent()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _showing = false;
        }

        private async UniTaskVoid RunQueue(CancellationToken lifetimeToken)
        {
            while (_queue.Count > 0)
            {
                if (lifetimeToken.IsCancellationRequested) return;

                var (text, type) = _queue.Dequeue();
                _showing = true;

                _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

                try
                {
                    await DisplayOne(text, type, _cts.Token);
                }
                catch (System.OperationCanceledException) { }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                    _showing = false;
                }
            }
        }

        private async UniTask DisplayOne(string text, BossBarkType type, CancellationToken ct)
        {
            ApplyLayout(text, type);

            float holdDuration = type switch
            {
                BossBarkType.PatternAnnounce => _patternDuration,
                BossBarkType.PhaseAnnounce   => _phaseDuration,
                BossBarkType.MerlinNarration => _merlinDuration,
                BossBarkType.BossIntro       => _introDuration,
                _                            => _barkDuration,
            };

            await FadeTo(1f, _fadeInDuration, ct);
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(holdDuration),
                cancellationToken: ct);
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
