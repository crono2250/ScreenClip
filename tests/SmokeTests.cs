using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ScreenClip
{
    internal static class SmokeTests
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                TestCrop();
                TestBlackoutAndUndo();
                TestRedFrame();
                TestHotkeyEntry();
                TestStartupSettings();
                UpdateTests.Run();
                Console.WriteLine("OK: crop, blackout, undo, red frame, hotkey entry, startup settings, automatic updates");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void Mouse(Control control, string method, int x, int y)
        {
            MethodInfo target = control.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            target.Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
        }

        private static void TestCrop()
        {
            var source = new Bitmap(100, 80);
            using (Graphics g = Graphics.FromImage(source)) g.Clear(Color.CornflowerBlue);
            using (var overlay = new CaptureOverlay(source, new Rectangle(0, 0, 100, 80)))
            {
                Bitmap result = null;
                overlay.SelectionCompleted += (s, bitmap) => { result = bitmap; };
                Mouse(overlay, "OnMouseDown", 10, 11);
                Mouse(overlay, "OnMouseMove", 40, 35);
                Mouse(overlay, "OnMouseUp", 40, 35);
                if (result == null || result.Width != 30 || result.Height != 24 ||
                    result.GetPixel(0, 0).ToArgb() != Color.CornflowerBlue.ToArgb())
                    throw new Exception("Crop test failed");
                result.Dispose();
            }
        }

        private static void TestBlackoutAndUndo()
        {
            var bitmap = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.White);
            using (var canvas = new ImageCanvas(bitmap) { Size = new Size(200, 200) })
            {
                canvas.Mode = EditMode.Blackout;
                Mouse(canvas, "OnMouseDown", 60, 60);
                Mouse(canvas, "OnMouseMove", 90, 90);
                Mouse(canvas, "OnMouseUp", 90, 90);
                if (canvas.Image.GetPixel(25, 25).ToArgb() != Color.Black.ToArgb() || !canvas.CanUndo)
                    throw new Exception("Blackout test failed");
                canvas.Undo();
                if (canvas.Image.GetPixel(25, 25).ToArgb() != Color.White.ToArgb())
                    throw new Exception("Undo test failed");
            }
        }

        private static void TestRedFrame()
        {
            var bitmap = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.White);
            using (var canvas = new ImageCanvas(bitmap) { Size = new Size(200, 200) })
            {
                canvas.Mode = EditMode.RedFrame;
                Mouse(canvas, "OnMouseDown", 60, 60);
                Mouse(canvas, "OnMouseMove", 90, 90);
                Mouse(canvas, "OnMouseUp", 90, 90);
                if (canvas.Image.GetPixel(10, 10).R < 200 ||
                    canvas.Image.GetPixel(25, 25).ToArgb() != Color.White.ToArgb())
                    throw new Exception("Red frame test failed");
            }
        }

        private static void TestHotkeyEntry()
        {
            using (var input = new HotkeyCaptureBox(new AppSettings()))
            {
                MethodInfo down = typeof(HotkeyCaptureBox).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo up = typeof(HotkeyCaptureBox).GetMethod("OnKeyUp", BindingFlags.NonPublic | BindingFlags.Instance);
                down.Invoke(input, new object[] { new KeyEventArgs(Keys.Control | Keys.Shift | Keys.P) });
                if (input.Modifiers != 6 || input.KeyCode != (int)Keys.P)
                    throw new Exception("Key combination entry failed");
                up.Invoke(input, new object[] { new KeyEventArgs(Keys.PrintScreen) });
                if (input.Modifiers != 0 || input.KeyCode != (int)Keys.PrintScreen ||
                    input.Text != "PrintScreen")
                    throw new Exception("PrintScreen entry failed");
            }
        }

        private static void TestStartupSettings()
        {
            const string oldJson = "{\"ImageFormat\":\"png\",\"KeyCode\":83,\"Modifiers\":6,\"SaveDirectory\":\"\"}";
            const string disabledJson = "{\"ImageFormat\":\"png\",\"KeyCode\":83,\"Modifiers\":6,\"SaveDirectory\":\"\",\"StartWithWindows\":false}";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(oldJson)))
            {
                AppSettings previous = SettingsStore.Read(stream);
                if (previous.StartWithWindows != true || previous.KeyCode != 83)
                    throw new Exception("Existing settings startup migration failed");
            }
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(disabledJson)))
            {
                AppSettings disabled = SettingsStore.Read(stream);
                if (disabled.StartWithWindows != false || disabled.Copy().StartWithWindows != false)
                    throw new Exception("Disabled startup setting was lost");
            }
            string command = StartupRegistration.CommandFor(@"C:\Program Files\ScreenClip\ScreenClip.exe");
            if (command != "\"C:\\Program Files\\ScreenClip\\ScreenClip.exe\" --autostart")
                throw new Exception("Startup command quoting failed");
        }
    }
}
