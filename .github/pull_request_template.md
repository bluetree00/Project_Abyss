## 무엇을
<!-- 이 PR이 바꾸는 것을 1~3줄로. "왜"가 자명하지 않으면 아래 칸에 적을 것. -->


## 왜
<!-- 배경·원인. 버그면 재현 조건과 근본 원인을 적는다. 증상만 적지 말 것. -->


## 어떻게 확인했나
<!-- 실제로 돌려본 것만 적는다. 안 해봤으면 "미확인"이라고 남길 것. -->
- [ ] 컴파일 에러 0 (`refresh_unity` → `read_console`, MCP 없으면 `dotnet build Assembly-CSharp.csproj`)
- [ ] 에디터 재생 확인
- [ ] 관련 씬/프리팹 육안 확인

## 체크
- [ ] `[SerializeField] private` 사용, public 필드 노출 없음
- [ ] `Update` 계열에서 `GetComponent`/`Find`/힙 할당 없음
- [ ] 비동기는 `UniTask` + `CancellationToken`
- [ ] 이벤트는 `OnEnable` 구독 / `OnDisable` 해제
- [ ] `.meta` 직접 수정 없음
- [ ] `AddressableAssetSettings.asset`을 스테이징하지 않음 <!-- m_currentHash만 변하는 게 정상 -->
- [ ] 에셋(프리팹·씬·UI)을 바꿨다면 빌드 시 Addressables가 함께 구워지는지 확인

## 리스크 / 후속
<!-- 세이브 호환, 밸런스 영향, 남긴 TODO. 없으면 "없음". -->

