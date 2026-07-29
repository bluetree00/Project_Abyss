using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 가웨인 정오 연출 — <b>머리 위 하늘에 떠 있는 해</b>.
///
/// 정오 진입 시 캐릭터 머리 위로 태양을 띄우고, 종료 시 서서히 지운다.
/// 플레이어를 따라다니되(SetParent) 발밑 오라(Rage/Cursed)와 겹치지 않게 위쪽에 둔다.
///
/// 태양 프리팹(Magic circle suns loop)은 -90° 눕힌 <b>바닥 마법진</b>이라, 그대로 띄우면 해가
/// 옆으로 누워 보인다 → 다시 세워서 화면을 정면으로 보게 한다. 루프 파티클이라 정오 내내 유지된다.
///
/// SolarZone/SolarMeteor와 같은 경량 자체 구현. 상태 토글은 GawainZenithRelic이 호출한다.
/// </summary>
public sealed class SolarNoonSun : MonoBehaviour
{
    private const string SunVfxKey = "vfx_gawain_noon_sun";   // 태양(루프)
    // 캐릭터 머리 위 상공에 <b>수직으로 세워</b> 띄운다(빌보드 — 카메라를 정면으로 마주 봄).
    // 마법진을 눕히거나 기울이면 옆으로 퍼져 화면을 덮는다 → 항상 카메라를 마주 보게 세운다.
    // PlanetCrash 아트는 원본 자체가 거대해서(반경 수십 m) 낮게·크게 두면 화면을 통째로 덮는다.
    // '하늘에 떠 있는 먼 태양'으로 읽히도록 <b>높이 올리고 크기는 줄인다</b>.
    private const float  Height    = 14f;    // 머리 위 상공 높이
    private const float  Forward   = 1.6f;   // 캐릭터 정면(로컬 +Z)으로 살짝 앞 — 카메라 가림 완화(높이는 유지)
    private const float  Scale     = 2.5f;   // 태양 크기(머리 위 정오 태양)
    private const float  FadeTime  = 0.4f;   // 등장/퇴장 페이드

    private Transform _host;
    private GameObject _sun;
    private CancellationTokenSource _cts;

    /// <summary>플레이어에 부착. GawainZenithRelic.OnAttach에서 1회 호출.</summary>
    public void Bind(Transform host) => _host = host;

    /// <summary>정오 진입/종료에 맞춰 켜고 끈다.</summary>
    public void SetActive(bool on)
    {
        if (on) ShowAsync().Forget();
        else    HideAsync().Forget();
    }

    private async UniTaskVoid ShowAsync()
    {
        if (_host == null || _sun != null) return;

        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        var ct = _cts.Token;

        GameObject sun = null;
        try { sun = await Managers.AddressableManager.InstantiateAsync(SunVfxKey, _host, false); }
        catch (Exception) { return; }   // 키 부재 — 무해

        if (sun == null || ct.IsCancellationRequested) { if (sun != null) Destroy(sun); return; }

        _sun = sun;
        var t = sun.transform;

        // 머리 위(+Y) 상공 + 캐릭터 정면으로 살짝 앞. 회전은 프리팹 원본 그대로 둔다(빌보드/틸트 안 함).
        t.localPosition = Vector3.up * Height + Vector3.forward * Forward;
        t.localScale    = Vector3.one * Scale;

        await FadeAsync(sun, 0f, 1f, ct);
    }

    private async UniTaskVoid HideAsync()
    {
        var sun = _sun;
        _sun = null;
        if (sun == null) return;

        _cts?.Cancel();   // 진행 중이던 등장 페이드 취소
        try
        {
            await FadeAsync(sun, GetAlpha(sun), 0f, this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (sun != null) Destroy(sun);
        }
    }

    // ── 파티클 스타트 컬러 알파를 일괄 조절해 페이드(머티리얼 인스턴스화 없이). ──
    private async UniTask FadeAsync(GameObject sun, float from, float to, CancellationToken ct)
    {
        var systems = sun.GetComponentsInChildren<ParticleSystem>(true);
        float e = 0f;
        while (e < 1f)
        {
            if (sun == null || ct.IsCancellationRequested) return;
            e += Time.deltaTime / Mathf.Max(0.01f, FadeTime);
            SetAlpha(systems, Mathf.Lerp(from, to, e));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (sun != null) SetAlpha(systems, to);
    }

    private static void SetAlpha(ParticleSystem[] systems, float a)
    {
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            var c = main.startColor.color;
            c.a = a;
            main.startColor = c;
        }
    }

    private static float GetAlpha(GameObject sun)
    {
        var ps = sun.GetComponentInChildren<ParticleSystem>();
        return ps != null ? ps.main.startColor.color.a : 1f;
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        if (_sun != null) Destroy(_sun);
    }
}
