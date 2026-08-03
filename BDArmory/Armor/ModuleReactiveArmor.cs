using UnityEngine;

using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.Utils;
using System;

namespace BDArmory.Armor
{
    public class ModuleReactiveArmor : BDAPartModule
    {
        [KSPField]
        public string sectionTransformName = "sections";

        [KSPField]
        public string armorName = "Reactive Armor";

        Transform[] sections;
        int[] sectionIndexes;

        [KSPField]
        public bool NXRA = false; //non-explosive reactive armor?

        [KSPField]
        public float SectionHP = 300; //non-explosive reactive armor?

        [KSPField]
        public float sensitivity = 30; //minimum caliber to trigger RA

        [KSPField]
        public float armorModifier = 1.25f; //armor thickness modifier

        [KSPField]
        public float ERAflyerPlateHalfDimension = 0.25f; //half of the average length of the flyer plate

        [KSPField]
        public float ERAgurneyConstant = 2700f; //gurney specific energy of the ERA, equal to sqrt(2E) (in m/s)

        [KSPField]
        public float ERArelativeEffectiveness = 1.72f; //tnt RE of the ERA explosive

        [KSPField]
        public float ERAexplosiveMass = 5f; //ERA explosive mass (in kg)

        [KSPField]
        public float ERAexplosiveDensity = 1650f; //ERA explosive density (in kg/m^3)

        [KSPField]
        public bool ERAbackingPlate = true; //backing plate ?

        [KSPField]
        public float ERAspacing = 0.1f; //spacing between back plate and armor

        [KSPField]
        public float ERAdetonationDelay = 50f; //detonation delay (in microseconds)

        [KSPField]
        public float ERAplateThickness = 16f; //plate thickness (in mm)

        [KSPField]
        public string ERAplateMaterial = "Mild Steel"; //plate material

        public int sectionsRemaining = 1;
        private int sectionsCount = 1;
        public float ERAexplosiveThickness { get; private set; } = -1f;

        Vector3 direction = default(Vector3);

        private string ExploModelPath = "BDArmory/Models/explosion/CASEexplosion";
        private string explSoundPath = "BDArmory/Sounds/explode1";
        public string SourceVessel = "";

        public void Start()
        {
            if (!NXRA) MakeArmorSectionArray(); //non-reactive armor doesn't need to compartmentalize HP into sections
            //UpdateSectionScales();
            if (HighLogic.LoadedSceneIsFlight)
            {
                SourceVessel = part.vessel.GetName();
            }
        }

        void OnGUI()
        {
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DEBUG_ARMOR)
            {
                try
                {
                    for (int i = 0; i < sectionsCount; ++i)
                    {
                        if (sectionIndexes[i] >= 0)
                        {
                            GUIUtils.DrawLineBetweenWorldPositions(sections[sectionIndexes[i]].position,
                                sections[sectionIndexes[i]].position + sections[sectionIndexes[i]].forward, 1, Color.blue);
                            GUIUtils.DrawLineBetweenWorldPositions(sections[sectionIndexes[i]].position,
                                sections[sectionIndexes[i]].position + sections[sectionIndexes[i]].up, 1, Color.red);
                            GUIUtils.DrawLineBetweenWorldPositions(sections[sectionIndexes[i]].position,
                                sections[sectionIndexes[i]].position + sections[sectionIndexes[i]].right, 1, Color.green);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BDArmory.MissileLauncher]: Exception thrown in OnGUI: " + e.Message + "\n" + e.StackTrace);
                }
            }
        }
        void MakeArmorSectionArray()
        {
            Transform segmentsTransform = part.FindModelTransform(sectionTransformName);
            sectionsCount = segmentsTransform.childCount;
            sections = new Transform[sectionsCount];
            sectionIndexes = new int[sectionsCount];
            for (int i = 0; i < sectionsCount; i++)
            {
                string sectionName = segmentsTransform.GetChild(i).name;
                int sectionIndex = int.Parse(sectionName.Substring(8)) - 1;
                sections[sectionIndex] = segmentsTransform.GetChild(i);
                sectionIndexes[sectionIndex] = i;
            }
            sectionIndexes.Shuffle();
            //sections.Shuffle(); //randomize order sections get removed
            sectionsRemaining = sectionsCount;
            var HP = part.FindModuleImplementing<HitpointTracker>();
            if (HP != null)
            {
                HP.maxHitPoints = (sectionsCount * SectionHP); //set HP based on number of sections
                HP.Hitpoints = (sectionsCount * SectionHP); 
                HP.SetupPrefab(); //and update hitpoint slider

                HP.Armor = ERAplateThickness;
                HP.ArmorThickness = ERAplateThickness;
                HP.ArmorTypeNum = ArmorInfo.armors.FindIndex(t => t.name == ERAplateMaterial) + 1;
                if (HP.ArmorTypeNum == 0)
                {
                    HP.ArmorTypeNum = ArmorInfo.armors.FindIndex(t => t.name == "None") + 1;
                    Debug.LogWarning($"[BDArmory.ReactiveArmor] WARNING: Part {part.name} has invalid armor type: {ERAplateMaterial}. Defaulted to Aluminum. Please fix ASAP!");
                }
                HP.ArmorSetup(null, null);
            }

            if (ERAbackingPlate)
            {
                ERAexplosiveThickness = 1000f * (ERAexplosiveMass / (ERAflyerPlateHalfDimension * ERAflyerPlateHalfDimension * ERAexplosiveDensity));
            }
        }

        public void UpdateSectionScales(int sectionDestroyed = -1, bool directionInput = false, Vector3 directionIn = default)
        {
            int destroyedIndex = -1;
            if (sectionDestroyed < 0)
                for (int i = 0; i < sectionsCount; ++i)
                {
                    sectionDestroyed = sectionIndexes[i];
                    if (sectionDestroyed >= 0)
                    {
                        destroyedIndex = i;
                        break;
                    }
                }
            else
                for (int i = 0; i < sectionsCount; ++i)
                {
                    if (sectionDestroyed == sectionIndexes[i])
                    {
                        destroyedIndex = i;
                        break;
                    }
                }

            if (directionInput)
                direction = -directionIn;
            else
                direction = -sections[sectionDestroyed].forward;

            ExplosionFx.CreateExplosion(sections[sectionDestroyed].transform.position, ERAexplosiveMass * ERArelativeEffectiveness * (ERAbackingPlate ? 1.5f: 1f), ExploModelPath, explSoundPath, ExplosionSourceType.BattleDamage, 30, part, SourceVessel, null, armorName, direction, 30, true);
            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.ReactiveArmor]: Removing section: {sectionDestroyed}, " + sectionsRemaining + " sections left");
            sectionsRemaining--;
            if (sectionsRemaining < 1 || destroyedIndex < 0)
            {
                part.Destroy();
            }
            else
            {
                var HP = part.FindModuleImplementing<HitpointTracker>();
                if (HP != null)
                {
                    HP.Hitpoints = Mathf.Clamp(HP.Hitpoints, 0, sectionsRemaining * SectionHP);
                }
                if (HP.Hitpoints < 0) part.Destroy();
            }
                
            sections[sectionDestroyed].localScale = Vector3.zero;
            sectionIndexes[destroyedIndex] = -1;
            /*for (int i = 0; i < sectionsCount; i++)
            {
                if (i < sectionsRemaining) sections[i].localScale = Vector3.one;
                else sections[i].localScale = Vector3.zero;
            }*/
        }
    }
}
