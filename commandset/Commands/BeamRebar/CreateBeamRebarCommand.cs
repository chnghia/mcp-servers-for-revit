using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using LibraryModule.Domain.Model;

namespace RevitMCPCommandSet.Commands.BeamRebar
{
    public class CreateBeamRebarCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateBeamRebarEventHandler _handler => (CreateBeamRebarEventHandler)Handler;

        public override string CommandName => "create_beam_rebar";

        public CreateBeamRebarCommand(UIApplication uiApp)
            : base(new CreateBeamRebarEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // 1. Get beam ID
                    string beamIdStr = parameters?["beamId"]?.ToString();
                    if (string.IsNullOrEmpty(beamIdStr))
                    {
                        throw new ArgumentException("Parameter 'beamId' is required.");
                    }

                    long idVal = long.Parse(beamIdStr);

                    // 2. Deserialize Settings directly to BeamRebarSettingModel
                    var settingsJson = parameters?["settings"] as JObject;
                    if (settingsJson == null)
                    {
                        throw new ArgumentException("Parameter 'settings' is required.");
                    }

                    var settings = settingsJson.ToObject<BeamRebarSettingModel>();

                    // 3. Set parameters on handler
                    _handler.BeamId = idVal;
                    _handler.Settings = settings;

                    // 4. Raise and wait
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (_handler.Result != null && _handler.Result.Success)
                        {
                            return new
                            {
                                success = true,
                                message = _handler.Result.Message,
                                stirrupCount = _handler.Result.StirrupCount,
                                mainRebarCount = _handler.Result.MainRebarCount,
                                createdRebarIds = _handler.Result.CreatedRebarIds?.Select(id => id.ToString()).ToList()
                            };
                        }
                        else
                        {
                            throw new Exception(_handler.Result?.Message ?? "Failed to create beam rebar.");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("Create beam rebar operation timed out.");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create beam rebar failed: {ex.Message}");
                }
            }
        }
    }
}
