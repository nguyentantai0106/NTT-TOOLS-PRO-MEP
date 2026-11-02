import subprocess

lsp = """
(defun c:DumpExactHeader ( / ss i ent ed type lay pt10 pt11 text h)
  (setq ss (ssget "X" '((-4 . ">=,*,*") (10 5620.0 90.0 0.0) (-4 . "<=,*,*") (10 5940.0 140.0 0.0))))
  (if (not ss) (setq ss (ssget "X")))
  (if ss
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq ent (ssname ss i))
        (setq ed (entget ent))
        (setq type (cdr (assoc 0 ed)))
        (setq lay (cdr (assoc 8 ed)))
        (setq pt10 (cdr (assoc 10 ed)))
        (setq pt11 (cdr (assoc 11 ed)))
        (if (and pt10 (>= (car pt10) 5620.0) (<= (car pt10) 5940.0) (>= (cadr pt10) 90.0) (<= (cadr pt10) 140.0))
          (cond
            ((= type "LINE")
             (princ (strcat "\\nLINE | Lay: " lay " | From: " (vl-prin1-to-string pt10) " To: " (vl-prin1-to-string pt11))))
            ((= type "LWPOLYLINE")
             (princ (strcat "\\nLWPOLYLINE | Lay: " lay " | Pts: "))
             (foreach item ed (if (= (car item) 10) (princ (strcat (vl-prin1-to-string (cdr item)) " "))))
            )
            ((or (= type "TEXT") (= type "MTEXT"))
             (princ (strcat "\\nTEXT | Lay: " lay " | H: " (rtos (cdr (assoc 40 ed)) 2 2) " | Pos: " (vl-prin1-to-string pt10) " | Val: " (cdr (assoc 1 ed)))))
          )
        )
        (setq i (1+ i))
      )
    )
  )
  (command "_QUIT" "_Y")
)
(c:DumpExactHeader)
"""

with open('scratch_inspect_brk.lsp', 'w', encoding='utf-8') as f:
    f.write(lsp)

dwg_path = r"g:\Other computers\My Computer\1.TỔNG HỢP DỰ ÁN\2025\2026\12.260412CPHT-FPTTTHUAN - HT CƠ ĐIỆN FPT TÂN THUẬN\2. BAN VE SHOPDRAWING\Tầng 9\MAU TU DIEN MIEN NAM.dwg"
scr_path = r"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\scratch_inspect.scr"
res = subprocess.run([r"C:\Program Files\Autodesk\AutoCAD 2023\accoreconsole.exe", "/i", dwg_path, "/s", scr_path], capture_output=True)

with open('scratch_exact_header.txt', 'wb') as f:
    f.write(res.stdout)
print("Done, length:", len(res.stdout))
