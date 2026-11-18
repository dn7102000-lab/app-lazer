// =====================================================================
//  Form1_Objects.cs  -  ĐỐI TƯỢNG: lớp, danh sách đối tượng, sắp xếp, mảng bản sao, mã vạch
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Lớp (layer) màu, thông số khắc theo lớp   (trước đây: Form1_Layers.cs)
//    2. Danh sách đối tượng (ẩn / khóa / thứ tự / đổi tên)   (trước đây: Form1_ObjectList.cs)
//    3. Menu Sắp xếp (căn chỉnh, thứ tự, nhóm)   (trước đây: Form1_Arrange.cs)
//    4. Tạo mảng bản sao   (trước đây: Form1_Array.cs)
//    5. Mã vạch / QR   (trước đây: Barcode.cs)
//    6. Thanh nút nhanh (Căn chỉnh / Nhóm / Lật / Xoay / Thứ tự)   (mới)
// =====================================================================

// =====================================================================
// PHẦN 1: Lớp (layer) màu, thông số khắc theo lớp   (trước đây là file Form1_Layers.cs)
// =====================================================================
// =====================================================================
//  Form1_Layers.cs  -  LỚP (LAYER) THEO MÀU, kiểu LightBurn
//
//  - Mỗi lớp có: tên, màu, công suất %, tốc độ F, số lần chạy, và cờ "Xuất" (bật / tắt xuất G-code).
//  - Mỗi đối tượng thuộc đúng 1 lớp. Đối tượng được vẽ bằng MÀU CỦA LỚP trên canvas
//    (đang chọn = xanh dương như cũ; lớp tắt "Xuất" = xám nhạt).
//  - Sửa thông số của lớp -> mọi đối tượng trong lớp đổi theo (thông số vẫn được ghi vào từng đối tượng
//    nên G-code, bản xem trước, file .lzr cũ... đều chạy như trước).
//  - Mục LASER ở bảng thông số bên phải cũng sửa LỚP của đối tượng đang chọn.
//  - Khi xuất G-code: chỉ các lớp bật "Xuất", chạy theo thứ tự lớp (Lớp 01 trước, Lớp 02 sau...).
//  - Đối tượng mới vẽ ra rơi vào lớp đang chọn trong bảng LỚP.
//  - Lưu trong file .lzr và bản nháp tự lưu (ProjectDataDTO.Layers, VectorItemDTO.LayerId).
//
//  Gọi InitLayers() 1 lần ở CUỐI InitWorkspaceLayout() (Form1_Tools.cs).
//  Giới hạn: tối đa 16 lớp; chỉ xóa được LỚP CUỐI và phải trống (để Hoàn tác không bị lệch số lớp).
// =====================================================================
namespace test01_1014
{
    using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;

    public class LaserLayer
    {
        public string Name { get; set; } = "Lớp";
        public Color Color { get; set; } = Color.Black;
        public int Power { get; set; } = 80;
        public int Speed { get; set; } = 1000;
        public int Passes { get; set; } = 1;
        public bool Output { get; set; } = true;       // false = không đưa vào G-code
        public int FillMode { get; set; }              // 0 = Đường viền, 1 = Tô kín, 2 = Tô kín + viền
        public double FillInterval { get; set; } = 0.1; // khoảng cách đường quét (mm)
        public float FillAngle { get; set; }
        public bool FillCross { get; set; }
        public bool FillBidir { get; set; } = true;
    }

    public class LayerDTO
    {
        public string Name { get; set; } = "";
        public int ColorArgb { get; set; }
        public int Power { get; set; } = 80;
        public int Speed { get; set; } = 1000;
        public int Passes { get; set; } = 1;
        public bool Output { get; set; } = true;
        public int FillMode { get; set; }
        public double FillInterval { get; set; } = 0.1;
        public float FillAngle { get; set; }
        public bool FillCross { get; set; }
        public bool FillBidir { get; set; } = true;
    }

    // Bảng màu hiện hành theo số lớp: Vectoritem.Draw đọc ở đây để vẽ đúng màu lớp
    public static class LayerColors
    {
        private static Color[] cols = { Color.Black };

        public static void Update(IList<LaserLayer> layers)
        {
            var a = new Color[Math.Max(1, layers.Count)];
            for (int i = 0; i < a.Length; i++)
                a[i] = i < layers.Count ? (layers[i].Output ? layers[i].Color : Color.FromArgb(175, 175, 175)) : Color.Black;
            cols = a;
        }

        public static Color Of(int layerId)
        {
            var c = cols;
            if (layerId < 0) layerId = 0;
            if (layerId >= c.Length) layerId = c.Length - 1;
            return c[layerId];
        }
    }

    public partial class Form1
    {
        private const int MaxLayers = 16;

        private static readonly Color[] LayerPalette =
        {
            Color.FromArgb(0, 0, 0),       Color.FromArgb(220, 40, 40),   Color.FromArgb(40, 90, 230),   Color.FromArgb(30, 150, 60),
            Color.FromArgb(240, 140, 20),  Color.FromArgb(150, 60, 190),  Color.FromArgb(0, 150, 170),   Color.FromArgb(215, 50, 140),
            Color.FromArgb(140, 90, 40),   Color.FromArgb(20, 50, 140),   Color.FromArgb(140, 150, 30),  Color.FromArgb(240, 120, 170),
            Color.FromArgb(110, 110, 110), Color.FromArgb(100, 200, 110), Color.FromArgb(80, 170, 240),  Color.FromArgb(130, 20, 40)
        };

        private static LaserLayer NewLayerAt(int i) =>
            new LaserLayer { Name = $"Lớp {i + 1:00}", Color = LayerPalette[i % LayerPalette.Length] };

        private static List<LaserLayer> MakeDefaultLayers()
        {
            var l = new List<LaserLayer>();
            for (int i = 0; i < 8; i++) l.Add(NewLayerAt(i));
            return l;
        }

        // Có sẵn 8 lớp ngay từ đầu (để chữ ký "đã sửa" lúc khởi động trùng với lúc giao diện dựng xong)
        private readonly List<LaserLayer> layers = MakeDefaultLayers();
        private int curLayer;                         // lớp đang chọn trong bảng: đối tượng MỚI sẽ rơi vào lớp này
        private bool layerUiBusy;
        private bool layersExpanded = true;

        private Panel layerPanel = null!;
        private Label layerHeader = null!;
        private ListBox layerList = null!;
        private Button layerColorBtn = null!, btnLayerAssign = null!, btnLayerSelect = null!, btnLayerAdd = null!, btnLayerDel = null!;
        private TextBox layerName = null!;
        private CheckBox layerOut = null!;
        private NumericUpDown layerPw = null!, layerSp = null!, layerPs = null!;
        private Label layerLblPw = null!, layerLblSp = null!, layerLblPs = null!;
        private ComboBox layerMode = null!;
        private Label layerLblMode = null!, layerLblInt = null!, layerLblAng = null!;
        private NumericUpDown layerFillInt = null!, layerFillAng = null!;
        private CheckBox layerFillBi = null!, layerFillCross = null!;
        private readonly List<Control> layerFillCtrls = new();      // chỉ hiện khi lớp ở chế độ Tô kín
        private readonly List<Control> layerBody = new();

        // ------------------------------------------------------------------
        //  Truy vấn lớp
        // ------------------------------------------------------------------
        private int LayerIndexOf(Vectoritem it) =>
            layers.Count == 0 ? 0 : Math.Clamp(it.LayerId, 0, layers.Count - 1);

        private LaserLayer LayerOf(Vectoritem it) => layers[LayerIndexOf(it)];

        // Các đối tượng sẽ được XUẤT G-code: chỉ lớp bật "Xuất", xếp theo thứ tự lớp (ổn định trong cùng lớp)
        private List<Vectoritem> ExportItems() =>
            projectItems.Where(i => !i.Hidden && LayerOf(i).Output).OrderBy(i => LayerIndexOf(i)).ToList();

        private static void LayerToItem(LaserLayer l, Vectoritem it)
        {
            it.LaserPower = l.Power; it.LaserSpeed = l.Speed; it.Passes = l.Passes;
            it.FillMode = l.FillMode; it.FillIntervalMm = l.FillInterval; it.FillAngle = l.FillAngle;
            it.FillCross = l.FillCross; it.FillBidir = l.FillBidir;
        }

        private static void ItemToLayer(Vectoritem it, LaserLayer l)
        {
            l.Power = it.LaserPower; l.Speed = it.LaserSpeed; l.Passes = it.Passes;
            l.FillMode = it.FillMode; l.FillInterval = it.FillIntervalMm; l.FillAngle = it.FillAngle;
            l.FillCross = it.FillCross; l.FillBidir = it.FillBidir;
        }

        private void AssignToLayer(Vectoritem it, int idx)
        {
            if (layers.Count == 0) return;
            idx = Math.Clamp(idx, 0, layers.Count - 1);
            it.LayerId = idx;
            var l = layers[idx];
            LayerToItem(l, it);
        }

        private void ApplyLayerParamsToItems(int idx)
        {
            var l = layers[idx];
            foreach (var it in projectItems)
                if (LayerIndexOf(it) == idx) { LayerToItem(l, it); }
        }

        // Sau Hoàn tác / Làm lại / đóng form G-code: thông số của đối tượng có thể đã đổi -> lớp lấy theo đối tượng đầu tiên trong lớp
        private void SyncLayersFromItems()
        {
            if (layers.Count == 0) return;
            var seen = new HashSet<int>();
            foreach (var it in projectItems)
            {
                int idx = LayerIndexOf(it);
                if (!seen.Add(idx)) continue;
                var l = layers[idx];
                ItemToLayer(it, l);
            }
            RefreshLayerUi();
        }

        // Mục LASER ở bảng thông số: sửa thông số của LỚP chứa đối tượng đó (và lớp của các đối tượng đang chọn cùng lúc)
        private void ApplyLaserFromItem(Vectoritem src)
        {
            if (layers.Count == 0) return;
            var targets = new HashSet<int> { LayerIndexOf(src) };
            foreach (var o in projectItems)
                if (o.IsSelected) targets.Add(LayerIndexOf(o));
            foreach (int idx in targets)
            {
                var l = layers[idx];
                l.Power = src.LaserPower; l.Speed = src.LaserSpeed; l.Passes = src.Passes;
                ApplyLayerParamsToItems(idx);
            }
            RefreshLayerUi();
        }

        // ------------------------------------------------------------------
        //  Lưu / nạp (file .lzr, bản nháp)
        // ------------------------------------------------------------------
        private List<LayerDTO> LayersToDto() =>
            layers.Select(l => new LayerDTO
            {
                Name = l.Name, ColorArgb = l.Color.ToArgb(),
                Power = l.Power, Speed = l.Speed, Passes = l.Passes, Output = l.Output,
                FillMode = l.FillMode, FillInterval = l.FillInterval, FillAngle = l.FillAngle,
                FillCross = l.FillCross, FillBidir = l.FillBidir
            }).ToList();

        // dto = null / rỗng (file cũ chưa có lớp): dùng 8 lớp mặc định, Lớp 01 lấy thông số của đối tượng đầu tiên
        private void LayersFromDto(List<LayerDTO>? dto, IEnumerable<Vectoritem> items)
        {
            layers.Clear();
            if (dto != null && dto.Count > 0)
            {
                int i = 0;
                foreach (var d in dto.Take(MaxLayers))
                {
                    var c = Color.FromArgb(d.ColorArgb);
                    if (c.A == 0) c = LayerPalette[i % LayerPalette.Length];
                    layers.Add(new LaserLayer
                    {
                        Name = string.IsNullOrWhiteSpace(d.Name) ? $"Lớp {i + 1:00}" : d.Name,
                        Color = Color.FromArgb(255, c),
                        Power = Math.Clamp(d.Power, 0, 100),
                        Speed = Math.Clamp(d.Speed, 1, 60000),
                        Passes = Math.Clamp(d.Passes, 1, 100),
                        Output = d.Output,
                        FillMode = Math.Clamp(d.FillMode, 0, 2),
                        FillInterval = d.FillInterval >= 0.02 ? Math.Min(d.FillInterval, 5.0) : 0.1,
                        FillAngle = d.FillAngle,
                        FillCross = d.FillCross,
                        FillBidir = d.FillBidir
                    });
                    i++;
                }
            }
            else
            {
                layers.AddRange(MakeDefaultLayers());
                var first = items.FirstOrDefault();
                if (first != null)
                {
                    ItemToLayer(first, layers[0]);
                }
            }
            curLayer = 0;
            LayersChanged();
            RefreshLayerUi();
        }

        private void ResetLayers()
        {
            layers.Clear();
            layers.AddRange(MakeDefaultLayers());
            curLayer = 0;
            LayersChanged();
            RefreshLayerUi();
        }

        private int LayersSignature()
        {
            var h = new HashCode();
            h.Add(layers.Count);
            foreach (var l in layers)
            {
                h.Add(l.Name); h.Add(l.Color.ToArgb());
                h.Add(l.Power); h.Add(l.Speed); h.Add(l.Passes); h.Add(l.Output);
                h.Add(l.FillMode); h.Add(l.FillInterval); h.Add(l.FillAngle); h.Add(l.FillCross); h.Add(l.FillBidir);
            }
            return h.ToHashCode();
        }

        private void LayersChanged()
        {
            LayerColors.Update(layers);
            layerList?.Invalidate();
            propPalette?.RefreshLayers();          // ô "Thuộc lớp" ở bảng thông số theo kịp tên / màu mới
            pictureBox1?.Invalidate();
        }

        private int savedLayerHeight;

        private void ToggleLayersExpanded()
        {
            layersExpanded = !layersExpanded;
            layerHeader.Text = (layersExpanded ? "▾" : "▸") + "  LỚP KHẮC (LAYER)";
            layerPanel.Visible = layersExpanded;

            try
            {
                if (!layersExpanded)
                {
                    savedLayerHeight = rightSplit.SplitterDistance;
                    rightSplit.Panel1MinSize = Scale96(26);
                    rightSplit.SplitterDistance = Scale96(26);
                    rightSplit.IsSplitterFixed = true;
                }
                else
                {
                    rightSplit.IsSplitterFixed = false;
                    rightSplit.Panel1MinSize = Scale96(100);
                    int h = savedLayerHeight >= Scale96(320) ? savedLayerHeight : Scale96(380);
                    int maxH = Math.Max(Scale96(100), rightSplit.Height - Scale96(100));
                    rightSplit.SplitterDistance = Math.Clamp(h, Scale96(100), maxH);
                }
            }
            catch { }
            LayoutLayers();
        }

        // ------------------------------------------------------------------
        //  Giao diện: bảng LỚP nằm đầu cột phải (trong Panel1 của rightSplit)
        // ------------------------------------------------------------------
        private void InitLayers()
        {
            if (layerPanel != null) return;
            LayerColors.Update(layers);

            layerHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = Scale96(26),
                Text = "▾  LỚP KHẮC (LAYER)",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(222, 229, 240),
                ForeColor = Color.FromArgb(40, 60, 100),
                Cursor = Cursors.Hand
            };
            layerHeader.Click += (s, e) => ToggleLayersExpanded();

            layerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                AutoScroll = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            layerList = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Scale96(26),
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle
            };
            layerList.DrawItem += DrawLayerRow;
            layerList.SelectedIndexChanged += (s, e) =>
            {
                if (layerUiBusy || layerList.SelectedIndex < 0) return;
                curLayer = layerList.SelectedIndex;
                FillLayerEditor();
            };
            layerList.DoubleClick += (s, e) => AssignSelectionToLayer();     // nháy đúp = gán đối tượng đang chọn vào lớp

