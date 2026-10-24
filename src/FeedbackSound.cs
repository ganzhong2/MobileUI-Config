using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Media;

namespace MobileUiConfig
{
    // 按键音效反馈：内置 4 种短促合成音（音量在采样层缩放），自定义 wav/mp3
    // 走 SoundPlayer / MCI 播放。全部在线程池线程执行，不阻塞触屏触发线程。
    internal static class FeedbackSound
    {
        private const int SampleRate = 44100;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr callback);

        // 右侧编辑器按键试听：只播放当前按键自身音效，不回落全局。
        public static void Play(VirtualKey key)
        {
            if (key == null || !key.SoundEnabled) return;
            try
            {
                ThreadPool.QueueUserWorkItem(delegate { PlayInternal(key.SoundPreset, key.SoundFile, key.SoundVolume); });
            }
            catch { }
        }

        // 播放某个按键配置的音效；按键未开启时回落全局音效配置；两者都关闭则静默。
        public static void Play(VirtualKey key, GlobalFeedbackConfig global)
        {
            if (key == null) return;
            int preset;
            string file;
            int volume;
            if (key.SoundEnabled)
            {
                preset = key.SoundPreset;
                file = key.SoundFile;
                volume = key.SoundVolume;
            }
            else if (global != null && global.SoundEnabled)
            {
                preset = global.SoundPreset;
                file = global.SoundFile;
                volume = global.SoundVolume;
            }
            else
            {
                return;
            }
            try
            {
                ThreadPool.QueueUserWorkItem(delegate { PlayInternal(preset, file, volume); });
            }
            catch { }
        }

        private static void PlayInternal(int preset, string file, int volume)
        {
            try
            {
                if (preset == 4)
                {
                    string path = ConfigStore.GetSoundPath(file);
                    if (path.Length == 0) return;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".mp3" || ext == ".wma" || ext == ".m4a" || ext == ".ogg")
                    {
                        PlayMci(path, volume);
                    }
                    else
                    {
                        using (SoundPlayer player = new SoundPlayer(path))
                        {
                            player.PlaySync();
                        }
                    }
                }
                else
                {
                    using (MemoryStream wav = BuildWav(preset, volume))
                    using (SoundPlayer player = new SoundPlayer(wav))
                    {
                        player.PlaySync();
                    }
                }
            }
            catch { }
        }

        private static void PlayMci(string path, int volume)
        {
            try
            {
                string alias = "fb" + Guid.NewGuid().ToString("N").Substring(0, 8);
                mciSendString("open \"" + path + "\" alias " + alias, null, 0, IntPtr.Zero);
                mciSendString("setaudio " + alias + " volume to " + (volume * 10).ToString(), null, 0, IntPtr.Zero);
                mciSendString("play " + alias + " wait", null, 0, IntPtr.Zero);
                mciSendString("close " + alias, null, 0, IntPtr.Zero);
            }
            catch { }
        }

        // 合成 16-bit PCM 单声道 wav：preset 0=轻咔哒 1=确认音 2=错误音 3=长音。
        private static MemoryStream BuildWav(int preset, int volume)
        {
            int ms;
            if (preset == 1) ms = 140;
            else if (preset == 2) ms = 180;
            else if (preset == 3) ms = 300;
            else ms = 45;
            int samples = SampleRate * ms / 1000;
            short[] data = new short[samples];
            double amp = (Math.Max(0, Math.Min(100, volume)) / 100.0) * 0.72;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / SampleRate;
                double v = 0;
                if (preset == 0)
                {
                    // 轻咔哒：1250Hz 方波，指数衰减。
                    double f = 1250.0;
                    double env = Math.Exp(-t * 55.0);
                    v = Math.Sign(Math.Sin(2.0 * Math.PI * f * t)) * env;
                }
                else if (preset == 1)
                {
                    // 确认音：660Hz 起，第 60ms 升到 990Hz，正弦缓入缓出。
                    double f = t < 0.06 ? 660.0 : 990.0;
                    double env = Envelope(t, ms / 1000.0, 0.02);
                    v = Math.Sin(2.0 * Math.PI * f * t) * env;
                }
                else if (preset == 2)
                {
                    // 错误音：200Hz 锯齿，快速衰减。
                    double f = 200.0;
                    double phase = (t * f) % 1.0;
                    double saw = 2.0 * phase - 1.0;
                    double env = Math.Exp(-t * 22.0);
                    v = saw * env;
                }
                else
                {
                    // 长音：440Hz 正弦，缓入缓出。
                    double env = Envelope(t, ms / 1000.0, 0.05);
                    v = Math.Sin(2.0 * Math.PI * 440.0 * t) * env;
                }
                data[i] = (short)(v * amp * 32767.0);
            }
            return WriteWav(data);
        }

        private static double Envelope(double t, double totalMs, double attack)
        {
            double total = totalMs;
            double a = Math.Min(attack, total / 2.0);
            double release = total - a;
            double env;
            if (t < a) env = t / a;
            else if (t > release) env = Math.Max(0.0, (total - t) / (total - release));
            else env = 1.0;
            return Math.Max(0.0, env);
        }

        private static MemoryStream WriteWav(short[] data)
        {
            int dataBytes = data.Length * 2;
            MemoryStream ms = new MemoryStream(44 + dataBytes);
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);              // PCM
                w.Write((short)1);              // 单声道
                w.Write(SampleRate);
                w.Write(SampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                for (int i = 0; i < data.Length; i++) w.Write(data[i]);
            }
            ms.Position = 0;
            return ms;
        }
    }
}
