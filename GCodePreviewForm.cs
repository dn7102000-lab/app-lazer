// =====================================================================
//  GCodePreviewForm.cs  -  Xem trước + MÔ PHỎNG + điều chỉnh G-code
//  Project: test01_1014 (.NET 8, WinForms)
//
//  Cách dùng (trong Form1):
//      using var f = new GCodePreviewForm(projectItems,
//                        new GCodeSettings { CanvasHeightPx = pictureBox1.Height });
//      f.ShowDialog(this);
//
//  Chức năng:
//   - Sinh G-code từ projectItems (dùng GCodeCompiler.cs)
//   - Chỉnh công suất / tốc độ / số lần chạy CỦA TỪNG ĐỐI TƯỢNG (ghi thẳng vào Vectoritem)
//   - Chỉnh chung: mm/pixel, S tối đa, bước quét ảnh, đen/trắng + ngưỡng, đảo ảnh
//   - Mô phỏng chạy: Play/Pause, tốc độ x1..x5000, kéo thanh tiến độ, đầu laser chạy,
//     dòng lệnh đang chạy được bôi chọn trong ô code
//   - Zoom (lăn chuột), kéo (giữ chuột), double-click = fit
//   - Lưu file .nc / .gcode
// =====================================================================
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace test01_1014
{
    // ------------------------------------------------------------------
    //  Đoạn thẳng đã parse từ G-code
    // ------------------------------------------------------------------
    public class Seg
    {
        public float X1, Y1, X2, Y2;
        public bool LaserOn;
        public int S;
        public int Feed;    // mm/phút
        public int Line;    // chỉ số dòng trong G-code (từ 0)
        public int Kind;    // 0 = thường / đường tô, 1 = đường VIỀN của "Tô kín + viền" (vẽ màu xanh)
        public int LayerId; // ID của tầng / lớp
        public string LayerName = "Lớp 01";
        public Color LayerColor = Color.Black;
    }

    public static class GCodeParser
    {
        static readonly Regex RxWord = new(@"([A-Z])\s*(-?\d*\.?\d+)", RegexOptions.Compiled);
        static readonly Regex RxParen = new(@"\(.*?\)", RegexOptions.Compiled);

        public static List<Seg> Parse(IEnumerable<string> lines, int rapidFeed)
        {
            var res = new List<Seg>();
            float x = 0, y = 0;
            int s = 0, feed = 1000, mode = 0, ln = -1, tag = 0;
            bool abs = true, laser = false;
            int curLayerId = 0;
            string curLayerName = "Lớp 01";
            Color curLayerColor = Color.Black;

            foreach (var raw in lines)
            {
                ln++;
                string line = raw;
                string tr = raw.TrimStart();
                if (tr.StartsWith("; LAYER_START:"))
                {
                    try
                    {
                        var parts = tr.Substring(14).Split('|');
                        if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out int lid))
                            curLayerId = lid;
                        if (parts.Length >= 2)
                            curLayerName = parts[1].Trim();
                        if (parts.Length >= 3)
                        {
                            string hex = parts[2].Trim();
                            if (hex.StartsWith("#")) hex = hex.Substring(1);
                            if (hex.Length == 6)
                            {
                                int rgb = int.Parse(hex, NumberStyles.HexNumber);
                                curLayerColor = Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                            }
                        }
                    }
                    catch { }
                }
                else if (tr.StartsWith("; outline")) tag = 1;
                else if (tr.StartsWith("; fill") || tr.StartsWith("; [")) tag = 0;
                int sc = line.IndexOf(';');
                if (sc >= 0) line = line.Substring(0, sc);
                line = RxParen.Replace(line.ToUpperInvariant(), "").Trim();
                if (line.Length == 0) continue;

                float nx = x, ny = y;
                bool moved = false;
                foreach (Match m in RxWord.Matches(line))
                {
                    char c = m.Groups[1].Value[0];
                    float v = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    switch (c)
                    {
                        case 'G':
                            if (v == 0) mode = 0;
                            else if (v == 1) mode = 1;
                            else if (v == 90) abs = true;
                            else if (v == 91) abs = false;
                            break;
                        case 'M':
                            if (v == 3 || v == 4) laser = true;
                            else if (v == 5) laser = false;
                            break;
                        case 'S': s = (int)v; break;
                        case 'F': feed = Math.Max(1, (int)v); break;
                        case 'X': nx = abs ? v : x + v; moved = true; break;
                        case 'Y': ny = abs ? v : y + v; moved = true; break;
                    }
                }
                if (moved)
                {
                    res.Add(new Seg
                    {
                        X1 = x, Y1 = y, X2 = nx, Y2 = ny,
                        LaserOn = mode == 1 && laser && s > 0,
                        S = s,
                        Feed = mode == 0 ? Math.Max(1, rapidFeed) : feed,
                        Line = ln,
                        Kind = tag,
                        LayerId = curLayerId,
                        LayerName = curLayerName,
                        LayerColor = curLayerColor
                    });
                    x = nx; y = ny;
                }
            }
            return res;
        }
    }

    // ------------------------------------------------------------------
    //  Canvas vẽ G-code: 2 lớp bitmap (kế hoạch + đã khắc) để mô phỏng mượt
    // ------------------------------------------------------------------
    public class GCodeCanvas : Control
    {
        List<Seg> segs = new();
        HashSet<int> dupSegIdx = new();   // index các đoạn bị trùng (để vẽ highlight đỏ)
        float scale = 5f;
        PointF offset = new(20, 20);
        Point last;
        bool drag;
        int maxS = 1;
        public bool UserAdjustedView { get; set; } = false;

        readonly HashSet<int> hiddenLayers = new();

        Bitmap? planBmp, burnBmp;
        bool dirty = true;
        int drawnUpTo = 0;
        int progIdx = 0;
        float progFrac = 0f;
        bool showHead = false;

        bool showRapid = true, paper = true;
        float beam = 0.1f;

        public bool ShowRapid { get => showRapid; set { showRapid = value; dirty = true; Invalidate(); } }
        public bool PaperMode { get => paper; set { paper = value; dirty = true; Invalidate(); } }
        public float BeamWidth { get => beam; set { beam = value; dirty = true; Invalidate(); } }

        public void SetLayerVisibility(int layerId, bool visible)
        {
            if (visible) hiddenLayers.Remove(layerId);
            else hiddenLayers.Add(layerId);
            dirty = true;
            Invalidate();
        }

        public bool IsLayerVisible(int layerId) => !hiddenLayers.Contains(layerId);

        public void ShowAllLayers()
        {
            hiddenLayers.Clear();
            dirty = true;
            Invalidate();
        }

        public void SoloLayer(int layerId)
        {
            hiddenLayers.Clear();
            foreach (var s in segs)
                if (s.LayerId != layerId) hiddenLayers.Add(s.LayerId);
            dirty = true;
            Invalidate();
        }

        public GCodeCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.Black;
        }

        public void Load(List<Seg> s, bool fit)
        {
            segs = s ?? new List<Seg>();
            hiddenLayers.Clear();
            dupSegIdx.Clear();
            maxS = Math.Max(1, segs.Count == 0 ? 1 : segs.Max(g => g.S));
            progIdx = segs.Count;       // mặc định: hiện kết quả hoàn tất
            progFrac = 0;
            showHead = false;
            dirty = true;
            if (fit) FitView(); else Invalidate();
        }

        public void FitView()
        {
            UserAdjustedView = false;
            if (segs.Count == 0 || Width < 50 || Height < 50) { Invalidate(); return; }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;

            // Ưu tiên tính khung bao trên các đường khắc thật (LaserOn = true) để tỉ lệ hiển thị không bị méo do G0 từ gốc toạ độ
            foreach (var g in segs)
            {
                if (hiddenLayers.Contains(g.LayerId) || !g.LaserOn) continue;
                any = true;
                minX = Math.Min(minX, Math.Min(g.X1, g.X2)); maxX = Math.Max(maxX, Math.Max(g.X1, g.X2));
                minY = Math.Min(minY, Math.Min(g.Y1, g.Y2)); maxY = Math.Max(maxY, Math.Max(g.Y1, g.Y2));
            }
            if (!any)
            {
                foreach (var g in segs)
                {
                    if (hiddenLayers.Contains(g.LayerId)) continue;
                    any = true;
                    minX = Math.Min(minX, Math.Min(g.X1, g.X2)); maxX = Math.Max(maxX, Math.Max(g.X1, g.X2));
                    minY = Math.Min(minY, Math.Min(g.Y1, g.Y2)); maxY = Math.Max(maxY, Math.Max(g.Y1, g.Y2));
                }
            }
            if (!any)
            {
                minX = minY = 0; maxX = maxY = 100;
            }

            float w = Math.Max(0.1f, maxX - minX), h = Math.Max(0.1f, maxY - minY);
            float padX = Math.Max(28f, Width * 0.07f);
            float padY = Math.Max(28f, Height * 0.07f);
            scale = Math.Min((Width - 2 * padX) / w, (Height - 2 * padY) / h);

            // Căn giữa hình ảnh toàn bộ trên khung nhìn
            float cx = (minX + maxX) / 2f;
            float cy = (minY + maxY) / 2f;
            offset = new PointF(Width / 2f - cx * scale, Height / 2f - cy * scale);

            dirty = true;
            Invalidate();
        }

        PointF ToScreen(float x, float y)
        {
            return new PointF(offset.X + x * scale, Height - (offset.Y + y * scale));
        }

        void SetupG(Graphics g)
        {
            g.SmoothingMode = segs.Count > 20000 ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
        }

        public void SetDuplicates(HashSet<int> indices)
        {
            dupSegIdx = indices;
            dirty = true;
            Invalidate();
        }

        Pen NewBurnPen()
        {
            return new Pen(Color.Black, Math.Max(1f, beam * scale))
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        }

        void DrawBurn(Graphics g, Pen pen, Seg s)
        {
            if (!s.LaserOn) return;
            if (hiddenLayers.Contains(s.LayerId)) return;

            Color baseCol = s.LayerColor;
            if (baseCol.IsEmpty || (baseCol.R == 0 && baseCol.G == 0 && baseCol.B == 0))
                baseCol = paper ? Color.FromArgb(25, 25, 25) : Color.FromArgb(220, 220, 220);

            int i = Math.Min(255, s.S * 255 / maxS);
            if (paper)
            {
                float pFact = 0.35f + 0.65f * (i / 255f);
                pen.Color = Color.FromArgb(
                    (int)(baseCol.R * pFact),
                    (int)(baseCol.G * pFact),
                    (int)(baseCol.B * pFact));
            }
            else
            {
                float pFact = 0.5f + 0.5f * (i / 255f);
                pen.Color = Color.FromArgb(
                    Math.Min(255, (int)(baseCol.R * pFact + 30)),
                    Math.Min(255, (int)(baseCol.G * pFact + 30)),
                    Math.Min(255, (int)(baseCol.B * pFact + 30)));
            }

            if (s.Kind == 1) // đường viền
                pen.Width = Math.Max(1.4f, beam * scale * 1.25f);
            else
                pen.Width = Math.Max(1f, beam * scale);

            g.DrawLine(pen, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
        }

        /// <summary>idx = số đoạn đã chạy xong, frac = phần trăm đoạn đang chạy (0..1).</summary>
        public void SetProgress(int idx, float frac, bool head)
        {
            idx = Math.Clamp(idx, 0, segs.Count);
            showHead = head;
            progFrac = frac;

            if (dirty || idx < drawnUpTo)
            {
                progIdx = idx;
                dirty = true;                       // sẽ dựng lại ở lần vẽ tới
            }
            else if (idx > drawnUpTo && burnBmp != null)
            {
                using (var g = Graphics.FromImage(burnBmp))
                using (var pen = NewBurnPen())
                {
                    SetupG(g);
                    for (int i = drawnUpTo; i < idx; i++) DrawBurn(g, pen, segs[i]);
                }
                drawnUpTo = idx;
                progIdx = idx;
            }
            else
            {
                progIdx = idx;
            }
            Invalidate();
        }

        void EnsureLayers()
        {
            if (Width < 2 || Height < 2) return;
            if (planBmp == null || planBmp.Width != Width || planBmp.Height != Height)
            {
                planBmp?.Dispose();
                burnBmp?.Dispose();
                planBmp = new Bitmap(Width, Height);
                burnBmp = new Bitmap(Width, Height);
                dirty = true;
            }
            if (!dirty) return;

            // Lớp kế hoạch: nền + trục + đường dự kiến
            using (var g = Graphics.FromImage(planBmp))
            {
                g.Clear(paper ? Color.FromArgb(248, 246, 240) : Color.FromArgb(20, 20, 24));
                SetupG(g);

                var o = ToScreen(0, 0);
                using (var ax = new Pen(paper ? Color.FromArgb(180, 180, 220) : Color.FromArgb(50, 50, 90)))
                {
                    g.DrawLine(ax, o.X, 0, o.X, Height);
                    g.DrawLine(ax, 0, o.Y, Width, o.Y);
                }

                bool drawPlanned = progIdx < segs.Count;    // đang mô phỏng -> hiện đường dự kiến mờ
                using (var rapid = new Pen(paper ? Color.FromArgb(195, 195, 215) : Color.FromArgb(65, 65, 75)) { DashStyle = DashStyle.Dot })
                {
                    var planPens = new Dictionary<int, Pen>();

                    foreach (var s in segs)
                    {
                        if (hiddenLayers.Contains(s.LayerId)) continue;

                        if (s.LaserOn)
                        {
                            if (drawPlanned)
                            {
                                if (!planPens.TryGetValue(s.LayerId, out var pPen))
                                {
                                    Color lc = s.LayerColor;
                                    if (lc.IsEmpty || (lc.R == 0 && lc.G == 0 && lc.B == 0))
                                        lc = paper ? Color.FromArgb(90, 90, 90) : Color.FromArgb(170, 170, 170);
                                    Color planCol = paper
                                        ? Color.FromArgb(65, lc.R, lc.G, lc.B)
                                        : Color.FromArgb(80, lc.R, lc.G, lc.B);
                                    pPen = new Pen(planCol, 1f);
                                    planPens[s.LayerId] = pPen;
                                }
                                g.DrawLine(pPen, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
                            }
                        }
                        else if (showRapid)
                        {
                            // Bỏ qua các bước nhảy G0 li ti < 1mm giữa các pixel ảnh để tránh làm tối mờ cả ảnh xem trước
                            float dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
                            if (dx * dx + dy * dy >= 1.0f)
                            {
                                g.DrawLine(rapid, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
                            }
                        }
                    }

                    foreach (var p in planPens.Values) p.Dispose();
                }

                // --- Vẽ nét cắt trùng lặp (highlight đỏ, vẽ đè lên planBmp) ---
                if (dupSegIdx.Count > 0)
                {
                    using var dupPen = new Pen(Color.FromArgb(220, 255, 30, 30), 3f)
                    {
                        DashStyle = DashStyle.Dash,
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round
                    };
                    for (int i = 0; i < segs.Count; i++)
                    {
                        if (!dupSegIdx.Contains(i)) continue;
                        var s = segs[i];
                        g.DrawLine(dupPen, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
                    }
                }
            }

            // Lớp đã khắc: vẽ các đoạn 0..progIdx-1
            using (var g = Graphics.FromImage(burnBmp!))
            using (var pen = NewBurnPen())
            {
                g.Clear(Color.Transparent);
                SetupG(g);
                for (int i = 0; i < progIdx && i < segs.Count; i++) DrawBurn(g, pen, segs[i]);
            }
            drawnUpTo = progIdx;
            dirty = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureLayers();
            if (planBmp == null || burnBmp == null) return;
            var g = e.Graphics;
            g.DrawImageUnscaled(planBmp, 0, 0);
            g.DrawImageUnscaled(burnBmp, 0, 0);

            if (showHead && segs.Count > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float hx, hy;
                Color headColor = Color.Red;
                if (progIdx < segs.Count)
                {
                    var cur = segs[progIdx];
                    if (hiddenLayers.Contains(cur.LayerId)) return; // Không vẽ con trỏ laser ở tầng đang ẩn

                    hx = cur.X1 + (cur.X2 - cur.X1) * progFrac;
                    hy = cur.Y1 + (cur.Y2 - cur.Y1) * progFrac;
                    if (!cur.LayerColor.IsEmpty && (cur.LayerColor.R != 0 || cur.LayerColor.G != 0 || cur.LayerColor.B != 0))
                        headColor = cur.LayerColor;
                    if (cur.LaserOn && progFrac > 0)
                    {
                        var part = new Seg
                        {
                            X1 = cur.X1, Y1 = cur.Y1, X2 = hx, Y2 = hy,
                            LaserOn = true, S = cur.S, Kind = cur.Kind,
                            LayerId = cur.LayerId, LayerColor = cur.LayerColor
                        };
                        using (var pen = NewBurnPen()) DrawBurn(g, pen, part);
                    }
                }
                else
                {
                    var l = segs[segs.Count - 1];
                    if (hiddenLayers.Contains(l.LayerId)) return;
                    hx = l.X2; hy = l.Y2;
                    if (!l.LayerColor.IsEmpty) headColor = l.LayerColor;
                }
                var p = ToScreen(hx, hy);
                using (var pen = new Pen(headColor, 2f))
                {
                    g.DrawEllipse(pen, p.X - 7, p.Y - 7, 14, 14);
                    g.DrawLine(pen, p.X - 12, p.Y, p.X + 12, p.Y);
                    g.DrawLine(pen, p.X, p.Y - 12, p.X, p.Y + 12);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!UserAdjustedView && segs.Count > 0 && Width > 50 && Height > 50)
            {
                FitView();
            }
            else
            {
                dirty = true;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float k = e.Delta > 0 ? 1.15f : 1 / 1.15f;
            float ky = Height - e.Y;
            offset.X = e.X - (e.X - offset.X) * k;
            offset.Y = ky - (ky - offset.Y) * k;
            scale *= k;
            UserAdjustedView = true;
            dirty = true;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e) { drag = true; last = e.Location; }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!drag) return;
            offset.X += e.X - last.X;
            offset.Y -= e.Y - last.Y;
            last = e.Location;
            UserAdjustedView = true;
            dirty = true;
            Invalidate();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); FitView(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { planBmp?.Dispose(); burnBmp?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    //  Phát hiện nét cắt trùng lặp giữa các tầng
    // ------------------------------------------------------------------
    public static class DuplicateCutDetector
    {
        const float TOL = 0.5f;      // mm dung sai - 2 đầu trong vòng 0.5mm coi là cùng điểm
        const float MIN_LEN = 0.3f;  // bỏ qua đoạn rất ngắn (đốm cháy, corner-pass)

        public record DupGroup(int IdxA, int IdxB, int LayerA, int LayerB, string LayerNameA, string LayerNameB, float Overlap);

        public static (HashSet<int> SegIndices, List<DupGroup> Groups) Detect(List<Seg> segs)
        {
            var indices = new HashSet<int>();
            var groups  = new List<DupGroup>();

            // Chỉ xét đoạn LaserOn thật sự
            var cuts = segs
                .Select((s, i) => (s, i))
                .Where(t => t.s.LaserOn && Len(t.s) >= MIN_LEN)
                .ToList();

            for (int a = 0; a < cuts.Count; a++)
            {
                var (sa, ia) = cuts[a];
                for (int b = a + 1; b < cuts.Count; b++)
                {
                    var (sb, ib) = cuts[b];
                    if (sa.LayerId == sb.LayerId) continue;   // cùng tầng không tính

                    float ov = OverlapLength(sa, sb, TOL);
                    if (ov <= MIN_LEN) continue;

                    if (indices.Add(ia) | indices.Add(ib))
                    {
                        groups.Add(new DupGroup(ia, ib, sa.LayerId, sb.LayerId,
                            sa.LayerName, sb.LayerName, ov));
                    }
                }
            }
            return (indices, groups);
        }

        static float Len(Seg s)
        {
            float dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        // Trả về chiều dài đoạn giao nhau (theo 1D projection lên trục của đoạn A)
        static float OverlapLength(Seg a, Seg b, float tol)
        {
            // Hướng và độ dài của đoạn A
            float ax = a.X2 - a.X1, ay = a.Y2 - a.Y1;
            float lenA = MathF.Sqrt(ax * ax + ay * ay);
            if (lenA < 0.001f) return 0;
            float ux = ax / lenA, uy = ay / lenA;  // unit vector A

            // Kiểm tra hướng song song (|cos θ| ≈ 1)
            float bx = b.X2 - b.X1, by = b.Y2 - b.Y1;
            float lenB = MathF.Sqrt(bx * bx + by * by);
            if (lenB < 0.001f) return 0;
            float dot = MathF.Abs(ux * (bx / lenB) + uy * (by / lenB));
            if (dot < 0.985f) return 0;   // cos(10°) ≈ 0.985 - không đủ song song

            // Khoảng cách vuông góc từ đầu B lên đường thẳng A
            float cx = b.X1 - a.X1, cy = b.Y1 - a.Y1;
            float perpDist = MathF.Abs(cx * uy - cy * ux); // cross product 2D = khoảng cách ⊥
            if (perpDist > tol) return 0;

            // Project 4 điểm lên trục A (1D)
            float pA1 = 0, pA2 = lenA;
            float pB1 = cx * ux + cy * uy;
            float pB2 = pB1 + (b.X2 - b.X1) * ux + (b.Y2 - b.Y1) * uy;
            if (pB1 > pB2) (pB1, pB2) = (pB2, pB1);

            float lo = MathF.Max(pA1, pB1);
            float hi = MathF.Min(pA2, pB2);
            return hi - lo;   // > 0 → có giao nhau
        }
    }

    // ------------------------------------------------------------------
    //  Form xem trước / mô phỏng / điều chỉnh
    // ------------------------------------------------------------------
    public class GCodePreviewForm : Form
    {
        static readonly double[] Mults = { 1, 5, 10, 50, 200, 1000, 5000 };

        readonly List<Vectoritem> _items;
        readonly GCodeSettings _cfg;
        readonly System.Windows.Forms.Timer _simTimer;
        readonly System.Windows.Forms.Timer _debounce;
        readonly Stopwatch _sw = new();

        string _code = "";
        List<Seg> _segs = new();
        double[] _cum = Array.Empty<double>();
        double _total, _simTime;
        bool _playing, _suppress, _pendingFit;
        int _lastLine = -1, _shownLines;

        // controls
        ListBox lstItems = null!;
        NumericUpDown nPower = null!, nSpeed = null!, nPass = null!;
        NumericUpDown nMm = null!, nMaxS = null!, nStep = null!, nThr = null!, nRapid = null!, nOverscan = null!, nContrast = null!, nBrightness = null!, nGamma = null!;
        ComboBox cbDither = null!, cbScanDir = null!;
        CheckBox chkSharpen = null!, chkInv = null!, chkOptimize = null!, chkInsideOut = null!, chkPaper = null!, chkRapid = null!, chkFollow = null!;
        TextBox txtCode = null!;
        SplitContainer split = null!;
        GCodeCanvas canvas = null!;
        Panel layerBar = null!;
        FlowLayoutPanel pnlLayerCards = null!;
        Button btnPlay = null!, btnStop = null!, btnPrevLayer = null!, btnNextLayer = null!, btnSpeed = null!;
        int _speedIdx = 3; // Mặc định x50
        TrackBar tbProg = null!;
        Label lblInfo = null!, lblTime = null!, lblLayerStatus = null!;
        readonly ToolTip _layerTip = new();

        public class SimLayer
        {
            public int LayerId;
            public string Name = "Lớp";
            public Color Color = Color.Black;
            public bool Visible = true;
            public int StartSegIdx;
            public int EndSegIdx;
            public double StartTime;
            public double EndTime;
            public double Duration => Math.Max(0, EndTime - StartTime);
            public double CutLen;
            public int S;
            public int Feed;
            public int SegCount => Math.Max(0, EndSegIdx - StartSegIdx + 1);
            public Panel? CardUi;
            public CheckBox? ChkUi;
        }

        readonly List<SimLayer> _simLayers = new();

        public GCodePreviewForm(List<Vectoritem> items, GCodeSettings? settings = null)
        {
            _items = items;
            _cfg = settings ?? new GCodeSettings();

            // Tự co giãn theo DPI màn hình: vị trí / kích thước viết bằng px ở 96 DPI sẽ được nhân theo tỉ lệ thật,
            // nên chữ (đã phóng theo hệ thống) không còn bị cắt.
            AutoScaleDimensions = new System.Drawing.SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "G-code - Xem trước, mô phỏng & điều chỉnh";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);       // chữ đậm cho dễ nhìn (ô code vẫn dùng Consolas)

            var scr = Screen.FromControl(this)?.WorkingArea ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
            Width = Math.Min(1260, (int)(scr.Width * 0.94));
            Height = Math.Min(780, (int)(scr.Height * 0.92));
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 560);
            WindowState = FormWindowState.Maximized; // Mặc định mở ra dãn tối đa toàn màn hình

            _simTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _simTimer.Tick += SimTick;
            _debounce = new System.Windows.Forms.Timer { Interval = 350 };
            _debounce.Tick += (s, e) => { Regenerate(_pendingFit); _pendingFit = false; };

            BuildUi();

            Shown += (s, e) =>
            {
                if (lstItems.Items.Count > 0) lstItems.SelectedIndex = 0;
                ApplySplitterDistance(240);
                Regenerate(true);
                BeginInvoke((Action)(() =>
                {
                    ApplySplitterDistance(240);
                    if (canvas.Width > 50 && canvas.Height > 50) canvas.FitView();
                }));
            };
            FormClosed += (s, e) => { _simTimer.Stop(); _debounce.Stop(); _layerTip.Dispose(); };
        }

        void ApplySplitterDistance(int distance)
        {
            try
            {
                int min = 120;
                int max = split.Width - 200;
                if (max > min)
                {
                    split.SplitterDistance = Math.Clamp(distance, min, max);
                }
            }
            catch { }
        }

        // ================= UI =================
        void BuildUi()
        {
            split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1
            };
            var left = new Panel { Dock = DockStyle.Left, Width = 310, Padding = new Padding(4), AutoScroll = true };
            Controls.Add(split);
            Controls.Add(left);
            left.SendToBack();

            // --- ô code (trái của split) ---
            txtCode = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, ReadOnly = true,
                HideSelection = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f)
            };
            split.Panel1.Controls.Add(txtCode);

            // --- canvas + thanh mô phỏng (phải của split) ---
            canvas = new GCodeCanvas { Dock = DockStyle.Fill };

            // --- Thanh Tầng khắc (Layer Bar) nằm trên cùng của vùng mô phỏng ---
            layerBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = Color.FromArgb(242, 245, 252),
                Padding = new Padding(6, 4, 6, 4)
            };
            var lblT = new Label
            {
                Text = "❖ TẦNG KHẮC:",
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 50, 90),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 16, 8, 0)
            };
            var pnlRight = new Panel
            {
                Dock = DockStyle.Right,
                Width = 96,
                BackColor = Color.Transparent
            };
            var btnAllLayers = new Button
            {
                Text = "Hiện tất cả",
                Width = 86,
                Height = 28,
                Left = 5,
                Top = 16,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnAllLayers.FlatAppearance.BorderColor = Color.LightGray;
            btnAllLayers.Click += (s, e) => ShowAllSimLayers();
            pnlRight.Controls.Add(btnAllLayers);

            pnlLayerCards = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent
            };
            layerBar.Controls.Add(pnlLayerCards);
            layerBar.Controls.Add(lblT);
            layerBar.Controls.Add(pnlRight);

            // --- Thanh điều khiển mô phỏng (Dock Bottom) dùng TableLayoutPanel để tự co giãn theo DPI ---
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(248, 249, 252),
                Padding = new Padding(4, 3, 4, 3),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Hàng 1: nhãn trạng thái
            bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Hàng 2: TrackBar
            bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Hàng 3: nút bấm

            split.Panel2.Controls.Add(canvas);     // Fill
            split.Panel2.Controls.Add(layerBar);   // Top
            split.Panel2.Controls.Add(bar);        // Bottom
            layerBar.SendToBack();
            bar.SendToBack();

            // --- Hàng 1: Panel chứa nhãn trạng thái tầng (trái) + thời gian (phải) ---
            var pnlStatus = new Panel { Dock = DockStyle.Fill, Height = 24 };

            lblLayerStatus = new Label
            {
                Left = 0,
                Top = 2,
                Height = 22,
                Text = "Tầng: --",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 60, 120),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lblTime = new Label
            {
                Top = 2,
                Width = 140,
                Height = 22,
                Text = "00:00 / 00:00",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            pnlStatus.Controls.Add(lblLayerStatus);
            pnlStatus.Controls.Add(lblTime);
            pnlStatus.Resize += (s, e) =>
            {
                lblTime.Left = pnlStatus.Width - lblTime.Width;
                lblLayerStatus.Width = Math.Max(50, lblTime.Left - 4);
            };

            // --- Hàng 2: TrackBar tiến độ ---
            tbProg = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                Height = 28
            };
            tbProg.Scroll += (s, e) =>
            {
                _simTime = tbProg.Value / 1000.0 * _total;
                UpdateSim(true);
            };

            // --- Hàng 3: Nút điều khiển & Tốc độ ---
            var pnlBtns = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoScroll = false,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 2, 0, 2)
            };

            btnPrevLayer = new Button { Width = 100, Height = 30, Text = "⏮ Tầng trước", Margin = new Padding(2, 1, 4, 1) };
            btnPrevLayer.Click += (s, e) => JumpPrevLayer();

            btnPlay = new Button { Width = 120, Height = 30, Text = "▶ Chạy mô phỏng", Margin = new Padding(2, 1, 4, 1) };
            btnPlay.Click += (s, e) => TogglePlay();

            btnNextLayer = new Button { Width = 100, Height = 30, Text = "⏭ Tầng sau", Margin = new Padding(2, 1, 4, 1) };
            btnNextLayer.Click += (s, e) => JumpNextLayer();

            btnStop = new Button { Width = 90, Height = 30, Text = "⏹ Xem xong", Margin = new Padding(2, 1, 12, 1) };
            btnStop.Click += (s, e) => ShowFinal();

            btnSpeed = new Button
            {
                Width = 115,
                Height = 30,
                Text = "⚡ Tốc độ: x50 ▾",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(245, 247, 252),
                ForeColor = Color.FromArgb(30, 45, 80),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Margin = new Padding(4, 1, 4, 1),
                Cursor = Cursors.Hand
            };
            btnSpeed.FlatAppearance.BorderColor = Color.FromArgb(180, 195, 220);

            var speedMenu = new ContextMenuStrip();
            for (int k = 0; k < Mults.Length; k++)
            {
                int idx = k;
                double m = Mults[k];
                var mi = new ToolStripMenuItem($"x{m:0.#}");
                mi.Checked = (k == _speedIdx);
                mi.Click += (s, e) =>
                {
                    _speedIdx = idx;
                    btnSpeed.Text = $"⚡ Tốc độ: x{m:0.#} ▾";
                    foreach (ToolStripMenuItem item in speedMenu.Items)
                        item.Checked = false;
                    mi.Checked = true;
                };
                speedMenu.Items.Add(mi);
            }

            btnSpeed.Click += (s, e) =>
            {
                speedMenu.Show(btnSpeed, 0, btnSpeed.Height);
            };

            pnlBtns.Controls.AddRange(new Control[] { btnPrevLayer, btnPlay, btnNextLayer, btnStop, btnSpeed });

            bar.Controls.Add(pnlStatus, 0, 0);
            bar.Controls.Add(tbProg, 0, 1);
            bar.Controls.Add(pnlBtns, 0, 2);

            // --- panel trái: thông số ---
            int y = 4;
            left.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = "ĐỐI TƯỢNG (chọn để chỉnh riêng):", Font = new Font(Font, FontStyle.Bold) });
            y += 22;
            lstItems = new ListBox { Left = 8, Top = y, Width = 290, Height = 110 };
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                string extra = it is VectorText t ? " - " + t.TextContent : "";
                lstItems.Items.Add((i + 1) + ". " + it.Name + extra);
            }
            lstItems.SelectedIndexChanged += (s, e) => LoadItemParams();
            left.Controls.Add(lstItems);
            y += 116;

            nPower = AddNum(left, "Công suất (%)", ref y, 80, 0, 100, 0, 5);
            nSpeed = AddNum(left, "Tốc độ F (mm/phút)", ref y, 1000, 1, 30000, 0, 100);
            nPass = AddNum(left, "Số lần chạy", ref y, 1, 1, 50, 0, 1);
            nPower.ValueChanged += (s, e) => ItemParamChanged();
            nSpeed.ValueChanged += (s, e) => ItemParamChanged();
            nPass.ValueChanged += (s, e) => ItemParamChanged();

            var btnAll = new Button { Left = 8, Top = y, Width = 290, Height = 28, Text = "Áp dụng 3 thông số cho TẤT CẢ" };
            btnAll.Click += (s, e) => ApplyToAll();
            left.Controls.Add(btnAll);
            y += 40;

            left.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = "THÔNG SỐ CHUNG:", Font = new Font(Font, FontStyle.Bold) });
            y += 22;
            nMm = AddNum(left, "mm trên mỗi pixel canvas", ref y, (decimal)_cfg.MmPerPixel, 0.01m, 10m, 3, 0.01m);
            nMm.Enabled = false;
            nMaxS = AddNum(left, "S tối đa của firmware ($30)", ref y, _cfg.MaxS, 1, 100000, 0, 100);
            nRapid = AddNum(left, "Tốc độ G0 để ước tính (mm/phút)", ref y, _cfg.RapidFeedForEstimate, 100, 60000, 0, 500);
            chkOptimize = AddChk(left, "Tối ưu đường chạy G0 (TSP / Nearest)", ref y, _cfg.OptimizeToolpath);
            chkInsideOut = AddChk(left, "Cắt lỗ trong trước, viền ngoài sau", ref y, _cfg.CutInsideOut);

            nMm.ValueChanged += (s, e) => ScheduleRegen(true);
            nMaxS.ValueChanged += (s, e) => ScheduleRegen(false);
            nRapid.ValueChanged += (s, e) => ScheduleRegen(false);
            chkOptimize.CheckedChanged += (s, e) => ScheduleRegen(false);
            chkInsideOut.CheckedChanged += (s, e) => ScheduleRegen(false);

            y += 6;
            left.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = "KHẮC ẢNH (RASTER):", Font = new Font(Font, FontStyle.Bold) });
            y += 22;

            string[] ditherItems = new[]
            {
                "Floyd-Steinberg (Mịn - Khuyên dùng)",
                "Atkinson (Tương phản cao - Nét)",
                "Stucki (Rất mịn - Khử sọc)",
                "Ma trận Bayer 4x4 (Retro)",
                "Ảnh xám PWM (Laser Mode $32=1)",
                "Đen trắng (Theo ngưỡng)"
            };
            int ditherIdx = _cfg.ImageDither switch
            {
                ImageDitherMode.FloydSteinberg => 0,
                ImageDitherMode.Atkinson => 1,
                ImageDitherMode.Stucki => 2,
                ImageDitherMode.OrderedBayer => 3,
                ImageDitherMode.Grayscale => 4,
                ImageDitherMode.Threshold => 5,
                _ => 0
            };
            cbDither = AddCmb(left, "Thuật toán tạo điểm ảnh:", ref y, ditherItems, ditherIdx);

            string[] scanDirItems = new[]
            {
                "Hai chiều (Zig-zag - Nhanh nhất)",
                "Một chiều (Chống rung rơ / răng cưa)"
            };
            int scanDirIdx = _cfg.ImageScanDir == RasterScanDirection.Unidirectional ? 1 : 0;
            cbScanDir = AddCmb(left, "Chế độ quét laser:", ref y, scanDirItems, scanDirIdx);

            nStep = AddNum(left, "Bước dòng quét ảnh (mm)", ref y, (decimal)_cfg.RasterStepMm, 0.02m, 2m, 2, 0.02m);
            nOverscan = AddNum(left, "Overscan chạy đà ngoài biên (mm)", ref y, (decimal)_cfg.OverscanMm, 0m, 10m, 1, 0.5m);
            nBrightness = AddNum(left, "Độ sáng ảnh (-100 .. +100)", ref y, (decimal)_cfg.ImageBrightness, -100m, 100m, 0, 5m);
            nContrast = AddNum(left, "Độ tương phản ảnh (0.2 .. 3.0)", ref y, (decimal)_cfg.ImageContrast, 0.2m, 3.0m, 1, 0.1m);
            nGamma = AddNum(left, "Gamma ảnh (0.2 .. 3.0)", ref y, (decimal)_cfg.ImageGamma, 0.2m, 3.0m, 1, 0.1m);
            nThr = AddNum(left, "Ngưỡng đen/trắng (0-255)", ref y, _cfg.ImageThreshold, 0, 255, 0, 5);
            nThr.Enabled = _cfg.ImageDither == ImageDitherMode.Threshold;

            chkSharpen = AddChk(left, "Tự động làm nét (Sharpen viền)", ref y, _cfg.ImageSharpen);
            chkInv = AddChk(left, "Đảo màu ảnh (khắc phần sáng)", ref y, _cfg.ImageInvert);

            cbDither.SelectedIndexChanged += (s, e) =>
            {
                nThr.Enabled = cbDither.SelectedIndex == 5; // Chỉ bật khi chọn Đen trắng theo ngưỡng
                ScheduleRegen(false);
            };
            cbScanDir.SelectedIndexChanged += (s, e) => ScheduleRegen(false);
            nStep.ValueChanged += (s, e) => ScheduleRegen(false);
            nOverscan.ValueChanged += (s, e) => ScheduleRegen(false);
            nBrightness.ValueChanged += (s, e) => ScheduleRegen(false);
            nContrast.ValueChanged += (s, e) => ScheduleRegen(false);
            nGamma.ValueChanged += (s, e) => ScheduleRegen(false);
            nThr.ValueChanged += (s, e) => ScheduleRegen(false);
            chkSharpen.CheckedChanged += (s, e) => ScheduleRegen(false);
            chkInv.CheckedChanged += (s, e) => ScheduleRegen(false);

            y += 6;
            left.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = "HIỂN THỊ CANVAS:", Font = new Font(Font, FontStyle.Bold) });
            y += 22;

            chkPaper = AddChk(left, "Xem kiểu giấy (đen = vết khắc)", ref y, true);
            chkRapid = AddChk(left, "Hiện đường di chuyển nhanh (G0)", ref y, true);
            chkFollow = AddChk(left, "Bôi chọn dòng lệnh đang chạy", ref y, true);
            chkPaper.CheckedChanged += (s, e) => canvas.PaperMode = chkPaper.Checked;
            chkRapid.CheckedChanged += (s, e) => canvas.ShowRapid = chkRapid.Checked;
            y += 6;

            var btnFit = new Button { Left = 8, Top = y, Width = 140, Height = 30, Text = "Fit view" };
            btnFit.Click += (s, e) => { canvas.UserAdjustedView = false; canvas.FitView(); };
            var btnSave = new Button { Left = 156, Top = y, Width = 142, Height = 30, Text = "Lưu G-code..." };
            btnSave.Click += (s, e) => SaveCode();
            left.Controls.Add(btnFit);
            left.Controls.Add(btnSave);
            y += 40;

            lblInfo = new Label { Left = 8, Top = y, Width = 290, Height = 150, Text = "..." };
            left.Controls.Add(lblInfo);
        }

        static NumericUpDown AddNum(Control p, string label, ref int y, decimal val, decimal min, decimal max, int dec, decimal inc)
        {
            p.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = label });
            y += 20;
            var n = new NumericUpDown
            {
                Left = 8, Top = y, Width = 290, DecimalPlaces = dec,
                Minimum = min, Maximum = max, Increment = inc
            };
            n.Value = Math.Min(max, Math.Max(min, val));
            p.Controls.Add(n);
            y += 30;
            return n;
        }

        static CheckBox AddChk(Control p, string text, ref int y, bool chk)
        {
            var c = new CheckBox { Left = 8, Top = y, Width = 290, Text = text, Checked = chk };
            p.Controls.Add(c);
            y += 24;
            return c;
        }

        static ComboBox AddCmb(Control p, string label, ref int y, string[] items, int selectedIdx)
        {
            p.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = label });
            y += 20;
            var cb = new ComboBox
            {
                Left = 8, Top = y, Width = 290,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cb.Items.AddRange(items);
            if (selectedIdx >= 0 && selectedIdx < items.Length) cb.SelectedIndex = selectedIdx;
            p.Controls.Add(cb);
            y += 32;
            return cb;
        }

        // ================= Thông số từng đối tượng =================
        void LoadItemParams()
        {
            int i = lstItems.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            var it = _items[i];
            _suppress = true;
            nPower.Value = Math.Min(nPower.Maximum, Math.Max(nPower.Minimum, it.LaserPower));
            nSpeed.Value = Math.Min(nSpeed.Maximum, Math.Max(nSpeed.Minimum, it.LaserSpeed));
            nPass.Value = Math.Min(nPass.Maximum, Math.Max(nPass.Minimum, it.Passes));
            _suppress = false;
        }

        void ItemParamChanged()
        {
            if (_suppress) return;
            int i = lstItems.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            var it = _items[i];
            it.LaserPower = (int)nPower.Value;
            it.LaserSpeed = (int)nSpeed.Value;
            it.Passes = (int)nPass.Value;
            ScheduleRegen(false);
        }

        void ApplyToAll()
        {
            foreach (var it in _items)
            {
                it.LaserPower = (int)nPower.Value;
                it.LaserSpeed = (int)nSpeed.Value;
                it.Passes = (int)nPass.Value;
            }
            ScheduleRegen(false);
        }

        // ================= Sinh G-code =================
        void ScheduleRegen(bool fit)
        {
            if (_suppress) return;
            _pendingFit |= fit;
            _debounce.Stop();
            _debounce.Start();
        }

        void ApplyCfg()
        {
            _cfg.MmPerPixel = (float)nMm.Value;
            _cfg.MaxS = (int)nMaxS.Value;
            _cfg.RasterStepMm = (double)nStep.Value;
            _cfg.OverscanMm = (double)nOverscan.Value;
            _cfg.ImageBrightness = (double)nBrightness.Value;
            _cfg.ImageContrast = (double)nContrast.Value;
            _cfg.ImageGamma = (double)nGamma.Value;
            _cfg.ImageThreshold = (int)nThr.Value;
            _cfg.ImageSharpen = chkSharpen.Checked;
            _cfg.ImageInvert = chkInv.Checked;

            _cfg.ImageDither = cbDither.SelectedIndex switch
            {
                0 => ImageDitherMode.FloydSteinberg,
                1 => ImageDitherMode.Atkinson,
                2 => ImageDitherMode.Stucki,
                3 => ImageDitherMode.OrderedBayer,
                4 => ImageDitherMode.Grayscale,
                5 => ImageDitherMode.Threshold,
                _ => ImageDitherMode.FloydSteinberg
            };
            _cfg.ImageBinary = _cfg.ImageDither != ImageDitherMode.Grayscale;

            _cfg.ImageScanDir = cbScanDir.SelectedIndex == 1
                ? RasterScanDirection.Unidirectional
                : RasterScanDirection.Bidirectional;

            _cfg.OptimizeToolpath = chkOptimize.Checked;
            _cfg.CutInsideOut = chkInsideOut.Checked;
            _cfg.RapidFeedForEstimate = (int)nRapid.Value;
        }

        void Regenerate(bool fit)
        {
            _debounce.Stop();
            _simTimer.Stop();
            _playing = false;
            btnPlay.Text = "▶ Chạy mô phỏng";

            try
            {
                Cursor = Cursors.WaitCursor;
                ApplyCfg();
                GCodeResult res = new GCodeCompiler(_cfg).Compile(_items);
                _code = res.Code;
                ShowCode(_code);

                _segs = GCodeParser.Parse(_code.Split('\n'), _cfg.RapidFeedForEstimate);
                bool hasRaster = _items.Any(it => it is VectorImage);
                canvas.BeamWidth = hasRaster ? (float)_cfg.RasterStepMm : 0.1f;
                canvas.Load(_segs, fit);
                BuildTimeline();

                lblInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "Dòng lệnh: {0}\nĐoạn: {1}\nKích thước: {2:0.#} x {3:0.#} mm\nQuãng đường cắt (G1): {4:0.#} mm\nQuãng đường chạy không (G0): {5:0.#} mm\nƯớc tính: {6:0.##} phút",
                    res.LineCount, _segs.Count, res.WidthMm, res.HeightMm, res.CutLengthMm, res.RapidLengthMm, res.EstimatedMinutes);
            }
            catch (Exception ex)
            {
                lblInfo.Text = "Lỗi: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        void ShowCode(string code)
        {
            const int maxShown = 30000;
            var lines = code.Split('\n');
            _lastLine = -1;
            if (lines.Length > maxShown)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < maxShown; i++) sb.Append(lines[i].TrimEnd('\r')).Append("\r\n");
                sb.Append("; ... (rút gọn khi hiển thị, tổng ").Append(lines.Length)
                  .Append(" dòng. File lưu vẫn đầy đủ)");
                txtCode.Text = sb.ToString();
                _shownLines = maxShown;
            }
            else
            {
                txtCode.Text = code;
                _shownLines = lines.Length;
            }
        }

        // ================= Mô phỏng theo Tầng =================
        void BuildTimeline()
        {
            int n = _segs.Count;
            _cum = new double[n];
            _simLayers.Clear();
            double t = 0;
            SimLayer? curL = null;

            for (int i = 0; i < n; i++)
            {
                var s = _segs[i];
                double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
                double len = Math.Sqrt(dx * dx + dy * dy);
                t += len / (Math.Max(1, s.Feed) / 60.0);    // giây
                _cum[i] = t;

                if (curL == null || curL.LayerId != s.LayerId)
                {
                    if (curL != null)
                    {
                        curL.EndSegIdx = i - 1;
                        curL.EndTime = _cum[i - 1];
                    }
                    curL = new SimLayer
                    {
                        LayerId = s.LayerId,
                        Name = string.IsNullOrWhiteSpace(s.LayerName) ? $"Lớp {s.LayerId + 1:00}" : s.LayerName,
                        Color = s.LayerColor,
                        StartSegIdx = i,
                        StartTime = i > 0 ? _cum[i - 1] : 0,
                        S = s.S,
                        Feed = s.Feed
                    };
                    _simLayers.Add(curL);
                }
                if (s.LaserOn) curL.CutLen += len;
            }

            if (curL != null)
            {
                curL.EndSegIdx = n - 1;
                curL.EndTime = n > 0 ? _cum[n - 1] : 0;
            }

            _total = t;
            _simTime = _total;                              // trạng thái ban đầu: hoàn tất
            BuildLayerCardsUi();
            UpdateTimeLabels();
            tbProg.Value = tbProg.Maximum;
            UpdateSim(false);
        }

        void BuildLayerCardsUi()
        {
            pnlLayerCards.Controls.Clear();
            _layerTip.RemoveAll();
            int lNum = 1;
            foreach (var l in _simLayers)
            {
                var card = new Panel
                {
                    Width = 165,
                    Height = 28,
                    Margin = new Padding(2, 2, 4, 2),
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Cursor = Cursors.Hand
                };

                var swatch = new Panel
                {
                    Left = 4, Top = 7, Width = 14, Height = 14,
                    BackColor = l.Color.IsEmpty ? Color.Black : l.Color
                };

                var chk = new CheckBox
                {
                    Left = 22, Top = 4, Width = 138, Height = 20,
                    Text = $"{lNum}. {l.Name} ({Fmt(l.Duration)})",
                    Checked = l.Visible,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    AutoEllipsis = true
                };

                int lid = l.LayerId;
                chk.CheckedChanged += (s, e) =>
                {
                    l.Visible = chk.Checked;
                    canvas.SetLayerVisibility(lid, l.Visible);

                    // Nếu tầng đang chạy vừa bị bỏ tích, tự chuyển sang tầng đang bật
                    var curL = GetActiveLayer(_simTime);
                    if (curL != null && !curL.Visible)
                    {
                        var visLayers = _simLayers.Where(x => x.Visible).ToList();
                        if (visLayers.Count > 0)
                        {
                            var nextVis = visLayers.FirstOrDefault(x => x.StartTime >= _simTime) ?? visLayers.First();
                            _simTime = nextVis.StartTime;
                        }
                    }
                    UpdateSim(_playing);
                };

                var captured = l;
                card.Click += (s, e) => JumpToLayer(captured);
                swatch.Click += (s, e) => JumpToLayer(captured);

                string tipText = $"Tầng {lNum}: {l.Name}\nThời gian: {Fmt(l.Duration)}\nChiều dài cắt: {l.CutLen:0.#} mm\nSố đoạn: {l.SegCount}\n(Tích chọn: Bật/Tắt mô phỏng tầng | Nháy đúp: Solo tầng này)";
                _layerTip.SetToolTip(card, tipText);
                _layerTip.SetToolTip(chk, tipText);
                _layerTip.SetToolTip(swatch, tipText);

                card.DoubleClick += (s, e) => SoloSimLayer(captured);
                chk.DoubleClick += (s, e) => SoloSimLayer(captured);

                card.Controls.Add(swatch);
                card.Controls.Add(chk);

                l.CardUi = card;
                l.ChkUi = chk;

                pnlLayerCards.Controls.Add(card);
                lNum++;
            }
        }

        void ShowAllSimLayers()
        {
            foreach (var l in _simLayers)
            {
                l.Visible = true;
                if (l.ChkUi != null) l.ChkUi.Checked = true;
            }
            canvas.ShowAllLayers();
            UpdateSim(_playing);
        }

        void SoloSimLayer(SimLayer target)
        {
            foreach (var l in _simLayers)
            {
                l.Visible = (l == target);
                if (l.ChkUi != null) l.ChkUi.Checked = l.Visible;
            }
            canvas.SoloLayer(target.LayerId);
            JumpToLayer(target);
        }

        void JumpToLayer(SimLayer l)
        {
            _simTime = l.StartTime;
            UpdateSim(true);
        }

        SimLayer? GetActiveLayer(double time)
        {
            if (_simLayers.Count == 0) return null;
            for (int i = 0; i < _simLayers.Count; i++)
            {
                var l = _simLayers[i];
                bool isLast = (i == _simLayers.Count - 1);
                if (isLast ? (time >= l.StartTime && time <= l.EndTime + 0.001) : (time >= l.StartTime && time < l.EndTime))
                    return l;
            }
            return _simTime >= _total - 0.05 ? _simLayers.Last() : _simLayers.First();
        }

        void JumpNextLayer()
        {
            var visLayers = _simLayers.Where(x => x.Visible).ToList();
            if (visLayers.Count == 0) return;

            var cur = GetActiveLayer(_simTime);
            int idx = cur != null ? visLayers.IndexOf(cur) : -1;
            if (idx >= 0 && idx + 1 < visLayers.Count)
            {
                _simTime = visLayers[idx + 1].StartTime;
            }
            else
            {
                _simTime = visLayers[0].StartTime;
            }
            UpdateSim(true);
        }

        void JumpPrevLayer()
        {
            var visLayers = _simLayers.Where(x => x.Visible).ToList();
            if (visLayers.Count == 0) return;

            var cur = GetActiveLayer(_simTime);
            int idx = cur != null ? visLayers.IndexOf(cur) : -1;
            if (cur != null && cur.Visible && _simTime - cur.StartTime > 0.8)
            {
                _simTime = cur.StartTime;
            }
            else if (idx > 0)
            {
                _simTime = visLayers[idx - 1].StartTime;
            }
            else
            {
                _simTime = visLayers.Last().StartTime;
            }
            UpdateSim(true);
        }

        int FindIdx(double t)
        {
            int lo = 0, hi = _cum.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cum[mid] <= t) lo = mid + 1; else hi = mid;
            }
            return lo;   // đoạn đầu tiên chưa chạy xong
        }

        void TogglePlay()
        {
            if (_segs.Count == 0) return;
            if (_playing)
            {
                _playing = false;
                _simTimer.Stop();
                btnPlay.Text = "▶ Chạy tiếp";
                return;
            }

            var visLayers = _simLayers.Where(x => x.Visible).ToList();
            if (visLayers.Count == 0)
            {
                MessageBox.Show(this, "Không có tầng nào được bật để mô phỏng.\nHãy tích chọn ít nhất một tầng.", "Mô phỏng G-code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double lastVisEnd = visLayers.Last().EndTime;
            var curL = GetActiveLayer(_simTime);
            // Nếu đã kết thúc tầng hiển thị cuối cùng, hoặc thời điểm hiện tại đang ở tầng bị tắt:
            // nhảy ngay về đầu tầng hiển thị thích hợp
            if (_simTime >= lastVisEnd - 0.05 || curL == null || !curL.Visible)
            {
                var nextVis = visLayers.FirstOrDefault(x => x.StartTime >= _simTime) ?? visLayers.First();
                _simTime = nextVis.StartTime;
            }

            _playing = true;
            btnPlay.Text = "⏸ Tạm dừng";
            _sw.Restart();
            _simTimer.Start();
            UpdateSim(true);
        }

        void ShowFinal()
        {
            _playing = false;
            _simTimer.Stop();
            btnPlay.Text = "▶ Chạy mô phỏng";
            var lastVis = _simLayers.LastOrDefault(x => x.Visible);
            _simTime = lastVis != null ? lastVis.EndTime : _total;
            UpdateSim(false);
        }

        void SimTick(object? sender, EventArgs e)
        {
            double dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();
            double mult = Mults[Math.Clamp(_speedIdx, 0, Mults.Length - 1)];
            _simTime += dt * mult;

            var visLayers = _simLayers.Where(x => x.Visible).ToList();
            if (visLayers.Count == 0)
            {
                _playing = false;
                _simTimer.Stop();
                btnPlay.Text = "▶ Chạy lại";
                UpdateSim(false);
                return;
            }

            double lastVisEnd = visLayers.Last().EndTime;
            if (_simTime >= lastVisEnd)
            {
                _simTime = lastVisEnd;
                _playing = false;
                _simTimer.Stop();
                btnPlay.Text = "▶ Chạy lại";
                UpdateSim(true);
                return;
            }

            // Kiểm tra xem thời điểm hiện tại có rơi vào tầng bị ẩn không
            var curL = GetActiveLayer(_simTime);
            if (curL != null && !curL.Visible)
            {
                // Tầng này bị ẩn -> nhảy cóc qua tầng hiển thị tiếp theo
                var nextVis = visLayers.FirstOrDefault(x => x.StartTime >= _simTime);
                if (nextVis != null)
                {
                    _simTime = nextVis.StartTime;
                }
                else
                {
                    _simTime = lastVisEnd;
                    _playing = false;
                    _simTimer.Stop();
                    btnPlay.Text = "▶ Chạy lại";
                }
            }

            UpdateSim(true);
        }

        void UpdateSim(bool head)
        {
            int idx = FindIdx(_simTime);
            float frac = 1f;
            if (idx < _segs.Count)
            {
                double start = idx > 0 ? _cum[idx - 1] : 0;
                double dur = _cum[idx] - start;
                frac = dur > 0 ? (float)Math.Clamp((_simTime - start) / dur, 0, 1) : 1f;
            }
            canvas.SetProgress(idx, frac, head);

            tbProg.Value = _total > 0 ? Math.Clamp((int)(_simTime / _total * 1000), 0, 1000) : 0;
            UpdateTimeLabels();

            // Cập nhật trạng thái Tầng đang chạy
            var activeL = GetActiveLayer(_simTime);
            if (activeL != null && activeL.Visible)
            {
                int lNum = _simLayers.IndexOf(activeL) + 1;
                double lCurTime = Math.Max(0, _simTime - activeL.StartTime);
                double pct = activeL.Duration > 0.001 ? Math.Clamp(lCurTime / activeL.Duration * 100.0, 0, 100) : 100;
                lblLayerStatus.Text = $"❖ Tầng {lNum}/{_simLayers.Count}: {activeL.Name} • {pct:0}% ({Fmt(lCurTime)}/{Fmt(activeL.Duration)})";
                lblLayerStatus.ForeColor = (activeL.Color.ToArgb() == Color.Black.ToArgb() || activeL.Color.IsEmpty)
                    ? Color.FromArgb(30, 40, 80)
                    : activeL.Color;

                foreach (var l in _simLayers)
                {
                    if (l.CardUi != null)
                    {
                        l.CardUi.BackColor = (l == activeL)
                            ? Color.FromArgb(220, 238, 255)
                            : Color.White;
                    }
                }
            }
            else if (activeL != null && !activeL.Visible)
            {
                lblLayerStatus.Text = $"❖ {activeL.Name} (đã bỏ chọn / không mô phỏng)";
                lblLayerStatus.ForeColor = Color.DimGray;
                foreach (var l in _simLayers)
                {
                    if (l.CardUi != null) l.CardUi.BackColor = Color.White;
                }
            }
            else
            {
                lblLayerStatus.Text = "Tầng: --";
                lblLayerStatus.ForeColor = Color.DimGray;
                foreach (var l in _simLayers)
                {
                    if (l.CardUi != null) l.CardUi.BackColor = Color.White;
                }
            }

            if (chkFollow.Checked && idx < _segs.Count) HighlightLine(_segs[idx].Line);
        }

        void UpdateTimeLabels()
        {
            lblTime.Text = Fmt(_simTime) + " / " + Fmt(_total);
        }

        static string Fmt(double sec)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, sec));
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        }

        void HighlightLine(int line)
        {
            if (line == _lastLine || line < 0 || line >= _shownLines) return;
            _lastLine = line;
            int s = txtCode.GetFirstCharIndexFromLine(line);
            if (s < 0) return;
            int e = txtCode.GetFirstCharIndexFromLine(line + 1);
            if (e < 0) e = txtCode.TextLength;
            txtCode.Select(s, Math.Max(0, e - s));
            txtCode.ScrollToCaret();
        }

        // ================= Lưu =================
        void SaveCode()
        {
            if (string.IsNullOrEmpty(_code)) return;
            using var d = new SaveFileDialog
            {
                Filter = "G-code (*.nc)|*.nc|G-code (*.gcode)|*.gcode|Text (*.txt)|*.txt",
                FileName = "output.nc"
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(d.FileName, _code, new UTF8Encoding(false));
            MessageBox.Show("Đã lưu: " + d.FileName, "Lưu G-code");
        }
    }
}
