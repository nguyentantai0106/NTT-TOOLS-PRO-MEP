# -*- coding: utf-8 -*-
with open(r'TaiAiAddin\Views\TaiAiPaletteControl.xaml', 'r', encoding='utf-8') as f:
    text = f.read()

idx = text.find('btnDrawTmcManual')
lines = text[:idx].splitlines()
for i, l in enumerate(lines):
    if 'x:Name="pnl' in l or 'x:Name="tab' in l or 'x:Name="border' in l:
        print(f'{i+1}: {l.strip()}')
