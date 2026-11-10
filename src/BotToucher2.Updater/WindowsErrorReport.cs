using System.Runtime.InteropServices;
internal static class WindowsErrorReport
{
    public static void Show(string message)
    {
        if (OperatingSystem.IsWindows()) MessageBox(IntPtr.Zero, message, "Bot Toucher 2 — update failed", 0x10);
        else Console.Error.WriteLine(message);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
