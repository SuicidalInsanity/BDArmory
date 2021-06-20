using System;
using System.Collections.Generic;
using System.Linq;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using KSP.Localization;
using UnityEngine;

namespace BDArmory.Core.Module
{
    public class HitpointTracker : PartModule, IPartMassModifier, IPartCostModifier
    {
        #region KSP Fields
        public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => armorMass + HullmassAdjust;

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleCost(float baseCost, ModifierStagingSituation situation) => armorCost;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        private float partMass = 1f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Hitpoints"),//Hitpoints
        UI_ProgressBar(affectSymCounterparts = UI_Scene.None, controlEnabled = false, scene = UI_Scene.All, maxValue = 100000, minValue = 0, requireFullControl = false)]
        public float Hitpoints;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorThickness"),//Armor Thickness
        UI_FloatRange(minValue = 0f, maxValue = 500f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float Armor = 10f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Armor Type"),//Ammunition Types
        UI_FloatRange(minValue = 1, maxValue = 999, stepIncrement = 1, scene = UI_Scene.All)]
        public float ArmorTypeNum = 1; //replace with prev/next buttons? //or a popup GUI box with a list of selectable types...

         //Add a part material type setting, so parts can be selected to be made out of wood/aluminium/steel to adjust base partmass/HP?
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Hull Type"),//hull material Types
        UI_FloatRange(minValue = 1, maxValue = 3, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float HullTypeNum = 2;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Current Hull Material")]//Status
        public string guiHullTypeString = "Aluminium";

        public float HullmassAdjust = 0f;

        private float OldArmorType = 1;

        [KSPField(advancedTweakable = true, guiActive = false, guiActiveEditor = true, guiName = "Armor Mass")]//armor mass
        public float armorMass = 0f;

        [KSPField(advancedTweakable = true, guiActive = false, guiActiveEditor = true, guiName = "Armor Cost")]//armor cost
        public float armorCost = 0f;

        [KSPField(isPersistant = true)]
        public string SelectedArmorType = "None"; //presumably Aubranium can use this to filter allowed/banned types

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Current Armor")]//Status
        public string guiArmorTypeString = "def";

        private ArmorInfo armorInfo;

        private bool armorReset = false;

        [KSPField(isPersistant = true)]
        public float maxHitPoints = 0f;

        [KSPField(isPersistant = true)]
        public float ArmorThickness = 0f;

        [KSPField(isPersistant = true)]
        public bool ArmorSet;

        [KSPField(isPersistant = true)]
        public string ExplodeMode = "Never";

        [KSPField(isPersistant = true)]
        public bool FireFX = true;

        [KSPField(isPersistant = true)]
        public float FireFXLifeTimeInSeconds = 5f;

        //Armor Vars
        [KSPField(isPersistant = true)]
        public float Density;
        [KSPField(isPersistant = true)]
        public float Diffusivity;
        [KSPField(isPersistant = true)]
        public float Ductility;
        [KSPField(isPersistant = true)]
        public float Hardness;
        [KSPField(isPersistant = true)]
        public float Strength;
        [KSPField(isPersistant = true)]
        public float SafeUseTemp;
        [KSPField(isPersistant = true)]
        public float Cost;

        private bool startsArmored = false;

        //Part vars
        public Vector3 partSize;
        [KSPField(isPersistant = true)]
        public float maxSupportedArmor = -1; //upper cap on armor per part, overridable in MM/.cfg
        public float armorVolume;
        private float sizeAdjust;
        AttachNode bottom;
        AttachNode top;

        #endregion KSP Fields

        #region Heart Bleed
        private double nextHeartBleedTime = 0;
        #endregion Heart Bleed

        private readonly float hitpointMultiplier = BDArmorySettings.HITPOINT_MULTIPLIER;

        private float previousHitpoints;
        private bool _updateHitpoints = false;
        private bool _forceUpdateHitpointsUI = false;
        private const int HpRounding = 100;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight) return;

