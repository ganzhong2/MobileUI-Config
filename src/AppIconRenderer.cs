using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MobileUiConfig
{
    // Shared app icon: a square phone screen with one stick on the left and a key cluster on the right.
    public static class AppIconRenderer
    {
        public static void Draw(Graphics g, RectangleF rect)
        {
            SmoothingMode oldSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(rect.Width, rect.Height);
            float x = rect.X + (rect.Width - s) / 2f;
            float y = rect.Y + (rect.Height - s) / 2f;

            // dark app tile
            RectangleF tile = new RectangleF(x + s * 0.01f, y + s * 0.01f, s * 0.98f, s * 0.98f);
            Theme.FillRounded(g, tile, s * 0.23f, Color.FromArgb(255, 9, 17, 31));
            using (Pen edge = new Pen(Color.FromArgb(110, 45, 76, 120), Math.Max(0.8f, s * 0.008f)))
            {
                Theme.DrawRounded(g, tile, s * 0.23f, edge.Color, edge.Width);
            }

            // phone body / narrow black bezel
            RectangleF body = new RectangleF(x + s * 0.035f, y + s * 0.035f, s * 0.93f, s * 0.93f);
            Theme.FillRounded(g, body, s * 0.205f, Color.FromArgb(255, 2, 5, 10));

            // screen
            RectangleF screen = new RectangleF(x + s * 0.09f, y + s * 0.09f, s * 0.82f, s * 0.82f);
            using (LinearGradientBrush screenBrush = new LinearGradientBrush(screen,
                Color.FromArgb(255, 17, 45, 78), Color.FromArgb(255, 8, 20, 38), 55f))
            {
                using (GraphicsPath path = Theme.RoundedPath(screen, s * 0.155f))
                {
                    g.FillPath(screenBrush, path);
                }
            }

            // notch
            RectangleF notch = new RectangleF(x + s * 0.42f, y + s * 0.073f, s * 0.16f, s * 0.023f);
            Theme.FillRounded(g, notch, notch.Height / 2f, Color.FromArgb(255, 2, 5, 10));

            // home indicator
            RectangleF home = new RectangleF(x + s * 0.42f, y + s * 0.885f, s * 0.16f, s * 0.010f);
            Theme.FillRounded(g, home, home.Height / 2f, Color.FromArgb(150, 190, 225, 255));

            // left joystick
            float joyOuter = s * 0.225f;
            PointF joy = new PointF(x + s * 0.285f, y + s * 0.59f);
            RectangleF joyRect = new RectangleF(joy.X - joyOuter / 2f, joy.Y - joyOuter / 2f, joyOuter, joyOuter);
            using (Pen ring = new Pen(Color.FromArgb(235, 232, 245, 255), Math.Max(1.2f, s * 0.018f)))
            {
                g.DrawEllipse(ring, joyRect);
            }
            float joyInner = s * 0.095f;
            RectangleF joyDot = new RectangleF(joy.X - joyInner / 2f, joy.Y - joyInner / 2f, joyInner, joyInner);
            using (LinearGradientBrush dotBrush = new LinearGradientBrush(joyDot,
                Color.FromArgb(255, 100, 230, 205), Color.FromArgb(255, 46, 155, 230), 45f))
            {
                g.FillEllipse(dotBrush, joyDot);
            }

            // right side virtual keys
            DrawKey(g, x + s * 0.625f, y + s * 0.34f, s * 0.085f, Color.FromArgb(255, 255, 112, 145));
            DrawKey(g, x + s * 0.795f, y + s * 0.39f, s * 0.080f, Color.FromArgb(255, 110, 175, 255));
            DrawKey(g, x + s * 0.610f, y + s * 0.625f, s * 0.082f, Color.FromArgb(255, 245, 195, 95));
            DrawKey(g, x + s * 0.790f, y + s * 0.675f, s * 0.085f, Color.FromArgb(255, 110, 225, 165));

            g.SmoothingMode = oldSmoothing;
        }

        private static void DrawKey(Graphics g, float cx, float cy, float d, Color color)
        {
            RectangleF r = new RectangleF(cx - d / 2f, cy - d / 2f, d, d);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, color)))
            {
                g.FillEllipse(fill, r);
            }
            using (Pen edge = new Pen(Color.FromArgb(210, 245, 250, 255), Math.Max(0.8f, d * 0.16f)))
            {
                g.DrawEllipse(edge, r);
            }
            float inner = d * 0.30f;
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
            {
                g.FillEllipse(dot, new RectangleF(cx - inner / 2f, cy - inner / 2f, inner, inner));
            }
        }
    }
}
