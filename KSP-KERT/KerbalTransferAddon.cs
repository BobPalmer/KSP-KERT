using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CIT_Util;
using CIT_Util.Types;
using UnityEngine;

namespace KERT
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalTransferAddon : MonoBehaviour
    {
        private const string ModuleName = "ModuleMaintenanceResourceTransfer";
        private const float MaxTransferDistance = 2.5f;
        private const int WaitInterval = 30;
        private const int WaitStepSelectedPart = WaitInterval/5;
        private const string MaintenanceGuiTitle = "Maintenance Resource Transfer";
        private const float MinGuiWidth = 350;
        internal Part SelectedPart;
        private bool _inputLockSet;
        //private double _lastDistanceDisplayedTime;
        private bool _showGui;
        private float _transferRate;
        private List<Transfer> _transfers;
        private int _waitCounter;
        private Rect _windowPos = new Rect(700, 500, MinGuiWidth, 40);
        private double LastDistance { get; set; }

        public void Awake()
        {
            GameEvents.onPartActionUICreate.Add(this.HandleActionMenuOpened);
            GameEvents.onPartActionUIDismiss.Add(this.HandleActionMenuClosed);
            GameEvents.onVesselChange.Add(this.HandleVesselChange);
            this._transfers = new List<Transfer>();
            this._transferRate = 1f;
            this._waitCounter = WaitInterval;
            //this._lastDistanceDisplayedTime = 0d;
        }

        private static double DistanceBetweenLocalPartAndTargetPart(Part targetPart, Part localPart)
        {
            return Vector3.Distance(localPart.transform.position, targetPart.transform.position);
        }

        public void FixedUpdate()
        {
            if (this._waitCounter > 0 && this._transfers.Count == 0 && !_showGui)
            {
                if (this.SelectedPart != null)
                {
                    this._waitCounter -= WaitStepSelectedPart;
                }
                else
                {
                    this._waitCounter--;
                }
                return;
            }
            this._waitCounter = WaitInterval;
            var activeVessel = FlightGlobals.ActiveVessel;
            var info = _checkVessel(activeVessel);
            if (!info.Item1)
            {
                if (this.SelectedPart != null)
                {
                    this._cleanSelectedPart();
                }
                this._showGui = false;
                return;
            }
            if (this.SelectedPart != null)
            {
                var maxDist = info.Item2 == OperationMode.Maintenance ? info.Item3.MaxDistance : MaxTransferDistance;
                //if (!activeVessel.isEVA && info.Item3.MaintenanceTransferActive && this.LastDistance < maxDist)
                //{
                //    this.ShowGui();
                //}
                if (!this._showGui && this._transfers.Count > 0)
                {
                    this._transfers.Clear();
                    _showTransferAbort();
                }
                var locPart = activeVessel.rootPart;
                if (info.Item2 == OperationMode.Maintenance)
                {
                    locPart = info.Item3.part;
                }
                this.LastDistance = DistanceBetweenLocalPartAndTargetPart(this.SelectedPart, locPart);
                var module = this.SelectedPart.FindModuleImplementing<ModuleMaintenanceResourceTransfer>();
                if (module != null)
                {
                    module.UpdateDistance(this.LastDistance, maxDist);
                }
                if (this.LastDistance > maxDist)
                {
                    this._transfers.Clear();
                    if (this._showGui)
                    {
                        this._showGui = false;
                        _showTransferAbort();
                    }
                    //if (info.Item2 == OperationMode.Maintenance)
                    //{
                    //    var now = Planetarium.GetUniversalTime();
                    //    if (now - this._lastDistanceDisplayedTime > 0.1f)
                    //    {
                    //        this._lastDistanceDisplayedTime = now;
                    //        OSD.PostMessageLowerRightCorner(string.Format("[KERT] Distance: {0:F2}", this.LastDistance), 0.1f);
                    //    }
                    //}
                }
                this._setPartHighlighting(true);
                var commonResources = GetCommonResources(activeVessel, this.SelectedPart, info.Item2, info.Item3);
                foreach (var transfer in this._transfers)
                {
                    var cRes = commonResources.FirstOrDefault(cr => cr.TargetPartResource.resourceName == transfer.ResourceName);
                    if (cRes != null)
                    {
                        var demand = this._transferRate*TimeWarp.fixedDeltaTime;
                        if (transfer.FromLocal)
                        {
                            var availableRes = GetOrderedPartResource(cRes.LocalPartResources, activeVessel, false, true, info.Item2, info.Item3);
                            var availableSpace = cRes.TargetPartResource.maxAmount - cRes.TargetPartResource.amount;
                            if (availableRes != null && availableSpace > 0)
                            {
                                var toTransfer = Math.Min(Math.Min(availableRes.amount, demand), availableSpace);
                                availableRes.amount -= toTransfer;
                                cRes.TargetPartResource.amount += toTransfer;
                            }
                        }
                        else
                        {
                            var availableRes = cRes.TargetPartResource.amount;
                            var availableSpace = GetOrderedPartResource(cRes.LocalPartResources, activeVessel, true, false, info.Item2, info.Item3);
                            if (availableRes > 0 && availableSpace != null)
                            {
                                var toTransfer = Math.Min(Math.Min(availableRes, demand), (availableSpace.maxAmount - availableSpace.amount));
                                cRes.TargetPartResource.amount -= toTransfer;
                                availableSpace.amount += toTransfer;
                            }
                        }
                    }
                }
            }
        }

        private static IEnumerable<PartResource> GetAllPartResourcesOfParts(IEnumerable<Part> parts)
        {
            var resList = new List<PartResource>();
            foreach (var part in parts)
            {
                resList.AddRange(part.Resources.OfType<PartResource>());
            }
            return resList;
        }

        private static IEnumerable<PartResource> GetAllPartResourcesOfVessel(Vessel evaVessel)
        {
            return GetAllPartResourcesOfParts(evaVessel.Parts);
        }

        private static List<CommonResource> GetCommonResources(Vessel vessel, Part part, OperationMode opMode, ModuleMaintenanceTransferEnabler module = null)
        {
            var resList = new List<CommonResource>();
            switch (opMode)
            {
                case OperationMode.Kerbal:
                {
                    resList = (from resource in part.Resources.OfType<PartResource>()
                               let kerbalResList = GetAllPartResourcesOfVessel(vessel).Where(kerbalResource => kerbalResource.resourceName == resource.resourceName).ToList()
                               where kerbalResList.Count > 0
                               select new CommonResource(resource, kerbalResList)).ToList();
                }
                    break;
                case OperationMode.Maintenance:
                {
                    if (module != null)
                    {
                        if (module.ConnectedPartsOnly)
                        {
                            var parts = new HashSet<Part> {module.part};
                            if (module.part.parent != null)
                            {
                                parts.Add(module.part.parent);
                            }
                            foreach (var child in module.part.children)
                            {
                                parts.Add(child);
                            }
                            foreach (var attachNode in module.part.attachNodes.Where(an => an.attachedPart != null))
                            {
                                parts.Add(attachNode.attachedPart);
                            }
                            resList = (from resource in part.Resources.OfType<PartResource>()
                                       let localResList = GetAllPartResourcesOfParts(parts).Where(r => r.resourceName == resource.resourceName).ToList()
                                       where localResList.Count > 0
                                       select new CommonResource(resource, localResList)).ToList();
                        }
                        else
                        {
                            resList = (from resource in part.Resources.OfType<PartResource>()
                                       let localResList = GetAllPartResourcesOfVessel(vessel).Where(r => r.resourceName == resource.resourceName).ToList()
                                       where localResList.Count > 0
                                       select new CommonResource(resource, localResList)).ToList();
                        }
                    }
                }
                    break;
            }

            return resList;
        }

        private static PartResource GetOrderedPartResource(IEnumerable<PartResource> localResources, Vessel activeVessel, bool rootFirst, bool transferOut, OperationMode opMode, ModuleMaintenanceTransferEnabler module)
        {
            const double treshold = 0.001d;
            var rootPart = (opMode == OperationMode.Kerbal) ? activeVessel.rootPart : module.part;
            if (transferOut)
            {
                var allWithAmount = localResources.Where(r => r.amount > treshold).ToList();
                if (allWithAmount.Count > 1)
                {
                    if (rootFirst)
                    {
                        foreach (var partResource in allWithAmount)
                        {
                            if (partResource.part == rootPart)
                            {
                                return partResource;
                            }
                        }
                    }
                    else
                    {
                        foreach (var partResource in allWithAmount)
                        {
                            if (partResource.part != rootPart)
                            {
                                return partResource;
                            }
                        }
                    }
                }
                return allWithAmount.Count >= 1 ? allWithAmount[0] : null;
            }
            var allWithSpace = localResources.Where(r => (r.maxAmount - r.amount) > treshold).ToList();
            if (allWithSpace.Count > 1)
            {
                if (rootFirst)
                {
                    foreach (var partResource in allWithSpace)
                    {
                        if (partResource.part == rootPart)
                        {
                            return partResource;
                        }
                    }
                }
                else
                {
                    foreach (var partResource in allWithSpace)
                    {
                        if (partResource.part != rootPart)
                        {
                            return partResource;
                        }
                    }
                }
            }
            return allWithSpace.Count >= 1 ? allWithSpace[0] : null;
        }

        private void HandleActionMenuClosed(Part data)
        {
            this._removeModuleFromPart();
            if (!this._showGui)
            {
                this._cleanSelectedPart();
            }
        }

        internal void ActionMenuOpened(Part part, bool userAction = false)
        {
            var activeVessel = FlightGlobals.ActiveVessel;
            if (part.vessel.isEVA || part.vessel == activeVessel)
            {
                return;
            }
            var vesselInfo = _checkVessel(activeVessel, userAction);
            if (!vesselInfo.Item1)
            {
                return;
            }
            var module = vesselInfo.Item3;
            var opMode = vesselInfo.Item2;
            var commRes = GetCommonResources(activeVessel, part, opMode, module);
            if (commRes.Count > 0)
            {
                if (this.SelectedPart != null)
                {
                    this._cleanSelectedPart();
                }
                this.SelectedPart = part;
                this._addModuleToPart();
            }
        }

        private void HandleActionMenuOpened(Part data)
        {
            this.ActionMenuOpened(data);
        }

        private void HandleVesselChange(Vessel data)
        {
            this._cleanSelectedPart();
            this._showGui = false;
            this.SelectedPart = null;
        }

        private IEnumerator InjectAddonRefIntoModule()
        {
            var run = true;
            while (run)
            {
                if (this.SelectedPart != null)
                {
                    if (this.SelectedPart.Modules.Contains(ModuleName))
                    {
                        var module = this._getModuleFromPart();
                        if (module != null)
                        {
                            module.Addon = this;
                            run = false;
                        }
                    }
                    else
                    {
                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    run = false;
                }
            }
        }

        public void OnDestroy()
        {
            GameEvents.onPartActionUICreate.Remove(this.HandleActionMenuOpened);
            GameEvents.onPartActionUIDismiss.Remove(this.HandleActionMenuClosed);
            GameEvents.onVesselChange.Remove(this.HandleVesselChange);
            this._cleanSelectedPart();
            this.SelectedPart = null;
        }

        public void OnGUI()
        {           
            const string inputLock = "KERT_Lock";
            if (!this._showGui && this._inputLockSet)
            {
                InputLockManager.RemoveControlLock(inputLock);
                this._inputLockSet = false;
            }
            if (!this._showGui)
            {
                return;
            }
            this._windowPos = GUILayout.Window(this.GetType().FullName.GetHashCode(), this._windowPos, this.WindowGui, MaintenanceGuiTitle, GUILayout.Width(200), GUILayout.Height(20));
            if (this._windowPos.IsMouseOverRect())
            {
                InputLockManager.SetControlLock(GlobalConst.GUIWindowLockMask, inputLock);
                this._inputLockSet = true;
            }
            else if (this._inputLockSet)
            {
                InputLockManager.RemoveControlLock(inputLock);
                this._inputLockSet = false;
            }
        }

        internal void ShowGui()
        {
            this._showGui = true;
        }

        protected void WindowGui(int windowID)
        {
            const int spacing = 3;
            const int bigSpacing = 7;
            var activeVessel = FlightGlobals.ActiveVessel;
            var info = _checkVessel(activeVessel);
            if (!info.Item1)
            {
                return;
            }
            var maxDist = info.Item2 == OperationMode.Maintenance ? info.Item3.MaxDistance : MaxTransferDistance;
            var inOutWidth = GUILayout.Width(40f);
            var doubleInOutWidth = GUILayout.Width(80f);
            var expandWidth = GUILayout.ExpandWidth(true);
            var commRes = GetCommonResources(activeVessel, this.SelectedPart, info.Item2, info.Item3);
            var kerbalDistance = string.Format("Curr. Distance: {0:0.00}/{1:0.00}", this.LastDistance, maxDist);
            GUILayout.BeginVertical();
            GUILayout.Label("Active Part: " + this.SelectedPart.name, expandWidth);
            GUILayout.Box("", new[] {GUILayout.MinWidth(MinGuiWidth), GUILayout.ExpandWidth(true), GUILayout.Height(0.5f)});
            GUILayout.Space(spacing);
            GUILayout.Label("Resources:");
            GUILayout.Space(spacing);
            foreach (var commonResource in commRes)
            {
                var localSums = new[] {commonResource.LocalPartResources.Sum(epr => epr.amount), commonResource.LocalPartResources.Sum(epr => epr.maxAmount)};
                var localAmounts = string.Format("Local: {0:0.00}/{1:0.00} ({2:P2})", localSums[0], localSums[1], localSums[0]/localSums[1]);
                var partAmounts = string.Format("Part: {0:0.00}/{1:0.00} ({2:P2})", commonResource.TargetPartResource.amount, commonResource.TargetPartResource.maxAmount,
                                                commonResource.TargetPartResource.amount/commonResource.TargetPartResource.maxAmount);
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(commonResource.TargetPartResource.resourceName, expandWidth);
                GUILayout.Space(bigSpacing);
                if (this._transfers.Any(t => t.ResourceName == commonResource.TargetPartResource.resourceName))
                {
                    var transfer = this._transfers.FirstOrDefault(t => t.ResourceName == commonResource.TargetPartResource.resourceName);
                    if (transfer != null)
                    {
                        var del = false;
                        if (transfer.FromLocal)
                        {
                            if (GUILayout.Button("Stop OUT", doubleInOutWidth))
                            {
                                del = true;
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("Stop IN", doubleInOutWidth))
                            {
                                del = true;
                            }
                        }
                        if (del)
                        {
                            this._transfers.Remove(transfer);
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button("IN", inOutWidth))
                    {
                        this._transfers.Add(new Transfer(commonResource.TargetPartResource.resourceName, false));
                    }
                    GUILayout.Space(spacing);
                    if (GUILayout.Button("OUT", inOutWidth))
                    {
                        this._transfers.Add(new Transfer(commonResource.TargetPartResource.resourceName, true));
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(spacing);
                GUILayout.BeginHorizontal();
                GUILayout.Label(localAmounts, expandWidth);
                GUILayout.Space(spacing);
                GUILayout.Label("|");
                GUILayout.Space(spacing);
                GUILayout.Label(partAmounts, expandWidth);
                GUILayout.EndHorizontal();
                //GUILayout.Box("", new[] {GUILayout.ExpandWidth(true), GUILayout.Height(1)});
                GUILayout.Space(bigSpacing);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Transfer Rate: " + this._transferRate, expandWidth);
            GUILayout.Space(bigSpacing);
            if (GUILayout.Button("0.1", inOutWidth))
            {
                this._transferRate = 0.1f;
            }
            if (GUILayout.Button("1", inOutWidth))
            {
                this._transferRate = 1f;
            }
            if (GUILayout.Button("10", inOutWidth))
            {
                this._transferRate = 10f;
            }
            if (GUILayout.Button("100", inOutWidth))
            {
                this._transferRate = 100f;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(bigSpacing);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close", doubleInOutWidth))
            {
                this._showGui = false;
                this._cleanSelectedPart();
            }
            GUILayout.Space(bigSpacing);
            GUILayout.Label(kerbalDistance, expandWidth);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void _addModuleToPart()
        {
            if (this.SelectedPart != null)
            {
                if (!this.SelectedPart.Modules.Contains(ModuleName))
                {
                    this.SelectedPart.AddModule(ModuleName);
                    this.StartCoroutine(this.InjectAddonRefIntoModule());
                }
            }
        }

        private static Tuple<bool, OperationMode, ModuleMaintenanceTransferEnabler> _checkVessel(Vessel vessel, bool userAction = false)
        {
            if (vessel.isEVA)
            {
                return new Tuple<bool, OperationMode, ModuleMaintenanceTransferEnabler>(true, OperationMode.Kerbal, null);
            }
            var module = _findModule(vessel);
            if (module != null && _checkVesselForTransfer(module, userAction))
            {
                return new Tuple<bool, OperationMode, ModuleMaintenanceTransferEnabler>(true, OperationMode.Maintenance, module);
            }
            return new Tuple<bool, OperationMode, ModuleMaintenanceTransferEnabler>(false, OperationMode.Maintenance, null);
        }

        private static bool _checkVesselForTransfer(ModuleMaintenanceTransferEnabler module, bool userAction)
        {
            var ok = true;
            var msg = string.Empty;
            if (!module.MaintenanceTransferActive)
            {
                msg = "No Maintenance-Transfer-Module active!";
                ok = false;
            }
            else if (!module.ConnectedPartsOnly)
            {
                if (module.TooManyParts)
                {
                    ok = false;
                    msg = "Maintenance vessel has too many parts!";
                }
                else if (module.TooHeavy)
                {
                    ok = false;
                    msg = "Maintenance vessel is too heavy!";
                }
            }
            if (!ok && userAction)
            {
                OSD.PostMessageUpperCenter("[KERT] " + msg, 2f);
            }
            return ok;
        }

        private void _cleanSelectedPart()
        {
            this._setPartHighlighting(false);
            this.SelectedPart = null;
        }

        private static ModuleMaintenanceTransferEnabler _findModule(Vessel vessel)
        {
            return (from p in vessel.Parts
                    let m = p.FindModuleImplementing<ModuleMaintenanceTransferEnabler>()
                    where m != null && m.MaintenanceTransferActive
                    select m).FirstOrDefault();
        }

        private ModuleMaintenanceResourceTransfer _getModuleFromPart()
        {
            if (this.SelectedPart != null)
            {
                if (this.SelectedPart.Modules.Contains(ModuleName))
                {
                    var moduleObj = this.SelectedPart.Modules[ModuleName];
                    if (moduleObj != null)
                    {
                        var module = moduleObj as ModuleMaintenanceResourceTransfer;
                        if (module != null)
                        {
                            return module;
                        }
                    }
                }
            }
            return null;
        }

        private void _removeModuleFromPart()
        {
            var module = this._getModuleFromPart();
            if (module != null && this.SelectedPart != null)
            {
                this.SelectedPart.RemoveModule(module);
            }
        }

        private void _setPartHighlighting(bool highlight)
        {
            if (this.SelectedPart != null)
            {
                if (highlight)
                {
                    this.SelectedPart.SetHighlightColor(Color.green);
                    this.SelectedPart.SetHighlight(true);
                }
                else
                {
                    this.SelectedPart.SetHighlightDefault();
                }
            }
        }

        private static void _showTransferAbort()
        {
            ScreenMessages.PostScreenMessage("Too far away, transfer aborted.", 3f, ScreenMessageStyle.UPPER_CENTER);
        }

        private class CommonResource
        {
            internal List<PartResource> LocalPartResources { get; private set; }
            internal PartResource TargetPartResource { get; private set; }

            public CommonResource(PartResource targetResource, List<PartResource> localResList)
            {
                this.TargetPartResource = targetResource;
                this.LocalPartResources = localResList;
            }
        }

        private enum OperationMode
        {
            Kerbal,
            Maintenance
        }

        private class Transfer
        {
            internal bool FromLocal { get; private set; }
            internal string ResourceName { get; private set; }

            internal Transfer(string resName, bool fromLocal)
            {
                this.ResourceName = resName;
                this.FromLocal = fromLocal;
            }
        }
    }

    public class ModuleMaintenanceResourceTransfer : PartModule
    {
        private const string OpenTransferGui = "Open Transfer GUI";
        private const string OpenGUI = "OpenGui";
        private bool _distanceOk;
        internal KerbalTransferAddon Addon { get; set; }

        [KSPEvent(name = OpenGUI, guiName = OpenTransferGui, guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = false, active = true, unfocusedRange = 15f)]
        public void OpenGui()
        {
            if (this.Addon != null)
            {
                if (this._distanceOk)
                {
                    if (!this.Addon.SelectedPart == this.part)
                    {
                        this.Addon.ActionMenuOpened(this.part, true);
                    }
                    this.Addon.ShowGui();
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Too far away!", 3f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        internal void UpdateDistance(double distance, double maxDistance)
        {
            this.Events[OpenGUI].guiName = OpenTransferGui + string.Format(" ({0:0.0}/{1:0.0})", distance, maxDistance);
            this._distanceOk = distance <= maxDistance;
        }
    }
}