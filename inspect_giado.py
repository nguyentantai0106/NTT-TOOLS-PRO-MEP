import subprocess, os

scr_content = """(defun c:insp ()
  (setq e (entnext))
  (while e
    (setq d (entget e))
    (princ (strcat (cdr (assoc 0 d)) " "))
    (setq pts nil)
    (foreach item d (if (= (car item) 10) (setq pts (cons (cdr item) pts))))
    (if pts (princ (vl-prin1-to-string pts)))
    (princ "\\n")
    (setq e (entnext e))
  )
  (command "_QUIT" "_Y")
)
(c:insp)
"""

with open('insp.lsp', 'w') as f:
    f.write(scr_content)
with open('insp.scr', 'w') as f:
    f.write('(load "' + os.path.abspath('insp.lsp').replace('\\', '/') + '")\n')

dwg = r'D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\thư viện\Điện\Thang máng\HTS-GIADO.dwg'
cmd = [r'C:\Program Files\Autodesk\AutoCAD 2023\accoreconsole.exe', '/i', dwg, '/s', os.path.abspath('insp.scr')]
proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
text = proc.stdout.decode('utf-16le', errors='ignore')
for line in text.splitlines():
    if any(k in line for k in ['LWPOLYLINE', 'LINE', 'CIRCLE', 'INSERT', 'POINT']):
        print(line)

for f in ['insp.lsp', 'insp.scr', 'inspect_giado.py']:
    if os.path.exists(f): os.remove(f)
