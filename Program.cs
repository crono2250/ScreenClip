using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenClip
{
    internal static class Program
    {
        private static Mutex singleInstance;

        [STAThread]
        private static void Main(string[] args)
        {
            bool launchedAtStartup = Array.IndexOf(args, StartupRegistration.Argument) >= 0;
            bool created;
            singleInstance = new Mutex(true, "Local\\ScreenClip_90c2ec55_42c3_4ba2_a482_806d209fc8a8", out created);
            if (!created)
            {
                if (!launchedAtStartup)
                    MessageBox.Show("ScreenClip は既に起動しています。通知領域のアイコンから操作してください。",
                        "ScreenClip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                singleInstance.Dispose();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new TrayContext(launchedAtStartup)); }
            finally
            {
                singleInstance.ReleaseMutex();
                singleInstance.Dispose();
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private readonly HotkeyWindow hotkeyWindow;
        private AppSettings settings;
        private CaptureOverlay overlay;
        private EditorForm editor;
        private SettingsForm settingsForm;

        public TrayContext(bool launchedAtStartup)
        {
            settings = SettingsStore.Load();
            trayIcon = CreateIcon();
            var menu = new ContextMenuStrip();
            menu.Items.Add("範囲を撮影", null, (s, e) => Capture());
            menu.Items.Add("設定", null, (s, e) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, (s, e) => ExitThread());
            tray = new NotifyIcon { Icon = trayIcon, Text = "ScreenClip", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => Capture();

            hotkeyWindow = new HotkeyWindow(Capture);
            if (!hotkeyWindow.Register(settings))
            {
                tray.ShowBalloonTip(6000, "ScreenClip",
                    "ホットキーを登録できませんでした。通知領域の設定から別のキーに変更してください。",
                    ToolTipIcon.Warning);
            }
            if (!launchedAtStartup)
            {
                try { StartupRegistration.Apply(settings.StartWithWindows == true); }
                catch (Exception)
                {
                    tray.ShowBalloonTip(6000, "ScreenClip",
                        "Windows サインイン時の自動起動を設定できませんでした。通知領域の設定から再試行してください。",
                        ToolTipIcon.Warning);
                }
            }
        }

        private static Icon CreateIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(Color.White, 3))
            {
                g.Clear(Color.FromArgb(32, 57, 77));
                g.DrawRectangle(pen, 5, 5, 21, 21);
                g.FillRectangle(Brushes.Red, 10, 10, 12, 12);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private void Capture()
        {
            if (overlay != null || editor != null || settingsForm != null) return;
            Bitmap desktop = null;
            try
            {
                Rectangle bounds = SystemInformation.VirtualScreen;
                desktop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(desktop))
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                overlay = new CaptureOverlay(desktop, bounds);
                desktop = null;
                overlay.SelectionCompleted += OnSelectionCompleted;
                overlay.FormClosed += (s, e) => { overlay = null; };
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception ex)
            {
                if (desktop != null) desktop.Dispose();
                if (overlay != null) { overlay.Dispose(); overlay = null; }
                MessageBox.Show("画面を撮影できませんでした。\n" + ex.Message,
                    "ScreenClip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSelectionCompleted(object sender, Bitmap selected)
        {
            CaptureOverlay selectedOverlay = overlay;
            if (selectedOverlay != null) selectedOverlay.Close();
            try
            {
                editor = new EditorForm(selected, SaveImage);
                selected = null;
                editor.FormClosed += (s, e) => { editor = null; };
                editor.Show();
                editor.Activate();
            }
            catch (Exception ex)
            {
                if (editor != null) { editor.Dispose(); editor = null; }
                MessageBox.Show("確認画面を開けませんでした。\n" + ex.Message,
                    "ScreenClip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (selected != null) selected.Dispose();
            }
        }

        private string SaveImage(Bitmap image)
        {
            string directory = string.IsNullOrWhiteSpace(settings.SaveDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory : settings.SaveDirectory;
            Directory.CreateDirectory(directory);
            string extension = settings.ImageFormat == "jpg" ? "jpg" : "png";
            string stem = DateTime.Now.ToString("yyyy-MMdd-ss", CultureInfo.InvariantCulture) +
                "(" + image.Width.ToString(CultureInfo.InvariantCulture) + " x " +
                image.Height.ToString(CultureInfo.InvariantCulture) + ")";
            string path = Path.Combine(directory, stem + "." + extension);
            for (int i = 1; File.Exists(path); i++)
                path = Path.Combine(directory, stem + "_" + i + "." + extension);

            if (extension == "jpg")
            {
                ImageCodecInfo jpeg = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.MimeType == "image/jpeg") jpeg = codec;
                if (jpeg == null) throw new InvalidOperationException("JPEG エンコーダーが見つかりません。");
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                    image.Save(path, jpeg, parameters);
                }
            }
            else image.Save(path, ImageFormat.Png);
            return path;
        }

        private void OpenSettings()
        {
            if (settingsForm != null) { settingsForm.Activate(); return; }
            SettingsForm dialog = new SettingsForm(settings.Copy());
            settingsForm = dialog;
            dialog.FormClosed += (s, e) => { settingsForm = null; };
            DialogResult result = dialog.ShowDialog();
            AppSettings next = dialog.Result;
            dialog.Dispose();
            if (result != DialogResult.OK) return;
            AppSettings previous = settings;
            if (!hotkeyWindow.Register(next))
            {
                MessageBox.Show("指定したホットキーは使用中か、登録できません。別の組み合わせを選んでください。",
                    "ScreenClip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                StartupRegistration.Apply(next.StartWithWindows == true);
                SettingsStore.Save(next);
                settings = next;
            }
            catch (Exception ex)
            {
                hotkeyWindow.Register(previous);
                try { StartupRegistration.Apply(previous.StartWithWindows == true); }
                catch (Exception) { }
                MessageBox.Show("設定を保存できませんでした。\n" + ex.Message,
                    "ScreenClip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void ExitThreadCore()
        {
            if (overlay != null) overlay.Close();
            if (editor != null) editor.Close();
            if (settingsForm != null) settingsForm.Close();
            hotkeyWindow.Dispose();
            tray.Visible = false;
            tray.Dispose();
            trayIcon.Dispose();
            base.ExitThreadCore();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x5343;
        private const int WmHotkey = 0x0312;
        private const uint NoRepeat = 0x4000;
        private readonly Action action;
        private bool registered;
        private AppSettings current;

        public HotkeyWindow(Action action)
        {
            this.action = action;
            CreateHandle(new CreateParams());
        }

        public bool Register(AppSettings settings)
        {
            AppSettings old = current;
            if (registered) UnregisterHotKey(Handle, HotkeyId);
            registered = RegisterHotKey(Handle, HotkeyId, settings.Modifiers | NoRepeat, (uint)settings.KeyCode);
            if (registered) { current = settings.Copy(); return true; }
            if (old != null)
            {
                registered = RegisterHotKey(Handle, HotkeyId, old.Modifiers | NoRepeat, (uint)old.KeyCode);
                current = registered ? old : null;
            }
            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId) action();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (registered) UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
