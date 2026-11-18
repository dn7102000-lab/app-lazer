// =====================================================================
//  Form1_Machine.cs  -  MÁY & KHỔ IN: kết nối, điều khiển máy, vật liệu, khổ in, đèn đỏ
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Điều khiển máy (COM / Wi-Fi), vật liệu   (trước đây: Form1_Machine.cs)
//    2. Khổ in, đèn đỏ, cài đặt G-code theo khổ in   (trước đây: Form1_PrintArea.cs)
// =====================================================================

// =====================================================================
// PHẦN: Điều khiển máy (COM / Wi-Fi), vật liệu   (trước đây là file Form1_Machine.cs)
// =====================================================================
// =====================================================================
//  Form1_Machine.cs  -  MÁY: điều khiển cổng COM + thư viện thông số vật liệu
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Điều khiển máy qua cổng COM (GRBL)
//    2. Thư viện thông số vật liệu (công suất / tốc độ lưu theo vật liệu)
// =====================================================================
// =====================================================================
// PHẦN 1: Điều khiển máy qua cổng COM (GRBL)
// =====================================================================
// =====================================================================
//  Form1_Machine.cs  -  ĐIỀU KHIỂN MÁY qua cổng COM (GRBL / GRBL-HAL / FluidNC dùng giao thức GRBL 1.1)
//
//  Menu "Máy > Điều khiển máy..." (Ctrl+Shift+M) mở cửa sổ điều khiển (không chặn cửa sổ chính):
//    - Kết nối cổng COM (chọn cổng + baud), trạng thái máy và toạ độ đầu laser theo thời gian thực
//    - Nút  Chạy khung / Bắt đầu / Tạm dừng-Tiếp tục / Dừng
//    - Gốc công việc: "Bắt đầu từ" Tuyệt đối | Vị trí hiện tại | Gốc người dùng  + bảng 9 điểm (góc, cạnh, tâm của
//      khung sản phẩm sẽ nằm tại đầu laser / gốc)  + nút "Đặt gốc tại đầu laser"
//    - Di chuyển đầu laser: 4 hướng, bước 0.1...50 mm hoặc giữ chuột để chạy liên tục, tốc độ, Home, Mở khóa
//    - Khung console: gõ lệnh G-code / $ tay, xem phản hồi của máy
//  G-code được gửi theo kiểu "đếm ký tự" của GRBL (đệm 128 byte) nên chạy liền mạch, không giật.
//
//  CẦN: NuGet "System.IO.Ports" (.NET 8 không có sẵn):  dotnet add package System.IO.Ports
//  Gọi InitMachine() 1 lần: đã gắn ở cuối InitPrintArea() (Form1_PrintArea.cs).
//  Hook machineSend (Form1_PrintArea.cs): "Xuất G-code khung đèn đỏ" khi đã kết nối sẽ hỏi chạy khung ngay trên máy.
// =====================================================================
namespace test01_1014
{
    using System.Globalization;
    using System.IO.Ports;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using Size = System.Drawing.Size;       // tránh nhập nhằng với OpenCvSharp
    using Point = System.Drawing.Point;
    using Font = System.Drawing.Font;
    public enum StartFrom { Absolute, CurrentPosition, UserOrigin }

    public enum ConnectionType { Serial, Telnet, WebSocket, HttpRest }

    public class MachineCfg
    {
        public ConnectionType ConnType { get; set; } = ConnectionType.Serial;
        public string Port { get; set; } = "";
        public int Baud { get; set; } = 115200;
        public string NetworkHost { get; set; } = "192.168.1.100";
        public int NetworkPort { get; set; } = 8080;  // 23 (Telnet), 8080/81 (WebSocket), 80 (HTTP)
        public StartFrom Mode { get; set; } = StartFrom.CurrentPosition;
        public int Anchor { get; set; } = 6;          // 0..8 theo hàng từ trên xuống: 0 trên-trái ... 4 tâm ... 6 dưới-trái, 8 dưới-phải
        public int StepIndex { get; set; } = 3;       // chỉ số trong bảng bước (0 = liên tục)
        public int JogFeed { get; set; } = 3000;      // mm/phút
        public bool AutoReconnect { get; set; } = true; // Tự động kết nối lại khi cắm lại cáp USB / mất mạng tạm thời
    }

    public sealed class JobPackage
    {
        public string Name { get; }
        public string Code { get; }
        public string Info { get; }
        public JobPackage(string name, string code, string info) { Name = name; Code = code; Info = info; }
    }

    public sealed class MachineStatus
    {
        public string State = "";           // Idle, Run, Hold, Jog, Alarm, Door, Check, Home, Sleep
        public double MX, MY, WX, WY;       // toạ độ máy / toạ độ làm việc (mm)
        public int Feed, Spindle;
    }

    public enum LogKind { Info, Sent, Recv, Error }

    // =====================================================================
    //  Kết nối nối tiếp + truyền G-code kiểu GRBL
    // =====================================================================
    public sealed class MachineLink : IDisposable
    {
        private const int RxBuffer = 120;                 // GRBL có đệm 128 byte; chừa dư cho chắc
        private readonly SynchronizationContext ui;
        private SerialPort? port;
        private TcpClient? tcpClient;
        private NetworkStream? tcpStream;
        private ClientWebSocket? wsClient;
        private HttpClient? httpClient;
        private CancellationTokenSource? netCts;
        private ConnectionType currentConnType = ConnectionType.Serial;
        private string httpBaseUrl = "";
        private readonly object sync = new();             // trạng thái job
        private readonly object wlock = new();            // ghi cổng
        private readonly object pumpLock = new();
        private readonly StringBuilder rx = new();
        private System.Threading.Timer? poll;
        private double[] wco = new double[3];
        private long lastHeartbeatTick;                   // thời điểm nhận phản hồi gần nhất (Environment.TickCount64)
        private int missedHeartbeats;                     // số lần ping không thấy phản hồi
        private const int MaxMissedHeartbeats = 8;        // quá ~2 giây không phản hồi khi đang mở -> coi như mất kết nối

        private List<string> job = new();
        private readonly Queue<int> inflight = new();
        private int next, acked, inBytes;
        private bool jobActive, awaitIdle;
        private string jobName = "";
        private readonly System.Diagnostics.Stopwatch clock = new();

        public event Action<string, LogKind>? Log;
        public event Action<MachineStatus>? StatusChanged;
        public event Action<bool>? ConnectionChanged;
        public event Action<int, int>? Progress;
        public event Action<bool, string>? JobEnded;

        public MachineStatus Last { get; private set; } = new();
        public bool IsOpen => currentConnType switch
        {
            ConnectionType.Serial => port?.IsOpen == true,
            ConnectionType.Telnet => tcpClient?.Connected == true,
            ConnectionType.WebSocket => wsClient?.State == WebSocketState.Open,
            ConnectionType.HttpRest => httpClient != null,
            _ => false
        };
        public bool JobActive { get { lock (sync) return jobActive; } }

        public MachineLink() { ui = SynchronizationContext.Current ?? new SynchronizationContext(); }

        public static string[] ListPorts()
        {
            try
            {
                var names = SerialPort.GetPortNames();
                Array.Sort(names, (a, b) =>
                {
                    int na = PortNo(a), nb = PortNo(b);
                    return na != nb ? na.CompareTo(nb) : string.CompareOrdinal(a, b);
                });
                return names;
            }
            catch { return Array.Empty<string>(); }
        }
        private static int PortNo(string s)
        {
            int i = s.Length; while (i > 0 && char.IsDigit(s[i - 1])) i--;
            return i < s.Length && int.TryParse(s[i..], out int n) ? n : int.MaxValue;
        }

        private void Post(Action a) => ui.Post(_ => { try { a(); } catch { } }, null);
        private void LogPost(string t, LogKind k) => Post(() => Log?.Invoke(t, k));