            if (part.partInfo == null)
            {
                // Loading of the prefab from the part config
                _updateHitpoints = true;
            }
            else
            {
                // Loading of the part from a saved craft
                if (HighLogic.LoadedSceneIsEditor)
                {
                    _updateHitpoints = true;
                    ArmorSet = false;
                }
                else // Loading of the part from a craft in flight mode
                {
                    if (BDArmorySettings.RESET_HP && part.vessel != null) // Reset Max HP
                    {
                        var maxHPString = ConfigNodeUtils.FindPartModuleConfigNodeValue(part.partInfo.partConfig, "HitpointTracker", "maxHitPoints");
                        if (!string.IsNullOrEmpty(maxHPString)) // Use the default value from the MM patch.
                        {
                            try
                            {
                                maxHitPoints = float.Parse(maxHPString);
                                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.HitPointTracker]: setting maxHitPoints of " + part + " on " + part.vessel.vesselName + " to " + maxHitPoints);
                                _updateHitpoints = true;
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("[BDArmory.HitPointTracker]: Failed to parse maxHitPoints configNode: " + e.Message);
                            }
                        }
                        else // Use the stock default value.
                            maxHitPoints = 0f;
                    }
                    else // Don't.
                    {
                        enabled = false;
                    }
                }
            }
        }

        public void SetupPrefab()
        {
            if (part != null)
            {
                var maxHitPoints_ = CalculateTotalHitpoints();

                if (!_forceUpdateHitpointsUI && previousHitpoints == maxHitPoints_) return;

                //Add Hitpoints
                UI_ProgressBar damageFieldFlight = (UI_ProgressBar)Fields["Hitpoints"].uiControlFlight;
                damageFieldFlight.maxValue = maxHitPoints_;
                damageFieldFlight.minValue = 0f;

                UI_ProgressBar damageFieldEditor = (UI_ProgressBar)Fields["Hitpoints"].uiControlEditor;
                damageFieldEditor.maxValue = maxHitPoints_;
                damageFieldEditor.minValue = 0f;

                Hitpoints = maxHitPoints_;

                if (!ArmorSet) overrideArmorSetFromConfig();

                previousHitpoints = maxHitPoints_;
            }
            else
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.HitpointTracker]: OnStart part is null");
            }
        }

        public override void OnStart(StartState state)
        {
            isEnabled = true;

            if (part != null) _updateHitpoints = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                UI_FloatRange armorField = (UI_FloatRange)Fields["Armor"].uiControlFlight;
                //Once started the max value of the field should be the initial one
                armorField.maxValue = Armor;
                part.RefreshAssociatedWindows();
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                int typecount = 0;
                for (int i = 0; i < ArmorInfo.armorNames.Count; i++)
                {
                    typecount++;
                }
                UI_FloatRange ATrangeEditor = (UI_FloatRange)Fields["ArmorTypeNum"].uiControlEditor;
                ATrangeEditor.onFieldChanged = ArmorSetup;
                ATrangeEditor.maxValue = (float)typecount;
                UI_FloatRange HTrangeEditor = (UI_FloatRange)Fields["HullTypeNum"].uiControlEditor;
                HTrangeEditor.onFieldChanged = HullSetup;
                //if part is an engine/fueltank don't allow wood construction/mass reduction
                //change out for vesselregistry when able
                if (part.isEngine() || part.HasFuel())
                {
                    HTrangeEditor.minValue = 2;
                }
            }
            GameEvents.onEditorShipModified.Add(ShipModified);
            bottom = part.FindAttachNode("bottom");
            top = part.FindAttachNode("top");
            //getSize returns size of a rectangular prism; most parts are circular, some are conical; use sizeAdjust to compensate
            if (bottom != null && top != null) //cylinder
            {
                sizeAdjust = 0.783f;
            }
            else if ((bottom == null && top != null) || (bottom != null && top == null)) //cone
            {
                sizeAdjust = 0.422f;
            }
            else //no bottom or top nodes, assume srf attached part; these are usually panels of some sort. Will need to determine method of ID'ing triangular panels/wings
            {                                                                                               //Wings at least could use WingLiftArea as a workaround for approx. surface area...
                sizeAdjust = 0.5f; //armor on one side, otherwise will have armor thickness on both sides of the panel, nonsensical + doiuble weight
            }
            armorMass = 0;
            partMass = part.mass;
            partSize = CalcPartBounds(this.part, this.transform).size;
            armorVolume =  // thickness * armor mass; moving it to Start since it only needs to be calc'd once
