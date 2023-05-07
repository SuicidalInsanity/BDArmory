using KSP.Localization;
using System.Collections.Generic;
using System;
using UnityEngine;
using static UnityEngine.GUILayout;

using BDArmory.Bullets;
using BDArmory.Utils;
using BDArmory.Weapons;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class BDAmmoSelector : MonoBehaviour
    {
        public static BDAmmoSelector Instance;
        private Rect windowRect = new Rect(350, 100, 350, 20);

        const float width = 350;
        const float margin = 5;
        const float buttonHeight = 20;

        private bool open = false;
        private bool save = false;
        private float height = 20;

        private string beltString = String.Empty;
        private string GUIstring = String.Empty;
        private string lastGUIstring = String.Empty;
        string countString = String.Empty;
        private int roundCounter = 0;
        int labelLines = 1;

        private Vector2 windowLocation;
        private ModuleWeapon selectedWeapon;

        public string SelectedAmmoType; //presumably Aubranium can use this to filter allowed/banned ammotypes

        public List<string> AList = new List<string>();
        public List<string> BList = new List<string>();
        public List<string> ammoDesc = new List<string>();
        private BulletInfo bulletInfo;
        public string guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Slug");

        GUIStyle labelStyle;
        GUIStyle titleStyle;
        private Vector2 scrollInfoVector;
        void Start()
        {
            labelStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
            labelStyle.alignment = TextAnchor.UpperLeft;
            labelStyle.normal.textColor = Color.white;

            titleStyle = new GUIStyle();
            titleStyle.normal.textColor = BDArmorySetup.BDGuiSkin.window.normal.textColor;
            titleStyle.font = BDArmorySetup.BDGuiSkin.window.font;
            titleStyle.fontSize = BDArmorySetup.BDGuiSkin.window.fontSize;
            titleStyle.fontStyle = BDArmorySetup.BDGuiSkin.window.fontStyle;
            titleStyle.alignment = TextAnchor.UpperCenter;
        }

        public void Open(ModuleWeapon weapon, Vector2 position)
        {
            open = true;
            selectedWeapon = weapon;
            windowLocation = position;
            beltString = String.Empty;
            GUIstring = String.Empty;
            countString = String.Empty;
            lastGUIstring = String.Empty;
            roundCounter = 0;
            if (weapon.ammoBelt != "def")
            {
                beltString = weapon.ammoBelt;
                BList = BDAcTools.ParseNames(beltString);
                for (int i = 0; i < BList.Count; i++)
                {
                    BulletInfo binfo = BulletInfo.bullets[BList[i].ToString()];
                    if (BList[i] != lastGUIstring)
                    {
                        GUIstring += countString.ToString();
                        GUIstring += (string.IsNullOrEmpty(binfo.DisplayName) ? binfo.name : binfo.DisplayName);
                        lastGUIstring = (string.IsNullOrEmpty(binfo.DisplayName) ? binfo.name : binfo.DisplayName);
                        roundCounter = 1;
                        countString = "; ";
                    }
                    else
                    {
                        roundCounter++;
                        countString = " X" + roundCounter + "; ";
                    }
                }
            }
            AList = BDAcTools.ParseNames(weapon.bulletType);

            for (int a = 0; a < AList.Count; a++)
            {
                bulletInfo = BulletInfo.bullets[AList[a].ToString()];
                guiAmmoTypeString = "";
                if (bulletInfo.subProjectileCount >= 2)
                {
                    guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Shot") + " ";
                }
                if (bulletInfo.apBulletMod >= 1.1)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_AP") + " ";
                }
                if (bulletInfo.apBulletMod < 1.1 && bulletInfo.apBulletMod > 0.8f)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_SAP") + " ";
                }
                if (bulletInfo.nuclear)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Nuclear") + " ";
                }
                if (bulletInfo.tntMass > 0 && !bulletInfo.nuclear)
                {
                    if (bulletInfo.fuzeType.ToLower() == "flak" || bulletInfo.fuzeType.ToLower() == "proximity")
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Flak") + " ";
                    }
                    else if (bulletInfo.explosive.ToLower() == "Shaped")
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Shaped") + " ";
                    }
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Explosive") + " ";
                }
                if (bulletInfo.incendiary)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Incendiary") + " ";
                }
                if (bulletInfo.EMP && !bulletInfo.nuclear)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_EMP") + " ";
                }
                if (bulletInfo.beehive)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Beehive") + " ";
                }
                if (bulletInfo.tntMass <= 0 && bulletInfo.apBulletMod <= 0.8)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Slug");
                }
                ammoDesc.Add(guiAmmoTypeString);
            }

        }
        protected virtual void OnGUI()
        {
            if (save)
            {
                save = false;

                using (List<Part>.Enumerator craftPart = EditorLogic.fetch.ship.parts.GetEnumerator())
                    while (craftPart.MoveNext())
                    {
                        if (craftPart.Current == null) continue;
                        using (List<ModuleWeapon>.Enumerator weapon = craftPart.Current.FindModulesImplementing<ModuleWeapon>().GetEnumerator())
                            while (weapon.MoveNext())
                            {
                                if (weapon.Current == null) continue;
                                if (weapon.Current.part.partName != selectedWeapon.part.partName) continue;
                                if (weapon.Current.GetShortName() != selectedWeapon.GetShortName()) continue;
                                weapon.Current.ammoBelt = beltString;
                                if (!string.IsNullOrEmpty(beltString))
                                {
                                    weapon.Current.useCustomBelt = true;
                                }
                                else
                                {
                                    weapon.Current.useCustomBelt = false;
                                }
                            }
                    }
            }
            if (open)
            {
				windowRect = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), windowRect, AmmoSelectorWindow, "", BDArmorySetup.BDGuiSkin.window);
            }
            PreventClickThrough();
        }
        private void AmmoSelectorWindow(int id)
        {
            float line = 0.5f;
            string labelString = GUIstring.ToString() + countString.ToString();
            GUI.Label(new Rect(margin, 0.5f * buttonHeight, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Ammo_Setup"), titleStyle);
            if (GUI.Button(new Rect(width - 18, 2, 16, 16), "X"))
            {
                open = false;
                beltString = String.Empty;
                GUIstring = String.Empty;
                countString = String.Empty;
                lastGUIstring = String.Empty;
            }
            line++;
            GUI.Label(new Rect(margin, line * buttonHeight, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Ammo_Weapon") + " " + selectedWeapon.GetShortName(), labelStyle);
            line++;
            GUI.Label(new Rect(margin, line * buttonHeight, width - 2 * margin, buttonHeight), StringUtils.Localize("#LOC_BDArmory_Ammo_Belt"), labelStyle);
            line += 1.2f;
            labelLines = Mathf.Clamp(Mathf.CeilToInt(labelString.Length / 50), 1, 4);
            BeginArea(new Rect(margin, line * buttonHeight, width - 2 * margin, labelLines * buttonHeight));
            using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(width - 2 * margin), Height(labelLines * buttonHeight)))
            {
                scrollInfoVector = scrollViewScope.scrollPosition;
                GUILayout.Label(labelString, labelStyle, Width(width - 50 - 2 * margin));
            }
            EndArea();
            line++;

            float ammolines = 0.1f;
            for (int i = 0; i < AList.Count; i++)
            {
                string ammoname = String.IsNullOrEmpty(BulletInfo.bullets[AList[i]].DisplayName) ? BulletInfo.bullets[AList[i]].name : BulletInfo.bullets[AList[i]].DisplayName;
                if (GUI.Button(new Rect(margin * 2, (line + labelLines + ammolines) * buttonHeight, (width - 4 * margin), buttonHeight), ammoname, BDArmorySetup.BDGuiSkin.button))
                {
                    beltString += BulletInfo.bullets[AList[i]].name;
                    beltString += "; ";
                    if (lastGUIstring != ammoname)
                    {
                        GUIstring += countString.ToString();
                        GUIstring += ammoname;
                        lastGUIstring = ammoname;
                        roundCounter = 1;
                        countString = "; ";
                    }
                    else
                    {
                        roundCounter++;
                        countString = " X" + roundCounter + "; ";
                    }
                }
                ammolines++;
                if (ammoDesc[i] != null)
                {
                    GUI.Label(new Rect(margin * 4, (line + labelLines + ammolines) * buttonHeight, (width - 8 * margin), buttonHeight), ammoDesc[i], labelStyle);
                    ammolines += 1.1f;
                }
            }
            if (GUI.Button(new Rect(margin * 5, (line + labelLines + ammolines) * buttonHeight, (width - (10 * margin)) / 2, buttonHeight), StringUtils.Localize("#LOC_BDArmory_reset")))
            {
                beltString = String.Empty;
                GUIstring = String.Empty;
                countString = String.Empty;
                lastGUIstring = String.Empty;
                labelLines = 1;
                roundCounter = 1;
            }
            if (GUI.Button(new Rect(((margin * 5) + ((width - (10 * margin)) / 2)), (line + labelLines + ammolines) * buttonHeight, (width - (10 * margin)) / 2, buttonHeight), StringUtils.Localize("#LOC_BDArmory_save")))
            {
                save = true;
                open = false;
            }
            line += 1.5f;
            height = Mathf.Lerp(height, (line + labelLines + ammolines) * buttonHeight, 0.15f);
            windowRect.height = height;
            GUI.DragWindow();
            GUIUtils.RepositionWindow(ref windowRect);
        }

        private void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
            windowRect = new Rect((Screen.width / 2) - (width / 2), (Screen.height / 2) - (height / 2), width, height);
        }

        private void OnDestroy()
        {
            open = false;
        }

        private void PreventClickThrough()
        {
            bool cursorInGUI = false;
            EditorLogic EdLogInstance = EditorLogic.fetch;
            if (!EdLogInstance)
            {
                return;
            }
            if (open)
            {
                cursorInGUI = windowRect.Contains(GetMousePos());
            }
            if (cursorInGUI)
            {
                if (!CameraMouseLook.GetMouseLook())
                    EdLogInstance.Lock(false, false, false, "BDABELTLOCK");
                else
                    EdLogInstance.Unlock("BDABELTLOCK");
            }
            else if (!cursorInGUI)
            {
                EdLogInstance.Unlock("BDABELTLOCK");
            }
        }
        private Vector3 GetMousePos()
        {
            Vector3 mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            return mousePos;
        }
    }
}
