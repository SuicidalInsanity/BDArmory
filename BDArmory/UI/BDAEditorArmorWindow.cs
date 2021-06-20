using System;
using System.Collections;
using System.Collections.Generic;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Module;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.Radar;
using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;

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
        private bool CalcArmor = false;
        private bool SetType = false;
        private bool SetThickness = false;
        private string selectedArmor = "None";
        private bool armorslist = false;
        private float Thickness = 10;
        private float oldThickness = -1;
        private float maxThickness = 60;
        private bool Visualizer = false;
        private bool oldVisualizer = false;
        private bool refreshVisualizer = false;
        private float updateTimer = 0;
        private bool isWood = false;
        private bool isSteel = false;
        private bool isAluminium = true;
        private int hullmat = 2;
        void Awake()
        {
        }

        void Start()
        {
            Instance = this;
            AddToolbarButton();
            GameEvents.onEditorShipModified.Add(OnEditorShipModifiedEvent);
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
            CalcArmor = true;
            refreshVisualizer = true;
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
            if (HighLogic.LoadedSceneIsEditor)
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
            CalcArmor = true;
        }

        public void HideToolbarGUI()
        {
            showArmorWindow = false;
            CalcArmor = false;
        }

        void Dummy()
        { }

        private void Update()
        {
            if (showArmorWindow)
            {
                updateTimer -= Time.fixedDeltaTime;

                if (updateTimer < 0)
                {
                    CalcArmor = true;
                    updateTimer = 0.5f;    //next update in half a sec only
                }
            }
        }

        void OnGUI()
        {
            if (showArmorWindow)
            {
                windowRect = GUI.Window(this.GetInstanceID(), windowRect, WindowArmor, windowTitle, BDArmorySetup.BDGuiSkin.window);
            }
            else
            {
                Visualizer = false;
                VisualizeArmor();
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
                SetType = false;
                CalculateArmorMass();
            }

            GUIStyle style = BDArmorySetup.BDGuiSkin.label;
            float line = 1.5f;
            style.fontStyle = FontStyle.Normal;
            Visualizer = GUI.Toggle(new Rect(10, line * lineHeight, 280, lineHeight), Visualizer, Localizer.Format("#LOC_BDArmory_ArmorVisualizer"), Visualizer ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
            if (refreshVisualizer || Visualizer != oldVisualizer)
            {
                VisualizeArmor();
            }
            line++;
            GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorTotalMass") + " " + totalArmorMass, style);
            line += 1.5f;
            GUI.Label(new Rect(10, line * lineHeight, 300, lineHeight), Localizer.Format("#LOC_BDArmory_ArmorThickness") + " " + Thickness + "mm", style);
            line++;
            Thickness = GUI.HorizontalSlider(new Rect(20, line * lineHeight, 260, lineHeight), Thickness, 0, maxThickness);
            Thickness /= 5;
            Thickness = Mathf.Round(Thickness);
            Thickness *= 5;
            line ++;
            if (Thickness != oldThickness)
            {
                oldThickness = Thickness;
                SetThickness = true;
                maxThickness = 0;
                CalculateArmorMass();
            }

            if (!armorslist)
            {
                FillArmorList();
                GUIStyle listStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
                listStyle.fixedHeight = 18; //make list contents slightly smaller
                armorBox = new BDGUIComboBox(new Rect(10, line * lineHeight, 280, lineHeight), new Rect(10, line * lineHeight, 280, lineHeight), armorBoxText, armorGUI, 120, listStyle);
                armorslist = true;
            }

            int selected_index = armorBox.Show();
            float armorLines = 0;
            armorLines++;
            if (armorBox.isClickedComboButton)
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
                }
            }
            previous_index = selected_index;
            line += 0.5f;
            float HullLines = 0;
            showHullMenu = GUI.Toggle(new Rect(10, (line + armorLines)*lineHeight, 280, lineHeight),
                showHullMenu, Localizer.Format("#LOC_BDArmory_HullMat"), showHullMenu ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
            HullLines += 1.15f;

            if (showHullMenu)
            {
                isSteel = GUI.Toggle(new Rect(10, (line + armorLines + HullLines) * lineHeight, 280, lineHeight),
    isSteel, Localizer.Format("#LOC_BDArmory_Steel"), isSteel ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                HullLines += 1.15f;
                if (isSteel)
                {
                    isWood = false;
                    isAluminium = false;
                    hullmat = 3;
                    CalculateArmorMass(true);

                }
                isWood = GUI.Toggle(new Rect(10, (line + armorLines + HullLines) * lineHeight, 280, lineHeight),
    isWood, Localizer.Format("#LOC_BDArmory_Wood"), isWood ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                HullLines += 1.15f;
                if (isWood)
                {
                    isAluminium = false;
                    isSteel = false;
                    hullmat = 1;
                    CalculateArmorMass(true);
                }
                isAluminium = GUI.Toggle(new Rect(10, (line + armorLines + HullLines) * lineHeight, 280, lineHeight),
    isAluminium, Localizer.Format("#LOC_BDArmory_Aluminium"), isAluminium ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                HullLines += 1.15f;
                if (isAluminium)
                {
                    isWood = false;
                    isSteel = false;
                    hullmat = 2;
                    CalculateArmorMass(true);
                }
                if (!isSteel && !isWood && !isAluminium)
                {
                    isAluminium = true;
                    hullmat = 2;
                    CalculateArmorMass(true);
                }
            }
            line += 0.5f;
            GUI.DragWindow();
            height = Mathf.Lerp(height, (line+armorLines+HullLines) * lineHeight, 0.15f);
            windowRect.height = height;
            BDGUIUtils.RepositionWindow(ref windowRect);
        }

        void CalculateArmorMass(bool vesselmass = false)
        {
            if (EditorLogic.RootPart == null)
                return;

            // Encapsulate editor ShipConstruct into a vessel:
            Vessel v = new Vessel();
            v.parts = EditorLogic.fetch.ship.Parts;
            totalArmorMass = 0;
            using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.IsMissile()) continue;
                    HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                    if (armor != null)
                    {
                        if (!vesselmass)
                        {
                            if (SetType || SetThickness)
                            {
                                if (SetThickness)
                                {
                                    if (armor.ArmorTypeNum > 1)
                                    {
                                        armor.Armor = Mathf.Clamp(Thickness, 0, armor.maxSupportedArmor);
                                        if (armor.maxSupportedArmor > maxThickness)
                                        {
                                            maxThickness = armor.maxSupportedArmor;
                                        }
                                    }
                                }
                                if (SetType)
                                {
                                    armor.ArmorTypeNum = (ArmorInfo.armors.FindIndex(t => t.name == selectedArmor) + 1);
                                    if (armor.ArmorThickness > 10)
                                    {
                                        if (armor.ArmorTypeNum < 2)
                                        {
                                            armor.ArmorTypeNum = 2; //don't set armor type "none" for armor panels
                                        }
                                        if (armor.maxSupportedArmor > maxThickness)
                                        {
                                            maxThickness = armor.maxSupportedArmor;
                                        }
                                    }
                                }
                                armor.ArmorSetup(null, null);
                            }
                            totalArmorMass += armor.armorMass;
                        }
                        else
                        {
                            armor.HullTypeNum = hullmat;
                            armor.HullSetup(null, null);
                        }
                    }
                }
            CalcArmor = false;
            if (SetType || SetThickness)
            {
                refreshVisualizer = true;
            }
            SetType = false;
            SetThickness = false;
            //add a shipModified call to get the Editor Engineeer report to update mass            
        }
        void VisualizeArmor()
        {
            if (EditorLogic.RootPart == null)
                return;
            // Encapsulate editor ShipConstruct into a vessel:
            Vessel v = new Vessel();
            v.parts = EditorLogic.fetch.ship.Parts;
            if (Visualizer)
            {
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        HitpointTracker a = parts.Current.GetComponent<HitpointTracker>();
                        if (a != null)
                        {
                            parts.Current.SetHighlightColor(Color.HSVToRGB((a.ArmorTypeNum / (ArmorInfo.armors.Count + 1)), ((a.Armor / a.maxSupportedArmor) * 2), 1f));
                            parts.Current.SetHighlight(true, false);
                            parts.Current.highlightType = Part.HighlightType.AlwaysOn;
                        }
                    }
            }
            else
            {
                using (List<Part>.Enumerator parts = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        HitpointTracker armor = parts.Current.GetComponent<HitpointTracker>();
                        if (armor != null)
                        {
                            parts.Current.highlightType = Part.HighlightType.OnMouseOver;
                            parts.Current.SetHighlightColor(Part.defaultHighlightPart);
                            parts.Current.SetHighlight(false, false);
                        }
                    }
            }
            oldVisualizer = Visualizer;
            refreshVisualizer = false;
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
                    EdLogInstance.Lock(false, false, false, "BDARCSLOCK");
                else
                    EdLogInstance.Unlock("BDARCSLOCK");
            }
            else if (!cursorInGUI)
            {
                EdLogInstance.Unlock("BDARCSLOCK");
            }
        }

        private Vector3 GetMousePos()
        {
            Vector3 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            return mousePos;
        }
    } //EditorRCsWindow
}
