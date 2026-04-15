namespace CodeRadar.Models
{
    public sealed class StackFrameInfo
    {
        public StackFrameInfo(int index, string functionName, string module, string language, string location, bool isUserCode)
        {
            Index = index;
            FunctionName = functionName ?? string.Empty;
            Module = module ?? string.Empty;
            Language = language ?? string.Empty;
            Location = location ?? string.Empty;
            IsUserCode = isUserCode;
        }

        public int Index { get; }

        public string FunctionName { get; }

        public string Module { get; }

        public string Language { get; }

        public string Location { get; }

        public bool IsUserCode { get; }

        public override string ToString() => $"[{Index}] {FunctionName}  ({Module})";
    }
}
