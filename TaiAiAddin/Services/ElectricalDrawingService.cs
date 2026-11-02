using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;

using Autodesk.AutoCAD.Colors;

using Autodesk.AutoCAD.DatabaseServices;

using Autodesk.AutoCAD.EditorInput;

using Autodesk.AutoCAD.Geometry;

using TaiAiAddin.Models;

namespace TaiAiAddin.Services

{

    public class ElectricalDrawingService

    {

        private const string LayerBorder = "E-BORDER";

        private const string LayerSchematic = "E-SCHEMATIC";

        private const string LayerBusbar = "E-BUSBAR";

        private const string LayerText = "E-TEXT";

        private const string LayerLoad3P = "E-LOAD-3P";

        private const string LayerLoad1P = "E-LOAD-1P";

        private const string LayerControl = "E-CONTROL";

        private const string LayerMetering = "E-METERING";

        private ObjectIdCollection _currentCabinetEntities = new ObjectIdCollection();

        /// <summary>

        /// Thông tin 2 điểm Point kết nối của Block MCCB

        /// </summary>

        public class BlockConnectionInfo

        {

            public bool HasPoints { get; set; }

            public Point3d Point1 { get; set; }

            public Point3d Point2 { get; set; }

            public double Distance { get; set; }

            public double RotationAngle { get; set; }

            public bool HasAttributes { get; set; }

            public List<string> AttributeTags { get; set; }

            public BlockConnectionInfo()

            {

                AttributeTags = new List<string>();

            }

        }

        /// <summary>

        /// Vẽ toàn bộ Sơ đồ nguyên lý tủ điện DẠNG DỌC với tham số tinh chỉnh kích thước từ Preview

        /// và các tùy chọn bật/tắt thiết bị phụ trợ (Cầu chì, Đèn báo pha, Relay, MCT, SPD, Spare)

        /// </summary>

        public bool DrawSingleLineDiagram(Document doc, ElectricalPanel panel, Point3d origin, DrawingParameters p = null)

        {

            if (doc == null || panel == null) return false;

            if (p == null) p = new DrawingParameters();

            if (p.LayoutScheme != LayoutScheme.LayoutBasic)
            {
                return PanelTreeDrawerService.DrawTreeDiagram(doc, panel, origin, p);
            }

            Database db = doc.Database;

            Editor ed = doc.Editor;

            _currentCabinetEntities.Clear();

            using (doc.LockDocument())

            {

                // Tải trước tất cả Block vào bản vẽ trước khi mở Transaction chính

                PreloadAllBlocks(db);

                using (Transaction tr = db.TransactionManager.StartTransaction())

                {

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Khởi tạo các Layer chuẩn màu

                EnsureLayer(db, tr, LayerBorder, 7);

                EnsureLayer(db, tr, LayerSchematic, 7);

                EnsureLayer(db, tr, LayerBusbar, 7);

                EnsureLayer(db, tr, LayerText, 7);

                EnsureLayer(db, tr, LayerLoad3P, 1);    // Màu 1: Đỏ (3 Pha)

                EnsureLayer(db, tr, LayerLoad1P, 3);    // Màu 3: Xanh lá (1 Pha)

                EnsureLayer(db, tr, LayerControl, 3);   // Màu 3: Xanh lá (Dây đo lường & Rơ le)

                EnsureLayer(db, tr, LayerMetering, 4);  // Màu 4: Cyan (Cầu chì, Đồng hồ PM)

                // 1. KÍCH THƯỚC HÌNH HỌC THEO THAM SỐ TINH CHỈNH & TỰ ĐỘNG CO DÃN

                if (p.AutoFitSpacing)

                {

                    p.AutoFitForPanel(panel);

                }

                double colW = p.ColWidth;

                double hdrMainW = p.HeaderMainWidth;

                double hdrSubW = p.HeaderSubWidth;

                double totalHdrW = p.TotalHeaderWidth;

                double hName = p.RowNameHeight;

                double hCircuit = p.RowCircuitHeight;

                double hPower = p.RowPowerHeight;

                double hCableZone = (p.RowCableHeight > 0) ? p.RowCableHeight : 85.0;

                double totalTableH = hName + hCircuit + hPower;

                int loadCount = panel.Loads.Count;

                int nSpareCols = p.IncludeSpare ? ((p.SpareCount3P + p.SpareCount1P > 0) ? (p.SpareCount3P + p.SpareCount1P) : Math.Max(1, p.SpareCount)) : 0;

                int totalCols = loadCount + nSpareCols + (p.IncludeSpd ? 1 : 0) + (panel.HasCapacitorBank ? 1 : 0);

                double totalLoadW = totalCols * colW;

                double totalTableW = totalHdrW + totalLoadW;

                double x0 = origin.X;

                double y0 = origin.Y;

                double yTableBottom = y0;

                double yTableTop = yTableBottom + totalTableH;

                // 2. VẼ BẢNG THỐNG KÊ PHÍA DƯỚI (VERTICAL TABLE GRID GỒM CÔNG SUẤT, SỐ MẠCH, PHỤ TẢI)

                DrawVerticalTableGrid(btr, tr, x0, yTableBottom, hdrMainW, hdrSubW, colW, totalCols,

                    hName, hCircuit, hPower, loadCount);

                // Điền dữ liệu text vào bảng

                PopulateTableData(btr, tr, panel, x0, yTableBottom, hdrMainW, hdrSubW, colW,

                    hName, hCircuit, hPower, p.TextSize, p.IncludeSpare, p.IncludeSpd, panel.HasCapacitorBank, p.SpareCount3P, p.SpareCount1P);

                // 3. KHUNG VỎ TỦ ĐIỆN PHÍA TRÊN (ENCLOSURE) - ĐẶT CÁCH MÉP TRÊN BẢNG 1 ĐOẠN CABLE ZONE

                double cabBot = yTableTop + hCableZone;

                double cabH = (p.CabinetHeight > 0) ? p.CabinetHeight : 145.0;

                double cabTop = cabBot + cabH;

                double cabLeft = x0 - 8.0;

                // Chiều rộng vỏ tủ: đảm bảo đủ chỗ cho cả bảng tải và cụm đo lường bên phải

                double minCabRight = x0 + totalHdrW + Math.Min(2.0, (double)loadCount) * colW + 65.0;

                double cabRight = Math.Max(x0 + totalTableW + 28.0, minCabRight);

                Entity cabRect = AddRectangle(btr, tr, new Point3d(cabLeft, cabTop, 0), new Point3d(cabRight, cabBot, 0), LayerBorder, 7);

                // 4. THANH CÁI ĐỒNG CHÍNH (MAIN COPPER BUSBAR)

                double busY = cabBot + 65.0;

                double busStartX = x0 + totalHdrW - 8.0;

                double busEndX = x0 + totalTableW + 8.0;

                AddLine(btr, tr, new Point3d(busStartX, busY, 0), new Point3d(busEndX, busY, 0), LayerBusbar, 7);

                // 5. NẠP VÀ PHÂN TÍCH 2 ĐIỂM POINT KẾT NỐI CỦA BLOCK MCCB & DOL

                ObjectId mccbBlockId = GetOrLoadMccbBlock(db);

                ObjectId dolBlockId = GetOrLoadDolBlock(db);

                BlockConnectionInfo connInfo = AnalyzeBlockConnection(db, tr, mccbBlockId);

                double targetBreakerCenterY = cabBot + 25.0;

                // 6. VẼ CÁC NHÁNH LỘ RA (DÂY NGUỒN CHẠY XUỐNG MÉP TRÊN BẢNG & GHI CHÚ CÁP DỌC THEO DÂY)

                for (int i = 0; i < loadCount; i++)

                {

                    ElectricalLoad load = panel.Loads[i];

                    double fx = x0 + totalHdrW + (i + 0.5) * colW;

                    bool is3P = load.Phase == PhaseType.ThreePhase;

                    string loadLayer = is3P ? LayerLoad3P : LayerLoad1P;

                    int loadColor = is3P ? 1 : 3;

                    AddCircle(btr, tr, new Point3d(fx, busY, 0), 0.8, LayerSchematic);

                    bool isSpareLoad = load.IsSpare || (!string.IsNullOrEmpty(load.LoadName) && (load.LoadName.ToUpper().Contains("SPARE") || load.LoadName.ToUpper().Contains("DỰ PHÒNG")));

                    bool drawWire = !isSpareLoad && load.DrawOutgoingWire;

                    SldInteractiveItem branchItem = (p != null) ? p.FindCustomItem("LOAD_CB_" + i) : null;
                    InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, fx, busY, yTableTop, targetBreakerCenterY, load.BreakerSpec, false, load.Starter, load.Control, loadLayer, loadColor, drawWire, branchItem);

                    // GHI CHÚ CÁP ĐIỆN DỌC THEO DÂY NGUỒN (CHỮ MÀU TRẮNG, 1 HÀNG DUY NHẤT, NẰM BÊN TRÁI DÂY)

                    string cableStr = load.FullCableString;

                    if (drawWire && !string.IsNullOrEmpty(cableStr))

                    {

                        Point3d textStartPt = new Point3d(fx - 1.2, yTableTop + 4.0, 0);

                        AddText(btr, tr, cableStr, textStartPt, p.TextSize > 0 ? p.TextSize : 2.0, AttachmentPoint.BottomLeft, LayerText, Math.PI / 2.0, 7);

                    }

                }

                int currentExtraCol = loadCount;

                // Nhánh Lộ Dự Phòng (nếu được bật - hỗ trợ nhiều lộ 3P & 1P, mặc định không vẽ dây ra từ CB)

                if (p.IncludeSpare)

                {

                    int n3P = p.SpareCount3P;

                    int n1P = p.SpareCount1P;

                    if (n3P + n1P == 0) n3P = Math.Max(1, p.SpareCount);

                    for (int s = 0; s < n3P; s++)

                    {

                        double spareCenterX = x0 + totalHdrW + (currentExtraCol + 0.5) * colW;

                        AddCircle(btr, tr, new Point3d(spareCenterX, busY, 0), 0.8, LayerSchematic);

                        InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spareCenterX, busY, yTableTop, targetBreakerCenterY, "MCCB 3P-25kA 40A", false, StarterType.Direct, ControlMode.None, LayerLoad3P, 1, p.DrawSpareOutgoingWire);

                        if (p.DrawSpareOutgoingWire)

                        {

                            AddText(btr, tr, "Cu/XLPE/PVC 4C-10mm2 + (E) Cu/PVC 1C-10mm2", new Point3d(spareCenterX - 1.2, yTableTop + 4.0, 0), p.TextSize > 0 ? p.TextSize : 2.0, AttachmentPoint.BottomLeft, LayerText, Math.PI / 2.0, 7);

                        }

                        currentExtraCol++;

                    }

                    for (int s = 0; s < n1P; s++)

                    {

                        double spareCenterX = x0 + totalHdrW + (currentExtraCol + 0.5) * colW;

                        AddCircle(btr, tr, new Point3d(spareCenterX, busY, 0), 0.8, LayerSchematic);

                        InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spareCenterX, busY, yTableTop, targetBreakerCenterY, "MCB 1P-6kA 16A", false, StarterType.Direct, ControlMode.None, LayerLoad1P, 3, p.DrawSpareOutgoingWire);

                        if (p.DrawSpareOutgoingWire)

                        {

                            AddText(btr, tr, "Cu/PVC/PVC 2Cx2.5mm2 + E2.5mm2", new Point3d(spareCenterX - 1.2, yTableTop + 4.0, 0), p.TextSize > 0 ? p.TextSize : 2.0, AttachmentPoint.BottomLeft, LayerText, Math.PI / 2.0, 7);

                        }

                        currentExtraCol++;

                    }

                }

                // Nhánh Chống Sét Lan Truyền SPD (nếu được bật)

                if (p.IncludeSpd)

                {

                    double spdCenterX = x0 + totalHdrW + (currentExtraCol + 0.5) * colW;

                    AddCircle(btr, tr, new Point3d(spdCenterX, busY, 0), 0.8, LayerSchematic);

                    double spdBoxY = cabBot + 10.0;

                    InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spdCenterX, busY, spdBoxY + 5.0, targetBreakerCenterY, "MCCB 3P-10kA 40A", false, StarterType.Direct, ControlMode.None, LayerSchematic, 7);

                    // Hộp SPD

                    AddRectangle(btr, tr, new Point3d(spdCenterX - 4.0, spdBoxY + 5.0, 0), new Point3d(spdCenterX + 4.0, spdBoxY - 5.0, 0), LayerSchematic, 7);

                    AddText(btr, tr, "CHỐNG SÉT LAN TRUYỀN", new Point3d(spdCenterX, spdBoxY - 9.0, 0), 2.0, AttachmentPoint.TopCenter, LayerText, 0);

                    currentExtraCol++;

                }

                // Nhánh Tụ Bù Công Suất Phản Kháng (nếu được bật)

                if (panel.HasCapacitorBank)

                {

                    double capCenterX = x0 + totalHdrW + (currentExtraCol + 0.5) * colW;

                    AddCircle(btr, tr, new Point3d(capCenterX, busY, 0), 0.8, LayerSchematic);

                    DrawCapacitorBankBranch(btr, tr, capCenterX, busY, cabBot, panel.CapacitorBankKvar, panel.CapacitorSteps);

                    currentExtraCol++;

                }

                // 7. NHÁNH CẤP NGUỒN TỔNG (INCOMING FEEDER) & CÁC BLOCK ĐO LƯỜNG (CẦU CHÌ, MCT, ĐÈN BÁO PHA, RELAY)

                double incomingX = x0 + totalHdrW + Math.Min(2.0, (double)loadCount) * colW;

                double incomingTop = cabTop + 25.0;

                double blkScale = (p != null && p.MeteringBlockScale > 0) ? p.MeteringBlockScale : 0.1;

                double topBranchY = cabTop - 15.0;

                // Vị trí MCT và Breaker Tổng

                double mctCenterY = busY + 22.0;

                double mainCbCenterY = p.IncludeMct ? ((topBranchY + mctCenterY + 14.0) / 2.0) : ((topBranchY + busY) / 2.0);

                double mccbDist = connInfo.Distance > 0 ? connInfo.Distance : 8.051;

                Point3d worldMccbTop = new Point3d(incomingX, mainCbCenterY + mccbDist / 2.0, 0);

                Point3d worldMccbBot = new Point3d(incomingX, mainCbCenterY - mccbDist / 2.0, 0);

                if (panel.Incomer == IncomerType.DualATS)

                {

                    // NGUỒN KÉP ATS

                    DrawDualAtsIncomer(btr, tr, incomingX, incomingTop, topBranchY, worldMccbTop, panel);

                }

                else if (panel.Incomer == IncomerType.DualInterlock)

                {

                    // NGUỒN KÉP KHÓA LIÊN ĐỘNG

                    DrawDualInterlockIncomer(btr, tr, incomingX, incomingTop, topBranchY, worldMccbTop, panel, p);

                }

                else

                {

                    // NGUỒN ĐƠN LƯỚI TIÊU CHUẨN

                    DrawIncomingArrow(btr, tr, new Point3d(incomingX, incomingTop, 0));

                    AddText(btr, tr, "CẤP NGUỒN TỪ CHỦ ĐẦU TƯ", new Point3d(incomingX, incomingTop + 7.0, 0), 2.8, AttachmentPoint.BottomCenter, LayerText, 0);

                    if (!string.IsNullOrEmpty(panel.MainCableSpec))

                    {

                        AddText(btr, tr, panel.MainCableSpec, new Point3d(incomingX, incomingTop + 2.5, 0), 2.3, AttachmentPoint.BottomCenter, LayerText, 0);

                    }

                    // Dây cấp nguồn đứng:

                    AddLine(btr, tr, new Point3d(incomingX, incomingTop, 0), new Point3d(incomingX, topBranchY, 0), LayerSchematic, 7);

                    // (Wire drawn via AddOrthogonalWire below)

                }

                // Chèn Block MCCB Tổng xoay đứng (Đồng bộ Move, Rotate, Scale từ Preview)
                SldInteractiveItem mainCustom = (p != null) ? p.FindCustomItem("MAIN_BREAKER") : null;
                double mainCadOffX = 0;
                double mainCadOffY = 0;
                double mainCadRot = 0;
                double mainCadScale = 1.0;
                if (mainCustom != null && !mainCustom.IsDeleted)
                {
                    mainCadOffX = mainCustom.OffsetX / 2.8;
                    mainCadOffY = -mainCustom.OffsetY / 2.8;
                    mainCadRot = mainCustom.RotationAngle * Math.PI / 180.0;
                    mainCadScale = mainCustom.Scale;
                }

                Point3d actualMccbCenter = new Point3d(incomingX + mainCadOffX, mainCbCenterY + mainCadOffY, 0);
                Point3d actualMccbTop = new Point3d(actualMccbCenter.X, actualMccbCenter.Y + mccbDist / 2.0 * mainCadScale, 0);
                Point3d actualMccbBot = new Point3d(actualMccbCenter.X, actualMccbCenter.Y - mccbDist / 2.0 * mainCadScale, 0);

                // Dây từ nguồn đỉnh tủ đến ngõ vào MCCB Tổng (Dây trực giao thích ứng)
                AddOrthogonalWire(btr, tr, new Point3d(incomingX, topBranchY, 0), actualMccbTop, LayerSchematic, 7);

