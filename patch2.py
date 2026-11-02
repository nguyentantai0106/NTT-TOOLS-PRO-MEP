import sys

file_path = r'd:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Services\ElectricalDrawingService.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    code = f.read()

# 1. Replace InsertBreakerWithConnectionPoints implementation
target_fn_start = 'private void InsertBreakerWithConnectionPoints(BlockTableRecord btr, Transaction tr, ObjectId blockId, ObjectId dolBlockId,\n            BlockConnectionInfo connInfo, double lineX, double fromY, double toY, double targetCenterY, string spec, bool isMainBreaker,\n            bool hasDol = false, string downstreamLayer = LayerSchematic, int downstreamColor = 7)'

new_fn_def = '''private void InsertBreakerWithConnectionPoints(BlockTableRecord btr, Transaction tr, ObjectId blockId, ObjectId dolBlockId,
            BlockConnectionInfo connInfo, double lineX, double fromY, double toY, double targetCenterY, string spec, bool isMainBreaker,
            StarterType starter = StarterType.Direct, ControlMode control = ControlMode.None,
            string downstreamLayer = LayerSchematic, int downstreamColor = 7)'''

assert target_fn_start in code, "Target function start not found!"

# Also find the body up to line 862
target_body_end = '''            if (hasDol)
            {
                double dolTopY = breakerBottomY - 2.5;
                double dolH = 5.66551;
                double dolBotY = dolTopY - dolH;

                // Đoạn dây nối từ đầu ra Breaker xuống cực trên bộ DOL
                AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, dolTopY, 0), downstreamLayer, downstreamColor);

                // Chèn Block DOL hoặc vẽ Fallback
                if (dolBlockId != ObjectId.Null && !dolBlockId.IsErased)
                {
                    BlockReference dolRef = new BlockReference(new Point3d(lineX, dolTopY, 0), dolBlockId);
                    dolRef.Layer = downstreamLayer;
                    dolRef.ColorIndex = downstreamColor;
                    btr.AppendEntity(dolRef);
                    tr.AddNewlyCreatedDBObject(dolRef, true);
                    _currentCabinetEntities.Add(dolRef.ObjectId);
                }
                else
                {
                    AddRectangle(btr, tr, new Point3d(lineX - 2.5, dolTopY, 0), new Point3d(lineX + 2.5, dolBotY, 0), downstreamLayer, downstreamColor);
                    AddText(btr, tr, "DOL", new Point3d(lineX, (dolTopY + dolBotY) / 2.0, 0), 1.6, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);
                }

                // Dây từ cực dưới bộ DOL xuống mép bảng tableTop
                if (dolBotY > toY)
                {
                    AddLine(btr, tr, new Point3d(lineX, dolBotY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);
                }
            }
            else
            {
                // Không có DOL: dây chạy thẳng từ đầu ra Breaker xuống mép bảng tableTop
                if (breakerBottomY > toY)
                {
                    AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);
                }
            }
        }'''

new_body_end = '''            // XỬ LÝ PHẦN DƯỚI ĐẦU RA CỦA BREAKER (BỘ KHỞI ĐỘNG STARTER & ĐIỀU KHIỂN)
            double starterTopY = breakerBottomY - 2.5;
            double starterBotY = starterTopY - 5.5;

            if (starter == StarterType.DOL)
            {
                AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

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
                AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

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
                AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

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
                AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, starterTopY, 0), downstreamLayer, downstreamColor);

                AddRectangle(btr, tr, new Point3d(lineX - 3.5, starterTopY, 0), new Point3d(lineX + 3.5, starterBotY, 0), downstreamLayer, downstreamColor);
                AddText(btr, tr, "SS", new Point3d(lineX, (starterTopY + starterBotY) / 2.0, 0), 1.6, AttachmentPoint.MiddleCenter, downstreamLayer, 0, downstreamColor);

                if (starterBotY > toY)
                {
                    AddLine(btr, tr, new Point3d(lineX, starterBotY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);
                }
            }
            else
            {
                if (breakerBottomY > toY)
                {
                    AddLine(btr, tr, new Point3d(lineX, breakerBottomY, 0), new Point3d(lineX, toY, 0), downstreamLayer, downstreamColor);
                }
            }

            // KÝ HIỆU CHẾ ĐỘ ĐIỀU KHIỂN (H-O-A / Phao FS / Áp suất PS)
            if (control != ControlMode.None)
            {
                double ctrlY = toY + 12.0;
                double ctrlX = lineX + 3.2;

                if (control == ControlMode.AutoManual)
                {
                    AddCircle(btr, tr, new Point3d(ctrlX, ctrlY, 0), 1.8, LayerControl, 3);
                    AddText(btr, tr, "HOA", new Point3d(ctrlX, ctrlY, 0), 1.2, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);
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
        }'''

