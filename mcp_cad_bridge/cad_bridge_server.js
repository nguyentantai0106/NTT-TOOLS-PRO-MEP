#!/usr/bin/env node
/**
 * NTT TOOLS - STANDALONE AutoCAD MCP Bridge Server (Zero Add-in Dependency)
 * ──────────────────────────────────────────────────────────────────────────────
 * Cầu nối độc lập 100%: AI Client ↔ Direct AutoCAD COM Automation (Python)
 * - Hoàn toàn không phụ thuộc vào cổng 5005 hay bất kỳ tính năng nào của TaiAiAddin
 * - Tự động định tuyến sang standalone_cad_mcp.py
 * - Non-blocking async stdio JSON-RPC
 */
"use strict";

const readline = require("readline");
const { spawn } = require("child_process");
const path = require("path");

const PROTOCOL_VERSION = "2024-11-05";
const SERVER_INFO = { name: "standalone-cad-bridge", version: "3.0.0" };
const DEFAULT_TIMEOUT_MS = 35000;
const PYTHON_SCRIPT = path.join(__dirname, "standalone_cad_mcp.py");

function logErr(msg) {
    process.stderr.write(`[standalone-cad-bridge] ${msg}\n`);
}

let pyProc = null;
let reqId = 1000;
const pendingCalls = new Map();

function ensurePythonProcess() {
    if (pyProc && !pyProc.killed) return pyProc;
    
    pyProc = spawn("python", [PYTHON_SCRIPT], {
        stdio: ["pipe", "pipe", "inherit"],
        env: process.env
    });

    let buffer = "";
    pyProc.stdout.on("data", (chunk) => {
        buffer += chunk.toString("utf8");
        const lines = buffer.split("\n");
        buffer = lines.pop();
        for (const line of lines) {
            const trimmed = line.trim();
            if (!trimmed) continue;
            try {
                const msg = JSON.parse(trimmed);
                if (msg.id !== undefined && pendingCalls.has(msg.id)) {
                    const cb = pendingCalls.get(msg.id);
                    pendingCalls.delete(msg.id);
                    cb(msg);
                }
            } catch (e) {
                logErr(`Parse error on python line: ${trimmed}`);
            }
        }
    });

    pyProc.on("error", (err) => {
        logErr(`Python bridge error: ${err.message}`);
    });

    pyProc.on("exit", (code) => {
        logErr(`Python bridge exited with code ${code}`);
        pyProc = null;
    });

    return pyProc;
}

async function callPythonMcpTool(name, args) {
    const proc = ensurePythonProcess();
    const id = ++reqId;
    const req = {
        jsonrpc: "2.0",
        id: id,
        method: "tools/call",
        params: { name, arguments: args || {} }
    };

    return new Promise((resolve) => {
        const timeout = setTimeout(() => {
            if (pendingCalls.has(id)) {
                pendingCalls.delete(id);
                resolve({ success: false, error: "Timeout 35s khi chờ standalone Python CAD bridge phản hồi" });
            }
        }, DEFAULT_TIMEOUT_MS);

        pendingCalls.set(id, (msg) => {
            clearTimeout(timeout);
            try {
                if (msg.result && msg.result.content && msg.result.content[0]) {
                    const parsed = JSON.parse(msg.result.content[0].text);
                    resolve(parsed);
                } else if (msg.error) {
                    resolve({ success: false, error: msg.error.message });
                } else {
                    resolve({ success: true, result: msg.result });
                }
            } catch (e) {
                resolve({ success: false, error: e.message, raw: msg });
            }
        });

        proc.stdin.write(JSON.stringify(req) + "\n");
    });
}