            layerColorBtn = new Button { FlatStyle = FlatStyle.Flat, Text = "" };
            layerColorBtn.FlatAppearance.BorderColor = Color.DimGray;
            layerColorBtn.Click += (s, e) => PickLayerColor();

            layerName = new TextBox();
            layerName.TextChanged += (s, e) => EditLayer("Name", l => l.Name = layerName.Text, false);

            layerOut = new CheckBox { Text = "Xuất", AutoSize = false, Checked = true };
            layerOut.CheckedChanged += (s, e) => EditLayer("Out", l => l.Output = layerOut.Checked, false);

            var small = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            layerLblPw = new Label { Text = "Công suất %", Font = small, TextAlign = ContentAlignment.BottomLeft };
            layerLblSp = new Label { Text = "Tốc độ F", Font = small, TextAlign = ContentAlignment.BottomLeft };
            layerLblPs = new Label { Text = "Số lần", Font = small, TextAlign = ContentAlignment.BottomLeft };
            layerPw = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5 };
            layerSp = new NumericUpDown { Minimum = 1, Maximum = 60000, Increment = 100 };
            layerPs = new NumericUpDown { Minimum = 1, Maximum = 100, Increment = 1 };
            layerPw.ValueChanged += (s, e) => EditLayer("Pw", l => l.Power = (int)layerPw.Value, true);
            layerSp.ValueChanged += (s, e) => EditLayer("Sp", l => l.Speed = (int)layerSp.Value, true);
            layerPs.ValueChanged += (s, e) => EditLayer("Ps", l => l.Passes = (int)layerPs.Value, true);

            layerLblMode = new Label { Text = "Chế độ khắc", Font = small, TextAlign = ContentAlignment.BottomLeft };
            layerMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            layerMode.Items.AddRange(new object[] { "Đường viền (Line)", "Tô kín (Fill)", "Tô kín + viền" });
            layerMode.SelectedIndexChanged += (s, e) =>
            {
                if (layerUiBusy || layerMode.SelectedIndex < 0) return;
                EditLayer("Mode", l => l.FillMode = layerMode.SelectedIndex, true);
                LayoutLayers();
            };
            layerLblInt = new Label { Text = "Cách dòng (mm)", Font = small, TextAlign = ContentAlignment.BottomLeft };
            layerLblAng = new Label { Text = "Góc quét (°)", Font = small, TextAlign = ContentAlignment.BottomLeft };
            layerFillInt = new NumericUpDown { Minimum = 0.02m, Maximum = 5m, DecimalPlaces = 2, Increment = 0.01m, Value = 0.1m };
            layerFillAng = new NumericUpDown { Minimum = -360, Maximum = 360, DecimalPlaces = 0, Increment = 5 };
            layerFillBi = new CheckBox { Text = "Quét 2 chiều", AutoSize = false };
            layerFillCross = new CheckBox { Text = "Quét chéo", AutoSize = false };
            layerFillInt.ValueChanged += (s, e) => EditLayer("FI", l => l.FillInterval = (double)layerFillInt.Value, true);
            layerFillAng.ValueChanged += (s, e) => EditLayer("FA", l => l.FillAngle = (float)layerFillAng.Value, true);
            layerFillBi.CheckedChanged += (s, e) => EditLayer("FB", l => l.FillBidir = layerFillBi.Checked, true);
            layerFillCross.CheckedChanged += (s, e) => EditLayer("FC", l => l.FillCross = layerFillCross.Checked, true);
            layerFillCtrls.AddRange(new Control[] { layerLblInt, layerLblAng, layerFillInt, layerFillAng, layerFillBi, layerFillCross });

            btnLayerAssign = new Button { Text = "Gán vào lớp" };
            btnLayerSelect = new Button { Text = "Chọn cả lớp" };
            btnLayerAdd = new Button { Text = "+ Thêm lớp" };
            btnLayerDel = new Button { Text = "Xóa lớp cuối" };
            btnLayerAssign.Click += (s, e) => AssignSelectionToLayer();
            btnLayerSelect.Click += (s, e) => SelectLayerItems();
            btnLayerAdd.Click += (s, e) => AddLayer();
            btnLayerDel.Click += (s, e) => DeleteLastLayer();

            var tip = new ToolTip();
            tip.SetToolTip(btnLayerAssign, "Chuyển các đối tượng đang chọn sang lớp này (hoặc nháy đúp vào lớp)");
            tip.SetToolTip(btnLayerSelect, "Chọn tất cả đối tượng thuộc lớp này");
            tip.SetToolTip(layerOut, "Bỏ chọn = lớp này không được đưa vào G-code (hiện màu xám)");
            tip.SetToolTip(layerColorBtn, "Đổi màu lớp");
            tip.SetToolTip(layerMode, "Đường viền: chạy theo nét vẽ. Tô kín: lấp đầy hình bằng các đường quét song song (chỉ áp dụng cho hình kín và chữ).");
            tip.SetToolTip(layerFillInt, "Khoảng cách giữa 2 đường quét. Nhỏ hơn = đậm hơn nhưng lâu hơn.");
            tip.SetToolTip(layerFillCross, "Quét thêm một lượt vuông góc với lượt đầu (đậm hơn, gấp đôi thời gian)");

            layerBody.AddRange(new Control[]
            {
                layerList, layerColorBtn, layerName, layerOut,
                layerLblPw, layerLblSp, layerLblPs, layerPw, layerSp, layerPs,
                layerLblMode, layerMode, layerLblInt, layerLblAng, layerFillInt, layerFillAng, layerFillBi, layerFillCross,
                btnLayerAssign, btnLayerSelect, btnLayerAdd, btnLayerDel
            });
            foreach (var c in layerBody) layerPanel.Controls.Add(c);

            layerPanel.Resize += (s, e) => LayoutLayers();
            rightSplit.Panel1.Controls.Add(layerPanel);        // Fill
            rightSplit.Panel1.Controls.Add(layerHeader);       // Top
            layerHeader.SendToBack();                          // Dock=Top PHẢI SendToBack để layerPanel (Fill) nằm dưới, không bị đè lên

            int savedH = LoadLayerHeight();
            int initH = savedH >= Scale96(320) ? savedH : Scale96(380);
            void ApplySplitDist()
            {
                try
                {
                    if (rightSplit != null && rightSplit.Height > Scale96(300))
                    {
                        int minH = rightSplit.Panel1MinSize;
                        int maxH = Math.Max(minH, rightSplit.Height - rightSplit.Panel2MinSize);
                        rightSplit.SplitterDistance = Math.Clamp(initH, minH, maxH);
                    }
                }
                catch { }
            }

            Shown += (s, e) => ApplySplitDist();
            if (rightSplit.Height > Scale96(300)) ApplySplitDist();
            else
            {
                EventHandler? onSize = null;
                onSize = (s, e) =>
                {
                    if (rightSplit != null && rightSplit.Height > Scale96(300))
                    {
                        rightSplit.SizeChanged -= onSize;
                        ApplySplitDist();
                    }
                };
                rightSplit.SizeChanged += onSize;
            }

            propPalette.LayersGet = () => layers.Select(l => (l.Name, l.Color)).ToList();
            propPalette.LayerPicked += AssignSelectionToLayerIndex;
            propPalette.RefreshLayers();

            RefreshLayerUi();
            LayoutLayers();
        }

        private void LayoutLayers()
        {
            if (layerPanel == null) return;
            int S(int n) => Scale96(n);
            int w = Math.Max(S(200), layerPanel.ClientSize.Width);
            int pad = S(8), gap = S(6), inner = Math.Max(S(150), w - 2 * pad);

            layerHeader.Text = (layersExpanded ? "▾" : "▸") + "  LỚP KHẮC (LAYER)";
            foreach (var c in layerBody) c.Visible = layersExpanded;

            if (!layersExpanded) return;

            var prevScroll = layerPanel.AutoScrollPosition;
            layerPanel.AutoScrollPosition = new Point(0, 0);

            int y = S(6);
            layerList.SetBounds(pad, y, inner, 4 * layerList.ItemHeight + S(4));
            y += layerList.Height + S(6);

            int chkW = S(72);
            layerColorBtn.SetBounds(pad, y, S(34), S(28));
            layerName.SetBounds(pad + S(40), y + S(1), inner - S(40) - chkW - gap, S(26));
            layerOut.SetBounds(w - pad - chkW, y + S(2), chkW, S(24));
            y += S(36);

            int colW = (inner - 2 * gap) / 3;
            var lbls = new[] { layerLblPw, layerLblSp, layerLblPs };
            var nums = new[] { layerPw, layerSp, layerPs };
            for (int i = 0; i < 3; i++)
            {
                int x = pad + i * (colW + gap);
                lbls[i].SetBounds(x, y, colW, S(18));
                nums[i].SetBounds(x, y + S(18), colW, S(26));
            }
            y += S(18) + S(34);

            layerLblMode.SetBounds(pad, y, inner, S(18));
            layerMode.SetBounds(pad, y + S(18), inner, S(26));
            y += S(18) + S(34);

            bool fillOn = curLayer >= 0 && curLayer < layers.Count && layers[curLayer].FillMode != 0;
            foreach (var c in layerFillCtrls) c.Visible = fillOn;
            if (fillOn)
            {
                int half = (inner - gap) / 2;
                layerLblInt.SetBounds(pad, y, half, S(18));
                layerLblAng.SetBounds(pad + half + gap, y, half, S(18));
                layerFillInt.SetBounds(pad, y + S(18), half, S(26));
                layerFillAng.SetBounds(pad + half + gap, y + S(18), half, S(26));
                y += S(18) + S(34);
                layerFillBi.SetBounds(pad, y, half + S(10), S(24));
                layerFillCross.SetBounds(pad + half + gap + S(10), y, half - S(10), S(24));
                y += S(30);
            }

            int bw = (inner - gap) / 2, bh = S(30);
            btnLayerAssign.SetBounds(pad, y, bw, bh);
            btnLayerSelect.SetBounds(pad + bw + gap, y, bw, bh);
            y += bh + S(4);
            btnLayerAdd.SetBounds(pad, y, bw, bh);
            btnLayerDel.SetBounds(pad + bw + gap, y, bw, bh);
            y += bh + S(8);

            layerPanel.AutoScrollMinSize = new Size(0, y);
            layerPanel.AutoScrollPosition = new Point(Math.Abs(prevScroll.X), Math.Abs(prevScroll.Y));
        }

        private void DrawLayerRow(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= layers.Count) return;
            var l = layers[e.Index];
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;

            using (var bg = new SolidBrush(sel ? Color.FromArgb(205, 228, 252) : Color.White))
                g.FillRectangle(bg, e.Bounds);

            int pad = Scale96(6), sw = Scale96(16);
            var r = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + (e.Bounds.Height - sw) / 2, sw, sw);
            using (var b = new SolidBrush(l.Color)) g.FillRectangle(b, r);
            g.DrawRectangle(Pens.Gray, r);

            int cnt = projectItems.Count(i => LayerIndexOf(i) == e.Index);
            string left = l.Name + (cnt > 0 ? $"  ({cnt})" : "") + (l.Output ? "" : "  - tắt");
            string fillTag = l.FillMode == 1 ? " · Tô" : l.FillMode == 2 ? " · Tô+viền" : "";
            string right = $"{l.Power}% · {l.Speed} · ×{l.Passes}{fillTag}";
            Color tc = l.Output ? Color.FromArgb(30, 30, 30) : Color.Gray;

            using var small = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            var rs = TextRenderer.MeasureText(g, right, small);
            var ls = TextRenderer.MeasureText(g, left, e.Font ?? layerPanel.Font);
            if (e.Bounds.Width - sw - 4 * pad < rs.Width + ls.Width && l.FillMode == 2)
            {
                right = $"{l.Power}% · {l.Speed} · ×{l.Passes} · Tô+v";
                rs = TextRenderer.MeasureText(g, right, small);
            }
            int leftW = Math.Min(ls.Width + pad, Math.Max(10, e.Bounds.Width - sw - 3 * pad - rs.Width));
            const TextFormatFlags fl = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            var nameRect = new Rectangle(r.Right + pad, e.Bounds.Y, Math.Max(10, e.Bounds.Width - sw - 3 * pad - rs.Width), e.Bounds.Height);
            TextRenderer.DrawText(g, left, e.Font ?? layerPanel.Font, nameRect, tc, fl);
            TextRenderer.DrawText(g, right, small,
                new Rectangle(e.Bounds.Right - rs.Width - pad, e.Bounds.Y, rs.Width, e.Bounds.Height),
                Color.DimGray, fl);
        }

        // Dựng lại danh sách + ô chỉnh theo dữ liệu hiện có
        private void RefreshLayerUi()
        {
            if (layerList == null) return;
            bool old = layerUiBusy;
            layerUiBusy = true;
            try
            {
                curLayer = Math.Clamp(curLayer, 0, Math.Max(0, layers.Count - 1));
                if (layerList.Items.Count != layers.Count)
                {
                    layerList.Items.Clear();
                    foreach (var l in layers) layerList.Items.Add(l.Name);
                }
                layerList.SelectedIndex = curLayer;
                FillLayerEditor();
                layerList.Invalidate();
            }
            finally { layerUiBusy = old; }
        }

        private void FillLayerEditor()
        {
            if (layers.Count == 0) return;
            bool old = layerUiBusy;
            layerUiBusy = true;
            try
            {
                var l = layers[Math.Clamp(curLayer, 0, layers.Count - 1)];
                layerColorBtn.BackColor = l.Color;
                if (layerName.Text != l.Name) layerName.Text = l.Name;
                layerOut.Checked = l.Output;
                layerPw.Value = Math.Min(layerPw.Maximum, Math.Max(layerPw.Minimum, l.Power));
                layerSp.Value = Math.Min(layerSp.Maximum, Math.Max(layerSp.Minimum, l.Speed));
                layerPs.Value = Math.Min(layerPs.Maximum, Math.Max(layerPs.Minimum, l.Passes));
                layerMode.SelectedIndex = Math.Clamp(l.FillMode, 0, 2);
                layerFillInt.Value = Math.Min(layerFillInt.Maximum, Math.Max(layerFillInt.Minimum, (decimal)l.FillInterval));
                layerFillAng.Value = Math.Min(layerFillAng.Maximum, Math.Max(layerFillAng.Minimum, (decimal)l.FillAngle));
                layerFillBi.Checked = l.FillBidir;
                layerFillCross.Checked = l.FillCross;
                btnLayerDel.Enabled = layers.Count > 1;
                btnLayerAdd.Enabled = layers.Count < MaxLayers;
            }
            finally { layerUiBusy = old; }
            LayoutLayers();                       // hiện / ẩn các ô của chế độ Tô kín theo lớp đang chọn
        }

        // Sửa thông số lớp đang chọn. propagate = true: ghi luôn vào mọi đối tượng trong lớp (kèm bước Hoàn tác gộp)
        private void EditLayer(string key, Action<LaserLayer> set, bool propagate)
        {
            if (layerUiBusy || curLayer < 0 || curLayer >= layers.Count) return;
            if (propagate) PushUndoCoalesced($"layer:{curLayer}:{key}");
            set(layers[curLayer]);
            if (propagate) ApplyLayerParamsToItems(curLayer);
            LayersChanged();
            if (propagate) UpdatePropertiesToUI();      // bảng Thông số bên phải (mục LASER / Tô kín) cập nhật theo lớp ngay
        }

        private void PickLayerColor()
        {
            if (curLayer < 0 || curLayer >= layers.Count) return;
            using var cd = new ColorDialog { Color = layers[curLayer].Color, FullOpen = true };
            if (cd.ShowDialog(this) != DialogResult.OK) return;
            layers[curLayer].Color = Color.FromArgb(255, cd.Color);
            layerColorBtn.BackColor = layers[curLayer].Color;
            LayersChanged();
        }

        // ------------------------------------------------------------------
        //  Các nút
        // ------------------------------------------------------------------
        private void AssignSelectionToLayer()
        {
            if (ReadOnlyView) return;
            var sel = SelectedList();
            if (sel.Count == 0 && selectedItem != null) sel.Add(selectedItem);
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Hãy chọn đối tượng trên canvas trước, rồi bấm \"Gán vào lớp\".", "Lớp khắc",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PushUndo();
            foreach (var it in sel) AssignToLayer(it, curLayer);
            UpdatePropertiesToUI();
            layerList.Invalidate();
            pictureBox1.Invalidate();
        }

        // Dùng cho ô "Thuộc lớp" ở bảng thông số: chuyển các đối tượng đang chọn sang lớp idx
        private void AssignSelectionToLayerIndex(int idx)
        {
            if (ReadOnlyView || idx < 0 || idx >= layers.Count) return;
            var sel = SelectedList();
            if (sel.Count == 0 && selectedItem != null) sel.Add(selectedItem);
            if (sel.Count == 0 || sel.All(i => LayerIndexOf(i) == idx)) return;
            PushUndo();
            foreach (var it in sel) AssignToLayer(it, idx);
            UpdatePropertiesToUI();
            layerList?.Invalidate();
            pictureBox1.Invalidate();
        }

        private void SelectLayerItems()
        {
            if (ReadOnlyView) return;
            var inLayer = projectItems.Where(i => LayerIndexOf(i) == curLayer && ObjSelectable(i)).ToList();
            if (inLayer.Count == 0)
            {
                MessageBox.Show(this, "Lớp này chưa có đối tượng nào.", "Lớp khắc", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var it in projectItems) it.IsSelected = false;
            foreach (var it in inLayer) it.IsSelected = true;
            selectedItem = inLayer[inLayer.Count - 1];
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void AddLayer()
        {
            if (layers.Count >= MaxLayers) return;
            layers.Add(NewLayerAt(layers.Count));
            curLayer = layers.Count - 1;
            layerList.Items.Add(layers[curLayer].Name);
            LayersChanged();
            RefreshLayerUi();
        }

        private void DeleteLastLayer()
        {
            if (layers.Count <= 1) return;
            int last = layers.Count - 1;
            int cnt = projectItems.Count(i => LayerIndexOf(i) == last);
            if (cnt > 0)
            {
                MessageBox.Show(this,
                    $"\"{layers[last].Name}\" đang có {cnt} đối tượng.\nHãy chuyển chúng sang lớp khác (nút \"Gán vào lớp\") rồi xóa lại.\n\n" +
                    "(Chỉ xóa được lớp cuối cùng và phải trống.)",
                    "Xóa lớp khắc", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            layers.RemoveAt(last);
            layerList.Items.RemoveAt(last);
            curLayer = Math.Min(curLayer, layers.Count - 1);
            LayersChanged();
            RefreshLayerUi();
        }
    }
}

// =====================================================================
// PHẦN 2: Danh sách đối tượng (ẩn / khóa / thứ tự / đổi tên)   (trước đây là file Form1_ObjectList.cs)
// =====================================================================
// =====================================================================
//  Form1_ObjectList.cs  -  DANH SÁCH ĐỐI TƯỢNG: ẩn / hiện, khóa, đổi thứ tự, đổi tên (giống EzCad / LightBurn)
//
//  Cột phải có 2 tab: "Thông số" (bảng cũ) | "Đối tượng" (danh sách này).
//  Mỗi dòng: [mắt] [khóa] [ô màu lớp] tên đối tượng ........ tên lớp
//    - Bấm mắt  : ẩn / hiện. Đối tượng ẩn KHÔNG vẽ, KHÔNG chọn được và KHÔNG được xuất G-code.
//    - Bấm khóa : khóa / mở khóa. Đối tượng khóa vẫn vẽ và vẫn khắc, nhưng không chọn / kéo / sửa được trên canvas.
//    - Bấm tên  : chọn (Ctrl / Shift để chọn nhiều, đồng bộ 2 chiều với canvas). Bấm đúp hoặc F2: đổi tên.
//    - Kéo dòng lên / xuống để đổi thứ tự (trên cùng của danh sách = nằm trên cùng trên canvas).
//    - Nút Lên / Xuống / Trên cùng / Dưới cùng, Hiện hết, Mở khóa hết, Đổi tên, Xóa. Chuột phải có menu tương ứng.
//  Thứ tự trong projectItems = thứ tự vẽ (cuối cùng = trên cùng); danh sách hiển thị ngược lại cho dễ nhìn.
//  Mọi thay đổi đều Hoàn tác (Ctrl+Z) được, và lưu cùng file dự án / autosave (Hidden, Locked, Name).
//
//  Cần: Vectoritem.cs, Form1_Extras.cs, Form1_Select.cs, Form1_Layers.cs, Form1.cs, Form1.Snap.cs đã cập nhật.
//  Gọi InitObjectList() 1 lần: đã gắn ở cuối InitPrintArea() (Form1_PrintArea.cs).
// =====================================================================
namespace test01_1014
{
    using System.Drawing.Drawing2D;
    using Size = System.Drawing.Size;       // tránh nhập nhằng với OpenCvSharp
    using Point = System.Drawing.Point;
    using Font = System.Drawing.Font;

    // ListBox tự vẽ: bấm vào mắt / khóa không đổi vùng chọn, kéo thả để đổi thứ tự
    internal sealed class ObjListBox : ListBox
    {
        public int Pad = 4, IconW = 26, RowH = 30;
        public int DropLine = -1;                         // vị trí chèn đang hiện (0..Items.Count), -1 = không
        public event Action<int, int>? IconClicked;       // (chỉ số dòng, 0 = mắt, 1 = khóa)
        public event Action<int>? RowDoubleClicked;
        public event Action<int>? Reordered;              // vị trí chèn trong danh sách

        private Point downPt;
        private bool dragArmed;

        public ObjListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            SelectionMode = SelectionMode.MultiExtended;
            IntegralHeight = false;
            AllowDrop = true;
            DoubleBuffered = true;
            BorderStyle = BorderStyle.FixedSingle;
        }

        private int IconAt(int x) =>
            x >= Pad && x < Pad + IconW ? 0 : x >= Pad + IconW && x < Pad + 2 * IconW ? 1 : -1;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int idx = IndexFromPoint(e.Location);
                if (idx >= 0)
                {
                    int ic = IconAt(e.X);
                    if (ic >= 0) { IconClicked?.Invoke(idx, ic); return; }       // không đổi vùng chọn
                    dragArmed = true; downPt = e.Location;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragArmed && e.Button == MouseButtons.Left)
            {
                var ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - downPt.X) >= ds.Width || Math.Abs(e.Y - downPt.Y) >= ds.Height)
                {
                    dragArmed = false;
                    if (SelectedIndices.Count > 0) DoDragDrop("objlist", DragDropEffects.Move);
                    return;
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragArmed = false; base.OnMouseUp(e); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int idx = IndexFromPoint(e.Location);
            if (idx >= 0 && IconAt(e.X) < 0) RowDoubleClicked?.Invoke(idx);
            base.OnMouseDoubleClick(e);
        }

        private int PosFromPoint(Point pt)
        {
            int idx = IndexFromPoint(pt);
            if (idx < 0) return pt.Y < 0 ? 0 : Items.Count;
            var r = GetItemRectangle(idx);
            return pt.Y < r.Top + r.Height / 2 ? idx : idx + 1;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
            var pt = PointToClient(new Point(e.X, e.Y));
            if (pt.Y < RowH && TopIndex > 0) TopIndex--;                                  // kéo sát mép: tự cuộn
            else if (pt.Y > Height - RowH && TopIndex < Items.Count - 1) TopIndex++;
            int pos = PosFromPoint(pt);
            if (pos != DropLine) { DropLine = pos; Invalidate(); }
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            int pos = DropLine;
            DropLine = -1; Invalidate();
            if (pos >= 0) Reordered?.Invoke(pos);
        }

        protected override void OnDragLeave(EventArgs e) { DropLine = -1; Invalidate(); }
    }

    public partial class Form1
    {
        private ObjListBox objList = null!;
        private Panel? objHost, objPanel;
        private RadioButton? tabProps, tabObjs;
        private ContextMenuStrip? objMenu;
        private ToolStripMenuItem? miObjHide, miObjLock;
        private System.Windows.Forms.Timer? objTimer;
        private int objSig = int.MinValue, objCtxIdx = -1;
        private bool objSync;
        private readonly Dictionary<Vectoritem, string> objLabels = new();

        // =================================================================
        //  Dựng giao diện
        // =================================================================
        private void InitObjectList()
        {
            if (objHost != null || propPalette == null || rightHost == null) return;

            objList = new ObjListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                RowH = Scale96(30), ItemHeight = Scale96(30), Pad = Scale96(4), IconW = Scale96(26)
            };
            objList.DrawItem += DrawObjRow;
            objList.SelectedIndexChanged += ObjSelectionChanged;
            objList.IconClicked += ObjIconClicked;
            objList.RowDoubleClicked += idx => RenameObjectAt(idx);
            objList.Reordered += ObjReorder;
            objList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F2) { RenameObjectAt(objList.SelectedIndex); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            };

            // ---- menu chuột phải ----
            objMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            var miRen = new ToolStripMenuItem("Đổi tên") { ShortcutKeyDisplayString = "F2" };
            miRen.Click += (s, e) => RenameObjectAt(objCtxIdx);
            miObjHide = new ToolStripMenuItem("Ẩn");
            miObjHide.Click += (s, e) => ObjIconClicked(objCtxIdx, 0);
            miObjLock = new ToolStripMenuItem("Khóa");
            miObjLock.Click += (s, e) => ObjIconClicked(objCtxIdx, 1);
            var miDel = new ToolStripMenuItem("Xóa");
            miDel.Click += (s, e) => DeleteSelected();
            objMenu.Items.AddRange(new ToolStripItem[] { miRen, new ToolStripSeparator(), miObjHide, miObjLock, new ToolStripSeparator(), miDel });
            objMenu.Opening += (s, e) =>
            {
                objCtxIdx = objList.IndexFromPoint(objList.PointToClient(Cursor.Position));
                if (objCtxIdx < 0 || ReadOnlyView) { e.Cancel = true; return; }
                var it = (Vectoritem)objList.Items[objCtxIdx];
                miObjHide!.Text = it.Hidden ? "Hiện" : "Ẩn";
                miObjLock!.Text = it.Locked ? "Mở khóa" : "Khóa";
            };
            objList.ContextMenuStrip = objMenu;

            // ---- hàng nút dưới danh sách: dạng lưới 2 hàng x 4 cột gọn gàng ----
            var btns = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                RowCount = 2,
                ColumnCount = 4,
                Height = Scale96(66),
                Padding = new Padding(0, Scale96(2), 0, 0),
                Margin = Padding.Empty
            };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            btns.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            btns.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            Button B(string text, Action act)
            {
                var b = new Button
                {
                    Text = text, Dock = DockStyle.Fill, UseVisualStyleBackColor = true,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = new Padding(Scale96(1))
                };
                b.Click += (s, e) => { if (!ReadOnlyView) act(); };
                return b;
            }
            btns.Controls.Add(B("▲ Lên", () => MoveInOrder(1)), 0, 0);
            btns.Controls.Add(B("▼ Xuống", () => MoveInOrder(2)), 1, 0);
            btns.Controls.Add(B("⇈ Đầu", () => MoveInOrder(0)), 2, 0);
            btns.Controls.Add(B("⇊ Cuối", () => MoveInOrder(3)), 3, 0);
            btns.Controls.Add(B("Hiện hết", ShowAllObjects), 0, 1);
            btns.Controls.Add(B("Mở khóa", UnlockAllObjects), 1, 1);
            btns.Controls.Add(B("Đổi tên", () => RenameObjectAt(objList.SelectedIndex)), 2, 1);
            btns.Controls.Add(B("Xóa", DeleteSelected), 3, 1);

            objPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            objPanel.Controls.Add(objList);
            objPanel.Controls.Add(btns);
            objList.BringToFront();

            // ---- 2 tab: Thông số | Đối tượng ----
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = Scale96(34), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            RadioButton Tab(string text, bool on)
            {
                var r = new RadioButton
                {
                    Appearance = Appearance.Button, Text = text, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat, Checked = on, Margin = new Padding(0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
                };
                r.FlatAppearance.BorderColor = Color.FromArgb(190, 196, 204);
                r.FlatAppearance.CheckedBackColor = Color.FromArgb(0, 120, 215);
                r.FlatAppearance.MouseOverBackColor = Color.FromArgb(225, 235, 248);
                r.BackColor = Color.FromArgb(240, 242, 245);
                r.ForeColor = on ? Color.White : Color.Black;
                r.CheckedChanged += (s, e) => r.ForeColor = r.Checked ? Color.White : Color.Black;
                return r;
            }
            tabProps = Tab("Thông số", true);
            tabObjs = Tab("Đối tượng", false);
            header.Controls.Add(tabProps, 0, 0);
            header.Controls.Add(tabObjs, 1, 0);
            tabObjs.CheckedChanged += (s, e) => ShowObjectTab(tabObjs!.Checked);

            // ---- container thay chỗ bảng thông số ở cột phải (trong Panel2 của rightSplit) ----
            objHost = new Panel { Dock = DockStyle.Fill };
            rightSplit.Panel2.Controls.Remove(propPalette);
            objHost.Controls.Add(propPalette);       // Fill
            objHost.Controls.Add(objPanel);          // Fill
            objHost.Controls.Add(header);            // Top: thêm sau cùng -> dock trước
            rightSplit.Panel2.Controls.Add(objHost);
            objHost.BringToFront();                  // Fill phải ở trên cùng thứ tự dock (các panel Top / Bottom thêm sau vẫn dock trước)

            objTimer = new System.Windows.Forms.Timer { Interval = 200 };
            objTimer.Tick += (s, e) =>
            {
                if (objPanel == null || !objPanel.Visible) return;
                if (ComputeObjSig() != objSig) RebuildObjList();
            };
            objTimer.Start();
        }

        private void ShowObjectTab(bool objs)
        {
            if (objPanel == null) return;
            propPalette.Visible = !objs;
            objPanel.Visible = objs;
            if (objs)
            {
                RebuildObjList();
                objList.BringToFront();
            }
        }

        // =================================================================
        //  Đồng bộ danh sách <-> dự án
        // =================================================================
        private int ComputeObjSig()
        {
            var h = new HashCode();
            h.Add(projectItems.Count); h.Add(ReadOnlyView);
            foreach (var it in projectItems)
            {
                h.Add(it.Id); h.Add(it.Name); h.Add(it.Hidden); h.Add(it.Locked); h.Add(it.IsSelected); h.Add(it.LayerId);
                if (it is VectorText t) h.Add(t.TextContent);
                h.Add(LayerOf(it).Name);
            }
            return h.ToHashCode();
        }

        private static string ObjBaseName(Vectoritem it)
        {
            if (it is VectorText t && it.Name == "Văn bản")
            {
                string s = (t.TextContent ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (s.Length > 22) s = s[..22] + "…";
                return s.Length > 0 ? "“" + s + "”" : "Văn bản";
            }
            return it.Name;
        }

        private void RebuildObjList()
        {
            if (objList == null) return;
            objSync = true;
            try
            {
                int top = objList.TopIndex;
                objList.BeginUpdate();
                objList.Items.Clear();

                // tên trùng nhau (vd nhiều "Hình chữ nhật") -> đánh số theo thứ tự tạo
                var count = projectItems.GroupBy(ObjBaseName).ToDictionary(g => g.Key, g => g.Count());
                var seen = new Dictionary<string, int>();
                objLabels.Clear();
                foreach (var it in projectItems)
                {
                    string b = ObjBaseName(it);
                    if (count[b] > 1) { seen[b] = seen.GetValueOrDefault(b) + 1; objLabels[it] = b + " " + seen[b]; }
                    else objLabels[it] = b;
                }

                for (int i = projectItems.Count - 1; i >= 0; i--) objList.Items.Add(projectItems[i]);   // trên cùng canvas = đầu danh sách
                for (int i = 0; i < objList.Items.Count; i++)
                    if (((Vectoritem)objList.Items[i]).IsSelected) objList.SetSelected(i, true);
                if (top > 0 && top < objList.Items.Count) objList.TopIndex = top;
                objList.EndUpdate();
                objList.Enabled = !ReadOnlyView;
            }
            finally { objSync = false; }
            objSig = ComputeObjSig();
        }

        private void ObjSelectionChanged(object? sender, EventArgs e)
        {
            if (objSync || ReadOnlyView) return;
            var chosen = new HashSet<Vectoritem>();
            foreach (var o in objList.SelectedItems)
                if (o is Vectoritem v && ObjSelectable(v)) chosen.Add(v);
            foreach (var it in projectItems) it.IsSelected = chosen.Contains(it);
            ExpandAllGroups();
            if (selectedItem != null && !selectedItem.IsSelected) selectedItem = null!;
            SetPrimaryFromSelection();
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();

            // dòng bị ẩn / khóa hoặc cả nhóm được mở rộng: danh sách phải vẽ lại cho khớp; còn lại giữ nguyên để không mất mốc Shift
            bool same = chosen.Count == objList.SelectedItems.Count && projectItems.Count(i => i.IsSelected) == chosen.Count;
            objSig = same ? ComputeObjSig() : int.MinValue;
        }

        private void ObjIconClicked(int idx, int which)
        {
            if (ReadOnlyView || idx < 0 || idx >= objList.Items.Count) return;
            var it = (Vectoritem)objList.Items[idx];
            PushUndo();
            if (which == 0) it.Hidden = !it.Hidden; else it.Locked = !it.Locked;
            if (!ObjSelectable(it)) it.IsSelected = false;
            if (selectedItem != null && !selectedItem.IsSelected) { selectedItem = null!; SetPrimaryFromSelection(); }
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
            RebuildObjList();
        }

        private void ShowAllObjects()
        {
            if (!projectItems.Any(i => i.Hidden)) return;
            PushUndo();
            foreach (var it in projectItems) it.Hidden = false;
            pictureBox1.Invalidate();
            RebuildObjList();
        }

        private void UnlockAllObjects()
        {
            if (!projectItems.Any(i => i.Locked)) return;
            PushUndo();
            foreach (var it in projectItems) it.Locked = false;
            pictureBox1.Invalidate();
            RebuildObjList();
        }

        private void ObjReorder(int insertPos)
        {
            if (ReadOnlyView) return;
            var L = new List<Vectoritem>();                                   // thứ tự đang hiện (trên cùng trước)
            for (int i = 0; i < objList.Items.Count; i++) L.Add((Vectoritem)objList.Items[i]);
            if (L.Count != projectItems.Count) { RebuildObjList(); return; }

            var D = new List<Vectoritem>();
            int before = 0;
            for (int i = 0; i < L.Count; i++)
                if (objList.GetSelected(i)) { D.Add(L[i]); if (i < insertPos) before++; }
            if (D.Count == 0) return;

            var dset = new HashSet<Vectoritem>(D);
            var R = L.Where(x => !dset.Contains(x)).ToList();
            int q = Math.Clamp(insertPos - before, 0, R.Count);
            var NL = new List<Vectoritem>(R);
            NL.InsertRange(q, D);
            if (NL.SequenceEqual(L)) return;

            PushUndo();
            projectItems.Clear();
            for (int i = NL.Count - 1; i >= 0; i--) projectItems.Add(NL[i]);
            pictureBox1.Invalidate();
            RebuildObjList();
        }

        // =================================================================
        //  Đổi tên
        // =================================================================
        private void RenameObjectAt(int idx)
        {
            if (ReadOnlyView || idx < 0 || idx >= objList.Items.Count) return;
            var it = (Vectoritem)objList.Items[idx];
            string? t = PromptObjectName(objLabels.TryGetValue(it, out var cur) ? cur : it.Name);
            if (string.IsNullOrWhiteSpace(t) || t.Trim() == it.Name) return;
            PushUndo();
            it.Name = t.Trim();
            RebuildObjList();
        }

        private string? PromptObjectName(string initial)
        {
            using var f = new Form
            {
                Text = "Đổi tên đối tượng", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                ClientSize = new Size(Scale96(360), Scale96(112)), Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            var lb = new Label { Text = "Tên mới:", Left = Scale96(12), Top = Scale96(12), AutoSize = true };
            var tb = new TextBox { Left = Scale96(12), Top = Scale96(36), Width = Scale96(336), Text = initial };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = Scale96(184), Top = Scale96(72), Width = Scale96(80), Height = Scale96(30) };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Left = Scale96(272), Top = Scale96(72), Width = Scale96(80), Height = Scale96(30) };
            f.Controls.AddRange(new Control[] { lb, tb, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Shown += (s, e) => { tb.Focus(); tb.SelectAll(); };
            return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
        }

        // =================================================================
        //  Vẽ từng dòng
        // =================================================================
        private void DrawObjRow(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= objList.Items.Count) return;
            var it = (Vectoritem)objList.Items[e.Index];
            var g = e.Graphics;
            var r = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;

            Color back = sel ? Color.FromArgb(0, 120, 215) : (e.Index % 2 == 0 ? Color.White : Color.FromArgb(246, 247, 250));
            Color fore = sel ? Color.White : (it.Hidden ? Color.FromArgb(150, 150, 150) : Color.Black);
            using (var bb = new SolidBrush(back)) g.FillRectangle(bb, r);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = objList.Pad, iw = objList.IconW;
            DrawEyeIcon(g, new Rectangle(r.X + pad, r.Y, iw, r.Height), !it.Hidden, sel);
            DrawLockIcon(g, new Rectangle(r.X + pad + iw, r.Y, iw, r.Height), it.Locked, sel);

            int sw = Scale96(12), sx = r.X + pad + 2 * iw + Scale96(4);
            var sq = new Rectangle(sx, r.Y + (r.Height - sw) / 2, sw, sw);
            using (var cb = new SolidBrush(it.Hidden ? Color.FromArgb(190, 190, 190) : LayerColors.Of(it.LayerId))) g.FillRectangle(cb, sq);
            using (var cp = new Pen(sel ? Color.White : Color.FromArgb(120, 120, 120))) g.DrawRectangle(cp, sq);

            int layerW = Scale96(70);
            int tx = sx + sw + Scale96(6);
            string label = objLabels.TryGetValue(it, out var l) ? l : it.Name;
            var nameRect = new Rectangle(tx, r.Y, Math.Max(10, r.Right - tx - layerW - Scale96(6)), r.Height);
            TextRenderer.DrawText(g, label, objList.Font, nameRect, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);

            using (var small = new Font("Segoe UI", 8f, FontStyle.Bold))
            {
                var lr = new Rectangle(r.Right - layerW - Scale96(4), r.Y, layerW, r.Height);
                TextRenderer.DrawText(g, LayerOf(it).Name, small, lr, sel ? Color.FromArgb(215, 230, 255) : Color.FromArgb(120, 120, 120),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Right);
            }

            // vạch chỉ chỗ thả khi kéo đổi thứ tự
            if (objList.DropLine >= 0)
            {
                using var dp = new Pen(Color.FromArgb(0, 120, 215), 2f);
                if (objList.DropLine == e.Index) g.DrawLine(dp, r.Left, r.Top + 1, r.Right, r.Top + 1);
                else if (objList.DropLine == objList.Items.Count && e.Index == objList.Items.Count - 1) g.DrawLine(dp, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            }
        }

        private static void DrawEyeIcon(Graphics g, Rectangle r, bool open, bool sel)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float w = r.Width * 0.66f, h = w * 0.52f;
            Color c = open ? (sel ? Color.White : Color.FromArgb(45, 45, 45))
                           : (sel ? Color.FromArgb(190, 215, 250) : Color.FromArgb(185, 185, 185));
            using var pen = new Pen(c, 1.6f);
            g.DrawEllipse(pen, cx - w / 2, cy - h / 2, w, h);
            if (open)
            {
                float p = h * 0.56f;
                using var b = new SolidBrush(c);
                g.FillEllipse(b, cx - p / 2, cy - p / 2, p, p);
            }
            else g.DrawLine(pen, cx - w / 2, cy + h / 2 + 1, cx + w / 2, cy - h / 2 - 1);
        }

        private static void DrawLockIcon(Graphics g, Rectangle r, bool locked, bool sel)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float bw = r.Width * 0.46f, bh = bw * 0.78f;
            Color c = locked ? (sel ? Color.White : Color.FromArgb(205, 105, 0))
                             : (sel ? Color.FromArgb(170, 200, 245) : Color.FromArgb(208, 208, 208));
            var body = new RectangleF(cx - bw / 2, cy - bh / 2 + bh * 0.22f, bw, bh);
            float sh = bw * 0.62f, sTop = body.Top - sh * 0.95f;
            using var pen = new Pen(c, 1.6f);
            var arc = new RectangleF(cx - sh / 2, sTop, sh, sh * 1.1f);
            float armY = arc.Top + arc.Height / 2f;
            g.DrawArc(pen, arc, 180, 180);
            g.DrawLine(pen, arc.Left, armY, arc.Left, body.Top);
            if (locked) g.DrawLine(pen, arc.Right, armY, arc.Right, body.Top);
            else g.DrawLine(pen, arc.Right, armY, arc.Right, armY + sh * 0.18f);        // quai khóa mở: chỉ một bên chạm thân
            if (locked) { using var b = new SolidBrush(c); g.FillRectangle(b, body); }
            else g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
        }
    }
}

