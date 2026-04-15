using System.Collections.Generic;

namespace CodeRadar.Services
{
    public static class LinqChainAnalyzer
    {
        public sealed class ChainSegment
        {
            public ChainSegment(string label, string cumulativeExpression)
            {
                Label = label;
                CumulativeExpression = cumulativeExpression;
            }

            public string Label { get; }
            public string CumulativeExpression { get; }
        }

        public static IReadOnlyList<ChainSegment> Parse(string expression)
        {
            var result = new List<ChainSegment>();
            if (string.IsNullOrWhiteSpace(expression))
                return result;

            expression = expression.Trim();

            var dotPositions = new List<int>();
            int depth = 0;
            bool inString = false, inChar = false;

            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];

                if (inString)
                {
                    if (c == '\\' && i + 1 < expression.Length) { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (inChar)
                {
                    if (c == '\\' && i + 1 < expression.Length) { i++; continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '\'') { inChar = true; continue; }

                // '<' and '>' are treated as operators (not generic brackets) because they
                // appear as operators far more often than as generic type arguments in
                // user-written watch expressions.
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') { if (depth > 0) depth--; }
                else if (c == '.' && depth == 0)
                {
                    int j = i + 1;
                    while (j < expression.Length && (char.IsLetterOrDigit(expression[j]) || expression[j] == '_'))
                        j++;
                    if (j < expression.Length && expression[j] == '(')
                    {
                        dotPositions.Add(i);
                    }
                }
            }

            if (dotPositions.Count == 0)
            {
                result.Add(new ChainSegment("source", expression));
                return result;
            }

            string source = expression.Substring(0, dotPositions[0]).Trim();
            if (source.Length > 0)
                result.Add(new ChainSegment("source", source));

            for (int s = 0; s < dotPositions.Count; s++)
            {
                int start = dotPositions[s];
                int end = (s + 1 < dotPositions.Count) ? dotPositions[s + 1] : expression.Length;
                string segment = expression.Substring(start, end - start).Trim();
                string cumulative = expression.Substring(0, end).Trim();
                result.Add(new ChainSegment(segment, cumulative));
            }

            return result;
        }
    }
}
