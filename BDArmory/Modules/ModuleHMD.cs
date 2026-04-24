using KSP.Localization;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using UnityEngine;

using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.Modules
{
    public class ModuleHMD : PartModule, IPartCostModifier
    {
        public float GetModuleCost(float defaultCost, ModifierStagingSituation situation)
        {
            return _HMDCost;
        }
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        [KSPField(isPersistant = true)]
        public bool HMD = false;

        private float _HMDCost = 0f;

        [KSPEvent(advancedTweakable = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_HMD_On", active = true)]//"Add HMD"
        public void ToggleHMD()
        {
            HMD = !HMD;
            if (!HMD)
            {
                Events["ToggleHMD"].guiName = StringUtils.Localize("#LOC_BDArmory_HMD_On");//"Add HMD"
                _HMDCost = 0;
            }
            else
            {
                Events["ToggleHMD"].guiName = StringUtils.Localize("#LOC_BDArmory_HMD_Off");//"Remove HMD"
                _HMDCost = BDArmorySettings.HMDCost * part.CrewCapacity;
            }
            GUIUtils.RefreshAssociatedWindows(part);
            using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                while (pSym.MoveNext())
                {
                    if (pSym.Current == null) continue;

                    var HMDSym = pSym.Current.FindModuleImplementing<ModuleHMD>();
                    if (HMDSym == null) continue;

                    HMDSym.HMD = HMD;

                    if (!HMD)
                    {
                        HMDSym.Events["ToggleHMD"].guiName = StringUtils.Localize("#LOC_BDArmory_HMD_On");//"Enable self-sealing tank"
                        HMDSym._HMDCost = 0;
                    }
                    else
                    {
                        HMDSym.Events["ToggleHMD"].guiName = StringUtils.Localize("#LOC_BDArmory_HMD_Off");//"Disable self-sealing tank"
                        HMDSym._HMDCost = BDArmorySettings.HMDCost * part.CrewCapacity;
                    }
                    GUIUtils.RefreshAssociatedWindows(pSym.Current);
                }
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        ModuleCommand cockpit;

        public void Start()
        {
            cockpit = part.FindModuleImplementing<ModuleCommand>();
            if (cockpit != null)
            {
                if (cockpit.minimumCrew >= 1)
                {
                    Events["ToggleHMD"].guiActiveEditor = true;
                    if (!HMD)
                    {
                        Events["ToggleHMD"].guiName = StringUtils.Localize("#LOC_BDArmory_HMD_On");//"Add HMD"
                    }
                    else
                    {
                        Events["ToggleHMD"].guiName = StringUtils.Localize("#LOC_BDArmory_HMD_Off");//"Remove HMD"
                        _HMDCost = BDArmorySettings.HMDCost * part.CrewCapacity;
                    }
                }
                else part.RemoveModule(this); //don't assign to drone cores
            }
            GUIUtils.RefreshAssociatedWindows(part);
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (cockpit == null || !HMD) part.RemoveModule(this); //No cockpit or cockpit with no HMD
            }
        }

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine($" Can outfit part with HMD."); //localize this at some point, future me
            return output.ToString();
        }

        public bool HasCrew()
        {
            // For now just look at enclosed cockpits
            return part.protoModuleCrew.Count > 0;
        }
    }
}
