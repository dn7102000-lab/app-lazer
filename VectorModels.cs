// =====================================================================
//  VectorModels.cs  -  ĐỐI TƯỢNG VECTOR & NHẬP / XỬ LÝ: mô hình đối tượng, SVG, DXF, Hershey, Potrace
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Mô hình đối tượng + DTO lưu file   (trước đây: Vectoritem.cs)
//    2. Đường dẫn vector   (trước đây: VectorPathItem.cs)
//    3. Font 1 nét, uốn chữ, Weld   (trước đây: HersheyFont.cs)
//    4. Tách nét ảnh (Potrace)   (trước đây: ImageTracer.cs)
//    5. Nhập SVG   (trước đây: SvgImporter.cs)
//    6. Nhập DXF   (trước đây: DxfImporter.cs)
// =====================================================================

// =====================================================================
// PHẦN: Mô hình đối tượng + DTO lưu file   (trước đây là file Vectoritem.cs)
// =====================================================================
namespace test01_1014
{
    using OpenCvSharp.Dnn;
    using test01_1014;
    using System;
    using System.Drawing;
    using System.Xml.Linq;
    using System.IO;
    using System.Text.Json;
    using System.Windows.Forms;
    using System.Collections.Generic;

    public class ProjectDataDTO
    {
        public string Name { get; set; } = "Laser Project";
        public string Version { get; set; } = "1.0";
        // Danh sách lưu chi tiết các item
        public List<VectorItemDTO> Items { get; set; } = new List<VectorItemDTO>();
        // Các lớp màu (công suất / tốc độ / số lần / bật-tắt xuất). File cũ không có thì rỗng -> dùng lớp mặc định
        public List<LayerDTO> Layers { get; set; } = new List<LayerDTO>();
    }

    public class VectorItemDTO
    {
        public string Shape { get; set; } = "";
        public string Type { get; set; } = ""; // "Rectangle", "Ellipse", "Line", "Text", "Image"
        public float X { get; set; }
        public float Y { get; set; }
        public float X2 { get; set; }           // đầu mút thứ 2 (dùng cho Line)
        public float Y2 { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string TextContent { get; set; } = "";
        public string FontName { get; set; } = "";
        public float FontSize { get; set; }
        public string ImageBase64 { get; set; } = ""; // Ảnh chuyển thành chuỗi Base64
        // Mặc định khớp với Vectoritem để file dự án cũ (không có các trường này) vẫn mở đúng
        public int LaserPower { get; set; } = 80;
        public int LaserSpeed { get; set; } = 1000;
        public int Passes { get; set; } = 1;
        // Thuộc tính mới (file cũ không có thì lấy mặc định)
        public float Rotation { get; set; }
        public bool FontBold { get; set; }
        public bool FontItalic { get; set; }
        public int EngraveMode { get; set; }          // 0 = theo cài đặt G-code, 1 = ảnh xám, 2 = đen trắng
        public int ImageThreshold { get; set; } = 128;
        public bool ImageInvert { get; set; }
        public bool FlipX { get; set; }               // lật ngang
        public bool FlipY { get; set; }               // lật dọc
        public string GroupId { get; set; } = "";    // rỗng = không thuộc nhóm nào
        public string BarcodeText { get; set; } = "";   // rỗng = không phải mã vạch (mã vạch được lưu kèm ảnh PNG)
        public string BarcodeType { get; set; } = "";   // tên BarcodeKind: QR, Code128...
        public int BarcodeQuiet { get; set; } = -1;
        public bool BarcodeShowText { get; set; }
        public int LayerId { get; set; }                // số thứ tự lớp (0 = Lớp 01)
        public int FillMode { get; set; }               // 0 = đường viền, 1 = tô kín, 2 = tô kín + viền
        public double FillIntervalMm { get; set; } = 0.1;
        public float FillAngle { get; set; }
        public bool FillCross { get; set; }
        public bool FillBidir { get; set; } = true;
        public string Name { get; set; } = "";          // tên do người dùng đặt (rỗng = tên mặc định)
        public bool Hidden { get; set; }                // ẩn: không vẽ, không chọn, không xuất G-code
        public bool Locked { get; set; }                // khóa: vẫn vẽ và xuất, nhưng không chọn / sửa được trên canvas
        public string PathDataJson { get; set; } = "";  // dữ liệu đường dẫn phức hợp (SubPaths của VectorPathItem)
        public bool SingleLineFont { get; set; }        // Font 1 nét (Hershey stick font)
        public bool WeldText { get; set; }              // Hàn liền nét chữ giao nhau
        public float ArcRadiusMm { get; set; }          // Bán kính uốn cong chữ (mm, 0 = thẳng)
        public float CharSpacingMm { get; set; }        // Giãn cách ký tự (mm)
    }
    // Các loại hình vẽ hỗ trợ
    public enum VectorType { Image, Rectangle, Ellipse, Line, Text, Polygon, Path }

    // Lớp cơ sở (Base Class)
    public abstract class Vectoritem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Vector Object";
        public VectorType Type { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool IsSelected { get; set; } = false;

        public int LaserPower { get; set; } = 80;
        public int LaserSpeed { get; set; } = 1000;
        public int Passes { get; set; } = 1;

        // Góc xoay (độ, thuận chiều kim đồng hồ) quanh tâm hình. Form1 lo việc vẽ / bấm trúng, G-code xoay đường cắt
        public float Rotation { get; set; }

        // Lật quanh tâm hình (áp dụng sau khi xoay) và nhãn nhóm: các đối tượng cùng GroupId được chọn / kéo chung
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }
        public string? GroupId { get; set; }

        // Lớp màu (số thứ tự, -1 = chưa gán: Form1 sẽ gán vào lớp đang chọn khi thêm vào canvas)
        public int LayerId { get; set; } = -1;
        // Danh sách đối tượng: ẩn (không vẽ / không chọn / không xuất G-code), khóa (vẫn vẽ + xuất nhưng không chọn được trên canvas)
        public bool Hidden { get; set; }
        public bool Locked { get; set; }
        // Màu nét vẽ: đang chọn = xanh dương, không thì màu của lớp
        // Chế độ khắc hình kín (lấy từ lớp): 0 = đường viền, 1 = tô kín (đường quét song song), 2 = tô kín + viền
        public int FillMode { get; set; }
        public double FillIntervalMm { get; set; } = 0.1;    // khoảng cách giữa 2 đường quét (mm)
        public float FillAngle { get; set; }                  // góc đường quét (độ, 0 = nằm ngang)
        public bool FillCross { get; set; }                   // quét thêm lượt vuông góc
        public bool FillBidir { get; set; } = true;           // quét 2 chiều (zigzag)
        protected Color StrokeColor => IsSelected ? Color.Blue : LayerColors.Of(LayerId);

        public abstract void Draw(Graphics g);
        public abstract bool HitTest(PointF point);

        // Di chuyển đối tượng tới vị trí mới (Line ghi đè để dịch cả 2 đầu mút)
        public virtual void MoveTo(float x, float y) { X = x; Y = y; }
    }

    public class VectorImage : Vectoritem
    {
        public Image LoadedImage { get; set; }

        // Chế độ khắc riêng của ảnh này: 0 = theo cài đặt xuất G-code, 1 = ảnh xám, 2 = đen trắng (1-bit)
        public int EngraveMode { get; set; }
        public int Threshold { get; set; } = 128;   // ngưỡng khi đen trắng
        public bool Invert { get; set; }            // khắc vùng sáng thay vì vùng tối

        public VectorImage(Image img)
        {
            Type = VectorType.Image;
            Name = "Hình ảnh";
            LoadedImage = img;
            Width = img.Width > 200 ? 200 : img.Width;   // Kích thước mặc định vừa phải
            Height = img.Height > 200 ? 200 : img.Height;
        }

        public override void Draw(Graphics g)
        {
            if (LoadedImage != null)
            {
                // Vẽ hình ảnh theo vị trí X, Y và kích thước Width, Height
                g.DrawImage(LoadedImage, X, Y, Width, Height);
            }

            // Vẽ khung viền nét đứt khi hình ảnh được chọn
            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    g.DrawRectangle(pen, X, Y, Width, Height);
                }
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }

