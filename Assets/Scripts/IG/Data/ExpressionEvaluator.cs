using System;
using System.Collections.Generic;
using System.Text;

namespace IGMain
{
    /// <summary>
    /// 라인/스퀘어 클리어 시 타일 값으로 구성된 수식을 정규화하고 계산한다.
    ///
    /// [정규화 적용 순서]
    /// 1. Tokenize                     : 연속 숫자 타일 → 하나의 수 토큰으로 병합.
    ///                                   두 숫자 사이의 공백은 "+" 로 승격 (그 외 공백은 스킵)
    /// 2. CollapseConsecutiveOperators : 연속 연산기호 → 첫 번째만 남김
    /// 3. RemoveDivisionByZero         : 나눗셈 기호 뒤에 토큰 "0"이 오면 둘 다 제거
    /// 4. TrimEdgeOperators            : 수식 앞뒤의 연산기호 제거
    ///
    /// ※ Collapse를 RemoveDivisionByZero 이전에 실행해야 한다.
    ///   "3/*0" 같이 연산자가 연속된 경우, Collapse 전에 RemoveDivByZero를 하면
    ///   /0 패턴을 못 잡고(뒤가 *이므로), Collapse 후 새로 생긴 /0을 처리 못한 채
    ///   3/0을 평가해버린다.
    ///
    /// [계산]
    /// - 사칙연산 우선순위 (×÷ > +-)
    /// - 결과는 Floor 후 long 반환 (음수 가능 → 점수 차감)
    /// - 정규화 후 숫자 없으면 0 반환
    /// </summary>
    public static class ExpressionEvaluator
    {
        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// 타일 값들을 이어붙인 문자열로부터 점수를 계산한다.
        /// IGBoardModel.ClearLine 에서 사용.
        /// </summary>
        public static long Evaluate(string expression)
        {
            var tokens = Tokenize(expression);
            tokens = CollapseConsecutiveOperators(tokens);
            tokens = RemoveDivisionByZero(tokens);
            tokens = TrimEdgeOperators(tokens);

            if (tokens.Count == 0) return 0;

            var postfix = ToPostfix(tokens);
            double result = Calculate(postfix);
            return (long)Math.Floor(result);
        }

        /// <summary>
        /// 타일 값 목록으로부터 점수와 수식에 실제 포함된 타일 인덱스를 함께 반환한다.
        /// 연출 시스템에서 "수식에 참여한 타일 vs 정규화로 제거된 타일"을 구분할 때 사용.
        /// tileValues: 라인 순서대로 나열된 각 타일의 값 문자열 (ex. "3", "+", "0")
        /// </summary>
        public static (long score, HashSet<int> includedIndices) EvaluateWithTracking(IReadOnlyList<string> tileValues)
        {
            var tokens = TokenizeIndexed(tileValues);
            tokens = CollapseConsecutiveOperatorsIndexed(tokens);
            tokens = RemoveDivisionByZeroIndexed(tokens);
            tokens = TrimEdgeOperatorsIndexed(tokens);

            var included = new HashSet<int>();
            foreach (var t in tokens)
                foreach (int idx in t.SourceIndices)
                    included.Add(idx);

            if (tokens.Count == 0) return (0, included);

            var plainTokens = new List<string>(tokens.Count);
            foreach (var t in tokens)
                plainTokens.Add(t.Value);

            double result = Calculate(ToPostfix(plainTokens));
            return ((long)Math.Floor(result), included);
        }

        // ── Normalization steps (internal — public for unit testing) ──────────