((((partSize.x * partSize.y) * 2) + ((partSize.x * partSize.z) * 2) + ((partSize.y * partSize.z) * 2)) * sizeAdjust);  //mass * surface area approximation of a cylinder, where H/W are unknown
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[ARMOR]: part size is (X: " + partSize.x + ";, Y: " + partSize.y + "; Z: " + partSize.z);
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[ARMOR]: size adjust mult: " + sizeAdjust + "; part srf area: " + ((((partSize.x * partSize.y) * 2) + ((partSize.x * partSize.z) * 2) + ((partSize.y * partSize.z) * 2)) * sizeAdjust));
            SetupPrefab();
            ArmorSetup(null, null);
        }

        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(ShipModified);
        }

        public void ShipModified(ShipConstruct data)
        {
            _updateHitpoints = true;
        }

        public override void OnUpdate()
        {
            RefreshHitPoints();
            if (BDArmorySettings.HEART_BLEED_ENABLED && ShouldHeartBleed())
            {
                HeartBleed();
            }
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (ArmorTypeNum != OldArmorType)
                {
                    OldArmorType = ArmorTypeNum;
                    ArmorSetup(null, null);
                }
                RefreshHitPoints();
            }
        }

        private void RefreshHitPoints()
        {
            if (_updateHitpoints)
            {
                SetupPrefab();
                _updateHitpoints = false;
                _forceUpdateHitpointsUI = false;
            }
        }

        private bool ShouldHeartBleed()
        {
            // wait until "now" exceeds the "next tick" value
            double dTime = Planetarium.GetUniversalTime();
            if (dTime < nextHeartBleedTime)
            {
                //Debug.Log(string.Format("[HitpointTracker] TimeSkip ShouldHeartBleed for {0} on {1}", part.name, part.vessel.vesselName));
                return false;
            }

            // assign next tick time
            double interval = BDArmorySettings.HEART_BLEED_INTERVAL;
            nextHeartBleedTime = dTime + interval;

            return true;
        }

        private void HeartBleed()
        {
            float rate = BDArmorySettings.HEART_BLEED_RATE;
            float deduction = Hitpoints * rate;
            if (Hitpoints - deduction < BDArmorySettings.HEART_BLEED_THRESHOLD)
            {
                // can't die from heart bleed
                return;
            }
            // deduct hp base on the rate
            //Debug.Log(string.Format("[HitpointTracker] Heart bleed {0} on {1} by {2:#.##} ({3:#.##}%)", part.name, part.vessel.vesselName, deduction, rate*100.0));
            AddDamage(deduction);
        }

        #region Hitpoints Functions

        public float CalculateTotalHitpoints()
        {
            float hitpoints;

            if (!part.IsMissile())
            {
                //var averageSize = part.GetAverageBoundSize();
                //var sphereRadius = averageSize * 0.5f;
                //var sphereSurface = 4 * Mathf.PI * sphereRadius * sphereRadius;
                //var structuralVolume = sphereSurface * 0.1f;
                var structuralVolume = ((partSize.x * partSize.y * partSize.z) * sizeAdjust);
                var density = ((partMass+HullmassAdjust) * 1000f) / structuralVolume;
                density = Mathf.Clamp(density, 1000, 10000);
                // if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.HitpointTracker]: Hitpoint Calc" + part.name + " | structuralVolume : " + structuralVolume);
                // if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.HitpointTracker]: Hitpoint Calc" + part.name + " | Density : " + density);

                var structuralMass = density * structuralVolume;
                //Debug.Log("[HP] " + part.name + " structural Volume: " + structuralVolume + "; density: " + density + " structural mass: " + structuralMass);
                // if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.HitpointTracker]: Hitpoint Calc" + part.name + " | structuralMass : " + structuralMass);
                //3. final calculations
                hitpoints = structuralMass * hitpointMultiplier * 0.333f;

                if (hitpoints > 10 * (partMass + HullmassAdjust) * 1000f || hitpoints < 0.1f * (partMass + HullmassAdjust) * 1000f)
                {
                    if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log($"[BDArmory.HitpointTracker]: Clamping hitpoints for part {part.name}");
                    hitpoints = hitpointMultiplier * (partMass + HullmassAdjust) * 333f;
                }

                // SuicidalInsanity B9 patch
                if (part.name.Contains("B9.Aero.Wing.Procedural"))
                {
                    if (part.Modules.Contains("FARWingAerodynamicModel") || part.Modules.Contains("FARControllableSurface"))
                    {
                        hitpoints = ((partMass + HullmassAdjust) * 1000f) * 3.5f * hitpointMultiplier * 0.333f; //To account for FAR's Strength-mass Scalar.
                    }
                    else
                    {
                        hitpoints = ((partMass + HullmassAdjust) * 1000f) * 7f * hitpointMultiplier * 0.333f; // since wings are basically a 2d object, lets have mass be our scalar - afterall, 2x the mass will ~= 2x the surfce area
                    } //breaks when pWings are made stupidly thick
                }
                if (HullTypeNum == 1)
                    {
                    hitpoints /= 4;
                    }
                    else if (HullTypeNum == 3)
                    {
                    hitpoints *= 1.75f;
                    }
                hitpoints = Mathf.Round(hitpoints / HpRounding) * HpRounding;
                if (hitpoints <= 0) hitpoints = HpRounding;
            }
            else
            {
                hitpoints = 5;
                Armor = 2;
            }

            //override based on part configuration for custom parts
            if (maxHitPoints != 0)
            {
                hitpoints = maxHitPoints;
            }

            if (hitpoints <= 0) hitpoints = HpRounding;
            return hitpoints;
        }

        public void DestroyPart()
        {
            if (partMass <= 2f) part.explosionPotential *= 0.85f;

            PartExploderSystem.AddPartToExplode(part);
        }

        public float GetMaxArmor()
        {
            UI_FloatRange armorField = (UI_FloatRange)Fields["Armor"].uiControlEditor;
            return armorField.maxValue;
        }

        public float GetMaxHitpoints()
        {
            UI_ProgressBar hitpointField = (UI_ProgressBar)Fields["Hitpoints"].uiControlEditor;
            return hitpointField.maxValue;
        }

        public bool GetFireFX()
        {
            return FireFX;
        }

        public void SetDamage(float partdamage)
        {
            Hitpoints -= partdamage;

            if (Hitpoints <= 0)
            {
                DestroyPart();
            }
        }

        public void AddDamage(float partdamage)
        {
            if (part.name == "Weapon Manager" || part.name == "BDModulePilotAI") return;

            partdamage = Mathf.Max(partdamage, 0f) * -1;
            Hitpoints += partdamage;

            if (Hitpoints <= 0)
            {
                DestroyPart();
            }
        }

        public void AddDamageToKerbal(KerbalEVA kerbal, float damage)
        {
            damage = Mathf.Max(damage, 0f) * -1;
            Hitpoints += damage;

            if (Hitpoints <= 0)
            {
                // oh the humanity!
                PartExploderSystem.AddPartToExplode(kerbal.part);
            }
        }
        #endregion Hitpoints Functions

        #region Armour
        public void ReduceArmor(float massToReduce)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log("[HPTracker] armor mass: " + armorMass + "; mass to reduce: " + (massToReduce * (Density / 1000000000)));
            }
            float reduceMass = (massToReduce * (Density / 1000000000));
            Armor -= ((1 - (reduceMass / armorMass)) * armorMass);
            if (Armor < 0)
            {
                Armor = 0;
            }
            armorMass -= reduceMass; //massToReduce is cm/3, armorMass is kg/m3
            if (armorMass < 0)
            {
                armorMass = 0;
            }
        }

        public void overrideArmorSetFromConfig()
        {
            ArmorSet = true;
            if (ArmorThickness != 0)
            {
                Armor = ArmorThickness;
                if (ArmorThickness > 10) //primarily panels, but any thing that starts with more than default armor
                {
                    startsArmored = true;
                    UI_FloatRange armortypes = (UI_FloatRange)Fields["ArmorTypeNum"].uiControlEditor;
                    armortypes.minValue = 2f; //prevent panels from being switched to "None" armor type
                }
            }
            if (maxSupportedArmor < 0) //hasn't been set in cfg
            {
                if (part.IsAero())
                {
                    maxSupportedArmor = 20;
                }
                else
                {
                    maxSupportedArmor = ((partSize.x / 20) * 1000); //~62mm for Size1, 125mm for S2, 185mm for S3
                    maxSupportedArmor /= 5;
                    maxSupportedArmor = Mathf.Round(maxSupportedArmor);
                    maxSupportedArmor *= 5;
                }
                if (ArmorThickness > 10 && ArmorThickness > maxSupportedArmor)//part has custom armor value, use that
                {
                    maxSupportedArmor = ArmorThickness;
                }
            }
            Debug.Log("[ARMOR] max supported armor for " + part.name + " is " + maxSupportedArmor);
            //if maxSupportedArmor > 0 && < armorThickness, that's entirely the fault of the MM patcher
            UI_FloatRange armorFieldFlight = (UI_FloatRange)Fields["Armor"].uiControlFlight;
            armorFieldFlight.minValue = 0f;
            armorFieldFlight.maxValue = maxSupportedArmor;
            UI_FloatRange armorFieldEditor = (UI_FloatRange)Fields["Armor"].uiControlEditor;
            armorFieldEditor.maxValue = maxSupportedArmor;
            armorFieldEditor.minValue = 0f;
            armorFieldEditor.onFieldChanged = ArmorSetup;
            part.RefreshAssociatedWindows();
        }
        public void ArmorSetup(BaseField field, object obj)
        {
            if ((ArmorTypeNum - 1) > ArmorInfo.armorNames.Count) //in case of trying to load a craft using a mod armor type that isn't installed and having a armorTypeNum larger than the index size
            {
                if (startsArmored)
                {
                    ArmorTypeNum = 2; //part starts with armor
                }
                else
                {
                    ArmorTypeNum = 1; //reset to 'None'
                }
            }
            armorInfo = ArmorInfo.armors[ArmorInfo.armorNames[(int)ArmorTypeNum - 1]]; //what does this return if armorname cannot be found (mod armor removed/not present in install?)
            if (startsArmored && ArmorTypeNum < 2)
            {
                ArmorTypeNum = 2;
            }
            //if (SelectedArmorType != ArmorInfo.armorNames[(int)ArmorTypeNum - 1]) //armor selection overridden by Editor widget
            //{
            //	armorInfo = ArmorInfo.armors[SelectedArmorType];
            //    ArmorTypeNum = ArmorInfo.armors.FindIndex(t => t.name == SelectedArmorType); //adjust part's current armor setting to match
            //}
            guiArmorTypeString = armorInfo.name;
            SelectedArmorType = armorInfo.name;
            Density = armorInfo.Density;
            Diffusivity = armorInfo.Diffusivity;
            Ductility = armorInfo.Ductility;
            Hardness = armorInfo.Hardness;
            Strength = armorInfo.Strength;
            SafeUseTemp = armorInfo.SafeUseTemp;
            SetArmor();
            armorMass = 0;
            armorCost = 0;
            if (ArmorTypeNum > 1) //don't apply cost/mass to None armor type
            {
                armorMass = (Armor / 1000) * armorVolume * Density / 1000; //armor mass in tons
                armorCost = armorVolume * armorInfo.Cost;
            }
            //part.RefreshAssociatedWindows(); //having this fire every time a change happens prevents sliders from being used. Add delay timer?
        }

        public void SetArmor()
        {
            if (ArmorTypeNum > 1)
            {
                UI_FloatRange armorFieldFlight = (UI_FloatRange)Fields["Armor"].uiControlFlight;
                if (armorFieldFlight.maxValue != maxSupportedArmor)
                {
                    armorReset = false;
                    armorFieldFlight.minValue = 0f;
                    armorFieldFlight.maxValue = maxSupportedArmor;
                }
                UI_FloatRange armorFieldEditor = (UI_FloatRange)Fields["Armor"].uiControlEditor;
                if (armorFieldEditor.maxValue != maxSupportedArmor)
                {
                    armorReset = false;
                    armorFieldEditor.maxValue = maxSupportedArmor;
                    armorFieldEditor.minValue = 0f;
                }
                armorFieldEditor.onFieldChanged = ArmorSetup;
                if (!armorReset)
                {
                    part.RefreshAssociatedWindows();
                }
                armorReset = true;
            }
            else
            {
                Armor = 10;
                UI_FloatRange armorFieldEditor = (UI_FloatRange)Fields["Armor"].uiControlEditor;
                armorFieldEditor.maxValue = 10; //max none armor to 10 (simulate part skin of alimunium)
                armorFieldEditor.minValue = 0;
                UI_FloatRange armorFieldFlight = (UI_FloatRange)Fields["Armor"].uiControlFlight;
                armorFieldFlight.minValue = 0f;
                armorFieldFlight.maxValue = 10;
                part.RefreshAssociatedWindows();
            }
        }
        private static Bounds CalcPartBounds(Part p, Transform t)
        {
            Bounds result = new Bounds(t.position, Vector3.zero);
            {
                if (p.collider && !p.Modules.Contains("LaunchClamp"))
                {
                    result.Encapsulate(p.collider.bounds);
                }
            }
            return result;
        }
        public void HullSetup(BaseField field, object obj)
        {
            if (part.isEngine() || part.HasFuel())
            {
                if (HullTypeNum < 2)
                {
                    HullTypeNum = 2;
                }
            }
            if (HullTypeNum == 1)
            {
                HullmassAdjust = (partMass / 3)- partMass;
                guiHullTypeString = Localizer.Format("#LOC_BDArmory_Wood");
            }
            else if (HullTypeNum == 2)
            {
                HullmassAdjust = 0;
                guiHullTypeString = Localizer.Format("#LOC_BDArmory_Aluminium");
            }
            else //hulltype 3
            {
                HullmassAdjust = partMass;
                guiHullTypeString = Localizer.Format("#LOC_BDArmory_Steel");
            }
            CalculateTotalHitpoints();
        }
        #endregion Armour
    }
}