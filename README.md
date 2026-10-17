# ProjectFS-3DMap-Rendering — 3D 맵 적용 후 (2026-09-10 ~ 2026-09-29)

> **ProjectFS 개인 기여 코드 시리즈** — 팀 프로젝트 ProjectFS(비공개 저장소)에서 본인이 작성한 코드만 추출·재분류한 저장소입니다.
> 1. [ProjectFS-MonsterAI](https://github.com/Youngdu-Min/ProjectFS-MonsterAI) — 몬스터 AI (~2026-04-19)
> 2. [ProjectFS-Multiplayer-2DMap](https://github.com/Youngdu-Min/ProjectFS-Multiplayer-2DMap) — 탈출·Worm·생존자 추적·2D 절차적 맵 생성 (2026-04-20 ~ 09-09)
> 3. [ProjectFS-3DMap-Rendering](https://github.com/Youngdu-Min/ProjectFS-3DMap-Rendering) — 3D 맵 생성·LevelModule·Convolution 포스트 프로세싱 (2026-09-10 ~)

- 기준 스냅샷: `1786fdf8` (`MYD-feature-3dMap` 최종, 2026-09-30 Develop 병합)
- 주요 커밋: `edd796bd` 2D→3D 맵 생성 대체 · `c08097ee` 맵 종류별 프리팹 가중치 · `b9dfc0ae` LevelModule · `00111b8e` Convolution Filter

## 추출 기준

- `git blame -M -C -C -w`(이동·복사 추적, 공백 무시) 결과에서 **해당 기간 본인 작성 라인이 파일의 50% 이상**인 파일만 포함
- 50% 미만으로 일부 수정만 한 파일은 포함하지 않고 아래 "3D 전환 연동 수정" 표에 라인 수만 기록
- 파일 내용은 기준 스냅샷 시점 원본 그대로이며, 폴더 구조만 기능별로 재분류
- 서드파티(NavMeshPlus, Backrooms 에셋)와 자동 생성 코드(`UserInputAction.cs`)는 제외
- 프로젝트 의존성(FishNet, UniTask, Odin Inspector 등)이 없어 단독 컴파일되지 않음 — 열람용

## 폴더 구조

| 폴더 | 내용 |
|---|---|
| `01_LevelGeneration_3D` | 2D 그리드 결과 → 3D 프리팹 베이크. `GridToWorld` 좌표 변환, `PrefabBaker` 바닥/벽 2패스 배치, `PrefabBakeTestRunner` 경계 방향별 벽·문틀 배치, 크루스칼 방식으로 고립되는 짧은 경계만 개방, 가중 랜덤 모듈 채움, 통행 불가 모듈 영역 제한, 전용 난수로 배치 순서 보호 |
| `02_LevelModule` | 모듈러 프리팹 규격(피벗·마감면·돌출 허용치) 검증 컴포넌트, `Editor/` 에셋 이름 파싱으로 래퍼 프리팹을 일괄 생성·갱신하는 에디터 툴 |
| `03_ConvolutionFilter` | URP Render Graph 기반 커널 합성곱 포스트 프로세싱 — Renderer Feature / Pass / 커널 정의(3x3·5x5, 흑백 변환 옵션) / 셰이더 |

> 이 기간만 예외적으로 `.shader` 1개 포함 (Convolution Filter의 핵심 구현부)
> `PrefabBakeTestRunner.cs`는 2D `TilemapBakeTestRunner`를 기반으로 확장 — 복사 추적 기준 1083줄 중 979줄이 이 기간 신규 작성

## 3D 전환 연동 수정 (복사 제외, 기존 파일 일부 수정)

| 원본 경로 (`Assets/_Project/02_Scripts/` 기준) | 기간 내 본인 라인 / 전체 |
|---|---|
| `02_Scenes/LevelGenerationScene.cs` | 1 / 100 |
| `03_Gameflow/Item/MagazineItemSpawner.cs` | 2 / 75 |
| `03_Gameflow/Map/ObjectiveStateController.cs` | 19 / 386 |
| `07_Data/PlayerManager.cs` | 9 / 288 |
| `LevelGeneration/MapSpawnHelper.cs` | 10 / 46 |
| `LevelGeneration/Runners/MapGenerationMasterRunner.cs` | 1 / 148 |
| `LevelGeneration/Runners/MapGenerationResult.cs` | 4 / 13 |

> 위 파일들의 3D 전환 이전 버전은 [ProjectFS-Multiplayer-2DMap](https://github.com/Youngdu-Min/ProjectFS-Multiplayer-2DMap)에 포함 (`ObjectiveStateController`, `MagazineItemSpawner`, `MapSpawnHelper`, `MapGenerationMasterRunner`, `MapGenerationResult`)

## 원본 경로 매핑

| 분류 경로 | 원본 경로 (`Assets/_Project/02_Scripts/` 기준) | 기간 내 본인 라인 / 전체 |
|---|---|---|
| `01_LevelGeneration_3D/GridToWorld.cs` | `LevelGeneration/Grid/GridToWorld.cs` | 14 / 14 |
| `01_LevelGeneration_3D/PrefabBaker.cs` | `LevelGeneration/Prefab/PrefabBaker.cs` | 45 / 56 |
| `01_LevelGeneration_3D/PrefabBakeTestRunner.cs` | `LevelGeneration/Runners/PrefabBakeTestRunner.cs` | 979 / 1083 |
| `02_LevelModule/LevelModule.cs` | `LevelGeneration/Prefab/LevelModule.cs` | 160 / 163 |
| `02_LevelModule/ModuleKind.cs` | `LevelGeneration/Prefab/ModuleKind.cs` | 1 / 1 |
| `02_LevelModule/Editor/LevelModuleWrapperGenerator.cs` | `LevelGeneration/Editor/LevelModuleWrapperGenerator.cs` | 172 / 175 |
| `03_ConvolutionFilter/ConvolutionFilter.cs` | `Rendering/ConvolutionFilter.cs` | 40 / 40 |
| `03_ConvolutionFilter/ConvolutionFilterFeature.cs` | `Rendering/ConvolutionFilterFeature.cs` | 45 / 53 |
| `03_ConvolutionFilter/ConvolutionFilterPass.cs` | `Rendering/ConvolutionFilterPass.cs` | 64 / 66 |
| `03_ConvolutionFilter/ConvolutionKernels.cs` | `Rendering/ConvolutionKernels.cs` | 61 / 61 |
| `03_ConvolutionFilter/ConvolutionFilter.shader` | `Rendering/ConvolutionFilter.shader` | 65 / 65 |