        /// <summary>
        /// 수식 문자열을 토큰 목록으로 변환. 연속된 숫자 문자는 하나의 수 토큰으로 병합.
        /// 예: "34+0" → ["34", "+", "0"]
        ///
        /// 공백(" ")은 기본적으로 스킵하지만, "직전 토큰이 수 && 바로 뒤 문자가 숫자"인
        /// 두 숫자 사이의 공백만 "+" 로 승격한다. (연속 공백이면 첫 승격 후 직전 토큰이
        /// 연산자가 되어 나머지는 자동으로 스킵 → "+" 하나로 축약)
        /// 예: "3 5" → ["3","+","5"],  "3  5" → ["3","+","5"],  "3 +5" → ["3","+","5"]
        /// ※ 숫자 타일이 공백 없이 딱 붙은 "35"는 기존대로 하나의 수(35)로 병합된다.
        /// </summary>
        public static List<string> Tokenize(string expr)
        {
            var tokens = new List<string>();
            int i = 0;

            while (i < expr.Length)
            {
                char c = expr[i];

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < expr.Length && char.IsDigit(expr[i]))
                        i++;
                    tokens.Add(expr.Substring(start, i - start));
                }
                else if (IsOperator(c.ToString()))
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else
                {
                    // 공백/미지 문자: 두 숫자 사이일 때만 "+" 로 승격, 그 외엔 스킵.
                    if (LastIsNumber(tokens) && i + 1 < expr.Length && char.IsDigit(expr[i + 1]))
                        tokens.Add("+");
                    i++;
                }
            }

