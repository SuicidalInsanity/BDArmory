using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Modules;
using BDArmory.Utils;
using BDArmory.Targeting;
using BDArmory.Radar;

namespace BDArmory.CounterMeasure
{
    [RequireComponent(typeof(Vessel))]
    public class VesselECMJInfo : MonoBehaviour
    {
        List<ModuleECMJammer> jammers;
        public Vessel vessel;
        private TargetInfo ti;
        bool jEnabled;

        public bool jammerEnabled
        {
            get { return jEnabled; }
        }

        float jStrength;

        public float jammerStrength
        {
            get { return jStrength; }
        }

        float lbs;

        public float lockBreakStrength
        {
            get { return lbs; }
        }

        float rcsr;

        public float rcsReductionFactor
        {
            get { return rcsr; }
        }
        void Awake()
        {
            jammers = new List<ModuleECMJammer>();
            vessel = GetComponent<Vessel>();
            vessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onPartJointBreak.Add(OnPartJointBreak);
            GameEvents.onPartDie.Add(OnPartDie);
        }

        void OnDestroy()
        {
            vessel.OnJustAboutToBeDestroyed -= AboutToBeDestroyed;
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
            GameEvents.onPartDie.Remove(OnPartDie);
        }

        void AboutToBeDestroyed()
        {
            Destroy(this);
        }

        void OnPartDie(Part p = null)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedCleanJammerListRoutine());
            }
        }

        void OnVesselCreate(Vessel v)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedCleanJammerListRoutine());
            }
        }

        void OnPartJointBreak(PartJoint j, float breakForce)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedCleanJammerListRoutine());
            }
        }

        public void AddJammer(ModuleECMJammer jammer)
        {
            if (!jammers.Contains(jammer))
            {
                jammers.Add(jammer);
            }

            UpdateJammerStrength();
        }

        public void RemoveJammer(ModuleECMJammer jammer)
        {
            jammers.Remove(jammer);

            UpdateJammerStrength();
        }

        public void UpdateJammerStrength()
        {
            jEnabled = jammers.Count > 0;

            if (!jammerEnabled)
            {
                jStrength = 0;
            }

            float totaljStrength = 0;
            float totalLBstrength = 0;
            float jSpamFactor = 1;
            float lbreakFactor = 1;

            float rcsrTotal = 1;
            float rcsrCount = 0;

            List<ModuleECMJammer>.Enumerator jammer = jammers.GetEnumerator();
            while (jammer.MoveNext())
            {
                if (jammer.Current == null) continue;
                if (jammer.Current.signalSpam)
                {
                    totaljStrength += jSpamFactor * jammer.Current.jammerStrength;
                    jSpamFactor *= 0.75f;
                }
                if (jammer.Current.lockBreaker)
                {
                    totalLBstrength += lbreakFactor * jammer.Current.lockBreakerStrength;
                    lbreakFactor *= 0.65f;
                }
                if (jammer.Current.rcsReduction)
                {
                    rcsrTotal *= jammer.Current.rcsReductionFactor;
                    rcsrCount++;
                }
            }
            jammer.Dispose();

            lbs = totalLBstrength;
            jStrength = totaljStrength;

            if (rcsrCount > 0)
            {
                rcsr = Mathf.Max((rcsrTotal * rcsrCount), 0.0f); //allow for 100% stealth (cloaking device) or stealth malus (radar reflectors)
            }
            else
            {
                rcsr = 1;
            }
			
            ti = RadarUtils.GetVesselRadarSignature(vessel);
            ti.radarRCSReducedSignature = ti.radarBaseSignature;
            ti.radarModifiedSignature = ti.radarBaseSignature;
            ti.radarLockbreakFactor = 1;
            //1) read vessel ecminfo for jammers with RCS reduction effect and multiply factor
            ti.radarRCSReducedSignature *= rcsr;
            ti.radarModifiedSignature *= rcsr;
            //2) increase in detectability relative to jammerstrength and vessel rcs signature:
            // rcs_factor = jammerStrength / modifiedSig / 100 + 1.0f
            ti.radarModifiedSignature *= (((totaljStrength / ti.radarRCSReducedSignature) / 100) + 1.0f);
            //3) garbling due to overly strong jamming signals relative to jammer's strength in relation to vessel rcs signature:
            // jammingDistance =  (jammerstrength / baseSig / 100 + 1.0) x js
            ti.radarJammingDistance = ((totaljStrength / ti.radarBaseSignature / 100) + 1.0f) * totaljStrength;
            //4) lockbreaking strength relative to jammer's lockbreak strength in relation to vessel rcs signature:
            // lockbreak_factor = baseSig/modifiedSig x (1 � lopckBreakStrength/baseSig/100)
            // Use clamp to prevent RCS reduction resulting in increased lockbreak factor, which negates value of RCS reduction)
            ti.radarLockbreakFactor = (ti.radarRCSReducedSignature == 0) ? 0f :
                Mathf.Max(Mathf.Clamp01(ti.radarRCSReducedSignature / ti.radarModifiedSignature) * (1 - (totalLBstrength / ti.radarRCSReducedSignature / 100)), 0); // 0 is minimum lockbreak factor
        }
        void OnFixedUpdate()
        {
            if (UI.BDArmorySetup.GameIsPaused) return;
            if (jEnabled && jammerStrength > 0)
            {
                using (var loadedvessels = UI.BDATargetManager.LoadedVessels.GetEnumerator())
                    while (loadedvessels.MoveNext())
                    {
                        // ignore null, unloaded
                        if (loadedvessels.Current == null || !loadedvessels.Current.loaded || loadedvessels.Current == vessel) continue;
                        float distance = (loadedvessels.Current.CoM - vessel.CoM).magnitude;
                        if (distance < jammerStrength * 10)
                        {
                            RadarWarningReceiver.PingRWR(loadedvessels.Current, vessel.CoM, RadarWarningReceiver.RWRThreatTypes.Jamming, 0.2f);
                        }
                    }
            }
        }
        public void DelayedCleanJammerList()
        {
            StartCoroutine(DelayedCleanJammerListRoutine());
        }

        IEnumerator DelayedCleanJammerListRoutine()
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;
            yield return wait;
            CleanJammerList();
        }

        void CleanJammerList()
        {
            vessel = GetComponent<Vessel>();

            if (!vessel)
            {
                Destroy(this);
            }
            jammers.RemoveAll(j => j == null);
            jammers.RemoveAll(j => j.vessel != vessel);

            using (var jam = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel).GetEnumerator())
                while (jam.MoveNext())
                {
                    if (jam.Current == null) continue;
                    if (jam.Current.jammerEnabled)
                    {
                        AddJammer(jam.Current);
                    }
                }
            UpdateJammerStrength();
        }
    }
}
