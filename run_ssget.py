import subprocess, os

cur_dir = os.path.abspath('.').replace('\\', '/')
lsp_path = cur_dir + '/test_ssget.lsp'
scr_path = cur_dir + '/test_ssget.scr'

with open(scr_path, 'w') as f:
    f.write('(load "' + lsp_path + '")\n')

dwgs = [
    'HTS Cable tray.dwg',
    'HTS Ladder.dwg',
    'HTS Elbow cable tray.dwg',
    'HTS Elbow 45 cable tray.dwg',
    'HTS-GIADO.dwg'
]

for fn in dwgs:
    dwg = r'D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Thu Viện\Điện\Thang máng\\' + fn
    print('\n=== DWG:', fn, '===')
    cmd = [
        r'C:\Program Files\Autodesk\AutoCAD 2023\accoreconsole.exe',
        '/i', dwg,
        '/s', scr_path
    ]
    proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15)
    text = proc.stdout.decode('utf-16le', errors='ignore')
    for line in text.splitlines():
        if any(k in line for k in ['Total entities', 'BlockName:', 'No entities']):
            print(line)
