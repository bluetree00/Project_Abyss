using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 화면(해상도·창모드) 사용자 설정의 <b>단일 소유자</b>. 저장은 PlayerPrefs — 사운드 볼륨과 같은 저장소다.
///
/// <para>부팅 시 화면을 정하는 곳은 원래 <c>AppBootstrapper.SystemSetup()</c> 하나뿐이었고, 거기서
/// "데스크톱에 들어가는 최대 16:9 / 전체화면(FullScreenWindow)"를 <b>무조건</b> 강제한다.
/// 그건 사용자 설정이 없을 때의 기본값으로 그대로 두고, 저장된 선택이 있을 때만 이 클래스가 덮어쓴다.</para>
///
/// <para>그래서 복원 훅은 <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>다.
/// AppBootstrapper는 BeforeSceneLoad에서 자기 GameObject를 만들고 그 Awake에서 SystemSetup을 부르므로,
/// AfterSceneLoad가 확실히 <b>나중</b>이다. 반대로 걸면 부팅 기본값이 사용자 선택을 덮어써 설정이 증발한다.</para>
///
/// <para>에디터에서는 <c>Screen.SetResolution</c>이 무시된다(Game 뷰 크기가 우선). 저장·복원 값만 확인 가능.</para>
/// </summary>
public static class ScreenSettings
{
    // ── Constants ────────────────────────────────────────────
    private const string kWidthKey      = "screen_width";
    private const string kHeightKey     = "screen_height";
    private const string kFullscreenKey = "screen_fullscreen";

    /// <summary>UI 기준 해상도(1920×1080)의 절반 미만은 목록에서 뺀다 — 텍스트가 읽히지 않는다.</summary>
    private const int MinWidth  = 1024;
    private const int MinHeight = 576;

    // ── Properties ───────────────────────────────────────────
    /// <summary>사용자가 한 번이라도 화면 설정을 저장했는지. false면 부팅 기본값(SystemSetup)을 그대로 둔다.</summary>
    public static bool HasSaved => PlayerPrefs.HasKey(kWidthKey);

    public static int  SavedWidth      => PlayerPrefs.GetInt(kWidthKey,  Screen.width);
    public static int  SavedHeight     => PlayerPrefs.GetInt(kHeightKey, Screen.height);
    public static bool SavedFullscreen => PlayerPrefs.GetInt(kFullscreenKey, 1) != 0;

    /// <summary>현재 실제 창모드가 전체화면 계열인지.</summary>
    public static bool IsFullscreenNow => Screen.fullScreenMode != FullScreenMode.Windowed;

    // ── Public Methods ───────────────────────────────────────

    /// <summary>해상도·창모드를 즉시 적용하고 저장한다.</summary>
    public static void Apply(int width, int height, bool fullscreen)
    {
        if (width <= 0 || height <= 0)
            return;

        PlayerPrefs.SetInt(kWidthKey, width);
        PlayerPrefs.SetInt(kHeightKey, height);
        PlayerPrefs.SetInt(kFullscreenKey, fullscreen ? 1 : 0);
        PlayerPrefs.Save();

        ApplyInternal(width, height, fullscreen);
    }

    /// <summary>
    /// 선택 가능한 해상도 목록. 모니터가 보고하는 목록에서 주사율 중복을 제거하고 오름차순 정렬한다.
    /// 목록이 비거나(에디터 일부 환경) 현재 해상도가 빠져 있으면 현재 값을 채워 넣는다 —
    /// 드롭다운에서 "선택된 항목 없음"이 되는 것을 막는다.
    /// </summary>
    public static List<Vector2Int> GetAvailableResolutions()
    {
        var list = new List<Vector2Int>();
        var seen = new HashSet<long>();

        foreach (var res in Screen.resolutions)
        {
            if (res.width < MinWidth || res.height < MinHeight)
                continue;

            long key = ((long)res.width << 32) | (uint)res.height;
            if (seen.Add(key))
                list.Add(new Vector2Int(res.width, res.height));
        }

        var current = new Vector2Int(Screen.width, Screen.height);
        long currentKey = ((long)current.x << 32) | (uint)current.y;
        if (seen.Add(currentKey))
            list.Add(current);

        if (HasSaved)
        {
            var saved = new Vector2Int(SavedWidth, SavedHeight);
            long savedKey = ((long)saved.x << 32) | (uint)saved.y;
            if (seen.Add(savedKey))
                list.Add(saved);
        }

        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    // ── Private Methods ──────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreOnBoot()
    {
        if (!HasSaved)
            return; // 저장된 선택 없음 — AppBootstrapper.SystemSetup의 기본값을 존중한다.

        ApplyInternal(SavedWidth, SavedHeight, SavedFullscreen);
    }

    private static void ApplyInternal(int width, int height, bool fullscreen)
    {
        var mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(width, height, mode);
    }
}