// =====================================================================
// PHẦN 3: Menu Sắp xếp (căn chỉnh, thứ tự, nhóm)   (trước đây là file Form1_Arrange.cs)
// =====================================================================
// =====================================================================
//  Form1_Arrange.cs  -  Menu "Sắp xếp" + Việt hóa tên menu
//
//  1) CĂN CHỈNH: chọn 1 hình -> căn theo VÙNG KHẮC; chọn nhiều hình (Ctrl+click / quét khung)
//     -> căn các hình với nhau theo khung bao của nhóm. "Căn giữa vùng khắc" với nhiều hình
//     dời cả nhóm như một khối vào giữa vùng khắc.
//     Vùng khắc = hình chữ nhật từ gốc thước (0,0) rộng x cao theo WorkAreaMm bên dưới.
//     => ĐỔI KÍCH THƯỚC MÁY CỦA ANH Ở DÒNG WorkAreaMm.
//
//  2) PHÂN PHỐI ĐỀU (ngang / dọc): dàn đều khoảng cách giữa các hình đang chọn (>= 3);
//     nếu chọn ít hơn 3 thì dàn đều TẤT CẢ đối tượng. Hai hình ngoài cùng đứng yên.
//
//  3) THỨ TỰ LỚP (áp dụng cho cả nhóm đang chọn): Đưa lên trên cùng / lên 1 lớp / xuống 1 lớp / xuống dưới cùng.
//     Thứ tự trong projectItems cũng là thứ tự CHẠY G-CODE (hình đứng trước khắc trước).
//
//  Gọi InitArrangeAndMenus() 1 lần ở cuối InitWorkspaceLayout() (Form1_Tools.cs).
//  Mọi thao tác đều có Undo (PushUndo) và bị khóa khi đang ở tab xem (ReadOnlyView).
// =====================================================================
namespace test01_1014
{
    public partial class Form1
    {
        // >>> KÍCH THƯỚC VÙNG KHẮC CỦA MÁY (mm). Sửa cho đúng với máy của anh. <<<
        // cũ:  private static readonly SizeF WorkAreaMm = new SizeF(400f, 400f);
        private SizeF WorkAreaMm => PrintAreaSizeMm;