    public class VectorRectangle : Vectoritem
    {
        public VectorRectangle() { Type = VectorType.Rectangle; Name = "Hình chữ nhật"; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
            {
                g.DrawRectangle(pen, X, Y, Width, Height);
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }
    public enum PolygonKind { Diamond, Pentagon, Hexagon, Star }

    public class VectorPolygon : Vectoritem
    {
        public PolygonKind Kind { get; }
        private readonly PointF[] unit;      // các đỉnh chuẩn hóa trong hộp 0..1

        public VectorPolygon(PolygonKind kind)
        {
            Kind = kind;
            Type = VectorType.Polygon;
            Name = kind switch
            {
                PolygonKind.Diamond => "Hình thoi",
                PolygonKind.Pentagon => "Ngũ giác",
                PolygonKind.Hexagon => "Lục giác",
                _ => "Ngôi sao"
            };
            unit = BuildUnit(kind);
            Width = 100; Height = 100;
        }

        public static PointF[] BuildUnit(PolygonKind kind)
        {
            var raw = new List<PointF>();
            switch (kind)
            {
                case PolygonKind.Diamond:
                    return new[] { new PointF(0.5f, 0), new PointF(1, 0.5f), new PointF(0.5f, 1), new PointF(0, 0.5f) };
                case PolygonKind.Pentagon: Regular(raw, 5, -90); break;
                case PolygonKind.Hexagon: Regular(raw, 6, 0); break;
                case PolygonKind.Star:
                    for (int i = 0; i < 10; i++)            // đỉnh ngoài/trong xen kẽ
                    {
                        double r = i % 2 == 0 ? 1.0 : 0.382;
                        double a = (-90 + 36 * i) * Math.PI / 180;
                        raw.Add(new PointF((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))));
                    }
                    break;
            }
            // Co giãn cho vừa khít hộp 0..1 để kéo 8 điểm là hình biến dạng theo
            float minX = raw.Min(p => p.X), maxX = raw.Max(p => p.X);
            float minY = raw.Min(p => p.Y), maxY = raw.Max(p => p.Y);
            return raw.Select(p => new PointF((p.X - minX) / (maxX - minX), (p.Y - minY) / (maxY - minY))).ToArray();
        }

        private static void Regular(List<PointF> pts, int n, double startDeg)
        {
            for (int i = 0; i < n; i++)
            {
                double a = (startDeg + 360.0 * i / n) * Math.PI / 180;
                pts.Add(new PointF((float)Math.Cos(a), (float)Math.Sin(a)));
            }
        }

        // Đỉnh theo tọa độ canvas (dùng cho vẽ, hit-test và xuất G-code)
        public PointF[] GetPoints()
        {
            var r = new PointF[unit.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = new PointF(X + unit[i].X * Width, Y + unit[i].Y * Height);
            return r;
        }

        public override void Draw(Graphics g)
        {
            using (var pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
                g.DrawPolygon(pen, GetPoints());

            if (IsSelected)   // khung nét đứt để thấy 8 điểm kéo nằm ở đâu
                using (var dash = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawRectangle(dash, X, Y, Width, Height);
        }

        public override bool HitTest(PointF pt)       // point-in-polygon (đúng cả với ngôi sao lõm)
        {
            var v = GetPoints();
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                if ((v[i].Y > pt.Y) != (v[j].Y > pt.Y) &&
                    pt.X < (v[j].X - v[i].X) * (pt.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                    inside = !inside;
            return inside;
        }
    }
    // Hình tròn / Elip
    public class VectorEllipse : Vectoritem
    {
        public VectorEllipse() { Type = VectorType.Ellipse; Name = "Hình tròn/Elip"; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
            {
                g.DrawEllipse(pen, X, Y, Width, Height);
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }

    // Đường thẳng
    public class VectorLine : Vectoritem
    {
        public float X2 { get; set; }
        public float Y2 { get; set; }

        // Độ "dày" vùng bấm trúng đường thẳng (Form1 chỉnh theo mức zoom)
        public static float HitTolerance = 6f;

        public VectorLine() { Type = VectorType.Line; Name = "Đường thẳng"; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
            {
                g.DrawLine(pen, X, Y, X2, Y2);
            }
        }

        public override bool HitTest(PointF p)
        {
            float dx = X2 - X, dy = Y2 - Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 == 0 ? 0 : ((p.X - X) * dx + (p.Y - Y) * dy) / len2;
            t = Math.Clamp(t, 0f, 1f);
            float ex = p.X - (X + t * dx), ey = p.Y - (Y + t * dy);
            return ex * ex + ey * ey <= HitTolerance * HitTolerance;
        }

        public override void MoveTo(float x, float y)
        {
            float dx = x - X, dy = y - Y;
            X = x; Y = y;
            X2 += dx; Y2 += dy;
        }
    }

    // Chữ văn bản
    public class VectorText : Vectoritem
    {
        public string TextContent { get; set; } = "Nhập văn bản";
        public string FontName { get; set; } = "Arial";
        public float FontSize { get; set; } = 16f;
        public bool FontBold { get; set; }
        public bool FontItalic { get; set; }
        public bool SingleLineFont { get; set; }      // true = Font 1 nét (Hershey) khắc nhanh
        public bool WeldText { get; set; }            // true = Hàn liền các chữ cái giao nhau
        public float ArcRadiusMm { get; set; }        // Bán kính uốn cong (mm, 0 = chữ thẳng, >0 vòm xuống, <0 võng lên)
        public float CharSpacingMm { get; set; }      // Khoảng cách tăng/giảm giữa các ký tự (mm)

        public bool HasCustomPath => SingleLineFont || WeldText || Math.Abs(ArcRadiusMm) >= 1f || Math.Abs(CharSpacingMm) > 0.01f;

        public Font MakeFont()
        {
            var st = (FontBold ? FontStyle.Bold : 0) | (FontItalic ? FontStyle.Italic : 0);
            try { return new Font(FontName, FontSize, st); }
            catch (ArgumentException) { return new Font(FontName, FontSize); }   // phông không có kiểu này
        }

        public VectorText()
        {
            Type = VectorType.Text;
            Name = "Văn bản";
            Width = 150;
            Height = 40;
        }

        public override void Draw(Graphics g)
        {
            if (HasCustomPath)
            {
                var subPaths = HersheyFont.BuildTextSubPaths(this, 0.25f, 0.3f);
                if (IsSelected)
                {
                    using (Pen selPen = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        g.DrawRectangle(selPen, X, Y, Width, Height);
                    }
                }
                using (Pen strokePen = new Pen(StrokeColor, SingleLineFont ? 1.5f : 1.3f))
                {
                    strokePen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    foreach (var sp in subPaths)
                    {
                        if (sp.Points.Count < 2) continue;
                        var pts = sp.Points.ToArray();
                        if (sp.IsClosed && pts.Length >= 3) g.DrawPolygon(strokePen, pts);
                        else g.DrawLines(strokePen, pts);
                    }
                }
                return;
            }

            using (Font font = MakeFont())
            {
                SizeF size = g.MeasureString(TextContent, font);
                Width = size.Width;
                Height = size.Height;

                // Vẽ viền xanh nét đứt bao quanh khi được chọn
                if (IsSelected)
                {
                    using (Pen pen = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        g.DrawRectangle(pen, X, Y, Width, Height);
                    }
                }

                // Vẽ chữ lên canvas
                using (Brush brush = new SolidBrush(StrokeColor))
                {
                    g.DrawString(TextContent, font, brush, X, Y);
                }
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }
}

// =====================================================================
// PHẦN: Đường dẫn vector   (trước đây là file VectorPathItem.cs)
// =====================================================================
namespace test01_1014
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Linq;
    /// <summary>
    /// Đối tượng vector đường dẫn phức hợp (gồm nhiều đoạn thẳng, đường cong, cung tròn, polyline)
    /// Dùng cho việc nhập các file vector tiêu chuẩn như SVG và DXF.
    /// </summary>
    public class VectorPathItem : Vectoritem
    {
        // Danh sách các đường con (mỗi đường con là danh sách điểm nối tiếp nhau)
        // và cờ IsClosed xác định xem đường con đó có khép kín không.
        public class SubPath
        {
            public List<PointF> Points { get; set; } = new List<PointF>();
            public bool IsClosed { get; set; }
        }

        public List<SubPath> SubPaths { get; set; } = new List<SubPath>();

        public VectorPathItem()
        {
            Type = VectorType.Path;
            Name = "Đường dẫn Vector";
        }

        public VectorPathItem(List<SubPath> subPaths, string name = "Đường dẫn Vector")
        {
            Type = VectorType.Path;
            Name = name;
            SetPaths(subPaths);
        }

        /// <summary>
        /// Cập nhật danh sách sub-paths và tính toán lại hộp bao (X, Y, Width, Height)
        /// theo tọa độ gốc cục bộ.
        /// </summary>
        public void SetPaths(List<SubPath> subPaths)
        {
            SubPaths = subPaths ?? new List<SubPath>();
            RecalculateBounds();
        }

        public void RecalculateBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasPoints = false;

            foreach (var sp in SubPaths)
            {
                foreach (var p in sp.Points)
                {
                    hasPoints = true;
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }

            if (hasPoints)
            {
                X = minX;
                Y = minY;
                Width = Math.Max(1f, maxX - minX);
                Height = Math.Max(1f, maxY - minY);
            }
            else
            {
                X = 0;
                Y = 0;
                Width = 10;
                Height = 10;
            }
        }

        public override void Draw(Graphics g)
        {
            if (SubPaths.Count == 0) return;

            using var pen = new Pen(StrokeColor, IsSelected ? 2f : 1f);

            foreach (var sp in SubPaths)
            {
                if (sp.Points.Count < 2) continue;
                var pts = sp.Points.ToArray();
                if (sp.IsClosed && pts.Length >= 3)
                {
                    g.DrawPolygon(pen, pts);
                }
                else
                {
                    g.DrawLines(pen, pts);
                }
            }

            if (IsSelected)
            {
                using var dash = new Pen(Color.Blue, 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(dash, X, Y, Width, Height);
            }
        }

        public override bool HitTest(PointF point)
        {
            // Kiểm tra sơ bộ bằng hộp bao
            RectangleF box = new RectangleF(X - 3, Y - 3, Width + 6, Height + 6);
            if (!box.Contains(point)) return false;

            const float toleranceSq = 25f; // Bán kính bấm trúng 5px

            foreach (var sp in SubPaths)
            {
                var pts = sp.Points;
                int count = pts.Count;
                if (count < 2) continue;

                // Nếu là hình khép kín và điểm rơi vào bên trong polygon
                if (sp.IsClosed && count >= 3 && PointInPolygon(point, pts))
                {
                    return true;
                }

                // Kiểm tra khoảng cách tới từng đoạn thẳng
                int limit = sp.IsClosed ? count : count - 1;
                for (int i = 0; i < limit; i++)
                {
                    var p1 = pts[i];
                    var p2 = pts[(i + 1) % count];
                    if (DistToSegmentSq(point, p1, p2) <= toleranceSq)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override void MoveTo(float x, float y)
        {
            float dx = x - X;
            float dy = y - Y;
            X = x;
            Y = y;

            foreach (var sp in SubPaths)
            {
                for (int i = 0; i < sp.Points.Count; i++)
                {
                    var p = sp.Points[i];
                    sp.Points[i] = new PointF(p.X + dx, p.Y + dy);
                }
            }
        }

        /// <summary>
        /// Tỷ lệ lại kích thước hình khi người dùng kéo 8 điểm điều khiển
        /// </summary>
        public void Scale(float newWidth, float newHeight, float newX, float newY)
        {
            float sx = Width > 0.001f ? newWidth / Width : 1f;
            float sy = Height > 0.001f ? newHeight / Height : 1f;

            foreach (var sp in SubPaths)
            {
                for (int i = 0; i < sp.Points.Count; i++)
                {
                    float px = newX + (sp.Points[i].X - X) * sx;
                    float py = newY + (sp.Points[i].Y - Y) * sy;
                    sp.Points[i] = new PointF(px, py);
                }
            }

            X = newX;
            Y = newY;
            Width = Math.Max(1f, newWidth);
            Height = Math.Max(1f, newHeight);
        }

        private static bool PointInPolygon(PointF pt, List<PointF> v)
        {
            bool inside = false;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            {
                if ((v[i].Y > pt.Y) != (v[j].Y > pt.Y) &&
                    pt.X < (v[j].X - v[i].X) * (pt.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float DistToSegmentSq(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f)
            {
                float dxx = p.X - a.X, dyy = p.Y - a.Y;
                return dxx * dxx + dyy * dyy;
            }
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float ex = p.X - (a.X + t * dx), ey = p.Y - (a.Y + t * dy);
            return ex * ex + ey * ey;
        }
    }
}

// =====================================================================
// PHẦN: Font 1 nét, uốn chữ, Weld   (trước đây là file HersheyFont.cs)
// =====================================================================
namespace test01_1014
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using SubPath = VectorPathItem.SubPath;

    /// <summary>
    /// Hỗ trợ:
    /// 1. Font 1 nét (Hershey Single-Line / Stick Font) có dấu Tiếng Việt để khắc tốc độ cao.
    /// 2. Uốn chữ theo cung tròn (Text on Arc / Curve).
    /// 3. Hàn liền nét chữ / hình giao nhau (Boolean Union / Weld) thành đường cắt thống nhất.
    /// 4. Chuyển đổi các hình cơ bản / văn bản thành VectorPathItem để sửa từng điểm nút (Node Edit).
    /// </summary>
    public static class HersheyFont
    {
        // Chiều cao cơ sở của hệ tọa độ Hershey chuẩn (từ y = -12 đến y = +9 -> cao 21 đơn vị)
        private const float BaseCapHeight = 21f;

        private struct GlyphDef
        {
            public float Left;
            public float Right;
            public List<PointF[]> Strokes;
        }

        // Bảng mã Hershey Simplex (ASCII 32..126), giải mã từ chuỗi vector chuẩn Hershey
        private static readonly Dictionary<char, string> RawSimplex = new()
        {
            [' '] = "JZ",
            ['!'] = "MWRFRT RRYQZR[SZRY",
            ['"'] = "JZNFNM RVFVM",
            ['#'] = "H]SBLb RYBRb RLOZO RKUYU",
            ['$'] = "H\\PBP_ RTBT_ RYIWGTFPFMGKIKKLMMNOOUQWRXSYUYXWZT[P[MZKX",
            ['%'] = "F^[FI[ RNFPHPJOLMMKMIKIIJGLFNFPGSHVHYG[F RWTUUTWTYV[X[ZZ[X[VYTWT",
            ['&'] = "E_\\O\\N[MZMYNXPVUTXRZP[L[JZIYHWHUISJRQNRMSKSIRGPFNGMIMKNNPQUXWZY[[[\\Z\\Y",
            ['\''] = "MWRHQGRFSGSIRKQL",
            ['('] = "KYVBTDRGPKOPOTPYR]T`Vb",
            [')'] = "KYNBPDRGTKUPUTTYR]P`Nb",
            ['*'] = "JZRFRR RMIWO RWIMO",
            ['+'] = "E_RIR[ RIR[R",
            [','] = "MWSZR[QZRYSZS\\R^Q_",
            ['-'] = "E_IR[R",
            ['.'] = "MWRYQZR[SZRY",
            ['/'] = "G][BIb",
            ['0'] = "H\\QFNGLJKOKRLWNZQ[S[VZXWYRYOXJVGSFQF",
            ['1'] = "H\\NJPISFS[",
            ['2'] = "H\\LKLJMHNGPFTFVGWHXJXLWNUQK[Y[",
            ['3'] = "H\\MFXFRNUNWOXPYSYUXXVZS[P[MZLYKW",
            ['4'] = "H\\UFKTZT RUFU[",
            ['5'] = "H\\WFMFLOMNPMSMVNXPYSYUXXVZS[P[MZLYKW",
            ['6'] = "H\\XIWGTFRFOGMJLOLTMXOZR[S[VZXXYUYTXQVOSNRNOOMQLT",
            ['7'] = "H\\YFO[ RKFYF",
            ['8'] = "H\\PFMGLILKMMONSOVPXRYTYWXYWZT[P[MZLYKWKTLRNPQOUNWMXKXIWGTFPF",
            ['9'] = "H\\XMWPURRSQSNRLPKMKLLINGQFRFUGWIXMXRWWUZR[P[MZLX",
            [':'] = "MWRMQNROSNRM RRYQZR[SZRY",
            [';'] = "MWRMQNROSNRM RSZR[QZRYSZS\\R^Q_",
            ['<'] = "F^ZIJRZ[",
            ['='] = "E_IO[O RIU[U",
            ['>'] = "F^JIZRJ[",
            ['?'] = "I[LKLJMHNGPFTFVGWHXJXLWNVORQRT RRYQZR[SZRY",
            ['@'] = "E`WNVLTKQKOLNMMPMSNUPVSVUUVS RQKOMNPNSOUPV RWKVSVUXVZV\\T]Q]O\\L[JYHWGTFQFNGLHJJILHOHRIUJWLYNZQ[T[WZYYZX RXKWSWUXV",
            ['A'] = "I[RFJ[ RRFZ[ RMTWT",
            ['B'] = "G\\KFK[ RKFTFWGXHYJYLXNWOTP RKPTPWQXRYTYWXYWZT[K[",
            ['C'] = "H]ZKYIWGUFQFOGMILKKNKSLVMXOZQ[U[WZYXZV",
            ['D'] = "G\\KFK[ RKFRFUGWIXKYNYSXVWXUZR[K[",
            ['E'] = "H[LFL[ RLFYF RLPTP RL[Y[",
            ['F'] = "HZLFL[ RLFYF RLPTP",
            ['G'] = "H]ZKYIWGUFQFOGMILKKNKSLVMXOZQ[U[WZYXZVZS RUSZS",
            ['H'] = "G]KFK[ RYFY[ RKPYP",
            ['I'] = "NVRFR[",
            ['J'] = "JZVFVVUYTZR[P[NZMYLVLT",
            ['K'] = "G\\KFK[ RYFKT RPOY[",
            ['L'] = "HYLFL[ RL[X[",
            ['M'] = "F^JFJ[ RJFR[ RZFR[ RZFZ[",
            ['N'] = "G]KFK[ RKFY[ RYFY[",
            ['O'] = "G]PFNGLIKKJNJSKVLXNZP[T[VZXXYVZSZNYKXIVGTFPF",
            ['P'] = "G\\KFK[ RKFTFWGXHYJYMXOWPTQKQ",
            ['Q'] = "G]PFNGLIKKJNJSKVLXNZP[T[VZXXYVZSZNYKXIVGTFPF RSWY]",
            ['R'] = "G\\KFK[ RKFTFWGXHYJYLXNWOTPKP RRPY[",
            ['S'] = "H\\YIWGTFPFMGKIKKLMMNOOUQWRXSYUYXWZT[P[MZKX",
            ['T'] = "JZRFR[ RKFYF",
            ['U'] = "G]KFKULXNZQ[S[VZXXYUYF",
            ['V'] = "I[JFR[ RZFR[",
            ['W'] = "F^HFM[ RRFM[ RRFW[ R\\FW[",
            ['X'] = "H\\KFY[ RK[YF",
            ['Y'] = "I[JFRPR[ RZFRP",
            ['Z'] = "H\\YFK[ RKFYF RK[Y[",
            ['['] = "KYOBOb RPBPb ROBVB RObVb",
            ['\\'] = "KYKFY^",
            [']'] = "KYTBTb RUBUb RNBUB RNbUb",
            ['^'] = "JZPLRITL RMORJWO RRJR[",
            ['_'] = "JZJ]Z]",
            ['`'] = "MWSFRGQIQKRLSKRJ",
            ['a'] = "I\\XMX[ RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['b'] = "H[LFL[ RLPNNPMSMUNWPXSXUWXUZS[P[NZLX",
            ['c'] = "I[XPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['d'] = "I\\XFX[ RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['e'] = "I[LSXSXQWOVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['f'] = "MYWFUFSGRJR[ ROMVM",
            ['g'] = "I\\XMX]W`VaTbQbOa RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['h'] = "I\\MFM[ RMQPNRMUMWNXQX[",
            ['i'] = "NVQFRGSFREQF RRMR[",
            ['j'] = "MWRFSGTFSERF RSMS^RaPbNb",
            ['k'] = "IZMFM[ RWMMW RQSX[",
            ['l'] = "NVRFR[",
            ['m'] = "CaGMG[ RGQJNLMOMQNRQR[ RRQUNWMZM\\N]Q][",
            ['n'] = "I\\MMM[ RMQPNRMUMWNXQX[",
            ['o'] = "I\\QMONMPLSLUMXOZQ[T[VZXXYUYSXPVNTMQM",
            ['p'] = "H[LMLb RLPNNPMSMUNWPXSXUWXUZS[P[NZLX",
            ['q'] = "I\\XMXb RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['r'] = "KXOMO[ ROSPPRNTMWM",
            ['s'] = "I[XPWNTMQMNNMPNRPSUTWUXWXXWZT[Q[NZMX",
            ['t'] = "MYRFRWSZU[W[ ROMVM",
            ['u'] = "I\\MMMWNZP[S[UZXW RXMX[",
            ['v'] = "JZLMR[ RXMR[",
            ['w'] = "G]JMN[ RRMN[ RRMV[ RZMV[",
            ['x'] = "I[MMX[ RMM[X",
            ['y'] = "JZLMR[ RXMR[P_NaLbKb",
            ['z'] = "I[XMM[ RMMXM RM[X[",
            ['{'] = "KYTBRCQDPFPHQJRKSMSOQQ RRCQEQGRISJTLTNSPORSTTVTXSZR[Q]Q_Ra RQSSUSWRYQZP\\P^Q`RaTb",
            ['|'] = "NVRBRb",
            ['}'] = "KYPBRCSDTFTHSJRKQMQOSQ RRCSESGRIQJPLPNQPURQTPVPXQZR[S]S_Ra RSSQUQWRYSZT\\T^S`RaPb",
            ['~'] = "F^IUISJPLONOPPTSVTXTZS[Q ISJQLPNPPQTTVUXUZT[Q[O"
        };

        private static readonly Dictionary<char, GlyphDef> GlyphCache = new();

        private static GlyphDef DecodeHershey(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 2)
                return new GlyphDef { Left = -5, Right = 5, Strokes = new List<PointF[]>() };

            float left = code[0] - 'R';
            float right = code[1] - 'R';
            var strokes = new List<PointF[]>();
            var cur = new List<PointF>();

            for (int i = 2; i + 1 < code.Length; i += 2)
            {
                char c1 = code[i];
                char c2 = code[i + 1];
                if (c1 == ' ' && c2 == 'R')
                {
                    if (cur.Count >= 2) strokes.Add(cur.ToArray());
                    cur.Clear();
                    continue;
                }
                float x = c1 - 'R';
                float y = c2 - 'R';
                cur.Add(new PointF(x, y));
            }
            if (cur.Count >= 2) strokes.Add(cur.ToArray());
            return new GlyphDef { Left = left, Right = right, Strokes = strokes };
        }

        // Phân tách ký tự Tiếng Việt sang ký tự gốc ASCII + dấu phụ + dấu thanh
        // accent: 0=none, 1=breve(ă), 2=circumflex(â,ê,ô), 3=horn(ơ,ư), 4=d-bar(đ)
        // tone: 0=none, 1=huyền(\), 2=sắc(/), 3=hỏi(?), 4=ngã(~), 5=nặng(.)
        private static void DecomposeVietnamese(char ch, out char baseChar, out int accent, out int tone)
        {
            accent = 0;
            tone = 0;
            baseChar = ch;

            if (ch == 'đ') { baseChar = 'd'; accent = 4; return; }
            if (ch == 'Đ') { baseChar = 'D'; accent = 4; return; }

            string norm = ch.ToString().Normalize(NormalizationForm.FormD);
            baseChar = norm[0];
            for (int i = 1; i < norm.Length; i++)
            {
                char m = norm[i];
                switch (m)
                {
                    case '\u0306': accent = 1; break; // ă
                    case '\u0302': accent = 2; break; // â, ê, ô
                    case '\u031B': accent = 3; break; // ơ, ư
                    case '\u0300': tone = 1; break;   // huyền
                    case '\u0301': tone = 2; break;   // sắc
                    case '\u0309': tone = 3; break;   // hỏi
                    case '\u0303': tone = 4; break;   // ngã
                    case '\u0323': tone = 5; break;   // nặng
                }
            }
        }

        private static GlyphDef GetGlyph(char ch)
        {
            lock (GlyphCache)
            {
                if (GlyphCache.TryGetValue(ch, out var cached)) return cached;

                DecomposeVietnamese(ch, out char baseCh, out int accent, out int tone);
                if (!RawSimplex.TryGetValue(baseCh, out string? raw))
                    raw = RawSimplex.TryGetValue(char.ToUpperInvariant(baseCh), out var up) ? up : RawSimplex['?'];

                GlyphDef g = DecodeHershey(raw);
                var strokes = new List<PointF[]>(g.Strokes.Select(s => (PointF[])s.Clone()));

                bool isUpper = char.IsUpper(baseCh);
                float topY = isUpper ? -14f : -7f;
                float cx = (g.Left + g.Right) * 0.5f;

                // Thêm dấu phụ Tiếng Việt (1 nét)
                if (accent == 4) // đ / Đ
                {
                    if (isUpper)
                        strokes.Add(new[] { new PointF(g.Left - 1f, -2f), new PointF(cx + 1f, -2f) });
                    else
                        strokes.Add(new[] { new PointF(cx - 2f, -8f), new PointF(g.Right + 1f, -8f) });
                }
                else if (accent == 1) // ă (dấu trăng)
                {
                    strokes.Add(new[]
                    {
                        new PointF(cx - 3.5f, topY - 4f),
                        new PointF(cx - 1.5f, topY - 2f),
                        new PointF(cx + 1.5f, topY - 2f),
                        new PointF(cx + 3.5f, topY - 4f)
                    });
                    topY -= 4.5f;
                }
                else if (accent == 2) // â, ê, ô (dấu mũ)
                {
                    strokes.Add(new[]
                    {
                        new PointF(cx - 3.5f, topY - 1.5f),
                        new PointF(cx, topY - 4.5f),
                        new PointF(cx + 3.5f, topY - 1.5f)
                    });
                    topY -= 5f;
                }
                else if (accent == 3) // ơ, ư (dấu móc)
                {
                    float hx = g.Right - 2f;
                    float hy = isUpper ? -11f : -4f;
                    strokes.Add(new[]
                    {
                        new PointF(hx, hy - 3f),
                        new PointF(hx + 2.5f, hy - 2f),
                        new PointF(hx + 2f, hy + 0.5f),
                        new PointF(hx, hy + 1f)
                    });
                }

                // Thêm dấu thanh Tiếng Việt (1 nét)
                switch (tone)
                {
                    case 1: // huyền
                        strokes.Add(new[] { new PointF(cx - 2.5f, topY - 4.5f), new PointF(cx + 1.5f, topY - 1.5f) });
                        break;
                    case 2: // sắc
                        strokes.Add(new[] { new PointF(cx - 1.5f, topY - 1.5f), new PointF(cx + 2.5f, topY - 4.5f) });
                        break;
                    case 3: // hỏi
                        strokes.Add(new[]
                        {
                            new PointF(cx - 1.5f, topY - 5f),
                            new PointF(cx + 1.5f, topY - 4.5f),
                            new PointF(cx + 1f, topY - 2.5f),
                            new PointF(cx - 0.5f, topY - 1.5f)
                        });
                        break;
                    case 4: // ngã
                        strokes.Add(new[]
                        {
                            new PointF(cx - 3f, topY - 2.5f),
                            new PointF(cx - 1f, topY - 4.5f),
                            new PointF(cx + 1f, topY - 2f),
                            new PointF(cx + 3f, topY - 4f)
                        });
                        break;
                    case 5: // nặng
                        strokes.Add(new[]
                        {
                            new PointF(cx - 0.6f, 13.5f),
                            new PointF(cx + 0.6f, 13.5f),
                            new PointF(cx + 0.6f, 14.5f),
                            new PointF(cx - 0.6f, 14.5f),
                            new PointF(cx - 0.6f, 13.5f)
                        });
                        break;
                }

                var res = new GlyphDef { Left = g.Left, Right = g.Right, Strokes = strokes };
                GlyphCache[ch] = res;
                return res;
            }
        }

        // =====================================================================
        //  Tạo danh sách SubPath cho VectorText (hỗ trợ Hershey 1 nét, Arc, Weld)
        // =====================================================================
        public static List<SubPath> BuildTextSubPaths(VectorText t, float mmPerPx = 0.25f, float curveTolPx = 0.25f)
        {
            if (string.IsNullOrEmpty(t.TextContent)) return new List<SubPath>();

            float emPx = t.FontSize * 96f / 72f;
            float charSpacePx = t.CharSpacingMm / Math.Max(0.01f, mmPerPx);
            bool hasArc = Math.Abs(t.ArcRadiusMm) >= 1f;
            float arcRadiusPx = t.ArcRadiusMm / Math.Max(0.01f, mmPerPx);

            List<SubPath> paths = t.SingleLineFont
                ? BuildHersheyPaths(t, emPx, charSpacePx, hasArc, arcRadiusPx)
                : BuildTrueTypePaths(t, emPx, charSpacePx, hasArc, arcRadiusPx, curveTolPx);

            // Nếu đang ở chế độ tùy chỉnh (1 nét, uốn cong, hàn chữ, giãn chữ), căn khung bao khớp (t.X, t.Y, t.Width, t.Height)
            if (paths.Count > 0 && t.HasCustomPath)
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                bool any = false;
                foreach (var sp in paths)
                {
                    foreach (var p in sp.Points)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                        any = true;
                    }
                }
                if (any)
                {
                    float dx = t.X - minX;
                    float dy = t.Y - minY;
                    if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f)
                    {
                        foreach (var sp in paths)
                        {
                            for (int i = 0; i < sp.Points.Count; i++)
                                sp.Points[i] = new PointF(sp.Points[i].X + dx, sp.Points[i].Y + dy);
                        }
                    }
                    t.Width = Math.Max(4f, maxX - minX);
                    t.Height = Math.Max(4f, maxY - minY);
                }
            }

            return paths;
        }

        private static List<SubPath> BuildHersheyPaths(VectorText t, float emPx, float charSpacePx, bool hasArc, float arcRadiusPx)
        {
            float scale = emPx / BaseCapHeight;
            float shear = t.FontItalic ? -0.22f : 0f;
            var lines = t.TextContent.Replace("\r\n", "\n").Split('\n');

            // Thu thập nét theo toạ độ cục bộ (gốc 0,0 tại góc trên-trái dòng đầu)
            var rawStrokes = new List<List<PointF>>();
            float maxLineW = 0f;
            float lineStep = emPx * 1.35f;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float curX = 0f;
                float baseY = li * lineStep + 14f * scale; // đẩy đỉnh chữ xuống dưới Y=0

                foreach (char ch in line)
                {
                    GlyphDef g = GetGlyph(ch);
                    float adv = (g.Right - g.Left) * scale + charSpacePx;
                    float offsetX = curX - g.Left * scale;

                    foreach (var st in g.Strokes)
                    {
                        var pts = new List<PointF>(st.Length);
                        foreach (var p in st)
                        {
                            float lx = offsetX + (p.X + p.Y * shear) * scale;
                            float ly = baseY + p.Y * scale;
                            pts.Add(new PointF(lx, ly));
                        }
                        if (pts.Count >= 2) rawStrokes.Add(pts);

                        // Nếu in đậm (Bold) trong chế độ 1 nét: thêm nét song song dịch nhẹ để đậm nét
                        if (t.FontBold && pts.Count >= 2)
                        {
                            float boldShift = Math.Max(0.6f, scale * 0.65f);
                            var pts2 = pts.Select(p => new PointF(p.X + boldShift, p.Y)).ToList();
                            rawStrokes.Add(pts2);
                        }
                    }
                    curX += Math.Max(scale * 2f, adv);
                }
                if (curX > maxLineW) maxLineW = curX;
            }

            var result = new List<SubPath>();
            float baselineY = 14f * scale + 9f * scale; // chân chữ (y = +9 trong Hershey)

            foreach (var st in rawStrokes)
            {
                // Chia nhỏ đoạn thẳng nếu uốn cong theo cung tròn để đường cong mượt
                List<PointF> densified = hasArc ? SubdividePolyline(st, 2.5f) : st;
                var transformed = new List<PointF>(densified.Count);
                foreach (var p in densified)
                {
                    PointF wp = hasArc
                        ? MapPointToArc(p, maxLineW * 0.5f, baselineY, arcRadiusPx, t.X, t.Y)
                        : new PointF(t.X + p.X, t.Y + p.Y);
                    transformed.Add(wp);
                }
                result.Add(new SubPath { Points = transformed, IsClosed = false });
            }

            return result;
        }

        private static List<SubPath> BuildTrueTypePaths(VectorText t, float emPx, float charSpacePx, bool hasArc, float arcRadiusPx, float curveTolPx)
        {
            FontFamily fam;
            try { fam = new FontFamily(t.FontName); }
            catch { fam = new FontFamily("Arial"); }

            using (fam)
            {
                FontStyle want = (t.FontBold ? FontStyle.Bold : 0) | (t.FontItalic ? FontStyle.Italic : 0);
                FontStyle style = fam.IsStyleAvailable(want) ? want
                                : fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                                : fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                                : FontStyle.Italic;

                bool needPerChar = hasArc || Math.Abs(charSpacePx) > 0.05f || t.WeldText;

                if (!needPerChar)
                {
                    using var gp = new GraphicsPath();
                    gp.AddString(t.TextContent, fam, (int)style, emPx, new PointF(t.X, t.Y), StringFormat.GenericDefault);
                    return ExtractSubPaths(gp, curveTolPx);
                }

                // Xử lý từng ký tự để hỗ trợ giãn cách chữ, uốn cong theo cung, và hàn chữ (Weld)
                var charShapes = new List<List<SubPath>>();
                var lines = t.TextContent.Replace("\r\n", "\n").Split('\n');
                float lineStep = emPx * 1.25f;

                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                using var measureFont = new Font(fam, emPx, style, GraphicsUnit.Pixel);
                var sf = new StringFormat(StringFormat.GenericTypographic)
                {
                    FormatFlags = StringFormatFlags.MeasureTrailingSpaces
                };

                // Tính tổng chiều rộng dòng đầu để căn tâm cung tròn
                float totalW = 0f;
                if (lines.Length > 0)
                {
                    foreach (char c in lines[0])
                    {
                        float cw = c == ' ' ? emPx * 0.32f : g.MeasureString(c.ToString(), measureFont, 10000, sf).Width;
                        totalW += cw + charSpacePx;
                    }
                }

                float baselineY = emPx * 0.85f;

                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li];
                    float curX = 0f;
                    float curY = li * lineStep;

                    foreach (char c in line)
                    {
                        float cw = c == ' ' ? emPx * 0.32f : g.MeasureString(c.ToString(), measureFont, 10000, sf).Width;
                        if (!char.IsWhiteSpace(c))
                        {
                            using var gp = new GraphicsPath();
                            gp.AddString(c.ToString(), fam, (int)style, emPx, new PointF(0f, 0f), sf);
                            var sub = ExtractSubPaths(gp, curveTolPx);

                            if (hasArc)
                            {
                                // Xoay và đặt ký tự vuông góc với tiếp tuyến cung tròn
                                float charCenterX = curX + cw * 0.5f;
                                foreach (var sp in sub)
                                {
                                    for (int i = 0; i < sp.Points.Count; i++)
                                    {
                                        PointF lp = new PointF(curX + sp.Points[i].X, curY + sp.Points[i].Y);
                                        sp.Points[i] = MapPointToArc(lp, totalW * 0.5f, baselineY, arcRadiusPx, t.X, t.Y);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var sp in sub)
                                {
                                    for (int i = 0; i < sp.Points.Count; i++)
                                    {
                                        sp.Points[i] = new PointF(t.X + curX + sp.Points[i].X, t.Y + curY + sp.Points[i].Y);
                                    }
                                }
                            }

                            if (sub.Count > 0) charShapes.Add(sub);
                        }
                        curX += cw + charSpacePx;
                    }
                }

                if (t.WeldText && charShapes.Count > 1)
                {
                    return WeldShapeGroups(charShapes);
                }

                return charShapes.SelectMany(s => s).ToList();
            }
        }

        // Ánh xạ 1 điểm (x, y) từ hệ tọa độ dòng chữ phẳng sang cung tròn bán kính R (px)
        // R > 0: uốn vòm xuống (tâm cung ở dưới chữ); R < 0: uốn võng lên (tâm cung ở trên chữ)
        private static PointF MapPointToArc(PointF p, float centerX, float baselineY, float arcRadiusPx, float originX, float originY)
        {
            float dx = p.X - centerX;
            float dy = baselineY - p.Y; // khoảng cách từ chân chữ hướng lên trên
            float absR = Math.Max(8f, Math.Abs(arcRadiusPx));

            if (arcRadiusPx > 0)
            {
                // Tâm cung nằm phía dưới chân chữ một đoạn absR
                float theta = dx / absR; // góc lệch so với trục đứng (radian)
                float r = Math.Max(1f, absR + dy);
                float wx = originX + centerX + r * MathF.Sin(theta);
                float wy = originY + baselineY + absR - r * MathF.Cos(theta);
                return new PointF(wx, wy);
            }
            else
            {
                // Tâm cung nằm phía trên chân chữ một đoạn absR
                float theta = dx / absR;
                float r = Math.Max(1f, absR - dy);
                float wx = originX + centerX + r * MathF.Sin(theta);
                float wy = originY + baselineY - absR + r * MathF.Cos(theta);
                return new PointF(wx, wy);
            }
        }

        private static List<PointF> SubdividePolyline(List<PointF> pts, float maxSegLen)
        {
            var res = new List<PointF>();
            if (pts.Count == 0) return res;
            res.Add(pts[0]);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                PointF a = pts[i], b = pts[i + 1];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)MathF.Ceiling(dist / maxSegLen));
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    res.Add(new PointF(a.X + dx * t, a.Y + dy * t));
                }
            }
            return res;
        }

        public static List<SubPath> ExtractSubPaths(GraphicsPath gp, float flattenTol = 0.25f)
        {
            var res = new List<SubPath>();
            if (gp.PointCount < 2) return res;

            using var flatPath = (GraphicsPath)gp.Clone();
            using var m = new Matrix();
            flatPath.Flatten(m, Math.Max(0.02f, flattenTol));

            if (flatPath.PointCount < 2) return res;
            var pts = flatPath.PathPoints;
            using var iter = new GraphicsPathIterator(flatPath);
            while (iter.NextSubpath(out int start, out int end, out bool closed) > 0)
            {
                var list = new List<PointF>(end - start + 1);
                for (int i = start; i <= end; i++)
                {
                    if (list.Count == 0 || Distance(list[^1], pts[i]) > 0.02f)
                        list.Add(pts[i]);
                }
                if (list.Count >= 2 && closed && Distance(list[0], list[^1]) <= 0.05f)
                    list.RemoveAt(list.Count - 1);

                if (list.Count >= 2)
                    res.Add(new SubPath { Points = list, IsClosed = closed });
            }
            return res;
        }

        // =====================================================================
        //  HÀN LIỀN NÉT (BOOLEAN UNION / WELD) CÁC NHÓM ĐƯỜNG VIỀN KÍN
        // =====================================================================
        private struct Seg
        {
            public PointF A;
            public PointF B;
            public int GroupIdx;
        }

        /// <summary>
        /// Hợp nhất (Boolean Union) nhiều nhóm đường контур (mỗi nhóm là 1 chữ cái hoặc 1 đối tượng)
        /// bằng cách tìm giao điểm đoạn thẳng, loại bỏ các đoạn nằm bên trong nhóm khác, và nối lại thành đường cắt kín.
        /// </summary>
        public static List<SubPath> WeldShapeGroups(List<List<SubPath>> groups)
        {
            if (groups.Count == 0) return new List<SubPath>();
            if (groups.Count == 1) return groups[0];

            // Chuẩn hóa chiều đa giác trong từng nhóm: đường bao ngoài cùng chiều kim đồng hồ (diện tích > 0 trên màn hình), lỗ ngược chiều
            var cleanGroups = new List<List<List<PointF>>>();
            foreach (var grp in groups)
            {
                var loops = new List<List<PointF>>();
                foreach (var sp in grp)
                {
                    if (sp.Points.Count < 3) continue;
                    var pts = new List<PointF>(sp.Points);
                    if (Distance(pts[0], pts[^1]) < 0.05f) pts.RemoveAt(pts.Count - 1);
                    if (pts.Count >= 3) loops.Add(pts);
                }
                NormalizeGroupOrientation(loops);
                if (loops.Count > 0) cleanGroups.Add(loops);
            }

            if (cleanGroups.Count <= 1)
                return groups.SelectMany(g => g).ToList();

            // Tạo danh sách tất cả các cạnh có hướng
            var rawSegs = new List<Seg>();
            var groupBounds = new RectangleF[cleanGroups.Count];

            for (int gi = 0; gi < cleanGroups.Count; gi++)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var loop in cleanGroups[gi])
                {
                    int n = loop.Count;
                    for (int i = 0; i < n; i++)
                    {
                        PointF a = loop[i];
                        PointF b = loop[(i + 1) % n];
                        if (Distance(a, b) < 0.01f) continue;
                        rawSegs.Add(new Seg { A = a, B = b, GroupIdx = gi });
                        if (a.X < minX) minX = a.X; if (a.Y < minY) minY = a.Y;
                        if (a.X > maxX) maxX = a.X; if (a.Y > maxY) maxY = a.Y;
                    }
                }
                groupBounds[gi] = new RectangleF(minX - 1f, minY - 1f, Math.Max(1f, maxX - minX + 2f), Math.Max(1f, maxY - minY + 2f));
            }

            // Tìm giao điểm giữa các cạnh thuộc các nhóm khác nhau để chia nhỏ cạnh
            int segCount = rawSegs.Count;
            var splitParams = new List<float>[segCount];
            for (int i = 0; i < segCount; i++) splitParams[i] = new List<float> { 0f, 1f };

            for (int i = 0; i < segCount; i++)
            {
                var s1 = rawSegs[i];
                for (int j = i + 1; j < segCount; j++)
                {
                    var s2 = rawSegs[j];
                    if (s1.GroupIdx == s2.GroupIdx) continue;
                    if (!SegBoundsOverlap(s1.A, s1.B, s2.A, s2.B)) continue;

                    if (TryIntersectSegments(s1.A, s1.B, s2.A, s2.B, out float t1, out float t2))
                    {
                        if (t1 > 0.001f && t1 < 0.999f) splitParams[i].Add(t1);
                        if (t2 > 0.001f && t2 < 0.999f) splitParams[j].Add(t2);
                    }
                }
            }

            // Giữ lại các đoạn con KHÔNG nằm bên trong bất kỳ nhóm nào khác
            var keptSegs = new List<(PointF A, PointF B)>();
            for (int i = 0; i < segCount; i++)
            {
                var s = rawSegs[i];
                var ts = splitParams[i];
                ts.Sort();

                for (int k = 0; k < ts.Count - 1; k++)
                {
                    float t0 = ts[k], t1 = ts[k + 1];
                    if (t1 - t0 < 0.001f) continue;

                    PointF p0 = Lerp(s.A, s.B, t0);
                    PointF p1 = Lerp(s.A, s.B, t1);
                    if (Distance(p0, p1) < 0.01f) continue;

                    PointF mid = new PointF((p0.X + p1.X) * 0.5f, (p0.Y + p1.Y) * 0.5f);
                    bool insideOther = false;
                    for (int gj = 0; gj < cleanGroups.Count; gj++)
                    {
                        if (gj == s.GroupIdx) continue;
                        if (!groupBounds[gj].Contains(mid)) continue;
                        if (IsPointInsideGroup(mid, cleanGroups[gj]))
                        {
                            insideOther = true;
                            break;
                        }
                    }

                    if (!insideOther)
                        keptSegs.Add((p0, p1));
                }
            }

            // Nối các đoạn thẳng còn lại thành các SubPath khép kín
            return StitchSegments(keptSegs);
        }

        private static void NormalizeGroupOrientation(List<List<PointF>> loops)
        {
            for (int i = 0; i < loops.Count; i++)
            {
                PointF testPt = loops[i][0];
                int depth = 0;
                for (int j = 0; j < loops.Count; j++)
                {
                    if (i == j) continue;
                    if (PointInPolygon(testPt, loops[j])) depth++;
                }
                bool isOuter = (depth % 2) == 0;
                double area = SignedArea(loops[i]);
                // Quy ước: đường ngoài có area > 0, đường lỗ có area < 0
                if ((isOuter && area < 0) || (!isOuter && area > 0))
                    loops[i].Reverse();
            }
        }

        private static double SignedArea(List<PointF> pts)
        {
            double sum = 0;
            for (int i = 0, n = pts.Count; i < n; i++)
            {
                PointF a = pts[i], b = pts[(i + 1) % n];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return sum * 0.5;
        }

        private static bool IsPointInsideGroup(PointF p, List<List<PointF>> groupLoops)
        {
            int hits = 0;
            foreach (var loop in groupLoops)
            {
                if (PointInPolygon(p, loop)) hits++;
            }
            return (hits % 2) == 1;
        }

        private static bool PointInPolygon(PointF p, List<PointF> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                PointF pi = poly[i], pj = poly[j];
                if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-12f) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool SegBoundsOverlap(PointF a1, PointF b1, PointF a2, PointF b2)
        {
            float minX1 = Math.Min(a1.X, b1.X) - 0.05f, maxX1 = Math.Max(a1.X, b1.X) + 0.05f;
            float minY1 = Math.Min(a1.Y, b1.Y) - 0.05f, maxY1 = Math.Max(a1.Y, b1.Y) + 0.05f;
            float minX2 = Math.Min(a2.X, b2.X), maxX2 = Math.Max(a2.X, b2.X);
            float minY2 = Math.Min(a2.Y, b2.Y), maxY2 = Math.Max(a2.Y, b2.Y);
            return maxX1 >= minX2 && maxX2 >= minX1 && maxY1 >= minY2 && maxY2 >= minY1;
        }

        private static bool TryIntersectSegments(PointF p1, PointF p2, PointF p3, PointF p4, out float t, out float u)
        {
            t = u = 0f;
            float rX = p2.X - p1.X, rY = p2.Y - p1.Y;
            float sX = p4.X - p3.X, sY = p4.Y - p3.Y;
            float rxs = rX * sY - rY * sX;
            if (Math.Abs(rxs) < 1e-6f) return false;

            float qpX = p3.X - p1.X, qpY = p3.Y - p1.Y;
            t = (qpX * sY - qpY * sX) / rxs;
            u = (qpX * rY - qpY * rX) / rxs;
            return t >= -0.001f && t <= 1.001f && u >= -0.001f && u <= 1.001f;
        }

        private static List<SubPath> StitchSegments(List<(PointF A, PointF B)> segs)
        {
            var result = new List<SubPath>();
            int n = segs.Count;
            if (n == 0) return result;

            bool[] used = new bool[n];
            const float snapTol = 0.25f;

            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                used[i] = true;

                var pts = new List<PointF> { segs[i].A, segs[i].B };
                while (true)
                {
                    PointF tail = pts[^1];
                    if ( pts.Count >= 3 && Distance(tail, pts[0]) <= snapTol)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        break;
                    }

                    int bestIdx = -1;
                    float bestDist = snapTol;
                    for (int j = 0; j < n; j++)
                    {
                        if (used[j]) continue;
                        float d = Distance(tail, segs[j].A);
                        if (d <= bestDist)
                        {
                            bestDist = d;
                            bestIdx = j;
                        }
                    }

                    if (bestIdx < 0)
                    {
                        // Thử nối ngược nếu cần
                        for (int j = 0; j < n; j++)
                        {
                            if (used[j]) continue;
                            float d = Distance(tail, segs[j].B);
                            if (d <= bestDist)
                            {
                                bestDist = d;
                                bestIdx = j;
                                used[bestIdx] = true;
                                pts.Add(segs[bestIdx].A);
                                goto NextStep;
                            }
                        }
                        break;
                    }

                    used[bestIdx] = true;
                    pts.Add(segs[bestIdx].B);
                NextStep:;
                }

                if (pts.Count >= 3)
                {
                    result.Add(new SubPath
                    {
                        Points = SimplifyCollinear(pts, 0.04f),
                        IsClosed = true
                    });
                }
            }

            return result;
        }

        private static List<PointF> SimplifyCollinear(List<PointF> pts, float tol)
        {
            if (pts.Count <= 4) return pts;
            var res = new List<PointF>(pts.Count);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                PointF prev = res.Count > 0 ? res[^1] : pts[(i - 1 + n) % n];
                PointF cur = pts[i];
                PointF next = pts[(i + 1) % n];
                if (PointToSegmentDistance(cur, prev, next) > tol)
                    res.Add(cur);
            }
            return res.Count >= 3 ? res : pts;
        }

        public static float PointToSegmentDistance(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-8f) return Distance(p, a);
            float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
            float projX = a.X + t * dx, projY = a.Y + t * dy;
            return Distance(p, new PointF(projX, projY));
        }

        private static PointF Lerp(PointF a, PointF b, float t) =>
            new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        // =====================================================================
        //  CHUYỂN ĐỔI HÌNH CƠ BẢN / CHỮ THÀNH VectorPathItem (ĐỂ SỬA ĐIỂM NÚT)
        // =====================================================================
        public static VectorPathItem? ConvertToPathItem(Vectoritem it, float mmPerPx = 0.25f)
        {
            var pathItem = new VectorPathItem
            {
                Name = it.Name + " (Đường dẫn)",
                LayerId = it.LayerId,
                LaserPower = it.LaserPower,
                LaserSpeed = it.LaserSpeed,
                Passes = it.Passes,
                FillMode = it.FillMode,
                FillIntervalMm = it.FillIntervalMm,
                FillAngle = it.FillAngle,
                FillCross = it.FillCross,
                FillBidir = it.FillBidir,
                Hidden = it.Hidden,
                Locked = it.Locked,
                GroupId = it.GroupId
            };

            List<SubPath> subPaths = ExtractItemSubPaths(it, mmPerPx);
            if (subPaths.Count == 0) return null;

            pathItem.SubPaths = subPaths;
            pathItem.RecalculateBounds();
            return pathItem;
        }

        public static List<SubPath> ExtractItemSubPaths(Vectoritem it, float mmPerPx = 0.25f)
        {
            var subPaths = new List<SubPath>();

            switch (it)
            {
                case VectorPathItem vpi:
                    foreach (var sp in vpi.SubPaths)
                        subPaths.Add(new SubPath { Points = new List<PointF>(sp.Points), IsClosed = sp.IsClosed });
                    break;

                case VectorRectangle r:
                    subPaths.Add(new SubPath
                    {
                        IsClosed = true,
                        Points = new List<PointF>
                        {
                            new(r.X, r.Y),
                            new(r.X + r.Width, r.Y),
                            new(r.X + r.Width, r.Y + r.Height),
                            new(r.X, r.Y + r.Height)
                        }
                    });
                    break;

                case VectorEllipse el:
                    {
                        int n = 48;
                        float cx = el.X + el.Width / 2f, cy = el.Y + el.Height / 2f;
                        float rx = el.Width / 2f, ry = el.Height / 2f;
                        var pts = new List<PointF>(n);
                        for (int i = 0; i < n; i++)
                        {
                            double a = 2 * Math.PI * i / n;
                            pts.Add(new PointF(cx + (float)(rx * Math.Cos(a)), cy + (float)(ry * Math.Sin(a))));
                        }
                        subPaths.Add(new SubPath { Points = pts, IsClosed = true });
                    }
                    break;

                case VectorPolygon poly:
                    subPaths.Add(new SubPath
                    {
                        Points = poly.GetPoints().ToList(),
                        IsClosed = true
                    });
                    break;

                case VectorLine ln:
                    subPaths.Add(new SubPath
                    {
                        Points = new List<PointF> { new(ln.X, ln.Y), new(ln.X2, ln.Y2) },
                        IsClosed = false
                    });
                    break;

                case VectorText vt:
                    subPaths = BuildTextSubPaths(vt, mmPerPx, 0.25f);
                    break;
            }

            // Áp dụng phép xoay / lật của đối tượng trực tiếp vào tọa độ các điểm nút
            if (it is not VectorLine && (it.Rotation != 0f || it.FlipX || it.FlipY))
            {
                float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
                double rad = it.Rotation * Math.PI / 180.0;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);

                foreach (var sp in subPaths)
                {
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        PointF p = sp.Points[i];
                        float dx = p.X - cx, dy = p.Y - cy;
                        if (it.FlipX) dx = -dx;
                        if (it.FlipY) dy = -dy;
                        float rx = cx + dx * cos - dy * sin;
                        float ry = cy + dx * sin + dy * cos;
                        sp.Points[i] = new PointF(rx, ry);
                    }
                }
            }

            return subPaths;
        }

        /// <summary>
        /// Hàn (Weld / Boolean Union) nhiều đối tượng đang chọn thành 1 VectorPathItem duy nhất.
        /// </summary>
        public static VectorPathItem? WeldItemsToPath(List<Vectoritem> items, float mmPerPx = 0.25f)
        {
            if (items == null || items.Count == 0) return null;

            var groups = new List<List<SubPath>>();
            var openPaths = new List<SubPath>();

            foreach (var it in items)
            {
                var sps = ExtractItemSubPaths(it, mmPerPx);
                var closed = sps.Where(s => s.IsClosed && s.Points.Count >= 3).ToList();
                var open = sps.Where(s => !s.IsClosed || s.Points.Count < 3).ToList();
                if (closed.Count > 0) groups.Add(closed);
                openPaths.AddRange(open);
            }

            var welded = WeldShapeGroups(groups);
            welded.AddRange(openPaths);
            if (welded.Count == 0) return null;

            var first = items[0];
            var res = new VectorPathItem
            {
                Name = "Đường hàn (Welded)",
                LayerId = first.LayerId,
                LaserPower = first.LaserPower,
                LaserSpeed = first.LaserSpeed,
                Passes = first.Passes,
                FillMode = first.FillMode,
                FillIntervalMm = first.FillIntervalMm,
                FillAngle = first.FillAngle,
                FillCross = first.FillCross,
                FillBidir = first.FillBidir,
                SubPaths = welded
            };
            res.RecalculateBounds();
            return res;
        }
    }
}

// =====================================================================
// PHẦN: Tách nét ảnh (Potrace)   (trước đây là file ImageTracer.cs)
// =====================================================================
namespace test01_1014
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Linq;
    using System.Windows.Forms;
    using OpenCvSharp;
    using OpenCvSharp.Extensions;
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using Font = System.Drawing.Font;
    using SubPath = VectorPathItem.SubPath;

    /// <summary>
    /// Tham số tách nét ảnh Raster sang Vector (Potrace / Contour-to-Vector).
    /// </summary>
    public class TraceOptions
    {
        public int Threshold { get; set; } = 128;          // Ngưỡng đen/trắng (0..255)
        public bool AutoOtsu { get; set; } = false;        // Tự động tính ngưỡng tối ưu theo Otsu
        public bool Invert { get; set; } = false;          // Đảo ngược vùng sáng / tối
        public int BlurRadius { get; set; } = 1;           // Làm mượt ảnh trước khi tách biên (0..5)
        public double TurdSizePx { get; set; } = 10.0;     // Loại bỏ đốm nhiễu nhỏ có diện tích < TurdSizePx (px²)
        public double AlphaMax { get; set; } = 1.0;        // Ngưỡng uốn cong Potrace (0 = đa giác sắc cạnh, 1.0..1.33 = làm mượt Bezier)
        public double CurveTolerance { get; set; } = 1.1;  // Độ thu gọn điểm nút (Douglas-Peucker epsilon, px)
        public bool KeepOriginalImage { get; set; } = false;
    }

    /// <summary>
    /// Bộ chuyển đổi ảnh Bitmap/Raster (logo, bản vẽ tay, hình chụp) thành đường cắt VectorPathItem mượt
    /// theo thuật toán Potrace (phát hiện biên lồng nhau + lọc nhiễu TurdSize + xấp xỉ đa giác tối ưu + làm mượt góc Bezier).
    /// </summary>
    public static class ImageTracer
    {
        /// <summary>
        /// Tách nét từ đối tượng VectorImage trên canvas thành VectorPathItem khớp đúng vị trí, kích thước, góc xoay.
        /// </summary>
        public static VectorPathItem? TraceImageItem(VectorImage imgItem, TraceOptions opt)
        {
            if (imgItem.LoadedImage == null) return null;

            var rawPaths = TraceBitmap(imgItem.LoadedImage, opt, out int bmpW, out int bmpH);
            if (rawPaths.Count == 0 || bmpW <= 0 || bmpH <= 0) return null;

            float sx = imgItem.Width / bmpW;
            float sy = imgItem.Height / bmpH;
            float ox = imgItem.X;
            float oy = imgItem.Y;

            var worldPaths = new List<SubPath>(rawPaths.Count);
            foreach (var sp in rawPaths)
            {
                var pts = new List<PointF>(sp.Points.Count);
                foreach (var p in sp.Points)
                {
                    pts.Add(new PointF(ox + p.X * sx, oy + p.Y * sy));
                }
                if (pts.Count >= 3)
                {
                    worldPaths.Add(new SubPath { Points = pts, IsClosed = true });
                }
            }

            if (worldPaths.Count == 0) return null;

            var pathItem = new VectorPathItem
            {
                Name = imgItem.Name + " (Vector Trace)",
                LayerId = imgItem.LayerId,
                LaserPower = imgItem.LaserPower,
                LaserSpeed = imgItem.LaserSpeed,
                Passes = imgItem.Passes,
                Rotation = imgItem.Rotation,
                FlipX = imgItem.FlipX,
                FlipY = imgItem.FlipY,
                FillMode = imgItem.FillMode,
                FillIntervalMm = imgItem.FillIntervalMm,
                FillAngle = imgItem.FillAngle,
                FillCross = imgItem.FillCross,
                FillBidir = imgItem.FillBidir,
                SubPaths = worldPaths
            };
            pathItem.RecalculateBounds();
            return pathItem;
        }

        /// <summary>
        /// Tách nét một ảnh System.Drawing.Image thành danh sách SubPath trong hệ tọa độ ảnh.
        /// </summary>
        public static List<SubPath> TraceBitmap(Image srcImage, TraceOptions opt, out int workW, out int workH)
        {
            workW = srcImage.Width;
            workH = srcImage.Height;
            var result = new List<SubPath>();
            if (workW <= 1 || workH <= 1) return result;

            // Giới hạn kích thước xử lý tối đa 1800px để đảm bảo tốc độ phản hồi thời gian thực
            float scale = Math.Min(1f, 1800f / Math.Max(workW, workH));
            int w = Math.Max(2, (int)Math.Round(workW * scale));
            int h = Math.Max(2, (int)Math.Round(workH * scale));
            float invScale = 1f / scale;

            using var flatBmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(flatBmp))
            {
                g.Clear(opt.Invert ? Color.Black : Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(srcImage, 0, 0, w, h);
            }

            using var bgr = BitmapConverter.ToMat(flatBmp);
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            using var blurred = new Mat();
            if (opt.BlurRadius > 0)
            {
                int k = 2 * Math.Clamp(opt.BlurRadius, 1, 8) + 1;
                Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(k, k), 0);
            }
            else
            {
                gray.CopyTo(blurred);
            }

            using var bin = new Mat();
            ThresholdTypes thType = opt.Invert ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv;
            if (opt.AutoOtsu) thType |= ThresholdTypes.Otsu;
            Cv2.Threshold(blurred, bin, Math.Clamp(opt.Threshold, 0, 255), 255, thType);

            // Tìm toàn bộ biên ngoài + lỗ bên trong (RetrievalModes.List lấy đủ cả đường bao ngoài lẫn lỗ)
            Cv2.FindContours(bin, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] _,
                             RetrievalModes.List, ContourApproximationModes.ApproxNone);

            double minArea = Math.Max(1.0, opt.TurdSizePx * scale * scale);
            double eps = Math.Max(0.35, opt.CurveTolerance * scale);
            float alphaMax = (float)Math.Clamp(opt.AlphaMax, 0.0, 1.34);

            foreach (var rawContour in contours)
            {
                if (rawContour.Length < 4) continue;
                double area = Math.Abs(Cv2.ContourArea(rawContour));
                if (area < minArea) continue;

                // Bước 1 (Potrace Polygonization): rút gọn bậc thang pixel thành đa giác tối ưu
                OpenCvSharp.Point[] poly = Cv2.ApproxPolyDP(rawContour, eps, closed: true);
                if (poly.Length < 3) continue;

                var polyPts = new List<PointF>(poly.Length);
                foreach (var pt in poly)
                {
                    polyPts.Add(new PointF(pt.X * invScale, pt.Y * invScale));
                }

                // Bước 2 (Potrace Curve Smoothing): phát hiện góc nhọn vs uốn cong Bezier mượt
                List<PointF> smoothed = alphaMax > 0.02f
                    ? SmoothPolygonPotrace(polyPts, alphaMax)
                    : polyPts;

                if (smoothed.Count >= 3)
                {
                    result.Add(new SubPath { Points = smoothed, IsClosed = true });
                }
            }

            return result;
        }

        /// <summary>
        /// Làm mượt đa giác theo nguyên lý Potrace:
        /// Tại mỗi đỉnh P_i, nối trung điểm M_{i-1} của cạnh trước với trung điểm M_i của cạnh sau.
        /// Dựa vào góc gập tại P_i và hệ số alphaMax:
        /// - Nếu là góc nhọn thực sự (alpha > alphaMax): giữ nguyên đỉnh sắc nét P_i.
        /// - Nếu là đoạn cong mượt: nội suy đường cong Bezier bậc 3 đi qua kiểm soát Potrace.
        /// </summary>
        private static List<PointF> SmoothPolygonPotrace(List<PointF> poly, float alphaMax)
        {
            int n = poly.Count;
            if (n < 3) return poly;

            var outPts = new List<PointF>(n * 4);

            for (int i = 0; i < n; i++)
            {
                PointF prev = poly[(i - 1 + n) % n];
                PointF cur = poly[i];
                PointF next = poly[(i + 1) % n];

                PointF m0 = new PointF((prev.X + cur.X) * 0.5f, (prev.Y + cur.Y) * 0.5f);
                PointF m1 = new PointF((cur.X + next.X) * 0.5f, (cur.Y + next.Y) * 0.5f);

                // Tính góc lệch giữa 2 vector (prev -> cur) và (cur -> next)
                float v1x = cur.X - prev.X, v1y = cur.Y - prev.Y;
                float v2x = next.X - cur.X, v2y = next.Y - cur.Y;
                float len1 = MathF.Sqrt(v1x * v1x + v1y * v1y);
                float len2 = MathF.Sqrt(v2x * v2x + v2y * v2y);

                if (len1 < 0.01f || len2 < 0.01f)
                {
                    outPts.Add(cur);
                    continue;
                }

                float cosAngle = Math.Clamp((v1x * v2x + v1y * v2y) / (len1 * len2), -1f, 1f);
                // cosAngle = 1: thẳng hàng; cosAngle = 0: vuông góc 90 độ; cosAngle < 0: góc nhọn gập ngược
                // Quy đổi sang tham số alpha của Potrace (góc càng nhọn thì alpha càng lớn)
                float turnFactor = (1f - cosAngle) * 0.85f;

                if (turnFactor > alphaMax)
                {
                    // Giữ đỉnh góc sắc nét
                    outPts.Add(m0);
                    outPts.Add(cur);
                }
                else
                {
                    // Tạo đường cong Bezier bậc 3 từ m0 tới m1 ôm theo đỉnh cur
                    float alpha = Math.Clamp(0.55f + 0.25f * alphaMax, 0.4f, 0.88f);
                    PointF c1 = new PointF(m0.X + (cur.X - m0.X) * alpha, m0.Y + (cur.Y - m0.Y) * alpha);
                    PointF c2 = new PointF(m1.X + (cur.X - m1.X) * alpha, m1.Y + (cur.Y - m1.Y) * alpha);

                    float segLen = MathF.Sqrt((m1.X - m0.X) * (m1.X - m0.X) + (m1.Y - m0.Y) * (m1.Y - m0.Y));
                    int steps = Math.Clamp((int)MathF.Ceiling(segLen / 3.5f), 3, 10);

                    for (int s = 0; s < steps; s++)
                    {
                        float t = (float)s / steps;
                        float u = 1f - t;
                        float bx = u * u * u * m0.X + 3f * u * u * t * c1.X + 3f * u * t * t * c2.X + t * t * t * m1.X;
                        float by = u * u * u * m0.Y + 3f * u * u * t * c1.Y + 3f * u * t * t * c2.Y + t * t * t * m1.Y;
                        outPts.Add(new PointF(bx, by));
                    }
                }
            }

            return outPts;
        }
    }

    /// <summary>
    /// Hộp thoại tách nét Vector (Potrace) có xem trước trực quan theo thời gian thực.
    /// </summary>
    public sealed class ImageTraceDialog : Form
    {
        private readonly VectorImage srcItem;
        private readonly PictureBox previewBox;
        private readonly NumericUpDown nudThreshold, nudBlur, nudTurd, nudSmooth, nudTolerance;
        private readonly CheckBox chkOtsu, chkInvert, chkKeepImg, chkShowBg;
        private readonly Label lblStats;
        private readonly System.Windows.Forms.Timer debounceTimer;

        public TraceOptions Options { get; } = new TraceOptions();
        public List<SubPath> PreviewPaths { get; private set; } = new();
        private int previewW = 1, previewH = 1;

        public ImageTraceDialog(VectorImage item)
        {
            srcItem = item;
            Options.Threshold = item.EngraveMode == 2 ? item.Threshold : 128;
            Options.Invert = item.Invert;

            Text = "Tách nét Vector từ ảnh (Potrace / Contour-to-Vector)";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 600);
            MinimumSize = new Size(740, 480);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2,
                SplitterDistance = 570
            };
            Controls.Add(split);

            // Bên trái: khung xem trước đường vector trên nền ảnh
            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 247, 250),
                BorderStyle = BorderStyle.FixedSingle
            };
            previewBox.Paint += PreviewBox_Paint;
            previewBox.Resize += (s, e) => previewBox.Invalidate();
            split.Panel1.Controls.Add(previewBox);

            // Bên phải: bảng thông số Potrace
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true, BackColor = Color.White };
            split.Panel2.Controls.Add(right);

