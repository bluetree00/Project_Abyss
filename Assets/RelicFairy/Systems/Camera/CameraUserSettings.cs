using UnityEngine;

/// <summary>
/// 플레이어 시점 카메라 사용자 조정(10-24 사용자 「플레이어 시점 카메라를 조금씩 조절해서 세팅을 할 수 있는 기능」) — 거리 · 높이 배율.
/// 저장은 PlayerPrefs(볼륨 · 그래픽과 같은 저장소). 적용은 게임플레이 FreeLook에 붙는 <see cref="CameraUserFraming"/>가 매 프레임 읽는다.
/// 설정 창이 끄는 동안 미리보기로 Set을 부르고, 「완료」에서 <see cref="Save"/> · 「취소」에서 열 때 값으로 되돌린다.
/// </summary>
public static class CameraUserSettings
{
    // ── Constants ───────────────────────────────────────────
    private const string DistanceKey = "cam_user_distance";
    private const string HeightKey   = "cam_user_height";
    public const float MinDistance = 0.8f, MaxDistance = 1.3f;
    public const float MinHeight   = 0.7f, MaxHeight   = 1.4f;

    // ── Static ──────────────────────────────────────────────
    private static bool  s_loaded;
    private static float s_distance = 1f;
    private static float s_height   = 1f;

    // ── Properties ──────────────────────────────────────────
    /// <summary>카메라 ↔ 플레이어 거리 배율(1 = 기본).</summary>
    public static float Distance { get { EnsureLoaded(); return s_distance; } }
    /// <summary>카메라 높이(내려다보는 정도) 배율 — 플레이어 기준 위쪽 거리에만 곱한다(1 = 기본).</summary>
    public static float Height   { get { EnsureLoaded(); return s_height; } }

    // ── Public Methods ──────────────────────────────────────
    public static void SetDistance(float v) { EnsureLoaded(); s_distance = Mathf.Clamp(v, MinDistance, MaxDistance); }
    public static void SetHeight(float v)   { EnsureLoaded(); s_height   = Mathf.Clamp(v, MinHeight, MaxHeight); }

    public static void Save()
    {
        EnsureLoaded();
        PlayerPrefs.SetFloat(DistanceKey, s_distance);
        PlayerPrefs.SetFloat(HeightKey, s_height);
        PlayerPrefs.Save();
    }

    // ── Private Methods ─────────────────────────────────────
    private static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded   = true;
        s_distance = Mathf.Clamp(PlayerPrefs.GetFloat(DistanceKey, 1f), MinDistance, MaxDistance);
        s_height   = Mathf.Clamp(PlayerPrefs.GetFloat(HeightKey, 1f), MinHeight, MaxHeight);
    }

    // 도메인 리로드가 꺼져 있으면 정적 값이 이전 플레이에서 남는다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_loaded = false;
}
