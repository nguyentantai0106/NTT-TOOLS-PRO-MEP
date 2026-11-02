# -*- coding: utf-8 -*-
import sys
sys.stdout.reconfigure(encoding='utf-8')
with open(r'TaiAiAddin\Views\TaiAiPaletteControl.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

idx_pnl = text.find('x:Name="pnlModuleElectrical"')
sub = text[idx_pnl:]

lines = sub.splitlines()
for i, l in enumerate(lines):
    if 'FluidCardStyle' in l or 'THIẾT KẾ' in l or 'BÓC TÁCH' in l:
        print(f'{i}: {l.strip()}')
