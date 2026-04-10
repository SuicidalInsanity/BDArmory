using BDArmory.Bullets;
using BDArmory.Extensions;
using BDArmory.GameModes.BattleDamage;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using Smooth.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace BDArmory.FX
{
    /// <summary>
    /// An attached gameobject similar to a bullet decal or fuel leak that will add water mass when submerged to the associated vessel
    /// </summary>

    class HullLeak : MonoBehaviour
    {
        public static ObjectPool hullLeakPool;
        public HullBreach HBController;
        public Dictionary<string, double> hullSection = new Dictionary<string, double>();
        public Vessel parentVessel;
        public Part parentPart;

        public double leakRate = 0; //water gain per second, in kg
        public float holeRadius = 0;
        double totalLeakAmount = 0; //
        public double sectionFloatability = 0; //total displacement volume of the compartment, sets ceiling for how much water the compartment can contain
        public enum FloodingType
        {
            Splinter = 0, // splinter damage/small caliber hits - non-penetrating holes < ~40mm dia. that would flood one room/cabin. Mk1 crewcan is 2.5m3; Cumulative hits cannot flood more than 1/4th compartment capacity.
            Minor = 1, // Penetrating shells/minor HE blast damage that warps doors, floods a couple of rooms. Cannot flood more that max 1/2 compartment capacity
            Major = 2, // Large-scale deep penetration/heavy blast damage that warps frames/bulkheads, major flooding. Floods 1/2 compartment capacity.
            Fatal = 3 //Torpedo hit or similar, massive hole in hull and substantial damage to compartmentalization; Floods entire compartment
        }
        public FloodingType holeType;
        public float lifeTime = -1; //damage control plugging/patching holes...?
        private float startTime;

        bool isFlooding = false;
        bool debugFlooding = false;
        public static ObjectPool CreateLeakPool()
        {
            if (hullLeakPool != null) return hullLeakPool;
            GameObject templateLeak = new GameObject("hullLeak");
            templateLeak.AddComponent<HullLeak>();
            templateLeak.SetActive(false);
            hullLeakPool = ObjectPool.CreateObjectPool(templateLeak, 100, true, true);
            return hullLeakPool;
        }

        public void AttachAt(Vessel v, Vector3 LocalPoint, Part hitPart = null)
        {
            if (hitPart != null)
            {
                parentPart = hitPart;
                parentPart.OnJustAboutToDie += OnParentDestroy;
                parentPart.OnJustAboutToBeDestroyed += OnParentDestroy;
            }
            parentVessel = v;
            transform.SetParent(parentVessel.vesselTransform);
            transform.position = transform.parent.TransformPoint(LocalPoint);
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(true); // Catch unloading events too.
            gameObject.SetActive(true);
        }

        public void AttachAtPart(Part hitPart, RaycastHit hit)
        {
            if (hitPart is null) return;
            parentPart = hitPart;
            parentVessel = parentPart.vessel;
            transform.SetParent(hit.collider.transform);
            transform.position = hit.point;
            parentPart.OnJustAboutToDie += OnParentDestroy;
            parentPart.OnJustAboutToBeDestroyed += OnParentDestroy;
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(true); // Catch unloading events too.
            gameObject.SetActive(true);
        }



        void OnParentDestroy()
        {
            if (parentPart is not null)
            {
                parentPart.OnJustAboutToDie -= OnParentDestroy;
                parentPart.OnJustAboutToBeDestroyed -= OnParentDestroy;
                if (parentPart == parentVessel.rootPart) Deactivate();
                else
                {
                    AttachAt(parentVessel, transform.position - parentVessel.rootPart.transform.position);
                    //the part the hole is in is gone, but hole may be still valid (hole larger than part/is also flooding an ajacent compartment)
                    //so transfer it over from partAttach to vesselAttach
                    HBController.OnPartDie(parentPart);
                }
            }
        }

        void OnEnable()
        {
            if (parentVessel == null)
            {
                gameObject.SetActive(false);
                return;
            }
            if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullLeak]: Leak added to {hullSection.Keys.FirstOrDefault()}");

            startTime = Time.time;
            parentVessel.OnJustAboutToBeDestroyed += AboutToBeDestroyed;
        }

        void OnDisable()
        {
            parentVessel = null;
            parentPart = null;
            leakRate = 0;
            totalLeakAmount = 0;
            holeRadius = 0;
            hullSection.Clear();
            HBController = null;
        }
        void AboutToBeDestroyed()
        {
            Destroy(this);
        }

        int timer = 0;
        void FixedUpdate()
        {
            debugFlooding = true;

            if (!gameObject.activeInHierarchy || !HighLogic.LoadedSceneIsFlight || BDArmorySetup.GameIsPaused) return;
            if (!HBController)
            {
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullLeak] HullBreach controller missing! removing hull leak.");
                Deactivate();
                return;
            }
            if (lifeTime >= 0 && Time.time - startTime > lifeTime)
            {
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullLeak] leak ended! Removing hull leak.");
                Deactivate();
            }
            if (!parentVessel || parentVessel.situation != Vessel.Situations.SPLASHED) return;

            var dist2Waterline = FlightGlobals.getAltitudeAtPos(transform.position);
            if (dist2Waterline > holeRadius + 1) //1 meter margin for waves/etc
            {
                debugFlooding = false;
                return; //alt + 1 for a 1m above waterline margin(wave action, bowwave, etc)
            }
            foreach (var compartment in hullSection)
            {
                if (!HBController.HSectionFlooding.ContainsKey(compartment.Key)) return;
                if (HBController.HSectionFlooding[compartment.Key] < sectionFloatability)
                {
                    //linear approximiation. yes, holes are not square, but this should be sufficiently close abstraction for performace; 1 + ((aboveWaterThresholdheight-(hole height + radius)
                    double holeFrac = Mathf.Clamp01(1 - ((holeRadius + dist2Waterline) - 1) / (holeRadius + holeRadius));
                    if (holeFrac <= 0) return;
                    float pressureMod = 1;
                    if (dist2Waterline < 0) pressureMod = 1 + (Mathf.Abs(dist2Waterline) / 10); //in 1g, waterpressure increases by basically 1 bar (100MPa) per 10m. TODO - local grav in case of ship battles on Eve/Laythe
                    double amount = (leakRate * compartment.Value * holeFrac * pressureMod) * Time.fixedDeltaTime;
                    isFlooding = true;
                    timer++;
                    switch (holeType)
                    {
                        case FloodingType.Splinter:
                            {
                                if (HBController.HSectionFlooding[compartment.Key] < sectionFloatability / 4) //outermost cabins flood, internal rooms unbreached, caps max flooding these holes can to to 1/4th total flotation
                                {
                                    if (HBController.HSectionFlooding[compartment.Key] + amount > sectionFloatability / 4)
                                    {
                                        amount = (sectionFloatability / 8) - (HBController.HSectionFlooding[compartment.Key] + amount);
                                        isFlooding = false;
                                    }
                                }
                                break;
                            }
                        case FloodingType.Minor:
                            {
                                if (HBController.HSectionFlooding[compartment.Key] < sectionFloatability / 2) //outer cabins flood, inner internal rooms unbreached, caps max flooding these holes can to to 1/2th total flotation
                                {
                                    if (totalLeakAmount + amount > sectionFloatability / 2)
                                    {
                                        amount = (sectionFloatability / 2) - (HBController.HSectionFlooding[compartment.Key] + amount);
                                        isFlooding = false;
                                    }
                                }
                                break;
                            }
                        case FloodingType.Major:
                            {
                                if (totalLeakAmount + amount > sectionFloatability / 2)  //major damage, deals 50% flooding damage. stacks with other flooding
                                {
                                    amount = (sectionFloatability / 2) - (HBController.HSectionFlooding[compartment.Key] + amount);
                                    isFlooding = false;
                                }
                                if (HBController.HSectionFlooding[compartment.Key] + amount > sectionFloatability)
                                {
                                    amount = sectionFloatability - (HBController.HSectionFlooding[compartment.Key] + amount);
                                    isFlooding = false;
                                }
                                break;
                            }
                        case FloodingType.Fatal:
                            {
                                if (HBController.HSectionFlooding[compartment.Key] + amount > sectionFloatability)
                                {
                                    amount = sectionFloatability - (HBController.HSectionFlooding[compartment.Key] + amount);
                                    isFlooding = false;
                                }
                                break;
                            }
                    }
                    amount = Mathf.Clamp((float)amount, 0, (float)sectionFloatability);
                    HBController.HSectionFlooding[compartment.Key] += amount;
                    HBController.HSectionFlooding[compartment.Key] = Mathf.Clamp((float)HBController.HSectionFlooding[compartment.Key], 0, (float)sectionFloatability);
                    totalLeakAmount = Mathf.Clamp((float)(totalLeakAmount + amount), 0, (float)sectionFloatability);
                    if (amount == 0) isFlooding = false;
                    if (timer >= 50 && isFlooding)
                        if (BDArmorySettings.DEBUG_HULLBREACH)
                            Debug.Log($"[BDArmory.HullLeak] {compartment.Key} undergoing {holeType} flooding at a rate of {(amount * 1000 / Time.fixedDeltaTime):F2} l of water/s ({HBController.HSectionFlooding[compartment.Key]:F2}({totalLeakAmount:F2})/{sectionFloatability:F2})m3 | ({leakRate * compartment.Value:F4}/{holeFrac:F2}/{pressureMod:F2})");
                }
                else isFlooding = false;
                if (HBController.isSinking) isFlooding = false; //job's done, shut down this process
            }
            if (timer >= 50) timer = 0;
            if (isFlooding = false || (lifeTime >= 0 && Time.time - startTime > lifeTime))
            {
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullLeak] leak finished! Removing hull leak.");
                Deactivate();
            }
            isFlooding = false;
        }

        void OnGUI()
        {
            if (HighLogic.LoadedSceneIsFlight && parentVessel == FlightGlobals.ActiveVessel &&
                BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled)
            {
                if (BDArmorySettings.DEBUG_LINES)
                {
                    GUIUtils.DrawTextureOnWorldPos(transform.position, debugFlooding ? BDArmorySetup.Instance.redDotTexture : BDArmorySetup.Instance.greenDotTexture, new Vector2(20, 20), 0);
                }
            }
        }

        void OnVesselUnloaded(Vessel vessel)
        {
            if (parentPart is not null && (parentPart.vessel is null || parentPart.vessel == vessel))
            {
                if (parentPart is not null)
                {
                    OnParentDestroy();
                }
            }
            else if (parentPart is null)
            {
                Deactivate(); // Sometimes (mostly when unloading a vessel) the parent becomes null without triggering OnParentDestroy.
            }
        }

        void OnVesselUnloaded_1_11(bool addRemove) // onVesselUnloaded event introduced in 1.11
        {
            if (addRemove)
                GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
            else
                GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
        }

        void Deactivate()
        {
            if (gameObject is not null && gameObject.activeSelf) // Deactivate even if a parent is already inactive.
            {
                parentPart = null;
                transform.parent = null; // Detach ourselves from the parent transform so we don't get destroyed if it does.
                gameObject.SetActive(false);
            }
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(false);
        }

        void OnDestroy() // This shouldn't be happening except on exiting KSP, but sometimes they get destroyed instead of disabled!
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 10) || Versioning.version_major > 1) // onVesselUnloaded event introduced in 1.11
                OnVesselUnloaded_1_11(false);
        }
    }
}
