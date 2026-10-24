using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 内部控件存在跨页面（虚拟按键页 / 虚拟手柄页）显隐差异的卡片实现本接口。
    // 卡片被展开、折叠快照恢复完成后，由 CardDock 回调本方法，让卡片按当前页面模式
    // 重新设置各子控件的期望可见性；避免折叠期间跨页切换时，展开用折叠前旧快照把
    // 上一页专属控件（如按键页 WASD 方向绑定框、显示模式条）重新恢复出来。
    public interface IPageVisibilityAware
    {
        void ReapplyPageVisibility();
    }

    // 单张可折叠 / 可拖拽卡片的运行时状态。Slot 为持久化折叠态索引，与运行时拖拽顺序无关。
    public sealed class CardDockItem
    {
        public Control Card;
        public Control TitleControl;
        public int Slot;
        public int ExpandedHeight;
        public int HeaderHeight;
        public int CenterY;
        public bool Collapsed;
        public bool Collapsible;
        public bool Draggable;
        public Panel Glyph;
        public Panel Handle;
        internal System.Windows.Forms.Timer ArmTimer;
        internal bool Armed;
        internal bool DragStarted;
        internal bool WasCollapsed;
        internal int PressOffsetY;
        internal int LastIndex;
        // 拖拽跟随视觉偏移：PressHost 为按下瞬间宿主坐标，Base 为 relayout 后的基准位置，
        // Offset 为叠加在基准上的小幅跟随量（受限范围，纯视觉，不参与排序判定）。
        internal int PressHostX;
        internal int PressHostY;
        internal int BaseX;
        internal int BaseY;
        internal int OffsetX;
        internal int OffsetY;
        internal readonly Dictionary<Control, bool> SavedVisibility = new Dictionary<Control, bool>();
    }

    // 通用卡片折叠 + 长按拖拽重排协调器：不依赖卡片继承特定基类，
    // 把手（点阵）在卡片最左、折叠箭头在卡片最右，覆盖层挂在卡片头部。
    // 折叠态只保留标题栏：隐藏标题以外的子控件，避免内容溢出遮挡折叠箭头。
    public sealed class CardDock
    {
        private readonly Panel _host;
        private readonly List<CardDockItem> _items = new List<CardDockItem>();
        private Action _relayout;
        private Action _syncOrder;
        private Action<CardDockItem> _stateChanged;
        private readonly int _headerHeight;
        private readonly Panel _insertLine;

        public CardDock(Panel host)
        {
            _host = host;
            _headerHeight = Theme.S(36);
            _insertLine = new Panel();
            _insertLine.BackColor = Theme.Accent;
            _insertLine.Height = Theme.S(3);
            _insertLine.Visible = false;
            _insertLine.Tag = "dockInsertLine";
            _host.Controls.Add(_insertLine);
            _insertLine.BringToFront();
        }

        public IReadOnlyList<CardDockItem> Items
        {
            get { return _items; }
        }

        // relayout：父容器按当前顺序与折叠高度重新纵向排列；
        // syncOrder：父容器把注册卡片按 _items 顺序同步到宿主 Controls 集合；
        // stateChanged：单张卡片折叠态变化，通知父层写回配置并保存。
        public void Bind(Action relayout, Action syncOrder, Action<CardDockItem> stateChanged)
        {
            _relayout = relayout;
            _syncOrder = syncOrder;
            _stateChanged = stateChanged;
        }

        public CardDockItem Register(Control card, int slot, int centerY, int expandedHeight,
            bool collapsible, bool draggable, bool collapsed, Control titleControl)
        {
            CardDockItem item = new CardDockItem();
            item.Card = card;
            item.TitleControl = titleControl;
            item.Slot = slot;
            item.CenterY = centerY;
            item.ExpandedHeight = expandedHeight;
            item.HeaderHeight = _headerHeight;
            item.Collapsed = collapsed;
            item.Collapsible = collapsible;
            item.Draggable = draggable;
            item.LastIndex = _items.Count;

            if (collapsible)
            {
                item.Glyph = CreateGlyph(item);
                card.Controls.Add(item.Glyph);
                item.Glyph.BringToFront();
            }
            if (draggable)
            {
                item.Handle = CreateHandle(item);
                card.Controls.Add(item.Handle);
                item.Handle.BringToFront();
            }

            ApplyVisibility(item, collapsed);
            card.Height = collapsed ? _headerHeight : expandedHeight;
            _items.Add(item);
            return item;
        }

        // 是否存在任一折叠卡片（用于一键按钮文案判断）。
        public bool AnyCollapsed
        {
            get
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].Card.Visible) continue;
                    if (_items[i].Collapsible && _items[i].Collapsed) return true;
                }
                return false;
            }
        }

        private Panel CreateGlyph(CardDockItem item)
        {
            Panel g = new Panel();
            g.Size = new Size(Theme.S(22), Theme.S(22));
            g.BackColor = Color.Transparent;
            g.Cursor = Cursors.Hand;
            g.Paint += delegate(object sender, PaintEventArgs e) { PaintGlyph(item, e.Graphics); };
            g.Click += delegate(object sender, EventArgs e) { Toggle(item); };
            RepositionGlyph(item, g);
            item.Card.Resize += delegate(object sender, EventArgs e) { RepositionGlyph(item, g); };
            return g;
        }

        private Panel CreateHandle(CardDockItem item)
        {
            Panel h = new Panel();
            h.Size = new Size(Theme.S(22), Theme.S(22));
            h.BackColor = Color.Transparent;
            h.Cursor = Cursors.SizeAll;
            PaintHandle(h);
            RepositionHandle(item, h);
            item.Card.Resize += delegate(object sender, EventArgs e) { RepositionHandle(item, h); };
            WireDrag(item, h);
            return h;
        }

        private void RepositionHandle(CardDockItem item, Panel h)
        {
            // 把手固定在卡片最左，垂直对齐标题中心线。
            int cy = Theme.S(item.CenterY);
            h.Location = new Point(Theme.S(8), cy - Theme.S(11));
        }

        private void RepositionGlyph(CardDockItem item, Panel g)
        {
            // 折叠箭头固定在卡片最右，位于头部开关外侧，不遮挡开关。
            int cy = Theme.S(item.CenterY);
            g.Location = new Point(item.Card.Width - Theme.S(30), cy - Theme.S(11));
        }

        private void PaintHandle(Panel h)
        {
            h.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float dot = Theme.S(2.2f);
                float gx = Theme.S(5f);
                float gapX = Theme.S(8f);
                float gy = Theme.S(4f);
                float gapY = Theme.S(7f);
                for (int col = 0; col < 2; col++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        float x = gx + col * gapX;
                        float y = gy + row * gapY;
                        using (SolidBrush brush = new SolidBrush(Theme.Muted))
                        {
                            e.Graphics.FillEllipse(brush, x, y, dot, dot);
                        }
                    }
                }
            };
        }

        private void PaintGlyph(CardDockItem item, Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = Theme.Scale;
            float cx = Theme.S(11f);
            float cy = Theme.S(11f);
            PointF[] pts;
            if (item.Collapsed)
            {
                // 折叠态：向右 ▸
                pts = new PointF[]
                {
                    new PointF(cx - 3.5f * s, cy - 5 * s),
                    new PointF(cx - 3.5f * s, cy + 5 * s),
                    new PointF(cx + 4.5f * s, cy)
                };
            }
            else
            {
                // 展开态：向下 ▾
                pts = new PointF[]
                {
                    new PointF(cx - 4.5f * s, cy - 3.5f * s),
                    new PointF(cx + 4.5f * s, cy - 3.5f * s),
                    new PointF(cx, cy + 4.5f * s)
                };
            }
            using (SolidBrush brush = new SolidBrush(Theme.TextSoft))
            {
                g.FillPolygon(brush, pts);
            }
        }

        // 折叠：隐藏标题以外的子控件；展开：恢复折叠前可见性。
        private void ApplyVisibility(CardDockItem item, bool collapsed)
        {
            Control.ControlCollection controls = item.Card.Controls;
            if (collapsed)
            {
                item.SavedVisibility.Clear();
                foreach (Control c in controls)
                {
                    if (c == item.Glyph || c == item.Handle) continue;
                    if (item.TitleControl != null && c == item.TitleControl) continue;
                    item.SavedVisibility[c] = c.Visible;
                    c.Visible = false;
                }
            }
            else
            {
                foreach (KeyValuePair<Control, bool> pair in item.SavedVisibility)
                {
                    if (pair.Key != null && !pair.Key.IsDisposed)
                    {
                        pair.Key.Visible = pair.Value;
                    }
                }
                item.SavedVisibility.Clear();
                // 快照恢复后，跨页显隐卡片按当前页面模式重设期望可见性，覆盖折叠前旧快照。
                IPageVisibilityAware aware = item.Card as IPageVisibilityAware;
                if (aware != null) aware.ReapplyPageVisibility();
            }
        }

        public void Toggle(CardDockItem item)
        {
            if (!item.Collapsible) return;
            SetCollapsed(item, !item.Collapsed);
        }

        private void SetCollapsed(CardDockItem item, bool collapsed)
        {
            item.Collapsed = collapsed;
            ApplyVisibility(item, collapsed);
            item.Card.Height = collapsed ? item.HeaderHeight : item.ExpandedHeight;
            if (item.Glyph != null) item.Glyph.Invalidate();
            if (_stateChanged != null) _stateChanged(item);
            if (_relayout != null) _relayout();
        }

        // 一键展开本 dock 内全部卡片。
        public void ExpandAll()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                CardDockItem item = _items[i];
                if (!item.Card.Visible) continue;
                if (!item.Collapsible) continue;
                if (!item.Collapsed) continue;
                item.Collapsed = false;
                ApplyVisibility(item, false);
                item.Card.Height = item.ExpandedHeight;
                if (item.Glyph != null) item.Glyph.Invalidate();
                if (_stateChanged != null) _stateChanged(item);
            }
            if (_relayout != null) _relayout();
        }

        // 展开指定卡片（若已展开则保持），供外部从右侧条目联动定位左侧面板。
        public void ExpandCard(Control card)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                CardDockItem item = _items[i];
                if (!ReferenceEquals(item.Card, card)) continue;
                if (item.Collapsed) SetCollapsed(item, false);
                return;
            }
        }

        // 一键收起本 dock 内全部卡片。
        public void CollapseAll()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                CardDockItem item = _items[i];
                if (!item.Card.Visible) continue;
                if (!item.Collapsible) continue;
                if (item.Collapsed) continue;
                item.Collapsed = true;
                ApplyVisibility(item, true);
                item.Card.Height = item.HeaderHeight;
                if (item.Glyph != null) item.Glyph.Invalidate();
                if (_stateChanged != null) _stateChanged(item);
            }
            if (_relayout != null) _relayout();
        }

        private void WireDrag(CardDockItem item, Panel handle)
        {
            item.ArmTimer = new System.Windows.Forms.Timer();
            item.ArmTimer.Interval = 300;
            item.ArmTimer.Tick += delegate
            {
                item.ArmTimer.Stop();
                item.Armed = true;
                item.DragStarted = false;
                item.WasCollapsed = item.Collapsed;
            };

            // 按下只在把手上触发；移动 / 抬起事件绑定到整张卡片并由卡片捕获鼠标，
            // 避免鼠标滑出小把手后事件丢失导致拖拽无反应。
            handle.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                item.Armed = false;
                item.DragStarted = false;
                item.LastIndex = _items.IndexOf(item);
                item.PressOffsetY = e.Y;
                Point hp = _host.PointToClient(Cursor.Position);
                item.PressHostX = hp.X;
                item.PressHostY = hp.Y;
                item.OffsetX = 0;
                item.OffsetY = 0;
                item.Card.Capture = true;
                item.ArmTimer.Start();
            };

            item.Card.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!item.Armed) return;
                if (e.Button != MouseButtons.Left) return;
                Point mp = _host.PointToClient(Cursor.Position);
                if (!item.DragStarted)
                {
                    item.DragStarted = true;
                    item.WasCollapsed = item.Collapsed;
                    if (item.Collapsed)
                    {
                        item.Collapsed = false;
                        ApplyVisibility(item, false);
                        item.Card.Height = item.ExpandedHeight;
                        if (_relayout != null) _relayout();
                    }
                    item.Card.BringToFront();
                    CaptureBase(item);
                }
                // 小幅跟随：按鼠标相对按下点的位移等比映射，限制在很小范围内，纯视觉反馈；
                // 目标偏移与当前偏移做一阶趋近，移动过程平滑不生硬。
                int dx = mp.X - item.PressHostX;
                int dy = mp.Y - item.PressHostY;
                int maxX = Theme.S(6);
                int maxY = Theme.S(9);
                int targetX = Clamp(dx * 18 / 100, -maxX, maxX);
                int targetY = Clamp(dy * 18 / 100, -maxY, maxY);
                item.OffsetX += (targetX - item.OffsetX) * 2 / 5;
                item.OffsetY += (targetY - item.OffsetY) * 2 / 5;

                int idx = ComputeInsertIndex(item, mp.Y);
                ShowInsertLine(idx);
                if (idx != item.LastIndex)
                {
                    ApplyReorder(item, idx);
                    if (_syncOrder != null) _syncOrder();
                    if (_relayout != null) _relayout();
                    CaptureBase(item);
                }
                ApplyOffset(item);
            };

            item.Card.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                item.ArmTimer.Stop();
                item.Card.Capture = false;
                _insertLine.Visible = false;
                if (!item.Armed)
                {
                    item.Armed = false;
                    return;
                }
                item.Armed = false;
                item.OffsetX = 0;
                item.OffsetY = 0;
                // 结束拖拽后恢复拖拽前的折叠状态，并回写配置。
                if (item.WasCollapsed)
                {
                    SetCollapsed(item, true);
                }
                else
                {
                    if (_relayout != null) _relayout();
                }
                item.DragStarted = false;
                item.WasCollapsed = false;
            };
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // relayout 后记录卡片基准位置（不含视觉偏移）。
        private void CaptureBase(CardDockItem item)
        {
            item.BaseX = item.Card.Left;
            item.BaseY = item.Card.Top;
        }

        // 在基准位置上叠加小幅跟随偏移，宽度高度保持基准布局不变。
        private void ApplyOffset(CardDockItem item)
        {
            item.Card.SetBounds(item.BaseX + item.OffsetX, item.BaseY + item.OffsetY,
                item.Card.Width, item.Card.Height);
        }

        private int ComputeInsertIndex(CardDockItem self, int mouseY)
        {
            int index = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                CardDockItem it = _items[i];
                if (it == self) continue;
                if (!it.Card.Visible) continue;
                int mid = it.Card.Top + it.Card.Height / 2;
                if (mouseY > mid) index++;
            }
            return index;
        }

        // 在目标插入位置绘制高亮指示线，让用户看清落点；隐藏卡片不参与可见排序。
        private void ShowInsertLine(int insertIndex)
        {
            List<CardDockItem> visible = new List<CardDockItem>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Card.Visible) visible.Add(_items[i]);
            }
            if (visible.Count == 0) return;
            int y;
            if (insertIndex <= 0)
            {
                y = visible[0].Card.Top - Theme.S(6);
            }
            else if (insertIndex >= visible.Count)
            {
                CardDockItem last = visible[visible.Count - 1];
                y = last.Card.Top + last.Card.Height + Theme.S(3);
            }
            else
            {
                CardDockItem before = visible[insertIndex - 1];
                y = before.Card.Top + before.Card.Height + Theme.S(3);
            }
            _insertLine.SetBounds(Theme.S(8), y, _host.Width - Theme.S(16), Theme.S(3));
            _insertLine.Visible = true;
            _insertLine.BringToFront();
        }

        private void ApplyReorder(CardDockItem item, int targetIndex)
        {
            _items.Remove(item);
            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex > _items.Count) targetIndex = _items.Count;
            _items.Insert(targetIndex, item);
            item.LastIndex = targetIndex;
        }
    }
}
