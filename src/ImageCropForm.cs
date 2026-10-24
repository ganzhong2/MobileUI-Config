using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class ImageCropForm : Form
    {
        private readonly Bitmap _image;
        private readonly PictureBox _view;
        private readonly Label _hint;
        private readonly ModernButton _ok;
        private readonly ModernButton _cancel;
        private Point _start;
        private Point _end;
        private bool _dragging;
        private bool _hasSelection;

        public Bitmap ResultBitmap { get; private set; }

        public ImageCropForm(Bitmap image)
        {
            _image = image == null ? null : new Bitmap(image);
            Text = "裁剪图片";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Bg;
            ClientSize = new Size(Theme.S(820), Theme.S(610));

            _hint = new Label();
            _hint.AutoSize = false;
            _hint.BackColor = Theme.BgSoft;
            _hint.ForeColor = Theme.Text;
            _hint.Font = Theme.UiFont(8.2f, false);
            _hint.TextAlign = ContentAlignment.MiddleCenter;
            _hint.Text = "拖动鼠标框选要识别的区域；建议裁切 60–320 px";
            Controls.Add(_hint);

            _view = new PictureBox();
            _view.BackColor = Color.FromArgb(8, 12, 20);
            _view.SizeMode = PictureBoxSizeMode.Zoom;
            _view.Cursor = Cursors.Cross;
            _view.Image = _image;
            _view.MouseDown += ViewOnMouseDown;
            _view.MouseMove += ViewOnMouseMove;
            _view.MouseUp += ViewOnMouseUp;
            _view.Paint += ViewOnPaint;
            Controls.Add(_view);

            _ok = new ModernButton();
            _ok.Text = "裁剪并使用";
            _ok.Style = ModernButtonStyle.Primary;
            _ok.Click += delegate
            {
                if (!_hasSelection) return;
                Rectangle crop = SelectionInImage();
                if (crop.Width < 4 || crop.Height < 4) return;
                ResultBitmap = _image.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_ok);

            _cancel = new ModernButton();
            _cancel.Text = "取消";
            _cancel.Style = ModernButtonStyle.Secondary;
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancel);

            Shown += delegate
            {
                OnResize(EventArgs.Empty);
                Rectangle area = ImageArea();
                int w = Math.Min(Theme.S(240), Math.Max(Theme.S(80), area.Width / 2));
                int h = Math.Min(Theme.S(180), Math.Max(Theme.S(60), area.Height / 2));
                _start = new Point(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2);
                _end = new Point(_start.X + w, _start.Y + h);
                _hasSelection = true;
                UpdateHint();
                _view.Invalidate();
            };
        }

        private Rectangle ImageArea()
        {
            if (_image == null) return Rectangle.Empty;
            float scale = Math.Min((float)_view.ClientSize.Width / _image.Width,
                (float)_view.ClientSize.Height / _image.Height);
            int w = Math.Max(1, (int)Math.Round(_image.Width * scale));
            int h = Math.Max(1, (int)Math.Round(_image.Height * scale));
            return new Rectangle((_view.ClientSize.Width - w) / 2, (_view.ClientSize.Height - h) / 2, w, h);
        }

        private Rectangle SelectionRect()
        {
            int x = Math.Min(_start.X, _end.X);
            int y = Math.Min(_start.Y, _end.Y);
            int w = Math.Abs(_end.X - _start.X);
            int h = Math.Abs(_end.Y - _start.Y);
            return new Rectangle(x, y, w, h);
        }

        private Rectangle SelectionInImage()
        {
            Rectangle area = ImageArea();
            Rectangle sel = SelectionRect();
            if (area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;
            float sx = (float)_image.Width / area.Width;
            float sy = (float)_image.Height / area.Height;
            int x = (int)Math.Round((sel.Left - area.Left) * sx);
            int y = (int)Math.Round((sel.Top - area.Top) * sy);
            int w = (int)Math.Round(sel.Width * sx);
            int h = (int)Math.Round(sel.Height * sy);
            x = Math.Max(0, Math.Min(_image.Width - 1, x));
            y = Math.Max(0, Math.Min(_image.Height - 1, y));
            w = Math.Max(1, Math.Min(_image.Width - x, w));
            h = Math.Max(1, Math.Min(_image.Height - y, h));
            return new Rectangle(x, y, w, h);
        }

        private void UpdateHint()
        {
            if (!_hasSelection || _image == null) return;
            Rectangle crop = SelectionInImage();
            _hint.Text = "拖动鼠标框选要识别的区域；建议裁切 60–320 px    当前：" +
                crop.Width + " × " + crop.Height + " px";
        }

        private void ViewOnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Rectangle area = ImageArea();
            _start = new Point(Math.Max(area.Left, Math.Min(area.Right, e.X)),
                Math.Max(area.Top, Math.Min(area.Bottom, e.Y)));
            _end = _start;
            _dragging = true;
            _hasSelection = true;
            _view.Invalidate();
        }

        private void ViewOnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Rectangle area = ImageArea();
            _end = new Point(Math.Max(area.Left, Math.Min(area.Right, e.X)),
                Math.Max(area.Top, Math.Min(area.Bottom, e.Y)));
            UpdateHint();
            _view.Invalidate();
        }

        private void ViewOnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = false;
            if (SelectionRect().Width < 4 || SelectionRect().Height < 4) _hasSelection = false;
            UpdateHint();
            _view.Invalidate();
        }

        private void ViewOnPaint(object sender, PaintEventArgs e)
        {
            if (!_hasSelection) return;
            Rectangle area = ImageArea();
            Rectangle sel = SelectionRect();
            sel.Intersect(area);
            if (sel.Width <= 0 || sel.Height <= 0) return;
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(dim, area.Left, area.Top, area.Width, sel.Top - area.Top);
                e.Graphics.FillRectangle(dim, area.Left, sel.Bottom, area.Width, area.Bottom - sel.Bottom);
                e.Graphics.FillRectangle(dim, area.Left, sel.Top, sel.Left - area.Left, sel.Height);
                e.Graphics.FillRectangle(dim, sel.Right, sel.Top, area.Right - sel.Right, sel.Height);
            }
            using (Pen pen = new Pen(Theme.Accent, 2f))
            {
                e.Graphics.DrawRectangle(pen, sel);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_hint == null) return;
            int pad = Theme.S(16);
            _hint.SetBounds(pad, Theme.S(10), ClientSize.Width - pad * 2, Theme.S(28));
            int bottom = Theme.S(58);
            _view.SetBounds(pad, Theme.S(44), ClientSize.Width - pad * 2,
                ClientSize.Height - Theme.S(44) - bottom);
            _cancel.SetBounds(ClientSize.Width - pad - Theme.S(100), ClientSize.Height - Theme.S(46),
                Theme.S(100), Theme.S(32));
            _ok.SetBounds(_cancel.Left - Theme.S(8) - Theme.S(130), ClientSize.Height - Theme.S(46),
                Theme.S(130), Theme.S(32));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null) _image.Dispose();
            base.Dispose(disposing);
        }
    }
}
