using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
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
            return true;
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

        void OnPartDie() => OnPartDie(null);
        void OnPartDie(Part p) => cleaningRequired = true;
        void OnVesselCreate(Vessel v) => cleaningRequired = true;
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
            foreach (CMDropper p in droppers)
            {
                switch (p.cmType)
                {
                    case CMDropper.CountermeasureTypes.Flare:
                        {
                            if (hasFlareGauge || p.hasGauge) break;
                            p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                            p.gauge.AmmoName = "Flares";
                            hasFlareGauge = true;
                            p.hasGauge = true;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Chaff:
                        {
                            if (hasChaffGauge || p.hasGauge) break;
                            p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                            p.gauge.AmmoName = "Chaff";
                            hasChaffGauge = true;
                            p.hasGauge = true;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Smoke:
                        {
                            if (hasSmokegauce || p.hasGauge) break;
                            p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                            p.gauge.AmmoName = "Smoke";
                            hasSmokegauce = true;
                            p.hasGauge = true;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Decoy:
                        {
                            if (hasDecoyfauge || p.hasGauge) break;
                            p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                            p.gauge.AmmoName = "Decoys";
                            hasDecoyfauge = true;
                            p.hasGauge = true;
                        }
                        break;
                    case CMDropper.CountermeasureTypes.Bubbles:
                        {
                            if (hasBubbleGauge || p.hasGauge) break;
                            p.gauge = (BDStagingAreaGauge)p.part.AddModule("BDStagingAreaGauge");
                            p.gauge.AmmoName = "Bubbles";
                            p.hasGauge = true;
                            hasBubbleGauge = true;
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
