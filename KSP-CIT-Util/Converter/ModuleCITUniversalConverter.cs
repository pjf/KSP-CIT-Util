using System;
using System.Collections.Generic;
using System.Linq;
using CIT_Util.Types;
using UnityEngine;

namespace CIT_Util.Converter
{
    public class ModuleCITUniversalConverter : PartModule
    {
        private const double MaxDeltaDef = 60*60*6;
        private const double MaxEcDeltaDef = 1;
        [KSPField] public string ConverterName = ConvUtil.NaString;
        [KSPField(isPersistant = true)] public bool ConverterActive;
        [KSPField(isPersistant = true)] public double LastUpdate;
        [KSPField] public double MaxDelta = MaxDeltaDef;
        [KSPField] public double MaxEcDelta = MaxEcDeltaDef;
        [KSPField(guiActive = true)] public string Status = ConvUtil.NaString;
        private PerformanceAdjustmentRatios _adjustmentRatios = PerformanceAdjustmentRatios.Default;
        private bool _initialized;
        private double _conversionRate;
        [KSPField] public string ConversionRate = "1.0";

        private List<ConverterResource> _inputResources;
        private List<ConverterResource> _outputResources;
        private PerformanceCurve _performanceCurve;

        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void ToggleConverter()
        {
            this.ConverterActive = !this.ConverterActive;
        }

