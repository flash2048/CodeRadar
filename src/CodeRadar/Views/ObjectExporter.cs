using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeRadar.Models;

namespace CodeRadar.Views
{
    public enum ExportFormat
    {
        Text,
        Json,
        CSharp
    }

    public sealed class ExportOptions
    {
        public int? MaxDepth { get; set; }
        public bool UseTypeFullName { get; set; }
        public bool IgnoreIndexes { get; set; }
        public bool IgnoreDefaultValues { get; set; }
        public bool PropertiesOnly { get; set; }
        public bool TrimRootName { get; set; }

        internal static ExportOptions Or(ExportOptions options) => options ?? new ExportOptions();
    }

    public static class ObjectExporter
    {
        public static string Export(VariableNode node, ExportFormat format)
            => Export(node, format, null);

        public static string Export(VariableNode node, ExportFormat format, ExportOptions options)
        {
            if (node == null) return string.Empty;
            options = ExportOptions.Or(options);

            // Pre-size the buffer: 64 bytes per expected node is a reasonable average.
            var sb = new StringBuilder(Math.Max(256, EstimateNodeCount(node, options) * 64));

            switch (format)
            {
                case ExportFormat.Text:   WriteText(sb, node, indent: 0, depth: 0, opts: options, isRoot: true); break;
                case ExportFormat.Json:   WriteJson(sb, node, indent: 0, depth: 0, opts: options); break;
                case ExportFormat.CSharp: WriteCSharpRoot(sb, node, options); break;
                default:                  WriteText(sb, node, indent: 0, depth: 0, opts: options, isRoot: true); break;
            }
            return sb.ToString();
        }

        private static int EstimateNodeCount(VariableNode node, ExportOptions opts)
        {
            int depthLimit = opts.MaxDepth ?? 8;
            return CountNodes(node, 0, depthLimit, 2000);
        }

        private static int CountNodes(VariableNode node, int depth, int depthLimit, int hardCap)
        {
            if (node == null || depth > depthLimit) return 0;
            int n = 1;
            for (int i = 0; i < node.Children.Count && n < hardCap; i++)
                n += CountNodes(node.Children[i], depth + 1, depthLimit, hardCap);
            return n;
        }

