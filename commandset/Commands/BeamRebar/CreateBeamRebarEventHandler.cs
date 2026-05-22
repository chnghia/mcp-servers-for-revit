using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using LibraryModule.Domain.Model;
using LibraryModule.DomainServices.BeamRebar;

namespace RevitMCPCommandSet.Commands.BeamRebar
{
    public class CreateBeamRebarEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long BeamId { get; set; }
        public BeamRebarSettingModel Settings { get; set; }
        public RebarCreationResult Result { get; private set; }

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
                if (Settings == null)
                {
                    Result = RebarCreationResult.Failed("Settings were null.");
                    return;
                }

#if REVIT2024_OR_GREATER
                ElementId beamId = new ElementId(BeamId);
#else
                ElementId beamId = new ElementId((int)BeamId);
#endif

                var beam = doc.GetElement(beamId);
                if (beam == null)
                {
                    Result = RebarCreationResult.Failed($"Cannot find structural framing element with ID {BeamId} in the current document.");
                    return;
                }

                using (var trans = new Transaction(doc, "Create Beam Rebar via MCP"))
                {
                    trans.Start();

                    var creationService = new BeamRebarCreationService();
                    Result = creationService.CreateRebars(doc, beam, Settings);

                    if (Result.Success)
                    {
                        trans.Commit();
                    }
                    else
                    {
                        trans.RollBack();
                    }
                }
            }
            catch (Exception ex)
            {
                Result = RebarCreationResult.Failed($"Error in event handler: {ex.Message}");
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Create Beam Rebar";
        }
    }
}
