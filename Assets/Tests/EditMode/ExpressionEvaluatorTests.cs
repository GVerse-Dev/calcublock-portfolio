using NUnit.Framework;
using IGMain;
using System.Collections.Generic;

namespace CalculationTetris.Tests.EditMode
{
    public class ExpressionEvaluatorTests
    {
        // ============================================================
        // 1. 정상 사칙연산
        // ============================================================
        [Test]
        [TestCase("1+2", 3L)]
        [TestCase("10-5", 5L)]
        [TestCase("3*4", 12L)]
        [TestCase("12/3", 4L)]
        [TestCase("12÷3", 4L)]
        [TestCase("3×4", 12L)]
        public void SimpleArithmetic_ReturnsCorrectValue(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 2. 연산자 우선순위 (×, ÷ 먼저)
        // ============================================================
        [Test]
        [TestCase("1+2*3", 7L)]
        [TestCase("10/2-3", 2L)]
        [TestCase("2+3*4-5", 9L)]
        [TestCase("100-50/5*2", 80L)]      // 50/5=10, 10*2=20, 100-20=80
        public void Precedence_IsRespected(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 3. 다자리 토큰화 + long 범위 검증
        // ============================================================
        [Test]
        [TestCase("12+34", 46L)]
        [TestCase("100/10", 10L)]
        [TestCase("999999999+1", 1000000000L)]        // 9자리 최대 + 1
        [TestCase("999999*999999", 999998000001L)]    // long 범위 안에서 곱셈 (int면 오버플로)
        public void MultiDigitTokens_AreParsedCorrectly(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 4. 연속 연산자 → 가장 앞의 것만 남김
        // ============================================================
        [Test]
        [TestCase("1++2", 3L)]
        [TestCase("10--5", 5L)]
        [TestCase("3**4", 12L)]
        [TestCase("5+-*3", 8L)]            // +/-/× 3중 연산자 → +만 살아남음 → 5+3
        [TestCase("5*-+3", 15L)]           // × 살아남음 → 5×3
        public void ConsecutiveOperators_AreCollapsed(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 5. 앞뒤 연산자 제거
        // ============================================================
        [Test]
        [TestCase("+1+2", 3L)]
        [TestCase("1+2-", 3L)]
        [TestCase("*1+2/", 3L)]
        [TestCase("+-1+2-+", 3L)]          // 양쪽 끝의 연속 연산자도 collapse 후 trim
        public void EdgeOperators_AreTrimmed(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 6. ÷0 제거 — "÷와 0만" 제거. 앞 숫자는 살아남음
        //    규칙 문서 예시: [6][÷][0][+][3] → [6][+][3] (결과 9)
        // ============================================================
        [Test]
        [TestCase("10/0+5", 15L)]          // 10+5 (10은 살아남음)
        [TestCase("10÷0+5", 15L)]          // ÷ 기호도 동일 동작
        [TestCase("10/0", 10L)]            // 뒤가 비면 10만 남음
        [TestCase("/0+5", 5L)]             // 앞에 숫자 없으면 +5만 남고 + trim → 5
        [TestCase("3/0/0+2", 5L)]          // 연속된 /0 둘 다 제거 → 3+2
        [TestCase("6÷0+3", 9L)]            // 규칙 문서 예시 그대로 (스펙 검증용)
        public void DivisionByZero_OnlyOperatorAndZeroAreRemoved(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 7. ×0은 제거되지 않음 (게임 룰의 차별 포인트)
        // ============================================================
        [Test]
        [TestCase("6*0+3", 3L)]            // 6×0=0, 0+3=3
        [TestCase("6×0+3", 3L)]
        [TestCase("0*5", 0L)]
        [TestCase("9*0*9+7", 7L)]          // 양쪽 ×0이라도 계산에 참여
        public void MultiplicationByZero_IsPreserved(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 8. 회귀 테스트: 정규화 단계 순서 (빈틈 3)
        //    /0 제거를 collapse보다 먼저 하면 "3/*0"이 그대로 → collapse → "3/0" → 💥
        //    올바른 순서(collapse → /0): "3/*0" → "3/0" → "3"
        //    ⚠ 이 테스트가 실패하면 ExpressionEvaluator의 정규화 단계 순서가 잘못된 것
        // ============================================================
        [Test]
        [TestCase("3/*0", 3L)]             // collapse → "3/0" → /0 제거 → "3"
        [TestCase("5÷×0+2", 7L)]           // collapse → "5÷0+2" → /0 제거 → "5+2"
        public void Regression_NormalizationOrder_DivOpZero(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 9. 정수 나눗셈 Floor (양수만)
        //    음수 나눗셈은 본 룰에서 발생 불가 — 피연산자가 항상 0-9 기반 비음수
        // ============================================================
        [Test]
        [TestCase("5/2", 2L)]              // 2.5 → 2
        [TestCase("1/3", 0L)]              // 0.33 → 0
        [TestCase("7/2", 3L)]              // 3.5 → 3
        [TestCase("100/7", 14L)]           // 14.28 → 14
        public void Division_FloorsToInteger(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 10. 음수 결과 허용 (뺄셈으로만 발생)
        // ============================================================
        [Test]
        [TestCase("5-10", -5L)]
        [TestCase("2-3-4", -5L)]
        [TestCase("0-1", -1L)]
        [TestCase("1-2*3", -5L)]           // 1 - (2×3) = -5
        [TestCase("10-9*2", -8L)]          // 우선순위 + 음수 결과
        public void NegativeResults_AreAllowed(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 11. 잘못된 / 빈 입력 → 0점
        // ============================================================
        [Test]
        [TestCase("", 0L)]
        [TestCase("+++", 0L)]              // collapse → "+" → trim → ""
        [TestCase("///*", 0L)]
        [TestCase("abc", 0L)]              // 알 수 없는 토큰 (방어적 처리)
        public void InvalidOrEmptyInput_ReturnsZero(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 11-b. 두 숫자 사이의 공백 → "+" 승격
        //     공백 타일 값은 " "(스페이스). 숫자-공백-숫자만 덧셈으로 처리.
        // ============================================================
        [Test]
        [TestCase("3 5", 8L)]              // 3+5
        [TestCase("3  5", 8L)]             // 연속 공백도 "+" 하나로 축약 → 3+5
        [TestCase("1 2 3", 6L)]            // 1+2+3
        [TestCase("12 34", 46L)]           // 다자리 유지: 12+34
        [TestCase("2 3*4", 14L)]           // 2 + (3×4) — 우선순위 유지
        public void BlankBetweenNumbers_BecomesPlus(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 11-c. 두 숫자 사이가 아닌 공백은 승격되지 않음 (기존대로 스킵)
        // ============================================================
        [Test]
        [TestCase(" 3+5", 8L)]             // 선행 공백: 숫자 사이 아님 → 스킵
        [TestCase("3+5 ", 8L)]             // 후행 공백: 스킵
        [TestCase("3 +5", 8L)]             // 숫자-공백-연산자 → 스킵 (실제 + 사용)
        [TestCase("3+ 5", 8L)]             // 연산자-공백-숫자 → 스킵
        [TestCase("3 -5", -2L)]            // 뺄셈 보존: 공백이 - 를 덮지 않음 → 3-5
        [TestCase("35", 35L)]              // 공백 없이 붙은 숫자는 병합 유지
        public void BlankNotBetweenNumbers_IsSkipped(string expression, long expected)
            => Assert.AreEqual(expected, ExpressionEvaluator.Evaluate(expression));

        // ============================================================
        // 12. EvaluateWithTracking — 기본 시나리오
        // ============================================================
        [Test]
        public void EvaluateWithTracking_BasicCase_ReturnsScoreAndIndices()
        {
            // index:    0    1    2    3    4    5    6    7    8
            var values = new List<string> { "+", "1", "2", "*", "3", "0", "/", "0", "-" };
            // tokenize:  +(0), 12(1,2), *(3), 30(4,5), /(6), 0(7), -(8)
            // /0 제거:   +(0), 12(1,2), *(3), 30(4,5),                  -(8)
            // collapse:  변화 없음
            // trim:            12(1,2), *(3), 30(4,5)
            // 평가:      12 × 30 = 360

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(360, score);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, included);
        }

        // ============================================================
        // 13. EvaluateWithTracking — /0 제거된 인덱스는 included에서 빠짐
        // ============================================================
        [Test]
        public void EvaluateWithTracking_DivByZero_ExcludesRemovedIndices()
        {
            // index:    0    1    2    3    4
            var values = new List<string> { "6", "/", "0", "+", "3" };
            // /0 제거: [6(0), +(3), 3(4)] = 9
            // / 와 0 두 타일이 included에서 빠져야 함

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(9, score);
            CollectionAssert.AreEquivalent(new[] { 0, 3, 4 }, included);
        }

        // ============================================================
        // 14. EvaluateWithTracking — ×0은 included 전부 포함
        //     (계산에 참여하므로 시각적으로도 회색 처리되면 안 됨)
        // ============================================================
        [Test]
        public void EvaluateWithTracking_MulByZero_AllIndicesIncluded()
        {
            // index:    0    1    2    3    4
            var values = new List<string> { "6", "*", "0", "+", "3" };
            // 6×0=0, 0+3=3. 전부 계산에 참여

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(3, score);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, included);
        }

        // ============================================================
        // 15. EvaluateWithTracking — 양쪽 끝 연산자는 included에서 빠짐
        // ============================================================
        [Test]
        public void EvaluateWithTracking_TrimmedEdges_AreExcluded()
        {
            // index:    0    1    2    3    4
            var values = new List<string> { "+", "5", "*", "3", "-" };
            // trim → [5(1), *(2), 3(3)] = 15

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(15, score);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, included);
        }

        // ============================================================
        // 16. EvaluateWithTracking — 다자리 숫자가 한 토큰의 일부일 때
        //     같은 토큰의 모든 타일 인덱스가 함께 포함/제외돼야 함
        // ============================================================
        [Test]
        public void EvaluateWithTracking_MultiDigitToken_IncludesAllTilesOfToken()
        {
            // index:    0    1    2    3    4    5
            var values = new List<string> { "9", "9", "9", "+", "1", "1" };
            // tokens: [999(0,1,2), +(3), 11(4,5)] = 1010

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(1010, score);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, included);
        }

        // ============================================================
        // 17. EvaluateWithTracking — 두 숫자 사이 공백이 "+" 로 승격되면
        //     그 공백 타일 인덱스도 included에 포함 (연출 하이라이트 일치)
        // ============================================================
        [Test]
        public void EvaluateWithTracking_BlankBetweenNumbers_IncludesBlankIndex()
        {
            // index:    0    1    2
            var values = new List<string> { "3", " ", "5" };
            // tokens: [3(0), +(1), 5(2)] = 8. 공백(1)이 + 로 승격되어 포함돼야 함

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(8, score);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, included);
        }

        // ============================================================
        // 18. EvaluateWithTracking — 숫자 사이가 아닌 공백은 제외
        // ============================================================
        [Test]
        public void EvaluateWithTracking_BlankNotBetweenNumbers_ExcludesBlankIndex()
        {
            // index:    0    1    2    3
            var values = new List<string> { "3", "+", " ", "5" };
            // 공백(2)의 직전 토큰이 연산자(+) → 승격 안 됨 → [3(0), +(1), 5(3)] = 8
            // 공백(2)는 included에서 빠져야 함

            var (score, included) = ExpressionEvaluator.EvaluateWithTracking(values);

            Assert.AreEqual(8, score);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, included);
        }
    }
}