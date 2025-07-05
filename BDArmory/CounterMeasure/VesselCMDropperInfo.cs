using BDArmory.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.CounterMeasure
{
    public class VesselCMDropperInfo : MonoBehaviour
    {
        List<CMDropper> droppers;
        public Vessel vessel;
        bool cleaningRequired = false;
        bool hasChaffGauge = false;
        bool hasFlareGauge = false;
        bool hasSmokegauce = false;
        bool hasDecoyfauge = false;
        bool hasBubbleGauge = false;
        public Dictionary <CMDropper.CountermeasureTypes, int> cmCounts;
        public Dictionary<CMDropper.CountermeasureTypes, int> cmMaxCounts;
        void Start()
        {
            if (!Setup())
            {
                Destroy(this);
                return;
            }
            vessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onPartJointBreak.Add(OnPartJointBreak);
            GameEvents.onPartDie.Add(OnPartDie);
            GameEvents.onVesselPartCountChanged.Add(OnVesselPartCountChanged);
            StartCoroutine(DelayedCleanListRoutine());
        }


        bool Setup()
        {
            if (!HighLogic.LoadedSceneIsFlight) return false;
            if (!vessel) vessel = GetComponent<Vessel>();
            if (!vessel)
            {
                Debug.Log("[BDArmory.VesselCMDropperInfo]: VesselCMDropperInfo was added to an object with no vessel component");
                return false;
            }
            if (droppers is null) droppers = new List<CMDropper>();
            cmCounts = new Dictionary<CMDropper.CountermeasureTypes, int>();
            cmMaxCounts = new Dictionary<CMDropper.CountermeasureTypes, int>();
            cmCounts.Add(CMDropper.CountermeasureTypes.Flare, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Chaff, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Smoke, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Bubbles, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Decoy, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Flare, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Chaff, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Smoke, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Bubbles, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Decoy, 0);
            return true;
        }

        void OnDestroy()
        {
            if (vessel) vessel.OnJustAboutToBeDestroyed -= AboutToBeDestroyed;
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
            GameEvents.onPartDie.Remove(OnPartDie);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
        }

        void AboutToBeDestroyed()
        {
            Destroy(this);
        }

        void OnPartDie() => OnPartDie(null);
        void OnPartDie(Part p) => cleaningRequired = true;
        void OnVesselCreate(Vessel v) => cleaningRequired = true;
        void OnVesselPartCountChanged(Vessel v) => cleaningRequired = true;
        void OnPartJointBreak(PartJoint j, float breakForce) => cleaningRequired = true;

        public void AddCMDropper(CMDropper CM)
        {
            if (droppers is null && !Setup())
            {
                Destroy(this);
                return;
            }

            if (!droppers.Contains(CM))
            {
                droppers.Add(CM);
            }

            DelayedCleanList();
        }

        public void RemoveCMDropper(CMDropper CM)
        {
            if (droppers is null && !Setup())
            {
                Destroy(this);
                return;
            }

            droppers.Remove(CM);
        }

        public void DelayedCleanList()
        {
            cleaningRequired = true;
        }
        void OnFixedUpdate()
        {
            if (UI.BDArmorySetup.GameIsPaused) return;

            if (cleaningRequired)
            {
                StartCoroutine(DelayedCleanListRoutine());
                cleaningRequired = false;
            }
        }
        IEnumerator DelayedCleanListRoutine()
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;
            yield return wait;
            CleanList();
        }

        void CleanList()
        {
            vessel = GetComponent<Vessel>();
            if (!vessel)
            {
                Destroy(this);
            }
            droppers.RemoveAll(j => j == null);
            droppers.RemoveAll(j => j.vessel != vessel); //cull destroyed CM boxes, if any, refresh Gauges on remainder
            cmCounts.Clear();
            cmMaxCounts.Clear();
            cmCounts.Add(CMDropper.CountermeasureTypes.Flare, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Chaff, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Smoke, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Bubbles, 0);
            cmCounts.Add(CMDropper.CountermeasureTypes.Decoy, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Flare, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Chaff, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Smoke, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Bubbles, 0);
            cmMaxCounts.Add(CMDropper.CountermeasureTypes.Decoy, 0);
            foreach (CMDropper p in droppers)
            {
                switch (p.cmType)
                {
                    case CMDropper.CountermeasureTypes.Flare:
                        {
                            if (!(hasFlareGauge || p.hasGauge))
                            {
                                p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                                p.gauge.AmmoName = "Flares";
                                hasFlareGauge = true;
                                p.hasGauge = true;
                            }
                            cmCounts[p.cmType] += p.cmCount;
                            cmMaxCounts[p.cmType] += p.maxCMCount;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Chaff:
                        {
                            if (!(hasChaffGauge || p.hasGauge))
                            {
                                p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                                p.gauge.AmmoName = "Chaff";
                                hasChaffGauge = true;
                                p.hasGauge = true;
                            }
                            cmCounts[p.cmType] += p.cmCount;
                            cmMaxCounts[p.cmType] += p.maxCMCount;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Smoke:
                        {
                            if (!(hasSmokegauce || p.hasGauge))
                            {
                                p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                                p.gauge.AmmoName = "Smoke";
                                hasSmokegauce = true;
                                p.hasGauge = true;
                            }
                            cmCounts[p.cmType] += p.cmCount;
                            cmMaxCounts[p.cmType] += p.maxCMCount;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Decoy:
                        {
                            if (!(hasDecoyfauge || p.hasGauge))
                            {
                                p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                                p.gauge.AmmoName = "Decoys";
                                hasDecoyfauge = true;
                                p.hasGauge = true;
                            }
                            cmCounts[p.cmType] += p.cmCount;
                            cmMaxCounts[p.cmType] += p.maxCMCount;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Bubbles:
                        {
                            if (!(hasBubbleGauge || p.hasGauge))
                            {
                                p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                                p.gauge.AmmoName = "Bubbles";
                                p.hasGauge = true;
                                hasBubbleGauge = true;
                            }   
                            cmCounts[p.cmType] += p.cmCount;
                            cmMaxCounts[p.cmType] += p.maxCMCount;
                        }
                        break;
                }
            }
            hasChaffGauge = false;
            hasFlareGauge = false;
            hasSmokegauce = false;
            hasDecoyfauge = false;
            hasBubbleGauge = false;
        }
    }
}
