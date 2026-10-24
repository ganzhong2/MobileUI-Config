
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MobileUiConfig
{
    public class RoundTextBox : UserControl    
  {        private readonly TextBox _textBox;
            private string _placeholder = "";
            private bool _focused;
            private Color _fillColor = Theme.SurfaceAlt;
            private Color _borderColor = Theme.StrokeSoft;
            private int _radius = 10;
            public RoundTextBox()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                  BackColor = Color.Transparent;
                  Height = Theme.S(40);
                  Font = Theme.UiFont(9.4f, false);
                  _textBox = new TextBox();
                  _textBox.BorderStyle = BorderStyle.None;
                  _textBox.BackColor = _fillColor;
                  _textBox.ForeColor = Theme.Text;
                  _textBox.Font = Font;
                  _textBox.AutoSize = false;
                  _textBox.Height = Theme.S(28);
                  _textBox.TabStop = true;
                  _textBox.TextChanged += new EventHandler(TextBoxOnTextChanged);
                  _textBox.GotFocus += new EventHandler(TextBoxOnGotFocus);
                  _textBox.LostFocus += new EventHandler(TextBoxOnLostFocus);
                  _textBox.KeyDown += new KeyEventHandler(TextBoxOnKeyDown);
                  _textBox.HandleCreated += delegate 
      { ApplyCue();
         
      }
;
                  Controls.Add(_textBox);
                  Invalidate();
              
    }
public TextBox InnerTextBox        
    {            get 
      { return _textBox;
         
      }
}        public string Placeholder        
    {            get 
      { return _placeholder;
         
      }
set            
      {                _placeholder = value == null ? "" : value;
                        ApplyCue();
                        Invalidate();
                    
      }
}        public Color FillColor        
    {            get 
      { return _fillColor;
         
      }
set            
      {                _fillColor = value;
                        _textBox.BackColor = value;
                        Invalidate();
                    
      }
}        public Color BorderColor        
    {            get 
      { return _borderColor;
         
      }
set 
      { _borderColor = value;
         Invalidate();
         
      }
}        public int Radius        
    {            get 
      { return _radius;
         
      }
set 
      { _radius = value;
         Invalidate();
         
      }
}        public bool ReadOnly        
    {            get 
      { return _textBox.ReadOnly;
         
      }
set            
      {                _textBox.ReadOnly = value;
                        _textBox.BackColor = _fillColor;
                        _textBox.ForeColor = value ? Theme.TextSoft : Theme.Text;
                    
      }
}        public override string Text        
    {            get 
      { return _textBox.Text;
         
      }
set 
      { _textBox.Text = value == null ? "" : value;
         
      }
}        protected override void OnHandleCreated(EventArgs e)        
    {            base.OnHandleCreated(e);
                  ApplyCue();
              
    }
private void ApplyCue()        
    {            if (!_textBox.IsHandleCreated) return;
                  try            
      {                NativeMethods.SendMessage(_textBox.Handle, NativeMethods.EM_SETCUEBANNER, new IntPtr(1), _placeholder);
                    
      }
catch            
      {            
      }
}        protected override void OnResize(EventArgs e)        
    {            base.OnResize(e);
                  LayoutInner();
              
    }
protected override void OnFontChanged(EventArgs e)        
    {            base.OnFontChanged(e);
                  if (_textBox != null) _textBox.Font = Font;
                  LayoutInner();
              
    }
private void LayoutInner()        
    {            if (_textBox == null) return;
                  int pad = Theme.S(13);
                  _textBox.Height = Math.Max(_textBox.Font.Height + Theme.S(5), Theme.S(28));
                  _textBox.Location = new Point(pad, Math.Max(0, (Height - _textBox.Height) / 2));
                  _textBox.Width = Math.Max(10, Width - pad * 2);
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                  Theme.FillRounded(e.Graphics, rect, _radius, _fillColor);
                  Color border = _focused ? Theme.Alpha(Theme.Accent, 210) : _borderColor;
                  Theme.DrawRounded(e.Graphics, rect, _radius, border, _focused ? 1.4f : 1f);
                  if (_focused)            
      {                using (Pen p = new Pen(Theme.Alpha(Theme.Accent, 26), 4f))                
        {                    Theme.DrawRounded(e.Graphics, rect, _radius, Theme.Alpha(Theme.Accent, 26), 4f);
                          
        }
}            base.OnPaint(e);
              
    }
protected override void OnPaintBackground(PaintEventArgs e)        
    {        
    }
private void TextBoxOnTextChanged(object sender, EventArgs e)        
    {            OnTextChanged(e);
              
    }
private void TextBoxOnGotFocus(object sender, EventArgs e)        
    {            _focused = true;
                  Invalidate();
              
    }
private void TextBoxOnLostFocus(object sender, EventArgs e)        
    {            _focused = false;
                  Invalidate();
              
    }
private void TextBoxOnKeyDown(object sender, KeyEventArgs e)        
    {            OnKeyDown(e);
              
    }
protected override void OnClick(EventArgs e)        
    {            _textBox.Focus();
                  base.OnClick(e);
              
    }
}
    public class ToggleSwitch : Control    
  {        private bool _checked;
            private bool _hover;
            private bool _pressed;
            public event EventHandler CheckedChanged;
            public ToggleSwitch()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                  Size = new Size(Theme.S(46), Theme.S(26));
                  Cursor = Cursors.Hand;
                  TabStop = true;
              
    }
public bool Checked        
    {            get 
      { return _checked;
         
      }
set            
      {                if (_checked == value) return;
                        _checked = value;
                        Invalidate();
                        EventHandler h = CheckedChanged;
                        if (h != null) h(this, EventArgs.Empty);
                    
      }
}        public void SetCheckedSilent(bool value)        
    {            _checked = value;
                  Invalidate();
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  Color track = _checked ? Theme.Accent : Theme.SurfaceHi;
                  if (_hover)            
      {                track = _checked ? Theme.Blend(Theme.Accent, Color.White, 0.10f) : Theme.Blend(Theme.SurfaceHi, Color.White, 0.07f);
                    
      }
RectangleF rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
                  Theme.FillRounded(e.Graphics, rect, Height / 2f, track);
                  if (!_checked)            
      {                Theme.DrawRounded(e.Graphics, rect, Height / 2f, Theme.Stroke, 1f);
                    
      }
float margin = 4f * s;
                  float knob = Height - margin * 2f;
                  float x = _checked ? Width - knob - margin : margin;
                  RectangleF knobRect = new RectangleF(x, margin, knob, knob);
                  using (SolidBrush shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))            
      {                e.Graphics.FillEllipse(shadow, new RectangleF(knobRect.X, knobRect.Y + 1.5f * s, knobRect.Width, knobRect.Height));
                    
      }
Theme.FillCircle(e.Graphics, knobRect, Color.White);
                  if (_pressed)            
      {                using (SolidBrush mask = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))                
        {                    e.Graphics.FillEllipse(mask, rect);
                          
        }
Theme.DrawRounded(e.Graphics, rect, Height / 2f, Color.FromArgb(95, 255, 255, 255), 1.2f);
                    
      }
if (Focused)            
      {                Theme.DrawRounded(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f,                    Theme.Alpha(Theme.Accent, 100), 1f);
                    
      }
}        protected override void OnMouseEnter(EventArgs e)        
    {            _hover = true;
                  Invalidate();
                  base.OnMouseEnter(e);
              
    }
protected override void OnMouseLeave(EventArgs e)        
    {            _hover = false;
                  Invalidate();
                  base.OnMouseLeave(e);
              
    }
