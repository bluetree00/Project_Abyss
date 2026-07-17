# 팀 작업 가이드 (Addressables · Git · LFS)

작성: 2026-07-17 (KBG)

이 문서는 **"최신 브랜치를 받았는데 UI/에셋이 갱신되지 않는"** 류의 문제를 없애기 위한 가이드입니다.
실제로 겪은 사례와 원인, 그리고 앞으로 지킬 컨벤션을 정리했습니다.

---

## 1. 가장 먼저 — Play Mode Script를 "Use Asset Database"로 바꾸세요

**에디터에서 작업하는 동안엔 이거 하나면 대부분의 문제가 사라집니다.**

```
Window > Asset Management > Addressables > Groups
  → 상단 툴바의 [Play Mode Script] → "Use Asset Database (fastest)" 선택
```

이렇게 하면 번들을 거치지 않고 **프로젝트 에셋을 직접** 읽으므로, git으로 받은 최신 UI/에셋이 **항상 그대로** 보입니다.
어드레서블을 빌드할 필요도 없어집니다.

> 이 설정은 **개인 로컬 설정**(EditorPrefs)이라 git으로 공유되지 않습니다. **각자 본인 PC에서** 한 번 바꿔주세요.

---

## 2. 왜 "UI가 갱신 안 되는" 일이 생기나

이 프로젝트는 **UI가 전부 Addressable**입니다.

```
@UIRoot
UI/Popup/UI_ShopPanel
UI/Popup/UI_CruciblePanel
UI/Popup/UI_RelicInfoPopup
... (Default Local Group = 로컬 그룹)
```

그리고 어드레서블 **빌드 산출물은 git으로 전달되지 않습니다**:

| 경로 | 용도 | git |
|---|---|---|
| `Library/com.unity.addressables/aa/...` | 로컬 그룹 빌드 결과(에디터가 읽는 곳) | **무시됨** |
| `Assets/StreamingAssets/aa/*` | 런타임 로컬 로드 경로 | **무시됨** |
| `Assets/AddressableAssetsData/*/*.json`·`.bin`·`.hash` | 카탈로그 | **무시됨** |
| `Assets/AddressableAssetsData/AddressableAssetSettings.asset` | 설정 | 추적됨 |
| `ServerData/StandaloneWindows64/*.bundle` | 원격 그룹 산출물 | 추적됨 |

즉 **번들은 각자 로컬에서 빌드**하는 구조입니다.

그래서 Play Mode가 `Use Existing Build`인 상태에서 예전에 구운 번들이 남아 있으면:

- git으로 최신 UI 소스는 받았지만 →
- 런타임은 **내 PC의 낡은 번들**에서 **옛 UI**를 로드 →
- `git pull` / `git lfs pull` / Reimport 를 아무리 해도 **원리상 안 고쳐짐**

**진단 순서**: 에셋 갱신이 "안 넘어온다" 싶으면 git부터 의심하지 말고
① 해당 에셋이 Addressable 그룹에 있나 → ② 로컬 그룹인가 → ③ Play Mode가 뭔가

---

## 3. 어드레서블을 빌드해야 하는 때

에디터 작업만 한다면 (1번 설정을 했다면) **빌드할 필요 없습니다.**

**반드시 빌드해야 하는 경우:**
- **플레이어 빌드(.exe)를 만들기 전** ← 안 하면 빌드에 옛 UI/에셋이 박힙니다
- Play Mode를 `Use Existing Build`로 두고 번들 상태로 테스트하고 싶을 때

**빌드 방법:**
```
RelicFairy > Addressables > Build Now          (클린 리빌드, 권장)
또는
Window > Asset Management > Addressables > Groups → Build > New Build > Default Build Script
```

> ⚠️ 2026-07-17 이전에는 `m_ActivePlayerDataBuilderIndex`가 `0`(FastMode)로 잘못 박혀 있어
> **어드레서블 빌드가 항상 0초 만에 실패**했습니다. 커밋 `ca87ff600`에서 `2`(PackedMode)로 고쳤습니다.
> 그 전에 만든 플레이어 빌드는 콘텐츠가 최신이 아니었을 수 있습니다.

---

## 4. Git 컨벤션

### 4-1. 브랜치 구조

| 브랜치 | 담당 | 역할 |
|---|---|---|
| `develop` | 공용 | 통합 브랜치 (여기서 합칩니다) |
| `dev/lee` | LEE | 보스 / 몬스터 |
| `dev/KBG-D` | KBG | 그 외 전반 |

### 4-2. ★ 머지는 반드시 **양방향**으로

가장 중요한 컨벤션입니다.

