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

        _bgmVolume    = PlayerPrefs.GetFloat(kBgmVolKey,    1f);
        _effectVolume = PlayerPrefs.GetFloat(kEffectVolKey, 1f);
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

        int requestVersion = type == Define.Sound.Bgm ? ++_bgmRequestVersion : 0;
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
        audioSource.volume = type == Define.Sound.Bgm ? volume * ChannelScale(Define.Sound.Bgm) : volume;

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

    public void PlayEffect(AudioClip audioClip, float volume = 1f, float pitch = 1f)
    {
        PlayPooledEffect(audioClip, null, volume, pitch, 0f, 1f, 500f, AudioRolloffMode.Logarithmic);
    }

    public void PlayEffectAt(
        AudioClip audioClip,
        Vector3 position,
        float volume = 1f,
        float pitch = 1f,
        float minDistance = 2f,
        float maxDistance = 25f,
        AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic)
    {
        PlayPooledEffect(audioClip, position, volume, pitch, 1f, minDistance, maxDistance, rolloffMode);
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

    private void PlayPooledEffect(
        AudioClip audioClip,
        Vector3? position,
        float volume,
        float pitch,
        float spatialBlend,
        float minDistance,
        float maxDistance,
        AudioRolloffMode rolloffMode)
    {
        if (audioClip == null)
            return;

        Init();

        var pooled = GetPooledAudioSource();
        var source = pooled.Source;
        if (source == null)
            return;

        source.gameObject.SetActive(true);
        source.transform.position = position ?? Vector3.zero;
        source.clip = audioClip;
        source.volume = volume * ChannelScale(Define.Sound.Effect);
        source.outputAudioMixerGroup = _sfxGroup; // 믹서 없으면 null = 기본 출력
        source.pitch = pitch;
        source.loop = false;
        source.playOnAwake = false;
        source.spatialBlend = Mathf.Clamp01(spatialBlend);
        source.rolloffMode = rolloffMode;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
        source.Play();

        float safePitch = Mathf.Max(0.01f, Mathf.Abs(pitch));
        float releaseDelay = Mathf.Max(MinReleaseDelay, audioClip.length / safePitch);
        ReleaseAfterAsync(pooled, pooled.Version, releaseDelay).Forget();
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
