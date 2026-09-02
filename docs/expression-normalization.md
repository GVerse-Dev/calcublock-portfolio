# 수식 정규화 파이프라인

> 📄 [`ExpressionEvaluator.cs`](../Assets/Scripts/IG/Data/ExpressionEvaluator.cs) ·
> [`ExpressionEvaluatorTests.cs`](../Assets/Tests/EditMode/ExpressionEvaluatorTests.cs)

---

## 문제

이 게임에서 점수는 **지워진 타일들이 만든 수식의 계산 결과**입니다.
그런데 타일이 지워지는 순서는 플레이어의 배치가 결정합니다. 게임은 "말이 되는 수식"을
보장해주지 않습니다. 실제로 들어오는 입력은 이런 것들입니다.

| 입력 | 문제 |
|---|---|
| `7 8` | 연산자 없이 숫자만 이어짐 |
| `+ + 5 −` | 연산자 연속, 양끝이 연산자 |
| `3 / 0` | 0으로 나누기 → 크래시 |
| `3 / * 0` | 위 두 문제가 겹침 |
| `+` | 숫자가 하나도 없음 |

"잘못된 입력이니 0점"으로 처리하면 게임이 성립하지 않습니다. 플레이어는 규칙대로 지웠을
뿐인데 점수가 안 나오는 것이기 때문입니다. **어떻게든 의미 있는 수식으로 복원해야 합니다.**

---

## 판단

정규화 로직을 계산 함수 안에 `if`로 흩뿌리면, 새 예외 케이스가 나올 때마다 조건이 하나씩
늘고 서로의 상호작용을 아무도 추적하지 못하게 됩니다.

그래서 **순서가 고정된 4단계 파이프라인**으로 분리했습니다.

```csharp
var tokens = Tokenize(expression);
tokens = CollapseConsecutiveOperators(tokens);
tokens = RemoveDivisionByZero(tokens);
tokens = TrimEdgeOperators(tokens);
```

| 단계 | 처리 | 예시 |
|---|---|---|
| 1. `Tokenize` | 연속된 숫자 타일을 하나의 수로 병합. 두 숫자 사이 공백은 `+`로 승격 | `1 2` → `12` / `7 _ 8` → `7 + 8` |
| 2. `CollapseConsecutiveOperators` | 연속된 연산 기호 중 첫 번째만 남김 | `+ + 5` → `+ 5` |
| 3. `RemoveDivisionByZero` | `÷` 뒤에 `0`이 오면 **두 토큰을 함께** 제거 | `6 / 0` → `6` |
| 4. `TrimEdgeOperators` | 수식 앞뒤에 남은 연산 기호 제거 | `+ 5 −` → `5` |

계산은 중위 → 후위(shunting-yard) 변환 후 스택 평가입니다. `×÷ > +−` 우선순위를 지키고,
결과는 `Math.Floor` 후 `long`으로 반환합니다. **음수 결과를 허용합니다** — 점수 차감이 게임
설계의 일부이기 때문입니다.

---

## 이 설계의 핵심: 순서가 정확성 조건입니다

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

`Collapse`가 **새로운 `/0` 쌍을 만들어낼 수 있다**는 것이 함정입니다. 따라서 `/0` 검사는
축약 **이후**에 와야 합니다. 정리 순서의 문제가 아니라 **바꾸면 크래시가 나는 조건**입니다.

그래서 이 순서 의존성을 반례와 함께 소스 상단 주석에 적어 두었습니다.

```csharp
/// ※ Collapse를 RemoveDivisionByZero 이전에 실행해야 한다.
///   "3/*0" 같이 연산자가 연속된 경우, Collapse 전에 RemoveDivByZero를 하면
///   /0 패턴을 못 잡고(뒤가 *이므로), Collapse 후 새로 생긴 /0을 처리 못한 채
///   3/0을 평가해버린다.
```

주석이 없으면 나중에 이 코드를 다시 볼 때 "단계 순서는 상관없겠지" 하고 바꾸기 쉽습니다.
그래서 무엇을 하는지가 아니라 **왜 이 순서여야 하는지**를 적었습니다.

그리고 주석만으로는 막을 수 없으므로 **회귀 테스트로 고정했습니다.**

```csharp
[Test]  // Regression_NormalizationOrder_DivOpZero
public void Regression_NormalizationOrder_DivOpZero(string expression, long expected)
```

테스트 이름 자체가 무엇을 지키는지 말하게 했습니다. 이 테스트가 깨지면 원인이 바로 보입니다.

---

## `0` 처리에서 곱셈과 나눗셈을 구분한 것

