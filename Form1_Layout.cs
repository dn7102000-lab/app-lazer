// =====================================================================
//  Form1_Layout.cs  -  GIAO DIỆN: bố cục, thanh trạng thái, bảng thông số, cài đặt ứng dụng
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Bố cục, thanh công cụ, tab xem, xem trước đường tô   (trước đây: Form1_Layout.cs)
//    2. Thanh trạng thái   (trước đây: Form1_Status.cs)
//    3. Bảng thông số đối tượng (có ô Tầng)   (trước đây: Form1.Palette.cs)
//    4. Cài đặt ứng dụng (ngôn ngữ, giao diện sáng/tối)   (trước đây: Form1_Settings.cs)
// =====================================================================

// =====================================================================
// PHẦN: Bố cục, thanh công cụ, tab xem, xem trước đường tô   (trước đây là file Form1_Layout.cs)
// =====================================================================
// =====================================================================
//  Form1_Layout.cs  -  BỐ CỤC: thanh công cụ, cột phải, tab xem, xem trước đường tô
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Bố cục cửa sổ, thanh công cụ trái, cột phải   (trước đây: Form1_Tools.cs)
//    2. Thanh tab đáy (Bản vẽ / Đã xử lý / Đường laser)   (trước đây: ViewTabBar.cs)
//    3. Xem trước đường tô kín (Fill / Hatch)   (trước đây: Form1_FillPreview.cs)
// =====================================================================
// =====================================================================
// PHẦN 1: Bố cục cửa sổ, thanh công cụ trái, cột phải   (trước đây là file Form1_Tools.cs)
// =====================================================================
// =====================================================================
//  Form1_Tools.cs  -  Thanh công cụ dọc bên trái + bố cục cột phải
//
//  1) THANH CÔNG CỤ TRÁI: chỉ là các nút bấm cho công cụ vẽ có sẵn trong Form1_drawtool.cs
//     (BeginDrawTool / CancelTool / curTool). Vẽ kéo thả, snap, bảng nhập số vẫn do file đó lo.
//       - Nút ứng với công cụ đang dùng (curTool) được tô sáng xanh, tự đổi theo khi chọn
//         công cụ từ menu / chuột phải (đồng bộ mỗi 150 ms)
//       - Chọn = hủy công cụ vẽ, quay về chọn / kéo hình
//       - Đa giác: bấm vào icon = vẽ loại đang chọn, bấm mũi tên = đổi loại
//       - Ảnh: mở hộp chọn ảnh rồi đặt vào giữa vùng đang nhìn
//       - Phím tắt (khi canvas đang giữ focus): V Chọn, L Đường, R Chữ nhật, E Elip, P Đa giác, T Chữ, I Ảnh
//         (Esc = hủy công cụ: đã có sẵn trong Form1_drawtool.cs)
//
//  2) CỘT PHẢI: chuyển bảng thông số PropertyPalette (Form1_Palette.cs) từ cột trái sang bên phải,
//     bỏ cột trái cũ và vùng trống bên phải canvas. Hai thanh trượt Canny / Blur (xem trước ảnh)
//     nằm ở đáy cột phải (kèm ô Đảo màu / âm bản), chỉ hiện khi đang ở tab "Edited".
//
//  Gọi InitWorkspaceLayout() 1 lần trong OnHandleCreated (Form1_Cad.cs), ngay sau InitPalette().
// =====================================================================
namespace test01_1014
{
    using OpenCvSharp.Dnn;
    using System.Drawing.Drawing2D;
    using test01_1014;
    using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;

    public partial class Form1
    {
        private ToolStrip toolBox = null!;
        private ToolStripButton? undoButton, redoButton;
        private ToolStripSplitButton? polyButton;
        private readonly Dictionary<DrawTool, ToolStripItem> toolButtons = new();
        private PolygonKind polyPick = PolygonKind.Pentagon;
        private DrawTool lastHighlight = (DrawTool)(-1);
        private PolygonKind lastPolyIcon = (PolygonKind)(-1);

        private Panel rightHost = null!;
        private SplitContainer rightSplit = null!;
        private Panel? secPreview;                       // 2 thanh trượt Canny / Blur
        private System.Windows.Forms.Timer? toolTimer;

        private int Scale96(int v) => (int)Math.Round(v * DeviceDpi / 96f);

        // ---- độ rộng bảng thông số & độ cao bảng lớp: kéo được và được nhớ lại giữa các lần mở ----
        private Splitter rightSplitter = null!;
        private static string RightWidthPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "panelwidth.txt");
        private static string LayerHeightPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "layerheight.txt");

        private int LoadRightWidth()
        {
            try
            {
                if (!System.IO.File.Exists(RightWidthPath)) return 0;
                return int.TryParse(System.IO.File.ReadAllText(RightWidthPath).Trim(), out int v) ? v : 0;
            }
            catch { return 0; }
        }

        private void SaveRightWidth()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RightWidthPath)!);
                System.IO.File.WriteAllText(RightWidthPath, rightHost.Width.ToString());
            }
            catch { }
        }

        private int LoadLayerHeight()
        {
            try
            {
                if (!System.IO.File.Exists(LayerHeightPath)) return 0;
                return int.TryParse(System.IO.File.ReadAllText(LayerHeightPath).Trim(), out int v) ? v : 0;
            }
            catch { return 0; }
        }

        private void SaveLayerHeight()
        {
            try
            {
                if (rightSplit == null || !layersExpanded) return;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LayerHeightPath)!);
                System.IO.File.WriteAllText(LayerHeightPath, rightSplit.SplitterDistance.ToString());
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Bố cục: thanh công cụ trái | canvas | bảng thông số phải
        // ------------------------------------------------------------------
        private void InitWorkspaceLayout()
        {
            if (toolBox != null) return;                  // chỉ chạy 1 lần
            if (propPalette == null) InitPalette();

            InitToolbox();                                // tự Controls.Add(toolBox)

            // ---- cột phải: bảng Lớp + Thông số/Đối tượng (kéo chia tỉ lệ được) + thanh trượt xem trước (Bottom) ----
            rightHost = new Panel { Dock = DockStyle.Right, Width = Scale96(340) };
            secPreview = BuildPreviewSection();
            rightHost.Controls.Add(secPreview);

            rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = Scale96(7),
                BackColor = Color.FromArgb(200, 206, 216),
                Panel1MinSize = Scale96(26),
                Panel2MinSize = Scale96(100),
                FixedPanel = FixedPanel.Panel1
            };
            rightSplit.SplitterMoved += (s, e) => SaveLayerHeight();

            if (propPalette != null)
            {
                splitContainer1.Panel1.Controls.Remove(propPalette);
                propPalette.Dock = DockStyle.Fill;
                rightSplit.Panel2.Controls.Add(propPalette);
            }

            rightHost.Controls.Add(rightSplit);
            rightSplit.BringToFront();

            Controls.Add(rightHost);

            // Thanh kéo giữa canvas và bảng thông số: kéo chuột trái/phải để đổi độ rộng bảng
            rightSplitter = new Splitter
            {
                Dock = DockStyle.Right,
                Width = Scale96(7),
                MinSize = Scale96(260),            // bảng không hẹp hơn mức này
                MinExtra = Scale96(300),           // canvas luôn còn ít nhất chừng này
                BackColor = Color.FromArgb(214, 218, 224),
                Cursor = Cursors.VSplit
            };
            rightSplitter.MouseEnter += (s, e) => rightSplitter.BackColor = Color.FromArgb(0, 120, 215);
            rightSplitter.MouseLeave += (s, e) => rightSplitter.BackColor = Color.FromArgb(214, 218, 224);
            rightSplitter.SplitterMoved += (s, e) => SaveRightWidth();
            Controls.Add(rightSplitter);
            rightHost.SendToBack();                // rightHost dock trước -> thanh kéo nằm sát bên trái nó

            int savedW = LoadRightWidth();
            if (savedW > 0) rightHost.Width = Math.Clamp(savedW, Scale96(260), Math.Max(Scale96(260), ClientSize.Width - Scale96(300)));

            // Thứ tự dock: menu trước -> 2 thanh bên -> canvas (Fill) sau cùng
            menuStrip1.SendToBack();
            splitContainer1.BringToFront();

            splitContainer1.Panel1Collapsed = true;       // bỏ cột trái cũ (palette đã chuyển đi)
            splitContainer2.Panel2Collapsed = true;       // bỏ vùng trống bên phải canvas

            if (activeTab is { Mode: ViewMode.Edited }) secPreview.Visible = true;

            // ---- Dọn menu cho gọn (các mục trùng với thanh công cụ / bảng phải) ----
            menuStrip1.Items.Remove(exportGCodeToolStripMenuItem);                 // bỏ khỏi thanh trên...
            exportGCodeToolStripMenuItem.Text = "Xuất G-code / Mô phỏng...";
            exportGCodeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.E;
            fiieToolStripMenuItem.DropDownItems.Insert(                            // ...đưa vào Tệp, trước dòng kẻ + Thoát
                fiieToolStripMenuItem.DropDownItems.Count - 2, exportGCodeToolStripMenuItem);

            shapeToolStripMenuItem.Visible = false;                                // Edit chỉ còn Undo / Redo
            textToolStripMenuItem.Visible = false;
            imageToolStripMenuItem.Visible = false;
            foreach (ToolStripItem it in toolStripMenuItem1.DropDownItems)         // dòng kẻ thừa sau Redo
                if (it is ToolStripSeparator) it.Visible = false;

            toolTimer = new System.Windows.Forms.Timer { Interval = 150 };
            toolTimer.Tick += (s, e) => SyncToolHighlight();
            toolTimer.Start();

            InitArrangeAndMenus();      // menu Sắp xếp + Việt hóa tên menu (Form1_Arrange.cs)
            InitStatusBar();            // thanh trạng thái dưới cùng (Form1_Status.cs)
            InitBarcodeAndArray();      // mã vạch + tạo mảng bản sao (Form1_Array.cs)
            InitPrintArea();            // <-- thêm: Khổ in (Form1_PrintArea.cs)
            InitLayers();               // bảng LỚP (layer) theo màu (Form1_Layers.cs)
            InitSettings();             // menu Tệp > Cài đặt... (ngôn ngữ, giao diện sáng/tối) (Form1_Settings.cs)
        }

        // 2 thanh trượt cũ (Canny / Blur) chuyển từ cột trái sang đáy cột phải
        private Panel BuildPreviewSection()
        {
            var p = new Panel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false,
                BackColor = Color.White
            };
            var hdr = new Label
            {
                Text = "XỬ LÝ ẢNH (XEM TRƯỚC)",
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(222, 229, 240),
                ForeColor = Color.FromArgb(40, 60, 100)
            };
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(8, 4, 8, 8)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (Control c in new Control[] { Canny, trackBarThreshold, Blur, trackBarBlur })
            {
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(3);
                t.Controls.Add(c);
            }
            chkTabInvert.Dock = DockStyle.Fill;                 // ô Đảo màu (âm bản) nằm dưới 2 thanh trượt
            chkTabInvert.Margin = new Padding(3, 8, 3, 3);
            t.Controls.Add(chkTabInvert);
            p.Controls.Add(t);      // thêm t trước, hdr sau -> hdr được dock trước (nằm trên cùng)
            p.Controls.Add(hdr);
            return p;
        }

        // ------------------------------------------------------------------
        //  Dựng thanh công cụ
        // ------------------------------------------------------------------
        private void InitToolbox()
        {
            int icon = IconSize;
            int cw = icon + Scale96(26), ch = icon + Scale96(14);

            toolBox = new ToolStrip
            {
                Dock = DockStyle.Left,
                LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
                GripStyle = ToolStripGripStyle.Hidden,
                CanOverflow = false,
                ImageScalingSize = new Size(icon, icon),
                BackColor = Color.FromArgb(243, 243, 243),
                Padding = new Padding(Scale96(4), Scale96(8), Scale96(4), Scale96(4)),
                Renderer = new ToolRenderer()
            };

            ToolStripButton NewBtn(string tip, System.Drawing.Image img, Action onClick)
            {
                var b = new ToolStripButton
                {
                    Image = img,
                    ToolTipText = tip,
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    ImageScaling = ToolStripItemImageScaling.None,
                    AutoSize = false,
                    Size = new Size(cw, ch),
                    Margin = new Padding(0, 2, 0, 2)
                };
                b.Click += (s, e) => onClick();
                toolBox.Items.Add(b);
                return b;
            }

            undoButton = NewBtn("Hoàn tác (Ctrl+Z)", MakeToolIcon("undo", icon), Undo);
            redoButton = NewBtn("Làm lại (Ctrl+Y)", MakeToolIcon("redo", icon), Redo);
            toolBox.Items.Add(new ToolStripSeparator());

            toolButtons[DrawTool.None] = NewBtn("Chọn / di chuyển (V)", MakeToolIcon("select", icon), PickSelect);
            nodeEditBtn = NewBtn("Sửa điểm nút / đường dẫn (N)", MakeToolIcon("node", icon), ToggleNodeEditMode);
            toolBox.Items.Add(new ToolStripSeparator());
            toolButtons[DrawTool.Line] = NewBtn("Đường thẳng (L)", MakeShapeIcon("line", icon), () => PickTool(DrawTool.Line));
            toolButtons[DrawTool.Rectangle] = NewBtn("Hình chữ nhật (R)", MakeShapeIcon("rect", icon), () => PickTool(DrawTool.Rectangle));
            toolButtons[DrawTool.Ellipse] = NewBtn("Hình tròn / elip (E)", MakeShapeIcon("circle", icon), () => PickTool(DrawTool.Ellipse));

            polyButton = new ToolStripSplitButton
            {
                Image = MakeShapeIcon(polyPick.ToString(), icon),
                ToolTipText = "Đa giác (P)",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None,
                AutoSize = false,
                Size = new Size(cw, ch),
                Margin = new Padding(0, 2, 0, 2)
            };
            polyButton.ButtonClick += (s, e) => PickTool(DrawTool.Polygon);
            foreach (var (name, kind) in PolygonList)
            {
                var mi = IconItem(name, name);
                var k = kind;
                mi.Click += (s, e) => { polyPick = k; PickTool(DrawTool.Polygon); };
                polyButton.DropDownItems.Add(mi);
            }
            toolButtons[DrawTool.Polygon] = polyButton;
            toolBox.Items.Add(polyButton);

            toolBox.Items.Add(new ToolStripSeparator());
            toolButtons[DrawTool.Text] = NewBtn("Chữ (T)", MakeToolIcon("text", icon), () => PickTool(DrawTool.Text));

            // Ảnh không phải "công cụ kéo thả": bấm là mở hộp chọn ảnh ngay (không tô sáng)
            NewBtn("Chèn ảnh (I)", MakeToolIcon("image", icon), PickImage);
            NewBtn("Tạo mã vạch / QR (B)", MakeToolIcon("barcode", icon), () => PickBarcode(null));
            toolBox.Items.Add(new ToolStripSeparator());
            NewBtn("Hàn liền đường cắt / chữ (W)", MakeToolIcon("weld", icon), WeldSelectedItems);
            NewBtn("Tạo mảng bản sao (Ctrl+Shift+A)", MakeToolIcon("array", icon), CreateArray);

            Controls.Add(toolBox);
            SyncToolHighlight();
        }

        // ------------------------------------------------------------------
        //  Hành động của các nút (chuyển cho Form1_drawtool.cs)
        // ------------------------------------------------------------------
        private void PickSelect()
        {
            if (ReadOnlyView) SetActiveTab(null);
            if (nodeEditMode) SetNodeEditMode(false);
            CancelTool();
            SyncToolHighlight();
            pictureBox1.Focus();
        }

        private void PickTool(DrawTool t)
        {
            if (nodeEditMode) SetNodeEditMode(false);
            BeginDrawTool(t, polyPick);       // tự thoát tab xem, hủy công cụ cũ, hiện bảng nhập số
            SyncToolHighlight();
        }

        private void PickImage()
        {
            if (ReadOnlyView) SetActiveTab(null);
            CancelTool();
            AddImageAt(ViewCenter());
            SyncToolHighlight();
        }

        // Tô sáng nút ứng với curTool (gọi định kỳ từ toolTimer và sau mỗi lần bấm nút)
        private void SyncToolHighlight()
        {
            if (toolBox == null) return;
            if (undoButton != null) undoButton.Enabled = !ReadOnlyView && undoStack.Count > 0;
            if (redoButton != null) redoButton.Enabled = !ReadOnlyView && redoStack.Count > 0;
            DrawTool now = curTool;

            // đang vẽ đa giác: icon của nút đa giác đổi theo loại đang dùng
            if (now == DrawTool.Polygon && polyButton != null && curPoly != lastPolyIcon)
            {
                polyPick = curPoly;
                polyButton.Image = MakeShapeIcon(curPoly.ToString(), IconSize);
                lastPolyIcon = curPoly;
                toolBox.Invalidate();
            }

            if (now == lastHighlight && (nodeEditBtn == null || (nodeEditBtn.Tag != null) == nodeEditMode)) return;
            lastHighlight = now;
            foreach (var kv in toolButtons)
                kv.Value.Tag = (!nodeEditMode && kv.Key == now) ? "active" : null;     // ToolRenderer đọc Tag để tô sáng
            if (nodeEditBtn != null)
                nodeEditBtn.Tag = nodeEditMode ? "active" : null;
            toolBox.Invalidate();
        }

        // Phím tắt công cụ (chỉ khi canvas đang giữ focus và không đang gõ chữ)
        private bool TryToolHotkey(Keys key)
        {
            if (toolBox == null || IsTextEditing() || !pictureBox1.Focused) return false;
            switch (key)
            {
                case Keys.V: PickSelect(); return true;
                case Keys.N: ToggleNodeEditMode(); return true;
                case Keys.S when nodeEditMode: SmoothSelectedNode(); return true;
                case Keys.W: WeldSelectedItems(); return true;
                case Keys.L: PickTool(DrawTool.Line); return true;
                case Keys.R: PickTool(DrawTool.Rectangle); return true;
                case Keys.E: PickTool(DrawTool.Ellipse); return true;
                case Keys.P: PickTool(DrawTool.Polygon); return true;
                case Keys.T: PickTool(DrawTool.Text); return true;
                case Keys.I: PickImage(); return true;
                case Keys.B: PickBarcode(null); return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  Icon cho Chọn / Chữ / Ảnh (Đường / Chữ nhật / Tròn / Đa giác dùng MakeShapeIcon có sẵn)
        // ------------------------------------------------------------------
        private static System.Drawing.Bitmap MakeToolIcon(string key, int size)
        {
            var bmp = new System.Drawing.Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float m = size * 0.15f, s = size - 2 * m;
            using var pen = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1.5f, size / 14f)) { LineJoin = LineJoin.Round };

            switch (key)
            {
                case "select":      // mũi tên con trỏ
                    var arrow = new[]
                    {
                        new PointF(m + s * 0.12f, m), new PointF(m + s * 0.12f, m + s * 0.88f),
                        new PointF(m + s * 0.34f, m + s * 0.68f), new PointF(m + s * 0.52f, m + s),
                        new PointF(m + s * 0.67f, m + s * 0.92f), new PointF(m + s * 0.49f, m + s * 0.62f),
                        new PointF(m + s * 0.78f, m + s * 0.60f)
                    };
                    g.FillPolygon(Brushes.White, arrow);
                    g.DrawPolygon(pen, arrow);
                    break;

                case "node":        // đường cong kèm 3 điểm nút vuông (Node Edit)
                    {
                        var p1 = new PointF(m + s * 0.12f, m + s * 0.82f);
                        var p2 = new PointF(m + s * 0.48f, m + s * 0.18f);
                        var p3 = new PointF(m + s * 0.88f, m + s * 0.68f);
                        g.DrawLines(pen, new[] { p1, p2, p3 });
                        float nr = Math.Max(2.2f, s * 0.12f);
                        foreach (var np in new[] { p1, p2, p3 })
                        {
                            g.FillRectangle(Brushes.White, np.X - nr, np.Y - nr, nr * 2, nr * 2);
                            g.DrawRectangle(pen, np.X - nr, np.Y - nr, nr * 2, nr * 2);
                        }
                    }
                    break;

                case "weld":        // 2 vòng tròn giao nhau hàn liền
                    g.DrawEllipse(pen, m + s * 0.04f, m + s * 0.18f, s * 0.58f, s * 0.58f);
                    g.DrawEllipse(pen, m + s * 0.38f, m + s * 0.18f, s * 0.58f, s * 0.58f);
                    break;

                case "undo":
                case "redo":      // mũi tên cong quay lại / đi tới
                    if (key == "redo") { g.TranslateTransform(size, 0); g.ScaleTransform(-1, 1); }
                    g.DrawArc(pen, m, m + s * 0.25f, s, s * 0.8f, 200, 250);
                    g.FillPolygon(new SolidBrush(pen.Color), new[]
                    {
                        new PointF(m + s * 0.02f, m + s * 0.05f), new PointF(m + s * 0.02f, m + s * 0.55f),
                        new PointF(m + s * 0.45f, m + s * 0.30f)
                    });
                    break;

                case "text":        // chữ T
                    using (var f = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var br = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("T", f, br, new RectangleF(0, 0, size, size), sf);
                    break;

                case "barcode":     // các vạch dọc to nhỏ
                    {
                        float[] bx = { 0.00f, 0.10f, 0.22f, 0.30f, 0.44f, 0.54f, 0.68f, 0.78f, 0.90f };
                        float[] bwid = { 0.06f, 0.08f, 0.04f, 0.10f, 0.06f, 0.10f, 0.05f, 0.08f, 0.10f };
                        for (int i = 0; i < bx.Length; i++)
                            g.FillRectangle(Brushes.Black, m + s * bx[i], m + s * 0.05f, Math.Max(1.2f, s * bwid[i]), s * 0.9f);
                    }
                    break;

                case "array":       // lưới 3x3 ô vuông nhỏ
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            var cell = new RectangleF(m + s * (0.02f + j * 0.35f), m + s * (0.02f + i * 0.35f), s * 0.26f, s * 0.26f);
                            if (i == 0 && j == 0) g.FillRectangle(Brushes.Black, cell);
                            else g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
                        }
                    break;

                case "image":       // khung ảnh + núi + mặt trời
                    g.DrawRectangle(pen, m, m + s * 0.1f, s, s * 0.8f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(m + s * 0.05f, m + s * 0.82f), new PointF(m + s * 0.38f, m + s * 0.45f),
                        new PointF(m + s * 0.58f, m + s * 0.68f), new PointF(m + s * 0.72f, m + s * 0.55f),
                        new PointF(m + s * 0.95f, m + s * 0.82f)
                    });
                    g.DrawEllipse(pen, m + s * 0.68f, m + s * 0.2f, s * 0.15f, s * 0.15f);
                    break;
            }
            return bmp;
        }

        private static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            int d = Math.Max(2, rad * 2);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------
        //  Vẽ nút: đang dùng = nền xanh nhạt + viền xanh, rê chuột = nền xám
        // ------------------------------------------------------------------
        private sealed class ToolRenderer : ToolStripProfessionalRenderer
        {
            public ToolRenderer() { RoundedEdges = false; }

            private static void PaintButton(Graphics g, ToolStripItem it)
            {
                bool active = it.Tag is string s && s == "active";
                bool hot = it.Selected || it.Pressed;
                if (!active && !hot) return;

                var r = new Rectangle(1, 1, it.Width - 3, it.Height - 3);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundRect(r, 6);
                using (var fill = new SolidBrush(active ? Color.FromArgb(205, 228, 252) : Color.FromArgb(226, 226, 226)))
                    g.FillPath(fill, path);
                if (active)
                    using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1.6f))
                        g.DrawPath(pen, path);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) =>
                PaintButton(e.Graphics, e.Item);

            protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
            {
                PaintButton(e.Graphics, e.Item);
                if (e.Item is ToolStripSplitButton sb)
                    OnRenderArrow(new ToolStripArrowRenderEventArgs(e.Graphics, sb, sb.DropDownButtonBounds,
                                  Color.FromArgb(90, 90, 90), ArrowDirection.Down));
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using var b = new SolidBrush(e.ToolStrip.BackColor);
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var p = new Pen(Color.FromArgb(215, 215, 215));
                e.Graphics.DrawLine(p, e.ToolStrip.Width - 1, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height);
            }
        }
    }
}
namespace test01_1014
{
    using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    // Thanh tab vẽ bằng code: tab 0 = "Edit" (không xóa được), các tab sau có nút ×
    public class ViewTabBar : Control
    {
        private List<string> titles = new() { "Edit" };
        private int selected;
        private int hoverClose = -1;
        private readonly List<Rectangle> tabRects = new();
        private readonly List<Rectangle> closeRects = new();

