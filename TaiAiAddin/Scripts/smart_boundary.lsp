;;; ==========================================================================
;;; SMART_ROOM_BOUNDARY.LSP - LỆNH XLMB (XỬ LÝ MẶT BẰNG & BO PHÒNG THÔNG MINH)
;;; Tự động bịt kín cửa đi, ôm cột kết cấu, bo tròn tường cong (Bulge), ôm tường xéo
;;; CHỈ BO DIỆN TÍCH CÁC PHÒNG BÊN TRONG CÔNG TRÌNH (Bỏ qua ngoài trời)
;;; Ghi MText Tên phòng + Diện tích (m2) và kết thúc: "Đã vẽ xong mời bạn Check"
;;; ==========================================================================

(vl-load-com)

(defun Smart-Sleep (ms / t0)
  (setq t0 (getvar "MILLISECS"))
  (while (< (- (getvar "MILLISECS") t0) ms))
)

(defun Get-SmartTempDir (/ tmp)
  (setq tmp (getvar "TEMPPREFIX"))
  (if (or (not tmp) (= tmp ""))
    (setq tmp (vl-string-right-trim "\\" (getvar "ROAMABLEROOTPREFIX")))
  )
  (vl-string-translate "\\" "/" tmp)
)

(defun Get-SmartEnginePath ()
  (cond
    ((findfile "d:/01. NGUYEN TAN TAI/CODING AI/SKILL XU LY MAT BANG/skills/smart-room-boundary/scripts/boundary_engine.py")
     "d:/01. NGUYEN TAN TAI/CODING AI/SKILL XU LY MAT BANG/skills/smart-room-boundary/scripts/boundary_engine.py")
    ((findfile "d:/01. NGUYEN TAN TAI/CODING AI/00.BAI 1_TOOLS/TaiAiAddin/Scripts/boundary_engine.py")
     "d:/01. NGUYEN TAN TAI/CODING AI/00.BAI 1_TOOLS/TaiAiAddin/Scripts/boundary_engine.py")
    (T "d:/01. NGUYEN TAN TAI/CODING AI/SKILL XU LY MAT BANG/skills/smart-room-boundary/scripts/boundary_engine.py")
  )
)

(defun Get-SmartPythonExe ()
  (cond
    ((findfile "C:\\Users\\ADMIN\\AppData\\Local\\Programs\\Orange\\python.exe")
     "C:\\Users\\ADMIN\\AppData\\Local\\Programs\\Orange\\python.exe")
    ((findfile "python.exe")
     "python.exe")
    (T "python")
  )
)

(defun Init-SmartBoundaryLayers ()
  (if (not (tblsearch "APPID" "SMART_ROOM_BOUNDARY"))
    (regapp "SMART_ROOM_BOUNDARY")
  )
  (if (not (tblsearch "LAYER" "DUONG_BAO"))
    (command "-LAYER" "M" "DUONG_BAO" "C" "1" "DUONG_BAO" "LW" "0.35" "DUONG_BAO" "")
  )
  (if (not (tblsearch "LAYER" "DUONG_BAO_TEXT"))
    (command "-LAYER" "M" "DUONG_BAO_TEXT" "C" "3" "DUONG_BAO_TEXT" "")
  )
)