                if (mccbBlockId != ObjectId.Null && !mccbBlockId.IsErased)
                {
                    BlockReference mainMccbRef = new BlockReference(actualMccbBot, mccbBlockId);
                    mainMccbRef.Rotation = connInfo.RotationAngle + mainCadRot;
                    mainMccbRef.ScaleFactors = new Scale3d(mainCadScale, mainCadScale, 1.0);
                    mainMccbRef.Layer = LayerSchematic;
                    btr.AppendEntity(mainMccbRef);
                    tr.AddNewlyCreatedDBObject(mainMccbRef, true);
                    _currentCabinetEntities.Add(mainMccbRef.ObjectId);
                }
                else
                {
                    double rectH = 8.0 * mainCadScale;
                    double rectW = 4.0 * mainCadScale;
                    AddRectangle(btr, tr, new Point3d(actualMccbCenter.X - rectW / 2.0, actualMccbCenter.Y + rectH / 2.0, 0),
                        new Point3d(actualMccbCenter.X + rectW / 2.0, actualMccbCenter.Y - rectH / 2.0, 0), LayerSchematic, 7);
                    AddLine(btr, tr, new Point3d(actualMccbCenter.X - rectW / 2.0, actualMccbCenter.Y - rectH / 2.0, 0),
                        new Point3d(actualMccbCenter.X + rectW / 2.0, actualMccbCenter.Y + rectH / 2.0, 0), LayerSchematic, 7);
                }

                // Chữ thông số MCCB Tổng (bên trái)
                string[] mparts = panel.MainBreakerSpec.Split(' ');
                string ml1 = mparts.Length > 0 ? mparts[0] : "MCCB";
                string ml2 = mparts.Length > 1 ? mparts[1] : "";
                string ml3 = mparts.Length > 2 ? mparts[2] : "";

                double mtextX = actualMccbCenter.X - 5.5 * mainCadScale;
                AddText(btr, tr, ml1, new Point3d(mtextX, actualMccbCenter.Y + 3.0 * mainCadScale, 0), 2.0 * mainCadScale, AttachmentPoint.MiddleRight, LayerText, 0);
                AddText(btr, tr, ml2, new Point3d(mtextX, actualMccbCenter.Y, 0), 2.0 * mainCadScale, AttachmentPoint.MiddleRight, LayerText, 0);
                if (!string.IsNullOrEmpty(ml3)) AddText(btr, tr, ml3, new Point3d(mtextX, actualMccbCenter.Y - 3.0 * mainCadScale, 0), 2.0 * mainCadScale, AttachmentPoint.MiddleRight, LayerText, 0);

                // Đoạn 3 & 4: Từ cực dưới MCCB Tổng -> MCT -> Busbar
                if (p.IncludeMct)
                {
                    AddOrthogonalWire(btr, tr, actualMccbBot, new Point3d(incomingX, mctCenterY + 4.0, 0), LayerSchematic, 7);
                    DrawMctRings(btr, tr, new Point3d(incomingX, mctCenterY, 0));
                    AddText(btr, tr, "3xMCT", new Point3d(incomingX - 6.0, mctCenterY, 0), 2.0, AttachmentPoint.MiddleRight, LayerText, 0);
                    AddLine(btr, tr, new Point3d(incomingX, mctCenterY - 4.0, 0), new Point3d(incomingX, busY, 0), LayerSchematic, 7);
                }
                else
                {
                    AddOrthogonalWire(btr, tr, actualMccbBot, new Point3d(incomingX, busY, 0), LayerSchematic, 7);
                }

                AddCircle(btr, tr, new Point3d(incomingX, busY, 0), 0.8, LayerSchematic);

                // 8. CỤM THIẾT BỊ PHỤ TRỢ (CẦU CHÌ, ĐÈN BÁO PHA, RELAY, PM - TƯƠNG TÁC TỰ DO)
                bool hasAccessory = p.IncludeFuse || p.IncludeLamps || p.IncludeRelay || p.IncludeMct;
                if (hasAccessory)
                {
                    AddCircle(btr, tr, new Point3d(incomingX, topBranchY, 0), 0.8, LayerSchematic);
                    double dropX = incomingX + 22.0;

                    // (A) CẦU CHÌ ĐO LƯỜNG (TỰ DO DI CHUYỂN, XOAY, SCALE)
                    if (p.IncludeFuse)
                    {
                        Point3d fusePt1, fusePt2;
                        ObjectId fuseBlockId = GetOrLoadFuseBlock(db, tr, out fusePt1, out fusePt2);
                        bool hasFuseBlock = (fuseBlockId != ObjectId.Null && !fuseBlockId.IsErased);
                        double fuseInX = incomingX + 4.0;
                        double fuseOutX = fuseInX + 10.192;

                        SldInteractiveItem fuseCustom = (p != null) ? p.FindCustomItem("ACCESSORY_FUSE") : null;
                        double fuseCadOffX = 0, fuseCadOffY = 0, fuseCadScale = 1.0, fuseCadRot = 0;
                        if (fuseCustom != null && !fuseCustom.IsDeleted)
                        {
                            fuseCadOffX = fuseCustom.OffsetX / 2.8;
                            fuseCadOffY = -fuseCustom.OffsetY / 2.8;
                            fuseCadScale = fuseCustom.Scale;
                            fuseCadRot = fuseCustom.RotationAngle * Math.PI / 180.0;
                        }

                        Point3d actualFuseIn = new Point3d(fuseInX + fuseCadOffX, topBranchY + fuseCadOffY, 0);
                        Point3d actualFuseOut = new Point3d(fuseOutX + fuseCadOffX, topBranchY + fuseCadOffY, 0);

                        AddOrthogonalWire(btr, tr, new Point3d(incomingX, topBranchY, 0), actualFuseIn, LayerSchematic, 7);

                        if (hasFuseBlock)
                        {
                            double fuseInsX = actualFuseIn.X - fusePt1.X * blkScale * fuseCadScale;
                            double fuseInsY = actualFuseIn.Y - fusePt1.Y * blkScale * fuseCadScale;
                            BlockReference fuseRef = new BlockReference(new Point3d(fuseInsX, fuseInsY, 0), fuseBlockId);
                            fuseRef.ScaleFactors = new Scale3d(blkScale * fuseCadScale, blkScale * fuseCadScale, 1.0);
                            fuseRef.Rotation = fuseCadRot;
                            fuseRef.Layer = LayerMetering;
                            fuseRef.ColorIndex = 4;
                            btr.AppendEntity(fuseRef);
                            tr.AddNewlyCreatedDBObject(fuseRef, true);
                            _currentCabinetEntities.Add(fuseRef.ObjectId);
                        }
                        else
                        {
                            AddRectangle(btr, tr, new Point3d(actualFuseIn.X, actualFuseIn.Y + 2.0 * fuseCadScale, 0), new Point3d(actualFuseOut.X, actualFuseOut.Y - 2.0 * fuseCadScale, 0), LayerMetering, 4);
                            AddText(btr, tr, "3x2A FUSE", new Point3d((actualFuseIn.X + actualFuseOut.X) / 2.0, actualFuseIn.Y + 3.2 * fuseCadScale, 0), 1.8 * fuseCadScale, AttachmentPoint.BottomCenter, LayerMetering, 0, 4);
                        }

                        AddOrthogonalWire(btr, tr, actualFuseOut, new Point3d(dropX, topBranchY, 0), LayerSchematic, 7);
                    }
                    else
                    {
                        AddLine(btr, tr, new Point3d(incomingX, topBranchY, 0), new Point3d(dropX, topBranchY, 0), LayerSchematic, 7);
                    }

                    // (B) ĐÈN BÁO PHA (R, Y, B - TỰ DO DI CHUYỂN, XOAY, SCALE)
                    if (p.IncludeLamps)
                    {
                        Point3d lampInPt;
                        ObjectId lampsBlockId = GetOrLoadLampsBlock(db, tr, out lampInPt);
                        bool hasLampsBlock = (lampsBlockId != ObjectId.Null && !lampsBlockId.IsErased);
                        double lampStartX = dropX + 2.0;

                        SldInteractiveItem lampsCustom = (p != null) ? p.FindCustomItem("ACCESSORY_LAMPS") : null;
                        double lampCadOffX = 0, lampCadOffY = 0, lampCadScale = 1.0, lampCadRot = 0;
                        if (lampsCustom != null && !lampsCustom.IsDeleted)
                        {
                            lampCadOffX = lampsCustom.OffsetX / 2.8;
                            lampCadOffY = -lampsCustom.OffsetY / 2.8;
                            lampCadScale = lampsCustom.Scale;
                            lampCadRot = lampsCustom.RotationAngle * Math.PI / 180.0;
                        }

                        Point3d actualLampStart = new Point3d(lampStartX + lampCadOffX, topBranchY + lampCadOffY, 0);
                        AddOrthogonalWire(btr, tr, new Point3d(dropX, topBranchY, 0), actualLampStart, LayerSchematic, 7);

                        if (hasLampsBlock)
                        {
                            double lampInsX = actualLampStart.X - lampInPt.X * blkScale * lampCadScale;
                            double lampInsY = actualLampStart.Y - lampInPt.Y * blkScale * lampCadScale;
                            BlockReference lampRef = new BlockReference(new Point3d(lampInsX, lampInsY, 0), lampsBlockId);
                            lampRef.ScaleFactors = new Scale3d(blkScale * lampCadScale, blkScale * lampCadScale, 1.0);
                            lampRef.Rotation = lampCadRot;
                            lampRef.Layer = LayerSchematic;
                            btr.AppendEntity(lampRef);
                            tr.AddNewlyCreatedDBObject(lampRef, true);
                            _currentCabinetEntities.Add(lampRef.ObjectId);
                        }
                        else
                        {
                            double[] lampOffsets = new double[] { 3.0 * lampCadScale, 10.0 * lampCadScale, 17.0 * lampCadScale };
                            string[] lampNames = new string[] { "R", "Y", "B" };
                            int[] lampColors = new int[] { 1, 2, 5 };
                            AddLine(btr, tr, actualLampStart, new Point3d(actualLampStart.X + 20.0 * lampCadScale, actualLampStart.Y, 0), LayerSchematic, 7);
                            for (int l = 0; l < 3; l++)
                            {
                                double lx = actualLampStart.X + lampOffsets[l];
                                AddCircle(btr, tr, new Point3d(lx, actualLampStart.Y, 0), 1.8 * lampCadScale, LayerSchematic, lampColors[l]);
                                AddText(btr, tr, lampNames[l], new Point3d(lx, actualLampStart.Y + 3.2 * lampCadScale, 0), 1.8 * lampCadScale, AttachmentPoint.BottomCenter, LayerText, 0, lampColors[l]);
                            }
                        }
                    }

                    // (C) DÂY ĐIỀU KHIỂN & ĐO LƯỜNG MÀU XANH LÁ RẼ XUỐNG RELAY & PM
                    if (p.IncludeRelay || p.IncludeMct)
                    {
                        AddCircle(btr, tr, new Point3d(dropX, topBranchY, 0), 0.6, LayerControl, 3);
                        double relayX = dropX + 7.0;
                        double relayY = topBranchY - 14.0;
                        double pmX = dropX + 7.0;
                        double pmY = mctCenterY;
                        double greenDropEndY = p.IncludeMct ? pmY : relayY;

                        AddLine(btr, tr, new Point3d(dropX, topBranchY, 0), new Point3d(dropX, greenDropEndY, 0), LayerControl, 3);

                        // (D) RƠ LE BẢO VỆ
                        if (p.IncludeRelay)
                        {
                            Point3d relayConnPt;
                            ObjectId relayBlockId = GetOrLoadRelayBlock(db, tr, out relayConnPt);
                            bool hasRelayBlock = (relayBlockId != ObjectId.Null && !relayBlockId.IsErased);

                            SldInteractiveItem relayCustom = (p != null) ? p.FindCustomItem("ACCESSORY_RELAY") : null;
                            double relayCadOffX = 0, relayCadOffY = 0, relayCadScale = 1.0, relayCadRot = 0;
                            if (relayCustom != null && !relayCustom.IsDeleted)
                            {
                                relayCadOffX = relayCustom.OffsetX / 2.8;
                                relayCadOffY = -relayCustom.OffsetY / 2.8;
                                relayCadScale = relayCustom.Scale;
                                relayCadRot = relayCustom.RotationAngle * Math.PI / 180.0;
                            }

                            Point3d actualRelay = new Point3d(relayX + relayCadOffX, relayY + relayCadOffY, 0);
                            AddOrthogonalWire(btr, tr, new Point3d(dropX, relayY, 0), actualRelay, LayerControl, 3);

                            if (hasRelayBlock)
                            {
                                double rScale = ((p.RelayScale > 0) ? p.RelayScale : 0.334) * relayCadScale;
                                double rInsX = actualRelay.X - relayConnPt.X * rScale;
                                double rInsY = actualRelay.Y - relayConnPt.Y * rScale;
                                BlockReference relayRef = new BlockReference(new Point3d(rInsX, rInsY, 0), relayBlockId);
                                relayRef.ScaleFactors = new Scale3d(rScale, rScale, 1.0);
                                relayRef.Rotation = relayCadRot;
                                relayRef.Layer = LayerControl;
                                relayRef.ColorIndex = 3;
                                btr.AppendEntity(relayRef);
                                tr.AddNewlyCreatedDBObject(relayRef, true);
                                _currentCabinetEntities.Add(relayRef.ObjectId);
                            }
                            else
                            {
                                AddRectangle(btr, tr, new Point3d(actualRelay.X, actualRelay.Y + 3.0 * relayCadScale, 0), new Point3d(actualRelay.X + 11.0 * relayCadScale, actualRelay.Y - 3.0 * relayCadScale, 0), LayerControl, 3);
                                AddLine(btr, tr, new Point3d(actualRelay.X + 5.5 * relayCadScale, actualRelay.Y + 3.0 * relayCadScale, 0), new Point3d(actualRelay.X + 5.5 * relayCadScale, actualRelay.Y - 3.0 * relayCadScale, 0), LayerControl, 3);
                                AddLine(btr, tr, actualRelay, new Point3d(actualRelay.X + 11.0 * relayCadScale, actualRelay.Y, 0), LayerControl, 3);
                                AddText(btr, tr, "OV", new Point3d(actualRelay.X + 2.75 * relayCadScale, actualRelay.Y + 1.5 * relayCadScale, 0), 1.5 * relayCadScale, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);
                                AddText(btr, tr, "UV", new Point3d(actualRelay.X + 8.25 * relayCadScale, actualRelay.Y + 1.5 * relayCadScale, 0), 1.5 * relayCadScale, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);
                                AddText(btr, tr, "EF", new Point3d(actualRelay.X + 2.75 * relayCadScale, actualRelay.Y - 1.5 * relayCadScale, 0), 1.5 * relayCadScale, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);
                                AddText(btr, tr, "OC", new Point3d(actualRelay.X + 8.25 * relayCadScale, actualRelay.Y - 1.5 * relayCadScale, 0), 1.5 * relayCadScale, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);
                            }
                        }

                        // (E) ĐỒNG HỒ ĐA NĂNG PM
                        if (p.IncludeMct)
                        {
                            SldInteractiveItem pmCustom = (p != null) ? p.FindCustomItem("ACCESSORY_PM") : null;
                            double pmCadOffX = 0, pmCadOffY = 0, pmCadScale = 1.0, pmCadRot = 0;
                            if (pmCustom != null && !pmCustom.IsDeleted)
                            {
                                pmCadOffX = pmCustom.OffsetX / 2.8;
                                pmCadOffY = -pmCustom.OffsetY / 2.8;
                                pmCadScale = pmCustom.Scale;
                                pmCadRot = pmCustom.RotationAngle * Math.PI / 180.0;
                            }

                            Point3d actualPm = new Point3d(pmX + pmCadOffX, pmY + pmCadOffY, 0);
                            AddOrthogonalWire(btr, tr, new Point3d(dropX, pmY, 0), actualPm, LayerControl, 3);
                            AddRectangle(btr, tr, new Point3d(actualPm.X, actualPm.Y + 4.5 * pmCadScale, 0), new Point3d(actualPm.X + 11.0 * pmCadScale, actualPm.Y - 4.5 * pmCadScale, 0), LayerMetering, 4);
                            AddText(btr, tr, "PM", new Point3d(actualPm.X + 5.5 * pmCadScale, actualPm.Y, 0), 2.2 * pmCadScale, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);
                            AddOrthogonalWire(btr, tr, new Point3d(incomingX + 1.5, mctCenterY, 0), actualPm, LayerControl, 3);
                            AddCircle(btr, tr, new Point3d(incomingX + 1.5, mctCenterY, 0), 0.5, LayerControl, 3);
                        }
                    }
                }