assert target_body_end in code, "Target body end not found!"
code = code.replace(target_fn_start, new_fn_def)
code = code.replace(target_body_end, new_body_end)

# 2. Update Incoming Feeder to support DualATS and DualInterlock
old_incomer_block = '''                // 7. NHÁNH CẤP NGUỒN TỔNG (INCOMING FEEDER) & CÁC BLOCK ĐO LƯỜNG (CẦU CHÌ, MCT, ĐÈN BÁO PHA, RELAY)
                double incomingX = x0 + totalHdrW + Math.Min(2.0, (double)loadCount) * colW;
                double incomingTop = cabTop + 25.0;

                DrawIncomingArrow(btr, tr, new Point3d(incomingX, incomingTop, 0));
                AddText(btr, tr, "CẤP NGUỒN TỪ CHỦ ĐẦU TƯ", new Point3d(incomingX, incomingTop + 7.0, 0), 2.8, AttachmentPoint.BottomCenter, LayerText, 0);
                if (!string.IsNullOrEmpty(panel.MainCableSpec))
                {
                    AddText(btr, tr, panel.MainCableSpec, new Point3d(incomingX, incomingTop + 2.5, 0), 2.3, AttachmentPoint.BottomCenter, LayerText, 0);
                }

                double blkScale = (p != null && p.MeteringBlockScale > 0) ? p.MeteringBlockScale : 0.1;
                double topBranchY = cabTop - 15.0;

                // Vị trí MCT và Breaker Tổng
                double mctCenterY = busY + 22.0;
                double mainCbCenterY = p.IncludeMct ? ((topBranchY + mctCenterY + 14.0) / 2.0) : ((topBranchY + busY) / 2.0);
                double mccbDist = connInfo.Distance > 0 ? connInfo.Distance : 8.051;
                Point3d worldMccbTop = new Point3d(incomingX, mainCbCenterY + mccbDist / 2.0, 0);
                Point3d worldMccbBot = new Point3d(incomingX, mainCbCenterY - mccbDist / 2.0, 0);

                // Dây cấp nguồn đứng:
                // Đoạn 1: Từ đầu cấp nguồn vào xuống điểm rẽ nhánh topBranchY
                AddLine(btr, tr, new Point3d(incomingX, incomingTop, 0), new Point3d(incomingX, topBranchY, 0), LayerSchematic, 7);

                // Đoạn 2: Từ điểm rẽ nhánh xuống cực trên MCCB Tổng
                AddLine(btr, tr, new Point3d(incomingX, topBranchY, 0), worldMccbTop, LayerSchematic, 7);'''

new_incomer_block = '''                // 7. NHÁNH CẤP NGUỒN TỔNG (INCOMING FEEDER) & CÁC BLOCK ĐO LƯỜNG (CẦU CHÌ, MCT, ĐÈN BÁO PHA, RELAY)
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
                    DrawDualInterlockIncomer(btr, tr, incomingX, incomingTop, topBranchY, worldMccbTop, panel);
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
                    AddLine(btr, tr, new Point3d(incomingX, topBranchY, 0), worldMccbTop, LayerSchematic, 7);
                }'''

assert old_incomer_block in code, "Old incomer block not found!"
code = code.replace(old_incomer_block, new_incomer_block)

# 3. Add Title Block and Front View before tr.Commit()
old_commit_block = '''                // 10. TIẾP ĐỊA TỦ ĐIỆN (EARTHING)
                DrawSideEarthing(btr, tr, new Point3d(cabRight, cabTop - 25.0, 0));'''

