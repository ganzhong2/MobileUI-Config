using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // Loads the icon that Explorer actually shows for this exe. The ICO uses PNG
    // compressed frames; Icon.ToBitmap() on those frames can produce garbage,
    // so the header bitmap is decoded straight from the 256px PNG frame.
    internal static class AppIcon
    {
        public static Icon LoadIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            try
            {
                string path = Path.Combine(ConfigStore.ExeDir, "app.ico");
                if (File.Exists(path)) return new Icon(path);
            }
            catch { }
            return (Icon)SystemIcons.Application.Clone();
        }

        public static Bitmap LoadBitmap()
        {
            try
            {
                string path = Path.Combine(ConfigStore.ExeDir, "app.ico");
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length > 6)
                    {
                        int count = BitConverter.ToUInt16(bytes, 4);
                        int bestOffset = -1;
                        int bestLength = 0;
                        int bestSize = 0;
                        for (int i = 0; i < count; i++)
                        {
                            int entry = 6 + i * 16;
                            if (entry + 16 > bytes.Length) break;
                            int w = bytes[entry] == 0 ? 256 : bytes[entry];
                            int h = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
                            int size = Math.Max(w, h);
                            int length = (int)BitConverter.ToUInt32(bytes, entry + 8);
                            int offset = (int)BitConverter.ToUInt32(bytes, entry + 12);
                            if (length <= 0 || offset < 0 || offset + length > bytes.Length) continue;
                            if (size > bestSize)
                            {
                                bestSize = size;
                                bestOffset = offset;
                                bestLength = length;
                            }
                        }
                        if (bestOffset >= 0 && bestLength > 8 &&
                            bytes[bestOffset] == 0x89 && bytes[bestOffset + 1] == 0x50 &&
                            bytes[bestOffset + 2] == 0x4E && bytes[bestOffset + 3] == 0x47)
                        {
                            using (MemoryStream ms = new MemoryStream(bytes, bestOffset, bestLength))
                            using (Image image = Image.FromStream(ms))
                            {
                                return new Bitmap(image);
                            }
                        }
                    }
                }
            }
            catch { }
            try
            {
                using (Icon icon = LoadIcon()) return icon.ToBitmap();
            }
            catch { }
            return new Bitmap(32, 32);
        }
    }
}