            return tokens;
        }

        /// <summary>토큰 목록의 마지막 토큰이 수(연산자가 아님)인지.</summary>
        private static bool LastIsNumber(List<string> tokens) =>
            tokens.Count > 0 && !IsOperator(tokens[tokens.Count - 1]);

        /// <summary>
        /// 나눗셈 기호(/ 또는 ÷) 바로 뒤 토큰이 정확히 "0"이면, 기호와 0 모두 제거.
        /// "00" 같은 병합 토큰은 대상이 아님 (단일 타일 "0"만 해당).
        /// 예: ["6", "/", "0", "+", "3"] → ["6", "+", "3"]
        /// </summary>
        public static List<string> RemoveDivisionByZero(List<string> tokens)
        {
            var result = new List<string>(tokens);
            // 뒤에서 앞으로 스캔 — 제거 시 인덱스 이동 없이 안전하게 처리
            for (int i = result.Count - 2; i >= 0; i--)
            {
                if (IsDiv(result[i]) && result[i + 1] == "0")
                {
                    result.RemoveAt(i + 1);
                    result.RemoveAt(i);
                }
            }
            return result;
        }

        /// <summary>
        /// 연속된 연산기호 중 첫 번째만 남기고 나머지 제거.
        /// 예: ["340", "+", "-", "2"] → ["340", "+", "2"]
        /// </summary>
        public static List<string> CollapseConsecutiveOperators(List<string> tokens)
        {
            var result = new List<string>();
            bool prevWasOp = false;

            foreach (var token in tokens)
            {
                bool currIsOp = IsOperator(token);
                if (currIsOp && prevWasOp)
                    continue; // 연속 연산기호 — 스킵
                result.Add(token);
                prevWasOp = currIsOp;
            }

            return result;
        }

        /// <summary>
        /// 수식 앞뒤의 연산기호를 제거.
        /// 예: ["+", "/", "340", "+", "2", "*"] → ["340", "+", "2"]
        /// </summary>
        public static List<string> TrimEdgeOperators(List<string> tokens)
        {
            var result = new List<string>(tokens);
            while (result.Count > 0 && IsOperator(result[0]))
                result.RemoveAt(0);
            while (result.Count > 0 && IsOperator(result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);
            return result;
        }

        // ── Shunting-yard & calculation ───────────────────────────────────────

        private static List<string> ToPostfix(List<string> tokens)
        {
            var output = new List<string>();
            var opStack = new Stack<string>();

            foreach (var token in tokens)
            {
                if (long.TryParse(token, out _))
                {
                    output.Add(token);
                }
                else
                {
                    while (opStack.Count > 0 && Precedence(opStack.Peek()) >= Precedence(token))
                        output.Add(opStack.Pop());
                    opStack.Push(token);
                }
            }

            while (opStack.Count > 0)
                output.Add(opStack.Pop());

            return output;
        }

        private static double Calculate(List<string> postfix)
        {
            var stack = new Stack<double>();

            foreach (var token in postfix)
            {
                if (long.TryParse(token, out long num))
                {
                    stack.Push(num);
                }
                else
                {
                    if (stack.Count < 2) return 0;
                    double b = stack.Pop();
                    double a = stack.Pop();

                    switch (token)
                    {
                        case "+": stack.Push(a + b); break;
                        case "-": stack.Push(a - b); break;
                        case "*":
                        case "×": stack.Push(a * b); break;
                        case "/":
                        case "÷": stack.Push(b == 0 ? 0 : a / b); break;
                    }
                }
            }

            return stack.Count > 0 ? stack.Pop() : 0;
        }

        private static int Precedence(string op)
        {
            if (op == "*" || op == "/" || op == "×" || op == "÷") return 2;
            if (op == "+" || op == "-") return 1;
            return 0;
        }

        private static bool IsDiv(string token) => token == "/" || token == "÷";

        private static bool IsOperator(string token) =>
            token == "+" || token == "-" || token == "*" || token == "/" || token == "×" || token == "÷";

        // ── Index-tracked variants (for EvaluateWithTracking) ─────────────────

        private readonly struct IndexedToken
        {
            public readonly string Value;
            public readonly int[] SourceIndices;

            public IndexedToken(string value, int[] indices)
            {
                Value = value;
                SourceIndices = indices;
            }
        }

        private static List<IndexedToken> TokenizeIndexed(IReadOnlyList<string> tileValues)
        {
            var tokens = new List<IndexedToken>();
            int i = 0;

            while (i < tileValues.Count)
            {
                string tv = tileValues[i];

                if (tv.Length == 1 && char.IsDigit(tv[0]))
                {
                    var digits = new StringBuilder();
                    var indices = new List<int>();

                    while (i < tileValues.Count && tileValues[i].Length == 1 && char.IsDigit(tileValues[i][0]))
                    {
                        digits.Append(tileValues[i]);
                        indices.Add(i);
                        i++;
                    }

                    tokens.Add(new IndexedToken(digits.ToString(), indices.ToArray()));
                }
                else if (IsOperator(tv))
                {
                    tokens.Add(new IndexedToken(tv, new[] { i }));
                    i++;
                }
                else
                {
                    // 공백 타일: 두 숫자 사이일 때만 "+" 로 승격. 승격된 공백 타일 인덱스를
                    // SourceIndices에 포함시켜 연출(참여 타일 하이라이트)과 점수를 일치시킨다.
                    bool lastIsNumber = tokens.Count > 0 && !IsOperator(tokens[tokens.Count - 1].Value);
                    bool nextIsDigit  = i + 1 < tileValues.Count
                                        && tileValues[i + 1].Length == 1
                                        && char.IsDigit(tileValues[i + 1][0]);
                    if (lastIsNumber && nextIsDigit)
                        tokens.Add(new IndexedToken("+", new[] { i }));
                    i++;
                }
            }

            return tokens;
        }

        private static List<IndexedToken> RemoveDivisionByZeroIndexed(List<IndexedToken> tokens)
        {
            var result = new List<IndexedToken>(tokens);
            for (int i = result.Count - 2; i >= 0; i--)
            {
                if (IsDiv(result[i].Value) && result[i + 1].Value == "0")
                {
                    result.RemoveAt(i + 1);
                    result.RemoveAt(i);
                }
            }
            return result;
        }

        private static List<IndexedToken> CollapseConsecutiveOperatorsIndexed(List<IndexedToken> tokens)
        {
            var result = new List<IndexedToken>();
            bool prevWasOp = false;

            foreach (var token in tokens)
            {
                bool currIsOp = IsOperator(token.Value);
                if (currIsOp && prevWasOp)
                    continue;
                result.Add(token);
                prevWasOp = currIsOp;
            }

            return result;
        }

        private static List<IndexedToken> TrimEdgeOperatorsIndexed(List<IndexedToken> tokens)
        {
            var result = new List<IndexedToken>(tokens);
            while (result.Count > 0 && IsOperator(result[0].Value))
                result.RemoveAt(0);
            while (result.Count > 0 && IsOperator(result[result.Count - 1].Value))
                result.RemoveAt(result.Count - 1);
            return result;
        }
    }
}
