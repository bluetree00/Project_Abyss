using System;
using System.Collections.Generic;
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

        [Header("Dissolve")]
        [Tooltip("디졸브 쉐이더 프로퍼티 이름. 1=완전 디졸브, 0=완전 표시.")]
        [SerializeField] private string _dissolvePropertyName = "_DissolveAmount";
        [Tooltip("디졸브 인 연출 시간 (초)")]
        [SerializeField] private float  _dissolveInDuration   = 1.0f;

        // ─────────────────────────────────────────────────────────
        // Properties
        // ─────────────────────────────────────────────────────────

        public LichForm CurrentForm { get; private set; } = LichForm.Phase1;

        // ─────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────

        private void Awake()
        {
            ApplyForm(LichForm.Phase1); // 의상·후드 기본 표시
            HideWeapons();              // 무기는 등장 연출 전까지 숨김
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

        /// <summary>무기(책·낫)만 숨긴다. 등장 연출 직전 초기 상태에 사용. 의상·후드는 현재 상태 유지.</summary>
        public void HideWeapons()
        {
            Toggle(_bookEquip,       false);
            Toggle(_bookss,          false);
            Toggle(_scytheEquipRoot, false);
        }

        /// <summary>
        /// 폼 전환 — 표시할 장비는 디졸브 인(1→0), 숨길 장비는 즉시 비활성화.
        /// 등장 연출·Phase2Entry 패턴에서 Forget()으로 호출한다.
        /// </summary>
        public async UniTask DissolveInFormAsync(LichForm form, CancellationToken ct)
        {
            int idx = (int)form;
            if (_presets == null || idx < 0 || idx >= _presets.Length) return;

            var p = _presets[idx];
            CurrentForm = form;

            // 숨길 오브젝트는 즉시 비활성화, 표시할 오브젝트는 현재 숨겨진 경우만 디졸브 인
            // (이미 활성 상태인 오브젝트는 스킵 — 의상·후드 등 기존 표시 유지)
            var tasks = new List<UniTask>(7);
            AddDissolveTask(_bookEquip,       p.bookActive,            tasks, ct);
            AddDissolveTask(_bookss,          p.bookssActive,          tasks, ct);
            AddDissolveTask(_scytheEquipRoot, p.scytheEquipRootActive, tasks, ct);
            AddDissolveTask(_clothing,        p.clothingActive,        tasks, ct);
            AddDissolveTask(_skirtSeparate,   p.skirtSeparateActive,   tasks, ct);
            AddDissolveTask(_hoodDown,        p.hoodDownActive,        tasks, ct);
            AddDissolveTask(_hoodUp,          p.hoodUpActive,          tasks, ct);

            if (tasks.Count > 0)
                await UniTask.WhenAll(tasks);
        }

        // ─────────────────────────────────────────────────────────
        // Private Methods
        // ─────────────────────────────────────────────────────────

        private void AddDissolveTask(GameObject go, bool targetActive, List<UniTask> tasks, CancellationToken ct)
        {
            if (!targetActive) { Toggle(go, false); return; }
            if (go == null) return;
            if (!go.activeSelf) tasks.Add(DissolveInAsync(go, ct));
            // 이미 활성 상태이면 그대로 유지 (불필요한 디졸브·깜빡임 방지)
        }

        private async UniTask DissolveInAsync(GameObject go, CancellationToken ct)
        {
            if (go == null) return;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                go.SetActive(true);
                return;
            }

            var block = new MaterialPropertyBlock();

            // 완전 디졸브 상태로 초기화 후 활성화
            SetDissolveAll(renderers, block, 1f);
            go.SetActive(true);

            float elapsed = 0f;
            try
            {
                while (elapsed < _dissolveInDuration)
                {
                    await UniTask.Yield(cancellationToken: ct);
                    elapsed += Time.deltaTime;
                    SetDissolveAll(renderers, block, 1f - Mathf.Clamp01(elapsed / _dissolveInDuration));
                }
            }
            catch (OperationCanceledException)
            {
                SetDissolveAll(renderers, block, 0f);
                return;
            }

            SetDissolveAll(renderers, block, 0f);
        }

        private void SetDissolveAll(Renderer[] renderers, MaterialPropertyBlock block, float value)
        {
            foreach (var r in renderers)
            {
                r.GetPropertyBlock(block);
                block.SetFloat(_dissolvePropertyName, value);
                r.SetPropertyBlock(block);
            }
        }

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
                new FormPreset   // [3] Phase2 — Style 4/5: 로브·책 OFF, 낫 ON, 뼈 노출
                {
                    formName              = "Phase2_Liberation",
                    bookActive            = false,
                    bookssActive          = false,
                    scytheEquipRootActive = true,
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
