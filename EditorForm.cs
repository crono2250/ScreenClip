using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenClip
{
    internal enum EditMode { None, Blackout, RedFrame }

    internal sealed class EditorForm : Form
    {
        private readonly ImageCanvas canvas;
        private readonly Func<Bitmap, string> saveAction;
        private readonly Label instruction;
        private readonly Button undo;
        private readonly Button black;
        private readonly Button red;

        public EditorForm(Bitmap selected, Func<Bitmap, string> saveAction)
        {
            this.saveAction = saveAction;
            Text = "ScreenClip — 保存前に確認";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1050, 740);
            MinimumSize = new Size(650, 430);
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Yu Gothic UI", 10);
            KeyPreview = true;

            var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White };
            header.Controls.Add(new Label
            {
                Text = "撮影した範囲を確認",
                Font = new Font("Yu Gothic UI", 15, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 45, 65),
                Location = new Point(22, 13), AutoSize = true
            });
            instruction = new Label
            {
                Text = selected.Width + " × " + selected.Height + " px  •  必要なら黒塗りや赤枠を追加してください。",
                ForeColor = Color.FromArgb(85, 100, 115),
                Location = new Point(24, 48), AutoSize = true
            };
            header.Controls.Add(instruction);
            Controls.Add(header);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Color.White };
            Controls.Add(footer);

            canvas = new ImageCanvas(selected) { Dock = DockStyle.Fill, Margin = new Padding(15) };
            canvas.AnnotationApplied += (s, e) => { undo.Enabled = canvas.CanUndo; SetMode(EditMode.None); };
            Controls.Add(canvas);
            canvas.BringToFront();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(14, 18, 14, 10)
            };
            footer.Controls.Add(buttons);

            Button save = MakeButton("保存", 100);
            save.BackColor = Color.FromArgb(30, 105, 205);
            save.ForeColor = Color.White;
            save.Click += (s, e) => Save();
            Button cancel = MakeButton("キャンセル", 100);
            cancel.Click += (s, e) => Close();
            red = MakeButton("赤枠", 90);
            red.Click += (s, e) => SetMode(canvas.Mode == EditMode.RedFrame ? EditMode.None : EditMode.RedFrame);
            black = MakeButton("黒塗", 90);
            black.Click += (s, e) => SetMode(canvas.Mode == EditMode.Blackout ? EditMode.None : EditMode.Blackout);
            undo = MakeButton("元に戻す", 105);
            undo.Enabled = false;
            undo.Click += (s, e) => { canvas.Undo(); undo.Enabled = canvas.CanUndo; };
            buttons.Controls.AddRange(new Control[] { save, cancel, red, black, undo });
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static Button MakeButton(string text, int width)
        {
            return new Button
            {
                Text = text, Width = width, Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(238, 243, 248),
                ForeColor = Color.FromArgb(35, 55, 75),
                Margin = new Padding(5, 0, 5, 0)
            };
        }

        private void SetMode(EditMode mode)
        {
            canvas.Mode = mode;
            black.BackColor = mode == EditMode.Blackout ? Color.FromArgb(198, 225, 255) : Color.FromArgb(238, 243, 248);
            red.BackColor = mode == EditMode.RedFrame ? Color.FromArgb(255, 215, 215) : Color.FromArgb(238, 243, 248);
            instruction.Text = mode == EditMode.None
                ? canvas.Image.Width + " × " + canvas.Image.Height + " px  •  必要なら黒塗りや赤枠を追加してください。"
                : "画像上をドラッグして " + (mode == EditMode.Blackout ? "黒塗り" : "赤枠") + " の範囲を指定してください。Esc で解除できます。";
        }

        private void Save()
        {
            try
            {
                saveAction(canvas.Image);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存できませんでした。\n" + ex.Message,
                    "ScreenClip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && canvas.Mode != EditMode.None)
            {
                SetMode(EditMode.None);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z))
            {
                canvas.Undo();
                undo.Enabled = canvas.CanUndo;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

    }

    internal sealed class ImageCanvas : Control
    {
        private readonly Stack<Bitmap> history = new Stack<Bitmap>();
        private Bitmap image;
        private EditMode mode;
        private bool dragging;
        private Point origin;
        private Point current;

        public event EventHandler AnnotationApplied;

        public Bitmap Image { get { return image; } }
        public bool CanUndo { get { return history.Count > 0; } }
        public EditMode Mode
        {
            get { return mode; }
            set { mode = value; dragging = false; Cursor = mode == EditMode.None ? Cursors.Default : Cursors.Cross; Invalidate(); }
        }

        public ImageCanvas(Bitmap image)
        {
            this.image = image;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(38, 48, 61);
        }

        private Rectangle ImageRect
        {
            get
            {
                int width = Math.Max(1, ClientSize.Width - 40);
                int height = Math.Max(1, ClientSize.Height - 40);
                double scale = Math.Min((double)width / image.Width, (double)height / image.Height);
                scale = Math.Min(scale, 1.0);
                int displayWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                int displayHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
                return new Rectangle((ClientSize.Width - displayWidth) / 2,
                    (ClientSize.Height - displayHeight) / 2, displayWidth, displayHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle imageRect = ImageRect;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(image, imageRect);
            using (Pen border = new Pen(Color.FromArgb(110, 130, 150)))
                e.Graphics.DrawRectangle(border, imageRect.X - 1, imageRect.Y - 1,
                    imageRect.Width + 1, imageRect.Height + 1);
            if (!dragging) return;
            Rectangle selected = ToDisplayRect(GetSelection());
            if (selected.Width < 1 || selected.Height < 1) return;
            Color color = mode == EditMode.Blackout ? Color.Black : Color.Red;
            using (Brush fill = new SolidBrush(Color.FromArgb(75, color)))
                e.Graphics.FillRectangle(fill, selected);
            using (Pen pen = new Pen(color, 2))
                e.Graphics.DrawRectangle(pen, selected);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (mode == EditMode.None || e.Button != MouseButtons.Left || !ImageRect.Contains(e.Location)) return;
            origin = ToImagePoint(e.Location);
            current = origin;
            dragging = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            current = ToImagePoint(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            current = ToImagePoint(e.Location);
            Rectangle rect = GetSelection();
            dragging = false;
            Capture = false;
            if (rect.Width < 2 || rect.Height < 2) { Invalidate(); return; }
            history.Push((Bitmap)image.Clone());
            using (Graphics g = Graphics.FromImage(image))
            {
                if (mode == EditMode.Blackout) g.FillRectangle(Brushes.Black, rect);
                else
                {
                    int thickness = Math.Max(2, Math.Min(6, Math.Min(image.Width, image.Height) / 250));
                    using (Pen pen = new Pen(Color.Red, thickness))
                    {
                        pen.Alignment = PenAlignment.Inset;
                        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                    }
                }
            }
            Invalidate();
            EventHandler handler = AnnotationApplied;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private Point ToImagePoint(Point point)
        {
            Rectangle rect = ImageRect;
            int x = (int)((long)(point.X - rect.X) * image.Width / rect.Width);
            int y = (int)((long)(point.Y - rect.Y) * image.Height / rect.Height);
            return new Point(Math.Max(0, Math.Min(image.Width - 1, x)),
                Math.Max(0, Math.Min(image.Height - 1, y)));
        }

        private Rectangle GetSelection()
        {
            return Rectangle.FromLTRB(Math.Min(origin.X, current.X), Math.Min(origin.Y, current.Y),
                Math.Max(origin.X, current.X) + 1, Math.Max(origin.Y, current.Y) + 1);
        }

        private Rectangle ToDisplayRect(Rectangle rect)
        {
            Rectangle display = ImageRect;
            int left = display.X + (int)Math.Round((double)rect.Left * display.Width / image.Width);
            int top = display.Y + (int)Math.Round((double)rect.Top * display.Height / image.Height);
            int right = display.X + (int)Math.Round((double)rect.Right * display.Width / image.Width);
            int bottom = display.Y + (int)Math.Round((double)rect.Bottom * display.Height / image.Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            image.Dispose();
            image = history.Pop();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                image.Dispose();
                foreach (Bitmap bitmap in history) bitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