        public void FixedUpdate()
        {
            if (!this._initialized
                || !this.ConverterActive
                || !HighLogic.LoadedSceneIsFlight
                || !HighLogic.LoadedSceneHasPlanetarium
                || Time.timeSinceLevelLoad < 1.0f
                || !FlightGlobals.ready)
            {
                this._setStatus(ConvStates.Inactive);
                return;
            }

            this.Fields["Status"].guiName = this.ConverterName;
            this.Events["ToggleConverter"].guiName = "Toggle " + this.ConverterName;

            //ConvUtil.Log(ConverterName + " is processing");
            var now = Planetarium.GetUniversalTime();
            var delta = now - this.LastUpdate;
            if (delta > this.MaxDelta)
            {
                delta = this.MaxDelta;
            }
            this.LastUpdate += delta;
            var ratio = delta*_conversionRate*TimeWarp.fixedDeltaTime;
            var availableInRes = this._findAvailableResources(this._inputResources);
            var availableOutRes = this._findAvailableResources(this._outputResources);
            this._setupAdjustmentRatios(_findSmallestAmount(availableInRes), _findSmallestAmount(availableOutRes));
            var inResTab = _createDemandLookupTable(this._inputResources, this._adjustRatio(ratio, false));
            var outResTab = _createDemandLookupTable(this._outputResources, this._adjustRatio(ratio, true));
            if (_canAllTakeDemand(availableInRes, inResTab))
            {
                if (_canAllTakeDemand(availableOutRes, outResTab))
                {
                    var rollbackBuffer = new List<Tuple<int, double>>(this._inputResources.Count + this._outputResources.Count);
                    var rollback = this._processResourceTransfers(availableInRes, inResTab, rollbackBuffer);
                    if (!rollback)
                    {
                        rollback = this._processResourceTransfers(availableOutRes, outResTab, rollbackBuffer);
                    }
                    if (rollback)
                    {
                        this._rollbackTransfers(rollbackBuffer);
                        this._setStatus(ConvStates.Malfunction);
                    }
                    else
                    {
                        this._setStatus(ConvStates.Active);
                    }
                }
                else
                {
                    this._setStatus(ConvStates.OutputFull);
                }
            }
            else
            {
                this._setStatus(ConvStates.InputDepleted);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Debug.Log(node);
            var inDefs = node.GetNodes("INPUT_DEF");
            var outDefs = node.GetNodes("OUTPUT_DEF");
            var curve = node.GetNode("CURVE_DEF");
            //this.ConverterName = node.GetValue("ConverterName");
            ConvUtil.Log("onload (" + this.ConverterName + "): indefs = " + inDefs.Length + " outDefs = " + outDefs.Length);
            this._inputResources = _processResourceDefinitions(inDefs, false);
            this._outputResources = _processResourceDefinitions(outDefs, true);
            this._performanceCurve = this._parsePerformanceCurveDefinition(curve);
            if (!double.TryParse(ConversionRate, out this._conversionRate))
            {
                this._conversionRate = 1d;
                ConvUtil.LogWarning("unable to parse conversion rate, defaulting to 1.0");
            }
            this.Fields["Status"].guiName = this.ConverterName;
            this.Events["ToggleConverter"].guiName = "Toggle " + this.ConverterName;
            this._initialized = true;
        }

        public override void OnStart(StartState state)
        {
            //TODO
        }

        private double _adjustRatio(double ratio, bool outres)
        {
            return ratio*(outres ? this._adjustmentRatios.OutputRatio : this._adjustmentRatios.InputRatio);
        }

        private static bool _canAllTakeDemand(IList<AvailableResourceInfo> availableRes, IDictionary<int, double> resTab)
        {
            for (var i = 0; i < availableRes.Count; i++)
            {
                var ari = availableRes[i];
                if (!ari.CanTakeDemand(resTab[ari.ResourceId]))
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<int, double> _createDemandLookupTable(IList<ConverterResource> convRes, double ratio)
        {
            var listCnt = convRes.Count;
            var lt = new Dictionary<int, double>(listCnt);
            for (var i = 0; i < listCnt; i++)
            {
                var cr = convRes[i];
                var finalRatio = cr.RatePerSecond*ratio;
                lt.Add(cr.ResourceId, finalRatio);
            }
            return lt;
        }

        private List<AvailableResourceInfo> _findAvailableResources(IList<ConverterResource> resList)
        {
            var resListCount = resList.Count;
            var retList = new List<AvailableResourceInfo>(resListCount);
            for (var i = 0; i < resListCount; i++)
            {
                var resDef = resList[i];
                var partRes = new List<PartResource>(10);
                this.part.GetConnectedResources(resDef.ResourceId, resDef.FlowMode, partRes);
                var amountAvailable = 0d;
                var spaceAvailable = 0d;
                for (var j = 0; j < partRes.Count; j++)
                {
                    var pr = partRes[j];
                    amountAvailable += pr.amount;
                    spaceAvailable += (pr.maxAmount - pr.amount);
                }
                retList.Add(new AvailableResourceInfo(resDef.ResourceId, resDef.OutputResource, amountAvailable, spaceAvailable, resDef.AllowOverflow));
            }
            return retList;
        }

        private static double _findSmallestAmount(IList<AvailableResourceInfo> resList)
        {
            var min = 1d;
            for (var i = 0; i < resList.Count; i++)
            {
                var r = resList[i];
                if (r.PercentageFilled < min)
                {
                    min = r.PercentageFilled;
                }
            }
            return min;
        }

        private void _logTransferError(string prefix, int rid, bool output, double diff)
        {
            ConvUtil.LogWarning(prefix + " of " + this._lookupResourceName(rid, output) + " failed (demand/actual difference = " + diff + ")");
        }

        private string _lookupResourceName(int resid, bool output)
        {
            var resName = (output ? this._outputResources : this._inputResources)
                .Where(converterResource => converterResource.ResourceId == resid)
                .Select(cr => cr.ResourceName)
                .FirstOrDefault();
            return resName ?? ConvUtil.NaString;
        }

        private PerformanceCurve _parsePerformanceCurveDefinition(ConfigNode curveDef)
        {
            if (curveDef == null)
            {
                return null;
            }
            //TODO
            return null;
        }

        private static List<ConverterResource> _processResourceDefinitions(IEnumerable<ConfigNode> defNodes, bool output)
        {
            var ret = new List<ConverterResource>();
            foreach (var configNode in defNodes.Where(cf => cf != null))
            {
                if (configNode.HasValue("ResourceName")
                    && configNode.HasValue("RatePerSecond")
                    && (!output || configNode.HasValue("AllowOverflow")))
                {
                    var resName = configNode.GetValue("ResourceName");
                    var ratePerSec = configNode.GetValue("RatePerSecond");
                    var allowOverflow = output ? configNode.GetValue("AllowOverflow") : "false";
                    double parsedRate;
                    if (double.TryParse(ratePerSec, out parsedRate))
                    {
                        bool parsedOverflow;
                        if (bool.TryParse(allowOverflow, out parsedOverflow))
                        {
                            ConverterResource cr;
                            if (ConverterResource.CreateNew(resName, parsedRate, out cr, output, parsedOverflow))
                            {
                                ret.Add(cr);
                            }
                        }
                        else
                        {
                            ConvUtil.LogError("unable to parse 'AllowOverflow' - ignoring resource definition");
                        }
                    }
                    else
                    {
                        ConvUtil.LogError("unable to parse 'RatePerSecond' - ignoring resource definition");
                    }
                }
            }
            return ret;
        }

        private bool _processResourceTransfers(IList<AvailableResourceInfo> availableRes, IDictionary<int, double> resTab, ICollection<Tuple<int, double>> rollbackBuffer)
        {
            for (var i = 0; i < availableRes.Count; i++)
            {
                var ari = availableRes[i];
                var output = ari.OutputResource;
                var rid = ari.ResourceId;
                var demand = resTab[rid];
                var transferResult = this._transferResource(rid, demand, output);
                rollbackBuffer.Add(new Tuple<int, double>(transferResult.Item2, transferResult.Item3));
                if (transferResult.Item1 || (ari.OutputResource && ari.AllowOverflow && ari.AvailableSpace <= ConvUtil.Epsilon))
                {
                    continue;
                }
                this._logTransferError(output ? "production" : "consumption", rid, output, transferResult.Item4);
                return true;
            }
            return false;
        }

        private void _rollbackTransfers(IEnumerable<Tuple<int, double>> rollbackBuffer)
        {
            ConvUtil.LogWarning("conversion failed, attempting rollback");
            foreach (var rollbackInfo in rollbackBuffer)
            {
                var demand = rollbackInfo.Item2*-1;
                this.part.RequestResource(rollbackInfo.Item1, demand);
            }
        }

        private void _setStatus(ConvStates status)
        {
            var text = ConvUtil.NaString;
            switch (status)
            {
                case ConvStates.Active:
                {
                    text = "Active";
                }
                    break;
                case ConvStates.Inactive:
                {
                    text = "Inactive";
                }
                    break;
                case ConvStates.InputDepleted:
                {
                    text = "Input depleted";
                }
                    break;
                case ConvStates.OutputFull:
                {
                    text = "Output full";
                }
                    break;
                case ConvStates.Malfunction:
                {
                    text = "Malfunction";
                }
                    break;
            }
            this.Status = text;
        }

        private void _setupAdjustmentRatios(double minInputReserve, double minOutputReserve)
        {
            if (this._performanceCurve != null)
            {
                this._adjustmentRatios = this._performanceCurve.GetRatios(minInputReserve, minOutputReserve);
            }
        }

        private Tuple<bool, int, double, double> _transferResource(int resourceId, double demand, bool output)
        {
            if (output)
            {
                demand *= -1;
            }
            var actual = this.part.RequestResource(resourceId, demand);
            var diff = Math.Abs(actual - demand);
            var success = diff < ConvUtil.Epsilon;
            return new Tuple<bool, int, double, double>(success, resourceId, actual, diff);
        }

        private enum ConvStates
        {
            Active,
            Inactive,
            InputDepleted,
            OutputFull,
            Malfunction
        }
    }
}