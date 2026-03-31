using UnityEngine;

public sealed class Managers : MonoBehaviour
{
    #region Singleton
    private static Managers s_instance;
    private static bool s_isQuitting;

    public static Managers Instance
    {
        get
        {
            if (s_isQuitting)
                return null;

            if (s_instance != null)
                return s_instance;

            GameObject go = GameObject.Find("@Managers");
            if (go == null)
                go = new GameObject("@Managers");

            s_instance = go.GetComponent<Managers>();
            if (s_instance == null)
                s_instance = go.AddComponent<Managers>();

            return s_instance;
        }
    }
    #endregion

    #region Core Managers
    private InputManager _input;
    private ResourceManager _resource;
    private ObjectPoolerManager _objectPoolerManager;
    private AddressableManager _addressableManager;
    private AnimationResourceManager _animationResources;

    private UIManager _ui;
    private CharacterDataManager _characterDataManager;

    private readonly PlayerManager _playerManager = new PlayerManager();
    private MonsterDataManager _monsterDataManager;
    private MonsterHPBarManager _monsterHPBar;

    // ---- Static Accessors (C# 9 Safe) ----
    public static InputManager Input
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._input == null)
                inst._input = new InputManager();

            return inst._input;
        }
    }

    public static ResourceManager Resource
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._resource == null)
                inst._resource = new ResourceManager();

            return inst._resource;
        }
    }

    public static ObjectPoolerManager ObjectPooler
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._objectPoolerManager == null)
                inst._objectPoolerManager = new ObjectPoolerManager();

            return inst._objectPoolerManager;
        }
    }

    public static AddressableManager AddressableManager
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._addressableManager == null)
                inst._addressableManager = new AddressableManager();

            return inst._addressableManager;
        }
    }

    public static AnimationResourceManager AnimationResources
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._animationResources == null)
                inst._animationResources = new AnimationResourceManager();

            return inst._animationResources;
        }
    }

    public static UIManager UI
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._ui == null)
                inst._ui = new UIManager();

            return inst._ui;
        }
    }

    public static CharacterDataManager CharacterData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._characterDataManager == null)
                inst._characterDataManager = new CharacterDataManager();

            return inst._characterDataManager;
        }
    }

    public static PlayerManager Player
    {
        get
        {
            var inst = Instance;
            return inst != null ? inst._playerManager : null;
        }
    }

    public static MonsterDataManager MonsterData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._monsterDataManager == null)
                inst._monsterDataManager = new MonsterDataManager();

            return inst._monsterDataManager;
        }
    }

    public static MonsterHPBarManager MonsterHPBar
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._monsterHPBar == null)
                inst._monsterHPBar = new MonsterHPBarManager();

            return inst._monsterHPBar;
        }
    }
    #endregion

    #region Unity
    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (_input != null)
            _input.OnUpdate();
    }

    private void OnApplicationQuit()
    {
        s_isQuitting = true;
    }

    private void OnDestroy()
    {
        s_isQuitting = true;

        if (s_instance == this)
            s_instance = null;
    }
    #endregion
}