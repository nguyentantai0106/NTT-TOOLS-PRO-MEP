import re

file_path = r'd:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Services\ElectricalDrawingService.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    code = f.read()

# 1. Update totalCols calculation
old_total_cols = 'int totalCols = loadCount + (p.IncludeSpare ? 1 : 0) + (p.IncludeSpd ? 1 : 0);'
new_total_cols = 'int totalCols = loadCount + (p.IncludeSpare ? 1 : 0) + (p.IncludeSpd ? 1 : 0) + (panel.HasCapacitorBank ? 1 : 0);'
code = code.replace(old_total_cols, new_total_cols)

# 2. Update PopulateTableData call
old_pop = 'PopulateTableData(btr, tr, panel, x0, yTableBottom, hdrMainW, hdrSubW, colW,\n                    hName, hCircuit, hPower, p.TextSize, p.IncludeSpare, p.IncludeSpd);'
new_pop = 'PopulateTableData(btr, tr, panel, x0, yTableBottom, hdrMainW, hdrSubW, colW,\n                    hName, hCircuit, hPower, p.TextSize, p.IncludeSpare, p.IncludeSpd, panel.HasCapacitorBank);'
code = code.replace(old_pop, new_pop)

# 3. Update PopulateTableData definition
old_pop_def = '''        private void PopulateTableData(BlockTableRecord btr, Transaction tr, ElectricalPanel panel,
            double x0, double y0, double hdrMainW, double hdrSubW, double colW,
            double hName, double hCircuit, double hPower, double textSize,
            bool includeSpare, bool includeSpd)'''
new_pop_def = '''        private void PopulateTableData(BlockTableRecord btr, Transaction tr, ElectricalPanel panel,
            double x0, double y0, double hdrMainW, double hdrSubW, double colW,
            double hName, double hCircuit, double hPower, double textSize,
            bool includeSpare, bool includeSpd, bool hasCapacitor = false)'''
code = code.replace(old_pop_def, new_pop_def)

# 4. In PopulateTableData, append capacitor column if present
old_pop_end = '''            if (includeSpd)
            {
                double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;
                AddText(btr, tr, "CH\\u1ED0NG S\\u00C9T\\nSPD", new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0, 7);
                AddText(btr, tr, "SPD", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
            }'''
new_pop_end = '''            if (includeSpd)
            {
                double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;
                AddText(btr, tr, "CH\\u1ED0NG S\\u00C9T\\nSPD", new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0, 7);
                AddText(btr, tr, "SPD", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
                extraCol++;
            }

            if (hasCapacitor)
            {
                double colCenterX = x0 + totalHdrW + (extraCol + 0.5) * colW;
                AddText(btr, tr, string.Format("T\\u1EE4 B\\u00D9\\n{0} kVAR", panel.CapacitorBankKvar), new Point3d(colCenterX, yName + hName / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerMetering, Math.PI / 2.0, 4);
                AddText(btr, tr, "CAP", new Point3d(colCenterX, yCircuit + hCircuit / 2.0, 0), textSize, AttachmentPoint.MiddleCenter, LayerMetering, 0, 4);
            }'''
code = code.replace(old_pop_end, new_pop_end)

# 5. In DrawSingleLineDiagram, update branch call:
old_call = 'InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, fx, busY, yTableTop, targetBreakerCenterY, load.BreakerSpec, false, load.HasDol, loadLayer, loadColor);'
new_call = 'InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, fx, busY, yTableTop, targetBreakerCenterY, load.BreakerSpec, false, load.Starter, load.Control, loadLayer, loadColor);'
code = code.replace(old_call, new_call)

# 6. Spare & SPD calls:
old_spare = 'InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spareCenterX, busY, yTableTop, targetBreakerCenterY, "MCCB 3P-10kA 40A", false, false, LayerSchematic, 7);'
new_spare = 'InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spareCenterX, busY, yTableTop, targetBreakerCenterY, "MCCB 3P-10kA 40A", false, StarterType.Direct, ControlMode.None, LayerSchematic, 7);'
code = code.replace(old_spare, new_spare)

old_spd = 'InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spdCenterX, busY, spdBoxY + 5.0, targetBreakerCenterY, "MCCB 3P-10kA 40A", false, false, LayerSchematic, 7);'
new_spd = 'InsertBreakerWithConnectionPoints(btr, tr, mccbBlockId, dolBlockId, connInfo, spdCenterX, busY, spdBoxY + 5.0, targetBreakerCenterY, "MCCB 3P-10kA 40A", false, StarterType.Direct, ControlMode.None, LayerSchematic, 7);'
code = code.replace(old_spd, new_spd)

# 7. Add capacitor branch in DrawSingleLineDiagram after SPD:
old_spd_block = '''                    // Hộp SPD
                    AddRectangle(btr, tr, new Point3d(spdCenterX - 4.0, spdBoxY + 5.0, 0), new Point3d(spdCenterX + 4.0, spdBoxY - 5.0, 0), LayerSchematic, 7);
                    AddText(btr, tr, "CHỐNG SÉT LAN TRUYỀN", new Point3d(spdCenterX, spdBoxY - 9.0, 0), 2.0, AttachmentPoint.TopCenter, LayerText, 0);
                    currentExtraCol++;
                }'''
new_spd_block = '''                    // Hộp SPD
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
                }'''
code = code.replace(old_spd_block, new_spd_block)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(code)

print('Patch part 1 done!')
