using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetBeamIdByGridCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetBeamIdByGridEventHandler _handler => (GetBeamIdByGridEventHandler)Handler;

        public override string CommandName => "get_beam_id_by_grid";

        public GetBeamIdByGridCommand(UIApplication uiApp)
            : base(new GetBeamIdByGridEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Trích xuất tham số từ JSON-RPC
                    string gridName = parameters?["gridName"]?.Value<string>();
                    if (string.IsNullOrEmpty(gridName))
                    {
                        throw new ArgumentException("Tham số 'gridName' là bắt buộc.");
                    }

                    string levelName = parameters?["levelName"]?.Value<string>();
                    double toleranceMm = parameters?["toleranceMm"]?.Value<double>() ?? 50.0;

                    // Thiết lập tham số cho handler
                    _handler.GridName = gridName;
                    _handler.LevelName = levelName;
                    _handler.ToleranceMm = toleranceMm;

                    // Kích hoạt External Event và đợi tối đa 15 giây
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (!_handler.IsSuccess)
                        {
                            throw new Exception(_handler.ErrorMessage);
                        }
                        return _handler.ResultBeams;
                    }
                    else
                    {
                        throw new TimeoutException("Thao tác lấy dầm theo lưới trục bị quá thời gian (Timeout).");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Lấy dầm theo lưới trục thất bại: {ex.Message}");
                }
            }
        }
    }
}