        private enum AlignMode { Left, CenterH, Right, Top, CenterV, Bottom, CenterBoth }

        private ToolStripMenuItem? arrangeRoot;
        private readonly List<ToolStripItem> arrangeNeedsSelection = new();   // chỉ bật khi có đối tượng chọn
        private readonly List<ToolStripItem> arrangeNeedsThree = new();       // chỉ bật khi có >= 3 đối tượng

        // ------------------------------------------------------------------
        //  Khởi tạo
        // ------------------------------------------------------------------
        private void InitArrangeAndMenus()
        {
            if (arrangeRoot != null) return;

            // Menu "Sắp xếp" nằm ngay sau "Chỉnh sửa"
            arrangeRoot = new ToolStripMenuItem("Sắp xếp");
            arrangeRoot.DropDownItems.AddRange(BuildGroupItems(true));            // Nhóm / Rã nhóm (Form1_Transform.cs)
            arrangeRoot.DropDownItems.Add(new ToolStripSeparator());
            arrangeRoot.DropDownItems.AddRange(BuildTransformItems());           // Xoay / Lật
            arrangeRoot.DropDownItems.Add(new ToolStripSeparator());
            arrangeRoot.DropDownItems.AddRange(BuildArrangeItems(true));
            arrangeRoot.DropDownOpening += (s, e) => RefreshArrangeEnabled();
            int at = menuStrip1.Items.IndexOf(toolStripMenuItem1);
            menuStrip1.Items.Insert(at >= 0 ? at + 1 : menuStrip1.Items.Count, arrangeRoot);

            // Chuột phải trên canvas: thêm mục "Sắp xếp" (chỉ thứ tự lớp cho gọn)
            if (pictureBox1.ContextMenuStrip is { } cms)
            {
                var sub = new ToolStripMenuItem("Thứ tự chồng");
                sub.DropDownItems.AddRange(BuildZOrderItems());
                cms.Items.Add(new ToolStripSeparator());
                cms.Items.AddRange(BuildGroupItems(false));
                cms.Items.Add(sub);
                cms.Opening += (s, e) => RefreshArrangeEnabled();
            }

            InitQuickBar();                          // thanh nút nhanh dưới menu (PHẦN 6)

            LocalizeMenus(menuStrip1.Items);
            if (pictureBox1.ContextMenuStrip != null) LocalizeMenus(pictureBox1.ContextMenuStrip.Items);
        }

        private ToolStripItem[] BuildArrangeItems(bool withAlign)
        {
            var list = new List<ToolStripItem>();

            ToolStripMenuItem Add(string text, Action act, bool needThree = false, Keys keys = Keys.None)
            {
                var mi = new ToolStripMenuItem(text) { ShortcutKeys = keys };
                mi.Click += (s, e) => act();
                (needThree ? arrangeNeedsThree : arrangeNeedsSelection).Add(mi);
                list.Add(mi);
                return mi;
            }

            if (withAlign)
            {
                Add("Căn trái", () => AlignSelected(AlignMode.Left));
                Add("Căn giữa theo chiều ngang", () => AlignSelected(AlignMode.CenterH));
                Add("Căn phải", () => AlignSelected(AlignMode.Right));
                list.Add(new ToolStripSeparator());
                Add("Căn trên", () => AlignSelected(AlignMode.Top));
                Add("Căn giữa theo chiều dọc", () => AlignSelected(AlignMode.CenterV));
                Add("Căn dưới", () => AlignSelected(AlignMode.Bottom));
                list.Add(new ToolStripSeparator());
                Add("Căn giữa vùng khắc", () => AlignSelected(AlignMode.CenterBoth));
                list.Add(new ToolStripSeparator());
                Add("Phân phối đều theo chiều ngang", () => DistributeAll(true), needThree: true);
                Add("Phân phối đều theo chiều dọc", () => DistributeAll(false), needThree: true);
                list.Add(new ToolStripSeparator());
            }

            list.AddRange(BuildZOrderItems());
            return list.ToArray();
        }

