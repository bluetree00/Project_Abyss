using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

public sealed class SoundManager
{
    private const string RootName = "@Sound";
    private const string EffectPoolRootName = "EffectPool";
    private const int InitialEffectPoolSize = 16;
    private const float MinReleaseDelay = 0.05f;
    private const string kBgmVolKey    = "sound_bgm_vol";
    private const string kEffectVolKey = "sound_effect_vol";
    private const string kMasterVolKey = "sound_master_vol";
    private const string kUiVolKey     = "sound_ui_vol";
    private const string kMixerParamMaster = "MasterVolume";
    private const string kMixerParamBgm    = "BgmVolume";
    private const string kMixerParamSfx    = "SfxVolume";
    private const string kMixerParamUi     = "UiVolume";
    private const string kMixerGroupBgm = "Master/BGM";
    private const string kMixerGroupSfx = "Master/SFX";
    private const float MinVolumeDb = -80f;

    private readonly AudioSource[] _audioSources = new AudioSource[(int)Define.Sound.MaxCount];
    private readonly Dictionary<string, AudioClip> _audioClips = new();
    private readonly Queue<PooledAudioSource> _availableEffects = new();
    private readonly List<PooledAudioSource> _effectPool = new();

    private Transform _effectPoolRoot;
    private bool _initialized;
    private int _bgmRequestVersion;
    private int _effectPoolVersion;
    private System.Threading.CancellationTokenSource _bgmFadeCts;
    private int _nextPoolId;
    private float _bgmVolume    = 1f;
    private float _effectVolume = 1f;
    private float _masterVolume = 1f;
    private float _uiVolume     = 1f;
    private SoundEventTableSO _eventTable;
    private AudioMixer _mixer;
    private AudioMixerGroup _bgmGroup;
    private AudioMixerGroup _sfxGroup;

    public float BgmVolume    => _bgmVolume;
    public float EffectVolume => _effectVolume;
    public float MasterVolume => _masterVolume;
    public float UiVolume     => _uiVolume;

    private sealed class PooledAudioSource
    {
        public AudioSource Source;
        public int Version;
    }

    public void SetMasterVolume(float volume)
    {
        _masterVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(kMasterVolKey, _masterVolume);
        PlayerPrefs.Save();
        if (_mixer != null)
            _mixer.SetFloat(kMixerParamMaster, LinearToDb(_masterVolume));
    }

    public void SetBgmVolume(float volume)
    {
        _bgmVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(kBgmVolKey, _bgmVolume);
        PlayerPrefs.Save();

        if (_mixer != null)
            _mixer.SetFloat(kMixerParamBgm, LinearToDb(_bgmVolume));
        else
        {
            var src = GetAudioSource(Define.Sound.Bgm);
            if (src != null) src.volume = _bgmVolume; // 폴백: 채널 곱셈
        }
    }

    public void SetEffectVolume(float volume)
    {
        _effectVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(kEffectVolKey, _effectVolume);
        PlayerPrefs.Save();
        if (_mixer != null)
            _mixer.SetFloat(kMixerParamSfx, LinearToDb(_effectVolume));
        // 폴백: 풀 이펙트는 재생 시점에 _effectVolume를 곱하므로 별도 처리 불필요
    }

    public void SetUiVolume(float volume)
    {
        _uiVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(kUiVolKey, _uiVolume);
        PlayerPrefs.Save();
        if (_mixer != null)
            _mixer.SetFloat(kMixerParamUi, LinearToDb(_uiVolume));
    }

    public void SetEventTable(SoundEventTableSO table) => _eventTable = table;

    public void SetMixer(AudioMixer mixer)
    {
        _mixer = mixer;
        if (_mixer == null)
            return;

        _bgmGroup = FindGroup(kMixerGroupBgm);
        _sfxGroup = FindGroup(kMixerGroupSfx);

        RouteExistingSources();
        ApplyAllVolumesToMixer();
    }

    public void PlayEvent(string eventId)
    {
        if (_eventTable == null || !_eventTable.TryGet(eventId, out var sfxKey, out var volume)) return;
        PlayEffectAsync(sfxKey, volume).Forget();
    }

