using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.BeamRebar
{
    public class DeleteBeamRebarEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long? BeamId { get; set; }
        public long? ColumnId { get; set; }
        public RebarDeletionResult Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                Result = null;

                if (BeamId == null && ColumnId == null)
                {
                    Result = RebarDeletionResult.Failed("Cả beamId và columnId đều trống. Phải cung cấp ít nhất một ID.");
                    return;
                }

                var hostIds = new HashSet<ElementId>();
                if (BeamId.HasValue)
                {
#if REVIT2024_OR_GREATER
                    hostIds.Add(new ElementId(BeamId.Value));
#else
                    hostIds.Add(new ElementId((int)BeamId.Value));
#endif
                }

                if (ColumnId.HasValue)
                {
#if REVIT2024_OR_GREATER
                    hostIds.Add(new ElementId(ColumnId.Value));
#else
                    hostIds.Add(new ElementId((int)ColumnId.Value));
#endif
                }

                int rebarCount = 0;
                int hostCount = 0;

                using (var trans = new Transaction(doc, "Remove Rebar via MCP"))
                {
                    trans.Start();

                    foreach (var hostId in hostIds)
                    {
                        var host = doc.GetElement(hostId);
                        if (host == null) continue;

                        var hostData = RebarHostData.GetRebarHostData(host);
                        if (hostData == null) continue;

                        var rebars = hostData.GetRebarsInHost();
                        if (rebars != null && rebars.Any())
                        {
                            var hostHadRebar = false;
                            foreach (var rebar in rebars)
                            {
                                try
                                {
                                    doc.Delete(rebar.Id);
                                    rebarCount++;
                                    hostHadRebar = true;
                                }
                                catch
                                {
                                    // Bỏ qua nếu thép bị ghim hoặc không cho phép xóa trực tiếp
                                }
                            }
                            if (hostHadRebar) hostCount++;
                        }
                    }

                    trans.Commit();
                }

                if (rebarCount > 0)
                {
                    Result = RebarDeletionResult.Succeeded($"Đã xóa thành công {rebarCount} thép rebar từ {hostCount} cấu kiện.", rebarCount);
                }
                else
                {
                    Result = RebarDeletionResult.Succeeded("Không tìm thấy thép rebar nào trong các cấu kiện đã chọn.", 0);
                }
            }
            catch (Exception ex)
            {
                Result = RebarDeletionResult.Failed($"Lỗi hệ thống khi xử lý xóa: {ex.Message}");
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Delete Beam Rebar";
        }
    }

    public class RebarDeletionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int DeletedCount { get; set; }

        public static RebarDeletionResult Failed(string msg)
        {
            return new RebarDeletionResult { Success = false, Message = msg, DeletedCount = 0 };
        }

        public static RebarDeletionResult Succeeded(string msg, int count)
        {
            return new RebarDeletionResult { Success = true, Message = msg, DeletedCount = count };
        }
    }
}
