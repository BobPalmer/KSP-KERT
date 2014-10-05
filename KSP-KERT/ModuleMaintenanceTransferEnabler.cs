using System.Linq;
using CIT_Util;

namespace KERT
{
    public class ModuleMaintenanceTransferEnabler : PartModule
    {
        private const int WaitInterval = 30;
        private const string EventName = "ToggleState";
        [KSPField(isPersistant = false)] public bool ConnectedPartsOnly = true;
        [KSPField(guiActive = true, guiName = "Maint. Transfer Active", isPersistant = false)] public bool MaintenanceTransferActive = false;
        [KSPField(isPersistant = false)] public float MaxDistance = 2.5f;
        [KSPField(isPersistant = false)] public float MaxMass = float.MaxValue;
        [KSPField(isPersistant = false)] public int MaxParts = int.MaxValue;
        private int _waitCounter = WaitInterval;

        internal bool TooHeavy
        {
            get { return this.part.vessel.Parts.Sum(p => p.mass) > this.MaxMass; }
        }

        internal bool TooManyParts
        {
            get { return this.part.vessel.Parts.Count > this.MaxParts; }
        }

        [KSPEvent(name = EventName, guiName = "Toggle Maint. Transfer", guiActive = true, active = true, unfocusedRange = 15f)]
        public void Toggle()
        {
            if (this.TooManyParts)
            {
                OSD.PostMessageUpperCenter("Vessel has too many parts!");
                return;
            }
            if (this.TooHeavy)
            {
                OSD.PostMessageUpperCenter("Vessel is too heavy!");
                return;
            }
            this.MaintenanceTransferActive = !this.MaintenanceTransferActive;
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }
            if (this._waitCounter > 0)
            {
                this._waitCounter--;
            }
            this._waitCounter = WaitInterval;
            if (this.ConnectedPartsOnly)
            {
                return;
            }
            var ev = this.Events[EventName];
            if (this.TooManyParts || this.TooHeavy)
            {
                ev.active = ev.guiActive = false;
            }
            else
            {
                ev.active = ev.guiActive = true;
            }
        }
    }
}