protected override void OnMouseDown(MouseEventArgs e)        
    {            if (e.Button == MouseButtons.Left)            
      {                _pressed = true;
                        Invalidate();
                    
      }
base.OnMouseDown(e);
              
    }
protected override void OnMouseUp(MouseEventArgs e)        
    {            _pressed = false;
                  if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))            
      {                Checked = !Checked;
                    
      }
Invalidate();
                  base.OnMouseUp(e);
              
    }
protected override bool IsInputKey(Keys keyData)        
    {            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
                  return base.IsInputKey(keyData);
              
    }
protected override void OnKeyDown(KeyEventArgs e)        
    {            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)            
      {                e.SuppressKeyPress = true;
                        Checked = !Checked;
                    
      }
base.OnKeyDown(e);
              
    }
}
    public class ModernSlider : Control    
  {        private int _min = 40;
            private int _max = 360;
            private int _value = 152;
            private bool _dragging;
            private bool _hover;
            public event EventHandler ValueChanged;
            public ModernSlider()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                  Height = Theme.S(30);
                  Cursor = Cursors.Hand;
                  TabStop = true;
              
    }
public int Minimum        
    {            get 
      { return _min;
         
      }
set 
      { _min = value;
         if (_max < _min) _max = _min;
         ClampValue();
         Invalidate();
         
      }
}        public int Maximum        
    {            get 
      { return _max;
         
      }
set 
      { _max = value;
         if (_max < _min) _min = _max;
         ClampValue();
         Invalidate();
         
      }
}        public int Value        
    {            get 
      { return _value;
         
      }
set            
      {                int v = AppConfig.Clamp(value, _min, _max);
                        if (v == _value) return;
                        _value = v;
                        Invalidate();
                        EventHandler h = ValueChanged;
                        if (h != null) h(this, EventArgs.Empty);
                    
      }
}        public void SetValueSilent(int value)        
    {            _value = AppConfig.Clamp(value, _min, _max);
                  Invalidate();
              
    }
private void ClampValue()        
    {            _value = AppConfig.Clamp(_value, _min, _max);
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  int pad = Theme.S(9);
                  float trackY = Height / 2f - 2f * s;
                  RectangleF track = new RectangleF(pad, trackY, Math.Max(10, Width - pad * 2), 4f * s);
                  Theme.FillRounded(e.Graphics, track, 2f * s, Theme.SurfaceHi);
                  float ratio = _max > _min ? (float)(_value - _min) / (float)(_max - _min) : 0f;
                  float thumbX = track.X + track.Width * ratio;
                  RectangleF filled = new RectangleF(track.X, track.Y, Math.Max(0f, thumbX - track.X), track.Height);
                  if (filled.Width > 0.5f)            
      {                using (LinearGradientBrush brush = new LinearGradientBrush(                    new RectangleF(track.X, track.Y, Math.Max(1f, track.Width), track.Height),                    Theme.Accent, Theme.Mint, 0f))                
        {                    Theme.FillRounded(e.Graphics, filled, 2f * s, brush);
                          
        }
}            float knob = (_hover || _dragging ? 18f : 16f) * s;
                  RectangleF knobRect = new RectangleF(thumbX - knob / 2f, Height / 2f - knob / 2f, knob, knob);
                  using (SolidBrush shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))            
      {                e.Graphics.FillEllipse(shadow, new RectangleF(knobRect.X + 0.5f, knobRect.Y + 2f * s, knob, knob));
                    
      }
Theme.FillCircle(e.Graphics, knobRect, Color.White);
                  Theme.DrawCircle(e.Graphics, knobRect, Theme.Alpha(Theme.Accent, 160), 2f * s);
              
    }
private void SetFromX(int x)        
    {            int pad = Theme.S(9);
                  float width = Math.Max(1, Width - pad * 2);
                  float ratio = (x - pad) / width;
                  if (ratio < 0f) ratio = 0f;
                  if (ratio > 1f) ratio = 1f;
                  Value = _min + (int)Math.Round((_max - _min) * ratio);
              
    }
protected override void OnMouseDown(MouseEventArgs e)        
    {            if (e.Button == MouseButtons.Left)            
      {                _dragging = true;
                        Focus();
                        SetFromX(e.X);
                        Invalidate();
                    
      }
base.OnMouseDown(e);
              
    }
protected override void OnMouseMove(MouseEventArgs e)        
    {            _hover = true;
                  if (_dragging) SetFromX(e.X);
                  Invalidate();
                  base.OnMouseMove(e);
              
    }
protected override void OnMouseUp(MouseEventArgs e)        
    {            _dragging = false;
                  Invalidate();
                  base.OnMouseUp(e);
              
    }
protected override void OnMouseLeave(EventArgs e)        
    {            _hover = false;
                  if (!_dragging) Invalidate();
                  base.OnMouseLeave(e);
              
    }
protected override bool IsInputKey(Keys keyData)        
    {            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End) return true;
                  return base.IsInputKey(keyData);
              
    }
protected override void OnKeyDown(KeyEventArgs e)        
    {            if (e.KeyCode == Keys.Left) Value -= 1;
                  else if (e.KeyCode == Keys.Right) Value += 1;
                  else if (e.KeyCode == Keys.Home) Value = _min;
                  else if (e.KeyCode == Keys.End) Value = _max;
                  base.OnKeyDown(e);
              
    }
}
    public class StepperBox : Control    
  {        private readonly TextBox _edit;
            private int _min = 40;
            private int _max = 360;
            private int _value = 152;
            private int _step = 1;
            private int _divisor = 1;
            private int _hoverZone = 0;
            private bool _updating;
            private bool _editing;
            public event EventHandler ValueChanged;
            public StepperBox()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                  Height = Theme.S(36);
                  Width = Theme.S(112);
                  Font = Theme.UiFont(9.2f, false);
                  _edit = new TextBox();
                  _edit.BorderStyle = BorderStyle.None;
                  _edit.BackColor = Theme.SurfaceAlt;
                  _edit.ForeColor = Theme.Text;
                  _edit.Font = Theme.UiFont(9.2f, false);
                  _edit.TextAlign = HorizontalAlignment.Center;
                  _edit.KeyDown += new KeyEventHandler(EditOnKeyDown);
                  _edit.LostFocus += new EventHandler(EditOnLostFocus);
                  _edit.KeyPress += new KeyPressEventHandler(EditOnKeyPress);
                  Controls.Add(_edit);
                  SetValueSilent(152);
                  _edit.Visible = false;
              
    }
public int Minimum        
    {            get 
      { return _min;
         
      }
set 
      { _min = value;
         if (_max < _min) _max = _min;
         ClampValue();
         
      }
}        public int Maximum        
    {            get 
      { return _max;
         
      }
set 
      { _max = value;
         if (_max < _min) _min = _max;
         ClampValue();
         
      }
}        public int Step        
    {            get 
      { return _step;
         
      }
set 
      { _step = Math.Max(1, value);
         
      }
}        public int Value        
    {            get 
      { return _value;
         
      }
set            
      {                int v = AppConfig.Clamp(value, _min, _max);
                        if (v == _value)                
        {                    UpdateEditText();
                              return;
                          
        }
_value = v;
                        UpdateEditText();
                        Invalidate();
                        EventHandler h = ValueChanged;
                        if (h != null) h(this, EventArgs.Empty);
                    
      }
}        public void SetValueSilent(int value)        
    {            _value = AppConfig.Clamp(value, _min, _max);
                  UpdateEditText();
                  Invalidate();
              
    }
