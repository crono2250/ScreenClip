using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ScreenClip
{
    internal sealed class SettingsForm : Form
    {
        private readonly HotkeyCaptureBox hotkey;
        private readonly ComboBox format;
        private readonly TextBox directory;
        private readonly CheckBox startWithWindows;
        private readonly CheckBox autoUpdate;
        private readonly NumericUpDown updateInterval;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings initial)
        {
            Text = "ScreenClip — 設定";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(550, 505);
            Font = new Font("Yu Gothic UI", 10);

            Controls.Add(new Label { Text = "撮影ホットキー", AutoSize = true, Location = new Point(25, 22) });
            hotkey = new HotkeyCaptureBox(initial)
            {
                Location = new Point(25, 50),
                Size = new Size(486, 32)
            };
            Controls.Add(hotkey);
            Controls.Add(new Label
            {
                Text = "この欄をクリックし、使いたいキーの組み合わせを押してください（PrintScreen 単体も可）。",
                AutoSize = true, Location = new Point(25, 90),
                ForeColor = Color.FromArgb(90, 105, 120),
                Font = new Font("Yu Gothic UI", 8.5f)
            });

            Controls.Add(new Label { Text = "保存形式", AutoSize = true, Location = new Point(25, 126) });
            format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(25, 154), Width = 160 };
            format.Items.AddRange(new object[] { "PNG", "JPG" });
            format.SelectedIndex = initial.ImageFormat == "jpg" ? 1 : 0;
            Controls.Add(format);

            Controls.Add(new Label { Text = "保存先フォルダー（空欄ならアプリと同じ場所）", AutoSize = true, Location = new Point(25, 203) });
            directory = new TextBox { Location = new Point(25, 231), Width = 405, Text = initial.SaveDirectory };
            Controls.Add(directory);
            var browse = new Button { Text = "参照…", Location = new Point(438, 228), Size = new Size(73, 32) };
            browse.Click += (s, e) => BrowseDirectory();
            Controls.Add(browse);

            startWithWindows = new CheckBox
            {
                Text = "Windows サインイン時に自動起動する",
                Location = new Point(25, 277), Size = new Size(390, 28),
                Checked = initial.StartWithWindows == true
            };
            Controls.Add(startWithWindows);

            autoUpdate = new CheckBox
            {
                Text = "GitHub Releases から自動更新する",
                Location = new Point(25, 315), Size = new Size(390, 28),
                Checked = initial.AutoUpdateEnabled == true
            };
            Controls.Add(autoUpdate);
            Controls.Add(new Label { Text = "確認間隔（分）", AutoSize = true, Location = new Point(25, 357) });
            updateInterval = new NumericUpDown
            {
                Location = new Point(165, 353), Width = 90, Minimum = 5, Maximum = 1440,
                Value = Math.Max(5, Math.Min(1440, initial.UpdateIntervalMinutes))
            };
            Controls.Add(updateInterval);
            Controls.Add(new Label
            {
                Text = "トークンや GitHub へのログインは不要です。\n撮影・編集・設定画面を閉じてから、更新して自動で再起動します。",
                Location = new Point(25, 399), Size = new Size(495, 44),
                ForeColor = Color.FromArgb(90, 105, 120), Font = new Font("Yu Gothic UI", 9)
            });

            var version = new LinkLabel
            {
                Text = "Version: " + VersionInfo.DisplayVersion,
                Location = new Point(25, 459), AutoSize = true,
                LinkColor = Color.FromArgb(60, 90, 125),
                Font = new Font("Yu Gothic UI", 9)
            };
            version.LinkClicked += (s, e) =>
            {
                if (VersionInfo.CommitId.Length == 40)
                    System.Diagnostics.Process.Start(VersionInfo.CommitUrl);
            };
            Controls.Add(version);

            var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(321, 452), Size = new Size(95, 34) };
            var save = new Button { Text = "保存", Location = new Point(423, 452), Size = new Size(88, 34) };
            save.Click += (s, e) => Save();
            Controls.Add(cancel);
            Controls.Add(save);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void BrowseDirectory()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "スクリーンショットの保存先を選択してください";
                if (Directory.Exists(directory.Text)) dialog.SelectedPath = directory.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) directory.Text = dialog.SelectedPath;
            }
        }

        private void Save()
        {
            string path = directory.Text.Trim();
            if (path.Length > 0 && !Directory.Exists(path))
            {
                MessageBox.Show(this, "保存先フォルダーが存在しません。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = new AppSettings
            {
                Modifiers = hotkey.Modifiers,
                KeyCode = hotkey.KeyCode,
                ImageFormat = format.SelectedIndex == 1 ? "jpg" : "png",
                SaveDirectory = path,
                StartWithWindows = startWithWindows.Checked,
                AutoUpdateEnabled = autoUpdate.Checked,
                UpdateIntervalMinutes = (int)updateInterval.Value
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