            int y = 10;
            int w = 285;

            Label AddHeader(string title)
            {
                var l = new Label
                {
                    Text = title,
                    Bounds = new Rectangle(10, y, w, 24),
                    BackColor = Color.FromArgb(222, 229, 240),
                    ForeColor = Color.FromArgb(40, 60, 100),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 0, 0, 0)
                };
                right.Controls.Add(l);
                y += 30;
                return l;
            }

            NumericUpDown AddNumRow(string label, decimal min, decimal max, decimal val, int dec, decimal inc)
            {
                var lbl = new Label { Text = label, Bounds = new Rectangle(10, y + 3, 180, 22), Font = new Font("Segoe UI", 9f, FontStyle.Regular) };
                var nud = new NumericUpDown
                {
                    Bounds = new Rectangle(195, y, 100, 26),
                    Minimum = min,
                    Maximum = max,
                    DecimalPlaces = dec,
                    Increment = inc,
                    Value = Math.Clamp(val, min, max)
                };
                nud.ValueChanged += (s, e) => ScheduleTrace();
                right.Controls.Add(lbl);
                right.Controls.Add(nud);
                y += 32;
                return nud;
            }

            CheckBox AddCheckRow(string text, bool isChecked)
            {
                var chk = new CheckBox
                {
                    Text = text,
                    Bounds = new Rectangle(12, y, w, 24),
                    Checked = isChecked,
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular)
                };
                chk.CheckedChanged += (s, e) => ScheduleTrace();
                right.Controls.Add(chk);
                y += 28;
                return chk;
            }

            AddHeader("1. NGƯỠNG TÁCH ĐEN / TRẮNG");
            nudThreshold = AddNumRow("Ngưỡng sáng tối (0-255):", 1, 254, Options.Threshold, 0, 5);
            chkOtsu = AddCheckRow("Tự động lấy ngưỡng tối ưu (Otsu)", false);
            chkOtsu.CheckedChanged += (s, e) => nudThreshold.Enabled = !chkOtsu.Checked;
            chkInvert = AddCheckRow("Đảo ngược vùng sáng / tối", Options.Invert);
            nudBlur = AddNumRow("Khử răng cưa ảnh (Blur 0-5):", 0, 5, 1, 0, 1);

            y += 6;
            AddHeader("2. LÀM MƯỢT POTRACE & LỌC NHIỄU");
            nudTurd = AddNumRow("Lọc bỏ đốm nhiễu < (px²):", 0, 1000, 10, 0, 5);
            nudSmooth = AddNumRow("Độ mượt cong Bezier (0-1.3):", 0m, 1.33m, 1.0m, 2, 0.1m);
            nudTolerance = AddNumRow("Độ thu gọn điểm nút (px):", 0.3m, 8.0m, 1.1m, 1, 0.2m);

            y += 6;
            AddHeader("3. TÙY CHỌN ĐẦU RA");
            chkShowBg = AddCheckRow("Hiện ảnh gốc mờ phía dưới", true);
            chkShowBg.CheckedChanged += (s, e) => previewBox.Invalidate();
            chkKeepImg = AddCheckRow("Giữ lại cả đối tượng ảnh gốc", false);

            y += 6;
            lblStats = new Label
            {
                Bounds = new Rectangle(10, y, w, 46),
                ForeColor = Color.FromArgb(30, 80, 150),
                Text = "Đang tính toán đường vector..."
            };
            right.Controls.Add(lblStats);
            y += 52;

            var btnOk = new Button
            {
                Text = "Tạo đường cắt Vector",
                Bounds = new Rectangle(10, y, 185, 36),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };
            var btnCancel = new Button
            {
                Text = "Hủy",
                Bounds = new Rectangle(202, y, 93, 36),
                DialogResult = DialogResult.Cancel
            };
            right.Controls.Add(btnOk);
            right.Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            debounceTimer = new System.Windows.Forms.Timer { Interval = 80 };
            debounceTimer.Tick += (s, e) =>
            {
                debounceTimer.Stop();
                RunTracePreview();
            };

            Shown += (s, e) => RunTracePreview();
        }

        private void ScheduleTrace()
        {
            if (debounceTimer == null) return;
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        private void RunTracePreview()
        {
            if (srcItem.LoadedImage == null) return;
            Options.Threshold = (int)nudThreshold.Value;
            Options.AutoOtsu = chkOtsu.Checked;
            Options.Invert = chkInvert.Checked;
            Options.BlurRadius = (int)nudBlur.Value;
            Options.TurdSizePx = (double)nudTurd.Value;
            Options.AlphaMax = (double)nudSmooth.Value;
            Options.CurveTolerance = (double)nudTolerance.Value;
            Options.KeepOriginalImage = chkKeepImg.Checked;

            PreviewPaths = ImageTracer.TraceBitmap(srcItem.LoadedImage, Options, out previewW, out previewH);
            int totalNodes = PreviewPaths.Sum(sp => sp.Points.Count);
            lblStats.Text = $"Số đường cắt (SubPaths): {PreviewPaths.Count}\nTổng số điểm nút (Nodes): {totalNodes}";
            previewBox.Invalidate();
        }

        private void PreviewBox_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (srcItem.LoadedImage == null || previewW <= 0 || previewH <= 0) return;

            float pad = 20f;
            float availW = Math.Max(50f, previewBox.Width - pad * 2);
            float availH = Math.Max(50f, previewBox.Height - pad * 2);
            float sc = Math.Min(availW / previewW, availH / previewH);
            float ox = (previewBox.Width - previewW * sc) * 0.5f;
            float oy = (previewBox.Height - previewH * sc) * 0.5f;

            // Khung nền trắng của ảnh
            g.FillRectangle(Brushes.White, ox, oy, previewW * sc, previewH * sc);

            if (chkShowBg.Checked)
            {
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.28f }; // độ mờ 28%
                using var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(srcItem.LoadedImage,
                    new Rectangle((int)ox, (int)oy, (int)(previewW * sc), (int)(previewH * sc)),
                    0, 0, srcItem.LoadedImage.Width, srcItem.LoadedImage.Height, GraphicsUnit.Pixel, ia);
            }

            g.DrawRectangle(Pens.Silver, ox, oy, previewW * sc, previewH * sc);

            // Vẽ các đường biên vector vừa tách được bằng nét đỏ Crimson
            using var pen = new Pen(Color.Crimson, 1.6f) { LineJoin = LineJoin.Round };
            foreach (var sp in PreviewPaths)
            {
                if (sp.Points.Count < 3) continue;
                var pts = new PointF[sp.Points.Count];
                for (int i = 0; i < sp.Points.Count; i++)
                {
                    pts[i] = new PointF(ox + sp.Points[i].X * sc, oy + sp.Points[i].Y * sc);
                }
                g.DrawPolygon(pen, pts);
            }
        }
    }
}