        public bool AllowClose { get; set; } = true;      // false = tab cố định, không có nút ×
        public event Action<int>? TabSelected;
        public event Action<int>? TabClosed;

        public ViewTabBar()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Dock = DockStyle.Bottom;
            Height = 30;
            BackColor = Color.FromArgb(232, 232, 232);
            Font = new Font("Segoe UI", 9f);
        }

        public void SetTabs(IEnumerable<string> newTitles, int newSelected)
        {
            titles = newTitles.ToList();
            selected = newSelected;
            hoverClose = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            tabRects.Clear(); closeRects.Clear();

            int pad = 12, x = 4, cs = Math.Max(8, (int)(Height * 0.28f));
            for (int i = 0; i < titles.Count; i++)
            {
                bool closable = AllowClose && i > 0, sel = i == selected;
                var tw = TextRenderer.MeasureText(g, titles[i], Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
                int w = pad + tw.Width + (closable ? 8 + cs : 0) + pad;
                var r = new Rectangle(x, 0, w, Height);
                tabRects.Add(r);

                using (var bg = new SolidBrush(sel ? Color.White : Color.FromArgb(214, 214, 214)))
                    g.FillRectangle(bg, r);
                if (sel)
                    using (var acc = new SolidBrush(Color.FromArgb(0, 120, 215)))
                        g.FillRectangle(acc, r.X, r.Y, r.Width, 3);

                TextRenderer.DrawText(g, titles[i], Font, new Rectangle(r.X + pad, 0, tw.Width + 2, Height),
                    sel ? Color.FromArgb(30, 30, 30) : Color.FromArgb(90, 90, 90),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                if (closable)
                {
                    var c = new Rectangle(r.Right - pad - cs, (Height - cs) / 2, cs, cs);
                    closeRects.Add(c);
                    if (hoverClose == i)
                        using (var hb = new SolidBrush(Color.FromArgb(235, 190, 190)))
                            g.FillEllipse(hb, c.X - 4, c.Y - 4, c.Width + 8, c.Height + 8);
                    using (var pen = new Pen(Color.FromArgb(70, 70, 70), 1.5f))
                    {
                        g.DrawLine(pen, c.Left, c.Top, c.Right, c.Bottom);
                        g.DrawLine(pen, c.Left, c.Bottom, c.Right, c.Top);
                    }
                }
                else closeRects.Add(Rectangle.Empty);

                x += w + 2;
            }
        }

        private int HitClose(Point p)
        {
            if (!AllowClose) return -1;
            for (int i = 1; i < closeRects.Count; i++)
            {
                var r = closeRects[i]; r.Inflate(5, 5);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitClose(e.Location);
            if (h != hoverClose) { hoverClose = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverClose = -1; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < tabRects.Count; i++)
            {
                if (!tabRects[i].Contains(e.Location)) continue;
                if (AllowClose && i > 0 && (e.Button == MouseButtons.Middle ||
                              (e.Button == MouseButtons.Left && HitClose(e.Location) == i)))
                    TabClosed?.Invoke(i);               // bấm × hoặc chuột giữa = xóa tab
                else if (e.Button == MouseButtons.Left)
                    TabSelected?.Invoke(i);
                return;
            }
        }
    }
}
namespace test01_1014
{
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using Font = System.Drawing.Font;

    public partial class Form1
    {
        private sealed class FillParts
        {
            public int Sig;
            public List<PointF[]> Fill = new();
            public List<PointF[]> Outline = new();
        }

        private readonly Dictionary<string, FillParts?> fillCache = new();
        private static readonly Color OutlineBlue = Color.FromArgb(30, 70, 200);

        private static bool CanFill(Vectoritem it) =>
            it.FillMode != 0 && it is not VectorText { SingleLineFont: true } &&
            (it is VectorRectangle || it is VectorEllipse || it is VectorPolygon || it is VectorText || it is VectorPathItem);

        private static int FillSig(Vectoritem it)
        {
            var h = new HashCode();
            h.Add(it.X); h.Add(it.Y); h.Add(it.Width); h.Add(it.Height);
            h.Add(it.Rotation); h.Add(it.FlipX); h.Add(it.FlipY); h.Add(it.LaserPower);
            h.Add(it.FillMode); h.Add(it.FillIntervalMm); h.Add(it.FillAngle); h.Add(it.FillCross); h.Add(it.FillBidir);
            if (it is VectorText t)
            {
                h.Add(t.TextContent); h.Add(t.FontName); h.Add(t.FontSize); h.Add(t.FontBold); h.Add(t.FontItalic);
                h.Add(t.SingleLineFont); h.Add(t.WeldText); h.Add(t.ArcRadiusMm); h.Add(t.CharSpacingMm);
            }
            if (it is VectorPolygon p) h.Add((int)p.Kind);
            if (it is VectorPathItem pi) h.Add(pi.SubPaths.Count);
            return h.ToHashCode();
        }

        private FillParts? FillPartsFor(Vectoritem it)
        {
            if (!CanFill(it)) return null;
            int sig = FillSig(it);
            if (fillCache.TryGetValue(it.Id, out var c) && c != null && c.Sig == sig) return c;
            if (c == null && fillCache.ContainsKey(it.Id) && fillCache_sig.TryGetValue(it.Id, out var s0) && s0 == sig) return null;

            if (fillCache.Count > 300) { fillCache.Clear(); fillCache_sig.Clear(); }
            FillParts? res = null;
            try
            {
                var cfg = new GCodeSettings { MmPerPixel = MmPerPx, MaxS = Math.Max(1, gcodeCfg.MaxS), CurveToleranceMm = gcodeCfg.CurveToleranceMm };
                var parts = new GCodeCompiler(cfg).GetFillParts(it);
                if (parts != null)
                {
                    res = new FillParts { Sig = sig };
                    res.Fill = parts.Value.Fill.Where(p => p.Count >= 2).Select(p => p.ToArray()).ToList();
                    res.Outline = parts.Value.Outline.Where(p => p.Count >= 2).Select(p => p.ToArray()).ToList();
                }
            }
            catch { res = null; }
            fillCache[it.Id] = res;
            fillCache_sig[it.Id] = sig;
            return res;
        }
        private readonly Dictionary<string, int> fillCache_sig = new();

        // Vẽ các đường tô (không xoay thêm vì đã xoay sẵn). Thưa bớt khi hiển thị nếu quá dày. false = không có gì để vẽ
        private bool DrawFillLines(Graphics g, Vectoritem it, Color color, int alpha, float widthPx)
        {
            var fp = FillPartsFor(it);
            if (fp == null || (fp.Fill.Count == 0 && fp.Outline.Count == 0)) return false;
            if (fp.Fill.Count > 0)
            {
                float stepPx = (float)Math.Max(0.01, it.FillIntervalMm / MmPerPx * zoom);      // khoảng cách 2 đường tô trên màn hình
                int k = Math.Max(1, (int)Math.Ceiling(4f / stepPx));
                if (it.FillCross) k = Math.Max(1, k);                                   // tô chéo: mỗi hướng thưa như nhau
                using var pen = new Pen(Color.FromArgb(alpha, color), Math.Max(0.5f, widthPx) / zoom);
                for (int i = 0; i < fp.Fill.Count; i += k)
                {
                    if (fp.Fill.Count / k > 30000) break;
                    g.DrawLines(pen, fp.Fill[i]);
                }
            }
            return true;
        }

        // Tab Đường laser: đỏ = đường tô; xanh dày hơn = đường viền (chỉ "Tô kín + viền"). false -> vẽ nét viền như thường
        private bool DrawLaserFill(Graphics g, Vectoritem it)
        {
            if (!DrawFillLines(g, it, Color.Crimson, 210, 1f)) return false;
            var fp = FillPartsFor(it);
            if (fp != null && fp.Outline.Count > 0)
            {
                using var pen = new Pen(OutlineBlue, 2.2f / zoom) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                foreach (var p in fp.Outline) g.DrawLines(pen, p);
            }
            return true;
        }

        // Tab Bản vẽ / Đã xử lý và canvas chính. Trả về true nếu hình CHỈ cần hiện đường tô (Tô kín, chưa chọn) -> bỏ DrawItem
        private bool DrawFillPreview(Graphics g, Vectoritem it)
        {
            if (it.FillMode == 0) return false;
            if (!DrawFillLines(g, it, LayerColors.Of(it.LayerId), 120, 1f)) return false;
            return it.FillMode == 1 && !it.IsSelected;
        }
    }
}

// =====================================================================
// PHẦN: Thanh trạng thái   (trước đây là file Form1_Status.cs)
// =====================================================================
// =====================================================================
//  Form1_Status.cs  -  THANH TRẠNG THÁI dưới cùng
//
//    X: 12.50  Y: 30.00 mm | Zoom: 100% | Đối tượng: 5 (chọn 2) | Vùng chọn: 40.00 × 25.00 mm | ... | ● Máy: chưa kết nối
//
//  - Tọa độ chuột cập nhật theo MouseMove (mm, gốc ở góc thước, giống thước trên canvas)
//  - Các ô còn lại làm mới mỗi 200 ms (chỉ gán lại chữ khi có đổi, không nháy)
//  - Ô "Máy" nằm sát phải. Khi làm phần kết nối cổng COM / GRBL, gọi:
//        SetMachineStatus("Máy: COM3 - GRBL sẵn sàng", true);
//        SetMachineStatus("Máy: chưa kết nối", false);
//  Gọi InitStatusBar() 1 lần ở cuối InitWorkspaceLayout() (Form1_Tools.cs).
// =====================================================================
namespace test01_1014
{
    public partial class Form1
    {
        private StatusStrip statusBar = null!;
        private ToolStripStatusLabel stCoord = null!, stZoom = null!, stCount = null!, stSel = null!, stMachine = null!;
        private System.Windows.Forms.Timer? statusTimer;

        private void InitStatusBar()
        {
            if (statusBar != null) return;

            statusBar = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                BackColor = Color.FromArgb(243, 243, 243),
                Font = new Font("Segoe UI", 9f)
            };

            ToolStripStatusLabel Cell(string text, int width) => new ToolStripStatusLabel(text)
            {
                AutoSize = false,
                Width = Scale96(width),
                TextAlign = ContentAlignment.MiddleLeft,
                BorderSides = ToolStripStatusLabelBorderSides.Right,
                BorderStyle = Border3DStyle.Etched
            };

            stCoord = Cell("X: —    Y: —", 200);
            stZoom = Cell("Zoom: 100%", 100);
            stCount = Cell("Đối tượng: 0", 150);
            stSel = Cell("Vùng chọn: —", 230);
            var spring = new ToolStripStatusLabel { Spring = true };
            stMachine = new ToolStripStatusLabel
            {
                AutoSize = true,
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                BorderStyle = Border3DStyle.Etched,
                Padding = new Padding(6, 0, 6, 0)
            };

            // Ô "Khổ in": AutoSize = luôn vừa chữ, không bị cắt (chữ do Form1_PrintArea.cs cập nhật)
            stArea = new ToolStripStatusLabel("Khổ in: —")
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BorderSides = ToolStripStatusLabelBorderSides.Right,
                BorderStyle = Border3DStyle.Etched,
                Padding = new Padding(4, 0, 6, 0)
            };

            // Zoom: bấm vào = thu phóng vừa vùng khắc (thay cho nhãn % trên canvas trước đây)
            stZoom.ToolTipText = "Bấm để thu phóng vừa vùng khắc  (Ctrl+0)";
            stZoom.Click += (s, e) => ZoomFitArea();
            statusBar.ShowItemToolTips = true;

            statusBar.Items.AddRange(new ToolStripItem[] { stCoord, stZoom, stCount, stSel, stArea, spring, stMachine });
            SetMachineStatus("Máy: chưa kết nối", false);

            Controls.Add(statusBar);
            statusBar.SendToBack();           // giành mép dưới trước các thanh bên
            menuStrip1.SendToBack();          // menu vẫn giữ mép trên

            pictureBox1.MouseMove += (s, e) =>
            {
                PointF w = ScreenToWorld(e.Location);
                stCoord.Text = $"X: {w.X * MmPerPx:0.00}    Y: {w.Y * MmPerPx:0.00} mm";
            };
            pictureBox1.MouseLeave += (s, e) => stCoord.Text = "X: —    Y: —";

            statusTimer = new System.Windows.Forms.Timer { Interval = 200 };
            statusTimer.Tick += (s, e) => RefreshStatus();
            statusTimer.Start();
            RefreshStatus();
        }

        // Dùng cho phần kết nối máy sau này
        public void SetMachineStatus(string text, bool connected)
        {
            if (stMachine == null) return;
            stMachine.Text = "●  " + text;
            stMachine.ForeColor = connected ? Color.FromArgb(30, 140, 60) : Color.Firebrick;
        }

        private void SetStatusText(ToolStripStatusLabel l, string t)
        {
            if (l.Text != t) l.Text = t;
        }

        private void RefreshStatus()
        {
            if (statusBar == null) return;

            SetStatusText(stZoom, $"Zoom: {zoom * 100:0}%");

            int total = projectItems.Count;
            var sel = ReadOnlyView ? new List<Vectoritem>() : ArrangeTargets();
            SetStatusText(stCount, sel.Count > 0 ? $"Đối tượng: {total}  (chọn {sel.Count})" : $"Đối tượng: {total}");

            if (sel.Count == 0) SetStatusText(stSel, "Vùng chọn: —");
            else
            {
                RectangleF u = UnionBounds(sel);
                SetStatusText(stSel, $"Vùng chọn: {u.Width * MmPerPx:0.00} × {u.Height * MmPerPx:0.00} mm");
            }
        }
    }
}

