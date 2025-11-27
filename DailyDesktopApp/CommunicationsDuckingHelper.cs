using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace DailyDesktopApp
{
    public static class CommunicationsDuckingHelper
    {
        private const string AudioKeyPath = @"Software\Microsoft\Multimedia\Audio";
        private const string DuckingValueName = "UserDuckingPreference";
        private const int DoNothingValue = 3;

        // Windows message broadcast constants
        private const int HWND_BROADCAST = 0xFFFF;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            int Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        public static void EnsureDoNothing()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(AudioKeyPath))
                {
                    if (key == null) return;

                    var currentObj = key.GetValue(DuckingValueName);
                    int current = currentObj is int i ? i : -1;

                    if (current != DoNothingValue)
                    {
                        key.SetValue(DuckingValueName, DoNothingValue, RegistryValueKind.DWord);

                        // Notify Windows that audio settings changed
                        IntPtr result;
                        SendMessageTimeout(
                            new IntPtr(HWND_BROADCAST),
                            WM_SETTINGCHANGE,
                            IntPtr.Zero,
                            AudioKeyPath,
                            SMTO_ABORTIFHUNG,
                            1000,
                            out result);
                    }
                }
            }
            catch
            {
                // Safe fail — nothing dangerous happens if this fails.
            }
        }
    }
}
