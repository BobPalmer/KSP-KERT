using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoreTransfer
{
    public class ModuleNoFlowTransfer : PartModule
    {
        private const string ModuleName = "ModuleNoFlowTransfer";
        internal Guid Id;
        [KSPField] public string ResourceName;
        private bool _initialized;
        private bool _readyToTransfer;
        private int _resourceFlow;
        private bool _transferOut;
        private ModuleNoFlowTransfer _transferPartner;

        internal bool ReadyToTransfer
        {
            get { return (this.ReadyToTransferIn || this.ReadyToTransferOut) && (this._transferPartner != null); }
        }

        internal bool ReadyToTransferIn
        {
            get { return this._readyToTransfer && !this._transferOut; }
        }

        internal bool ReadyToTransferOut
        {
            get { return this._readyToTransfer && this._transferOut; }
        }

        [KSPEvent(name = "AbortTransfer", guiName = "Abort Transfer")]
        public void AbortTransfer()
        {
            this.Reset();
            GetAllOtherModules(this).ForEach(m => m.Reset());
        }

        private static List<ModuleNoFlowTransfer> GetAllOtherModules(ModuleNoFlowTransfer excludeModule)
        {
            if (excludeModule.part == null || excludeModule.part.vessel == null)
            {
                Debug.Log("[NoFlowTransfer] unable to get vessel parts");
                return new List<ModuleNoFlowTransfer>();
            }
            var modules = excludeModule.part.vessel.Parts
                                       .Where(p => p.Modules.Contains(ModuleName))
                                       .Select(p => p.Modules[ModuleName] as ModuleNoFlowTransfer);
            return modules.Where(m => m.Id != excludeModule.Id && m.ResourceName == excludeModule.ResourceName).ToList();
        }

        private static string GetDisplayString(string resName, int length)
        {
            if (resName.Length <= length)
            {
                return resName;
            }
            var arr = resName.Take(length - 1).ToArray();
            var arr2 = new char[length];
            arr.CopyTo(arr2, 0);
            arr2[length - 1] = '.';
            return new string(arr2);
        }

        internal void HighlightAsTargetable()
        {
            this.part.SetHighlightColor(Color.blue);
            this.part.SetHighlight(true);
        }

        public override void OnFixedUpdate()
        {
            if (!this._initialized || !this.ReadyToTransfer)
            {
                return;
            }
            this.part.SetHighlight(true);
            if (!this.ReadyToTransferOut)
            {
                return;
            }
            var localRes = this.part.Resources[this.ResourceName];
            var partnerRes = this._transferPartner.part.Resources[this.ResourceName];
            var availableRes = localRes.amount;
            var availableSpace = partnerRes.maxAmount - partnerRes.amount;
            var maxTransfer = Math.Min(availableRes, availableSpace);
            var deltaDemand = this._resourceFlow*TimeWarp.fixedDeltaTime;
            var toTransfer = maxTransfer > deltaDemand ? deltaDemand : maxTransfer;
            localRes.amount -= toTransfer;
            partnerRes.amount += toTransfer;
            Debug.Log("transferring " + toTransfer);
        }

        public override void OnStart(StartState state)
        {
            this.Id = Guid.NewGuid();
            this._transferOut = true;
            this._readyToTransfer = false;
            this._initialized = true;
            if (string.IsNullOrEmpty(this.ResourceName))
            {
                this._initialized = false;
                Debug.Log("[NoFlowTransfer] unable to start, resource name empty");
                return;
            }
            this.part.force_activate();
            this._resourceFlow = 1;
            var displayResString = GetDisplayString(this.ResourceName, 10);
            var rateEvent = this.Events["ToggleTransferRate"];
            rateEvent.guiName = displayResString + " rate = " + this._resourceFlow;
            rateEvent.active = rateEvent.guiActive = true;
            this.Events["AbortTransfer"].guiName = "Abort " + displayResString + " T.";
            this.Events["TransferIn"].guiName = "Transf. IN " + displayResString;
            this.Events["TransferOut"].guiName = "Transf. OUT " + displayResString;
            this.UpdateGui(false);
        }

        private static void RemoveModuleFromList(List<ModuleNoFlowTransfer> list, ModuleNoFlowTransfer module)
        {
            var remModule = list.FirstOrDefault(m => m.Id == module.Id);
            if (remModule != null)
            {
                list.Remove(remModule);
            }
        }

        internal void Reset()
        {
            this.part.SetHighlightDefault();
            this._readyToTransfer = false;
            this._transferPartner = null;
            this.UpdateGui(false);
        }

        internal void SetTransferPartner(ModuleNoFlowTransfer partnerModule)
        {
            this._transferPartner = partnerModule;
            if (this._transferOut)
            {
                this._highlightAsTargeter();
            }
            else
            {
                this._highlightAsTarget();
            }
        }

        [KSPEvent(name = "ToggleTransferRate", guiName = "ToggleTransferRate")]
        public void ToggleTransferRate()
        {
            switch (this._resourceFlow)
            {
                case 1:
                    this._resourceFlow = 10;
                    break;
                case 10:
                    this._resourceFlow = 100;
                    break;
                default:
                    this._resourceFlow = 1;
                    break;
            }
            this.Events["ToggleTransferRate"].guiName = GetDisplayString(this.ResourceName, 10) + " rate = " + this._resourceFlow;
        }

        [KSPEvent(name = "TransferIn", guiName = "Transfer In")]
        public void TransferIn()
        {
            if (!this._initialized)
            {
                return;
            }
            this.UpdateGui(true);
            this._transferOut = false;
            this._readyToTransfer = true;
            var otherModules = GetAllOtherModules(this);
            if (otherModules.Any(m => m.ReadyToTransferOut))
            {
                var targeterModule = otherModules.First(m => m.ReadyToTransferOut);
                RemoveModuleFromList(otherModules, targeterModule);
                otherModules.ForEach(m => m.Reset());
                this.SetTransferPartner(targeterModule);
                targeterModule.SetTransferPartner(this);
                return;
            }
            foreach (var moduleNoFlowTransfer in otherModules)
            {
                moduleNoFlowTransfer.Reset();
                moduleNoFlowTransfer.HighlightAsTargetable();
            }
            ScreenMessages.PostScreenMessage("Choose another part to transfer from.");
        }

        [KSPEvent(name = "TransferOut", guiName = "Transfer Out")]
        public void TransferOut()
        {
            if (!this._initialized)
            {
                return;
            }
            this.UpdateGui(true);
            this._transferOut = true;
            this._readyToTransfer = true;
            var otherModules = GetAllOtherModules(this);
            if (otherModules.Any(m => m.ReadyToTransferIn))
            {
                var targetModule = otherModules.First(m => m.ReadyToTransferIn);
                RemoveModuleFromList(otherModules, targetModule);
                otherModules.ForEach(m => m.Reset());
                this.SetTransferPartner(targetModule);
                targetModule.SetTransferPartner(this);
                return;
            }
            foreach (var moduleNoFlowTransfer in otherModules)
            {
                moduleNoFlowTransfer.Reset();
                moduleNoFlowTransfer.HighlightAsTargetable();
            }
            ScreenMessages.PostScreenMessage("Choose another part to transfer to.");
        }

        private void UpdateGui(bool showAbort)
        {
            var abortEvent = this.Events["AbortTransfer"];
            var inEvent = this.Events["TransferIn"];
            var outEvent = this.Events["TransferOut"];
            if (showAbort)
            {
                abortEvent.active = (abortEvent.guiActive) = true;
                inEvent.active = inEvent.guiActive = outEvent.active = outEvent.guiActive = false;
                return;
            }
            abortEvent.active = abortEvent.guiActive = false;
            inEvent.active = inEvent.guiActive = outEvent.active = outEvent.guiActive = true;
        }

        private void _highlightAsTarget()
        {
            this.part.SetHighlightColor(Color.green);
            this.part.SetHighlight(true);
        }

        private void _highlightAsTargeter()
        {
            this.part.SetHighlightColor(Color.red);
            this.part.SetHighlight(true);
        }
    }
}