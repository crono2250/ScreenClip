using System;
using Microsoft.Win32;

namespace ScreenClip
{
    internal static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ScreenClip";
        internal const string Argument = "--autostart";

        internal static string CommandFor(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath) || executablePath.IndexOf('"') >= 0)
                throw new ArgumentException("実行ファイルのパスが無効です。", "executablePath");
            string command = "\"" + executablePath + "\" " + Argument;
            if (command.Length > 260)
                throw new InvalidOperationException("実行ファイルのパスが長すぎるため、自動起動に登録できません。");
            return command;
        }

        public static void Apply(bool enabled)
        {
            if (enabled)
            {
                string command = CommandFor(System.Windows.Forms.Application.ExecutablePath);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) throw new InvalidOperationException("自動起動の登録先を開けませんでした。");
                    if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal) ||
                        key.GetValueKind(ValueName) != RegistryValueKind.String)
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                }
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    if (key != null) key.DeleteValue(ValueName, false);
            }
        }
    }
}
