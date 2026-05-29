import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerGetBeamIdByGridTool(server: McpServer) {
  server.tool(
    "get_beam_id_by_grid",
    "Lấy danh sách và Element ID của các dầm (OST_StructuralFraming) chạy dọc theo một đường lưới trục (Grid) chỉ định.",
    {
      gridName: z
        .string()
        .describe("Tên của đường lưới trục cần tìm dầm dọc theo (ví dụ: 'A', 'B', '1', '2')"),
      levelName: z
        .string()
        .optional()
        .describe("Tùy chọn: Tên Level để lọc dầm (ví dụ: 'Level 1', 'Tầng 2')"),
      toleranceMm: z
        .number()
        .optional()
        .describe("Tùy chọn: Khoảng sai lệch tối đa cho phép giữa dầm và lưới trục tính bằng milimét. Mặc định là 50mm"),
    },
    async (args, extra) => {
      const params = {
        gridName: args.gridName,
        levelName: args.levelName,
        toleranceMm: args.toleranceMm !== undefined ? args.toleranceMm : 50,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_beam_id_by_grid", params);
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
              text: `Lấy dầm theo lưới trục thất bại: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
