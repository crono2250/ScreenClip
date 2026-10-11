using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenClip
{
    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap desktop;
        private Point origin;
        private Point current;
        private bool dragging;

        public event EventHandler<Bitmap> SelectionCompleted;

        public CaptureOverlay(Bitmap desktop, Rectangle virtualBounds)
        {
            this.desktop = desktop;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Text = "範囲を選択 — Esc でキャンセル";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImageUnscaled(desktop, Point.Empty);
            using (Brush dim = new SolidBrush(Color.FromArgb(140, Color.Black)))
                e.Graphics.FillRectangle(dim, ClientRectangle);

            if (!dragging) return;
            Rectangle selection = GetSelection();
            if (selection.Width == 0 || selection.Height == 0) return;
            e.Graphics.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
            using (Pen border = new Pen(Color.FromArgb(230, 70, 150, 255), 2))
                e.Graphics.DrawRectangle(border, selection.X, selection.Y,
                    selection.Width - 1, selection.Height - 1);
            string size = selection.Width + " × " + selection.Height;
            SizeF textSize = e.Graphics.MeasureString(size, Font);
            float labelY = selection.Y > textSize.Height + 8
                ? selection.Y - textSize.Height - 5 : selection.Bottom + 5;
            RectangleF label = new RectangleF(selection.X, labelY,
                textSize.Width + 12, textSize.Height + 6);
            using (Brush background = new SolidBrush(Color.FromArgb(210, 20, 30, 45)))
                e.Graphics.FillRectangle(background, label);
            e.Graphics.DrawString(size, Font, Brushes.White, label.X + 6, label.Y + 3);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            origin = Clamp(e.Location);
            current = origin;
            dragging = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            current = Clamp(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            current = Clamp(e.Location);
            Rectangle selected = GetSelection();
            dragging = false;
            Capture = false;
            if (selected.Width < 2 || selected.Height < 2) { Invalidate(); return; }
            Bitmap crop = desktop.Clone(selected, desktop.PixelFormat);
            EventHandler<Bitmap> handler = SelectionCompleted;
            if (handler != null) handler(this, crop);
            else crop.Dispose();
        }

        private Point Clamp(Point point)
        {
            return new Point(Math.Max(0, Math.Min(desktop.Width, point.X)),
                Math.Max(0, Math.Min(desktop.Height, point.Y)));
        }

        private Rectangle GetSelection()
        {
            return Rectangle.FromLTRB(Math.Min(origin.X, current.X), Math.Min(origin.Y, current.Y),
                Math.Max(origin.X, current.X), Math.Max(origin.Y, current.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) desktop.Dispose();
            base.Dispose(disposing);
        }
    }
}
