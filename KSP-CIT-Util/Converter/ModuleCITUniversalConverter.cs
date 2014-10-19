﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CIT_Util.Types;
using UnityEngine;

namespace CIT_Util.Converter
{
    public class ModuleCITUniversalConverter : PartModule
    {
        private const byte RemTimeUpdateInterval = 5;
        private const byte RemTimeUpdateIntervalEditor = 30;
        [KSPField] public string ConversionRate = "1.0";
        [KSPField(isPersistant = true)] public bool ConverterActive;
        [KSPField] public string ConverterName = ConvUtil.NaString;
        [KSPField(isPersistant = false)] public string CurveString;
        [KSPField(isPersistant = false)] public string InputDefsString;
        [KSPField(isPersistant = true)] public double LastUpdate;
        [KSPField] public double MaxDelta = ConvUtil.MaxDelta;
        [KSPField] public double MaxEcDelta = ConvUtil.ElectricChargeMaxDelta;
        [KSPField(isPersistant = false)] public string OutputDefsString;
        [KSPField(guiActive = true, guiActiveEditor = true)] public string RemTime = ConvUtil.NaString;
        [KSPField(guiActive = true, guiActiveEditor = true)] public string Status = ConvUtil.NaString;
        private PerformanceAdjustmentRatios _adjustmentRatios = PerformanceAdjustmentRatios.Default;
        private Tuple<List<AvailableResourceInfo>, List<AvailableResourceInfo>> _availResForRemTime;
        private double _conversionRate;
        private int _electricChargeId;
        private bool _initialized;
        private List<ConverterResource> _inputResources;
        private List<ConverterResource> _outputResources;
        private PerformanceCurve _performanceCurve;
        private byte _remTimeUpdateCounter = RemTimeUpdateInterval;
        private bool _trySmallerDelta;

