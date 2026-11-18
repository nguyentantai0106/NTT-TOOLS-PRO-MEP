(() => {
  const STORAGE_KEY = "ntt_sld_studio_project_v1";

  const defaultPanels = () => ([
    {
      PanelName: "DB-XLDC",
      ParentPanelName: "MSB",
      VoltageV: 380,
      PowerFactor: 0.85,
      SimultaneousFactor: 0.85,
      UsageFactor: 1.0,
      DefaultCableType: "Cu/XLPE/PVC",
      Incomer: "SingleNormal",
      HasCapacitorBank: false,
      TargetCosPhi: 0.95,
      Loads: [
        { Stt: 1, CircuitNumber: "C1", LoadName: "Chiếu sáng tầng 1", PowerKW: 2.2, Phase: "SinglePhase", AssignedPhase: "R", BreakerType: "MCB", CableLength: 25, Starter: "Direct", Control: "Timer", LoadType: "Chiếu sáng" },
        { Stt: 2, CircuitNumber: "C2", LoadName: "Ổ cắm văn phòng", PowerKW: 3.5, Phase: "SinglePhase", AssignedPhase: "Y", BreakerType: "RCBO", CableLength: 28, Starter: "Direct", Control: "None", LoadType: "Ổ cắm" },
        { Stt: 3, CircuitNumber: "C3", LoadName: "Ổ cắm khu kỹ thuật", PowerKW: 3.0, Phase: "SinglePhase", AssignedPhase: "B", BreakerType: "RCBO", CableLength: 22, Starter: "Direct", Control: "None", LoadType: "Ổ cắm" },
        { Stt: 4, CircuitNumber: "C4", LoadName: "Quạt hút khói hành lang", PowerKW: 7.5, Phase: "ThreePhase", AssignedPhase: "R-Y-B", BreakerType: "MCB", CableLength: 45, Starter: "DOL", Control: "BMS", LoadType: "Động cơ" },
        { Stt: 5, CircuitNumber: "C5", LoadName: "Bơm cấp nước sinh hoạt 1", PowerKW: 11.0, Phase: "ThreePhase", AssignedPhase: "R-Y-B", BreakerType: "MCCB", CableLength: 40, Starter: "VFD", Control: "FloatSwitch", LoadType: "Động cơ" }
      ]
    },
    {
      PanelName: "MSB",
      ParentPanelName: "MBA-630kVA",
      VoltageV: 380,
      PowerFactor: 0.88,
      SimultaneousFactor: 0.8,
      UsageFactor: 1.0,
      DefaultCableType: "Cu/XLPE/PVC",
      Incomer: "DualATS",
      HasCapacitorBank: true,
      TargetCosPhi: 0.95,
      Loads: [
        { Stt: 1, CircuitNumber: "F1", LoadName: "Cấp nguồn tủ DB-XLDC", PowerKW: 27.2, Phase: "ThreePhase", AssignedPhase: "R-Y-B", BreakerType: "MCCB", CableLength: 35, Starter: "Direct", Control: "None", LoadType: "Tủ nhánh" },
        { Stt: 2, CircuitNumber: "F2", LoadName: "Cấp nguồn tủ Chiller", PowerKW: 45.0, Phase: "ThreePhase", AssignedPhase: "R-Y-B", BreakerType: "MCCB", CableLength: 55, Starter: "VFD", Control: "BMS", LoadType: "Động cơ" }
      ]
    }
  ]);

  let state = {
    panels: defaultPanels(),
    activeIndex: 0,
    zoom: 1.0,
    params: {
      LayoutScheme: "SouthRegion",
      TreeOrientation: "HorizontalLeftRight",
      SymbolRegion: "South_Iec",
      IncludeFuse: true,
      IncludeLamps: true,
      IncludeRelay: true,
      IncludeMct: true,
      IncludeSpd: true,
      IncludeSpare: false,
      SpareCount3P: 1,
      SpareCount1P: 0,
      IncludeTitleBlock: false,
      IncludeLegend: false,
      AutoFitSpacing: true
    }
  };

  function loadSavedState() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed && Array.isArray(parsed.panels) && parsed.panels.length > 0) {
          state.panels = parsed.panels;
          state.activeIndex = Math.min(parsed.activeIndex || 0, state.panels.length - 1);
          if (parsed.params) state.params = { ...state.params, ...parsed.params };
        }
      }
    } catch (_) {}
  }

  function saveState() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({
        panels: state.panels,
        activeIndex: state.activeIndex,
        params: state.params
      }));
    } catch (_) {}
  }

  const getActivePanel = () => state.panels[state.activeIndex];
  const setStatus = (msg) => {
    document.getElementById("footerStatus").textContent = msg;
  };

  async function postJson(url, data) {
    const resp = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    });
    const json = await resp.json();
    if (!resp.ok) throw new Error(json.message || "Lỗi máy chủ");
    return json;
  }

  async function recalculateActive() {
    const p = getActivePanel();
    try {
      const updated = await postJson("/api/calculate", p);
      state.panels[state.activeIndex] = updated;
      saveState();
      renderAll();
    } catch (e) {
      setStatus("Lỗi tính toán: " + e.message);
    }
  }

  function renderPanelSelector() {
    const sel = document.getElementById("selPanel");
    sel.innerHTML = "";
    state.panels.forEach((p, idx) => {
      const opt = document.createElement("option");
      opt.value = idx;
      opt.textContent = p.PanelName || `DB-${idx + 1}`;
      if (idx === state.activeIndex) opt.selected = true;
      sel.appendChild(opt);
    });

    const p = getActivePanel();
    document.getElementById("inpPanelName").value = p.PanelName || "";
    document.getElementById("inpParentPanel").value = p.ParentPanelName || "MSB";
    document.getElementById("inpKdt").value = p.SimultaneousFactor ?? 0.85;
    document.getElementById("inpCosPhi").value = p.PowerFactor ?? 0.85;
    document.getElementById("selIncomer").value = p.Incomer || "SingleNormal";
    document.getElementById("chkCapBank").checked = !!p.HasCapacitorBank;
    document.getElementById("chkLegend").checked = !!state.params.IncludeLegend;
    document.getElementById("chkTitleBlock").checked = !!state.params.IncludeTitleBlock;
  }

  function renderKpis() {
    const p = getActivePanel();
    document.getElementById("kpiPdat").textContent = `${p.TotalInstalledPowerKW || 0} kW`;
    document.getElementById("kpiLoadCount").textContent = `${(p.Loads || []).length} lộ phụ tải`;
    document.getElementById("kpiPtt").textContent = `${p.TotalCalculatedPowerKW || 0} kW`;
    document.getElementById("kpiCapBank").textContent = p.HasCapacitorBank ? `Tụ bù: ${p.CapacitorBankKvar || 0} kVAr` : "Không có tụ bù";
    document.getElementById("kpiItt").textContent = `${p.TotalCurrentA || 0} A`;
    document.getElementById("kpiMainCb").textContent = p.MainBreakerSpec || "MCCB 3P-32A";
    const unb = p.UnbalancePercent ?? 0;
    const unbEl = document.getElementById("kpiUnbalance");
    unbEl.textContent = `${unb}%`;
    unbEl.style.color = unb > 15 ? "var(--danger)" : "var(--success)";
    document.getElementById("kpiPhasePowers").textContent = `${p.PhasePowerR || 0} / ${p.PhasePowerY || 0} / ${p.PhasePowerB || 0} kW`;
    document.getElementById("kpiMainCable").textContent = p.MainCableSpec || "-";
  }

  function renderTable() {
    const p = getActivePanel();
    const tbody = document.getElementById("loadsTableBody");
    tbody.innerHTML = "";

    (p.Loads || []).forEach((ld, idx) => {
      const tr = document.createElement("tr");
      const duClass = ld.IsVoltageDropExceeded ? "badge-warn" : "badge-ok";
      tr.innerHTML = `
        <td class="mono">${idx + 1}</td>
        <td><input type="text" data-field="CircuitNumber" value="${esc(ld.CircuitNumber || `C${idx + 1}`)}" /></td>
        <td><input type="text" data-field="LoadName" value="${esc(ld.LoadName || "")}" /></td>
        <td><input type="number" step="0.1" min="0" class="mono" data-field="PowerKW" value="${ld.PowerKW ?? 1}" /></td>
        <td>
          <select data-field="Phase">
            <option value="ThreePhase" ${ld.Phase === "ThreePhase" ? "selected" : ""}>3 Pha</option>
            <option value="SinglePhase" ${ld.Phase === "SinglePhase" ? "selected" : ""}>1 Pha</option>
          </select>
        </td>
        <td>
          <select data-field="AssignedPhase" ${ld.Phase === "ThreePhase" ? "disabled" : ""}>
            <option value="R-Y-B" ${ld.AssignedPhase === "R-Y-B" ? "selected" : ""}>R-Y-B</option>
            <option value="R" ${ld.AssignedPhase === "R" ? "selected" : ""}>Pha R</option>
            <option value="Y" ${ld.AssignedPhase === "Y" ? "selected" : ""}>Pha Y</option>
            <option value="B" ${ld.AssignedPhase === "B" ? "selected" : ""}>Pha B</option>
          </select>
        </td>
        <td class="mono">${ld.CurrentA ?? 0}</td>
        <td>
          <select data-field="BreakerType">
            <option value="MCB" ${ld.BreakerType === "MCB" ? "selected" : ""}>MCB</option>
            <option value="MCCB" ${ld.BreakerType === "MCCB" ? "selected" : ""}>MCCB</option>
            <option value="RCBO" ${ld.BreakerType === "RCBO" ? "selected" : ""}>RCBO</option>
          </select>
        </td>
        <td class="mono" style="color:var(--cyan)">${esc(ld.BreakerSpec || "")}</td>
        <td class="mono">${esc(ld.CableSpec || "")}</td>
        <td><input type="number" step="1" min="1" class="mono" data-field="CableLength" value="${ld.CableLength ?? 25}" /></td>
        <td class="mono ${duClass}">${ld.VoltageDropPercent ?? 0}%</td>
        <td>
          <select data-field="Starter">
            <option value="Direct" ${ld.Starter === "Direct" ? "selected" : ""}>Trực tiếp</option>
            <option value="DOL" ${ld.Starter === "DOL" ? "selected" : ""}>DOL</option>
            <option value="StarDelta" ${ld.Starter === "StarDelta" ? "selected" : ""}>Sao-Tam giác (Y-Δ)</option>
            <option value="VFD" ${ld.Starter === "VFD" ? "selected" : ""}>Biến tần (VFD)</option>
            <option value="SoftStarter" ${ld.Starter === "SoftStarter" ? "selected" : ""}>Khởi động mềm</option>
          </select>
        </td>
        <td>
          <select data-field="Control">
            <option value="None" ${ld.Control === "None" ? "selected" : ""}>Không</option>
            <option value="BMS" ${ld.Control === "BMS" ? "selected" : ""}>BMS</option>
            <option value="PLC" ${ld.Control === "PLC" ? "selected" : ""}>PLC</option>
            <option value="Timer" ${ld.Control === "Timer" ? "selected" : ""}>Timer</option>
            <option value="FloatSwitch" ${ld.Control === "FloatSwitch" ? "selected" : ""}>Phao</option>
            <option value="AutoManual" ${ld.Control === "AutoManual" ? "selected" : ""}>H-O-A</option>
          </select>
        </td>
        <td><button class="btn btn-sm btn-danger btn-del-row">✕</button></td>
      `;

      tr.querySelectorAll("input, select").forEach((el) => {
        el.addEventListener("change", () => {
          const field = el.dataset.field;
          let val = el.value;
          if (field === "PowerKW" || field === "CableLength") val = parseFloat(val) || 0;
          ld[field] = val;
          if (field === "Phase" && val === "SinglePhase" && ld.AssignedPhase === "R-Y-B") {
            ld.AssignedPhase = "R";
          }
          recalculateActive();
        });
      });

      tr.querySelector(".btn-del-row").addEventListener("click", () => {
        p.Loads.splice(idx, 1);
        recalculateActive();
      });

      tbody.appendChild(tr);
    });
  }

  function renderPreviewControls() {
    document.getElementById("selLayoutScheme").value = state.params.LayoutScheme;
    document.getElementById("selOrientation").value = state.params.TreeOrientation;
    document.getElementById("wrapOrientation").style.display =
      (state.params.LayoutScheme === "SouthRegion" || state.params.LayoutScheme === "NorthRegion") ? "inline-flex" : "none";
    document.getElementById("chkFuse").checked = state.params.IncludeFuse;
    document.getElementById("chkLamps").checked = state.params.IncludeLamps;
    document.getElementById("chkRelay").checked = state.params.IncludeRelay;
    document.getElementById("chkMct").checked = state.params.IncludeMct;
    document.getElementById("chkSpd").checked = state.params.IncludeSpd;
    document.getElementById("chkSpare").checked = state.params.IncludeSpare;
    document.getElementById("zoomPctText").textContent = `${Math.round(state.zoom * 100)}%`;
  }

  function drawSldPreview() {
    const canvas = document.getElementById("sldCanvas");
    const ctx = canvas.getContext("2d");
    const p = getActivePanel();
    const loads = p.Loads || [];
    const pr = state.params;

    const isHorizontal = (pr.LayoutScheme === "SouthRegion" || pr.LayoutScheme === "NorthRegion") && pr.TreeOrientation === "HorizontalLeftRight";
    const baseW = isHorizontal ? 1240 : Math.max(1060, 260 + loads.length * 115);
    const baseH = isHorizontal ? Math.max(660, 250 + loads.length * 46) : 740;

    canvas.width = Math.round(baseW * state.zoom);
    canvas.height = Math.round(baseH * state.zoom);

    ctx.save();
    ctx.scale(state.zoom, state.zoom);
    ctx.fillStyle = "#0D1119";
    ctx.fillRect(0, 0, baseW, baseH);

    // Khung tên nếu bật
    if (pr.IncludeTitleBlock) {
      ctx.strokeStyle = "#FFD600";
      ctx.lineWidth = 2;
      ctx.strokeRect(12, 12, baseW - 24, baseH - 24);
      ctx.fillStyle = "#FFD600";
      ctx.font = "bold 12px Segoe UI";
      ctx.fillText(`KHUNG TÊN A3 - SƠ ĐỒ NGUYÊN LÝ TỦ ${p.PanelName}`, 24, 30);
    }

    if (isHorizontal) {
      drawHorizontalSouthNorth(ctx, p, loads, pr, baseW, baseH);
    } else {
      drawVerticalDiagram(ctx, p, loads, pr, baseW, baseH);
    }
    ctx.restore();
  }

  function drawHorizontalSouthNorth(ctx, p, loads, pr, w, h) {
    const busX = 130;
    const tableX = 290;
    const tableW = w - tableX - 40;
    const startY = 80;
    const rowH = 42;

    // Header bảng
    ctx.strokeStyle = "#FFD600";
    ctx.lineWidth = 1.5;
    ctx.strokeRect(tableX, startY - 52, tableW, 52 + Math.max(1, loads.length) * rowH + 96);

    ctx.fillStyle = "#00E5FF";
    ctx.font = "bold 14px Segoe UI";
    ctx.fillText(`SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN: ${p.PanelName} (${pr.LayoutScheme === "SouthRegion" ? "CHUẨN MIỀN NAM IEC" : "CHUẨN MIỀN BẮC TCVN"})`, tableX + 12, startY - 30);
    ctx.fillStyle = "#94A3B8";
    ctx.font = "11px Segoe UI";
    ctx.fillText(`TỪ TỦ: ${p.ParentPanelName || "MSB"} | Kđt: ${p.SimultaneousFactor} | Cosφ: ${p.PowerFactor}`, tableX + 12, startY - 12);

    // Busbar trục đứng
    const busEndY = startY + Math.max(1, loads.length) * rowH + 65;
    ctx.strokeStyle = "#FF5252";
    ctx.lineWidth = 4;
    ctx.beginPath();
    ctx.moveTo(busX, startY - 10);
    ctx.lineTo(busX, busEndY);
    ctx.stroke();
    ctx.fillStyle = "#FF5252";
    ctx.font = "bold 11px JetBrains Mono";
    ctx.fillText("BUSBAR", busX - 24, startY - 18);

    // Các nhánh tải
    loads.forEach((ld, i) => {
      const y = startY + i * rowH + rowH / 2;
      // Đường phân hàng
      ctx.strokeStyle = "#263147";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(tableX, startY + (i + 1) * rowH);
      ctx.lineTo(tableX + tableW, startY + (i + 1) * rowH);
      ctx.stroke();

      // Dây từ busbar -> CB -> Bảng
      ctx.strokeStyle = "#00E5FF";
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(busX, y);
      ctx.lineTo(busX + 55, y);
      ctx.moveTo(busX + 95, y);
      ctx.lineTo(tableX, y);
      ctx.stroke();

      // Ký hiệu CB
      const isRcbo = (ld.BreakerType || "").toUpperCase().includes("RCBO");
      ctx.strokeStyle = isRcbo ? "#FF9100" : "#00E676";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(busX + 55, y);
      ctx.lineTo(busX + 88, y - 10);
      ctx.stroke();
      ctx.strokeRect(busX + 68, y - 4, 8, 8);
      if (isRcbo) {
        ctx.fillStyle = "#FF9100";
        ctx.font = "9px JetBrains Mono";
        ctx.fillText("RCBO", busX + 60, y + 14);
      }

      // Chữ trong bảng
      ctx.fillStyle = "#F8FAFC";
      ctx.font = "11.5px JetBrains Mono";
      ctx.fillText(ld.BreakerSpec || "", tableX + 10, y + 4);
      ctx.fillStyle = "#FF9100";
      ctx.fillText(ld.CircuitNumber || `C${i + 1}`, tableX + 205, y + 4);
      ctx.fillStyle = "#94A3B8";
      ctx.fillText(ld.CableSpec || "", tableX + 265, y + 4);
      ctx.fillStyle = "#00E5FF";
      ctx.fillText(`${ld.PowerKW} kW (${ld.AssignedPhase})`, tableX + 530, y + 4);
      ctx.fillStyle = "#F8FAFC";
      ctx.font = "12px Segoe UI";
      ctx.fillText(ld.LoadName || "", tableX + 670, y + 4);
    });

    // Hàng nguồn tổng đáy bảng
    const sumY = startY + Math.max(1, loads.length) * rowH + 45;
    ctx.strokeStyle = "#00E676";
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(busX, sumY);
    ctx.lineTo(tableX, sumY);
    ctx.stroke();

    ctx.fillStyle = "#00E676";
    ctx.font = "bold 12px JetBrains Mono";
    ctx.fillText(`ĐẦU VÀO: ${p.MainBreakerSpec} | Ptt=${p.TotalCalculatedPowerKW}kW | Itt=${p.TotalCurrentA}A`, tableX + 10, sumY + 4);

    if (pr.IncludeFuse || pr.IncludeLamps || pr.IncludeMct) {
      ctx.fillStyle = "#FFD600";
      ctx.font = "10.5px Segoe UI";
      ctx.fillText("Cụm Đo Lường & Đèn Báo Pha + 3xMCT", busX - 95, sumY - 24);
    }
  }

  function drawVerticalDiagram(ctx, p, loads, pr, w, h) {
    const busY = 230;
    const startX = 110;
    const colW = 110;
    const busEndX = Math.max(startX + 240, startX + loads.length * colW);
    const midX = (startX + busEndX) / 2;

    // Tiêu đề
    ctx.fillStyle = "#00E5FF";
    ctx.font = "bold 15px Segoe UI";
    ctx.fillText(`TỦ ĐIỆN: ${p.PanelName} [${pr.LayoutScheme}]`, 40, 46);
    ctx.fillStyle = "#94A3B8";
    ctx.font = "12px JetBrains Mono";
    ctx.fillText(`Pđặt: ${p.TotalInstalledPowerKW}kW | Ptt: ${p.TotalCalculatedPowerKW}kW | Itt: ${p.TotalCurrentA}A | ${p.MainBreakerSpec}`, 40, 68);

    // Nguồn vào từ trên xuống Busbar
    ctx.strokeStyle = "#00E5FF";
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(midX, 85);
    ctx.lineTo(midX, 135);
    ctx.moveTo(midX, 175);
    ctx.lineTo(midX, busY);
    ctx.stroke();

    // Main Breaker
    ctx.strokeStyle = "#00E676";
    ctx.strokeRect(midX - 14, 135, 28, 40);
    ctx.fillStyle = "#00E676";
    ctx.font = "bold 11px JetBrains Mono";
    ctx.fillText(p.MainBreakerSpec || "MCCB", midX + 22, 160);

    // Thanh cái ngang
    ctx.strokeStyle = "#FF5252";
    ctx.lineWidth = 5;
    ctx.beginPath();
    ctx.moveTo(startX - 40, busY);
    ctx.lineTo(busEndX, busY);
    ctx.stroke();

    // Nhánh phụ tải xuống dưới
    loads.forEach((ld, i) => {
      const x = startX + i * colW + 40;
      ctx.strokeStyle = ld.Phase === "ThreePhase" ? "#FF5252" : "#00E676";
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(x, busY);
      ctx.lineTo(x, busY + 55);
      ctx.moveTo(x, busY + 95);
      ctx.lineTo(x, busY + 180);
      ctx.stroke();

      // CB nhánh
      ctx.strokeStyle = "#00E5FF";
      ctx.strokeRect(x - 10, busY + 55, 20, 40);

      // Thông tin lộ
      ctx.save();
      ctx.translate(x, busY + 195);
      ctx.rotate(-Math.PI / 2);
      ctx.fillStyle = "#F8FAFC";
      ctx.font = "11px Segoe UI";
      ctx.fillText(`${ld.CircuitNumber}: ${ld.LoadName} (${ld.PowerKW}kW)`, -180, 4);
      ctx.restore();
    });
  }

  function drawRiserPreview() {
    const canvas = document.getElementById("riserCanvas");
    const ctx = canvas.getContext("2d");
    const w = canvas.width;
    const h = canvas.height;

    ctx.fillStyle = "#0D1119";
    ctx.fillRect(0, 0, w, h);

    const panels = state.panels;
    const msb = panels.find((x) => (x.PanelName || "").toUpperCase().includes("MSB")) || panels[0];
    const children = panels.filter((x) => x !== msb);

    // Draw MSB at bottom center
    const rootX = w / 2 - 120;
    const rootY = h - 110;
    ctx.fillStyle = "#1E2433";
    ctx.strokeStyle = "#00E5FF";
    ctx.lineWidth = 2;
    ctx.fillRect(rootX, rootY, 240, 72);
    ctx.strokeRect(rootX, rootY, 240, 72);
    ctx.fillStyle = "#00E5FF";
    ctx.font = "bold 14px Segoe UI";
    ctx.fillText(msb.PanelName || "MSB", rootX + 14, rootY + 24);
    ctx.fillStyle = "#F8FAFC";
    ctx.font = "11.5px JetBrains Mono";
    ctx.fillText(`Ptt: ${msb.TotalCalculatedPowerKW || 0}kW | ${msb.MainBreakerSpec || ""}`, rootX + 14, rootY + 46);

    const count = Math.max(1, children.length);
    const spacing = Math.min(260, (w - 80) / count);
    const startX = (w - (count - 1) * spacing) / 2 - 105;

    children.forEach((ch, idx) => {
      const cx = startX + idx * spacing;
      const cy = 90;

      ctx.strokeStyle = "#FF9100";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(w / 2, rootY);
      ctx.lineTo(w / 2, rootY - 60);
      ctx.lineTo(cx + 105, rootY - 60);
      ctx.lineTo(cx + 105, cy + 72);
      ctx.stroke();

      ctx.fillStyle = "#1E2433";
      ctx.strokeStyle = "#00E676";
      ctx.fillRect(cx, cy, 210, 72);
      ctx.strokeRect(cx, cy, 210, 72);
      ctx.fillStyle = "#00E676";
      ctx.font = "bold 13px Segoe UI";
      ctx.fillText(ch.PanelName, cx + 12, cy + 24);
      ctx.fillStyle = "#F8FAFC";
      ctx.font = "11px JetBrains Mono";
      ctx.fillText(`Ptt: ${ch.TotalCalculatedPowerKW || 0}kW (${ch.TotalCurrentA || 0}A)`, cx + 12, cy + 44);
      ctx.fillStyle = "#94A3B8";
      ctx.fillText(ch.MainBreakerSpec || "", cx + 12, cy + 62);
    });
  }

  function renderAll() {
    renderPanelSelector();
    renderKpis();
    renderTable();
    renderPreviewControls();
    drawSldPreview();
    drawRiserPreview();
  }

  async function refreshStatus() {
    try {
      const resp = await fetch("/api/info");
      const info = await resp.json();
      document.getElementById("lanUrlText").textContent = `http://${info.lanIp}:${info.port}`;
      const dot = document.getElementById("cadDot");
      const txt = document.getElementById("cadStatusText");
      if (info.cad && info.cad.connected) {
        dot.classList.add("online");
        txt.textContent = `AutoCAD Đã Kết Nối (${info.cad.docName || "DWG"})`;
      } else {
        dot.classList.remove("online");
        txt.textContent = "Chưa Kết Nối AutoCAD (Mở CAD & chạy NTTRELOAD)";
      }
    } catch (_) {}
  }

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  function bindEvents() {
    // Tabs
    document.querySelectorAll(".tab-btn").forEach((btn) => {
      btn.addEventListener("click", () => {
        document.querySelectorAll(".tab-btn").forEach((b) => b.classList.remove("active"));
        document.querySelectorAll(".tab-pane").forEach((p) => p.classList.remove("active"));
        btn.classList.add("active");
        document.getElementById(btn.dataset.tab).classList.add("active");
        drawSldPreview();
        drawRiserPreview();
      });
    });

    // Panel bar
    document.getElementById("selPanel").addEventListener("change", (e) => {
      state.activeIndex = parseInt(e.target.value, 10) || 0;
      recalculateActive();
    });

    document.getElementById("btnAddPanel").addEventListener("click", () => {
      const n = state.panels.length + 1;
      state.panels.push({
        PanelName: `DB-0${n}`,
        ParentPanelName: "MSB",
        VoltageV: 380,
        PowerFactor: 0.85,
        SimultaneousFactor: 0.85,
        UsageFactor: 1.0,
        DefaultCableType: "Cu/XLPE/PVC",
        Incomer: "SingleNormal",
        Loads: [
          { Stt: 1, CircuitNumber: "C1", LoadName: "Chiếu sáng", PowerKW: 2.0, Phase: "SinglePhase", AssignedPhase: "R", BreakerType: "MCB", CableLength: 25, Starter: "Direct", Control: "None" }
        ]
      });
      state.activeIndex = state.panels.length - 1;
      recalculateActive();
    });

    document.getElementById("btnClonePanel").addEventListener("click", () => {
      const clone = JSON.parse(JSON.stringify(getActivePanel()));
      clone.PanelName = `${clone.PanelName}_COPY`;
      state.panels.push(clone);
      state.activeIndex = state.panels.length - 1;
      recalculateActive();
    });

    document.getElementById("btnDeletePanel").addEventListener("click", () => {
      if (state.panels.length <= 1) return alert("Phải giữ ít nhất 1 tủ điện!");
      state.panels.splice(state.activeIndex, 1);
      state.activeIndex = Math.max(0, state.activeIndex - 1);
      recalculateActive();
    });

    ["inpPanelName", "inpParentPanel", "inpKdt", "inpCosPhi", "selIncomer", "chkCapBank"].forEach((id) => {
      document.getElementById(id).addEventListener("change", () => {
        const p = getActivePanel();
        p.PanelName = document.getElementById("inpPanelName").value.trim() || "DB-01";
        p.ParentPanelName = document.getElementById("inpParentPanel").value.trim() || "MSB";
        p.SimultaneousFactor = parseFloat(document.getElementById("inpKdt").value) || 0.85;
        p.PowerFactor = parseFloat(document.getElementById("inpCosPhi").value) || 0.85;
        p.Incomer = document.getElementById("selIncomer").value;
        p.HasCapacitorBank = document.getElementById("chkCapBank").checked;
        recalculateActive();
      });
    });

    document.getElementById("btnAddLoad").addEventListener("click", () => {
      const p = getActivePanel();
      const n = (p.Loads || []).length + 1;
      p.Loads.push({
        Stt: n, CircuitNumber: `C${n}`, LoadName: `Phụ tải mới ${n}`, PowerKW: 3.0,
        Phase: "ThreePhase", AssignedPhase: "R-Y-B", BreakerType: "MCB",
        CableLength: 25, Starter: "Direct", Control: "None", LoadType: "Động cơ"
      });
      recalculateActive();
    });

    document.getElementById("btnBalancePhases").addEventListener("click", async () => {
      try {
        const updated = await postJson("/api/balance-phases", getActivePanel());
        state.panels[state.activeIndex] = updated;
        saveState();
        renderAll();
        setStatus(`Đã cân pha tự động! Độ lệch pha hiện tại: ${updated.UnbalancePercent}%`);
      } catch (e) { setStatus("Lỗi cân pha: " + e.message); }
    });

    document.getElementById("btnOptimizeCables").addEventListener("click", async () => {
      try {
        const updated = await postJson("/api/optimize-cables", getActivePanel());
        state.panels[state.activeIndex] = updated;
        saveState();
        renderAll();
        setStatus("Đã tối ưu tiết diện cáp theo giới hạn sụt áp TCVN!");
      } catch (e) { setStatus("Lỗi tối ưu cáp: " + e.message); }
    });

    // Preview controls
    document.getElementById("selLayoutScheme").addEventListener("change", (e) => {
      state.params.LayoutScheme = e.target.value;
      state.params.SymbolRegion = e.target.value === "NorthRegion" ? "North_Tcvn" : "South_Iec";
      saveState();
      renderPreviewControls();
      drawSldPreview();
    });
    document.getElementById("selOrientation").addEventListener("change", (e) => {
      state.params.TreeOrientation = e.target.value;
      saveState();
      drawSldPreview();
    });
    [
      ["chkFuse", "IncludeFuse"], ["chkLamps", "IncludeLamps"], ["chkRelay", "IncludeRelay"],
      ["chkMct", "IncludeMct"], ["chkSpd", "IncludeSpd"], ["chkSpare", "IncludeSpare"],
      ["chkLegend", "IncludeLegend"], ["chkTitleBlock", "IncludeTitleBlock"]
    ].forEach(([id, key]) => {
      document.getElementById(id).addEventListener("change", (e) => {
        state.params[key] = e.target.checked;
        saveState();
        drawSldPreview();
      });
    });

    document.getElementById("btnZoomIn").addEventListener("click", () => { state.zoom = Math.min(2.0, state.zoom + 0.15); renderPreviewControls(); drawSldPreview(); });
    document.getElementById("btnZoomOut").addEventListener("click", () => { state.zoom = Math.max(0.4, state.zoom - 0.15); renderPreviewControls(); drawSldPreview(); });
    document.getElementById("btnZoomReset").addEventListener("click", () => { state.zoom = 1.0; renderPreviewControls(); drawSldPreview(); });

    // Excel
    document.getElementById("btnTemplateExcel").addEventListener("click", () => { window.location.href = "/api/template-excel"; });
    document.getElementById("btnExportExcel").addEventListener("click", async () => {
      const resp = await fetch("/api/export-excel", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(getActivePanel())
      });
      const blob = await resp.blob();
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = `${getActivePanel().PanelName || "SLD"}.xlsx`;
      a.click();
    });

    document.getElementById("btnImportExcel").addEventListener("click", () => document.getElementById("excelFileInput").click());
    document.getElementById("excelFileInput").addEventListener("change", async (e) => {
      const file = e.target.files[0];
      if (!file) return;
      const fd = new FormData();
      fd.append("file", file);
      const resp = await fetch("/api/import-excel", { method: "POST", body: fd });
      const json = await resp.json();
      if (json.loads) {
        getActivePanel().Loads = json.loads;
        recalculateActive();
        setStatus(`Đã nhập ${json.loads.length} lộ phụ tải từ file Excel!`);
      }
      e.target.value = "";
    });

    // Save / Load JSON
    document.getElementById("btnSaveJson").addEventListener("click", () => {
      const blob = new Blob([JSON.stringify(state, null, 2)], { type: "application/json" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = "SLD_Project.json";
      a.click();
    });
    document.getElementById("btnLoadJson").addEventListener("click", () => document.getElementById("jsonFileInput").click());
    document.getElementById("jsonFileInput").addEventListener("change", async (e) => {
      const file = e.target.files[0];
      if (!file) return;
      const text = await file.text();
      const parsed = JSON.parse(text);
      if (parsed && Array.isArray(parsed.panels)) {
        state.panels = parsed.panels;
        state.activeIndex = 0;
        if (parsed.params) state.params = { ...state.params, ...parsed.params };
        recalculateActive();
        setStatus("Đã nạp dự án JSON thành công!");
      }
      e.target.value = "";
    });

    // Draw into AutoCAD
    document.getElementById("btnDrawCad").addEventListener("click", async () => {
      const pickPoint = document.getElementById("chkPickPoint").checked;
      setStatus(pickPoint ? "⏳ Đang chờ Kỹ sư click chọn điểm đặt trên màn hình AutoCAD..." : "⏳ Đang gửi lệnh vẽ sang AutoCAD...");
      try {
        const res = await postJson("/api/draw-cad", {
          panel: getActivePanel(),
          params: state.params,
          pickPoint,
          origin: { x: 0, y: 0 }
        });
        if (res.success) {
          setStatus("✅ " + res.message);
          alert(res.message);
        } else {
          setStatus("⚠️ " + (res.message || "Không thể vẽ vào AutoCAD."));
          alert(res.message || "Không thể vẽ vào AutoCAD.");
        }
      } catch (e) {
        setStatus("❌ " + e.message);
        alert(e.message);
      }
    });
  }

  loadSavedState();
  bindEvents();
  recalculateActive();
  refreshStatus();
  setInterval(refreshStatus, 5000);
})();