private void ClampValue()        
    {            _value = AppConfig.Clamp(_value, _min, _max);
                  UpdateEditText();
              
    }
// 显示除数：1 = 整数；100 = 两位小数（内部 Value 仍为整数，显示时除以 Divisor）
public int Divisor
    {
      get { return _divisor; }
      set
      {
        _divisor = value < 1 ? 1 : value;
        UpdateEditText();
        Invalidate();
      }
    }
// 小数形式的取值，配合 Divisor 使用
public double DecimalValue
    {
      get { return _value / (double)_divisor; }
      set { Value = (int)Math.Round(value * _divisor); }
    }
public void SetDecimalSilent(double value)
    {
      SetValueSilent((int)Math.Round(value * _divisor));
    }
private string FormatValue()
    {
      if (_divisor <= 1) return _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
      int decimals = _divisor == 10 ? 1 : (_divisor == 100 ? 2 : 3);
      return (_value / (double)_divisor).ToString("F" + decimals,
        System.Globalization.CultureInfo.InvariantCulture);
    }
private void UpdateEditText()        
    {            if (_edit == null) return;
                  _updating = true;
                  if (!_edit.Focused || string.IsNullOrEmpty(_edit.Text))            
      {                _edit.Text = _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    
      }
_updating = false;
              
    }
protected override void OnResize(EventArgs e)        
    {            base.OnResize(e);
                  if (_edit == null) return;
                  _edit.Height = Math.Max(_edit.Font.Height + Theme.S(4), Theme.S(24));
                  _edit.Location = new Point(Theme.S(11), Math.Max(0, (Height - _edit.Height) / 2));
                  _edit.Width = Math.Max(10, Width - Theme.S(66));
                  if (!_editing) _edit.Visible = false;
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                  Color fill = Enabled ? Theme.SurfaceAlt : Theme.Blend(Theme.SurfaceAlt, Theme.Bg, 0.42f);
                  Theme.FillRounded(e.Graphics, rect, Theme.S(9), fill);
                  Theme.DrawRounded(e.Graphics, rect, Theme.S(9), _edit.Focused && Enabled ? Theme.Alpha(Theme.Accent, 200) : Theme.StrokeSoft, 1f);
                  float zone = Theme.S(28);
                  RectangleF minus = new RectangleF(Width - zone * 2, 0, zone, Height);
                  RectangleF plus = new RectangleF(Width - zone, 0, zone, Height);
                  if (_hoverZone == 1 && Enabled) Theme.FillRounded(e.Graphics, minus, Theme.S(7), Theme.Alpha(Theme.Accent, 26));
                  if (_hoverZone == 2 && Enabled) Theme.FillRounded(e.Graphics, plus, Theme.S(7), Theme.Alpha(Theme.Accent, 26));
                  Color symbol = Color.White;
                  using (Pen p = new Pen(symbol, 2.0f))            
      {                p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        float arm = Theme.S(6);
                        float minusCx = minus.X + minus.Width / 2f;
                        float plusCx = plus.X + plus.Width / 2f;
                        e.Graphics.DrawLine(p, minusCx - arm, Height / 2f, minusCx + arm, Height / 2f);
                        e.Graphics.DrawLine(p, plusCx - arm, Height / 2f, plusCx + arm, Height / 2f);
                        e.Graphics.DrawLine(p, plusCx, Height / 2f - arm, plusCx, Height / 2f + arm);
                    
      }
if (!_editing)            
      {                Theme.DrawText(e.Graphics, FormatValue(),                    Theme.UiFont(9.4f, true), Color.White,                    new RectangleF(Theme.S(9), 0, Math.Max(10, Width - zone * 2 - Theme.S(14)), Height),                    StringAlignment.Center, StringAlignment.Center, true);
                    
      }
Theme.DrawRounded(e.Graphics, new RectangleF(Width - zone * 2 - 1, Theme.S(7), 0.5f, Height - Theme.S(14)),                0f, Theme.StrokeSoft, 1f);
                  base.OnPaint(e);
              
    }
protected override void OnPaintBackground(PaintEventArgs e)        
    {        
    }
protected override void OnMouseMove(MouseEventArgs e)        
    {            float zone = Theme.S(28);
                  int zoneIndex = e.X >= Width - zone ? 2 : (e.X >= Width - zone * 2 ? 1 : 0);
                  if (zoneIndex != _hoverZone)            
      {                _hoverZone = zoneIndex;
                        Cursor = zoneIndex > 0 ? Cursors.Hand : Cursors.IBeam;
                        Invalidate();
                    
      }
base.OnMouseMove(e);
              
    }
protected override void OnMouseLeave(EventArgs e)        
    {            _hoverZone = 0;
                  Cursor = Cursors.IBeam;
                  Invalidate();
                  base.OnMouseLeave(e);
              
    }
protected override void OnMouseDown(MouseEventArgs e)        
    {            if (!Enabled) return;
                  if (e.Button == MouseButtons.Left)            
      {                float zone = Theme.S(28);
                        if (e.X >= Width - zone)                
        {                    Value += _step;
                          
        }
else if (e.X >= Width - zone * 2)                
        {                    Value -= _step;
                          
        }
else                
        {                    BeginEdit();
                          
        }
Invalidate();
                    
      }
base.OnMouseDown(e);
              
    }
private void BeginEdit()        
    {            if (!Enabled) return;
                  _editing = true;
                  _edit.Text = FormatValue();
                  _edit.Visible = true;
                  _edit.BringToFront();
                  _edit.Focus();
                  _edit.SelectAll();
              
    }
protected override void OnEnabledChanged(EventArgs e)        
    {            base.OnEnabledChanged(e);
                  if (_edit != null)            
      {                _edit.BackColor = Enabled ? Theme.SurfaceAlt : Theme.Blend(Theme.SurfaceAlt, Theme.Bg, 0.42f);
                        _edit.ForeColor = Enabled ? Theme.Text : Theme.Faint;
                        if (!Enabled)                
        {                    _editing = false;
                              _edit.Visible = false;
                          
        }
}            Cursor = Enabled ? Cursors.IBeam : Cursors.Default;
                  Invalidate();
              
    }
private void EditOnKeyPress(object sender, KeyPressEventArgs e)        
    {            if (char.IsControl(e.KeyChar)) return;
                  if (char.IsDigit(e.KeyChar)) return;
                  // 小数模式下允许输入一个小数点
                  if (_divisor > 1 && (e.KeyChar == '.' || e.KeyChar == ',') && _edit.Text.IndexOf('.') < 0)
      {                e.KeyChar = '.';
                        return;
      }
                  e.Handled = true;
              
    }        private void EditOnKeyDown(object sender, KeyEventArgs e)        
    {            if (e.KeyCode == Keys.Enter)            
      {                CommitEdit();
                        _editing = false;
                        _edit.Visible = false;
                        e.SuppressKeyPress = true;
                        Invalidate();
                    
      }
else if (e.KeyCode == Keys.Up)            
      {                Value += _step;
                        e.SuppressKeyPress = true;
                    
      }
else if (e.KeyCode == Keys.Down)            
      {                Value -= _step;
                        e.SuppressKeyPress = true;
                    
      }
else if (e.KeyCode == Keys.Escape)            
      {                _editing = false;
                        _edit.Visible = false;
                        UpdateEditText();
                        Invalidate();
                        Parent.Focus();
                    
      }
base.OnKeyDown(e);
              
    }
