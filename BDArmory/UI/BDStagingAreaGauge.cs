﻿using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;

using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    public class BDStagingAreaGauge : PartModule
    {
        public string AmmoName = "";

        //UI gauges(next to staging icon)
        private ProtoStageIconInfo heatGauge;
        private ProtoStageIconInfo emptyGauge;
        private ProtoStageIconInfo ammoGauge;
        private ProtoStageIconInfo cmGauge;
        private ProtoStageIconInfo reloadBar;

        void Start()
        {
            GameEvents.onVesselSwitching.Add(ReloadIconOnVesselSwitch);
            part.stagingIconAlwaysShown = true;
            part.stackIconGrouping = StackIconGrouping.SAME_TYPE;
            ForceRedraw();
        }

        void OnDestroy()
        {
            GameEvents.onVesselSwitching.Remove(ReloadIconOnVesselSwitch);
        }

        private void ReloadIconOnVesselSwitch(Vessel data0, Vessel data1)
        {
            if (part != null && part.vessel != null && part.vessel.isActiveVessel)
            {
                ForceRedraw();
            }
        }

        public void UpdateAmmoMeter(float ammoLevel)
        {
            if (BDArmorySettings.SHOW_AMMO_GAUGES && !BDArmorySettings.INFINITE_AMMO)
            {
                if (ammoLevel > 0)
                {
                    if (emptyGauge != null)
                    {
                        ForceRedraw();
                    }
                    if (ammoGauge == null)
                    {
                        ammoGauge = InitAmmoGauge(AmmoName);
                    }
                    ammoGauge?.SetValue(ammoLevel, 0, 1);
                }
                else
                {
                    if (ammoGauge != null)
                    {
                        ForceRedraw();
                    }
                    if (emptyGauge == null)
                    {
                        emptyGauge = InitEmptyGauge(StringUtils.Localize("#LOC_BDArmory_ProtoStageIconInfo_AmmoOut"));
                        emptyGauge?.SetValue(1, 0, 1);
                    }
                }
            }
            else if (ammoGauge != null || emptyGauge != null)
            {
                ForceRedraw();
            }
        }

        public void UpdateCMMeter(float cmLevel)
        {
            if (BDArmorySettings.SHOW_AMMO_GAUGES && !BDArmorySettings.INFINITE_COUNTERMEASURES)
            {
                if (cmLevel > 0)
                {
                    if (emptyGauge != null)
                    {
                        ForceRedraw();
                    }
                    if (cmGauge == null)
                    {
                        cmGauge = InitCMGauge(AmmoName);
                    }
                    cmGauge?.SetValue(cmLevel, 0, 1);
                }
                else
                {
                    if (cmGauge != null)
                    {
                        ForceRedraw();
                    }
                    if (emptyGauge == null)
                    {
                        emptyGauge = InitEmptyGauge(StringUtils.Localize("#LOC_BDArmory_ProtoStageIconInfo_CMsOut"));
                        emptyGauge?.SetValue(1, 0, 1);
                    }
                }
            }
            else if (cmGauge != null || emptyGauge != null)
            {
                ForceRedraw();
            }
        }

        /// <param name="heatLevel">0 is no heat, 1 is max heat</param>
        public void UpdateHeatMeter(float heatLevel)
        {
            //heat
            if (heatLevel > (1f / 3))
            {
                if (heatGauge == null)
                {
                    heatGauge = InitHeatGauge();
                }
                heatGauge?.SetValue((heatLevel * 3 - 1) / 2, 0, 1);    //null check
            }
            else if (heatGauge != null && heatLevel < 0.25f)
            {
                ForceRedraw();
            }
        }

        /// <param name="reloadProgress">0 is just fired, 1 is just reloaded</param>
        public void UpdateReloadMeter(float reloadRemaining)
        {
            if (reloadRemaining < 1)
            {
                if (reloadBar == null)
                {
                    reloadBar = InitReloadBar();
                }
                reloadBar?.SetValue(reloadRemaining, 0, 1);
            }
            else if (reloadBar != null)
            {
                ForceRedraw();
            }
        }

        private void ForceRedraw()
        {
            part.stackIcon.ClearInfoBoxes();
            //null everything so other gauges will properly re-initialize post ClearinfoBoxes()
            ammoGauge = null;
            cmGauge = null;
            heatGauge = null;
            reloadBar = null;
            emptyGauge = null;
        }

        private void EnsureStagingIcon()
        {
            // Fallback icon in case no icon is set for the part
            if (string.IsNullOrEmpty(part.stagingIcon))
            {
                part.stagingIcon = "SOLID_BOOSTER";
                part.stackIcon.CreateIcon();
                part.stagingIconAlwaysShown = true;
                part.stackIconGrouping = StackIconGrouping.SAME_TYPE;
                ForceRedraw();
            }
        }

        private ProtoStageIconInfo InitReloadBar()
        {
            EnsureStagingIcon();
            ProtoStageIconInfo v = part.stackIcon.DisplayInfo();
            if (v == null)
                return v;
            v.SetMsgBgColor(XKCDColors.DarkGrey);
            v.SetMsgTextColor(XKCDColors.White);
            v.SetMessage(StringUtils.Localize("#LOC_BDArmory_ProtoStageIconInfo_Reloading"));//"Reloading"
            v.SetProgressBarBgColor(XKCDColors.DarkGrey);
            v.SetProgressBarColor(XKCDColors.Silver);

            return v;
        }

        private ProtoStageIconInfo InitHeatGauge() //thanks DYJ
        {
            EnsureStagingIcon();
            ProtoStageIconInfo v = part.stackIcon.DisplayInfo();

            // fix nullref if no stackicon exists
            if (v != null)
            {
                v.SetMsgBgColor(XKCDColors.DarkRed);
                v.SetMsgTextColor(XKCDColors.Orange);
                v.SetMessage(StringUtils.Localize("#LOC_BDArmory_ProtoStageIconInfo_Overheat"));//"Overheat"
                v.SetProgressBarBgColor(XKCDColors.DarkRed);
                v.SetProgressBarColor(XKCDColors.Orange);
            }
            return v;
        }

        private ProtoStageIconInfo InitCMGauge(string ammoName) //thanks DYJ
        {
            EnsureStagingIcon();
            ProtoStageIconInfo a = part.stackIcon.DisplayInfo();
            // fix nullref if no stackicon exists
            if (a != null)
            {
                a.SetMsgBgColor(XKCDColors.Silver);
                a.SetMsgTextColor(XKCDColors.Brick);
                //a.SetMessage("Ammunition");
                a.SetMessage($"{ammoName}");
                a.SetProgressBarBgColor(XKCDColors.DarkGrey);
                a.SetProgressBarColor(XKCDColors.Brick);
            }
            return a;
        }
        private ProtoStageIconInfo InitAmmoGauge(string ammoName) //thanks DYJ
        {
            EnsureStagingIcon();
            ProtoStageIconInfo a = part.stackIcon.DisplayInfo();
            // fix nullref if no stackicon exists
            if (a != null)
            {
                a.SetMsgBgColor(XKCDColors.Grey);
                a.SetMsgTextColor(XKCDColors.Yellow);
                //a.SetMessage("Ammunition");
                a.SetMessage($"{ammoName}");
                a.SetProgressBarBgColor(XKCDColors.DarkGrey);
                a.SetProgressBarColor(XKCDColors.Yellow);
            }
            return a;
        }
        private ProtoStageIconInfo InitEmptyGauge(string message) //could remove emptygauge, mainly a QoL thing, removal might increase performance slightly
        {
            EnsureStagingIcon();
            ProtoStageIconInfo g = part.stackIcon.DisplayInfo();
            // fix nullref if no stackicon exists
            if (g != null)
            {
                g.SetMsgBgColor(XKCDColors.AlmostBlack);
                g.SetMsgTextColor(XKCDColors.Yellow);
                g.SetMessage(message);
                g.SetProgressBarBgColor(XKCDColors.Yellow);
                g.SetProgressBarColor(XKCDColors.Black);
            }
            return g;
        }
    }
}
