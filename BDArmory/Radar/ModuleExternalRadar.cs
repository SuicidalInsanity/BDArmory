using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using KSP.Localization;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using BDArmory.Weapons.Missiles;
using BDArmory.Competition;

namespace BDArmory.Radar
{
    public class ModuleExternalRadar : ModuleRadar
    {
        #region KSPFields (Part Configuration)

        [KSPField]
        public float maxDatalinkRange = -1; //-1 for satellite link/infinite range, else dist in m

        //[KSPField]
        //public bool detonateOnDisable = false; - this just leaves debris; easier to have them auto-cleanup when dead

        //[KSPField]
        //public bool retractOnDisable = false; - irrelevant

        //[KSPField]
        //public bool requireDirectConnection = false; - irrelevant; if datalinkrange > 0, this is intrinsically true

        [KSPField]
        public float deployDelay = -1f;

        //[KSPField]
        //public bool deployAltitudeTrigger = false; - irrelevant, true if deployAlt > 1 unless for whatever reason we need sonars that only activate after sinking x hundred meters

        [KSPField]
        public float deployAltitude = -1f;

        [KSPField]
        public bool deployWhenLanded = true; //is this *landedOrSplashed*, or specifically for ground touchdown?

        [KSPField]
        public bool OnlyLinkToParent = false; //transmitting on a private channel to parent craft, or broadcasting to all team craft?

        float deployTime = 0f;

        private bool wasEnabled = false;

        protected bool[] linksActive;

        bool isConnected = false;

        #endregion KSPFields (Part Configuration)

        MissileLauncher ml = null;

        ModuleRadar mr = null;

        VesselRadarData parentVRD;

        public MissileLauncher mssl => ml;
        public bool isValid => ml != null;

        private MissileFire pwpmr;
        public MissileFire ParentWeaponManager
        {
            get
            {
                if (!pwpmr) GetPWPMR();
                return pwpmr;
            }
        }

        public void GetPWPMR()
        {
            // If somehow the missile is gone the sensor *should* be dead...
            if (!ml)
            {
                pwpmr = null;
                return;
            }
            if (!mr) //this shouldn't be getting called in this case
            {
                pwpmr = null;
                return;
            }
            // Return FiredByWM
            if (ml.FiredByWM)
            {
                pwpmr = ml.FiredByWM;
                return;
            }
            if (!OnlyLinkToParent)
            {
                // If dead, return the first linkedToVessels
                if (mr.LinkedVessels == null)
                {
                    pwpmr = null;
                    return;
                }
                for (int i = 0; i < mr.LinkedVessels.Count; i++)
                {
                    if (mr.LinkedVessels[i] != null)
                    {
                        pwpmr = mr.LinkedVessels[i].weaponManager;
                        return;
                    }
                }
            }
            pwpmr = null;
            return;
        }

        void Start()
        {
            ml = part.FindModuleImplementing<MissileLauncher>();
            mr = part.FindModuleImplementing<ModuleRadar>();
            mr.MER = this;

            Events[nameof(Toggle)].guiActive = false;
            Events[nameof(Toggle)].guiActiveEditor = false;
            maxLocks = 0; //don't allow locks on sonobuoys, etc. You could have a locking sonobuoy if sufficiently large/expsensive, but we don't have semi-active sonar homing torpedoes (which also make little sense), so...
            canLock = false;
        }

        public void ArmSensor()
        {
            deployTime = Time.time + deployDelay;
            StartCoroutine(SensorActivationCoroutine());
            if (pwpmr == null) return;
            parentVRD = pwpmr.vessel.gameObject.GetComponent<VesselRadarData>(); //ensure parent VRD is set up prior to probe activation
            if (parentVRD == null)
            {
                parentVRD = pwpmr.vessel.gameObject.AddComponent<VesselRadarData>();
                parentVRD.weaponManager = pwpmr; //this will need to update if !onlyLinkToParent and parent dies
            }
        }