private void EditOnLostFocus(object sender, EventArgs e)        
    {            CommitEdit();
                  _editing = false;
                  _edit.Visible = false;
                  Invalidate();
              
    }
private void CommitEdit()        
    {            if (_updating) return;
                  if (_divisor > 1)            
      {                double parsed;
                        if (double.TryParse(_edit.Text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out parsed))
                            Value = (int)Math.Round(parsed * _divisor);
                        else
                            UpdateEditText();
                        return;
      }
                  int parsedInt;
                  if (int.TryParse(_edit.Text, out parsedInt))            
      {                Value = parsedInt;
                    
      }
else            
      {                UpdateEditText();
                    
      }
    }    
  }
public class SelectOption    
  {        public string Id;
            public string Text;
            public SelectOption(string id, string text)        
    {            Id = id == null ? "" : id;
                  Text = text == null ? "" : text;
              
    }
public override string ToString()        
    {            return Text;
              
    }
}
    public class ModernSelect : Control    
  {        private readonly List<SelectOption> _options = new List<SelectOption>();
            private string _selectedId = "";
            private bool _hover;
            private bool _pressed;
            private ContextMenuStrip _menu;
            public event EventHandler SelectionChanged;
            public ModernSelect()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                  Height = Theme.S(40);
                  Cursor = Cursors.Hand;
                  Font = Theme.UiFont(9.2f, false);
                  TabStop = true;
              
    }
public string SelectedId        
    {            get 
      { return _selectedId;
         
      }
}        public string SelectedText        
    {            get            
      {                SelectOption option = Find(_selectedId);
                        return option == null ? "" : option.Text;
                    
      }
}        public void SetItems(List<SelectOption> options, string selectedId)        
    {            _options.Clear();
                  if (options != null) _options.AddRange(options);
                  _selectedId = selectedId == null ? "" : selectedId;
                  if (Find(_selectedId) == null && _options.Count > 0) _selectedId = _options[0].Id;
                  Invalidate();
              
    }
public void SetSelectedSilent(string id)        
    {            _selectedId = id == null ? "" : id;
                  Invalidate();
              
    }
private SelectOption Find(string id)        
    {            for (int i = 0;
       i < _options.Count;
       i++)            
      {                if (_options[i].Id == id) return _options[i];
                    
      }
return null;
              
    }
protected override void OnMouseDown(MouseEventArgs e)        
    {            if (e.Button != MouseButtons.Left || !Enabled) return;
                  _pressed = true;
                  Invalidate();
                  BuildMenu();
                  if (_menu != null) _menu.Show(this, new Point(0, Height + Theme.S(2)));
                  base.OnMouseDown(e);
              
    }
protected override void OnMouseUp(MouseEventArgs e)        
    {            _pressed = false;
                  Invalidate();
                  base.OnMouseUp(e);
              
    }
private void BuildMenu()        
    {            if (_menu != null)            
      {                _menu.Dispose();
                        _menu = null;
                    
      }
_menu = new ContextMenuStrip();
                  _menu.Renderer = new DarkMenuRenderer();
                  _menu.BackColor = Theme.SurfaceAlt;
                  _menu.ForeColor = Theme.Text;
                  _menu.ShowImageMargin = false;
                  _menu.Width = Math.Max(Theme.S(160), Width);
                  for (int i = 0;
       i < _options.Count;
       i++)            
      {                SelectOption option = _options[i];
                        ToolStripMenuItem item = new ToolStripMenuItem(option.Text);
                        item.Checked = option.Id == _selectedId;
                        item.Click += delegate                
        {                    _selectedId = option.Id;
                              Invalidate();
                              EventHandler h = SelectionChanged;
                              if (h != null) h(this, EventArgs.Empty);
                          
        }
;
                        _menu.Items.Add(item);
                    
      }
}        protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                  Color fill = Enabled ? Theme.SurfaceAlt : Theme.Blend(Theme.SurfaceAlt, Theme.Bg, 0.4f);
                  Theme.FillRounded(e.Graphics, rect, 9f * s, fill);
                  Theme.DrawRounded(e.Graphics, rect, 9f * s,                _pressed ? Theme.Accent : (_hover ? Theme.Stroke : Theme.StrokeSoft), 1f);
                  string text = SelectedText;
                  if (string.IsNullOrEmpty(text)) text = "未选择";
                  Theme.DrawText(e.Graphics, text, Font, Enabled ? Theme.Text : Theme.Faint,                new RectangleF(12 * s, 0, Width - 40 * s, Height), StringAlignment.Near, StringAlignment.Center, true);
                  float cx = Width - 18 * s;
                  float cy = Height / 2f;
                  using (Pen p = new Pen(Theme.Muted, 1.4f))            
      {                p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        e.Graphics.DrawLine(p, cx - 4 * s, cy - 2 * s, cx, cy + 2 * s);
                        e.Graphics.DrawLine(p, cx, cy + 2 * s, cx + 4 * s, cy - 2 * s);
                    
      }
if (_pressed)            
      {                Theme.FillRounded(e.Graphics, new RectangleF(2, 2, Width - 4, Height - 4), 8f * s,                    Color.FromArgb(28, 255, 255, 255));
                    
      }
}        protected override void OnMouseEnter(EventArgs e)        
    {            _hover = true;
                  Invalidate();
                  base.OnMouseEnter(e);
              
    }
protected override void OnMouseLeave(EventArgs e)        
    {            _hover = false;
                  if (!_pressed) Invalidate();
                  base.OnMouseLeave(e);
              
    }
protected override bool IsInputKey(Keys keyData)        
    {            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
                  return base.IsInputKey(keyData);
              
    }
protected override void OnKeyDown(KeyEventArgs e)        
    {            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)            
      {                e.SuppressKeyPress = true;
                        BuildMenu();
                        if (_menu != null) _menu.Show(this, new Point(0, Height + Theme.S(2)));
                    
      }
base.OnKeyDown(e);
              
    }
protected override void Dispose(bool disposing)        
    {            if (disposing && _menu != null)            
      {                _menu.Dispose();
                        _menu = null;
                    
      }
base.Dispose(disposing);
              
    }
}
    public class KeyCaptureBox : Control    
  {        private KeyBinding _binding = new KeyBinding();
            private bool _capturing;
            private bool _hover;
            private bool _kbdHover;
            private ContextMenuStrip _menu;
            public event EventHandler BindingChanged;
            public KeyCaptureBox()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                  Height = Theme.S(44);
                  Cursor = Cursors.Hand;
                  TabStop = true;
                  Font = Theme.UiFont(9.4f, false);
                  BuildMenu();
              
    }
public KeyBinding Binding        
    {            get 
      { return _binding;
         
      }
set            
      {                _binding = value == null ? new KeyBinding() : value;
                        Invalidate();
                    
      }
}        public void SetBindingSilent(KeyBinding binding)        
    {            _binding = binding == null ? new KeyBinding() : binding;
                  _capturing = false;
                  Invalidate();
              
    }
private void BuildMenu()        
    {            _menu = new ContextMenuStrip();
                  _menu.Renderer = new DarkMenuRenderer();
                  _menu.BackColor = Theme.SurfaceAlt;
                  _menu.ForeColor = Theme.Text;
                  _menu.ShowImageMargin = false;
                  AddMenuItem("鼠标左键", BindingKind.MouseButton, 0);
                  AddMenuItem("鼠标右键", BindingKind.MouseButton, 1);
                  AddMenuItem("鼠标中键", BindingKind.MouseButton, 2);
                  _menu.Items.Add(new ToolStripSeparator());
                  AddMenuItem("滚轮向上", BindingKind.MouseWheel, 1);
                  AddMenuItem("滚轮向下", BindingKind.MouseWheel, -1);
                  _menu.Items.Add(new ToolStripSeparator());
                  ToolStripMenuItem clear = new ToolStripMenuItem("清除绑定");
                  clear.Click += delegate            
      {                SetBinding(new KeyBinding());
                    
      }
;
                  _menu.Items.Add(clear);
              
    }
