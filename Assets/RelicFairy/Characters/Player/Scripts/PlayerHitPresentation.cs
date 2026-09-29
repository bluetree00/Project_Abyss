using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI.Overlay;
using UnityEngine;

/// <summary>
/// 플레이어 피격 연출 — 등급(약·중·강)마다 세기를 나눈다. 모든 공격이 같은 세기면 스트레스이고,
/// 강한 공격은 맞은 순간이 읽혀야 한다(설계: 기획/RelicFairy_플레이어피격연출_등급_설계_20260920).
/// <see cref="PlayerController.OnHitTaken"/>을 받아 카메라 흔들림 · (강) 줌·히트스톱 · 몸 번쩍임 · 피격음을 낸다.
/// 비네트는 HUD(HitFxPresenter)가 같은 신호로 맡는다. PlayerController가 런타임에 자동 부착한다.
/// </summary>
public sealed class PlayerHitPresentation : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private static readonly int   BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int   EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color FlashTint       = new(1f, 0.35f, 0.32f, 1f);   // 붉은 번쩍임

    private const float HeavyHitStopScale    = 0.05f;
    private const float HeavyHitStopDuration = 0.05f;   // 실시간 — 슬로모·일시정지 중엔 HitFeelService가 생략
    private const float HeavyZoomIntensity   = 0.12f;   // 09-21 두 번 낮춤(0.35 → 0.18 → 0.12) — 빛살이 세면 맞은 순간이 오히려 안 읽힌다
    private const float HeavyZoomDuration    = 0.16f;

    private readonly struct Profile
    {
        public readonly float ShakeAmp, ShakeDur, FlashDur, FlashStrength, FlashEmission, SfxVolume;
        public Profile(float shakeAmp, float shakeDur, float flashDur, float flashStrength, float flashEmission, float sfxVolume)
        {
            ShakeAmp = shakeAmp; ShakeDur = shakeDur; FlashDur = flashDur;
            FlashStrength = flashStrength; FlashEmission = flashEmission; SfxVolume = sfxVolume;
        }
    }

    // 09-21 전체적으로 낮춤 — 세게 주면 교란이 된다. 등급 차이는 유지하되 은은하게.
    private static readonly Profile LightHit  = new(0.022f, 0.10f, 0.06f, 0.22f, 0.35f, 0.55f);
    private static readonly Profile MediumHit = new(0.055f, 0.16f, 0.10f, 0.38f, 0.75f, 0.8f);
    private static readonly Profile HeavyHit  = new(0.105f, 0.24f, 0.14f, 0.58f, 1.4f,  1f);

    // ── Private ───────────────────────────────────────────────────
    private PlayerController        _controller;
    private DodgePresentation       _dodge;
    private SkinnedMeshRenderer[]   _renderers;
    private MaterialPropertyBlock   _mpb;
    private CancellationTokenSource _cts;
    private int  _flashGen;
    private bool _flashActive;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        TryGetComponent(out _dodge);
        _mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();
        if (_controller != null) _controller.OnHitTaken += HandleHitTaken;
    }

    private void Update()
    {
        // 빈사 경고(붉은 후처리)는 피격 순간이 아니라 '지금 위험하다'를 계속 알린다 → 매 프레임 남은 체력만 넘긴다.
        var stats = _controller != null ? _controller.RuntimeStats : null;
        if (stats == null || stats.MaxHp <= 0) return;

        // 체력 0이면 경고를 접는다. 사망 연출(붉은 비네트 → 암전 → 멀린 부활)이 화면을 맡는 구간이라,
        // 경고까지 최대로 깔려 있으면 겹쳐서 과하고 부활 장면까지 붉게 남는다. 살아나면 다시 올라온다.
        LowHpVolumeService.SetHpRatio(stats.Hp <= 0 ? 1f : (float)stats.Hp / stats.MaxHp);
    }

    private void OnDisable()
    {
        if (_controller != null) _controller.OnHitTaken -= HandleHitTaken;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        ClearFlash();
        LowHpVolumeService.Clear();   // 사망·런 종료로 꺼질 때 붉은 기운이 남지 않게
    }

    // ── Private Methods ───────────────────────────────────────────
    private static Profile ProfileOf(HitWeight weight) => weight switch
    {
        HitWeight.Heavy  => HeavyHit,
        HitWeight.Medium => MediumHit,
        _                => LightHit,
    };

    private string SfxKeyOf(HitWeight weight)
    {
        var data = _controller != null ? _controller.CharacterData : null;
        if (data == null) return null;
        return weight switch
        {
            HitWeight.Heavy  => data.hitSfxHeavy,
            HitWeight.Medium => data.hitSfxMedium,
            _                => data.hitSfxLight,
        };
    }

    private async UniTaskVoid FlashAsync(int gen, Profile p, CancellationToken token)
    {
        // 저스트 회피·회피 무적 틴트가 몸을 쓰는 중이면 번쩍임을 건너뛴다(같은 머티리얼 블록 — 덮어쓰면 틴트가 사라진다).
        if (_dodge != null && _dodge.BodyTintActive) return;
        if (_renderers == null || _renderers.Length == 0 || _renderers[0] == null)
            _renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (_renderers.Length == 0) return;

        Color baseC = Color.Lerp(Color.white, FlashTint, p.FlashStrength);
        baseC.a = 1f;
        _mpb.Clear();
        _mpb.SetColor(BaseColorId, baseC);
        _mpb.SetColor(EmissionColorId, FlashTint * p.FlashEmission);
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].SetPropertyBlock(_mpb);
        _flashActive = true;

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(p.FlashDur), ignoreTimeScale: true, cancellationToken: token);
        }
        catch (OperationCanceledException) { return; }

        if (gen == _flashGen) ClearFlash();
    }

    private void ClearFlash()
    {
        if (!_flashActive) return;
        _flashActive = false;
        // 그 사이 회피 틴트가 몸을 잡았으면 그쪽이 주인이다 — 지우지 않는다.
        if (_dodge != null && _dodge.BodyTintActive) return;
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void HandleHitTaken(HitWeight weight, Vector3 hitDir, int damage)
    {
        var p = ProfileOf(weight);

        // 가해자→피해자 방향으로 화면을 밀어 "어디서 맞았는지"가 읽히게 한다(가해자 미상이면 무방향).
        HitFeelService.CameraShakeDirectional(hitDir, p.ShakeAmp, p.ShakeDur);

        if (weight == HitWeight.Heavy)
        {
            HitFeelService.HitStop(HeavyHitStopScale, HeavyHitStopDuration);
            Managers.UI?.GetOverlayUI<FXLayer>()?.ZoomIn(HeavyZoomIntensity, HeavyZoomDuration);
        }

        string key = SfxKeyOf(weight);
        if (!string.IsNullOrEmpty(key)) Managers.Sound?.PlayEffectAsync(key, p.SfxVolume).Forget();

        if (_cts != null) FlashAsync(++_flashGen, p, _cts.Token).Forget();
    }
}
