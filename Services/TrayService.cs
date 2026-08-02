using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace SimpitLauncher.Services;

public sealed class TrayService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int WmTrayIcon = WmApp + 1;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRbuttonUp = 0x0205;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const int IdShow = 1001;
    private const int IdStart = 1002;
    private const int IdStop = 1003;
    private const int IdExit = 1004;

    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly SubclassProc _subclassProc;
    private NOTIFYICONDATA _data;
    private bool _added;
    private bool _disposed;

    public event Action? ShowRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? ExitRequested;

    public TrayService(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
        _subclassProc = WndProc;

        if (!SetWindowSubclass(_hwnd, _subclassProc, 1, 0))
        {
            throw new InvalidOperationException("Failed to subclass window for tray messages.");
        }

        _data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = LoadIcon(),
            szTip = "Simpit Launcher"
        };

        _added = Shell_NotifyIcon(NimAdd, ref _data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_added)
        {
            Shell_NotifyIcon(NimDelete, ref _data);
            _added = false;
        }

        RemoveWindowSubclass(_hwnd, _subclassProc, 1);
        if (_data.hIcon != nint.Zero)
        {
            DestroyIcon(_data.hIcon);
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint refData)
    {
        if (msg == WmTrayIcon)
        {
            var mouseMsg = (int)(lParam & 0xFFFF);
            if (mouseMsg == WmLButtonDblClk)
            {
                ShowRequested?.Invoke();
            }
            else if (mouseMsg == WmRbuttonUp)
            {
                ShowContextMenu();
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, (nuint)IdShow, "Show");
        AppendMenu(menu, MfString, (nuint)IdStart, "Start");
        AppendMenu(menu, MfString, (nuint)IdStop, "Stop");
        AppendMenu(menu, MfSeparator, 0, string.Empty);
        AppendMenu(menu, MfString, (nuint)IdExit, "Exit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd, pt.X, pt.Y, 0, _hwnd, nint.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case IdShow:
                ShowRequested?.Invoke();
                break;
            case IdStart:
                StartRequested?.Invoke();
                break;
            case IdStop:
                StopRequested?.Invoke();
                break;
            case IdExit:
                ExitRequested?.Invoke();
                break;
        }
    }

    private static nint LoadIcon()
    {
        // Prefer the small icon embedded in the exe (ApplicationIcon) — most reliable for tray.
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            ExtractIconEx(exe, 0, out var large, out var small, 1);
            if (small != nint.Zero)
            {
                if (large != nint.Zero && large != small)
                {
                    DestroyIcon(large);
                }

                return small;
            }

            if (large != nint.Zero)
            {
                return large;
            }
        }

        // Fallback: load AppIcon.ico at the system small-icon size.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!File.Exists(iconPath) && !string.IsNullOrWhiteSpace(exe))
        {
            var dir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                iconPath = Path.Combine(dir, "Assets", "AppIcon.ico");
            }
        }

        if (File.Exists(iconPath))
        {
            var cx = GetSystemMetrics(SmCxSmIcon);
            var cy = GetSystemMetrics(SmCySmIcon);
            if (cx <= 0) { cx = 16; }
            if (cy <= 0) { cy = 16; }

            var icon = LoadImage(nint.Zero, iconPath, ImageIcon, cx, cy, LrLoadFromFile);
            if (icon != nint.Zero)
            {
                return icon;
            }
        }

        return LoadIcon(nint.Zero, IdiApplication);
    }

    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;
    private const nint IdiApplication = 32512;

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint refData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
