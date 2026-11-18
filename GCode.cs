// =====================================================================
//  GCode.cs  -  G-CODE: bộ biên dịch + form mô phỏng
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Bộ biên dịch G-code, khung đèn đỏ, xử lý ảnh raster   (trước đây: GCode.cs)
//    2. Form mô phỏng / xem trước G-code   (trước đây: GCodePreviewForm.cs)
// =====================================================================

// =====================================================================
// PHẦN: Bộ biên dịch G-code, khung đèn đỏ, xử lý ảnh raster   (trước đây là file GCode.cs)
// =====================================================================
// =====================================================================
//  GCode.cs  -  G-CODE: biên dịch hình thành G-code và G-code khung đèn đỏ
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Biên dịch G-code   (trước đây: GCodeCompiler.cs)
//    2. Khung đèn đỏ / khung bao   (trước đây: GCodeFrame.cs)
// =====================================================================
// =====================================================================
// PHẦN 1: Biên dịch G-code   (trước đây là file GCodeCompiler.cs)
// =====================================================================
// =====================================================================
//  GCodeCompiler.cs  -  Biên dịch danh sách Vectoritem -> chuỗi G-code
//  Project: test01_1014 (.NET 8, WinForms)
//
//  Hỗ trợ:
//    VectorRectangle / VectorEllipse / VectorLine  -> khắc/cắt viền
//    VectorText                                    -> khắc viền chữ (outline)
//    VectorImage                                   -> khắc ảnh raster (zig-zag)
//
//  Quy ước đơn vị:
//    Canvas: pixel, gốc góc TRÊN-TRÁI, Y hướng xuống
//    G-code: mm,    gốc góc DƯỚI-TRÁI, Y hướng lên   (chuẩn máy GRBL)
//    => nhân MmPerPixel và lật trục Y.
//
//  Công suất: Vectoritem.LaserPower (0-100 %) -> S = MaxS * % / 100
//  Tốc độ   : Vectoritem.LaserSpeed (mm/phút) -> F
//  Số lần   : Vectoritem.Passes
// =====================================================================
namespace test01_1014
{
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.Text;

    public enum ImageDitherMode
    {
        FloydSteinberg = 0, // Tán xạ Floyd-Steinberg (Mịn - Khuyên dùng)
        Atkinson = 1,       // Tán xạ Atkinson (Tương phản cao - Nét)
        Stucki = 2,         // Tán xạ Stucki (Rất mịn)
        OrderedBayer = 3,   // Ma trận trật tự Bayer 4x4
        Grayscale = 4,      // Ảnh xám (PWM biến thiên công suất laser)
        Threshold = 5       // Đen trắng nhị phân theo ngưỡng
    }

    /// <summary>Thứ tự khắc / mô phỏng: tự tối ưu, theo tầng, hoặc theo lớp.</summary>
    public enum EngraveOrderMode
    {
        Auto = 0,   // Tự động tối ưu (lớp thấp → cao, ảnh trước, TSP, lỗ trong trước)
        Tier = 1,   // Theo TẦNG: tầng cao nhất khắc trước → thấp nhất (đúng thứ tự danh sách đối tượng)
        Layer = 2   // Theo LỚP: lớp cao nhất khắc trước → thấp nhất; trong lớp: tầng cao trước
    }

    public enum RasterScanDirection
    {
        Bidirectional = 0,  // Hai chiều (Zig-zag)
        Unidirectional = 1  // Một chiều (Chống răng cưa / mép phẳng)
    }

    public partial class GCodeSettings
    {
        /// <summary>1 pixel trên canvas = bao nhiêu mm. 0.25 => ô lưới 20 px = 5 mm.</summary>
        public float MmPerPixel { get; set; } = 0.25f;

        /// <summary>true: góc dưới-trái của cả bản vẽ là (0,0). false: dùng toạ độ tuyệt đối của canvas.</summary>
        public bool OriginAtJobCorner { get; set; } = true;

        /// <summary>Chiều cao canvas (px). Chỉ dùng khi OriginAtJobCorner = false.</summary>
        public float CanvasHeightPx { get; set; } = 600f;

        /// <summary>Giá trị S tối đa của firmware (GRBL mặc định $30=1000).</summary>
        public int MaxS { get; set; } = 1000;

        /// <summary>M4 = laser động (khuyên dùng cho GRBL), M3 = laser hằng.</summary>
        public string LaserOnCommand { get; set; } = "M4";

        /// <summary>Sai số khi xấp xỉ đường cong bằng đoạn thẳng (mm).</summary>
        public double CurveToleranceMm { get; set; } = 0.05;

        /// <summary>Khoảng cách giữa 2 dòng quét khi khắc ảnh (mm).</summary>
        public double RasterStepMm { get; set; } = 0.1;

        public bool ImageBinary { get; set; } = false;   // true: đen/trắng 1-bit
        public int ImageThreshold { get; set; } = 128;   // ngưỡng khi ImageBinary
        public bool ImageInvert { get; set; } = false;   // true: khắc phần sáng

        /// <summary>Thuật toán tạo điểm cho ảnh (Dithering / Grayscale / Threshold).</summary>
        public ImageDitherMode ImageDither { get; set; } = ImageDitherMode.FloydSteinberg;

        /// <summary>Hướng quét raster: 2 chiều (Zig-zag) hoặc 1 chiều (Unidirectional).</summary>
        public RasterScanDirection ImageScanDir { get; set; } = RasterScanDirection.Bidirectional;

        /// <summary>Quãng đường chạy đà ngoài biên ảnh (mm) để đầu laser đạt đủ tốc độ trước khi đốt, chống cháy mép.</summary>
        public double OverscanMm { get; set; } = 1.0;

        /// <summary>Tự động làm nét biên ảnh (Unsharp Mask) trước khi tạo điểm.</summary>
        public bool ImageSharpen { get; set; } = true;

        /// <summary>Độ tương phản ảnh (0.2 .. 3.0, chuẩn 1.0).</summary>
        public double ImageContrast { get; set; } = 1.0;

        /// <summary>Độ sáng ảnh (-128 .. 128, chuẩn 0).</summary>
        public double ImageBrightness { get; set; } = 0.0;

        /// <summary>Gamma ảnh (0.2 .. 3.0, chuẩn 1.0). Tăng gamma (> 1.0) để làm sáng các vùng tối và chi tiết chìm mà không cháy vùng sáng.</summary>
        public double ImageGamma { get; set; } = 1.0;

        /// <summary>Thông tin về tên và màu của từng lớp.</summary>
        public Dictionary<int, (string Name, Color Color)> LayerMeta { get; set; } = new();

        /// <summary>Chế độ thứ tự khắc (Auto / theo tầng / theo lớp).</summary>
        public EngraveOrderMode OrderMode { get; set; } = EngraveOrderMode.Auto;

        /// <summary>Khắc theo tầng (Order by Layer): chạy hết đối tượng của tầng này mới sang tầng tiếp theo.</summary>
        public bool OrderByLayer { get; set; } = true;

        /// <summary>Khắc ảnh trước, cắt viền sau (tránh vật liệu xê dịch khi cắt xong).</summary>
        public bool EngraveImagesFirst { get; set; } = true;

        /// <summary>Tối ưu hóa thứ tự đường cắt (TSP Nearest-Neighbor + 2-Opt + chọn đỉnh bắt đầu gần nhất) để giảm quãng đường G0.</summary>
        public bool OptimizeToolpath { get; set; } = true;

        /// <summary>Cắt lỗ bên trong trước, đường bao bên ngoài sau (Inside-Out) để vật cắt không bị rơi/lệch sớm.</summary>
        public bool CutInsideOut { get; set; } = true;

        public bool ReturnToOrigin { get; set; } = true;

        /// <summary>Tốc độ di chuyển nhanh giả định, chỉ dùng để ước tính thời gian.</summary>
        public int RapidFeedForEstimate { get; set; } = 3000;
    }

    public class GCodeResult
    {
        public string Code { get; set; } = "";
        public int LineCount { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double CutLengthMm { get; set; }     // tổng quãng đường G1
        public double RapidLengthMm { get; set; }   // tổng quãng đường G0
        public double EstimatedMinutes { get; set; }
    }

    public partial class GCodeCompiler
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly GCodeSettings _cfg;

