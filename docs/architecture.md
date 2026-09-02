# 아키텍처

> 📄 [`Core/`](../Assets/Scripts/Core/) · [`IG/`](../Assets/Scripts/IG/) · [`Managers/`](../Assets/Scripts/Managers/)

---

## 전체 구조

MVC + Manager 패턴입니다.

```
┌─────────────────────────────────────────────────────────────┐
│  IGEngine  (진입점 — MonoBehaviour)                          │
│    Awake  : Firebase 초기화, 폰트 주입                        │
│    Start  : InitializeManagers → InitializeControllers       │
│             → StartGame → 화면 변경 구독                       │
└─────────────────────────────────────────────────────────────┘
        │
        ├── Manager (13개 싱글톤) ── 시스템 단위 기능
        │      ManagerBase<T>    : 생명주기 3단계 보유 (6개)
        │      SingletonClass<T> : 상태만 보유 (7개)
        │
        └── Controller (5개) ── 게임 로직
               │
               ├── Model ── 데이터 · 상태 ── UniRx IObservable 로 변경 방출
               │
               └── View  ── 구독해서 그린다 (Model 은 View 를 모른다)
```

| 레이어 | 역할 | 예시 |
|---|---|---|
| **Model** | 게임 데이터 · 상태 | `IGBoardModel`, `IGBlockModel`, `ScoreModel` |
| **View** | 시각적 표현 | `IGBoardView`, `HUDView` *(발췌본 미포함)* |
| **Controller** | 게임 로직 | `IGGameController`, `IGBoardController`, `IGInputController` |
| **Manager** | 시스템 단위 기능 | `PoolManager`, `SaveManager`, `AdManager` |

---

## 싱글톤 두 계층

전부 싱글톤이지만 **생명주기 요구가 다릅니다.**

```csharp
public abstract class ManagerBase<T> : SingletonClass<T> where T : ManagerBase<T>
{
    public abstract void InitializeManager();
    public abstract void ClearManager();
    public abstract void FinalizeManager();

    protected override void OnApplicationQuit()
    {
        FinalizeManager();
        ClearManager();
        base.OnApplicationQuit();
    }
}
```

**`ManagerBase<T>` (6개)** — 게임 진행에 따라 **초기화 / 리셋 / 정리**가 필요한 것들입니다.
`ClearManager()`는 종료가 아니라 **게임 재시작 시 상태를 비우는** 용도입니다.

```
IGGameManager  GameStateManager  PoolManager  AdManager  SignInManager  ThemeManager
```

**`SingletonClass<T>` 직접 상속 (7개)** — 상태만 들고 있고 게임 라운드와 무관한 것들입니다.
3단계 생명주기를 강제할 이유가 없어 상속 계층을 나눴습니다.

```
SaveManager  SettingsManager  AudioManager  GameStatsManager
DifficultyManager  LocalStorage  UIPopupManager
```

> 추상 메서드가 3개면 구현체는 그걸 다 채워야 합니다. 채울 내용이 없는 클래스에
> 빈 메서드 3개를 만들게 하는 대신 계층을 나눴습니다.
> 반대로, 이 분리 때문에 "이 매니저는 어느 쪽인가"를 매번 판단해야 하는 비용이 생겼습니다.

### 싱글톤 접근자가 방어하는 것

`SingletonClass<T>.Instance`는 단순 지연 생성이 아닙니다.

| 상황 | 처리 |
|---|---|
| 애플리케이션 종료 중 | `null` 반환 — **종료 순서 때문에 좀비 객체가 되살아나는 것**을 막습니다 |
| 에디터 플레이 재시작 | 정적 필드 강제 리셋 — 도메인 리로드를 끈 상태에서 이전 세션 참조가 남는 문제 |
| `Awake` 전 접근 | `DontDestroyOnLoad` 선반영 |

`IsValidInstance()`도 함께 둡니다. 호출부에서 **"없으면 만들지 말고 그냥 건너뛴다"** 를
표현하기 위해서입니다. `IGEngine`의 초기화 시퀀스가 이 형태를 씁니다.

```csharp
if (IGGameManager.IsValidInstance()) IGGameManager.Instance.InitializeManager();
if (PoolManager.IsValidInstance())   PoolManager.Instance.InitializeManager();
```

`Instance`를 그냥 부르면 **초기화 시퀀스가 매니저를 생성해버려서** 씬 구성과 무관하게
객체가 생깁니다. 접근과 생성을 구분하려는 것입니다.

> ⚠️ 이 구조 전체의 한계는 [known-limitations.md](known-limitations.md) #1에 적었습니다.
> **13개 싱글톤과 179군데의 `.Instance` 호출이 이 프로젝트의 가장 큰 부채입니다.**

---

## Model → View 통신: UniRx

Model은 View의 존재를 모릅니다. 변경을 `IObservable`로 방출할 뿐입니다.

```csharp
// ScoreModel.cs
public IObservable<long>          OnScoreChangedObservable     => _onScoreChanged.AsObservable();
public IObservable<int>           OnComboChangedObservable     => _onComboChanged.AsObservable();
public IObservable<long>          OnBestScoreChangedObservable => _onBestScoreChanged.AsObservable();
public IObservable<long>          OnScoreAddedObservable       => _onScoreAdded.AsObservable();
public IObservable<ComboChipData> OnComboChipObservable        => _onComboChip.AsObservable();
```

스트림을 목적별로 쪼갠 것이 핵심입니다. `OnScoreChanged`(현재 점수)와
`OnScoreAdded`(증가분)는 같은 사건에서 나오지만 소비처가 다릅니다 — 전자는 HUD 숫자,
후자는 획득 연출입니다. 하나로 합치면 모든 구독자가 "지금 뭐가 바뀐 건지"를 매번 계산해야
합니다.

