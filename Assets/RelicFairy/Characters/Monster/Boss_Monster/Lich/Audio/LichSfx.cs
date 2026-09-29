using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 소리 재생기 — 칸(<see cref="LichSfxSlot"/>)으로 부르고 <see cref="SoundManager"/>의 풀 소스로 튼다.
/// 목록이 없거나 칸이 비면 조용히 건너뛴다(패턴은 소리와 무관하게 돈다).
/// </summary>
public static class LichSfx
{
    public const string SetAddress = "Lich/SFX/LichSfxSet";

    private const float DefaultMinDistance = 14f;   // 제단 60 m · 카메라가 멀어 기본 2 m면 대부분 작게 들린다
    private const float MaxDistance        = 70f;

    private static LichSfxSetSO s_set;
    private static bool         s_loading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_set     = null;
        s_loading = false;
    }

    public static async UniTask LoadAsync()
    {
        if (s_set != null || s_loading) return;
        s_loading = true;
        try
        {
            var am = Managers.AddressableManager;
            if (am == null) return;
            s_set = await am.TryLoadAssetAsync<LichSfxSetSO>(SetAddress);
            if (s_set == null)
                Debug.LogWarning($"[LichSfx] 소리 목록을 찾지 못했다 — {SetAddress} (소리 없이 진행)");
            else
                Debug.Log($"[LichSfx] 소리 목록 로드 — {s_set.Entries.Count}칸");
        }
        finally
        {
            s_loading = false;
        }
    }

    /// <summary>한 번 재생. 재생 중인 소스를 돌려준다(먼저 끊을 때 <see cref="Stop"/>).</summary>
    public static AudioSource Play(LichSfxSlot slot, Vector3 position, float volumeScale = 1f)
    {
        var sound = Managers.Sound;
        if (sound == null || s_set == null || !s_set.TryGet(slot, out var e)) return null;
        return sound.PlayEffectAt(e.clip, position, Volume(e) * volumeScale, Pitch(e),
                                  MinDistance(e), MaxDistance, AudioRolloffMode.Logarithmic, e.startTime);
    }

    /// <summary>반복 재생 — 자동으로 끝나지 않으니 <see cref="StopLoop"/>로 끈다.</summary>
    public static AudioSource PlayLoop(LichSfxSlot slot, Vector3 position, float volumeScale = 1f)
    {
        var sound = Managers.Sound;
        if (sound == null || s_set == null || !s_set.TryGet(slot, out var e)) return null;
        return sound.PlayLoopingEffectAt(e.clip, position, Volume(e) * volumeScale, Pitch(e),
                                         MinDistance(e), MaxDistance);
    }

    /// <summary>한 번 재생한 소리를 먼저 끊는다(같은 소스가 이미 다른 소리로 재사용됐으면 건드리지 않는다).</summary>
    public static void Stop(ref AudioSource source, LichSfxSlot slot)
    {
        if (source == null) return;
        if (s_set != null && s_set.TryGet(slot, out var e))
            Managers.Sound?.StopEffect(source, e.clip);
        source = null;
    }

    public static void StopLoop(ref AudioSource source)
    {
        if (source == null) return;
        Managers.Sound?.StopLoopingEffect(source);
        source = null;
    }

    private static float Volume(in LichSfxEntry e)      => e.volume > 0f ? e.volume : 1f;
    private static float Pitch(in LichSfxEntry e)       => e.pitch > 0f ? e.pitch : 1f;
    private static float MinDistance(in LichSfxEntry e) => e.minDistance > 0f ? e.minDistance : DefaultMinDistance;
}
}
