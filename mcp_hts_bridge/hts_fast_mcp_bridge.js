#!/usr/bin/env node
/**
 * HTS-MCP-AutoCAD Fast Bridge (Port 5088)
 * ──────────────────────────────────────────────────────────────────────────────
 * Cầu nối siêu tốc: AI Client (Antigravity / Claude Desktop / Cursor) 
 *                ↔ HTS-MCP-AutoCAD Native Server (http://localhost:5088/mcp/)
 * 
 * - In-process HTTP Server trong AutoCAD (acad.exe)
 * - Tự động nạp toàn bộ 187 tools chuyên sâu (MEP, Drawing, Dynamic Block, Dim, Text)
 * - Phản hồi siêu tốc qua JSON-RPC stdio
 */
"use strict";

const readline = require("readline");

const HTS_BASE = process.env.HTS_MCP_URL || "http://localhost:5088/mcp";
const PROTOCOL_VERSION = "2024-11-05";
const SERVER_INFO = { name: "hts-autocad-fast-mcp", version: "2.0.0" };

function logErr(msg) {
    process.stderr.write(`[HTS-Fast-MCP] ${msg}\n`);
}

async function htsRequest(path, options = {}) {
    const url = `${HTS_BASE}${path}`;
    const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
    let resp;
    try {
        resp = await fetch(url, { ...options, headers });
    } catch (e) {
        throw new Error(`Không thể kết nối HTS-MCP tại ${url}. Đảm bảo AutoCAD đang mở.`);
    }

    const text = await resp.text();
    if (!resp.ok) {
        throw new Error(`HTTP ${resp.status} ${resp.statusText}: ${text.slice(0, 300)}`);
    }

    try {
        return JSON.parse(text);
    } catch (e) {
        return { success: true, text: text };
    }
}

const handlers = {
    "initialize": async () => ({
        protocolVersion: PROTOCOL_VERSION,
        capabilities: { tools: { listChanged: true } },
        serverInfo: SERVER_INFO,
    }),

    "notifications/initialized": async () => null,

    "tools/list": async () => {
        try {
            const data = await htsRequest("/tools/list");
            const rawTools = Array.isArray(data) ? data : (data.tools || []);
            const tools = rawTools.map((t) => ({
                name: t.name,
                description: t.description || "",
                inputSchema: t.parameters || t.inputSchema || { type: "object", properties: {} },
            }));
            return { tools };
        } catch (e) {
            logErr(`Lỗi lấy danh sách tools: ${e.message}`);
            return { tools: [] };
        }
    },

    "tools/call": async (params) => {
        const name = params?.name;
        const args = params?.arguments || {};
        if (!name) throw new Error("Yêu cầu tham số 'name' của tool");

        try {
            const res = await htsRequest("/tools/call", {
                method: "POST",
                body: JSON.stringify({ name, arguments: args }),
            });

            const contentText = typeof res === "string" 
                ? res 
                : JSON.stringify(res, null, 2);

            return {
                content: [{ type: "text", text: contentText }],
                isError: res?.success === false,
            };
        } catch (e) {
            return {
                content: [{ type: "text", text: `Lỗi gọi tool '${name}': ${e.message}` }],
                isError: true,
            };
        }
    },

    "ping": async () => ({}),
};

function reply(obj) {
    process.stdout.write(JSON.stringify(obj) + "\n");
}

const rl = readline.createInterface({ input: process.stdin, terminal: false });

let pendingRequests = 0;
let isClosed = false;

function checkExit() {
    if (isClosed && pendingRequests === 0) {
        process.exit(0);
    }
}

rl.on("line", async (line) => {
    line = line.trim();
    if (!line) return;

    let msg;
    try {
        msg = JSON.parse(line);
    } catch (e) {
        return;
    }

    pendingRequests++;
    const isNotification = msg.id === undefined || msg.id === null;
    const response = { jsonrpc: "2.0", id: msg.id };

    try {
        const handler = handlers[msg.method];
        if (!handler) {
            response.error = { code: -32601, message: `Method not found: ${msg.method}` };
        } else {
            const result = await handler(msg.params || {});
            if (result === null && isNotification) return;
            response.result = result === null ? {} : result;
        }
    } catch (e) {
        response.error = { code: -32603, message: e.message };
    } finally {
        pendingRequests--;
    }

    if (!isNotification) {
        reply(response);
    }
    checkExit();
});

rl.on("close", () => {
    isClosed = true;
    checkExit();
});

// Kiểm tra kết nối ban đầu ra stderr
(async () => {
    try {
        const health = await htsRequest("/health");
        logErr(`✅ Đã kết nối HTS-MCP-AutoCAD: v${health.version}, ${health.toolCount} tools khả dụng.`);
    } catch (e) {
        logErr(`⚠️ Chưa kết nối được HTS-MCP (Port 5088): ${e.message}`);
    }
})();
