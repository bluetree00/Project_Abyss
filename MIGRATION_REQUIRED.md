# 네임스페이스 마이그레이션 가이드
> 게임명 확정: **Project Abyss → Relic Fairy**
> `namespace Abyss.*` → `namespace RelicFairy.*` 전체 변경

---

## 작업 전 필수 확인

- [ ] 현재 작업 중인 내용 **커밋 완료** (미커밋 상태에서 진행 금지)
- [ ] Unity **종료**
- [ ] Visual Studio / Rider **종료**

---

## Step 1 — PowerShell 스크립트 실행

프로젝트 루트(`CLAUDE.md`가 있는 폴더)에서 PowerShell을 열고 아래 스크립트를 붙여넣기 후 실행합니다.

```powershell
Get-ChildItem -Path "Assets" -Recurse -Filter "*.cs" | ForEach-Object {
    $c = [System.IO.File]::ReadAllText($_.FullName)
    $u = $c `
        -replace 'namespace Abyss', 'namespace RelicFairy' `
        -replace 'using Abyss\.', 'using RelicFairy.' `
        -replace 'menuName = "Abyss/', 'menuName = "RelicFairy/'
    $u = [regex]::Replace($u, 'Abyss\.(?=[A-Z])', 'RelicFairy.')
    if ($u -ne $c) {
        [System.IO.File]::WriteAllText($_.FullName, $u, [System.Text.Encoding]::UTF8)
        Write-Host "변경: $($_.Name)"
    }
}
Write-Host "--- 완료 ---"
```

---

## Step 2 — Assets 폴더 이름 변경

**Windows 탐색기**에서:

```
Assets/Abyss/        →  Assets/RelicFairy/
Assets/Abyss.meta    →  Assets/RelicFairy.meta
```

> ⚠️ 반드시 탐색기로 rename 해야 `.meta` GUID가 보존됩니다.  
> Unity 또는 VS Code가 열린 상태에서 rename 시 액세스 거부가 발생합니다.

---

## Step 3 — Unity 열기 및 컴파일 확인

Unity를 열고 Console 창에서 **Error가 0개**인지 확인합니다.

에러가 남아 있으면 해당 파일을 열어 `Abyss` 문자열이 남아있는지 검색 후 수동 수정합니다.

```powershell
# 잔여 Abyss 참조 확인용
grep -rn "namespace Abyss\|using Abyss\." Assets/ --include="*.cs"
```

---

## Step 4 — 커밋

```bash
git add -A
git commit -m "refactor: namespace Abyss → RelicFairy 전체 적용"
```

---

## Step 5 — develop 브랜치 merge

```bash
git fetch origin
git merge origin/develop
```

> 이 시점에서 develop(우리 브랜치)도 동일한 네임스페이스를 쓰고 있으므로  
> **실제 로직 변경분만 충돌**로 남습니다.

---

## 충돌 발생 시 처리 원칙

| 충돌 유형 | 처리 방법 |
|---|---|
| namespace/using 줄 충돌 | `RelicFairy.*` 버전 유지 |
| 로직 충돌 | 양쪽 의도 확인 후 직접 병합 |
| 파일 경로 충돌 (`Assets/Abyss/` vs `Assets/RelicFairy/`) | `RelicFairy/` 경로 유지, 내용은 최신 작업분 반영 |

---

## 변경되지 않는 항목 (건드리지 않아도 됨)

- Addressable 주소 키 (`"Lich/LichConfig"` 등) — 네임스페이스와 무관
- `.asset` / `.prefab` 파일 — Unity가 recompile 시 `m_EditorClassIdentifier` 자동 갱신
- `BuildScript.cs`의 출력 경로 (`AbyssBuild/Abyss.exe`) — 파일 경로이므로 변경 불필요
