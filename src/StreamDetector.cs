using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MobileUiConfig
{
    internal static class StreamDetector
    {
        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int MIB_TCP_STATE_ESTAB = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        private struct TcpConn
        {
            public ushort LocalPort;
            public uint RemoteAddr;
            public ushort RemotePort;

            public TcpConn(ushort lp, uint ra, ushort rp)
            {
                LocalPort = lp;
                RemoteAddr = ra;
                RemotePort = rp;
            }
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf,
            int tableClass, int reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder, int ulAf,
            int tableClass, int reserved);

        // Well-known always-on/system UDP ports that must not count as a
        // streaming session (mDNS, SSDP, NTP, NetBIOS, DHCP...).
        private static bool IsSystemUdpPort(ushort port)
        {
            return port == 53 || port == 67 || port == 68 || port == 69 || port == 123 ||
                   port == 137 || port == 138 || port == 139 || port == 161 || port == 162 ||
                   port == 500 || port == 1900 || port == 4500 || port == 5353 || port == 5355;
        }

        private static bool HasActiveUdpSession(uint pid)
        {
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, 1, 0);
            if (size <= 0) return false;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedUdpTable(buffer, ref size, false, AF_INET, 1, 0) != 0) return false;
                int count = Marshal.ReadInt32(buffer);
                IntPtr rowPtr = IntPtr.Add(buffer, 4);
                for (int i = 0; i < count; i++)
                {
                    // MIB_UDPROW_OWNER_PID: localAddr(4) localPort(4) owningPid(4)
                    uint localPortRaw = (uint)Marshal.ReadInt32(rowPtr, 4);
                    uint owningPid = (uint)Marshal.ReadInt32(rowPtr, 8);
                    rowPtr = IntPtr.Add(rowPtr, 12);
                    if (owningPid != pid) continue;
                    ushort lp = (ushort)((localPortRaw >> 8) | (localPortRaw << 8));
                    if (!IsSystemUdpPort(lp)) return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return false;
        }

        public static List<string> ParseList(string configured)
        {
            List<string> result = new List<string>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            string all = (configured == null ? "" : configured) + ",moonlight,sunshine,apollo,uu,parsec,gameviewer,todesk,anydesk,rustdesk,steamlink,chiaki";
            string[] parts = all.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim().ToLowerInvariant();
                if (s.Length == 0 || seen.ContainsKey(s)) continue;
                seen[s] = true;
                result.Add(s);
            }
            return result;
        }

        public static bool IsStreamProcess(int pid, string configuredList)
        {
            if (pid <= 0) return false;
            string processName = "";
            try
            {
                using (Process p = Process.GetProcessById(pid)) processName = p.ProcessName;
            }
            catch { return false; }
            if (string.IsNullOrEmpty(processName)) return false;
            string n = processName.ToLowerInvariant();
            if (n.StartsWith("sunshine") || n.StartsWith("gameviewer")) return true;
            List<string> names = ParseList(configuredList);
            for (int i = 0; i < names.Count; i++)
                if (n.Contains(names[i])) return true;
            return false;
        }

        private static string _sunshineLogPath;
        private static bool _sunshineLogResolved;

        // Moonlight sessions: Sunshine/Apollo carries the stream in ways that
        // leave almost no user-mode socket state (short-lived TCP, no visible
        // UDP), but its log file always records session start/end. Tailing the
        // log is the definitive "screen is being transmitted" signal.
        private static string FindSunshineLog()
        {
            if (_sunshineLogResolved) return _sunshineLogPath;
            _sunshineLogResolved = true;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SunshineService"))
                {
                    string image = key == null ? null : key.GetValue("ImagePath") as string;
                    if (!string.IsNullOrEmpty(image))
                    {
                        image = image.Trim('"');
                        string dir = Path.GetDirectoryName(image);
                        // <root>\tools\sunshinesvc.exe -> <root>\config\sunshine.log
                        if (string.Equals(Path.GetFileName(dir), "tools", StringComparison.OrdinalIgnoreCase))
                            dir = Path.GetDirectoryName(dir);
                        string candidate = Path.Combine(dir, "config", "sunshine.log");
                        if (File.Exists(candidate)) { _sunshineLogPath = candidate; return _sunshineLogPath; }
                    }
                }
            }
            catch { }
            string fallback = @"C:\Program Files\Sunshine\config\sunshine.log";
            if (File.Exists(fallback)) _sunshineLogPath = fallback;
            return _sunshineLogPath;
        }

        private static bool? DetectSunshineSession()
        {
            string log = FindSunshineLog();
            if (log == null) return null;
            try
            {
                using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    const int tail = 524288;
                    long start = Math.Max(0, fs.Length - tail);
                    fs.Seek(start, SeekOrigin.Begin);
                    byte[] buf = new byte[fs.Length - start];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    string text = Encoding.UTF8.GetString(buf, 0, read);
                    int connect = Math.Max(
                        text.LastIndexOf("CLIENT CONNECTED", StringComparison.Ordinal),
                        text.LastIndexOf("New streaming session started", StringComparison.Ordinal));
                    int disconnect = text.LastIndexOf("CLIENT DISCONNECTED", StringComparison.Ordinal);
                    if (connect < 0 && disconnect < 0) return null;
                    return connect > disconnect;
                }
            }
            catch { return null; }
        }

        public static bool Detect(string configuredList, out string reason)
        {            reason = "未检测到串流会话";
            // Definitive signal for Moonlight/Sunshine: session state from its log.
            bool? sunshineSession = DetectSunshineSession();
            if (sunshineSession.HasValue)
            {
                if (sunshineSession.Value)
                {
                    reason = "Sunshine/Moonlight 会话（日志：客户端已连接）";
                    return true;
                }
                // Log says no active session; still allow the other signals
                // below to detect non-Sunshine streamers.
            }
            // System-level signal 1: the current session itself is a remote
            // desktop session (RDP / Terminal Services).
            try
            {
                if (System.Windows.Forms.SystemInformation.TerminalServerSession)
                {
                    reason = "远程桌面（RDP）会话";
                    return true;
                }
            }
            catch { }

            List<string> names = ParseList(configuredList);
            Dictionary<uint, List<TcpConn>> tcp;
            try
            {
                tcp = GetEstablishedByPid();
            }
            catch
            {
                reason = "读取 TCP 会话失败";
                return false;
            }

            foreach (KeyValuePair<uint, List<TcpConn>> pair in tcp)
            {
                // System-level signal 2: an inbound connection is established on
                // a well-known remote-display host port, no matter which process
                // owns it. This is the "PC is transmitting its screen" state and
                // covers Moonlight/Sunshine/GameStream, RDP, VNC, Parsec,
                // Steam Remote Play, TeamViewer, AnyDesk, etc.
                List<TcpConn> conns = pair.Value;
                for (int i = 0; i < conns.Count; i++)
                {
                    string service = PortServiceName(conns[i].LocalPort);
                    if (service != null)
                    {
                        string owner = ResolveProcessName(pair.Key);
                        reason = service + " 画面传输会话（本机端口 " + conns[i].LocalPort
                            + (owner.Length > 0 ? "，进程 " + owner : "") + "）";
                        return true;
                    }
                }

                // Fallback: known streaming process with established connections
                // (covers domestic software that uses dynamic high ports).
                string processName = ResolveProcessName(pair.Key);
                if (string.IsNullOrEmpty(processName)) continue;
                string n = processName.ToLowerInvariant();

                // Host processes (Sunshine / Apollo / GameStream) carry the actual
                // stream over UDP, which has no connection state. They bind their
                // UDP streaming ports only while a client session is active, so
                // treat any non-system UDP endpoint as an active session.
                bool isHostProc = n.StartsWith("sunshine") || n.StartsWith("apollo") || n.StartsWith("nvstreamer");
                if (isHostProc && (pair.Value.Count > 0 || HasActiveUdpSession(pair.Key)))
                {
                    reason = "Moonlight / Sunshine 串流主机活动会话（进程 " + processName + "）";
                    return true;
                }
                if (n.StartsWith("gameviewer"))
                {
                    reason = "网易UU远程 / GameViewer 活动连接（" + pair.Value.Count + " 条）";
                    return true;
                }
                for (int i = 0; i < names.Count; i++)
                {
                    if (n.Contains(names[i]))
                    {
                        reason = "串流进程 " + processName + " 活动连接 " + pair.Value.Count + " 条";
                        return true;
                    }
                }
            }
            return false;
        }

        private static string PortServiceName(ushort localPort)
        {
            if (localPort == 3389) return "远程桌面 RDP";
            if (localPort >= 47984 && localPort <= 47990) return "Moonlight / Sunshine / GameStream 串流";
            if (localPort >= 5900 && localPort <= 5910) return "VNC";
            if (localPort >= 5800 && localPort <= 5810) return "VNC";
            if (localPort == 5938) return "TeamViewer";
            if (localPort == 6568) return "AnyDesk";
            if (localPort >= 8000 && localPort <= 8003) return "Parsec";
            if (localPort >= 27036 && localPort <= 27037) return "Steam 远程畅玩";
            return null;
        }

        private static string ResolveProcessName(uint pid)
        {
            try
            {
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName ?? "";
            }
            catch { return ""; }
        }

        private static Dictionary<uint, List<TcpConn>> GetEstablishedByPid()
        {
            Dictionary<uint, List<TcpConn>> result = new Dictionary<uint, List<TcpConn>>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size <= 0) return result;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                {
                    return result;
                }
                int count = Marshal.ReadInt32(buffer);
                IntPtr rowPtr = IntPtr.Add(buffer, 4);
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    MIB_TCPROW_OWNER_PID row =
                        (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID));
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                    if (row.state != MIB_TCP_STATE_ESTAB) continue;
                    ushort lp = (ushort)((row.localPort >> 8) | (row.localPort << 8));
                    ushort rp = (ushort)((row.remotePort >> 8) | (row.remotePort << 8));
                    if (!result.ContainsKey(row.owningPid)) result[row.owningPid] = new List<TcpConn>();
                    result[row.owningPid].Add(new TcpConn(lp, row.remoteAddr, rp));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }
    }
}