    public void Init()
    {
        if (_initialized)
            return;

        _bgmVolume    = PlayerPrefs.GetFloat(kBgmVolKey,    0.25f);
        // 효과음 기본값 — 전투 타격음이 체감상 과했다. 믹서 에셋이 아직 없어 마스터 볼륨이
        // 폴백 경로에서 적용되지 않으므로, 실질적인 조절 레버는 이 채널 값이다.
        _effectVolume = PlayerPrefs.GetFloat(kEffectVolKey, 0.6f);
        _masterVolume = PlayerPrefs.GetFloat(kMasterVolKey, 1f);
        _uiVolume     = PlayerPrefs.GetFloat(kUiVolKey,     1f);

        var root = GameObject.Find(RootName);
        if (root == null)
            root = new GameObject(RootName);

        Object.DontDestroyOnLoad(root);
        EnsureAudioSource(root.transform, Define.Sound.Bgm, loop: true);
        EnsureAudioSource(root.transform, Define.Sound.Effect, loop: false);
        EnsureEffectPool(root.transform);

        var bgmSrc = GetAudioSource(Define.Sound.Bgm);
        if (bgmSrc != null) bgmSrc.volume = ChannelScale(Define.Sound.Bgm);

        if (_mixer != null) // 믹서가 Init보다 먼저 주입된 경우: 라우팅 + 프리팹 볼륨 재적용
        {
            RouteExistingSources();
            ApplyAllVolumesToMixer();
        }

        _initialized = true;
    }

    public void Clear()
    {
        _bgmFadeCts?.Cancel();
        _bgmFadeCts?.Dispose();
        _bgmFadeCts = null;
        _bgmRequestVersion++;
        _effectPoolVersion++;
        _availableEffects.Clear();

        for (int i = 0; i < _audioSources.Length; i++)
        {
            var audioSource = _audioSources[i];
            if (audioSource == null)
                continue;

            audioSource.Stop();
            audioSource.clip = null;
            audioSource.pitch = 1f;
            audioSource.volume = 1f;
        }

        foreach (var pooled in _effectPool)
        {
            ResetPooledSource(pooled);
            _availableEffects.Enqueue(pooled);
        }

        _audioClips.Clear();
    }

    public UniTask PlayBgmAsync(string key, float volume = 1f, float pitch = 1f)
        => PlayAsync(key, Define.Sound.Bgm, volume, pitch);

    public async UniTask FadeOutBgmAsync(float duration = 1.5f, System.Threading.CancellationToken ct = default)
    {
        _bgmRequestVersion++;
        _bgmFadeCts?.Cancel();
        _bgmFadeCts?.Dispose();
        _bgmFadeCts = new System.Threading.CancellationTokenSource();

        using var linkedCts = ct == default
            ? null
            : System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, _bgmFadeCts.Token);
        var linkedCt = linkedCts?.Token ?? _bgmFadeCts.Token;

        try
        {
            var src = GetAudioSource(Define.Sound.Bgm);
            if (src == null || !src.isPlaying) return;

            await FadeBgmSourceAsync(src, 0f, duration, linkedCt);
            src.Stop();
            src.clip = null;
            src.volume = 1f;
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>현재 BGM을 fadeOutDuration 초 동안 페이드아웃하면서 동시에 다음 클립을 로드한 뒤
    /// 갭 없이 재생을 이어간다. 중첩 호출 시 이전 페이드를 취소하고 새 전환을 시작한다.</summary>
    public async UniTask CrossfadeBgmAsync(string key, float fadeOutDuration = 1.5f, System.Threading.CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        _bgmRequestVersion++;
        _bgmFadeCts?.Cancel();
        _bgmFadeCts?.Dispose();
        _bgmFadeCts = new System.Threading.CancellationTokenSource();

        using var linkedCts = ct == default
            ? null
            : System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, _bgmFadeCts.Token);
        var linkedCt = linkedCts?.Token ?? _bgmFadeCts.Token;

        try
        {
            var loadTask = GetOrAddAudioClipAsync(key);

            var src = GetAudioSource(Define.Sound.Bgm);
            if (src != null && src.isPlaying)
            {
                await FadeBgmSourceAsync(src, 0f, fadeOutDuration, linkedCt);
                src.Stop();
                src.clip = null;
                src.volume = 1f;
            }

            var clip = await loadTask;
            if (clip == null) return;

            linkedCt.ThrowIfCancellationRequested();
            Play(clip, Define.Sound.Bgm);
        }
        catch (System.OperationCanceledException) { }
    }

