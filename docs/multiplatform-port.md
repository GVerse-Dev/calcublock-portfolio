# Android → WebGL(앱인토스) 포팅

> 📄 [`Ads/`](../Assets/Scripts/Ads/) · [`SignIn/`](../Assets/Scripts/SignIn/) ·
> [`Storage/`](../Assets/Scripts/Storage/) · [`Core/SafeAreaSource.cs`](../Assets/Scripts/Core/SafeAreaSource.cs)

---

## 배경

Google Play 출시 후, **앱인토스(Apps in Toss)** — 토스 앱 안에서 도는 미니앱 플랫폼 —
으로 포팅을 시작했습니다. Unity WebGL 빌드가 토스 웹뷰에서 실행되는 구조입니다.

문제는 게임 로직 바깥이 전부 다르다는 것이었습니다.

| | Android | 앱인토스 (WebGL) |
|---|---|---|
| 광고 | Google Mobile Ads + UMP 동의 | AIT 광고 SDK (동의 개념 없음) |
| 로그인 | Google Play Games Services | 토스 로그인 |
| 리더보드 | GPGS 리더보드 | AIT 리더보드 |
| 세이브 | `persistentDataPath` 파일 | 웹뷰 IndexedDB (+ 네이티브 미러) |
| 세이프 에어리어 | `Screen.safeArea` | 토스가 그리는 X 버튼 좌표 |
| 지표 | Firebase Analytics | 앱인토스 콘솔 |
| 스레드 | SDK 콜백이 Java 백그라운드 스레드 | 단일 스레드 |

---

## 원칙: 분기는 생성 지점 한 곳에만

`#if UNITY_WEBGL`을 게임 코드 곳곳에 뿌리면 두 플랫폼이 서로를 오염시킵니다.
어느 쪽을 고쳐도 다른 쪽이 깨질 수 있고, 그걸 확인하려면 매번 양쪽을 빌드해야 합니다.

그래서 **경계마다 인터페이스를 긋고, 플랫폼 분기는 구현체를 고르는 지점에만
남겼습니다.**

```csharp
// AdManager.cs — 게임 전체에서 광고 SDK를 고르는 분기는 여기가 전부다
#if UNITY_WEBGL
    _provider = new AitAdProvider();
#else
    _provider = new AdMobProvider();
#endif
```

| 경계 | 인터페이스 | Android | WebGL |
|---|---|---|---|
| 광고 | [`IAdProvider`](../Assets/Scripts/Ads/IAdProvider.cs) | `AdMobProvider` | `AitAdProvider` |
| 로그인 | [`ISignInService`](../Assets/Scripts/SignIn/ISignInService.cs) | `GpgsSignInService` | `AitSignInService` |
| 세이프에어리어 | [`SafeAreaSource`](../Assets/Scripts/Core/SafeAreaSource.cs) | `Screen.safeArea` | `AitSafeAreaProvider` |
| 세이브 | [`LocalStorage`](../Assets/Scripts/Storage/LocalStorage.cs) | 파일 | + `AitSaveMirror` |

`ISignInService`에는 구현이 하나 더 있습니다 —
[`NullSignInService`](../Assets/Scripts/SignIn/NullSignInService.cs).
로그인이 없는 환경에서 **호출부가 null 검사를 하지 않아도 되게** 하려는 것입니다.
"로그인 없음"을 `null`로 표현하면 모든 호출부에 분기가 하나씩 생깁니다.

---

## 인터페이스 주석에 계약을 적은 이유

구현체가 하나일 때는 인터페이스 주석이 사치처럼 느껴집니다. 둘이 되는 순간 달라집니다.
같은 메서드가 구현마다 다른 의미를 갖기 시작하는데, 그건 호출부에서는 보이지 않습니다.

`IAdProvider.ShowInterstitial`의 `onClosed(bool shown)`가 그런 자리였습니다.

```csharp
/// onClosed(shown) 의 shown 은 **광고가 실제로 화면에 노출된 뒤 닫혔는지**를 뜻한다.
/// 노출 실패(OnAdFullScreenContentFailed), 미동의, 광고 미준비 같은 경로는 게임 흐름을
/// 막지 않기 위해 콜백을 즉시 돌려주지만 shown=false 다.
///
/// 이 구분이 필요한 이유: 호출자(AdGatePolicy)가 노출 횟수를 디스크에 영속화하므로,
/// 안 띄운 것을 띄웠다고 기록하면 오염이 세션을 넘어 남는다.
```