        private ToolStripItem[] BuildZOrderItems()
        {
            var list = new List<ToolStripItem>();
            void Add(string text, int mode, Keys keys, string keyText)
            {
                // ShortcutKeyDisplayString: nếu không đặt, WinForms hiện tên phím thô "Oem6" / "OemOpenBrackets"
                var mi = new ToolStripMenuItem(text) { ShortcutKeys = keys, ShortcutKeyDisplayString = keyText };
                mi.Click += (s, e) => MoveInOrder(mode);
                arrangeNeedsSelection.Add(mi);
                list.Add(mi);
            }
            Add("Đưa lên trên cùng", 0, Keys.Control | Keys.Shift | Keys.OemCloseBrackets, "Ctrl+Shift+]");
            Add("Đưa lên một tầng", 1, Keys.Control | Keys.OemCloseBrackets, "Ctrl+]");
            Add("Đưa xuống một tầng", 2, Keys.Control | Keys.OemOpenBrackets, "Ctrl+[");
            Add("Đưa xuống dưới cùng", 3, Keys.Control | Keys.Shift | Keys.OemOpenBrackets, "Ctrl+Shift+[");
            return list.ToArray();
        }

        private void RefreshArrangeEnabled()
        {
            bool edit = !ReadOnlyView;
            bool has = edit && selectedItem != null;
            foreach (var mi in arrangeNeedsSelection) mi.Enabled = has;
            foreach (var mi in arrangeNeedsThree) mi.Enabled = edit && projectItems.Count >= 3;
            RefreshTransformEnabled();
        }

        // ------------------------------------------------------------------
        //  Khung bao của đối tượng (có tính xoay) - đơn vị px canvas
        // ------------------------------------------------------------------
        private static RectangleF ItemBounds(Vectoritem it)
        {
            if (it is VectorLine l)
            {
                float x0 = Math.Min(l.X, l.X2), x1 = Math.Max(l.X, l.X2);
                float y0 = Math.Min(l.Y, l.Y2), y1 = Math.Max(l.Y, l.Y2);
                return RectangleF.FromLTRB(x0, y0, x1, y1);
            }

            var r = new RectangleF(it.X, it.Y, it.Width, it.Height);
            if (it.Rotation == 0f) return r;

            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            var pts = new[]
            {
                RotateAround(new PointF(r.Left,  r.Top),    cx, cy, it.Rotation),
                RotateAround(new PointF(r.Right, r.Top),    cx, cy, it.Rotation),
                RotateAround(new PointF(r.Right, r.Bottom), cx, cy, it.Rotation),
                RotateAround(new PointF(r.Left,  r.Bottom), cx, cy, it.Rotation)
            };
            return RectangleF.FromLTRB(pts.Min(p => p.X), pts.Min(p => p.Y),
                                       pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        private static void ShiftItem(Vectoritem it, float dx, float dy) =>
            it.MoveTo(it.X + dx, it.Y + dy);        // Line tự dời cả 2 đầu mút

        private void AfterArrange()
        {
            UpdatePropertiesToUI();                 // các ô số bên phải chạy theo
            pictureBox1.Invalidate();
        }

        // ------------------------------------------------------------------
        //  Căn chỉnh: 1 hình -> so với vùng khắc; nhiều hình -> so với khung bao của nhóm
        // ------------------------------------------------------------------
        private List<Vectoritem> ArrangeTargets()
        {
            var sel = SelectedList();
            if (sel.Count == 0 && selectedItem != null) sel.Add(selectedItem);
            return sel;
        }

        private static RectangleF UnionBounds(IEnumerable<Vectoritem> items)
        {
            RectangleF u = RectangleF.Empty; bool first = true;
            foreach (var it in items)
            {
                var b = ItemBounds(it);
                u = first ? b : RectangleF.Union(u, b);
                first = false;
            }
            return u;
        }

        private void AlignSelected(AlignMode mode)
        {
            if (ReadOnlyView) return;
            var sel = ArrangeTargets();
            if (sel.Count == 0) return;

            bool group = sel.Count > 1;
            float areaW = WorkAreaMm.Width / MmPerPx, areaH = WorkAreaMm.Height / MmPerPx;
            RectangleF target = group ? UnionBounds(sel) : new RectangleF(0, 0, areaW, areaH);

            var moves = new List<(Vectoritem It, float Dx, float Dy)>();

            if (group && mode == AlignMode.CenterBoth)
            {
                // nhiều hình: dời CẢ NHÓM như một khối vào giữa vùng khắc
                float gx = areaW / 2f - (target.Left + target.Width / 2f);
                float gy = areaH / 2f - (target.Top + target.Height / 2f);
                foreach (var it in sel) moves.Add((it, gx, gy));
            }
            else
            {
                foreach (var it in sel)
                {
                    RectangleF b = ItemBounds(it);
                    float dx = 0, dy = 0;
                    if (mode is AlignMode.Left) dx = target.Left - b.Left;
                    if (mode is AlignMode.Right) dx = target.Right - b.Right;
                    if (mode is AlignMode.CenterH or AlignMode.CenterBoth)
                        dx = target.Left + target.Width / 2f - (b.Left + b.Width / 2f);
                    if (mode is AlignMode.Top) dy = target.Top - b.Top;
                    if (mode is AlignMode.Bottom) dy = target.Bottom - b.Bottom;
                    if (mode is AlignMode.CenterV or AlignMode.CenterBoth)
                        dy = target.Top + target.Height / 2f - (b.Top + b.Height / 2f);
                    moves.Add((it, dx, dy));
                }
            }

            if (moves.All(m => Math.Abs(m.Dx) < 1e-3f && Math.Abs(m.Dy) < 1e-3f)) return;

            PushUndo();
            CancelInteractions();
            foreach (var (it, dx, dy) in moves) ShiftItem(it, dx, dy);
            AfterArrange();
        }

        // ------------------------------------------------------------------
        //  Phân phối đều: các hình đang chọn (>= 3); nếu chọn ít hơn thì dùng TẤT CẢ đối tượng
        // ------------------------------------------------------------------
        private void DistributeAll(bool horizontal)
        {
            if (ReadOnlyView) return;

            var pool = SelectedList();
            if (pool.Count < 3) pool = projectItems.ToList();
            if (pool.Count < 3)
            {
                MessageBox.Show(this, "Cần ít nhất 3 đối tượng để phân phối đều.", AppTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var list = pool
                .Select(i => (Item: i, B: ItemBounds(i)))
                .OrderBy(t => horizontal ? t.B.Left : t.B.Top)
                .ToList();

            float Size(RectangleF r) => horizontal ? r.Width : r.Height;
            float Start(RectangleF r) => horizontal ? r.Left : r.Top;
            float End(RectangleF r) => horizontal ? r.Right : r.Bottom;

            float start = Start(list[0].B);
            float end = list.Max(t => End(t.B));
            float total = list.Sum(t => Size(t.B));
            float gap = (end - start - total) / (list.Count - 1);

            PushUndo();
            CancelInteractions();
            float pos = start;
            foreach (var (item, b) in list)
            {
                float d = pos - Start(b);
                if (Math.Abs(d) > 1e-3f) ShiftItem(item, horizontal ? d : 0, horizontal ? 0 : d);
                pos += Size(b) + gap;
            }
            AfterArrange();
        }

        // ------------------------------------------------------------------
        //  Thứ tự chồng - các "tầng" (cả nhóm đang chọn, giữ nguyên thứ tự tương đối giữa các hình)
        //  0 = lên trên cùng, 1 = lên 1 lớp, 2 = xuống 1 lớp, 3 = xuống dưới cùng
        // ------------------------------------------------------------------
        private void MoveInOrder(int mode)
        {
            if (ReadOnlyView) return;
            var sel = ArrangeTargets();
            if (sel.Count == 0) return;

            var before = projectItems.ToList();
            var chosen = new HashSet<Vectoritem>(sel);
            int n = projectItems.Count;

            if (mode == 0 || mode == 3)
            {
                var picked = projectItems.Where(chosen.Contains).ToList();
                var rest = projectItems.Where(i => !chosen.Contains(i)).ToList();
                projectItems.Clear();
                if (mode == 0) { projectItems.AddRange(rest); projectItems.AddRange(picked); }
                else { projectItems.AddRange(picked); projectItems.AddRange(rest); }
            }
            else if (mode == 1)                      // đi lên 1 bậc: duyệt từ trên xuống
            {
                for (int i = n - 2; i >= 0; i--)
                    if (chosen.Contains(projectItems[i]) && !chosen.Contains(projectItems[i + 1]))
                        (projectItems[i], projectItems[i + 1]) = (projectItems[i + 1], projectItems[i]);
            }
            else                                     // đi xuống 1 bậc: duyệt từ dưới lên
            {
                for (int i = 1; i < n; i++)
                    if (chosen.Contains(projectItems[i]) && !chosen.Contains(projectItems[i - 1]))
                        (projectItems[i], projectItems[i - 1]) = (projectItems[i - 1], projectItems[i]);
            }

            if (projectItems.SequenceEqual(before)) return;

            // đã đổi thật: phục hồi thứ tự cũ để PushUndo chụp đúng trạng thái trước, rồi áp lại
            var after = projectItems.ToList();
            projectItems.Clear(); projectItems.AddRange(before);
            PushUndo();
            CancelInteractions();
            projectItems.Clear(); projectItems.AddRange(after);
            AfterArrange();
        }

        // ------------------------------------------------------------------
        //  Việt hóa tên menu (chỉ đổi chữ hiển thị, không đụng tới logic)
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, string> ViText = new()
        {
            ["Edit"] = "Chỉnh sửa",
            ["View"] = "Xem",
            ["Share"] = "Chia sẻ",
            ["Original"] = "Bản vẽ",
            ["Edited"] = "Đã xử lý",
            ["Laser Path"] = "Đường laser",
            ["Grid"] = "Lưới",
            ["Ruler"] = "Thước",
            ["Undo"] = "Hoàn tác",
            ["Redo"] = "Làm lại",
            ["Copy"] = "Sao chép",
            ["Cut"] = "Cắt",
            ["Paste"] = "Dán",
            ["Delete"] = "Xóa",
            ["Insert"] = "Chèn",
            ["Shape"] = "Hình",
            ["shape"] = "Hình",
            ["Line"] = "Đường thẳng",
            ["Rectangle"] = "Hình chữ nhật",
            ["Circle"] = "Hình tròn",
            ["circle"] = "Hình tròn",
            ["Text"] = "Chữ",
            ["text"] = "Chữ",
            ["Image"] = "Ảnh",
            ["image"] = "Ảnh"
        };

        private static void LocalizeMenus(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                if (it.Text != null && ViText.TryGetValue(it.Text, out string? vi)) it.Text = vi;
                if (it is ToolStripDropDownItem dd) LocalizeMenus(dd.DropDownItems);
            }
        }
    }
}

// =====================================================================
// PHẦN 4: Tạo mảng bản sao   (trước đây là file Form1_Array.cs)
// =====================================================================
// =====================================================================
//  Form1_Array.cs  -  TẠO MÃ VẠCH (menu / nút / nháy đúp) + TẠO MẢNG BẢN SAO
//
//  1) MÃ VẠCH: nút trên thanh công cụ trái, mục "Chèn mã vạch / QR..." trong menu Chỉnh sửa và menu chuột phải,
//     nháy đúp vào mã (hoặc nút "Sửa nội dung / loại mã" ở bảng thông số) để sửa. Xem Barcode.cs.
//
//  2) TẠO MẢNG (Array): chọn 1 hoặc nhiều đối tượng (hình, chữ, ảnh, mã vạch...) rồi
//        menu Sắp xếp > Tạo mảng...   hoặc   Ctrl+Shift+A   hoặc nút trên thanh công cụ trái.
//     - LƯỚI: số cột x số hàng, khoảng cách theo "khe hở mép-mép" hoặc "bước tâm-tâm".
//     - VÒNG TRÒN: số bản, tổng góc, tâm quay (mm), có thể xoay bản sao theo vòng.
//     - Tự tăng số: chữ / mã vạch có số ở cuối (SN-0001) sẽ thành SN-0002, SN-0003... ở các bản sao.
//     - Chọn nhiều đối tượng thì mỗi bản sao có thể gộp thành 1 nhóm. Cả mảng chỉ cần 1 lần Hoàn tác.
//
//  Gọi InitBarcodeAndArray() 1 lần ở cuối InitWorkspaceLayout() (Form1_Tools.cs).
// =====================================================================
namespace test01_1014
{
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;

    public partial class Form1
    {
        private bool barcodeInited;

        private void InitBarcodeAndArray()
        {
            if (barcodeInited) return;
            barcodeInited = true;

            // ---- menu Chỉnh sửa: Chèn mã vạch ----
            var miBar = new ToolStripMenuItem("Chèn mã vạch / QR...");
            miBar.Click += (s, e) => PickBarcode(null);
            toolStripMenuItem1.DropDownItems.Add(new ToolStripSeparator());
            toolStripMenuItem1.DropDownItems.Add(miBar);

            // ---- menu Sắp xếp: Tạo mảng ----
            if (arrangeRoot != null)
            {
                var miArr = new ToolStripMenuItem("Tạo mảng (Array)...") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.A };
                miArr.Click += (s, e) => CreateArray();
                arrangeNeedsSelection.Add(miArr);
                arrangeRoot.DropDownItems.Add(new ToolStripSeparator());
                arrangeRoot.DropDownItems.Add(miArr);
            }

            // ---- menu chuột phải ----
            if (pictureBox1.ContextMenuStrip is { } cms)
            {
                var cBar = new ToolStripMenuItem("Chèn mã vạch / QR...");
                var cArr = new ToolStripMenuItem("Tạo mảng (Array)...");
                cBar.Click += (s, e) => PickBarcode(contextWorldPos);
                cArr.Click += (s, e) => CreateArray();
                cms.Items.Add(new ToolStripSeparator());
                cms.Items.Add(cBar);
                cms.Items.Add(cArr);
                cms.Opening += (s, e) => cArr.Enabled = !ReadOnlyView && selectedItem != null;
            }

            // ---- bảng thông số: nút "Sửa nội dung / loại mã" ----
            propPalette.BarcodeEditClicked += () =>
            {
                if (!ReadOnlyView && selectedItem is VectorBarcode b) EditBarcode(b);
            };

            // ---- nháy đúp vào mã vạch để sửa ----
            pictureBox1.MouseDoubleClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || ReadOnlyView || curTool != DrawTool.None) return;
                var w = ScreenToWorld(e.Location);
                if (selectedItem is VectorBarcode b && HitTestItem(b, w)) EditBarcode(b);
            };
        }

        // ------------------------------------------------------------------
        //  MÃ VẠCH
        // ------------------------------------------------------------------
        private void PickBarcode(PointF? at)
        {
            if (ReadOnlyView) SetActiveTab(null);
            CancelTool();
            PointF c = at ?? ViewCenter();

            using var dlg = new BarcodeForm(DeviceDpi, null, BarcodeKind.QR, -1, false);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    var bc = new VectorBarcode(dlg.ResultContent, dlg.ResultKind, dlg.ResultQuiet, dlg.ResultShowText);
                    bc.X = c.X - bc.Width / 2f;
                    bc.Y = c.Y - bc.Height / 2f;
                    AddItem(bc);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Không tạo được mã:\n" + ex.Message, "Mã vạch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            SyncToolHighlight();
            pictureBox1.Focus();
        }