                // 8.1. VẼ CÁC ĐỐI TƯỢNG SAO CHÉP (COPIED ITEMS TRÊN BẢN VẼ CAD)
                if (p != null && p.SldCustomItems != null)
                {
                    foreach (var cItem in p.SldCustomItems)
                    {
                        if (cItem.IsCopied && !cItem.IsDeleted)
                        {
                            double cOffX = cItem.OffsetX / 2.8;
                            double cOffY = -cItem.OffsetY / 2.8;
                            double cRot = cItem.RotationAngle * Math.PI / 180.0;
                            double cScale = cItem.Scale;
                            Point3d cPos = new Point3d(x0 + totalHdrW + cOffX, targetBreakerCenterY + cOffY, 0);

                            if (cItem.ItemType == "MainBreaker" || cItem.ItemType == "BranchBreaker")
                            {
                                if (mccbBlockId != ObjectId.Null && !mccbBlockId.IsErased)
                                {
                                    BlockReference cRef = new BlockReference(cPos, mccbBlockId);
                                    cRef.Rotation = connInfo.RotationAngle + cRot;
                                    cRef.ScaleFactors = new Scale3d(cScale, cScale, 1.0);
                                    cRef.Layer = LayerSchematic;
                                    btr.AppendEntity(cRef);
                                    tr.AddNewlyCreatedDBObject(cRef, true);
                                    _currentCabinetEntities.Add(cRef.ObjectId);
                                }
                                else
                                {
                                    AddRectangle(btr, tr, new Point3d(cPos.X - 2.0 * cScale, cPos.Y + 4.0 * cScale, 0), new Point3d(cPos.X + 2.0 * cScale, cPos.Y - 4.0 * cScale, 0), LayerSchematic, 7);
                                }
                                AddText(btr, tr, cItem.DisplayName, new Point3d(cPos.X - 5.0 * cScale, cPos.Y, 0), 1.6 * cScale, AttachmentPoint.MiddleRight, LayerText, 0);
                            }
                        }
                    }
                }

                // 9. TIÊU ĐỀ TỦ ĐIỆN & CÔNG SUẤT

                double titleX = cabLeft + 8.0;

                double titleY = cabTop - 10.0;

                AddText(btr, tr, string.Format("TỦ ĐIỆN: {0}", panel.PanelName), new Point3d(titleX, titleY, 0), 3.5, AttachmentPoint.TopLeft, LayerText, 0);

                AddText(btr, tr, string.Format("(Ptt= {0:N3} KW)", panel.TotalCalculatedPowerKW), new Point3d(titleX, titleY - 7.0, 0), 3.0, AttachmentPoint.TopLeft, LayerText, 0);

                // 10. TIẾP ĐỊA TỦ ĐIỆN (EARTHING)

                                // 10. TIẾP ĐỊA TỦ ĐIỆN (EARTHING)

                DrawSideEarthing(btr, tr, new Point3d(cabRight, cabTop - 25.0, 0));

                // TÍNH TOÁN TỶ LỆ KHUNG BẢN VẼ A3 (420 x 297 mm)

                double diagWidth = Math.Max(cabRight, x0 + totalTableW) - origin.X + 15.0;

                double diagHeight = incomingTop - yTableBottom + 30.0;

                double scaleA3 = 1.0;

                if (p.IncludeTitleBlock)

                {

                    double sx = (diagWidth + (p.IncludeLegend ? 105.0 : 20.0)) / 330.0;

                    double sy = diagHeight / 265.0;

                    double maxRatio = Math.Max(sx, sy);

                    if (maxRatio > 1.0)

                    {

                        scaleA3 = Math.Ceiling(maxRatio * 10.0) / 10.0;

                    }

                }

                // 11. BẢNG CHÚ THÍCH KÝ HIỆU BLOCK (LEGEND) NẾU ĐƯỢC CHỌN

                if (p.IncludeLegend)

                {

                    double legX = origin.X + Math.Max(cabRight + 12.0, x0 + totalTableW + 10.0);

                    if (p.IncludeTitleBlock && (legX > origin.X + 355.0 * scaleA3 - 102.0))

                    {

                        legX = origin.X + 355.0 * scaleA3 - 102.0;

                    }

                    double legY = origin.Y + (p.IncludeTitleBlock ? (282.0 * scaleA3) : incomingTop);

                    // Đồng bộ tọa độ dịch chuyển tự do từ Canvas Preview sang CAD

                    legX += (p.LegendOffsetX / 2.8) * scaleA3;

                    legY -= (p.LegendOffsetY / 2.8) * scaleA3;

                    DrawLegendTable(db, btr, tr, new Point3d(legX, legY, 0), panel, p, scaleA3);

                }

                // 12. LỒNG KHUNG BẢN VẼ A3 NGANG (TỪ HT - Khung ten A3-HE DIEN.dwg)

                if (p.IncludeTitleBlock)

                {

                    InsertTitleBlockA3(db, btr, tr, origin, scaleA3, p, panel, 

                        !string.IsNullOrEmpty(p.DrawingTitle) ? p.DrawingTitle : ("SƠ ĐỒ NGUYÊN LÝ TỦ " + panel.PanelName), 

                        !string.IsNullOrEmpty(p.DrawingNumber) ? (p.DrawingNumber + "-01") : "EL-01");

                }

                // 13. VẼ MẶT NGOÀI VỎ TỦ THÀNH BẢN VẼ RIÊNG BIỆT (FRONT VIEW ENCLOSURE SHEET)

                if (p.DrawFrontView)

                {

                    double fvOffsetX = p.IncludeTitleBlock ? (450.0 * scaleA3) : (Math.Max(cabRight, x0 + totalTableW) + 50.0);

                    Point3d fvOrigin = new Point3d(origin.X + fvOffsetX, origin.Y, 0);

                    DrawFrontViewEnclosureSheet(db, btr, tr, fvOrigin, scaleA3, p, panel);

                }

                // 11. TẠO GROUP VÀ GẮN XDATA ĐỂ PHỤC VỤ CHỨC NĂNG CẬP NHẬT TỦ ĐIỆN VỀ SAU

                try

                {

                    RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);

                    if (!rat.Has(PanelStorageService.RegAppName))

                    {

                        RegAppTableRecord ratr = new RegAppTableRecord();

                        ratr.Name = PanelStorageService.RegAppName;

                        rat.Add(ratr);

                        tr.AddNewlyCreatedDBObject(ratr, true);

                    }

                    // Lưu toàn bộ cấu hình tủ vào XData của vỏ tủ cabRect

                    ResultBuffer rb = PanelStorageService.SerializePanelToXData(panel, origin, p);

                    cabRect.XData = rb;

                    // Gắn thêm XData nhỏ nhận diện panelName lên các đối tượng chính để double-click ở đâu cũng nhận được

