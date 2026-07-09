using System;
using System.Runtime.InteropServices;

namespace SampleApp.Com;

[ComVisible(true)]
[Guid("12345678-1234-1234-1234-123456789abc")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMyComInterface
{
    void DoWork();
}

public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
