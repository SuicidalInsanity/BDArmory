using BDArmory.Core.Extension;
using UnityEngine;

namespace BDArmory.Core.Module
{
    public class HitpointTracker : PartModule
    {
        #region KSP Fields

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Hitpoints"),//Hitpoints
        UI_ProgressBar(affectSymCounterparts = UI_Scene.None, controlEnabled = false, scene = UI_Scene.All, maxValue = 100000, minValue = 0, requireFullControl = false)]
        public float Hitpoints;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorThickness"),//Armor Thickness
        UI_FloatRange(minValue = 1f, maxValue = 500f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float Armor = 10f;

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

        #endregion KSP Fields

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
                }
                else
                    enabled = false;
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

                //Add Armor
                UI_FloatRange armorFieldFlight = (UI_FloatRange)Fields["Armor"].uiControlFlight;
                armorFieldFlight.maxValue = 500f;
                armorFieldFlight.minValue = 10;

                UI_FloatRange armorFieldEditor = (UI_FloatRange)Fields["Armor"].uiControlEditor;
                armorFieldEditor.maxValue = 500f;
                armorFieldEditor.minValue = 10f;
                part.RefreshAssociatedWindows();

                if (!ArmorSet) overrideArmorSetFromConfig();

                previousHitpoints = maxHitPoints_;
            }
            else
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[HitpointTracker]: OnStart part is null");
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
            GameEvents.onEditorShipModified.Add(ShipModified);
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
        }

        public void Update()
        {
            RefreshHitPoints();
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

        #region Hitpoints Functions

        public float CalculateTotalHitpoints()
		{
			float hitpoints;

			if (!part.IsMissile())
			{
				//var averageSize = part.GetAverageBoundSize(); 
				//var sphereRadius = averageSize * 0.5f; //most parts are cylindrical, why use a sphere for cylindrical volume calcs?
				//var sphereSurface = 4 * Mathf.PI * sphereRadius * sphereRadius;
				//var structuralVolume = sphereSurface * 0.1f; 
				var averageSize = part.GetVolume(); // this grabs x/y/z dimensions from PartExtensions.cs 
				var structuralVolume = averageSize * 0.785f; //a cylinder diameter X length y is ~78.5% the volume of a rectangle of h/w x, length y. 
															 //(mk2 parts are ~66% volume of equivalent rectangle, but are reinforced hulls, so..
			//if (part.IsCone())                              //cones are ~36-37% volume
			//{                                               //parts that aren't cylinders or close enough and need exceptions: Wings, control surfaces, radiators/solar panels
			//	structuralVolume = averageSize * 0.368f;
			//}
				var dryPartmass = part.mass - part.resourceMass;
				var density = (dryPartmass * 1000) / structuralVolume;  // account for resource mass, density to be calc'd from drymass

				//var structuralMass = density * structuralVolume; // this means HP is solely determined my part mass, after assuming all parts have min density of 1000kg/m3
				//Debug.Log("[BDArmory]: Hitpoint Calc" + part.name + " | structuralMass : " + structuralMass);
				//3. final calculations
				//hitpoints = structuralMass * hitpointMultiplier * 0.33f; 

				if (dryPartmass < 1) //differentiate between sub ton and 1 ton + parts, the former need a boost to bave some health, the latter need a nerf or will have health for days
				{
					density = Mathf.Clamp(density, 150, 350);// things like crew cabins are heavy, but most of that mass isn't going to be structural plating, so lets limit structural density
															 // important to note: a lot of the HP values in the old system came from the calculation assuming everytihng had a minimum density of 1000kg/m3
					hitpoints = ((dryPartmass * density) * 20) * hitpointMultiplier * 0.33f; //multiplying mass by density extrapolates volume, so parts wit hthe same vol, but different mass appropriately affected (eg Mk1 strucural fuselage vs m1 LF tank
																							 //as well as parts of fdifferent vol, but same density - all fueltanks - similarly affected
					if (hitpoints > (dryPartmass * 3500) || hitpoints < (dryPartmass * 350))
					{
						//Debug.Log($"[BDArmory]: HitpointTracker::Clamping hitpoints for part {part.name}");
						hitpoints = Mathf.Clamp(hitpoints, (dryPartmass * 350), (dryPartmass * 3500)); // if HP is 10x more or 10x than 1/10th drymass in kg, clamp to 10x more/less
					}
				}
				else
				{
					density = Mathf.Clamp(density, 75, 175); //lower stuctural density on very large parts to preven HP bloat
					hitpoints = ((dryPartmass * density) * 10) * hitpointMultiplier * 0.33f;
					if (part.IsMotor())
					{
						hitpoints = ((dryPartmass * density) *4) * hitpointMultiplier * 0.33f; ; // engines in KSP very dense - leads to massive HP due to large mass, small volume. Engines also don't respond well to being shot, so...
					}                                       //^ may want to consider bumping this up to 4.5-5
					if (hitpoints > (dryPartmass * 2500) || hitpoints < (dryPartmass * 250))
					{
						//Debug.Log($"[BDArmory]: HitpointTracker::Clamping hitpoints for part {part.name}");
						hitpoints = Mathf.Clamp(hitpoints, (dryPartmass * 250), (dryPartmass * 2500)); // if HP is 10x more or 10x than 1/10th drymass in kg, clamp to 10x more/less
					}

				}

				//Debug.Log("[BDArmory]: Hitpoint Calc" + part.name + " | structuralVolume : " + structuralVolume);
				//Debug.Log("[BDArmory]: Hitpoint Calc" + part.name + " | dry mass : " + dryPartmass);
				//Debug.Log("[BDArmory]: Hitpoint Calc" + part.name + " | Density : " + density);

				if (part.IsAero())
				{
					hitpoints = (dryPartmass * 1000) * 3.5f * hitpointMultiplier * 0.33f; // stock wings are half the mass of proc wings, at least in FAR. Will need to check stock aero wing masses.

					if (part.name.Contains("B9.Aero.Wing.Procedural")) //Only IDs B9 proc wings, no others. Find a better way besides hardcoding in a reference to this specific trio of parts?
					{
						hitpoints = (dryPartmass * 1000) * 1.75f * hitpointMultiplier * 0.33f; // since wings are basically a 2d object, lets have mass be our scalar - afterall, 2x the mass will ~= 2x the surface area
					}
					Debug.Log("[BDArmory]: Hitpoint Calc" + part.name + " | Is Aero part");
				}
				if (part.IsCtrlSrf())
				{
					hitpoints = (((dryPartmass * 1000) * 3.5f) + 100) * hitpointMultiplier * 0.33f; // Crtl surfaces will have actuators of some flavor, are going to be more vulnerable to damage. +100 for guaranteed min health
				}

				hitpoints = Mathf.CeilToInt(hitpoints / HpRounding) * HpRounding;
				if (hitpoints <= 50) // this could also be boosted to increase global HP floor
				{
					hitpoints = 50; // maybe 200 or so?
				}
			}
			else
			{
				hitpoints = 5;
				Armor = 2;
			}

        public void DestroyPart()
        {
            if (part.mass <= 2f) part.explosionPotential *= 0.85f;

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

            partdamage = Mathf.Max(partdamage, 0.01f) * -1;
            Hitpoints += partdamage;

            if (Hitpoints <= 0)
            {
                DestroyPart();
            }
        }

        public void AddDamageToKerbal(KerbalEVA kerbal, float damage)
        {
            damage = Mathf.Max(damage, 0.01f) * -1;
            Hitpoints += damage;

            if (Hitpoints <= 0)
            {
                // oh the humanity!
                PartExploderSystem.AddPartToExplode(kerbal.part);
            }
        }

        public void ReduceArmor(float massToReduce)
        {
            Armor -= massToReduce;
            if (Armor < 0)
            {
                Armor = 0;
            }
        }

        public void overrideArmorSetFromConfig(float thickness = 0)
        {
            ArmorSet = true;
            if (ArmorThickness != 0)
            {
                Armor = ArmorThickness;
            }
        }

        #endregion Hitpoints Functions
    }
}
