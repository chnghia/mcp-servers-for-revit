#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { registerTools } from "./tools/register.js";
import http from "http";
import crypto from "crypto";

// 创建服务器实例
const server = new McpServer({
  name: "mcp-server-for-revit",
  version: "1.0.0",
});

// 解析命令行参数
const args = process.argv.slice(2);
let port: number | null = null;
let useSse = false;

// 检查环境变量 PORT
if (process.env.PORT) {
  const envPort = parseInt(process.env.PORT, 10);
  if (!isNaN(envPort)) {
    port = envPort;
    useSse = true;
  }
}

// 检查命令行参数 --port, -p, --sse
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--port" || args[i] === "-p") {
    const nextArg = args[i + 1];
    if (nextArg) {
      const parsedPort = parseInt(nextArg, 10);
      if (!isNaN(parsedPort)) {
        port = parsedPort;
        useSse = true;
      }
    }
  } else if (args[i] === "--sse") {
    useSse = true;
  }
}

// 启动服务器
async function main() {
  // 注册工具
  await registerTools(server);

  if (useSse) {
    const ssePort = port || 3000;
    const transports: Record<string, SSEServerTransport> = {};
    const streamableTransports: Record<string, StreamableHTTPServerTransport> = {};

    const httpServer = http.createServer(async (req, res) => {
      const url = new URL(req.url || "", `http://${req.headers.host || "localhost"}`);
      const pathname = url.pathname;
      const method = req.method;
      
      const rawMcpSessionId = req.headers["mcp-session-id"];
      const mcpSessionId = Array.isArray(rawMcpSessionId) ? rawMcpSessionId[0] : rawMcpSessionId;

      // Add CORS headers
      res.setHeader("Access-Control-Allow-Origin", "*");
      res.setHeader("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
      res.setHeader("Access-Control-Allow-Headers", req.headers["access-control-request-headers"] || "*");

      console.error(`[MCP Server Log] Request: ${method} ${req.url} (Pathname: ${pathname})`);
      console.error(`[MCP Server Log] Active session IDs (SSE): ${Object.keys(transports).join(", ")}`);
      console.error(`[MCP Server Log] Active session IDs (Streamable): ${Object.keys(streamableTransports).join(", ")}`);

      if (method === "OPTIONS") {
        console.error(`[MCP Server Log] Handled OPTIONS preflight`);
        res.writeHead(200);
        res.end();
        return;
      }

      // 1. GET requests on /sse or /
      if (method === "GET" && (pathname === "/sse" || pathname === "/")) {
        if (mcpSessionId) {
          // Streamable HTTP client notification stream establishment
          console.error(`[MCP Server Log] Streamable HTTP GET for session: ${mcpSessionId}`);
          const transport = streamableTransports[mcpSessionId];
          if (!transport) {
            console.error(`[MCP Server Log] Session not found for Streamable HTTP GET: ${mcpSessionId}`);
            res.writeHead(404, { "Content-Type": "text/plain" });
            res.end("Session not found");
            return;
          }
          try {
            await transport.handleRequest(req, res);
          } catch (error) {
            console.error("[MCP Server Log] Error handling Streamable HTTP GET:", error);
            if (!res.headersSent) {
              res.writeHead(500, { "Content-Type": "text/plain" });
              res.end("Internal Server Error");
            }
          }
        } else {
          // Legacy SSE client stream establishment
          console.error(`[MCP Server Log] Legacy SSE GET request`);
          try {
            const transport = new SSEServerTransport("/messages", res);
            const sessionId = transport.sessionId;
            transports[sessionId] = transport;

            transport.onclose = () => {
              console.error(`SSE transport closed for session ${sessionId}`);
              delete transports[sessionId];
            };

            const sessionServer = new McpServer({
              name: "mcp-server-for-revit",
              version: "1.0.0",
            });
            await registerTools(sessionServer);

            await sessionServer.connect(transport);
            console.error(`Established SSE stream with session ID: ${sessionId}`);
          } catch (error) {
            console.error("Error establishing SSE stream:", error);
            if (!res.headersSent) {
              res.writeHead(500, { "Content-Type": "text/plain" });
              res.end("Error establishing SSE stream");
            }
          }
        }
        return;
      }

      // 2. POST requests on /sse or /
      if (method === "POST" && (pathname === "/sse" || pathname === "/")) {
        if (mcpSessionId) {
          // Existing Streamable HTTP session sending request/response
          console.error(`[MCP Server Log] Streamable HTTP POST for session: ${mcpSessionId}`);
          const transport = streamableTransports[mcpSessionId];
          if (!transport) {
            console.error(`[MCP Server Log] Session not found for Streamable HTTP POST: ${mcpSessionId}`);
            res.writeHead(404, { "Content-Type": "text/plain" });
            res.end("Session not found");
            return;
          }
          try {
            await transport.handleRequest(req, res);
          } catch (error) {
            console.error("[MCP Server Log] Error handling Streamable HTTP POST:", error);
            if (!res.headersSent) {
              res.writeHead(500, { "Content-Type": "text/plain" });
              res.end("Internal Server Error");
            }
          }
        } else {
          // New Streamable HTTP connection (initialization)
          console.error(`[MCP Server Log] New Streamable HTTP initialization POST request`);
          try {
            const sessionId = crypto.randomUUID();
            console.error(`[MCP Server Log] Generating new Streamable HTTP session ID: ${sessionId}`);
            const transport = new StreamableHTTPServerTransport({
              sessionIdGenerator: () => sessionId
            });

            streamableTransports[sessionId] = transport;

            transport.onclose = () => {
              console.error(`Streamable transport closed for session ${sessionId}`);
              delete streamableTransports[sessionId];
            };

            const sessionServer = new McpServer({
              name: "mcp-server-for-revit",
              version: "1.0.0",
            });
            await registerTools(sessionServer);

            await sessionServer.connect(transport);
            await transport.handleRequest(req, res);
          } catch (error) {
            console.error("Error establishing Streamable HTTP session:", error);
            if (!res.headersSent) {
              res.writeHead(500, { "Content-Type": "text/plain" });
              res.end("Error establishing Streamable HTTP session");
            }
          }
        }
        return;
      }

      // 3. DELETE requests on /sse or /
      if (method === "DELETE" && (pathname === "/sse" || pathname === "/")) {
        if (!mcpSessionId) {
          res.writeHead(400, { "Content-Type": "text/plain" });
          res.end("Missing Mcp-Session-Id header");
          return;
        }
        console.error(`[MCP Server Log] Streamable HTTP DELETE for session: ${mcpSessionId}`);
        const transport = streamableTransports[mcpSessionId];
        if (!transport) {
          res.writeHead(404, { "Content-Type": "text/plain" });
          res.end("Session not found");
          return;
        }
        try {
          await transport.handleRequest(req, res);
        } catch (error) {
          console.error("[MCP Server Log] Error handling Streamable HTTP DELETE:", error);
          if (!res.headersSent) {
            res.writeHead(500, { "Content-Type": "text/plain" });
            res.end("Internal Server Error");
          }
        }
        return;
      }

      // 4. POST requests to /messages (legacy SSE message sending)
      if (method === "POST" && pathname === "/messages") {
        const sessionId = url.searchParams.get("sessionId");
        if (!sessionId) {
          res.writeHead(400, { "Content-Type": "text/plain" });
          res.end("Missing sessionId parameter");
          return;
        }

        const transport = transports[sessionId];
        if (!transport) {
          res.writeHead(404, { "Content-Type": "text/plain" });
          res.end("Session not found");
          return;
        }

        try {
          await transport.handlePostMessage(req, res);
        } catch (error) {
          console.error("Error handling request:", error);
          if (!res.headersSent) {
            res.writeHead(500, { "Content-Type": "text/plain" });
            res.end("Error handling request");
          }
        }
        return;
      }

      // 5. Unhandled routes
      res.writeHead(404, { "Content-Type": "text/plain" });
      res.end("Not Found");
    });

    httpServer.listen(ssePort, "0.0.0.0", () => {
      console.error(`Revit MCP Server start success`);
      console.error(`SSE Server listening on host: 0.0.0.0, port: ${ssePort}`);
      console.error(`Connect to SSE endpoint at: http://0.0.0.0:${ssePort}/sse`);
    });

    // 处理退出信号以优雅关闭连接
    const shutdown = async () => {
      console.error("Shutting down SSE server...");
      httpServer.close();
      for (const sessionId in transports) {
        try {
          await transports[sessionId].close();
        } catch (error) {
          console.error(`Error closing transport for session ${sessionId}:`, error);
        }
      }
      for (const sessionId in streamableTransports) {
        try {
          await streamableTransports[sessionId].close();
        } catch (error) {
          console.error(`Error closing Streamable transport for session ${sessionId}:`, error);
        }
      }
      process.exit(0);
    };

    process.on("SIGINT", shutdown);
    process.on("SIGTERM", shutdown);
  } else {
    // 默认使用 Stdio 传输层
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error("Revit MCP Server start success (Stdio)");
  }
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});
