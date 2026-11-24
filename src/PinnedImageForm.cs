using System;
using System.Drawing;
using System.Windows.Forms;

namespace Shiying
{
    sealed class PinnedImageForm : Form
    {
        readonly Bitmap _image;
        bool _imageDisposed;
        bool _dragging;
        Point _dragStart;
        Point _windowStart;

        public PinnedImageForm(Bitmap image, Point location)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.SizeAll;
            Location = location;
            ClientSize = image.Size;
            MinimumSize = new Size(1, 1);
            MaximumSize = image.Size;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImageUnscaled(_image, Point.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                Close();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _windowStart = Location;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }

            Point now = Cursor.Position;
            Location = new Point(
                _windowStart.X + now.X - _dragStart.X,
                _windowStart.Y + now.Y - _dragStart.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_imageDisposed)
            {
                _imageDisposed = true;
                _image.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
