using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class GameLibraryBar : Control
    {
        private List<GameProfileEntry> _profiles = new List<GameProfileEntry>();
        private string _activePath = "";
        private readonly List<RectangleF> _tabRects = new List<RectangleF>();
        private readonly List<RectangleF> _tabAreas = new List<RectangleF>();
        private RectangleF _addRect;
        private RectangleF _saveRect;
        private RectangleF _exportRect;
        private RectangleF _importRect;
        private int _hover = -1;
        private readonly ContextMenuStrip _tabMenu;
        private string _contextPath = "";
        private bool _pendingTabMenu;

        public event Action<string> GameSelected;
        public event Action<string> GameExportRequested;
        public event Action<string> ShortcutRequested;
        public event Action<string> RenameRequested;
        public event Action AddRequested;
        public event Action SaveRequested;
        public event Action ImportRequested;

        public GameLibraryBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            Height = Theme.S(42);
            BackColor = Theme.BgSoft;
            Cursor = Cursors.Default;

            _tabMenu = new ContextMenuStrip();
            _tabMenu.Renderer = new DarkMenuRenderer();
            _tabMenu.BackColor = Theme.SurfaceAlt;
            _tabMenu.ForeColor = Theme.Text;
            _tabMenu.ShowImageMargin = false;
            _tabMenu.Font = Theme.UiFont(10.5f, false);
            _tabMenu.MinimumSize = new Size(Theme.S(250), 0);

            ToolStripMenuItem rename = StyleMenuItem(new ToolStripMenuItem("重命名"));
            rename.Click += delegate
            {
                Action<string> h = RenameRequested;
                if (h != null && _contextPath.Length > 0) h(_contextPath);
            };
            _tabMenu.Items.Add(rename);

            ToolStripMenuItem export = StyleMenuItem(new ToolStripMenuItem("导出配置"));
            export.Click += delegate
            {
                Action<string> h = GameExportRequested;
                if (h != null && _contextPath.Length > 0) h(_contextPath);
            };
            _tabMenu.Items.Add(export);

            ToolStripMenuItem shortcut = StyleMenuItem(new ToolStripMenuItem("创建快捷方式"));
            shortcut.Click += delegate
            {
                Action<string> h = ShortcutRequested;
                if (h != null && _contextPath.Length > 0) h(_contextPath);
            };
            _tabMenu.Items.Add(shortcut);
        }

        private ToolStripMenuItem StyleMenuItem(ToolStripMenuItem item)
        {
            item.AutoSize = false;
            item.Size = new Size(Theme.S(230), Theme.S(40));
            item.Padding = new Padding(Theme.S(16), 0, Theme.S(12), 0);
            item.TextAlign = ContentAlignment.MiddleLeft;
            return item;
        }

        public void SetProfiles(List<GameProfileEntry> profiles, string activePath)
        {
            _profiles = profiles == null ? new List<GameProfileEntry>() : profiles;
            _activePath = activePath == null ? "" : activePath;
            _hover = -1;
            Invalidate();
        }

        private void LayoutRects()
        {
            _tabRects.Clear();
            _tabAreas.Clear();
            float s = Theme.Scale;
            float left = Theme.S(96);
            float right = Width - Theme.S(16);
            float btnH = Theme.S(28);
            float btnY = (Height - btnH) / 2f;
            float importW = Theme.S(72);
            float exportW = Theme.S(72);
            float saveW = Theme.S(72);
            _importRect = new RectangleF(right - importW, btnY, importW, btnH);
            _exportRect = new RectangleF(_importRect.X - Theme.S(6) - exportW, btnY, exportW, btnH);
            _saveRect = new RectangleF(_exportRect.X - Theme.S(6) - saveW, btnY, saveW, btnH);
            float tabsRight = _saveRect.X - Theme.S(8);
            float addW = Theme.S(34);
            float tabW = Math.Max(Theme.S(68), Math.Min(Theme.S(130), (tabsRight - left - addW - Theme.S(20)) / Math.Max(1, _profiles.Count)));
            float x = left;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (x + tabW > tabsRight - addW - Theme.S(4)) break;
                _tabRects.Add(new RectangleF(x, btnY, tabW, btnH));
                _tabAreas.Add(new RectangleF(x, 0, tabW, Height));
                x += tabW + Theme.S(5);
            }
            _addRect = new RectangleF(x, btnY, addW, btnH);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRects();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            LayoutRects();
            using (SolidBrush bg = new SolidBrush(Theme.BgSoft)) e.Graphics.FillRectangle(bg, ClientRectangle);
            using (Pen line = new Pen(Theme.StrokeSoft, 1f))
                e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
            Theme.DrawText(e.Graphics, "游戏配置", Theme.UiFont(7.8f, false), Theme.Faint,
                new RectangleF(Theme.S(16), 0, Theme.S(74), Height), StringAlignment.Near, StringAlignment.Center, true);

            for (int i = 0; i < _tabRects.Count && i < _profiles.Count; i++)
            {
                GameProfileEntry entry = _profiles[i];
                bool active = entry.Path == _activePath;
                Color fill = active ? Theme.AccentDeep : (_hover == i ? Theme.SurfaceHi : Theme.SurfaceAlt);
                Theme.FillRounded(e.Graphics, _tabRects[i], Theme.S(7), fill);
                Theme.DrawRounded(e.Graphics, _tabRects[i], Theme.S(7), active ? Theme.Accent : Theme.StrokeSoft, 1f);
                Theme.DrawText(e.Graphics, entry.Name, Theme.UiFont(7.8f, active), active ? Color.White : Theme.Muted,
                    new RectangleF(_tabRects[i].X + Theme.S(8), _tabRects[i].Y,
                        _tabRects[i].Width - Theme.S(16), _tabRects[i].Height),
                    StringAlignment.Center, StringAlignment.Center, true);
            }

            DrawAction(e.Graphics, _addRect, "+", false);
            DrawAction(e.Graphics, _saveRect, "保存游戏", false);
            DrawAction(e.Graphics, _exportRect, "导出", false);
            DrawAction(e.Graphics, _importRect, "导入", true);
        }

        private void DrawAction(Graphics g, RectangleF rect, string text, bool primary)
        {
            Theme.FillRounded(g, rect, Theme.S(7), primary ? Theme.Alpha(Theme.Accent, 55) : Theme.SurfaceAlt);
            Theme.DrawRounded(g, rect, Theme.S(7), primary ? Theme.Accent : Theme.StrokeSoft, 1f);
            Theme.DrawText(g, text, Theme.UiFont(text == "+" ? 12f : 7.8f, true), Color.White, rect,
                StringAlignment.Center, StringAlignment.Center, true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hover = -1;
            for (int i = 0; i < _tabAreas.Count; i++) if (_tabAreas[i].Contains(e.Location)) hover = i;
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pendingTabMenu)
            {
                _pendingTabMenu = false;
                if (ClientRectangle.Contains(e.Location))
                {
                    _tabMenu.Show(this, PointToScreen(e.Location));
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            LayoutRects();
            for (int i = 0; i < _tabAreas.Count && i < _profiles.Count; i++)
            {
                if (!_tabAreas[i].Contains(e.Location)) continue;
                _contextPath = _profiles[i].Path;
                if (e.Button == MouseButtons.Left && _profiles[i].Path != _activePath)
                {
                    Action<string> select = GameSelected;
                    if (select != null) select(_profiles[i].Path);
                }
                // Show the menu after the mouse button is released. Showing it on
                // MouseDown lets the following MouseUp click the first menu item.
                _pendingTabMenu = true;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (_addRect.Contains(e.Location))
            {
                Action h = AddRequested; if (h != null) h();
            }
            else if (_saveRect.Contains(e.Location))
            {
                Action h = SaveRequested; if (h != null) h();
            }
            else if (_exportRect.Contains(e.Location))
            {
                Action<string> h = GameExportRequested; if (h != null) h(_activePath);
            }
            else if (_importRect.Contains(e.Location))
            {
                Action h = ImportRequested; if (h != null) h();
            }
            base.OnMouseDown(e);
        }
    }
}