        // ---- Job trung gian (vẫn ở toạ độ pixel) ----
        abstract class Job
        {
            public string Name = "";
            public int Power;     // giá trị S đã quy đổi
            public int Feed;
            public int Passes = 1;
            public int LayerId;
            public string LayerName = "Lớp 01";
            public Color LayerColor = Color.Black;
        }
        sealed class PathJob : Job
        {
            public List<List<PointF>> Paths = new();
            public int FillCount;            // > 0: CountFill đường đầu là đường TÔ, phần còn lại là đường VIỀN
        }
        sealed class RasterJob : Job
        {
            public RectangleF Rect;          // vị trí + kích thước (px)
            public int Cols, Rows;
            public int[] Pw = Array.Empty<int>();   // S cho từng pixel lưới
            public RasterScanDirection ScanDirection = RasterScanDirection.Bidirectional;
            public double OverscanMm = 1.0;
        }

        public GCodeCompiler(GCodeSettings? settings = null)
        {
            _cfg = settings ?? new GCodeSettings();
        }

        public GCodeSettings Settings => _cfg;

        // =================================================================
        //  HÀM CHÍNH
        // =================================================================
        public GCodeResult Compile(IEnumerable<Vectoritem> items)
        {
            // 1) Đổi từng Vectoritem thành Job
            var jobs = new List<Job>();
            foreach (var it in items)
            {
                Job? j = BuildJob(it);
                if (j != null) jobs.Add(j);
            }
            if (jobs.Count == 0)
                throw new InvalidOperationException("Không có đối tượng nào để xuất G-code.");

            // Thứ tự đối tượng đầu vào đã là TẦNG cao → thấp (Form1.ExportItems); OrderBy / GroupBy bên dưới đều ổn định
            if (_cfg.OrderMode == EngraveOrderMode.Auto && _cfg.EngraveImagesFirst)
                jobs = jobs.OrderBy(j => j is RasterJob ? 0 : 1).ToList();   // OrderBy ổn định

            // 2) Khung bao của toàn bộ bản vẽ (px)
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var job in jobs)
            {
                if (job is PathJob pj)
                {
                    foreach (var path in pj.Paths)
                        foreach (var p in path)
                        {
                            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                        }
                }
                else if (job is RasterJob rj)
                {
                    minX = Math.Min(minX, rj.Rect.Left); maxX = Math.Max(maxX, rj.Rect.Right);
                    minY = Math.Min(minY, rj.Rect.Top); maxY = Math.Max(maxY, rj.Rect.Bottom);
                }
            }

            // 3) Hàm đổi toạ độ canvas(px) -> máy(mm), có lật trục Y
            float k = _cfg.MmPerPixel;
            float originX = _cfg.OriginAtJobCorner ? minX : _cfg.AbsOriginXpx;
            float originY = _cfg.OriginAtJobCorner ? maxY
                          : (float.IsNaN(_cfg.AbsOriginYpx) ? _cfg.CanvasHeightPx : _cfg.AbsOriginYpx);
            double MX(float px) => (px - originX) * k;
            double MY(float py) => (originY - py) * k;

            // 3b) Thứ tự khắc theo chế độ đã chọn
            if (_cfg.OrderMode == EngraveOrderMode.Tier)
            {
                // Giữ nguyên thứ tự tầng cao → thấp, không xếp lại gì cả
            }
            else if (_cfg.OrderMode == EngraveOrderMode.Layer || _cfg.OrderByLayer)
            {
                var layerOrdered = new List<Job>();
                var layerGroups = _cfg.OrderMode == EngraveOrderMode.Layer
                    ? jobs.GroupBy(j => j.LayerId).OrderByDescending(g => g.Key)     // lớp cao nhất trước
                    : jobs.GroupBy(j => j.LayerId).OrderBy(g => g.Key);              // Auto: lớp thấp trước
                foreach (var grp in layerGroups)
                {
                    var lJobs = grp.ToList();
                    if (_cfg.OrderMode == EngraveOrderMode.Auto && _cfg.EngraveImagesFirst)
                        lJobs = lJobs.OrderBy(j => j is RasterJob ? 0 : 1).ToList();
                    if (_cfg.OptimizeToolpath || _cfg.CutInsideOut)
                        lJobs = OptimizeJobSequence(lJobs, originX, originY);
                    layerOrdered.AddRange(lJobs);
                }
                jobs = layerOrdered;
            }
            else
            {
                if (_cfg.EngraveImagesFirst)
                    jobs = jobs.OrderBy(j => j is RasterJob ? 0 : 1).ToList();
                if (_cfg.OptimizeToolpath || _cfg.CutInsideOut)
                    jobs = OptimizeJobSequence(jobs, originX, originY);
            }

            // 4) Phát sinh G-code
            var sb = new StringBuilder();
            int lines = 0;
            double cx = 0, cy = 0, cutLen = 0, rapidLen = 0, minutes = 0;

            void Line(string s) { sb.AppendLine(s); lines++; }
            void Rapid(double x, double y)
            {
                rapidLen += Dist(cx, cy, x, y);
                cx = x; cy = y;
                Line($"G0 X{F(x)} Y{F(y)}");
            }
            void CutXY(double x, double y, int feed, string extra)
            {
                double d = Dist(cx, cy, x, y);
                cutLen += d; minutes += d / Math.Max(1, feed);
                cx = x; cy = y;
                Line($"G1 X{F(x)} Y{F(y)}{extra}");
            }
            void CutX(double x, int s, int feed)
            {
                double d = Math.Abs(x - cx);
                cutLen += d; minutes += d / Math.Max(1, feed);
                cx = x;
                Line($"G1 X{F(x)} S{s}");
            }

            double widthMm = (maxX - minX) * k, heightMm = (maxY - minY) * k;
            Line($"; test01_1014 G-code | {jobs.Count} job | {F(widthMm)} x {F(heightMm)} mm");
            Line("G21 G90");
            Line($"{_cfg.LaserOnCommand} S0");

            int currentLayer = -1;
            int idx = 0;
            foreach (var job in jobs)
            {
                idx++;
                if (job.LayerId != currentLayer)
                {
                    if (currentLayer != -1)
                    {
                        Line($"; LAYER_END: {currentLayer}");
                        Line("");
                    }
                    currentLayer = job.LayerId;
                    string colHex = $"#{job.LayerColor.R:X2}{job.LayerColor.G:X2}{job.LayerColor.B:X2}";
                    Line("; ========================================================");
                    Line($"; TẦNG: {job.LayerName} (Màu: {colHex})");
                    Line("; ========================================================");
                    Line($"; LAYER_START: {job.LayerId} | {job.LayerName} | {colHex}");
                }

                Line($"; [{idx}] {job.Name} | S{job.Power} F{job.Feed} x{job.Passes}");
                Line($"F{job.Feed}");

                for (int pass = 1; pass <= job.Passes; pass++)
                {
                    if (job.Passes > 1) Line($"; pass {pass}/{job.Passes}");

                    if (job is PathJob pj)
                    {
                        int pIdx = -1;
                        foreach (var path in pj.Paths)
                        {
                            pIdx++;
                            // nhãn để bản xem trước G-code tô màu riêng cho đường viền (Tô kín + viền)
                            if (pj.FillCount > 0 && pIdx == 0) Line("; fill");
                            else if (pj.FillCount > 0 && pIdx == pj.FillCount) Line("; outline");
                            if (path.Count < 2) continue;
                            Rapid(MX(path[0].X), MY(path[0].Y));
                            for (int i = 1; i < path.Count; i++)
                                CutXY(MX(path[i].X), MY(path[i].Y), job.Feed,
                                      i == 1 ? $" S{job.Power}" : "");
                            Line("S0");
                        }
                    }
                    else if (job is RasterJob rj)
                    {
                        double x0 = MX(rj.Rect.Left);
                        double yTop = MY(rj.Rect.Top);
                        double stepX = rj.Rect.Width * k / rj.Cols;
                        double stepY = rj.Rect.Height * k / rj.Rows;
                        double ov = Math.Max(0.0, rj.OverscanMm);
                        bool isUni = rj.ScanDirection == RasterScanDirection.Unidirectional;
                        bool ltr = true;

                        for (int r = 0; r < rj.Rows; r++)
                        {
                            int lo = -1, hi = -1, rowOff = r * rj.Cols;
                            for (int c = 0; c < rj.Cols; c++)
                                if (rj.Pw[rowOff + c] > 0) { if (lo < 0) lo = c; hi = c; }
                            if (lo < 0) continue;                       // dòng trống -> bỏ qua

                            double y = yTop - (r + 0.5) * stepY;
                            double xStart = x0 + lo * stepX;
                            double xEnd = x0 + (hi + 1) * stepX;

                            if (ltr || isUni)
                            {
                                // Quét từ Trái -> Phải
                                if (ov > 0.0)
                                {
                                    Rapid(xStart - ov, y);
                                    CutX(xStart, 0, job.Feed); // chạy đà đạt tốc độ F với laser tắt (S0)
                                }
                                else
                                {
                                    Rapid(xStart, y);
                                }

                                int cur = rj.Pw[rowOff + lo];
                                for (int c = lo + 1; c <= hi + 1; c++)
                                {
                                    int v = c <= hi ? rj.Pw[rowOff + c] : -1;
                                    if (v != cur)
                                    {
                                        CutX(x0 + c * stepX, cur, job.Feed);
                                        cur = v;
                                    }
                                }

                                if (ov > 0.0)
                                {
                                    CutX(xEnd + ov, 0, job.Feed); // chạy đà thoát ngoài biên với laser tắt (S0)
                                }
                            }
                            else
                            {
                                // Quét từ Phải -> Trái (chỉ khi Bidirectional)
                                if (ov > 0.0)
                                {
                                    Rapid(xEnd + ov, y);
                                    CutX(xEnd, 0, job.Feed); // chạy đà đạt tốc độ F với laser tắt (S0)
                                }
                                else
                                {
                                    Rapid(xEnd, y);
                                }

                                int cur = rj.Pw[rowOff + hi];
                                for (int c = hi - 1; c >= lo - 1; c--)
                                {
                                    int v = c >= lo ? rj.Pw[rowOff + c] : -1;
                                    if (v != cur)
                                    {
                                        CutX(x0 + (c + 1) * stepX, cur, job.Feed);
                                        cur = v;
                                    }
                                }

                                if (ov > 0.0)
                                {
                                    CutX(xStart - ov, 0, job.Feed); // chạy đà thoát ngoài biên với laser tắt (S0)
                                }
                            }

                            Line("S0");
                            if (!isUni) ltr = !ltr;
                        }
                    }
                }
            }

