import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDeleteBeamRebarTool(server: McpServer) {
  server.tool(
    "delete_beam_rebar",
    "Xóa cốt thép dầm và/hoặc cột cấu trúc dựa vào Element ID tương ứng trong dự án Revit.",
    {
      beamId: z.string().optional().describe("ElementId của dầm cấu trúc cần xóa cốt thép (ví dụ: '123456')"),
      columnId: z.string().optional().describe("ElementId của cột cấu trúc cần xóa cốt thép (ví dụ: '654321')"),
    },
    async (args, extra) => {
      if (!args.beamId && !args.columnId) {
        return {
          content: [
            {
              type: "text",
              text: "Lỗi: Bạn phải cung cấp ít nhất một trong hai tham số beamId hoặc columnId.",
            },
          ],
          isError: true,
        };
      }

      const params = {
        beamId: args.beamId || null,
        columnId: args.columnId || null,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("delete_beam_rebar", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Xóa cốt thép dầm thất bại: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