private void AddMenuItem(string text, BindingKind kind, int code)        
    {            ToolStripMenuItem item = new ToolStripMenuItem(text);
                  item.Click += delegate            
      {                SetBinding(new KeyBinding(kind, code));
                    
      }
;
                  _menu.Items.Add(item);
              
    }
private void SetBinding(KeyBinding binding)        
    {            _binding = binding;
                  _capturing = false;
                  Invalidate();
                  EventHandler h = BindingChanged;
                  if (h != null) h(this, EventArgs.Empty);
              
    }
protected override void OnMouseDown(MouseEventArgs e)        
    {            if (!Enabled) return;
                  Focus();
                  if (e.Button == MouseButtons.Right)            
      {                _capturing = false;
                        _menu.Show(this, e.Location);
                    
      }
else if (e.Button == MouseButtons.Left && IsKeyboardButtonHit(e.Location))            
      {                ShowVirtualKeyboard();
                        return;
                    
      }
else if (e.Button == MouseButtons.Left)            
      {                _capturing = true;
                        Invalidate();
                    
      }
base.OnMouseDown(e);
              
    }
private bool IsKeyboardButtonHit(Point loc)        
    {            float s = Theme.Scale;
                  int bw = (int)(76 * s);
                  int bx = Width - bw - (int)(10 * s);
                  int bh = (int)(28 * s);
                  int by = (Height - bh) / 2;
                  return loc.X >= bx && loc.X <= bx + bw && loc.Y >= by && loc.Y <= by + bh;
              
    }
private void ShowVirtualKeyboard()        
    {            using (VirtualKeyboardDialog dlg = new VirtualKeyboardDialog(_binding))            
      {                Point screen = PointToScreen(new Point(0, Height));
                        dlg.StartPosition = FormStartPosition.Manual;
                        dlg.Location = new Point(Math.Max(0, screen.X - 120), Math.Max(0, screen.Y + 4));
                        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected != null)                
        {                    SetBinding(dlg.Selected);
                          
        }
}        
    }
protected override void OnEnabledChanged(EventArgs e)        
    {            base.OnEnabledChanged(e);
                  if (!Enabled) _capturing = false;
                  Invalidate();
              
    }
protected override void OnLostFocus(EventArgs e)        
    {            _capturing = false;
                  Invalidate();
                  base.OnLostFocus(e);
              
    }
protected override bool IsInputKey(Keys keyData)        
    {            return true;
              
    }
protected override void OnKeyDown(KeyEventArgs e)        
    {            if (!_capturing)            
      {                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)                
        {                    _capturing = true;
                              e.SuppressKeyPress = true;
                              Invalidate();
                          
        }
base.OnKeyDown(e);
                        return;
                    
      }
e.SuppressKeyPress = true;
                  if (e.KeyCode == Keys.Escape)            
      {                _capturing = false;
                        Invalidate();
                        return;
                    
      }
if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)            
      {                SetBinding(new KeyBinding());
                        return;
                    
      }
// Win 键会立即打开开始菜单并切换焦点，程序通常收不到消息；            // 但 Ctrl/Shift/Alt 等修饰键必须能独立作为主键绑定，这里不再过滤。            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)      
{                Invalidate();
                        return;
                    
      }
// 收集修饰键，但排除主键自身（避免 "Alt + Alt" 这种自引用）    
int mods = 0;
Keys kc = e.KeyCode;
                  if (kc != Keys.ControlKey && kc != Keys.LControlKey && kc != Keys.RControlKey                && (ModifierKeys & Keys.Control) == Keys.Control) mods |= (int)Keys.Control;
                  if (kc != Keys.ShiftKey && kc != Keys.LShiftKey && kc != Keys.RShiftKey                && (ModifierKeys & Keys.Shift) == Keys.Shift) mods |= (int)Keys.Shift;
                  if (kc != Keys.Menu && kc != Keys.LMenu && kc != Keys.RMenu                && (ModifierKeys & Keys.Alt) == Keys.Alt) mods |= (int)Keys.Alt;
                  SetBinding(new KeyBinding(BindingKind.Keyboard, (int)kc, mods));
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                  Color fill = Enabled ? Theme.SurfaceAlt : Theme.Blend(Theme.SurfaceAlt, Theme.Bg, 0.42f);
                  Color border = _capturing ? Theme.Accent : (_hover && Enabled ? Theme.Stroke : Theme.StrokeSoft);
                  Theme.FillRounded(e.Graphics, rect, 9f * s, fill);
                  Theme.DrawRounded(e.Graphics, rect, 9f * s, border, _capturing ? 1.5f : 1f);
                  RectangleF keyBox = new RectangleF(9 * s, 8 * s, 32 * s, Height - 16 * s);
                  Theme.FillRounded(e.Graphics, keyBox, 6f * s, Theme.Alpha(Theme.Accent, Enabled ? (_capturing ? 50 : 30) : 14));
                  Theme.DrawText(e.Graphics, "⌨", Theme.UiFont(10f, false),                Enabled ? Theme.Blend(Theme.Accent, Color.White, 0.45f) : Theme.Faint,                keyBox, StringAlignment.Center, StringAlignment.Center, false);
                  string caption;
                  Color captionColor;
                  if (!Enabled)            
      {                caption = _binding.IsEmpty ? "未绑定" : _binding.DisplayName;
                        captionColor = Theme.Faint;
                    
      }
else if (_capturing)            
      {                caption = "请按下要绑定的按键…";
                        captionColor = Theme.Accent;
                    
      }
else if (_binding.IsEmpty)            
      {                caption = "点击后按下按键进行绑定";
                        captionColor = Theme.Muted;
                    
      }
else            
      {                caption = _binding.DisplayName;
                        captionColor = Theme.Text;
                    
      }
Theme.DrawText(e.Graphics, caption, Font, captionColor,                new RectangleF(50 * s, 0, Math.Max(20, Width - 50 * s - (10 * s) - (76 * s)), Height),                StringAlignment.Near, StringAlignment.Center, true);
                  // 右侧"打开键盘"按钮：图标 + 文字，完整可点击区域。    
int bW = (int)(76 * s);
int bH = (int)(28 * s);
                  int bX = Width - bW - (int)(10 * s);
                  int bY = (Height - bH) / 2;
                  RectangleF bRect = new RectangleF(bX + 0.5f, bY + 0.5f, bW - 1f, bH - 1f);
                  Color bFill;
                  Color bBorder;
                  Color bText;
                  if (!Enabled)            
      {                bFill = Theme.Blend(Theme.SurfaceHi, Theme.Bg, 0.5f);
                        bBorder = Theme.StrokeSoft;
                        bText = Theme.Faint;
                    
      }
else if (_kbdHover)            
      {                bFill = Theme.SurfaceHover;
                        bBorder = Theme.Accent;
                        bText = Theme.Blend(Theme.Accent, Color.White, 0.3f);
                    
      }
else            
      {                bFill = Theme.Alpha(Theme.Accent, 18);
                        bBorder = Theme.Alpha(Theme.Accent, 70);
                        bText = Theme.Blend(Theme.Accent, Color.White, 0.15f);
                    
      }