            if (currentLayer != -1)
            {
                Line($"; LAYER_END: {currentLayer}");
            }

            Line("M5");
            if (_cfg.ReturnToOrigin) Rapid(0, 0);

            minutes += rapidLen / Math.Max(1, _cfg.RapidFeedForEstimate);

            return new GCodeResult
            {
                Code = sb.ToString(),
                LineCount = lines,
                WidthMm = widthMm,
                HeightMm = heightMm,
                CutLengthMm = cutLen,
                RapidLengthMm = rapidLen,
                EstimatedMinutes = minutes
            };
        }

        // =================================================================
        //  TỐI ƯU HÓA ĐƯỜNG CHẠY DAO / LASER (TSP + INSIDE-OUT)
        // =================================================================
        private List<Job> OptimizeJobSequence(List<Job> jobs, float startX, float startY)
        {
            if (jobs.Count == 0) return jobs;

            var result = new List<Job>(jobs.Count);
            PointF head = new PointF(startX, startY);

            int i = 0;
            while (i < jobs.Count)
            {
                if (jobs[i] is RasterJob)
                {
                    // Nhóm các RasterJob liên tiếp
                    var rGroup = new List<RasterJob>();
                    while (i < jobs.Count && jobs[i] is RasterJob rj)
                    {
                        rGroup.Add(rj);
                        i++;
                    }
                    if (_cfg.OptimizeToolpath && rGroup.Count > 1)
                    {
                        while (rGroup.Count > 0)
                        {
                            int best = 0;
                            float bestD = float.MaxValue;
                            for (int k = 0; k < rGroup.Count; k++)
                            {
                                var rc = rGroup[k].Rect;
                                float d = DistSq(head, new PointF(rc.Left, rc.Top));
                                if (d < bestD) { bestD = d; best = k; }
                            }
                            var pick = rGroup[best];
                            rGroup.RemoveAt(best);
                            result.Add(pick);
                            head = new PointF(pick.Rect.Right, pick.Rect.Bottom);
                        }
                    }
                    else
                    {
                        foreach (var rj in rGroup)
                        {
                            result.Add(rj);
                            head = new PointF(rj.Rect.Right, rj.Rect.Bottom);
                        }
                    }
                    continue;
                }

                // Nhóm các PathJob liên tiếp có cùng thông số (Power, Feed, Passes) để tối ưu thứ tự cắt
                var pGroup = new List<PathJob>();
                var firstPj = (PathJob)jobs[i];
                while (i < jobs.Count && jobs[i] is PathJob pj &&
                       pj.Power == firstPj.Power && pj.Feed == firstPj.Feed && pj.Passes == firstPj.Passes)
                {
                    pGroup.Add(pj);
                    i++;
                }

                // Tách các PathJob có tô kín (FillCount > 0) chạy trước các PathJob chỉ cắt viền
                var fillJobs = pGroup.Where(p => p.FillCount > 0 && p.FillCount >= p.Paths.Count).ToList();
                var fillAndOutlineJobs = pGroup.Where(p => p.FillCount > 0 && p.FillCount < p.Paths.Count).ToList();
                var cutOnlyJobs = pGroup.Where(p => p.FillCount == 0).ToList();

                // 1) Các job chỉ tô kín: sắp xếp Nearest-Neighbor
                OrderPathJobsOnly(fillJobs, ref head, allowInsideOut: false);
                result.AddRange(fillJobs);

                // 2) Các job vừa tô kín vừa cắt viền: tối ưu phần đường viền bên trong từng job
                OrderPathJobsOnly(fillAndOutlineJobs, ref head, allowInsideOut: _cfg.CutInsideOut);
                result.AddRange(fillAndOutlineJobs);

                // 3) Các job chỉ cắt viền: sắp xếp thứ tự giữa các đối tượng (Inside-Out + Nearest-Neighbor + 2-Opt)
                //    và tối ưu các đường con bên trong từng đối tượng
                OrderPathJobsOnly(cutOnlyJobs, ref head, allowInsideOut: _cfg.CutInsideOut);
                result.AddRange(cutOnlyJobs);
            }

            return result;
        }

        private void OrderPathJobsOnly(List<PathJob> pJobs, ref PointF head, bool allowInsideOut)
        {
            if (pJobs.Count == 0) return;

            // Tính khung bao và đường bao ngoài đại diện cho từng PathJob để kiểm tra lồng nhau (Inside-Out giữa các đối tượng rời)
            int m = pJobs.Count;
            var bounds = new RectangleF[m];
            var outerPolys = new List<PointF>?[m];

            for (int i = 0; i < m; i++)
            {
                bounds[i] = GetJobBounds(pJobs[i]);
                outerPolys[i] = GetLargestClosedLoop(pJobs[i]);
            }

            var jobDepth = new int[m];
            if (allowInsideOut && m > 1)
            {
                for (int i = 0; i < m; i++)
                {
                    if (pJobs[i].Paths.Count == 0 || pJobs[i].Paths[0].Count == 0) continue;
                    PointF samplePt = pJobs[i].Paths[0][0];
                    int depth = 0;
                    for (int j = 0; j < m; j++)
                    {
                        if (i == j || outerPolys[j] == null) continue;
                        if (!bounds[j].Contains(bounds[i])) continue;
                        if (IsPointInPolygon(samplePt, outerPolys[j]!))
                            depth++;
                    }
                    jobDepth[i] = depth;
                }
            }

            // Nhóm theo từng mức độ sâu (từ sâu nhất = bên trong nhất ra ngoài cùng = 0)
            var orderedJobs = new List<PathJob>(m);
            int maxD = m > 0 ? jobDepth.Max() : 0;

            for (int d = maxD; d >= 0; d--)
            {
                var bucket = new List<PathJob>();
                for (int i = 0; i < m; i++)
                {
                    if (jobDepth[i] == d) bucket.Add(pJobs[i]);
                }
                if (bucket.Count == 0) continue;

                if (_cfg.OptimizeToolpath)
                {
                    while (bucket.Count > 0)
                    {
                        int bestIdx = 0;
                        float bestDist = float.MaxValue;
                        for (int k = 0; k < bucket.Count; k++)
                        {
                            // Tối ưu thử đường nội bộ của job hoặc lấy khoảng cách gần nhất tới điểm trong job
                            float dist = MinDistToJob(head, bucket[k]);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestIdx = k;
                            }
                        }
                        var chosen = bucket[bestIdx];
                        bucket.RemoveAt(bestIdx);
                        OptimizeSubPathsInsideJob(chosen, ref head, allowInsideOut);
                        orderedJobs.Add(chosen);
                    }
                }
                else
                {
                    foreach (var pj in bucket)
                    {
                        OptimizeSubPathsInsideJob(pj, ref head, allowInsideOut);
                        orderedJobs.Add(pj);
                    }
                }
            }

