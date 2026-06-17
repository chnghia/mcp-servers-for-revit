using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.BeamRebar
{
    public class DeleteBeamRebarCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteBeamRebarEventHandler _handler => (DeleteBeamRebarEventHandler)Handler;

        public override string CommandName => "delete_beam_rebar";

        public DeleteBeamRebarCommand(UIApplication uiApp)
            : base(new DeleteBeamRebarEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string beamIdStr = parameters?["beamId"]?.ToString();
                    string columnIdStr = parameters?["columnId"]?.ToString();

                    if (string.IsNullOrEmpty(beamIdStr) && string.IsNullOrEmpty(columnIdStr))
                    {
                        throw new ArgumentException("Phải cung cấp ít nhất tham số 'beamId' hoặc 'columnId'.");
                    }

                    long? beamId = null;
                    if (!string.IsNullOrEmpty(beamIdStr))
                    {
                        beamId = long.Parse(beamIdStr);
                    }

                    long? columnId = null;
                    if (!string.IsNullOrEmpty(columnIdStr))
                    {
                        columnId = long.Parse(columnIdStr);
                    }

                    _handler.BeamId = beamId;
                    _handler.ColumnId = columnId;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (_handler.Result != null && _handler.Result.Success)
                        {
                            return new
                            {
                                success = true,
                                message = _handler.Result.Message,
                                deletedCount = _handler.Result.DeletedCount
                            };
                        }
                        else
                        {
                            throw new Exception(_handler.Result?.Message ?? "Failed to delete beam rebar.");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("Delete beam rebar operation timed out.");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Delete beam rebar failed: {ex.Message}");
                }
            }
        }
    }
}