;; ==========================================================================
;; LỆNH CHÍNH: XLMB & NTTXLMB
;; ==========================================================================
(defun c:XLMB (/ oldEcho oldFileDia stamp tempDir dxfPath outLsp pyEngine pyExe pyCmd timer ss minPt maxPt bboxStr)
  (Init-SmartBoundaryLayers)
  (setq oldEcho (getvar "CMDECHO"))
  (setq oldFileDia (getvar "FILEDIA"))
  (setvar "CMDECHO" 0)

  (princ "\n[XLMB] Quét chọn vùng mặt bằng kiến trúc (hoặc nhấn ENTER để quét toàn bộ): ")
  (setq ss (ssget))

  (setq bboxStr "")
  (if ss
    (progn
      (setq minPt (list 1e99 1e99) maxPt (list -1e99 -1e99))
      (vlax-for obj (vla-get-ActiveSelectionSet (vla-get-ActiveDocument (vlax-get-acad-object)))
        (if (vlax-property-available-p obj 'GetBoundingBox)
          (progn
            (vla-GetBoundingBox obj 'bMin 'bMax)
            (setq bMin (vlax-safearray->list bMin))
            (setq bMax (vlax-safearray->list bMax))
            (setq minPt (list (min (car minPt) (car bMin)) (min (cadr minPt) (cadr bMin))))
            (setq maxPt (list (max (car maxPt) (car bMax)) (max (cadr maxPt) (cadr bMax))))
          )
        )
      )
      (if (and (< (car minPt) (car maxPt)) (< (cadr minPt) (cadr maxPt)))
        (setq bboxStr (strcat " --bbox " 
                              (rtos (- (car minPt) 500.0) 2 1) " "
                              (rtos (- (cadr minPt) 500.0) 2 1) " "
                              (rtos (+ (car maxPt) 500.0) 2 1) " "
                              (rtos (+ (cadr maxPt) 500.0) 2 1)))
      )
      (princ (strcat "\n[XLMB] Đã chọn vùng mặt bằng (" (itoa (sslength ss)) " đối tượng)."))
    )
    (princ "\n[XLMB] Đã chọn quét toàn bộ bản vẽ.")
  )

  (princ "\n[XLMB] Đang phân tích hình học không gian & tự động bo các phòng bên trong nhà...")
  
  (setq tempDir (Get-SmartTempDir))
  (setq stamp (itoa (getvar "MILLISECS")))
  (setq dxfPath (strcat tempDir "plan_" stamp ".dxf"))
  (setq outLsp  (strcat tempDir "res_" stamp ".lsp"))
  (setq pyEngine (Get-SmartEnginePath))
  (setq pyExe (Get-SmartPythonExe))

  ;; Xuất file DXF tạm thời
  (setvar "FILEDIA" 0)
  (command "DXFOUT" dxfPath "V" "2018" "16")
  (setvar "FILEDIA" oldFileDia)

  ;; Khởi chạy Engine Python Universal
  (setq pyCmd (strcat "\"" pyExe "\" \"" pyEngine "\" --dxf \"" dxfPath "\" --out \"" outLsp "\"" bboxStr))
  (startapp pyCmd)

  ;; Vòng lặp chờ kết quả xử lý (tối đa 15 giây)
  (setq timer 0)
  (while (and (not (findfile outLsp)) (< timer 150))
    (Smart-Sleep 100)
    (setq timer (1+ timer))
  )

  ;; Nạp kết quả vẽ các đường bao và nhãn diện tích
  (if (findfile outLsp)
    (progn
      (load outLsp)
      (vl-file-delete outLsp)
      (if (findfile dxfPath) (vl-file-delete dxfPath))
    )
    (progn
      (if (findfile dxfPath) (vl-file-delete dxfPath))
      (princ "\n[CẢNH BÁO] Không tạo được đường bao phòng hợp lệ hoặc quá thời gian xử lý!\n")
    )
  )

  (setvar "CMDECHO" oldEcho)
  (princ)
)

(defun c:NTTXLMB () (c:XLMB))
(defun c:BAOTATCA () (c:XLMB))

;; ==========================================================================
;; LỆNH BAOPHONG: CLICK 1 ĐIỂM TRONG PHÒNG ĐỂ TỰ ĐỘNG TÌM GÓC & BO KÍN
;; ==========================================================================
(defun c:BAOPHONG (/ oldEcho oldFileDia pt ptX ptY tempDir stamp dxfPath outLsp pyEngine pyExe pyCmd timer)
  (Init-SmartBoundaryLayers)
  (setq oldEcho (getvar "CMDECHO"))
  (setq oldFileDia (getvar "FILEDIA"))
  (setvar "CMDECHO" 0)

  (setq pt (getpoint "\n[BAOPHONG] Click chọn 1 điểm bên trong phòng cần bo: "))
  (if (not pt)
    (progn
      (setvar "CMDECHO" oldEcho)
      (princ "\n[BAOPHONG] Đã hủy lệnh.\n")
      (princ)
    )
    (progn
      (setq ptX (rtos (car pt) 2 2))
      (setq ptY (rtos (cadr pt) 2 2))
      (princ (strcat "\n[BAOPHONG] Đang tìm các điểm góc & bo kín phòng tại (" ptX ", " ptY ")..."))

      (setq tempDir (Get-SmartTempDir))
      (setq stamp (itoa (getvar "MILLISECS")))
      (setq dxfPath (strcat tempDir "plan_" stamp ".dxf"))
      (setq outLsp  (strcat tempDir "res_" stamp ".lsp"))
      (setq pyEngine (Get-SmartEnginePath))
      (setq pyExe (Get-SmartPythonExe))

      ;; Xuất file DXF tạm thời
      (setvar "FILEDIA" 0)
      (command "DXFOUT" dxfPath "V" "2018" "16")
      (setvar "FILEDIA" oldFileDia)

      ;; Khởi chạy Engine Python với tham số --point
      (setq pyCmd (strcat "\"" pyExe "\" \"" pyEngine "\" --dxf \"" dxfPath "\" --out \"" outLsp "\" --point " ptX " " ptY))
      (startapp pyCmd)

      ;; Vòng lặp chờ kết quả xử lý (tối đa 15 giây)
      (setq timer 0)
      (while (and (not (findfile outLsp)) (< timer 150))
        (Smart-Sleep 100)
        (setq timer (1+ timer))
      )

      ;; Nạp kết quả vẽ đường bao và nhãn diện tích
      (if (findfile outLsp)
        (progn
          (load outLsp)
          (vl-file-delete outLsp)
          (if (findfile dxfPath) (vl-file-delete dxfPath))
        )
        (progn
          (if (findfile dxfPath) (vl-file-delete dxfPath))
          (princ "\n[CẢNH BÁO] Không tạo được đường bao phòng hợp lệ hoặc quá thời gian xử lý!\n")
        )
      )

      (setvar "CMDECHO" oldEcho)
      (princ)
    )
  )
)

(defun c:NTTBAOPHONG () (c:BAOPHONG))
(defun c:NTT_BAOPHONG () (c:BAOPHONG))

;; ==========================================================================
;; LỆNH XEMPHONG: XEM THÔNG TIN XDATA CỦA ĐƯỜNG BAO PHÒNG
;; ==========================================================================
(defun c:XEMPHONG (/ ent elist xdata appdata)
  (setq ent (car (entsel "\n[XEMPHONG] Chọn đường bao phòng (LWPOLYLINE): ")))
  (if ent
    (progn
      (setq elist (entget ent '("SMART_ROOM_BOUNDARY")))
      (setq xdata (assoc -3 elist))
      (if xdata
        (progn
          (princ "\n=== THÔNG TIN PHÒNG (SMART BOUNDARY) ===")
          (setq appdata (cdr (car (cdr xdata))))
          (foreach item appdata
            (if (= (car item) 1000) (princ (strcat "\n- " (cdr item))))
            (if (= (car item) 1040) (princ (strcat "\n- Thông số kỹ thuật: " (rtos (cdr item) 2 2))))
          )
          (princ "\n========================================\n")
        )
        (princ "\n[XEMPHONG] Đối tượng này không có dữ liệu XData 'SMART_ROOM_BOUNDARY'.\n")
      )
    )
  )
  (princ)
)

(defun c:NTTXEMPHONG () (c:XEMPHONG))

(Init-SmartBoundaryLayers)
(princ "\n[XLMB] Tiện ích Bo Phòng Thông Minh (Tự tìm góc & bo kín phòng hở) đã sẵn sàng!")
(princ "\n  - Gõ lệnh 'XLMB' hoặc 'BAOTATCA' để quét toàn bộ mặt bằng.")
(princ "\n  - Gõ lệnh 'BAOPHONG' để click bo từng phòng riêng lẻ.\n")
(princ)