            pJobs.Clear();
            pJobs.AddRange(orderedJobs);
        }

        private void OptimizeSubPathsInsideJob(PathJob pj, ref PointF head, bool allowInsideOut)
        {
            if (pj.Paths.Count == 0) return;

            // Giữ nguyên các đường tô kín (0 .. FillCount - 1) ở đầu, nhưng vẫn cập nhật vị trí head
            int startOutline = Math.Clamp(pj.FillCount, 0, pj.Paths.Count);
            for (int i = 0; i < startOutline; i++)
            {
                var fp = pj.Paths[i];
                if (fp.Count > 0) head = fp[^1];
            }

            int outlineCount = pj.Paths.Count - startOutline;
            if (outlineCount <= 0) return;

            var outlines = pj.Paths.GetRange(startOutline, outlineCount)
                                   .Where(p => p.Count >= 2)
                                   .ToList();
            if (outlines.Count == 0) return;

            // Tính độ sâu lồng nhau (Inside-Out) cho các đường cắt viền trong cùng PathJob
            int n = outlines.Count;
            var isClosed = new bool[n];
            var pBounds = new RectangleF[n];
            for (int i = 0; i < n; i++)
            {
                var p = outlines[i];
                isClosed[i] = p.Count >= 3 && DistSq(p[0], p[^1]) <= 0.25f;
                pBounds[i] = GetPathBounds(p);
            }

            var depth = new int[n];
            if (allowInsideOut && n > 1)
            {
                for (int i = 0; i < n; i++)
                {
                    PointF testPt = outlines[i][0];
                    int d = 0;
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j || !isClosed[j]) continue;
                        if (!pBounds[j].Contains(pBounds[i])) continue;
                        if (IsPointInPolygon(testPt, outlines[j]))
                            d++;
                    }
                    depth[i] = d;
                }
            }

            var finalOutlines = new List<List<PointF>>(n);
            int maxDepth = depth.Max();

            for (int d = maxDepth; d >= 0; d--)
            {
                var indices = new List<int>();
                for (int i = 0; i < n; i++)
                    if (depth[i] == d) indices.Add(i);
                if (indices.Count == 0) continue;

                if (!_cfg.OptimizeToolpath)
                {
                    foreach (int idx in indices)
                    {
                        finalOutlines.Add(outlines[idx]);
                        head = outlines[idx][^1];
                    }
                    continue;
                }

                // Greedy Nearest-Neighbor kết hợp chọn đỉnh bắt đầu gần nhất (cho đường kín) hoặc đảo chiều (cho đường hở)
                var levelPaths = new List<(List<PointF> Path, bool Closed)>(indices.Count);
                var used = new bool[indices.Count];

                for (int step = 0; step < indices.Count; step++)
                {
                    int bestLocal = -1;
                    float bestDistSq = float.MaxValue;
                    bool bestReverse = false;
                    int bestStartVertex = 0;

                    for (int k = 0; k < indices.Count; k++)
                    {
                        if (used[k]) continue;
                        int origIdx = indices[k];
                        var path = outlines[origIdx];
                        bool closed = isClosed[origIdx];

                        if (closed)
                        {
                            // Có thể bắt đầu cắt tại bất kỳ đỉnh nào trên đường khép kín gần đầu laser nhất
                            int vCount = path.Count - 1;
                            for (int v = 0; v < vCount; v++)
                            {
                                float dSq = DistSq(head, path[v]);
                                if (dSq < bestDistSq)
                                {
                                    bestDistSq = dSq;
                                    bestLocal = k;
                                    bestStartVertex = v;
                                    bestReverse = false;
                                }
                            }
                        }
                        else
                        {
                            // Đường hở: kiểm tra cả điểm đầu lẫn điểm cuối (cho phép đảo chiều cắt)
                            float dStart = DistSq(head, path[0]);
                            if (dStart < bestDistSq)
                            {
                                bestDistSq = dStart;
                                bestLocal = k;
                                bestReverse = false;
                            }
                            float dEnd = DistSq(head, path[^1]);
                            if (dEnd < bestDistSq)
                            {
                                bestDistSq = dEnd;
                                bestLocal = k;
                                bestReverse = true;
                            }
                        }
                    }

                    if (bestLocal < 0) break;
                    used[bestLocal] = true;
                    int chosenOrig = indices[bestLocal];
                    var chosenPath = outlines[chosenOrig];
                    bool chosenClosed = isClosed[chosenOrig];

                    if (chosenClosed && bestStartVertex > 0)
                    {
                        chosenPath = RotateClosedLoop(chosenPath, bestStartVertex);
                    }
                    else if (!chosenClosed && bestReverse)
                    {
                        chosenPath = new List<PointF>(chosenPath);
                        chosenPath.Reverse();
                    }

                    levelPaths.Add((chosenPath, chosenClosed));
                    head = chosenPath[^1];
                }

                // Tối ưu 2-Opt cục bộ để gỡ các đoạn nhảy chéo G0
                if (levelPaths.Count >= 4 && levelPaths.Count <= 250)
                {
                    TwoOptRefine(levelPaths);
                    if (levelPaths.Count > 0)
                        head = levelPaths[^1].Path[^1];
                }

                foreach (var lp in levelPaths)
                    finalOutlines.Add(lp.Path);
            }

            // Gán lại danh sách đường đã tối ưu vào PathJob
            var newPaths = new List<List<PointF>>(startOutline + finalOutlines.Count);
            for (int i = 0; i < startOutline; i++) newPaths.Add(pj.Paths[i]);
            newPaths.AddRange(finalOutlines);
            pj.Paths = newPaths;
        }

        private static void TwoOptRefine(List<(List<PointF> Path, bool Closed)> tour)
        {
            int n = tour.Count;
            bool improved = true;
            int passes = 0;

            while (improved && passes < 4)
            {
                improved = false;
                passes++;
                for (int i = 0; i < n - 2; i++)
                {
                    PointF aEnd = tour[i].Path[^1];
                    PointF bStart = tour[i + 1].Path[0];
                    float cur1 = DistSq(aEnd, bStart);

                    for (int j = i + 2; j < n - 1; j++)
                    {
                        PointF cEnd = tour[j].Path[^1];
                        PointF dStart = tour[j + 1].Path[0];
                        float curCost = cur1 + DistSq(cEnd, dStart);

                        // Nếu đảo đoạn từ (i+1 .. j), điểm vào mới là điểm cuối của tour[j] và điểm ra là điểm đầu của tour[i+1]
                        PointF newB = tour[j].Path[^1];
                        PointF newC = tour[i + 1].Path[0];
                        float newCost = DistSq(aEnd, newB) + DistSq(newC, dStart);

                        if (newCost + 1e-3f < curCost)
                        {
                            // Đảo ngược thứ tự từ i+1 đến j và đảo chiều từng path bên trong
                            tour.Reverse(i + 1, j - i);
                            for (int k = i + 1; k <= j; k++)
                            {
                                var rev = new List<PointF>(tour[k].Path);
                                rev.Reverse();
                                tour[k] = (rev, tour[k].Closed);
                            }
                            improved = true;
                            aEnd = tour[i].Path[^1];
                            bStart = tour[i + 1].Path[0];
                            cur1 = DistSq(aEnd, bStart);
                        }
                    }
                }
            }
        }

        private static List<PointF> RotateClosedLoop(List<PointF> loop, int startIdx)
        {
            int vCount = loop.Count - 1; // phần tử cuối trùng phần tử đầu
            if (vCount < 3 || startIdx <= 0 || startIdx >= vCount) return loop;

            var rotated = new List<PointF>(loop.Count);
            for (int i = 0; i < vCount; i++)
            {
                rotated.Add(loop[(startIdx + i) % vCount]);
            }
            rotated.Add(rotated[0]);
            return rotated;
        }

        private static float MinDistToJob(PointF pt, PathJob pj)
        {
            int start = Math.Clamp(pj.FillCount, 0, pj.Paths.Count);
            float best = float.MaxValue;
            for (int i = start; i < pj.Paths.Count; i++)
            {
                var p = pj.Paths[i];
                if (p.Count == 0) continue;
                float d0 = DistSq(pt, p[0]);
                if (d0 < best) best = d0;
                float d1 = DistSq(pt, p[^1]);
                if (d1 < best) best = d1;
            }
            if (best == float.MaxValue && pj.Paths.Count > 0 && pj.Paths[0].Count > 0)
                best = DistSq(pt, pj.Paths[0][0]);
            return best;
        }

        private static RectangleF GetJobBounds(PathJob pj)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            foreach (var path in pj.Paths)
            {
                foreach (var p in path)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    any = true;
                }
            }
            return any ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;
        }

        private static RectangleF GetPathBounds(List<PointF> path)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in path)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        private static List<PointF>? GetLargestClosedLoop(PathJob pj)
        {
            List<PointF>? best = null;
            double bestArea = 0;
            int start = Math.Clamp(pj.FillCount, 0, pj.Paths.Count);
            for (int i = start; i < pj.Paths.Count; i++)
            {
                var p = pj.Paths[i];
                if (p.Count < 4 || DistSq(p[0], p[^1]) > 0.25f) continue;
                var b = GetPathBounds(p);
                double area = (double)b.Width * b.Height;
                if (area > bestArea) { bestArea = area; best = p; }
            }
            return best;
        }

        private static bool IsPointInPolygon(PointF pt, List<PointF> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                PointF pi = poly[i], pj = poly[j];
                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y + 1e-12f) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float DistSq(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        // =================================================================
        //  Vectoritem -> Job
        // =================================================================
        Job? BuildJob(Vectoritem it, bool withFill = true)
        {
            int power = Math.Clamp(it.LaserPower, 0, 100);
            int s = (int)Math.Round(_cfg.MaxS * power / 100.0);
            if (s <= 0) return null;                       // công suất 0% -> bỏ qua

            Job? job = it switch
            {
                VectorRectangle r => RectJob(r),
                VectorEllipse e => EllipseJob(e),
                VectorLine l => LineJob(l),
                VectorPolygon pg => PolygonJob(pg),
                VectorPathItem pi => PathItemJob(pi),
                VectorText t => TextJob(t),
                VectorImage im => RasterJobFrom(im, s),
                _ => null
            };
            if (job == null) return null;
            if ((it.Rotation != 0f || it.FlipX || it.FlipY) && job is PathJob pj && it is not VectorLine) RotatePaths(pj, it);   // hình đã xoay / lật
            // Tô kín: đường quét song song lấp đầy hình (tính SAU khi xoay nên góc quét là góc tuyệt đối trên bản vẽ; bỏ qua với font 1 nét)
            if (withFill && it.FillMode != 0 && it is not VectorLine && it is not VectorText { SingleLineFont: true } && job is PathJob fj) ApplyFill(fj, it);

            job.Power = s;
            job.Feed = Math.Max(1, it.LaserSpeed);
            job.Passes = Math.Max(1, it.Passes);
            int lid = it.LayerId < 0 ? 0 : it.LayerId;
            job.LayerId = lid;
            if (_cfg.LayerMeta.TryGetValue(lid, out var meta))
            {
                job.LayerName = meta.Name;
                job.LayerColor = meta.Color;
            }
            else
            {
                job.LayerName = $"Lớp {lid + 1:00}";
                job.LayerColor = LayerColors.Of(lid);
            }
            return job;
        }

        // Xoay mọi điểm của đường cắt quanh tâm hình (cùng chiều với Form1: thuận chiều kim đồng hồ trên màn hình)
        static void RotatePaths(PathJob job, Vectoritem it)
        {
            var b = Box(it);
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            double a = it.Rotation * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            foreach (var path in job.Paths)
                for (int i = 0; i < path.Count; i++)
                {
                    double dx = path[i].X - cx, dy = path[i].Y - cy;
                    if (it.FlipX) dx = -dx;                  // lật trước, xoay sau (giống cách Form1 vẽ)
                    if (it.FlipY) dy = -dy;
                    path[i] = new PointF((float)(cx + dx * cos - dy * sin), (float)(cy + dx * sin + dy * cos));
                }
        }

        // Khung bao của hình chữ nhật b sau khi xoay deg độ quanh tâm
        static RectangleF RotatedBounds(RectangleF b, float deg)
        {
            if (deg == 0f) return b;
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            double a = deg * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var (px, py) in new[] { (b.Left, b.Top), (b.Right, b.Top), (b.Right, b.Bottom), (b.Left, b.Bottom) })
            {
                double dx = px - cx, dy = py - cy;
                float x = (float)(cx + dx * cos - dy * sin), y = (float)(cy + dx * sin + dy * cos);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        static RectangleF Box(Vectoritem v)
        {
            return new RectangleF(Math.Min(v.X, v.X + v.Width), Math.Min(v.Y, v.Y + v.Height),
                                  Math.Abs(v.Width), Math.Abs(v.Height));
        }

        PathJob RectJob(VectorRectangle r)
        {
            var b = Box(r);
            var job = new PathJob { Name = "Rectangle" };
            job.Paths.Add(new List<PointF>
            {
                new(b.Left, b.Top), new(b.Right, b.Top), new(b.Right, b.Bottom),
                new(b.Left, b.Bottom), new(b.Left, b.Top)
            });
            return job;
        }

        PathJob EllipseJob(VectorEllipse e)
        {
            var b = Box(e);
            float rx = b.Width / 2f, ry = b.Height / 2f;
            float cx = b.Left + rx, cy = b.Top + ry;

            // Số đoạn thẳng đủ để sai số hình học <= CurveToleranceMm
            double rMm = Math.Max(rx, ry) * _cfg.MmPerPixel;
            double tol = Math.Max(0.001, _cfg.CurveToleranceMm);
            int n = rMm <= tol ? 16 : (int)Math.Ceiling(Math.PI / Math.Acos(1 - tol / rMm));
            n = Math.Clamp(n, 16, 1440);

            var pts = new List<PointF>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(new PointF(cx + (float)(rx * Math.Cos(a)), cy + (float)(ry * Math.Sin(a))));
            }
            var job = new PathJob { Name = "Ellipse" };
            job.Paths.Add(pts);
            return job;
        }
        PathJob PolygonJob(VectorPolygon p)
        {
            var pts = new List<PointF>(p.GetPoints());
            pts.Add(pts[0]);                          // khép kín đường viền
            var job = new PathJob { Name = p.Name };
            job.Paths.Add(pts);
            return job;
        }

        PathJob PathItemJob(VectorPathItem pi)
        {
            var job = new PathJob { Name = pi.Name };
            foreach (var sp in pi.SubPaths)
            {
                if (sp.Points.Count < 2) continue;
                var list = new List<PointF>(sp.Points);
                if (sp.IsClosed && list.Count >= 3)
                {
                    list.Add(list[0]); // khép kín đường cắt
                }
                job.Paths.Add(list);
            }
            return job;
        }

        PathJob LineJob(VectorLine l)
        {
            var job = new PathJob { Name = "Line" };
            job.Paths.Add(new List<PointF> { new(l.X, l.Y), new(l.X2, l.Y2) });
            return job;
        }

        PathJob? TextJob(VectorText t)
        {
            if (string.IsNullOrEmpty(t.TextContent)) return null;

            float flat = (float)(Math.Max(0.001, _cfg.CurveToleranceMm) / _cfg.MmPerPixel);

            if (t.HasCustomPath)
            {
                var subPaths = HersheyFont.BuildTextSubPaths(t, _cfg.MmPerPixel, flat);
                if (subPaths.Count == 0) return null;
                var customJob = new PathJob { Name = "Text '" + Ascii(t.TextContent) + "'" };
                foreach (var sp in subPaths)
                {
                    if (sp.Points.Count < 2) continue;
                    var list = new List<PointF>(sp.Points);
                    if (sp.IsClosed && list.Count >= 3 && list[0] != list[^1])
                        list.Add(list[0]);
                    customJob.Paths.Add(list);
                }
                return customJob.Paths.Count == 0 ? null : customJob;
            }

            FontFamily fam;
            try { fam = new FontFamily(t.FontName); }
            catch { fam = new FontFamily("Arial"); }

            using (fam)
            using (var gp = new GraphicsPath())
            {
                FontStyle want = (t.FontBold ? FontStyle.Bold : 0) | (t.FontItalic ? FontStyle.Italic : 0);
                FontStyle style = fam.IsStyleAvailable(want) ? want
                                : fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                                : fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                                : FontStyle.Italic;

                // Vectoritem.Draw dùng new Font(name, size) -> size tính bằng POINT ở 96 DPI
                float emPx = t.FontSize * 96f / 72f;
                gp.AddString(t.TextContent, fam, (int)style, emPx,
                             new PointF(t.X, t.Y), new StringFormat(StringFormat.GenericDefault));

                using (var m = new Matrix()) gp.Flatten(m, flat);

                var pts = gp.PathPoints;
                var job = new PathJob { Name = "Text '" + Ascii(t.TextContent) + "'" };

                using (var iter = new GraphicsPathIterator(gp))
                {
                    while (iter.NextSubpath(out int start, out int end, out bool closed) > 0)
                    {
                        var list = new List<PointF>();
                        for (int i = start; i <= end; i++) list.Add(pts[i]);
                        if (closed && list.Count > 0) list.Add(list[0]);
                        job.Paths.Add(list);
                    }
                }
                return job.Paths.Count == 0 ? null : job;
            }
        }

        // =================================================================
        //  Tô kín (Fill / Hatch)
        // =================================================================
        // Thay đường viền bằng các đường quét song song (quy tắc chẵn-lẻ nên lỗ trong chữ O, B... vẫn để trống)
        void ApplyFill(PathJob job, Vectoritem it)
        {
            var closed = job.Paths.Where(p => p.Count >= 3).ToList();
            if (closed.Count == 0) return;

            float stepPx = (float)(Math.Max(0.02, it.FillIntervalMm) / _cfg.MmPerPixel);
            var fill = new List<List<PointF>>();
            HatchPass(closed, it.FillAngle, stepPx, it.FillBidir, fill);
            if (it.FillCross) HatchPass(closed, it.FillAngle + 90f, stepPx, it.FillBidir, fill);
            if (fill.Count == 0) return;                          // hình nhỏ hơn khoảng cách tô: giữ nguyên đường viền
            job.FillCount = fill.Count;
            if (it.FillMode == 2) fill.AddRange(job.Paths);       // tô trước, viền sau
            job.Paths = fill;
        }

        static void HatchPass(List<List<PointF>> polys, float angleDeg, float stepPx, bool bidir, List<List<PointF>> output)
        {
            double a = angleDeg * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);

            // xoay hình một góc -a để các đường quét trở thành đường NGANG
            var rot = new List<(double X, double Y)[]>();
            double yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var p in polys)
            {
                var arr = new (double X, double Y)[p.Count];
                for (int i = 0; i < p.Count; i++)
                {
                    double x = p[i].X * ca + p[i].Y * sa;
                    double y = -p[i].X * sa + p[i].Y * ca;
                    arr[i] = (x, y);
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
                rot.Add(arr);
            }
            if (yMax <= yMin) return;

            double step = Math.Max(1e-4, stepPx);
            long rows = (long)Math.Ceiling((yMax - yMin) / step);
            if (rows > 400000)
                throw new InvalidOperationException("Khoảng cách đường tô quá nhỏ so với kích thước hình (hơn 400.000 đường). Hãy tăng khoảng cách tô.");

            var xs = new List<double>();
            var segs = new List<(double A, double B)>();
            int rowNo = 0;
            for (long r = 0; r < rows; r++)
            {
                double y = yMin + (r + 0.5) * step;
                xs.Clear();
                foreach (var arr in rot)
                {
                    int n = arr.Length;
                    for (int i = 0; i < n; i++)
                    {
                        var p0 = arr[i];
                        var p1 = arr[(i + 1) % n];
                        if ((p0.Y <= y && p1.Y > y) || (p1.Y <= y && p0.Y > y))
                            xs.Add(p0.X + (y - p0.Y) * (p1.X - p0.X) / (p1.Y - p0.Y));
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                segs.Clear();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    if (xs[k + 1] - xs[k] > 1e-6) segs.Add((xs[k], xs[k + 1]));
                if (segs.Count == 0) continue;

                bool rev = bidir && (rowNo & 1) == 1;               // hàng lẻ chạy ngược lại (zigzag)
                if (rev) for (int k = segs.Count - 1; k >= 0; k--) output.Add(HSeg(segs[k].B, segs[k].A, y, ca, sa));
                else foreach (var s in segs) output.Add(HSeg(s.A, s.B, y, ca, sa));
                rowNo++;
            }
        }

        // đoạn quét (xa -> xb tại tung độ y trong hệ đã xoay) đổi ngược về hệ bản vẽ
        static List<PointF> HSeg(double xa, double xb, double y, double ca, double sa) => new List<PointF>
        {
            new PointF((float)(xa * ca - y * sa), (float)(xa * sa + y * ca)),
            new PointF((float)(xb * ca - y * sa), (float)(xb * sa + y * ca))
        };

        RasterJob? RasterJobFrom(VectorImage vi, int maxPower)
        {
            if (vi.LoadedImage == null) return null;
            var b = Box(vi);
            if (b.Width <= 0 || b.Height <= 0) return null;

            // Ảnh đã xoay / lật: khắc theo khung bao SAU khi xoay (ảnh được vẽ xoay vào lưới điểm ảnh)
            bool xf = vi.Rotation != 0f || vi.FlipX || vi.FlipY;
            RectangleF area = xf ? RotatedBounds(b, vi.Rotation) : b;

            double step = Math.Max(0.02, _cfg.RasterStepMm);
            int cols = Math.Max(1, (int)Math.Round(area.Width * _cfg.MmPerPixel / step));
            int rows = Math.Max(1, (int)Math.Round(area.Height * _cfg.MmPerPixel / step));
            if ((long)cols * rows > 20_000_000)
                throw new InvalidOperationException("Ảnh quá lớn so với bước quét. Hãy tăng RasterStepMm.");

            // Chế độ khắc riêng của từng ảnh (0 = theo cài đặt chung)
            ImageDitherMode mode = _cfg.ImageDither;
            if (vi.EngraveMode == 1) mode = ImageDitherMode.Grayscale;
            else if (vi.EngraveMode == 2) mode = ImageDitherMode.Threshold;

            int engThr = vi.EngraveMode == 0 ? _cfg.ImageThreshold : vi.Threshold;
            bool engInv = vi.EngraveMode == 0 ? _cfg.ImageInvert : vi.Invert;

            int[] pw;
            using (var bmp = new Bitmap(cols, rows, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    // vùng trong suốt = trắng = không khắc. Ảnh xoay + đảo màu: nền ngoài ảnh phải là đen (sau đảo = không khắc)
                    g.Clear(xf && engInv ? Color.Black : Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    if (!xf) g.DrawImage(vi.LoadedImage, 0, 0, cols, rows);
                    else
                    {
                        float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
                        g.ScaleTransform(cols / area.Width, rows / area.Height);
                        g.TranslateTransform(-area.Left, -area.Top);
                        g.TranslateTransform(cx, cy);
                        if (vi.Rotation != 0f) g.RotateTransform(vi.Rotation);
                        g.ScaleTransform(vi.FlipX ? -1f : 1f, vi.FlipY ? -1f : 1f);
                        g.TranslateTransform(-cx, -cy);
                        g.DrawImage(vi.LoadedImage, b.Left, b.Top, b.Width, b.Height);
                    }
                }

                pw = LaserRasterProcessor.Process(
                    bmp, cols, rows, mode, engThr, engInv,
                    _cfg.ImageSharpen, _cfg.ImageContrast, _cfg.ImageBrightness, _cfg.ImageGamma, maxPower);
            }

            return new RasterJob
            {
                Name = "Image",
                Rect = area,
                Cols = cols,
                Rows = rows,
                Pw = pw,
                ScanDirection = _cfg.ImageScanDir,
                OverscanMm = Math.Max(0.0, _cfg.OverscanMm)
            };
        }

        // =================================================================
        //  Tiện ích
        // =================================================================
        static string F(double v)
        {
            if (Math.Abs(v) < 0.0005) v = 0;               // tránh "-0"
            return v.ToString("0.###", Inv);
        }

        static double Dist(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Comment G-code nên là ASCII (nhiều bộ gửi/firmware lỗi với tiếng Việt có dấu)
        static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string norm = s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in norm)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c < 32 || c > 126 ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}
namespace test01_1014
{
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Text;

    public partial class GCodeSettings
    {
        /// <summary>Gốc toạ độ X (px canvas) khi xuất theo vị trí thật trên vùng khắc (OriginAtJobCorner = false).</summary>
        public float AbsOriginXpx { get; set; } = 0f;

        /// <summary>Gốc toạ độ Y (px canvas). NaN = dùng CanvasHeightPx (gốc ở góc DƯỚI-TRÁI như cũ).</summary>
        public float AbsOriginYpx { get; set; } = float.NaN;
    }

    public class FrameOptions
    {
        public float MarginMm { get; set; } = 0f;          // chừa lề quanh sản phẩm
        public float PowerPercent { get; set; } = 1f;      // % công suất đèn đỏ (0 = chỉ di chuyển, không bắn)
        public int Feed { get; set; } = 3000;              // mm/phút
        public int Loops { get; set; } = 1;                // số vòng chạy khung
        public bool Cross { get; set; } = false;           // thêm chữ thập ở tâm khung
        public string OnCmd { get; set; } = "";            // lệnh bật đèn đỏ riêng (rỗng = dùng laser công suất thấp)
        public string OffCmd { get; set; } = "";           // lệnh tắt đèn đỏ riêng
    }

    public partial class GCodeCompiler
    {
        // Khung bao (px) của phần sẽ khắc; null nếu không có gì để khắc
        public RectangleF? GetBoundsPx(IEnumerable<Vectoritem> items)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            void Extend(float x, float y)
            {
                any = true;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            foreach (var it in items)
            {
                int power = Math.Clamp(it.LaserPower, 0, 100);
                if ((int)Math.Round(_cfg.MaxS * power / 100.0) <= 0) continue;     // 0% = không khắc

                if (it is VectorImage vi)           // ảnh / mã vạch: không dựng ảnh raster chỉ để lấy khung
                {
                    var b = Box(vi);
                    if (b.Width <= 0 || b.Height <= 0) continue;
                    var r = RotatedBounds(b, vi.Rotation);
                    Extend(r.Left, r.Top); Extend(r.Right, r.Bottom);
                    continue;
                }

                if (BuildJob(it, false) is PathJob pj)      // false = không dựng đường tô (chỉ cần khung bao)
                    foreach (var path in pj.Paths)
                        foreach (var p in path) Extend(p.X, p.Y);
            }
            return any ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : null;
        }

        // Đường laser của 1 đối tượng ở chế độ tô kín, tách riêng đường TÔ và đường VIỀN (toạ độ px canvas).
        // null nếu đối tượng không tô được / công suất 0% / lỗi. Dùng để xem trước trên canvas.
        public (List<List<PointF>> Fill, List<List<PointF>> Outline)? GetFillParts(Vectoritem it)
        {
            if (it.FillMode == 0 || it is VectorLine || it is VectorImage) return null;
            try
            {
                if (BuildJob(it, true) is not PathJob pj) return null;
                var fill = new List<List<PointF>>();
                var outline = new List<List<PointF>>();
                if (pj.FillCount > 0)
                    for (int i = 0; i < pj.Paths.Count; i++) (i < pj.FillCount ? fill : outline).Add(pj.Paths[i]);
                else
                    outline.AddRange(pj.Paths);          // hình nhỏ hơn khoảng cách tô: chỉ còn đường viền
                return (fill, outline);
            }
            catch { return null; }
        }

        // G-code chạy viền khung bao của sản phẩm (định vị bằng đèn đỏ)
        public GCodeResult CompileFrame(IEnumerable<Vectoritem> items, FrameOptions o)
        {
            RectangleF bb = GetBoundsPx(items)
                ?? throw new InvalidOperationException("Không có đối tượng nào (công suất > 0%) để lấy khung đèn đỏ.");

            float k = _cfg.MmPerPixel;
            // Gốc toạ độ: GIỐNG HỆT Compile (chế độ góc bản vẽ dùng khung bao CHƯA cộng lề)
            float originX = _cfg.OriginAtJobCorner ? bb.Left : _cfg.AbsOriginXpx;
            float originY = _cfg.OriginAtJobCorner ? bb.Bottom
                          : (float.IsNaN(_cfg.AbsOriginYpx) ? _cfg.CanvasHeightPx : _cfg.AbsOriginYpx);
            double MX(float px) => (px - originX) * k;
            double MY(float py) => (originY - py) * k;

            float m = Math.Max(0f, o.MarginMm) / k;
            double x0 = MX(bb.Left - m), x1 = MX(bb.Right + m);
            double yLow = MY(bb.Bottom + m), yHigh = MY(bb.Top - m);     // Y máy hướng lên
            double wMm = x1 - x0, hMm = yHigh - yLow;

            int feed = Math.Max(1, o.Feed);
            int loops = Math.Clamp(o.Loops, 1, 100);
            bool custom = !string.IsNullOrWhiteSpace(o.OnCmd);
            int s = (int)Math.Round(_cfg.MaxS * Math.Clamp(o.PowerPercent, 0f, 100f) / 100.0);

            var sb = new StringBuilder();
            int lines = 0;
            void Line(string t) { sb.AppendLine(t); lines++; }
            string Pt(double x, double y) => $"X{F(x)} Y{F(y)}";

            Line($"; RED LIGHT FRAME {F(wMm)} x {F(hMm)} mm, {loops} loop(s)");
            Line(custom ? "; mode: custom red-light commands"
                        : s > 0 ? $"; mode: low-power laser S{s} ({F(o.PowerPercent)}%) - CHECK it does not mark the material"
                                : "; mode: dry run, laser OFF (S0), head movement only");
            Line("G21 G90");
            if (!custom) Line($"{_cfg.LaserOnCommand} S0");
            Line($"F{feed}");

            double cutLen = 0, rapidLen = 0;
            double cx = 0, cy = 0;
            void Rapid(double x, double y) { rapidLen += Dist(cx, cy, x, y); cx = x; cy = y; Line($"G0 {Pt(x, y)}"); }
            void Draw(double x, double y, bool first)
            {
                cutLen += Dist(cx, cy, x, y); cx = x; cy = y;
                Line($"G1 {Pt(x, y)}{(first && !custom && s > 0 ? " S" + s : "")}");
            }
            void On() { if (custom) Line(o.OnCmd.Trim()); }
            void Off() { if (custom) Line(o.OffCmd.Trim()); else Line("S0"); }

            for (int i = 0; i < loops; i++)
            {
                if (loops > 1) Line($"; loop {i + 1}/{loops}");
                Rapid(x0, yLow); On();
                Draw(x1, yLow, true); Draw(x1, yHigh, false); Draw(x0, yHigh, false); Draw(x0, yLow, false);
                Off();
            }

            if (o.Cross)
            {
                double mx = (x0 + x1) / 2, my = (yLow + yHigh) / 2;
                double arm = Math.Min(5.0, Math.Min(wMm, hMm) / 4);
                Line("; center cross");
                Rapid(mx - arm, my); On(); Draw(mx + arm, my, true); Off();
                Rapid(mx, my - arm); On(); Draw(mx, my + arm, true); Off();
            }

            if (!custom) Line("M5");
            if (_cfg.ReturnToOrigin) Rapid(0, 0);

            return new GCodeResult
            {
                Code = sb.ToString(),
                LineCount = lines,
                WidthMm = wMm,
                HeightMm = hMm,
                CutLengthMm = cutLen,
                RapidLengthMm = rapidLen,
                EstimatedMinutes = cutLen / feed + rapidLen / Math.Max(1, _cfg.RapidFeedForEstimate)
            };
        }
    }

    // =====================================================================
    //  Bộ xử lý ảnh Raster cho máy khắc Laser:
    //  - Grayscale 8-bit NTSC
    //  - Brightness & Contrast
    //  - Unsharp Mask Sharpening
    //  - Dithering: Floyd-Steinberg, Atkinson, Stucki, Ordered Bayer 4x4
    //  - Threshold (Đen trắng nhị phân)
    //  - PWM Grayscale
    // =====================================================================
    public static class LaserRasterProcessor
    {
        public static int[] Process(
            Bitmap bmp,
            int cols,
            int rows,
            ImageDitherMode mode,
            int threshold,
            bool invert,
            bool sharpen,
            double contrast,
            double brightness,
            double gamma,
            int maxPower)
        {
            var pw = new int[cols * rows];
            float[] gray = new float[cols * rows];

            var data = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                    ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] buf = new byte[stride * rows];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                float cFact = (float)Math.Clamp(contrast, 0.2, 3.0);
                float bAdd = (float)Math.Clamp(brightness, -128.0, 128.0);
                float gPow = (float)(gamma > 0.1 ? 1.0 / Math.Clamp(gamma, 0.2, 4.0) : 1.0);
                bool applyGamma = Math.Abs(gPow - 1.0f) > 0.01f;

                for (int y = 0; y < rows; y++)
                {
                    int rowOff = y * stride;
                    int grayOff = y * cols;
                    for (int x = 0; x < cols; x++)
                    {
                        int i = rowOff + x * 3;
                        // NTSC Grayscale: 0.299 R + 0.587 G + 0.114 B
                        float gVal = (buf[i + 2] * 299f + buf[i + 1] * 587f + buf[i] * 114f) / 1000f;

                        // Độ tương phản & độ sáng
                        if (cFact != 1.0f || bAdd != 0.0f)
                        {
                            gVal = (gVal - 128f) * cFact + 128f + bAdd;
                        }

                        // Hiệu chỉnh Gamma: làm sáng vùng tối mà không cháy vùng sáng
                        if (applyGamma)
                        {
                            float norm = Math.Clamp(gVal / 255f, 0f, 1f);
                            gVal = 255f * MathF.Pow(norm, gPow);
                        }

                        if (invert) gVal = 255f - gVal;
                        gray[grayOff + x] = Math.Clamp(gVal, 0f, 255f);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // Làm nét (Sharpen) bằng lọc Unsharp 3x3
            if (sharpen && cols > 2 && rows > 2)
            {
                float[] sharp = (float[])gray.Clone();
                for (int y = 1; y < rows - 1; y++)
                {
                    int yCur = y * cols;
                    int yPrev = (y - 1) * cols;
                    int yNext = (y + 1) * cols;
                    for (int x = 1; x < cols - 1; x++)
                    {
                        float val = 5f * gray[yCur + x]
                                    - gray[yPrev + x]
                                    - gray[yNext + x]
                                    - gray[yCur + (x - 1)]
                                    - gray[yCur + (x + 1)];
                        sharp[yCur + x] = Math.Clamp(val, 0f, 255f);
                    }
                }
                gray = sharp;
            }

            // Dithering / Thresholding / Grayscale
            float thrVal = (float)Math.Clamp(threshold, 0, 255);

            switch (mode)
            {
                case ImageDitherMode.Grayscale:
                    for (int i = 0; i < gray.Length; i++)
                    {
                        // Độ xám: 0 là đen (đốt mạnh nhất = maxPower), 255 là trắng (không đốt = 0)
                        float level = 255f - gray[i];
                        pw[i] = Math.Clamp((int)Math.Round(level * maxPower / 255f), 0, maxPower);
                    }
                    break;

                case ImageDitherMode.Threshold:
                    for (int i = 0; i < gray.Length; i++)
                    {
                        pw[i] = gray[i] < thrVal ? maxPower : 0;
                    }
                    break;

                case ImageDitherMode.FloydSteinberg:
                    for (int y = 0; y < rows; y++)
                    {
                        int yCur = y * cols;
                        int yNext = (y + 1) * cols;
                        for (int x = 0; x < cols; x++)
                        {
                            int idx = yCur + x;
                            float oldVal = gray[idx];
                            float newVal = oldVal < thrVal ? 0f : 255f;
                            float err = oldVal - newVal;
                            gray[idx] = newVal;

                            if (x + 1 < cols)
                                gray[idx + 1] += err * (7f / 16f);
                            if (y + 1 < rows)
                            {
                                if (x - 1 >= 0)
                                    gray[yNext + (x - 1)] += err * (3f / 16f);
                                gray[yNext + x] += err * (5f / 16f);
                                if (x + 1 < cols)
                                    gray[yNext + (x + 1)] += err * (1f / 16f);
                            }

                            pw[idx] = newVal == 0f ? maxPower : 0;
                        }
                    }
                    break;

                case ImageDitherMode.Atkinson:
                    for (int y = 0; y < rows; y++)
                    {
                        int yCur = y * cols;
                        int yNext = (y + 1) * cols;
                        int yNext2 = (y + 2) * cols;
                        for (int x = 0; x < cols; x++)
                        {
                            int idx = yCur + x;
                            float oldVal = gray[idx];
                            float newVal = oldVal < thrVal ? 0f : 255f;
                            float err = (oldVal - newVal) / 8f;
                            gray[idx] = newVal;

                            if (x + 1 < cols) gray[idx + 1] += err;
                            if (x + 2 < cols) gray[idx + 2] += err;
                            if (y + 1 < rows)
                            {
                                if (x - 1 >= 0) gray[yNext + (x - 1)] += err;
                                gray[yNext + x] += err;
                                if (x + 1 < cols) gray[yNext + (x + 1)] += err;
                            }
                            if (y + 2 < rows)
                            {
                                gray[yNext2 + x] += err;
                            }

                            pw[idx] = newVal == 0f ? maxPower : 0;
                        }
                    }
                    break;

                case ImageDitherMode.Stucki:
                    for (int y = 0; y < rows; y++)
                    {
                        int yCur = y * cols;
                        int yNext = (y + 1) * cols;
                        int yNext2 = (y + 2) * cols;
                        for (int x = 0; x < cols; x++)
                        {
                            int idx = yCur + x;
                            float oldVal = gray[idx];
                            float newVal = oldVal < thrVal ? 0f : 255f;
                            float err = (oldVal - newVal) / 42f;
                            gray[idx] = newVal;

                            if (x + 1 < cols) gray[idx + 1] += err * 8f;
                            if (x + 2 < cols) gray[idx + 2] += err * 4f;

                            if (y + 1 < rows)
                            {
                                if (x - 2 >= 0) gray[yNext + (x - 2)] += err * 2f;
                                if (x - 1 >= 0) gray[yNext + (x - 1)] += err * 4f;
                                gray[yNext + x] += err * 8f;
                                if (x + 1 < cols) gray[yNext + (x + 1)] += err * 4f;
                                if (x + 2 < cols) gray[yNext + (x + 2)] += err * 2f;
                            }

                            if (y + 2 < rows)
                            {
                                if (x - 2 >= 0) gray[yNext2 + (x - 2)] += err * 1f;
                                if (x - 1 >= 0) gray[yNext2 + (x - 1)] += err * 2f;
                                gray[yNext2 + x] += err * 4f;
                                if (x + 1 < cols) gray[yNext2 + (x + 1)] += err * 2f;
                                if (x + 2 < cols) gray[yNext2 + (x + 2)] += err * 1f;
                            }

                            pw[idx] = newVal == 0f ? maxPower : 0;
                        }
                    }
                    break;

                case ImageDitherMode.OrderedBayer:
                    int[,] bayer4 = new int[4, 4]
                    {
                        {  0,  8,  2, 10 },
                        { 12,  4, 14,  6 },
                        {  3, 11,  1,  9 },
                        { 15,  7, 13,  5 }
                    };
                    for (int y = 0; y < rows; y++)
                    {
                        int yCur = y * cols;
                        for (int x = 0; x < cols; x++)
                        {
                            int idx = yCur + x;
                            float bayerThr = (bayer4[y % 4, x % 4] + 0.5f) * (255f / 16f);
                            pw[idx] = gray[idx] < bayerThr ? maxPower : 0;
                        }
                    }
                    break;
            }

            return pw;
        }
    }
}

// =====================================================================
// PHẦN: Form mô phỏng / xem trước G-code   (trước đây là file GCodePreviewForm.cs)
// =====================================================================
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
namespace test01_1014
{
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;
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
        ComboBox cbOrder = null!;
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
            cbOrder = AddCmb(left, "THỨ TỰ KHẮC (mô phỏng + khắc thật):", ref y, new[]
            {
                "Tự động tối ưu (mặc định)",
                "Theo TẦNG (tầng cao → thấp)",
                "Theo LỚP (lớp cao → thấp)"
            }, (int)_cfg.OrderMode);
            chkOptimize = AddChk(left, "Tối ưu đường chạy G0 (TSP / Nearest)", ref y, _cfg.OptimizeToolpath);
            chkInsideOut = AddChk(left, "Cắt lỗ trong trước, viền ngoài sau", ref y, _cfg.CutInsideOut);

            nMm.ValueChanged += (s, e) => ScheduleRegen(true);
            nMaxS.ValueChanged += (s, e) => ScheduleRegen(false);
            nRapid.ValueChanged += (s, e) => ScheduleRegen(false);
            cbOrder.SelectedIndexChanged += (s, e) =>
            {
                chkOptimize.Enabled = chkInsideOut.Enabled = cbOrder.SelectedIndex != 1;   // theo tầng: không xếp lại
                ScheduleRegen(false);
            };
            chkOptimize.Enabled = chkInsideOut.Enabled = cbOrder.SelectedIndex != 1;
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

            _cfg.OrderMode = (EngraveOrderMode)Math.Max(0, cbOrder.SelectedIndex);
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