        private static void WriteText(StringBuilder sb, VariableNode node, int indent, int depth,
                                      ExportOptions opts, bool isRoot)
        {
            AppendIndent(sb, indent);

            if (!(isRoot && opts.TrimRootName))
            {
                string label = node.Name;
                if (opts.IgnoreIndexes && LooksLikeIndexer(label))
                    label = string.Empty;

                if (!string.IsNullOrEmpty(label))
                {
                    sb.Append(label);
                    if (!string.IsNullOrEmpty(node.Type))
                        sb.Append(" : ").Append(FormatType(node.Type, opts));
                    sb.Append(" = ");
                }
                else if (!string.IsNullOrEmpty(node.Type))
                {
                    sb.Append(FormatType(node.Type, opts)).Append(" = ");
                }
            }

            sb.AppendLine(node.IsValid ? node.Value : "<invalid>");

            if (BeyondDepth(depth + 1, opts)) return;

            var children = node.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!Accept(child, opts)) continue;
                WriteText(sb, child, indent + 1, depth + 1, opts, isRoot: false);
            }
        }

        private static void WriteJson(StringBuilder sb, VariableNode node, int indent, int depth,
                                      ExportOptions opts)
        {
            if (!node.IsValid || node.IsNull) { sb.Append("null"); return; }

            if (node.HasChildren && !BeyondDepth(depth + 1, opts))
            {
                bool isSequence = LooksLikeSequence(node);
                char open = isSequence ? '[' : '{';
                char close = isSequence ? ']' : '}';

                // Find first accepted child to know whether to emit empty container.
                var children = node.Children;
                int firstIdx = -1;
                for (int i = 0; i < children.Count; i++)
                {
                    if (Accept(children[i], opts)) { firstIdx = i; break; }
                }
                if (firstIdx < 0) { sb.Append(open).Append(close); return; }

                sb.Append(open).AppendLine();

                bool first = true;
                for (int i = firstIdx; i < children.Count; i++)
                {
                    var child = children[i];
                    if (!Accept(child, opts)) continue;

                    if (!first) sb.Append(',').AppendLine();
                    AppendIndent(sb, indent + 1);
                    if (!isSequence)
                        AppendJsonString(sb, child.Name).Append(": ");
                    WriteJson(sb, child, indent + 1, depth + 1, opts);
                    first = false;
                }

                sb.AppendLine();
                AppendIndent(sb, indent).Append(close);
                return;
            }

            AppendJsonLeaf(sb, node);
        }

        private static void AppendJsonLeaf(StringBuilder sb, VariableNode node)
        {
            var v = node.Value ?? string.Empty;
            var t = node.Type ?? string.Empty;
            if (LooksLikeString(t)) { AppendJsonString(sb, Strip(v)); return; }
            if (LooksLikeBool(t, v)) { sb.Append(v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false"); return; }
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { sb.Append(v); return; }
            AppendJsonString(sb, Strip(v));
        }

        private static StringBuilder AppendJsonString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return sb; }
            sb.Append('"');
            int runStart = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string esc = null;
                switch (c)
                {
                    case '\\': esc = "\\\\"; break;
                    case '"':  esc = "\\\""; break;
                    case '\b': esc = "\\b"; break;
                    case '\f': esc = "\\f"; break;
                    case '\n': esc = "\\n"; break;
                    case '\r': esc = "\\r"; break;
                    case '\t': esc = "\\t"; break;
                    default:
                        if (c < 0x20)
                            esc = "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
                        break;
                }
                if (esc != null)
                {
                    if (i > runStart) sb.Append(s, runStart, i - runStart);
                    sb.Append(esc);
                    runStart = i + 1;
                }
            }
            if (runStart < s.Length) sb.Append(s, runStart, s.Length - runStart);
            sb.Append('"');
            return sb;
        }

        private static void WriteCSharpRoot(StringBuilder sb, VariableNode node, ExportOptions opts)
        {
            if (!opts.TrimRootName)
            {
                sb.Append("var ");
                AppendSafeIdentifier(sb, node.Name);
                sb.Append(" = ");
            }
            WriteCSharp(sb, node, indent: 0, depth: 0, opts);
            if (!opts.TrimRootName) sb.Append(';');
        }

        private static void WriteCSharp(StringBuilder sb, VariableNode node, int indent, int depth,
                                        ExportOptions opts)
        {
            if (!node.IsValid) { sb.Append("default /* invalid */"); return; }
            if (node.IsNull)   { sb.Append("null");                 return; }

            if (node.HasChildren && !BeyondDepth(depth + 1, opts))
            {
                bool isSequence = LooksLikeSequence(node);
                var children = node.Children;

                int firstIdx = -1;
                for (int i = 0; i < children.Count; i++)
                {
                    if (Accept(children[i], opts)) { firstIdx = i; break; }
                }

                if (isSequence)
                {
                    sb.Append("new[]");
                }
                else if (!string.IsNullOrWhiteSpace(node.Type))
                {
                    sb.Append("new ").Append(FormatType(node.Type, opts));
                }
                else
                {
                    sb.Append("new");
                }

                if (firstIdx < 0) { sb.Append(" { }"); return; }

                sb.AppendLine();
                AppendIndent(sb, indent);
                sb.Append('{').AppendLine();

                bool first = true;
                for (int i = firstIdx; i < children.Count; i++)
                {
                    var child = children[i];
                    if (!Accept(child, opts)) continue;

                    if (!first) sb.Append(',').AppendLine();
                    AppendIndent(sb, indent + 1);
                    if (!isSequence)
                    {
                        AppendSafeIdentifier(sb, child.Name).Append(" = ");
                    }
                    WriteCSharp(sb, child, indent + 1, depth + 1, opts);
                    first = false;
                }
                sb.AppendLine();
                AppendIndent(sb, indent);
                sb.Append('}');
                return;
            }

            AppendCSharpLeaf(sb, node);
        }

        private static void AppendCSharpLeaf(StringBuilder sb, VariableNode node)
        {
            var v = node.Value ?? string.Empty;
            var t = node.Type ?? string.Empty;
            if (LooksLikeString(t)) { sb.Append('"'); AppendEscapedCSharpString(sb, Strip(v)); sb.Append('"'); return; }
            if (LooksLikeBool(t, v)) { sb.Append(v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false"); return; }
            if (LooksLikeChar(t, v)) { sb.Append('\'').Append(Strip(v)).Append('\''); return; }
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { sb.Append(v); return; }
            if (!string.IsNullOrEmpty(t) && !t.Contains(" ") && v.IndexOf('.') > 0) { sb.Append(v); return; }
            sb.Append('"'); AppendEscapedCSharpString(sb, Strip(v)); sb.Append('"');
        }

        private static void AppendEscapedCSharpString(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int runStart = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string esc = null;
                switch (c)
                {
                    case '\\': esc = "\\\\"; break;
                    case '"':  esc = "\\\""; break;
                    case '\n': esc = "\\n"; break;
                    case '\r': esc = "\\r"; break;
                    case '\t': esc = "\\t"; break;
                }
                if (esc != null)
                {
                    if (i > runStart) sb.Append(s, runStart, i - runStart);
                    sb.Append(esc);
                    runStart = i + 1;
                }
            }
            if (runStart < s.Length) sb.Append(s, runStart, s.Length - runStart);
        }

        private static bool Accept(VariableNode c, ExportOptions opts)
        {
            if (opts.PropertiesOnly)
            {
                if (IsSyntheticName(c.Name)) return false;
                if (LooksLikeIndexer(c.Name)) return false;
            }
            if (opts.IgnoreDefaultValues && IsDefaultValue(c)) return false;
            return true;
        }

        private static bool IsDefaultValue(VariableNode node)
        {
            if (node == null) return true;
            if (node.IsNull) return true;
            if (!node.IsValid) return false;

            var v = (node.Value ?? string.Empty).Trim();
            if (v.Length == 0) return true;
            if (v == "0" || v == "0.0" || v == "0m" || v == "0L" || v == "0d" || v == "0f") return true;
            if (v == "false" || v == "False" || v == "FALSE") return true;
            if (v == "\"\"" || v == "''") return true;
            if (v == "null" || v == "Nothing" || v == "<null>") return true;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d == 0d
                && IsNumericType(node.Type))
                return true;
            return false;
        }

        private static bool IsNumericType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            string t = type.ToLowerInvariant();
            return t.Contains("int") || t.Contains("long") || t.Contains("short") ||
                   t.Contains("byte") || t.Contains("double") || t.Contains("float") ||
                   t.Contains("decimal") || t.Contains("single");
        }

        private static bool IsSyntheticName(string name)
            => name == "Raw View" || name == "Static members"
            || name == "Non-Public members" || name == "Results View";

        private static bool LooksLikeIndexer(string name)
            => !string.IsNullOrEmpty(name) && name.Length >= 2
               && name[0] == '[' && name[name.Length - 1] == ']';

        private static bool LooksLikeSequence(VariableNode node)
        {
            if (node.Children.Count == 0) return false;
            if (LooksLikeIndexer(node.Children[0].Name)) return true;

            var t = node.Type ?? string.Empty;
            return t.EndsWith("[]", StringComparison.Ordinal)
                   || t.IndexOf("List<", StringComparison.Ordinal) >= 0
                   || t.IndexOf("IEnumerable", StringComparison.Ordinal) >= 0
                   || t.IndexOf("Collection", StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeString(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type == "string" || type == "System.String" ||
                   type.EndsWith(".String", StringComparison.Ordinal);
        }

        private static bool LooksLikeBool(string type, string value)
        {
            if (string.Equals(type, "bool", StringComparison.Ordinal) ||
                string.Equals(type, "System.Boolean", StringComparison.Ordinal))
                return true;
            var v = (value ?? string.Empty).Trim().ToLowerInvariant();
            return v == "true" || v == "false";
        }

        private static bool LooksLikeChar(string type, string value)
        {
            return (type == "char" || type == "System.Char")
                   && !string.IsNullOrEmpty(value)
                   && value.Length == 3 && value[0] == '\'' && value[2] == '\'';
        }

        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private static string FormatType(string type, ExportOptions opts)
        {
            if (string.IsNullOrEmpty(type)) return "object";
            if (opts.UseTypeFullName) return type;
            return SimplifyType(type);
        }

        private static string SimplifyType(string type)
        {
            if (string.IsNullOrEmpty(type)) return "object";
            if (type.IndexOf('<') > 0) return type;
            var idx = type.LastIndexOf('.');
            if (idx < 0 || idx == type.Length - 1) return type;
            return type.Substring(idx + 1);
        }

        private static StringBuilder AppendSafeIdentifier(StringBuilder sb, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { sb.Append("value"); return sb; }
            int start = sb.Length;
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length > start && char.IsDigit(sb[start]))
                sb.Insert(start, '_');
            return sb;
        }

        private static StringBuilder AppendIndent(StringBuilder sb, int indent)
        {
            if (indent > 0) sb.Append(' ', indent * 2);
            return sb;
        }

        private static bool BeyondDepth(int depth, ExportOptions opts)
            => opts.MaxDepth.HasValue && depth > opts.MaxDepth.Value;
    }
}