// =====================================================================
// PHẦN: Bảng thông số đối tượng (có ô Tầng)   (trước đây là file Form1.Palette.cs)
// =====================================================================
// =====================================================================
//  Form1.Palette.cs  -  Bảng THÔNG SỐ đối tượng kiểu AutoCAD (Properties palette)
//
//  Thay cho các ô Rộng/Cao + chữ cũ ở cột trái. Chia thành các mục rõ ràng:
//    ĐỐI TƯỢNG                : tên loại hình đang chọn
//    VỊ TRÍ & KÍCH THƯỚC (mm) : X, Y, Rộng, Cao (+ Khóa tỉ lệ)          [chữ nhật, tròn, đa giác, ảnh, chữ]
//                               Điểm đầu/cuối X,Y, Dài, Góc              [đường thẳng]
//    CHỮ                      : Nội dung, Font, Cỡ chữ                    [văn bản]
//    LASER                    : Công suất %, Tốc độ F, Số lần chạy        [mọi đối tượng]
//  Sửa ở đây là hình trên canvas đổi ngay (có Undo). Kéo thả trên canvas thì các ô số chạy theo.
//  Toạ độ tính bằng mm theo thước trên canvas (gốc ở góc thước, Y hướng xuống).
// =====================================================================
namespace test01_1014
{
    using OpenCvSharp.Dnn;
    using System.Drawing.Drawing2D;
    using test01_1014;
    using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;
    // ------------------------------------------------------------------
    //  Control bảng thông số
    // ------------------------------------------------------------------
    public class PropertyPalette : Panel
    {
        public float MmPerPx { get; set; } = 0.25f;

        public event Action<string>? BeforeEdit;     // sắp sửa (để Form1 lưu Undo; key dùng gộp các lần sửa liên tiếp)
        public event Action? Edited;                 // đã sửa -> vẽ lại canvas
        public event Action? ExportClicked;
        public event Action? BarcodeEditClicked;     // nút "Sửa nội dung / loại mã" của mã vạch
        public event Action? NodeEditClicked;        // nút "Sửa điểm nút (Node Edit)"
        public event Action? SmoothNodeClicked;      // nút "Bo tròn đỉnh đang chọn"
        public event Action? WeldClicked;            // nút "Hàn liền đường cắt (Weld)"
        public event Action? TraceImageClicked;      // nút "Tách nét Vector (Potrace)..." của ảnh
        public event Action? DeleteClicked;
        public Func<Vectoritem, (int Pos, int Count)>? TierGet;      // tầng của đối tượng (1 = thấp nhất, Count = cao nhất)
        public Action<Vectoritem, int>? TierSet;                     // chuyển đối tượng sang tầng khác
        public Func<List<(string Name, Color Col)>>? LayersGet;     // danh sách lớp (tên + màu) do Form1 cung cấp
        public event Action<int>? LayerPicked;                      // chọn lớp khác cho đối tượng đang chọn
        public event Action<Vectoritem>? FillApplied;    // sửa mục TÔ KÍN -> Form1 chép sang các đối tượng cùng được chọn
        public event Action<Vectoritem>? LaserApplied;   // sửa mục LASER -> Form1 chép sang các đối tượng cùng được chọn
        private int extraSelected;
        public int ExtraSelected                          // số đối tượng chọn thêm ngoài đối tượng chính
        {
            get => extraSelected;
            set
            {
                if (extraSelected == value) return;
                extraSelected = value;
                if (!built) return;
                SyncLock(); UpdateEnabled(); Relayout(); RefreshValues();
            }
        }            // số đối tượng chọn thêm ngoài đối tượng chính

        private sealed class Row
        {
            public Label Lbl = null!;
            public Control Ctl = null!;
            public Func<Vectoritem, bool> Vis = null!;
        }

        private readonly List<(Label Header, List<Row> Rows)> sections = new();
        private readonly Dictionary<string, NumericUpDown> nums = new();
        private readonly Dictionary<string, Label> labels = new();
        private readonly Label lblName, lblEmpty;
        private readonly ChainToggle chain;
        private readonly TextBox txtContent;
        private readonly ComboBox cmbFont;
        private readonly Button btnExport, btnDelete, btnBarcode, btnNodeEdit, btnSmoothNode, btnWeldItems, btnTraceImage;
        private readonly CheckBox chkBold, chkItalic, chkInvert, chkSingleLine, chkWeldText;
        private readonly Panel pnlStyle;
        private readonly ComboBox cmbEngrave, cmbFill, cmbLayer;
        private List<Color> layerCols = new();
        private string layerSig = "";
        private readonly CheckBox chkFillBi, chkFillCross;
        private bool fillVisCache;

        private Vectoritem? item;
        private bool updating;
        private readonly bool built;

        // --- móc nối với Form1 ---
        public Func<Vectoritem, bool>? LockGet;                    // trạng thái khóa tỉ lệ của đối tượng (Form1 nhớ theo Id)
        public Action<Vectoritem, bool>? LockSet;
        public Func<Vectoritem, RectangleF>? ItemBoundsPx;        // khung bao (có tính xoay) của 1 đối tượng, px
        public Func<RectangleF>? GroupBoundsPx;                   // khung bao của cả nhóm đang chọn, px
        public Action<string, float>? GroupNumber;                // sửa X / Y / Rộng / Cao của cả nhóm (đơn vị px)

        private bool IsLineSolo(Vectoritem i) => i is VectorLine && ExtraSelected == 0;
        private bool IsTextSolo(Vectoritem i) => i is VectorText && ExtraSelected == 0;
        private bool IsBoxOrGroup(Vectoritem i) => i is not VectorLine || ExtraSelected > 0;
        private bool ChainVisible() => item != null && (ExtraSelected > 0 || (item is not VectorLine && item is not VectorText));

        private void SyncLock()
        {
            if (item == null) return;
            bool old = updating;
            updating = true;
            try { chain.Checked = LockGet?.Invoke(item) ?? (item is VectorImage); }
            finally { updating = old; }
        }

