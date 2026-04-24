using BDArmory.Control;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BDArmory.GameModes.BattleDamage
{

    /// <summary>
    /// A method of adding sinking mechanics to KSP ships, abstracting flooding and compartmentalization due to most KSP boats being hollow boxes
    /// Flooding is done by abstracing the volume a hull would take up if fully present, and adding mass to one or more sections of this volume as they flood
    ///
    /// TODO - identify engines/electrics/ammo and have consequences for compartment flooding these are in
    ///  - brick engines that are flooded, lock turrets due to hydraulics down (if engineering offline), batteries discharge to 0
    ///  - or would this be a BattleDamage_Subsystems thing? Simplest would be a Tier 3 (sensors/engines/hydraultcs) EMP on the craft..?
    ///   - how would we determine Engineering loss? > Major+ hull breach + > 40% flooded for all 4 port/star compartments? just the aft 2?
    ///     - what about different compartment layouts? would be flooded sternfor tiny vessels with bow+stern; longer vessels with more than 2 port/star compartments... idk
    /// Pumps? separate parts or an intrinsic abstracted set of pumps that reduce total flooding over time, using EC? 
    ///  - Easier to just abstract it; could state a pump size that'll drain X m3 of water/s for y EC, with more water drained = higher EC cost
    ///  Dynamic Compartmentalization based on vessel dimensions vs hardcoded Bow/Stern + Port/Starboard Fore/Aft
    /// </summary>
    public class HullBreach : PartModule
    {
        double totalFloatability; //m3 hull volume/ total tons of water hull will hold when fully flooded
        public double sectionFloatability = 0; //compartment hull volume. 1/6th of totalFloatablity
        public List<Part> waterLineParts = new List<Part>();
        public Vector3 upDir; //for tracking capsizing, etc
        Dictionary<string, Vector3> HullSections = new Dictionary<string, Vector3>();//center point for Bow/Port Fore/Starboard Fore/Port Aft/Starboard Aft/Stern
        public Dictionary<string, double> HSectionFlooding = new Dictionary<string, double>(); //tracking for total water in vessel, in tons
        public Dictionary<string, double> prevHSectionFlooding = new Dictionary<string, double>(); //tracking for total water in vessel
        Vector2 VesselCenterOffset = Vector2.zero; //offset in m from geometric center of craft at waterline to rootPart
        public Vector2 VesselSize = Vector2.zero;
        float displacement = 0;
        int SectNum = 1;
        BDModuleSurfaceAI surfaceAI = null;
        private StringBuilder telemetryString = new StringBuilder();
        void Start()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            GameEvents.onPartDie.Add(OnPartDie);
            GameEvents.onVesselPartCountChanged.Add(OnVesselPartCountChanged);
            surfaceAI = VesselModuleRegistry.GetModule<BDModuleSurfaceAI>(vessel);
            if (surfaceAI && surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Land && surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Stationary)//Assuming ships are not going to be fitted with Pilot/VTOL/Orbital AI
                StartCoroutine(DelayedStart());
            else
            {
                GameEvents.onPartDie.Remove(OnPartDie);
                GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
                //part.RemoveModule(this);
                Destroy(this);
            }
        }
        //Vector3 debugVRTup = Vector3.zero;
        //Vector3 debugVRTright = Vector3.zero;
        //Vector3 debugVRTforward = Vector3.zero;
        IEnumerator DelayedStart()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            yield return wait;
            while (vessel.situation != Vessel.Situations.SPLASHED) yield return wait; //wait until boat in water
                                                                                      //if (vessel.IsUnderwater()) //commented out until better solution for dealing with KP bouyancy tank mod is implemented.
                                                                                      //{
                                                                                      //   isSinking = true; //loading a sunk ship on the bottom
                                                                                      //}
                                                                                      //else
                                                                                      //{
            yield return new WaitForSeconds(7); //wait for vessel to come to rest. I'm told the average settle time for a large ship can be 5-10 seconds
            float foreLength = 0;
            float aftLength = 0;
            float portBeam = 0;
            float starBeam = 0;
            //debugVRTforward = vessel.ReferenceTransform.forward;
            Quaternion priorRotation = part.transform.rotation;    //should be rootpart
            Quaternion vesselRotation = vessel.transform.rotation;
            Quaternion CFHRotation = vessel.ReferenceTransform.rotation;
            if (vesselRotation == CFHRotation) vessel.SetRotation(Quaternion.identity);
            else vessel.SetRotation(Quaternion.Inverse(CFHRotation) * vesselRotation);
            //this is rotating the vessel, which means it's no longer in-line with the water, potentially...
            //grab vessel height above surface for where water level should be, then do the grab bottom bounds, and compare that distance to 'water line hight'
            float waterLineHeight = FlightGlobals.getAltitudeAtPos(vessel.rootPart.transform.position); //height offset of root part from waterlevel? 
                                                                                                        //debugVRTup = vessel.ReferenceTransform.up;
                                                                                                        //debugVRTright = vessel.ReferenceTransform.right;
            foreach (Part p in vessel.parts)
            {
                //do some filtering. Control surface parts - hydrofoils/rudders/etc likely to not be part ofthe Hull proper, so their loss shouldn't cause holes
                //engines usually inside the hull, their loss shouldn't cause leaks (admittedly, if you've taken engine loss, you almost certainly already *have* leaks...)
                if (p.isEngine()) continue; //will cause issues if any large boat hull parts packs that have sterns with integrated propellers
                if (p.isControlSurface()) continue;
                if (p.Modules.GetModule<LaunchClamp>() != null) continue;
                if (ProjectileUtils.IsIgnoredPart(p)) continue; //AI/WM/flags/decals
                Vector3 partOffset = Vector3.zero;
                //Fixme - this should be all part colldiers, not first...
                Vector3 bottom = p.collider.ClosestPointOnBounds(vessel.ReferenceTransform.position + vessel.ReferenceTransform.forward * 1000);
                partOffset = bottom - vessel.rootPart.transform.position;
                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] rootDist2waterline: {waterLineHeight:F2}; part bottom: {partOffset.y:F2}; wLH + bottom: {(waterLineHeight + partOffset.y):F2}");
                if (waterLineHeight + partOffset.y > 0)
                {
                    if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Part {p.partInfo.title} above water, skipping");
                    continue;
                }
                Vector3 fore = p.collider.ClosestPointOnBounds(vessel.ReferenceTransform.position + vessel.ReferenceTransform.up * 1000);
                partOffset = fore - vessel.rootPart.transform.position;
                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Part {p.partInfo.title} fore: {partOffset.z:F3}");
                if (partOffset.z > foreLength) foreLength = Mathf.Abs(partOffset.z);
                Vector3 aft = p.collider.ClosestPointOnBounds(vessel.ReferenceTransform.position + vessel.ReferenceTransform.up * -1000);
                partOffset = aft - vessel.rootPart.transform.position;
                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Part {p.partInfo.title} aft: {-partOffset.z:F3}");
                if (-partOffset.z > aftLength) aftLength = Mathf.Abs(partOffset.z);
                Vector3 port = p.collider.ClosestPointOnBounds(vessel.ReferenceTransform.position + vessel.ReferenceTransform.right * -1000);
                partOffset = port - vessel.rootPart.transform.position;
                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Part {p.partInfo.title} port: {-partOffset.x:F3}");
                if (-partOffset.x > portBeam) portBeam = Mathf.Abs(partOffset.x);
                Vector3 star = p.collider.ClosestPointOnBounds(vessel.ReferenceTransform.position + vessel.ReferenceTransform.right * 1000);
                partOffset = star - vessel.rootPart.transform.position;
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Part {p.partInfo.title} fore: {(fore - vessel.rootPart.transform.position).z:F2}, aft: {-(aft - vessel.rootPart.transform.position).z:F2}, port: {-(port - vessel.rootPart.transform.position).x:F2}, star: {(star - vessel.rootPart.transform.position).x:F2}");
                if (partOffset.x > starBeam) starBeam = Mathf.Abs(partOffset.x);

                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] current vessel length: ({foreLength:F2} + {aftLength:F2}) {foreLength + aftLength:F2}; beam: ({portBeam:F2} + {starBeam:F2}) {portBeam + starBeam:F2}");
                waterLineParts.Add(p);
                p.rigidAttachment = true;
            }
            //take average beam of mid 75% of ship's length to ID any abberant beam values (from, say, random sponson or outrigger)?
            vessel.SetRotation(priorRotation);
            VesselCenterOffset = new Vector2((starBeam - portBeam) / 2, (foreLength - aftLength) / 2); //root part is not necessarily in geometric center of craft
            if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] {vessel.GetName()} VesseLCenterOffset is {VesselCenterOffset.x}, {VesselCenterOffset.y}");

            VesselSize = new Vector2(portBeam + starBeam, foreLength + aftLength);
            displacement = vessel.GetTotalMass();
            totalFloatability = VesselSize.x * (VesselSize.y * 0.75f) * (VesselSize.x * 0.7f); //abstracted displacement, m3. using a 0.75x mod for length to abstract bow/stern; 0.7x width for submerged hull + freeboard
                                                                                               //if (totalFloatability > 2 * displacement) totalFloatability = 2 * displacement;
            if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] {vessel.GetName()} waterline length: {VesselSize.y:F2}m; beam: {VesselSize.x:F2}m, displacment is {displacement:F2}t; hull volume is {(VesselSize.x * (VesselSize.y * 0.75f) * (VesselSize.x * 0.7f)):F2}({totalFloatability:F2})m3");
            //Set up Hull Sections
            //For a 6 section vessel, that means Bow, Port Fore, Starboard Fore, Port Aft, Startboard Aft, Stern sections
            //Vessel is thus 4 sections long; Bow is (0, vesselLength * .375) from vesselCenter, Port1 is (vesselBeam * 0.25, vessellength * 0.125), star1 is (-Beam * 0.25...), etc
            /*
            HullSections.Add("Bow", new Vector3(VesselCenterOffset.x, (VesselSize.y * 0.375f) + VesselCenterOffset.y, waterLineHeight));
            HullSections.Add("StarFore", new Vector3((VesselSize.x * 0.25f) + VesselCenterOffset.x, (VesselSize.y * 0.125f) + VesselCenterOffset.y, waterLineHeight));
            HullSections.Add("PortFore", new Vector3((-VesselSize.x * 0.25f) + VesselCenterOffset.x, (VesselSize.y * 0.125f) + VesselCenterOffset.y, waterLineHeight));
            HullSections.Add("StarAft", new Vector3((VesselSize.x * 0.25f) + VesselCenterOffset.x, (-VesselSize.y * 0.125f) + VesselCenterOffset.y, waterLineHeight));
            HullSections.Add("PortAft", new Vector3((-VesselSize.x * 0.25f) + VesselCenterOffset.x, (-VesselSize.y * 0.125f) + VesselCenterOffset.y, waterLineHeight));
            HullSections.Add("Stern", new Vector3(VesselCenterOffset.x, (-VesselSize.y * 0.375f) + VesselCenterOffset.y, waterLineHeight));

            //grab list of batteries/ammo boxes/engines/other things that tend not to work well submerged in saltwater for disabling those as a compartment floods?
            sectionFloatability = totalFloatability / HullSections.Keys.Count;
            */
            //TODO - have compartment number be determined by vessel length - doesn't make sense for a 15m sloop to have more than Bow/Stern, or a 100m carrier to only have a pair of flank compartments
            //or maybe by width - a very narrow hull probably wouldn't have port/starboard compartmentalization, just longitudinal bulkheads
            //Hmm. ok, but what if a 15.1m sloop? are the bow/stern compartments now 7.55m instead of 7.5? do they suddenly now be only 5m long to fit an amidships compartment?
            //could just have arbitrary thresholds - craft < 10m long have bow/stern; craft <20m get bow/amidships/stern; craft < 50m get bow/fore/aft/stern
            //would be simpler to just have a minimum compartment length; if length is, say, 5m, that 14m boat can't fit 3 compartments so it then onlygets bow/stern at 7m erach, etc
            //botes < 10m get 1 compartment
            // 10.1 - 20m get 2 Bow / stern(E)
            // 20.1 - 30m - bow / amidships(E) / stern.
            // 30.1 - 50m get 4 - bow / fore / aft(E) / stern
            // 50.1 - 75m get 5 - bow / fore / midships(E) / aft / stern
            // 75.1 - 100m get 6 - bow / fore1 / fore2 / aft1(E) / aft2 / stern
            // 100 + get 7 ? -bow / fore1 / fore2 / midships(E) / aft1 / aft2 / stern
            //Ratios for this ? port / star is +/ -25 % of beam from centerline
            // fore/ aft would be any for 1(name ?)
            // +/- 25% of length for 2 /= 4  length /= 4  *1
            // 0, +/- 33% length for 3 /= 3  length /= 6  (/6) * 0,2
            // +/- 125%, .375 % length for 4  length/= 8 , (length/8) * 1,3
            // 0, +/- 20%, 40% length for 5  length/= 10, (/10) * 0,2,4
            // +/- 8.33%, 25%, 41.66% for 6  length/= 12, (/12) * 1,3,5
            // 0, +/-14.28%, 28.57%, 42.58% for 7 length /14, (/14) * 0,2,4,6
            //If wider than 5m, get port/star compartments for non-bow/stern sections
            //(E)sections are engineering; if these fatally / fully flood, knock out engines /?turrets?/?pumps?/Electric generators?

            SectNum = VesselSize.y < 10 ? 1 : VesselSize.y < 20 ? 2 : VesselSize.y < 30 ? 3 : VesselSize.y < 50 ? 4 : VesselSize.y < 75 ? 5 : VesselSize.y < 100 ? 6 : 7;
            if (VesselSize.y < 10) HullSections.Add("Hull", new Vector3(VesselCenterOffset.x, VesselCenterOffset.y, waterLineHeight));
            else
            {
                string side = "";
                List<float> Xoffset = new List<float>();
                if (VesselSize.x < 3.75f) Xoffset.Add(0);
                else
                {
                    Xoffset.Add(VesselSize.x / 4);
                    Xoffset.Add(-VesselSize.x / 4);
                }
                Dictionary<string, float> Yoffset = new Dictionary<string, float>();
                float segmentOffset = (VesselSize.y / (SectNum * 2));

                HullSections.Add("Bow", new Vector3(VesselCenterOffset.x, (segmentOffset * (SectNum - 1)) + VesselCenterOffset.y, waterLineHeight));
                HullSections.Add("Stern", new Vector3(VesselCenterOffset.x, -(segmentOffset * (SectNum - 1)) + VesselCenterOffset.y, waterLineHeight));

                if (SectNum == 3 || SectNum == 5 || SectNum == 7)
                    Yoffset.Add("Midships", VesselCenterOffset.y);

                if (SectNum == 4 || SectNum == 6)
                {
                    Yoffset.Add("Fore", segmentOffset + VesselCenterOffset.y);
                    Yoffset.Add("Aft", -segmentOffset + VesselCenterOffset.y);
                }
                if (SectNum == 5 || SectNum == 7)
                {
                    Yoffset.Add("Fore", (segmentOffset * 2) + VesselCenterOffset.y);
                    Yoffset.Add("Aft", -(segmentOffset * 2) + VesselCenterOffset.y);
                }
                if (SectNum == 6)
                {
                    Yoffset.Add("Fore2", (segmentOffset * 3) + VesselCenterOffset.y);
                    Yoffset.Add("Aft2", -(segmentOffset * 3) + VesselCenterOffset.y);
                }
                if (SectNum == 7)
                {
                    Yoffset.Add("Fore2", (segmentOffset * 4) + VesselCenterOffset.y);
                    Yoffset.Add("Aft2", -(segmentOffset * 4) + VesselCenterOffset.y);
                }

                foreach (var Xoff in Xoffset)
                {
                    side = Xoff - VesselCenterOffset.x > 0 ? "Star" : Xoff - VesselCenterOffset.x < 0 ? "Port" : "";
                    foreach (var Yoff in Yoffset)
                    {
                        HullSections.Add(side + Yoff.Key, new Vector3(Xoff, Yoff.Value, waterLineHeight));
                    }
                }
            }
            sectionFloatability = totalFloatability / HullSections.Keys.Count;
            foreach (var loc in HullSections.Keys)
            {
                HSectionFlooding.Add(loc, 0);
                prevHSectionFlooding.Add(loc, 0);
            }
            SetUpCapsizeFlooding();
            //add a boxCollider of 0.75x beam, 0.75x(?) length to center of vessel, height should be 0.4x beam, and flush with waterline (so vesseloffsetx, vesseloffset.y, -vesselSize.x * 2 centerpoint)
            //to serve as a collider to eat hits to the 'internals' of a ship, since most KSP ships basically air surrounded by a bodykit shell
            //will need to add exception so hits to this collider don't do damage to rootPart it'd be attahed to.
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(VesselCenterOffset.x, VesselCenterOffset.y, VesselSize.x * 0.2f);
            col.transform.localScale = Vector3.one;
            col.name = "BDAHullBreachCitadelCollider";
            col.transform.SetParent(vessel.rootPart.transform);
            col.size = new Vector3(VesselSize.x * 0.75f, VesselSize.y * 0.75f, VesselSize.x * 0.4f);
            col.isTrigger = true; //to prevent the ship gibbing itself on collider spawn
            col.enabled = true;
            //}
        }

        void SetUpCapsizeFlooding()
        {
            if (surfaceAI.SurfaceType == AIUtils.VehicleMovementType.Submarine) return; //exception for submarines so they don't flood themselves submerging
            HullLeak.CreateLeakPool();
            foreach (var compartment in HullSections)
            {
                //generate some hull leaks in the superstructure area to cause vessel to flood if it capsizes
                if (compartment.Key == "Bow" || compartment.Key == "Stern") continue;
                var Leak = HullLeak.hullLeakPool.GetPooledObject();
                var leakFX = Leak.GetComponentInChildren<HullLeak>();
                Vector3 sectLocalPos = new Vector3(compartment.Value.x, compartment.Value.y, compartment.Value.z - (VesselSize.x * 0.4f));
                leakFX.AttachAt(vessel, sectLocalPos);
                leakFX.leakRate = (VesselSize.y * 0.2) * (VesselSize.x * 0.2f) * BDArmorySettings.BD_TANK_LEAK_RATE;
                leakFX.holeRadius = 0.5f;
                leakFX.holeType = HullLeak.FloodingType.Fatal;
                leakFX.HBController = this;
                leakFX.hullSection.Add(compartment.Key, 1);
                leakFX.sectionFloatability = sectionFloatability;
                leakFX.capsizeLeak = true;
                Leak.SetActive(true);
            }
            //add check to start opposige side flooding if capsized on it's side and only fore/aft comaprtment flooded, but bow/stern/opposite fore/aft compartment fine and providing enough buoyancy to keep afloat?
            //if a vessel is on its side, the in-the-air opposite compartments shouldn't be contributing anything to floatation...
        }
        public bool isSinking = false;
        int timer = 0;
        void FixedUpdate()
        {
            if (vessel == null)
            {
                part.RemoveModule(this);
            }
            if (!vessel.Splashed) return;
            //if (isSinking) return; //all buoyancy removed from submerged vessel, no need for this to run //commented out due to KP buoyancy mod - this still active even with all aprts buoyancy = -1
            upDir = (vessel.transform.position - vessel.mainBody.transform.position).normalized; 
            double totalWater = 0;
            Vector3 grav = FlightGlobals.getGeeForceAtPosition(vessel.CoM);
            //if (!isSinking)
            {
                timer++;
                foreach (var loc in HullSections)
                {
                    if (HSectionFlooding.ContainsKey(loc.Key))
                    {
                        if (HSectionFlooding.TryGetValue(loc.Key, out double water))
                        {
                            double oldWater = prevHSectionFlooding[loc.Key];
                            if (water > 0)
                            {
                                if (timer >= 50 && !isSinking)
                                {
                                    if (BDArmorySettings.DEBUG_HULLBREACH)
                                    {
                                        if (water > sectionFloatability * 0.98f)
                                            Debug.Log($"[BDArmory.HullBreach] {vessel.GetName()}'s {loc.Key} flooded!");
                                        else
                                            Debug.Log($"[BDArmory.HullBreach] {vessel.GetName()}'s {loc.Key}: flooding detected: {water:F2} t of water in compartment!");
                                    }
                                }
                                vessel.rootPart.rb.AddForceAtPosition(grav * Mathf.Lerp((float)oldWater, (float)water, 0.02f), vessel.rootPart.transform.position + vessel.ReferenceTransform.right * loc.Value.x + vessel.ReferenceTransform.up * loc.Value.y + vessel.ReferenceTransform.forward * loc.Value.z, ForceMode.Force);
                                totalWater += water;
                            }
                        }
                    }
                }
                if (timer >= 50) timer = 0;
            }
            if (totalWater / totalFloatability > 0.64f || surfaceAI.SurfaceType != AIUtils.VehicleMovementType.Submarine && vessel.altitude < -10) //if flood volume was only submerged hull area, use .66 (only bow/stern unfollded). But since there's some freeboard volume that can also fill, reduce threshold a tad?
                //if this is above 0.57 that means the entire initial displacement volume of the hull is now full of water; .66 means you need 4 of the 6 compartments fully flooded to down the ship
                //have depth check for low buoyancy vessels that startto go down before critical flood levels aciheved.
                //have these ratios betetermined by hullsection count?

            {
                if (vessel.altitude < -10 || totalWater / totalFloatability > .83f)
                {
                    isSinking = true; //vessel (presumably) fully under the waves at this point and subject to heavy flooding, remove buoyancy and let gravity take its course
                                      //remaining floatability check should account for submarines that are submerged by default
                }
            }
            if (vessel.ActiveController().WM == null) //Vessel destroyed/GM killed; sink
            {
                isSinking = true;
            }
            if (isSinking)
            {
                foreach (Part p in vessel.parts)
                {
                    p.buoyancy = -1;
                }
                //Destroy(this);
                //GameEvents.onPartDie.Remove(OnPartDie);
                //GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
                return;
            }
        }
        void OnGUI()
        {
            if (HighLogic.LoadedSceneIsFlight && vessel == FlightGlobals.ActiveVessel &&
                BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled)
            {
                if (isSinking) return;
                if (BDArmorySettings.DEBUG_LINES)
                {
                    foreach (var compartment in HullSections)
                    {
                        GUIUtils.MarkPosition(vessel.rootPart.transform.position + vessel.ReferenceTransform.right * compartment.Value.x +
                            vessel.ReferenceTransform.up * compartment.Value.y + vessel.ReferenceTransform.forward * compartment.Value.z, vessel.ReferenceTransform, Color.cyan, size: 4);

                        if (HSectionFlooding.TryGetValue(compartment.Key, out double water))
                        {
                            if (water > 0)
                            {
                                Vector3 compartmentLoc = vessel.rootPart.transform.position + vessel.ReferenceTransform.right * compartment.Value.x +
                                    vessel.ReferenceTransform.up * compartment.Value.y;
                                GUIUtils.DrawLineBetweenWorldPositions(compartmentLoc, compartmentLoc + upDir.normalized * 2, 30 * (float)(water / sectionFloatability), Color.blue);
                                if (GUIUtils.WorldToGUIPos(compartmentLoc + upDir.normalized * 2, out Vector2 guiPos))
                                {
                                    float amount = Mathf.Lerp((float)prevHSectionFlooding[compartment.Key], (float)water, 0.02f);
                                    GUI.Label(new(guiPos.x - 50, guiPos.y + 10, 100, 32), $"{water:F2}t | {(water / sectionFloatability) * 100:F2}%");
                                }
                            }
                        }
                    }
                    //GUIUtils.DrawLineBetweenWorldPositions(vessel.ReferenceTransform.position, vessel.ReferenceTransform.position + debugVRTup * 10 , 2, Color.green);
                    //GUIUtils.DrawLineBetweenWorldPositions(vessel.ReferenceTransform.position, vessel.ReferenceTransform.position + debugVRTright * 10, 2, Color.red);
                    //GUIUtils.DrawLineBetweenWorldPositions(vessel.ReferenceTransform.position, vessel.ReferenceTransform.position + debugVRTforward * 10, 2, Color.blue);

                    //GUIUtils.DrawLineBetweenWorldPositions(vessel.ReferenceTransform.position, vessel.ReferenceTransform.position + debugProjectedLoc, 2, Color.black);
                    //GUIUtils.DrawLineBetweenWorldPositions(vessel.ReferenceTransform.position + debugProjectedLoc, vessel.ReferenceTransform.position + debugHitLoc, 2, Color.grey);
                }
            }
        }

        public void OnPartDie(Part p)
        {
            if (p == part)
            {
                try
                {
                    isSinking = true;
                    foreach (Part vp in vessel.parts)
                    {
                        vp.buoyancy = -1;
                    }
                    Destroy(this); // Force this module to be removed from the gameObject as something is holding onto part references and causing a memory leak.
                    GameEvents.onPartDie.Remove(OnPartDie);
                    GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
                    return;
                }
                catch (Exception e)
                {
                    Debug.Log("[BDArmory.HullBreach]: Error OnPartDie: " + e.Message);
                }
            }
            if (waterLineParts.Contains(p)) //should we track superstructure holes as well? would only really be relevant if ship capsizing (already flooding in that case) or already on the way to the bottom...
            {
                //generate a new HullLeak to simulate the new big hole in the side of the ship
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] part loss detected! spawning new hole!");
                HullLeak.CreateLeakPool();
                double scale = p.radiativeArea / 3;
                string partname = p.partInfo.name.ToLower();
                if (partname.Contains("PanelArmor") || partname.Contains("slopeArmor") || partname.Contains("B9.Aero.Wing.Procedural"))
                    scale = p.radiativeArea / 2;
                float caliber = BDAMath.Sqrt((float)scale / Mathf.PI);
                var existingHoles = p.GetComponentsInChildren<HullLeak>();
                if (existingHoles != null) //see if our hew hole would go inside an existing one
                {
                    foreach (var hole in existingHoles)
                    {
                        float distanceSqr = (part.transform.position - hole.transform.position).sqrMagnitude; //pWings, armor panels have edge offsets. Option A: modify adjustableArmorpanel/proceduralWing to update CoMOffset based on panel dia, so that's usable
                        if (hole.holeRadius > caliber / 2)
                        {
                            if (distanceSqr < hole.holeRadius * hole.holeRadius)
                            {
                                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Attempting to add new leak inside existing hole, aborting!");
                                return;
                            }
                        }
                        else
                        {
                            if (distanceSqr < caliber * caliber / 4)
                            {
                                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] New hole overlaps smaller old hole, removing previous leak");
                                hole.lifeTime = 1;
                            }
                        }
                    }
                }
                var Leak = HullLeak.hullLeakPool.GetPooledObject();
                var leakFX = Leak.GetComponentInChildren<HullLeak>();

                leakFX.leakRate = scale * BDArmorySettings.BD_TANK_LEAK_RATE;                
                leakFX.holeRadius = caliber;
                leakFX.holeType = scale > sectionFloatability / 20 ? HullLeak.FloodingType.Fatal : scale > sectionFloatability / 4 ? HullLeak.FloodingType.Major : scale > 1 ? HullLeak.FloodingType.Minor : HullLeak.FloodingType.Splinter;
                leakFX.HBController = this;
                leakFX.sectionFloatability = sectionFloatability;
                //grab this part's loc relative to root, then apply VesselCenterOffset, then compare that coord to the section divisors
                //This needs to be vessel orientation agnostic, else a vessel rotated to heading 315, say, might have a location report at 7m,2m,7m from center instead of 0,2,10m that it should be
                Vector3 VRT = vessel.rootPart.transform.position;               
                Vector3 partLoc = p.transform.position - VRT;
                //need to get the port/star X/fore/aft Y offset of the hit in meters to the root for compartment ID, need to account vor vessel orientation
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] hitLoc: {partLoc.x:F2}, {partLoc.y:F2}, {partLoc.z:F2}m");
                Vector3 projectedLoc = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(partLoc, vessel.ReferenceTransform.forward), vessel.ReferenceTransform.right);
                //this will give a fore/aft hit offset, but will potentially result in a longer X offset due to z elevation differences. Doesn't matter, we just need to know if x is larger/smaller, not exact dist
                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] projectedLoc: {projectedLoc.x:F2}, {projectedLoc.y:F2}, {projectedLoc.z:F2}m");
                Vector2 adjustedLoc = new Vector2(((VRT + partLoc) - (VRT + projectedLoc)).magnitude, (VRT - (VRT + projectedLoc)).magnitude);
                //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] adjustedLoc: {adjustedLoc.x:F2}, {adjustedLoc.y:F2}m");
                adjustedLoc.x += VesselCenterOffset.x;
                adjustedLoc.y += VesselCenterOffset.y;
                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] adjustedHitLoc: {adjustedLoc.x:F2}, {adjustedLoc.y:F2}m");                
                float holeFracAft = 0;
                float holeFracFore = 0;
                string vesselSide = "";
                if (VesselSize.x < 3.75) vesselSide = "";
                else
                {
                    if (Vector3.Dot(partLoc.normalized, vessel.ReferenceTransform.right) > 0)
                    {
                        vesselSide = "Star";
                        adjustedLoc.x *= -1;
                    }
                    else vesselSide = "Port";
                }
                if (Vector3.Dot(partLoc.normalized, vessel.ReferenceTransform.up) < 0) adjustedLoc.y *= -1;
                leakFX.AttachAt(p.vessel, adjustedLoc);
                if (SectNum == 1)
                {
                    holeFracAft = Mathf.Clamp01(((caliber / 2) + adjustedLoc.y - (VesselSize.y / 2)) / caliber); //percent of hole extending past stern
                    holeFracFore = Mathf.Clamp01(1 - (((caliber / 2) + adjustedLoc.y - (-VesselSize.y / 2)) / caliber)); //percent of hole extending past bow
                    leakFX.hullSection.Add($"Hull", 1 - (holeFracFore + holeFracAft));
                    if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] hole in Hull; holeFracFore: {holeFracFore:F2}, holeFracAft: {holeFracAft:F2}");
                }
                else
                {
                    float halfCompLength = VesselSize.y / (2 * SectNum);
                    foreach (var kvp in HullSections)
                    {
                        if (kvp.Key.Contains("Port") && vesselSide != "Port" || kvp.Key.Contains("Star") && vesselSide != "Star") continue;
                        Debug.Log($"[HullBreach] sect: {kvp.Key}; adjustedLoc.y:{adjustedLoc.y:F2}, MoreThan: {kvp.Value.y - halfCompLength}? {adjustedLoc.y > (kvp.Value.y - halfCompLength)}; lessThan {kvp.Value.y + halfCompLength}? {adjustedLoc.y < (kvp.Value.y + halfCompLength)}");
                        if (adjustedLoc.y > (kvp.Value.y - halfCompLength - (caliber / 2)) && adjustedLoc.y < (kvp.Value.y + halfCompLength + (caliber / 2)))
                        {
                            holeFracFore = Mathf.Clamp01(1 - (((caliber / 2) + adjustedLoc.y - (kvp.Value.y - halfCompLength)) / caliber)); //percent of hole overlapping adjacent compartment fore
                            holeFracAft = Mathf.Clamp01(((caliber / 2) + adjustedLoc.y - (kvp.Value.y + halfCompLength)) / caliber); //percent of hole overlaping adjacent compartment aft

                            if (1 - (holeFracFore + holeFracAft) > 0) leakFX.hullSection.Add(kvp.Key, 1 - (holeFracFore + holeFracAft));
                            if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] hole in {kvp.Key}; holeFracFore: {holeFracFore:F2}, holeFracAft: {holeFracAft:F2}");
                        }
                    }
                }
                if (BDArmorySettings.DEBUG_HULLBREACH)
                    foreach (var item in leakFX.hullSection)
                    {
                        Debug.Log($"[BDArmory.HullBreach]: Part {part.partInfo.title} destroyed, generating flooding in {item.Key} compartment at a base rate of {leakFX.leakRate * item.Value * 1000:F3}({item.Value * 100}%) liter/s; total floodable volume {leakFX.sectionFloatability:F2}m3");
                    }
                Leak.SetActive(true);
            }
            foreach (Part part in p.children) //make debris separated from the ship by part destruction sink
            {
                part.buoyancy = -1;
            }
        }

        void OnVesselPartCountChanged(Vessel v)
        {
            using (List<Part>.Enumerator p = waterLineParts.GetEnumerator())
            {
                while (p.MoveNext())
                {
                    if (p.Current == null) continue;
                    if (p.Current.vessel != this.vessel)
                    {
                        if (p.Current.vesselType == VesselType.Debris) p.Current.buoyancy = -10; //have this check if part is made of wood/other MassMod < 1 hull material...?
                    }
                }
            }
        }

        //Vector3 debugHitLoc = Vector3.zero;
        //Vector3 debugProjectedLoc = Vector3.zero;
        //Vector3 debugAdjustedLoc = Vector3.zero;

        public static void AddHullLeak(RaycastHit hit, Part hitPart, float caliber, float area = -1)
        {
            if (BDArmorySettings.HULLBREACH && hitPart.Modules.GetModule<HitpointTracker>().Hitpoints > 0)
            {
                if (hitPart.isEngine()) return;
                if (hitPart.isControlSurface()) return;
                if (ProjectileUtils.IsIgnoredPart(hitPart)) return; //AI/WM/flags/decals

                var HBComponent = hitPart.vessel.GetComponent<HullBreach>();
                if (caliber <= 90)
                {
                    if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] HullLeak is too small to realistically matter, aborting!");
                    return;
                }
                caliber /= 1000; //mm to m conversion
                if (HBComponent != null)
                {
                    var existingHoles = hitPart.GetComponentsInChildren<HullLeak>();
                    if (existingHoles != null) //see if our hew hole would go inside an existing one
                    {
                        foreach (var hole in existingHoles)
                        {
                            float distanceSqr = (hit.point - hole.transform.position).sqrMagnitude;

                            if (distanceSqr < hole.holeRadius * hole.holeRadius)
                            {
                                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] Attempting to add new leak inside existing hole, aborting!");
                                return;
                                //technically, this should be the round passing though the hole and then detonating on an internal cabin or whatever would be inside of the outer hull plate...
                                //would need to extend this so armor pen calcs also check to see if a hit hands inside an existing hole, though, for proper penetration calculation
                            }
                            else
                            {
                                if (distanceSqr < caliber * caliber / 4)
                                {
                                    if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] New hole overlaps smaller old hole, removing previous leak");
                                    hole.lifeTime = 1;
                                }
                            }
                        }
                    }
                    HullLeak.CreateLeakPool();
                    var Leak = HullLeak.hullLeakPool.GetPooledObject();
                    var leakFX = Leak.GetComponentInChildren<HullLeak>();
                    
                    var scale = area > 0 ? area : Mathf.PI * (caliber / 2) * (caliber / 2);
                    leakFX.AttachAtPart(hitPart, hit);
                    leakFX.leakRate = scale * BDArmorySettings.BD_TANK_LEAK_RATE; //m2
                    leakFX.holeRadius = caliber / 2;
                    leakFX.holeType = scale > 20 ? HullLeak.FloodingType.Fatal : scale > 4 ? HullLeak.FloodingType.Major : scale > 1 ? HullLeak.FloodingType.Minor : HullLeak.FloodingType.Splinter;
                    leakFX.HBController = HBComponent;
                    leakFX.sectionFloatability = HBComponent.sectionFloatability;
                    Vector3 VRT = hitPart.vessel.rootPart.transform.position;
                    Vector3 hitLoc = hit.point - VRT;
                    //HBComponent.debugHitLoc = hitLoc;
                    //need to get the port/star X/fore/aft Y offset of the hit in meters to the root for compartment ID, need to account vor vessel orientation
                    if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] hitLoc: {hitLoc.x:F2}, {hitLoc.y:F2}, {hitLoc.z:F2}m");
                    Vector3 projectedLoc = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(hitLoc, hitPart.vessel.ReferenceTransform.forward), hitPart.vessel.ReferenceTransform.right);
                    //this will give a fore/aft hit offset, but will potentially result in a longer X offset due to z elevation differences. Doesn't matter, we just need to know if x is larger/smaller, not exact dist
                    //HBComponent.debugProjectedLoc = projectedLoc;
                    //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] projectedLoc: {projectedLoc.x:F2}, {projectedLoc.y:F2}, {projectedLoc.z:F2}m");
                    Vector2 adjustedLoc = new Vector2(((VRT + hitLoc) - (VRT + projectedLoc)).magnitude, (VRT - (VRT + projectedLoc)).magnitude);
                    //HBComponent.debugAdjustedLoc = adjustedLoc;
                    //if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] adjustedLoc: {adjustedLoc.x:F2}, {adjustedLoc.y:F2}m");
                    adjustedLoc.x += HBComponent.VesselCenterOffset.x;
                    adjustedLoc.y += HBComponent.VesselCenterOffset.y;
                    if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] adjustedHitLoc: {adjustedLoc.x:F2}, {adjustedLoc.y:F2}m");
                    float holeFracFore = 0; //fraction of hole overlapping bow-wards adjacent compartment
                    float holeFracAft = 0;  //fraction of hole overlapping aft-wards adjacent compartment
                    string vesselSide = "";
                    if (HBComponent.VesselSize.x < 3.75) vesselSide = "";
                    else
                    {
                        if (Vector3.Dot(hitLoc.normalized, hitPart.vessel.ReferenceTransform.right) > 0)
                        {
                            vesselSide = "Star";
                            adjustedLoc.x *= -1;
                        }
                        else vesselSide = "Port";
                    }
                    if (Vector3.Dot(hitLoc.normalized, hitPart.vessel.ReferenceTransform.up) < 0) adjustedLoc.y *= -1;
                    if (HBComponent.SectNum == 1)
                    {
                        holeFracAft = Mathf.Clamp01(((caliber / 2) + adjustedLoc.y - (HBComponent.VesselSize.y / 2)) / caliber); //percent of hole extending past stern
                        holeFracFore = Mathf.Clamp01(1 - (((caliber / 2) + adjustedLoc.y - (-HBComponent.VesselSize.y / 2)) / caliber)); //percent of hole extending past bow
                        leakFX.hullSection.Add($"Hull", 1 - (holeFracFore + holeFracAft));
                        if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] hole in Hull; holeFracFore: {holeFracFore:F2}, holeFracAft: {holeFracAft:F2}");
                    }
                    else
                    {
                        float halfCompLength = HBComponent.VesselSize.y / (2 * HBComponent.SectNum);
                        foreach (var kvp in HBComponent.HullSections)
                        {
                            if (kvp.Key.Contains("Port") && vesselSide != "Port" || kvp.Key.Contains("Star") && vesselSide != "Star") continue;
                            Debug.Log($"[HullBreach] sect: {kvp.Key}; adjustedLoc.y:{adjustedLoc.y:F2}, MoreThan: {kvp.Value.y - halfCompLength}? {adjustedLoc.y > (kvp.Value.y - halfCompLength)}; lessThan {kvp.Value.y + halfCompLength}? {adjustedLoc.y < (kvp.Value.y + halfCompLength)}");
                            if (adjustedLoc.y > (kvp.Value.y - halfCompLength - (caliber / 2)) && adjustedLoc.y < (kvp.Value.y + halfCompLength + (caliber / 2)))
                            {
                                holeFracFore = Mathf.Clamp01(1 - (((caliber / 2) + adjustedLoc.y - (kvp.Value.y - halfCompLength)) / caliber)); //percent of hole overlapping adjacent compartment fore
                                holeFracAft = Mathf.Clamp01(((caliber / 2) + adjustedLoc.y - (kvp.Value.y + halfCompLength)) / caliber); //percent of hole overlaping adjacent compartment aft

                                if (1 - (holeFracFore + holeFracAft) > 0) leakFX.hullSection.Add(kvp.Key, 1 - (holeFracFore + holeFracAft));
                                if (BDArmorySettings.DEBUG_HULLBREACH) Debug.Log($"[BDArmory.HullBreach] hole in {kvp.Key}; holeFracFore: {holeFracFore:F2}, holeFracAft: {holeFracAft:F2}");
                            }
                        }
                    }
                    if (BDArmorySettings.DEBUG_HULLBREACH)
                    {
                        Debug.Log($"[BDArmory.HullBreach]: Part {hitPart.partInfo.title} holed; total floodable volume {leakFX.sectionFloatability:F2}m3. Generating flooding in:");
                        foreach (var item in leakFX.hullSection)
                        {
                            Debug.Log($" -{item.Key} compartment at a base rate of {leakFX.leakRate * item.Value * 1000:F3}({item.Value * 100}%) liter/s");
                        }
                    }
                    Leak.SetActive(true);
                }
            }
        }
    }
}