`ComboChipData`처럼 **연출에 필요한 값을 한 묶음으로** 보내는 스트림도 있습니다.

```csharp
public struct ComboChipData
{
    public long  baseScore;    // ExpressionEvaluator 결과
    public float mult;         // 콤보 배수 (1.0 · 1.1 … 최대 2.0)
    public long  total;        // 실제 추가 점수 = baseScore × mult
    public int   clearedCount; // 한 턴에 지워진 라인/스퀘어 수 (라벨 텍스트 결정용)
}
```

View가 `total`만 받으면 "왜 이만큼인지"를 표현할 수 없습니다. **연출에 필요한 정보를 Model이
계산 시점에 함께 실어 보냅니다.**

> ⚠️ 다만 구독 해제 규율은 사람이 지켜야 합니다. 프로젝트 규칙은 `AddTo(gameObject)`이지만
> 강제 장치가 없습니다. 실제로 구독 누수가 게임오버 소프트락을 만든 적이 있습니다.

---

## 게임플레이 데이터 흐름

```
IGInputController  드래그 · 드롭 좌표
        ↓
IGBlockController  배치 가능 판정 (블록 형상 × 보드 상태)
        ↓
IGBoardModel       타일 확정 → 라인/스퀘어 완성 검사
        ↓
ExpressionEvaluator  지워진 타일 값 → 정규화 → 점수
        ↓
ScoreModel         점수 · 콤보 갱신 → IObservable 방출
        ↓
HUDView / ScoreGainToast / ComboChipToast   구독해서 그림
```

**좌표계** — 보드는 `Vector2Int` 그리드입니다. X는 왼→오른쪽(0-8), Y는 위→아래(0-8),
원점 (0,0)은 좌상단입니다. 블록 형상은 3×3 정수 배열(`1`=채움, `0`=빔)로 정의합니다.

**타일 값 생성** — 단순 무작위가 아니라 진행도에 따라 확률이 변합니다.

| 클래스 | 역할 |
|---|---|
| `TilePhaseProfile` | 한 페이즈의 타일별 목표 가중치 (정규화 안 된 원시값) |
| `TileProbabilityResolver` | 페이즈 간 **보간** 후 정규화 |
| `TileValueGenerator` | 확률에 따라 실제 타일 값 생성 |
| `TileValueSanitizer` | 생성 결과 보정 |

페이즈 사이를 보간하는 이유는 난이도가 **계단이 아니라 연속적으로** 변하게 하기
위해서입니다. `TileIndex` enum과 가중치 배열의 순서는 반드시 일치해야 하며, 그 제약이 주석에
명시되어 있습니다 (`Count`를 sentinel로 두어 길이를 고정합니다).

---

## 오브젝트 풀링

`PoolManager`가 타일과 블록을 관리합니다. 9×9 보드에서 타일은 매 턴 생성·파괴되므로
`Instantiate`/`Destroy`를 그대로 쓰면 GC 스파이크가 납니다.

**풀 반환 누락은 조용히 누적됩니다** — 그래서 [AutoPlayBot](automated-qa.md)이 장시간
플레이하며 구간별 메모리 스냅샷을 남깁니다. 정적 분석으로는 잡히지 않는 종류의 버그입니다.

---

## 플랫폼 경계

Android와 WebGL(앱인토스)의 차이는 인터페이스 뒤로 숨깁니다.
자세한 내용은 **[multiplatform-port.md](multiplatform-port.md)** 를 참고해 주세요.

| 경계 | 인터페이스 | Android | WebGL |
|---|---|---|---|
| 광고 | `IAdProvider` | `AdMobProvider` | `AitAdProvider` |
| 로그인 | `ISignInService` | `GpgsSignInService` | `AitSignInService` |
| 세이프에어리어 | `SafeAreaSource` | `Screen.safeArea` | `AitSafeAreaProvider` |
| 세이브 | `LocalStorage` | 파일 | + `AitSaveMirror` |

**플랫폼 분기(`#if UNITY_WEBGL`)는 구현체를 고르는 지점에만 있습니다.** 게임 코드에는
없습니다.

> 이 구역은 게임 코어보다 의존성이 정리되어 있는데, 나중에 만들면서 처음부터 구현체가
> 둘일 것을 알고 설계했기 때문입니다. 반대로 게임 코어는 싱글톤 결합이 남아 있습니다.

---

## 진입점 시퀀스

`IGEngine.Start()`의 순서에는 제약이 있고, 그게 주석에 남아 있습니다.

```
InitializeManagers()     매니저 초기화
InitializeControllers()  컨트롤러 초기화 (IGBoardModel 이 orthographicSize 를 한 번 설정)
StartGame()              SetupCamera() 가 그 값을 덮는다  ← 최종 결정 지점
```

그래서 뷰포트가 바뀌었을 때 `SetupCamera`만 다시 부르면 로드 시점 상태가 그대로
재현됩니다. 이걸 몰랐다면 화면이 바뀔 때마다 전체를 다시 초기화했을 것입니다.

```csharp
ScreenChangeWatcher.EnsureRunning();
ScreenChangeWatcher.OnChanged += SetupCamera;
```

화면이 고정된 플랫폼에서는 이벤트가 발생하지 않아 동작이 동일합니다.
그리고 `OnDestroy`에서 구독을 해제합니다 — 정적 이벤트라 해제를 빠뜨리면 씬을 오갈 때마다
누적됩니다.
