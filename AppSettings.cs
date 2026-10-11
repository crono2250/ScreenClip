using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace ScreenClip
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public uint Modifiers { get; set; }
        [DataMember] public int KeyCode { get; set; }
        [DataMember] public string ImageFormat { get; set; }
        [DataMember] public string SaveDirectory { get; set; }

        public AppSettings()
        {
            Modifiers = 0x0002 | 0x0004; // Ctrl + Shift
            KeyCode = (int)Keys.S;
            ImageFormat = "png";
            SaveDirectory = "";
        }

        public AppSettings Copy()
        {
            return new AppSettings
            {
                Modifiers = Modifiers,
                KeyCode = KeyCode,
                ImageFormat = ImageFormat,
                SaveDirectory = SaveDirectory
            };
        }

        public string HotkeyText
        {
            get { return HotkeyTextFor(Modifiers, KeyCode); }
        }

        public static string HotkeyTextFor(uint modifiers, int keyCode)
        {
            string text = "";
            if ((modifiers & 0x0002) != 0) text += "Ctrl + ";
            if ((modifiers & 0x0001) != 0) text += "Alt + ";
            if ((modifiers & 0x0004) != 0) text += "Shift + ";
            if ((modifiers & 0x0008) != 0) text += "Win + ";
            Keys key = (Keys)keyCode;
            if (key == Keys.PrintScreen) return text + "PrintScreen";
            if (key >= Keys.D0 && key <= Keys.D9) return text + ((int)key - (int)Keys.D0);
            return text + key;
        }
    }

    internal static class SettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenClip", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();
                using (FileStream stream = File.OpenRead(SettingsPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = (AppSettings)serializer.ReadObject(stream);
                    if (settings == null || settings.KeyCode == 0 ||
                        (settings.ImageFormat != "png" && settings.ImageFormat != "jpg"))
                        return new AppSettings();
                    if (settings.SaveDirectory == null) settings.SaveDirectory = "";
                    return settings;
                }
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            string directory = Path.GetDirectoryName(SettingsPath);
            Directory.CreateDirectory(directory);
            string temporary = SettingsPath + ".tmp";
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings);
            }
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
        }
    }
}
