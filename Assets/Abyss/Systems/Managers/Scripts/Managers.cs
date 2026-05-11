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
    private ObjectPoolerManager _objectPoolerManager;
    private AddressableManager _addressableManager;
    private AnimationResourceManager _animationResources;
    private SoundManager _soundManager;

    private UIManager _ui;
    private CharacterDataManager _characterDataManager;

    private readonly PlayerManager _playerManager = new PlayerManager();
    private MonsterDataManager _monsterDataManager;
    private MapDataManager _mapDataManager;
    private PlayerDataManager _playerDataManager;
    private ServerEquipmentDataManager _serverEquipmentDataManager;
    private MonsterHPBarManager _monsterHPBar;
    private ItemDataManager _itemDataManager;
    private BlockDataManager _blockDataManager;
    private BuffDataManager _buffDataManager;
    private ChapterDataManager _chapterDataManager;
    private ElementEffectDataManager _elementEffectDataManager;
    private ServerMonsterStatDataManager _serverMonsterStatDataManager;
    private ShopDataManager _shopDataManager;
    private QuestManager _questManager;

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

    public static SoundManager Sound
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._soundManager == null)
                inst._soundManager = new SoundManager();

            return inst._soundManager;
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

    public static PlayerDataManager PlayerData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._playerDataManager == null)
                inst._playerDataManager = new PlayerDataManager();

            return inst._playerDataManager;
        }
    }

    public static ServerEquipmentDataManager ServerEquipment
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._serverEquipmentDataManager == null)
                inst._serverEquipmentDataManager = new ServerEquipmentDataManager();

            return inst._serverEquipmentDataManager;
        }
    }

    public static MapDataManager MapData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._mapDataManager == null)
                inst._mapDataManager = new MapDataManager();

            return inst._mapDataManager;
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

    public static ItemDataManager ItemData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._itemDataManager == null)
                inst._itemDataManager = new ItemDataManager();

            return inst._itemDataManager;
        }
    }

    public static BlockDataManager BlockData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._blockDataManager == null)
                inst._blockDataManager = new BlockDataManager();

            return inst._blockDataManager;
        }
    }

    public static BuffDataManager BuffData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._buffDataManager == null)
                inst._buffDataManager = new BuffDataManager();

            return inst._buffDataManager;
        }
    }

    public static ChapterDataManager ChapterData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._chapterDataManager == null)
                inst._chapterDataManager = new ChapterDataManager();

            return inst._chapterDataManager;
        }
    }

    public static ElementEffectDataManager ElementEffectData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._elementEffectDataManager == null)
                inst._elementEffectDataManager = new ElementEffectDataManager();

            return inst._elementEffectDataManager;
        }
    }

    public static ServerMonsterStatDataManager ServerMonsterStat
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._serverMonsterStatDataManager == null)
                inst._serverMonsterStatDataManager = new ServerMonsterStatDataManager();

            return inst._serverMonsterStatDataManager;
        }
    }

    public static ShopDataManager ShopData
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._shopDataManager == null)
                inst._shopDataManager = new ShopDataManager();

            return inst._shopDataManager;
        }
    }

    public static QuestManager Quest
    {
        get
        {
            var inst = Instance;
            if (inst == null) return null;

            if (inst._questManager == null)
                inst._questManager = new QuestManager();

            return inst._questManager;
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