Theme.FillRounded(e.Graphics, bRect, 6f * s, bFill);
                  Theme.DrawRounded(e.Graphics, bRect, 6f * s, bBorder, 1f);
                  // 小键盘图标（GDI 手绘外框 + 2×2 按键格，稳定跨字体）    
float ikbx = bRect.X + 6 * s;
float ikby = bRect.Y + (bRect.Height - 12 * s) / 2;
                  float ikbw = 14 * s;
                  float ikbh = 12 * s;
                  using (GraphicsPath gpath = Theme.RoundedPath(new RectangleF(ikbx, ikby, ikbw, ikbh), 2f * s))            
      {                using (Pen p = new Pen(bText, 1.2f * s))                
        {                    e.Graphics.DrawPath(p, gpath);
                          
        }
}            // 4 个小格    
float cellG = 0.8f * s;
float cw = (ikbw - cellG) / 2f;
                  float ch = (ikbh - cellG) / 2f;
                  for (int row = 0;
       row < 2;
       row++)            
      {                for (int col = 0;
         col < 2;
         col++)                
        {                    RectangleF cell = new RectangleF(ikbx + cellG / 2f + col * (cw + cellG),                                                      ikby + cellG / 2f + row * (ch + cellG),                                                      cw - 0.5f, ch - 0.5f);
                              Theme.FillRounded(e.Graphics, cell, 0.8f * s, bText);
                          
        }
}
// "打开键盘"文字
RectangleF textRect = new RectangleF(ikbx + ikbw + 5 * s, bRect.Y,        
bRect.Width - (ikbw + 11 * s), bRect.Height);
Theme.DrawText(e.Graphics, "打开键盘", Theme.UiFont(7.8f, false), bText,                textRect, StringAlignment.Center, StringAlignment.Center, true);
                  base.OnPaint(e);
              
    }
protected override void OnPaintBackground(PaintEventArgs e)        
    {        
    }
protected override void OnMouseEnter(EventArgs e)        
    {            _hover = true;
                  Invalidate();
                  base.OnMouseEnter(e);
              
    }
protected override void OnMouseMove(MouseEventArgs e)        
    {            bool k = IsKeyboardButtonHit(e.Location);
                  if (k != _kbdHover)            
      {                _kbdHover = k;
                        Invalidate();
                    
      }
base.OnMouseMove(e);
              
    }
protected override void OnMouseLeave(EventArgs e)        
    {            _hover = false;
                  _kbdHover = false;
                  Invalidate();
                  base.OnMouseLeave(e);
              
    }
protected override void Dispose(bool disposing)        
    {            if (disposing && _menu != null) _menu.Dispose();
                  base.Dispose(disposing);
              
    }
}
    public class DarkMenuRenderer : ToolStripProfessionalRenderer    
  {        public DarkMenuRenderer()            : base(new DarkMenuColorTable())        
    {            RoundedEdges = true;
              
    }
protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)        
    {            if (!e.Item.Selected)            
      {                base.OnRenderMenuItemBackground(e);
                        return;
                    
      }
Rectangle rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                  using (SolidBrush brush = new SolidBrush(Theme.SurfaceHi))            using (GraphicsPath path = Theme.RoundedPath(rect, 6f))            
      {                e.Graphics.FillPath(brush, path);
                    
      }
}        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)        
    {            using (Pen p = new Pen(Theme.Stroke, 1f))            
      {                int y = e.Item.Height / 2;
                        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
                    
      }
}    
  }
public class DarkMenuColorTable : ProfessionalColorTable    
  {        public override Color MenuBorder 
    { get 
      { return Theme.Stroke;
         
      }
}        public override Color MenuItemBorder 
    { get 
      { return Color.Transparent;
         
      }
}        public override Color MenuItemSelected 
    { get 
      { return Theme.SurfaceHi;
         
      }
}        public override Color ToolStripDropDownBackground 
    { get 
      { return Theme.SurfaceAlt;
         
      }
}        public override Color ImageMarginGradientBegin 
    { get 
      { return Theme.SurfaceAlt;
         
      }
}        public override Color ImageMarginGradientMiddle 
    { get 
      { return Theme.SurfaceAlt;
         
      }
}        public override Color ImageMarginGradientEnd 
    { get 
      { return Theme.SurfaceAlt;
         
      }
}        public override Color SeparatorDark 
    { get 
      { return Theme.Stroke;
         
      }
}        public override Color SeparatorLight 
    { get 
      { return Theme.Stroke;
         
      }
}    
  }
public enum ResizeEdge    
  {        Left,        Right,        Top,        Bottom,        TopLeft,        TopRight,        BottomLeft,        BottomRight    
  }
public class ResizeHandle : Control    
  {        private readonly ResizeEdge _edge;
            public ResizeHandle(ResizeEdge edge)        
    {            _edge = edge;
                  BackColor = Theme.Bg;
                  switch (_edge)            
      {                case ResizeEdge.Left:                case ResizeEdge.Right:                    Cursor = Cursors.SizeWE;
                            break;
                        case ResizeEdge.Top:                case ResizeEdge.Bottom:                    Cursor = Cursors.SizeNS;
                            break;
                        case ResizeEdge.TopLeft:                case ResizeEdge.BottomRight:                    Cursor = Cursors.SizeNWSE;
                            break;
                        default:                    Cursor = Cursors.SizeNESW;
                            break;
                    
      }
}        protected override void OnMouseDown(MouseEventArgs e)        
    {            if (e.Button == MouseButtons.Left && Parent != null)            
      {                int hit;
                        switch (_edge)                
        {                    case ResizeEdge.Left: hit = NativeMethods.HTLEFT;
           break;
                              case ResizeEdge.Right: hit = NativeMethods.HTRIGHT;
           break;
                              case ResizeEdge.Top: hit = NativeMethods.HTTOP;
           break;
                              case ResizeEdge.Bottom: hit = NativeMethods.HTBOTTOM;
           break;
                              case ResizeEdge.TopLeft: hit = NativeMethods.HTTOPLEFT;
           break;
                              case ResizeEdge.TopRight: hit = NativeMethods.HTTOPRIGHT;
           break;
                              case ResizeEdge.BottomLeft: hit = NativeMethods.HTBOTTOMLEFT;
           break;
                              default: hit = NativeMethods.HTBOTTOMRIGHT;
           break;
                          
        }
NativeMethods.ReleaseCapture();
                        NativeMethods.SendMessage(Parent.Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(hit), IntPtr.Zero);
                    
      }
base.OnMouseDown(e);
              
    }
}
    public enum ToastKind    
  {        Info,        Success,        Error    
  }
public class ToastView : Control    
  {        private string _message = "";
            private ToastKind _kind = ToastKind.Info;
            public ToastView()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                  BackColor = Color.Transparent;
                  Height = Theme.S(48);
                  Width = Theme.S(360);
                  Font = Theme.UiFont(9f, false);
                  Visible = false;
              
    }
