<h1 align="center">Project Abyss</h1>
<h3 align="center">Roguelike Action PC Game</h3>

<p align="center">
  <b>확장 가능한 아키텍처</b> · <b>모듈형 시스템</b> · <b>Data-Driven 구조</b>
</p>

<hr/>

## About Project

<strong>Project Abyss</strong>는 스테이지 클리어 기반의 로그라이크 액션 PC 게임이다.  
플레이어는 전투를 통해 장비와 퍼즐을 획득하고 조합하여 캐릭터를 성장시키며, 최종 챕터 클리어를 목표로 진행함.

<br/>

### Core Gameplay
- 액션 중심 전투 구조
- 퍼즐 기반 능력 강화 시스템
- 장비 모듈화 성장 구조
- 로그라이크 반복 플레이 설계

<hr/>

## Tech Stack

<p>
  <img src="https://img.shields.io/badge/Engine-Unity-000000?style=for-the-badge&logo=unity"/>
  <img src="https://img.shields.io/badge/Language-C%23-239120?style=for-the-badge&logo=c-sharp"/>
  <img src="https://img.shields.io/badge/Input-New%20Input%20System-00599C?style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Resource-Addressables-FF6F00?style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Backend-뒤끝-1E88E5?style=for-the-badge"/>
</p>

<hr/>

## Architecture & Design

### 1) Layer FSM 기반 캐릭터 구조
- 플레이어/몬스터의 <b>LayerFSM</b> 구조 개발  
- 상태/행동 분리로 <b>확장성</b> 확보  
- 행동 계층화로 <b>유지보수성</b> 강화

### 2) Managers 패턴 기반 시스템 구조
- 매니저를 관리하는 <b>Managers</b> 디자인 패턴 구조 설계
- <b>Run 단위 생명주기</b> 분리 및 책임 기반 모듈화

```text
Managers
 ├── Core
 │    ├── GameRunManager        (Run 생명주기/흐름 제어)
 │    ├── StagePointManager     (스테이지 이동/선택 관리)
 │    └── RoomManager           (룸 로드/선택/가중치 처리)
 │
 ├── Data
 │    ├── CharacterDataManager  (캐릭터 데이터)
 │    ├── EquipmentDataManager  (장비 데이터)
 │    ├── PuzzleDataManager     (퍼즐/스탯 반영)
 │    └── TableDataManager      (서버 테이블 반영/매핑)
 │
 ├── System
 │    ├── AddressableManager    (리소스 로딩/관리)
 │    ├── PoolManager           (오브젝트 풀링)
 │    └── InputManager          (입력 시스템/정책)
 │
 └── UI
      ├── HUDManager
      ├── UIManager
      └── UIDataProvider        (UI 바인딩/갱신 데이터 제공)
