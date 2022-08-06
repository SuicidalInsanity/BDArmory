using System.Collections;
using System.Collections.Generic;
using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;

using BDArmory.Armor;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    internal class BDAEditorArmorWindow : MonoBehaviour
    {
        public static BDAEditorArmorWindow Instance = null;
        private ApplicationLauncherButton toolbarButton = null;

        private bool showArmorWindow = false;
        private bool showHullMenu = false;
        private string windowTitle = Localizer.Format("#LOC_BDArmory_ArmorTool");
        private Rect windowRect = new Rect(300, 150, 300, 350);
        private float lineHeight = 20;
        private float height = 20;
        private GUIContent[] armorGUI;
        private GUIContent armorBoxText;
        private BDGUIComboBox armorBox;
        private int previous_index = -1;

        private float totalArmorMass;
        private float totalArmorCost;
        private float totalLift;
        private float wingLoading;
        private bool CalcArmor = false;
        private bool shipModifiedfromCalcArmor = false;
        private bool SetType = false;
        private bool SetThickness = false;
        private string selectedArmor = "None";
        private bool ArmorStats = false;
        private float ArmorDensity = 0;
        private float ArmorStrength = 200;
        private float ArmorHardness = 300;
        private float ArmorDuctility = 0.6f;
        private float ArmorDiffusivity = 237;
        private float ArmorMaxTemp = 993;
        private float ArmorCost = 0;
        private bool armorslist = false;
        private float Thickness = 10;
        private bool useNumField = false;
        private float oldThickness = 10;
        private float maxThickness = 60;
        private bool Visualizer = false;
        private bool HPvisualizer = false;
        private bool HullVisualizer = false;
        private bool LiftVisualizer = false;
        private bool oldVisualizer = false;
        private bool oldHPvisualizer = false;
        private bool oldHullVisualizer = false;
        private bool oldLiftVisualizer = false;
        private bool refreshVisualizer = false;
        private bool refreshHPvisualizer = false;
        private bool refreshHullvisualizer = true;
        private bool refreshLiftvisualizer = false;
        private bool isWood = false;
        private bool isSteel = false;
        private bool isAluminium = true;
        private int hullmat = 2;

        private float steelValue = 1;
        private float armorValue = 1;
        private float relValue = 1;
        private float exploValue;

        Dictionary<string, NumericInputField> thicknessField;
        void Awake()
        {
        }

        void Start()
        {
            Instance = this;
            AddToolbarButton();
            thicknessField = new Dictionary<string, NumericInputField>
            {
                {"Thickness", gameObject.AddComponent<NumericInputField>().Initialise(0, 10, 0, 1500) }, // FIXME should use maxThickness instead of 1500 here.
            };
            GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
            /*
            var modifiedCaliber = (15) + (15) * (2f * 0.15f * 0.15f);
            float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(0.388f, 1109);
            float yieldStrength = modifiedCaliber * modifiedCaliber * Mathf.PI / 100f * 940 * 30;
            if (ArmorDuctility > 0.25f)
            {
                yieldStrength *= 0.7f;
            }
            float newCaliber = ProjectileUtils.CalculateDeformation(yieldStrength, bulletEnergy, 30, 1109, 1176, 7850, 0.19f, 0.8f, false);
            */
            //steelValue = ProjectileUtils.CalculatePenetration(30, newCaliber, 0.388f, 1109, 0.15f, 7850, 940, 30, 0.8f, false);
            steelValue = ProjectileUtils.CalculatePenetration(30, 1109, 0.388f, 0.8f, 940, 8.45001135e-07f, 0.656060636f, 1.20190930f, 1.77791929f);
            exploValue = 940 * 1.15f * 7.85f;
        }

        private void FillArmorList()
        {
            armorGUI = new GUIContent[ArmorInfo.armors.Count];
            for (int i = 0; i < ArmorInfo.armors.Count; i++)
            {
                GUIContent gui = new GUIContent(ArmorInfo.armors[i].name);
                armorGUI[i] = gui;
            }

            armorBoxText = new GUIContent();
            armorBoxText.text = Localizer.Format("#LOC_BDArmory_ArmorSelect");
        }

        private void OnEditorShipModifiedEvent(ShipConstruct data)
        {
            delayedRefreshVisuals = true;
            if (!delayedRefreshVisualsInProgress)
                StartCoroutine(DelayedRefreshVisuals());
        }

        private bool delayedRefreshVisuals = false;
        private bool delayedRefreshVisualsInProgress = false;
        IEnumerator DelayedRefreshVisuals()
        {
            delayedRefreshVisualsInProgress = true;
            while (delayedRefreshVisuals) // Wait until ship modified events stop coming.
            {
                delayedRefreshVisuals = false;
                yield return null;
                yield return null; // Two yield nulls to wait for HP changes to delayed ship modified events in HitpointTracker
            }
            delayedRefreshVisualsInProgress = false;

            if (showArmorWindow)
            {
                if (!shipModifiedfromCalcArmor)
                {
                    CalcArmor = true;
                }
                if (Visualizer || HPvisualizer || HullVisualizer || LiftVisualizer)
                {
                    refreshVisualizer = true;
                    refreshHPvisualizer = true;
                    refreshHullvisualizer = true;
                    refreshLiftvisualizer = true;
                }
                shipModifiedfromCalcArmor = false;
            }
        }

        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnEditorShipModifiedEvent);

            if (toolbarButton)
            {
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
                toolbarButton = null;
            }
        }

        IEnumerator ToolbarButtonRoutine()
        {
            if (toolbarButton || (!HighLogic.LoadedSceneIsEditor)) yield break;
            while (!ApplicationLauncher.Ready)
            {
                yield return null;
            }

            AddToolbarButton();
        }

        void AddToolbarButton()
        {
            if (HighLogic.LoadedSceneIsEditor && !BDArmorySettings.LEGACY_ARMOR)
            {
                if (toolbarButton == null)
                {
                    Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon_Armor", false);
                    toolbarButton = ApplicationLauncher.Instance.AddModApplication(ShowToolbarGUI, HideToolbarGUI, Dummy, Dummy, Dummy, Dummy, ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB, buttonTexture);
                }
            }
        }

        public void ShowToolbarGUI()
        {
            showArmorWindow = true;
            CalculateArmorMass();
        }

        public void HideToolbarGUI()
        {
            showArmorWindow = false;
            CalcArmor = false;
            Visualizer = false;
            HPvisualizer = false;
            HullVisualizer = false;
            LiftVisualizer = false;
            Visualize();
        }

        void Dummy()
        { }

        void OnGUI()
        {
            if (showArmorWindow)
            {
                windowRect = GUI.Window(this.GetInstanceID(), windowRect, WindowArmor, windowTitle, BDArmorySetup.BDGuiSkin.window);
            }
            PreventClickThrough();
        }

        void WindowArmor(int windowID)
        {
            if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), "X"))
            {
                HideToolbarGUI();
            }
            if (CalcArmor)
            {
                CalcArmor = false;
                SetType = false;
                CalculateArmorMass();
            }

            GUIStyle style = BDArmorySetup.BDGuiSkin.label;

            useNumField = GUI.Toggle(new Rect(windowRect.width - 36, 2, 16, 16), useNumField, "#", useNumField ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);

            float line = 1.5f;

            style.fontStyle = FontStyle.Normal;

            if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorHPVisualizer"), HPvisualizer ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                HPvisualizer = !HPvisualizer;
                if (HPvisualizer)
                {
                    Visualizer = false;
                    HullVisualizer = false;
                    LiftVisualizer = false;
                }
            }
            line += 1.25f;


            if (!BDArmorySettings.RESET_ARMOUR)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorVisualizer"), Visualizer ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
                {
                    Visualizer = !Visualizer;
                    if (Visualizer)
                    {
                        HPvisualizer = false;
                        HullVisualizer = false;
                        LiftVisualizer = false;
                    }
                }
                line += 1.25f;
            }

            if (!BDArmorySettings.RESET_HULL)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorHullVisualizer"), HullVisualizer ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
                {
                    HullVisualizer = !HullVisualizer;
                    if (HullVisualizer)
                    {
                        HPvisualizer = false;
                        Visualizer = false;
                        LiftVisualizer = false;
                    }
                }
                line += 1.25f;
            }

            if (!FerramAerospace.hasFAR)
            {
                if (GUI.Button(new Rect(10, line * lineHeight, 280, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorLiftVisualizer"), LiftVisualizer ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
                {
                    LiftVisualizer = !LiftVisualizer;
                    if (LiftVisualizer)
                    {
                        Visualizer = false;
                        HullVisualizer = false;
                        HPvisualizer = false;
                    }
                }
                line += 1.25f;
            }

            line += 0.25f;

            if ((refreshHPvisualizer || HPvisualizer != oldHPvisualizer) || (refreshVisualizer || Visualizer != oldVisualizer) || (refreshHullvisualizer || HullVisualizer != oldHullVisualizer) || (refreshLiftvisualizer || LiftVisualizer != oldLiftVisualizer))
            {
                Visualize();
            }

            if (!BDArmorySettings.RESET_ARMOUR)
            {
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorThickness") + ": " + Thickness + "mm", style);
                line++;
                if (!useNumField)
                {
                    Thickness = GUI.HorizontalSlider(new Rect(20, line * lineHeight, 260, lineHeight), Thickness, 0, maxThickness);
                    //Thickness /= 5;
                    Thickness = Mathf.Round(Thickness);
                    //Thickness *= 5;
                    line++;
                }
                else
                {
                    thicknessField["Thickness"].tryParseValue(GUI.TextField(new Rect(20, line * lineHeight, 260, lineHeight), thicknessField["Thickness"].possibleValue, 4));
                    Thickness = Mathf.Min((float)thicknessField["Thickness"].currentValue, maxThickness); // FIXME Mathf.Min shouldn't be necessary if the maxValue of the thicknessField has been updated for maxThickness
                    line++;
                }
                line += 0.75f;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorTotalMass") + ": " + totalArmorMass.ToString("0.00"), style);
                line++;
                GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorTotalCost") + ": " + Mathf.Round(totalArmorCost), style);
                if (!FerramAerospace.hasFAR)
                {
                    line++;
                    GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorTotalLift") + ": " + totalLift.ToString("0.00"), style);
                    line++;
                    GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorWingLoading") + ": " + wingLoading.ToString("0.00"), style);
                }
                line += 1.5f;
            }
            float StatLines = 0;
            float armorLines = 0;
            if (!BDArmorySettings.RESET_ARMOUR)
            {
                if (Thickness != oldThickness)
                {
                    oldThickness = Thickness;
                    SetThickness = true;
                    maxThickness = 10;
                    thicknessField["Thickness"].maxValue = maxThickness;
                    CalculateArmorMass();
                }
                GUI.Label(new Rect(40, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorSelect"), style);
                line++;
                if (!armorslist)
                {
                    FillArmorList();
                    GUIStyle listStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
                    listStyle.fixedHeight = 18; //make list contents slightly smaller
                    armorBox = new BDGUIComboBox(new Rect(10, line * lineHeight, 280, lineHeight), new Rect(10, line * lineHeight, 280, lineHeight), armorBoxText, armorGUI, 120, listStyle);
                    armorslist = true;
                }

                int selected_index = armorBox.Show();
                armorLines++;
                if (armorBox.isOpen)
                {
                    armorLines += 6;
                }
                if (selected_index != previous_index)
                {
                    if (selected_index != -1)
                    {
                        selectedArmor = ArmorInfo.armors[selected_index].name;
                        SetType = true;
                        CalculateArmorMass();
                        CalculateArmorStats();
                    }
                    previous_index = selected_index;
                    CalculateArmorMass();
                }
                line += 0.5f;
                if (GameSettings.ADVANCED_TWEAKABLES)
                {
                    ArmorStats = GUI.Toggle(new Rect(10, (line + armorLines) * lineHeight, 280, lineHeight), ArmorStats, Localizer.Format("#LOC_BDArmory_ArmorStats"), ArmorStats ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                    StatLines++;
                    if (ArmorStats)
                    {
                        if (selectedArmor != "None")
                        {
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorStrength") + " " + ArmorStrength, style);
                            //StatLines++;
                            GUI.Label(new Rect(135, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorHardness") + " " + ArmorHardness, style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorDuctility") + " " + ArmorDuctility, style);
                            //StatLines++;
                            GUI.Label(new Rect(135, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorDiffusivity") + " " + ArmorDiffusivity, style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorMaxTemp") + " " + ArmorMaxTemp + " K", style);
                            //StatLines++;
                            GUI.Label(new Rect(135, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorDensity") + " " + ArmorDensity + " kg/m3", style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 120, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorCost") + " " + ArmorCost + "/m3", style);
                            StatLines++;
                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_BulletResist") + ": " + (relValue < 1.2 ? (relValue < 0.5 ? "* * * * *" : "* * * *") : (relValue > 2.8 ? (relValue > 4 ? "*" : "* *") : "* * *")), style);
                            StatLines++;

                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_ExplosionResist") + ": " + ((ArmorDuctility < 0.05f && ArmorHardness < 500) ? "* *" : (exploValue > 8000 ? (exploValue > 20000 ? "* * * * *" : "* * * *") : (exploValue < 4000 ? (exploValue < 2000 ? "*" : "* *") : "* * *"))), style);
                            StatLines++;

                            GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_LaserResist") + ": " + (ArmorDiffusivity > 150 ? (ArmorDiffusivity > 199 ? "* * * * *" : "* * * *") : (ArmorDiffusivity < 50 ? (ArmorDiffusivity < 10 ? "*" : "* *") : "* * *")), style);
                            StatLines++;

                            if (ArmorDuctility < 0.05)
                            {
                                if (ArmorHardness > 500) GUI.Label(new Rect(15, (line + armorLines + StatLines) * lineHeight, 260, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorShatterWarning"), style);
                                StatLines++;
                            }
                        }
                        if (selectedArmor != "Mild Steel" && selectedArmor != "None")
                        {
                            GUI.Label(new Rect(10, (line + armorLines + StatLines) * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_EquivalentThickness") + ": " + relValue * Thickness + "mm", style);
                            line++;
                        }
                    }
                }
            }
            float HullLines = 0;
            if (!BDArmorySettings.RESET_HULL)
            {
                line += 0.5f;
                showHullMenu = GUI.Toggle(new Rect(10, (line + armorLines + StatLines) * lineHeight, 280, lineHeight),
                    showHullMenu, Localizer.Format("#LOC_BDArmory_Armor_HullMat"), showHullMenu ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                HullLines += 1.15f;

                if (showHullMenu)
                {
                    if (isSteel != (isSteel = GUI.Toggle(new Rect(10, (line + armorLines + StatLines + HullLines) * lineHeight, 280, lineHeight), isSteel, Localizer.Format("#LOC_BDArmory_Steel"), isSteel ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)))
                    {
                        if (isSteel)
                        {
                            isWood = false;
                            isAluminium = false;
                            hullmat = 3;
                            CalculateArmorMass(true);
                        }
                    }
                    HullLines += 1.15f;
                    if (isWood != (isWood = GUI.Toggle(new Rect(10, (line + armorLines + StatLines + HullLines) * lineHeight, 280, lineHeight), isWood, Localizer.Format("#LOC_BDArmory_Wood"), isWood ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)))
                    {
                        if (isWood)
                        {
                            isAluminium = false;
                            isSteel = false;
                            hullmat = 1;
                            CalculateArmorMass(true);
                        }
                    }
                    HullLines += 1.15f;
                    if (isAluminium != (isAluminium = GUI.Toggle(new Rect(10, (line + armorLines + StatLines + HullLines) * lineHeight, 280, lineHeight), isAluminium, Localizer.Format("#LOC_BDArmory_Aluminium"), isAluminium ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)))
                    {
                        if (isAluminium)
                        {
                            isWood = false;
                            isSteel = false;
                            hullmat = 2;
                            CalculateArmorMass(true);
                        }
                    }
                    HullLines += 1.15f;
                    if (!isSteel && !isWood && !isAluminium)
                    {
                        isAluminium = true;
                        hullmat = 2;
                        CalculateArmorMass(true);
                    }
                }
            }
            line += 0.5f;
            GUI.DragWindow();
            height = Mathf.Lerp(height, (line + armorLines + StatLines + HullLines) * lineHeight, 0.15f);
            windowRect.height = height;
            GUIUtils.RepositionWindow(ref windowRect);
        }

        void CalculateArmorMass(bool vesselmass = false)
        {
            if (EditorLogic.RootPart == null)
                return;

            bool modified = false;
            totalArmorMass = 0;
            totalArmorCost = 0;
            var selectedArmorIndex = ArmorInfo.armors.FindIndex(t => t.name == selectedArmor);
            if (selectedArmorIndex < 0)
                return;
            using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.IsMissile()) continue;
                    HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                    if (armor != null)
                    {
                        if (!vesselmass)
                        {
                            if (armor.maxSupportedArmor > maxThickness)
                            {
                                maxThickness = armor.maxSupportedArmor;
                                thicknessField["Thickness"].maxValue = maxThickness;
                            }
                            if (SetType || SetThickness)
                            {
                                if (SetThickness)
                                {
                                    if (armor.ArmorTypeNum > 1)
                                    {
                                        armor.Armor = Mathf.Clamp(Thickness, 0, armor.maxSupportedArmor);
                                    }
                                }
                                if (SetType)
                                {
                                    armor.ArmorTypeNum = selectedArmorIndex + 1;
                                    if (armor.ArmorThickness > 10)
                                    {
                                        if (armor.ArmorTypeNum < 2)
                                        {
                                            armor.ArmorTypeNum = 2; //don't set armor type "none" for armor panels
                                        }
                                        if (armor.maxSupportedArmor > maxThickness)
                                        {
                                            maxThickness = armor.maxSupportedArmor;
                                            thicknessField["Thickness"].maxValue = maxThickness;
                                        }
                                    }
                                }
                                armor.ArmorModified(null, null);
                                modified = true;
                            }
                            totalArmorMass += armor.armorMass;
                            totalArmorCost += armor.armorCost;
                        }
                        else
                        {
                            armor.HullTypeNum = hullmat;
                            armor.HullModified(null, null);
                            modified = true;
                        }

                    }
                }
            CalcArmor = false;
            if ((SetType || SetThickness) && (Visualizer || HPvisualizer))
            {
                refreshVisualizer = true;
            }
            SetType = false;
            SetThickness = false;
            ArmorCost = ArmorInfo.armors[selectedArmorIndex].Cost;
            ArmorDensity = ArmorInfo.armors[selectedArmorIndex].Density;
            ArmorDiffusivity = ArmorInfo.armors[selectedArmorIndex].Diffusivity;
            ArmorDuctility = ArmorInfo.armors[selectedArmorIndex].Ductility;
            ArmorHardness = ArmorInfo.armors[selectedArmorIndex].Hardness;
            ArmorMaxTemp = ArmorInfo.armors[selectedArmorIndex].SafeUseTemp;
            ArmorStrength = ArmorInfo.armors[selectedArmorIndex].Strength;

            if (modified)
            {
                shipModifiedfromCalcArmor = true;
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }

            if (!FerramAerospace.hasFAR)
                CalculateTotalLift(); // Re-calculate lift and wing loading on armor change
        }

        void CalculateTotalLift()
        {
            if (EditorLogic.RootPart == null)
                return;

            totalLift = 0;
            using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.IsMissile()) continue;
                    ModuleLiftingSurface wing = parts.Current.GetComponent<ModuleLiftingSurface>();
                    if (wing != null)
                    {
                        totalLift += wing.deflectionLiftCoeff * Vector3.Project(wing.transform.forward, Vector3.up).sqrMagnitude; // Only return vertically oriented lift components
                    }
                }
            wingLoading = totalLift / EditorLogic.fetch.ship.GetTotalMass();
        }

        void Visualize()
        {
            if (EditorLogic.RootPart == null)
                return;
            if (Visualizer || HPvisualizer || HullVisualizer || LiftVisualizer)
            {
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        if (parts.Current.name.Contains("conformaldecals")) continue;
                        HitpointTracker a = parts.Current.GetComponent<HitpointTracker>();
                        if (a != null)
                        {
                            Color VisualizerColor = Color.HSVToRGB((Mathf.Clamp(a.Hitpoints, 100, 1600) / 1600) / 3, 1, 1);
                            if (Visualizer)
                            {
                                VisualizerColor = Color.HSVToRGB(a.ArmorTypeNum / (ArmorInfo.armors.Count + 1), (a.Armor / maxThickness), 1f);
                            }
                            if (HullVisualizer)
                            {
                                VisualizerColor = Color.HSVToRGB(a.HullTypeNum / 3, 1, 1f);
                            }
                            if (LiftVisualizer)
                            {
                                ModuleLiftingSurface wing = parts.Current.GetComponent<ModuleLiftingSurface>();
                                if (wing != null)
                                    VisualizerColor = Color.HSVToRGB(Mathf.Clamp01(Mathf.Log10(wing.deflectionLiftCoeff + 1f)) / 3, 1, 1);
                                else
                                    VisualizerColor = Color.HSVToRGB(0, 1, 1);
                            }
                            var r = parts.Current.GetComponentsInChildren<Renderer>();
                            {
                                if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                {
                                    if (!a.RegisterProcWingShader) //procwing defaultshader left null on start so current shader setup can be grabbed at visualizer runtime
                                    {
                                        for (int s = 0; s < r.Length; s++)
                                        {
                                            a.defaultShader.Add(r[s].material.shader);
                                            //Debug.Log("[Visualizer] " + parts.Current.name + " shader is " + r[s].material.shader.name);
                                            if (r[s].material.HasProperty("_Color"))
                                            {
                                                a.defaultColor.Add(r[s].material.color);
                                            }
                                        }
                                        a.RegisterProcWingShader = true;
                                    }
                                }
                                for (int i = 0; i < r.Length; i++)
                                {
                                    if (r[i].material.shader.name.Contains("Alpha")) continue;
                                    r[i].material.shader = Shader.Find("KSP/Unlit");
                                    if (r[i].material.HasProperty("_Color"))
                                    {
                                        r[i].material.SetColor("_Color", VisualizerColor);
                                    }
                                }
                            }
                            //Debug.Log("[VISUALIZER] modding shaders on " + parts.Current.name);//can confirm that procwings aren't getting shaders applied, yet they're still getting applied. 
                            //at least this fixes the procwings widgets getting colored
                        }
                    }
            }
            if (!Visualizer && !HPvisualizer && !HullVisualizer && !LiftVisualizer)
            {
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                        if (parts.Current.name.Contains("conformaldecals")) continue;
                        //so, this gets called when GUI closed, without touching the hp/armor visualizer at all.
                        //Now, on GUI close, it runs the latter half of visualize to shut off any visualizer effects and reset stuff.
                        //Procs wings turn orange at this point... oh. That's why: The visualizer reset is grabbing a list of shaders and colors at *part spawn!*
                        //pWings use dynamic shaders to paint themselves, so it's not reapplying the latest shader /color config, but the initial one, the one from the part icon  
                        var r = parts.Current.GetComponentsInChildren<Renderer>();
                        if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                        {
                            if (!armor.RegisterProcWingShader) //procwing defaultshader left null on start so current shader setup can be grabbed at visualizer runtime
                            {
                                for (int s = 0; s < r.Length; s++)
                                {
                                    armor.defaultShader.Add(r[s].material.shader);
                                    //Debug.Log("[Visualizer] " + parts.Current.name + " shader is " + r[s].material.shader.name);
                                    if (r[s].material.HasProperty("_Color"))
                                    {
                                        armor.defaultColor.Add(r[s].material.color);
                                    }
                                }
                                armor.RegisterProcWingShader = true;
                            }
                        }
                        //Debug.Log("[VISUALIZER] applying shader to " + parts.Current.name);
                        for (int i = 0; i < r.Length; i++)
                        {
                            try
                            {
                                if (r[i].material.shader != armor.defaultShader[i])
                                {
                                    if (armor.defaultShader[i] != null)
                                    {
                                        r[i].material.shader = armor.defaultShader[i];
                                    }
                                    if (armor.defaultColor[i] != null)
                                    {
                                        if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                        {
                                            //r[i].material.SetColor("_Emissive", armor.defaultColor[i]); //?
                                            r[i].material.SetColor("_MainTex", armor.defaultColor[i]); //this doesn't work either
                                            //LayeredSpecular has _MainTex, _Emissive, _SpecColor,_RimColor, _TemperatureColor, and _BurnColor
                                            // source: https://github.com/tetraflon/B9-PWings-Modified/blob/master/B9%20PWings%20Fork/shaders/SpecularLayered.shader
                                            //This works.. occasionally. Sometimes it will properly reset pwing tex/color, most of the time it doesn't. need to test later
                                        }
                                        else
                                        {
                                            r[i].material.SetColor("_Color", armor.defaultColor[i]);
                                        }
                                    }
                                    else
                                    {
                                        if (parts.Current.name.Contains("B9.Aero.Wing.Procedural"))
                                        {
                                            //r[i].material.SetColor("_Emissive", Color.white);
                                            r[i].material.SetColor("_MainTex", Color.white);
                                        }
                                        else
                                        {
                                            r[i].material.SetColor("_Color", Color.white);
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                //Debug.Log("[BDAEditorArmorWindow]: material on " + parts.Current.name + "could not find default shader/color");
                            }
                        }
                    }
            }
            oldVisualizer = Visualizer;
            oldHPvisualizer = HPvisualizer;
            oldHullVisualizer = HullVisualizer;
            oldLiftVisualizer = LiftVisualizer;
            refreshVisualizer = false;
            refreshHPvisualizer = false;
            refreshHullvisualizer = false;
        }

        /// <summary>
        /// Lock the model if our own window is shown and has cursor focus to prevent click-through.
        /// Code adapted from FAR Editor GUI
        /// </summary>
        private void PreventClickThrough()
        {
            bool cursorInGUI = false;
            EditorLogic EdLogInstance = EditorLogic.fetch;
            if (!EdLogInstance)
            {
                return;
            }
            if (showArmorWindow)
            {
                cursorInGUI = windowRect.Contains(GetMousePos());
            }
            if (cursorInGUI)
            {
                if (!CameraMouseLook.GetMouseLook())
                    EdLogInstance.Lock(false, false, false, "BDAArmorLOCK");
                else
                    EdLogInstance.Unlock("BDAArmorLOCK");
            }
            else if (!cursorInGUI)
            {
                EdLogInstance.Unlock("BDAArmorLOCK");
            }
        }

        private Vector3 GetMousePos()
        {
            Vector3 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            return mousePos;
        }

        private void CalculateArmorStats()
        {
            if (selectedArmor == "Mild Steel")
            {
                relValue = 1;
            }
            else
            {
                /*float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(0.388f, 1109);
                var modifiedCaliber = (15) + (15) * (2f * ArmorDuctility * ArmorDuctility);
                float yieldStrength = modifiedCaliber * modifiedCaliber * Mathf.PI / 100f * ArmorStrength * (ArmorDensity / 7850f) * 30;
                if (ArmorDuctility > 0.25f)
                {
                    yieldStrength *= 0.7f;
                }
                float newCaliber = ProjectileUtils.CalculateDeformation(yieldStrength, bulletEnergy, 30, 1109, 1176, 7850, 0.19f, 0.8f, false);
                */
                //armorValue = ProjectileUtils.CalculatePenetration(30, newCaliber, 0.388f, 1109, ArmorDuctility, ArmorDensity, ArmorStrength, 30, 0.8f, false);
                armorValue = ProjectileUtils.CalculatePenetration(30, 1109, 0.388f, 0.8f, 940, 8.45001135e-07f, 0.656060636f, 1.20190930f, 1.77791929f);
                relValue = Mathf.Round(armorValue / steelValue * 10) / 10;
                exploValue = ArmorStrength * (1 + ArmorDuctility) * (ArmorDensity / 1000);
            }
        }
    }
}