public void ShowToast(string message, ToastKind kind)        
    {            _message = message == null ? "" : message;
                  _kind = kind;
                  using (Graphics g = CreateGraphics())            
      {                float w = g.MeasureString(_message, Font).Width + Theme.S(92);
                        Width = (int)Math.Min(Theme.S(520), Math.Max(Theme.S(260), w));
                    
      }
Invalidate();
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  RectangleF rect = new RectangleF(1, 1, Width - 2, Height - 2);
                  using (SolidBrush shadow = new SolidBrush(Color.FromArgb(95, 0, 0, 0)))            
      {                Theme.FillRounded(e.Graphics, new RectangleF(3 * s, 4 * s, Width - 4 * s, Height - 4 * s), 14f * s, shadow);
                    
      }
Theme.FillRounded(e.Graphics, rect, 14f * s, Color.FromArgb(244, 24, 34, 51));
                  Theme.DrawRounded(e.Graphics, rect, 14f * s, Theme.Stroke, 1f);
                  Color dot = _kind == ToastKind.Error ? Theme.Danger : (_kind == ToastKind.Success ? Theme.Mint : Theme.Accent);
                  RectangleF dotRect = new RectangleF(18 * s, Height / 2f - 4 * s, 8 * s, 8 * s);
                  Theme.FillCircle(e.Graphics, dotRect, dot);
                  Theme.DrawText(e.Graphics, _message, Font, Theme.Text,                new RectangleF(38 * s, 0, Width - 54 * s, Height), StringAlignment.Near, StringAlignment.Center, true);
              
    }
}
    public class StatusPill : Control    
  {        private string _status = "检测中";
            private Color _dot = Theme.Gold;
            private bool _pressed;
            public StatusPill()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                  BackColor = Color.Transparent;
                  Cursor = Cursors.Hand;
                  Font = Theme.UiFont(8.1f, false);
                  Height = Theme.S(26);
                  Width = Theme.S(132);
              
    }
public void SetStatus(string text, Color dot)        
    {            _status = text == null ? "" : text;
                  _dot = dot;
                  Invalidate();
              
    }
public string StatusText        
    {            get 
      { return _status;
         
      }
}        protected override void OnPaint(PaintEventArgs e)        
    {            Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                  Theme.FillRounded(e.Graphics, rect, Height / 2f, Theme.Alpha(Theme.SurfaceAlt, 190));
                  Theme.DrawRounded(e.Graphics, rect, Height / 2f, Theme.StrokeSoft, 1f);
                  float dot = 6f * s;
                  Theme.FillCircle(e.Graphics, new RectangleF(11 * s, Height / 2f - dot / 2f, dot, dot), _dot);
                  Theme.DrawText(e.Graphics, _status, Font, Theme.TextSoft,                new RectangleF(23 * s, 0, Width - 31 * s, Height), StringAlignment.Near, StringAlignment.Center, true);
                  if (_pressed)            
      {                Theme.FillRounded(e.Graphics, rect, Height / 2f, Color.FromArgb(35, 255, 255, 255));
                    
      }
}        protected override void OnMouseDown(MouseEventArgs e)        
    {            _pressed = true;
                  Invalidate();
                  base.OnMouseDown(e);
              
    }
protected override void OnMouseUp(MouseEventArgs e)        
    {            _pressed = false;
                  Invalidate();
                  base.OnMouseUp(e);
              
    }
protected override void OnMouseEnter(EventArgs e)        
    {            base.OnMouseEnter(e);
                  Invalidate();
              
    }
}
    public class SmoothScrollPane : Panel    
  {        private Control _content;
            private int _offset;
            private int _maxOffset;
            private bool _hoverBar;
            private bool _draggingBar;
            private int _dragStartY;
            private int _dragStartOffset;
            public SmoothScrollPane()        
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                  BackColor = Theme.Surface;
              
    }
public Control Content        
    {            get 
      { return _content;
         
      }
}        public int ScrollOffset        
    {            get 
      { return _offset;
         
      }
}        public int MaximumOffset        
    {            get 
      { return _maxOffset;
         
      }
}        public int ContentWidth        
    {            get            
      {                float s = Theme.Scale;
                        return Math.Max(80, Width - (int)(10 * s));
                    
      }
}        public void SetContent(Control content)        
    {            if (_content != null) Controls.Remove(_content);
                  _content = content;
                  if (_content != null)            
      {                _content.Left = 0;
                        _content.Top = 0;
                        _content.Width = ContentWidth;
                        Controls.Add(_content);
                    
      }
UpdateScrollMetrics();
              
    }
public void UpdateScrollMetrics()        
    {            if (_content == null) return;
                  float s = Theme.Scale;
                  _content.Width = ContentWidth;
                  _maxOffset = Math.Max(0, _content.Height - Height);
                  if (_offset > _maxOffset) _offset = _maxOffset;
                  if (_offset < 0) _offset = 0;
                  _content.Top = -_offset;
                  Invalidate();
              
    }
public void ScrollBy(int pixels)        
    {            if (_content == null) return;
                  int next = AppConfig.Clamp(_offset + pixels, 0, _maxOffset);
                  if (next == _offset) return;
                  _offset = next;
                  _content.Top = -_offset;
                  Invalidate();
              
    }
public void ScrollToTop()        
    {            ScrollBy(-_offset);
              
    }
protected override void OnResize(EventArgs e)        
    {            base.OnResize(e);
                  if (_content == null) return;
                  _content.Width = ContentWidth;
                  UpdateScrollMetrics();
              
    }
protected override void OnMouseWheel(MouseEventArgs e)        
    {            ScrollBy(e.Delta > 0 ? -72 : 72);
                  base.OnMouseWheel(e);
              
    }
private RectangleF GetThumb()        
    {            float s = Theme.Scale;
                  int barX = Width - (int)(10 * s);
                  if (_content == null || _maxOffset <= 0) return RectangleF.Empty;
                  float trackTop = 4 * s;
                  float trackHeight = Height - 8 * s;
                  float thumbHeight = Math.Max(32 * s, trackHeight * Height / (float)_content.Height);
                  float y = trackTop + (trackHeight - thumbHeight) * (_offset / (float)_maxOffset);
                  return new RectangleF(barX, y, 4 * s, thumbHeight);
              
    }
protected override void OnPaint(PaintEventArgs e)        
    {            base.OnPaint(e);
                  if (_content == null || _maxOffset <= 0) return;
                  Theme.SetupGraphics(e.Graphics);
                  float s = Theme.Scale;
                  int barX = Width - (int)(10 * s);
                  RectangleF track = new RectangleF(barX + 1 * s, 4 * s, 2 * s, Height - 8 * s);
                  Theme.FillRounded(e.Graphics, track, 1 * s, Color.FromArgb(16, 140, 160, 190));
                  RectangleF thumb = GetThumb();
                  Theme.FillRounded(e.Graphics, thumb, 2 * s,                _hoverBar || _draggingBar ? Color.FromArgb(150, 144, 163, 190) : Color.FromArgb(100, 118, 136, 163));
              
    }
protected override void OnMouseEnter(EventArgs e)        
    {            base.OnMouseEnter(e);
                  _hoverBar = true;
                  Invalidate();
              
    }
protected override void OnMouseLeave(EventArgs e)        
    {            base.OnMouseLeave(e);
                  _hoverBar = false;
                  if (!_draggingBar) Invalidate();
              
    }
protected override void OnMouseMove(MouseEventArgs e)        
    {            base.OnMouseMove(e);
                  if (_draggingBar)            
      {                float s = Theme.Scale;
                        float trackHeight = Height - 8 * s;
                        RectangleF thumb = GetThumb();
                        float range = Math.Max(1f, trackHeight - thumb.Height);
                        int dy = e.Y - _dragStartY;
                        int next = _dragStartOffset + (int)Math.Round(dy * _maxOffset / range);
                        next = AppConfig.Clamp(next, 0, _maxOffset);
                        if (next != _offset)                
        {                    _offset = next;
                              _content.Top = -_offset;
                              Invalidate();
                          
        }
}        
    }