```bash
# ① 먼저 develop을 내 브랜치로 가져온다  ← 이걸 빠뜨리면 안 됩니다
git fetch origin
git checkout dev/lee
git merge origin/develop

# ② 충돌 해결 + Unity에서 정상 동작 확인

# ③ 그 다음에 내 작업을 develop으로 올린다
git checkout develop
git pull
git merge dev/lee
git push origin develop
```

**`dev/lee → develop` 방향만 하고 `develop → dev/lee`를 안 하면**, 내 브랜치엔 상대 작업이 영영 안 들어옵니다.
(실제로 이것 때문에 LEE 쪽에서 UI가 계속 옛날 것으로 보였습니다.)

또한 **수백 커밋 뒤진 브랜치를 develop으로 바로 머지하면** 충돌 해결을 잘못할 때 상대 작업이 되돌아갈 위험이 큽니다.
**①을 먼저** 하면 이 위험이 크게 줄어듭니다.

### 4-3. 충돌 났을 때

- **자기 담당 영역**(LEE=보스/몬스터, KBG=그 외)은 **담당자 것 우선**
- 담당 밖 파일에서 충돌이 나면 → **덮어쓰지 말고 담당자에게 물어보기**
- 특히 `PlayerController.cs`, `MonsterBase.cs` 처럼 양쪽이 함께 건드리는 파일은 주의
  (보스 지원용 추가는 유지하되, 상대 영역 로직을 되돌리지 않도록)

### 4-4. 커밋에 넣지 말 것

- `*.bak`, `*.bak.meta` 등 **백업 파일**
- 어드레서블 **빌드 산출물** (어차피 gitignore)
- `.meta` 파일을 **손으로 만들거나 지우지 말 것** — Unity가 자동 관리합니다

---

## 5. LFS 컨벤션

### 5-1. 프리팹은 **텍스트**가 기본입니다

`.gitattributes`에 **`*.prefab filter=lfs`(전체 프리팹 LFS)를 넣지 마세요.**

이유:
- LFS 프리팹은 **3-way 머지가 불가**해집니다 → 프리팹 충돌이 all-or-nothing이 되어 팀 작업이 깨집니다
- 규칙 추가 전에 커밋된 프리팹들과 불일치가 생겨, **계속 "수정됨"으로 뜨는 churn**이 발생합니다

### 5-2. 대용량 보스 프리팹만 예외적으로 LFS

GitHub는 **단일 파일 100MB 한도**가 있고, 일부 보스 프리팹은 이를 넘습니다
(`Arena_Boss_Ch3.prefab` = **약 240MB**). 이런 것만 명시적으로 LFS에 넣습니다:

```gitattributes
# 대용량 보스 프리팹만 LFS (일반 프리팹은 텍스트 유지 → 3-way 머지 가능)
Assets/RelicFairy/Characters/Monster/Monster/DragonBoss/Prefab/DragonBoss.prefab filter=lfs diff=lfs merge=lfs -text
Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Prefab/ForestGuardian.prefab filter=lfs diff=lfs merge=lfs -text
Assets/RelicFairy/Systems/Stage/MapGen/Prefabs/Arena_Boss_*.prefab filter=lfs diff=lfs merge=lfs -text
```

**새로 100MB 넘는 프리팹을 추가한다면** → `.gitattributes`에 **직접 등록**해야 합니다.
안 하면 push가 `exceeds GitHub's file size limit`로 거부됩니다.

> 머지 중 `.gitattributes`가 충돌하면 **위 방식(보스 경로만 LFS)** 쪽으로 맞춰주세요.

### 5-3. LFS 데이터 받기

브랜치를 받았는데 **텍스처/스프라이트가 깨져 보이면**, 포인터만 있고 실제 데이터가 없는 상태입니다:

```bash
git lfs pull
```

---

## 6. 문제 해결 체크리스트

**"UI/에셋이 갱신 안 됨"**
1. Play Mode Script가 `Use Asset Database (fastest)` 인가? → 아니면 바꾸기 (§1)
2. `git merge origin/develop` 을 **내 브랜치에** 했나? (§4-2)
3. `git lfs pull` 했나? (§5-3)
4. Unity에서 Reimport, 그래도 안 되면 프로젝트 닫고 `Library` 삭제 후 재열기

**"push가 거부됨 (file size limit)"**
→ 100MB 넘는 파일을 LFS 없이 커밋한 것. `.gitattributes`에 등록 (§5-2)

**"프리팹이 계속 수정됨으로 뜸"**
→ `.gitattributes`에 전체 `*.prefab` LFS 규칙이 들어왔는지 확인 (§5-1)

**"어드레서블 빌드했는데 그대로임"**
→ 빌드가 **실패**했을 수 있습니다. `AddressablesBuildRunner`는 실패해도 `"build complete"`를 찍으니
   콘솔에서 `Addressable content build failure` 가 있는지 직접 확인하세요.
