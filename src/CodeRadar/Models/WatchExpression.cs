using System;

namespace CodeRadar.Models
{
    public sealed class WatchExpression
    {
        public WatchExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Expression cannot be empty.", nameof(expression));

            Expression = expression.Trim();
            Id = Guid.NewGuid();
        }

        public Guid Id { get; }

        public string Expression { get; }

        public override string ToString() => Expression;
    }
}