// =====================================================================
// PHẦN: Nhập SVG   (trước đây là file SvgImporter.cs)
// =====================================================================
namespace test01_1014
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    /// <summary>
    /// Bộ phân tích cú pháp và nhập file vector SVG dành cho máy cắt/khắc laser.
    /// Hỗ trợ: <path> (M, L, H, V, C, S, Q, T, A, Z cả hoa lẫn thường),
    /// <polyline>, <polygon>, <line>, <rect>, <circle>, <ellipse>,
    /// nhóm <g> với thuộc tính transform (translate, scale, rotate, matrix).
    /// </summary>
    public static class SvgImporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class SvgImportResult
        {
            public List<VectorPathItem> Items { get; } = new List<VectorPathItem>();
            public float Width { get; set; }
            public float Height { get; set; }
        }

        public static SvgImportResult Import(string filePath, float mmPerPx = 0.25f)
        {
            using var stream = File.OpenRead(filePath);
            return Import(stream, mmPerPx);
        }

        public static SvgImportResult Import(Stream stream, float mmPerPx = 0.25f)
        {
            var res = new SvgImportResult();
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root == null) return res;

            // Xử lý viewBox hoặc width/height của SVG
            float svgW = ParseLength(root.Attribute("width")?.Value, mmPerPx, 400);
            float svgH = ParseLength(root.Attribute("height")?.Value, mmPerPx, 400);

            var vbAttr = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(vbAttr))
            {
                var vbParts = SplitNumbers(vbAttr);
                if (vbParts.Count >= 4)
                {
                    if (svgW <= 0) svgW = vbParts[2];
                    if (svgH <= 0) svgH = vbParts[3];
                }
            }

            res.Width = svgW;
            res.Height = svgH;

            var currentMatrix = new Matrix2D(); // Identity
            ParseContainer(root, currentMatrix, res.Items, mmPerPx);

            return res;
        }

        private static void ParseContainer(XElement container, Matrix2D parentTransform, List<VectorPathItem> output, float mmPerPx)
        {
            foreach (var elem in container.Elements())
            {
                string localName = elem.Name.LocalName.ToLowerInvariant();
                var elemTransform = Matrix2D.Multiply(parentTransform, ParseTransform(elem.Attribute("transform")?.Value));

                switch (localName)
                {
                    case "g":
                        ParseContainer(elem, elemTransform, output, mmPerPx);
                        break;
                    case "path":
                        ParsePathElement(elem, elemTransform, output);
                        break;
                    case "line":
                        ParseLineElement(elem, elemTransform, output);
                        break;
                    case "rect":
                        ParseRectElement(elem, elemTransform, output);
                        break;
                    case "circle":
                        ParseCircleElement(elem, elemTransform, output);
                        break;
                    case "ellipse":
                        ParseEllipseElement(elem, elemTransform, output);
                        break;
                    case "polyline":
                        ParsePolylineElement(elem, elemTransform, output, false);
                        break;
                    case "polygon":
                        ParsePolylineElement(elem, elemTransform, output, true);
                        break;
                }
            }
        }

        private static void ParsePathElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            string? d = elem.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d)) return;

            var subPaths = ParsePathData(d);
            if (subPaths.Count > 0)
            {
                // Áp dụng transform cho từng điểm
                foreach (var sp in subPaths)
                {
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        sp.Points[i] = xform.Transform(sp.Points[i]);
                    }
                }

                string name = elem.Attribute("id")?.Value ?? "SVG Path";
                var item = new VectorPathItem(subPaths, name);
                output.Add(item);
            }
        }

        private static void ParseLineElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float x1 = ParseFloat(elem.Attribute("x1")?.Value);
            float y1 = ParseFloat(elem.Attribute("y1")?.Value);
            float x2 = ParseFloat(elem.Attribute("x2")?.Value);
            float y2 = ParseFloat(elem.Attribute("y2")?.Value);

            var sp = new VectorPathItem.SubPath
            {
                Points = new List<PointF> { xform.Transform(new PointF(x1, y1)), xform.Transform(new PointF(x2, y2)) },
                IsClosed = false
            };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Line"));
        }

        private static void ParseRectElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float x = ParseFloat(elem.Attribute("x")?.Value);
            float y = ParseFloat(elem.Attribute("y")?.Value);
            float w = ParseFloat(elem.Attribute("width")?.Value);
            float h = ParseFloat(elem.Attribute("height")?.Value);
            if (w <= 0 || h <= 0) return;

            var pts = new List<PointF>
            {
                xform.Transform(new PointF(x, y)),
                xform.Transform(new PointF(x + w, y)),
                xform.Transform(new PointF(x + w, y + h)),
                xform.Transform(new PointF(x, y + h))
            };

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = true };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Rect"));
        }

        private static void ParseCircleElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float cx = ParseFloat(elem.Attribute("cx")?.Value);
            float cy = ParseFloat(elem.Attribute("cy")?.Value);
            float r = ParseFloat(elem.Attribute("r")?.Value);
            if (r <= 0) return;

            var pts = ApproximateEllipse(cx, cy, r, r, 64);
            for (int i = 0; i < pts.Count; i++) pts[i] = xform.Transform(pts[i]);

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = true };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Circle"));
        }

        private static void ParseEllipseElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float cx = ParseFloat(elem.Attribute("cx")?.Value);
            float cy = ParseFloat(elem.Attribute("cy")?.Value);
            float rx = ParseFloat(elem.Attribute("rx")?.Value);
            float ry = ParseFloat(elem.Attribute("ry")?.Value);
            if (rx <= 0 || ry <= 0) return;

            var pts = ApproximateEllipse(cx, cy, rx, ry, 64);
            for (int i = 0; i < pts.Count; i++) pts[i] = xform.Transform(pts[i]);

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = true };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Ellipse"));
        }

        private static void ParsePolylineElement(XElement elem, Matrix2D xform, List<VectorPathItem> output, bool closed)
        {
            string? rawPts = elem.Attribute("points")?.Value;
            if (string.IsNullOrWhiteSpace(rawPts)) return;

            var nums = SplitNumbers(rawPts);
            if (nums.Count < 4) return;

            var pts = new List<PointF>();
            for (int i = 0; i + 1 < nums.Count; i += 2)
            {
                pts.Add(xform.Transform(new PointF(nums[i], nums[i + 1])));
            }

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = closed };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? (closed ? "SVG Polygon" : "SVG Polyline")));
        }

        private static List<PointF> ApproximateEllipse(float cx, float cy, float rx, float ry, int segments)
        {
            var pts = new List<PointF>(segments);
            double step = Math.PI * 2 / segments;
            for (int i = 0; i < segments; i++)
            {
                double angle = i * step;
                pts.Add(new PointF(cx + (float)(rx * Math.Cos(angle)), cy + (float)(ry * Math.Sin(angle))));
            }
            return pts;
        }

        #region SVG Path Parser (D attribute)
        public static List<VectorPathItem.SubPath> ParsePathData(string d)
        {
            var subPaths = new List<VectorPathItem.SubPath>();
            var currentPoints = new List<PointF>();
            bool isCurrentClosed = false;

            float curX = 0, curY = 0;
            float startX = 0, startY = 0;
            float lastCtrlX = 0, lastCtrlY = 0;
            char lastCmd = '\0';

            void FinishSubPath()
            {
                if (currentPoints.Count > 1)
                {
                    subPaths.Add(new VectorPathItem.SubPath
                    {
                        Points = new List<PointF>(currentPoints),
                        IsClosed = isCurrentClosed
                    });
                }
                currentPoints.Clear();
                isCurrentClosed = false;
            }

            // Tách tokens: lệnh (chữ cái) hoặc tham số số
            var tokens = TokenizePathData(d);
            int idx = 0;

            while (idx < tokens.Count)
            {
                string tok = tokens[idx];
                char cmd = tok[0];
                if (char.IsLetter(cmd))
                {
                    idx++;
                }
                else
                {
                    // Lặp lại lệnh trước đó nếu không có chữ cái mới (ngoại trừ M -> L hoặc m -> l)
                    if (lastCmd == 'M') cmd = 'L';
                    else if (lastCmd == 'm') cmd = 'l';
                    else cmd = lastCmd;
                }

                bool rel = char.IsLower(cmd);
                char uCmd = char.ToUpperInvariant(cmd);

                switch (uCmd)
                {
                    case 'M':
                        {
                            if (currentPoints.Count > 0) FinishSubPath();
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }
                            curX = startX = x;
                            curY = startY = y;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'L':
                        {
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }
                            curX = x; curY = y;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'H':
                        {
                            float x = NextFloat(tokens, ref idx);
                            if (rel) x += curX;
                            curX = x;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'V':
                        {
                            float y = NextFloat(tokens, ref idx);
                            if (rel) y += curY;
                            curY = y;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'C': // Cubic Bezier: x1 y1 x2 y2 x y
                        {
                            float x1 = NextFloat(tokens, ref idx);
                            float y1 = NextFloat(tokens, ref idx);
                            float x2 = NextFloat(tokens, ref idx);
                            float y2 = NextFloat(tokens, ref idx);
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel)
                            {
                                x1 += curX; y1 += curY;
                                x2 += curX; y2 += curY;
                                x += curX; y += curY;
                            }
                            ApproximateCubicBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x2, y2), new PointF(x, y), currentPoints);
                            lastCtrlX = x2; lastCtrlY = y2;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'S': // Smooth Cubic Bezier: x2 y2 x y
                        {
                            float x2 = NextFloat(tokens, ref idx);
                            float y2 = NextFloat(tokens, ref idx);
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel)
                            {
                                x2 += curX; y2 += curY;
                                x += curX; y += curY;
                            }
                            // Điểm điều khiển 1 đối xứng với lastCtrl qua cur
                            float x1 = (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's') ? 2 * curX - lastCtrlX : curX;
                            float y1 = (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's') ? 2 * curY - lastCtrlY : curY;

                            ApproximateCubicBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x2, y2), new PointF(x, y), currentPoints);
                            lastCtrlX = x2; lastCtrlY = y2;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'Q': // Quadratic Bezier: x1 y1 x y
                        {
                            float x1 = NextFloat(tokens, ref idx);
                            float y1 = NextFloat(tokens, ref idx);
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel)
                            {
                                x1 += curX; y1 += curY;
                                x += curX; y += curY;
                            }
                            ApproximateQuadBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x, y), currentPoints);
                            lastCtrlX = x1; lastCtrlY = y1;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'T': // Smooth Quadratic Bezier: x y
                        {
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }
                            float x1 = (lastCmd == 'Q' || lastCmd == 'q' || lastCmd == 'T' || lastCmd == 't') ? 2 * curX - lastCtrlX : curX;
                            float y1 = (lastCmd == 'Q' || lastCmd == 'q' || lastCmd == 'T' || lastCmd == 't') ? 2 * curY - lastCtrlY : curY;

                            ApproximateQuadBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x, y), currentPoints);
                            lastCtrlX = x1; lastCtrlY = y1;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'A': // Arc: rx ry x-axis-rotation large-arc-flag sweep-flag x y
                        {
                            float rx = Math.Abs(NextFloat(tokens, ref idx));
                            float ry = Math.Abs(NextFloat(tokens, ref idx));
                            float rot = NextFloat(tokens, ref idx);
                            bool largeArc = NextFloat(tokens, ref idx) != 0;
                            bool sweep = NextFloat(tokens, ref idx) != 0;
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }

                            ApproximateArc(curX, curY, rx, ry, rot, largeArc, sweep, x, y, currentPoints);
                            curX = x; curY = y;
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'Z':
                        {
                            isCurrentClosed = true;
                            if (currentPoints.Count > 0 && (Math.Abs(curX - startX) > 0.001f || Math.Abs(curY - startY) > 0.001f))
                            {
                                currentPoints.Add(new PointF(startX, startY));
                            }
                            curX = startX;
                            curY = startY;
                            lastCtrlX = curX; lastCtrlY = curY;
                            FinishSubPath();
                            lastCmd = cmd;
                        }
                        break;

                    default:
                        idx++;
                        break;
                }
            }

            FinishSubPath();
            return subPaths;
        }

        private static List<string> TokenizePathData(string d)
        {
            var list = new List<string>();
            int i = 0, len = d.Length;
            while (i < len)
            {
                char c = d[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(c))
                {
                    list.Add(c.ToString());
                    i++;
                    continue;
                }
                // Số (bao gồm dấu +, -, số thập phân, e/E khoa học)
                int start = i;
                if (c == '+' || c == '-') i++;
                bool hasDot = false;
                while (i < len)
                {
                    char ch = d[i];
                    if (char.IsDigit(ch)) { i++; }
                    else if (ch == '.' && !hasDot) { hasDot = true; i++; }
                    else if ((ch == 'e' || ch == 'E') && i + 1 < len)
                    {
                        i++;
                        if (d[i] == '+' || d[i] == '-') i++;
                    }
                    else break;
                }
                if (i > start)
                {
                    list.Add(d.Substring(start, i - start));
                }
                else i++;
            }
            return list;
        }

        private static float NextFloat(List<string> tokens, ref int idx)
        {
            if (idx >= tokens.Count) return 0f;
            float val = ParseFloat(tokens[idx]);
            idx++;
            return val;
        }

        private static void ApproximateCubicBezier(PointF p0, PointF p1, PointF p2, PointF p3, List<PointF> pts, int steps = 20)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                float tt = t * t;
                float uu = u * u;
                float uuu = uu * u;
                float ttt = tt * t;

                float x = uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X;
                float y = uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y;
                pts.Add(new PointF(x, y));
            }
        }

        private static void ApproximateQuadBezier(PointF p0, PointF p1, PointF p2, List<PointF> pts, int steps = 16)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                float tt = t * t;
                float uu = u * u;

                float x = uu * p0.X + 2 * u * t * p1.X + tt * p2.X;
                float y = uu * p0.Y + 2 * u * t * p1.Y + tt * p2.Y;
                pts.Add(new PointF(x, y));
            }
        }

        private static void ApproximateArc(float x0, float y0, float rx, float ry, float angleDeg, bool largeArc, bool sweep, float x1, float y1, List<PointF> pts)
        {
            if (rx <= 1e-5f || ry <= 1e-5f || (Math.Abs(x0 - x1) < 1e-5f && Math.Abs(y0 - y1) < 1e-5f))
            {
                pts.Add(new PointF(x1, y1));
                return;
            }

            double phi = angleDeg * Math.PI / 180.0;
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);

            double dx = (x0 - x1) / 2.0;
            double dy = (y0 - y1) / 2.0;
            double x1p = cosPhi * dx + sinPhi * dy;
            double y1p = -sinPhi * dx + cosPhi * dy;

            double rxSq = rx * rx;
            double rySq = ry * ry;
            double x1pSq = x1p * x1p;
            double y1pSq = y1p * y1p;

            // Kiểm tra bán kính có đủ lớn không
            double lambda = (x1pSq / rxSq) + (y1pSq / rySq);
            if (lambda > 1.0)
            {
                double lSqrt = Math.Sqrt(lambda);
                rx = (float)(rx * lSqrt);
                ry = (float)(ry * lSqrt);
                rxSq = rx * rx;
                rySq = ry * ry;
            }

            double sign = (largeArc == sweep) ? -1.0 : 1.0;
            double sq = ((rxSq * rySq) - (rxSq * y1pSq) - (rySq * x1pSq)) / ((rxSq * y1pSq) + (rySq * x1pSq));
            if (sq < 0) sq = 0;
            double coef = sign * Math.Sqrt(sq);

            double cxp = coef * ((rx * y1p) / ry);
            double cyp = coef * -((ry * x1p) / rx);

            double cx = cosPhi * cxp - sinPhi * cyp + ((x0 + x1) / 2.0);
            double cy = sinPhi * cxp + cosPhi * cyp + ((y0 + y1) / 2.0);

            double ux = (x1p - cxp) / rx;
            double uy = (y1p - cyp) / ry;
            double vx = (-x1p - cxp) / rx;
            double vy = (-y1p - cyp) / ry;

            double startAngle = Math.Atan2(uy, ux);
            double deltaAngle = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);

            if (!sweep && deltaAngle > 0) deltaAngle -= 2 * Math.PI;
            else if (sweep && deltaAngle < 0) deltaAngle += 2 * Math.PI;

            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(deltaAngle) / (Math.PI / 18))); // mỗi 10 độ 1 đoạn
            for (int i = 1; i <= steps; i++)
            {
                double theta = startAngle + deltaAngle * (i / (double)steps);
                double ex = rx * Math.Cos(theta);
                double ey = ry * Math.Sin(theta);
                double px = cosPhi * ex - sinPhi * ey + cx;
                double py = sinPhi * ex + cosPhi * ey + cy;
                pts.Add(new PointF((float)px, (float)py));
            }
        }
        #endregion

        #region Helpers & Matrix Transform
        private static float ParseFloat(string? s, float fallback = 0f)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : fallback;
        }

        private static float ParseLength(string? s, float mmPerPx, float fallback = 0f)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s.EndsWith("mm")) return ParseFloat(s.Substring(0, s.Length - 2)) / mmPerPx;
            if (s.EndsWith("cm")) return (ParseFloat(s.Substring(0, s.Length - 2)) * 10f) / mmPerPx;
            if (s.EndsWith("in")) return (ParseFloat(s.Substring(0, s.Length - 2)) * 25.4f) / mmPerPx;
            if (s.EndsWith("pt")) return (ParseFloat(s.Substring(0, s.Length - 2)) * (25.4f / 72f)) / mmPerPx;
            if (s.EndsWith("px")) return ParseFloat(s.Substring(0, s.Length - 2));
            return ParseFloat(s, fallback);
        }

        private static List<float> SplitNumbers(string text)
        {
            var res = new List<float>();
            var rx = new Regex(@"[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?");
            foreach (Match m in rx.Matches(text))
            {
                if (float.TryParse(m.Value, NumberStyles.Float, Inv, out float v)) res.Add(v);
            }
            return res;
        }

        private static Matrix2D ParseTransform(string? tStr)
        {
            var mat = new Matrix2D();
            if (string.IsNullOrWhiteSpace(tStr)) return mat;

            var rx = new Regex(@"(translate|rotate|scale|matrix)\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
            foreach (Match m in rx.Matches(tStr))
            {
                string fn = m.Groups[1].Value.ToLowerInvariant();
                var args = SplitNumbers(m.Groups[2].Value);
                if (args.Count == 0) continue;

                switch (fn)
                {
                    case "translate":
                        float tx = args[0];
                        float ty = args.Count > 1 ? args[1] : 0f;
                        mat = Matrix2D.Multiply(mat, Matrix2D.CreateTranslation(tx, ty));
                        break;
                    case "scale":
                        float sx = args[0];
                        float sy = args.Count > 1 ? args[1] : sx;
                        mat = Matrix2D.Multiply(mat, Matrix2D.CreateScale(sx, sy));
                        break;
                    case "rotate":
                        float ang = args[0];
                        if (args.Count >= 3)
                        {
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateTranslation(args[1], args[2]));
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateRotation(ang));
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateTranslation(-args[1], -args[2]));
                        }
                        else
                        {
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateRotation(ang));
                        }
                        break;
                    case "matrix":
                        if (args.Count >= 6)
                        {
                            var custom = new Matrix2D(args[0], args[1], args[2], args[3], args[4], args[5]);
                            mat = Matrix2D.Multiply(mat, custom);
                        }
                        break;
                }
            }
            return mat;
        }

        public struct Matrix2D
        {
            public float M11, M12, M21, M22, DX, DY;

            public Matrix2D(float m11, float m12, float m21, float m22, float dx, float dy)
            {
                M11 = m11; M12 = m12; M21 = m21; M22 = m22; DX = dx; DY = dy;
            }

            public static Matrix2D Identity => new Matrix2D(1, 0, 0, 1, 0, 0);

            public static Matrix2D CreateTranslation(float dx, float dy) => new Matrix2D(1, 0, 0, 1, dx, dy);
            public static Matrix2D CreateScale(float sx, float sy) => new Matrix2D(sx, 0, 0, sy, 0, 0);
            public static Matrix2D CreateRotation(float deg)
            {
                double rad = deg * Math.PI / 180.0;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                return new Matrix2D(cos, sin, -sin, cos, 0, 0);
            }

            public PointF Transform(PointF p) => new PointF(M11 * p.X + M21 * p.Y + DX, M12 * p.X + M22 * p.Y + DY);

            public static Matrix2D Multiply(Matrix2D a, Matrix2D b)
            {
                return new Matrix2D(
                    a.M11 * b.M11 + a.M12 * b.M21,
                    a.M11 * b.M12 + a.M12 * b.M22,
                    a.M21 * b.M11 + a.M22 * b.M21,
                    a.M21 * b.M12 + a.M22 * b.M22,
                    a.DX * b.M11 + a.DY * b.M21 + b.DX,
                    a.DX * b.M12 + a.DY * b.M22 + b.DY
                );
            }
        }
        #endregion
    }
}

