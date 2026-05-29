using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services
{
    public class GetBeamIdByGridEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Tham số đầu vào
        public string GridName { get; set; }
        public string LevelName { get; set; }
        public double ToleranceMm { get; set; } = 50.0;

        // Kết quả đầu ra
        public List<BeamGridInfo> ResultBeams { get; private set; } = new List<BeamGridInfo>();
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }

        // Đồng bộ hóa trạng thái
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                IsSuccess = false;
                ErrorMessage = string.Empty;
                ResultBeams.Clear();

                var doc = app.ActiveUIDocument.Document;

                // 1. Tìm đường lưới trục (Grid) theo tên chỉ định
                var grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .ToList();

                var targetGrid = grids.FirstOrDefault(g => g.Name.Equals(GridName, StringComparison.OrdinalIgnoreCase));
                if (targetGrid == null)
                {
                    var availableGridNames = string.Join(", ", grids.Select(g => $"'{g.Name}'"));
                    ErrorMessage = $"Không tìm thấy đường lưới trục nào có tên '{GridName}'. Các lưới trục hiện có trong mô hình: {availableGridNames}";
                    return;
                }

                Curve gridCurve = targetGrid.Curve;
                if (gridCurve == null)
                {
                    ErrorMessage = $"Đường lưới trục '{GridName}' không có thông tin hình học (Curve).";
                    return;
                }

                // Quy đổi sai số từ mm sang feet (đơn vị của Revit)
                double toleranceFeet = ToleranceMm / 304.8;

                // 2. Lấy danh sách dầm (Structural Framing)
                var beams = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                // Lọc theo Level nếu người dùng chỉ định
                if (!string.IsNullOrEmpty(LevelName))
                {
                    beams = beams.Where(b =>
                    {
                        string lvlName = GetLevelName(b, doc);
                        return lvlName.Equals(LevelName, StringComparison.OrdinalIgnoreCase);
                    }).ToList();
                }

                // 3. Quét dầm và lọc ra các dầm nằm đồng phẳng dọc theo lưới trục
                foreach (var beam in beams)
                {
                    if (beam.Location is LocationCurve locCurve)
                    {
                        Curve beamCurve = locCurve.Curve;
                        if (beamCurve != null && IsCollinear(gridCurve, beamCurve, toleranceFeet))
                        {
                            string lvlName = GetLevelName(beam, doc);
                            XYZ startPt = beamCurve.GetEndPoint(0);
                            XYZ endPt = beamCurve.GetEndPoint(1);

                            ResultBeams.Add(new BeamGridInfo
                            {
                                BeamId = beam.Id.GetValue(),
                                Name = beam.Name,
                                FamilyName = beam.Symbol.Family.Name,
                                TypeName = beam.Symbol.Name,
                                LevelName = lvlName,
                                LengthMm = beamCurve.Length * 304.8,
                                StartPointMm = new double[] { startPt.X * 304.8, startPt.Y * 304.8, startPt.Z * 304.8 },
                                EndPointMm = new double[] { endPt.X * 304.8, endPt.Y * 304.8, endPt.Z * 304.8 }
                            });
                        }
                    }
                }

                IsSuccess = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Lỗi hệ thống khi xử lý: {ex.Message}";
                IsSuccess = false;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private bool IsCollinear(Curve gridCurve, Curve beamCurve, double tolerance)
        {
            if (gridCurve == null || beamCurve == null) return false;

            XYZ gStart = gridCurve.GetEndPoint(0);
            XYZ gEnd = gridCurve.GetEndPoint(1);
            XYZ bStart = beamCurve.GetEndPoint(0);
            XYZ bEnd = beamCurve.GetEndPoint(1);

            // Chiếu lên mặt phẳng XY (Z = 0)
            XYZ gStartXY = new XYZ(gStart.X, gStart.Y, 0);
            XYZ gEndXY = new XYZ(gEnd.X, gEnd.Y, 0);
            XYZ bStartXY = new XYZ(bStart.X, bStart.Y, 0);
            XYZ bEndXY = new XYZ(bEnd.X, bEnd.Y, 0);

            // 1. Trường hợp cả đường lưới và dầm đều là đoạn thẳng
            if (gridCurve is Line && beamCurve is Line)
            {
                XYZ gridVec = gEndXY - gStartXY;
                double gridLen = gridVec.GetLength();
                if (gridLen < 0.001) return false;

                XYZ gridDir = gridVec.Normalize();

                // Tính khoảng cách từ 2 đầu dầm đến đường thẳng lưới trục vô hạn
                // d = |(P - A) x Dir| trên XY plane
                XYZ vStart = bStartXY - gStartXY;
                double distStart = Math.Abs(vStart.X * gridDir.Y - vStart.Y * gridDir.X);

                XYZ vEnd = bEndXY - gStartXY;
                double distEnd = Math.Abs(vEnd.X * gridDir.Y - vEnd.Y * gridDir.X);

                return distStart < tolerance && distEnd < tolerance;
            }
            else
            {
                // 2. Trường hợp đường cong (như lưới cong)
                Curve projectedGrid = null;
                if (gridCurve is Arc)
                {
                    XYZ gMid = gridCurve.Evaluate(0.5, true);
                    XYZ gMidXY = new XYZ(gMid.X, gMid.Y, 0);
                    try
                    {
                        projectedGrid = Arc.Create(gStartXY, gEndXY, gMidXY);
                    }
                    catch
                    {
                        projectedGrid = Line.CreateBound(gStartXY, gEndXY);
                    }
                }
                else
                {
                    projectedGrid = Line.CreateBound(gStartXY, gEndXY);
                }

                if (projectedGrid != null)
                {
                    double distStart = projectedGrid.Distance(bStartXY);
                    double distEnd = projectedGrid.Distance(bEndXY);

                    XYZ bMid = beamCurve.Evaluate(0.5, true);
                    XYZ bMidXY = new XYZ(bMid.X, bMid.Y, 0);
                    double distMid = projectedGrid.Distance(bMidXY);

                    return distStart < tolerance && distEnd < tolerance && distMid < tolerance;
                }
            }

            return false;
        }

        private string GetLevelName(Element element, Document doc)
        {
            ElementId levelId = element.LevelId;
            if (levelId == ElementId.InvalidElementId)
            {
                Parameter levelParam = element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                if (levelParam != null && levelParam.StorageType == StorageType.ElementId)
                {
                    levelId = levelParam.AsElementId();
                }
            }

            if (levelId != ElementId.InvalidElementId)
            {
                Level level = doc.GetElement(levelId) as Level;
                if (level != null)
                {
                    return level.Name;
                }
            }
            return "Không xác định";
        }

        public string GetName()
        {
            return "Lấy danh sách dầm theo lưới trục";
        }
    }

    public class BeamGridInfo
    {
        public long BeamId { get; set; }
        public string Name { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string LevelName { get; set; }
        public double LengthMm { get; set; }
        public double[] StartPointMm { get; set; }
        public double[] EndPointMm { get; set; }
    }
}