new_commit_block = '''                // 10. TIẾP ĐỊA TỦ ĐIỆN (EARTHING)
                DrawSideEarthing(btr, tr, new Point3d(cabRight, cabTop - 25.0, 0));

                // 12. VẼ MẶT NGOÀI CÁNH TỦ ĐIỆN (FRONT VIEW LAYOUT) NẾU ĐƯỢC CHỌN
                if (p.DrawFrontView)
                {
                    double fvX = Math.Max(cabRight, x0 + totalTableW) + 25.0;
                    DrawFrontViewLayout(btr, tr, new Point3d(fvX, cabBot, 0), cabH, panel);
                }

                // 13. LỒNG KHUNG BẢN VẼ KỸ THUẬT A3/A2 & KHUNG TÊN TITLE BLOCK
                if (p.IncludeTitleBlock)
                {
                    double minX = cabLeft - 10.0;
                    double maxX = Math.Max(cabRight, x0 + totalTableW) + (p.DrawFrontView ? 95.0 : 20.0);
                    double minY = yTableBottom - 10.0;
                    double maxY = incomingTop + 20.0;
                    DrawTitleBlock(btr, tr, minX, minY, maxX, maxY, p, panel);
                }'''

assert old_commit_block in code, "Old commit block not found!"
code = code.replace(old_commit_block, new_commit_block)