그래서 "무엇을 뜻하는가"와 "왜 그 구분이 필요한가"를 같이 적었습니다. 이유를 안 적어두면
두 번째 구현을 쓸 때 "광고 호출이 끝났으니 true"로 채우기 쉽고, 그러면 빈도 제한 상태가
조용히 오염됩니다. 버그는 몇 세션 뒤에 "광고가 너무 안 나온다"로 나타나 추적이 어렵습니다.

같은 이유로 `DiscardLoadedAds()`에도 존재 이유를 적었습니다 — 사용자가 세션 중 동의를
철회했을 때 **이미 로드된 광고를 파기하지 않으면 그 세션 내내 계속 노출됩니다.**

---

## 같은 인터페이스라도 SDK마다 지켜야 할 규약이 다릅니다

`AitAdProvider`를 쓰면서 발견한 차이들입니다. AdMob과 인터페이스는 같지만 **내부에서
지켜야 할 규약이 다릅니다.**

| | AdMob | 앱인토스 |
|---|---|---|
| 동의 | UMP로 직접 수집 | **없음** — 토스 앱이 관리 |
| 준비 여부 | SDK가 `IsLoaded` 제공 | **조회 API 없음** → `loaded` 이벤트로 직접 상태 보유 |
| 노출 후 | 재로드 권장 | **소비됨** — `dismissed` 후 반드시 재로드 (규약) |
| 구독 해제 | — | 콜백 API가 해제용 `Action` 반환, **반드시 호출** |
| 스레드 | Java 백그라운드 → 마셜링 필요 | 단일 스레드 → 불필요 |

동의 항목이 특히 함정이었습니다. WebGL 스텁의 `ConsentManager.CanRequestAds`는 `false`를
돌려줍니다. Android 코드를 그대로 두면 **광고가 아예 나가지 않습니다.** 그래서
`AitAdProvider`는 `ConsentManager`를 보지 않고, 그 이유를 클래스 주석에 남겼습니다.
이유가 없으면 나중에 "동의 확인이 빠졌네" 하고 되돌리기 쉬운 자리라서입니다.

**에디터·일반 브라우저에서는 광고가 렌더되지 않습니다**(토스 앱 또는 샌드박스 안에서만
뜹니다). 그 환경에서 이 클래스는 항상 "준비 안 됨"으로 동작해 **게임 흐름을 그대로
통과시킵니다.** 개발 중에 광고 때문에 막히지 않게 하려는 의도적 설계입니다.

---

## 세이프 에어리어 — 피해야 할 것이 노치가 아니었습니다

Android에서는 `Screen.safeArea`가 곧 정답이라 고민할 게 없습니다.
WebGL에서는 두 가지가 동시에 어긋납니다.

1. 브라우저는 노치·펀치홀 인셋을 `Screen.safeArea`로 알려주지 않습니다. **화면 전체가
   나옵니다.**
2. 그리고 정작 피해야 할 것은 기기 노치가 아니라 **토스가 그리는 우상단 X 버튼**입니다.
   그건 토스만 아는 좌표라 AIT API로 받아야 합니다.

그래서 인셋의 **출처를 한 겹 분리**했습니다. 소비처(`SafeAreaHandler`,
`SafeAreaCameraAligner`, `SafeAreaEdgeOffset`)는 `SafeAreaSource.Current`만 읽습니다.

```csharp
public static Rect Current => _hasOverride ? _override : Screen.safeArea;
```

덮어쓴 값이 없으면 **기존과 완전히 동일하게 동작합니다.** Android 경로에 위험을 만들지 않고
WebGL만 추가하는 방식입니다.

그리고 `SetOverride`는 **별도의 알림을 쏘지 않습니다.** 이미 `ScreenChangeWatcher`가 이 값을
감시하고 있어서, 다음 프레임에 기존 구독자들이 그대로 갱신을 받습니다.
**통지 경로를 둘로 두면 순서와 중복을 관리해야 하므로 하나로 유지했습니다.**

---

## 세이브 — 웹뷰 저장소는 언제 비워질지 모릅니다

WebGL의 저장 수단은 사실상 웹뷰의 IndexedDB 하나입니다. 파일도 `PlayerPrefs`도 전부 거기에
있습니다. 그리고 **토스 웹뷰가 그걸 언제 비울지 보장이 없습니다.** 비워지면 최고 점수가
0으로 돌아갑니다. 플랫폼 심사 요건에도 "재접속 후 플레이 기록 유지"가 있습니다.

