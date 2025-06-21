using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
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
            StartCoroutine(DelayedStart());
        }


        IEnumerator DelayedStart()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            foreach (CMDropper p in droppers)
            {
                if (p.countermeasureType)
            }
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

            UpdateJammerStrength()
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
            droppers.RemoveAll(j => j.vessel != vessel);
        }
    }
}