`RemoveDivisionByZero`는 **나눗셈만** 건드립니다. 곱셈의 `0`은 그대로 둡니다.

| 입력 | 결과 | 이유 |
|---|---|---|
| `6 / 0` | `6` | 정의되지 않음 → 연산자와 0을 함께 제거 |
| `6 * 0` | `0` | 수학적으로 유효 → 그대로 계산 |

`× 0`을 함께 제거해버리면 **"곱셈으로 점수를 키우려다 0 타일을 밟아서 날렸다"는 게임적
긴장이 사라집니다.** 0 타일은 플레이어가 피해야 할 위험 요소로 설계한 것이므로, 기술적으로
처리 가능하다고 해서 편의상 제거하면 설계 의도가 깨집니다.

테스트에서도 이 둘을 별도 케이스로 분리했습니다
(`DivisionByZero_OnlyOperatorAndZeroAreRemoved` / `MultiplicationByZero_IsPreserved`).

---

## `EvaluateWithTracking` — 연출을 위한 두 번째 API

정규화는 토큰을 **버립니다.** 그런데 화면에서는 "수식에 참여한 타일"과 "정규화로 제거된
타일"을 다르게 연출해야 합니다. 지워지는 건 같지만 점수에 기여한 것은 일부이기 때문입니다.

그래서 같은 파이프라인의 **인덱스 추적 버전**을 두었습니다.

```csharp
public static (long score, HashSet<int> includedIndices)
    EvaluateWithTracking(IReadOnlyList<string> tileValues)
```

각 토큰이 원본 타일 인덱스 목록(`SourceIndices`)을 들고 다니므로, 여러 타일이 병합된
다자리 수(`1`+`2` → `12`)도 **구성 타일 전부**가 포함으로 잡힙니다.

> **비용도 적어둡니다.** 파이프라인이 사실상 두 벌(일반 / 인덱스 추적)이라 정규화 규칙을
> 바꾸면 양쪽을 함께 고쳐야 합니다. 지금은 규칙이 안정적이라 감수하고 있지만, 규칙이 더
> 늘어나면 인덱스 추적 버전으로 일원화하고, 일반 버전은 그 위의 얇은 래퍼로 만들 생각입니다.

---

## 테스트 79개의 구성

한 클래스에 79개는 많아 보이지만, **게임 점수 로직이 통째로 여기 걸려 있습니다.**
여기가 조용히 틀리면 플레이어는 점수가 이상하다는 것만 알고 원인은 아무도 모릅니다.

케이스는 경계값 중심으로 관심사별로 나눴습니다.

| 그룹 | 확인하는 것 |
|---|---|
| `SimpleArithmetic` | 기본 사칙연산 |
| `Precedence_IsRespected` | `×÷ > +−` 우선순위 |
| `MultiDigitTokens_AreParsedCorrectly` | 숫자 타일 병합 |
| `ConsecutiveOperators_AreCollapsed` | 2단계 |
| `EdgeOperators_AreTrimmed` | 4단계 |
| `DivisionByZero_...` / `MultiplicationByZero_IsPreserved` | 3단계, 그리고 곱셈과의 구분 |
| **`Regression_NormalizationOrder_DivOpZero`** | **단계 간 순서** |
| `Division_FloorsToInteger` | 내림 처리 |
| `NegativeResults_AreAllowed` | 음수 점수 |
| `InvalidOrEmptyInput_ReturnsZero` | 빈 수식 · 숫자 없음 |
| `BlankBetweenNumbers_BecomesPlus` / `BlankNotBetweenNumbers_IsSkipped` | 공백 승격 규칙 |
| `EvaluateWithTracking_*` (7개) | 인덱스 추적 — 위 규칙 각각에 대응 |

각 정규화 단계마다 테스트 그룹이 하나씩 있고, **단계 사이의 순서를 지키는 테스트가 따로
있습니다.** 단위 하나하나가 맞는 것과 조합이 맞는 것은 다른 문제이기 때문입니다.

---

## 남은 한계

- 정규화 규칙이 코드에 박혀 있어 기획이 규칙을 바꾸려면 코드를 고쳐야 합니다.
  규칙 수가 지금(4개)에서 크게 늘면 데이터 주도로 빼는 게 맞습니다.
- `EvaluateWithTracking`과 `Evaluate`의 파이프라인 이중화 (위에 적었습니다).
- 아주 긴 수식에서 `double` 중간 계산의 정밀도 손실 가능성이 있습니다. 실제 도달 가능한
  타일 수(한 턴 최대 클리어)에서는 문제가 없다고 보고 있으나 **명시적으로 검증하지는
  않았습니다.**
