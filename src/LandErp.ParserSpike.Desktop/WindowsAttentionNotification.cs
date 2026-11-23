using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LandErp.ParserSpike.Desktop;

/// <summary>Native Windows notification tied to the existing window, without a background host.</summary>
internal sealed class WindowsAttentionNotification : IDisposable
{
    private const int Callback = 0x8001;
    private readonly HwndSource source;
    private readonly Action open;
    private bool added;
    private bool disposed;
    public WindowsAttentionNotification(Window window, Action open)
    {
        this.open = open;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        source.AddHook(OnMessage);
    }
    public bool Show(string text)
    {
        var data = Data();
        data.Flags = 1 | 2 | 4 | 16; // Callback, icon, tooltip and balloon text.
        data.CallbackMessage = Callback;
        data.Icon = LoadIcon(nint.Zero, (nint)32515);
        data.Tooltip = "LandErp Parser — требуется действие";
        data.Title = "LandErp Parser — требуется действие";
        data.Info = text.Length > 255 ? text[..255] : text;
        data.InfoFlags = 2 | 16; // Warning without an unsolicited sound.
        data.Timeout = 10000;
        if (!added)
        {
            data.Flags &= ~16u;
            added = ShellNotifyIcon(0, ref data);
            data.Flags |= 16;
        }
        return added && ShellNotifyIcon(1, ref data);
    }
    public void Clear()
    {
        if (!added) return;
        var data = Data();
        _ = ShellNotifyIcon(2, ref data);
        added = false;
    }
    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = source.Handle, Id = 1,
        Tooltip = "", Info = "", Title = ""
    };
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Callback && ((long)lParam is 0x405 or 0x203))
        {
            handled = true;
            open();
        }
        return nint.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        Clear();
        source.RemoveHook(OnMessage);
        disposed = true;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tooltip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint operation, ref NotifyData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LoadIcon(nint instance, nint name);
}