# 4. Append new helper methods before the class closing brace
helper_methods = '''
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
            AddText(btr, tr, string.Format("T\\u1EE4 B\\u00D9 {0} kVAR", kvar), new Point3d(cx, capY + 1.0, 0), 1.4, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);
            AddText(btr, tr, string.Format("({0} C\\u1EA4P B\\u00D9)", steps), new Point3d(cx, capY - 1.5, 0), 1.2, AttachmentPoint.MiddleCenter, LayerText, 0);
        }

        private void DrawDualAtsIncomer(BlockTableRecord btr, Transaction tr, double incomingX, double incomingTop, double topBranchY, Point3d worldMccbTop, ElectricalPanel panel)
        {
            double src1X = incomingX - 9.0;
            double src2X = incomingX + 9.0;
            double atsTopY = topBranchY + 14.0;
            double atsBotY = topBranchY + 2.0;

            // Nguồn 1 EVN
            DrawIncomingArrow(btr, tr, new Point3d(src1X, incomingTop, 0));
            AddText(btr, tr, "NGU\\u1ED2N 1: L\\u01AF\\u1EDAI EVN", new Point3d(src1X, incomingTop + 6.0, 0), 2.2, AttachmentPoint.BottomCenter, LayerText, 0);
            AddLine(btr, tr, new Point3d(src1X, incomingTop, 0), new Point3d(src1X, atsTopY, 0), LayerSchematic, 7);

            // Nguồn 2 GEN
            DrawIncomingArrow(btr, tr, new Point3d(src2X, incomingTop, 0));
            AddText(btr, tr, "NGU\\u1ED2N 2: M\\u00C1Y PH\\u00C1T (GEN)", new Point3d(src2X, incomingTop + 6.0, 0), 2.2, AttachmentPoint.BottomCenter, LayerText, 0);
            AddLine(btr, tr, new Point3d(src2X, incomingTop, 0), new Point3d(src2X, atsTopY, 0), LayerSchematic, 7);

            // ATS Box
            AddRectangle(btr, tr, new Point3d(incomingX - 13.0, atsTopY, 0), new Point3d(incomingX + 13.0, atsBotY, 0), LayerMetering, 4);
            AddText(btr, tr, "B\\u1ED8 CHUY\\u1EC2N NGU\\u1ED2N T\\u1EF0 \\u0110\\u1ED8NG ATS", new Point3d(incomingX, (atsTopY + atsBotY) / 2.0 + 1.5, 0), 2.0, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);
            AddText(btr, tr, "(AUTOMATIC TRANSFER SWITCH)", new Point3d(incomingX, (atsTopY + atsBotY) / 2.0 - 1.5, 0), 1.5, AttachmentPoint.MiddleCenter, LayerText, 0);

            // Dây ngõ ra từ ATS xuống cực trên MCCB Tổng
            AddLine(btr, tr, new Point3d(incomingX, atsBotY, 0), worldMccbTop, LayerSchematic, 7);
        }

        private void DrawDualInterlockIncomer(BlockTableRecord btr, Transaction tr, double incomingX, double incomingTop, double topBranchY, Point3d worldMccbTop, ElectricalPanel panel)
        {
            double src1X = incomingX - 8.0;
            double src2X = incomingX + 8.0;
            double cbY = worldMccbTop.Y + 4.0;

            // Nguồn 1 EVN
            DrawIncomingArrow(btr, tr, new Point3d(src1X, incomingTop, 0));
            AddText(btr, tr, "NGU\\u1ED2N 1 (EVN)", new Point3d(src1X, incomingTop + 5.0, 0), 2.0, AttachmentPoint.BottomCenter, LayerText, 0);
            AddLine(btr, tr, new Point3d(src1X, incomingTop, 0), new Point3d(src1X, cbY + 4.0, 0), LayerSchematic, 7);

            // MCCB 1
            AddRectangle(btr, tr, new Point3d(src1X - 2.5, cbY + 4.0, 0), new Point3d(src1X + 2.5, cbY - 4.0, 0), LayerSchematic, 7);
            AddLine(btr, tr, new Point3d(src1X - 2.5, cbY - 4.0, 0), new Point3d(src1X + 2.5, cbY + 4.0, 0), LayerSchematic, 7);

            // Nguồn 2 GEN
            DrawIncomingArrow(btr, tr, new Point3d(src2X, incomingTop, 0));
            AddText(btr, tr, "NGU\\u1ED2N 2 (M\\u00C1Y PH\\u00C1T)", new Point3d(src2X, incomingTop + 5.0, 0), 2.0, AttachmentPoint.BottomCenter, LayerText, 0);
            AddLine(btr, tr, new Point3d(src2X, incomingTop, 0), new Point3d(src2X, cbY + 4.0, 0), LayerSchematic, 7);

            // MCCB 2
            AddRectangle(btr, tr, new Point3d(src2X - 2.5, cbY + 4.0, 0), new Point3d(src2X + 2.5, cbY - 4.0, 0), LayerSchematic, 7);
            AddLine(btr, tr, new Point3d(src2X - 2.5, cbY - 4.0, 0), new Point3d(src2X + 2.5, cbY + 4.0, 0), LayerSchematic, 7);

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

        private void DrawFrontViewLayout(BlockTableRecord btr, Transaction tr, Point3d startPt, double cabH, ElectricalPanel panel)
        {
            double fvW = 60.0;
            double fvH = cabH;
            double x1 = startPt.X;
            double y1 = startPt.Y;
            double x2 = x1 + fvW;
            double y2 = y1 + fvH;

            // Vỏ tủ ngoài
            AddRectangle(btr, tr, new Point3d(x1, y2, 0), new Point3d(x2, y1, 0), LayerBorder, 7);
            // Khung cánh cửa trong
            AddRectangle(btr, tr, new Point3d(x1 + 3.0, y2 - 3.0, 0), new Point3d(x2 - 3.0, y1 + 3.0, 0), LayerBorder, 7);

            // Chân đế Plinth
            AddRectangle(btr, tr, new Point3d(x1, y1, 0), new Point3d(x2, y1 - 8.0, 0), LayerBorder, 7);
            AddText(btr, tr, "CH\\u00C2N \\u0110\\u1EBE PLINTH", new Point3d((x1 + x2) / 2.0, y1 - 4.0, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText, 0);

            // Nhãn tên tủ điện
            AddRectangle(btr, tr, new Point3d(x1 + 8.0, y2 - 7.0, 0), new Point3d(x2 - 8.0, y2 - 16.0, 0), LayerSchematic, 7);
            AddText(btr, tr, string.Format("T\\u1EE6 \\u0110I\\u1EC6N: {0}", panel.PanelName), new Point3d((x1 + x2) / 2.0, y2 - 11.5, 0), 2.2, AttachmentPoint.MiddleCenter, LayerText, 0);

            // 3 Đèn báo pha R - Y - B
            double lampY = y2 - 25.0;
            double lampSpacing = 8.0;
            double midX = (x1 + x2) / 2.0;

            AddCircle(btr, tr, new Point3d(midX - lampSpacing, lampY, 0), 2.0, LayerLoad3P, 1);
            AddText(btr, tr, "R", new Point3d(midX - lampSpacing, lampY - 3.5, 0), 1.5, AttachmentPoint.MiddleCenter, LayerLoad3P, 0, 1);

            AddCircle(btr, tr, new Point3d(midX, lampY, 0), 2.0, LayerControl, 2);
            AddText(btr, tr, "Y", new Point3d(midX, lampY - 3.5, 0), 1.5, AttachmentPoint.MiddleCenter, LayerControl, 0, 2);

            AddCircle(btr, tr, new Point3d(midX + lampSpacing, lampY, 0), 2.0, LayerLoad1P, 3);
            AddText(btr, tr, "B", new Point3d(midX + lampSpacing, lampY - 3.5, 0), 1.5, AttachmentPoint.MiddleCenter, LayerLoad1P, 0, 3);

            // Đồng hồ đa năng PM
            double pmY = lampY - 18.0;
            AddRectangle(btr, tr, new Point3d(midX - 7.0, pmY + 7.0, 0), new Point3d(midX + 7.0, pmY - 7.0, 0), LayerMetering, 4);
            AddText(btr, tr, "\\u0110\\u1ED2NG H\\u1ED2 PM\\n3P DIGITAL", new Point3d(midX, pmY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);

            // Khóa tay nắm cánh tủ
            double lockX = x2 - 7.0;
            double lockY = (y1 + y2) / 2.0;
            AddRectangle(btr, tr, new Point3d(lockX - 1.5, lockY + 5.0, 0), new Point3d(lockX + 1.5, lockY - 5.0, 0), LayerSchematic, 7);
            AddCircle(btr, tr, new Point3d(lockX, lockY, 0), 1.0, LayerSchematic, 7);

            // Tiêu đề dưới
            AddText(btr, tr, "M\\u1EB6T NGO\\u00C0I C\\u00C1NH T\\u1EE6 (FRONT VIEW)", new Point3d((x1 + x2) / 2.0, y1 - 14.0, 0), 2.6, AttachmentPoint.TopCenter, LayerText, 0);
            AddText(btr, tr, string.Format("K\\u00CDCH TH\\u01AF\\u1EDAAC: H{0} x W{1} x D{2} mm", panel.EstimatedHeight, panel.EstimatedWidth, panel.EstimatedDepth),
                new Point3d((x1 + x2) / 2.0, y1 - 19.0, 0), 2.0, AttachmentPoint.TopCenter, LayerText, 0);
        }

        private void DrawTitleBlock(BlockTableRecord btr, Transaction tr, double minX, double minY, double maxX, double maxY, DrawingParameters p, ElectricalPanel panel)
        {
            // Khung ngoài bản vẽ
            AddRectangle(btr, tr, new Point3d(minX, maxY, 0), new Point3d(maxX, minY, 0), LayerBorder, 7);

            // Khung viền kỹ thuật lề trong
            double mLeft = minX + 10.0;
            double mRight = maxX - 5.0;
            double mTop = maxY - 5.0;
            double mBot = minY + 5.0;
            AddRectangle(btr, tr, new Point3d(mLeft, mTop, 0), new Point3d(mRight, mBot, 0), LayerBorder, 7);

            // Bảng khung tên (Title Block) góc phải dưới
            double tbW = 95.0;
            double tbH = 32.0;
            double tbX1 = mRight - tbW;
            double tbY1 = mBot;
            double tbX2 = mRight;
            double tbY2 = mBot + tbH;

            AddRectangle(btr, tr, new Point3d(tbX1, tbY2, 0), new Point3d(tbX2, tbY1, 0), LayerBorder, 7);

            // Đường kẻ ngang trong khung tên
            AddLine(btr, tr, new Point3d(tbX1, tbY1 + 22.0, 0), new Point3d(tbX2, tbY1 + 22.0, 0), LayerBorder, 7);
            AddLine(btr, tr, new Point3d(tbX1, tbY1 + 12.0, 0), new Point3d(tbX2, tbY1 + 12.0, 0), LayerBorder, 7);

            // Đường kẻ dọc phân cách
            AddLine(btr, tr, new Point3d(tbX1 + 35.0, tbY2, 0), new Point3d(tbX1 + 35.0, tbY1, 0), LayerBorder, 7);

            // Điền chữ trong khung tên
            AddText(btr, tr, "NTT TOOLS", new Point3d(tbX1 + 17.5, tbY1 + 27.0, 0), 3.0, AttachmentPoint.MiddleCenter, LayerText, 0);
            AddText(btr, tr, "Thi\\u1EBFt k\\u1EBF / Shop / BKL", new Point3d(tbX1 + 17.5, tbY1 + 23.5, 0), 1.4, AttachmentPoint.MiddleCenter, LayerText, 0);

            AddText(btr, tr, "D\\u1EF0 \\u00C1N / PROJECT:", new Point3d(tbX1 + 37.0, tbY1 + 30.0, 0), 1.5, AttachmentPoint.TopLeft, LayerText, 0);
            AddText(btr, tr, p.ProjectName ?? "H\\u1EC6 TH\\u1ED0NG \\u0110I\\u1EC6N T\\u00D2A NH\\u00C0", new Point3d(tbX1 + 37.0, tbY1 + 25.0, 0), 2.0, AttachmentPoint.TopLeft, LayerText, 0);

            AddText(btr, tr, "T\\u00CAN B\\u1EA2N V\\u1EBD / TITLE:", new Point3d(tbX1 + 2.0, tbY1 + 20.0, 0), 1.5, AttachmentPoint.TopLeft, LayerText, 0);
            AddText(btr, tr, string.Format("S\\u01A0 \\u0110\\u1ED2 NGUY\\u00CAN L\\u00DD T\\u1EE6 {0}", panel.PanelName), new Point3d(tbX1 + 37.0, tbY1 + 17.0, 0), 2.2, AttachmentPoint.TopLeft, LayerText, 0);

            AddText(btr, tr, string.Format("K\\u1EF8 S\\u01AF: {0}", p.DesignerName ?? "NTT TOOLS"), new Point3d(tbX1 + 2.0, tbY1 + 6.0, 0), 1.7, AttachmentPoint.MiddleLeft, LayerText, 0);
            AddText(btr, tr, string.Format("T\\u1EF8 L\\u1EC6: {0}", p.DrawingScale ?? "1:25"), new Point3d(tbX1 + 40.0, tbY1 + 6.0, 0), 1.7, AttachmentPoint.MiddleLeft, LayerText, 0);
            AddText(btr, tr, string.Format("NG\\u00C0Y: {0:dd/MM/yyyy}", DateTime.Now), new Point3d(tbX1 + 68.0, tbY1 + 6.0, 0), 1.7, AttachmentPoint.MiddleLeft, LayerText, 0);
        }

        /// <summary>
        /// Vẽ Cây Sơ Đồ Phân Phối Điện Tổng Thể (Riser Diagram) liên kết hệ thống các tủ điện
        /// </summary>
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
                AddText(btr, tr, "TR\\u1EA0M BI\\u1EBEN \\u00C1P (MBA) 1000kVA 22/0.4kV", new Point3d(rootX, rootY + 3.0, 0), 2.6, AttachmentPoint.MiddleCenter, LayerText, 0);
                AddText(btr, tr, "C\\u1EA4P NGU\\u1ED2N CH\\u00CDNH TO\\u00C0N C\\u00D4NG TR\\u00CCNH", new Point3d(rootX, rootY - 4.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerLoad3P, 0, 1);

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
                    AddText(btr, tr, string.Format("T\\u1EE6 \\u0110I\\u1EC6N: {0}", p.PanelName), new Point3d(px, panelRowY + 5.0, 0), 2.4, AttachmentPoint.MiddleCenter, LayerText, 0);
                    AddText(btr, tr, string.Format("Ptt = {0:N2} kW | Itt = {1:N1} A", p.TotalCalculatedPowerKW, p.TotalCurrentA), new Point3d(px, panelRowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerLoad3P, 0, 1);
                    AddText(btr, tr, string.Format("S\\u1ED1 l\\u1ED9 ra: {0} t\\u1EA3i", p.Loads != null ? p.Loads.Count : 0), new Point3d(px, panelRowY - 5.0, 0), 1.6, AttachmentPoint.MiddleCenter, LayerText, 0);
                }

                tr.Commit();
            }

            ed.WriteMessage("\\n[NTT TOOLS] \\u0110\\u00E3 ho\\u00E0n th\\u00E0nh v\\u1EBD S\\u01A1 \\u0111\\u1ED3 C\\u00E2y Ph\\u00E2n Ph\\u1ED1i (Riser Diagram) th\\u00E0nh c\\u00F4ng!");
            return true;
        }

        #endregion
'''

# Find the last closing brace before the namespace end
last_brace_idx = code.rfind('}')
second_last_brace_idx = code.rfind('}', 0, last_brace_idx)

code = code[:second_last_brace_idx] + helper_methods + code[second_last_brace_idx:]

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(code)

print('Patch part 2 completed successfully!')