        // ---------------- Kết nối ----------------
        public string? Open(string name, int baud)
        {
            Close();
            currentConnType = ConnectionType.Serial;
            try
            {
                var p = new SerialPort(name, baud, Parity.None, 8, StopBits.One)
                {
                    Encoding = Encoding.ASCII, ReadTimeout = 500, WriteTimeout = 1500,
                    DtrEnable = true, RtsEnable = false, NewLine = "\n"
                };
                p.DataReceived += OnData;
                p.Open();
                lock (sync)
                {
                    rx.Clear();
                    ResetJobState();
                    Last = new MachineStatus();
                    wco = new double[3];
                    lastHeartbeatTick = Environment.TickCount64;
                    missedHeartbeats = 0;
                }
                port = p;
                poll = new System.Threading.Timer(HeartbeatTick, null, 800, 250);
                Post(() => ConnectionChanged?.Invoke(true));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public string? OpenTelnet(string host, int portNum)
        {
            Close();
            currentConnType = ConnectionType.Telnet;
            try
            {
                var client = new TcpClient();
                client.SendTimeout = 1500;
                client.ReceiveTimeout = 1500;
                client.Connect(host, portNum);
                var stream = client.GetStream();

                lock (sync)
                {
                    rx.Clear();
                    ResetJobState();
                    Last = new MachineStatus();
                    wco = new double[3];
                    lastHeartbeatTick = Environment.TickCount64;
                    missedHeartbeats = 0;
                }

                tcpClient = client;
                tcpStream = stream;
                netCts = new CancellationTokenSource();

                Task.Run(() => ReadTcpLoop(stream, netCts.Token));

                poll = new System.Threading.Timer(HeartbeatTick, null, 800, 250);
                Post(() => ConnectionChanged?.Invoke(true));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private async Task ReadTcpLoop(NetworkStream stream, CancellationToken token)
        {
            byte[] buf = new byte[2048];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int n = await stream.ReadAsync(buf, 0, buf.Length, token);
                    if (n <= 0) break;
                    string txt = Encoding.ASCII.GetString(buf, 0, n);
                    ProcessIncomingText(txt);
                }
                catch
                {
                    break;
                }
            }
            if (!token.IsCancellationRequested)
            {
                LogPost("Mất kết nối Telnet", LogKind.Error);
                _ = Task.Run(Close);
            }
        }

        public string? OpenWebSocket(string host, int portNum)
        {
            Close();
            currentConnType = ConnectionType.WebSocket;
            try
            {
                var ws = new ClientWebSocket();
                string uriStr = $"ws://{host}:{portNum}/ws";
                var cts = new CancellationTokenSource(3000);
                ws.ConnectAsync(new Uri(uriStr), cts.Token).GetAwaiter().GetResult();

                lock (sync)
                {
                    rx.Clear();
                    ResetJobState();
                    Last = new MachineStatus();
                    wco = new double[3];
                    lastHeartbeatTick = Environment.TickCount64;
                    missedHeartbeats = 0;
                }

                wsClient = ws;
                netCts = new CancellationTokenSource();

                Task.Run(() => ReadWebSocketLoop(ws, netCts.Token));

                poll = new System.Threading.Timer(HeartbeatTick, null, 800, 250);
                Post(() => ConnectionChanged?.Invoke(true));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private async Task ReadWebSocketLoop(ClientWebSocket ws, CancellationToken token)
        {
            byte[] buf = new byte[4096];
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.Count > 0)
                    {
                        string txt = Encoding.UTF8.GetString(buf, 0, result.Count);
                        ProcessIncomingText(txt);
                    }
                }
                catch
                {
                    break;
                }
            }
            if (!token.IsCancellationRequested)
            {
                LogPost("Mất kết nối WebSocket", LogKind.Error);
                _ = Task.Run(Close);
            }
        }

        public string? OpenHttpRest(string host, int portNum)
        {
            Close();
            currentConnType = ConnectionType.HttpRest;
            try
            {
                httpBaseUrl = $"http://{host}:{portNum}";
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                
                // Test ping bằng cách gửi dấu ? tới /command
                var resp = client.GetAsync($"{httpBaseUrl}/command??").GetAwaiter().GetResult();

                lock (sync)
                {
                    rx.Clear();
                    ResetJobState();
                    Last = new MachineStatus();
                    wco = new double[3];
                    lastHeartbeatTick = Environment.TickCount64;
                    missedHeartbeats = 0;
                }

                httpClient = client;
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ProcessIncomingText(body);

                poll = new System.Threading.Timer(HeartbeatTick, null, 800, 250);
                Post(() => ConnectionChanged?.Invoke(true));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public void Close()
        {
            System.Threading.Timer? t;
            SerialPort? p;
            TcpClient? tc;
            NetworkStream? ts;
            ClientWebSocket? ws;
            HttpClient? hc;
            CancellationTokenSource? cts;
            bool wasJob;

            lock (sync)
            {
                t = poll; poll = null;
                p = port; port = null;
                tc = tcpClient; tcpClient = null;
                ts = tcpStream; tcpStream = null;
                ws = wsClient; wsClient = null;
                hc = httpClient; httpClient = null;
                cts = netCts; netCts = null;
                wasJob = jobActive;
                ResetJobState();
            }

            t?.Dispose();
            cts?.Cancel();
            cts?.Dispose();

            if (p != null)
            {
                try { p.DataReceived -= OnData; if (p.IsOpen) p.Close(); } catch { }
                try { p.Dispose(); } catch { }
            }

            if (ts != null) { try { ts.Close(); ts.Dispose(); } catch { } }
            if (tc != null) { try { tc.Close(); tc.Dispose(); } catch { } }
            if (ws != null) { try { ws.Abort(); ws.Dispose(); } catch { } }
            if (hc != null) { try { hc.Dispose(); } catch { } }

            if (wasJob) Post(() => JobEnded?.Invoke(false, "Mất kết nối với máy"));
            Post(() => ConnectionChanged?.Invoke(false));
        }

        private void HeartbeatTick(object? _)
        {
            if (!IsOpen) return;

            // Kiểm tra timeout: nếu cổng mở nhưng nhiều lần ping không có phản hồi nào
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref lastHeartbeatTick) > 2500)
            {
                int missed = Interlocked.Increment(ref missedHeartbeats);
                if (missed >= MaxMissedHeartbeats)
                {
                    LogPost("Máy không phản hồi heartbeat ping (mất kết nối USB / WiFi / tắt nguồn)", LogKind.Error);
                    Task.Run(Close);
                    return;
                }
            }

            Write(new[] { (byte)'?' });
        }

        public void Dispose() => Close();

        private void ResetJobState()
        {
            jobActive = false; awaitIdle = false; job = new List<string>(); inflight.Clear(); inBytes = 0; next = 0; acked = 0;
        }

        // ---------------- Ghi ----------------
        private bool Write(byte[] b)
        {
            try
            {
                lock (wlock)
                {
                    if (!IsOpen) return false;

                    switch (currentConnType)
                    {
                        case ConnectionType.Serial:
                            port?.Write(b, 0, b.Length);
                            break;

                        case ConnectionType.Telnet:
                            tcpStream?.Write(b, 0, b.Length);
                            break;

                        case ConnectionType.WebSocket:
                            if (wsClient?.State == WebSocketState.Open)
                            {
                                wsClient.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                            }
                            break;

                        case ConnectionType.HttpRest:
                            if (httpClient != null)
                            {
                                string text = Encoding.ASCII.GetString(b);
                                if (text.Length == 1 && (text[0] == '?' || text[0] == '!' || text[0] == '~'))
                                {
                                    // Gửi realtime command tới endpoint /command
                                    _ = httpClient.GetStringAsync($"{httpBaseUrl}/command?{Uri.EscapeDataString(text)}")
                                        .ContinueWith(t => { if (t.IsCompletedSuccessfully) ProcessIncomingText(t.Result); });
                                }
                                else
                                {
                                    var content = new StringContent(text, Encoding.UTF8, "text/plain");
                                    _ = httpClient.PostAsync($"{httpBaseUrl}/command", content)
                                        .ContinueWith(t => { if (t.IsCompletedSuccessfully) ProcessIncomingText(t.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()); });
                                }
                            }
                            break;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogPost("Mất kết nối: " + ex.Message, LogKind.Error);
                Task.Run(Close);
                return false;
            }
        }

        public void Realtime(byte cmd) => Write(new[] { cmd });

        // Gửi 1 lệnh tay (không đi qua bộ đệm job). false nếu đang chạy job.
        public bool SendLine(string line, bool echo = true)
        {
            if (!IsOpen) return false;
            if (echo) LogPost("> " + line, LogKind.Sent);
            return Write(Encoding.ASCII.GetBytes(line + "\n"));
        }

        public void Jog(double dx, double dy, int feed)
        {
            var c = CultureInfo.InvariantCulture;
            SendLine($"$J=G21G91X{dx.ToString("0.###", c)}Y{dy.ToString("0.###", c)}F{feed}", false);
        }
        public void JogCancel() => Realtime(0x85);
        public void Hold() => Realtime((byte)'!');
        public void Resume() => Realtime((byte)'~');

        // ---------------- Job ----------------
        private static IEnumerable<string> CleanLines(IEnumerable<string> src)
        {
            foreach (var raw in src)
            {
                string s = raw;
                int c = s.IndexOf(';'); if (c >= 0) s = s[..c];
                int a;
                while ((a = s.IndexOf('(')) >= 0)
                {
                    int b = s.IndexOf(')', a);
                    if (b < 0) { s = s[..a]; break; }
                    s = s.Remove(a, b - a + 1);
                }
                s = s.Trim();
                if (s.Length > 0) yield return s;
            }
        }

        public string? StartJob(IEnumerable<string> lines, string name)
        {
            if (!IsOpen) return "Chưa kết nối máy.";
            var list = CleanLines(lines).ToList();
            if (list.Count == 0) return "G-code trống.";
            foreach (var l in list) if (l.Length > RxBuffer - 2) return "Có dòng G-code quá dài (" + l.Length + " ký tự): " + l[..40] + "...";
            lock (sync)
            {
                if (jobActive) return "Đang có một công việc chạy.";
                ResetJobState();
                job = list; jobName = name; jobActive = true;
            }
            clock.Restart();
            Post(() => Progress?.Invoke(0, list.Count));
            Pump();
            return null;
        }

        private void Pump()
        {
            lock (pumpLock)
            {
                var send = new StringBuilder();
                lock (sync)
                {
                    if (!jobActive) return;
                    while (next < job.Count)
                    {
                        int len = job[next].Length + 1;
                        if (inBytes + len > RxBuffer) break;
                        send.Append(job[next]).Append('\n');
                        inflight.Enqueue(len); inBytes += len; next++;
                    }
                }
                if (send.Length > 0) Write(Encoding.ASCII.GetBytes(send.ToString()));
            }
        }

        // Dừng hẳn: giữ (!) rồi reset mềm (Ctrl-X) -> tắt laser, xoá hàng đợi của máy
        public void Stop()
        {
            bool was;
            lock (sync) { was = jobActive; ResetJobState(); }
            Realtime(0x21);
            Task.Delay(150).ContinueWith(_ => Realtime(0x18));
            LogPost("■ Dừng: giữ (!) rồi reset mềm (Ctrl-X). Nếu máy báo Alarm, bấm \"Mở khóa\".", LogKind.Info);
            if (was) Post(() => JobEnded?.Invoke(false, "Đã dừng"));
        }

        private void FailJob(string msg)
        {
            bool was;
            lock (sync) { was = jobActive; ResetJobState(); }
            if (!was) return;
            Realtime(0x21);                                         // giữ máy lại, tránh chạy tiếp phần đã nằm trong bộ đệm
            Task.Delay(150).ContinueWith(_ => Realtime(0x18));
            Post(() => JobEnded?.Invoke(false, msg));
        }

        // ---------------- Nhận ----------------
        private void OnData(object? sender, SerialDataReceivedEventArgs e)
        {
            string txt;
            try { txt = port?.ReadExisting() ?? ""; } catch { return; }
            if (txt.Length == 0) return;
            ProcessIncomingText(txt);
        }

        private void ProcessIncomingText(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return;
            Interlocked.Exchange(ref lastHeartbeatTick, Environment.TickCount64);
            Interlocked.Exchange(ref missedHeartbeats, 0);
            var lines = new List<string>();
            lock (sync)
            {
                rx.Append(txt);
                string all = rx.ToString();
                int idx;
                while ((idx = all.IndexOf('\n')) >= 0)
                {
                    lines.Add(all[..idx].Trim('\r', ' '));
                    all = all[(idx + 1)..];
                }
                rx.Clear();
                rx.Append(all);
            }
            foreach (var l in lines)
            {
                if (l.Length > 0) HandleLine(l);
            }
        }

        private void HandleLine(string l)
        {
            if (l[0] == '<') { ParseStatus(l); return; }

            bool ack = l == "ok";
            bool err = l.StartsWith("error", StringComparison.OrdinalIgnoreCase);
            if (ack || err)
            {
                bool inJob; int n = 0, total = 0; string badLine = "";
                lock (sync)
                {
                    inJob = jobActive;
                    if (inJob && inflight.Count > 0)
                    {
                        inBytes -= inflight.Dequeue(); acked++;
                        n = acked; total = job.Count;
                        if (err && n - 1 < job.Count) badLine = job[n - 1];
                    }
                }
                if (err)
                {
                    LogPost(l + " - " + DescribeError(l), LogKind.Error);
                    if (inJob) { if (badLine.Length > 0) LogPost("  tại dòng " + n + ": " + badLine, LogKind.Error); FailJob("Lỗi ở dòng " + n + ": " + DescribeError(l)); }
                    return;
                }
                if (!inJob) { LogPost("ok", LogKind.Recv); return; }
                if (n % 10 == 0 || n >= total) { int a = n, t = total; Post(() => Progress?.Invoke(a, t)); }
                lock (sync) { if (jobActive && acked >= job.Count) awaitIdle = true; }
                Pump();
                return;
            }

            if (l.StartsWith("ALARM", StringComparison.OrdinalIgnoreCase))
            {
                LogPost(l + " - " + DescribeAlarm(l), LogKind.Error);
                FailJob("Máy báo động: " + DescribeAlarm(l));
                return;
            }
            if (l.StartsWith("Grbl ", StringComparison.OrdinalIgnoreCase))
            {
                LogPost(l, LogKind.Recv);
                FailJob("Máy khởi động lại (reset) giữa chừng");
                return;
            }
            LogPost(l, LogKind.Recv);
        }

        private static double[] Nums(string v) =>
            v.Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();

        private void ParseStatus(string l)
        {
            var parts = l.Trim('<', '>').Split('|');
            var st = new MachineStatus { State = parts[0] };
            double[]? m = null, w = null;
            for (int i = 1; i < parts.Length; i++)
            {
                int c = parts[i].IndexOf(':'); if (c < 0) continue;
                string k = parts[i][..c], v = parts[i][(c + 1)..];
                switch (k)
                {
                    case "MPos": m = Nums(v); break;
                    case "WPos": w = Nums(v); break;
                    case "WCO": wco = Nums(v); break;
                    case "FS": { var f = Nums(v); st.Feed = f.Length > 0 ? (int)f[0] : 0; st.Spindle = f.Length > 1 ? (int)f[1] : 0; break; }
                    case "F": { var f = Nums(v); st.Feed = f.Length > 0 ? (int)f[0] : 0; break; }
                }
            }
            double wx = wco.Length > 0 ? wco[0] : 0, wy = wco.Length > 1 ? wco[1] : 0;
            if (m != null && m.Length >= 2) { st.MX = m[0]; st.MY = m[1]; st.WX = w is { Length: >= 2 } ? w[0] : m[0] - wx; st.WY = w is { Length: >= 2 } ? w[1] : m[1] - wy; }
            else if (w != null && w.Length >= 2) { st.WX = w[0]; st.WY = w[1]; st.MX = w[0] + wx; st.MY = w[1] + wy; }
            else { st.MX = Last.MX; st.MY = Last.MY; st.WX = Last.WX; st.WY = Last.WY; }

            bool ended = false; int total = 0; string name = "";
            lock (sync)
            {
                Last = st;
                if (jobActive && awaitIdle && st.State.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
                {
                    ended = true; total = job.Count; name = jobName; ResetJobState();
                }
            }
            Post(() => StatusChanged?.Invoke(st));
            if (ended)
            {
                var el = clock.Elapsed;
                Post(() =>
                {
                    Progress?.Invoke(total, total);
                    JobEnded?.Invoke(true, $"Hoàn tất \"{name}\" - {total} dòng, {(int)el.TotalMinutes}:{el.Seconds:00}");
                });
            }
        }

        public static string DescribeError(string l)
        {
            int.TryParse(l.Split(':').LastOrDefault()?.Trim(), out int n);
            return n switch
            {
                1 => "thiếu chữ lệnh G-code",
                2 => "sai định dạng số",
                3 => "lệnh $ không hợp lệ",
                4 => "giá trị âm không cho phép",
                5 => "chưa bật homing ($22)",
                8 => "máy đang chạy (không ở trạng thái rảnh) - lệnh này cần Idle",
                9 => "G-code bị khoá: máy đang báo động, bấm \"Mở khóa\" ($X) hoặc Home",
                10 => "soft limit cần homing trước",
                15 => "đích di chuyển vượt hành trình máy (soft limit)",
                20 => "lệnh G-code không được hỗ trợ",
                21 => "xung đột nhóm lệnh G-code",
                22 => "chưa đặt tốc độ F",
                23 => "lệnh cần số nguyên",
                24 => "2 lệnh cùng dùng chung trục",
                33 => "mục tiêu di chuyển không hợp lệ",
                38 => "dòng G-code quá dài",
                _ => "mã lỗi " + n
            };
        }

        public static string DescribeAlarm(string l)
        {
            int.TryParse(l.Split(':').LastOrDefault()?.Trim(), out int n);
            return n switch
            {
                1 => "chạm công tắc hành trình cứng (hard limit)",
                2 => "lệnh vượt hành trình máy (soft limit)",
                3 => "bị reset khi đang chạy - có thể đã mất vị trí, nên Home lại",
                4 => "dò (probe) lỗi",
                5 => "dò (probe) lỗi",
                6 => "Home thất bại: chưa nhận công tắc",
                7 => "Home thất bại: cửa mở",
                8 => "Home thất bại: không tìm thấy công tắc",
                9 => "Home thất bại: không tìm thấy công tắc",
                _ => "mã báo động " + n
            };
        }
    }

    // =====================================================================
    //  Cửa sổ điều khiển máy
    // =====================================================================
    public sealed class MachineForm : Form
    {
        private static readonly double[] Steps = { 0, 0.1, 0.5, 1, 5, 10, 50 };
        private static readonly string[] StepNames = { "Liên tục (giữ chuột)", "0.1 mm", "0.5 mm", "1 mm", "5 mm", "10 mm", "50 mm" };
        private static readonly string[] AnchorText = { "↖", "↑", "↗", "←", "●", "→", "↙", "↓", "↘" };
        private static readonly string[] AnchorTip =
        {
            "Góc trên-trái", "Giữa cạnh trên", "Góc trên-phải",
            "Giữa cạnh trái", "Tâm khung sản phẩm", "Giữa cạnh phải",
            "Góc dưới-trái", "Giữa cạnh dưới", "Góc dưới-phải"
        };

        private readonly MachineLink link;
        private readonly MachineCfg cfg;
        private readonly Func<bool, JobPackage?> buildJob;       // true = khung đèn đỏ, false = công việc khắc
        private readonly Func<double> travelMm;
        private readonly Action saveCfg;
        private readonly int dpi;
        private bool allowClose, building = true;

        private ComboBox cbConnType = null!, cbPort = null!, cbBaud = null!, cbMode = null!, cbStep = null!;
        private TextBox txtNetHost = null!;
        private NumericUpDown nudNetPort = null!;
        private Button btnRefresh = null!, btnConnect = null!, btnFrame = null!, btnStart = null!, btnPause = null!, btnStop = null!,
                       btnSetOrigin = null!, btnHome = null!, btnUnlock = null!, btnSend = null!, btnClear = null!;
        private Button[] jogBtns = Array.Empty<Button>();
        private readonly RadioButton[] anchorBtns = new RadioButton[9];
        private Label lblState = null!, lblPos = null!, lblMach = null!, lblJob = null!, lblModeNote = null!;
        private NumericUpDown nudFeed = null!;
        private ProgressBar pb = null!;
        private RichTextBox rtb = null!;
        private TextBox txtCmd = null!;
        private readonly List<string> history = new();
        private int histPos;
        private readonly ToolTip tip = new();

        private CheckBox chkAutoReconnect = null!;
        private readonly System.Windows.Forms.Timer autoReconnectTimer = new System.Windows.Forms.Timer();
        private bool wasConnectedBeforeDisconnect;
        private string lastConnectedPort = "";
        private int lastConnectedBaud = 115200;

        private int S(int v) => (int)Math.Round(v * dpi / 96f);

        public MachineForm(MachineLink link, MachineCfg cfg, int dpi, Func<bool, JobPackage?> buildJob, Func<double> travelMm, Action saveCfg)
        {
            this.link = link; this.cfg = cfg; this.dpi = dpi; this.buildJob = buildJob; this.travelMm = travelMm; this.saveCfg = saveCfg;

            Text = "Điều khiển máy";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(S(800), wa.Width - S(40)), Math.Min(S(680), wa.Height - S(90)));
            MinimumSize = new Size(Math.Min(S(640), wa.Width - S(40)), Math.Min(S(520), wa.Height - S(90)));
            Location = new Point(wa.Right - Width - S(20), wa.Top + S(40));

            BuildUi();

            link.Log += AppendLog;
            link.StatusChanged += OnStatus;
            link.ConnectionChanged += OnConnection;
            link.Progress += OnProgress;
            link.JobEnded += OnJobEnded;

            autoReconnectTimer.Interval = 1200; // kiểm tra mỗi 1.2s
            autoReconnectTimer.Tick += AutoReconnectTick;
            autoReconnectTimer.Start();

            RefreshPorts();
            building = false;
            UpdateUi();
        }

        // ------------------------------------------------------------------
        //  Dựng giao diện
        // ------------------------------------------------------------------
        private Button Btn(string text, int w, int h = 34) => new Button
        {
            Text = text, Width = S(w), Height = S(h), UseVisualStyleBackColor = true,
            Margin = new Padding(0, 0, S(6), S(6))
        };

        private FlowLayoutPanel Flow() => new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty
        };

        private GroupBox Grp(string title, Control inner)
        {
            var g = new GroupBox
            {
                Text = title.Replace("&", "&&"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(8), S(4), S(8), S(6)), Margin = new Padding(0, 0, 0, S(6))
            };
            inner.Dock = DockStyle.Top;
            g.Controls.Add(inner);
            return g;
        }

        private Label Lbl(string text, int w, int h = 28) => new Label
        {
            Text = text, Width = S(w), Height = S(h), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, S(4), S(6))
        };

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(8)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            void AddRow(Control c, bool fill = false)
            {
                root.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
                c.Dock = DockStyle.Fill;
                root.Controls.Add(c, 0, row++);
            }

            // ---- Kết nối + vị trí ----
            var fa = Flow();
            cbConnType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(110), Margin = new Padding(0, 0, S(6), S(6)) };
            cbConnType.Items.AddRange(new object[] { "USB / COM", "Wi-Fi Telnet", "WebSocket", "HTTP REST" });
            cbConnType.SelectedIndex = (int)cfg.ConnType;

            cbPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(110), Margin = new Padding(0, 0, S(6), S(6)) };
            btnRefresh = Btn("Làm mới", 80, 28);
            cbBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = S(85), Margin = new Padding(0, 0, S(6), S(6)) };
            cbBaud.Items.AddRange(new object[] { "9600", "57600", "115200", "230400", "250000", "460800", "921600" });
            cbBaud.Text = cfg.Baud.ToString();