    public UniTask PlayEffectAsync(string key, float volume = 1f, float pitch = 1f)
        => PlayAsync(key, Define.Sound.Effect, volume, pitch);

    public async UniTask PlayEffectAtAsync(
        string key,
        Vector3 position,
        float volume = 1f,
        float pitch = 1f,
        float minDistance = 2f,
        float maxDistance = 25f,
        AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var audioClip = await GetOrAddAudioClipAsync(key);
        PlayEffectAt(audioClip, position, volume, pitch, minDistance, maxDistance, rolloffMode);
    }

    public async UniTask PlayAsync(string key, Define.Sound type = Define.Sound.Effect, float volume = 1f, float pitch = 1f)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        int requestVersion;
        if (type == Define.Sound.Bgm)
        {
            _bgmFadeCts?.Cancel(); // 진행 중인 페이드아웃 중단
            requestVersion = ++_bgmRequestVersion;
        }
        else
        {
            requestVersion = 0;
        }
        var audioClip = await GetOrAddAudioClipAsync(key);

        if (type == Define.Sound.Bgm && requestVersion != _bgmRequestVersion)
            return;

        Play(audioClip, type, volume, pitch);
    }

    public void Play(AudioClip audioClip, Define.Sound type = Define.Sound.Effect, float volume = 1f, float pitch = 1f)
    {
        if (audioClip == null)
            return;

        Init();

        var audioSource = GetAudioSource(type);
        if (audioSource == null)
            return;

        audioSource.pitch = pitch;
        // 채널 음량은 BGM·효과음 모두에 적용한다. 예전엔 BGM만 곱해서, 공용 효과음 소스만
        // 효과음 볼륨 설정을 무시하고 원본 크기로 나갔다(풀 경로는 정상 적용 중).
        audioSource.volume = volume * ChannelScale(type);

        if (type == Define.Sound.Bgm)
        {
            if (audioSource.clip == audioClip && audioSource.isPlaying)
                return;

            audioSource.Stop();
            audioSource.clip = audioClip;
            audioSource.Play();
            return;
        }

        PlayEffect(audioClip, volume, pitch);
    }

    public AudioSource PlayEffect(AudioClip audioClip, float volume = 1f, float pitch = 1f)
    {
        return PlayPooledEffect(audioClip, null, volume, pitch, 0f, 1f, 500f, AudioRolloffMode.Logarithmic);
    }

    /// <summary>재생 중인 AudioSource를 반환한다 — 연결된 이펙트가 먼저 사라지면 호출 측에서 source.Stop()으로 함께 끊을 수 있다.</summary>
    /// <param name="startTime">클립 앞부분의 무음/예비음을 건너뛰고 싶을 때 재생 시작 지점(초)을 지정한다.</param>
    public AudioSource PlayEffectAt(
        AudioClip audioClip,
        Vector3 position,
        float volume = 1f,
        float pitch = 1f,
        float minDistance = 2f,
        float maxDistance = 25f,
        AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic,
        float startTime = 0f)
    {
        return PlayPooledEffect(audioClip, position, volume, pitch, 1f, minDistance, maxDistance, rolloffMode,
            startTime: startTime);
    }

    public void StopBgm()
    {
        _bgmRequestVersion++;

        var audioSource = GetAudioSource(Define.Sound.Bgm);
        if (audioSource == null)
            return;

        audioSource.Stop();
        audioSource.clip = null;
    }

    private async UniTask FadeBgmSourceAsync(AudioSource source, float targetVolume, float duration, System.Threading.CancellationToken ct)
    {
        float start = source.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(start, targetVolume, Mathf.Clamp01(elapsed / duration));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        source.volume = targetVolume;
    }

    private async UniTask<AudioClip> GetOrAddAudioClipAsync(string key)
    {
        if (_audioClips.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var addressables = Managers.AddressableManager;
        if (addressables == null)
            return null;

        var audioClip = await addressables.TryLoadAssetAsync<AudioClip>(key);
        if (audioClip == null)
        {
            Debug.LogWarning($"[SoundManager] AudioClip missing. key={key}");
            return null;
        }

        _audioClips[key] = audioClip;
        return audioClip;
    }

    private AudioSource GetAudioSource(Define.Sound type)
    {
        int index = (int)type;
        if (index < 0 || index >= (int)Define.Sound.MaxCount)
            return null;

        return _audioSources[index];
    }

    // 믹서가 있으면 채널 음량은 믹서가 처리 → 소스엔 per-clip 상대볼륨(1f)만.
    // 믹서가 없으면(폴백) 기존처럼 소스 볼륨에 채널 음량을 곱한다.
    private float ChannelScale(Define.Sound type)
    {
        if (_mixer != null)
            return 1f;
        return type == Define.Sound.Bgm ? _bgmVolume : _effectVolume;
    }

    private static float LinearToDb(float linear)
        => linear <= 0.0001f ? MinVolumeDb : Mathf.Log10(linear) * 20f;

    private AudioMixerGroup FindGroup(string path)
    {
        var groups = _mixer.FindMatchingGroups(path);
        return groups != null && groups.Length > 0 ? groups[0] : null;
    }

    private void RouteExistingSources()
    {
        var bgmSrc = GetAudioSource(Define.Sound.Bgm);
        if (bgmSrc != null) bgmSrc.outputAudioMixerGroup = _bgmGroup;

        var effectSrc = GetAudioSource(Define.Sound.Effect);
        if (effectSrc != null) effectSrc.outputAudioMixerGroup = _sfxGroup;

        foreach (var pooled in _effectPool)
        {
            if (pooled?.Source != null)
                pooled.Source.outputAudioMixerGroup = _sfxGroup;
        }
    }

    private void ApplyAllVolumesToMixer()
    {
        _mixer.SetFloat(kMixerParamMaster, LinearToDb(_masterVolume));
        _mixer.SetFloat(kMixerParamBgm,    LinearToDb(_bgmVolume));
        _mixer.SetFloat(kMixerParamSfx,    LinearToDb(_effectVolume));
        _mixer.SetFloat(kMixerParamUi,     LinearToDb(_uiVolume));
    }

    private void EnsureAudioSource(Transform root, Define.Sound type, bool loop)
    {
        int index = (int)type;
        if (_audioSources[index] != null)
            return;

        string sourceName = type.ToString();
        var child = root.Find(sourceName);
        GameObject go;

        if (child != null)
        {
            go = child.gameObject;
        }
        else
        {
            go = new GameObject(sourceName);
            go.transform.SetParent(root, false);
        }

        var audioSource = go.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = go.AddComponent<AudioSource>();

        audioSource.loop = loop;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        _audioSources[index] = audioSource;
    }

    private void EnsureEffectPool(Transform root)
    {
        if (_effectPoolRoot == null)
        {
            var child = root.Find(EffectPoolRootName);
            if (child == null)
            {
                var go = new GameObject(EffectPoolRootName);
                go.transform.SetParent(root, false);
                _effectPoolRoot = go.transform;
            }
            else
            {
                _effectPoolRoot = child;
            }
        }

        if (_effectPool.Count == 0)
        {
            var existingSources = _effectPoolRoot.GetComponentsInChildren<AudioSource>(true);
            foreach (var source in existingSources)
            {
                source.Stop();
                source.playOnAwake = false;
                source.loop = false;
                source.gameObject.SetActive(false);

                var pooled = new PooledAudioSource { Source = source };
                _effectPool.Add(pooled);
                _availableEffects.Enqueue(pooled);
            }

            _nextPoolId = Mathf.Max(_nextPoolId, _effectPool.Count);
        }

        while (_effectPool.Count < InitialEffectPoolSize)
            _availableEffects.Enqueue(CreatePooledAudioSource());
    }

    /// <summary>재생 중인 풀링 사운드를 정지한다. source.clip이 expectedClip과 다르면 이미 다른 소리로
    /// 재사용된 소스이므로 건드리지 않는다 — 이펙트 수명에 사운드를 묶을 때 사용.</summary>
    public void StopEffect(AudioSource source, AudioClip expectedClip)
    {
        if (source == null || expectedClip == null) return;
        if (source.clip == expectedClip && source.isPlaying)
            source.Stop();
    }

    /// <summary>여러 개의 짧은 이펙트가 동시에 같은 사운드를 트리거할 때(예: 한 구역에 깔리는 잔불 이펙트들)
    /// 개별 재생 대신 구역 전체를 대표하는 루프 사운드 1개로 묶기 위한 API.
    /// 자동 해제되지 않으므로 반드시 StopLoopingEffect로 직접 정지/반환해야 한다.</summary>
    public AudioSource PlayLoopingEffectAt(
        AudioClip audioClip,
        Vector3 position,
        float volume = 1f,
        float pitch = 1f,
        float minDistance = 2f,
        float maxDistance = 25f,
        AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic)
    {
        return PlayPooledEffect(audioClip, position, volume, pitch, 1f, minDistance, maxDistance, rolloffMode,
            loop: true, autoRelease: false);
    }

    /// <summary>PlayLoopingEffectAt으로 받은 소스를 정지하고 풀에 반환한다.</summary>
    public void StopLoopingEffect(AudioSource source)
    {
        if (source == null) return;

        var pooled = _effectPool.Find(p => p.Source == source);
        if (pooled == null)
        {
            source.Stop();
            return;
        }

        pooled.Version++;
        ResetPooledSource(pooled);
        _availableEffects.Enqueue(pooled);
    }

    private AudioSource PlayPooledEffect(
        AudioClip audioClip,
        Vector3? position,
        float volume,
        float pitch,
        float spatialBlend,
        float minDistance,
        float maxDistance,
        AudioRolloffMode rolloffMode,
        bool loop = false,
        bool autoRelease = true,
        float startTime = 0f)
    {
        if (audioClip == null)
            return null;

        Init();

        var pooled = GetPooledAudioSource();
        var source = pooled.Source;
        if (source == null)
            return null;

        source.gameObject.SetActive(true);
        source.transform.position = position ?? Vector3.zero;
        source.clip = audioClip;
        source.volume = volume * ChannelScale(Define.Sound.Effect);
        source.outputAudioMixerGroup = _sfxGroup; // 믹서 없으면 null = 기본 출력
        source.pitch = pitch;
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = Mathf.Clamp01(spatialBlend);
        source.rolloffMode = rolloffMode;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
        source.time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, audioClip.length - 0.01f));
        source.Play();

        if (autoRelease)
        {
            float safePitch = Mathf.Max(0.01f, Mathf.Abs(pitch));
            float remainingLength = Mathf.Max(0f, audioClip.length - source.time);
            float releaseDelay = Mathf.Max(MinReleaseDelay, remainingLength / safePitch);
            ReleaseAfterAsync(pooled, pooled.Version, releaseDelay).Forget();
        }

        return source;
    }

    private PooledAudioSource GetPooledAudioSource()
    {
        if (_availableEffects.Count > 0)
        {
            var pooled = _availableEffects.Dequeue();
            pooled.Version++;
            return pooled;
        }

        var created = CreatePooledAudioSource();
        created.Version++;
        return created;
    }

    private PooledAudioSource CreatePooledAudioSource()
    {
        var go = new GameObject($"EffectSource_{_nextPoolId++:00}");
        if (_effectPoolRoot != null)
            go.transform.SetParent(_effectPoolRoot, false);

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = _sfxGroup; // 믹서 없으면 null = 기본 출력
        go.SetActive(false);

        var pooled = new PooledAudioSource { Source = source };
        _effectPool.Add(pooled);
        return pooled;
    }

    private async UniTaskVoid ReleaseAfterAsync(PooledAudioSource pooled, int sourceVersion, float delay)
    {
        int poolVersion = _effectPoolVersion;

        await UniTask.Delay(System.TimeSpan.FromSeconds(delay), DelayType.UnscaledDeltaTime);

        if (poolVersion != _effectPoolVersion || pooled == null || pooled.Version != sourceVersion)
            return;

        ResetPooledSource(pooled);
        _availableEffects.Enqueue(pooled);
    }

    private static void ResetPooledSource(PooledAudioSource pooled)
    {
        if (pooled?.Source == null)
            return;

        var source = pooled.Source;
        source.Stop();
        source.clip = null;
        source.volume = 1f;
        source.pitch = 1f;
        source.loop = false;
        source.spatialBlend = 0f;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.transform.localPosition = Vector3.zero;
        source.gameObject.SetActive(false);
    }
}