그래서 세이브를 토스 **네이티브 앱의 로컬 저장소**에 복제합니다
([`AitSaveMirror`](../Assets/Scripts/Storage/AitSaveMirror.cs)).
웹뷰 저장소와 수명주기가 달라서, 웹뷰 데이터가 정리돼도 이쪽은 남습니다.

다만 이건 서버 저장이 아닙니다 — SDK에 서버 저장 API가 없습니다. 따라서
**기기 변경·토스 앱 재설치는 이것으로 막지 못합니다.** 한계를 함께 적어 둡니다.

### 서명본이 아니라 payload를 복제한 이유

세이브에는 변조 탐지를 위한 HMAC 서명이 붙습니다([save-integrity.md](save-integrity.md)).
그러면 서명된 봉투를 통째로 복제하는 게 자연스러워 보이는데, 그렇게 하면 백업이
무용지물이 됩니다.

```
HMAC 키는 PlayerPrefs 에 있다.
WebGL 에서 PlayerPrefs 는 세이브 파일과 같은 IndexedDB 에 산다.
  → 우리가 막으려는 그 사건(웹뷰 데이터 정리)이 일어나면 키도 함께 사라진다.
  → 서명본을 복원해도 키가 달라 검증이 반드시 실패한다.
  → 백업이 100% 무용지물이 된다.
```

그래서 **서명 이전의 payload**를 복제하고, 복원한 값은 현재 키로 다시 서명해서 로컬에
씁니다. 복원값을 무조건 믿는 것도 아닙니다 — `SaveManager`가 파일에서 읽은 것과 똑같이
클램프와 워터마크 검사를 태웁니다.

진행 중이던 판(`sessionData`)은 **복제하지 않습니다.** 되감기 방어가 `PlayerPrefs`의
일련번호에 묶여 있어 복제·복원이 그 방어와 얽히기 때문입니다. 심사 요건도 "기록 유지"라
최고 점수와 누적 통계면 충분해서, 범위를 여기까지로 정했습니다.

타임아웃은 무기한 대기(0)를 쓰지 않고 10초를 줍니다. **응답이 오지 않아도 게임은 계속돼야
합니다.**

---

## 계측 — 빌드에 남지 않게 하기

앱인토스 콘솔로 나가는 계측은 Play 빌드에 들어가면 안 됩니다.
모든 진입점에 `[Conditional("UNITY_WEBGL")]`을 달았습니다.

이 특성은 메서드 본문이 아니라 **호출부를** 컴파일 단계에서 지웁니다. 따라서 Android 빌드의
IL에는 호출이 아예 남지 않습니다. 계측용 필드도 게임 코드에 만들지 않았습니다 — 상태는 전부
`Telemetry` 클래스가 갖습니다.

> ⚠️ **함정.** `Conditional` 규약상 **인자 식도 평가되지 않습니다.** 인자 자리에 부수효과가
> 있는 식을 두면 Android에서 조용히 실행되지 않습니다. 이 경고를 클래스 주석에 박아
> 두었습니다.

콘솔 API의 제약도 설계에 영향을 줬습니다. 콘솔은 로그별 **일자별 카운트**와 파라미터
**이름**만 돌려주고 **값의 분포는 주지 않습니다.** 그래서 분포가 필요한 축(턴 수·튜토리얼
스텝)은 **로그 이름에 버킷으로 구웠습니다** (`tut_step_03`, `ad_{stage}_{slot}_{os}` 같은
식). 로그 이름은 개수 제한이 없고 이름마다 카운트가 나오기 때문입니다.

도구가 주는 것에 맞춰 데이터 모양을 정한 셈이고, 그 이유를 주석에 남겼습니다.

---

## 현재 상태

포팅은 **진행 중**입니다. 광고 · 로그인 · 리더보드 · 세이브 · 세이프에어리어 · 계측의
추상화와 WebGL 구현이 들어가 있고, 플랫폼 심사 · 정산 등 행정 트랙이 남아 있습니다.

이 저장소는 코드 발췌본이라 빌드 파이프라인(WebGL 번들 · 로더 · 정적 서버)은 포함하지
않았습니다. 추상화 경계가 어떻게 그어졌는지를 보는 용도입니다.