            txtNetHost = new TextBox { Width = S(120), Text = cfg.NetworkHost, Margin = new Padding(0, 0, S(4), S(6)) };
            nudNetPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(cfg.NetworkPort, 1, 65535), Width = S(70), Margin = new Padding(0, 0, S(6), S(6)) };

            btnConnect = Btn("Kết nối", 96, 28);
            chkAutoReconnect = new CheckBox
            {
                Text = "Tự kết nối lại",
                Checked = cfg.AutoReconnect,
                AutoSize = true,
                Margin = new Padding(0, S(4), S(6), S(6))
            };
            chkAutoReconnect.CheckedChanged += (s, e) => { cfg.AutoReconnect = chkAutoReconnect.Checked; saveCfg(); };
            tip.SetToolTip(chkAutoReconnect, "Tự động phát hiện và kết nối lại máy khi cáp USB bị lỏng hoặc mạng chập chờn");
            lblState = Lbl("Chưa kết nối", 170); lblState.ForeColor = Color.Gray;
            fa.Controls.AddRange(new Control[] { cbConnType, cbPort, btnRefresh, cbBaud, txtNetHost, nudNetPort, btnConnect, chkAutoReconnect, lblState });

            lblPos = new Label { AutoSize = true, Font = new Font("Consolas", 13f, FontStyle.Bold), Text = "X       0.000   Y       0.000  mm", Margin = new Padding(0, 0, S(12), S(2)) };
            lblMach = new Label { AutoSize = true, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Text = "", Margin = new Padding(0, S(4), 0, S(2)) };
            var fp = Flow(); fp.Controls.AddRange(new Control[] { lblPos, lblMach });
            var colA = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            colA.Controls.Add(fa); colA.Controls.Add(fp);
            AddRow(Grp("Kết nối & vị trí đầu laser", colA));

            // ---- Chạy ----
            btnFrame = Btn("Chạy khung", 112); btnStart = Btn("Bắt đầu", 112); btnPause = Btn("Tạm dừng", 112); btnStop = Btn("Dừng", 90);
            foreach (var b in new[] { btnStart, btnStop }) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.ForeColor = Color.White; }
            pb = new ProgressBar { Width = S(360), Height = S(18), Margin = new Padding(0, S(6), S(8), S(6)) };
            lblJob = Lbl("", 300, 28);
            var fr = Flow(); fr.Controls.AddRange(new Control[] { btnFrame, btnStart, btnPause, btnStop, pb, lblJob });
            AddRow(Grp("Chạy công việc", fr));

            // ---- Gốc công việc | Di chuyển ----
            var two = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            two.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Gốc
            cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(220), Margin = new Padding(0, 0, 0, S(6)) };
            cbMode.Items.AddRange(new object[] { "Tuyệt đối (gốc máy)", "Vị trí hiện tại", "Gốc người dùng" });
            cbMode.SelectedIndex = (int)cfg.Mode;
            var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, Margin = new Padding(0, 0, S(10), 0) };
            for (int i = 0; i < 9; i++)
            {
                var rb = new RadioButton
                {
                    Appearance = Appearance.Button, Text = AnchorText[i], TextAlign = ContentAlignment.MiddleCenter,
                    Width = S(38), Height = S(34), Margin = new Padding(0), Checked = i == cfg.Anchor, Tag = i
                };
                tip.SetToolTip(rb, AnchorTip[i]);
                rb.CheckedChanged += (s, e) =>
                {
                    if (building || s is not RadioButton r || !r.Checked) return;
                    cfg.Anchor = (int)r.Tag!; saveCfg();
                };
                anchorBtns[i] = rb;
                grid.Controls.Add(rb, i % 3, i / 3);
            }
            btnSetOrigin = Btn("Đặt gốc tại đầu laser", 190, 34);
            lblModeNote = new Label { AutoSize = true, MaximumSize = new Size(S(200), 0), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = Padding.Empty };
            var right = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            right.Controls.Add(btnSetOrigin); right.Controls.Add(lblModeNote);
            var gridRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
            gridRow.Controls.Add(grid); gridRow.Controls.Add(right);
            var colD = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            colD.Controls.Add(cbMode); colD.Controls.Add(gridRow);
            var gD = Grp("Gốc công việc (Bắt đầu từ)", colD); gD.Dock = DockStyle.Fill; gD.Margin = new Padding(0, 0, S(4), S(6));
            two.Controls.Add(gD, 0, 0);

            // Di chuyển
            var pad = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, Margin = new Padding(0, 0, S(10), 0) };
            var jb = new List<Button>();
            Button JogBtn(string t, int dx, int dy)
            {
                var b = new Button { Text = t, Width = S(46), Height = S(40), Margin = new Padding(0), UseVisualStyleBackColor = true };
                b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) JogDown(dx, dy); };
                b.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) JogUp(); };
                jb.Add(b); return b;
            }
            pad.Controls.Add(JogBtn("↑", 0, 1), 1, 0);
            pad.Controls.Add(JogBtn("←", -1, 0), 0, 1);
            var btnCenter = new Button { Text = "Gốc", Width = S(46), Height = S(40), Margin = new Padding(0), UseVisualStyleBackColor = true };
            btnCenter.Click += (s, e) => GoOrigin();
            tip.SetToolTip(btnCenter, "Đưa đầu laser về gốc công việc (X0 Y0)");
            jb.Add(btnCenter);
            pad.Controls.Add(btnCenter, 1, 1);
            pad.Controls.Add(JogBtn("→", 1, 0), 2, 1);
            pad.Controls.Add(JogBtn("↓", 0, -1), 1, 2);
            jogBtns = jb.ToArray();

            cbStep = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(170), Margin = new Padding(0, 0, 0, S(6)) };
            cbStep.Items.AddRange(StepNames);
            cbStep.SelectedIndex = Math.Clamp(cfg.StepIndex, 0, StepNames.Length - 1);
            nudFeed = new NumericUpDown { Minimum = 10, Maximum = 30000, Increment = 100, Value = Math.Clamp(cfg.JogFeed, 10, 30000), Width = S(80), Margin = new Padding(0, 0, S(4), S(6)) };
            var feedRow = Flow(); feedRow.Controls.Add(nudFeed); feedRow.Controls.Add(Lbl("mm/phút", 64));
            btnHome = Btn("Home ($H)", 90, 30); btnUnlock = Btn("Mở khóa ($X)", 110, 30);
            var rightE = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            var hb = Flow(); hb.Controls.Add(btnHome); hb.Controls.Add(btnUnlock);
            rightE.Controls.Add(cbStep); rightE.Controls.Add(feedRow); rightE.Controls.Add(hb);
            var rowE = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
            rowE.Controls.Add(pad); rowE.Controls.Add(rightE);
            var gE = Grp("Di chuyển đầu laser", rowE); gE.Dock = DockStyle.Fill; gE.Margin = new Padding(S(4), 0, 0, S(6));
            two.Controls.Add(gE, 1, 0);
            AddRow(two);

            // ---- Console ----
            rtb = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f, FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, DetectUrls = false, HideSelection = false
            };
            txtCmd = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f, FontStyle.Bold), Margin = new Padding(0, S(4), S(6), 0) };
            btnSend = Btn("Gửi", 70, 30); btnClear = Btn("Xóa log", 80, 30);
            btnSend.Margin = new Padding(0, S(4), S(6), 0); btnClear.Margin = new Padding(0, S(4), 0, 0);
            var cmdRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, AutoSize = true };
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmdRow.Controls.Add(txtCmd, 0, 0); cmdRow.Controls.Add(btnSend, 1, 0); cmdRow.Controls.Add(btnClear, 2, 0);
            var cons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            cons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cons.Controls.Add(rtb, 0, 0); cons.Controls.Add(cmdRow, 0, 1);
            var gC = new GroupBox { Text = "Console (gõ lệnh G-code / $ rồi Enter; ? ! ~ gửi tức thời)", Padding = new Padding(S(6), S(2), S(6), S(6)) };
            gC.Controls.Add(cons);
            AddRow(gC, true);

            Controls.Add(root);

            // ---- Sự kiện ----
            cbConnType.SelectedIndexChanged += (s, e) =>
            {
                if (building) return;
                cfg.ConnType = (ConnectionType)cbConnType.SelectedIndex;
                // Gợi ý cổng mặc định cho từng giao thức
                if (cfg.ConnType == ConnectionType.Telnet && nudNetPort.Value == 8080) nudNetPort.Value = 23;
                else if (cfg.ConnType == ConnectionType.WebSocket && nudNetPort.Value == 23) nudNetPort.Value = 8080;
                else if (cfg.ConnType == ConnectionType.HttpRest && (nudNetPort.Value == 23 || nudNetPort.Value == 8080)) nudNetPort.Value = 80;
                cfg.NetworkPort = (int)nudNetPort.Value;
                saveCfg();
                UpdateUi();
            };
            txtNetHost.TextChanged += (s, e) => { if (!building) { cfg.NetworkHost = txtNetHost.Text.Trim(); saveCfg(); } };
            nudNetPort.ValueChanged += (s, e) => { if (!building) { cfg.NetworkPort = (int)nudNetPort.Value; saveCfg(); } };
            btnRefresh.Click += (s, e) => RefreshPorts();
            btnConnect.Click += (s, e) => ToggleConnect();
            btnFrame.Click += (s, e) => RunFrame();
            btnStart.Click += (s, e) => StartJob();
            btnPause.Click += (s, e) => PauseResume();
            btnStop.Click += (s, e) => link.Stop();
            btnSetOrigin.Click += (s, e) => SetOrigin();
            btnHome.Click += (s, e) => { if (Confirm("Chạy Home ($H)? Đầu laser sẽ di chuyển về công tắc hành trình.")) link.SendLine("$H"); };
            btnUnlock.Click += (s, e) => link.SendLine("$X");
            btnSend.Click += (s, e) => SendTyped();
            btnClear.Click += (s, e) => rtb.Clear();
            txtCmd.KeyDown += TxtCmd_KeyDown;
            cbMode.SelectedIndexChanged += (s, e) => { if (building) return; cfg.Mode = (StartFrom)cbMode.SelectedIndex; saveCfg(); UpdateUi(); };
            cbStep.SelectedIndexChanged += (s, e) => { if (building) return; cfg.StepIndex = cbStep.SelectedIndex; saveCfg(); };
            nudFeed.ValueChanged += (s, e) => { if (building) return; cfg.JogFeed = (int)nudFeed.Value; saveCfg(); };
            cbBaud.TextChanged += (s, e) => { if (!building && int.TryParse(cbBaud.Text, out int b) && b > 0) { cfg.Baud = b; saveCfg(); } };
            cbPort.SelectedIndexChanged += (s, e) => { if (!building && cbPort.SelectedItem is string p) { cfg.Port = p; saveCfg(); } };

            tip.SetToolTip(btnFrame, "Chạy viền khung bao của sản phẩm (đèn đỏ / laser công suất thấp) để căn vị trí");
            tip.SetToolTip(btnStart, "Gửi G-code của các lớp đang bật \"Xuất\" xuống máy và khắc");
            tip.SetToolTip(btnStop, "Dừng ngay: giữ + reset mềm, tắt laser");
            tip.SetToolTip(btnSetOrigin, "Đặt toạ độ làm việc (0,0) tại vị trí đầu laser hiện tại (G10 L20 P1)");
        }

        // ------------------------------------------------------------------
        //  Trạng thái giao diện
        // ------------------------------------------------------------------
        private static string StateName(string s)
        {
            string b = s.Split(':')[0];
            return b switch
            {
                "Idle" => "Sẵn sàng", "Run" => "Đang chạy", "Hold" => "Tạm dừng", "Jog" => "Đang di chuyển",
                "Alarm" => "BÁO ĐỘNG", "Door" => "Cửa mở", "Check" => "Chế độ kiểm tra", "Home" => "Đang Home", "Sleep" => "Ngủ",
                _ => b
            };
        }

        private static void Tint(Button b, Color c)
        {
            b.BackColor = b.Enabled ? c : Color.FromArgb(205, 205, 205);
            b.ForeColor = b.Enabled ? Color.White : Color.FromArgb(120, 120, 120);
        }

        private void UpdateUi()
        {
            if (IsDisposed) return;
            bool conn = link.IsOpen, job = link.JobActive;
            string st = link.Last.State ?? "";
            bool idle = st.StartsWith("Idle", StringComparison.OrdinalIgnoreCase);
            bool jog = st.StartsWith("Jog", StringComparison.OrdinalIgnoreCase);
            bool hold = st.StartsWith("Hold", StringComparison.OrdinalIgnoreCase);

            btnConnect.Text = conn ? "Ngắt kết nối" : "Kết nối";
            cbConnType.Enabled = !conn;
            bool isSerial = cfg.ConnType == ConnectionType.Serial;
            cbPort.Visible = cbBaud.Visible = btnRefresh.Visible = isSerial;
            cbPort.Enabled = cbBaud.Enabled = btnRefresh.Enabled = !conn && isSerial;
            txtNetHost.Visible = nudNetPort.Visible = !isSerial;
            txtNetHost.Enabled = nudNetPort.Enabled = !conn && !isSerial;
            btnFrame.Enabled = btnStart.Enabled = conn && !job && idle;
            btnPause.Enabled = conn && (job || hold);
            btnPause.Text = hold ? "Tiếp tục" : "Tạm dừng";
            btnStop.Enabled = conn;
            Tint(btnStart, Color.FromArgb(46, 160, 67));
            Tint(btnStop, Color.FromArgb(200, 50, 50));
            btnSetOrigin.Enabled = conn && !job && idle;
            btnHome.Enabled = btnUnlock.Enabled = conn && !job;
            foreach (var b in jogBtns) b.Enabled = conn && !job && (idle || jog);
            nudFeed.Enabled = cbStep.Enabled = true;
            btnSend.Enabled = txtCmd.Enabled = conn;

            bool rel = cfg.Mode != StartFrom.Absolute;
            foreach (var rb in anchorBtns) rb.Enabled = rel;
            btnSetOrigin.Visible = true;
            lblModeNote.Text = cfg.Mode switch
            {
                StartFrom.Absolute => "Toạ độ canvas = toạ độ máy. Cần Home hoặc gốc máy đã chuẩn.",
                StartFrom.CurrentPosition => "Đưa đầu laser tới vị trí mong muốn rồi Bắt đầu: điểm đã chọn trong 9 ô nằm đúng tại đầu laser.",
                _ => "Dùng gốc đã đặt bằng \"Đặt gốc tại đầu laser\"; điểm chọn trong 9 ô nằm tại gốc đó."
            };
        }

        private void OnConnection(bool open)
        {
            if (IsDisposed) return;
            if (open)
            {
                wasConnectedBeforeDisconnect = true;
                lastConnectedPort = cfg.Port;
                lastConnectedBaud = cfg.Baud;
                AppendLog($"● Đã kết nối {cfg.Port} @ {cfg.Baud}", LogKind.Info);
            }
            else
            {
                AppendLog("○ Đã ngắt kết nối", LogKind.Info);
                lblState.Text = "Chưa kết nối";
                lblState.ForeColor = Color.Gray;
            }
            UpdateUi();
        }

        private void AutoReconnectTick(object? sender, EventArgs e)
        {
            if (IsDisposed || link.IsOpen) return;
            if (!cfg.AutoReconnect || !wasConnectedBeforeDisconnect) return;

            string targetPort = !string.IsNullOrEmpty(lastConnectedPort) ? lastConnectedPort : cfg.Port;
            if (string.IsNullOrEmpty(targetPort)) return;

            // Kiểm tra danh sách cổng COM hiện tại
            var ports = MachineLink.ListPorts();
            if (ports.Contains(targetPort))
            {
                int baud = lastConnectedBaud > 0 ? lastConnectedBaud : cfg.Baud;
                AppendLog($"Đang thử tự động kết nối lại {targetPort}...", LogKind.Info);
                string? err = link.Open(targetPort, baud);
                if (err == null)
                {
                    AppendLog($"✔ Tự động kết nối lại thành công {targetPort}", LogKind.Info);
                    RefreshPorts();
                }
            }
        }

        private void OnStatus(MachineStatus s)
        {
            if (IsDisposed) return;
            lblState.Text = StateName(s.State);
            string b = s.State.Split(':')[0];
            lblState.ForeColor = b switch
            {
                "Idle" => Color.FromArgb(30, 140, 60),
                "Run" or "Jog" or "Home" => Color.FromArgb(210, 120, 0),
                "Hold" => Color.FromArgb(180, 140, 0),
                "Alarm" => Color.Firebrick,
                _ => Color.DimGray
            };
            lblPos.Text = $"X {s.WX,9:0.000}   Y {s.WY,9:0.000}  mm";
            lblMach.Text = $"máy: {s.MX:0.000}, {s.MY:0.000}   F{s.Feed}  S{s.Spindle}";
            UpdateUi();
        }

        private void OnProgress(int done, int total)
        {
            if (IsDisposed || total <= 0) return;
            pb.Maximum = total; pb.Value = Math.Clamp(done, 0, total);
            lblJob.Text = $"{done}/{total} dòng ({100.0 * done / total:0}%)";
        }

        private void OnJobEnded(bool ok, string msg)
        {
            if (IsDisposed) return;
            lblJob.Text = msg;
            AppendLog((ok ? "✔ " : "✖ ") + msg, ok ? LogKind.Info : LogKind.Error);
            if (ok) System.Media.SystemSounds.Asterisk.Play();
            UpdateUi();
        }

        private void AppendLog(string text, LogKind kind)
        {
            if (IsDisposed) return;
            Color c = kind switch
            {
                LogKind.Sent => Color.FromArgb(130, 190, 255),
                LogKind.Error => Color.FromArgb(255, 110, 110),
                LogKind.Info => Color.FromArgb(255, 210, 100),
                _ => Color.Gainsboro
            };
            if (rtb.TextLength > 150000) { rtb.Select(0, 70000); rtb.SelectedText = ""; }
            rtb.SelectionStart = rtb.TextLength; rtb.SelectionLength = 0;
            rtb.SelectionColor = c;
            rtb.AppendText(text + "\n");
            rtb.SelectionColor = rtb.ForeColor;
            rtb.ScrollToCaret();
        }

        // ------------------------------------------------------------------
        //  Hành động
        // ------------------------------------------------------------------
        private void RefreshPorts()
        {
            building = true;
            cbPort.Items.Clear();
            foreach (var p in MachineLink.ListPorts()) cbPort.Items.Add(p);
            int i = cfg.Port.Length > 0 ? cbPort.Items.IndexOf(cfg.Port) : -1;
            if (i < 0 && cbPort.Items.Count > 0) i = 0;
            if (i >= 0) cbPort.SelectedIndex = i;
            building = false;
            if (cbPort.SelectedItem is string sel) cfg.Port = sel;
        }

        private bool Confirm(string msg) =>
            MessageBox.Show(this, msg, "Điều khiển máy", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes;

        private void ToggleConnect()
        {
            if (link.IsOpen)
            {
                if (link.JobActive && !Confirm("Máy đang chạy. Ngắt kết nối sẽ DỪNG máy. Tiếp tục?")) return;
                wasConnectedBeforeDisconnect = false; // Người dùng chủ động ngắt -> không tự kết nối lại
                if (link.JobActive) link.Stop();
                link.Close();
                return;
            }

            string? err = null;
            if (cfg.ConnType == ConnectionType.Serial)
            {
                if (cbPort.SelectedItem is not string port)
                {
                    MessageBox.Show(this, "Không thấy cổng COM nào. Cắm cáp USB của máy rồi bấm \"Làm mới\".", "Điều khiển máy");
                    return;
                }
                if (!int.TryParse(cbBaud.Text, out int baud) || baud <= 0) { MessageBox.Show(this, "Baud không hợp lệ.", "Điều khiển máy"); return; }
                cfg.Port = port; cfg.Baud = baud; saveCfg();
                err = link.Open(port, baud);
                if (err != null)
                {
                    AppendLog("Không mở được " + port + ": " + err, LogKind.Error);
                    MessageBox.Show(this, "Không mở được " + port + ":\n" + err + "\n\nCổng có thể đang được phần mềm khác dùng.", "Điều khiển máy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                string host = txtNetHost.Text.Trim();
                int netPort = (int)nudNetPort.Value;
                if (string.IsNullOrEmpty(host))
                {
                    MessageBox.Show(this, "Vui lòng nhập địa chỉ IP của bộ điều khiển (ví dụ 192.168.1.100).", "Điều khiển máy");
                    return;
                }
                cfg.NetworkHost = host; cfg.NetworkPort = netPort; saveCfg();

                AppendLog($"Đang kết nối tới {host}:{netPort} ({cfg.ConnType})...", LogKind.Info);

                err = cfg.ConnType switch
                {
                    ConnectionType.Telnet => link.OpenTelnet(host, netPort),
                    ConnectionType.WebSocket => link.OpenWebSocket(host, netPort),
                    ConnectionType.HttpRest => link.OpenHttpRest(host, netPort),
                    _ => "Loại kết nối không hợp lệ"
                };

                if (err != null)
                {
                    AppendLog($"Không kết nối được tới {host}:{netPort}: {err}", LogKind.Error);
                    MessageBox.Show(this, $"Không kết nối được tới {host}:{netPort}:\n{err}\n\nHãy kiểm tra xem ESP32/FluidNC đã bật Wi-Fi và cùng dải mạng chưa.", "Điều khiển máy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static string[] PrefixFor(StartFrom m) =>
            m == StartFrom.CurrentPosition ? new[] { "G90", "G54", "G10 L20 P1 X0 Y0" } : new[] { "G90", "G54" };

        private string ModeText() => cfg.Mode switch
        {
            StartFrom.Absolute => "Tuyệt đối (gốc máy)",
            StartFrom.CurrentPosition => "Vị trí hiện tại - điểm \"" + AnchorTip[Math.Clamp(cfg.Anchor, 0, 8)] + "\" tại đầu laser",
            _ => "Gốc người dùng - điểm \"" + AnchorTip[Math.Clamp(cfg.Anchor, 0, 8)] + "\" tại gốc"
        };

        // Gửi 1 gói G-code xuống máy. Trả true nếu đã bắt đầu chạy.
        public bool RunPackage(JobPackage pk)
        {
            if (!link.IsOpen) { MessageBox.Show(this, "Chưa kết nối máy.", "Điều khiển máy"); return false; }
            if (link.JobActive) { MessageBox.Show(this, "Máy đang chạy một công việc khác.", "Điều khiển máy"); return false; }
            string st = link.Last.State ?? "";
            if (!st.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"Máy chưa sẵn sàng (trạng thái: {StateName(st)}).\nNếu đang báo động, bấm \"Mở khóa\" hoặc Home.", "Điều khiển máy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var lines = new List<string>(PrefixFor(cfg.Mode));
            lines.AddRange(pk.Code.Split('\n'));
            string? err = link.StartJob(lines, pk.Name);
            if (err != null) { MessageBox.Show(this, err, "Điều khiển máy", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            lblJob.Text = pk.Name + "...";
            AppendLog($"▶ {pk.Name}: {pk.Info}  |  {ModeText()}", LogKind.Info);
            UpdateUi();
            return true;
        }

        public void RunFrame()
        {
            var pk = buildJob(true);
            if (pk != null) RunPackage(pk);
        }

        private void StartJob()
        {
            var pk = buildJob(false);
            if (pk == null) return;
            var rs = MessageBox.Show(this,
                $"Bắt đầu khắc?\n\nKích thước: {pk.Info}\nBắt đầu từ: {ModeText()}\n\n" +
                "Laser sẽ BẮN THẬT. Kiểm tra vật liệu, kính bảo hộ và vị trí đầu laser trước khi tiếp tục.",
                "Bắt đầu khắc", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (rs == DialogResult.Yes) RunPackage(pk);
        }

        private void PauseResume()
        {
            if ((link.Last.State ?? "").StartsWith("Hold", StringComparison.OrdinalIgnoreCase)) link.Resume();
            else link.Hold();
        }

        private void SetOrigin()
        {
            if (!link.SendLine("G10 L20 P1 X0 Y0")) return;
            building = true; cbMode.SelectedIndex = (int)StartFrom.UserOrigin; building = false;
            cfg.Mode = StartFrom.UserOrigin; saveCfg();
            AppendLog("Đã đặt gốc công việc (0,0) tại vị trí đầu laser hiện tại.", LogKind.Info);
            UpdateUi();
        }

        private void GoOrigin()
        {
            if (!link.IsOpen) return;
            link.SendLine("G90 G54 G0 X0 Y0");
        }

        private void JogDown(int dx, int dy)
        {
            if (!link.IsOpen || link.JobActive) return;
            double step = Steps[Math.Clamp(cbStep.SelectedIndex, 0, Steps.Length - 1)];
            int feed = (int)nudFeed.Value;
            if (step <= 0)
            {
                double d = Math.Max(10, travelMm());
                link.Jog(dx * d, dy * d, feed);          // giữ chuột để chạy liên tục; thả chuột -> huỷ lệnh jog
            }
            else link.Jog(dx * step, dy * step, feed);
        }

        private void JogUp()
        {
            if (!link.IsOpen) return;
            if (Steps[Math.Clamp(cbStep.SelectedIndex, 0, Steps.Length - 1)] <= 0) link.JogCancel();
        }

        // ---- Console ----
        private void SendTyped()
        {
            string t = txtCmd.Text.Trim();
            txtCmd.Clear();
            if (t.Length == 0 || !link.IsOpen) return;
            if (history.Count == 0 || history[^1] != t) history.Add(t);
            histPos = history.Count;
            if (t == "?") { link.Realtime((byte)'?'); return; }
            if (t == "!") { link.Hold(); return; }
            if (t == "~") { link.Resume(); return; }
            if (link.JobActive) { AppendLog("Đang chạy công việc - chỉ gửi được ? ! ~ (hoặc bấm Dừng).", LogKind.Error); return; }
            link.SendLine(t);
        }

        private void TxtCmd_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendTyped(); }
            else if (e.KeyCode == Keys.Up && history.Count > 0) { histPos = Math.Max(0, histPos - 1); txtCmd.Text = history[histPos]; txtCmd.SelectionStart = txtCmd.TextLength; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down && history.Count > 0)
            {
                histPos = Math.Min(history.Count, histPos + 1);
                txtCmd.Text = histPos < history.Count ? history[histPos] : "";
                txtCmd.SelectionStart = txtCmd.TextLength; e.SuppressKeyPress = true;
            }
        }

        // ------------------------------------------------------------------
        //  Đóng cửa sổ = ẩn đi (kết nối vẫn giữ)
        // ------------------------------------------------------------------
        public void Terminate() { allowClose = true; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            link.Log -= AppendLog; link.StatusChanged -= OnStatus; link.ConnectionChanged -= OnConnection;
            link.Progress -= OnProgress; link.JobEnded -= OnJobEnded;
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !building) { RefreshPortsKeepOpen(); UpdateUi(); }
        }

        private void RefreshPortsKeepOpen() { if (!link.IsOpen) RefreshPorts(); }
    }

    // =====================================================================
    //  Gắn vào Form1
    // =====================================================================
    public partial class Form1
    {
        private MachineLink? machine;
        private MachineForm? machineForm;
        private MachineCfg machCfg = new();

        private static string MachineCfgPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "machine.json");

        private void InitMachine()
        {
            if (machine != null) return;
            try
            {
                if (File.Exists(MachineCfgPath))
                {
                    var c = JsonSerializer.Deserialize<MachineCfg>(File.ReadAllText(MachineCfgPath));
                    if (c != null)
                    {
                        c.Anchor = Math.Clamp(c.Anchor, 0, 8);
                        c.Baud = c.Baud > 0 ? c.Baud : 115200;
                        machCfg = c;
                    }
                }
            }
            catch { /* file hỏng thì dùng mặc định */ }

            machine = new MachineLink();
            machineSend = MachineSendFrame;              // "Xuất G-code khung đèn đỏ" -> chạy thẳng trên máy khi đã kết nối

            var root = new ToolStripMenuItem("Máy");
            var miOpen = new ToolStripMenuItem("Điều khiển máy...") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.M };
            miOpen.Click += (s, e) => OpenMachineWindow();
            var miFrame = new ToolStripMenuItem("Chạy khung (căn vị trí)");
            miFrame.Click += (s, e) => { OpenMachineWindow(); machineForm?.RunFrame(); };
            var miDisc = new ToolStripMenuItem("Ngắt kết nối máy");
            miDisc.Click += (s, e) => machine?.Close();
            root.DropDownItems.AddRange(new ToolStripItem[] { miOpen, miFrame, new ToolStripSeparator(), miDisc });

            int idx = -1;
            for (int i = 0; i < menuStrip1.Items.Count; i++) if (menuStrip1.Items[i].Text == "Khổ in") { idx = i; break; }
            menuStrip1.Items.Insert(idx >= 0 ? idx + 1 : menuStrip1.Items.Count, root);

            InitMaterials();                             // menu "Vật liệu" (PHẦN 2 bên dưới)

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing && machine?.JobActive == true &&
                    MessageBox.Show(this, "Máy đang chạy một công việc. Thoát phần mềm sẽ DỪNG máy.\n\nVẫn thoát?", "Điều khiển máy",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    e.Cancel = true;
            };
            FormClosed += (s, e) =>
            {
                SaveMachineCfg();
                try { if (machine?.JobActive == true) machine.Stop(); } catch { }
                machineForm?.Terminate();
                machine?.Dispose();
            };
        }

        private void SaveMachineCfg()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MachineCfgPath)!);
                File.WriteAllText(MachineCfgPath, JsonSerializer.Serialize(machCfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void OpenMachineWindow()
        {
            if (machine == null) return;
            if (machineForm == null || machineForm.IsDisposed)
                machineForm = new MachineForm(machine, machCfg, DeviceDpi, BuildMachineJob,
                                              () => Math.Max(printCfg.WidthMm, printCfg.HeightMm), SaveMachineCfg);
            if (!machineForm.Visible) machineForm.Show(this);
            machineForm.BringToFront();
        }

        // Toạ độ G-code theo chế độ "Bắt đầu từ": Tuyệt đối = như xuất file; 2 chế độ còn lại = điểm đã chọn (9 ô) của khung sản phẩm là (0,0)
        private GCodeSettings BuildMachineSettings()
        {
            var c = BuildGCodeSettings();
            if (machCfg.Mode != StartFrom.Absolute)
            {
                var bb = new GCodeCompiler(c).GetBoundsPx(ExportItems());
                if (bb is RectangleF r)
                {
                    int a = Math.Clamp(machCfg.Anchor, 0, 8);
                    c.OriginAtJobCorner = false;
                    c.AbsOriginXpx = r.Left + r.Width * (a % 3) / 2f;
                    c.AbsOriginYpx = r.Top + r.Height * (a / 3) / 2f;
                }
            }
            return c;
        }

        private JobPackage? BuildMachineJob(bool frame)
        {
            if (projectItems.Count == 0) { MessageBox.Show(this, "Canvas đang trống.", "Điều khiển máy"); return null; }
            var items = ExportItems();
            if (items.Count == 0)
            {
                MessageBox.Show(this, "Không có đối tượng nào ở lớp đang bật \"Xuất\".\nHãy bật \"Xuất\" cho ít nhất một lớp có đối tượng.", "Điều khiển máy");
                return null;
            }
            if (!frame && machCfg.Mode == StartFrom.Absolute && !ConfirmInsideArea()) return null;
            try
            {
                var comp = new GCodeCompiler(BuildMachineSettings());
                GCodeResult res = frame ? comp.CompileFrame(items, BuildFrameOptions()) : comp.Compile(items);
                string t = res.EstimatedMinutes >= 1 ? $"{res.EstimatedMinutes:0.#} phút" : $"{res.EstimatedMinutes * 60:0} giây";
                return new JobPackage(frame ? "Khung đèn đỏ" : "Khắc", res.Code,
                                      $"{res.WidthMm:0.##} × {res.HeightMm:0.##} mm, ước tính {t}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không tạo được G-code:\n" + ex.Message, "Điều khiển máy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        // Hook của "Xuất G-code khung đèn đỏ" (Form1_PrintArea.cs). true = đã xử lý (không hiện hộp lưu file nữa)
        private bool MachineSendFrame(string code)
        {
            if (machine == null || !machine.IsOpen) return false;
            var rs = MessageBox.Show(this,
                "Đang kết nối máy.\n\nCó = chạy khung ngay trên máy\nKhông = lưu thành file G-code\nHủy = bỏ qua",
                "Khung đèn đỏ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (rs == DialogResult.Cancel) return true;
            if (rs == DialogResult.No) return false;
            OpenMachineWindow();
            machineForm?.RunPackage(new JobPackage("Khung đèn đỏ", code, "khung bao sản phẩm"));
            return true;
        }
    }
}
namespace test01_1014
{
    using System.IO.Ports;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text;
    using Size = System.Drawing.Size;       // tránh nhập nhằng với OpenCvSharp
    using System.Globalization;
    using System.Text.Json;
    using Point = System.Drawing.Point;
    using Font = System.Drawing.Font;

    public class MaterialPreset
    {
        public string Name { get; set; } = "";
        public double ThicknessMm { get; set; }               // 0 = không ghi độ dày
        public string Operation { get; set; } = "Khắc";       // Khắc / Cắt / Đánh dấu / ... (tự gõ được)
        public int Power { get; set; } = 80;                  // %
        public int Speed { get; set; } = 1000;                // mm/phút
        public int Passes { get; set; } = 1;
        public int FillMode { get; set; }                     // 0 = đường viền, 1 = tô kín, 2 = tô kín + viền
        public double FillInterval { get; set; } = 0.1;
        public float FillAngle { get; set; }
        public bool FillCross { get; set; }
        public bool FillBidir { get; set; } = true;
        public string Note { get; set; } = "";

        [System.Text.Json.Serialization.JsonIgnore]
        public string Title => (Name.Trim() + (ThicknessMm > 0 ? " " + ThicknessMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm" : "")).Trim();

        [System.Text.Json.Serialization.JsonIgnore]
        public string Key => Name.Trim().ToLowerInvariant() + "|" + ThicknessMm.ToString("0.###", CultureInfo.InvariantCulture) + "|" + Operation.Trim().ToLowerInvariant();

        [System.Text.Json.Serialization.JsonIgnore]
        public string Summary => $"{Power}%, F{Speed}, {Passes} lần" + (FillMode == 0 ? "" : FillMode == 1 ? ", tô kín" : ", tô kín + viền");

        public MaterialPreset Clone() => (MaterialPreset)MemberwiseClone();

        public void Normalize()
        {
            Name = (Name ?? "").Trim(); Operation = (Operation ?? "").Trim(); Note = Note ?? "";
            ThicknessMm = Math.Clamp(ThicknessMm, 0, 1000);
            Power = Math.Clamp(Power, 0, 100); Speed = Math.Clamp(Speed, 1, 60000); Passes = Math.Clamp(Passes, 1, 100);
            FillMode = Math.Clamp(FillMode, 0, 2); FillInterval = Math.Clamp(FillInterval, 0.02, 5); FillAngle = Math.Clamp(FillAngle, -360f, 360f);
        }
    }

    // ------------------------------------------------------------------
    //  Hộp thoại thêm / sửa một vật liệu
    // ------------------------------------------------------------------
    internal sealed class MaterialEditDialog : Form
    {
        public MaterialPreset Result { get; private set; }
        private readonly TextBox tbName, tbNote;
        private readonly NumericUpDown nTh, nPw, nSp, nPs, nInt, nAng;
        private readonly ComboBox cbOp, cbMode;
        private readonly CheckBox chBi, chCross;

        public MaterialEditDialog(MaterialPreset src, int dpi, string title)
        {
            int S(int v) => (int)Math.Round(v * dpi / 96f);
            Result = src.Clone();
            Text = title;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(440), S(590));

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(S(12), S(10), S(12), S(4)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            int row = 0;
            Label L(string s) => new Label { Text = s, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Height = S(30), Margin = new Padding(0, S(3), 0, S(3)) };
            void Row(string label, Control c, int h = 30)
            {
                c.Dock = DockStyle.Fill; c.Margin = new Padding(0, S(3), 0, S(3));
                t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(h + 6)));
                t.Controls.Add(L(label), 0, row); t.Controls.Add(c, 1, row); row++;
            }
            NumericUpDown N(decimal min, decimal max, decimal v, int dec, decimal inc) =>
                new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = dec, Increment = inc, Value = Math.Min(max, Math.Max(min, v)) };

            tbName = new TextBox { Text = src.Name };
            nTh = N(0, 1000, (decimal)src.ThicknessMm, 2, 0.5m);
            cbOp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = src.Operation };
            cbOp.Items.AddRange(new object[] { "Khắc", "Cắt", "Đánh dấu", "Khắc ảnh" });
            nPw = N(0, 100, src.Power, 0, 5);
            nSp = N(1, 60000, src.Speed, 0, 100);
            nPs = N(1, 100, src.Passes, 0, 1);
            cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbMode.Items.AddRange(new object[] { "Đường viền (Line)", "Tô kín (Fill)", "Tô kín + viền" });
            cbMode.SelectedIndex = Math.Clamp(src.FillMode, 0, 2);
            nInt = N(0.02m, 5m, (decimal)src.FillInterval, 2, 0.01m);
            nAng = N(-360, 360, (decimal)src.FillAngle, 0, 5);
            chBi = new CheckBox { Text = "Quét 2 chiều", Checked = src.FillBidir, AutoSize = true };
            chCross = new CheckBox { Text = "Quét chéo", Checked = src.FillCross, AutoSize = true };
            var chk = new FlowLayoutPanel { WrapContents = false };
            chk.Controls.Add(chBi); chk.Controls.Add(chCross);
            tbNote = new TextBox { Text = src.Note, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };

            Row("Tên vật liệu", tbName);
            Row("Độ dày (mm, 0 = bỏ trống)", nTh);
            Row("Công việc", cbOp);
            Row("Công suất (%)", nPw);
            Row("Tốc độ F (mm/phút)", nSp);
            Row("Số lần chạy", nPs);
            Row("Chế độ khắc", cbMode);
            Row("Cách dòng tô (mm)", nInt);
            Row("Góc quét (°)", nAng);
            Row("Kiểu quét", chk);
            Row("Ghi chú", tbNote, 90);
            Controls.Add(t);

            void SyncFill() { bool on = cbMode.SelectedIndex != 0; nInt.Enabled = nAng.Enabled = chBi.Enabled = chCross.Enabled = on; }
            cbMode.SelectedIndexChanged += (s, e) => SyncFill();
            SyncFill();

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = S(48), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(S(12), S(6), S(12), S(6)) };
            var ok = new Button { Text = "Lưu", DialogResult = DialogResult.OK, Width = S(90), Height = S(32) };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Width = S(90), Height = S(32) };
            bar.Controls.Add(cancel); bar.Controls.Add(ok);
            Controls.Add(bar);
            AcceptButton = ok; CancelButton = cancel;
            Shown += (s, e) => { tbName.Focus(); tbName.SelectAll(); };

            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                if (string.IsNullOrWhiteSpace(tbName.Text))
                {
                    MessageBox.Show(this, "Hãy nhập tên vật liệu.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true; tbName.Focus(); return;
                }
                Result = new MaterialPreset
                {
                    Name = tbName.Text.Trim(), ThicknessMm = (double)nTh.Value, Operation = cbOp.Text.Trim(),
                    Power = (int)nPw.Value, Speed = (int)nSp.Value, Passes = (int)nPs.Value,
                    FillMode = cbMode.SelectedIndex, FillInterval = (double)nInt.Value, FillAngle = (float)nAng.Value,
                    FillCross = chCross.Checked, FillBidir = chBi.Checked, Note = tbNote.Text.Trim()
                };
                Result.Normalize();
            };
        }
    }

    // ------------------------------------------------------------------
    //  Cửa sổ thư viện vật liệu
    // ------------------------------------------------------------------
    internal sealed class MaterialLibraryDialog : Form
    {
        private readonly List<MaterialPreset> lib;
        private readonly Func<MaterialPreset> fromLayer;      // thông số của lớp đang chọn
        private readonly Func<string> layerInfo;
        private readonly Action<MaterialPreset> apply;
        private readonly Action save;
        private readonly int dpi;
        private readonly ListView lv;
        private readonly TextBox tbFind;
        private readonly Label lblInfo;

        private int S(int v) => (int)Math.Round(v * dpi / 96f);

        public MaterialLibraryDialog(List<MaterialPreset> lib, int dpi, Func<MaterialPreset> fromLayer, Func<string> layerInfo,
                                     Action<MaterialPreset> apply, Action save)
        {
            this.lib = lib; this.dpi = dpi; this.fromLayer = fromLayer; this.layerInfo = layerInfo; this.apply = apply; this.save = save;
            Text = "Thư viện vật liệu";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(S(960), wa.Width - S(40)), Math.Min(S(560), wa.Height - S(90)));
            MinimumSize = new Size(Math.Min(S(720), wa.Width - S(40)), Math.Min(S(380), wa.Height - S(90)));

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, GridLines = true, HideSelection = false
            };
            lv.Columns.Add("Vật liệu", S(190)); lv.Columns.Add("Công việc", S(80)); lv.Columns.Add("Công suất", S(80));
            lv.Columns.Add("Tốc độ", S(70)); lv.Columns.Add("Số lần", S(60)); lv.Columns.Add("Chế độ", S(100)); lv.Columns.Add("Ghi chú", S(300));
            lv.DoubleClick += (s, e) => ApplySelected();
            lv.SelectedIndexChanged += (s, e) => UpdateButtons();

            // thanh nút dưới
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(S(8), S(6), S(8), S(2)) };
            Button B(string text, int w, Action act)
            {
                var b = new Button { Text = text, Width = S(w), Height = S(32), Margin = new Padding(0, 0, S(6), S(6)), UseVisualStyleBackColor = true };
                b.Click += (s, e) => act(); bar.Controls.Add(b); return b;
            }
            btnApply = B("Áp dụng cho lớp đang chọn", 210, ApplySelected);
            B("Lưu lớp đang chọn...", 170, AddFromLayer);
            btnEdit = B("Sửa...", 70, EditSelected);
            btnUpdate = B("Cập nhật từ lớp", 140, UpdateFromLayer);
            btnDup = B("Nhân bản", 90, DuplicateSelected);
            btnDel = B("Xóa", 60, DeleteSelected);
            B("Nhập...", 70, ImportFile);
            btnExp = B("Xuất...", 70, ExportFile);
            B("Đóng", 70, Close);

            // hàng tìm kiếm + thông tin lớp
            var find = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(36), Padding = new Padding(S(8), S(4), S(8), 0) };
            find.Controls.Add(new Label { Text = "Tìm:", Width = S(40), Height = S(26), TextAlign = ContentAlignment.MiddleLeft });
            tbFind = new TextBox { Width = S(260), Margin = new Padding(0, S(1), 0, 0) };
            tbFind.TextChanged += (s, e) => Reload();
            find.Controls.Add(tbFind);
            lblInfo = new Label { Dock = DockStyle.Top, Height = S(28), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(S(8), 0, 0, 0), ForeColor = Color.FromArgb(40, 60, 100), BackColor = Color.FromArgb(222, 229, 240) };

            Controls.Add(lv);          // Fill trước, các thanh Bottom / Top thêm sau -> được dock trước
            Controls.Add(bar);
            Controls.Add(find);
            Controls.Add(lblInfo);
            Reload();
        }

        private Button btnApply = null!, btnEdit = null!, btnUpdate = null!, btnDup = null!, btnDel = null!, btnExp = null!;

        private MaterialPreset? Sel => lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as MaterialPreset : null;

        private void UpdateButtons()
        {
            bool has = Sel != null;
            btnApply.Enabled = btnEdit.Enabled = btnUpdate.Enabled = btnDup.Enabled = btnDel.Enabled = has;
            btnExp.Enabled = lib.Count > 0;
        }

        private void Reload(MaterialPreset? keep = null)
        {
            keep ??= Sel;
            lblInfo.Text = layerInfo();
            string q = tbFind.Text.Trim().ToLowerInvariant();
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var p in lib.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.ThicknessMm).ThenBy(x => x.Operation))
            {
                if (q.Length > 0 && !(p.Title + " " + p.Operation + " " + p.Note).ToLowerInvariant().Contains(q)) continue;
                var it = new ListViewItem(p.Title) { Tag = p };
                it.SubItems.Add(p.Operation);
                it.SubItems.Add(p.Power + "%");
                it.SubItems.Add("F" + p.Speed);
                it.SubItems.Add(p.Passes.ToString());
                it.SubItems.Add(p.FillMode == 0 ? "Đường viền" : p.FillMode == 1 ? "Tô kín" : "Tô kín + viền");
                it.SubItems.Add(p.Note.Replace("\r", " ").Replace("\n", " "));
                lv.Items.Add(it);
                if (ReferenceEquals(p, keep)) it.Selected = true;
            }
            lv.EndUpdate();
            UpdateButtons();
        }

        private void ApplySelected()
        {
            if (Sel is not { } p) return;
            apply(p);
            DialogResult = DialogResult.OK;
            Close();
        }

        // thêm mới; trùng (tên + độ dày + công việc) thì hỏi ghi đè
        private void AddPreset(MaterialPreset np)
        {
            np.Normalize();
            var dup = lib.FirstOrDefault(x => x.Key == np.Key);
            if (dup != null)
            {
                var rs = MessageBox.Show(this, $"Đã có \"{dup.Title}\" ({dup.Operation}). Ghi đè bằng thông số mới?", Text,
                                         MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (rs != DialogResult.Yes) return;
                lib.Remove(dup);
            }
            lib.Add(np);
            save();
            Reload(np);
        }

        private void AddFromLayer()
        {
            var p = fromLayer();
            using var d = new MaterialEditDialog(p, dpi, "Lưu thông số lớp thành vật liệu mới");
            if (d.ShowDialog(this) == DialogResult.OK) AddPreset(d.Result);
        }

        private void EditSelected()
        {
            if (Sel is not { } p) return;
            using var d = new MaterialEditDialog(p, dpi, "Sửa vật liệu");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var np = d.Result;
            var clash = lib.FirstOrDefault(x => !ReferenceEquals(x, p) && x.Key == np.Key);
            if (clash != null)
            {
                if (MessageBox.Show(this, $"Đã có \"{clash.Title}\" ({clash.Operation}). Ghi đè mục đó?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                lib.Remove(clash);
            }
            int i = lib.IndexOf(p);
            if (i >= 0) lib[i] = np; else lib.Add(np);
            save();
            Reload(np);
        }

        private void UpdateFromLayer()
        {
            if (Sel is not { } p) return;
            if (MessageBox.Show(this, $"Ghi thông số của lớp đang chọn đè lên \"{p.Title}\"?\n(Tên, độ dày, công việc, ghi chú giữ nguyên)", Text,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var l = fromLayer();
            p.Power = l.Power; p.Speed = l.Speed; p.Passes = l.Passes;
            p.FillMode = l.FillMode; p.FillInterval = l.FillInterval; p.FillAngle = l.FillAngle; p.FillCross = l.FillCross; p.FillBidir = l.FillBidir;
            save();
            Reload(p);
        }

        private void DuplicateSelected()
        {
            if (Sel is not { } p) return;
            var c = p.Clone();
            c.Name = p.Name + " (bản sao)";
            lib.Add(c);
            save();
            Reload(c);
        }

        private void DeleteSelected()
        {
            if (Sel is not { } p) return;
            if (MessageBox.Show(this, $"Xóa vật liệu \"{p.Title}\" ({p.Operation})?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            lib.Remove(p);
            save();
            Reload();
        }

        private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

        private void ExportFile()
        {
            using var sfd = new SaveFileDialog { Filter = "Thư viện vật liệu (*.json)|*.json", FileName = "vat-lieu.json", Title = "Xuất thư viện vật liệu" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(lib, JsonOpt), new System.Text.UTF8Encoding(false));
                MessageBox.Show(this, $"Đã xuất {lib.Count} vật liệu.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, "Không ghi được file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ImportFile()
        {
            using var ofd = new OpenFileDialog { Filter = "Thư viện vật liệu (*.json)|*.json|Tất cả (*.*)|*.*", Title = "Nhập thư viện vật liệu" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var list = JsonSerializer.Deserialize<List<MaterialPreset>>(File.ReadAllText(ofd.FileName)) ?? new();
                int added = 0, replaced = 0;
                foreach (var p in list)
                {
                    p.Normalize();
                    if (p.Name.Length == 0) continue;
                    var dup = lib.FirstOrDefault(x => x.Key == p.Key);
                    if (dup != null) { lib.Remove(dup); replaced++; } else added++;
                    lib.Add(p);
                }
                save();
                Reload();
                MessageBox.Show(this, $"Đã nhập: thêm {added}, ghi đè {replaced} vật liệu.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, "File không đúng định dạng:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    // ------------------------------------------------------------------
    //  Gắn vào Form1
    // ------------------------------------------------------------------
    public partial class Form1
    {
        private readonly List<MaterialPreset> materials = new();
        private ToolStripMenuItem? miMaterialRoot;

        private static string MaterialsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "materials.json");

        private void InitMaterials()
        {
            if (miMaterialRoot != null) return;
            LoadMaterials();
            miMaterialRoot = new ToolStripMenuItem("Vật liệu");
            miMaterialRoot.DropDownOpening += (s, e) => RebuildMaterialMenu();
            RebuildMaterialMenu();

            int idx = -1;
            for (int i = 0; i < menuStrip1.Items.Count; i++) if (menuStrip1.Items[i].Text == "Máy") { idx = i; break; }
            menuStrip1.Items.Insert(idx >= 0 ? idx + 1 : menuStrip1.Items.Count, miMaterialRoot);
        }

        private void LoadMaterials()
        {
            try
            {
                if (!File.Exists(MaterialsPath)) return;
                var list = JsonSerializer.Deserialize<List<MaterialPreset>>(File.ReadAllText(MaterialsPath));
                if (list == null) return;
                materials.Clear();
                foreach (var p in list) { p.Normalize(); if (p.Name.Length > 0) materials.Add(p); }
            }
            catch { /* file hỏng thì bắt đầu thư viện trống */ }
        }

        private void SaveMaterials()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MaterialsPath)!);
                File.WriteAllText(MaterialsPath, JsonSerializer.Serialize(materials, new JsonSerializerOptions { WriteIndented = true }),
                                  new System.Text.UTF8Encoding(false));
            }
            catch { }
            RebuildMaterialMenu();
        }

        private MaterialPreset MaterialFromCurrentLayer()
        {
            var l = layers[Math.Clamp(curLayer, 0, layers.Count - 1)];
            return new MaterialPreset
            {
                Name = "", Operation = l.FillMode == 0 ? "Khắc" : "Khắc ảnh / tô",
                Power = l.Power, Speed = l.Speed, Passes = l.Passes,
                FillMode = l.FillMode, FillInterval = l.FillInterval, FillAngle = l.FillAngle, FillCross = l.FillCross, FillBidir = l.FillBidir
            };
        }

        private string CurrentLayerInfo()
        {
            if (layers.Count == 0) return "";
            var l = layers[Math.Clamp(curLayer, 0, layers.Count - 1)];
            return $"Lớp đang chọn: {l.Name}  -  {l.Power}%, F{l.Speed}, {l.Passes} lần. Nháy đúp một vật liệu để áp dụng cho lớp này.";
        }

        // Ghi thông số vật liệu vào LỚP đang chọn (tên / màu / "Xuất" của lớp giữ nguyên); đối tượng trong lớp đổi theo
        private void ApplyMaterial(MaterialPreset p)
        {
            if (ReadOnlyView) { MessageBox.Show(this, "Đang ở tab xem. Hãy quay về tab Bản vẽ để áp dụng vật liệu.", "Vật liệu"); return; }
            if (layers.Count == 0) return;
            int idx = Math.Clamp(curLayer, 0, layers.Count - 1);
            PushUndo();
            var l = layers[idx];
            l.Power = Math.Clamp(p.Power, 0, 100); l.Speed = Math.Clamp(p.Speed, 1, 60000); l.Passes = Math.Clamp(p.Passes, 1, 100);
            l.FillMode = Math.Clamp(p.FillMode, 0, 2); l.FillInterval = Math.Clamp(p.FillInterval, 0.02, 5);
            l.FillAngle = p.FillAngle; l.FillCross = p.FillCross; l.FillBidir = p.FillBidir;
            ApplyLayerParamsToItems(idx);
            RefreshLayerUi();
            LayersChanged();
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void OpenMaterialLibrary()
        {
            using var dlg = new MaterialLibraryDialog(materials, DeviceDpi, MaterialFromCurrentLayer, CurrentLayerInfo, ApplyMaterial, SaveMaterials);
            dlg.ShowDialog(this);
        }

        private void SaveLayerAsMaterial()
        {
            using var d = new MaterialEditDialog(MaterialFromCurrentLayer(), DeviceDpi, "Lưu thông số lớp thành vật liệu mới");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var np = d.Result; np.Normalize();
            var dup = materials.FirstOrDefault(x => x.Key == np.Key);
            if (dup != null)
            {
                if (MessageBox.Show(this, $"Đã có \"{dup.Title}\" ({dup.Operation}). Ghi đè bằng thông số mới?", "Vật liệu",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                materials.Remove(dup);
            }
            materials.Add(np);
            SaveMaterials();
        }

        private void RebuildMaterialMenu()
        {
            if (miMaterialRoot == null) return;
            var items = miMaterialRoot.DropDownItems;
            items.Clear();

            var miLib = new ToolStripMenuItem("Thư viện vật liệu...");
            miLib.Click += (s, e) => OpenMaterialLibrary();
            var miSave = new ToolStripMenuItem("Lưu lớp hiện tại thành vật liệu...");
            miSave.Click += (s, e) => SaveLayerAsMaterial();
            items.Add(miLib); items.Add(miSave); items.Add(new ToolStripSeparator());

            string layerName = layers.Count > 0 ? layers[Math.Clamp(curLayer, 0, layers.Count - 1)].Name : "lớp";
            items.Add(new ToolStripMenuItem("Áp dụng cho " + layerName.Replace("&", "&&") + ":") { Enabled = false });

            if (materials.Count == 0)
            {
                items.Add(new ToolStripMenuItem("(chưa có vật liệu nào - hãy lưu từ lớp hiện tại)") { Enabled = false });
                return;
            }
            const int MaxShown = 25;
            var sorted = materials.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.ThicknessMm).ThenBy(x => x.Operation).ToList();
            foreach (var p in sorted.Take(MaxShown))
            {
                var mp = p;
                var mi = new ToolStripMenuItem($"{p.Title} - {p.Operation}   ({p.Summary})".Replace("&", "&&"));
                mi.Click += (s, e) => ApplyMaterial(mp);
                items.Add(mi);
            }
            if (sorted.Count > MaxShown)
            {
                var more = new ToolStripMenuItem($"... còn {sorted.Count - MaxShown} mục nữa (mở thư viện)");
                more.Click += (s, e) => OpenMaterialLibrary();
                items.Add(more);
            }
        }
    }
}

// =====================================================================
// PHẦN: Khổ in, đèn đỏ, cài đặt G-code theo khổ in   (trước đây là file Form1_PrintArea.cs)
// =====================================================================
// =====================================================================
//  Form1_PrintArea.cs  -  KHỔ IN: vùng khắc thực tế, tỉ lệ 1:1, đèn đỏ định vị
//
//  1) ĐỊNH HÌNH KHÔNG GIAN THỰC TẾ
//     Menu "Khổ in > Cài đặt khổ in...": chọn mẫu ống kính F-theta (70/110/150/175/200 mm), bàn cắt
//     (300/400 mm) hoặc nhập Rộng x Cao tùy ý. Canvas vẽ khung vùng khắc từ gốc thước (0,0), làm tối phần
//     ngoài, đánh dấu gốc toạ độ máy, cảnh báo hình vượt ra ngoài (viền đỏ nét đứt + hỏi khi xuất G-code).
//     Vùng khắc này cũng là vùng dùng cho các lệnh "Căn giữa / Căn chỉnh" ở menu Sắp xếp.
//
//  2) TỈ LỆ BẢN VẼ 1:1
//     - Mọi kích thước trên canvas tính bằng mm theo MmPerPx và G-code ra đúng mm đó (không méo, không phóng).
//     - "Kích thước thật 1:1" (Ctrl+1): zoom sao cho 1 mm trên màn hình = 1 mm thật khi đặt thước lên màn hình.
//     - "Hiệu chuẩn tỉ lệ màn hình": đo đoạn 100 mm bằng thước thật, nhập số đo được, phần mềm tự bù sai lệch.
//     - "Thu phóng vừa vùng khắc" (Ctrl+0).
//     - Chọn gốc toạ độ G-code: góc trái-dưới (GRBL) hoặc tâm (galvo EzCad/BSL/Ruida) và xuất theo vị trí
//       thật trên vùng khắc, để vị trí trên canvas = vị trí trên bàn máy.
//
//  3) ĐÈN ĐỎ ĐỊNH VỊ (RED LIGHT)
//     - "Hiện khung đèn đỏ" (F4): khung đỏ nét đứt chạy quanh biên ngoài của sản phẩm (phần SẼ khắc), kèm kích thước
//       và toạ độ máy. Khung đỏ đặc + cảnh báo nếu vượt vùng khắc.
//     - "Xuất G-code khung đèn đỏ...": file G-code chạy viền khung bằng laser công suất RẤT THẤP, hoặc dùng
//       lệnh bật/tắt đèn đỏ riêng của bo (ô "Lệnh bật/tắt"). Công suất 0 = chạy đầu laser không bắn.
//       Sau này khi có kết nối cổng COM: gán machineSend để gửi thẳng thay vì lưu file.
//
//  Gọi InitPrintArea() 1 lần ở CUỐI InitWorkspaceLayout() (Form1_Tools.cs), sau InitBarcodeAndArray().
//  Cần GCodeCompiler.cs (bản đã đổi partial) và GCodeFrame.cs.
// =====================================================================
namespace test01_1014
{
    using System.Drawing.Drawing2D;
    using System.Text;
    using System.Text.Json;
    public enum PrintOrigin { BottomLeft, Center }

    public class PrintAreaCfg
    {
        public float WidthMm { get; set; } = 400f;
        public float HeightMm { get; set; } = 400f;
        public PrintOrigin Origin { get; set; } = PrintOrigin.BottomLeft;
        public bool AbsolutePositions { get; set; } = true;     // xuất G-code theo vị trí thật trên vùng khắc
        public bool ShowFrame { get; set; } = true;
        public bool DimOutside { get; set; } = true;
        public bool WarnOutside { get; set; } = true;
        public int EngraveOrder { get; set; } = 0;              // 0 = tự tối ưu, 1 = theo tầng, 2 = theo lớp
        public float ScreenCalib { get; set; } = 1f;            // hệ số hiệu chuẩn màn hình

        // Đèn đỏ
        public bool ShowRedLight { get; set; } = false;
        public float RedMarginMm { get; set; } = 0f;
        public float RedPowerPercent { get; set; } = 1f;
        public int RedFeed { get; set; } = 3000;
        public int RedLoops { get; set; } = 1;
        public bool RedCross { get; set; } = false;
        public string RedOnCmd { get; set; } = "";
        public string RedOffCmd { get; set; } = "";
    }

    // ------------------------------------------------------------------
    //  Hộp thoại Cài đặt khổ in
    // ------------------------------------------------------------------
    public class PrintAreaDialog : Form
    {
        private static readonly (string Label, float W, float H)[] Presets =
        {
            ("70 × 70 mm  (lens F-theta)", 70, 70),
            ("110 × 110 mm  (lens F-theta)", 110, 110),
            ("150 × 150 mm  (lens F-theta)", 150, 150),
            ("175 × 175 mm  (lens F-theta)", 175, 175),
            ("200 × 200 mm  (lens F-theta)", 200, 200),
            ("300 × 300 mm", 300, 300),
            ("400 × 400 mm  (bàn cắt)", 400, 400),
        };

        public PrintAreaCfg Result { get; private set; }

        private readonly PrintAreaCfg cfg;
        private readonly int dpi;
        private bool updating;

        private ComboBox cmbPreset = null!;
        private NumericUpDown numW = null!, numH = null!;
        private CheckBox chkAbs = null!, chkFrame = null!, chkDim = null!, chkWarn = null!, chkRedShow = null!, chkCross = null!;
        private RadioButton rbBL = null!, rbCenter = null!;
        private NumericUpDown numMargin = null!, numPower = null!, numFeed = null!, numLoops = null!;
        private TextBox txtOn = null!, txtOff = null!;

        private int Sc(int v) => (int)Math.Round(v * dpi / 96f);

        public PrintAreaDialog(PrintAreaCfg current, int dpi)
        {
            this.dpi = dpi;
            cfg = JsonSerializer.Deserialize<PrintAreaCfg>(JsonSerializer.Serialize(current)) ?? new PrintAreaCfg();
            Result = current;

            Text = "Cài đặt khổ in";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            ClientSize = new Size(Sc(500), Sc(640));
            Build();
            Load2();
        }

        private Label Lbl(Control p, string text, int x, int y, int w, int h = 20, bool note = false)
        {
            var l = new Label { Text = text, Left = Sc(x), Top = Sc(y), Width = Sc(w), Height = Sc(h) };
            if (note) { l.ForeColor = Color.DimGray; l.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); }
            p.Controls.Add(l);
            return l;
        }

        private NumericUpDown Num(Control p, int x, int y, int w, decimal min, decimal max, int dec, decimal inc)
        {
            var n = new NumericUpDown
            {
                Left = Sc(x),
                Top = Sc(y),
                Width = Sc(w),
                Minimum = min,
                Maximum = max,
                DecimalPlaces = dec,
                Increment = inc
            };
            p.Controls.Add(n);
            return n;
        }

        private CheckBox Chk(Control p, string text, int x, int y, int w)
        {
            var c = new CheckBox { Text = text, Left = Sc(x), Top = Sc(y), Width = Sc(w), Height = Sc(22) };
            p.Controls.Add(c);
            return c;
        }

        private void Build()
        {
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Sc(52) };
            Controls.Add(body);
            Controls.Add(bar);

            GroupBox Group(string title, int top, int height)
            {
                var g = new GroupBox { Text = title, Left = Sc(12), Top = Sc(top), Width = Sc(458), Height = Sc(height) };
                body.Controls.Add(g);
                return g;
            }

            // ---- 1. Khổ in ----
            var ga = Group("1. Khổ in / vùng khắc thực tế", 8, 168);
            Lbl(ga, "Mẫu có sẵn:", 14, 28, 100);
            cmbPreset = new ComboBox { Left = Sc(120), Top = Sc(25), Width = Sc(318), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var p in Presets) cmbPreset.Items.Add(p.Label);
            cmbPreset.Items.Add("Tùy chỉnh");
            ga.Controls.Add(cmbPreset);
            Lbl(ga, "Rộng (mm):", 14, 62, 100);
            numW = Num(ga, 120, 59, 120, 10, 5000, 1, 5);
            Lbl(ga, "Cao (mm):", 14, 94, 100);
            numH = Num(ga, 120, 91, 120, 10, 5000, 1, 5);
            Lbl(ga, "Lens F-theta: nhập đúng trường quét ghi trên ống kính. Bàn cắt: phần máy di chuyển được.",
                14, 122, 430, 36, true);
            cmbPreset.SelectedIndexChanged += (s, e) =>
            {
                if (updating || cmbPreset.SelectedIndex < 0 || cmbPreset.SelectedIndex >= Presets.Length) return;
                updating = true;
                numW.Value = (decimal)Presets[cmbPreset.SelectedIndex].W;
                numH.Value = (decimal)Presets[cmbPreset.SelectedIndex].H;
                updating = false;
            };
            EventHandler sizeChanged = (s, e) => { if (!updating) SyncPresetFromSize(); };
            numW.ValueChanged += sizeChanged;
            numH.ValueChanged += sizeChanged;

            // ---- 2. Gốc toạ độ ----
            var gb = Group("2. Gốc toạ độ & xuất G-code", 184, 142);
            chkAbs = Chk(gb, "Xuất G-code theo vị trí thật trên vùng khắc", 14, 24, 430);
            Lbl(gb, "Tắt = gốc (0,0) là góc dưới-trái của cả bản vẽ (đặt gốc máy ngay tại vật liệu).", 30, 46, 410, 32, true);
            rbBL = new RadioButton { Text = "Gốc ở góc trái-dưới  (GRBL, LightBurn)", Left = Sc(30), Top = Sc(82), Width = Sc(400), Height = Sc(22) };
            rbCenter = new RadioButton { Text = "Gốc ở tâm vùng khắc  (galvo EzCad / BSL / Ruida)", Left = Sc(30), Top = Sc(106), Width = Sc(400), Height = Sc(22) };
            gb.Controls.Add(rbBL); gb.Controls.Add(rbCenter);
            chkAbs.CheckedChanged += (s, e) => { rbBL.Enabled = rbCenter.Enabled = chkAbs.Checked; };

            // ---- 3. Hiển thị ----
            var gc = Group("3. Hiển thị trên canvas", 334, 104);
            chkFrame = Chk(gc, "Hiện khung vùng khắc + dấu gốc toạ độ", 14, 22, 430);
            chkDim = Chk(gc, "Làm tối phần nằm ngoài vùng khắc", 14, 46, 430);
            chkWarn = Chk(gc, "Cảnh báo khi hình vượt vùng khắc (hỏi lại khi xuất G-code)", 14, 70, 436);

            // ---- 4. Đèn đỏ ----
            var gd = Group("4. Đèn đỏ định vị (Red Light)", 446, 262);
            chkRedShow = Chk(gd, "Hiện khung đèn đỏ (biên ngoài sản phẩm) trên canvas   [F4]", 14, 22, 436);
            Lbl(gd, "Lề quanh sản phẩm (mm):", 14, 54, 190);
            numMargin = Num(gd, 220, 51, 100, 0, 100, 1, 0.5m);
            Lbl(gd, "Công suất đèn đỏ (%):", 14, 84, 190);
            numPower = Num(gd, 220, 81, 100, 0, 100, 1, 0.5m);
            Lbl(gd, "0 = chỉ chạy đầu laser, không bắn", 328, 80, 125, 40, true);
            Lbl(gd, "Tốc độ chạy khung (mm/phút):", 14, 114, 206);
            numFeed = Num(gd, 220, 111, 100, 100, 60000, 0, 500);
            Lbl(gd, "Số vòng chạy khung:", 14, 144, 190);
            numLoops = Num(gd, 220, 141, 100, 1, 20, 0, 1);
            chkCross = Chk(gd, "Thêm chữ thập ở tâm khung", 14, 170, 430);
            Lbl(gd, "Lệnh bật đèn đỏ riêng:", 14, 200, 190);
            txtOn = new TextBox { Left = Sc(220), Top = Sc(197), Width = Sc(220) };
            Lbl(gd, "Lệnh tắt đèn đỏ riêng:", 14, 228, 190);
            txtOff = new TextBox { Left = Sc(220), Top = Sc(225), Width = Sc(220) };
            gd.Controls.Add(txtOn); gd.Controls.Add(txtOff);
            // chú thích đặt ngoài nhóm cho khỏi chật
            var hint = new Label
            {
                Text = "Để trống 2 ô lệnh = dùng laser công suất thấp. Công suất 1% vẫn có thể để lại vết trên một số vật liệu: " +
                       "thử trên phế liệu hoặc đặt 0.",
                Left = Sc(14),
                Top = Sc(714),
                Width = Sc(440),
                Height = Sc(50),
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            body.Controls.Add(hint);

            // ---- nút ----
            var ok = new Button { Text = "OK", Left = Sc(290), Top = Sc(10), Width = Sc(90), Height = Sc(30), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Hủy", Left = Sc(390), Top = Sc(10), Width = Sc(90), Height = Sc(30), DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Collect();
            bar.Controls.Add(ok); bar.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        private void SyncPresetFromSize()
        {
            int idx = Presets.Length;     // "Tùy chỉnh"
            for (int i = 0; i < Presets.Length; i++)
                if (Math.Abs((float)numW.Value - Presets[i].W) < 0.01f && Math.Abs((float)numH.Value - Presets[i].H) < 0.01f) { idx = i; break; }
            updating = true;
            cmbPreset.SelectedIndex = idx;
            updating = false;
        }

        private void Load2()
        {
            updating = true;
            numW.Value = Math.Min(numW.Maximum, Math.Max(numW.Minimum, (decimal)cfg.WidthMm));
            numH.Value = Math.Min(numH.Maximum, Math.Max(numH.Minimum, (decimal)cfg.HeightMm));
            updating = false;
            SyncPresetFromSize();

            chkAbs.Checked = cfg.AbsolutePositions;
            rbBL.Checked = cfg.Origin == PrintOrigin.BottomLeft;
            rbCenter.Checked = cfg.Origin == PrintOrigin.Center;
            rbBL.Enabled = rbCenter.Enabled = cfg.AbsolutePositions;

            chkFrame.Checked = cfg.ShowFrame; chkDim.Checked = cfg.DimOutside; chkWarn.Checked = cfg.WarnOutside;

            chkRedShow.Checked = cfg.ShowRedLight;
            numMargin.Value = Math.Min(numMargin.Maximum, Math.Max(numMargin.Minimum, (decimal)cfg.RedMarginMm));
            numPower.Value = Math.Min(numPower.Maximum, Math.Max(numPower.Minimum, (decimal)cfg.RedPowerPercent));
            numFeed.Value = Math.Min(numFeed.Maximum, Math.Max(numFeed.Minimum, cfg.RedFeed));
            numLoops.Value = Math.Min(numLoops.Maximum, Math.Max(numLoops.Minimum, cfg.RedLoops));
            chkCross.Checked = cfg.RedCross;
            txtOn.Text = cfg.RedOnCmd; txtOff.Text = cfg.RedOffCmd;
        }

        private void Collect()
        {
            cfg.WidthMm = (float)numW.Value; cfg.HeightMm = (float)numH.Value;
            cfg.AbsolutePositions = chkAbs.Checked;
            cfg.Origin = rbCenter.Checked ? PrintOrigin.Center : PrintOrigin.BottomLeft;
            cfg.ShowFrame = chkFrame.Checked; cfg.DimOutside = chkDim.Checked; cfg.WarnOutside = chkWarn.Checked;
            cfg.ShowRedLight = chkRedShow.Checked;
            cfg.RedMarginMm = (float)numMargin.Value;
            cfg.RedPowerPercent = (float)numPower.Value;
            cfg.RedFeed = (int)numFeed.Value;
            cfg.RedLoops = (int)numLoops.Value;
            cfg.RedCross = chkCross.Checked;
            cfg.RedOnCmd = txtOn.Text.Trim(); cfg.RedOffCmd = txtOff.Text.Trim();
            Result = cfg;
        }
    }

    // ------------------------------------------------------------------
    //  Gắn vào Form1
    // ------------------------------------------------------------------
    public partial class Form1
    {
        private PrintAreaCfg printCfg = new();
        private bool printInited, paMenuSync;
        private ToolStripMenuItem miPaShow = null!, miPaRed = null!;
        private ToolStripStatusLabel? stArea;
        private System.Windows.Forms.Timer? redTimer;
        private float redPhase;
        private RectangleF? redRectPx;
        private int redSig = int.MinValue;

        // Sau này (kết nối cổng COM / GRBL): gán hàm gửi G-code vào đây; trả về true nếu đã gửi.
        private Func<string, bool>? machineSend;

        private static string PrintCfgPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "printarea.json");

        private RectangleF AreaPx => new RectangleF(0, 0, printCfg.WidthMm / MmPerPx, printCfg.HeightMm / MmPerPx);

        // Vùng khắc dùng cho menu Sắp xếp / Căn chỉnh (xem Form1_Arrange.cs)
        private SizeF PrintAreaSizeMm => new SizeF(printCfg.WidthMm, printCfg.HeightMm);

        // =================================================================
        //  Khởi tạo
        // =================================================================
        private void InitPrintArea()
        {
            if (printInited) return;
            printInited = true;
            LoadPrintCfg();

            var root = new ToolStripMenuItem("Khổ in");
            var miSet = new ToolStripMenuItem("Cài đặt khổ in...");
            miSet.Click += (s, e) => OpenPrintAreaDialog();

            miPaShow = new ToolStripMenuItem("Hiện vùng khắc trên canvas") { CheckOnClick = true, Checked = printCfg.ShowFrame };
            miPaShow.CheckedChanged += (s, e) =>
            {
                if (paMenuSync) return;
                printCfg.ShowFrame = miPaShow.Checked; SavePrintCfg(); pictureBox1.Invalidate();
            };

            var miFit = new ToolStripMenuItem("Thu phóng vừa vùng khắc") { ShortcutKeys = Keys.Control | Keys.D0 };
            miFit.Click += (s, e) => ZoomFitArea();
            var miActual = new ToolStripMenuItem("Kích thước thật 1:1") { ShortcutKeys = Keys.Control | Keys.D1 };
            miActual.Click += (s, e) => ZoomActualSize();
            var miCal = new ToolStripMenuItem("Hiệu chuẩn tỉ lệ màn hình...");
            miCal.Click += (s, e) => CalibrateScreen();

            miPaRed = new ToolStripMenuItem("Hiện khung đèn đỏ (biên sản phẩm)") { CheckOnClick = true, Checked = printCfg.ShowRedLight, ShortcutKeys = Keys.F4 };
            miPaRed.CheckedChanged += (s, e) =>
            {
                if (paMenuSync) return;
                printCfg.ShowRedLight = miPaRed.Checked; SavePrintCfg(); RedLightChanged();
            };
            var miRedExp = new ToolStripMenuItem("Xuất G-code khung đèn đỏ...");
            miRedExp.Click += (s, e) => ExportRedLight();

            root.DropDownItems.AddRange(new ToolStripItem[]
            {
                miSet, miPaShow, new ToolStripSeparator(),
                miFit, miActual, miCal, new ToolStripSeparator(),
                miPaRed, miRedExp
            });

            int idx = arrangeRoot != null ? menuStrip1.Items.IndexOf(arrangeRoot) : -1;
            if (idx < 0) idx = menuStrip1.Items.IndexOf(viewToolStripMenuItem);
            menuStrip1.Items.Insert(idx >= 0 ? idx + 1 : menuStrip1.Items.Count, root);

            // Ô "Khổ in" trên thanh trạng thái do InitStatusBar (Form1_Status.cs) tạo; ở đây chỉ cập nhật chữ
            UpdateAreaStatus();

            redTimer = new System.Windows.Forms.Timer { Interval = 110 };
            redTimer.Tick += (s, e) =>
            {
                if (!printCfg.ShowRedLight || ReadOnlyView) return;
                redPhase = (redPhase + 0.6f) % 7f;                    // chạy nét đứt như đèn đỏ đang quét
                var r = RedRect();
                if (r != null) pictureBox1.Invalidate(RedScreenRect(r.Value));
            };
            redTimer.Start();

            pictureBox1.OverlayPaint += OnPrintAreaPaint;

            InitMachine();                                  // menu "Máy" + điều khiển cổng COM (Form1_Machine.cs)
            InitObjectList();                               // tab "Đối tượng" ở cột phải (Form1_ObjectList.cs)
        }

        private void UpdateAreaStatus()
        {
            if (stArea != null)
                stArea.Text = $"Khổ in: {printCfg.WidthMm:0.##} × {printCfg.HeightMm:0.##} mm";
        }

        private void SyncPrintMenus()
        {
            paMenuSync = true;
            miPaShow.Checked = printCfg.ShowFrame;
            miPaRed.Checked = printCfg.ShowRedLight;
            paMenuSync = false;
        }

        private void RedLightChanged()
        {
            redSig = int.MinValue;
            pictureBox1.Invalidate();
        }

        private void OpenPrintAreaDialog()
        {
            using var dlg = new PrintAreaDialog(printCfg, DeviceDpi);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            bool sizeChanged = Math.Abs(dlg.Result.WidthMm - printCfg.WidthMm) > 0.001f ||
                               Math.Abs(dlg.Result.HeightMm - printCfg.HeightMm) > 0.001f;
            dlg.Result.ScreenCalib = printCfg.ScreenCalib; dlg.Result.EngraveOrder = printCfg.EngraveOrder;      // hiệu chuẩn có hộp thoại riêng
            printCfg = dlg.Result;
            SavePrintCfg();
            SyncPrintMenus();
            UpdateAreaStatus();
            redSig = int.MinValue;
            if (sizeChanged) ZoomFitArea(); else pictureBox1.Invalidate();
        }

        private void LoadPrintCfg()
        {
            try
            {
                if (!File.Exists(PrintCfgPath)) return;
                var c = JsonSerializer.Deserialize<PrintAreaCfg>(File.ReadAllText(PrintCfgPath));
                if (c == null) return;
                c.WidthMm = Math.Clamp(c.WidthMm, 10f, 5000f);
                c.HeightMm = Math.Clamp(c.HeightMm, 10f, 5000f);
                c.ScreenCalib = Math.Clamp(c.ScreenCalib, 0.3f, 3f);
                printCfg = c;
                gcodeCfg.OrderMode = (EngraveOrderMode)Math.Clamp(c.EngraveOrder, 0, 2);
            }
            catch { /* file hỏng thì dùng mặc định */ }
        }

        // Gọi sau khi đóng form mô phỏng: nhớ chế độ thứ tự khắc cho lần mở sau
        private void SaveEngraveOrder()
        {
            if (printCfg.EngraveOrder == (int)gcodeCfg.OrderMode) return;
            printCfg.EngraveOrder = (int)gcodeCfg.OrderMode;
            SavePrintCfg();
        }

        private void SavePrintCfg()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrintCfgPath)!);
                File.WriteAllText(PrintCfgPath, JsonSerializer.Serialize(printCfg));
            }
            catch { }
        }

        // =================================================================
        //  Thu phóng: vừa vùng khắc / kích thước thật 1:1 / hiệu chuẩn
        // =================================================================
        // Pixel màn hình trên 1 mm THẬT (DPI Windows báo, nhân hệ số hiệu chuẩn người dùng)
        private float PxPerMmReal() => DeviceDpi / 25.4f * printCfg.ScreenCalib;

        private void ZoomFitArea()
        {
            if (ReadOnlyView) SetActiveTab(null);
            var a = AreaPx;
            float availW = Math.Max(50, pictureBox1.Width - Inset - 40);
            float availH = Math.Max(50, pictureBox1.Height - Inset - 40);
            zoom = Math.Clamp(Math.Min(availW / a.Width, availH / a.Height), 0.1f, 20f);
            pan = new PointF(Inset + (pictureBox1.Width - Inset - a.Width * zoom) / 2f,
                             Inset + (pictureBox1.Height - Inset - a.Height * zoom) / 2f);
            pictureBox1.Invalidate();
        }

        private void ZoomActualSize()
        {
            // 1 mm thật = PxPerMmReal() px màn hình; canvas có zoom/MmPerPx px màn hình trên 1 mm
            float nz = Math.Clamp(PxPerMmReal() * MmPerPx, 0.1f, 20f);
            var c = new Point(pictureBox1.Width / 2, pictureBox1.Height / 2);
            var w = ScreenToWorld(c);                  // giữ nguyên điểm đang ở giữa màn hình
            zoom = nz;
            pan = new PointF(c.X - w.X * zoom, c.Y - w.Y * zoom);
            pictureBox1.Invalidate();
        }

        private void CalibrateScreen()
        {
            float a = PxPerMmReal();
            int lenPx = (int)Math.Round(100f * a);
            using var f = new Form
            {
                Text = "Hiệu chuẩn tỉ lệ màn hình",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(Math.Max(Scale96(470), lenPx + Scale96(70)), Scale96(268)),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            var info = new Label
            {
                Text = "Đặt thước thật lên màn hình, đo đoạn đen bên dưới (đang là 100 mm theo cài đặt hiện tại) " +
                       "rồi nhập số đo được. Phần mềm sẽ bù lại để \"Kích thước thật 1:1\" khớp thước.",
                Bounds = new Rectangle(Scale96(14), Scale96(10), f.ClientSize.Width - Scale96(28), Scale96(56))
            };
            var ruler = new Panel
            {
                Bounds = new Rectangle(Scale96(14), Scale96(72), f.ClientSize.Width - Scale96(28), Scale96(56)),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            ruler.Paint += (s, e) =>
            {
                var g = e.Graphics;
                int x0 = Scale96(16), y = ruler.Height / 2;
                using var p = new Pen(Color.Black, 2f);
                g.DrawLine(p, x0, y, x0 + lenPx, y);
                for (int i = 0; i <= 10; i++)                          // vạch mỗi 10 mm
                {
                    int x = x0 + (int)Math.Round(i * 10 * a);
                    int h = (i == 0 || i == 10) ? Scale96(12) : Scale96(6);
                    g.DrawLine(p, x, y - h, x, y + h);
                }
                using var fnt = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                g.DrawString("100 mm", fnt, Brushes.DimGray, x0 + lenPx / 2 - Scale96(22), y + Scale96(10));
            };
            var lbl = new Label { Text = "Đo được (mm):", Bounds = new Rectangle(Scale96(14), Scale96(146), Scale96(110), Scale96(24)) };
            var num = new NumericUpDown
            {
                DecimalPlaces = 1,
                Minimum = 20,
                Maximum = 500,
                Value = 100,
                Increment = 0.5m,
                Bounds = new Rectangle(Scale96(130), Scale96(143), Scale96(100), Scale96(26))
            };
            var cur = new Label
            {
                Text = $"Hệ số hiện tại: {printCfg.ScreenCalib:0.###}",
                Bounds = new Rectangle(Scale96(250), Scale96(146), Scale96(200), Scale96(24)),
                ForeColor = Color.DimGray
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(f.ClientSize.Width - Scale96(190), Scale96(222), Scale96(80), Scale96(30)) };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(f.ClientSize.Width - Scale96(100), Scale96(222), Scale96(80), Scale96(30)) };
            var reset = new Button { Text = "Đặt lại mặc định", Bounds = new Rectangle(Scale96(14), Scale96(222), Scale96(140), Scale96(30)) };
            reset.Click += (s, e) => { printCfg.ScreenCalib = 1f; SavePrintCfg(); f.DialogResult = DialogResult.Cancel; f.Close(); pictureBox1.Invalidate(); };
            f.Controls.AddRange(new Control[] { info, ruler, lbl, num, cur, ok, cancel, reset });
            f.AcceptButton = ok; f.CancelButton = cancel;

            if (f.ShowDialog(this) != DialogResult.OK) return;
            printCfg.ScreenCalib = Math.Clamp(printCfg.ScreenCalib * 100f / (float)num.Value, 0.3f, 3f);
            SavePrintCfg();
            pictureBox1.Invalidate();
        }

        // =================================================================
        //  Cài đặt G-code theo khổ in + kiểm tra vượt vùng
        // =================================================================
        // Dùng chung gcodeCfg (Form1_AutoSave.cs) để S tối đa, bước quét... được nhớ giữa các lần mở
        private GCodeSettings BuildGCodeSettings()
        {
            var c = gcodeCfg;
            c.MmPerPixel = MmPerPx;                              // khóa tỉ lệ canvas -> mm: KHÔNG đổi ở nơi khác
            float wPx = printCfg.WidthMm / MmPerPx, hPx = printCfg.HeightMm / MmPerPx;
            c.OriginAtJobCorner = !printCfg.AbsolutePositions;
            c.CanvasHeightPx = hPx;
            if (printCfg.Origin == PrintOrigin.Center) { c.AbsOriginXpx = wPx / 2f; c.AbsOriginYpx = hPx / 2f; }
            else { c.AbsOriginXpx = 0f; c.AbsOriginYpx = hPx; }
            c.LayerMeta.Clear();
            for (int i = 0; i < layers.Count; i++)
                c.LayerMeta[i] = (layers[i].Name, layers[i].Color);
            return c;
        }

        private FrameOptions BuildFrameOptions() => new FrameOptions
        {
            MarginMm = printCfg.RedMarginMm,
            PowerPercent = printCfg.RedPowerPercent,
            Feed = printCfg.RedFeed,
            Loops = printCfg.RedLoops,
            Cross = printCfg.RedCross,
            OnCmd = printCfg.RedOnCmd,
            OffCmd = printCfg.RedOffCmd
        };

        private bool RectOutsideArea(RectangleF b)
        {
            var a = AreaPx;
            float eps = 0.01f / MmPerPx;                          // dung sai 0.01 mm
            return b.Left < a.Left - eps || b.Top < a.Top - eps || b.Right > a.Right + eps || b.Bottom > a.Bottom + eps;
        }

        private List<Vectoritem> ItemsOutsideArea()
        {
            var res = new List<Vectoritem>();
            foreach (var it in ExportItems())              // chỉ tính các lớp bật "Xuất"
                if (RectOutsideArea(ItemBounds(it))) res.Add(it);
            return res;
        }

        // Gọi trước khi mở form xuất G-code. true = tiếp tục
        private bool ConfirmInsideArea()
        {
            if (!printCfg.WarnOutside) return true;
            var o = ItemsOutsideArea();
            if (o.Count == 0) return true;
            var rs = MessageBox.Show(this,
                $"Có {o.Count} đối tượng nằm ngoài vùng khắc {printCfg.WidthMm:0.##} × {printCfg.HeightMm:0.##} mm.\n\n" +
                "Phần nằm ngoài sẽ không khắc được và có thể làm máy chạy quá hành trình.\n\nVẫn tiếp tục xuất G-code?",
                "Vượt vùng khắc", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            return rs == DialogResult.Yes;
        }

        // =================================================================
        //  Đèn đỏ
        // =================================================================
        // Khung bao (px) của phần sẽ khắc + lề; lưu đệm theo nội dung dự án
        private RectangleF? RedRect()
        {
            int sig = HashCode.Combine(ComputeContentSignature(), printCfg.RedMarginMm, gcodeCfg.MaxS);
            if (sig != redSig)
            {
                redSig = sig;
                redRectPx = null;
                try
                {
                    var bb = new GCodeCompiler(BuildGCodeSettings()).GetBoundsPx(ExportItems());
                    if (bb is RectangleF r)
                    {
                        float m = printCfg.RedMarginMm / MmPerPx;
                        r.Inflate(m, m);
                        redRectPx = r;
                    }
                }
                catch { redRectPx = null; }
            }
            return redRectPx;
        }

        private Rectangle RedScreenRect(RectangleF r)
        {
            var sr = new RectangleF(pan.X + r.X * zoom, pan.Y + r.Y * zoom, r.Width * zoom, r.Height * zoom);
            return Rectangle.Inflate(Rectangle.Ceiling(sr), 14, 14);
        }

        // Toạ độ máy (mm) của 1 điểm canvas, theo gốc đã chọn; chỉ có nghĩa khi xuất theo vị trí thật
        private PointF PxToMachineMm(PointF p)
        {
            float k = MmPerPx;
            var a = AreaPx;
            return printCfg.Origin == PrintOrigin.Center
                ? new PointF((p.X - a.Width / 2f) * k, (a.Height / 2f - p.Y) * k)
                : new PointF(p.X * k, (a.Height - p.Y) * k);
        }

        private void ExportRedLight()
        {
            if (projectItems.Count == 0) { MessageBox.Show(this, "Canvas đang trống.", "Đèn đỏ"); return; }
            try
            {
                var comp = new GCodeCompiler(machine?.IsOpen == true ? BuildMachineSettings() : BuildGCodeSettings());   // đã nối máy: toạ độ theo "Bắt đầu từ"
                GCodeResult res = comp.CompileFrame(ExportItems(), BuildFrameOptions());

                var rr = RedRect();
                if (printCfg.WarnOutside && rr is RectangleF r0 && RectOutsideArea(r0) &&
                    MessageBox.Show(this, "Khung đèn đỏ vượt ra ngoài vùng khắc. Vẫn xuất?", "Vượt vùng khắc",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                if (machineSend != null && machineSend(res.Code))      // đã kết nối máy: gửi thẳng
                    return;

                using var sfd = new SaveFileDialog
                {
                    Filter = "G-code (*.nc)|*.nc|G-code (*.gcode)|*.gcode|Text (*.txt)|*.txt",
                    FileName = "redlight_frame.nc",
                    Title = "Lưu G-code khung đèn đỏ"
                };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(sfd.FileName, res.Code, new UTF8Encoding(false));

                string mode = !string.IsNullOrWhiteSpace(printCfg.RedOnCmd) ? "lệnh đèn đỏ riêng"
                            : printCfg.RedPowerPercent > 0 ? $"laser công suất thấp {printCfg.RedPowerPercent:0.#}%"
                            : "không bắn laser";
                MessageBox.Show(this,
                    $"Đã lưu khung {res.WidthMm:0.##} × {res.HeightMm:0.##} mm ({printCfg.RedLoops} vòng, {mode}).\n" +
                    $"Ước tính {res.EstimatedMinutes * 60:0} giây.",
                    "Khung đèn đỏ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không tạo được khung đèn đỏ:\n" + ex.Message, "Đèn đỏ",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // =================================================================
        //  Vẽ lớp phủ: vùng khắc + đèn đỏ
        // =================================================================
        private void OnPrintAreaPaint(object? sender, PaintEventArgs e)
        {
            if (ReadOnlyView || (!printCfg.ShowFrame && !printCfg.ShowRedLight)) return;
            var g = e.Graphics;
            var area = AreaPx;
            var outside = printCfg.ShowFrame && printCfg.WarnOutside ? ItemsOutsideArea() : new List<Vectoritem>();
            RectangleF? red = printCfg.ShowRedLight ? RedRect() : null;

            var state = g.Save();
            try
            {
                g.SetClip(new Rectangle(Inset, Inset, Math.Max(0, pictureBox1.Width - Inset),
                                        Math.Max(0, pictureBox1.Height - Inset)), CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(pan.X, pan.Y);
                g.ScaleTransform(zoom, zoom);

                if (printCfg.ShowFrame) PaintAreaWorld(g, area, outside);
                if (red is RectangleF rr) PaintRedWorld(g, rr);
            }
            finally { g.Restore(state); }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (printCfg.ShowFrame) PaintAreaLabels(g, area, outside.Count);
            if (red is RectangleF rl) PaintRedLabel(g, rl);
        }

        private void PaintAreaWorld(Graphics g, RectangleF area, List<Vectoritem> outside)
        {
            if (printCfg.DimOutside)       // làm tối phần ngoài vùng khắc
            {
                var tl = ScreenToWorld(Point.Empty);
                var br = ScreenToWorld(new Point(pictureBox1.Width, pictureBox1.Height));
                var st = g.Save();
                using (var hole = new Region(area)) g.ExcludeClip(hole);   // Graphics.ExcludeClip không nhận RectangleF
                using var dim = new SolidBrush(Color.FromArgb(70, 60, 60, 70));
                g.FillRectangle(dim, tl.X - 2, tl.Y - 2, br.X - tl.X + 4, br.Y - tl.Y + 4);
                g.Restore(st);
            }

            using (var pen = new Pen(Color.FromArgb(210, 0, 90, 170), 2f / zoom))
                g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);

            // Dấu gốc toạ độ máy
            float len = 16f / zoom;
            using var cap = new AdjustableArrowCap(3, 4);
            using var op = new Pen(Color.FromArgb(220, 200, 40, 40), 2f / zoom);
            if (printCfg.Origin == PrintOrigin.Center)
            {
                float cx = area.X + area.Width / 2f, cy = area.Y + area.Height / 2f;
                g.DrawLine(op, cx - len, cy, cx + len, cy);
                g.DrawLine(op, cx, cy - len, cx, cy + len);
                g.DrawEllipse(op, cx - len / 3f, cy - len / 3f, 2f * len / 3f, 2f * len / 3f);
            }
            else
            {
                op.CustomEndCap = cap;
                g.DrawLine(op, area.Left, area.Bottom, area.Left + len * 1.8f, area.Bottom);     // +X
                g.DrawLine(op, area.Left, area.Bottom, area.Left, area.Bottom - len * 1.8f);     // +Y (hướng lên)
            }

            // Hình vượt vùng: khung đỏ nét đứt
            if (outside.Count > 0)
            {
                using var wp = new Pen(Color.FromArgb(230, 220, 30, 30), 1.5f / zoom) { DashStyle = DashStyle.Dash };
                foreach (var it in outside)
                {
                    var b = ItemBounds(it);
                    g.DrawRectangle(wp, b.X, b.Y, b.Width, b.Height);
                }
            }
        }

        private void PaintAreaLabels(Graphics g, RectangleF area, int outsideCount)
        {
            float sx = pan.X + area.Left * zoom, sy = pan.Y + area.Top * zoom;
            bool actual = Math.Abs(zoom - PxPerMmReal() * MmPerPx) < 0.004f * zoom;

            string title = $"Vùng khắc {printCfg.WidthMm:0.##} × {printCfg.HeightMm:0.##} mm" + (actual ? "   ● 1:1" : "");
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                var sz = g.MeasureString(title, f);
                float ty = sy - sz.Height - 4;
                if (ty < Inset + 2) ty = sy + 4;                              // hết chỗ phía trên thì đặt vào trong
                float tx = Math.Max(Inset + 4, sx + 2);
                using var bg = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
                g.FillRectangle(bg, tx - 2, ty - 1, sz.Width + 6, sz.Height + 2);
                g.DrawString(title, f, actual ? Brushes.DarkGreen : Brushes.RoyalBlue, tx, ty);
            }

            // Nhãn gốc toạ độ
            using (var f = new Font("Segoe UI", 8.5f))
            {
                float ox, oy;
                if (printCfg.Origin == PrintOrigin.Center)
                { ox = pan.X + (area.Left + area.Width / 2f) * zoom + 8; oy = pan.Y + (area.Top + area.Height / 2f) * zoom + 6; }
                else
                { ox = pan.X + area.Left * zoom + 6; oy = pan.Y + area.Bottom * zoom + 4; }
                g.DrawString(printCfg.AbsolutePositions ? "0,0" : "gốc theo bản vẽ", f, Brushes.Firebrick, ox, oy);
            }

            if (outsideCount > 0)
            {
                string w = $"⚠ {outsideCount} đối tượng vượt ra ngoài vùng khắc";
                using var f = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                var sz = g.MeasureString(w, f);
                float x = pictureBox1.Width - sz.Width - 14, y = Inset + 8;
                using var bg = new SolidBrush(Color.FromArgb(235, 255, 235, 235));
                using var bd = new Pen(Color.Firebrick);
                g.FillRectangle(bg, x - 6, y - 3, sz.Width + 12, sz.Height + 6);
                g.DrawRectangle(bd, x - 6, y - 3, sz.Width + 12, sz.Height + 6);
                g.DrawString(w, f, Brushes.Firebrick, x, y);
            }
        }

        private void PaintRedWorld(Graphics g, RectangleF r)
        {
            bool over = RectOutsideArea(r);
            float w = (over ? 2.6f : 1.8f) / zoom;

            using (var tint = new SolidBrush(Color.FromArgb(over ? 26 : 14, 255, 0, 0)))
                g.FillRectangle(tint, r);

            using (var pen = new Pen(Color.FromArgb(240, over ? 255 : 230, 20, 20), w)
            { DashPattern = new float[] { 4f, 3f }, DashOffset = redPhase })
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            // 4 góc đậm (dễ thấy điểm mốc khi nhìn lên máy)
            float L = Math.Min(14f / zoom, Math.Min(r.Width, r.Height) / 3f);
            using var cp = new Pen(Color.FromArgb(255, 220, 0, 0), w * 1.8f);
            g.DrawLines(cp, new[] { new PointF(r.Left, r.Top + L), new PointF(r.Left, r.Top), new PointF(r.Left + L, r.Top) });
            g.DrawLines(cp, new[] { new PointF(r.Right - L, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + L) });
            g.DrawLines(cp, new[] { new PointF(r.Right, r.Bottom - L), new PointF(r.Right, r.Bottom), new PointF(r.Right - L, r.Bottom) });
            g.DrawLines(cp, new[] { new PointF(r.Left + L, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Bottom - L) });
        }

        private void PaintRedLabel(Graphics g, RectangleF r)
        {
            bool over = RectOutsideArea(r);
            string t = $"Đèn đỏ {r.Width * MmPerPx:0.##} × {r.Height * MmPerPx:0.##} mm";
            if (printCfg.AbsolutePositions)
            {
                var a = PxToMachineMm(new PointF(r.Left, r.Top));
                var b = PxToMachineMm(new PointF(r.Right, r.Bottom));
                t += $"   X {Math.Min(a.X, b.X):0.#} → {Math.Max(a.X, b.X):0.#}   Y {Math.Min(a.Y, b.Y):0.#} → {Math.Max(a.Y, b.Y):0.#}";
            }
            if (over) t += "   ⚠ VƯỢT VÙNG KHẮC";

            using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
            var sz = g.MeasureString(t, f);
            float sx = pan.X + r.Left * zoom, sy = pan.Y + r.Bottom * zoom + 6;
            if (sy + sz.Height > pictureBox1.Height - 30) sy = pan.Y + r.Top * zoom - sz.Height - 6;
            sx = Math.Max(Inset + 4, Math.Min(sx, pictureBox1.Width - sz.Width - 8));
            using var bg = new SolidBrush(Color.FromArgb(230, 255, 245, 245));
            g.FillRectangle(bg, sx - 2, sy - 1, sz.Width + 6, sz.Height + 2);
            g.DrawString(t, f, Brushes.Firebrick, sx, sy);
        }
    }
}
