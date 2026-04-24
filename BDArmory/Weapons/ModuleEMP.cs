using BDArmory.Damage;
using BDArmory.Settings;
using BDArmory.Utils;
using System.Text;
using UnityEngine;

namespace BDArmory.Weapons
{
    public class ModuleEMP : PartModule
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_EMPBlastRadius"),//EMP Blast Radius
         UI_Label(affectSymCounterparts = UI_Scene.All, controlEnabled = true, scene = UI_Scene.All)]
        public float proximity = 5000;

        [KSPField]
        public bool AllowReboot = false;

        public bool Armed = false;

        static RaycastHit[] electroHits;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();
                part.OnJustAboutToBeDestroyed += DetonateEMPRoutine;
                if (electroHits == null) { electroHits = new RaycastHit[100]; }
            }
            base.OnStart(state);
        }

        public void DetonateEMPRoutine()
        {
            if (!Armed) return;
            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ModuleEMP]: Detonating EMP from {part.partInfo.name} with blast range {proximity}m.");
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || !v.loaded || v.packed) continue;
                if (VesselModuleRegistry.IgnoredVesselTypes.Contains(v.vesselType)) continue;
                if (!v.HoldPhysics)
                {
                    double targetDistance = Vector3d.Distance(this.vessel.GetWorldPos3D(), v.GetWorldPos3D());

                    if (targetDistance <= proximity)
                    {
                        var EMPDamage = ((proximity - (float)targetDistance) * 10) * BDArmorySettings.DMG_MULTIPLIER; //this way craft at edge of blast might only get disabled instead of bricked

                        Vector3 commandDir = Vector3.zero;
                        float shieldvalue = float.PositiveInfinity;
                        foreach (var moduleCommand in VesselModuleRegistry.GetModuleCommands(v))
                        {
                            //see how many parts are between emitter and the nearest command part to see which one is least shielded
                            var distToCommand = commandDir.magnitude;
                            var ElecRay = new Ray(part.transform.position, commandDir);
                            const int layerMask = (int)(LayerMasks.Parts | LayerMasks.Wheels);
                            var partCount = Physics.RaycastNonAlloc(ElecRay, electroHits, distToCommand, layerMask);
                            if (partCount == electroHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                            {
                                electroHits = Physics.RaycastAll(ElecRay, distToCommand, layerMask);
                                partCount = electroHits.Length;
                            }
                            for (int mwh = 0; mwh < partCount; ++mwh)
                            {
                                Part partHit = electroHits[mwh].collider.GetComponentInParent<Part>();
                                if (partHit == null) continue;
                                if (ProjectileUtils.IsIgnoredPart(partHit)) continue;
                                float testShieldValue = 0;
                                //AoE EMP field EMP damage mitigation - -1 EMP damage per mm of conductive armor/5t of conductive hull mass per part occluding command part from emission source         
                                var Armor = partHit.FindModuleImplementing<HitpointTracker>();
                                if (Armor != null && partHit.Rigidbody != null)
                                {
                                    if (Armor.Diffusivity > 15) testShieldValue += Armor.Armour;
                                    if (Armor.HullMassAdjust > 0) testShieldValue += (partHit.mass * 4);
                                }
                                if (testShieldValue < shieldvalue) shieldvalue = testShieldValue;
                            }
                        }
                        EMPDamage -= shieldvalue;
                        if (EMPDamage > 0)
                        {
                            var emp = v.rootPart.FindModuleImplementing<ModuleDrainEC>();
                            if (emp == null)
                            {
                                emp = (ModuleDrainEC)v.rootPart.AddModule("ModuleDrainEC");
                            }
                            emp.softEMP = AllowReboot; //can bypass DMP damage cap
                            emp.incomingDamage = EMPDamage;
                        }
                    }
                }
            }
        }

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(System.Environment.NewLine);
            output.AppendLine($"- EMP Blast Radius: {proximity} m");
            return output.ToString();
        }
    }
}