        public void FixedUpdate()
        {
            if (!this._initialized
                || !this.ConverterActive
                || !HighLogic.LoadedSceneIsFlight
                || !HighLogic.LoadedSceneHasPlanetarium
                || Time.timeSinceLevelLoad < 1.0f
                || !FlightGlobals.ready
                || (this.vessel != null
                    && (!this.vessel.loaded
                        || this.vessel.packed)))
            {
                this._setStatus(ConvStates.Inactive);
                this._trySmallerDelta = false;
                return;
            }
            if (this.LastUpdate < 0)
            {
                this.LastUpdate = Planetarium.GetUniversalTime();
                return;
            }

            var now = Planetarium.GetUniversalTime();
            var delta = now - this.LastUpdate;
            delta = Math.Min(delta, this.MaxDelta);
            var triedSmallerDelta = false;
            if (this._trySmallerDelta)
            {
                delta *= 0.1d;
                this._trySmallerDelta = false;
                triedSmallerDelta = true;
            }
            //Debug.Log("[UC] delta=" + delta + " trysmaller=" + triedSmallerDelta + " convrate=" + this._conversionRate);
            this.LastUpdate += delta;
            var ratio = delta*this._conversionRate;
            var availableInRes = this._findAvailableResources(this._inputResources);
            var availableOutRes = this._findAvailableResources(this._outputResources);
            this._availResForRemTime = new Tuple<List<AvailableResourceInfo>, List<AvailableResourceInfo>>(availableInRes, availableOutRes);
            //Debug.Log("[UC] availableresIn = " + availableInRes.Count + " availableoutres = " + availableOutRes.Count);
            this._setupAdjustmentRatios(_findSmallestAmount(availableInRes), _findSmallestAmount(availableOutRes));
            var inResTab = this._createDemandLookupTable(this._inputResources, this._adjustRatio(ratio, false));
            var outResTab = this._createDemandLookupTable(this._outputResources, this._adjustRatio(ratio, true));
            var unableToProcess = false;
            if (_canAllTakeDemand(availableInRes, inResTab, this._inputResources.Count > 0))
            {
                if (_canAllTakeDemand(availableOutRes, outResTab, this._outputResources.Count > 0))
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
                        unableToProcess = true;
                    }
                    else
                    {
                        this._setStatus(ConvStates.Active);
                    }
                }
                else
                {
                    this._setStatus(ConvStates.OutputFull);
                    unableToProcess = true;
                }
            }
            else
            {
                unableToProcess = true;
                this._setStatus(ConvStates.InputDepleted);
            }
            if (!unableToProcess || triedSmallerDelta || !(delta > ConvUtil.RetryDeltaThreshold))
            {
                return;
            }
            this.LastUpdate -= delta;
            this._trySmallerDelta = true;
        }

        public override void OnLoad(ConfigNode node)
        {
            //base.OnLoad(node);
            Debug.Log(node);
            var inDefs = node.GetNodes("INPUT_DEF");
            var outDefs = node.GetNodes("OUTPUT_DEF");
            var curve = node.GetNode("CURVE_DEF");
            if (inDefs.Length > 0)
            {
                this.InputDefsString = _stringifyResDefs(inDefs, false);
            }
            else if (!string.IsNullOrEmpty(this.InputDefsString))
            {
                inDefs = _defNodesFromString(this.InputDefsString, false);
            }
            if (outDefs.Length > 0)
            {
                this.OutputDefsString = _stringifyResDefs(outDefs, true);
            }
            else if (!string.IsNullOrEmpty(this.OutputDefsString))
            {
                outDefs = _defNodesFromString(this.OutputDefsString, true);
            }
            if (curve != null)
            {
                this.CurveString = _stringifyCurveDef(curve);
            }
            else if (!string.IsNullOrEmpty(this.CurveString))
            {
                curve = _curveNodeFromString(this.CurveString);
            }
            this._inputResources = _processResourceDefinitions(inDefs, false);
            this._outputResources = _processResourceDefinitions(outDefs, true);
            this._performanceCurve = this._parsePerformanceCurveDefinition(curve);
            if (!double.TryParse(this.ConversionRate, out this._conversionRate))
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
            var ecDef = PartResourceLibrary.Instance.GetDefinition(ConvUtil.ElectricCharge);
            if (ecDef != null)
            {
                this._electricChargeId = ecDef.id;
            }
            else
            {
                this._electricChargeId = -1;
            }
            //TODO
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true)]
        public void ToggleConverter()
        {
            this.LastUpdate = -1d;
            this.ConverterActive = !this.ConverterActive;
        }

        public void Update()
        {
            if (this._remTimeUpdateCounter > 0)
            {
                this._remTimeUpdateCounter--;
                return;
            }
            this._remTimeUpdateCounter = HighLogic.LoadedSceneIsEditor ? RemTimeUpdateIntervalEditor : RemTimeUpdateInterval;
            this._updateGui();
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (!this._initialized)
                {
                    return;
                }
                if (!this.ConverterActive)
                {
                    this._availResForRemTime = new Tuple<List<AvailableResourceInfo>, List<AvailableResourceInfo>>(this._findAvailableResources(this._inputResources), this._findAvailableResources(this._outputResources));
                }
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                if (!this._initialized)
                {
                    this.OnLoad(new ConfigNode());
                }
                this._availResForRemTime = new Tuple<List<AvailableResourceInfo>, List<AvailableResourceInfo>>(this._findAvailableResourcesInEditor(this._inputResources), this._findAvailableResourcesInEditor(this._outputResources));
                //Debug.Log("Update in editor");
            }
            var inConst = this._findEarliestConstraint(this._inputResources, this._availResForRemTime.Item1, int.MaxValue);
            var earliestConstraint = inConst.Item2;
            var lowestTakes = inConst.Item1;
            var lowestPercent = inConst.Item3;
            var outConst = this._findEarliestConstraint(this._outputResources, this._availResForRemTime.Item2, lowestTakes);
            if (outConst.Item1 < lowestTakes)
            {
                earliestConstraint = outConst.Item2;
                lowestTakes = outConst.Item1;
                lowestPercent = outConst.Item3;
            }
            this.RemTime = _convertRemainingToDisplayText(lowestTakes, lowestPercent, earliestConstraint);
        }

        private double _adjustRatio(double ratio, bool outres)
        {
            return ratio*(outres ? this._adjustmentRatios.OutputRatio : this._adjustmentRatios.InputRatio);
        }

        private static bool _canAllTakeDemand(IList<AvailableResourceInfo> availableRes, IDictionary<int, double> resTab, bool required)
        {
            for (var i = 0; i < availableRes.Count; i++)
            {
                var ari = availableRes[i];
                if (!ari.CanTakeDemand(resTab[ari.ResourceId]))
                {
                    return false;
                }
            }
            return !required || availableRes.Count > 0;
        }

        private static string _convertRemainingToDisplayText(double remainingSeconds, double remainingPercent, ConverterResource earliestConstraint)
        {
            string displayText;
            if (earliestConstraint == null)
            {
                displayText = "no limit";
                return displayText;
            }
            var days = remainingSeconds/21600;
            if (days > 1)
            {
                displayText = string.Format("{0:#0.#} days", days);
            }
            else
            {
                var timespan = TimeSpan.FromSeconds(remainingSeconds);
                displayText = string.Format("{0:D2}:{1:D2}:{2:D2}", timespan.Hours, timespan.Minutes, timespan.Seconds);
            }
            return displayText + string.Format(" ({0:P2})", remainingPercent);
        }

        private Dictionary<int, double> _createDemandLookupTable(IList<ConverterResource> convRes, double ratio)
        {
            var listCnt = convRes.Count;
            var lt = new Dictionary<int, double>(listCnt);
            for (var i = 0; i < listCnt; i++)
            {
                var cr = convRes[i];
                var finalRatio = cr.RatePerSecond*ratio;
                if (cr.ResourceName == ConvUtil.ElectricCharge)
                {
                    var ecMax = this._conversionRate*cr.RatePerSecond*this.MaxEcDelta;
                    finalRatio = Math.Min(ecMax, finalRatio);
                    //Debug.Log("[UC] ecMax=" + ecMax);
                }
                lt.Add(cr.ResourceId, finalRatio);
                //Debug.Log("[UC] " + cr.ResourceName + " ratio=" + finalRatio + " which is per second=" + finalRatio/ratio);
            }
            return lt;
        }

        private static ConfigNode _curveNodeFromString(string curveString)
        {
            var rules = curveString.Split(new[] {";"}, StringSplitOptions.RemoveEmptyEntries);
            var node = new ConfigNode("CURVE_DEF");
            foreach (var rule in rules)
            {
                node.AddValue("Rule", rule);
            }
            return node;
        }

        private static ConfigNode[] _defNodesFromString(string defString, bool output)
        {
            var nodes = new List<ConfigNode>();
            var defs = defString.Split(new[] {";"}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var def in defs)
            {
                var values = def.Split(new[] {":"}, StringSplitOptions.RemoveEmptyEntries);
                var node = new ConfigNode(output ? "OUTPUT_DEF" : "INPUT_DEF");
                node.AddValue("ResourceName", values[0]);
                node.AddValue("RatePerSecond", values[1]);
                node.AddValue("AllowOverflow", values[2]);
                nodes.Add(node);
            }
            return nodes.ToArray();
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

        private List<AvailableResourceInfo> _findAvailableResourcesInEditor(ICollection<ConverterResource> resList)
        {
            var availRes = new List<AvailableResourceInfo>(resList.Count);
            foreach (var converterResource in resList)
            {
                List<Part> parts;
                switch (converterResource.FlowMode)
                {
                    case ResourceFlowMode.NO_FLOW:
                    {
                        parts = new List<Part> {this.part};
                    }
                        break;
                    case ResourceFlowMode.ALL_VESSEL:
                    {
                        parts = EditorLogic.fetch.ship.Parts;
                    }
                        break;
                    case ResourceFlowMode.STAGE_PRIORITY_FLOW:
                    {
                        parts = this.part.FindPartsInSameStage(EditorLogic.fetch.ship.Parts, converterResource.OutputResource);
                    }
                        break;
                    case ResourceFlowMode.STACK_PRIORITY_SEARCH:
                    {
                        parts = this.part.FindPartsInSameResStack(EditorLogic.fetch.ship.Parts, new HashSet<Part>(), converterResource.OutputResource);
                    }
                        break;
                    default:
                    {
                        parts = new List<Part>();
                    }
                        break;
                }
                var cresource = converterResource;
                var partRes = parts.Where(p => p.Resources.Contains(cresource.ResourceId)).Select(p => p.Resources.Get(cresource.ResourceId)).ToList();
                var availAmount = 0d;
                var availSpace = 0d;
                foreach (var pr in partRes)
                {
                    availAmount += pr.amount;
                    availSpace += (pr.maxAmount - pr.amount);
                }
                var ar = new AvailableResourceInfo(cresource.ResourceId, cresource.OutputResource, availAmount, availSpace, cresource.AllowOverflow);
                availRes.Add(ar);
            }
            return availRes;
        }

        private Tuple<int, ConverterResource, double> _findEarliestConstraint(List<ConverterResource> conres, List<AvailableResourceInfo> availres, int lTakes)
        {
            ConverterResource earliestConstraint = null;
            var lowestTakes = lTakes;
            AvailableResourceInfo earliestConstraintInfo = null;
            for (var i = 0; i < conres.Count; i++)
            {
                var cr = conres[i];
                var ar = availres.Where(avr => avr.ResourceId == cr.ResourceId).Select(avr => avr).FirstOrDefault();
                if (ar != null)
                {
                    var takes = ar.TimesCanTakeDemand(cr.RatePerSecond);
                    if (takes < lowestTakes)
                    {
                        lowestTakes = takes;
                        earliestConstraint = cr;
                        earliestConstraintInfo = ar;
                    }
                }
            }
            var remPercent = earliestConstraintInfo != null ? (earliestConstraintInfo.OutputResource ? 1d - earliestConstraintInfo.PercentageFilled : earliestConstraintInfo.PercentageFilled) : 0d;
            return new Tuple<int, ConverterResource, double>(lowestTakes, earliestConstraint, remPercent);
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
            if (defNodes == null)
            {
                return ret;
            }
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

        private static string _stringifyCurveDef(ConfigNode curve)
        {
            var sb = new StringBuilder();
            var values = curve.GetValues("Rule");
            var l = values.Length;
            for (var i = 0; i < l; i++)
            {
                var val = values[i];
                sb.Append(val);
                if (i < (l - 1))
                {
                    sb.Append(";");
                }
            }
            return sb.ToString();
        }

        private static string _stringifyResDefs(IList<ConfigNode> defNodes, bool output)
        {
            var sb = new StringBuilder();
            var l = defNodes.Count;
            for (var i = 0; i < l; i++)
            {
                var configNode = defNodes[i];
                sb.Append(configNode.GetValue("ResourceName"));
                sb.Append(":");
                sb.Append(configNode.GetValue("RatePerSecond"));
                sb.Append(":");
                sb.Append(output ? configNode.GetValue("AllowOverflow") : "false");
                if (i < (l - 1))
                {
                    sb.Append(";");
                }
            }
            return sb.ToString();
        }

        private Tuple<bool, int, double, double> _transferResource(int resourceId, double demand, bool output)
        {
            if (output)
            {
                demand *= -1;
            }
            var actual = this.part.RequestResource(resourceId, demand);
            //Debug.Log("[UC] resid=" + resourceId + " demand=" + demand + " actual=" + actual);
            var diff = Math.Abs(actual - demand);
            var treshold = resourceId == this._electricChargeId ? 1d : ConvUtil.Epsilon;
            var success = diff < treshold;
            return new Tuple<bool, int, double, double>(success, resourceId, actual, diff);
        }

        private void _updateGui()
        {
            this.Fields["Status"].guiName = this.ConverterName;
            this.Events["ToggleConverter"].guiName = "Toggle " + this.ConverterName;
            this.Fields["RemTime"].guiName = this.ConverterName;
            if (HighLogic.LoadedSceneIsEditor)
            {
                this._setStatus(this.ConverterActive ? ConvStates.Active : ConvStates.Inactive);
            }
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