const TOOLS_METADATA = [
    {
        name: "cad_activate_server",
        description: "Kích hoạt phiên làm việc của MCP Server (Direct COM, không cần Add-in).",
        inputSchema: {
            type: "object",
            properties: {
                confirmation: { type: "string", default: "khởi động MCP server" }
            }
        }
    },
    {
        name: "cad_deactivate_server",
        description: "Khóa quyền truy cập MCP Server.",
        inputSchema: { type: "object", properties: {} }
    },
    {
        name: "cad_get_status",
        description: "Kiểm tra trạng thái kết nối trực tiếp giữa MCP Server và AutoCAD qua COM Automation: Tên bản vẽ DWG, số lượng layer, trạng thái sẵn sàng (Hoàn toàn độc lập, không qua Addin).",
        inputSchema: { type: "object", properties: {} }
    },
    {
        name: "cad_get_drawing_info",
        description: "Lấy thông tin tổng quan về bản vẽ AutoCAD đang mở: Tên file DWG, số lượng Layer, số lượng Block và đối tượng ModelSpace.",
        inputSchema: { type: "object", properties: {} }
    },
    {
        name: "cad_run_cad_command",
        description: "Gửi lệnh CAD hoặc AutoLISP trực tiếp vào dòng lệnh của AutoCAD qua COM (SendCommand) hoàn toàn độc lập, không qua Addin.",
        inputSchema: {
            type: "object",
            properties: {
                command: { type: "string", description: "Chuỗi lệnh CAD hoặc biểu thức LISP (VD: '._REGEN', '(load \"...\")')." }
            },
            required: ["command"]
        }
    },
    {
        name: "cad_smart_boundary",
        description: "Tự động quét mặt bằng kiến trúc, nhận diện tường cột, bo kín đường bao phòng và tính diện tích thông thủy độc lập 100%.",
        inputSchema: {
            type: "object",
            properties: {
                detect_text: { type: "boolean", default: true, description: "Tự động tìm text tên phòng gần tâm." }
            }
        }
    },
    {
        name: "cad_analyze_dwg",
        description: "Phân tích cấu trúc sâu của bản vẽ DWG hiện hành: Thống kê đối tượng ModelSpace, danh sách layer, blocks trực tiếp qua COM.",
        inputSchema: { type: "object", properties: {} }
    },
    {
        name: "cad_draw_sld",
        description: "Vẽ Sơ đồ nguyên lý đơn tuyến (SLD) tủ điện mẫu trực tiếp lên bản vẽ CAD.",
        inputSchema: {
            type: "object",
            properties: {
                panel_name: { type: "string", default: "MSB-MCP-AI" },
                insert_x: { type: "number", default: 0.0 },
                insert_y: { type: "number", default: 0.0 }
            }
        }
    },
    {
        name: "cad_draw_cable_tray",
        description: "Vẽ tuyến thang máng cáp trực tiếp lên bản vẽ CAD.",
        inputSchema: {
            type: "object",
            properties: {
                width_mm: { type: "number", default: 300.0 },
                height_mm: { type: "number", default: 100.0 }
            }
        }
    }
];

async function handleToolExecution(name, args) {
    return await callPythonMcpTool(name, args);
}

const handlers = {
    "initialize": async () => ({
        protocolVersion: PROTOCOL_VERSION,
        capabilities: { tools: { listChanged: true } },
        serverInfo: SERVER_INFO,
    }),

    "notifications/initialized": async () => null,

    "tools/list": async () => ({ tools: TOOLS_METADATA }),

    "tools/call": async (params) => {
        const name = params?.name;
        const args = params?.arguments || {};
        if (!name) throw new Error("Yêu cầu tham số 'name' của tool");

        const result = await handleToolExecution(name, args);
        const textOutput = JSON.stringify(result, null, 2);

        return {
            content: [{ type: "text", text: textOutput }],
            isError: result?.success === false
        };
    },

    "ping": async () => ({})
};

function reply(obj) {
    process.stdout.write(JSON.stringify(obj) + "\n");
}

let pendingRequests = 0;
let isClosed = false;

function checkExit() {
    if (isClosed && pendingRequests === 0) {
        if (pyProc && !pyProc.killed) {
            pyProc.kill();
        }
        process.exit(0);
    }
}

const rl = readline.createInterface({ input: process.stdin, terminal: false });

rl.on("line", async (line) => {
    line = line.trim();
    if (!line) return;

    let msg;
    try {
        msg = JSON.parse(line);
    } catch {
        return;
    }

    pendingRequests++;
    const isNotification = msg.id === undefined || msg.id === null;
    const response = { jsonrpc: "2.0", id: msg.id };

    try {
        const handler = handlers[msg.method];
        if (handler) {
            const res = await handler(msg.params);
            if (!isNotification) {
                response.result = res;
                reply(response);
            }
        } else {
            if (!isNotification) {
                response.error = { code: -32601, message: `Method not found: ${msg.method}` };
                reply(response);
            }
        }
    } catch (err) {
        if (!isNotification) {
            response.error = { code: -32603, message: err.message };
            reply(response);
        }
    } finally {
        pendingRequests--;
        checkExit();
    }
});

rl.on("close", () => {
    isClosed = true;
    checkExit();
});

logErr("STANDALONE CAD MCP Bridge (Direct COM) ready.");