        private void EditBarcode(VectorBarcode bc)
        {
            using var dlg = new BarcodeForm(DeviceDpi, bc.Content, bc.Kind, bc.Quiet, bc.ShowText);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var nb = BarcodeEncoder.Render(dlg.ResultContent, dlg.ResultKind, dlg.ResultQuiet, dlg.ResultShowText);
                nb.Dispose();                                    // chỉ kiểm tra hợp lệ trước khi lưu Undo
                PushUndo();
                bc.Rebuild(dlg.ResultContent, dlg.ResultKind, dlg.ResultQuiet, dlg.ResultShowText);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không sửa được mã:\n" + ex.Message, "Mã vạch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        // ------------------------------------------------------------------
        //  TẠO MẢNG
        // ------------------------------------------------------------------
        private void CreateArray()
        {
            if (ReadOnlyView) return;
            var sel = ArrangeTargets();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Hãy chọn đối tượng cần tạo mảng trước.", "Tạo mảng", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CancelInteractions();

            RectangleF u = UnionBounds(sel);
            bool hasSerial = sel.Any(i => i is VectorText || i is VectorBarcode);
            using var dlg = new ArrayForm(DeviceDpi, MmPerPx, u, sel.Count, hasSerial);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            ArrayParams p = dlg.Result;

            // ---- danh sách vị trí của các bản sao ----
            var places = new List<Action<Vectoritem>>();
            if (!p.Polar)
            {
                float gx = p.GapX / MmPerPx, gy = p.GapY / MmPerPx;
                float pitchX = p.EdgeGap ? u.Width + gx : gx;
                float pitchY = p.EdgeGap ? u.Height + gy : gy;
                for (int r = 0; r < p.Rows; r++)
                    for (int c = 0; c < p.Cols; c++)
                    {
                        if (r == 0 && c == 0) continue;
                        float dx = c * pitchX, dy = r * pitchY;
                        places.Add(it => ShiftItem(it, dx, dy));
                    }
            }
            else
            {
                float cx = p.CenterXmm / MmPerPx, cy = p.CenterYmm / MmPerPx;
                bool full = Math.Abs(p.TotalAngle) >= 359.999f;
                float step = full ? p.TotalAngle / p.Count : (p.Count > 1 ? p.TotalAngle / (p.Count - 1) : 0f);
                if (!p.Clockwise) step = -step;
                bool turn = p.RotateItems;
                for (int i = 1; i < p.Count; i++)
                {
                    float ang = i * step;
                    places.Add(it => RotateItemAbout(it, cx, cy, ang, turn));
                }
            }

            int total = places.Count * sel.Count;
            if (total == 0) return;
            if (total > 3000 &&
                MessageBox.Show(this, $"Sẽ tạo {total} đối tượng, có thể làm chậm chương trình. Tiếp tục?", "Tạo mảng",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // ---- tạo bản sao ----
            var copies = new List<Vectoritem>();
            int serialFail = 0;
            for (int k = 0; k < places.Count; k++)
            {
                string? gid = sel.Count > 1 && p.GroupCopies ? Guid.NewGuid().ToString("N") : null;
                foreach (var src in sel)
                {
                    var c = CloneItem(src, true);
                    if (c == null) continue;
                    c.Id = Guid.NewGuid().ToString();
                    c.GroupId = gid;
                    places[k](c);
                    if (p.Serial && !ApplySerial(c, (long)(k + 1) * p.SerialStep)) serialFail++;
                    copies.Add(c);
                }
            }

            PushUndo();
            projectItems.AddRange(copies);
            foreach (var o in projectItems) o.IsSelected = false;
            foreach (var c in copies) c.IsSelected = true;
            selectedItem = copies[copies.Count - 1];
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();

            if (serialFail > 0)
                MessageBox.Show(this, $"{serialFail} bản sao không tăng số được (nội dung không có số ở cuối, hoặc số không hợp lệ với loại mã). Các bản này giữ nguyên nội dung gốc.",
                                "Tạo mảng", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Tăng số cuối của chữ / mã vạch. true = đã đổi được (hoặc đối tượng không có nội dung để đổi)
        private static bool ApplySerial(Vectoritem c, long add)
        {
            if (c is VectorText t)
            {
                string n = BarcodeEncoder.IncrementTrailing(t.TextContent, add);
                if (n == t.TextContent) return false;
                t.TextContent = n;
                return true;
            }
            if (c is VectorBarcode b)
            {
                try
                {
                    string n = BarcodeEncoder.SerialContent(b.Kind, b.Content, add);
                    if (n == b.Content) return false;
                    b.Rebuild(n, b.Kind, b.Quiet, b.ShowText);
                    return true;
                }
                catch { return false; }
            }
            return true;
        }

        // Quay một đối tượng quanh điểm (px, py). turn = true: đối tượng cũng xoay theo; false: chỉ dời vị trí
        private static void RotateItemAbout(Vectoritem it, float px, float py, float deg, bool turn)
        {
            if (it is VectorLine l)
            {
                if (turn)
                {
                    var a = RotateAround(new PointF(l.X, l.Y), px, py, deg);
                    var b = RotateAround(new PointF(l.X2, l.Y2), px, py, deg);
                    l.X = a.X; l.Y = a.Y; l.X2 = b.X; l.Y2 = b.Y;
                }
                else
                {
                    var m0 = new PointF((l.X + l.X2) / 2f, (l.Y + l.Y2) / 2f);
                    var m1 = RotateAround(m0, px, py, deg);
                    ShiftItem(l, m1.X - m0.X, m1.Y - m0.Y);
                }
                return;
            }
            var c0 = new PointF(it.X + it.Width / 2f, it.Y + it.Height / 2f);
            var c1 = RotateAround(c0, px, py, deg);
            it.X = c1.X - it.Width / 2f;
            it.Y = c1.Y - it.Height / 2f;
            if (turn) it.Rotation = NormAngle(it.Rotation + deg);
        }
    }

    // ------------------------------------------------------------------
    //  Thông số mảng
    // ------------------------------------------------------------------
    public class ArrayParams
    {
        public bool Polar;
        // lưới
        public int Cols = 3, Rows = 3;
        public float GapX = 5f, GapY = 5f;     // mm
        public bool EdgeGap = true;            // true: khe hở mép-mép; false: bước tâm-tâm
        // vòng tròn
        public int Count = 6;                  // số bản (gồm cả bản gốc)
        public float TotalAngle = 360f;
        public float CenterXmm, CenterYmm;
        public bool RotateItems = true;
        public bool Clockwise = true;
        // chung
        public bool Serial;
        public int SerialStep = 1;
        public bool GroupCopies = true;
    }

    // ------------------------------------------------------------------
    //  Hộp thoại Tạo mảng
    // ------------------------------------------------------------------
    public class ArrayForm : Form
    {
        public ArrayParams Result = new();

        private readonly RadioButton rbGrid = new() { Text = "Lưới (hàng x cột)", AutoSize = true, Checked = true };
        private readonly RadioButton rbPolar = new() { Text = "Vòng tròn", AutoSize = true };
        private readonly Panel pnlGrid = new();
        private readonly Panel pnlPolar = new();

        private readonly NumericUpDown numCols = new() { Minimum = 1, Maximum = 500, Value = 3 };
        private readonly NumericUpDown numRows = new() { Minimum = 1, Maximum = 500, Value = 3 };
        private readonly ComboBox cmbGap = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown numGapX = new() { Minimum = -10000, Maximum = 10000, DecimalPlaces = 2, Increment = 1, Value = 5 };
        private readonly NumericUpDown numGapY = new() { Minimum = -10000, Maximum = 10000, DecimalPlaces = 2, Increment = 1, Value = 5 };

        private readonly NumericUpDown numCount = new() { Minimum = 2, Maximum = 1000, Value = 6 };
        private readonly NumericUpDown numAngle = new() { Minimum = -360, Maximum = 360, DecimalPlaces = 1, Value = 360 };
        private readonly NumericUpDown numCX = new() { Minimum = -100000, Maximum = 100000, DecimalPlaces = 2 };
        private readonly NumericUpDown numCY = new() { Minimum = -100000, Maximum = 100000, DecimalPlaces = 2 };
        private readonly CheckBox chkTurn = new() { Text = "Xoay bản sao theo vòng", AutoSize = true, Checked = true };
        private readonly CheckBox chkCw = new() { Text = "Quay cùng chiều kim đồng hồ", AutoSize = true, Checked = true };

        private readonly CheckBox chkSerial = new() { Text = "Tự tăng số ở cuối chữ / mã vạch", AutoSize = true };
        private readonly NumericUpDown numStep = new() { Minimum = 1, Maximum = 100000, Value = 1 };
        private readonly CheckBox chkGroup = new() { Text = "Gộp mỗi bản sao thành 1 nhóm", AutoSize = true, Checked = true };
        private readonly Label lblInfo = new() { ForeColor = Color.DimGray };
        private readonly Button btnOk = new() { Text = "Tạo", DialogResult = DialogResult.OK };

        private readonly float mmW, mmH;
        private int lastGapMode = 0;

        public ArrayForm(float dpi, float mmPerPx, RectangleF unionPx, int selCount, bool hasSerial)
        {
            int S(int v) => (int)Math.Round(v * dpi / 96f);
            mmW = unionPx.Width * mmPerPx;
            mmH = unionPx.Height * mmPerPx;

            Text = "Tạo mảng bản sao";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(430), S(440));

            rbGrid.Location = new Point(S(14), S(12));
            rbPolar.Location = new Point(S(190), S(12));

            // ---- panel lưới ----
            pnlGrid.Bounds = new Rectangle(S(14), S(42), S(402), S(150));
            Place(pnlGrid, S, "Số cột", numCols, 0, 0);
            Place(pnlGrid, S, "Số hàng", numRows, 210, 0);
            cmbGap.Items.AddRange(new object[] { "Khe hở giữa các bản (mép - mép)", "Bước lặp (từ bản này đến bản kế)" });
            cmbGap.SelectedIndex = 0;
            pnlGrid.Controls.Add(new Label { Text = "Khoảng cách tính theo", Bounds = new Rectangle(0, S(40), S(200), S(20)) });
            cmbGap.Bounds = new Rectangle(0, S(60), S(402), S(26));
            pnlGrid.Controls.Add(cmbGap);
            Place(pnlGrid, S, "Ngang (mm)", numGapX, 0, 96);
            Place(pnlGrid, S, "Dọc (mm)", numGapY, 210, 96);

            // ---- panel vòng tròn ----
            pnlPolar.Bounds = pnlGrid.Bounds;
            pnlPolar.Visible = false;
            Place(pnlPolar, S, "Số bản (gồm bản gốc)", numCount, 0, 0);
            Place(pnlPolar, S, "Tổng góc (độ)", numAngle, 210, 0);
            Place(pnlPolar, S, "Tâm quay X (mm)", numCX, 0, 48);
            Place(pnlPolar, S, "Tâm quay Y (mm)", numCY, 210, 48);
            chkTurn.Location = new Point(0, S(100));
            chkCw.Location = new Point(0, S(124));
            pnlPolar.Controls.Add(chkTurn);
            pnlPolar.Controls.Add(chkCw);

            // tâm quay mặc định: ngay dưới vùng chọn, cách một khoảng bằng ~1,5 lần kích thước lớn nhất
            float r = Math.Max(mmW, mmH) * 1.5f + 10f;
            numCX.Value = Clamp((decimal)(unionPx.Left * mmPerPx + mmW / 2f), numCX);
            numCY.Value = Clamp((decimal)(unionPx.Top * mmPerPx + mmH / 2f + r), numCY);

            // ---- phần chung ----
            chkSerial.Location = new Point(S(14), S(204));
            chkSerial.Enabled = hasSerial;
            var lblStep = new Label { Text = "Bước tăng", Bounds = new Rectangle(S(260), S(206), S(70), S(20)) };
            numStep.Bounds = new Rectangle(S(332), S(202), S(80), S(26));
            chkGroup.Location = new Point(S(14), S(234));
            chkGroup.Enabled = selCount > 1;
            if (selCount <= 1) chkGroup.Checked = false;

            var note = new Label
            {
                Text = "Bản gốc giữ nguyên vị trí. Lưới: bản sao mở rộng sang phải và xuống dưới (nhập số âm để đi hướng ngược lại). " +
                       "Vòng tròn: bản gốc nằm ở vị trí đầu tiên trên vòng.",
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Bounds = new Rectangle(S(14), S(266), S(402), S(66))
            };
            lblInfo.Bounds = new Rectangle(S(14), S(340), S(402), S(24));
            lblInfo.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            btnOk.Bounds = new Rectangle(S(250), S(396), S(80), S(30));
            var btnCancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(S(336), S(396), S(80), S(30)) };

            Controls.AddRange(new Control[] { rbGrid, rbPolar, pnlGrid, pnlPolar, chkSerial, lblStep, numStep, chkGroup, note, lblInfo, btnOk, btnCancel });
            AcceptButton = btnOk; CancelButton = btnCancel;

            rbGrid.CheckedChanged += (s, e) => { pnlGrid.Visible = rbGrid.Checked; pnlPolar.Visible = !rbGrid.Checked; Refresh2(); };
            cmbGap.SelectedIndexChanged += (s, e) => ConvertGap();
            foreach (var n in new NumericUpDown[] { numCols, numRows, numGapX, numGapY, numCount, numAngle, numCX, numCY, numStep })
                n.ValueChanged += (s, e) => Refresh2();
            Refresh2();
        }

        private static decimal Clamp(decimal v, NumericUpDown n) => Math.Min(n.Maximum, Math.Max(n.Minimum, Math.Round(v, 2)));

        private static void Place(Panel parent, Func<int, int> S, string label, NumericUpDown num, int x, int y)
        {
            parent.Controls.Add(new Label { Text = label, Bounds = new Rectangle(S(x), S(y), S(200), S(20)) });
            num.Bounds = new Rectangle(S(x), S(y + 20), S(190), S(26));
            parent.Controls.Add(num);
        }

        // Đổi cách tính khoảng cách thì quy đổi số cho khớp (bước = kích thước + khe hở)
        private void ConvertGap()
        {
            int mode = cmbGap.SelectedIndex;
            if (mode == lastGapMode) return;
            decimal dx = (decimal)mmW, dy = (decimal)mmH;
            if (mode == 1) { numGapX.Value = Clamp(numGapX.Value + dx, numGapX); numGapY.Value = Clamp(numGapY.Value + dy, numGapY); }
            else { numGapX.Value = Clamp(numGapX.Value - dx, numGapX); numGapY.Value = Clamp(numGapY.Value - dy, numGapY); }
            lastGapMode = mode;
        }

        private void Refresh2()
        {
            int n = rbGrid.Checked ? (int)(numCols.Value * numRows.Value) - 1 : (int)numCount.Value - 1;
            lblInfo.Text = n > 0 ? $"Sẽ tạo thêm {n} bản sao" : "Chưa có bản sao nào (tăng số cột / hàng)";
            btnOk.Enabled = n > 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                Result = new ArrayParams
                {
                    Polar = rbPolar.Checked,
                    Cols = (int)numCols.Value,
                    Rows = (int)numRows.Value,
                    GapX = (float)numGapX.Value,
                    GapY = (float)numGapY.Value,
                    EdgeGap = cmbGap.SelectedIndex == 0,
                    Count = (int)numCount.Value,
                    TotalAngle = (float)numAngle.Value,
                    CenterXmm = (float)numCX.Value,
                    CenterYmm = (float)numCY.Value,
                    RotateItems = chkTurn.Checked,
                    Clockwise = chkCw.Checked,
                    Serial = chkSerial.Checked && chkSerial.Enabled,
                    SerialStep = (int)numStep.Value,
                    GroupCopies = chkGroup.Checked && chkGroup.Enabled
                };
            }
            base.OnFormClosing(e);
        }
    }
}

// =====================================================================
// PHẦN 5: Mã vạch / QR   (trước đây là file Barcode.cs)
// =====================================================================
// =====================================================================
//  Barcode.cs  -  MÃ VẠCH / QR
//
//  CẦN CÀI THƯ VIỆN (1 lần): Visual Studio -> Project -> Manage NuGet Packages -> Browse -> "ZXing.Net" -> Install
//  (chỉ cần gói ZXing.Net, không cần gói bindings).
//
//  Cách hoạt động: VectorBarcode kế thừa VectorImage, nội dung mã được vẽ thành ảnh đen/trắng nên
//  mọi thứ của ảnh dùng được luôn: kéo, xoay, đổi kích thước, khóa tỉ lệ, lưu file, nháp tự động, Undo
//  và xuất G-code (khắc raster, chế độ Đen trắng, khắc phần ĐEN của mã).
//
//  Loại mã: QR, Data Matrix, Aztec, PDF417 (2D) và Code 128, Code 39, Code 93, EAN-13, EAN-8, UPC-A, UPC-E,
//  ITF, Codabar (1D).
// =====================================================================
namespace test01_1014
{
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Text.RegularExpressions;
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;

