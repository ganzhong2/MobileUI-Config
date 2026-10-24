using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MobileUiConfig
{
    internal static class Program
    {
        public static readonly uint ShowSettingsMessage = RegisterWindowMessage("MobileUIConfig.ShowSettings");
        private static Mutex _instanceMutex;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT data);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private const int WM_COPYDATA = 0x004A;
        private const int HWND_BROADCAST = 0xFFFF;

        private static bool SendLaunchToExisting(string profilePath)
        {
            IntPtr target = FindWindow(null, "移动端UI");
            if (target == IntPtr.Zero) return false;
            string payload = profilePath + "|1";
            IntPtr memory = Marshal.StringToHGlobalUni(payload);
            try
            {
                COPYDATASTRUCT data = new COPYDATASTRUCT();
                data.dwData = IntPtr.Zero;
                data.cbData = (payload.Length + 1) * 2;
                data.lpData = memory;
                IntPtr result = SendMessage(target, WM_COPYDATA, IntPtr.Zero, ref data);
                return result != IntPtr.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        private static void KillStaleInstances()
        {
            try
            {
                DateTime exeTime = File.GetLastWriteTime(Application.ExecutablePath);
                Process current = Process.GetCurrentProcess();
                Process[] all = Process.GetProcessesByName(current.ProcessName);
                for (int i = 0; i < all.Length; i++)
                {
                    Process p = all[i];
                    if (p == null || p.Id == current.Id) continue;
                    try
                    {
                        // A process started before this exe was replaced is an
                        // old running build; terminate it so the new build wins.
                        if (p.StartTime < exeTime.AddSeconds(-5)) p.Kill();
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
        }

        private static bool AcquireSingleInstance(bool background)
        {
            bool created;
            _instanceMutex = new Mutex(true, "Local\\MobileUIConfig.SingleInstance.v4", out created);
            return created;
        }
        [STAThread]
        private static void Main(string[] args)
        {
            ConfigStore.Log("=== 启动 v3.8 (2026-10-21 10:23) ===");
            ConfigStore.EnsureAssets();
#if NET8_0_WINDOWS
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                try { InputSender.ReleaseAll(); } catch { }
            };
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                ConfigStore.Log("未处理异常: " + e.Exception);
                MessageBox.Show("程序遇到错误：\r\n" + e.Exception.Message, "移动端UI配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                ConfigStore.Log("严重异常: " + e.ExceptionObject);
            };

            string snapshot = null;
            string overlaySnapshot = null;
            string captureScreen = null;
            int width = 0;
            int height = 0;
            int mode = -1;
            int view = -1;
            int scroll = 0;
            int deckScroll = 0;
            bool menu = false;
            bool background = false;
            bool overlayTest = false;
            bool overlayMenu = false;
            string orientation = null;
            string profilePath = null;
            bool launchOnStart = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--snapshot" && i + 1 < args.Length)
                {
                    snapshot = args[i + 1];
                }
                else if (args[i] == "--overlay-snapshot" && i + 1 < args.Length)
                {
                    overlaySnapshot = args[i + 1];
                }
                else if (args[i] == "--capture-screen" && i + 1 < args.Length)
                {
                    captureScreen = args[i + 1];
                }
                else if (args[i] == "--width" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out width);
                }
                else if (args[i] == "--height" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out height);
                }
                else if (args[i] == "--mode" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out mode);
                }
                else if (args[i] == "--view" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out view);
                }
                else if (args[i] == "--scroll" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out scroll);
                }
                else if (args[i] == "--deck" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out deckScroll);
                }
                else if (args[i] == "--menu")
                {
                    menu = true;
                }
                else if (args[i] == "--orientation" && i + 1 < args.Length)
                {
                    orientation = args[i + 1];
                }
                else if (args[i] == "--background" || args[i] == "--silent")
                {
                    background = true;
                }
                else if (args[i] == "--overlay-test")
                {
                    overlayTest = true;
                    background = true;
                }
                else if (args[i] == "--overlay-menu")
                {
                    overlayMenu = true;
                }
                else if (args[i] == "--profile" && i + 1 < args.Length)
                {
                    profilePath = args[i + 1];
                }
                else if (args[i] == "--launch")
                {
                    launchOnStart = true;
                }
            }

            try
            {
                if (captureScreen != null)
                {
                    CaptureScreen(captureScreen);
                    return;
                }
                if (snapshot != null)
                {
                    RunSnapshot(snapshot, width, height, mode, view, menu, scroll, orientation, deckScroll);
                    return;
                }
                if (overlaySnapshot != null)
                {
                    RunOverlaySnapshot(overlaySnapshot, overlayMenu);
                    return;
                }

                KillStaleInstances();
                // Wait briefly for killed processes to fully exit and release the mutex.
                System.Threading.Thread.Sleep(300);
                if (profilePath != null && launchOnStart && SendLaunchToExisting(profilePath))
                {
                    ConfigStore.Log("已将快捷方式启动请求发送给现有实例");
                    return;
                }
                if (!AcquireSingleInstance(background))
                {
                    // Mutex still held by an old instance; try to activate its window
                    // instead of silently exiting, so the user sees something.
                    try
                    {
                        Process current = Process.GetCurrentProcess();
                        Process[] others = Process.GetProcessesByName(current.ProcessName);
                        for (int i = 0; i < others.Length; i++)
                        {
                            if (others[i].Id != current.Id && others[i].MainWindowHandle != IntPtr.Zero)
                            {
                                // Send a message to show the existing window (best effort).
                                ShowWindow(others[i].MainWindowHandle, 9); // SW_RESTORE
                                SetForegroundWindow(others[i].MainWindowHandle);
                            }
                        }
                    }
                    catch { }
                    ConfigStore.Log(background ? "已有实例在后台运行，本次静默退出" : "已有实例运行，已请求打开设置窗口");
                    return;
                }
                // Clear any modifier/key state left behind by a previous crash.
                try { InputSender.ReleaseAll(); } catch { }
                Application.Run(new MainForm(background, overlayTest, profilePath, launchOnStart));
            }
            catch (Exception ex)
            {
                ConfigStore.Log("启动失败: " + ex);
                MessageBox.Show("程序启动失败：\r\n" + ex.Message, "移动端UI配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ConfigStore.Log("=== 退出 ===");
            }
        }

        private static void RunOverlaySnapshot(string path, bool showMenu)
        {
            using (OverlayDemoBackdrop backdrop = new OverlayDemoBackdrop())
            {
                backdrop.Show();
                Application.DoEvents();
                MainForm form = new MainForm(true, true);
                form.Show();
                DateTime end = DateTime.Now.AddMilliseconds(2600);
                while (DateTime.Now < end)
                {
                    Application.DoEvents();
                    Thread.Sleep(30);
                }
                if (showMenu)
                {
                    form.OpenOverlayMenuForTest();
                    DateTime menuEnd = DateTime.Now.AddMilliseconds(700);
                    while (DateTime.Now < menuEnd)
                    {
                        Application.DoEvents();
                        Thread.Sleep(30);
                    }
                }
                CaptureScreen(path);
                form.ForceExit();
                backdrop.Close();
            }
        }

        private static void CaptureScreen(string path)
        {
            try
            {
                Rectangle bounds = Screen.PrimaryScreen.Bounds;
                using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    }
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("屏幕截图失败: " + ex);
            }
        }

        private static void RunSnapshot(string path, int width, int height, int mode, int view, bool menu, int scroll,
            string orientation, int deckScroll)
        {
            MainForm form = new MainForm();
            if (width > 0 && height > 0)
            {
                form.ClientSize = new Size(Math.Max(form.MinimumSize.Width, width), Math.Max(form.MinimumSize.Height, height));
            }
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(20, 20);
            form.Show();
            form.Activate();
            if (!string.IsNullOrEmpty(orientation) && orientation.ToLowerInvariant().StartsWith("p"))
            {
                form.SetOrientationForSnapshot(DisplayOrientation.Portrait);
            }
            if (mode >= 0 || view >= 0 || menu || scroll != 0)
            {
                form.ApplySnapshotOptions(mode >= 0 ? mode : 0, view >= 0 ? view : 0, menu, scroll);
            }
            if (deckScroll != 0) form.ScrollDeckForSnapshot(deckScroll);
            DateTime end = DateTime.Now.AddMilliseconds(900);
            while (DateTime.Now < end)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            form.PerformLayout();
            Application.DoEvents();
            // 首轮布局完成后滚动范围才有效，这里再滚动一次确保快照停在目标面板。
            if (deckScroll != 0) form.ScrollDeckForSnapshot(deckScroll);
            try
            {
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("截图失败: " + ex);
            }
            form.ForceExit();
        }
    }

    internal class OverlayDemoBackdrop : Form
    {
        public OverlayDemoBackdrop()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = Color.FromArgb(7, 11, 18);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            using (LinearGradientBrush bg = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(13, 20, 34), Color.FromArgb(5, 8, 13), 90f))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }
            using (GraphicsPath glow = new GraphicsPath())
            {
                glow.AddEllipse(-Width * 0.1f, Height * 0.25f, Width * 0.7f, Height * 0.8f);
                using (PathGradientBrush brush = new PathGradientBrush(glow))
                {
                    brush.CenterColor = Color.FromArgb(34, 55, 110, 200);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, 55, 110, 200) };
                    e.Graphics.FillPath(brush, glow);
                }
            }
            float step = 48f;
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(18, 130, 160, 210)))
            {
                for (float x = 0; x < Width; x += step)
                {
                    for (float y = 0; y < Height; y += step)
                    {
                        e.Graphics.FillEllipse(dot, x, y, 1.6f, 1.6f);
                    }
                }
            }
            Theme.DrawText(e.Graphics, "STREAMING OVERLAY PREVIEW", Theme.Semibold(13f), Color.FromArgb(110, 150, 185, 230),
                new RectangleF(48, 42, Width - 96, 30), StringAlignment.Near, StringAlignment.Center, false);
            Theme.DrawText(e.Graphics, "串流模式 · 实际悬浮层", Theme.Semibold(24f), Color.FromArgb(235, 238, 246, 255),
                new RectangleF(48, 72, Width - 96, 50), StringAlignment.Near, StringAlignment.Center, false);
        }
    }
}
