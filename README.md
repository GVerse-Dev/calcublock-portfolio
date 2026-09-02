<div align="center">

# CalcuBlock : Math Puzzle

**블록 퍼즐에 사칙연산을 결합한 모바일 게임입니다.**
줄이 지워질 때 타일 값들이 하나의 수식이 되고, 그 계산 결과가 점수가 됩니다.

[![Google Play](https://img.shields.io/badge/Google_Play-출시_중-414141?logo=googleplay&logoColor=white)](https://play.google.com/store/apps/details?id=com.gversedev.calcublock)
![Unity](https://img.shields.io/badge/Unity-6000.4.5f1-000000?logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)
![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white)

### [▶ Google Play에서 다운로드](https://play.google.com/store/apps/details?id=com.gversedev.calcublock)

</div>

---

## 요약

혼자 만들어서 Google Play에 출시한 퍼즐 게임입니다.

회사에서는 이미 만들어진 시스템 위에 콘텐츠를 얹는 일을 했습니다.
처음부터 끝까지 직접 해보고 싶어서 시작했습니다.

|  |  |
|---|---|
| 🎯 **출시** | Google Play 정식 출시 (`com.gversedev.calcublock`, v1.0.3) |
| 🧪 **QA** | Unity Test Runner 88개 케이스 · 자동 플레이 봇 · 실기기 확인 |
| 📊 **계측** | 시뮬레이션 2,000판으로 난이도 확인, 출시 후 콘솔 지표로 기능 제거 판단 |
| 🌐 **확장** | 광고·로그인 SDK를 인터페이스로 추상화, WebGL 포팅 진행 중 |
| 🛠 **운영** | GDPR 동의 수집 · 세이브 무결성 · 크래시 수집 · 스토어 심사 |

|  |  |
|---|---|
| **기간** | 착수 2022.11 · **실질 개발 2026.05–2026.08 (4개월)** · 최종 커밋 2026.08.31 |
| **작업량** | 커밋 279건 중 **226건(81%)이 2026.05–08에 집중** |
| **인원** | 1인 개발 (기획 · 개발 · 밸런싱 · 심사 · 출시) |
| **엔진 / 언어** | Unity 6000.4.5f1 (URP) / C# · UniRx · DOTween · NUnit |

<details>
<summary><b>📌 저장소 구성</b></summary>

<br>

출시 중인 게임이라 원본 저장소는 비공개입니다. 여기에는 코드 일부만 옮겨 두었습니다.

- 에셋·SDK·씬·`.meta` 파일이 빠져 있어 **Unity로는 열리지 않습니다.** 열람용입니다.
- 경로는 원본 그대로 유지했습니다.
- 운영 광고 ID는 자리표시자로 대체했고, 그 외 코드는 손대지 않았습니다.

원본 174개 파일 · 21,545 LOC 중 99개 파일 · 13,028 LOC를 옮겼습니다.

```
Assets/
├── Scripts/
│   ├── Core/          SingletonClass, ManagerBase, IGEngine, Telemetry
│   ├── IG/            게임 코어 (Data · Model · Controller · Interface)
│   ├── Ads/           IAdProvider + AdMob/앱인토스 구현, 동의, 노출 정책
│   ├── SignIn/        ISignInService + GPGS/앱인토스/Null 구현
│   ├── Storage/       LocalStorage, AitSaveMirror, SessionIntegrity
│   ├── Managers/      GameStateManager, PoolManager, SaveManager, ThemeManager
│   └── Firebase/      Crashlytics · Analytics 초기화
├── QA/AutoPlayBot/    자동 플레이 봇 + 메모리 계측
├── Tests/             EditMode(단위 · 시뮬레이션) · PlayMode(스모크 · 통합)
├── Editor/            빌드 스크립트, 스토어 빌드 가드
└── SimulationData/    시뮬레이션 결과 CSV
```

뺀 것은 View 레이어 대부분, UI 위젯, 디자인 시스템, 마케팅 영상 촬영 도구,
아트 에셋, 서드파티 SDK, 씬 파일, `ProjectSettings/` 입니다.

</details>

### 게임 규칙

9×9 그리드에 블록을 놓아 가로줄 · 세로줄 · 3×3 스퀘어를 완성합니다.
일반 블록 퍼즐과 다른 점은 **타일마다 숫자나 연산 기호(`+ − × ÷`)가 올라가 있다**는 것입니다.

줄이 사라질 때 그 타일들이 하나의 수식으로 이어지고, 계산 결과가 그대로 점수가 됩니다.
즉 "줄을 지운다"가 아니라 **"어떤 수식이 만들어지도록 지울 것인가"** 가 플레이의 축입니다.

---

## 만들면서 내린 판단

> 요지만 먼저 적었습니다. **▸ 를 누르면 근거와 코드가 펼쳐집니다.**

### 1. 계산할 수 없는 수식을 점수로 바꾸기

플레이어는 `3 / * 0`, `+ + 5 −`, `7 8` 같은 배열을 얼마든지 만듭니다. 그래도 점수는 반드시
나와야 합니다.

순서가 고정된 4단계로 수식을 정리했습니다. 중요한 건 **단계의 순서 자체가 정확성 조건**이라는
점입니다. 반례를 회귀 테스트로 고정했고, 이 클래스 하나에 테스트 79개가 붙어 있습니다.

<details>
<summary><b>▸ 순서를 바꾸면 왜 크래시가 나는가</b></summary>

<br>

| 단계 | 처리 | 예시 |
|---|---|---|
| 1. `Tokenize` | 연속된 숫자 타일을 하나의 수로 병합. 두 숫자 사이 공백은 `+`로 승격 | `1 2` → `12` |
| 2. `CollapseConsecutiveOperators` | 연속된 연산 기호 중 첫 번째만 남김 | `+ + 5` → `+ 5` |
| 3. `RemoveDivisionByZero` | `÷` 뒤에 `0`이 오면 **두 토큰을 함께** 제거 | `6 / 0` → `6` |
| 4. `TrimEdgeOperators` | 수식 앞뒤에 남은 연산 기호 제거 | `+ 5 −` → `5` |

**2번과 3번의 순서를 바꾸면 0으로 나누기가 통과합니다.**

```
입력: 3 / * 0

[잘못된 순서] RemoveDivisionByZero → Collapse
  ① RemoveDivisionByZero: "/" 다음 토큰이 "*" 이므로 /0 패턴 미검출. 통과.
  ② Collapse:             "/ *" → "/" 로 축약. 여기서 "3 / 0" 이 새로 생성됨.
  ③ 평가:                 3 / 0  →  크래시
                          ↑ 검사 단계는 ①에서 이미 지나갔다

[올바른 순서] Collapse → RemoveDivisionByZero
  ① Collapse:             "3 / * 0" → "3 / 0"
  ② RemoveDivisionByZero: "/ 0" 검출 → 둘 다 제거 → "3"
  ③ 평가:                 3
```

`Collapse`가 **새로운 `/0` 쌍을 만들어낼 수 있다**는 것이 함정입니다. 취향 문제가 아니라
바꾸면 크래시가 나는 조건이라, 반례와 함께 소스 상단 주석에 적어 두었습니다.
주석만으로는 막을 수 없으므로 테스트 이름이 무엇을 지키는지 말하게 했습니다.

```csharp
public void Regression_NormalizationOrder_DivOpZero(string expression, long expected)
```

**곱셈의 `0`은 일부러 그대로 둡니다.** `× 0`을 함께 제거하면 "곱셈으로 점수를 키우려다 0
타일을 밟아서 날렸다"는 긴장이 사라집니다. 0 타일은 피해야 할 위험 요소로 설계한 것이라,
처리 가능하다고 해서 없애면 의도가 깨집니다.

</details>

📄 [`ExpressionEvaluator.cs`](Assets/Scripts/IG/Data/ExpressionEvaluator.cs) ·
[전체 문서 →](docs/expression-normalization.md)

---

### 2. 두 번째 플랫폼에서 게임 로직을 고치지 않기

출시 후 앱인토스(토스 미니앱) 포팅을 시작했습니다. 광고 · 로그인 · 리더보드 · 세이브가
전부 다른 SDK입니다.

경계마다 인터페이스를 긋고, **플랫폼 분기는 구현체를 고르는 지점 한 곳에만** 두었습니다.

| 경계 | 인터페이스 | Android | WebGL |
|---|---|---|---|
| 광고 | `IAdProvider` | `AdMobProvider` | `AitAdProvider` |
| 로그인 | `ISignInService` | `GpgsSignInService` | `AitSignInService` |
| 세이프에어리어 | `SafeAreaSource` | `Screen.safeArea` | `AitSafeAreaProvider` |
| 세이브 | `LocalStorage` | 파일 | + `AitSaveMirror` |

<details>
<summary><b>▸ 인터페이스에 계약을 적는 이유, 그리고 컴파일러가 안 잡아준 것</b></summary>

<br>

```csharp
// AdManager.cs — 게임 전체에서 광고 SDK를 고르는 분기는 여기가 전부다
#if UNITY_WEBGL
    _provider = new AitAdProvider();
#else
    _provider = new AdMobProvider();
#endif
```

구현체가 하나일 때는 인터페이스 주석이 사치처럼 느껴집니다. 둘이 되는 순간 달라집니다.
같은 메서드가 구현마다 다른 의미를 갖기 시작하고, 그건 호출부에서 보이지 않습니다.

`IAdProvider.ShowInterstitial`의 `onClosed(bool shown)`가 그런 자리였습니다.

```csharp
/// onClosed(shown) 의 shown 은 **광고가 실제로 화면에 노출된 뒤 닫혔는지**를 뜻한다.
/// 노출 실패, 미동의, 광고 미준비 같은 경로는 게임 흐름을 막지 않기 위해 콜백을
/// 즉시 돌려주지만 shown=false 다.
///
/// 이 구분이 필요한 이유: 호출자(AdGatePolicy)가 노출 횟수를 디스크에 영속화하므로,
/// 안 띄운 것을 띄웠다고 기록하면 오염이 세션을 넘어 남는다.
```

이유를 안 적어두면 나중에 두 번째 구현을 쓸 때 "광고 호출이 끝났으니 true"로 채우기 쉽고,
그러면 빈도 제한 상태가 조용히 오염됩니다. 버그는 몇 세션 뒤에 "광고가 너무 안 나온다"로
나타나 추적이 어렵습니다.

#### WebGL 첫 실행이 백지 화면이었습니다

포팅 초기에 브라우저에서 타이틀이 백지로 떴습니다.

```
ArgumentNullException: Value cannot be null. Parameter name: type
  at GoogleMobileAds.Ump.Api.Utils.GetClientFactory()
  at IGMain.Ads.ConsentManager.Gather()
  at AdManager.InitializeManager()  →  TitleScene.Awake()
```

UMP가 플랫폼 클라이언트를 **리플렉션으로** 찾는데 WebGL 구현체가 없어서 던졌고,
그 호출이 `Awake` 스택 안이라 씬 초기화가 통째로 중단됐습니다.

**GoogleMobileAds는 Any-Platform DLL이라 컴파일은 통과합니다.** 즉 `#if`로 코드를 갈라도
컴파일러가 잡아주지 못하고 런타임에만 드러나는 부류였습니다. `ConsentManager` 전체를
`#if !UNITY_WEBGL`로 감싸고 스텁을 뒀습니다. 호출부 7곳은 손대지 않았고, 스텁도 원본 계약
(`onComplete`를 정확히 한 번 호출)을 지킵니다.

같은 이유로 Firebase도 WebGL 컴파일을 막고 있었습니다. 파사드 표면을 유지한 스텁으로
격리해 호출부 6곳을 무수정으로 뒀습니다. 반대로 GPGS는 손댈 필요가 없었는데,
`SignInManager`의 기존 `#if UNITY_ANDROID` 분기가 이미 `NullSignInService`로
폴백하고 있었기 때문입니다.

</details>

📄 [`IAdProvider.cs`](Assets/Scripts/Ads/IAdProvider.cs) ·
[전체 문서 →](docs/multiplatform-port.md)

---

## 터진 문제를 좁혀 간 과정

### 3. 게임오버가 아무 반응 없이 멈추던 문제

게임오버 팝업을 코루틴으로 지연시키는 변경을 넣은 뒤 회귀가 났습니다. 팝업이 안 뜨는 정도가
아니라 **점수 저장 · 실패 연출 · 전면 광고가 전부 실행되지 않는 복구 불가 상태**였습니다.

원인은 세 단계로 얽혀 있었고, **셋 중 둘은 그 변경 전부터 이미 있던 잠복 결함**이었습니다.

<details>
<summary><b>▸ 잠복해 있던 누수가 어떻게 소프트락이 됐는가</b></summary>

<br>

실기기 로그는 이렇게 나왔습니다.

```
ArgumentNullException: Value cannot be null (parameter: obj)
  at MonoBehaviour.StartCoroutine
  at IGMain.HUDView.OnGameStateChanged (HUDView.cs:37)
  at GameStateManager.SetGameState
  at IGMain.IGGameController.CheckGameOver
```

**① 중복 구독** — `UIScreenManager.AddToRegistry`가 `page.Initialize()`를 부르는데
`HUDView`에 재초기화 가드가 없어 구독이 중복으로 쌓였습니다. `OnDestroy`의 `-=`는 하나만
떼므로 **파괴된 인스턴스를 가리키는 구독이 남았습니다.**

**② 그 누수가 무해하게 숨어 있었습니다** — 기존 코드는 `UIPopupManager.Instance.Open<>()`
이라 파괴된 컴포넌트에서도 동작했습니다. `StartCoroutine`은 살아 있는 `MonoBehaviour`를
요구하므로, 코루틴으로 바꾼 순간 비로소 예외가 됐습니다.

**③ 멀티캐스트 델리게이트** — 예외가 나면 **뒤 구독자가 호출되지 않습니다.** 게다가 예외가
`CheckGameOver`까지 올라가 그 뒤 코드가 전부 취소됐습니다. 살아 있는 `HUDView`가 호출되지
않아 팝업이 안 뜨고, 점수 저장과 광고도 함께 날아간 것입니다.

수정은 두 가지입니다.

- `GameStateManager`: 상태 변경 통지를 **구독자별 `try/catch`로 격리**했습니다.
  상태 전이가 게임 흐름의 근간이라, **한 구독자 때문에 전체가 멈추지 않도록** 했습니다.
  범인을 찾을 수 있도록 선언 타입과 메서드 이름을 로그에 남깁니다.
- `HUDView.Initialize`: `-=` 후 `+=`로 중복 구독을 차단했습니다.

**얻은 것** — "구독 누수는 메모리 문제"라고만 생각했는데, 실제로는 **기능이 통째로 멈추는
경로**였습니다. 그리고 그것이 무해해 보였던 이유가 "예전 코드가 우연히 관대했기
때문"이라는 것도요. 이후 게임 흐름 이벤트(부활·재시작·홈)에도 같은 격리를 적용했습니다.

</details>

📄 [`GameStateManager.cs`](Assets/Scripts/Managers/GameStateManager.cs)

---

### 4. 에디터에서 재현되지 않던 스레드 버그

보상형 광고를 다 봤는데 부활이 안 되는 버그가 있었습니다. **예외 로그는 나오지 않았고,
에디터에서는 재현되지 않았습니다.**

원인은 광고 SDK 콜백이 Java 백그라운드 스레드에서 올라오는 것이었습니다. 호출부마다
대응하는 대신 콜백을 메인 스레드로 넘기는 계층을 SDK 경계에 두었습니다.

<details>
<summary><b>▸ SDK가 준 도구를 쓰지 않은 이유</b></summary>

<br>

GMA가 제공하는 `MobileAdsEventExecutor`를 쓰는 게 자연스러워 보입니다. 쓰지 않았습니다.

그건 `MobileAds.Initialize()` 과정에서 생성됩니다. 그런데 동의 수집은 **초기화 이전**
단계입니다. 즉 그 도구를 쓰면 동의 수집 콜백이 **아예 실행되지 않습니다.**

UniRx 디스패처는 GMA와 무관하게 동작하므로 그쪽을 썼습니다. 예약 순서가 보존되므로
보상 적립 콜백과 광고 종료 콜백의 선후 관계도 유지됩니다.

</details>

📄 [`AdMainThread.cs`](Assets/Scripts/Ads/AdMainThread.cs)

---

### 5. 출시 후 iOS 광고 노출이 0건이었던 문제

앱인토스 출시 후 광고 지표를 확인하다 발견했습니다. 3일치 실측입니다.

| OS | 광고 요청 | 노출 | 사용자 | **요청/사용자** |
|---|---|---|---|---|
| Android | 79 | 12 | 20 | **3.95** |
| **iOS** | **29** | **0** | 14 | **2.07** |

**요청/사용자가 정확히 2.07에서 멈춘 것**이 단서였습니다. 초기화 때 전면·리워드를 각각 한 번
던지고 그 뒤로 재로드가 전혀 돌지 않았다는 뜻입니다. Android는 3.95로 재로드가 돌고 있었습니다.

<details>
<summary><b>▸ 규약을 어긴 채 한쪽에서만 우연히 동작하고 있었습니다</b></summary>

<br>

`AitAdProvider.Initialize()`가 전면과 리워드를 **동시에** 로드하고 있었습니다.
플랫폼 가이드는 이걸 금지합니다.

> "광고 그룹 ID는 반드시 1개씩 순차적으로 로드해 주세요."
> "광고의 `loaded` 이벤트를 수신한 이후 다음 광고를 로드해 주세요."

동시에 던지면 나중 요청이 앞 요청의 자리를 덮어써 `loaded`가 유실됩니다. 그런데 이 SDK는
**`IsLoaded` 조회를 주지 않아 `loaded` 이벤트로만 준비 여부를 판정합니다.** 이벤트가
유실되면 그 광고는 세션 내내 준비되지 않고, 실패(`err`)가 아니라 **무응답**이라
재시도 경로도 타지 않았습니다(`IsLoading`이 true로 굳음).

**Android만 이 제약이 풀려 있었습니다.** 플랫폼 공지에 "Android 5.267.0부터 복수 광고
인스턴스 관리 개선"이 있고 iOS에는 같은 공지가 없습니다. 즉 **규약을 어긴 채 Android에서만
우연히 동작하던 상태**였고, iOS 출시로 드러난 것입니다.

수정은 로드 레인을 하나로 직렬화하고, 지난 시도의 늦은 콜백이 현재 상태를 덮지 않도록
로드마다 세대 번호를 붙였습니다. 광고가 닫힐 때 나가는 재로드도 같은 레인을 탑니다.

**반증 가능성을 남겨 뒀습니다.** 노출 0건만 놓고 보면 단순 fill 실패일 수도 있습니다.
Android 노출률 15.2%를 그대로 적용하면 29회에서 0회가 나올 확률은 0.8%지만, iOS는 ATT
때문에 fill이 낮은 것이 흔해서 실제 노출률이 1/3이면 22%까지 올라갑니다. 그래서 수정과
함께 **원격에서 실패 지점을 판정할 진단 로그**를 같이 실었습니다.

> ⛔ **iOS 광고는 로컬에서 검증할 방법이 없습니다.** 샌드박스는 인앱 광고를 지원하지 않고,
> 시뮬레이터에는 토스 앱을 설치할 수 없고, 브라우저에서는 광고 API가 동작하지 않습니다.
> 남는 건 iOS 실기기뿐이라, 기기가 없으면 배포 후 콘솔 지표로만 판정해야 합니다.
> 이걸 알아내는 데 시간을 꽤 썼습니다.

</details>

📄 [`AitAdProvider.cs`](Assets/Scripts/Ads/AitAdProvider.cs)

---

## 숫자로 판단한 것

### 6. 난이도를 체감이 아니라 숫자로 확인하기

1인 개발이라 밸런스를 봐줄 사람이 없었습니다. Unity를 켜지 않고 규칙만 재현하는 시뮬레이션을
만들어 **전략별로 1,000판씩** 자동 플레이시켰습니다.

| | 무작위 배치 (하한) | 탐욕 배치 (숙련자 근사) |
|---|---|---|
| 평균 생존 턴 | 16.2 | **32.1** |
| **중앙값 점수** | 5 | **68,010** |
| 평균 점수 | 3,442,950 | 27,431,228 |

여기서 **평균과 중앙값이 5자릿수 넘게 벌어집니다.** 곱셈 연쇄가 한 번 터지면 한 판에 수억
점이 나오기 때문에 평균이 상위 몇 판에 끌려갑니다. 이 게임의 밸런스는 **평균이 아니라
중앙값으로 봐야 한다**는 걸 여기서 알았습니다.

<details>
<summary><b>▸ 전체 실측값과 거기서 읽은 것</b></summary>

<br>

`SimulationBoard` / `SimBlock` / `SimBoardTile`은 `MonoBehaviour`가 아닌 순수 C#이라
렌더링·입력 의존성이 없습니다. EditMode에서 수 초 내에 1,000판이 끝납니다.
재현성을 위해 전역 시드를 시작에 한 번 설정합니다.

타일 확률 생성(`TileValueGenerator`)과 점수 계산(`ExpressionEvaluator`)은
**프로덕션 코드를 그대로 가져다 씁니다.** 시뮬레이션이 실제 게임과 달라지면 나오는 숫자가
틀린 근거가 되기 때문입니다.

| | 무작위 (하한) | 탐욕 (상한 근사) |
|---|---|---|
| 평균 생존 턴 | 16.2 | 32.1 |
| 중앙값 점수 | 5 | 68,010 |
| p10 점수 | 0 | 196 |
| p90 점수 | 71,173 | 26,078,214 |
| 평균 점수 | 3,442,950 | 27,431,228 |
| 최고 점수 | 974,910,860 | 1,111,142,851 |
| 음수 점수로 끝난 판 | 3.7% | 4.1% |
| 종료 시 보드 점유율 | 0.601 | 0.607 |

**평균으로 밸런스를 보면 안 됩니다.** 이걸 몰랐다면 "평균 340만 점"을 기준으로 목표 구간을
잡았을 것이고, 실제 플레이어의 90%가 그 근처도 못 가는 밸런스가 나왔을 것입니다.

**실력이 생존보다 점수에 훨씬 크게 반영됩니다.** 생존 턴은 2배 차이인데 중앙값 점수는 1만
배가 넘습니다. 오래 버티는 것보다 큰 수식을 만드는 게 압도적으로 중요하다는 뜻이고,
초보와 숙련자의 리더보드 격차가 크게 벌어진다는 뜻이기도 합니다.

`GreedyStrategy`는 "숙련자가 이렇게 둔다"의 근사입니다. 정확한 최적해가 아니라
상한의 대용치로 쓰고 있습니다.

</details>

📄 [`Simulation/`](Assets/Tests/EditMode/Simulation/) ·
[`SimulationData/`](Assets/SimulationData/) ·
[전체 문서 →](docs/automated-qa.md)

---

### 7. "게임이 재미없나"를 지표로 분해하기

출시 후 리텐션이 낮아 원인을 찾아야 했습니다. 막연한 질문이라 지표와 실제 화면으로 쪼갰습니다.

**세션 안의 신호는 나쁘지 않았습니다.** 체류 145~245초, 소거를 한 번도 못 한 게임오버 0건,
고의도 유입 코호트의 D1은 퍼즐 장르 평균권이었습니다. D1이 0%인 쪽은 전부 저의도 유입이었습니다.

그래서 결론을 이렇게 냈습니다 — **재미가 없는 게 아니라, 재미가 화면에서 안 보인다.**

<details>
<summary><b>▸ 계측이 실제로 바꾼 결정 세 가지</b></summary>

<br>

#### ① 차별점이 화면에 안 보였습니다

세션 복원 API로 **중반 판을 주입한 뒤 스크린샷을 찍어** 실제 화면을 확인했습니다.
숫자 · 연산자 · 공백 타일이 **전부 같은 색**이었습니다. 수식 게임인데 수식이 보이지 않는
상태였습니다.

원인이 뜻밖이었습니다. **색 정의는 이미 있었습니다.** `TileColorMapper`와 팔레트의
`blockAdd/Sub/Mul/Div` 필드가 정의만 되고 **뷰에 연결돼 있지 않았습니다**
(주석 처리된 호출 흔적까지 남아 있었습니다).

연결한 뒤에도 `+`만 구분이 안 됐는데, 팔레트의 `blockAdd`(#0096B8)가 기본 타일색(#0088B0)과
사실상 같은 색이었기 때문입니다. **CTColors 상수와 팔레트 값이 다르고, 실제 렌더는 팔레트가
이깁니다.** 그린(#16A15B)으로 조정해 해결했습니다.

동일한 판을 주입해 전/후를 같은 조건에서 비교했습니다. 이 촬영 방식은 마케팅 스크린샷에도
그대로 씁니다.

#### ② 튜토리얼 강제 팝업을 지웠습니다

콘솔 실측(8/19~25)에서 `tut_step_01` 8회 → `tut_step_02` 4회인데 **`tut_close`가 0건**
이었습니다. X를 눌러 닫은 게 아니라 **앱째로 이탈했다**는 뜻입니다. 12장을 완주한 쪽도
다음 날 복귀가 없었습니다.

즉 강제 팝업은 이탈만 만들고 리텐션에 기여하지 못했습니다. 그래서 제거했습니다.

**대체 연출은 일부러 넣지 않았습니다.** 어중간한 반쪽 튜토리얼보다 게임 문법에 맡기는 쪽을
택했고, 수동 열람 경로(타이틀의 도움말 버튼)만 남겼습니다.

> 여기서 배운 건 **"이벤트가 0건인 것"도 데이터**라는 점입니다. `tut_close` 0건이
> 없었다면 "튜토리얼을 보다 닫았다"로 읽고 내용을 고치려 들었을 것입니다.

#### ③ 만들어 둔 알림 기능을 되돌렸습니다

주간 푸시 알림 연동을 구현해 두고 **되돌렸습니다(revert).**

돌아올 이유가 되는 게임 상태 변화(예: 하트 회복)가 없는 상태에서 정기 푸시는 빈
리마인더고, 구독 요청은 유저의 순간만 씁니다. **리텐션은 알림이 아니라 돌아올 이유를
만드는 쪽에서 풀어야 한다**고 판단했습니다.

되돌리면서 커밋 해시를 남겨, 데일리 챌린지처럼 알릴 거리가 생기면 되살릴 수 있게 했습니다.

#### 그리고 다음 지표를 심었습니다

`retry / (game_over_clear + game_over_noclear)` — **다시하기율**입니다.
낮으면 코어 재미 문제, 높은데 다음 날 복귀가 낮으면 재방문 훅 문제로 **가설이 갈라지도록**
설계한 지표입니다. 게임오버 화면의 재시작만 세고(일시정지 재시작 제외), 판 상태가
리셋되기 전에 기록해 복원된 판을 제외합니다.

</details>

📄 [`Telemetry.cs`](Assets/Scripts/Core/Telemetry.cs) ·
[`TileColorMapper.cs`](Assets/Scripts/IG/View/TileColorMapper.cs)

---

## QA를 어떻게 하는가

Unity Test Runner로 돌립니다. 총 **88개 케이스**입니다.

자동 플레이 봇은 별도 실행 도구가 아니라 **테스트 러너 안에 들어가 있습니다.**
별도 도구로 두면 바쁠 때 건너뛰게 되어서, 테스트를 돌리면 같이 돌아가도록 했습니다.

### 1. EditMode — 로직 단위

| 테스트 | 케이스 | 대상 |
|---|---|---|
| [`ExpressionEvaluatorTests`](Assets/Tests/EditMode/ExpressionEvaluatorTests.cs) | **79** | 수식 정규화 · 계산 · 인덱스 추적 |
| [`LevelDesignSimulation`](Assets/Tests/EditMode/LevelDesignSimulation.cs) | 2 | 시뮬레이션 실행 + CSV 생성 |

Unity 실행 없이 도는 구간입니다. 그래서 빠르고, 그래서 자주 돌립니다.

### 2. PlayMode — 실제 씬에서

| 테스트 | 케이스 | 확인하는 것 |
|---|---|---|
| [`SmokeTests`](Assets/Tests/PlayMode/SmokeTests.cs) | 2 | 씬 로드, 매니저 싱글톤 초기화 |
| [`GameplayTests`](Assets/Tests/PlayMode/GameplayTests.cs) | 2 | 블록 3개 생성, 보드 모델 초기화 |

`PlayModeTestBase`가 **TitleScene → IGScene 순서로** 로드합니다.
`UIManager`, `HUDView` 같은 `DontDestroyOnLoad` 객체가 TitleScene에서 생성되기 때문에,
IGScene만 띄우면 실제와 다른 상태가 됩니다. **테스트 환경이 실제 실행 순서와 같아야 합니다.**

### 3. 자동 플레이 봇 — 스트레스

[`AutoPlayBotStressTests`](Assets/QA/AutoPlayBot/Tests/AutoPlayBotStressTests.cs) 3개
케이스가 봇에게 **10게임을 연속으로** 시키면서 세 가지를 봅니다.

| 검사 | 임계값 |
|---|---|
| `NoUnhandledExceptions` | 10게임 동안 미처리 예외 0건 (게임당 최대 2,000배치) |
| `NoMemoryLeak` | 첫 게임 대비 마지막 게임 종료 메모리 증가율 **50% 이내** |
| `StaysWithinFrameBudget` | 배치 1회가 **16.67ms(60fps) 이내**, JIT 워밍업 10회 제외 |

5분 수동 플레이로는 200턴 뒤에 터지는 풀 반환 누락이나 구독 해제 누락을 잡을 수 없어서
만든 것입니다.

<details>
<summary><b>▸ 봇에게 쓰기 권한을 주지 않은 이유</b></summary>

<br>

`IReadOnlyBoardState`로 **읽기 전용 보드만 노출**합니다.

봇이 보드를 직접 조작할 수 있으면 테스트 작성은 훨씬 쉬워집니다. "이 상황을 만들고 싶으니
타일을 직접 세팅하자"가 가능해지기 때문입니다.

그러면 **프로덕션 코드가 실제로 도달할 수 없는 상태에서 통과한 결과**가 됩니다.
봇이 게임을 플레이해서 도달한 상태여야 의미가 있다고 보고, 인터페이스로 막아 두었습니다.

배치 전략은 `IPlacementStrategy`로 주입받습니다. 러너와 전략이 분리되어 있어 전략을 추가할
때 러너를 건드릴 일이 없습니다.

</details>

### 4. 실기기 — 에디터에서 재현되지 않는 것들

광고 콜백 스레드 문제도, 세이브 파일 교체도 **에디터에서는 드러나지 않았습니다.**
그래서 목적별로 빌드를 나눠 두었습니다.

| 빌드 메뉴 | 용도 |
|---|---|
| `Android Dev APK` | 개발 확인. 테스트 광고 + 봇 어셈블리 포함 |
| `Android Consent-Test APK` | **GDPR 동의 폼 검증.** `DEBUG_CONSENT`로 EEA 지역을 시뮬레이션 |
| `Android Release APK` | 릴리스 동작 확인 |
| `Android Store AAB` | 스토어 업로드. 릴리스 keystore 서명 |

동의 폼은 EEA 지역에서만 뜨므로 국내에서는 확인할 방법이 없습니다.
그래서 **지역을 시뮬레이션하는 전용 빌드**를 따로 만들었습니다.

<details>
<summary><b>▸ 디버그 심볼이 스토어로 새는 것을 빌드 실패로 막습니다</b></summary>

<br>

빌드 환경 전환은 `PlayerSettings`에 심볼을 **영구 기록**합니다. Dev로 바꾼 뒤 Release로
되돌리는 걸 잊으면 그대로 스토어 AAB가 나갑니다. 결과가 특히 나쁩니다.

| 심볼 | 새어 나갔을 때 |
|---|---|
| `DEBUG_ADS` | 광고 단위가 테스트용으로 바뀌어 **전 사용자 광고 수익이 0** (Play·앱인토스 양쪽) |
| `DEBUG_CONSENT` | EEA가 시뮬레이션되어 동의 상태가 매 실행 초기화 |
| `IG_GAMELOOP_BUILD` | QA 자동플레이 봇 어셈블리가 릴리스에 실림 |

[`StoreBuildGuard`](Assets/Editor/StoreBuildGuard.cs)가 릴리스 타깃 빌드 전처리에서 이
심볼들을 검사하고 **빌드를 실패시킵니다.** 무거운 작업이 시작되기 전에 걸리도록
콜백 순서를 앞쪽에 두었습니다.

```
/// 안전장치를 사람의 기억에 맡기면 언젠가 반드시 뚫리므로 빌드를 실패시킨다.
```

스토어 빌드는 릴리스 keystore와 alias 비밀번호가 비어 있어도 실패합니다.
디버그 키로 서명된 AAB가 올라가는 것을 막기 위해서입니다.

</details>

> **테스트가 88개 중 79개가 한 클래스에 몰려 있습니다.** 우연이 아니라 싱글톤 결합의
> 결과이고, 아래 약점 항목에 그대로 적었습니다.

---

## 아키텍처

MVC + Manager 구조입니다. 싱글톤은 13개이고, 생명주기 3단계를 갖는 `ManagerBase<T>` 계열
6개와 상태만 들고 있는 `SingletonClass<T>` 계열 7개로 나뉩니다.

Model은 View의 존재를 모릅니다. 변경을 UniRx `IObservable`로 방출할 뿐이고, View가 그것을
구독합니다.

<details>
<summary><b>▸ 레이어 구성과 데이터 흐름</b></summary>

<br>

| 레이어 | 역할 | 예시 |
|---|---|---|
| **Model** | 데이터 · 상태 | `IGBoardModel`, `IGBlockModel`, `ScoreModel` |
| **View** | 시각적 표현 | `IGBoardView`, `HUDView` *(발췌본 미포함)* |
| **Controller** | 게임 로직 | `IGGameController`, `IGBoardController`, `IGInputController` |
| **Manager** | 시스템 기능 | `PoolManager`, `SaveManager`, `AdManager` |

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

**스트림을 목적별로 쪼갠 것이 핵심입니다.** `OnScoreChanged`(현재 점수)와
`OnScoreAdded`(증가분)는 같은 사건에서 나오지만 소비처가 다릅니다 — 전자는 HUD 숫자,
후자는 획득 연출입니다. 하나로 합치면 모든 구독자가 "지금 뭐가 바뀐 건지"를 매번 계산해야
합니다.

**타일 값 생성**은 단순 무작위가 아니라 진행도에 따라 확률이 변합니다. `TilePhaseProfile`이
페이즈별 가중치를 갖고, `TileProbabilityResolver`가 페이즈 사이를 **보간**합니다.
난이도가 계단이 아니라 연속적으로 변하게 하기 위해서입니다.

</details>

📄 [전체 문서 →](docs/architecture.md)

---

## 출시까지 직접 한 것

| 영역 | 내용 |
|---|---|
| **수익화** | AdMob 전면 / 보상형(부활) 광고, 노출 빈도 제한 정책 |
| **개인정보 규제** | UMP 동의 수집(GDPR), 동의 철회 시 로드된 광고 파기 |
| **세이브 무결성** | HMAC 서명 검증, 변조·되감기 방어 ([문서](docs/save-integrity.md)) |
| **안정성** | Firebase Crashlytics, 네이티브 디버그 심볼 자동 생성 |
| **계정 · 진행도** | Google Play Games Services 로그인 · 리더보드 |
| **디바이스 대응** | Safe Area 정렬 (노치 · 홀펀치) |
| **배포** | 목적별 빌드 4종, 스토어 빌드 가드 |
| **행정** | 개인사업자 등록, 게임제작업 등록, 스토어 심사, 개인정보처리방침 한/영 |

---

## 지금 알고 있는 약점

포트폴리오 목적의 저장소이므로 현재 코드의 문제도 그대로 적습니다.

1. **싱글톤 결합** — `.Instance` 호출이 179군데라 단위 테스트에서 갈아끼울 수 없습니다.
   테스트가 수식 엔진 한 곳에 몰린 것도 이 때문입니다. **가장 우선순위가 높은 항목입니다.**
2. **`GameStateManager`는 상태 머신이 아닙니다** — 전이 유효성 검사가 없어
   `GameOver → Playing` 같은 전이가 코드로 막혀 있지 않습니다.
3. **Controller 비대화** — `IGGameController` 681 LOC. 입력·상태·뷰·매니저 호출이 한 곳에 있습니다.
4. **설정값 하드코딩** — 광고 ID 등 환경 의존 값이 상수로 박혀 있습니다.
5. **CI를 운영하다 폐기했습니다** — GitHub Actions로 2개월 넘게 Android 빌드를 돌렸지만
   지금은 로컬 빌드만 씁니다. 없앤 이유는 아래에 적었습니다.

<details>
<summary><b>▸ 왜 1번이 병목인가, 그리고 개선 순서</b></summary>

<br>

`.Instance` 호출 상위 파일입니다.

| 파일 | 호출 수 |
|---|---|
| `IGGameController.cs` | 32 |
| `MainPanel.cs` | 19 |
| `PauseView.cs` | 14 |
| `GameOverPopup.cs` | 13 |

이 부채가 무엇을 막고 있는지는 **테스트 분포로 드러납니다.** EditMode 테스트 79개가 전부
`ExpressionEvaluator` — 정적 클래스이고 의존성이 없는 유일한 핵심 로직 — 에 몰려 있습니다.
다른 걸 테스트하려면 씬을 띄워야 하고, 그래서 `PlayModeTestBase`가 존재합니다.

전면 리팩터링부터 하지 않는 이유는, 주입으로 바꿔도 테스트가 못 붙는 지점이 있을 수
있고 그걸 모른 채 179군데를 다 고치면 되돌리기 어렵기 때문입니다.

```
1단계  IGGameController 부터 의존성을 초기화 파라미터로 노출
2단계  그 상태로 EditMode 테스트를 붙일 수 있는지 확인 (실효성 검증)
3단계  효과가 확인되면 Controller → View 순으로 확대
```

**개선 순서**

```
지금 할 수 있는 것 (부채와 무관)
  #2 상태 전이 테이블  ← 가장 비용 대비 효과가 크다

1번을 풀어야 열리는 것
  #3 Controller 분리 · 테스트 커버리지 확대 전반

여유가 생기면
  #6 시뮬레이션 규칙 공유   #7 파이프라인 일원화
  #4 설정 외부화            #8 구독 관리 베이스
```

전체 8개 항목과 각각의 근거는 [known-limitations.md](docs/known-limitations.md)에 있습니다.

</details>

<details>
<summary><b>▸ CI를 왜 없앴는가</b></summary>

<br>

2026년 5월부터 8월까지 GitHub Actions로 Android 빌드를 돌렸습니다. 러너 디스크 정리,
키스토어 주입, APK 추출 경로까지 손봐 가며 30번 넘게 워크플로를 고쳤습니다.

없앤 이유는 **CI가 다른 개선을 막고 있었기 때문**입니다.

`game-ci`는 프로젝트의 `BuildScript`가 아니라 **자체 빌더**를 씁니다. 그래서
`ProjectSettings.asset`이 유일한 설정 소스여야 했고, Unity 6의 **Build Profiles**로
플랫폼별 설정을 분리하는 작업이 막혀 있었습니다. 앱인토스(WebGL) 포팅에서 SDK가 전역
설정을 바꾸는 문제(`stripEngineCode`, `runInBackground`)를 만나면서 이 분리가 필요해졌고,
CI를 접는 쪽을 택했습니다.

곁가지로 하나 더 정리됐습니다. `StoreBuildGuard`에는 **CI 우회로**(`-allowDebugDefines`)가
있었습니다. 비-Development 릴리스 빌드에 디버그 심볼을 의도적으로 켜던 CI 잡이 유일한
사용처였는데, CI가 없어지면서 **가드를 끌 수 있는 경로가 하나 사라졌습니다.**

지금 빌드 경로는 `Window/Build` 메뉴와 `BuildScript`의 배치모드 호출 둘뿐입니다.
1인 프로젝트에서 CI 유지비가 그 이득보다 컸다는 판단이고, 인원이 늘면 다시 붙일 자리입니다.

</details>

---

## AI 도구를 어떻게 썼는가

구현의 상당 부분을 Claude Code와 Cursor로 진행했습니다. 감출 이유가 없다고 판단해 적어둡니다.

구현은 AI 도구로 했고, 그 결과물이 실기기에서 맞게 도는지 확인하는 일은 직접 했습니다.
위의 문제들(광고 콜백 스레드, 세이브 교체)은 **모두 AI가 짚어주지 않았고 실기기에서
드러났습니다.** 수식 엔진에 테스트 79개를 붙인 것도 생성된 코드를 신뢰할 근거가 필요했기
때문입니다.

싱글톤 179군데는 초기에 속도를 얻으려고 넘어간 결과입니다.
**생성 속도가 빠를수록 구조 결정을 미루기 쉽다**는 점이 이 프로젝트에서 가장 크게 배운
부분입니다.

---

## 문서

| 문서 | 내용 |
|---|---|
| [expression-normalization.md](docs/expression-normalization.md) | 4단계 정규화, 순서 제약, 테스트 79개 구성 |
| [multiplatform-port.md](docs/multiplatform-port.md) | Android → WebGL 포팅, 인터페이스 경계 |
| [automated-qa.md](docs/automated-qa.md) | 봇과 시뮬레이션, 1,000판 실측 결과 |
| [save-integrity.md](docs/save-integrity.md) | 세이브 무결성 검증 설계 |
| [architecture.md](docs/architecture.md) | MVC + Manager 구성, UniRx 흐름 |
| [known-limitations.md](docs/known-limitations.md) | 한계 8개와 개선 순서 |

---

<div align="center">

**박광규** · Unity Client Programmer

[Portfolio](https://sincere-afternoon-a0f.notion.site/Client-Programmer-326ecdddc215803a8a89ffb87ac21f98) · [Blog](https://gverse-dev.tistory.com) · [GitHub](https://github.com/GVerse-Dev)

</div>
