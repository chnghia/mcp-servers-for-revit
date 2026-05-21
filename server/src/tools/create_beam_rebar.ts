import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

// Định nghĩa enum tương tự phía C# để Zod tự động validate và hướng dẫn AI Client
const StirrupShapeTypeSchema = z.enum([
  "Closed",
  "UShapeTop",
  "UShapeBottom",
  "UShape",
  "DoubleH",
  "OffsetH",
  "DoubleV",
  "OffsetV"
]);

const StirrupDistributionTypeSchema = z.enum([
  "SingleZone",
  "TwoZones",
  "ThreeZonesSymmetric",
  "ThreeZonesCustom"
]);

const RebarHookTypeSchema = z.union([
  z.enum(["None", "Hook90", "Hook135", "Hook180"]),
  z.number().describe("0: None, 90: Hook90, 135: Hook135, 180: Hook180")
]);

const BeamRebarSettingSchema = z.object({
  // Stirrup Configuration
  StirrupShape: StirrupShapeTypeSchema.default("Closed").describe("Kiểu dáng cốt thép đai (Closed, UShapeTop, UShapeBottom, etc.)"),
  Overlap: z.number().int().default(10).describe("Lần đường kính thép neo đai chéo chồng lên nhau"),
  MiddleBarsCount: z.number().int().default(2).describe("Số thanh thép giữa"),
  StirrupBarTypeName: z.string().default("10M").describe("Tên loại thép đai (ví dụ: '10M')"),
  ShapeName: z.string().default("").describe("Tên Shape family sử dụng"),
  StirrupHookAtStart: RebarHookTypeSchema.default("None").describe("Hook đầu đai"),
  StirrupHookAtEnd: RebarHookTypeSchema.default("None").describe("Hook cuối đai"),
  Cover: z.number().default(25.0).describe("Lớp bê tông bảo vệ tính bằng mm"),
  IsHalfStirrupIn: z.boolean().default(false).describe("Có vẽ đai lồng dạng chữ L hay không"),

  // Stirrup Distribution
  DistributionType: StirrupDistributionTypeSchema.default("SingleZone").describe("Kiểu phân bổ cốt đai (SingleZone, TwoZones, ThreeZonesSymmetric, ThreeZonesCustom)"),
  IsPercentageMode: z.boolean().default(true).describe("Phân bổ thép đai theo phần trăm (true) hay theo chiều dài cụ thể mm (false)"),
  L1: z.number().default(100).describe("Chiều dài phân dải Zone 1 (% hoặc mm)"),
  L2: z.number().default(0).describe("Chiều dài phân dải Zone 2 (% hoặc mm)"),
  L3: z.number().default(0).describe("Chiều dài phân dải Zone 3 (% hoặc mm)"),
  IsRemoveFirst: z.boolean().default(false).describe("Có bỏ thanh thép đai đầu tiên sát gối không"),
  IsRemoveLast: z.boolean().default(false).describe("Có bỏ thanh thép đai cuối cùng sát gối không"),
  S1: z.number().default(100).describe("Khoảng cách (spacing) cốt đai vùng S1 (mm)"),
  S2: z.number().default(0).describe("Khoảng cách (spacing) cốt đai vùng S2 (mm)"),
  S3: z.number().default(0).describe("Khoảng cách (spacing) cốt đai vùng S3 (mm)"),

  // Top Bars
  EnableTopBars: z.boolean().default(true).describe("Bật cốt thép dọc phía trên"),
  TopBarTypeName: z.string().default("25M").describe("Tên loại thép dọc trên (ví dụ: '25M')"),
  TopHookStart: RebarHookTypeSchema.default("None").describe("Hook ở điểm bắt đầu thép trên"),
  TopHookEnd: RebarHookTypeSchema.default("None").describe("Hook ở điểm kết thúc thép trên"),
  TopBarsCount: z.number().int().default(2).describe("Số lượng thanh thép dọc trên"),

  // Bottom Bars
  EnableBottomBars: z.boolean().default(true).describe("Bật cốt thép dọc phía dưới"),
  BottomBarTypeName: z.string().default("25M").describe("Tên loại thép dọc dưới (ví dụ: '25M')"),
  BottomHookStart: RebarHookTypeSchema.default("None").describe("Hook ở điểm bắt đầu thép dưới"),
  BottomHookEnd: RebarHookTypeSchema.default("None").describe("Hook ở điểm kết thúc thép dưới"),
  BottomBarsCount: z.number().int().default(2).describe("Số lượng thanh thép dọc dưới"),

  // Side Bars
  EnableSideBars: z.boolean().default(true).describe("Bật cốt thép cấu tạo hông (Side Bars)"),
  SideBarTypeName: z.string().default("25M").describe("Tên loại thép dọc hông (ví dụ: '25M')"),
  SideHookStart: RebarHookTypeSchema.default("None").describe("Hook đầu thép hông"),
  SideHookEnd: RebarHookTypeSchema.default("None").describe("Hook cuối thép hông"),
  SideBarsPerSideCount: z.number().int().default(2).describe("Số lượng thép hông mỗi bên dầm"),
});

export function registerCreateBeamRebarTool(server: McpServer) {
  server.tool(
    "create_beam_rebar",
    "Tạo cốt thép dầm tự động bằng cách gọi trực tiếp thư viện AconsEngineer qua cổng DLL. Dầm thép sẽ được bố trí đầy đủ cốt dọc trên, cốt dọc dưới, cốt đai và cốt hông dựa theo các tham số chi tiết trong cấu hình 'settings'.",
    {
      beamId: z.string().describe("ElementId của dầm cấu trúc cần vẽ cốt thép (dạng chuỗi số nguyên, ví dụ: '123456')"),
      settings: BeamRebarSettingSchema.describe("Cấu hình chi tiết bố trí cốt thép dầm"),
    },
    async (args, extra) => {
      const params = {
        beamId: args.beamId,
        settings: args.settings,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_beam_rebar", params);
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
              text: `Create beam rebar failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
