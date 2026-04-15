using CodeRadar.Models;

namespace CodeRadar.ViewModels
{
    public sealed class StackFrameViewModel : ObservableObject
    {
        private bool _isPinned;

        public StackFrameViewModel(StackFrameInfo info)
        {
            Info = info;
        }

        public StackFrameInfo Info { get; }

        public int Index => Info.Index;
        public string FunctionName => Info.FunctionName;
        public string Module => Info.Module;
        public string Language => Info.Language;
        public string Location => Info.Location;
        public bool IsUserCode => Info.IsUserCode;

        // Used to match pins after the stack is re-captured on the next break.
        // Overloads share a key, which is acceptable for this UI.
        public string PinKey => $"{Module}!{FunctionName}";

        public bool IsPinned
        {
            get => _isPinned;
            set => SetProperty(ref _isPinned, value);
        }
    }
}