    public enum BarcodeKind { QR, DataMatrix, Aztec, PDF417, Code128, Code39, Code93, EAN13, EAN8, UPCA, UPCE, ITF, Codabar }

    // ------------------------------------------------------------------
    //  Mã hóa: nội dung -> ảnh bitmap đen/trắng (mỗi module = s điểm ảnh, có viền trống)
    // ------------------------------------------------------------------
    public static class BarcodeEncoder
    {
        public static readonly (BarcodeKind Kind, string Name, string Hint)[] Kinds =
        {
            (BarcodeKind.QR,         "QR Code",             "Văn bản, đường dẫn (URL), số... bất kỳ"),
            (BarcodeKind.DataMatrix, "Data Matrix",         "Văn bản / số, mã vuông nhỏ gọn"),
            (BarcodeKind.Aztec,      "Aztec",               "Văn bản / số bất kỳ"),
            (BarcodeKind.PDF417,     "PDF417",              "Văn bản dài, mã chữ nhật nhiều hàng"),
            (BarcodeKind.Code128,    "Code 128",            "Chữ, số, ký tự ASCII bất kỳ (phổ biến nhất)"),
            (BarcodeKind.Code39,     "Code 39",             "Chữ HOA A-Z, số 0-9, khoảng trắng và - . $ / + %"),
            (BarcodeKind.Code93,     "Code 93",             "Chữ HOA A-Z, số 0-9 và - . $ / + % khoảng trắng"),
            (BarcodeKind.EAN13,      "EAN-13",              "12 hoặc 13 chữ số (số kiểm tra tự tính)"),
            (BarcodeKind.EAN8,       "EAN-8",               "7 hoặc 8 chữ số (số kiểm tra tự tính)"),
            (BarcodeKind.UPCA,       "UPC-A",               "11 hoặc 12 chữ số (số kiểm tra tự tính)"),
            (BarcodeKind.UPCE,       "UPC-E",               "7 hoặc 8 chữ số, bắt đầu bằng 0 hoặc 1"),
            (BarcodeKind.ITF,        "ITF (Interleaved 2/5)", "Chỉ chữ số, số lượng chữ số phải CHẴN"),
            (BarcodeKind.Codabar,    "Codabar",             "Số 0-9 và - $ : / . + ; nên bắt đầu và kết thúc bằng A, B, C hoặc D"),
        };

        public static bool Is2D(BarcodeKind k) =>
            k == BarcodeKind.QR || k == BarcodeKind.DataMatrix || k == BarcodeKind.Aztec || k == BarcodeKind.PDF417;

        public static string NameOf(BarcodeKind k) => Kinds.First(x => x.Kind == k).Name;
        public static string HintOf(BarcodeKind k) => Kinds.First(x => x.Kind == k).Hint;

        public static int DefaultQuiet(BarcodeKind k) => Is2D(k) ? 2 : 6;

        private static ZXing.BarcodeFormat ToFormat(BarcodeKind k) => k switch
        {
            BarcodeKind.QR => ZXing.BarcodeFormat.QR_CODE,
            BarcodeKind.DataMatrix => ZXing.BarcodeFormat.DATA_MATRIX,
            BarcodeKind.Aztec => ZXing.BarcodeFormat.AZTEC,
            BarcodeKind.PDF417 => ZXing.BarcodeFormat.PDF_417,
            BarcodeKind.Code128 => ZXing.BarcodeFormat.CODE_128,
            BarcodeKind.Code39 => ZXing.BarcodeFormat.CODE_39,
            BarcodeKind.Code93 => ZXing.BarcodeFormat.CODE_93,
            BarcodeKind.EAN13 => ZXing.BarcodeFormat.EAN_13,
            BarcodeKind.EAN8 => ZXing.BarcodeFormat.EAN_8,
            BarcodeKind.UPCA => ZXing.BarcodeFormat.UPC_A,
            BarcodeKind.UPCE => ZXing.BarcodeFormat.UPC_E,
            BarcodeKind.ITF => ZXing.BarcodeFormat.ITF,
            _ => ZXing.BarcodeFormat.CODABAR
        };

        // quiet < 0 = dùng viền mặc định. Ném ngoại lệ (kèm lời giải thích) nếu nội dung không hợp lệ với loại mã.
        public static System.Drawing.Bitmap Render(string content, BarcodeKind kind, int quiet, bool showText)
        {
            if (string.IsNullOrEmpty(content)) throw new ArgumentException("Nội dung mã đang trống.");
            if (kind == BarcodeKind.Code39 || kind == BarcodeKind.Code93) content = content.ToUpperInvariant();

            var hints = new Dictionary<ZXing.EncodeHintType, object> { [ZXing.EncodeHintType.MARGIN] = 0 };
            if (kind == BarcodeKind.QR)
            {
                hints[ZXing.EncodeHintType.CHARACTER_SET] = "UTF-8";
                hints[ZXing.EncodeHintType.ERROR_CORRECTION] = ZXing.QrCode.Internal.ErrorCorrectionLevel.M;
            }

            bool linear = !Is2D(kind);
            ZXing.Common.BitMatrix m;
            try
            {
                m = new ZXing.MultiFormatWriter().encode(content, ToFormat(kind), 0, linear ? 1 : 0, hints);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Nội dung không hợp lệ với " + NameOf(kind) + ": " + HintOf(kind) + "\n(" + ex.Message + ")");
            }

            // Cắt sát phần có điểm đen (đề phòng thư viện tự thêm lề)
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < m.Height; y++)
                for (int x = 0; x < m.Width; x++)
                    if (m[x, y])
                    {
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
            if (x1 < 0) throw new ArgumentException("Không tạo được mã từ nội dung này.");
            int w = x1 - x0 + 1, h = y1 - y0 + 1;

            int s = linear ? 3 : Math.Min(24, Math.Max(2, (int)Math.Ceiling(220.0 / Math.Max(w, h))));
            int q = quiet >= 0 ? quiet : DefaultQuiet(kind);
            int qy = linear ? Math.Max(2, q / 2) : q;
            int barH = linear ? Math.Max(30, (int)Math.Round(w * 0.28)) : h;     // chiều cao vạch (đơn vị module)
            int textH = linear && showText ? 9 : 0;

            int bw = (w + 2 * q) * s, bh = (barH + textH + 2 * qy) * s;
            var bmp = new System.Drawing.Bitmap(bw, bh, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.White);

                if (linear)
                {
                    int x = 0;
                    while (x < w)
                    {
                        if (!m[x0 + x, y0]) { x++; continue; }
                        int run = 1;
                        while (x + run < w && m[x0 + x + run, y0]) run++;
                        g.FillRectangle(Brushes.Black, (q + x) * s, qy * s, run * s, barH * s);
                        x += run;
                    }
                    if (textH > 0)
                    {
                        using var f = new Font("Arial", textH * s * 0.78f, FontStyle.Regular, GraphicsUnit.Pixel);
                        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.DrawString(content, f, Brushes.Black, new RectangleF(0, (qy + barH) * s, bw, textH * s), sf);
                    }
                }
                else
                {
                    for (int y = 0; y < h; y++)
                    {
                        int x = 0;
                        while (x < w)
                        {
                            if (!m[x0 + x, y0 + y]) { x++; continue; }
                            int run = 1;
                            while (x + run < w && m[x0 + x + run, y0 + y]) run++;
                            g.FillRectangle(Brushes.Black, (q + x) * s, (qy + y) * s, run * s, s);
                            x += run;
                        }
                    }
                }
            }
            return bmp;
        }

        // ------------------------------------------------------------------
        //  Tăng số tự động (cho "tạo mảng"): tăng cụm chữ số CUỐI CÙNG của chuỗi, giữ số 0 đứng đầu
        //  "SN-0007" +1 -> "SN-0008";  "A99" +1 -> "A100";  không có số thì giữ nguyên
        // ------------------------------------------------------------------
        public static string IncrementTrailing(string s, long add)
        {
            var m = Regex.Match(s, @"(\d+)(\D*)$");
            if (!m.Success) return s;
            string digits = m.Groups[1].Value;
            if (!decimal.TryParse(digits, out decimal v)) return s;
            v += add;
            if (v < 0) v = 0;
            string ns = v.ToString("0");
            if (ns.Length < digits.Length) ns = ns.PadLeft(digits.Length, '0');
            return s.Substring(0, m.Groups[1].Index) + ns + m.Groups[2].Value;
        }

        // EAN-13 / EAN-8 / UPC-A: nếu đã gõ đủ cả số kiểm tra thì bỏ số đó đi rồi mới tăng (thư viện tự tính lại số kiểm tra)
        public static string SerialContent(BarcodeKind kind, string content, long add)
        {
            int full = kind switch { BarcodeKind.EAN13 => 13, BarcodeKind.EAN8 => 8, BarcodeKind.UPCA => 12, _ => -1 };
            if (full > 0 && content.Length == full && content.All(char.IsDigit))
                content = content.Substring(0, full - 1);
            return IncrementTrailing(content, add);
        }
    }

    // ------------------------------------------------------------------
    //  Đối tượng mã vạch (là một VectorImage đặc biệt)
    // ------------------------------------------------------------------
    public class VectorBarcode : VectorImage
    {
        public string Content { get; private set; }
        public BarcodeKind Kind { get; private set; }
        public int Quiet { get; private set; }          // viền trống (module), -1 = mặc định theo loại mã
        public bool ShowText { get; private set; }      // hiện chữ dưới vạch (mã 1D)

        // Tạo mới từ nội dung (có thể ném ArgumentException nếu nội dung sai)
        public VectorBarcode(string content, BarcodeKind kind, int quiet = -1, bool showText = false)
            : this(BarcodeEncoder.Render(content, kind, quiet, showText), content, kind, quiet, showText) { }

        // Dựng từ ảnh có sẵn (mở file, sao chép, Undo) - không cần mã hóa lại
        public VectorBarcode(System.Drawing.Image img, string content, BarcodeKind kind, int quiet, bool showText) : base(img)
        {
            Content = content; Kind = kind; Quiet = quiet; ShowText = showText;
            Name = "Barcode";
            EngraveMode = 2;                            // đen trắng: khắc phần đen của mã
            float w = Math.Min(img.Width, 200);
            Width = w;
            Height = w * img.Height / Math.Max(1, img.Width);
        }

        // Đổi nội dung / loại mã, giữ nguyên chiều rộng (chiều cao theo tỉ lệ mới). Lỗi thì không đổi gì.
        public void Rebuild(string content, BarcodeKind kind, int quiet, bool showText)
        {
            var bmp = BarcodeEncoder.Render(content, kind, quiet, showText);
            LoadedImage = bmp;                          // ảnh cũ để GC dọn (có thể đang được bản Undo dùng chung)
            Content = content; Kind = kind; Quiet = quiet; ShowText = showText;
            Height = Width * bmp.Height / Math.Max(1, bmp.Width);
        }

        // Vẽ không làm mờ: mỗi module giữ cạnh sắc nét dù phóng to / thu nhỏ
        public override void Draw(Graphics g)
        {
            var oi = g.InterpolationMode;
            var op = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try { base.Draw(g); }
            finally { g.InterpolationMode = oi; g.PixelOffsetMode = op; }
        }
    }

    // PictureBox phóng to không làm mờ (xem trước mã)
    public class NearestPictureBox : PictureBox
    {
        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            base.OnPaint(pe);
        }
    }

    // ------------------------------------------------------------------
    //  Hộp thoại tạo / sửa mã vạch (có xem trước)
    // ------------------------------------------------------------------
    public class BarcodeForm : Form
    {
        public string ResultContent = "";
        public BarcodeKind ResultKind = BarcodeKind.QR;
        public int ResultQuiet = -1;
        public bool ResultShowText;

        private readonly ComboBox cmbType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox txtContent = new() { Multiline = true, AcceptsReturn = false, ScrollBars = ScrollBars.Vertical };
        private readonly NumericUpDown numQuiet = new() { Minimum = 0, Maximum = 20 };
        private readonly CheckBox chkText = new() { Text = "Hiện chữ dưới mã", AutoSize = true };
        private readonly Label lblHint = new() { ForeColor = Color.DimGray };
        private readonly Label lblErr = new() { ForeColor = Color.Firebrick };
        private readonly NearestPictureBox preview = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        private readonly Button btnOk = new() { Text = "OK", DialogResult = DialogResult.OK };
        private bool loading = true;

        public BarcodeForm(float dpi, string? content, BarcodeKind kind, int quiet, bool showText)
        {
            int S(int v) => (int)Math.Round(v * dpi / 96f);
            bool editing = content != null;
            Text = editing ? "Sửa mã vạch / QR" : "Tạo mã vạch / QR";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblHint.Font = new Font("Segoe UI", 8f, FontStyle.Bold);   // gợi ý: đậm, cỡ nhỏ hơn một chút để vừa 1 dòng
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(460), S(486));

            var l1 = new Label { Text = "Loại mã", Bounds = new Rectangle(S(14), S(12), S(200), S(20)) };
            cmbType.Bounds = new Rectangle(S(14), S(34), S(432), S(26));
            foreach (var k in BarcodeEncoder.Kinds) cmbType.Items.Add(k.Name);
            lblHint.Bounds = new Rectangle(S(14), S(64), S(432), S(20));

            var l2 = new Label { Text = "Nội dung", Bounds = new Rectangle(S(14), S(90), S(200), S(20)) };
            txtContent.Bounds = new Rectangle(S(14), S(112), S(432), S(62));

            var l3 = new Label { Text = "Viền trống (module)", Bounds = new Rectangle(S(14), S(188), S(150), S(22)) };
            numQuiet.Bounds = new Rectangle(S(170), S(184), S(70), S(26));
            chkText.Location = new Point(S(262), S(186));

            preview.Bounds = new Rectangle(S(14), S(220), S(432), S(190));
            lblErr.Bounds = new Rectangle(S(14), S(414), S(432), S(36));

            btnOk.Bounds = new Rectangle(S(290), S(452), S(75), S(28));
            var btnCancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(S(371), S(452), S(75), S(28)) };

            Controls.AddRange(new Control[] { l1, cmbType, lblHint, l2, txtContent, l3, numQuiet, chkText, preview, lblErr, btnOk, btnCancel });
            AcceptButton = btnOk; CancelButton = btnCancel;

            cmbType.SelectedIndex = Math.Max(0, Array.FindIndex(BarcodeEncoder.Kinds, k => k.Kind == kind));
            txtContent.Text = content ?? "";
            numQuiet.Value = quiet >= 0 ? Math.Min(20, quiet) : BarcodeEncoder.DefaultQuiet(kind);
            chkText.Checked = showText;
            loading = false;

