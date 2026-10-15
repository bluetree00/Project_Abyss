using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대장장이 말(10-15 · 기획 「재련소·강화·돌발이벤트 개선안」 §5 · 사용자 결정 3 「패널 안 초상 + 말풍선」).
/// 예전엔 아래 띠 한 줄(18px · 모루 아이콘)이라 읽히지 않았다 — 「대사가 팝업에 들어가니 유저는 신경 쓰지 않는다」.
///  · 초상 68 — 방의 NPC 모델을 에디터에서 찍은 그림(스킨 <see cref="CrucibleSkinSO.BlacksmithPortrait"/> · 없으면 모루 아이콘).
///  · 말풍선 — 20px 두 줄까지 · 타자기(초당 40자) + 짧은 말소리(ui_hover 블립 · 낮게) · 새 말에 초상이 살짝 끄덕 · 다 말하고 2.5초 뒤 옅어진다.
///  · 시스템 안내(「파츠를 먼저 고른다」)는 말풍선이 아니라 오른쪽 판 안내 줄(_v3Fail) — 대장장이 말과 섞지 않는다.
/// 모든 대사는 <see cref="Say"/>로 — _dialogText.text에 직접 넣지 않는다(타자기 · 옅어짐이 어긋난다).
/// </summary>
public sealed partial class UI_CruciblePanel
{
    private const float SayPortraitPx  = 68f;
    private const float SayBubbleX     = 84f;      // 초상 68 + 틈 16 — 꼬리 마름모가 그 사이에 걸친다
    private const float SayBubbleW     = V3LeftW - SayBubbleX;   // 왼쪽 판 오른쪽 끝(760)까지 — 단축키 안내(780~)와 겹치지 않는다
    private const float SayCharsPerSec = 40f;
    private const int   SayBlipEvery   = 3;        // 글자 3개마다 말소리 한 번(공백 · 문장부호는 세지 않는다)
    private const float SayBlipPitch   = 0.8f;     // 낮은 목소리 — 표의 피치 흔들림 위에 곱한다
    private const float SayBlipVolume  = 0.3f;
    private const float SayFadeAfter   = 2.5f;
    private const float SayFadeDur     = 0.5f;
    private const float SayFadedAlpha  = 0.5f;     // 옅어질 뿐 사라지지 않는다 — 마지막 말을 다시 읽을 수 있게
    private const float SayNodPx       = 3f;
    private const float SayNodDur      = 0.22f;

    private CanvasGroup   _sayGroup;
    private RectTransform _sayPortrait;
    private Vector2       _sayPortraitHome;
    private int           _sayGen;

    /// <summary>대장장이가 말한다 — 타자기 · 말소리 · 끄덕 · 2.5초 뒤 옅어짐. 새 말이 오면 앞 말은 바로 끊긴다.</summary>
    private void Say(string line)
    {
        if (_dialogText == null || string.IsNullOrEmpty(line)) return;
        _dialogText.text = line;
        _dialogText.maxVisibleCharacters = int.MaxValue;
        int gen = ++_sayGen;
        if (_sayGroup != null) _sayGroup.alpha = 1f;
        if (!_v3Ready || !isActiveAndEnabled) return;   // v3가 아니면 한 번에(옛 띠)
        SayTypeAsync(gen).Forget();
        SayNodAsync(gen).Forget();
    }

    /// <summary>초상 68 — 띠 왼쪽(세로 가운데) · 그림이 있으면 테 안을 꽉 채운다. 끄덕 기준 위치도 여기서 잡는다.</summary>
    private void LayoutV3Portrait(RectTransform por)
    {
        BP(por, 0f, (V3BarH - SayPortraitPx) * 0.5f, SayPortraitPx, SayPortraitPx);
        _sayPortrait     = por;
        _sayPortraitHome = por.anchoredPosition;
        var face = _skin != null ? _skin.BlacksmithPortrait : null;
        if (face == null) return;   // 그림이 없으면 모루 아이콘 그대로
        if (por.Find("Fill/Art") is RectTransform art && art.TryGetComponent<Image>(out var img))
        {
            ShopUIStyle.Stretch(art);
            img.sprite         = face;
            img.color          = Color.white;
            img.preserveAspect = true;
        }
    }

    private async UniTaskVoid SayTypeAsync(int gen)
    {
        try
        {
            _dialogText.ForceMeshUpdate();
            var info  = _dialogText.textInfo;
            int total = info.characterCount;
            _dialogText.maxVisibleCharacters = 0;
            float t = 0f;
            int shown = 0, sinceBlip = SayBlipEvery - 1;   // 첫 글자에 바로 소리
            while (shown < total)
            {
                if (gen != _sayGen) return;
                t += Time.unscaledDeltaTime;
                int want = Mathf.Min(total, Mathf.FloorToInt(t * SayCharsPerSec) + 1);
                bool blip = false;
                for (; shown < want; shown++)
                {
                    char c = info.characterInfo[shown].character;
                    if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) continue;
                    if (++sinceBlip >= SayBlipEvery) { sinceBlip = 0; blip = true; }
                }
                if (blip) SayBlip();   // 한 프레임에 여러 글자가 나와도 소리는 한 번
                _dialogText.maxVisibleCharacters = shown;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
            _dialogText.maxVisibleCharacters = int.MaxValue;

            float h = 0f;
            while (h < SayFadeAfter)
            {
                if (gen != _sayGen) return;
                h += Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
            float f = 0f;
            while (f < SayFadeDur && _sayGroup != null)
            {
                if (gen != _sayGen) return;
                f += Time.unscaledDeltaTime;
                _sayGroup.alpha = Mathf.Lerp(1f, SayFadedAlpha, f / SayFadeDur);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>새 말에 초상이 3px 올라갔다 내려온다(0.22초) — 「살짝」이 상한.</summary>
    private async UniTaskVoid SayNodAsync(int gen)
    {
        if (_sayPortrait == null) return;
        try
        {
            float t = 0f;
            while (t < SayNodDur && gen == _sayGen)
            {
                t += Time.unscaledDeltaTime;
                _sayPortrait.anchoredPosition = _sayPortraitHome + new Vector2(0f, SayNodPx * Mathf.Sin(Mathf.Clamp01(t / SayNodDur) * Mathf.PI));
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }
        catch (OperationCanceledException) { return; }
        if (gen == _sayGen) _sayPortrait.anchoredPosition = _sayPortraitHome;
    }

    private static void SayBlip()
    {
        var sound = Managers.Sound;
        if (sound == null) return;
        sound.PlayUiEvent(SoundEvent.UiHover, SayBlipPitch * UnityEngine.Random.Range(0.94f, 1.06f), SayBlipVolume);
    }
}
