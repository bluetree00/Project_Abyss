using System;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace RelicFairy.Monster
{
    public enum LichForm
    {
        Phase1        = 0,  // 봉인 — HoodUp + Book + Clothing          (Style 1)
        Phase1_Open   = 1,  // 봉인 변형 — HoodDn + Book + Clothing     (Style 2)
        Phase1_Skirt  = 2,  // 봉인 변형 — HoodDn + Book + Clothing + Skirt (Style 3)
        Phase2        = 3,  // 해방 — 로브·책 전체 OFF, 뼈 노출          (Style 4/5)
    }

    /// <summary>
    /// TheReaper SourceMesh의 장비 GO를 토글해 리치의 외형 폼을 전환한다.
    /// LichMonster에서 ApplyPhase2Buffs 호출 시 자동 연동됨.
    ///
    /// Inspector 설정:
    ///   1. SourceMesh 하위 GO를 각 슬롯에 드래그앤드롭
    ///   2. Presets 배열 index = LichForm enum value 순서와 반드시 일치
    ///
    /// 노드 경로 (SourceMesh 하위 기준):
    ///   BookEquip     → Acessories/BookEquip
    ///   Bookss        → Bookss
    ///   Clothing      → Clothing
    ///   SkirtSeparate → SkirtSeparate
    ///   HoodDown      → HoodDn
    ///   HoodUp        → HoodUp
    ///   ScytheEquipRoot → Scythe_Equip  (독립 소품, 보통 false)
    /// </summary>
    public class LichFormController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────
        // Nested Types
        // ─────────────────────────────────────────────────────────

        [Serializable]
        public class FormPreset
        {
            public string formName;

            [Header("Equipment")]
            public bool bookActive;
            public bool bookssActive;
            // scytheActive: 루트 Scythe_Equip (독립 소품). 손뼈 낫은 항상 ON이므로 보통 false 유지.
            public bool scytheEquipRootActive;

            [Header("Body")]
            public bool clothingActive;
            public bool skirtSeparateActive;

            [Header("Hood")]
            public bool hoodDownActive;
            public bool hoodUpActive;
        }

        // ─────────────────────────────────────────────────────────
        // SerializeField
        // ─────────────────────────────────────────────────────────

        [Header("SourceMesh GO References")]
        [SerializeField] private GameObject _bookEquip;
        [SerializeField] private GameObject _bookss;
        [SerializeField] private GameObject _scytheEquipRoot;
        [SerializeField] private GameObject _clothing;
        [SerializeField] private GameObject _skirtSeparate;
        [SerializeField] private GameObject _hoodDown;
        [SerializeField] private GameObject _hoodUp;

        [Header("Form Presets (index = LichForm enum 순서)")]
        [SerializeField] private FormPreset[] _presets;

        // ─────────────────────────────────────────────────────────
        // Properties
        // ─────────────────────────────────────────────────────────

        public LichForm CurrentForm { get; private set; } = LichForm.Phase1;

        // ─────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────

        private void Awake()
        {
            ApplyForm(LichForm.Phase1);
        }

        // ─────────────────────────────────────────────────────────
        // Public Methods
        // ─────────────────────────────────────────────────────────

        /// <summary>즉시 폼 전환 (컷씬·VFX 없음).</summary>
        public void ApplyForm(LichForm form)
        {
            int idx = (int)form;
            if (_presets == null || idx < 0 || idx >= _presets.Length) return;

            ApplyPreset(_presets[idx]);
            CurrentForm = form;
        }

        /// <summary>
        /// 폼 전환 — SoundManager·VFX 훅 포인트 포함.
        /// Phase2Entry 패턴에서 await로 호출한다.
        /// </summary>
        public async UniTask ApplyFormWithTransitionAsync(LichForm form, CancellationToken ct)
        {
            int idx = (int)form;
            if (_presets == null || idx < 0 || idx >= _presets.Length) return;

            // TODO: SoundManager.Instance.Play("Lich_FormChange") 연동
            // TODO: VFX 파티클 재생 훅

            ApplyPreset(_presets[idx]);
            CurrentForm = form;

            await UniTask.Yield(cancellationToken: ct);
        }

        // ─────────────────────────────────────────────────────────
        // Private Methods
        // ─────────────────────────────────────────────────────────

        private void ApplyPreset(FormPreset preset)
        {
            Toggle(_bookEquip,       preset.bookActive);
            Toggle(_bookss,          preset.bookssActive);
            Toggle(_scytheEquipRoot, preset.scytheEquipRootActive);
            Toggle(_clothing,        preset.clothingActive);
            Toggle(_skirtSeparate,   preset.skirtSeparateActive);
            Toggle(_hoodDown,        preset.hoodDownActive);
            Toggle(_hoodUp,          preset.hoodUpActive);
        }

        private static void Toggle(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }

        // ─────────────────────────────────────────────────────────
        // Editor Helper
        // ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void Reset()
        {
            _presets = new FormPreset[]
            {
                new FormPreset   // [0] Phase1 — Style 1: HoodUp + Book + Clothing
                {
                    formName              = "Phase1_Sealed",
                    bookActive            = true,
                    bookssActive          = false,
                    scytheEquipRootActive = false,
                    clothingActive        = true,
                    skirtSeparateActive   = false,
                    hoodDownActive        = false,
                    hoodUpActive          = true,
                },
                new FormPreset   // [1] Phase1_Open — Style 2: HoodDn + Book + Clothing
                {
                    formName              = "Phase1_Open",
                    bookActive            = true,
                    bookssActive          = false,
                    scytheEquipRootActive = false,
                    clothingActive        = true,
                    skirtSeparateActive   = false,
                    hoodDownActive        = true,
                    hoodUpActive          = false,
                },
                new FormPreset   // [2] Phase1_Skirt — Style 3: HoodDn + Book + Clothing + Skirt
                {
                    formName              = "Phase1_Skirt",
                    bookActive            = true,
                    bookssActive          = false,
                    scytheEquipRootActive = false,
                    clothingActive        = true,
                    skirtSeparateActive   = true,
                    hoodDownActive        = true,
                    hoodUpActive          = false,
                },
                new FormPreset   // [3] Phase2 — Style 4/5: 로브·책 전체 OFF, 뼈 노출
                {
                    formName              = "Phase2_Liberation",
                    bookActive            = false,
                    bookssActive          = false,
                    scytheEquipRootActive = false,
                    clothingActive        = false,
                    skirtSeparateActive   = false,
                    hoodDownActive        = false,
                    hoodUpActive          = false,
                },
            };
        }
#endif
    }
}