            cmbType.SelectedIndexChanged += (s, e) =>
            {
                var k = CurrentKind();
                if (!loading) numQuiet.Value = BarcodeEncoder.DefaultQuiet(k);
                UpdatePreview();
            };
            txtContent.TextChanged += (s, e) => UpdatePreview();
            numQuiet.ValueChanged += (s, e) => UpdatePreview();
            chkText.CheckedChanged += (s, e) => UpdatePreview();
            Shown += (s, e) => { txtContent.Focus(); txtContent.SelectAll(); };

            UpdatePreview();
        }

        private BarcodeKind CurrentKind() =>
            cmbType.SelectedIndex >= 0 ? BarcodeEncoder.Kinds[cmbType.SelectedIndex].Kind : BarcodeKind.QR;

        private void UpdatePreview()
        {
            if (loading) return;
            var k = CurrentKind();
            lblHint.Text = BarcodeEncoder.HintOf(k);
            chkText.Enabled = !BarcodeEncoder.Is2D(k);

            var old = preview.Image;
            try
            {
                preview.Image = BarcodeEncoder.Render(txtContent.Text, k, (int)numQuiet.Value, chkText.Checked && chkText.Enabled);
                lblErr.Text = "";
                btnOk.Enabled = true;
            }
            catch (Exception ex)
            {
                preview.Image = null;
                lblErr.Text = ex.Message;
                btnOk.Enabled = false;
            }
            old?.Dispose();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                ResultContent = txtContent.Text;
                ResultKind = CurrentKind();
                ResultQuiet = (int)numQuiet.Value;
                ResultShowText = chkText.Checked && chkText.Enabled;
            }
            base.OnFormClosing(e);
        }
    }
}

// =====================================================================
// PHẦN 6: Thanh nút nhanh (Căn chỉnh / Nhóm / Lật / Xoay / Thứ tự)
// =====================================================================
//  Thanh nằm ngay dưới thanh menu, toàn bộ là nút biểu tượng (rê chuột để xem tên + phím tắt):
//    Căn: trái | giữa ngang | phải | trên | giữa dọc | dưới | giữa vùng khắc | phân phối ngang | phân phối dọc
//    Nhóm / Rã nhóm | Lật ngang / Lật dọc | Xoay 90° phải / trái | Thứ tự: trên cùng / lên 1 / xuống 1 / dưới cùng
//  Gọi lại đúng các hàm của menu Sắp xếp (AlignSelected, DistributeAll, GroupSelection, FlipSelection, RotateSelection,
//  MoveInOrder) nên cùng quy tắc, cùng Hoàn tác. Nút tự xám khi không dùng được (chưa chọn gì, đang ở tab xem...).
//  Gọi InitQuickBar() ở cuối InitArrangeAndMenus() (PHẦN 3).
// =====================================================================
namespace test01_1014
{
    using System.Drawing.Drawing2D;
    using Size = System.Drawing.Size;
    using Point = System.Drawing.Point;
    using Font = System.Drawing.Font;

    public partial class Form1
    {
        private ToolStrip? quickBar;
        private System.Windows.Forms.Timer? quickTimer;
        private readonly List<(ToolStripButton Btn, Func<bool> Enabled)> quickBtns = new();

        private void InitQuickBar()
        {
            if (quickBar != null) return;
            int icon = Scale96(22), bs = Scale96(34);

            quickBar = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                CanOverflow = true,
                ImageScalingSize = new Size(icon, icon),
                BackColor = Color.FromArgb(243, 243, 243),
                Padding = new Padding(Scale96(6), Scale96(3), Scale96(6), Scale96(3)),
                Renderer = new ToolRenderer()
            };

            Func<bool> hasSel = () => !ReadOnlyView && (selectedItem != null || SelCount > 0);
            Func<bool> canEdit = () => !ReadOnlyView;

            void Add(string key, string tip, Action act, Func<bool> enabled)
            {
                var b = new ToolStripButton
                {
                    Image = MakeQuickIcon(key, icon),
                    ToolTipText = tip,
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    ImageScaling = ToolStripItemImageScaling.None,
                    AutoSize = false,
                    Size = new Size(bs, bs),
                    Margin = new Padding(1, 1, 1, 1)
                };
                b.Click += (s, e) => { if (b.Enabled) act(); };
                quickBar.Items.Add(b);
                quickBtns.Add((b, enabled));
            }
            void Sep() => quickBar.Items.Add(new ToolStripSeparator());

            Add("alL", "Căn trái", () => AlignSelected(AlignMode.Left), hasSel);
            Add("alCH", "Căn giữa theo chiều ngang", () => AlignSelected(AlignMode.CenterH), hasSel);
            Add("alR", "Căn phải", () => AlignSelected(AlignMode.Right), hasSel);
            Sep();
            Add("alT", "Căn trên", () => AlignSelected(AlignMode.Top), hasSel);
            Add("alCV", "Căn giữa theo chiều dọc", () => AlignSelected(AlignMode.CenterV), hasSel);
            Add("alB", "Căn dưới", () => AlignSelected(AlignMode.Bottom), hasSel);
            Sep();
            Add("alArea", "Căn giữa vùng khắc", () => AlignSelected(AlignMode.CenterBoth), hasSel);
            Add("distH", "Phân phối đều theo chiều ngang (cần ≥ 3 đối tượng)", () => DistributeAll(true), () => canEdit() && projectItems.Count >= 3);
            Add("distV", "Phân phối đều theo chiều dọc (cần ≥ 3 đối tượng)", () => DistributeAll(false), () => canEdit() && projectItems.Count >= 3);
            Sep();
            Add("group", "Nhóm (Ctrl+G)", GroupSelection, () => canEdit() && SelCount >= 2);
            Add("ungroup", "Rã nhóm (Ctrl+Shift+G)", UngroupSelection,
                () => canEdit() && SelectedList().Any(i => !string.IsNullOrEmpty(i.GroupId)));
            Sep();
            Add("flipH", "Lật ngang (Ctrl+Shift+H)", () => FlipSelection(true), hasSel);
            Add("flipV", "Lật dọc (Ctrl+Shift+V)", () => FlipSelection(false), hasSel);
            Add("rotR", "Xoay 90° phải (Ctrl+R)", () => RotateSelection(90), hasSel);
            Add("rotL", "Xoay 90° trái (Ctrl+Shift+R)", () => RotateSelection(-90), hasSel);
            Sep();
            Add("zTop", "Đưa lên trên cùng (Ctrl+Shift+])", () => MoveInOrder(0), hasSel);
            Add("zUp", "Đưa lên một tầng (Ctrl+])", () => MoveInOrder(1), hasSel);
            Add("zDown", "Đưa xuống một tầng (Ctrl+[)", () => MoveInOrder(2), hasSel);
            Add("zBottom", "Đưa xuống dưới cùng (Ctrl+Shift+[)", () => MoveInOrder(3), hasSel);

            // dock: thanh nút nằm ngay dưới menu (menu luôn dock trước)
            Controls.Add(quickBar);
            quickBar.SendToBack();
            menuStrip1.SendToBack();

            quickTimer = new System.Windows.Forms.Timer { Interval = 150 };
            quickTimer.Tick += (s, e) => RefreshQuickBar();
            quickTimer.Start();
            RefreshQuickBar();
        }

        private void RefreshQuickBar()
        {
            foreach (var (b, en) in quickBtns)
            {
                bool on;
                try { on = en(); } catch { on = false; }
                if (b.Enabled != on) b.Enabled = on;
            }
        }

        // ------------------------------------------------------------------
        //  Biểu tượng vẽ bằng GDI+ (nét đậm, xanh dương = phần được nhấn mạnh)
        // ------------------------------------------------------------------
        private static System.Drawing.Bitmap MakeQuickIcon(string key, int size)
        {
            var bmp = new System.Drawing.Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float m = size * 0.12f, s = size - 2 * m;
            float lw = Math.Max(1.6f, size / 13f);
            var ink = Color.FromArgb(45, 45, 45);
            var blue = Color.FromArgb(0, 120, 215);
            using var pen = new Pen(ink, lw) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var bluePen = new Pen(blue, lw) { LineJoin = LineJoin.Round };
            using var blueBrush = new SolidBrush(blue);
            using var inkBrush = new SolidBrush(ink);
            using var whiteBrush = new SolidBrush(Color.White);
            using var dash = new Pen(blue, Math.Max(1f, lw * 0.8f)) { DashStyle = DashStyle.Dash };

            // hình chữ nhật theo toạ độ 0..1 của khung vẽ
            RectangleF R(float x, float y, float w, float h) => new RectangleF(m + x * s, m + y * s, w * s, h * s);
            PointF P(float x, float y) => new PointF(m + x * s, m + y * s);

            // biểu tượng căn: vẽ bản "căn trái / giữa / phải" theo chiều ngang, bản dọc = xoay 90° (trái -> trên)
            void AlignH(string mode)
            {
                float line = mode == "L" ? 0.04f : mode == "CH" ? 0.5f : 0.96f;
                float x1, x2;                                        // vị trí bắt đầu của 2 thanh
                float w1 = 0.66f, w2 = 0.40f;
                if (mode == "L") { x1 = 0.04f + 0.10f; x2 = 0.04f + 0.10f; }
                else if (mode == "CH") { x1 = 0.5f - w1 / 2; x2 = 0.5f - w2 / 2; }
                else { x1 = 0.96f - 0.10f - w1; x2 = 0.96f - 0.10f - w2; }
                g.DrawLine(pen, P(line, 0.02f), P(line, 0.98f));
                g.FillRectangle(blueBrush, R(x1, 0.16f, w1, 0.26f));
                g.FillRectangle(inkBrush, R(x2, 0.58f, w2, 0.26f));
            }

            if (key.StartsWith("al") && key != "alArea")
            {
                string mode = key.Substring(2);
                bool vertical = mode is "T" or "CV" or "B";
                if (vertical)
                {
                    g.TranslateTransform(size, 0); g.RotateTransform(90);            // trái -> trên, giữa -> giữa, phải -> dưới
                    mode = mode == "T" ? "L" : mode == "CV" ? "CH" : "R";
                }
                AlignH(mode);
                g.ResetTransform();
            }
            else switch (key)
            {
                case "alArea":      // khung vùng khắc nét đứt + hình nhỏ ở chính giữa
                    g.DrawRectangle(dash, m, m, s, s);
                    g.FillRectangle(blueBrush, R(0.32f, 0.32f, 0.36f, 0.36f));
                    g.DrawLine(pen, P(0.5f, 0.12f), P(0.5f, 0.24f)); g.DrawLine(pen, P(0.5f, 0.76f), P(0.5f, 0.88f));
                    g.DrawLine(pen, P(0.12f, 0.5f), P(0.24f, 0.5f)); g.DrawLine(pen, P(0.76f, 0.5f), P(0.88f, 0.5f));
                    break;

                case "distH":
                case "distV":
                    if (key == "distV") { g.TranslateTransform(size, 0); g.RotateTransform(90); }
                    g.DrawLine(pen, P(0.04f, 0.04f), P(0.04f, 0.96f));
                    g.DrawLine(pen, P(0.96f, 0.04f), P(0.96f, 0.96f));
                    g.FillRectangle(blueBrush, R(0.24f, 0.22f, 0.14f, 0.56f));
                    g.FillRectangle(blueBrush, R(0.43f, 0.22f, 0.14f, 0.56f));
                    g.FillRectangle(blueBrush, R(0.62f, 0.22f, 0.14f, 0.56f));
                    g.ResetTransform();
                    break;

                case "group":       // 2 hình nằm trong 1 khung nét đứt
                    g.DrawRectangle(dash, m, m, s, s);
                    g.FillRectangle(whiteBrush, R(0.16f, 0.16f, 0.36f, 0.36f)); g.DrawRectangle(pen, R(0.16f, 0.16f, 0.36f, 0.36f).X, R(0.16f, 0.16f, 0.36f, 0.36f).Y, 0.36f * s, 0.36f * s);
                    g.FillRectangle(blueBrush, R(0.48f, 0.48f, 0.36f, 0.36f));
                    break;

                case "ungroup":     // 2 hình tách rời, khung nét đứt bị bỏ
                    g.FillRectangle(whiteBrush, R(0.02f, 0.02f, 0.40f, 0.40f)); g.DrawRectangle(pen, R(0.02f, 0.02f, 0.40f, 0.40f).X, R(0.02f, 0.02f, 0.40f, 0.40f).Y, 0.40f * s, 0.40f * s);
                    g.FillRectangle(blueBrush, R(0.58f, 0.58f, 0.40f, 0.40f));
                    break;

                case "flipH":
                case "flipV":
                    if (key == "flipV") { g.TranslateTransform(size, 0); g.RotateTransform(90); }
                    g.DrawLine(dash, P(0.5f, 0.0f), P(0.5f, 1.0f));
                    g.FillPolygon(blueBrush, new[] { P(0.40f, 0.14f), P(0.40f, 0.86f), P(0.04f, 0.86f) });
                    g.DrawPolygon(pen, new[] { P(0.60f, 0.14f), P(0.60f, 0.86f), P(0.96f, 0.86f) });
                    g.ResetTransform();
                    break;

                case "rotR":
                case "rotL":
                    {
                        if (key == "rotL") { g.TranslateTransform(size, 0); g.ScaleTransform(-1, 1); }
                        var rc = R(0.10f, 0.10f, 0.80f, 0.80f);
                        float start = -70f, sweep = 270f;
                        g.DrawArc(pen, rc, start, sweep);
                        double th = (start + sweep) * Math.PI / 180.0;
                        float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f, r = rc.Width / 2f;
                        var tip = new PointF(cx + r * (float)Math.Cos(th), cy + r * (float)Math.Sin(th));
                        var tan = new PointF(-(float)Math.Sin(th), (float)Math.Cos(th));       // hướng đi (chiều kim đồng hồ)
                        var nor = new PointF((float)Math.Cos(th), (float)Math.Sin(th));
                        float a = s * 0.20f;
                        g.FillPolygon(inkBrush, new[]
                        {
                            new PointF(tip.X + tan.X * a, tip.Y + tan.Y * a),
                            new PointF(tip.X + nor.X * a * 0.8f, tip.Y + nor.Y * a * 0.8f),
                            new PointF(tip.X - nor.X * a * 0.8f, tip.Y - nor.Y * a * 0.8f)
                        });
                        g.FillRectangle(blueBrush, R(0.38f, 0.38f, 0.24f, 0.24f));
                        g.ResetTransform();
                        break;
                    }

                case "zTop":
                case "zUp":
                case "zDown":
                case "zBottom":
                    {
                        bool front = key is "zTop" or "zUp";
                        var back = R(0.04f, 0.04f, 0.52f, 0.52f);
                        var fore = R(0.40f, 0.40f, 0.56f, 0.56f);
                        // hình nằm dưới vẽ trước, hình nằm trên vẽ sau; hình được nhấn mạnh (xanh) là hình sẽ đổi chỗ
                        g.FillRectangle(front ? whiteBrush : blueBrush, back); g.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
                        g.FillRectangle(front ? blueBrush : whiteBrush, fore); g.DrawRectangle(pen, fore.X, fore.Y, fore.Width, fore.Height);
                        if (key is "zUp" or "zDown")
                        {
                            bool up = key == "zUp";
                            var tri = up
                                ? new[] { P(0.80f, 0.04f), P(0.94f, 0.26f), P(0.66f, 0.26f) }
                                : new[] { P(0.20f, 0.96f), P(0.34f, 0.74f), P(0.06f, 0.74f) };
                            g.FillPolygon(inkBrush, tri);
                        }
                        break;
                    }
            }
            return bmp;
        }
    }
}