        IEnumerator SensorActivationCoroutine()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (!(Time.time > deployTime && (deployAltitude < 0 || vessel.altitude < deployAltitude) && (!deployWhenLanded || (vessel.LandedOrSplashed || vessel.altitude < 1))))
            {
                yield return wait;
            }
            mr.EnableRadar(false);
            LinkSensorToNet();
            while (!mr.radarEnabled) yield return wait;
            wasEnabled = true;
            if (parentVRD != null)
                parentVRD.queueLinks = true;
        } 
        public void LinkSensorToNet()
        {
            UpdateLinkedVessels(BDATargetManager.RegisterExternalSensor(this));
            linksActive = new bool[mr.LinkedVessels.Count];
            vesselRadarData.AddSensor(this); //otherwise the sensor only registers when vrd.RefreshAvailableLinks() is called
            vesselRadarData.queueLinks = true;
        }

        public void UnlinkSensorFromNet()
        {
            List<VesselRadarData>.Enumerator vrd = mr.LinkedVessels.GetEnumerator();
            while (vrd.MoveNext())
            {
                if (vrd.Current == null) continue;
                vrd.Current.RemoveDataFromRadar(this);
            }
            vrd.Dispose();
            UpdateLinkedVessels(null);
            BDATargetManager.RemoveExternalSensor(this);

        }

        public override void OnFixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (radarEnabled) CheckLinks();
            if (mr != null && wasEnabled && !mr.radarEnabled) //out of electricity and shut down
            {
                UnlinkSensorFromNet();
                if (ml != null)
                    ml.Detonate(); //sonobuoy's dry, remove it
                wasEnabled = false;
            }
        }

        public void CheckLinks()
        {
            // If < 0 we don't care about EITHER range or LoS
            if (maxDatalinkRange < 0f)
            {
                isConnected = true;
                for (int i = 0; i < mr.LinkedVessels.Count; i++)
                {
                    linksActive[i] = true;
                }
                return;
            }
            isConnected = false;
            Vector3 adjustedPos = vessel.LandedOrSplashed ? (vessel.CoM + vessel.up * ((vessel.mainBody.ocean && vessel.altitude < 0f) ? (5f - vessel.altitude) : 5f)) : vessel.CoM;
            for (int i = 0; i < mr.LinkedVessels.Count; i++)
            {
                Vessel currVessel = mr.LinkedVessels[i].vessel;
                Vector3 currPos = currVessel.CoM;
                // If range == 0, we don't care about range, only LoS
                if (maxDatalinkRange > 0f && (currPos - vessel.CoM).sqrMagnitude > maxDatalinkRange * maxDatalinkRange)
                {
                    linksActive[i] = false;
                    continue;
                }
                // LoS must not be blocked
                if (!RadarUtils.TerrainCheck(adjustedPos, currPos, vessel.mainBody))
                {
                    linksActive[i] = true;
                    isConnected = true;
                }
                else
                {
                    linksActive[i] = false;
                }
            }
        }
        public override void ReceiveContactData(TargetSignatureData contactData, bool _locked)
        {
            if (!isConnected) return;
            for (int i = 0; i < mr.LinkedVessels.Count; i++)
            {
                if (maxDatalinkRange >= 0f && !linksActive[i]) continue;
                VesselRadarData currVRD = mr.LinkedVessels[i];
                if (currVRD == null) continue;
                if (currVRD.canReceiveRadarData && currVRD.vessel != contactData.vessel)
                {
                    currVRD.AddRadarContact(this, contactData, _locked, true);
                }
            }
        }

        public void CheckLinkArraySize()
        {
            // Check array size...
            if (linksActive.Length >= mr.LinkedVessels.Count) return;

            // Resize array...
            linksActive = new bool[mr.LinkedVessels.Count];
            // Populate array if datalinkRange < 0f
            // Not technically necessary since we skip this check in the code if
            // datalinkRange < 0, but this will probably save a headache in case
            // that gets changed for some reason...
            if (maxDatalinkRange < 0f)
            {
                for (int i = 0; i < linksActive.Length; i++)
                {
                    linksActive[i] = true;
                }
            }
        }

        public override void LinkToVRD(VesselRadarData vrd)
        {
            BDATargetManager.LinkExternalSensorGroup(vrd, this);
        }

    }
}