protected override void OnMouseDown(MouseEventArgs e)        
    {            if (e.Button == MouseButtons.Left && _maxOffset > 0)            
      {                float s = Theme.Scale;
                        int barX = Width - (int)(10 * s);
                        if (e.X >= barX - (int)(3 * s))                
        {                    RectangleF thumb = GetThumb();
                              if (thumb.Contains(e.Location))                    
          {                        _draggingBar = true;
                                    _dragStartY = e.Y;
                                    _dragStartOffset = _offset;
                                    Capture = true;
                                
          }
else if (e.Y < thumb.Top)                    
          {                        ScrollBy(-Height + 60);
                                
          }
else                    
          {                        ScrollBy(Height - 60);
                                
          }
Invalidate();
                          
        }
}            base.OnMouseDown(e);
              
    }
protected override void OnMouseUp(MouseEventArgs e)        
    {            if (_draggingBar)            
      {                _draggingBar = false;
                        Capture = false;
                        Invalidate();
                    
      }
base.OnMouseUp(e);
              
    }
}
        // 自动切换面板内的模式标签条：与顶部全局模式条联动，点击即切换当前编辑的模式。
public class ModeTagStrip : Panel
  {        private readonly ModernButton _add;
            private readonly ModernButton _remove;
            private readonly List<ModeTab> _tabs = new List<ModeTab>();
            public event Action<int> ModeSelected;
            public event EventHandler AddRequested;
            public event EventHandler RenameRequested;
            public event EventHandler RemoveRequested;
            public ModeTagStrip()
    {            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                  BackColor = Theme.SurfaceAlt;
                  Height = Theme.S(38);
                  _add = new ModernButton();
                  _add.Text = "添加";
                  _add.Icon = "＋";
                  _add.Style = ModernButtonStyle.Soft;
                  _add.Font = Theme.UiFont(8.0f, false);
                  _add.Click += delegate
      {                EventHandler h = AddRequested;
                        if (h != null) h(this, EventArgs.Empty);

      }
;
                  _remove = new ModernButton();
                  _remove.Text = "删除";
                  _remove.Style = ModernButtonStyle.Ghost;
                  _remove.Font = Theme.UiFont(8.0f, false);
                  _remove.Click += delegate
      {                EventHandler h = RemoveRequested;
                        if (h != null) h(this, EventArgs.Empty);

      }
;
                  Controls.Add(_add);
                  Controls.Add(_remove);

    }
public void SetModes(IList<string> names, int selected)        
    {            if (selected < 0) selected = 0;
                  SuspendLayout();
                  for (int i = 0;
       i < _tabs.Count;
       i++)            
      {                Controls.Remove(_tabs[i]);
                        _tabs[i].Dispose();
                    
      }
_tabs.Clear();
                  if (names != null)            
      {                for (int i = 0;
         i < names.Count;
         i++)                
        {                    int index = i;
                              ModeTab tab = new ModeTab();
                              tab.Index = i;
                              tab.ModeName = names[i];
                              tab.Selected = i == selected;
                              tab.SelectedRequested += delegate                    
          {                        Action<int> h = ModeSelected;
                                    if (h != null) h(index);
                                
          }
;
                              tab.RenameRequested += delegate                    
          {                        EventHandler h = RenameRequested;
                                    if (h != null) h(this, EventArgs.Empty);
                                
          }
;
                              _tabs.Add(tab);
                              Controls.Add(tab);
                          
        }
}            ResumeLayout();
                  LayoutChildren();
                  Invalidate();
              
    }
protected override void OnResize(EventArgs e)        
    {            base.OnResize(e);
                  LayoutChildren();
              
    }
private void LayoutChildren()
    {            // 手动布局：标签按 preferred 宽度排列，空间不足时按比例压缩，            // 始终为右侧「删除」「添加」两个按钮保留完整区域，杜绝重叠。
if (_add == null || _remove == null) return;
int pad = Theme.S(8);
                  int gap = Theme.S(6);
                  int btnW = Theme.S(64);
                  int btnH = Theme.S(34);
                  // 最右为卡片折叠箭头预留避让宽度，避免按钮与箭头重叠。
                  int arrowReserve = Theme.S(34);
                  int addX = Math.Max(0, Width - btnW - arrowReserve);
                  int removeX = Math.Max(0, addX - gap - btnW);
                  _add.SetBounds(addX, Math.Max(0, (Height - btnH) / 2), btnW, btnH);
                  _remove.SetBounds(removeX, Math.Max(0, (Height - btnH) / 2), btnW, btnH);
                  int n = _tabs.Count;
                  if (n == 0) return;
                  int tabH = Theme.S(34);
                  int tabY = Math.Max(0, (Height - tabH) / 2);
                  int avail = Math.Max(0, removeX - gap - pad);
                  int[] widths = new int[n];
                  int total = 0;
                  for (int i = 0;
       i < n;
       i++)            
      {                widths[i] = _tabs[i].PreferredWidth;
                        total += widths[i];
                    
      }
total += gap * (n - 1);
                  if (total > avail && avail > 0)            
      {                // 先整体等比压缩，再保证每个标签不小于最小宽度。        
int minW = Theme.S(46);
double ratio = (double)(avail - gap * (n - 1)) / Math.Max(1, total - gap * (n - 1));
                        int used = gap * (n - 1);
                        for (int i = 0;
         i < n;
         i++)                
        {                    widths[i] = Math.Max(minW, (int)Math.Floor(widths[i] * ratio));
                              used += widths[i];
                          
        }
while (used > avail)                
        {                    // 从最长的标签继续压缩，直到放得下。            
int longest = 0;
for (int i = 1;
           i < n;
           i++) if (widths[i] > widths[longest]) longest = i;
                              if (widths[longest] <= Theme.S(30)) break;
                              widths[longest]--;
                              used--;
                          
        }
}            int x = pad;
                  for (int i = 0;
       i < n;
       i++)            
      {                _tabs[i].SetBounds(x, tabY, widths[i], tabH);
                        x += widths[i] + gap;
                    
      }
}        protected override void OnPaintBackground(PaintEventArgs e)        
    {            // 标签条必须自己把整块背景涂满：标签与「添加」按钮都是透明背景，            // 父级（本控件）不涂背景时会露出此前窗口布局留下的旧像素（错位重影）。    
Theme.SetupGraphics(e.Graphics);
using (SolidBrush brush = new SolidBrush(BackColor))            
      {                e.Graphics.FillRectangle(brush, ClientRectangle);
                    
      }
}    
  }
internal static class NativeMethods    
  {        public const int EM_SETCUEBANNER = 0x1501;
            public const int WM_NCHITTEST = 0x0084;
            public const int WM_NCLBUTTONDOWN = 0x00A1;
            public const int HTCAPTION = 2;
            public const int HTLEFT = 10;
            public const int HTRIGHT = 11;
            public const int HTTOP = 12;
            public const int HTTOPLEFT = 13;
            public const int HTTOPRIGHT = 14;
            public const int HTBOTTOM = 15;
            public const int HTBOTTOMLEFT = 16;
            public const int HTBOTTOMRIGHT = 17;
            public const int HTCLIENT = 1;
            public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
            [DllImport("user32.dll")]        public static extern bool ReleaseCapture();
            [DllImport("user32.dll")]        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
            [DllImport("dwmapi.dll")]        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);
            [DllImport("user32.dll")]        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);
            [DllImport("user32.dll")]        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
  }
}