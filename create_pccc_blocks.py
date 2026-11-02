import os
import sys
import win32com.client
import pythoncom

def create_blocks():
    target_dir = r"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\PCCC"
    os.makedirs(target_dir, exist_ok=True)
    
    acad = win32com.client.Dispatch("AutoCAD.Application")
    acad.Visible = True
    
    def pt(x, y, z=0.0):
        return win32com.client.VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_R8, (float(x), float(y), float(z)))
    
    def clear_doc(doc):
        # Delete all model space entities
        ms = doc.ModelSpace
        for i in range(ms.Count - 1, -1, -1):
            try:
                ms.Item(i).Delete()
            except:
                pass

    # 1. Sprinkler_Pendant.dwg
    def gen_sprinkler_pendant(doc):
        ms = doc.ModelSpace
        # Outer circle R=150
        c1 = ms.AddCircle(pt(0, 0), 150.0)
        c1.Color = 1 # Red
        # Inner circle R=60
        c2 = ms.AddCircle(pt(0, 0), 60.0)
        c2.Color = 1
        # Cross lines
        l1 = ms.AddLine(pt(-150, 0), pt(150, 0))
        l1.Color = 1
        l2 = ms.AddLine(pt(0, -150), pt(0, 150))
        l2.Color = 1
        # Pendant deflector (downward fan)
        l3 = ms.AddLine(pt(-90, -70), pt(90, -70))
        l3.Color = 1
        # Downward teeth
        for x in [-90, -45, 0, 45, 90]:
            t = ms.AddLine(pt(x, -70), pt(x * 1.2, -110))
            t.Color = 1

    # 2. Sprinkler_Upright.dwg
    def gen_sprinkler_upright(doc):
        ms = doc.ModelSpace
        c1 = ms.AddCircle(pt(0, 0), 150.0)
        c1.Color = 1
        c2 = ms.AddCircle(pt(0, 0), 60.0)
        c2.Color = 1
        l1 = ms.AddLine(pt(-150, 0), pt(150, 0))
        l1.Color = 1
        l2 = ms.AddLine(pt(0, -150), pt(0, 150))
        l2.Color = 1
        # Upright deflector (upward fan)
        l3 = ms.AddLine(pt(-90, 70), pt(90, 70))
        l3.Color = 1
        for x in [-90, -45, 0, 45, 90]:
            t = ms.AddLine(pt(x, 70), pt(x * 1.2, 110))
            t.Color = 1

    # 3. Sprinkler_Sidewall.dwg
    def gen_sprinkler_sidewall(doc):
        ms = doc.ModelSpace
        # Wall plate
        pts = win32com.client.VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_R8, (-20.0, -150.0, 0.0, 20.0, -150.0, 0.0, 20.0, 150.0, 0.0, -20.0, 150.0, 0.0))
        # Draw 4 lines for wall plate
        p1 = ms.AddLine(pt(-20, -150), pt(20, -150))
        p2 = ms.AddLine(pt(20, -150), pt(20, 150))
        p3 = ms.AddLine(pt(20, 150), pt(-20, 150))
        p4 = ms.AddLine(pt(-20, 150), pt(-20, -150))
        for p in [p1, p2, p3, p4]: p.Color = 8
        # Semicircle head
        c1 = ms.AddCircle(pt(20, 0), 100.0)
        c1.Color = 1
        # Spray rays to right
        for ang in [-45, -20, 0, 20, 45]:
            import math
            rad = math.radians(ang)
            lx = 20.0 + 180.0 * math.cos(rad)
            ly = 180.0 * math.sin(rad)
            r = ms.AddLine(pt(20, 0), pt(lx, ly))
            r.Color = 1

    # 4. Tu_Chua_Chay_Vach_Tuong.dwg
    def gen_tu_vach_tuong(doc):
        ms = doc.ModelSpace
        # Cabinet box 800x250
        p1 = ms.AddLine(pt(-400, -125), pt(400, -125))
        p2 = ms.AddLine(pt(400, -125), pt(400, 125))
        p3 = ms.AddLine(pt(400, 125), pt(-400, 125))
        p4 = ms.AddLine(pt(-400, 125), pt(-400, -125))
        for p in [p1, p2, p3, p4]: p.Color = 1
        # Diagonal line
        d1 = ms.AddLine(pt(-400, -125), pt(400, 125))
        d1.Color = 1
        # Hose reel circle R=70
        c_reel = ms.AddCircle(pt(-180, 0), 70.0)
        c_reel.Color = 1
        # Nozzle
        noz = ms.AddLine(pt(-110, 0), pt(-60, 40))
        noz.Color = 1
        # Extinguishers on right side (2 small cylinders)
        b1_1 = ms.AddLine(pt(100, -90), pt(200, -90))
        b1_2 = ms.AddLine(pt(200, -90), pt(200, 90))
        b1_3 = ms.AddLine(pt(200, 90), pt(100, 90))
        b1_4 = ms.AddLine(pt(100, 90), pt(100, -90))
        for b in [b1_1, b1_2, b1_3, b1_4]: b.Color = 1
        txt = ms.AddText("PCCC", pt(115, -15), 35.0)
        txt.Color = 1

    # 5. Binh_MFZ4_Bot.dwg
    def gen_binh_bot(doc):
        ms = doc.ModelSpace
        # Circle diameter 180
        c1 = ms.AddCircle(pt(0, 0), 90.0)
        c1.Color = 1
        # Inner circle
        c2 = ms.AddCircle(pt(0, 0), 75.0)
        c2.Color = 1
        # Cross symbol
        ms.AddLine(pt(-60, 0), pt(60, 0)).Color = 1
        ms.AddLine(pt(0, -60), pt(0, 60)).Color = 1
        txt = ms.AddText("MFZ4", pt(-45, -70), 30.0)
        txt.Color = 1

    # 6. Binh_MT3_CO2.dwg
    def gen_binh_co2(doc):
        ms = doc.ModelSpace
        # Circle diameter 160
        c1 = ms.AddCircle(pt(0, 0), 80.0)
        c1.Color = 1
        # Horn nozzle (funnel)
        ms.AddLine(pt(80, 0), pt(140, 40)).Color = 1
        ms.AddLine(pt(80, 0), pt(140, -40)).Color = 1
        ms.AddLine(pt(140, 40), pt(140, -40)).Color = 1
        txt = ms.AddText("MT3", pt(-35, -15), 30.0)
        txt.Color = 1

    # 7. Van_Bao_Dong.dwg
    def gen_alarm_valve(doc):
        ms = doc.ModelSpace
        # Flanges
        ms.AddLine(pt(-120, -100), pt(-120, 100)).Color = 1
        ms.AddLine(pt(120, -100), pt(120, 100)).Color = 1
        # Valve body circle
        ms.AddCircle(pt(0, 0), 100.0).Color = 1
        # Water motor gong bell
        ms.AddCircle(pt(0, 180), 60.0).Color = 1
        ms.AddLine(pt(0, 100), pt(0, 120)).Color = 1
        txt = ms.AddText("AV", pt(-25, -20), 40.0)
        txt.Color = 1

    # 8. Van_Giam_Ap.dwg
    def gen_van_giam_ap(doc):
        ms = doc.ModelSpace
        # 2 opposed triangles
        ms.AddLine(pt(-100, -70), pt(0, 0)).Color = 1
        ms.AddLine(pt(-100, 70), pt(0, 0)).Color = 1
        ms.AddLine(pt(-100, -70), pt(-100, 70)).Color = 1
        ms.AddLine(pt(100, -70), pt(0, 0)).Color = 1
        ms.AddLine(pt(100, 70), pt(0, 0)).Color = 1
        ms.AddLine(pt(100, -70), pt(100, 70)).Color = 1
        # Pilot loop
        ms.AddLine(pt(-50, 0), pt(-50, 80)).Color = 1
        ms.AddLine(pt(-50, 80), pt(50, 80)).Color = 1
        ms.AddLine(pt(50, 80), pt(50, 0)).Color = 1
        ms.AddCircle(pt(0, 80), 20.0).Color = 1
        txt = ms.AddText("PRV", pt(-35, -45), 30.0)
        txt.Color = 1

    # 9. Hong_Tiep_Nuoc_2Nga.dwg
    def gen_hong_tiep_nuoc(doc):
        ms = doc.ModelSpace
        # Main riser
        ms.AddLine(pt(-30, -120), pt(-30, 0)).Color = 1
        ms.AddLine(pt(30, -120), pt(30, 0)).Color = 1
        ms.AddLine(pt(-30, -120), pt(30, -120)).Color = 1
        # Branch Left
        ms.AddLine(pt(-30, 0), pt(-120, 60)).Color = 1
        ms.AddCircle(pt(-120, 60), 35.0).Color = 1
        # Branch Right
        ms.AddLine(pt(30, 0), pt(120, 60)).Color = 1
        ms.AddCircle(pt(120, 60), 35.0).Color = 1
        txt = ms.AddText("HTN", pt(-35, -70), 30.0)
        txt.Color = 1

    # 10. Dau_Bao_Khoi.dwg
    def gen_dau_bao_khoi(doc):
        ms = doc.ModelSpace
        ms.AddCircle(pt(0, 0), 100.0).Color = 4 # Cyan
        ms.AddCircle(pt(0, 0), 70.0).Color = 4
        txt = ms.AddText("S", pt(-20, -30), 60.0)
        txt.Color = 4

    # 11. Dau_Bao_Nhiet.dwg
    def gen_dau_bao_nhiet(doc):
        ms = doc.ModelSpace
        ms.AddCircle(pt(0, 0), 100.0).Color = 1 # Red
        ms.AddCircle(pt(0, 0), 70.0).Color = 1
        txt = ms.AddText("H", pt(-20, -30), 60.0)
        txt.Color = 1

    # 12. Nut_Nhan_Khan.dwg
    def gen_nut_nhan_khan(doc):
        ms = doc.ModelSpace
        # Box 160x160
        ms.AddLine(pt(-80, -80), pt(80, -80)).Color = 1
        ms.AddLine(pt(80, -80), pt(80, 80)).Color = 1
        ms.AddLine(pt(80, 80), pt(-80, 80)).Color = 1
        ms.AddLine(pt(-80, 80), pt(-80, -80)).Color = 1
        ms.AddCircle(pt(0, 0), 40.0).Color = 1
        txt = ms.AddText("F", pt(-15, -25), 50.0)
        txt.Color = 1

    # 13. Chuong_Den_Bao_Chay.dwg
    def gen_chuong_den(doc):
        ms = doc.ModelSpace
        # Half circle bell
        ms.AddLine(pt(-80, 0), pt(80, 0)).Color = 1
        ms.AddCircle(pt(0, 0), 80.0).Color = 1
        # Strobe light triangle on top
        ms.AddLine(pt(-50, 0), pt(0, 70)).Color = 2 # Yellow
        ms.AddLine(pt(0, 70), pt(50, 0)).Color = 2
        txt = ms.AddText("B", pt(-15, -55), 40.0)
        txt.Color = 1

    generators = {
        "Sprinkler_Pendant.dwg": gen_sprinkler_pendant,
        "Sprinkler_Upright.dwg": gen_sprinkler_upright,
        "Sprinkler_Sidewall.dwg": gen_sprinkler_sidewall,
        "Tu_Chua_Chay_Vach_Tuong.dwg": gen_tu_vach_tuong,
        "Binh_MFZ4_Bot.dwg": gen_binh_bot,
        "Binh_MT3_CO2.dwg": gen_binh_co2,
        "Van_Bao_Dong.dwg": gen_alarm_valve,
        "Van_Giam_Ap.dwg": gen_van_giam_ap,
        "Hong_Tiep_Nuoc_2Nga.dwg": gen_hong_tiep_nuoc,
        "Dau_Bao_Khoi.dwg": gen_dau_bao_khoi,
        "Dau_Bao_Nhiet.dwg": gen_dau_bao_nhiet,
        "Nut_Nhan_Khan.dwg": gen_nut_nhan_khan,
        "Chuong_Den_Bao_Chay.dwg": gen_chuong_den,
    }

    doc = acad.ActiveDocument
    for filename, fn in generators.items():
        file_path = os.path.join(target_dir, filename)
        clear_doc(doc)
        fn(doc)
        doc.SaveAs(file_path)
        print("Generated:", filename)

    clear_doc(doc)
    doc.SaveAs(os.path.join(target_dir, "temp_clean.dwg"))
    print("ALL 13 PCCC BLOCKS GENERATED SUCCESSFULLY!")

if __name__ == "__main__":
    create_blocks()