        private void UpdateEnabled()
        {
            if (item == null) return;
            bool en = ExtraSelected > 0 || item is not VectorText;    // chữ: rộng/cao do font quyết định
            nums["W"].Enabled = nums["H"].Enabled = en;
        }

        private void SetLabelTexts(Vectoritem it)
        {
            bool grp = ExtraSelected > 0, rot = !grp && it.Rotation != 0f && it is not VectorLine;
            labels["X"].Text = grp ? "X (nhóm)" : rot ? "X (bao)" : "X";
            labels["Y"].Text = grp ? "Y (nhóm)" : rot ? "Y (bao)" : "Y";
            labels["W"].Text = grp ? "Rộng (nhóm)" : "Rộng";
            labels["H"].Text = grp ? "Cao (nhóm)" : "Cao";
        }

        // Dời hình sao cho góc trên-trái của KHUNG BAO (hình đã xoay) hoặc của hình (chưa xoay) về đúng toạ độ nhập
        private void MoveBoxTo(float? x, float? y)
        {
            if (item == null) return;
            if (item.Rotation != 0f && item is not VectorLine && ItemBoundsPx != null)
            {
                var bb = ItemBoundsPx(item);
                item.MoveTo(item.X + (x.HasValue ? x.Value - bb.X : 0f), item.Y + (y.HasValue ? y.Value - bb.Y : 0f));
            }
            else item.MoveTo(x ?? item.X, y ?? item.Y);
        }

        private static bool IsLine(Vectoritem i) => i is VectorLine;
        private static bool IsText(Vectoritem i) => i is VectorText;
        private static bool IsBox(Vectoritem i) => i is not VectorLine;                         // mọi thứ có khung Rộng x Cao
        private static bool IsResizable(Vectoritem i) => i is not VectorLine && i is not VectorText;
        private static bool Any(Vectoritem i) => true;
        private static bool IsShape(Vectoritem i) => i is VectorRectangle || i is VectorEllipse || i is VectorPolygon || i is VectorPathItem;   // hình xoay được
        private static bool IsImage(Vectoritem i) => i is VectorImage;
        private static bool IsFillable(Vectoritem i) => (i is VectorRectangle || i is VectorEllipse || i is VectorPolygon || i is VectorText || i is VectorPathItem) && i is not VectorText { SingleLineFont: true };   // hình kín + chữ
        private static bool FillOn(Vectoritem i) => IsFillable(i) && i.FillMode != 0;

        public PropertyPalette()
        {
            AutoScroll = true;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);     // chữ đậm cho dễ nhìn; mọi ô con kế thừa

            lblName = new Label
            {
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 90, 160),
                ForeColor = Color.White
            };
            lblEmpty = new Label
            {
                Text = "Chưa chọn đối tượng.\n\nClick một hình trên canvas để chỉnh thông số, " +
                       "hoặc chọn công cụ vẽ (Shape / Text) rồi kéo thả trên canvas.",
                ForeColor = Color.DimGray
            };
            Controls.Add(lblName);
            Controls.Add(lblEmpty);

