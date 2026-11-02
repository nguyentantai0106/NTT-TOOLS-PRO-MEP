﻿﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ nhập danh sách phụ tải hai chiều từ file Excel (.xlsx OpenXML, .xls SpreadsheetML hoặc CSV)
    /// Hỗ trợ Smart Header Mapping linh hoạt và trích xuất ngược toàn diện thành các đối tượng Sơ Đồ Nguyên Lý.
    /// </summary>
    public class ExcelImportService
    {
        /// <summary>
        /// Nhập toàn bộ danh sách các tủ điện từ file Excel (.xlsx đa Sheet, .xls hoặc .csv)
        /// Mỗi Sheet đại diện cho một tủ điện kỹ thuật (ví dụ DB-HVAC-01, MCC-HVAC-SAY-01, v.v.)
        /// </summary>
        public List<ElectricalPanel> ImportPanelsFromExcel(string filePath)
        {
            List<ElectricalPanel> panels = new List<ElectricalPanel>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return panels;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".xlsx")
            {
                panels = ImportPanelsFromOpenXmlXlsx(filePath);
            }
            else if (ext == ".csv" || ext == ".txt")
            {
                List<ElectricalLoad> loads = ImportFromCsv(filePath);
                if (loads != null && loads.Count > 0)
                {
                    ElectricalPanel p = new ElectricalPanel();
                    p.PanelName = Path.GetFileNameWithoutExtension(filePath);
                    p.Loads = loads;
                    panels.Add(p);
                }
            }
            else
            {
                // .xls hoặc SpreadsheetML
                panels = ImportPanelsFromSpreadsheetML(filePath);
                if (panels.Count == 0)
                {
                    List<ElectricalLoad> loads = ImportFromCsv(filePath);
                    if (loads != null && loads.Count > 0)
                    {
                        ElectricalPanel p = new ElectricalPanel();
                        p.PanelName = Path.GetFileNameWithoutExtension(filePath);
                        p.Loads = loads;
                        panels.Add(p);
                    }
                }
            }

            return panels;
        }

        /// <summary>
        /// Đọc phụ tải từ file Excel (trả về danh sách phụ tải của tủ đầu tiên để tương thích ngược)
        /// </summary>
        public List<ElectricalLoad> ImportLoads(string filePath)
        {
            List<ElectricalPanel> panels = ImportPanelsFromExcel(filePath);
            if (panels != null && panels.Count > 0)
            {
                return panels[0].Loads;
            }
            return new List<ElectricalLoad>();
        }

        #region Xử lý OpenXML (.xlsx) Native với FileShare.ReadWrite

        private List<ElectricalPanel> ImportPanelsFromOpenXmlXlsx(string filePath)
        {
            List<ElectricalPanel> panels = new List<ElectricalPanel>();

            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    // 1. Đọc Shared Strings Table bằng duyệt Node thuần túy
                    List<string> sharedStrings = new List<string>();
                    ZipArchiveEntry sstEntry = zip.GetEntry("xl/sharedStrings.xml");
                    if (sstEntry != null)
                    {
                        using (Stream sstStream = sstEntry.Open())
                        {
                            XmlDocument doc = new XmlDocument();
                            doc.Load(sstStream);
                            if (doc.DocumentElement != null)
                            {
                                foreach (XmlNode si in doc.DocumentElement.ChildNodes)
                                {
                                    if (si.LocalName != "si") continue;
                                    StringBuilder sbText = new StringBuilder();
                                    foreach (XmlNode child in si.ChildNodes)
                                    {
                                        if (child.LocalName == "t")
                                        {
                                            sbText.Append(child.InnerText);
                                        }
                                        else if (child.LocalName == "r")
                                        {
                                            foreach (XmlNode rChild in child.ChildNodes)
                                            {
                                                if (rChild.LocalName == "t") sbText.Append(rChild.InnerText);
                                            }
                                        }
                                    }
                                    sharedStrings.Add(sbText.ToString());
                                }
                            }
                        }
                    }

                    // 2. Đọc Relationships để ánh xạ r:id sang tên file worksheet
                    Dictionary<string, string> rels = new Dictionary<string, string>();
                    ZipArchiveEntry relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
                    if (relsEntry != null)
                    {
                        using (Stream relStream = relsEntry.Open())
                        {
                            XmlDocument doc = new XmlDocument();
                            doc.Load(relStream);
                            if (doc.DocumentElement != null)
                            {
                                foreach (XmlNode r in doc.DocumentElement.ChildNodes)
                                {
                                    if (r.LocalName != "Relationship") continue;
                                    string id = r.Attributes["Id"] != null ? r.Attributes["Id"].Value : "";
                                    string target = r.Attributes["Target"] != null ? r.Attributes["Target"].Value : "";
                                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                                    {
                                        if (target.StartsWith("/")) target = target.Substring(1);
                                        if (!target.StartsWith("xl/")) target = "xl/" + target;
                                        rels[id] = target;
                                    }
                                }
                            }
                        }
                    }

                    // 3. Đọc Workbook để lấy danh sách Sheet
                    ZipArchiveEntry wbEntry = zip.GetEntry("xl/workbook.xml");
                    if (wbEntry != null)
                    {
                        using (Stream wbStream = wbEntry.Open())
                        {
                            XmlDocument doc = new XmlDocument();
                            doc.Load(wbStream);
                            XmlNodeList sheetNodes = doc.GetElementsByTagName("sheet");
                            if (sheetNodes != null)
                            {
                                foreach (XmlNode sh in sheetNodes)
                                {
                                    string sheetName = sh.Attributes["name"] != null ? sh.Attributes["name"].Value : "";
                                    string rId = "";
                                    if (sh.Attributes["r:id"] != null) rId = sh.Attributes["r:id"].Value;
                                    else if (sh.Attributes["id"] != null) rId = sh.Attributes["id"].Value;

                                    if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(rId)) continue;
                                    if (!rels.ContainsKey(rId)) continue;

                                    string targetPath = rels[rId];
                                    ZipArchiveEntry sheetEntry = zip.GetEntry(targetPath);
                                    if (sheetEntry == null) continue;

                                    List<Dictionary<int, string>> sheetRows = ReadWorksheetCells(sheetEntry, sharedStrings);
                                    if (sheetRows != null && sheetRows.Count > 0)
                                    {
                                        ElectricalPanel pnl = ParsePanelFromRows(sheetName, sheetRows);
                                        if (pnl != null && pnl.Loads != null && pnl.Loads.Count > 0)
                                        {
                                            panels.Add(pnl);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ExcelImportService] Lỗi OpenXML: " + ex.Message);
            }

            return panels;
        }

        private List<Dictionary<int, string>> ReadWorksheetCells(ZipArchiveEntry sheetEntry, List<string> sharedStrings)
        {
            List<Dictionary<int, string>> rows = new List<Dictionary<int, string>>();

            using (Stream stream = sheetEntry.Open())
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(stream);

                XmlNodeList rowNodes = doc.GetElementsByTagName("row");
                if (rowNodes == null) return rows;

                foreach (XmlNode rowNode in rowNodes)
                {
                    Dictionary<int, string> rowDict = new Dictionary<int, string>();

                    foreach (XmlNode c in rowNode.ChildNodes)
                    {
                        if (c.LocalName != "c") continue;

                        string cellRef = c.Attributes["r"] != null ? c.Attributes["r"].Value : "";
                        int colIdx = GetColumnIndexFromCellReference(cellRef);
                        if (colIdx < 0) continue;

                        string cellType = c.Attributes["t"] != null ? c.Attributes["t"].Value : "";
                        string val = "";

                        if (cellType == "s")
                        {
                            foreach (XmlNode child in c.ChildNodes)
                            {
                                if (child.LocalName == "v")
                                {
                                    int strIdx;
                                    if (int.TryParse(child.InnerText, out strIdx))
                                    {
                                        if (strIdx >= 0 && strIdx < sharedStrings.Count) val = sharedStrings[strIdx];
                                    }
                                    break;
                                }
                            }
                        }
                        else if (cellType == "inlineStr")
                        {
                            foreach (XmlNode isNode in c.ChildNodes)
                            {
                                if (isNode.LocalName == "is")
                                {
                                    foreach (XmlNode tNode in isNode.ChildNodes)
                                    {
                                        if (tNode.LocalName == "t") { val = tNode.InnerText; break; }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Số hoặc kết quả công thức
                            foreach (XmlNode child in c.ChildNodes)
                            {
                                if (child.LocalName == "v")
                                {
                                    val = child.InnerText;
                                    break;
                                }
                            }
                        }

                        rowDict[colIdx] = val != null ? val.Trim() : string.Empty;
                    }

                    if (rowDict.Count > 0)
                    {
                        rows.Add(rowDict);
                    }
                }
            }

            return rows;
        }

        private int GetColumnIndexFromCellReference(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;
            int col = 0;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char c = char.ToUpperInvariant(cellRef[i]);
                if (c >= 'A' && c <= 'Z')
                {
                    col = col * 26 + (c - 'A' + 1);
                }
                else
                {
                    break;
                }
            }
            return col - 1; // 0-based
        }

        #endregion

        #region Xử lý SpreadsheetML (.xls XML) & CSV

        private List<ElectricalPanel> ImportPanelsFromSpreadsheetML(string filePath)
        {
            List<ElectricalPanel> panels = new List<ElectricalPanel>();
            try
            {
                XmlDocument doc = new XmlDocument();
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    doc.Load(fs);
                }

                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("ss", "urn:schemas-microsoft-com:office:spreadsheet");

                XmlNodeList worksheetNodes = doc.SelectNodes("//ss:Worksheet", nsmgr);
                if (worksheetNodes == null || worksheetNodes.Count == 0) return panels;

                foreach (XmlNode ws in worksheetNodes)
                {
                    string sheetName = ws.Attributes["ss:Name"] != null ? ws.Attributes["ss:Name"].Value : "Tủ điện";
                    XmlNodeList rowNodes = ws.SelectNodes("ss:Table/ss:Row", nsmgr);
                    if (rowNodes == null || rowNodes.Count == 0) continue;

                    List<Dictionary<int, string>> rows = new List<Dictionary<int, string>>();

                    foreach (XmlNode rowNode in rowNodes)
                    {
                        Dictionary<int, string> rowDict = new Dictionary<int, string>();
                        XmlNodeList cellNodes = rowNode.SelectNodes("ss:Cell", nsmgr);
                        if (cellNodes == null) continue;

                        int currentCol = 0;
                        foreach (XmlNode cell in cellNodes)
                        {
                            int explicitIdx;
                            if (cell.Attributes["ss:Index"] != null && int.TryParse(cell.Attributes["ss:Index"].Value, out explicitIdx))
                            {
                                currentCol = explicitIdx - 1;
                            }

                            XmlNode dataNode = cell.SelectSingleNode("ss:Data", nsmgr);
                            string text = dataNode != null ? dataNode.InnerText.Trim() : string.Empty;
                            rowDict[currentCol] = text;
                            currentCol++;
                        }

                        if (rowDict.Count > 0) rows.Add(rowDict);
                    }

                    ElectricalPanel pnl = ParsePanelFromRows(sheetName, rows);
                    if (pnl != null && pnl.Loads != null && pnl.Loads.Count > 0)
                    {
                        panels.Add(pnl);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ExcelImportService] Lỗi SpreadsheetML: " + ex.Message);
            }

            return panels;
        }

        private List<ElectricalLoad> ImportFromCsv(string filePath)
        {
            List<Dictionary<int, string>> rows = new List<Dictionary<int, string>>();
            try
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    char sep = ',';
                    if (line.Contains(";")) sep = ';';
                    else if (line.Contains("\t")) sep = '\t';

                    string[] parts = line.Split(sep);
                    Dictionary<int, string> r = new Dictionary<int, string>();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        r[i] = parts[i].Trim();
                    }
                    rows.Add(r);
                }
            }
            catch { }

            ElectricalPanel pnl = ParsePanelFromRows(Path.GetFileNameWithoutExtension(filePath), rows);
            return pnl != null ? pnl.Loads : new List<ElectricalLoad>();
        }

        #endregion

        #region Smart Header Mapping & Parsing Nghiệp Vụ Điện

        /// <summary>
        /// Phân tích ma trận các ô từ một Sheet thành đối tượng ElectricalPanel hoàn chỉnh
        /// </summary>
        private ElectricalPanel ParsePanelFromRows(string sheetName, List<Dictionary<int, string>> rows)
        {
            if (rows == null || rows.Count == 0) return null;

            ElectricalPanel panel = new ElectricalPanel();
            panel.PanelName = !string.IsNullOrEmpty(sheetName) ? sheetName.Trim() : "DB-HVAC-01";
            panel.Loads = new List<ElectricalLoad>();
            panel.SimultaneousFactor = 0.85;
            panel.PowerFactor = 0.85;
            panel.VoltageV = 380;

            // Xác định cấu hình Nguồn ATS nếu tên Sheet có chữ MSB, ATS hoặc NGUON KEP
            string upperSheet = panel.PanelName.ToUpperInvariant();
            if (upperSheet.Contains("ATS") || upperSheet.Contains("MSB") || upperSheet.Contains("TONG") || upperSheet.Contains("MDB"))
            {
                panel.Incomer = IncomerType.DualATS;
            }

            // 1. Tìm dòng Header bằng Smart Header Detection (quét 12 dòng đầu tiên)
            int headerRowIdx = -1;
            int colStt = -1, colName = -1, colPower = -1, colKdt = -1, colCosPhi = -1, colItt = -1;
            int colPhase = -1, colLength = -1, colCbType = -1, colCbRating = -1, colCbIcu = -1;
            int colLoadType = -1;
            int colCableInsul = -1, colCableCores = -1, colCableSize = -1, colVoltageDrop = -1;
            int colStarter = -1, colControl = -1, colNotes = -1;

            int scanLimit = Math.Min(12, rows.Count);
            for (int r = 0; r < scanLimit; r++)
            {
                Dictionary<int, string> row = rows[r];

                foreach (var kvp in row)
                {
                    string text = NormalizeHeaderText(kvp.Value);
                    int c = kvp.Key;

                    if (text == "STT" || text.StartsWith("STT") || text == "LO" || text == "MA LO" || text == "CIRCUIT" || text == "ITEM")
                    {
                        if (colStt == -1) colStt = c;
                    }
                    else if (IsLoadNameColumn(text))
                    {
                        if (colName == -1) colName = c;
                    }
                    else if (IsPowerColumn(text))
                    {
                        if (colPower == -1) colPower = c;
                    }
                    else if (text.Contains("KDT") || text.Contains("DONG THOI") || text.Contains("SIMULTANEOUS"))
                    {
                        if (colKdt == -1) colKdt = c;
                    }
                    else if (text.Contains("COS") || text.Contains("COSPHI"))
                    {
                        if (colCosPhi == -1) colCosPhi = c;
                    }
                    else if (text == "ITT" || text.Contains("DONG TINH TOAN") || text.Contains("DONG DIEN") || text.Contains("CURRENT"))
                    {
                        if (colItt == -1) colItt = c;
                    }
                    else if (text == "PHA" || text.Contains("SO PHA") || text == "PHASE" || text == "1P/3P")
                    {
                        if (colPhase == -1) colPhase = c;
                    }
                    else if (text.Contains("CHIEU DAI") || text.Contains("LENGTH") || text.Contains("L (M)") || text.Contains("L(M)"))
                    {
                        if (colLength == -1) colLength = c;
                    }
                    else if (text.Contains("LOAI TAI") || text.Contains("LOAD TYPE"))
                    {
                        if (colLoadType == -1) colLoadType = c;
                    }
                    else if (text == "LOAI CB" || text == "CB" || text.Contains("BREAKER"))
                    {
                        if (colCbType == -1) colCbType = c;
                    }
                    else if (text == "LOAI" && !text.Contains("TAI"))
                    {
                        if (colCbType == -1) colCbType = c;
                    }
                    else if (text.Contains("THIET BI BAO VE"))
                    {
                        if (colCbType == -1 && r > 0) colCbType = c;
                    }
                    else if (text.Contains("DINH MUC") || text.Contains("CHON APTOMAT") || text == "IN" || text.Contains("RATING") || text.Contains("AMPERE"))
                    {
                        if (colCbRating == -1) colCbRating = c;
                    }
                    else if (text.Contains("DONG CAT") || text.Contains("NGAN MACH") || text.Contains("ICU") || text.Contains("KA"))
                    {
                        if (colCbIcu == -1) colCbIcu = c;
                    }
                    else if (text.Contains("CACH DIEN") || text.Contains("CHUNG LOAI") || text == "XLPE" || text == "PVC")
                    {
                        if (colCableInsul == -1) colCableInsul = c;
                    }
                    else if (text.Contains("SO SOI") || text.Contains("SO LOI") || text == "CORE")
                    {
                        if (colCableCores == -1) colCableCores = c;
                    }
                    else if (text.Contains("TIET DIEN") || text.Contains("PHASE (MM2)") || text.Contains("MM2") || text.Contains("SIZE"))
                    {
                        if (colCableSize == -1) colCableSize = c;
                    }
                    else if (text.Contains("SUT AP") || text.Contains("VOLTAGE DROP"))
                    {
                        if (colVoltageDrop == -1) colVoltageDrop = c;
                    }
                    else if (text.Contains("KHOI DONG") || text.Contains("STARTER") || text.Contains("STARTING") || text.Contains("START") || text.Contains("DOL") || text.Contains("Y-D"))
                    {
                        if (colStarter == -1) colStarter = c;
                    }
                    else if (text.Contains("DIEU KHIEN") || text.Contains("CONTROL") || text.Contains("HOA") || text.Contains("FS"))
                    {
                        if (colControl == -1) colControl = c;
                    }
                    else if (text.Contains("GHI CHU") || text.Contains("NOTE") || text.Contains("REMARKS"))
                    {
                        if (colNotes == -1) colNotes = c;
                    }
                }

                // Nhận diện dòng Header: bắt buộc có STT hoặc đồng thời có Tên tải và Công suất
                bool hasStt = (colStt != -1);
                bool hasName = (colName != -1);
                bool hasPower = (colPower != -1);

                if ((hasStt && (hasName || hasPower || colCbRating != -1)) || (hasName && hasPower))
                {
                    headerRowIdx = r;

                    // Quét tiếp dòng phụ bên dưới (Header 2 tầng) để bổ sung các cột chi tiết
                    if (r + 1 < scanLimit)
                    {
                        Dictionary<int, string> subRow = rows[r + 1];
                        foreach (var kvp in subRow)
                        {
                            string subText = NormalizeHeaderText(kvp.Value);
                            int c = kvp.Key;
                            if (subText.Contains("LOAI TAI") || subText.Contains("LOAD TYPE"))
                            {
                                colLoadType = c;
                                if (colCbType == c) colCbType = -1;
                            }
                            else if (subText == "LOAI" || subText == "LOAI CB" || subText == "CHUNG LOAI")
                            {
                                colCbType = c;
                            }
                            if (subText == "PHA" || subText.Contains("SO PHA") || subText == "1P/3P")
                            {
                                colPhase = c;
                            }
                            if ((subText.Contains("DINH MUC") || subText.Contains("CHON APTOMAT") || subText == "IN" || subText.Contains("RATING")) && colCbRating == -1) colCbRating = c;
                            if ((subText.Contains("DONG CAT") || subText.Contains("NGAN MACH") || subText.Contains("ICU") || subText.Contains("KA")) && colCbIcu == -1) colCbIcu = c;
                            if ((subText.Contains("CACH DIEN") || subText.Contains("CHUNG LOAI") || subText == "XLPE" || subText == "PVC") && colCableInsul == -1) colCableInsul = c;
                            if ((subText.Contains("SO SOI") || subText.Contains("SO LOI")) && colCableCores == -1) colCableCores = c;
                            if ((subText.Contains("TIET DIEN") || subText.Contains("MM2")) && colCableSize == -1) colCableSize = c;
                            if ((subText.Contains("CHIEU DAI") || subText.Contains("L (M)")) && colLength == -1) colLength = c;
                            if (subText.Contains("SUT AP") && colVoltageDrop == -1) colVoltageDrop = c;
                            if ((subText.Contains("KHOI DONG") || subText.Contains("STARTER") || subText.Contains("STARTING") || subText.Contains("DOL") || subText.Contains("Y-D")) && colStarter == -1) colStarter = c;
                            if ((subText.Contains("DIEU KHIEN") || subText.Contains("CONTROL") || subText.Contains("HOA") || subText.Contains("FS")) && colControl == -1) colControl = c;
                        }
                        if (colCbType == colLoadType)
                        {
                            colCbType = -1;
                        }
                        // Dòng tiếp theo là dòng sub-header, bước bắt đầu dữ liệu thực là r + 1
                        headerRowIdx = r + 1;
                    }
                    break;
                }
            }

            // Fallback nếu không phát hiện được tiêu đề: gán theo vị trí mặc định phổ biến
            if (headerRowIdx == -1)
            {
                colStt = 0;
                colName = 1;
                colPhase = 2;
                colPower = 3;
                colLength = 4;
                colStarter = 5;
                colControl = 6;
                headerRowIdx = 0;
            }

            // Nếu colName vẫn chưa tìm thấy nhưng colStt=0 và colPower=4 (hoặc tương tự), colName thường là cột 1
            if (colName == -1)
            {
                colName = (colStt == 0) ? 1 : 0;
            }

            int loadSeq = 1;

            // 2. Duyệt qua các dòng dữ liệu sau dòng Header
            for (int r = headerRowIdx + 1; r < rows.Count; r++)
            {
                Dictionary<int, string> row = rows[r];

                string sttStr = GetValueOrDefault(row, colStt);
                string nameStr = GetValueOrDefault(row, colName);
                string loadTypeStr = GetValueOrDefault(row, colLoadType);
                string pwrStr = GetValueOrDefault(row, colPower);
                string kdtStr = GetValueOrDefault(row, colKdt);
                string cosStr = GetValueOrDefault(row, colCosPhi);
                string ittStr = GetValueOrDefault(row, colItt);
                string phaseStr = GetValueOrDefault(row, colPhase);
                string lenStr = GetValueOrDefault(row, colLength);
                string cbTypeStr = GetValueOrDefault(row, colCbType);
                string cbRatingStr = GetValueOrDefault(row, colCbRating);
                string cbIcuStr = GetValueOrDefault(row, colCbIcu);
                string cableInsulStr = GetValueOrDefault(row, colCableInsul);
                string cableCoresStr = GetValueOrDefault(row, colCableCores);
                string cableSizeStr = GetValueOrDefault(row, colCableSize);
                string starterStr = GetValueOrDefault(row, colStarter);
                string ctrlStr = GetValueOrDefault(row, colControl);
                string notesStr = GetValueOrDefault(row, colNotes);

                // Kiểm tra dòng tổng kết (Summary Row) hoặc dòng ghi chú chân trang
                string combinedRowText = (sttStr + " " + nameStr + " " + notesStr).ToUpperInvariant();
                string normCombined = NormalizeHeaderText(combinedRowText);

                if (string.IsNullOrEmpty(sttStr) || normCombined.Contains("TONG") || normCombined.Contains("SUAT DAT") || normCombined.Contains("DONG THOI") || normCombined.Contains("TOTAL"))
                {
                    if (normCombined.Contains("CONG SUAT") || normCombined.Contains("SUAT DAT") || normCombined.Contains("TOTAL"))
                    {
                        double pTotal;
                        if (TryParseDouble(pwrStr, out pTotal) && pTotal > 0)
                        {
                            panel.TotalCalculatedPowerKW = pTotal;
                        }
                        double cVal;
                        if (TryParseDouble(cosStr, out cVal) && cVal > 0)
                        {
                            panel.PowerFactor = cVal;
                        }
                        double iVal;
                        if (TryParseDouble(ittStr, out iVal) && iVal > 0)
                        {
                            panel.TotalCurrentA = iVal;
                        }
                    }
                    else if (normCombined.Contains("DONG THOI") || normCombined.Contains("SIMULTANEOUS"))
                    {
                        double kVal;
                        if (TryParseDouble(kdtStr, out kVal) && kVal > 0)
                        {
                            panel.SimultaneousFactor = kVal;
                        }
                    }
                    continue;
                }

                // Bỏ qua dòng trống hoàn toàn hoặc dòng lặp lại tiêu đề
                if (string.IsNullOrEmpty(sttStr) && string.IsNullOrEmpty(nameStr) && string.IsNullOrEmpty(pwrStr))
                {
                    continue;
                }
                if (normCombined.Contains("TEN PHU TAI") || normCombined.Contains("THIET BI BAO VE") || normCombined.Contains("TINH TOAN"))
                {
                    continue;
                }

                // Xây dựng ElectricalLoad
                ElectricalLoad load = new ElectricalLoad();
                load.Stt = loadSeq++;
                load.LoadName = !string.IsNullOrEmpty(nameStr) ? nameStr.Trim() : ("Phụ tải " + load.Stt);

                // Chuẩn hóa Loại tải
                string normLoadType = NormalizeHeaderText(loadTypeStr);
                if (normLoadType.Contains("MOTOR") || normLoadType.Contains("DONG CO"))
                {
                    load.LoadType = "Động cơ";
                }
                else if (normLoadType.Contains("FEEDER") || normLoadType.Contains("CAP NGUON") || normLoadType.Contains("PANEL"))
                {
                    load.LoadType = "Cấp nguồn tủ";
                }
                else if (normLoadType.Contains("CHIEU SANG") || normLoadType.Contains("LIGHTING"))
                {
                    load.LoadType = "Chiếu sáng";
                }
                else if (normLoadType.Contains("O CAM") || normLoadType.Contains("SOCKET"))
                {
                    load.LoadType = "Ổ cắm";
                }
                else if (normLoadType.Contains("OTHER") || normLoadType.Contains("KHAC"))
                {
                    load.LoadType = "Tải khác";
                }
                else if (!string.IsNullOrEmpty(loadTypeStr))
                {
                    load.LoadType = loadTypeStr.Trim();
                }
                else
                {
                    // Fallback theo tên tải nếu không có cột Loại Tải trong file Excel
                    string normN = NormalizeHeaderText(load.LoadName);
                    if (normN.Contains("SPARE") || normN.Contains("DU PHONG"))
                    {
                        load.IsSpare = true;
                        load.DrawOutgoingWire = false;
                        load.LoadType = "Tải khác";
                    }
                    else if (normN.Contains("CHIEU SANG") || normN.Contains("DEN") || normN.Contains("LIGHT"))
                    {
                        load.LoadType = "Chiếu sáng";
                    }
                    else if (normN.Contains("O CAM") || normN.Contains("SOCKET"))
                    {
                        load.LoadType = "Ổ cắm";
                    }
                    else if (normN.Contains("DB-") || normN.Contains("TU PHAN PHOI") || normN.Contains("SUB-PANEL") || (normN.Contains("MCC-") && !normN.Contains("SAY") && !normN.Contains("FAN")))
                    {
                        load.LoadType = "Cấp nguồn tủ";
                    }
                    else
                    {
                        load.LoadType = "Động cơ";
                    }
                }

                // Xử lý Công suất (kW)
                double power = 10.0;
                double parsedP;
                if (TryParseDouble(pwrStr, out parsedP))
                {
                    power = parsedP;
                }
                load.PowerKW = Math.Max(0.0, power);

                // Xử lý Chiều dài cáp (m)
                double length = 25.0;
                double parsedL;
                if (TryParseDouble(lenStr, out parsedL))
                {
                    length = parsedL;
                }
                load.CableLength = Math.Max(1.0, length);

                // Xử lý Số pha (1P / 3P)
                string normPhase = NormalizeHeaderText(phaseStr);
                if (normPhase.Contains("1") || normPhase == "1P" || normPhase.Contains("1 PHA"))
                {
                    load.Phase = PhaseType.SinglePhase;
                }
                else if (normPhase.Contains("3") || normPhase == "3P" || normPhase.Contains("3 PHA"))
                {
                    load.Phase = PhaseType.ThreePhase;
                }
                else
                {
                    // Tự suy đoán dựa theo công suất và tên tải
                    load.Phase = load.PowerKW > 7.5 ? PhaseType.ThreePhase : PhaseType.SinglePhase;
                }

                // Xử lý Thiết bị bảo vệ (Aptomat)
                string cbType = !string.IsNullOrEmpty(cbTypeStr) ? cbTypeStr.Trim().ToUpperInvariant() : "";
                string cbRating = !string.IsNullOrEmpty(cbRatingStr) ? cbRatingStr.Trim() : "";
                string cbIcu = !string.IsNullOrEmpty(cbIcuStr) ? cbIcuStr.Trim() : "";

                if (cbType.Contains("RCBO")) cbType = "RCBO";
                else if (cbType.Contains("RCCB")) cbType = "RCCB";
                else if (cbType.Contains("MCCB")) cbType = "MCCB";
                else if (cbType.Contains("MCB")) cbType = "MCB";
                else
                {
                    cbType = (load.Phase == PhaseType.ThreePhase && load.PowerKW > 7.5) ? "MCCB" : (load.Phase == PhaseType.ThreePhase ? "MCB" : "MCB");
                }

                string poles = "1P";
                if (load.Phase == PhaseType.ThreePhase)
                {
                    poles = (normPhase.Contains("4") || normPhase == "4P") ? "4P" : "3P";
                    if (cbType == "RCCB" || cbType == "RCBO") poles = "4P";
                }
                else
                {
                    poles = (normPhase.Contains("2") || normPhase == "2P" || cbType == "RCCB" || cbType == "RCBO") ? "2P" : "1P";
                }

                string ratingNum = "";
                if (!string.IsNullOrEmpty(cbRating))
                {
                    Match m = Regex.Match(cbRating, @"\d+");
                    if (m.Success) ratingNum = m.Value;
                }
                if (string.IsNullOrEmpty(ratingNum))
                {
                    ratingNum = (load.Phase == PhaseType.ThreePhase ? (cbType == "MCCB" ? "40" : "20") : "16");
                }

                string icuVal = !string.IsNullOrEmpty(cbIcu) ? cbIcu : (load.Phase == PhaseType.ThreePhase ? (cbType == "MCCB" ? "25kA" : "10kA") : "6kA");
                if (!icuVal.ToUpperInvariant().EndsWith("KA")) icuVal += "kA";

                load.BreakerType = string.Format("{0} {1}", cbType, poles);
                load.BreakerSpec = string.Format("{0} {1}-{2} {3}A", cbType, poles, icuVal, ratingNum);
                load.IsBreakerUserDefined = true;

                // Xử lý Cáp điện
                string cSize = !string.IsNullOrEmpty(cableSizeStr) ? cableSizeStr.Trim() : "";
                string cInsul = !string.IsNullOrEmpty(cableInsulStr) ? cableInsulStr.Trim().ToUpperInvariant() : "XLPE";
                string cCores = !string.IsNullOrEmpty(cableCoresStr) ? cableCoresStr.Trim() : "";

                if (!string.IsNullOrEmpty(cSize))
                {
                    string cableTypePrefix = cInsul.Contains("PVC") ? "Cu/PVC/PVC" : "Cu/XLPE/PVC";
                    load.CableType = cableTypePrefix;
                    load.ConductorSize = cSize;

                    if (cSize.Contains("+") || cSize.Contains("x") || cSize.Contains("X"))
                    {
                        load.CableSpec = string.Format("{0} {1}mm2", cableTypePrefix, cSize);
                    }
                    else
                    {
                        string coreStr = load.Phase == PhaseType.ThreePhase ? "4C" : "2C";
                        if (cCores == "1" || cCores.Contains("1C")) coreStr = load.Phase == PhaseType.ThreePhase ? "3x1C" : "2x1C";
                        load.CableSpec = string.Format("{0} {1}-{2}mm2", cableTypePrefix, coreStr, cSize);
                    }

                    // Tự tính tiết diện dây tiếp địa PE (E) theo IEC 60364-5-54
                    double sizeVal = 2.5;
                    Match mSize = Regex.Match(cSize, @"\d+(\.\d+)?");
                    if (mSize.Success) double.TryParse(mSize.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out sizeVal);

                    double groundSize = sizeVal <= 16 ? sizeVal : (sizeVal <= 35 ? 16 : sizeVal / 2.0);
                    load.GroundSize = groundSize.ToString("0.#");
                    load.GroundWireSpec = string.Format("(E) Cu/PVC 1C-{0}mm2", load.GroundSize);
                }

                // Xử lý Phương thức Khởi động (Starter)
                string normStarter = NormalizeHeaderText(starterStr);
                string normName = NormalizeHeaderText(load.LoadName);

                if (normStarter.Contains("STAR") || normStarter.Contains("SAO") || normStarter.Contains("Y-D") || normStarter.Contains("Y-I") || starterStr.Contains("Δ") || starterStr.Contains("\u0394"))
                {
                    load.Starter = StarterType.StarDelta;
                }
                else if (normStarter.Contains("VFD") || normStarter.Contains("BIEN TAN") || normStarter.Contains("INVERTER"))
                {
                    load.Starter = StarterType.VFD;
                }
                else if (normStarter.Contains("SOFT") || normStarter.Contains("MEM") || normStarter.Contains("SS"))
                {
                    load.Starter = StarterType.SoftStarter;
                }
                else if (normStarter.Contains("DOL") || normStarter.Contains("TRUC TIEP"))
                {
                    load.Starter = StarterType.DOL;
                }
                else if (load.LoadType == "Động cơ")
                {
                    // Nếu là Động cơ mà cột khởi động để trống: <= 7.5kW mặc định DOL, > 7.5kW mặc định Sao - Tam giác
                    load.Starter = load.PowerKW <= 7.5 ? StarterType.DOL : StarterType.StarDelta;
                }
                else
                {
                    // Tải khác / Cấp nguồn tủ / Chiếu sáng / Ổ cắm -> Mặc định Direct (-)
                    load.Starter = StarterType.Direct;
                }

                // Xử lý Chế độ Điều khiển (Control Mode)
                string normCtrl = NormalizeHeaderText(ctrlStr);
                string normNotes = NormalizeHeaderText(notesStr);

                if (normCtrl.Contains("BMS") || normNotes.Contains("BMS") || normName.Contains("DDC"))
                {
                    load.Control = ControlMode.BMS;
                }
                else if (normCtrl.Contains("PLC") || normNotes.Contains("PLC"))
                {
                    load.Control = ControlMode.PLC;
                }
                else if (normCtrl.Contains("TIMER") || normNotes.Contains("TIMER") || normNotes.Contains("HEN GIO"))
                {
                    load.Control = ControlMode.Timer;
                }
                else if (normCtrl.Contains("FS") || normCtrl.Contains("PHAO") || normNotes.Contains("PHAO") || normName.Contains("SUMP"))
                {
                    load.Control = ControlMode.FloatSwitch;
                }
                else if (normCtrl.Contains("PS") || normCtrl.Contains("AP SUAT") || normNotes.Contains("AP SUAT") || normName.Contains("BOOSTER"))
                {
                    load.Control = ControlMode.PressureSwitch;
                }
                else if (normCtrl.Contains("HOA") || normCtrl.Contains("AOM") || normCtrl.Contains("AUTO") || load.Starter != StarterType.Direct)
                {
                    load.Control = ControlMode.AutoManual;
                }
                else
                {
                    load.Control = ControlMode.None;
                }

                // Đánh dấu phụ tải Dự Phòng (Spare) nếu có
                if (normName.Contains("SPARE") || normName.Contains("DU PHONG"))
                {
                    load.IsSpare = true;
                    load.DrawOutgoingWire = false;
                    load.PowerKW = 0.0;
                }

                panel.Loads.Add(load);
            }

            return panel;
        }

        private bool IsLoadNameColumn(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("BAO VE")) return false; // Không nhầm với "THIET BI BAO VE"
            if (text.Contains("CHON DAY") || text.Contains("TINH TOAN")) return false; // Không nhầm với tiêu đề nhóm
            if (text.Contains("TEN") && (text.Contains("TAI") || text.Contains("PHU") || text.Contains("THIET BI"))) return true;
            if (text.Contains("PHU TAI") || text.Contains("DIEN GIAI") || text.Contains("LOAD NAME") || text.Contains("DESCRIPTION")) return true;
            if (text.Contains("LO CAP") || text.Contains("DANH MUC") || text == "TEN" || text == "THIET BI") return true;
            return false;
        }

        private bool IsPowerColumn(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("BANG") || text.Contains("TINH TOAN") || text.Contains("CHON DAY")) return false; // Không nhầm tiêu đề bảng
            if (text.Contains("PD (KW)") || text.Contains("P(KW)") || text.Contains("P (KW)") || text.Contains("PD/TB") || text == "KW" || text == "P" || text.StartsWith("PD")) return true;
            if (text == "CONG SUAT" || text == "CONG SUAT (KW)" || (text.Contains("CONG SUAT") && !text.Contains("TONG"))) return true;
            return false;
        }

        private string NormalizeHeaderText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Thay thế các ký tự lỗi mã hóa UTF-8 phổ biến thành chữ thường
            string s = text.Replace('\uFFFD', ' ').Replace('?', ' ');
            string norm = RemoveDiacritics(s).ToUpperInvariant().Trim();
            // Gom nhiều khoảng trắng
            norm = Regex.Replace(norm, @"\s+", " ");
            return norm;
        }

        private string GetValueOrDefault(Dictionary<int, string> row, int colIdx)
        {
            if (colIdx >= 0 && row.ContainsKey(colIdx)) return row[colIdx];
            return string.Empty;
        }

        private bool TryParseDouble(string text, out double val)
        {
            val = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string clean = text.Trim().Replace(',', '.');
            return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out val);
        }

        private string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string normalized = text.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in normalized)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
        }

        #endregion

        #region Tạo File Mẫu Template Đầy Đủ (.xlsx & .xls)

        /// <summary>
        /// Tạo file mẫu Excel chuẩn kỹ thuật (.xlsx hoặc .xls) bao gồm đầy đủ các lộ mẫu tiêu biểu
        /// </summary>
        public bool CreateSampleTemplate(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext == ".xlsx")
                {
                    return CreateSampleTemplateXlsx(filePath);
                }
                else
                {
                    return CreateSampleTemplateSpreadsheetML(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ExcelImportService] Lỗi tạo template: " + ex.Message);
                return false;
            }
        }

        private bool CreateSampleTemplateXlsx(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // [Content_Types].xml
                ZipArchiveEntry ctEntry = zip.CreateEntry("[Content_Types].xml");
                using (StreamWriter w = new StreamWriter(ctEntry.Open(), Encoding.UTF8))
                {
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n" +
                            "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n" +
                            "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n" +
                            "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n" +
                            "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n" +
                            "</Types>");
                }

                // _rels/.rels
                ZipArchiveEntry relsEntry = zip.CreateEntry("_rels/.rels");
                using (StreamWriter w = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
                {
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
                            "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n" +
                            "</Relationships>");
                }

                // xl/_rels/workbook.xml.rels
                ZipArchiveEntry wbRelsEntry = zip.CreateEntry("xl/_rels/workbook.xml.rels");
                using (StreamWriter w = new StreamWriter(wbRelsEntry.Open(), Encoding.UTF8))
                {
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
                            "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n" +
                            "</Relationships>");
                }

                // xl/workbook.xml
                ZipArchiveEntry wbEntry = zip.CreateEntry("xl/workbook.xml");
                using (StreamWriter w = new StreamWriter(wbEntry.Open(), Encoding.UTF8))
                {
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n" +
                            "  <sheets>\n" +
                            "    <sheet name=\"DB-HVAC-01\" sheetId=\"1\" r:id=\"rId1\"/>\n" +
                            "  </sheets>\n" +
                            "</workbook>");
                }

                // xl/worksheets/sheet1.xml
                ZipArchiveEntry sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
                using (StreamWriter w = new StreamWriter(sheetEntry.Open(), Encoding.UTF8))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    sb.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                    sb.AppendLine("  <sheetData>");

                    // Row 1: Tiêu đề Bảng tính
                    sb.AppendLine("    <row r=\"1\">");
                    sb.AppendLine("      <c r=\"A1\" t=\"inlineStr\"><is><t>BẢNG TÍNH TOÁN PHỤ TẢI &amp; THIẾT KẾ SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN (NTT TOOLS)</t></is></c>");
                    sb.AppendLine("    </row>");

                    // Row 2: Header chính
                    sb.AppendLine("    <row r=\"2\">");
                    string[] headers = new string[]
                    {
                        "STT", "Tên phụ tải", "Pd (kW)", "Kdt", "Cos phi", "Số pha", "Chiều dài (m)",
                        "Loại CB", "Dòng định mức (A)", "Dòng cắt (kA)", "Cách điện", "Tiết diện cáp (mm2)",
                        "Khởi động", "Điều khiển", "Ghi chú"
                    };
                    string[] cols = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        sb.AppendLine(string.Format("      <c r=\"{0}2\" t=\"inlineStr\"><is><t>{1}</t></is></c>", cols[i], headers[i]));
                    }
                    sb.AppendLine("    </row>");

                    // Các dòng dữ liệu mẫu đại diện đầy đủ mọi trường hợp
                    string[][] sampleData = new string[][]
                    {
                        new string[] { "L1", "BƠM NƯỚC LẠNH CHILLER 01", "30.0", "1.0", "0.85", "3P", "35", "MCCB", "80", "25kA", "XLPE", "35", "Y-D", "HOA", "Khởi động Sao - Tam giác" },
                        new string[] { "L2", "BƠM NƯỚC GIẢI NHIỆT 01", "22.0", "1.0", "0.85", "3P", "30", "MCCB", "63", "25kA", "XLPE", "25", "Y-D", "HOA", "Khởi động Sao - Tam giác" },
                        new string[] { "L3", "QUẠT TĂNG ÁP CẦU THANG FAN-01", "15.0", "1.0", "0.85", "3P", "25", "MCCB", "40", "18kA", "XLPE", "10", "DOL", "BMS", "Khởi động trực tiếp DOL" },
                        new string[] { "L4", "BƠM TĂNG ÁP BOOSTER PUMP", "11.0", "1.0", "0.85", "3P", "40", "MCCB", "32", "18kA", "XLPE", "6", "VFD", "PS", "Biến tần điều áp PS" },
                        new string[] { "L5", "BƠM CỨU HỎA BÙ ÁP JOCKEY PUMP", "7.5", "1.0", "0.85", "3P", "20", "MCCB", "25", "18kA", "XLPE", "4", "SS", "PS", "Khởi động mềm Soft Starter" },
                        new string[] { "L6", "CHIẾU SÁNG PHÒNG KỸ THUẬT", "2.5", "1.0", "0.90", "1P", "30", "RCBO", "16", "6kA", "PVC", "2.5", "Direct", "Timer", "RCBO chống rò 30mA + Timer" },
                        new string[] { "L7", "Ổ CẮM BẢO TRÌ PHÒNG MÁY", "3.0", "1.0", "0.85", "1P", "20", "RCBO", "20", "6kA", "PVC", "2.5", "Direct", "None", "RCBO chống rò 30mA" },
                        new string[] { "L8", "NGUỒN CẤP TỦ PHÂN PHỐI DB-TẦNG 1", "45.0", "0.85", "0.85", "3P", "60", "MCCB", "125", "36kA", "XLPE", "50", "Direct", "None", "Cấp nguồn đến tủ DB-TẦNG 1" },
                        new string[] { "L9", "DỰ PHÒNG 3 PHA (SPARE 3P)", "0.0", "1.0", "0.85", "3P", "0", "MCCB", "40", "25kA", "XLPE", "-", "Direct", "None", "Lộ dự phòng 3P" },
                        new string[] { "L10", "DỰ PHÒNG 1 PHA (SPARE 1P)", "0.0", "1.0", "0.85", "1P", "0", "MCB", "16", "6kA", "PVC", "-", "Direct", "None", "Lộ dự phòng 1P" }
                    };

                    int rIdx = 3;
                    foreach (string[] row in sampleData)
                    {
                        sb.AppendLine(string.Format("    <row r=\"{0}\">", rIdx));
                        for (int c = 0; c < row.Length; c++)
                        {
                            sb.AppendLine(string.Format("      <c r=\"{0}{1}\" t=\"inlineStr\"><is><t>{2}</t></is></c>", cols[c], rIdx, row[c]));
                        }
                        sb.AppendLine("    </row>");
                        rIdx++;
                    }

                    // Dòng tổng kết
                    sb.AppendLine(string.Format("    <row r=\"{0}\">", rIdx));
                    sb.AppendLine(string.Format("      <c r=\"A{0}\" t=\"inlineStr\"><is><t></t></is></c>", rIdx));
                    sb.AppendLine(string.Format("      <c r=\"B{0}\" t=\"inlineStr\"><is><t>Tổng công suất tính toán</t></is></c>", rIdx));
                    sb.AppendLine(string.Format("      <c r=\"C{0}\" t=\"inlineStr\"><is><t>112.5</t></is></c>", rIdx));
                    sb.AppendLine(string.Format("      <c r=\"D{0}\" t=\"inlineStr\"><is><t>0.85</t></is></c>", rIdx));
                    sb.AppendLine(string.Format("      <c r=\"E{0}\" t=\"inlineStr\"><is><t>0.85</t></is></c>", rIdx));
                    sb.AppendLine("    </row>");

                    sb.AppendLine("  </sheetData>");
                    sb.AppendLine("</worksheet>");

                    w.Write(sb.ToString());
                }
            }

            return true;
        }

        private bool CreateSampleTemplateSpreadsheetML(string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
            sb.AppendLine(" <Styles>");
            sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\"><Alignment ss:Vertical=\"Center\"/><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\"/></Style>");
            sb.AppendLine("  <Style ss:ID=\"Header\"><Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/><Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#005A9E\" ss:Pattern=\"Solid\"/></Style>");
            sb.AppendLine("  <Style ss:ID=\"Center\"><Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/></Style>");
            sb.AppendLine("  <Style ss:ID=\"Right\"><Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/></Style>");
            sb.AppendLine(" </Styles>");

            sb.AppendLine(" <Worksheet ss:Name=\"DB-HVAC-01\">");
            sb.AppendLine("  <Table>");
            sb.AppendLine("   <Column ss:Width=\"50\"/>");
            sb.AppendLine("   <Column ss:Width=\"220\"/>");
            sb.AppendLine("   <Column ss:Width=\"80\"/>");
            sb.AppendLine("   <Column ss:Width=\"60\"/>");
            sb.AppendLine("   <Column ss:Width=\"60\"/>");
            sb.AppendLine("   <Column ss:Width=\"70\"/>");
            sb.AppendLine("   <Column ss:Width=\"90\"/>");
            sb.AppendLine("   <Column ss:Width=\"70\"/>");
            sb.AppendLine("   <Column ss:Width=\"110\"/>");
            sb.AppendLine("   <Column ss:Width=\"90\"/>");
            sb.AppendLine("   <Column ss:Width=\"80\"/>");
            sb.AppendLine("   <Column ss:Width=\"120\"/>");
            sb.AppendLine("   <Column ss:Width=\"100\"/>");
            sb.AppendLine("   <Column ss:Width=\"90\"/>");
            sb.AppendLine("   <Column ss:Width=\"180\"/>");

            // Header
            sb.AppendLine("   <Row ss:Height=\"24\">");
            string[] headers = new string[]
            {
                "STT", "Tên phụ tải", "Pd (kW)", "Kdt", "Cos phi", "Số pha", "Chiều dài (m)",
                "Loại CB", "Dòng định mức (A)", "Dòng cắt (kA)", "Cách điện", "Tiết diện cáp (mm2)",
                "Khởi động", "Điều khiển", "Ghi chú"
            };
            foreach (string h in headers)
            {
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">{0}</Data></Cell>", h));
            }
            sb.AppendLine("   </Row>");

            // Sample rows
            string[] sampleRows = new string[]
            {
                "L1|BƠM NƯỚC LẠNH CHILLER 01|30.0|1.0|0.85|3P|35|MCCB|80|25kA|XLPE|35|Y-D|HOA|Khởi động Sao - Tam giác",
                "L2|BƠM NƯỚC GIẢI NHIỆT 01|22.0|1.0|0.85|3P|30|MCCB|63|25kA|XLPE|25|Y-D|HOA|Khởi động Sao - Tam giác",
                "L3|QUẠT TĂNG ÁP CẦU THANG FAN-01|15.0|1.0|0.85|3P|25|MCCB|40|18kA|XLPE|10|DOL|BMS|Khởi động trực tiếp DOL",
                "L4|BƠM TĂNG ÁP BOOSTER PUMP|11.0|1.0|0.85|3P|40|MCCB|32|18kA|XLPE|6|VFD|PS|Biến tần điều áp PS",
                "L5|BƠM CỨU HỎA BÙ ÁP JOCKEY PUMP|7.5|1.0|0.85|3P|20|MCCB|25|18kA|XLPE|4|SS|PS|Khởi động mềm Soft Starter",
                "L6|CHIẾU SÁNG PHÒNG KỸ THUẬT|2.5|1.0|0.90|1P|30|RCBO|16|6kA|PVC|2.5|Direct|Timer|RCBO chống rò 30mA + Timer",
                "L7|Ổ CẮM BẢO TRÌ PHÒNG MÁY|3.0|1.0|0.85|1P|20|RCBO|20|6kA|PVC|2.5|Direct|None|RCBO chống rò 30mA",
                "L8|NGUỒN CẤP TỦ PHÂN PHỐI DB-TẦNG 1|45.0|0.85|0.85|3P|60|MCCB|125|36kA|XLPE|50|Direct|None|Cấp nguồn đến tủ DB-TẦNG 1",
                "L9|DỰ PHÒNG 3 PHA (SPARE 3P)|0.0|1.0|0.85|3P|0|MCCB|40|25kA|XLPE|-|Direct|None|Lộ dự phòng 3P",
                "L10|DỰ PHÒNG 1 PHA (SPARE 1P)|0.0|1.0|0.85|1P|0|MCB|16|6kA|PVC|-|Direct|None|Lộ dự phòng 1P"
            };

            foreach (string item in sampleRows)
            {
                string[] p = item.Split('|');
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[0]));
                sb.AppendLine(string.Format("    <Cell><Data ss:Type=\"String\">{0}</Data></Cell>", p[1]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Right\"><Data ss:Type=\"Number\">{0}</Data></Cell>", p[2]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Right\"><Data ss:Type=\"Number\">{0}</Data></Cell>", p[3]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Right\"><Data ss:Type=\"Number\">{0}</Data></Cell>", p[4]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[5]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Right\"><Data ss:Type=\"Number\">{0}</Data></Cell>", p[6]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[7]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[8]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[9]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[10]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[11]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[12]));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"Center\"><Data ss:Type=\"String\">{0}</Data></Cell>", p[13]));
                sb.AppendLine(string.Format("    <Cell><Data ss:Type=\"String\">{0}</Data></Cell>", p[14]));
                sb.AppendLine("   </Row>");
            }

            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");
            sb.AppendLine("</Workbook>");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return true;
        }

        #endregion
    }
}
