﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Modules;
using BDArmory.Utils;
using System.Linq;

namespace BDArmory.CounterMeasure
{
    public class VesselCloakInfo : MonoBehaviour
    {
        List<ModuleCloakingDevice> cloaks;
        public Vessel vessel;
        public List<Part> cloakedParts;

        bool cEnabled;

        public bool cloakEnabled
        {
            get { return cEnabled; }
        }

        float orf = 1;
        public float opticalReductionFactor
        {
            get { return orf; }
        }

        float trf = 1;
        public float thermalReductionFactor
        {
            get { return trf; }
        }

        void Start()
        {
            vessel = GetComponent<Vessel>();
            if (!vessel)
            {
                Debug.Log("[BDArmory.VesselCloakInfo]: VesselCloakInfo was added to an object with no vessel component");
                Destroy(this);
                return;
            }
            cloaks = new List<ModuleCloakingDevice>();
            vessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onPartJointBreak.Add(OnPartJointBreak);
            GameEvents.onPartDie.Add(OnPartDie);
        }

        void OnDestroy()
        {
            if (vessel) vessel.OnJustAboutToBeDestroyed -= AboutToBeDestroyed;
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
                StartCoroutine(DelayedCleanCloakListRoutine());
            }
        }

        void OnVesselCreate(Vessel v)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedCleanCloakListRoutine());
            }
        }

        void OnPartJointBreak(PartJoint j, float breakForce)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedCleanCloakListRoutine());
            }
        }
        public void AddCloak(ModuleCloakingDevice cloak)
        {
            if (!cloaks.Contains(cloak))
            {
                cloaks.Add(cloak);
            }

            UpdateCloakStrength();
        }

        public void RemoveCloak(ModuleCloakingDevice cloak)
        {
            cloaks.Remove(cloak);

            UpdateCloakStrength();
        }

        void UpdateCloakStrength()
        {
            cEnabled = cloaks.Count > 0;

            trf = 1;
            orf = 1;

            using (List<ModuleCloakingDevice>.Enumerator cloak = cloaks.GetEnumerator())
                while (cloak.MoveNext())
                {
                    if (cloak.Current == null) continue;
                    if (cloak.Current.thermalReductionFactor < trf)
                    {
                        trf = cloak.Current.thermalReductionFactor;
                    }
                    if (cloak.Current.opticalReductionFactor < orf)
                    {
                        orf = cloak.Current.opticalReductionFactor;
                    }
                }
        }

        public void DelayedCleanCloakList()
        {
            StartCoroutine(DelayedCleanCloakListRoutine());
        }

        IEnumerator DelayedCleanCloakListRoutine()
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;
            yield return wait;
            using (List<Part>.Enumerator cloaked = cloakedParts.GetEnumerator())
                while (cloaked.MoveNext())
                {
                    if (cloaked.Current == null) continue;
                    if (cloaked.Current.vessel != vessel) //if part has been detached from the main vessel
                    {
                        foreach (var cD in cloaks.Where(j => j.vessel == cloaked.Current.vessel)) //check if the new debris vessel didn't also have a device (if for whatever reson the vessel had multiple)
                        {
                            if (!cD.OpticalCloaking || !cD.enabled) cloaked.Current.SetOpacity(1); //and if it doesn't/isn't on/isn't an optical cloak, decloak the debris
                        }
                    }                        
                }
            CleanCloakList();
        }

        void CleanCloakList()
        {
            vessel = GetComponent<Vessel>();

            if (!vessel)
            {
                Destroy(this);
            }
            cloaks.RemoveAll(j => j == null);
            cloaks.RemoveAll(j => j.vessel != vessel);

            using (var cl = VesselModuleRegistry.GetModules<ModuleCloakingDevice>(vessel).GetEnumerator())
                while (cl.MoveNext())
                {
                    if (cl.Current == null) continue;
                    if (cl.Current.cloakEnabled)
                    {
                        AddCloak(cl.Current);
                    }
                }
            UpdateCloakStrength();
        }
    }
}