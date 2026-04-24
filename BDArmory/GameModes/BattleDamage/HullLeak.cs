using BDArmory.GameModes.BattleDamage;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
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
        bool attachedToPart = false;
        public double leakRate = 0; //water gain per second, in m3
        public float holeRadius = 0;
        double totalLeakAmount = 0; //
        public double sectionFloatability = 0; //total displacement volume of the compartment, sets ceiling for how much water the compartment can contain
        public bool capsizeLeak = false; //for determining capsize flooding; ignore holeFrac calcs
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
                attachedToPart = true;
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
            attachedToPart = true;
            parentVessel = parentPart.vessel;
            transform.SetParent(hit.collider.transform);
            transform.position = hit.point;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
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
                if (BDArmorySettings.HULLBREACH) Debug.Log($"[BDarmory.HullLeak] OnParentDestroy called.");
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
            float HoleOrientation = Mathf.Clamp(1 - Mathf.Abs(Vector3.Dot(HBController.upDir.normalized, transform.up)), 0.05f, 1); //height modification of hole; holes that are perpendicualr to water are full height, holes in the upper deck parallel to sea would have 0 'depth', etc
            if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullLeak] Hole Dot: {Vector3.Dot(HBController.upDir.normalized, transform.up):F2}, holeOri: {HoleOrientation:F2}");
            if (dist2Waterline > (holeRadius * HoleOrientation) + 1) //1 meter margin for waves/etc
            {
                debugFlooding = false;
                return;
            }
            timer++;
            if (timer >= 50) //todo Timewarp adjsutments
            {
                foreach (var compartment in hullSection)
                {
                    if (!HBController.HSectionFlooding.ContainsKey(compartment.Key)) return;
                    if (HBController.HSectionFlooding[compartment.Key] < sectionFloatability)
                    {
                        //linear approximiation. yes, holes are not square, but this should be sufficiently close abstraction for performace; 1 + ((aboveWaterThresholdheight-(hole height + radius)
                        double holeFracWL = Mathf.Clamp01((holeRadius + dist2Waterline - 1) / (holeRadius + holeRadius)); //portion of hole above waterline + 1m wave margin
                        double holeFracKeel = Mathf.Clamp01(1 - ((holeRadius + dist2Waterline + (float)(HBController.VesselSize.x * 0.4)) / (holeRadius + holeRadius))); //portion of hole below bottom vessel
   
                        if (capsizeLeak) holeFracKeel = 0;
                        double holeFrac = 1 - (holeFracWL + holeFracKeel);
                      
                        if (BDArmorySettings.HULLBREACH) Debug.Log($"[BDArmory.HullLeak] WLoverlap: {holeFracWL:F2}; KeelOverlap: {holeFracKeel:F2}; holeFrac: {holeFrac:F2}");
                        if (holeFrac <= 0) return; //hole completely above waterline
                        float pressureMod = 1;
                        if (dist2Waterline < 0) pressureMod = 1 + (Mathf.Abs(dist2Waterline) / 10); //in 1g, waterpressure increases by basically 1 bar (100MPa) per 10m. TODO - local grav in case of ship battles on Eve/Laythe
                        double amount = (leakRate * Mathf.Max((float)parentVessel.horizontalSrfSpeed, 1) * holeFrac * pressureMod); //if adding speed modifier, have hook to have Ai slow in the event of major leaks to reduce water intake?
                        isFlooding = true;

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
                        amount = Mathf.Clamp((float)amount, 0, (float)sectionFloatability / 5); //unless compartment literally hollow, there'd be hallways/cabins/bulkheads/etc slowing progression of water into compartment
                        HBController.prevHSectionFlooding[compartment.Key] = HBController.HSectionFlooding[compartment.Key];
                        HBController.HSectionFlooding[compartment.Key] += amount;
                        HBController.HSectionFlooding[compartment.Key] = Mathf.Clamp((float)HBController.HSectionFlooding[compartment.Key], 0, (float)sectionFloatability);
                        totalLeakAmount = Mathf.Clamp((float)(totalLeakAmount + amount), 0, (float)sectionFloatability);
                        if (amount == 0) isFlooding = false;
                        if (timer >= 50 && isFlooding)
                            if (BDArmorySettings.DEBUG_HULLBREACH)
                                Debug.Log($"[BDArmory.HullLeak] {compartment.Key} undergoing {holeType} flooding at a rate of {(amount * 1000):F2} l of water/s ({HBController.HSectionFlooding[compartment.Key]:F2}({totalLeakAmount:F2})/{sectionFloatability:F2})m3 | ({leakRate * compartment.Value:F4}/{holeFrac:F2}/{pressureMod:F2})");
                    }
                    else isFlooding = false;
                }
                timer = 0;
            }
            if ((HBController.isSinking) || (isFlooding = false) || (lifeTime >= 0 && Time.time - startTime > lifeTime))
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
                    GUIUtils.DrawLineBetweenWorldPositions(transform.position, transform.position + transform.forward * 10 , 1, Color.blue);
                    GUIUtils.DrawLineBetweenWorldPositions(transform.position, transform.position + transform.right * 10, 1, Color.red);
                    GUIUtils.DrawLineBetweenWorldPositions(transform.position, transform.position + transform.up * 10, 1, Color.green);
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
            else if (parentPart is null && attachedToPart || parentVessel is null)
            {
                if (BDArmorySettings.HULLBREACH) Debug.Log($"[BDarmory.HullLeak] Parent is null, deactivating.");
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
