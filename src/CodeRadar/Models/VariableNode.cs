using System.Collections.Generic;

namespace CodeRadar.Models
{
    public sealed class VariableNode
    {
        public VariableNode(
            string name,
            string value,
            string type,
            bool isValid,
            bool isNull,
            IReadOnlyList<VariableNode> children)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
            Type = type ?? string.Empty;
            IsValid = isValid;
            IsNull = isNull;
            Children = children ?? System.Array.Empty<VariableNode>();
        }

        public string Name { get; }

        public string Value { get; }

        public string Type { get; }

        public bool IsValid { get; }

        public bool IsNull { get; }

        public IReadOnlyList<VariableNode> Children { get; }

        public bool HasChildren => Children.Count > 0;
    }
}
