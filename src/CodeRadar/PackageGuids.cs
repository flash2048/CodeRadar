using System;

namespace CodeRadar
{
    internal static class PackageGuids
    {
        public const string PackageGuidString = "3a2f9d50-1c4b-4b3a-9b0e-5b9f6e0c8a11";
        public static readonly Guid PackageGuid = new Guid(PackageGuidString);

        public const string CommandSetGuidString = "8f7c7a2c-4a3e-4d1f-9d85-2c7b7b0a1a22";
        public static readonly Guid CommandSetGuid = new Guid(CommandSetGuidString);

        public const string ToolWindowGuidString = "d1b2e5c7-7a4f-4f22-9a9d-3e6b1c2d4f33";
        public static readonly Guid ToolWindowGuid = new Guid(ToolWindowGuidString);
    }

    internal static class PackageIds
    {
        public const int ShowCodeRadarWindowCommandId = 0x0100;
        public const int CodeRadarMenuGroup           = 0x1020;

        public const int CodeRadarEditorContextGroup  = 0x1021;
        public const int CodeRadarExtensionsGroup     = 0x1022;
        public const int CodeRadarToolsGroup          = 0x1023;
        public const int CodeRadarSubMenu             = 0x1030;
        public const int CodeRadarSubMenuGroup        = 0x1040;

        public const int EditorAddToWatchesCommandId  = 0x0200;
        public const int EditorExportObjectCommandId  = 0x0201;
        public const int EditorDecomposeLinqCommandId = 0x0202;
        public const int EditorShowWindowCommandId    = 0x0203;
        public const int EditorShowImageCommandId     = 0x0204;
    }

    internal static class BitmapIds
    {
        public const int ShowWindow = 1;
        public const int AddWatch   = 2;
        public const int Export     = 3;
        public const int Linq       = 4;
        public const int Image      = 5;
    }
}