            // ---- VỊ TRÍ & KÍCH THƯỚC ----
            // ---- LỚP (đối tượng này thuộc lớp nào; đổi ở đây = chuyển sang lớp khác) ----
            var lay = NewSection("LỚP KHẮC (LAYER)");
            var lblLay = new Label { Text = "Lớp khắc" };
            cmbLayer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
            cmbLayer.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= cmbLayer.Items.Count) return;
                e.DrawBackground();
                var r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, 14, e.Bounds.Height - 6);
                using (var b = new SolidBrush(e.Index < layerCols.Count ? layerCols[e.Index] : Color.Gray)) e.Graphics.FillRectangle(b, r);
                e.Graphics.DrawRectangle(Pens.Gray, r);
                TextRenderer.DrawText(e.Graphics, cmbLayer.Items[e.Index]?.ToString() ?? "", e.Font ?? Font,
                    new Rectangle(r.Right + 6, e.Bounds.Y, e.Bounds.Width - r.Width - 14, e.Bounds.Height),
                    e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            cmbLayer.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item == null || cmbLayer.SelectedIndex < 0) return;
                LayerPicked?.Invoke(cmbLayer.SelectedIndex);
            };
            Controls.Add(lblLay); Controls.Add(cmbLayer);
            lay.Add(new Row { Lbl = lblLay, Ctl = cmbLayer, Vis = Any });
            AddNum(lay, "Tier", "Tầng (cao = khắc trước)", i => ExtraSelected == 0, 1, 100000, 0, 1);

            var pos = NewSection("VỊ TRÍ & KÍCH THƯỚC (mm) · gốc = góc trên-trái");
            AddNum(pos, "X", "X", IsBoxOrGroup, -100000, 100000, 2, 1);
            AddNum(pos, "Y", "Y", IsBoxOrGroup, -100000, 100000, 2, 1);
            AddNum(pos, "W", "Rộng", IsBoxOrGroup, 0.25m, 100000, 2, 1);
            AddNum(pos, "H", "Cao", IsBoxOrGroup, 0.25m, 100000, 2, 1);

            chain = new ChainToggle { Visible = false };
            chain.CheckedChanged += (s, e) => { if (!updating && item != null) LockSet?.Invoke(item, chain.Checked); };
            Controls.Add(chain);
            AddNum(pos, "Rot", "Xoay (°)", i => i is not VectorLine && ExtraSelected == 0, -360, 360, 1, 1);

            AddNum(pos, "X1", "Điểm đầu X", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "Y1", "Điểm đầu Y", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "X2", "Điểm cuối X", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "Y2", "Điểm cuối Y", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "Len", "Chiều dài", IsLineSolo, 0, 100000, 2, 1);
            AddNum(pos, "Ang", "Góc (°, 0 = sang phải)", IsLineSolo, 0, 360, 1, 5);

            // ---- CHỮ ----
            var txt = NewSection("CHỮ");
            var lblC = new Label { Text = "Nội dung" };
            txtContent = new TextBox();
            Controls.Add(lblC); Controls.Add(txtContent);
            txt.Add(new Row { Lbl = lblC, Ctl = txtContent, Vis = IsTextSolo });

            var lblF = new Label { Text = "Font" };
            cmbFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var f in FontFamily.Families) cmbFont.Items.Add(f.Name);
            Controls.Add(lblF); Controls.Add(cmbFont);
            txt.Add(new Row { Lbl = lblF, Ctl = cmbFont, Vis = IsTextSolo });

            AddNum(txt, "FS", "Cỡ chữ (pt)", IsTextSolo, 1, 1000, 1, 1);

            var lblSt = new Label { Text = "Kiểu" };
            chkBold = new CheckBox
            {
                Text = "B",
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(0, 0, 60, 28),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            chkItalic = new CheckBox
            {
                Text = "I",
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(68, 0, 60, 28),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold | FontStyle.Italic)
            };
            pnlStyle = new Panel();
            pnlStyle.Controls.Add(chkBold); pnlStyle.Controls.Add(chkItalic);
            Controls.Add(lblSt); Controls.Add(pnlStyle);
            txt.Add(new Row { Lbl = lblSt, Ctl = pnlStyle, Vis = IsTextSolo });

            var lblSL = new Label();
            chkSingleLine = new CheckBox { Text = "Font 1 nét (Hershey - khắc nhanh)", AutoSize = true };
            Controls.Add(lblSL); Controls.Add(chkSingleLine);
            txt.Add(new Row { Lbl = lblSL, Ctl = chkSingleLine, Vis = IsTextSolo });

            var lblWT = new Label();
            chkWeldText = new CheckBox { Text = "Hàn liền nét chữ giao nhau (Weld)", AutoSize = true };
            Controls.Add(lblWT); Controls.Add(chkWeldText);
            txt.Add(new Row { Lbl = lblWT, Ctl = chkWeldText, Vis = i => i is VectorText vt && !vt.SingleLineFont && ExtraSelected == 0 });

            AddNum(txt, "ArcR", "Uốn cong R (mm, 0=thẳng)", IsTextSolo, -5000, 5000, 1, 5);
            AddNum(txt, "CSp", "Giãn chữ (mm)", IsTextSolo, -50, 100, 2, 0.2m);

            // ---- ĐƯỜNG DẪN & SỬA NÚT (NODE EDIT / WELD) ----
            var nod = NewSection("ĐƯỜNG DẪN & ĐIỂM NÚT (NODE / WELD)");
            var lblNE = new Label();
            btnNodeEdit = new Button { Text = "Sửa điểm nút / đường dẫn (Phím N)", Height = 30 };
            btnNodeEdit.Click += (s, e) => NodeEditClicked?.Invoke();
            Controls.Add(lblNE); Controls.Add(btnNodeEdit);
            nod.Add(new Row { Lbl = lblNE, Ctl = btnNodeEdit, Vis = i => i is not VectorImage && ExtraSelected == 0 });

            var lblSM = new Label();
            btnSmoothNode = new Button { Text = "Bo tròn đỉnh đang chọn (Phím S)", Height = 30 };
            btnSmoothNode.Click += (s, e) => SmoothNodeClicked?.Invoke();
            Controls.Add(lblSM); Controls.Add(btnSmoothNode);
            nod.Add(new Row { Lbl = lblSM, Ctl = btnSmoothNode, Vis = i => i is VectorPathItem && ExtraSelected == 0 });

            var lblWD = new Label();
            btnWeldItems = new Button { Text = "Hàn gộp thành đường cắt (Phím W)", Height = 30 };
            btnWeldItems.Click += (s, e) => WeldClicked?.Invoke();
            Controls.Add(lblWD); Controls.Add(btnWeldItems);
            nod.Add(new Row { Lbl = lblWD, Ctl = btnWeldItems, Vis = i => i is not VectorImage });

            // ---- MÃ VẠCH / QR ----
            var bar = NewSection("MÃ VẠCH / QR");
            var lblBar = new Label { Text = "Nội dung / loại mã" };
            btnBarcode = new Button { Text = "Sửa nội dung / loại mã…" };
            btnBarcode.Click += (s, e) => BarcodeEditClicked?.Invoke();
            Controls.Add(lblBar); Controls.Add(btnBarcode);
            bar.Add(new Row { Lbl = lblBar, Ctl = btnBarcode, Vis = i => i is VectorBarcode && ExtraSelected == 0 });

            // ---- KHẮC ẢNH ----
            var img = NewSection("KHẮC ẢNH");
            var lblM = new Label { Text = "Chế độ" };
            cmbEngrave = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEngrave.Items.AddRange(new object[] { "Theo cài đặt G-code", "Ảnh xám (nhiều mức)", "Đen trắng (1-bit)" });
            Controls.Add(lblM); Controls.Add(cmbEngrave);
            img.Add(new Row { Lbl = lblM, Ctl = cmbEngrave, Vis = IsImage });

            AddNum(img, "Th", "Ngưỡng (0-255)", i => i is VectorImage v && v.EngraveMode == 2, 0, 255, 0, 1);

            var lblInv = new Label();
            chkInvert = new CheckBox { Text = "Đảo màu (khắc vùng sáng)", AutoSize = true };
            Controls.Add(lblInv); Controls.Add(chkInvert);
            img.Add(new Row { Lbl = lblInv, Ctl = chkInvert, Vis = i => i is VectorImage v && v.EngraveMode != 0 });

            var lblTr = new Label();
            btnTraceImage = new Button { Text = "Tách nét Vector / Potrace (Cắt viền)…", Height = 30 };
            btnTraceImage.Click += (s, e) => TraceImageClicked?.Invoke();
            Controls.Add(lblTr); Controls.Add(btnTraceImage);
            img.Add(new Row { Lbl = lblTr, Ctl = btnTraceImage, Vis = i => i is VectorImage && ExtraSelected == 0 });

            // ---- TÔ KÍN (riêng từng đối tượng; sửa lớp thì đè lại theo lớp) ----
            var fil = NewSection("TÔ KÍN (FILL) - RIÊNG ĐỐI TƯỢNG");
            var lblFm = new Label { Text = "Chế độ" };
            cmbFill = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFill.Items.AddRange(new object[] { "Đường viền", "Tô kín", "Tô kín + viền" });
            Controls.Add(lblFm); Controls.Add(cmbFill);
            fil.Add(new Row { Lbl = lblFm, Ctl = cmbFill, Vis = IsFillable });
            AddNum(fil, "FI", "Cách dòng (mm)", FillOn, 0.02m, 5, 2, 0.01m);
            AddNum(fil, "FA", "Góc quét (°)", FillOn, -360, 360, 0, 5);
            var lblFb = new Label();
            chkFillBi = new CheckBox { Text = "Quét 2 chiều", AutoSize = true };
            Controls.Add(lblFb); Controls.Add(chkFillBi);
            fil.Add(new Row { Lbl = lblFb, Ctl = chkFillBi, Vis = FillOn });
            var lblFc = new Label();
            chkFillCross = new CheckBox { Text = "Quét chéo (thêm lượt vuông góc)", AutoSize = true };
            Controls.Add(lblFc); Controls.Add(chkFillCross);
            fil.Add(new Row { Lbl = lblFc, Ctl = chkFillCross, Vis = FillOn });

            // ---- LASER ----
            var las = NewSection("LASER");
            AddNum(las, "Pw", "Công suất (%)", Any, 0, 100, 0, 5);
            AddNum(las, "Sp", "Tốc độ F (mm/phút)", Any, 1, 60000, 0, 100);
            AddNum(las, "Ps", "Số lần chạy", Any, 1, 100, 0, 1);

            // ---- nút ----
            btnExport = new Button { Text = "Xuất G-code / Mô phỏng…", Height = 32 };
            btnDelete = new Button { Text = "Xóa đối tượng", Height = 32 };
            btnExport.Click += (s, e) => ExportClicked?.Invoke();
            btnDelete.Click += (s, e) => DeleteClicked?.Invoke();
            Controls.Add(btnExport); Controls.Add(btnDelete);

            // ---- sự kiện sửa nội dung chữ / font / khóa tỉ lệ ----
            txtContent.TextChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:text:" + t.Id);
                t.TextContent = txtContent.Text;
                Edited?.Invoke();
                RefreshValues();
            };
            cmbFont.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item is not VectorText t || cmbFont.SelectedItem == null) return;
                BeforeEdit?.Invoke("pal:font:" + t.Id);
                t.FontName = cmbFont.SelectedItem.ToString()!;
                Edited?.Invoke();
                RefreshValues();
            };

            chkBold.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:bold:" + t.Id);
                t.FontBold = chkBold.Checked;
                Edited?.Invoke();
                RefreshValues();
            };
            chkItalic.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:italic:" + t.Id);
                t.FontItalic = chkItalic.Checked;
                Edited?.Invoke();
                RefreshValues();
            };
            chkSingleLine.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:sline:" + t.Id);
                t.SingleLineFont = chkSingleLine.Checked;
                Edited?.Invoke();
                Relayout();
                RefreshValues();
            };
            chkWeldText.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:weld:" + t.Id);
                t.WeldText = chkWeldText.Checked;
                Edited?.Invoke();
                RefreshValues();
            };
            cmbEngrave.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item is not VectorImage vi || cmbEngrave.SelectedIndex < 0) return;
                BeforeEdit?.Invoke("pal:emode:" + vi.Id);
                vi.EngraveMode = cmbEngrave.SelectedIndex;
                Edited?.Invoke();
                Relayout();                     // ô Ngưỡng / Đảo màu ẩn hiện theo chế độ
            };
            cmbFill.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item == null || !IsFillable(item) || cmbFill.SelectedIndex < 0) return;
                BeforeEdit?.Invoke("pal:fmode:" + item.Id);
                item.FillMode = cmbFill.SelectedIndex;
                FillApplied?.Invoke(item);
                Edited?.Invoke();
                Relayout();                     // các ô cách dòng / góc ẩn hiện theo chế độ
            };
            chkFillBi.CheckedChanged += (s, e) =>
            {
                if (updating || item == null || !IsFillable(item)) return;
                BeforeEdit?.Invoke("pal:fbi:" + item.Id);
                item.FillBidir = chkFillBi.Checked;
                FillApplied?.Invoke(item);
                Edited?.Invoke();
            };
            chkFillCross.CheckedChanged += (s, e) =>
            {
                if (updating || item == null || !IsFillable(item)) return;
                BeforeEdit?.Invoke("pal:fcr:" + item.Id);
                item.FillCross = chkFillCross.Checked;
                FillApplied?.Invoke(item);
                Edited?.Invoke();
            };
            chkInvert.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorImage vi) return;
                BeforeEdit?.Invoke("pal:einv:" + vi.Id);
                vi.Invert = chkInvert.Checked;
                Edited?.Invoke();
            };

            built = true;
            Relayout();
        }

        // ---------- dựng giao diện ----------
        private List<Row> NewSection(string title)
        {
            var header = new Label
            {
                Text = title,
                UseMnemonic = false,                       // giữ nguyên dấu "&" trong tiêu đề
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(222, 229, 240),
                ForeColor = Color.FromArgb(40, 60, 100)
            };
            Controls.Add(header);
            var rows = new List<Row>();
            sections.Add((header, rows));
            return rows;
        }

        private void AddNum(List<Row> rows, string id, string label, Func<Vectoritem, bool> vis,
                            decimal min, decimal max, int dec, decimal inc)
        {
            var lbl = new Label { Text = label, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            var n = new NumericUpDown { DecimalPlaces = dec, Minimum = min, Maximum = max, Increment = inc };
            n.ValueChanged += (s, e) => { if (!updating) ApplyNumber(id, (double)n.Value); };
            Controls.Add(lbl); Controls.Add(n);
            rows.Add(new Row { Lbl = lbl, Ctl = n, Vis = vis });
            nums[id] = n; labels[id] = lbl;
        }

        // ---------- gắn đối tượng ----------
        public void Bind(Vectoritem? it)
        {
            item = it;
            updating = true;
            try
            {
                if (it != null)
                {
                    SyncLock();
                    UpdateEnabled();
                }
            }
            finally { updating = false; }
            RefreshValues();
            Relayout();
        }

        // Cập nhật số hiển thị theo đối tượng (bỏ qua ô đang gõ dở)
        public void RefreshValues()
        {
            if (item == null) return;
            bool old = updating;
            updating = true;
            try
            {
                float k = MmPerPx;
                var it = item;
                lblName.Text = ExtraSelected > 0 ? $"{it.Name}  (+{ExtraSelected} đối tượng khác)" : it.Name;

                if (ExtraSelected > 0 && GroupBoundsPx != null)
                {   // chọn nhiều: X / Y / Rộng / Cao là của khung bao CẢ NHÓM
                    var gb = GroupBoundsPx();
                    Set("X", gb.X * k); Set("Y", gb.Y * k); Set("W", gb.Width * k); Set("H", gb.Height * k);
                }
                else if (it.Rotation != 0f && it is not VectorLine && ItemBoundsPx != null)
                {   // hình đã xoay: X / Y theo khung bao (thấy được trên canvas), Rộng / Cao là kích thước riêng của hình
                    var bb = ItemBoundsPx(it);
                    Set("X", bb.X * k); Set("Y", bb.Y * k);
                    Set("W", it.Width * k); Set("H", it.Height * k);
                }
                else
                {
                    Set("X", it.X * k); Set("Y", it.Y * k);
                    Set("W", it.Width * k); Set("H", it.Height * k);
                }
                Set("Rot", it.Rotation);
                SetLabelTexts(it);

                if (it is VectorLine ln)
                {
                    float dx = ln.X2 - ln.X, dy = ln.Y2 - ln.Y;
                    double ang = Math.Atan2(-dy, dx) * 180.0 / Math.PI;      // Y hướng lên cho dễ hiểu
                    if (ang < 0) ang += 360;
                    Set("X1", ln.X * k); Set("Y1", ln.Y * k);
                    Set("X2", ln.X2 * k); Set("Y2", ln.Y2 * k);
                    Set("Len", Math.Sqrt(dx * dx + dy * dy) * k);
                    Set("Ang", ang);
                }
                if (it is VectorText t)
                {
                    Set("FS", t.FontSize);
                    Set("ArcR", t.ArcRadiusMm);
                    Set("CSp", t.CharSpacingMm);
                    if (!txtContent.ContainsFocus && txtContent.Text != t.TextContent) txtContent.Text = t.TextContent;
                    if (!cmbFont.ContainsFocus) SelectFont(t.FontName);
                    chkBold.Checked = t.FontBold;
                    chkItalic.Checked = t.FontItalic;
                    chkSingleLine.Checked = t.SingleLineFont;
                    chkWeldText.Checked = t.WeldText;
                }
                if (it is VectorImage vi)
                {
                    cmbEngrave.SelectedIndex = Math.Clamp(vi.EngraveMode, 0, 2);
                    Set("Th", vi.Threshold);
                    chkInvert.Checked = vi.Invert;
                }
                if (TierGet != null)
                {
                    var (tp, tc) = TierGet(it);
                    nums["Tier"].Maximum = Math.Max(1, tc);
                    Set("Tier", tp);
                }
                RebuildLayerCombo();
                if (cmbLayer.Items.Count > 0) cmbLayer.SelectedIndex = Math.Clamp(it.LayerId, 0, cmbLayer.Items.Count - 1);
                if (IsFillable(it))
                {
                    cmbFill.SelectedIndex = Math.Clamp(it.FillMode, 0, 2);
                    Set("FI", it.FillIntervalMm); Set("FA", it.FillAngle);
                    chkFillBi.Checked = it.FillBidir;
                    chkFillCross.Checked = it.FillCross;
                }
                Set("Pw", it.LaserPower); Set("Sp", it.LaserSpeed); Set("Ps", it.Passes);
            }
            finally { updating = old; }
            bool fv = FillOn(item);
            if (fv != fillVisCache) { fillVisCache = fv; if (built) Relayout(); }
        }

        // Dựng lại danh sách lớp trong ô chọn khi tên / màu / số lớp đổi
        private void RebuildLayerCombo()
        {
            var ls = LayersGet?.Invoke();
            if (ls == null) return;
            string sig = string.Join("|", ls.Select(x => x.Name + ":" + x.Col.ToArgb()));
            if (sig == layerSig && cmbLayer.Items.Count == ls.Count) return;
            layerSig = sig;
            layerCols = ls.Select(x => x.Col).ToList();
            cmbLayer.Items.Clear();
            foreach (var x in ls) cmbLayer.Items.Add(x.Name);
        }

        // Form1 gọi khi lớp đổi tên / màu / số lượng
        public void RefreshLayers()
        {
            if (!built) return;
            bool old = updating;
            updating = true;
            try
            {
                RebuildLayerCombo();
                if (item != null && cmbLayer.Items.Count > 0)
                    cmbLayer.SelectedIndex = Math.Clamp(item.LayerId, 0, cmbLayer.Items.Count - 1);
                cmbLayer.Invalidate();
            }
            finally { updating = old; }
        }

        private void Set(string id, double v)
        {
            var n = nums[id];
            if (n.ContainsFocus || double.IsNaN(v) || double.IsInfinity(v)) return;
            decimal d = Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)Math.Round(v, 4)));
            if (n.Value != d) n.Value = d;
        }

        private void SelectFont(string name)
        {
            int i = cmbFont.FindStringExact(name);
            if (i < 0) { cmbFont.Items.Add(name); i = cmbFont.Items.Count - 1; }
            if (cmbFont.SelectedIndex != i) cmbFont.SelectedIndex = i;
        }

        // ---------- áp giá trị gõ vào đối tượng ----------
        private void ApplyNumber(string id, double v)
        {
            if (item == null) return;
            float k = MmPerPx;
            float px = (float)(v / k);

            if (ExtraSelected > 0 && (id == "X" || id == "Y" || id == "W" || id == "H"))
            {   // chọn nhiều: sửa cả nhóm
                BeforeEdit?.Invoke("pal:group:" + id);
                GroupNumber?.Invoke(id, px);
                Edited?.Invoke();
                RefreshValues();
                return;
            }

            BeforeEdit?.Invoke("pal:" + item.Id + ":" + id);

            switch (id)
            {
                case "X": MoveBoxTo(px, null); break;
                case "Y": MoveBoxTo(null, px); break;
                case "W": SetSize(px, null); break;
                case "H": SetSize(null, px); break;

                case "X1": if (item is VectorLine a1) a1.X = px; break;
                case "Y1": if (item is VectorLine a2) a2.Y = px; break;
                case "X2": if (item is VectorLine a3) a3.X2 = px; break;
                case "Y2": if (item is VectorLine a4) a4.Y2 = px; break;
                case "Len":
                    if (item is VectorLine l)
                    {
                        float dx = l.X2 - l.X, dy = l.Y2 - l.Y;
                        double cur = Math.Sqrt(dx * dx + dy * dy);
                        if (cur < 1e-6) l.X2 = l.X + px;
                        else { l.X2 = l.X + (float)(dx / cur * px); l.Y2 = l.Y + (float)(dy / cur * px); }
                    }
                    break;
                case "Ang":
                    if (item is VectorLine m)
                    {
                        float dx = m.X2 - m.X, dy = m.Y2 - m.Y;
                        double len = Math.Sqrt(dx * dx + dy * dy), a = v * Math.PI / 180.0;
                        m.X2 = m.X + (float)(len * Math.Cos(a));
                        m.Y2 = m.Y - (float)(len * Math.Sin(a));
                    }
                    break;

                case "FS": if (item is VectorText t) t.FontSize = (float)v; break;
                case "ArcR": if (item is VectorText ta) ta.ArcRadiusMm = (float)v; break;
                case "CSp": if (item is VectorText tc) tc.CharSpacingMm = (float)v; break;
                case "Rot": item.Rotation = (float)v; break;
                case "Tier": TierSet?.Invoke(item, (int)v); break;
                case "FI": item.FillIntervalMm = v; FillApplied?.Invoke(item); break;
                case "FA": item.FillAngle = (float)v; FillApplied?.Invoke(item); break;
                case "Th": if (item is VectorImage vimg) vimg.Threshold = (int)v; break;
                case "Pw": item.LaserPower = (int)v; LaserApplied?.Invoke(item); break;
                case "Sp": item.LaserSpeed = (int)v; LaserApplied?.Invoke(item); break;
                case "Ps": item.Passes = (int)v; LaserApplied?.Invoke(item); break;
            }
            Edited?.Invoke();
            RefreshValues();      // cập nhật các ô phụ thuộc (ví dụ khóa tỉ lệ đổi Cao khi gõ Rộng)
        }

        private void SetSize(float? w, float? h)
        {
            if (item == null || !IsResizable(item)) return;
            float ow = item.Width, oh = item.Height;
            float nw = w ?? ow, nh = h ?? oh;
            if (chain.Checked && ow > 0.0001f && oh > 0.0001f)        // khóa tỉ lệ: đổi một cạnh thì cạnh kia theo
            {
                if (w != null) nh = nw * oh / ow;
                else nw = nh * ow / oh;
            }
            nw = Math.Max(1f, nw); nh = Math.Max(1f, nh);
            float nx = item.X, ny = item.Y;
            if (item.Rotation != 0f)                                  // hình đã xoay: giữ nguyên TÂM, không bị nhảy vị trí
            {
                float cx = item.X + ow / 2f, cy = item.Y + oh / 2f;
                nx = cx - nw / 2f; ny = cy - nh / 2f;
            }
            if (item is VectorPathItem pi)
            {
                pi.Scale(nw, nh, nx, ny);
            }
            else
            {
                item.X = nx; item.Y = ny;
                item.Width = nw; item.Height = nh;
            }
        }

        // ---------- bố cục ----------
        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (built) Relayout();
        }

        private void Relayout()
        {
            // Luôn chừa chỗ cho thanh cuộn dọc (có / không có thì bề rộng dùng được vẫn như nhau) -> không bao giờ sinh thanh cuộn ngang
            int w = Math.Max(150, ClientSize.Width - (VScroll ? 0 : SystemInformation.VerticalScrollBarWidth));
            int y = 0;

            bool has = item != null;
            lblName.Visible = has;
            lblEmpty.Visible = !has;
            btnDelete.Visible = has;
            bool hasChain = ChainVisible();
            int yW = -1, yH = -1, xChain = 0;

            if (has) { lblName.SetBounds(0, y, w, 28); y += 34; }
            else { lblEmpty.SetBounds(10, 10, w - 20, 90); y += 110; }

            foreach (var (header, rows) in sections)
            {
                bool any = has && rows.Any(r => r.Vis(item!));
                header.Visible = any;
                if (!any)
                {
                    foreach (var r in rows) { r.Lbl.Visible = false; r.Ctl.Visible = false; }
                    continue;
                }
                header.SetBounds(0, y, w, 22); y += 28;

                foreach (var r in rows)
                {
                    bool v = r.Vis(item!);
                    r.Lbl.Visible = r.Ctl.Visible = v;
                    if (!v) continue;
                    if (ReferenceEquals(r.Ctl, btnBarcode) || ReferenceEquals(r.Ctl, btnNodeEdit) ||
                        ReferenceEquals(r.Ctl, btnSmoothNode) || ReferenceEquals(r.Ctl, btnWeldItems) ||
                        ReferenceEquals(r.Ctl, btnTraceImage))      // nút dài, nằm riêng một dòng cho đủ chữ
                    {
                        r.Lbl.Visible = false;
                        r.Ctl.SetBounds(10, y, w - 20, 30);
                        y += 36;
                        continue;
                    }
                    if (ReferenceEquals(r.Ctl, chkSingleLine) || ReferenceEquals(r.Ctl, chkWeldText))
                    {
                        r.Lbl.Visible = false;
                        r.Ctl.SetBounds(12, y, w - 24, 24);
                        y += 28;
                        continue;
                    }
                    bool isW = ReferenceEquals(r.Ctl, nums["W"]), isH = ReferenceEquals(r.Ctl, nums["H"]);
                    int cw = Math.Max(60, w - 142 - 12 - (hasChain && (isW || isH) ? 34 : 0));
                    bool tall = ReferenceEquals(r.Ctl, pnlStyle);          // hàng nút B / I cao hơn các hàng khác
                    int rh = tall ? 28 : 24;
                    r.Lbl.SetBounds(10, y, 130, rh);
                    r.Ctl.SetBounds(142, y, cw, rh);
                    if (isW) { yW = y; xChain = 142 + cw; }
                    if (isH) yH = y;
                    y += tall ? 34 : 30;
                }
                y += 6;
            }

            if (hasChain && yW >= 0 && yH > yW)                 // nút xích nằm giữa 2 ô Rộng / Cao
            {
                chain.SetBounds(xChain + 2, yW + 1, 30, yH - yW + 24);
                chain.Visible = true;
            }
            else chain.Visible = false;

            y += 4;
            btnExport.SetBounds(10, y, w - 20, 32); y += 38;
            if (has) { btnDelete.SetBounds(10, y, w - 20, 32); y += 38; }
            AutoScrollMinSize = new Size(0, y + 20);
        }
    }

    // ------------------------------------------------------------------
    //  Gắn bảng vào Form1
    // ------------------------------------------------------------------
    public partial class Form1
    {
        private PropertyPalette propPalette = null!;
        private System.Windows.Forms.Timer palTimer = null!;
        private Vectoritem? palItem;
        private int palSig;

        private void InitPalette()
        {
            if (propPalette != null) return;      // chỉ chạy 1 lần

            propPalette = new PropertyPalette { Dock = DockStyle.Fill, MmPerPx = MmPerPx };
            propPalette.BeforeEdit += key => PushUndoCoalesced(key);
            propPalette.Edited += () => pictureBox1.Invalidate();
            propPalette.ExportClicked += () => exportGCodeToolStripMenuItem.PerformClick();
            propPalette.DeleteClicked += () => DeleteSelected();
            propPalette.NodeEditClicked += () => ToggleNodeEditMode();
            propPalette.SmoothNodeClicked += () => SmoothSelectedNode();
            propPalette.WeldClicked += () => WeldSelectedItems();
            propPalette.TraceImageClicked += () => TraceSelectedImage();
            propPalette.LockGet = it => SelCount > 1 ? groupLock : IsRatioLocked(it);
            propPalette.LockSet = (it, on) => { if (SelCount > 1) groupLock = on; else ratioLock[it.Id] = on; };
            propPalette.ItemBoundsPx = it => ItemBounds(it);
            propPalette.GroupBoundsPx = () => UnionBounds(SelectedList());
            propPalette.GroupNumber = ApplyGroupNumber;
            propPalette.FillApplied += src =>          // tô kín: chép sang các hình kín / chữ đang được chọn cùng
            {
                foreach (var o in projectItems)
                    if (o.IsSelected && o != src && (o is VectorRectangle || o is VectorEllipse || o is VectorPolygon || o is VectorText))
                    {
                        o.FillMode = src.FillMode; o.FillIntervalMm = src.FillIntervalMm; o.FillAngle = src.FillAngle;
                        o.FillCross = src.FillCross; o.FillBidir = src.FillBidir;
                    }
            };
            propPalette.TierGet = it => (projectItems.IndexOf(it) + 1, projectItems.Count);
            propPalette.TierSet = (it, n) => MoveItemToTier(it, n);
            propPalette.LaserApplied += src => ApplyLaserFromItem(src);     // sửa mục LASER = sửa thông số của LỚP (Form1_Layers.cs)

            // Phủ lên cột trái cũ (panelProperties / textpanel / thanh Blur-Canny) -> khỏi phải xóa chúng
            splitContainer1.Panel1.Controls.Add(propPalette);
            propPalette.BringToFront();
            if (splitContainer1.SplitterDistance < 290) splitContainer1.SplitterDistance = 290;

            // Theo dõi đối tượng đang chọn: đổi lựa chọn -> gắn lại, kéo thả -> cập nhật số
            palTimer = new System.Windows.Forms.Timer { Interval = 120 };
            palTimer.Tick += (s, e) =>
            {
                Vectoritem? sel = ReadOnlyView ? null : selectedItem;
                int extra = ReadOnlyView ? 0 : Math.Max(0, SelCount - 1);
                if (extra != propPalette.ExtraSelected) { propPalette.ExtraSelected = extra; propPalette.RefreshValues(); }
                if (!ReferenceEquals(sel, palItem))
                {
                    palItem = sel;
                    propPalette.Bind(sel);
                    palSig = ItemSig(sel);
                    return;
                }
                int sg = ItemSig(sel);
                if (sg != palSig) { palSig = sg; propPalette.RefreshValues(); }
            };
            palTimer.Start();
        }

        // ------------------------------------------------------------------
        //  Khóa tỉ lệ (nút xích) + nhập số X / Y / Rộng / Cao cho CẢ NHÓM đang chọn
        // ------------------------------------------------------------------
        private bool groupLock;                                            // khóa tỉ lệ khi sửa Rộng / Cao của nhóm
        private readonly Dictionary<string, bool> ratioLock = new();       // khóa tỉ lệ của từng đối tượng (theo Id; ảnh mặc định = khóa)
        private bool IsRatioLocked(Vectoritem it) => ratioLock.TryGetValue(it.Id, out bool v) ? v : it is VectorImage;

        private void ApplyGroupNumber(string id, float px)
        {
            var sel = SelectedList();
            if (sel.Count < 2) return;
            RectangleF u = UnionBounds(sel);
            switch (id)
            {
                case "X": foreach (var it in sel) ShiftItem(it, px - u.Left, 0f); break;
                case "Y": foreach (var it in sel) ShiftItem(it, 0f, px - u.Top); break;
                case "W":
                    if (u.Width < 1e-3f) return;
                    { float sx = px / u.Width; ScaleGroup(sel, u, sx, groupLock ? sx : 1f); }
                    break;
                case "H":
                    if (u.Height < 1e-3f) return;
                    { float sy = px / u.Height; ScaleGroup(sel, u, groupLock ? sy : 1f, sy); }
                    break;
            }
            pictureBox1.Invalidate();
        }

        // Co giãn cả nhóm quanh góc trên-trái của khung bao. Hình xoay 90° đổi chỗ 2 hệ số; xoay góc lẻ thì co đều (không méo được)
        private void ScaleGroup(List<Vectoritem> sel, RectangleF u, float sx, float sy)
        {
            foreach (var it in sel)
            {
                if (it is VectorLine l)
                {
                    l.X = u.Left + (l.X - u.Left) * sx; l.Y = u.Top + (l.Y - u.Top) * sy;
                    l.X2 = u.Left + (l.X2 - u.Left) * sx; l.Y2 = u.Top + (l.Y2 - u.Top) * sy;
                    continue;
                }
                float cx = u.Left + (it.X + it.Width / 2f - u.Left) * sx;
                float cy = u.Top + (it.Y + it.Height / 2f - u.Top) * sy;

                float rot = ((it.Rotation % 180f) + 180f) % 180f;
                float fw, fh;
                if (rot < 0.5f || rot > 179.5f) { fw = sx; fh = sy; }
                else if (Math.Abs(rot - 90f) < 0.5f) { fw = sy; fh = sx; }
                else fw = fh = MathF.Sqrt(sx * sy);

                if (it is VectorText t)
                {
                    t.FontSize = Math.Clamp(t.FontSize * fh, 1f, 1000f);
                    SizeF sz = MeasureText(t);
                    t.Width = sz.Width; t.Height = sz.Height;
                }
                else
                {
                    it.Width = Math.Max(1f, it.Width * fw);
                    it.Height = Math.Max(1f, it.Height * fh);
                }
                it.X = cx - it.Width / 2f; it.Y = cy - it.Height / 2f;
            }
        }

        // Chuyển đối tượng sang tầng n (1 = thấp nhất; cuối danh sách projectItems = tầng cao nhất)
        private void MoveItemToTier(Vectoritem it, int n)
        {
            int idx = projectItems.IndexOf(it);
            if (idx < 0) return;
            int to = Math.Clamp(n - 1, 0, projectItems.Count - 1);
            if (to == idx) return;
            projectItems.RemoveAt(idx);
            projectItems.Insert(to, it);
            pictureBox1.Invalidate();
            RebuildObjList();
        }

        private int ItemSig(Vectoritem? it)
        {
            if (it == null) return 0;
            var h = new HashCode();
            h.Add(projectItems.IndexOf(it)); h.Add(projectItems.Count);
            h.Add(it.X); h.Add(it.Y); h.Add(it.Width); h.Add(it.Height);
            h.Add(it.LaserPower); h.Add(it.LaserSpeed); h.Add(it.Passes);
            h.Add(ItemExtrasHash(it));
            if (it is VectorLine l) { h.Add(l.X2); h.Add(l.Y2); }
            else if (it is VectorText t) { h.Add(t.TextContent); h.Add(t.FontName); h.Add(t.FontSize); }
            return h.ToHashCode();
        }
    }

    // ##############################################################################
    //  Nút "xích" khóa tỉ lệ: ngoặc nối ô Rộng và ô Cao, giữa là mắt xích
    //  (mắt xích đóng + xanh = đang khóa; hở + xám = mở khóa)
    // ##############################################################################
    public class ChainToggle : Control
    {
        private bool isChecked, hover;
        private readonly ToolTip tip = new();
        public event EventHandler? CheckedChanged;

        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                tip.SetToolTip(this, isChecked ? "Đang khóa tỉ lệ Rộng / Cao (bấm để mở khóa)" : "Bấm để khóa tỉ lệ Rộng / Cao");
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ChainToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            Size = new Size(28, 60);
            tip.SetToolTip(this, "Bấm để khóa tỉ lệ Rộng / Cao");
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        private static GraphicsPath Round(RectangleF r, float rad)
        {
            float d = rad * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color col = isChecked ? Color.FromArgb(0, 102, 204) : Color.FromArgb(140, 140, 140);
            float top = 11f, bot = Height - 13f, vx = Width - 8f, mid = (top + bot) / 2f;

            if (hover)
                using (var hb = new SolidBrush(Color.FromArgb(40, 0, 120, 215)))
                    g.FillRectangle(hb, vx - 11, mid - 16, 22, 32);

            // ngoặc: nối từ ô Rộng (trên) xuống ô Cao (dưới), chừa chỗ cho mắt xích ở giữa
            using (var pen = new Pen(col, 1.6f))
            {
                g.DrawLine(pen, 3, top, vx, top);
                g.DrawLine(pen, 3, bot, vx, bot);
                g.DrawLine(pen, vx, top, vx, mid - 12);
                g.DrawLine(pen, vx, mid + 12, vx, bot);

                if (isChecked)       // 2 mắt xích lồng vào nhau
                {
                    using var a = Round(new RectangleF(vx - 4f, mid - 11f, 8f, 13f), 3.5f);
                    using var b = Round(new RectangleF(vx - 4f, mid - 2f, 8f, 13f), 3.5f);
                    g.DrawPath(pen, a);
                    g.DrawPath(pen, b);
                }
                else                 // 2 mắt xích tách rời
                {
                    using var a = Round(new RectangleF(vx - 4f, mid - 14f, 8f, 11f), 3.5f);
                    using var b = Round(new RectangleF(vx - 4f, mid + 3f, 8f, 11f), 3.5f);
                    g.DrawPath(pen, a);
                    g.DrawPath(pen, b);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}

// =====================================================================
// PHẦN: Cài đặt ứng dụng (ngôn ngữ, giao diện sáng/tối)   (trước đây là file Form1_Settings.cs)
// =====================================================================
// =====================================================================
//  Form1_Settings.cs  -  CÀI ĐẶT ỨNG DỤNG: ngôn ngữ (Tiếng Việt / English) và giao diện (Sáng / Tối)
//
//  Mở bằng menu  Tệp > Cài đặt...  (Ctrl+,). Lưu ở %AppData%\test01_1014\settings.json.
//  Cách làm (không sửa các file giao diện khác):
//    - Ngôn ngữ: bảng dịch Việt -> Anh (AppLang.Raw bên dưới). Cứ ~0.4 giây app quét mọi cửa sổ đang mở,
//      nhãn / nút / tab / menu / gợi ý nào có trong bảng thì đổi chữ. Muốn dịch thêm: thêm 1 dòng  "tiếng Việt¦English"  vào bảng.
//      Không dịch: nội dung người dùng (tên đối tượng, chữ trong ô nhập), các ô chọn (ComboBox) và thông báo hiện lúc chạy.
//    - Giao diện tối: đổi màu nền / chữ theo quy tắc chung (màu đậm như xanh, đỏ giữ nguyên), thanh tiêu đề tối, menu tối.
//      Canvas vẽ và danh sách đối tượng tự vẽ giữ nền sáng.
//  Gọi InitSettings() 1 lần: đã gắn cuối InitWorkspaceLayout() (Form1_Layout.cs).
// =====================================================================
namespace test01_1014
{
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    public sealed class AppSettings
    {
        public string Language { get; set; } = "vi";   // "vi" | "en"
        public string Theme { get; set; } = "light";   // "light" | "dark"
    }

    // ------------------------------------------------------------------
    //  Bảng dịch Việt -> Anh
    // ------------------------------------------------------------------
    internal static class AppLang
    {
        private static Dictionary<string, string>? map;

        public static string Tr(string vi)
        {
            if (string.IsNullOrWhiteSpace(vi)) return vi;
            map ??= Build();
            string t = vi.Trim();
            if (map.TryGetValue(t, out var e)) return vi.Replace(t, e);
            int c = t.IndexOf(':');                                   // "Khổ in: 400 × 400 mm" -> "Print area: 400 × 400 mm"
            if (c > 0 && map.TryGetValue(t[..c], out var e2)) return vi.Replace(t[..c], e2);
            return vi;
        }

        private static Dictionary<string, string> Build()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in Raw.Split('\n'))
            {
                int i = line.IndexOf('¦');
                if (i <= 0) continue;
                string k = line[..i].Trim(), v = line[(i + 1)..].Trim();
                if (k.Length > 0 && v.Length > 0) d[k] = v;           // trùng khóa thì lấy dòng sau
            }
            return d;
        }

        private const string Raw = @"
Tệp¦File
Mới¦New
Mở...¦Open...
Lưu¦Save
Lưu thành...¦Save as...
Nhập Vector (SVG, DXF)...¦Import vector (SVG, DXF)...
Xuất G-code / Mô phỏng...¦Export G-code / Simulate...
Xuất G-code / Mô phỏng…¦Export G-code / Simulate…
Cài đặt...¦Settings...
Thoát¦Exit
Chỉnh sửa¦Edit
Xem¦View
Chia sẻ¦Share
Bản gốc¦Original
Đã xử lý¦Processed
Đường laser¦Laser path
Bản vẽ¦Drawing
Lưới¦Grid
Thước¦Ruler
Hoàn tác¦Undo
Làm lại¦Redo
Hoàn tác (Ctrl+Z)¦Undo (Ctrl+Z)
Làm lại (Ctrl+Y)¦Redo (Ctrl+Y)
Sao chép¦Copy
Cắt¦Cut
Dán¦Paste
Xóa¦Delete
Chèn¦Insert
Hình¦Shape
Đường thẳng¦Line
Hình chữ nhật¦Rectangle
Hình tròn¦Circle
Chữ¦Text
Ảnh¦Image
Sắp xếp¦Arrange
Khổ in¦Print area
Máy¦Machine
Vật liệu¦Material
Căn giữa / Căn chỉnh¦Align
Căn trái¦Align left
Căn phải¦Align right
Căn trên¦Align top
Căn dưới¦Align bottom
Căn giữa theo chiều ngang¦Center horizontally
Căn giữa theo chiều dọc¦Center vertically
Căn giữa vùng khắc¦Center in work area
Phân phối đều theo chiều ngang¦Distribute horizontally
Phân phối đều theo chiều dọc¦Distribute vertically
Nhóm (Group)¦Group
Rã nhóm (Ungroup)¦Ungroup
Nhóm (Ctrl+G)¦Group (Ctrl+G)
Rã nhóm (Ctrl+Shift+G)¦Ungroup (Ctrl+Shift+G)
Lật¦Flip
Lật ngang (trái ↔ phải)¦Flip horizontal (left ↔ right)
Lật dọc (trên ↔ dưới)¦Flip vertical (top ↔ bottom)
Lật ngang (Ctrl+Shift+H)¦Flip horizontal (Ctrl+Shift+H)
Lật dọc (Ctrl+Shift+V)¦Flip vertical (Ctrl+Shift+V)
Xoay 90° phải¦Rotate 90° right
Xoay 90° trái¦Rotate 90° left
Xoay 90° phải (Ctrl+R)¦Rotate 90° right (Ctrl+R)
Xoay 90° trái (Ctrl+Shift+R)¦Rotate 90° left (Ctrl+Shift+R)
Xoay theo góc...¦Rotate by angle...
Xoay theo góc¦Rotate by angle
Xoay¦Rotate
Thứ tự chồng¦Stacking order
Đưa lên trên cùng¦Bring to front
Đưa xuống dưới cùng¦Send to back
Đưa lên một tầng¦Bring forward
Đưa xuống một tầng¦Send backward
Đưa lên trên cùng (Ctrl+Shift+])¦Bring to front (Ctrl+Shift+])
Đưa xuống dưới cùng (Ctrl+Shift+[)¦Send to back (Ctrl+Shift+[)
Đưa lên một tầng (Ctrl+])¦Bring forward (Ctrl+])
Đưa xuống một tầng (Ctrl+[)¦Send backward (Ctrl+[)
Chuyển thành đường dẫn (Path)¦Convert to path
Tạo mảng (Array)...¦Create array...
Tạo mảng bản sao¦Create copy array
Tạo mảng bản sao (Ctrl+Shift+A)¦Create copy array (Ctrl+Shift+A)
Hàn liền đường cắt / chữ (W)¦Weld cut lines / text (W)
Hàn liền đường cắt / chữ (Weld)¦Weld cut lines / text
Hàn liền đường cắt (Weld)¦Weld cut lines
Hàn gộp thành đường cắt (Phím W)¦Weld into cut path (key W)
Chọn / di chuyển (V)¦Select / move (V)
Sửa điểm nút / đường dẫn (N)¦Edit nodes / path (N)
Sửa điểm nút / đường dẫn (Phím N)¦Edit nodes / path (key N)
Sửa điểm nút (Node Edit)¦Edit nodes
Bo tròn đỉnh đang chọn¦Round selected corner
Bo tròn đỉnh đang chọn (Phím S)¦Round selected corner (key S)
Đường thẳng (L)¦Line (L)
Hình chữ nhật (R)¦Rectangle (R)
Hình tròn / elip (E)¦Circle / ellipse (E)
Đa giác (P)¦Polygon (P)
Chữ (T)¦Text (T)
Chèn ảnh (I)¦Insert image (I)
Tạo mã vạch / QR (B)¦Create barcode / QR (B)
Tạo mã vạch / QR¦Create barcode / QR
Chèn mã vạch / QR...¦Insert barcode / QR...
Sửa mã vạch / QR¦Edit barcode / QR
Mã vạch¦Barcode
Tách nét Vector (Potrace)...¦Trace to vector (Potrace)...
Tách nét Vector / Potrace (Cắt viền)…¦Trace to vector / Potrace (cut outline)…
Thông số¦Properties
Đối tượng¦Objects
Lớp¦Layer
Lớp khắc¦Laser layer
LỚP KHẮC (LAYER)¦LASER LAYER
▾  LỚP KHẮC (LAYER)¦▾  LASER LAYER
+ Thêm lớp¦+ Add layer
Xóa lớp cuối¦Delete last layer
Xóa lớp khắc¦Delete layer
Chọn cả lớp¦Select whole layer
Chọn tất cả¦Select all
Đổi màu lớp¦Change layer color
Cập nhật từ lớp¦Update from layer
Áp dụng cho lớp đang chọn¦Apply to selected layer
Gán vào lớp¦Assign to layer
Thuộc lớp¦Belongs to layer
VỊ TRÍ & KÍCH THƯỚC (mm) · gốc = góc trên-trái¦POSITION & SIZE (mm) · origin = top-left
Rộng¦Width
Cao¦Height
Rộng (nhóm)¦Width (group)
Cao (nhóm)¦Height (group)
X (nhóm)¦X (group)
Y (nhóm)¦Y (group)
X (bao)¦X (bounds)
Y (bao)¦Y (bounds)
Xoay (°)¦Rotate (°)
Điểm đầu X¦Start X
Điểm đầu Y¦Start Y
Điểm cuối X¦End X
Điểm cuối Y¦End Y
Chiều dài¦Length
Góc (°, 0 = sang phải)¦Angle (°, 0 = right)
CHỮ¦TEXT
Nội dung¦Content
Font¦Font
Kiểu¦Style
Cỡ chữ (pt)¦Font size (pt)
Font 1 nét (Hershey - khắc nhanh)¦Single-stroke font (Hershey - fast engraving)
Hàn liền nét chữ giao nhau (Weld)¦Weld overlapping letters
Uốn cong R (mm, 0=thẳng)¦Curve radius R (mm, 0 = straight)
Giãn chữ (mm)¦Letter spacing (mm)
ĐƯỜNG DẪN & ĐIỂM NÚT (NODE / WELD)¦PATH & NODES (NODE / WELD)
MÃ VẠCH / QR¦BARCODE / QR
Nội dung / loại mã¦Content / code type
Sửa nội dung / loại mã…¦Edit content / code type…
Sửa nội dung / loại mã¦Edit content / code type
KHẮC ẢNH¦IMAGE ENGRAVING
Chế độ¦Mode
Ngưỡng (0-255)¦Threshold (0-255)
Đảo màu (khắc vùng sáng)¦Invert (engrave light areas)
TÔ KÍN (FILL) - RIÊNG ĐỐI TƯỢNG¦FILL - PER OBJECT
Cách dòng (mm)¦Line spacing (mm)
Góc quét (°)¦Scan angle (°)
Quét 2 chiều¦Bidirectional scan
Quét chéo (thêm lượt vuông góc)¦Cross-hatch (extra perpendicular pass)
LASER¦LASER
Công suất (%)¦Power (%)
Tốc độ F (mm/phút)¦Speed F (mm/min)
Số lần chạy¦Passes
Xuất G-code / Mô phỏng…¦Export G-code / Simulate…
Xóa đối tượng¦Delete object
Chưa chọn đối tượng.¦No object selected.
XỬ LÝ ẢNH (XEM TRƯỚC)¦IMAGE PROCESSING (PREVIEW)
Đảo màu (âm bản)¦Invert (negative)
Lên¦Up
Xuống¦Down
Trên cùng¦Top
Dưới cùng¦Bottom
Hiện hết¦Show all
Mở khóa hết¦Unlock all
Đổi tên¦Rename
Ẩn¦Hide
Hiện¦Show
Khóa¦Lock
Mở khóa¦Unlock
Điều khiển máy¦Machine control
Điều khiển máy...¦Machine control...
Kết nối & vị trí đầu laser¦Connection & laser head position
Kết nối & vị trí đầu laser (mm)¦Connection & laser head position (mm)
Chạy công việc¦Run job
Gốc công việc (Bắt đầu từ)¦Job origin (Start from)
Di chuyển đầu laser¦Jog laser head
Kết nối¦Connect
Ngắt kết nối¦Disconnect
Ngắt kết nối máy¦Disconnect machine
Làm mới¦Refresh
Tự kết nối lại¦Auto reconnect
Chưa kết nối¦Not connected
Sẵn sàng¦Ready
Đang chạy¦Running
Tạm dừng¦Pause
Tiếp tục¦Resume
Dừng¦Stop
Bắt đầu¦Start
Chạy khung¦Run frame
Chạy khung (căn vị trí)¦Run frame (positioning)
Chạy file G-code...¦Run G-code file...
Mở bảng Điều khiển¦Open control panel
Đang Home¦Homing
Đang di chuyển¦Jogging
Cửa mở¦Door open
Ngủ¦Sleep
Chế độ kiểm tra¦Check mode
BÁO ĐỘNG¦ALARM
Tuyệt đối (gốc máy)¦Absolute (machine origin)
Vị trí hiện tại¦Current position
Gốc người dùng¦User origin
Đặt gốc tại đầu laser¦Set origin at laser head
Gốc¦Origin
Liên tục (giữ chuột)¦Continuous (hold mouse)
Mở khóa ($X)¦Unlock ($X)
Gửi¦Send
Xóa log¦Clear log
Console (gõ lệnh rồi Enter; ? ! ~ gửi tức thời)¦Console (type a command + Enter; ? ! ~ are sent instantly)
Góc trên-trái¦Top-left corner
Giữa cạnh trên¦Top edge center
Góc trên-phải¦Top-right corner
Giữa cạnh trái¦Left edge center
Tâm khung sản phẩm¦Product frame center
Giữa cạnh phải¦Right edge center
Góc dưới-trái¦Bottom-left corner
Giữa cạnh dưới¦Bottom edge center
Góc dưới-phải¦Bottom-right corner
Cài đặt khổ in¦Print area settings
Cài đặt khổ in...¦Print area settings...
Hiện khung vùng khắc + dấu gốc toạ độ¦Show work area frame + origin mark
Hiện vùng khắc trên canvas¦Show work area on canvas
Hiện khung đèn đỏ¦Show red-light frame
Kích thước thật 1:1¦Actual size 1:1
Thu phóng vừa vùng khắc¦Zoom to work area
Hiệu chuẩn tỉ lệ màn hình...¦Calibrate screen scale...
Xuất G-code khung đèn đỏ...¦Export red-light frame G-code...
Thư viện vật liệu...¦Material library...
Lưu lớp hiện tại thành vật liệu...¦Save current layer as material...
Lưu lớp đang chọn...¦Save selected layer...
Thư viện vật liệu¦Material library
Sửa vật liệu¦Edit material
Tên vật liệu¦Material name
Nhân bản¦Duplicate
Sửa...¦Edit...
Nhập...¦Import...
Xuất...¦Export...
Tìm:¦Search:
Đóng¦Close
Hủy¦Cancel
Hủy (Esc)¦Cancel (Esc)
Có¦Yes
Không¦No
Tạo¦Create
Tạo ở giữa¦Create in center
Tạo ở giữa (Enter)¦Create in center (Enter)
Đặt lại mặc định¦Reset to default
Áp dụng¦Apply
Công suất¦Power
Tốc độ¦Speed
Tốc độ F¦Speed F
Số lần¦Passes
Độ dày (mm, 0 = bỏ trống)¦Thickness (mm, 0 = blank)
Khắc¦Engrave
Cắt¦Cut
Đánh dấu¦Mark
Khắc ảnh¦Image engraving
Ghi chú¦Notes
Đường viền¦Outline
Tô kín¦Filled
Tô kín + viền¦Filled + outline
Tô kín (Fill)¦Fill
Đường viền (Line)¦Line
Chế độ khắc¦Engraving mode
Theo cài đặt G-code¦Per G-code settings
Ảnh xám (nhiều mức)¦Grayscale (multi-level)
Đen trắng (1-bit)¦Black & white (1-bit)
Hiện chữ dưới mã¦Show text under code
Loại mã¦Code type
Số bản (gồm bản gốc)¦Copies (incl. original)
Số cột¦Columns
Số hàng¦Rows
Tạo mảng¦Create array
Áp dụng 3 thông số cho TẤT CẢ¦Apply 3 settings to ALL
ĐỐI TƯỢNG (chọn để chỉnh riêng):¦OBJECTS (select to edit individually):
THÔNG SỐ CHUNG:¦GENERAL SETTINGS:
mm trên mỗi pixel canvas¦mm per canvas pixel
S tối đa của firmware¦Firmware max S
Bước dòng quét ảnh (mm)¦Image scan line step (mm)
Ngưỡng đen/trắng (0-255)¦Black/white threshold (0-255)
Tốc độ G0 để ước tính (mm/phút)¦G0 speed for estimate (mm/min)
Ảnh đen/trắng (1-bit)¦Black & white image (1-bit)
Đảo ảnh (khắc phần sáng)¦Invert image (engrave light parts)
Tối ưu đường chạy G0 (TSP / Nearest)¦Optimize G0 travel (TSP / Nearest)
Cắt lỗ trong trước, viền ngoài sau¦Cut inner holes first, outer outline last
Xem kiểu giấy (đen = vết khắc)¦Paper view (black = burn)
Hiện đường di chuyển nhanh (G0)¦Show rapid moves (G0)
Bôi chọn dòng lệnh đang chạy¦Highlight running G-code line
Fit view¦Fit view
Lưu G-code...¦Save G-code...
Lưu G-code¦Save G-code
Tốc độ sim:¦Sim speed:
▶ Chạy mô phỏng¦▶ Run simulation
▶ Chạy tiếp¦▶ Continue
▶ Chạy lại¦▶ Run again
⏸ Tạm dừng¦⏸ Pause
⏹ Hiện kết quả¦⏹ Show result
Cài đặt¦Settings
Ngôn ngữ¦Language
Giao diện¦Appearance
OK¦OK
Ngôn ngữ áp dụng cho menu, nhãn và nút. Thông báo hiện lúc chạy và danh sách trong ô chọn vẫn là tiếng Việt.¦Language applies to menus, labels and buttons. Run-time messages and dropdown lists stay in Vietnamese.
Giao diện tối đổi màu nền và chữ của các cửa sổ. Canvas vẽ và danh sách đối tượng giữ nền sáng.¦Dark mode recolors windows. The drawing canvas and the object list keep a light background.
";
    }

    // ------------------------------------------------------------------
    //  Giao diện sáng / tối (đổi màu theo quy tắc, nhớ màu gốc để trả lại)
    // ------------------------------------------------------------------
    internal static class AppTheme
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? subIdList);

        private sealed class ColEnt { public bool Init; public Color OB, OF, LB, LF; public bool BtnVs; public FlatStyle BtnFlat; public string Native = ""; }
        private sealed class TxtEnt { public string Orig = "", Last = ""; }
        private sealed class FormEnt { public string Mode = ""; }

        private static readonly ConditionalWeakTable<Control, ColEnt> cols = new();
        private static readonly ConditionalWeakTable<object, TxtEnt> txts = new();
        private static readonly ConditionalWeakTable<Form, FormEnt> forms = new();

        private static readonly Color D0 = Color.FromArgb(37, 37, 38), D1 = Color.FromArgb(50, 50, 52), D2 = Color.FromArgb(64, 64, 66);
        private static readonly Color F0 = Color.FromArgb(235, 235, 235), F1 = Color.FromArgb(180, 180, 180);

        private static double Lum(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        private static bool Grey(Color c, int tol) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= tol;

        private static Color MapBack(Color c)
        {
            if (c.A == 0 || !Grey(c, 30)) return c;                      // trong suốt / màu đậm (xanh, đỏ...) giữ nguyên
            double l = Lum(c);
            return l >= 0.9 ? D0 : l >= 0.75 ? D1 : l >= 0.55 ? D2 : c;
        }

        private static Color MapFore(Color c)
        {
            if (c.A == 0 || !Grey(c, 40)) return c;
            double l = Lum(c);
            return l < 0.3 ? F0 : l < 0.6 ? F1 : c;
        }

        // Màu đã bị chuyển sang tối bởi chính mình (control tạo sau, thừa hưởng màu nền tối của cha) -> khi về sáng trả lại màu hệ thống
        private static Color Unmap(Color back) => back == D0 || back == D1 || back == D2 ? SystemColors.Control : back;
        private static Color UnmapFore(Color f) => f == F0 || f == F1 ? SystemColors.ControlText : f;

        private static bool Skip(Control c) =>
            c is PictureBox || c is ProgressBar || c is RichTextBox || c is TrackBar ||
            c.GetType().Name is "GCodeCanvas" or "ObjListBox" or "CanvasBox";

        public static void ApplyAll(bool dark, bool english)
        {
            foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
            {
                if (f.IsDisposed) continue;
                ApplyForm(f, dark, english);
            }
        }

        private static void ApplyForm(Form f, bool dark, bool english)
        {
            string mode = dark ? "dark" : "light";
            var fe = forms.GetOrCreateValue(f);
            if (f.IsHandleCreated && fe.Mode != mode)
            {
                int v = dark ? 1 : 0;
                try { if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4); } catch { }
                fe.Mode = mode;
            }
            Walk(f, dark, english);
        }

        private static void Walk(Control c, bool dark, bool english)
        {
            try
            {
                if (!Skip(c)) { Theme(c, dark); Translate(c, english); }
                if (c is ToolStrip ts) foreach (ToolStripItem it in ts.Items) WalkItem(it, english);
                if (c.ContextMenuStrip != null) foreach (ToolStripItem it in c.ContextMenuStrip.Items) WalkItem(it, english);
            }
            catch { /* một control lỗi không làm hỏng cả lượt quét */ }
            foreach (Control ch in c.Controls) Walk(ch, dark, english);
        }

        private static void WalkItem(ToolStripItem it, bool english)
        {
            TranslateItem(it, english);
            if (it is ToolStripDropDownItem dd) foreach (ToolStripItem sub in dd.DropDownItems) WalkItem(sub, english);
        }

        // ---------------- dịch ----------------
        private static bool Translatable(Control c) =>
            c is Label || c is ButtonBase || c is GroupBox || c is TabPage || c is Form;

        private static void Translate(Control c, bool english)
        {
            if (!Translatable(c) || c.GetType().Name == "Form1") return;
            string cur = c.Text;
            var e = txts.GetOrCreateValue(c);
            if (string.IsNullOrEmpty(e.Orig) || cur != e.Last) e.Orig = cur;       // app vừa tự đổi chữ -> coi là chữ gốc mới
            string want = english ? AppLang.Tr(e.Orig) : e.Orig;
            if (c.Text != want) c.Text = want;
            e.Last = c.Text;
        }

        private static void TranslateItem(ToolStripItem it, bool english)
        {
            if (it is ToolStripSeparator) return;
            var e = txts.GetOrCreateValue(it);
            string cur = it.Text;
            if (string.IsNullOrEmpty(e.Orig) || cur != e.Last) e.Orig = cur;
            string want = english ? AppLang.Tr(e.Orig) : e.Orig;
            if (it.Text != want) it.Text = want;
            e.Last = it.Text;
        }

        // ---------------- màu ----------------
        private static void Theme(Control c, bool dark)
        {
            var e = cols.GetOrCreateValue(c);
            if (!e.Init)
            {
                e.Init = true; e.OB = e.LB = c.BackColor; e.OF = e.LF = c.ForeColor;
                if (c is Button b0) { e.BtnVs = b0.UseVisualStyleBackColor; e.BtnFlat = b0.FlatStyle; }
            }
            else
            {
                if (c.BackColor != e.LB) e.OB = c.BackColor;       // app tự đổi màu sau lần quét trước (vd: nút Bắt đầu xanh / xám)
                if (c.ForeColor != e.LF) e.OF = c.ForeColor;
            }

            Color ob = dark ? e.OB : Unmap(e.OB), of = dark ? e.OF : UnmapFore(e.OF);
            Color tb, tf;
            if (c is Button && dark && Grey(ob, 30) && Lum(ob) >= 0.55) tb = D2;             // nút thường: xám đậm hơn nền một chút
            else tb = dark ? MapBack(ob) : ob;
            tf = dark ? MapFore(of) : of;

            if (c.BackColor != tb) c.BackColor = tb;
            if (c.ForeColor != tf) c.ForeColor = tf;
            e.LB = c.BackColor; e.LF = c.ForeColor;

            if (c is Button b)
            {
                if (dark && Grey(tb, 30) && b.FlatStyle != FlatStyle.Flat)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Color.FromArgb(100, 100, 105);
                    b.FlatAppearance.BorderSize = 1;
                }
                else if (!dark && b.FlatStyle != e.BtnFlat)
                {
                    b.FlatStyle = e.BtnFlat;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.BorderColor = Color.Empty;
                }
                if (!dark) b.UseVisualStyleBackColor = e.BtnVs;                              // đặt BackColor làm mất cờ này nên trả lại sau cùng
            }

            string native = dark ? "dark" : "light";
            if (c.IsHandleCreated && e.Native != native && (c is ScrollableControl || c is ComboBox || c is TextBoxBase || c is UpDownBase || c is ListControl))
            {
                try { SetWindowTheme(c.Handle, dark ? (c is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer") : null, null); } catch { }
                e.Native = native;
            }
        }

        // ---------------- menu / thanh công cụ ----------------
        public static void SetRenderer(bool dark)
        {
            if (dark) ToolStripManager.Renderer = new DarkRenderer();
            else ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => D0;
            public override Color MenuBorder => Color.FromArgb(80, 80, 84);
            public override Color MenuItemBorder => Color.FromArgb(90, 90, 95);
            public override Color MenuItemSelected => Color.FromArgb(70, 70, 74);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(70, 70, 74);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(70, 70, 74);
            public override Color MenuItemPressedGradientBegin => D2;
            public override Color MenuItemPressedGradientMiddle => D2;
            public override Color MenuItemPressedGradientEnd => D2;
            public override Color MenuStripGradientBegin => D1;
            public override Color MenuStripGradientEnd => D1;
            public override Color ImageMarginGradientBegin => D0;
            public override Color ImageMarginGradientMiddle => D0;
            public override Color ImageMarginGradientEnd => D0;
            public override Color SeparatorDark => Color.FromArgb(85, 85, 88);
            public override Color SeparatorLight => Color.FromArgb(85, 85, 88);
            public override Color ToolStripBorder => Color.FromArgb(80, 80, 84);
            public override Color ToolStripGradientBegin => D1;
            public override Color ToolStripGradientMiddle => D1;
            public override Color ToolStripGradientEnd => D1;
            public override Color StatusStripGradientBegin => D1;
            public override Color StatusStripGradientEnd => D1;
            public override Color ButtonSelectedGradientBegin => Color.FromArgb(70, 70, 74);
            public override Color ButtonSelectedGradientMiddle => Color.FromArgb(70, 70, 74);
            public override Color ButtonSelectedGradientEnd => Color.FromArgb(70, 70, 74);
            public override Color ButtonSelectedBorder => Color.FromArgb(90, 90, 95);
        }

        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? F0 : Color.FromArgb(120, 120, 120);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = F0;
                base.OnRenderArrow(e);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Hộp thoại Cài đặt
    // ------------------------------------------------------------------
    internal sealed class SettingsForm : Form
    {
        private readonly ComboBox cbLang, cbTheme;
        private readonly Action<string, string> apply;

        public SettingsForm(AppSettings cur, Action<string, string> apply)
        {
            this.apply = apply;
            AutoScaleDimensions = new System.Drawing.SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Bold);
            Text = "Cài đặt";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(430, 270);

            Controls.Add(new Label { Text = "Ngôn ngữ", Left = 16, Top = 22, Width = 130, Height = 24, TextAlign = ContentAlignment.MiddleLeft });
            cbLang = new ComboBox { Left = 150, Top = 20, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cbLang.Items.AddRange(new object[] { "Tiếng Việt", "English" });
            cbLang.SelectedIndex = cur.Language == "en" ? 1 : 0;
            Controls.Add(cbLang);

            Controls.Add(new Label { Text = "Giao diện", Left = 16, Top = 62, Width = 130, Height = 24, TextAlign = ContentAlignment.MiddleLeft });
            cbTheme = new ComboBox { Left = 150, Top = 60, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cbTheme.Items.AddRange(new object[] { "Sáng (Light)", "Tối (Dark)" });
            cbTheme.SelectedIndex = cur.Theme == "dark" ? 1 : 0;
            Controls.Add(cbTheme);

            Controls.Add(new Label
            {
                Text = "Ngôn ngữ áp dụng cho menu, nhãn và nút. Thông báo hiện lúc chạy và danh sách trong ô chọn vẫn là tiếng Việt.",
                Left = 16, Top = 104, Width = 394, Height = 56, ForeColor = Color.DimGray, Font = new System.Drawing.Font("Segoe UI", 8.5f, FontStyle.Bold)
            });
            Controls.Add(new Label
            {
                Text = "Giao diện tối đổi màu nền và chữ của các cửa sổ. Canvas vẽ và danh sách đối tượng giữ nền sáng.",
                Left = 16, Top = 164, Width = 394, Height = 40, ForeColor = Color.DimGray, Font = new System.Drawing.Font("Segoe UI", 8.5f, FontStyle.Bold)
            });

            var ok = new Button { Text = "OK", Left = 150, Top = 222, Width = 80, Height = 32, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Hủy", Left = 238, Top = 222, Width = 80, Height = 32, DialogResult = DialogResult.Cancel };
            var ap = new Button { Text = "Áp dụng", Left = 326, Top = 222, Width = 84, Height = 32 };
            ok.Click += (s, e) => Commit();
            ap.Click += (s, e) => Commit();
            Controls.AddRange(new Control[] { ok, cancel, ap });
            AcceptButton = ok; CancelButton = cancel;
        }

        private void Commit() => apply(cbLang.SelectedIndex == 1 ? "en" : "vi", cbTheme.SelectedIndex == 1 ? "dark" : "light");
    }

    // ------------------------------------------------------------------
    //  Gắn vào Form1
    // ------------------------------------------------------------------
    public partial class Form1
    {
        private AppSettings appSettings = new();
        private System.Windows.Forms.Timer? uiTimer;
        private bool uiPending;

        private static string AppSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "settings.json");

        private void InitSettings()
        {
            if (uiTimer != null) return;
            try
            {
                if (File.Exists(AppSettingsPath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettingsPath));
                    if (s != null) appSettings = new AppSettings { Language = s.Language == "en" ? "en" : "vi", Theme = s.Theme == "dark" ? "dark" : "light" };
                }
            }
            catch { /* file hỏng thì dùng mặc định */ }

            // Mục "Cài đặt..." trong menu Tệp, ngay trên dòng kẻ + Thoát
            var items = fiieToolStripMenuItem.DropDownItems;
            var mi = new ToolStripMenuItem("Cài đặt...") { ShortcutKeys = Keys.Control | Keys.Oemcomma };
            mi.Click += (s, e) => ShowSettings();
            int exit = -1;
            for (int i = 0; i < items.Count; i++) if (items[i].Text == "Thoát") { exit = i; break; }
            if (exit < 0) { items.Add(new ToolStripSeparator()); items.Add(mi); }
            else
            {
                int at = exit > 0 && items[exit - 1] is ToolStripSeparator ? exit - 1 : exit;
                items.Insert(at, new ToolStripSeparator());
                items.Insert(at + 1, mi);
            }

            ApplyAppSettings(false);
            uiTimer = new System.Windows.Forms.Timer { Interval = 400 };
            uiTimer.Tick += (s, e) =>
            {
                bool active = appSettings.Theme == "dark" || appSettings.Language == "en";
                if (active || uiPending) { AppTheme.ApplyAll(appSettings.Theme == "dark", appSettings.Language == "en"); uiPending = false; }
            };
            uiTimer.Start();
        }

        private void ShowSettings()
        {
            using var dlg = new SettingsForm(appSettings, (lang, theme) =>
            {
                appSettings.Language = lang; appSettings.Theme = theme;
                ApplyAppSettings(true);
            });
            dlg.ShowDialog(this);
        }

        private void ApplyAppSettings(bool save)
        {
            bool dark = appSettings.Theme == "dark";
            AppTheme.SetRenderer(dark);
            AppTheme.ApplyAll(dark, appSettings.Language == "en");
            uiPending = true;                       // quét thêm 1 lượt cho các control tạo ra ngay sau đó
            menuStrip1.Invalidate();
            if (save)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsPath)!);
                    File.WriteAllText(AppSettingsPath, JsonSerializer.Serialize(appSettings, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }
    }
}