                    ResultBuffer idRb = new ResultBuffer(

                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, PanelStorageService.RegAppName),

                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, panel.PanelName)

                    );

                    foreach (ObjectId entId in _currentCabinetEntities)

                    {

                        if (entId.IsValid && !entId.IsNull && !entId.IsErased)

                        {

                            try

                            {

                                DBObject entObj = tr.GetObject(entId, OpenMode.ForWrite, false, true);

                                if (entObj != null && !(entObj is AttributeReference) && entObj != cabRect)

                                {

                                    entObj.XData = idRb;

                                }

                            }

                            catch { }

                        }

                    }

                    DBDictionary groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);

                    string cleanName = "TAIAI_PANEL_" + panel.PanelName.Replace("-", "_").Replace(" ", "_");

                    if (groupDict.Contains(cleanName))

                    {

                        Group oldG = tr.GetObject(groupDict.GetAt(cleanName), OpenMode.ForWrite) as Group;

                        if (oldG != null) oldG.Erase();

                    }

                    Group newG = new Group("Tủ điện " + panel.PanelName, true);

                    foreach (ObjectId id in _currentCabinetEntities)

                    {

                        if (id.IsValid && !id.IsNull && !id.IsErased)

                        {

                            try

                            {

                                DBObject obj = tr.GetObject(id, OpenMode.ForRead, false, true);

                                if (obj is Entity && !(obj is AttributeReference))

                                {

                                    newG.Append(id);

                                }

                            }

                            catch { }

                        }

                    }

                    groupDict.SetAt(cleanName, newG);

                    tr.AddNewlyCreatedDBObject(newG, true);

                }

                catch { }

                tr.Commit();

                }

            }

            ed.WriteMessage(string.Format("\n[TaiAi] Đã hoàn thành vẽ Sơ đồ nguyên lý tủ điện '{0}' chuẩn kích thước không lỗi chữ!", panel.PanelName));

            return true;

        }

        /// <summary>

        /// Cập nhật lại sơ đồ nguyên lý tủ điện đã xuất ra trước đó trên bản vẽ AutoCAD

        /// Tự động tìm tủ cũ, xóa các đối tượng cũ và vẽ lại đúng vị trí ban đầu với dữ liệu mới.

        /// </summary>

        public bool UpdateSingleLineDiagram(Document doc, ElectricalPanel panel, DrawingParameters p)

        {

            if (doc == null || panel == null) return false;

            Editor ed = doc.Editor;

            Database db = doc.Database;

            Point3d insertPt = Point3d.Origin;

            bool foundOld = false;

            using (doc.LockDocument())

            using (Transaction tr = db.TransactionManager.StartTransaction())

            {

                DBDictionary groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);

                string cleanName = "TAIAI_PANEL_" + panel.PanelName.Replace("-", "_").Replace(" ", "_");

                ObjectId oldGroupId = ObjectId.Null;

                if (groupDict.Contains(cleanName))

                {

                    oldGroupId = groupDict.GetAt(cleanName);

                }

                else

                {

                    foreach (DBDictionaryEntry entry in groupDict)

                    {

                        if (entry.Key.StartsWith("TAIAI_PANEL_"))

                        {

                            oldGroupId = entry.Value;

                            break;

                        }

                    }

                }

                if (oldGroupId != ObjectId.Null && !oldGroupId.IsErased)

                {

                    Group grp = tr.GetObject(oldGroupId, OpenMode.ForWrite) as Group;

                    if (grp != null)

                    {

                        ObjectId[] ids = grp.GetAllEntityIds();

                        foreach (ObjectId id in ids)

                        {

                            if (!id.IsErased)

                            {

                                DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                                ResultBuffer rb = obj.GetXDataForApplication("TAIAI_PANEL");

                                if (rb != null)

                                {

                                    TypedValue[] tvs = rb.AsArray();

                                    for (int i = 0; i < tvs.Length; i++)

                                    {

                                        if (tvs[i].TypeCode == (short)DxfCode.ExtendedDataXCoordinate)

                                        {

                                            insertPt = (Point3d)tvs[i].Value;

                                            foundOld = true;

                                            break;

                                        }

                                    }

                                }

                                if (foundOld) break;

                            }

                        }

                        if (!foundOld && ids.Length > 0)

                        {

                            Extents3d totalExt = new Extents3d();

                            bool hasExt = false;

                            foreach (ObjectId id in ids)

                            {

                                if (!id.IsErased)

                                {

                                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                                    if (ent != null)

                                    {

                                        if (!hasExt) { totalExt = ent.GeometricExtents; hasExt = true; }

                                        else totalExt.AddExtents(ent.GeometricExtents);

                                    }

                                }

                            }

                            if (hasExt)

                            {

                                insertPt = totalExt.MinPoint;

                                foundOld = true;

                            }

                        }

                        // Xóa BlockReference Bảng chú thích ký hiệu cũ nếu có

                        string legBlockName = "TAIAI_LEGEND_" + panel.PanelName.Replace("-", "_").Replace(" ", "_");

                        BlockTableRecord btrCurr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        foreach (ObjectId entId in btrCurr)

                        {

                            if (!entId.IsErased)

                            {

                                BlockReference br = tr.GetObject(entId, OpenMode.ForWrite, false, true) as BlockReference;

                                if (br != null)

                                {

                                    BlockTableRecord btrTgt = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

                                    if (btrTgt != null && btrTgt.Name == legBlockName)

                                    {

                                        br.Erase();

                                    }

                                }

                            }

                        }

                        // Xóa các đối tượng cũ (loc an toan tranh xoa trung AttributeReference)

                        foreach (ObjectId id in ids)

                        {

                            if (id.IsValid && !id.IsNull && !id.IsErased)

                            {

                                try

                                {

                                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false, true);

                                    if (obj != null && !(obj is AttributeReference))

                                    {

                                        obj.Erase();

                                    }

                                }

                                catch { }

                            }

                        }

                        grp.Erase();

                    }

                }

                tr.Commit();

            }

            // Nếu không tự tìm thấy, nhắc người dùng click chọn 1 đối tượng trên tủ cũ

            if (!foundOld)

            {

                PromptEntityOptions peo = new PromptEntityOptions("\n[TaiAi] Chọn một đối tượng bất kỳ trên sơ đồ tủ điện cũ cần cập nhật (hoặc Enter để chọn điểm mới): ");

                peo.AllowNone = true;

                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status == PromptStatus.OK)

                {

                    using (doc.LockDocument())

                    using (Transaction tr = db.TransactionManager.StartTransaction())

                    {

                        Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForWrite) as Entity;

                        if (ent != null)

                        {

                            insertPt = ent.GeometricExtents.MinPoint;

                            ent.Erase();

                            foundOld = true;

                        }

                        tr.Commit();

                    }

                }

            }

            // Nếu vẫn chưa có điểm đặt, hỏi điểm chèn mới

            if (!foundOld)

            {

                PromptPointOptions ppo = new PromptPointOptions("\n[TaiAi] Chọn điểm đặt Sơ đồ nguyên lý tủ điện trên bản vẽ: ");

                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status != PromptStatus.OK) return false;

                insertPt = ppr.Value;

            }

            return DrawSingleLineDiagram(doc, panel, insertPt, p);

        }

        #region Block Points & Placement Helpers

        private List<Point3d> GetPointsFromBlockRecord(BlockTableRecord btr, Transaction tr)

        {

            List<Point3d> points = new List<Point3d>();

            foreach (ObjectId id in btr)

            {

                DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                DBPoint pt = obj as DBPoint;

                if (pt != null)

                {

                    points.Add(pt.Position);

                }

                else

                {

                    BlockReference subRef = obj as BlockReference;

                    if (subRef != null)

                    {

                        BlockTableRecord subBtr = tr.GetObject(subRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

                        if (subBtr != null)

                        {

                            List<Point3d> subPts = GetPointsFromBlockRecord(subBtr, tr);

                            foreach (Point3d sp in subPts)

                            {

                                points.Add(sp.TransformBy(subRef.BlockTransform));

                            }

                        }

                    }

                }

            }

            return points;

        }

        private BlockConnectionInfo AnalyzeBlockConnection(Database db, Transaction tr, ObjectId blockId)

        {

            BlockConnectionInfo info = new BlockConnectionInfo();

            if (blockId == ObjectId.Null || blockId.IsErased) return info;

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);

            List<Point3d> points = GetPointsFromBlockRecord(btr, tr);

            if (points.Count >= 2)

            {

                Point3d p1 = points[0];

                Point3d p2 = points[1];

                double dx = p2.X - p1.X;

                double dy = p2.Y - p1.Y;

                double dist = Math.Sqrt(dx * dx + dy * dy);

                info.HasPoints = true;

                info.Point1 = p1;

                info.Point2 = p2;

                info.Distance = dist;

                info.RotationAngle = Math.PI / 2.0;

            }

            else

            {

                info.HasPoints = false;

                info.Point1 = Point3d.Origin;

                info.Point2 = new Point3d(0, 8.051, 0);

                info.Distance = 8.051;

                info.RotationAngle = Math.PI / 2.0;

            }

            if (btr.HasAttributeDefinitions)

            {

                info.HasAttributes = true;

                foreach (ObjectId id in btr)

                {

                    AttributeDefinition att = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;

                    if (att != null)

                    {

                        info.AttributeTags.Add(att.Tag);

                    }

                }

            }

            return info;

        }

        private void InsertBreakerWithConnectionPoints(BlockTableRecord btr, Transaction tr, ObjectId blockId, ObjectId dolBlockId,
            BlockConnectionInfo connInfo, double lineX, double fromY, double toY, double targetCenterY, string spec, bool isMainBreaker,
            StarterType starter = StarterType.Direct, ControlMode control = ControlMode.None,
            string downstreamLayer = LayerSchematic, int downstreamColor = 7, bool drawOutgoingWire = true,
            SldInteractiveItem customItem = null)

        {

            double brkOffX = 0;
            double brkOffY = 0;
            double brkRot = 0;
            double brkScale = 1.0;
            if (customItem != null && !customItem.IsDeleted)
            {
                brkOffX = customItem.OffsetX / 2.8;
                brkOffY = -customItem.OffsetY / 2.8;
                brkRot = customItem.RotationAngle * Math.PI / 180.0;
                brkScale = customItem.Scale;
            }

            double actualCenterX = lineX + brkOffX;
            double actualCenterY = targetCenterY + brkOffY;
            double breakerBottomY;

            if (blockId != ObjectId.Null && !blockId.IsErased)
            {
                double dist = connInfo.Distance > 0 ? connInfo.Distance : 8.051;
                double halfDist = dist / 2.0;
                Point3d worldTop = new Point3d(actualCenterX, actualCenterY + halfDist * brkScale, 0);
                Point3d worldBot = new Point3d(actualCenterX, actualCenterY - halfDist * brkScale, 0);
                breakerBottomY = worldBot.Y;
                Point3d insertPos = worldBot;
                double rotation = connInfo.RotationAngle + brkRot;

                AddOrthogonalWire(btr, tr, new Point3d(lineX, fromY, 0), worldTop, LayerSchematic, 7);

                BlockReference blkRef = new BlockReference(insertPos, blockId);
                blkRef.Rotation = rotation;
                blkRef.ScaleFactors = new Scale3d(brkScale, brkScale, 1.0);
                blkRef.Layer = LayerSchematic;

                btr.AppendEntity(blkRef);

                tr.AddNewlyCreatedDBObject(blkRef, true);

                _currentCabinetEntities.Add(blkRef.ObjectId);

                bool hasHandledAttribute = false;

                BlockTableRecord btrBlock = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);

                if (btrBlock.HasAttributeDefinitions)

                {

                    foreach (ObjectId attId in btrBlock)

                    {

                        AttributeDefinition attDef = tr.GetObject(attId, OpenMode.ForRead) as AttributeDefinition;

                        if (attDef != null && !attDef.Constant)

                        {

                            AttributeReference attRef = new AttributeReference();

                            attRef.SetAttributeFromBlock(attDef, blkRef.BlockTransform);

                            attRef.TextString = spec;

                            blkRef.AttributeCollection.AppendAttribute(attRef);

                            tr.AddNewlyCreatedDBObject(attRef, true);

                            hasHandledAttribute = true;

                        }

                    }

                }

                if (!hasHandledAttribute)

                {

                    string[] parts = spec.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    string l1 = parts.Length > 0 ? parts[0] : "";

                    string l2 = "";

                    string l3 = "";

                    if (parts.Length == 2)

                    {

                        l2 = parts[1];

                    }

                    else if (parts.Length == 3)

                    {

                        l2 = parts[1];

                        l3 = parts[2];

                    }

                    else if (parts.Length >= 4)

                    {

                        l2 = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));

                        l3 = parts[parts.Length - 1];

                    }

                    double textX = actualCenterX - 4.2 * brkScale;

                    AddText(btr, tr, l1, new Point3d(textX, actualCenterY + 2.5 * brkScale, 0), 1.6 * brkScale, AttachmentPoint.MiddleRight, downstreamLayer, 0, downstreamColor);

                    AddText(btr, tr, l2, new Point3d(textX, actualCenterY, 0), 1.6 * brkScale, AttachmentPoint.MiddleRight, downstreamLayer, 0, downstreamColor);

                    if (!string.IsNullOrEmpty(l3))

                    {

                        AddText(btr, tr, l3, new Point3d(textX, actualCenterY - 2.5 * brkScale, 0), 1.6 * brkScale, AttachmentPoint.MiddleRight, downstreamLayer, 0, downstreamColor);

                    }

                    bool isRccb = !string.IsNullOrEmpty(spec) && (spec.ToUpper().Contains("RCCB") || spec.ToUpper().Contains("RCBO"));

                    if (isRccb)

                    {

                        AddText(btr, tr, "\u0394I 30mA", new Point3d(lineX + 3.0, targetCenterY, 0), 1.2, AttachmentPoint.MiddleLeft, downstreamLayer, 0, 30);

                    }

                }

            }

            else

            {

                // Vẽ ký hiệu Aptomat hình chữ nhật dự phòng

                double rectH = 8.0;

                double rectW = 4.0;

                Point3d pTop = new Point3d(actualCenterX, actualCenterY + (rectH * brkScale) / 2.0, 0);

                Point3d pBot = new Point3d(actualCenterX, actualCenterY - (rectH * brkScale) / 2.0, 0);

                breakerBottomY = pBot.Y;

                AddOrthogonalWire(btr, tr, new Point3d(lineX, fromY, 0), pTop, LayerSchematic, 7);

                AddRectangle(btr, tr, new Point3d(actualCenterX - (rectW * brkScale) / 2.0, actualCenterY + (rectH * brkScale) / 2.0, 0),

                    new Point3d(actualCenterX + (rectW * brkScale) / 2.0, actualCenterY - (rectH * brkScale) / 2.0, 0), LayerSchematic, 7);

                AddLine(btr, tr, new Point3d(actualCenterX - (rectW * brkScale) / 2.0, actualCenterY - (rectH * brkScale) / 2.0, 0),

                    new Point3d(actualCenterX + (rectW * brkScale) / 2.0, actualCenterY + (rectH * brkScale) / 2.0, 0), LayerSchematic, 7);

                string[] parts = spec.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                string l1 = parts.Length > 0 ? parts[0] : "";

                string l2 = "";

                string l3 = "";

                if (parts.Length == 2)

                {

                    l2 = parts[1];

                }

                else if (parts.Length == 3)

                {

                    l2 = parts[1];

                    l3 = parts[2];

                }

                else if (parts.Length >= 4)

                {

                    l2 = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));

                    l3 = parts[parts.Length - 1];

                }

                double textX = lineX - 4.2;

                AddText(btr, tr, l1, new Point3d(textX, targetCenterY + 2.5, 0), 1.6, AttachmentPoint.MiddleRight, downstreamLayer, 0, downstreamColor);

                AddText(btr, tr, l2, new Point3d(textX, targetCenterY, 0), 1.6, AttachmentPoint.MiddleRight, downstreamLayer, 0, downstreamColor);

                if (!string.IsNullOrEmpty(l3))

                {

                    AddText(btr, tr, l3, new Point3d(textX, targetCenterY - 2.5, 0), 1.6, AttachmentPoint.MiddleRight, downstreamLayer, 0, downstreamColor);

                }

                bool isRccbElse = !string.IsNullOrEmpty(spec) && (spec.ToUpper().Contains("RCCB") || spec.ToUpper().Contains("RCBO"));

                if (isRccbElse)

                {

                    AddText(btr, tr, "\u0394I 30mA", new Point3d(lineX + 3.0, targetCenterY, 0), 1.2, AttachmentPoint.MiddleLeft, downstreamLayer, 0, 30);

                }

            }

            if (!drawOutgoingWire)

            {

                return;

            }

            // XỬ LÝ PHẦN DƯỚI ĐẦU RA CỦA BREAKER (CÓ DOL HOẶC KHÔNG CÓ DOL)

            // XỬ LÝ PHẦN DƯỚI ĐẦU RA CỦA BREAKER (BỘ KHỞI ĐỘNG STARTER & ĐIỀU KHIỂN)

            double starterTopY = breakerBottomY - 2.5;

            double starterBotY = starterTopY - 5.5;

            if (starter == StarterType.DOL)

            {

                AddOrthogonalWire(btr, tr, new Point3d(actualCenterX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

                if (dolBlockId != ObjectId.Null && !dolBlockId.IsErased)

                {

                    BlockReference dolRef = new BlockReference(new Point3d(lineX, starterTopY, 0), dolBlockId);

                    dolRef.Layer = downstreamLayer;

                    dolRef.ColorIndex = downstreamColor;

                    btr.AppendEntity(dolRef);

                    tr.AddNewlyCreatedDBObject(dolRef, true);

                    _currentCabinetEntities.Add(dolRef.ObjectId);

                }

                else

                {

                    AddRectangle(btr, tr, new Point3d(lineX - 2.5, starterTopY, 0), new Point3d(lineX + 2.5, starterBotY, 0), downstreamLayer, downstreamColor);

                    AddText(btr, tr, "DOL", new Point3d(lineX, (starterTopY + starterBotY) / 2.0, 0), 1.6, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);

                }

                if (starterBotY > toY)

                {

                    AddLine(btr, tr, new Point3d(lineX, starterBotY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);

                }

            }

            else if (starter == StarterType.StarDelta)

            {

                // KHỞI ĐỘNG SAO - TAM GIÁC (Y - Δ): CẤP 2 ĐƯỜNG CÁP 2x3C

                AddOrthogonalWire(btr, tr, new Point3d(actualCenterX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

                AddRectangle(btr, tr, new Point3d(lineX - 4.0, starterTopY, 0), new Point3d(lineX + 4.0, starterBotY, 0), downstreamLayer, downstreamColor);

                AddText(btr, tr, "Y - Δ", new Point3d(lineX, (starterTopY + starterBotY) / 2.0 + 0.8, 0), 1.8, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);

                AddText(btr, tr, "STARTER", new Point3d(lineX, (starterTopY + starterBotY) / 2.0 - 1.2, 0), 1.2, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);

                // 2 ĐƯỜNG CÁP SONG SONG RA ĐỘNG CƠ (2x3C)

                double cOffset = 1.4;

                if (starterBotY > toY)

                {

                    AddLine(btr, tr, new Point3d(lineX - cOffset, starterBotY, 0), new Point3d(lineX - cOffset, toY, 0), downstreamLayer, downstreamColor);

                    AddLine(btr, tr, new Point3d(lineX + cOffset, starterBotY, 0), new Point3d(lineX + cOffset, toY, 0), downstreamLayer, downstreamColor);

                }

            }

            else if (starter == StarterType.VFD)

            {

                // BIẾN TẦN VFD

                AddOrthogonalWire(btr, tr, new Point3d(actualCenterX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

                AddRectangle(btr, tr, new Point3d(lineX - 3.5, starterTopY, 0), new Point3d(lineX + 3.5, starterBotY, 0), downstreamLayer, downstreamColor);

                AddLine(btr, tr, new Point3d(lineX - 3.5, starterBotY, 0), new Point3d(lineX + 3.5, starterTopY, 0), downstreamLayer, downstreamColor);

                AddText(btr, tr, "VFD", new Point3d(lineX, (starterTopY + starterBotY) / 2.0, 0), 1.6, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);

                if (starterBotY > toY)

                {

                    AddLine(btr, tr, new Point3d(lineX, starterBotY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);

                }

            }

            else if (starter == StarterType.SoftStarter)

            {

                // KHỞI ĐỘNG MỀM SOFT STARTER

                AddOrthogonalWire(btr, tr, new Point3d(actualCenterX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

                AddRectangle(btr, tr, new Point3d(lineX - 3.5, starterTopY, 0), new Point3d(lineX + 3.5, starterBotY, 0), downstreamLayer, downstreamColor);

                AddText(btr, tr, "SS", new Point3d(lineX, (starterTopY + starterBotY) / 2.0, 0), 1.6, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);

                if (starterBotY > toY)

                {

                    AddLine(btr, tr, new Point3d(lineX, starterBotY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);

                }

            }

            else

            {

                AddOrthogonalWire(btr, tr, new Point3d(actualCenterX, breakerBottomY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);

            }

            // KÝ HIỆU CHẾ ĐỘ ĐIỀU KHIỂN (H-O-A / Phao FS / Áp suất PS)

            if (control != ControlMode.None)

            {

                double ctrlY = toY + 12.0;

                double ctrlX = lineX + 3.2;

                if (control == ControlMode.AutoManual)

                {

                    AddCircle(btr, tr, new Point3d(ctrlX, ctrlY, 0), 1.8, LayerControl, 3);

                    AddText(btr, tr, "A-O-M", new Point3d(ctrlX, ctrlY, 0), 1.0, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);

                    AddLine(btr, tr, new Point3d(lineX, ctrlY, 0), new Point3d(ctrlX - 1.8, ctrlY, 0), LayerControl, 3);

                }

                else if (control == ControlMode.FloatSwitch)

                {

                    AddRectangle(btr, tr, new Point3d(ctrlX - 1.8, ctrlY + 1.8, 0), new Point3d(ctrlX + 1.8, ctrlY - 1.8, 0), LayerControl, 3);

                    AddText(btr, tr, "FS", new Point3d(ctrlX, ctrlY, 0), 1.3, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);

                    AddLine(btr, tr, new Point3d(lineX, ctrlY, 0), new Point3d(ctrlX - 1.8, ctrlY, 0), LayerControl, 3);

                }

                else if (control == ControlMode.PressureSwitch)

                {

                    AddRectangle(btr, tr, new Point3d(ctrlX - 1.8, ctrlY + 1.8, 0), new Point3d(ctrlX + 1.8, ctrlY - 1.8, 0), LayerControl, 3);

                    AddText(btr, tr, "PS", new Point3d(ctrlX, ctrlY, 0), 1.3, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);

                    AddLine(btr, tr, new Point3d(lineX, ctrlY, 0), new Point3d(ctrlX - 1.8, ctrlY, 0), LayerControl, 3);

                }

            }

        }

        #endregion

        #region Block Loader Helpers

        private void PreloadAllBlocks(Database db)

        {

            PreloadBlock(db, "MCCB", "MCCB.dwg", new string[] { "mccb.dwg" });

            PreloadBlock(db, "C\u1EA6U CH\u00CC", "c\u1EA7u ch\u00EC.dwg", new string[] { "cau_chi.dwg", "cầu chì.dwg" });

            PreloadBlock(db, "CAU_CHI", "c\u1EA7u ch\u00EC.dwg", new string[] { "cau_chi.dwg", "cầu chì.dwg" });

            PreloadBlock(db, "\u0111\u00E8n b\u00E1o", "\u0111\u00E8n b\u00E1o pha.dwg", new string[] { "den_bao_pha.dwg", "đèn báo.dwg", "đèn báo pha.dwg" });

            PreloadBlock(db, "DEN_BAO", "\u0111\u00E8n b\u00E1o pha.dwg", new string[] { "den_bao_pha.dwg" });

            PreloadBlock(db, "RELAY BAO VE", "RELAY.dwg", new string[] { "relay.dwg" });

            PreloadBlock(db, "RELAY", "RELAY.dwg", new string[] { "relay.dwg" });

            PreloadBlock(db, "MCT", "MCT.dwg", new string[] { "mct.dwg" });

            PreloadBlock(db, "DOL", "DOL.dwg", new string[] { "dol.dwg" });

        }

        private ObjectId GetOrLoadDolBlock(Database targetDb)

        {

            return GetOrLoadBlock(targetDb, "DOL", new string[] { "dol", "*U3" }, "DOL.dwg", new string[] { "dol.dwg" });

        }

        private void PreloadBlock(Database targetDb, string blockName, string primaryDwg, string[] alternateDwgs)

        {

            try

            {

                using (Transaction tr = targetDb.TransactionManager.StartTransaction())

                {

                    BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);

                    if (bt.Has(blockName)) return;

                    tr.Commit();

                }

                string dwgPath = FindBlockPath(primaryDwg, alternateDwgs);

                if (string.IsNullOrEmpty(dwgPath) || !File.Exists(dwgPath)) return;

                using (Database sourceDb = new Database(false, true))

                {

                    sourceDb.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, true, "");

                    targetDb.Insert(blockName, sourceDb, true);

                }

            }

            catch { }

        }

        private string FindBlockPath(string primaryName, string[] alternateNames)

        {

            List<string> candidateNames = new List<string>();

            if (!string.IsNullOrEmpty(primaryName)) candidateNames.Add(primaryName.ToLowerInvariant());

            if (alternateNames != null)

            {

                foreach (string an in alternateNames)

                {

                    if (!string.IsNullOrEmpty(an)) candidateNames.Add(an.ToLowerInvariant());

                }

            }

            List<string> searchRoots = new List<string>();

            try

            {

                string asmPath = Assembly.GetExecutingAssembly().Location;

                string asmDir = Path.GetDirectoryName(asmPath);

                if (!string.IsNullOrEmpty(asmDir))

                {

                    searchRoots.Add(asmDir);

                    DirectoryInfo p1 = Directory.GetParent(asmDir);

                    if (p1 != null)

                    {

                        searchRoots.Add(p1.FullName);

                        DirectoryInfo p2 = Directory.GetParent(p1.FullName);

                        if (p2 != null) searchRoots.Add(p2.FullName);

                    }

                }

            }

            catch { }

            try

            {

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                searchRoots.Add(Path.Combine(appData, "Autodesk", "ApplicationPlugins", "TaiAiAddin.bundle"));

            }

            catch { }

            searchRoots.Add(@"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS");

            try

            {

                searchRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

            }

            catch { }

            foreach (string root in searchRoots)

            {

                if (Directory.Exists(root))

                {

                    try

                    {

                        string[] files = Directory.GetFiles(root, "*.dwg", SearchOption.AllDirectories);

                        foreach (string file in files)

                        {

                            string fName = Path.GetFileName(file).ToLowerInvariant();

                            foreach (string target in candidateNames)

                            {

                                if (fName == target || fName == target + ".dwg")

                                {

                                    return file;

                                }

                            }

                        }

                    }

                    catch { }

                }

            }

            return null;

        }

        private ObjectId GetOrLoadBlock(Database targetDb, string preferredName, string[] alternateNames, string primaryDwg, string[] alternateDwgs)

        {

            List<string> names = new List<string>();

            if (!string.IsNullOrEmpty(preferredName)) names.Add(preferredName);

            if (alternateNames != null) names.AddRange(alternateNames);

            // 1. Kiểm tra nếu Block đã có sẵn trong bản vẽ

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())

            {

                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);

                foreach (string n in names)

                {

                    if (bt.Has(n)) return bt[n];

                }

            }

            // 2. Chưa có trong BlockTable: Thử nạp on-demand từ file DWG

            try

            {

                string dwgPath = FindBlockPath(primaryDwg, alternateDwgs);

                if (!string.IsNullOrEmpty(dwgPath) && File.Exists(dwgPath))

                {

                    string blockToInsert = !string.IsNullOrEmpty(preferredName) ? preferredName : Path.GetFileNameWithoutExtension(dwgPath);

                    using (Database sourceDb = new Database(false, true))

                    {

                        sourceDb.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, true, "");

                        targetDb.Insert(blockToInsert, sourceDb, true);

                    }

                    using (Transaction tr = targetDb.TransactionManager.StartTransaction())

                    {

                        BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);

                        if (bt.Has(blockToInsert)) return bt[blockToInsert];

                        foreach (string n in names)

                        {

                            if (bt.Has(n)) return bt[n];

                        }

                    }

                }

            }

            catch { }

            return ObjectId.Null;

        }

        private ObjectId GetOrLoadMccbBlock(Database targetDb)

        {

            return GetOrLoadBlock(targetDb, "MCCB", new string[] { "mccb" }, "MCCB.dwg", null);

        }

        private ObjectId GetOrLoadFuseBlock(Database targetDb, Transaction tr, out Point3d p1, out Point3d p2)

        {

            p1 = Point3d.Origin;

            p2 = new Point3d(101.9237, 0, 0);

            ObjectId blkId = GetOrLoadBlock(targetDb, "C\u1EA6U CH\u00CC", new string[] { "CẦU CHÌ", "CAU_CHI", "cau_chi", "cầu chì" }, "c\u1EA7u ch\u00EC.dwg", new string[] { "cau_chi.dwg" });

            if (blkId == ObjectId.Null || blkId.IsErased) return ObjectId.Null;

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkId, OpenMode.ForRead);

            List<Point3d> pts = GetPointsFromBlockRecord(btr, tr);

            if (pts.Count >= 2)

            {

                pts.Sort(delegate(Point3d a, Point3d b) { return a.X.CompareTo(b.X); });

                p1 = pts[0];

                p2 = pts[pts.Count - 1];

            }

            return blkId;

        }

        private ObjectId GetOrLoadLampsBlock(Database targetDb, Transaction tr, out Point3d pIn)

        {

            pIn = Point3d.Origin;

            ObjectId blkId = GetOrLoadBlock(targetDb, "\u0111\u00E8n b\u00E1o", new string[] { "đèn báo", "den_bao_pha", "DEN_BAO_PHA", "đèn báo pha", "den_bao", "DEN_BAO" }, "\u0111\u00E8n b\u00E1o pha.dwg", new string[] { "den_bao_pha.dwg", "đèn báo.dwg" });

            if (blkId == ObjectId.Null || blkId.IsErased) return ObjectId.Null;

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkId, OpenMode.ForRead);

            List<Point3d> pts = GetPointsFromBlockRecord(btr, tr);

            if (pts.Count > 0)

            {

                pts.Sort(delegate(Point3d a, Point3d b) { return a.X.CompareTo(b.X); });

                pIn = pts[0];

            }

            return blkId;

        }

        private ObjectId GetOrLoadRelayBlock(Database targetDb, Transaction tr, out Point3d pConn)

        {

            pConn = Point3d.Origin;

            ObjectId blkId = GetOrLoadBlock(targetDb, "RELAY BAO VE", new string[] { "RELAY", "relay", "relay bao ve" }, "RELAY.dwg", new string[] { "relay.dwg" });

            if (blkId == ObjectId.Null || blkId.IsErased) return ObjectId.Null;

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkId, OpenMode.ForRead);

            List<Point3d> pts = GetPointsFromBlockRecord(btr, tr);

            if (pts.Count > 0)

            {

                pts.Sort(delegate(Point3d a, Point3d b) { return a.X.CompareTo(b.X); });

                pConn = pts[0];

            }

            return blkId;

        }

        private ObjectId GetOrLoadMctBlock(Database targetDb, Transaction tr, out Point3d pDrop, out Point3d pTop, out Point3d pBot)

        {

            pDrop = Point3d.Origin;

            pTop = new Point3d(0, 0, 0);

            pBot = new Point3d(0, -25.7973, 0);

            ObjectId blkId = GetOrLoadBlock(targetDb, "MCT", new string[] { "mct" }, "MCT.dwg", new string[] { "mct.dwg" });

            if (blkId == ObjectId.Null || blkId.IsErased) return ObjectId.Null;

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkId, OpenMode.ForRead);

            List<Point3d> pts = GetPointsFromBlockRecord(btr, tr);

            if (pts.Count >= 2)

            {

                List<Point3d> feederPts = new List<Point3d>();

                foreach (Point3d pt in pts)

                {

                    if (Math.Abs(pt.X) < 10.0)

                    {

                        feederPts.Add(pt);

                    }

                }

                if (feederPts.Count >= 2)

                {

                    feederPts.Sort(delegate(Point3d a, Point3d b) { return b.Y.CompareTo(a.Y); });

                    pTop = feederPts[0];

                    pBot = feederPts[feederPts.Count - 1];

                }

            }

            return blkId;

        }

        #endregion

        #region Table Grid & Data Drawing Helpers

        private void DrawVerticalTableGrid(BlockTableRecord btr, Transaction tr,

            double x0, double y0, double hdrMainW, double hdrSubW, double colW, int totalCols,

            double hName, double hCircuit, double hPower, int loadCount)

        {

            double totalHdrW = hdrMainW + hdrSubW;

            double totalLoadW = totalCols * colW;

            double totalW = totalHdrW + totalLoadW;

            double totalH = hName + hCircuit + hPower;

            double yName = y0;

            double yCircuit = yName + hName;

            double yPower = yCircuit + hCircuit;

            double yTop = yPower + hPower;

            AddRectangle(btr, tr, new Point3d(x0, yTop, 0), new Point3d(x0 + totalW, y0, 0), LayerBorder, 7);

            AddLine(btr, tr, new Point3d(x0, yCircuit, 0), new Point3d(x0 + totalW, yCircuit, 0), LayerBorder, 7);

            AddLine(btr, tr, new Point3d(x0, yPower, 0), new Point3d(x0 + totalW, yPower, 0), LayerBorder, 7);

            AddLine(btr, tr, new Point3d(x0 + hdrMainW, yTop, 0), new Point3d(x0 + hdrMainW, y0, 0), LayerBorder, 7);

            AddLine(btr, tr, new Point3d(x0 + totalHdrW, yTop, 0), new Point3d(x0 + totalHdrW, y0, 0), LayerBorder, 7);

            double subH = hPower / 3.0;

            AddLine(btr, tr, new Point3d(x0 + hdrMainW, yPower + 2 * subH, 0), new Point3d(x0 + totalHdrW, yPower + 2 * subH, 0), LayerBorder, 7);

            AddLine(btr, tr, new Point3d(x0 + hdrMainW, yPower + subH, 0), new Point3d(x0 + totalHdrW, yPower + subH, 0), LayerBorder, 7);

            for (int c = 1; c <= totalCols; c++)

            {

                double cx = x0 + totalHdrW + c * colW;

                AddLine(btr, tr, new Point3d(cx, yTop, 0), new Point3d(cx, y0, 0), LayerBorder, 7);

            }

        }

        private void PopulateTableData(BlockTableRecord btr, Transaction tr, ElectricalPanel panel,

            double x0, double y0, double hdrMainW, double hdrSubW, double colW,

            double hName, double hCircuit, double hPower, double textSize,

            bool includeSpare, bool includeSpd, bool hasCapacitor = false,

            int spareCount3P = 1, int spareCount1P = 0)

        {

            double totalHdrW = hdrMainW + hdrSubW;

            double yName = y0;

            double yCircuit = yName + hName;

            double yPower = yCircuit + hCircuit;

            double yTop = yPower + hPower;

            double midMainX = x0 + hdrMainW / 2.0;

            AddText(btr, tr, "C\u00D4NG SU\u1EA4T\nLOAD (KW)", new Point3d(midMainX, yPower + hPower / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddText(btr, tr, "S\u1ED0 M\u1EA0CH / CCT", new Point3d(midMainX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddText(btr, tr, "PH\u1EE4 T\u1EA2I / LOAD", new Point3d(midMainX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0);

            double subMidX = x0 + hdrMainW + hdrSubW / 2.0;

            double subH = hPower / 3.0;

            AddText(btr, tr, "R", new Point3d(subMidX, yPower + 2.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddText(btr, tr, "Y", new Point3d(subMidX, yPower + 1.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddText(btr, tr, "B", new Point3d(subMidX, yPower + 0.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0);

            int count = panel.Loads.Count;

            for (int i = 0; i < count; i++)

            {

                ElectricalLoad load = panel.Loads[i];

                double colCenterX = x0 + totalHdrW + (i + 0.5) * colW;

                bool is3P = load.Phase == PhaseType.ThreePhase;

                string loadLayer = is3P ? LayerLoad3P : LayerLoad1P;

                int loadColor = is3P ? 1 : 3;

                bool isSpareLoad = load.IsSpare || (!string.IsNullOrEmpty(load.LoadName) && (load.LoadName.ToUpper().Contains("SPARE") || load.LoadName.ToUpper().Contains("DỰ PHÒNG")));

                string pr = isSpareLoad ? "-" : load.DisplayPowerR;

                string py = isSpareLoad ? "-" : load.DisplayPowerY;

                string pb = isSpareLoad ? "-" : load.DisplayPowerB;

                string cct = isSpareLoad && (string.IsNullOrEmpty(load.CircuitNumber) || load.CircuitNumber.StartsWith("C")) ? "SPARE" : load.CircuitNumber;

                AddText(btr, tr, pr, new Point3d(colCenterX, yPower + 2.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, loadLayer, 0, loadColor);

                AddText(btr, tr, py, new Point3d(colCenterX, yPower + 1.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, loadLayer, 0, loadColor);

                AddText(btr, tr, pb, new Point3d(colCenterX, yPower + 0.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, loadLayer, 0, loadColor);

                AddText(btr, tr, cct, new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, loadLayer, 0, loadColor);

                string loadNameText = load.LoadName;

                if (loadNameText.Length > 20 && !loadNameText.Contains("\n"))

                {

                    int mid = loadNameText.Length / 2;

                    int splitIdx = loadNameText.IndexOfAny(new char[] { ' ', '-' }, mid - 5);

                    if (splitIdx > 0 && splitIdx < loadNameText.Length - 3)

                    {

                        loadNameText = loadNameText.Substring(0, splitIdx + 1) + "\n" + loadNameText.Substring(splitIdx + 1);

                    }

                }

                AddText(btr, tr, loadNameText, new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, loadLayer, Math.PI / 2.0, loadColor);

            }

            int extraCol = count;

            if (includeSpare)

            {

                int n3P = spareCount3P;

                int n1P = spareCount1P;

                if (n3P + n1P == 0) n3P = 1;

                for (int s = 0; s < n3P; s++)

                {

                    double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;

                    AddText(btr, tr, "-", new Point3d(colCenterX, yPower + 2.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    AddText(btr, tr, "-", new Point3d(colCenterX, yPower + 1.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    AddText(btr, tr, "-", new Point3d(colCenterX, yPower + 0.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    AddText(btr, tr, "L\u1ED8 D\u1EF0 PH\u00D2NG 3P\nSPARE", new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0, 7);

                    AddText(btr, tr, "SPARE", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    extraCol++;

                }

                for (int s = 0; s < n1P; s++)

                {

                    double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;

                    AddText(btr, tr, "-", new Point3d(colCenterX, yPower + 2.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    AddText(btr, tr, "-", new Point3d(colCenterX, yPower + 1.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    AddText(btr, tr, "-", new Point3d(colCenterX, yPower + 0.5 * subH, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    AddText(btr, tr, "L\u1ED8 D\u1EF0 PH\u00D2NG 1P\nSPARE", new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0, 7);

                    AddText(btr, tr, "SPARE", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                    extraCol++;

                }

            }

            if (includeSpd)

            {

                double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;

                AddText(btr, tr, "CH\u1ED0NG S\u00C9T\nSPD", new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0, 7);

                AddText(btr, tr, "SPD", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                extraCol++;

            }

            if (hasCapacitor)

            {

                double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;

                AddText(btr, tr, string.Format("T\u1EE4 B\u00D9\n{0} kVAR", panel.CapacitorBankKvar), new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerMetering, Math.PI / 2.0, 4);

                AddText(btr, tr, "CAP", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);

            }

        }

        #endregion

        private void DrawMctRings(BlockTableRecord btr, Transaction tr, Point3d center)

        {

            double r = 1.6;

            AddCircle(btr, tr, new Point3d(center.X, center.Y + 2.2, 0), r, LayerMetering, 4);

            AddCircle(btr, tr, new Point3d(center.X, center.Y, 0), r, LayerMetering, 4);

            AddCircle(btr, tr, new Point3d(center.X, center.Y - 2.2, 0), r, LayerMetering, 4);

        }

        private void DrawIncomingArrow(BlockTableRecord btr, Transaction tr, Point3d pt)

        {

            Polyline arrow = new Polyline(3);

            arrow.AddVertexAt(0, new Point2d(pt.X - 2.5, pt.Y + 4.0), 0, 0, 0);

            arrow.AddVertexAt(1, new Point2d(pt.X + 2.5, pt.Y + 4.0), 0, 0, 0);

            arrow.AddVertexAt(2, new Point2d(pt.X, pt.Y), 0, 0, 0);

            arrow.Closed = true;

            arrow.Layer = LayerSchematic;

            btr.AppendEntity(arrow);

            tr.AddNewlyCreatedDBObject(arrow, true);

            _currentCabinetEntities.Add(arrow.ObjectId);

        }

        private void DrawSideEarthing(BlockTableRecord btr, Transaction tr, Point3d pos)

        {

            AddRectangle(btr, tr, new Point3d(pos.X - 18.0, pos.Y + 4.0, 0), new Point3d(pos.X - 10.0, pos.Y - 4.0, 0), LayerSchematic, 7);

            AddText(btr, tr, "E", new Point3d(pos.X - 14.0, pos.Y, 0), 2.2, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddLine(btr, tr, new Point3d(pos.X - 10.0, pos.Y, 0), new Point3d(pos.X + 12.0, pos.Y, 0), LayerSchematic, 7);

            AddCircle(btr, tr, new Point3d(pos.X, pos.Y, 0), 0.7, LayerSchematic);

            double gx = pos.X + 12.0;

            AddLine(btr, tr, new Point3d(gx, pos.Y, 0), new Point3d(gx, pos.Y - 8.0, 0), LayerSchematic, 7);

            double gy = pos.Y - 8.0;

            AddLine(btr, tr, new Point3d(gx - 4.0, gy, 0), new Point3d(gx + 4.0, gy, 0), LayerSchematic, 7);

            AddLine(btr, tr, new Point3d(gx - 2.5, gy - 2.0, 0), new Point3d(gx + 2.5, gy - 2.0, 0), LayerSchematic, 7);

            AddLine(btr, tr, new Point3d(gx - 1.0, gy - 4.0, 0), new Point3d(gx + 1.0, gy - 4.0, 0), LayerSchematic, 7);

            AddText(btr, tr, "R < 4 OHM", new Point3d(gx + 2.0, gy - 6.0, 0), 2.2, AttachmentPoint.TopLeft, LayerText, 0);

        }

        private Entity AddLine(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, int colorIndex = 0, bool addToCabinetEntities = true)
        {
            Line line = new Line(p1, p2);
            line.Layer = layer;
            if (colorIndex > 0) line.ColorIndex = colorIndex;
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            if (addToCabinetEntities) _currentCabinetEntities.Add(line.ObjectId);
            return line;
        }

        private void AddOrthogonalWire(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, int colorIndex = 0, bool addToCabinetEntities = true)
        {
            if (Math.Abs(p1.X - p2.X) < 0.2)
            {
                AddLine(btr, tr, p1, p2, layer, colorIndex, addToCabinetEntities);
            }
            else if (Math.Abs(p1.Y - p2.Y) < 0.2)
            {
                AddLine(btr, tr, p1, p2, layer, colorIndex, addToCabinetEntities);
            }
            else
            {
                double midY = (p1.Y + p2.Y) / 2.0;
                Point3d pm1 = new Point3d(p1.X, midY, 0);
                Point3d pm2 = new Point3d(p2.X, midY, 0);
                AddLine(btr, tr, p1, pm1, layer, colorIndex, addToCabinetEntities);
                AddLine(btr, tr, pm1, pm2, layer, colorIndex, addToCabinetEntities);
                AddLine(btr, tr, pm2, p2, layer, colorIndex, addToCabinetEntities);
            }
        }

        private Entity AddRectangle(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, int colorIndex = 0, bool addToCabinetEntities = true)

        {

            Polyline pline = new Polyline(4);

            pline.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);

            pline.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);

            pline.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);

            pline.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);

            pline.Closed = true;

            pline.Layer = layer;

            if (colorIndex > 0) pline.ColorIndex = colorIndex;

            btr.AppendEntity(pline);

            tr.AddNewlyCreatedDBObject(pline, true);

            if (addToCabinetEntities) _currentCabinetEntities.Add(pline.ObjectId);

            return pline;

        }

        private Entity AddCircle(BlockTableRecord btr, Transaction tr, Point3d center, double radius, string layer, int colorIndex = 0, bool addToCabinetEntities = true)

        {

            Circle circle = new Circle(center, Vector3d.ZAxis, radius);

            circle.Layer = layer;

            if (colorIndex > 0) circle.ColorIndex = colorIndex;

            btr.AppendEntity(circle);

            tr.AddNewlyCreatedDBObject(circle, true);

            if (addToCabinetEntities) _currentCabinetEntities.Add(circle.ObjectId);

            return circle;

        }

        private Entity AddText(BlockTableRecord btr, Transaction tr, string text, Point3d pos, double height, AttachmentPoint align, string layer, double rotation = 0, int colorIndex = 0, bool addToCabinetEntities = true)

        {

            MText mtext = new MText();

            mtext.Contents = text;

            mtext.TextHeight = height;

            mtext.Attachment = align;

            mtext.Location = pos;

            mtext.Rotation = rotation;

            mtext.Layer = layer;

            if (colorIndex > 0)

            {

                mtext.ColorIndex = colorIndex;

            }

            btr.AppendEntity(mtext);

            tr.AddNewlyCreatedDBObject(mtext, true);

            if (addToCabinetEntities) _currentCabinetEntities.Add(mtext.ObjectId);

            return mtext;

        }

        private void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)

        {

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))

            {

                lt.UpgradeOpen();

                LayerTableRecord ltr = new LayerTableRecord();

                ltr.Name = layerName;

                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);

                lt.Add(ltr);

                tr.AddNewlyCreatedDBObject(ltr, true);

            }

        }

        /// <summary>

        /// Tìm tên tủ điện từ một Entity bất kỳ được chọn hoặc double-click

        /// </summary>

        public static string GetPanelNameFromEntity(Entity ent, Transaction tr, Database db)

        {

            if (ent == null) return null;

            // 1. Kiểm tra XData TAIAI_PANEL trực tiếp trên đối tượng

            ResultBuffer rb = ent.GetXDataForApplication(PanelStorageService.RegAppName);

            if (rb != null)

            {

                TypedValue[] tvs = rb.AsArray();

                foreach (TypedValue tv in tvs)

                {

                    if (tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString && !tv.Value.ToString().StartsWith("LOAD:"))

                    {

                        return tv.Value.ToString();

                    }

                }

            }

            // 2. Kiểm tra nếu đối tượng thuộc Group TAIAI_PANEL_*

            ObjectIdCollection groupIds = ent.GetPersistentReactorIds();

            if (groupIds != null)

            {

                foreach (ObjectId gid in groupIds)

                {

                    if (gid.IsValid && !gid.IsNull && !gid.IsErased)

                    {

                        Group grp = tr.GetObject(gid, OpenMode.ForRead) as Group;

                        if (grp != null && !string.IsNullOrEmpty(grp.Name) && grp.Name.StartsWith("TAIAI_PANEL_"))

                        {

                            string pName = grp.Name.Substring("TAIAI_PANEL_".Length).Replace("_", "-");

                            return pName;

                        }

                    }

                }

            }

            return null;

        }

        /// <summary>

        /// Thử khôi phục toàn bộ đối tượng ElectricalPanel từ XData lưu trên bản vẽ CAD

        /// </summary>

        public static ElectricalPanel RestorePanelFromDrawing(string panelName, Transaction tr, Database db)

        {

            if (string.IsNullOrEmpty(panelName)) return null;

            DBDictionary groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);

            string cleanName = "TAIAI_PANEL_" + panelName.Replace("-", "_").Replace(" ", "_");

            if (groupDict.Contains(cleanName))

            {

                ObjectId gid = groupDict.GetAt(cleanName);

                Group grp = tr.GetObject(gid, OpenMode.ForRead) as Group;

                if (grp != null)

                {

                    ObjectId[] ids = grp.GetAllEntityIds();

                    foreach (ObjectId id in ids)

                    {

                        if (id.IsValid && !id.IsNull && !id.IsErased)

                        {

                            DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                            ResultBuffer rb = obj.GetXDataForApplication(PanelStorageService.RegAppName);

                            if (rb != null)

                            {

                                Point3d pt;

                                ElectricalPanel panel = PanelStorageService.DeserializePanelFromXData(rb, out pt);

                                if (panel != null && panel.Loads.Count > 0)

                                {

                                    return panel;

                                }

                            }

                        }

                    }

                }

            }

            return null;

        }

        #region New Technical Features: ATS, Capacitor Bank, Front View, Title Block, Riser

        private void DrawCapacitorBankBranch(BlockTableRecord btr, Transaction tr, double cx, double busY, double cabBot, double kvar, int steps)

        {

            double cbY = cabBot + 38.0;

            double kmY = cabBot + 24.0;

            double capY = cabBot + 9.0;

            // Dây từ thanh cái xuống CB tụ bù

            AddLine(btr, tr, new Point3d(cx, busY, 0), new Point3d(cx, cbY + 4.0, 0), LayerSchematic, 7);

            // Aptomat bảo vệ tụ bù

            AddRectangle(btr, tr, new Point3d(cx - 2.5, cbY + 4.0, 0), new Point3d(cx + 2.5, cbY - 4.0, 0), LayerSchematic, 7);

            AddLine(btr, tr, new Point3d(cx - 2.5, cbY - 4.0, 0), new Point3d(cx + 2.5, cbY + 4.0, 0), LayerSchematic, 7);

            AddText(btr, tr, "MCCB 3P", new Point3d(cx - 3.0, cbY, 0), 1.5, AttachmentPoint.MiddleRight, LayerText, 0);

            // Dây từ CB xuống Contactor

            AddLine(btr, tr, new Point3d(cx, cbY - 4.0, 0), new Point3d(cx, kmY + 3.0, 0), LayerSchematic, 7);

            // Contactor KM

            AddRectangle(btr, tr, new Point3d(cx - 2.5, kmY + 3.0, 0), new Point3d(cx + 2.5, kmY - 3.0, 0), LayerSchematic, 7);

            AddText(btr, tr, "KM", new Point3d(cx, kmY, 0), 1.5, AttachmentPoint.MiddleCenter, LayerText, 0);

            // Dây xuống khối tụ bù

            AddLine(btr, tr, new Point3d(cx, kmY - 3.0, 0), new Point3d(cx, capY + 3.5, 0), LayerSchematic, 7);

            // Khối tụ bù màu Cyan

            AddRectangle(btr, tr, new Point3d(cx - 6.0, capY + 3.5, 0), new Point3d(cx + 6.0, capY - 3.5, 0), LayerMetering, 4);

            AddText(btr, tr, string.Format("T\u1EE4 B\u00D9 {0} kVAR", kvar), new Point3d(cx, capY + 1.0, 0), 1.4, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);

            AddText(btr, tr, string.Format("({0} C\u1EA4P B\u00D9)", steps), new Point3d(cx, capY - 1.5, 0), 1.2, AttachmentPoint.MiddleCenter, LayerText, 0);

        }

        private void DrawDualAtsIncomer(BlockTableRecord btr, Transaction tr, double incomingX, double incomingTop, double topBranchY, Point3d worldMccbTop, ElectricalPanel panel)

        {

            double src1X = incomingX - 9.0;

            double src2X = incomingX + 9.0;

            double atsTopY = topBranchY + 14.0;

            double atsBotY = topBranchY + 2.0;

            // Nguồn 1 EVN

            DrawIncomingArrow(btr, tr, new Point3d(src1X, incomingTop, 0));

            AddText(btr, tr, "NGU\u1ED2N 1: L\u01AF\u1EDAI EVN", new Point3d(src1X, incomingTop + 6.0, 0), 2.2, AttachmentPoint.BottomCenter, LayerText, 0);

            AddLine(btr, tr, new Point3d(src1X, incomingTop, 0), new Point3d(src1X, atsTopY, 0), LayerSchematic, 7);

            // Nguồn 2 GEN

            DrawIncomingArrow(btr, tr, new Point3d(src2X, incomingTop, 0));

            AddText(btr, tr, "NGU\u1ED2N 2: M\u00C1Y PH\u00C1T (GEN)", new Point3d(src2X, incomingTop + 6.0, 0), 2.2, AttachmentPoint.BottomCenter, LayerText, 0);

            AddLine(btr, tr, new Point3d(src2X, incomingTop, 0), new Point3d(src2X, atsTopY, 0), LayerSchematic, 7);

            // ATS Box

            AddRectangle(btr, tr, new Point3d(incomingX - 13.0, atsTopY, 0), new Point3d(incomingX + 13.0, atsBotY, 0), LayerMetering, 4);

            AddText(btr, tr, "B\u1ED8 CHUY\u1EC2N NGU\u1ED2N T\u1EF0 \u0110\u1ED8NG ATS", new Point3d(incomingX, (atsTopY + atsBotY) / 2.0 + 1.5, 0), 2.0, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);

            AddText(btr, tr, "(AUTOMATIC TRANSFER SWITCH)", new Point3d(incomingX, (atsTopY + atsBotY) / 2.0 - 1.5, 0), 1.5, AttachmentPoint.MiddleCenter, LayerText, 0);

            // Dây ngõ ra từ ATS xuống cực trên MCCB Tổng

            AddLine(btr, tr, new Point3d(incomingX, atsBotY, 0), worldMccbTop, LayerSchematic, 7);

        }

        private void DrawDualInterlockIncomer(BlockTableRecord btr, Transaction tr, double incomingX, double incomingTop, double topBranchY, Point3d worldMccbTop, ElectricalPanel panel, DrawingParameters p)

        {

            double src1X = incomingX - 8.0;

            double src2X = incomingX + 8.0;

            double cbY = worldMccbTop.Y + 4.0;

            // Nguồn 1 EVN (Tương tác tự do)
            DrawIncomingArrow(btr, tr, new Point3d(src1X, incomingTop, 0));
            AddText(btr, tr, "NGU\u1ED2N 1 (EVN)", new Point3d(src1X, incomingTop + 5.0, 0), 2.0, AttachmentPoint.BottomCenter, LayerText, 0);

            SldInteractiveItem evnCustom = (p != null) ? p.FindCustomItem("INTERLOCK_CB_EVN") : null;
            double evnCadOffX = 0, evnCadOffY = 0, evnScale = 1.0;
            if (evnCustom != null && !evnCustom.IsDeleted)
            {
                evnCadOffX = evnCustom.OffsetX / 2.8;
                evnCadOffY = -evnCustom.OffsetY / 2.8;
                evnScale = evnCustom.Scale;
            }
            Point3d actualEvnPt = new Point3d(src1X + evnCadOffX, cbY + evnCadOffY, 0);

            AddOrthogonalWire(btr, tr, new Point3d(src1X, incomingTop, 0), new Point3d(actualEvnPt.X, actualEvnPt.Y + 4.0 * evnScale, 0), LayerSchematic, 7);
            AddRectangle(btr, tr, new Point3d(actualEvnPt.X - 2.5 * evnScale, actualEvnPt.Y + 4.0 * evnScale, 0), new Point3d(actualEvnPt.X + 2.5 * evnScale, actualEvnPt.Y - 4.0 * evnScale, 0), LayerSchematic, 7);
            AddLine(btr, tr, new Point3d(actualEvnPt.X - 2.5 * evnScale, actualEvnPt.Y - 4.0 * evnScale, 0), new Point3d(actualEvnPt.X + 2.5 * evnScale, actualEvnPt.Y + 4.0 * evnScale, 0), LayerSchematic, 7);

            // Nguồn 2 GEN (Tương tác tự do)
            DrawIncomingArrow(btr, tr, new Point3d(src2X, incomingTop, 0));
            AddText(btr, tr, "NGU\u1ED2N 2 (M\u00C1Y PH\u00C1T)", new Point3d(src2X, incomingTop + 5.0, 0), 2.0, AttachmentPoint.BottomCenter, LayerText, 0);

            SldInteractiveItem genCustom = (p != null) ? p.FindCustomItem("INTERLOCK_CB_GEN") : null;
            double genCadOffX = 0, genCadOffY = 0, genScale = 1.0;
            if (genCustom != null && !genCustom.IsDeleted)
            {
                genCadOffX = genCustom.OffsetX / 2.8;
                genCadOffY = -genCustom.OffsetY / 2.8;
                genScale = genCustom.Scale;
            }
            Point3d actualGenPt = new Point3d(src2X + genCadOffX, cbY + genCadOffY, 0);

            AddOrthogonalWire(btr, tr, new Point3d(src2X, incomingTop, 0), new Point3d(actualGenPt.X, actualGenPt.Y + 4.0 * genScale, 0), LayerSchematic, 7);
            AddRectangle(btr, tr, new Point3d(actualGenPt.X - 2.5 * genScale, actualGenPt.Y + 4.0 * genScale, 0), new Point3d(actualGenPt.X + 2.5 * genScale, actualGenPt.Y - 4.0 * genScale, 0), LayerSchematic, 7);
            AddLine(btr, tr, new Point3d(actualGenPt.X - 2.5 * genScale, actualGenPt.Y - 4.0 * genScale, 0), new Point3d(actualGenPt.X + 2.5 * genScale, actualGenPt.Y + 4.0 * genScale, 0), LayerSchematic, 7);

            // Khóa liên động

            AddLine(btr, tr, new Point3d(src1X + 2.5, cbY, 0), new Point3d(src2X - 2.5, cbY, 0), LayerControl, 3);

            AddText(btr, tr, "INTERLOCK", new Point3d(incomingX, cbY + 2.0, 0), 1.5, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);

            // Gộp 2 đầu ra xuống

            double joinY = cbY - 9.0;

            AddLine(btr, tr, new Point3d(src1X, cbY - 4.0, 0), new Point3d(src1X, joinY, 0), LayerSchematic, 7);

            AddLine(btr, tr, new Point3d(src2X, cbY - 4.0, 0), new Point3d(src2X, joinY, 0), LayerSchematic, 7);

            AddLine(btr, tr, new Point3d(src1X, joinY, 0), new Point3d(src2X, joinY, 0), LayerSchematic, 7);

            AddCircle(btr, tr, new Point3d(incomingX, joinY, 0), 0.7, LayerSchematic);

            AddLine(btr, tr, new Point3d(incomingX, joinY, 0), worldMccbTop, LayerSchematic, 7);

        }

                public static string FindTitleBlockDwgPath()

        {

            string p1 = @"C:\Users\ADMIN\Desktop\HT - Khung ten A3-HE DIEN.dwg";

            if (System.IO.File.Exists(p1)) return p1;

            string p2 = @"C:\Users\ADMIN\AppData\Roaming\Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Windows\HT_Khung_ten_A3_HE_DIEN.dwg";

            if (System.IO.File.Exists(p2)) return p2;

            string p3 = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Windows\HT_Khung_ten_A3_HE_DIEN.dwg";

            if (System.IO.File.Exists(p3)) return p3;

            string p4 = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thu Viện\Điện\HT - Khung ten A3-HE DIEN.dwg";

            if (System.IO.File.Exists(p4)) return p4;

            return null;

        }

        private void InsertTitleBlockA3(Database db, BlockTableRecord btr, Transaction tr, Point3d insertPt, double scale, DrawingParameters p, ElectricalPanel panel, string drawingTitle, string sheetNo)

        {

            string blockName = "HT_KHUNG_TEN_A3_HE_DIEN";

            ObjectId blockId = ObjectId.Null;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            if (bt.Has(blockName))

            {

                blockId = bt[blockName];

            }

            else

            {

                string dwgPath = FindTitleBlockDwgPath();

                if (!string.IsNullOrEmpty(dwgPath) && System.IO.File.Exists(dwgPath))

                {

                    try

                    {

                        using (Database sourceDb = new Database(false, true))

                        {

                            sourceDb.ReadDwgFile(dwgPath, System.IO.FileShare.ReadWrite, true, "");

                            blockId = db.Insert(blockName, sourceDb, true);

                        }

                    }

                    catch (Exception)

                    {

                        blockId = ObjectId.Null;

                    }

                }

            }

            if (!blockId.IsNull)

            {

                BlockReference br = new BlockReference(insertPt, blockId);

                br.ScaleFactors = new Scale3d(scale, scale, 1.0);

                br.Layer = LayerBorder;

                btr.AppendEntity(br);

                tr.AddNewlyCreatedDBObject(br, true);

                _currentCabinetEntities.Add(br.ObjectId);

                double s = scale;

                double ox = insertPt.X;

                double oy = insertPt.Y;

                string proj = !string.IsNullOrEmpty(p.ProjectName) ? p.ProjectName : "CÔNG TRÌNH CƠ ĐIỆN TÒA NHÀ & NHÀ XƯỞNG";

                AddText(btr, tr, proj, new Point3d(ox + 388.5 * s, oy + 53.0 * s, 0), 2.2 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

                string title = !string.IsNullOrEmpty(drawingTitle) ? drawingTitle : (!string.IsNullOrEmpty(p.DrawingTitle) ? p.DrawingTitle : "SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN");

                AddText(btr, tr, title, new Point3d(ox + 388.5 * s, oy + 35.0 * s, 0), 2.4 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

                AddText(btr, tr, string.Format("TỦ ĐIỆN: {0}", panel.PanelName), new Point3d(ox + 388.5 * s, oy + 43.5 * s, 0), 1.9 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

                string drawnBy = !string.IsNullOrEmpty(p.DrawnBy) ? p.DrawnBy : "NGUYỄN TẤN TÀI";

                AddText(btr, tr, drawnBy, new Point3d(ox + 377.5 * s, oy + 64.5 * s, 0), 1.6 * s, AttachmentPoint.MiddleLeft, LayerText, 0);

                string desBy = !string.IsNullOrEmpty(p.DesignerName) ? p.DesignerName : "NTT TOOLS";

                AddText(btr, tr, desBy, new Point3d(ox + 377.5 * s, oy + 69.5 * s, 0), 1.6 * s, AttachmentPoint.MiddleLeft, LayerText, 0);

                string chkBy = !string.IsNullOrEmpty(p.CheckedBy) ? p.CheckedBy : "CAO VĂN SIÊU";

                AddText(btr, tr, chkBy, new Point3d(ox + 377.5 * s, oy + 74.4 * s, 0), 1.6 * s, AttachmentPoint.MiddleLeft, LayerText, 0);

                string appBy = !string.IsNullOrEmpty(p.ApprovedBy) ? p.ApprovedBy : "DƯƠNG CAO SƠN";

                AddText(btr, tr, appBy, new Point3d(ox + 377.5 * s, oy + 79.4 * s, 0), 1.6 * s, AttachmentPoint.MiddleLeft, LayerText, 0);

                string sc = !string.IsNullOrEmpty(p.DrawingScale) ? p.DrawingScale : (s > 1.05 ? string.Format("1:{0:0}", s * 25.0) : "1:25");

                AddText(btr, tr, sc, new Point3d(ox + 372.0 * s, oy + 8.7 * s, 0), 1.6 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

                string dwgNo = !string.IsNullOrEmpty(sheetNo) ? sheetNo : (!string.IsNullOrEmpty(p.DrawingNumber) ? p.DrawingNumber : "EL-01");

                AddText(btr, tr, dwgNo, new Point3d(ox + 372.0 * s, oy + 13.7 * s, 0), 1.8 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

                AddText(btr, tr, DateTime.Now.ToString("dd/MM/yyyy"), new Point3d(ox + 372.5 * s, oy + 191.1 * s, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

                string rev = !string.IsNullOrEmpty(p.Revision) ? p.Revision : "00";

                AddText(btr, tr, rev, new Point3d(ox + 406.6 * s, oy + 13.7 * s, 0), 1.6 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

            }

            else

            {

                DrawFallbackTitleBlock(btr, tr, insertPt, scale, p, panel, drawingTitle, sheetNo);

            }

        }

        private void DrawFallbackTitleBlock(BlockTableRecord btr, Transaction tr, Point3d insertPt, double scale, DrawingParameters p, ElectricalPanel panel, string drawingTitle, string sheetNo)

        {

            double w = 420.0 * scale;

            double h = 297.0 * scale;

            double x1 = insertPt.X;

            double y1 = insertPt.Y;

            double x2 = x1 + w;

            double y2 = y1 + h;

            AddRectangle(btr, tr, new Point3d(x1, y2, 0), new Point3d(x2, y1, 0), LayerBorder, 7);

            AddRectangle(btr, tr, new Point3d(x1 + 10.0 * scale, y2 - 5.0 * scale, 0), new Point3d(x2 - 5.0 * scale, y1 + 5.0 * scale, 0), LayerBorder, 7);

            double tbW = 95.0 * scale;

            double tbH = 36.0 * scale;

            double tbX1 = x2 - 5.0 * scale - tbW;

            double tbY1 = y1 + 5.0 * scale;

            AddRectangle(btr, tr, new Point3d(tbX1, tbY1 + tbH, 0), new Point3d(x2 - 5.0 * scale, tbY1, 0), LayerBorder, 7);

            AddText(btr, tr, "NTT TOOLS - MEP PRO", new Point3d(tbX1 + tbW / 2.0, tbY1 + tbH - 6.0 * scale, 0), 2.2 * scale, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddText(btr, tr, string.Format("DỰ ÁN: {0}", p.ProjectName ?? "CÔNG TRÌNH MEP"), new Point3d(tbX1 + 4.0 * scale, tbY1 + tbH - 14.0 * scale, 0), 1.6 * scale, AttachmentPoint.MiddleLeft, LayerText, 0);

            AddText(btr, tr, string.Format("BẢN VẼ: {0}", drawingTitle ?? "SƠ ĐỒ NGUYÊN LÝ"), new Point3d(tbX1 + 4.0 * scale, tbY1 + tbH - 22.0 * scale, 0), 1.8 * scale, AttachmentPoint.MiddleLeft, LayerText, 0);

            AddText(btr, tr, string.Format("SỐ BV: {0}  |  TỶ LỆ: {1}", sheetNo ?? "EL-01", p.DrawingScale ?? "1:25"), new Point3d(tbX1 + 4.0 * scale, tbY1 + 5.0 * scale, 0), 1.5 * scale, AttachmentPoint.MiddleLeft, LayerText, 0);

        }

        private void DrawLegendTable(Database db, BlockTableRecord currentSpace, Transaction tr, Point3d tableOrigin, ElectricalPanel panel, DrawingParameters p, double scale)

        {

            string cleanName = panel.PanelName.Replace("-", "_").Replace(" ", "_");

            string blockName = "TAIAI_LEGEND_" + cleanName;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            BlockTableRecord btrLegend;

            if (bt.Has(blockName))

            {

                btrLegend = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForWrite);

                foreach (ObjectId id in btrLegend)

                {

                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite);

                    obj.Erase();

                }

            }

            else

            {

                btrLegend = new BlockTableRecord();

                btrLegend.Name = blockName;

                btrLegend.Origin = Point3d.Origin;

                bt.Add(btrLegend);

                tr.AddNewlyCreatedDBObject(btrLegend, true);

            }

            // Tọa độ gốc bên trong Block: (0,0) là góc trên bên trái

            double ox = 0.0;

            double topY = 0.0;

            double s = Math.Max(0.85, Math.Min(1.3, scale));

            double tblW = 98.0 * s;

            double col1W = 20.0 * s;

            double col2W = 28.0 * s;

            double col3W = tblW - col1W - col2W;

            double x0 = ox;

            double x1 = x0 + col1W;

            double x2 = x1 + col2W;

            double x3 = x0 + tblW;

            double rowH = 7.5 * s;

            double titleH = 7.0 * s;

            double hdrH = 5.5 * s;

            bool has1P = false;

            bool hasDol = false;

            bool hasStarDelta = false;

            bool hasVfd = false;

            bool hasSs = false;

            bool hasHoa = false;

            bool hasFsPs = false;

            bool hasRccb = false;

            if (panel.Loads != null)

            {

                foreach (ElectricalLoad ld in panel.Loads)

                {

                    if (ld.Phase == PhaseType.SinglePhase) has1P = true;

                    if (ld.Starter == StarterType.DOL || ld.HasDol) hasDol = true;

                    if (ld.Starter == StarterType.StarDelta) hasStarDelta = true;

                    if (ld.Starter == StarterType.VFD) hasVfd = true;

                    if (ld.Starter == StarterType.SoftStarter) hasSs = true;

                    if (ld.Control == ControlMode.AutoManual) hasHoa = true;

                    if (ld.Control == ControlMode.FloatSwitch || ld.Control == ControlMode.PressureSwitch) hasFsPs = true;

                    if (!string.IsNullOrEmpty(ld.BreakerSpec) && (ld.BreakerSpec.ToUpper().Contains("RCCB") || ld.BreakerSpec.ToUpper().Contains("RCBO"))) hasRccb = true;

                }

            }

            var items = new List<Tuple<string, string, string, Action<double, double>>>();

            items.Add(new Tuple<string, string, string, Action<double, double>>(

                "MCCB",

                "Aptomat khối (MCCB 3P)",

                "Đóng cắt, bảo vệ quá tải & ngắn mạch nguồn tổng.",

                (cx, cy) => {

                    AddCircle(btrLegend, tr, new Point3d(cx, cy + 2.2 * s, 0), 0.6 * s, LayerSchematic, 0, false);

                    AddCircle(btrLegend, tr, new Point3d(cx, cy - 2.2 * s, 0), 0.6 * s, LayerSchematic, 0, false);

                    AddLine(btrLegend, tr, new Point3d(cx, cy + 1.6 * s, 0), new Point3d(cx + 2.0 * s, cy - 1.6 * s, 0), LayerSchematic, 7, false);

                }

            ));

            if (has1P)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "MCB",

                    "Aptomat tép (MCB 1P/3P)",

                    "Bảo vệ đóng ngắt lộ tải phân phối nhánh.",

                    (cx, cy) => {

                        AddCircle(btrLegend, tr, new Point3d(cx, cy + 1.8 * s, 0), 0.5 * s, LayerLoad1P, 3, false);

                        AddCircle(btrLegend, tr, new Point3d(cx, cy - 1.8 * s, 0), 0.5 * s, LayerLoad1P, 3, false);

                        AddLine(btrLegend, tr, new Point3d(cx, cy + 1.6 * s, 0), new Point3d(cx + 1.8 * s, cy - 1.6 * s, 0), LayerLoad1P, 3, false);

                    }

                ));

            }

            if (hasRccb)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "RCCB/RCBO",

                    "Aptomat chống rò (30mA)",

                    "Bảo vệ chống dòng rò đất (30mA), quá tải & ngắn mạch.",

                    (cx, cy) => {

                        AddCircle(btrLegend, tr, new Point3d(cx, cy + 1.8 * s, 0), 0.5 * s, LayerSchematic, 30, false);

                        AddCircle(btrLegend, tr, new Point3d(cx, cy - 1.8 * s, 0), 0.5 * s, LayerSchematic, 30, false);

                        AddLine(btrLegend, tr, new Point3d(cx, cy + 1.6 * s, 0), new Point3d(cx + 1.8 * s, cy - 1.6 * s, 0), LayerSchematic, 30, false);

                        AddText(btrLegend, tr, "\u0394I", new Point3d(cx + 2.4 * s, cy, 0), 1.2 * s, AttachmentPoint.MiddleLeft, LayerText, 0, 30, false);

                    }

                ));

            }

            if (hasDol)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "DOL",

                    "Khởi động trực tiếp (DOL)",

                    "Cụm Contactor + Rơ le nhiệt bảo vệ động cơ.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 5.5 * s, cy + 2.5 * s, 0), new Point3d(cx + 5.5 * s, cy - 2.5 * s, 0), LayerSchematic, 1, false);

                        AddText(btrLegend, tr, "DOL", new Point3d(cx, cy, 0), 1.6 * s, AttachmentPoint.MiddleCenter, LayerSchematic, 0, 1, false);

                    }

                ));

            }

            if (hasStarDelta)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "Y - Δ",

                    "Khởi động Sao - Tam giác",

                    "Giảm dòng khởi động động cơ công suất lớn.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 6.0 * s, cy + 2.5 * s, 0), new Point3d(cx + 6.0 * s, cy - 2.5 * s, 0), LayerSchematic, 1, false);

                        AddText(btrLegend, tr, "Y-Δ", new Point3d(cx, cy, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerSchematic, 0, 1, false);

                    }

                ));

            }

            if (hasVfd)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "VFD",

                    "Biến tần (Inverter/VFD)",

                    "Điều khiển vô cấp tốc độ động cơ theo tải.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 5.5 * s, cy + 2.5 * s, 0), new Point3d(cx + 5.5 * s, cy - 2.5 * s, 0), LayerSchematic, 6, false);

                        AddText(btrLegend, tr, "VFD", new Point3d(cx, cy, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerSchematic, 0, 6, false);

                    }

                ));

            }

            if (hasSs)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "SS",

                    "Khởi động mềm (Soft Starter)",

                    "Tăng áp dần đều tránh sụt áp lưới và búa nước.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 5.5 * s, cy + 2.5 * s, 0), new Point3d(cx + 5.5 * s, cy - 2.5 * s, 0), LayerSchematic, 2, false);

                        AddText(btrLegend, tr, "SS", new Point3d(cx, cy, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerSchematic, 0, 2, false);

                    }

                ));

            }

            if (p.IncludeFuse)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "FUSE",

                    "Cầu chì đo lường (3x2A)",

                    "Bảo vệ mạch đo lường và tín hiệu đèn báo.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 4.5 * s, cy + 1.8 * s, 0), new Point3d(cx + 4.5 * s, cy - 1.8 * s, 0), LayerMetering, 4, false);

                        AddLine(btrLegend, tr, new Point3d(cx - 5.0 * s, cy, 0), new Point3d(cx + 5.0 * s, cy, 0), LayerMetering, 4, false);

                    }

                ));

            }

            if (p.IncludeLamps)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "ĐÈN BÁO",

                    "Đèn báo pha R-Y-B",

                    "Chỉ thị có điện trên các pha thanh cái chính.",

                    (cx, cy) => {

                        AddCircle(btrLegend, tr, new Point3d(cx - 3.2 * s, cy, 0), 1.1 * s, LayerSchematic, 1, false);

                        AddCircle(btrLegend, tr, new Point3d(cx, cy, 0), 1.1 * s, LayerSchematic, 2, false);

                        AddCircle(btrLegend, tr, new Point3d(cx + 3.2 * s, cy, 0), 1.1 * s, LayerSchematic, 5, false);

                    }

                ));

            }

            if (p.IncludeMct)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "3xMCT",

                    "Biến dòng đo lường (CT)",

                    "Biến đổi dòng sơ cấp sang 5A cho đồng hồ đo.",

                    (cx, cy) => {

                        AddCircle(btrLegend, tr, new Point3d(cx - 2.2 * s, cy, 0), 1.4 * s, LayerSchematic, 0, false);

                        AddCircle(btrLegend, tr, new Point3d(cx, cy, 0), 1.4 * s, LayerSchematic, 0, false);

                        AddCircle(btrLegend, tr, new Point3d(cx + 2.2 * s, cy, 0), 1.4 * s, LayerSchematic, 0, false);

                    }

                ));

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "PM",

                    "Đồng hồ đa năng (Digital PM)",

                    "Đo và hiển thị V, A, kW, kVA, kWh, cosφ.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 4.5 * s, cy + 2.2 * s, 0), new Point3d(cx + 4.5 * s, cy - 2.2 * s, 0), LayerMetering, 4, false);

                        AddText(btrLegend, tr, "PM", new Point3d(cx, cy, 0), 1.4 * s, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4, false);

                    }

                ));

            }

            if (p.IncludeRelay)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "RELAY",

                    "Rơ-le bảo vệ số",

                    "Bảo vệ quá áp, thấp áp, chạm đất, quá dòng.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 5.5 * s, cy + 2.2 * s, 0), new Point3d(cx + 5.5 * s, cy - 2.2 * s, 0), LayerControl, 3, false);

                        AddText(btrLegend, tr, "OV/UV", new Point3d(cx, cy, 0), 1.2 * s, AttachmentPoint.MiddleCenter, LayerControl, 0, 3, false);

                    }

                ));

            }

            if (p.IncludeSpd)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "SPD",

                    "Chống sét lan truyền",

                    "Triệt tiêu xung quá điện áp đột biến.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 5.0 * s, cy + 2.0 * s, 0), new Point3d(cx + 5.0 * s, cy - 2.0 * s, 0), LayerControl, 6, false);

                        AddText(btrLegend, tr, "SPD", new Point3d(cx, cy, 0), 1.3 * s, AttachmentPoint.MiddleCenter, LayerControl, 0, 6, false);

                    }

                ));

            }

            if (panel.HasCapacitorBank)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "TỤ BÙ",

                    "Tụ bù công suất phản kháng",

                    "Tự động bù cosφ >= 0.95, giảm tiền phạt EVN.",

                    (cx, cy) => {

                        AddLine(btrLegend, tr, new Point3d(cx - 3.0 * s, cy + 2.0 * s, 0), new Point3d(cx - 3.0 * s, cy - 2.0 * s, 0), LayerSchematic, 5, false);

                        AddLine(btrLegend, tr, new Point3d(cx - 1.5 * s, cy + 2.0 * s, 0), new Point3d(cx - 1.5 * s, cy - 2.0 * s, 0), LayerSchematic, 5, false);

                        AddLine(btrLegend, tr, new Point3d(cx + 1.5 * s, cy + 2.0 * s, 0), new Point3d(cx + 1.5 * s, cy - 2.0 * s, 0), LayerSchematic, 5, false);

                        AddLine(btrLegend, tr, new Point3d(cx + 3.0 * s, cy + 2.0 * s, 0), new Point3d(cx + 3.0 * s, cy - 2.0 * s, 0), LayerSchematic, 5, false);

                    }

                ));

            }

            if (hasHoa)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "A-O-M",

                    "Chuyển mạch 3 vị trí (A-O-M)",

                    "Lựa chọn chế độ điều khiển Tay - Dừng - Tự động.",

                    (cx, cy) => {

                        AddRectangle(btrLegend, tr, new Point3d(cx - 5.0 * s, cy + 2.0 * s, 0), new Point3d(cx + 5.0 * s, cy - 2.0 * s, 0), LayerSchematic, 7, false);

                        AddText(btrLegend, tr, "A-O-M", new Point3d(cx, cy, 0), 1.0 * s, AttachmentPoint.MiddleCenter, LayerSchematic, 0, 7, false);

                    }

                ));

            }

            if (hasFsPs)

            {

                items.Add(new Tuple<string, string, string, Action<double, double>>(

                    "FS / PS",

                    "Tín hiệu cảm biến (Phao/Áp)",

                    "Tiếp điểm liên động phao báo mức hoặc áp suất.",

                    (cx, cy) => {

                        AddCircle(btrLegend, tr, new Point3d(cx, cy, 0), 1.8 * s, LayerControl, 3, false);

                        AddText(btrLegend, tr, "FS", new Point3d(cx, cy, 0), 1.3 * s, AttachmentPoint.MiddleCenter, LayerControl, 0, 3, false);

                    }

                ));

            }

            int nRows = items.Count;

            double totalH = titleH + hdrH + nRows * rowH;

            double botY = topY - totalH;

            AddRectangle(btrLegend, tr, new Point3d(x0, topY, 0), new Point3d(x3, botY, 0), LayerBorder, 7, false);

            AddLine(btrLegend, tr, new Point3d(x0, topY - titleH, 0), new Point3d(x3, topY - titleH, 0), LayerBorder, 7, false);

            AddText(btrLegend, tr, "BẢNG CHÚ THÍCH KÝ HIỆU (LEGEND)", new Point3d(x0 + tblW / 2.0, topY - titleH / 2.0, 0), 2.0 * s, AttachmentPoint.MiddleCenter, LayerText, 0, 4, false);

            double yHdr = topY - titleH - hdrH;

            AddLine(btrLegend, tr, new Point3d(x0, yHdr, 0), new Point3d(x3, yHdr, 0), LayerBorder, 7, false);

            AddLine(btrLegend, tr, new Point3d(x1, topY - titleH, 0), new Point3d(x1, botY, 0), LayerBorder, 7, false);

            AddLine(btrLegend, tr, new Point3d(x2, topY - titleH, 0), new Point3d(x2, botY, 0), LayerBorder, 7, false);

            AddText(btrLegend, tr, "KÝ HIỆU", new Point3d((x0 + x1) / 2.0, topY - titleH - hdrH / 2.0, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerText, 0, 0, false);

            AddText(btrLegend, tr, "THIẾT BỊ", new Point3d((x1 + x2) / 2.0, topY - titleH - hdrH / 2.0, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerText, 0, 0, false);

            AddText(btrLegend, tr, "CHỨC NĂNG KỸ THUẬT", new Point3d((x2 + x3) / 2.0, topY - titleH - hdrH / 2.0, 0), 1.5 * s, AttachmentPoint.MiddleCenter, LayerText, 0, 0, false);

            for (int r = 0; r < nRows; r++)

            {

                double rowTop = yHdr - r * rowH;

                double rowBot = rowTop - rowH;

                double midY = (rowTop + rowBot) / 2.0;

                if (r < nRows - 1)

                {

                    AddLine(btrLegend, tr, new Point3d(x0, rowBot, 0), new Point3d(x3, rowBot, 0), LayerBorder, 8, false);

                }

                var item = items[r];

                item.Item4((x0 + x1) / 2.0, midY);

                AddText(btrLegend, tr, item.Item1, new Point3d(x1 + 2.0 * s, midY + 1.2 * s, 0), 1.5 * s, AttachmentPoint.MiddleLeft, LayerText, 0, 0, false);

                AddText(btrLegend, tr, item.Item2, new Point3d(x1 + 2.0 * s, midY - 1.2 * s, 0), 1.2 * s, AttachmentPoint.MiddleLeft, LayerText, 0, 8, false);

                AddText(btrLegend, tr, item.Item3, new Point3d(x2 + 2.0 * s, midY, 0), 1.2 * s, AttachmentPoint.MiddleLeft, LayerText, 0, 0, false);

            }

            // Chèn BlockReference vào ModelSpace tại vị trí tableOrigin

            // Đối tượng Block này cho phép người dùng click chọn 1 lần và kéo di chuyển (grip drag) tự do trên CAD

            BlockReference brLegend = new BlockReference(tableOrigin, btrLegend.Id);

            brLegend.Layer = LayerBorder;

            currentSpace.AppendEntity(brLegend);

            tr.AddNewlyCreatedDBObject(brLegend, true);

        }

        private void DrawFrontViewEnclosureSheet(Database db, BlockTableRecord btr, Transaction tr, Point3d origin, double scale, DrawingParameters p, ElectricalPanel panel)

        {

            if (p.IncludeTitleBlock)

            {

                InsertTitleBlockA3(db, btr, tr, origin, scale, p, panel, 

                    "MẶT NGOÀI & KÍCH THƯỚC VỎ TỦ ĐIỆN " + panel.PanelName, 

                    !string.IsNullOrEmpty(p.DrawingNumber) ? (p.DrawingNumber + "-02") : "EL-02");

            }

            double s = scale;

            double sheetCenterX = origin.X + 175.0 * s;

            double sheetCenterY = origin.Y + 145.0 * s;

            ElectricalCalculator.EstimateEnclosureDimensions(panel);

            double estH = panel.EstimatedHeight > 0 ? panel.EstimatedHeight : 1400.0;

            double estW = panel.EstimatedWidth > 0 ? panel.EstimatedWidth : 800.0;

            double estD = panel.EstimatedDepth > 0 ? panel.EstimatedDepth : 350.0;

            double drawH = 150.0 * s;

            double drawW = Math.Max(75.0 * s, drawH * ((double)estW / Math.Max(1.0, (double)estH)));

            double cabX1 = sheetCenterX - drawW / 2.0 - 20.0 * s;

            double cabY1 = sheetCenterY - drawH / 2.0 + 5.0 * s;

            double cabX2 = cabX1 + drawW;

            double cabY2 = cabY1 + drawH;

            double plinthH = 10.0 * s;

            AddRectangle(btr, tr, new Point3d(cabX1 + 2.0 * s, cabY1, 0), new Point3d(cabX2 - 2.0 * s, cabY1 - plinthH, 0), LayerBorder, 8);

            AddText(btr, tr, "CHÂN ĐẾ TỦ (PLINTH 100mm)", new Point3d((cabX1 + cabX2) / 2.0, cabY1 - plinthH / 2.0, 0), 1.6 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddRectangle(btr, tr, new Point3d(cabX1, cabY2, 0), new Point3d(cabX2, cabY1, 0), LayerBorder, 7);

            double doorMargin = 4.0 * s;

            double dX1 = cabX1 + doorMargin;

            double dY1 = cabY1 + doorMargin;

            double dX2 = cabX2 - doorMargin;

            double dY2 = cabY2 - doorMargin;

            AddRectangle(btr, tr, new Point3d(dX1, dY2, 0), new Point3d(dX2, dY1, 0), LayerBorder, 8);

            for (int hg = 0; hg < 3; hg++)

            {

                double hgY = dY1 + (hg + 0.5) * (dY2 - dY1) / 3.0;

                AddRectangle(btr, tr, new Point3d(cabX1, hgY + 3.0 * s, 0), new Point3d(cabX1 + 2.5 * s, hgY - 3.0 * s, 0), LayerSchematic, 7);

            }

            double doorMidX = (dX1 + dX2) / 2.0;

            double lampY = dY2 - 18.0 * s;

            double lampSpacing = 16.0 * s;

            AddCircle(btr, tr, new Point3d(doorMidX - lampSpacing, lampY, 0), 2.5 * s, LayerLoad3P, 1);

            AddText(btr, tr, "R", new Point3d(doorMidX - lampSpacing, lampY - 4.5 * s, 0), 1.8 * s, AttachmentPoint.MiddleCenter, LayerLoad3P, 0, 1);

            AddCircle(btr, tr, new Point3d(doorMidX, lampY, 0), 2.5 * s, LayerControl, 2);

            AddText(btr, tr, "Y", new Point3d(doorMidX, lampY - 4.5 * s, 0), 1.8 * s, AttachmentPoint.MiddleCenter, LayerControl, 0, 2);

            AddCircle(btr, tr, new Point3d(doorMidX + lampSpacing, lampY, 0), 2.5 * s, LayerLoad1P, 3);

            AddText(btr, tr, "B", new Point3d(doorMidX + lampSpacing, lampY - 4.5 * s, 0), 1.8 * s, AttachmentPoint.MiddleCenter, LayerLoad1P, 0, 3);

            double pmY = lampY - 26.0 * s;

            double pmW = 22.0 * s;

            double pmH = 20.0 * s;

            AddRectangle(btr, tr, new Point3d(doorMidX - pmW / 2.0, pmY + pmH / 2.0, 0), new Point3d(doorMidX + pmW / 2.0, pmY - pmH / 2.0, 0), LayerMetering, 4);

            AddText(btr, tr, "ĐỒNG HỒ ĐA NĂNG PM", new Point3d(doorMidX, pmY + 3.0 * s, 0), 1.8 * s, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);

            AddText(btr, tr, "MULTIFUNCTION METER", new Point3d(doorMidX, pmY - 3.0 * s, 0), 1.4 * s, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);

            double lockX = dX2 - 6.0 * s;

            double lockY = (dY1 + dY2) / 2.0;

            AddRectangle(btr, tr, new Point3d(lockX - 2.0 * s, lockY + 12.0 * s, 0), new Point3d(lockX + 2.0 * s, lockY - 12.0 * s, 0), LayerSchematic, 7);

            AddCircle(btr, tr, new Point3d(lockX, lockY - 5.0 * s, 0), 1.5 * s, LayerSchematic, 7);

            double louvY = dY1 + 18.0 * s;

            double louvW = 30.0 * s;

            for (int lv = -2; lv <= 2; lv++)

            {

                AddLine(btr, tr, new Point3d(doorMidX - louvW / 2.0, louvY + lv * 2.5 * s, 0), new Point3d(doorMidX + louvW / 2.0, louvY + lv * 2.5 * s, 0), LayerBorder, 8);

            }

            AddText(btr, tr, "CHỚP THÔNG GIÓ", new Point3d(doorMidX, louvY - 9.0 * s, 0), 1.4 * s, AttachmentPoint.MiddleCenter, LayerText, 0);

            double dimX = cabX2 + 15.0 * s;

            AddLine(btr, tr, new Point3d(cabX2 + 4.0 * s, cabY1 - plinthH, 0), new Point3d(dimX + 5.0 * s, cabY1 - plinthH, 0), LayerBorder, 8);

            AddLine(btr, tr, new Point3d(cabX2 + 4.0 * s, cabY2, 0), new Point3d(dimX + 5.0 * s, cabY2, 0), LayerBorder, 8);

            AddLine(btr, tr, new Point3d(dimX, cabY1 - plinthH, 0), new Point3d(dimX, cabY2, 0), LayerBorder, 7);

            AddText(btr, tr, string.Format("H = {0} mm", estH), new Point3d(dimX + 3.0 * s, (cabY1 - plinthH + cabY2) / 2.0, 0), 2.2 * s, AttachmentPoint.MiddleLeft, LayerText, 0);

            double dimY = cabY2 + 12.0 * s;

            AddLine(btr, tr, new Point3d(cabX1, cabY2 + 2.0 * s, 0), new Point3d(cabX1, dimY + 4.0 * s, 0), LayerBorder, 8);

            AddLine(btr, tr, new Point3d(cabX2, cabY2 + 2.0 * s, 0), new Point3d(cabX2, dimY + 4.0 * s, 0), LayerBorder, 8);

            AddLine(btr, tr, new Point3d(cabX1, dimY, 0), new Point3d(cabX2, dimY, 0), LayerBorder, 7);

            AddText(btr, tr, string.Format("W = {0} mm", estW), new Point3d((cabX1 + cabX2) / 2.0, dimY + 3.0 * s, 0), 2.2 * s, AttachmentPoint.BottomCenter, LayerText, 0);

            double specX = cabX2 + 45.0 * s;

            double specY = cabY2;

            double specW = 90.0 * s;

            double specH = 65.0 * s;

            AddRectangle(btr, tr, new Point3d(specX, specY, 0), new Point3d(specX + specW, specY - specH, 0), LayerBorder, 7);

            AddLine(btr, tr, new Point3d(specX, specY - 8.0 * s, 0), new Point3d(specX + specW, specY - 8.0 * s, 0), LayerBorder, 7);

            AddText(btr, tr, "BẢNG THÔNG SỐ KỸ THUẬT VỎ TỦ", new Point3d(specX + specW / 2.0, specY - 4.0 * s, 0), 1.8 * s, AttachmentPoint.MiddleCenter, LayerText, 0, 4);

            string[] specs = new string[] {

                string.Format("Kích thước: {0}x{1}x{2} mm (HxWxD)", estH, estW, estD),

                "Vật liệu: Thép tấm cán nguội dày 1.5 - 2.0 mm",

                "Bề mặt: Sơn tĩnh điện công nghiệp RAL 7035",

                "Cấp bảo vệ: IP43 (Trong nhà) / IP54 (Ngoài trời)",

                "Dạng ngăn cách: Form 2B / Form 3B - IEC 61439",

                "Chân đế: Thép hình U100 sơn đen chống rỉ",

                "Cánh tủ: Bản lề thép, góc mở 110 độ có khóa"

            };

            for (int sp = 0; sp < specs.Length; sp++)

            {

                double sy = specY - 14.0 * s - sp * 7.0 * s;

                AddText(btr, tr, specs[sp], new Point3d(specX + 3.0 * s, sy, 0), 1.5 * s, AttachmentPoint.MiddleLeft, LayerText, 0);

            }

        }

public bool DrawRiserDiagram(Document doc, List<ElectricalPanel> panels, Point3d origin)

        {

            if (doc == null || panels == null || panels.Count == 0) return false;

            Database db = doc.Database;

            Editor ed = doc.Editor;

            using (doc.LockDocument())

            using (Transaction tr = db.TransactionManager.StartTransaction())

            {

                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                EnsureLayer(db, tr, LayerBorder, 7);

                EnsureLayer(db, tr, LayerSchematic, 7);

                EnsureLayer(db, tr, LayerText, 7);

                EnsureLayer(db, tr, LayerLoad3P, 1);

                double rootX = origin.X;

                double rootY = origin.Y;

                // 1. Trạm biến áp MBA ở đỉnh

                double mbaW = 95.0;

                double mbaH = 22.0;

                AddRectangle(btr, tr, new Point3d(rootX - mbaW / 2.0, rootY + mbaH / 2.0, 0), new Point3d(rootX + mbaW / 2.0, rootY - mbaH / 2.0, 0), LayerBorder, 7);

                AddText(btr, tr, "TR\u1EA0M BI\u1EBEN \u00C1P (MBA) 1000kVA 22/0.4kV", new Point3d(rootX, rootY + 3.0, 0), 2.6, AttachmentPoint.MiddleCenter, LayerText, 0);

                AddText(btr, tr, "C\u1EA4P NGU\u1ED2N CH\u00CDNH TO\u00C0N C\u00D4NG TR\u00CCNH", new Point3d(rootX, rootY - 4.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerLoad3P, 0, 1);

                // Dây nguồn chính xuống các tủ

                double lineDownY = rootY - mbaH / 2.0;

                double panelRowY = lineDownY - 35.0;

                AddLine(btr, tr, new Point3d(rootX, lineDownY, 0), new Point3d(rootX, lineDownY - 15.0, 0), LayerSchematic, 7);

                int pCount = panels.Count;

                double colSpacing = 75.0;

                double totalW = (pCount - 1) * colSpacing;

                double startX = rootX - totalW / 2.0;

                // Thanh cái phân phối ngang

                AddLine(btr, tr, new Point3d(startX, lineDownY - 15.0, 0), new Point3d(startX + totalW, lineDownY - 15.0, 0), LayerSchematic, 7);

                for (int i = 0; i < pCount; i++)

                {

                    ElectricalPanel p = panels[i];

                    double px = startX + i * colSpacing;

                    // Dây nhánh xuống tủ

                    AddLine(btr, tr, new Point3d(px, lineDownY - 15.0, 0), new Point3d(px, panelRowY + 12.0, 0), LayerSchematic, 7);

                    AddCircle(btr, tr, new Point3d(px, lineDownY - 15.0, 0), 0.8, LayerSchematic);

                    AddText(btr, tr, p.MainBreakerSpec ?? "MCCB 3P", new Point3d(px - 1.5, panelRowY + 22.0, 0), 1.6, AttachmentPoint.MiddleRight, LayerText, 0);

                    // Khối biểu diễn tủ điện

                    double pw = 65.0;

                    double ph = 24.0;

                    AddRectangle(btr, tr, new Point3d(px - pw / 2.0, panelRowY + ph / 2.0, 0), new Point3d(px + pw / 2.0, panelRowY - ph / 2.0, 0), LayerBorder, 7);

                    AddText(btr, tr, string.Format("T\u1EE6 \u0110I\u1EC6N: {0}", p.PanelName), new Point3d(px, panelRowY + 5.0, 0), 2.4, AttachmentPoint.MiddleCenter, LayerText, 0);

                    AddText(btr, tr, string.Format("Ptt = {0:N2} kW | Itt = {1:N1} A", p.TotalCalculatedPowerKW, p.TotalCurrentA), new Point3d(px, panelRowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerLoad3P, 0, 1);

                    AddText(btr, tr, string.Format("S\u1ED1 l\u1ED9 ra: {0} t\u1EA3i", p.Loads != null ? p.Loads.Count : 0), new Point3d(px, panelRowY - 5.0, 0), 1.6, AttachmentPoint.MiddleCenter, LayerText, 0);

                }

                tr.Commit();

            }

            ed.WriteMessage("\n[NTT TOOLS] \u0110\u00E3 ho\u00E0n th\u00E0nh v\u1EBD S\u01A1 \u0111\u1ED3 C\u00E2y Ph\u00E2n Ph\u1ED1i (Riser Diagram) th\u00E0nh c\u00F4ng!");

            return true;

        }

        #endregion

}

}