// =====================================================================
// PHẦN: Nhập DXF   (trước đây là file DxfImporter.cs)
// =====================================================================
namespace test01_1014
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    /// <summary>
    /// Bộ phân tích cú pháp và nhập file AutoCAD DXF (R12 / 2000 / ASCII) chuyên dụng cho máy cắt laser.
    /// Hỗ trợ các thực thể trong phần ENTITIES:
    /// - LINE: Đoạn thẳng
    /// - LWPOLYLINE: Polyline nhẹ 2D (bao gồm toạ độ và hệ số độ cong cung tròn Bulge)
    /// - POLYLINE / VERTEX / SEQEND: Polyline cổ điển 2D
    /// - CIRCLE: Đường tròn
    /// - ARC: Cung tròn (tâm, bán kính, góc bắt đầu, góc kết thúc)
    /// - SPLINE: Đường cong Spline nội suy
    /// </summary>
    public static class DxfImporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class DxfImportResult
        {
            public List<VectorPathItem> Items { get; } = new List<VectorPathItem>();
        }

        public static DxfImportResult Import(string filePath, float mmPerPx = 0.25f)
        {
            using var reader = new StreamReader(filePath);
            return Import(reader, mmPerPx);
        }

        public static DxfImportResult Import(TextReader reader, float mmPerPx = 0.25f)
        {
            var res = new DxfImportResult();
            var pairs = ReadGroupPairs(reader);

            int idx = 0;
            int count = pairs.Count;

            // Tìm phần ENTITIES
            while (idx < count)
            {
                var p = pairs[idx];
                if (p.Code == 0 && p.Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
                {
                    if (idx + 1 < count && pairs[idx + 1].Code == 2 && pairs[idx + 1].Value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase))
                    {
                        idx += 2;
                        break;
                    }
                }
                idx++;
            }

            var subPaths = new List<VectorPathItem.SubPath>();

            while (idx < count)
            {
                var p = pairs[idx];
                if (p.Code == 0)
                {
                    string entityType = p.Value.ToUpperInvariant();
                    if (entityType == "ENDSEC" || entityType == "EOF") break;

                    idx++;
                    switch (entityType)
                    {
                        case "LINE":
                            ParseLine(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "LWPOLYLINE":
                            ParseLwPolyline(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "POLYLINE":
                            ParsePolyline(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "CIRCLE":
                            ParseCircle(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "ARC":
                            ParseArc(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "SPLINE":
                            ParseSpline(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        default:
                            // Bỏ qua các thực thể không hỗ trợ (TEXT, MTEXT, POINT, INSERT...)
                            break;
                    }
                }
                else
                {
                    idx++;
                }
            }

            if (subPaths.Count > 0)
            {
                // Quy đổi trục Y của DXF (DXF Y hướng lên, còn Canvas Y hướng xuống)
                // Ta tìm bounding box và lật trục Y cho đúng chiều vẽ CAD
                FlipYCoordinate(subPaths);

                var pathItem = new VectorPathItem(subPaths, "Bản vẽ DXF");
                res.Items.Add(pathItem);
            }

            return res;
        }

        private static void FlipYCoordinate(List<VectorPathItem.SubPath> subPaths)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var sp in subPaths)
            {
                foreach (var pt in sp.Points)
                {
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            if (minY <= maxY)
            {
                float sumY = minY + maxY;
                foreach (var sp in subPaths)
                {
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        var pt = sp.Points[i];
                        sp.Points[i] = new PointF(pt.X, sumY - pt.Y);
                    }
                }
            }
        }

        #region Entity Parsers
        private static void ParseLine(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            float x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 10: x1 = ParseFloat(p.Value) / mmPerPx; break;
                    case 20: y1 = ParseFloat(p.Value) / mmPerPx; break;
                    case 11: x2 = ParseFloat(p.Value) / mmPerPx; break;
                    case 21: y2 = ParseFloat(p.Value) / mmPerPx; break;
                }
            }

            subPaths.Add(new VectorPathItem.SubPath
            {
                Points = new List<PointF> { new PointF(x1, y1), new PointF(x2, y2) },
                IsClosed = false
            });
        }

        private class LwVertex
        {
            public float X, Y;
            public double Bulge;
        }

        private static void ParseLwPolyline(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            bool isClosed = false;
            var vertices = new List<LwVertex>();
            LwVertex? curVert = null;

            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 70: // Flags (bit 1 = closed)
                        int flags = ParseInt(p.Value);
                        isClosed = (flags & 1) == 1;
                        break;
                    case 10: // X coordinate starts a new vertex
                        curVert = new LwVertex { X = ParseFloat(p.Value) / mmPerPx };
                        vertices.Add(curVert);
                        break;
                    case 20: // Y coordinate
                        if (curVert != null) curVert.Y = ParseFloat(p.Value) / mmPerPx;
                        break;
                    case 42: // Bulge factor for arc
                        if (curVert != null) curVert.Bulge = ParseDouble(p.Value);
                        break;
                }
            }

            if (vertices.Count >= 2)
            {
                var pts = ExpandBulgeVertices(vertices, isClosed);
                if (pts.Count >= 2)
                {
                    subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = isClosed });
                }
            }
        }

        private static void ParsePolyline(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            bool isClosed = false;
            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                if (p.Code == 70)
                {
                    isClosed = (ParseInt(p.Value) & 1) == 1;
                }
            }

            var vertices = new List<LwVertex>();

            while (idx < pairs.Count)
            {
                if (pairs[idx].Code == 0)
                {
                    string ent = pairs[idx].Value.ToUpperInvariant();
                    if (ent == "SEQEND" || ent == "ENDSEC" || ent == "EOF")
                    {
                        idx++;
                        break;
                    }
                    if (ent == "VERTEX")
                    {
                        idx++;
                        var v = new LwVertex();
                        while (idx < pairs.Count && pairs[idx].Code != 0)
                        {
                            var vp = pairs[idx++];
                            switch (vp.Code)
                            {
                                case 10: v.X = ParseFloat(vp.Value) / mmPerPx; break;
                                case 20: v.Y = ParseFloat(vp.Value) / mmPerPx; break;
                                case 42: v.Bulge = ParseDouble(vp.Value); break;
                            }
                        }
                        vertices.Add(v);
                        continue;
                    }
                    break;
                }
                idx++;
            }

            if (vertices.Count >= 2)
            {
                var pts = ExpandBulgeVertices(vertices, isClosed);
                if (pts.Count >= 2)
                {
                    subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = isClosed });
                }
            }
        }

        private static List<PointF> ExpandBulgeVertices(List<LwVertex> vertices, bool isClosed)
        {
            var pts = new List<PointF>();
            int count = vertices.Count;
            int limit = isClosed ? count : count - 1;

            for (int i = 0; i < limit; i++)
            {
                var v1 = vertices[i];
                var v2 = vertices[(i + 1) % count];

                if (i == 0) pts.Add(new PointF(v1.X, v1.Y));

                if (Math.Abs(v1.Bulge) > 1e-6)
                {
                    // Thêm các điểm cung tròn Bulge giữa v1 và v2
                    AddBulgeArcPoints(new PointF(v1.X, v1.Y), new PointF(v2.X, v2.Y), v1.Bulge, pts);
                }
                else
                {
                    pts.Add(new PointF(v2.X, v2.Y));
                }
            }
            return pts;
        }

        private static void AddBulgeArcPoints(PointF p1, PointF p2, double bulge, List<PointF> pts)
        {
            double theta = 4.0 * Math.Atan(bulge);
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 1e-5) return;

            double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            double sagitta = Math.Abs(bulge) * chord / 2.0;

            // Tính trung điểm của dây cung
            double mx = (p1.X + p2.X) / 2.0;
            double my = (p1.Y + p2.Y) / 2.0;

            // Vector pháp tuyến của dây cung
            double nx = -dy / chord;
            double ny = dx / chord;

            // Hướng tâm cung tròn
            double sign = bulge > 0 ? 1.0 : -1.0;
            double distToCenter = radius - sagitta;
            double cx = mx - sign * nx * distToCenter;
            double cy = my - sign * ny * distToCenter;

            double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
            double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);

            double span = a2 - a1;
            if (bulge > 0 && span < 0) span += 2 * Math.PI;
            else if (bulge < 0 && span > 0) span -= 2 * Math.PI;

            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(span) / (Math.PI / 18)));
            for (int s = 1; s <= steps; s++)
            {
                double a = a1 + span * (s / (double)steps);
                pts.Add(new PointF((float)(cx + radius * Math.Cos(a)), (float)(cy + radius * Math.Sin(a))));
            }
        }

        private static void ParseCircle(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            float cx = 0, cy = 0, r = 0;
            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 10: cx = ParseFloat(p.Value) / mmPerPx; break;
                    case 20: cy = ParseFloat(p.Value) / mmPerPx; break;
                    case 40: r = ParseFloat(p.Value) / mmPerPx; break;
                }
            }

            if (r > 0)
            {
                var pts = new List<PointF>(64);
                for (int i = 0; i < 64; i++)
                {
                    double a = i * Math.PI * 2 / 64;
                    pts.Add(new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
                }
                subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = true });
            }
        }

        private static void ParseArc(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            float cx = 0, cy = 0, r = 0;
            float startAngle = 0, endAngle = 360;

            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 10: cx = ParseFloat(p.Value) / mmPerPx; break;
                    case 20: cy = ParseFloat(p.Value) / mmPerPx; break;
                    case 40: r = ParseFloat(p.Value) / mmPerPx; break;
                    case 50: startAngle = ParseFloat(p.Value); break;
                    case 51: endAngle = ParseFloat(p.Value); break;
                }
            }

            if (r > 0)
            {
                double a1 = startAngle * Math.PI / 180.0;
                double a2 = endAngle * Math.PI / 180.0;
                double span = a2 - a1;
                while (span <= 0) span += 2 * Math.PI;

                int steps = Math.Max(4, (int)Math.Ceiling(span / (Math.PI / 18)));
                var pts = new List<PointF>(steps + 1);
                for (int i = 0; i <= steps; i++)
                {
                    double a = a1 + span * (i / (double)steps);
                    pts.Add(new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
                }
                subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = false });
            }
        }

        private static void ParseSpline(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            var pts = new List<PointF>();
            float curX = 0;
            bool isClosed = false;

            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 70:
                        isClosed = (ParseInt(p.Value) & 1) == 1;
                        break;
                    case 10: // Fit / Control point X
                        curX = ParseFloat(p.Value) / mmPerPx;
                        break;
                    case 20: // Y
                        pts.Add(new PointF(curX, ParseFloat(p.Value) / mmPerPx));
                        break;
                }
            }

            if (pts.Count >= 2)
            {
                subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = isClosed });
            }
        }
        #endregion

        #region Group Code / Pair Reader
        public struct DxfPair
        {
            public int Code;
            public string Value;
            public DxfPair(int c, string v) { Code = c; Value = v; }
        }

        private static List<DxfPair> ReadGroupPairs(TextReader reader)
        {
            var list = new List<DxfPair>();
            string? lineCode;
            while ((lineCode = reader.ReadLine()) != null)
            {
                lineCode = lineCode.Trim();
                if (string.IsNullOrEmpty(lineCode)) continue;

                if (!int.TryParse(lineCode, NumberStyles.Integer, Inv, out int code)) continue;
                string? val = reader.ReadLine();
                if (val == null) break;
                list.Add(new DxfPair(code, val.Trim()));
            }
            return list;
        }

        private static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : 0f;

        private static double ParseDouble(string s) =>
            double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : 0.0;

        private static int ParseInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : 0;
        #endregion
    }
}
