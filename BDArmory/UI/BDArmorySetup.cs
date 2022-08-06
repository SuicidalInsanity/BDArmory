using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP.Localization;
using KSP.UI.Screens;

using BDArmory.Armor;
using BDArmory.Bullets;
using BDArmory.Competition;
using BDArmory.Competition.RemoteOrchestration;
using BDArmory.Competition.VesselSpawning;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.GameModes;
using BDArmory.Modules;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.Utils;
using BDArmory.Weapons;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class BDArmorySetup : MonoBehaviour
    {
        public static bool SMART_GUARDS = true;
        public static bool showTargets = true;

        //=======Window position settings Git Issue #13
        [BDAWindowSettingsField] public static Rect WindowRectToolbar;
        [BDAWindowSettingsField] public static Rect WindowRectGps;
        [BDAWindowSettingsField] public static Rect WindowRectSettings;
        [BDAWindowSettingsField] public static Rect WindowRectRadar;
        [BDAWindowSettingsField] public static Rect WindowRectRwr;
        [BDAWindowSettingsField] public static Rect WindowRectVesselSwitcher;
        [BDAWindowSettingsField] public static Rect WindowRectWingCommander = new Rect(45, 75, 240, 800);
        [BDAWindowSettingsField] public static Rect WindowRectTargetingCam;

        [BDAWindowSettingsField] public static Rect WindowRectRemoteOrchestration;// = new Rect(45, 100, 200, 200);
        [BDAWindowSettingsField] public static Rect WindowRectEvolution;
        [BDAWindowSettingsField] public static Rect WindowRectVesselSpawner;
        [BDAWindowSettingsField] public static Rect WindowRectAI;

        //reflection field lists
        static FieldInfo[] iFs;

        static FieldInfo[] inputFields
        {
            get
            {
                if (iFs == null)
                {
                    iFs = typeof(BDInputSettingsFields).GetFields();
                }
                return iFs;
            }
        }

        //dependency checks
        bool ModuleManagerLoaded = false;
        bool PhysicsRangeExtenderLoaded = false;
        PropertyInfo PREModEnabledField = null;

        //EVENTS
        public delegate void VolumeChange();

        public static event VolumeChange OnVolumeChange;

        public delegate void SavedSettings();

        public static event SavedSettings OnSavedSettings;

        public delegate void PeaceEnabled();

        public static event PeaceEnabled OnPeaceEnabled;

        //particle optimization
        public static int numberOfParticleEmitters = 0;
        public static BDArmorySetup Instance;
        public static bool GAME_UI_ENABLED = true;
        public string Version { get; private set; } = "Unknown";

        //toolbar button
        static bool toolbarButtonAdded = false;

        //settings gui
        public static bool windowSettingsEnabled;
        public string fireKeyGui;

        //editor alignment
        public static bool showWeaponAlignment;

        // Gui Skin
        public static GUISkin BDGuiSkin = HighLogic.Skin;

        //toolbar gui
        public static bool hasAddedButton = false;
        public static bool windowBDAToolBarEnabled;
        float toolWindowWidth = 400;
        float toolWindowHeight = 100;
        float columnWidth = 400;
        bool showWeaponList;
        bool showGuardMenu;
        bool showModules;
        bool showPriorities;
        bool showTargetOptions;
        bool showEngageList;
        int numberOfModules;
        bool showWindowGPS;
        bool infoLinkEnabled;
        bool NumFieldsEnabled;
        int numberOfButtons = 6; // 6 without evolution, will adjust automatically.
        private Vector2 scrollInfoVector;
        public Dictionary<string, NumericInputField> textNumFields;

        //gps window
        public bool showingWindowGPS
        {
            get { return showWindowGPS; }
        }

        bool saveWindowPosition = false;
        float gpsEntryCount;
        float gpsEntryHeight = 24;
        float gpsBorder = 5;
        bool editingGPSName;
        int editingGPSNameIndex;
        bool hasEnteredGPSName;
        string newGPSName = String.Empty;

        public MissileFire ActiveWeaponManager;
        public bool missileWarning;
        public float missileWarningTime = 0;

        //load range stuff
        VesselRanges combatVesselRanges = new VesselRanges();
        float physRangeTimer;

        public static List<CMFlare> Flares = new List<CMFlare>();

        public List<string> mutators = new List<string>();
        bool[] mutators_selected;

        List<string> dependencyWarnings = new List<string>();
        double dependencyLastCheckTime = 0;

        //gui styles
        GUIStyle centerLabel;
        GUIStyle centerLabelRed;
        GUIStyle centerLabelOrange;
        GUIStyle centerLabelBlue;
        GUIStyle leftLabel;
        GUIStyle leftLabelBold;
        GUIStyle infoLinkStyle;
        GUIStyle leftLabelRed;
        GUIStyle rightLabelRed;
        GUIStyle leftLabelGray;
        GUIStyle rippleSliderStyle;
        GUIStyle rippleThumbStyle;
        GUIStyle kspTitleLabel;
        GUIStyle middleLeftLabel;
        GUIStyle middleLeftLabelOrange;
        GUIStyle targetModeStyle;
        GUIStyle targetModeStyleSelected;
        GUIStyle waterMarkStyle;
        GUIStyle redErrorStyle;
        GUIStyle redErrorShadowStyle;

        public SortedList<string, BDTeam> Teams = new SortedList<string, BDTeam>
        {
            { "Neutral", new BDTeam("Neutral", neutral: true) }
        };

        static float _SystemMaxMemory = 0;
        public static float SystemMaxMemory
        {
            get
            {
                if (_SystemMaxMemory == 0)
                {
                    _SystemMaxMemory = SystemInfo.systemMemorySize / 1024; // System Memory in GB.
                    if (BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > _SystemMaxMemory + 1) BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD = _SystemMaxMemory + 1;
                }
                return _SystemMaxMemory;
            }
        }
        string CheatCodeGUI = "";
        string HoSString = "";
        public string HoSTag = "";
        bool enteredHoS = false;

        //competition mode
        string compDistGui = "1000";

        #region Textures

        public static string textureDir = "BDArmory/Textures/";

        bool drawCursor;
        Texture2D cursorTexture = GameDatabase.Instance.GetTexture(textureDir + "aimer", false);

        private Texture2D dti;

        public Texture2D directionTriangleIcon
        {
            get { return dti ? dti : dti = GameDatabase.Instance.GetTexture(textureDir + "directionIcon", false); }
        }

        private Texture2D cgs;

        public Texture2D crossedGreenSquare
        {
            get { return cgs ? cgs : cgs = GameDatabase.Instance.GetTexture(textureDir + "crossedGreenSquare", false); }
        }

        private Texture2D dlgs;

        public Texture2D dottedLargeGreenCircle
        {
            get
            {
                return dlgs
                    ? dlgs
                    : dlgs = GameDatabase.Instance.GetTexture(textureDir + "dottedLargeGreenCircle", false);
            }
        }

        private Texture2D ogs;

        public Texture2D openGreenSquare
        {
            get { return ogs ? ogs : ogs = GameDatabase.Instance.GetTexture(textureDir + "openGreenSquare", false); }
        }

        private Texture2D gdott;

        public Texture2D greenDotTexture
        {
            get { return gdott ? gdott : gdott = GameDatabase.Instance.GetTexture(textureDir + "greenDot", false); }
        }

        private Texture2D rdott;

        public Texture2D redDotTexture
        {
            get { return rdott ? rdott : rdott = GameDatabase.Instance.GetTexture(textureDir + "redDot", false); }
        }
        private Texture2D gdt;

        public Texture2D greenDiamondTexture
        {
            get { return gdt ? gdt : gdt = GameDatabase.Instance.GetTexture(textureDir + "greenDiamond", false); }
        }

        private Texture2D lgct;

        public Texture2D largeGreenCircleTexture
        {
            get { return lgct ? lgct : lgct = GameDatabase.Instance.GetTexture(textureDir + "greenCircle3", false); }
        }

        private Texture2D gct;

        public Texture2D greenCircleTexture
        {
            get { return gct ? gct : gct = GameDatabase.Instance.GetTexture(textureDir + "greenCircle2", false); }
        }

        private Texture2D gpct;

        public Texture2D greenPointCircleTexture
        {
            get
            {
                if (gpct == null)
                {
                    gpct = GameDatabase.Instance.GetTexture(textureDir + "greenPointCircle", false);
                }
                return gpct;
            }
        }

        private Texture2D gspct;

        public Texture2D greenSpikedPointCircleTexture
        {
            get
            {
                return gspct ? gspct : gspct = GameDatabase.Instance.GetTexture(textureDir + "greenSpikedCircle", false);
            }
        }

        private Texture2D wSqr;

        public Texture2D whiteSquareTexture
        {
            get { return wSqr ? wSqr : wSqr = GameDatabase.Instance.GetTexture(textureDir + "whiteSquare", false); }
        }

        private Texture2D oWSqr;

        public Texture2D openWhiteSquareTexture
        {
            get
            {
                return oWSqr ? oWSqr : oWSqr = GameDatabase.Instance.GetTexture(textureDir + "openWhiteSquare", false);
                ;
            }
        }

        private Texture2D tDir;

        public Texture2D targetDirectionTexture
        {
            get
            {
                return tDir
                    ? tDir
                    : tDir = GameDatabase.Instance.GetTexture(textureDir + "targetDirectionIndicator", false);
            }
        }

        private Texture2D hInd;

        public Texture2D horizonIndicatorTexture
        {
            get
            {
                return hInd ? hInd : hInd = GameDatabase.Instance.GetTexture(textureDir + "horizonIndicator", false);
            }
        }

        private Texture2D si;

        public Texture2D settingsIconTexture
        {
            get { return si ? si : si = GameDatabase.Instance.GetTexture(textureDir + "settingsIcon", false); }
        }


        private Texture2D FAimg;

        public Texture2D FiringAngleImage
        {
            get { return FAimg ? FAimg : FAimg = GameDatabase.Instance.GetTexture(textureDir + "FiringAnglePic", false); }
        }

        #endregion Textures

        public static bool GameIsPaused
        {
            get { return PauseMenu.isOpen || Time.timeScale == 0; }
        }

        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor))
            {
                windowSettingsEnabled = false; // Close the settings on other scenes (it's been saved when the other scene was destroyed).
            }

            // Create settings file if not present or migrate the old one to the PluginsData folder for compatibility with ModuleManager.
            var fileNode = ConfigNode.Load(BDArmorySettings.settingsConfigURL);
            if (fileNode == null)
            {
                fileNode = ConfigNode.Load(BDArmorySettings.oldSettingsConfigURL); // Try the old location.
                if (fileNode == null)
                {
                    fileNode = new ConfigNode();
                    fileNode.AddNode("BDASettings");
                }
                if (!Directory.GetParent(BDArmorySettings.settingsConfigURL).Exists)
                { Directory.GetParent(BDArmorySettings.settingsConfigURL).Create(); }
                var success = fileNode.Save(BDArmorySettings.settingsConfigURL);
                if (success && File.Exists(BDArmorySettings.oldSettingsConfigURL)) // Remove the old settings if it exists and the new settings were saved.
                { File.Delete(BDArmorySettings.oldSettingsConfigURL); }
            }

            // window position settings
            WindowRectToolbar = new Rect(Screen.width - toolWindowWidth - 40, 150, toolWindowWidth, toolWindowHeight);
            // Default, if not in file.
            WindowRectGps = new Rect(0, 0, WindowRectToolbar.width - 10, 0);
            SetupSettingsSize();
            BDAWindowSettingsField.Load();
            CheckIfWindowsSettingsAreWithinScreen();

            WindowRectGps.width = WindowRectToolbar.width - 10;

            // Load settings
            LoadConfig();

            // Ensure AutoSpawn folder exists.
            if (!Directory.Exists(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn")))
            { Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn")); }
            // Ensure GameData/Custom/Flags folder exists.
            if (!Directory.Exists(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Custom", "Flags")))
            { Directory.CreateDirectory(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Custom", "Flags")); }
        }

        void Start()
        {
            //wmgr toolbar
            if (HighLogic.LoadedSceneIsFlight)
                saveWindowPosition = true;     //otherwise later we should NOT save the current window positions!

            // // Create settings file if not present.
            // if (ConfigNode.Load(BDArmorySettings.settingsConfigURL) == null)
            // {
            //     var node = new ConfigNode();
            //     node.AddNode("BDASettings");
            //     node.Save(BDArmorySettings.settingsConfigURL);
            // }

            // // window position settings
            // WindowRectToolbar = new Rect(Screen.width - toolWindowWidth - 40, 150, toolWindowWidth, toolWindowHeight);
            // // Default, if not in file.
            // WindowRectGps = new Rect(0, 0, WindowRectToolbar.width - 10, 0);
            // SetupSettingsSize();
            // BDAWindowSettingsField.Load();
            // CheckIfWindowsSettingsAreWithinScreen();

            // WindowRectGps.width = WindowRectToolbar.width - 10;

            // //settings
            // LoadConfig();

            physRangeTimer = Time.time;
            GAME_UI_ENABLED = true;
            fireKeyGui = BDInputSettingsFields.WEAP_FIRE_KEY.inputString;

            //setup gui styles
            centerLabel = new GUIStyle();
            centerLabel.alignment = TextAnchor.UpperCenter;
            centerLabel.normal.textColor = Color.white;

            centerLabelRed = new GUIStyle();
            centerLabelRed.alignment = TextAnchor.UpperCenter;
            centerLabelRed.normal.textColor = Color.red;

            centerLabelOrange = new GUIStyle();
            centerLabelOrange.alignment = TextAnchor.UpperCenter;
            centerLabelOrange.normal.textColor = XKCDColors.BloodOrange;

            centerLabelBlue = new GUIStyle();
            centerLabelBlue.alignment = TextAnchor.UpperCenter;
            centerLabelBlue.normal.textColor = XKCDColors.AquaBlue;

            leftLabel = new GUIStyle();
            leftLabel.alignment = TextAnchor.UpperLeft;
            leftLabel.normal.textColor = Color.white;

            leftLabelBold = new GUIStyle();
            leftLabelBold.alignment = TextAnchor.UpperLeft;
            leftLabelBold.normal.textColor = Color.white;
            leftLabelBold.fontStyle = FontStyle.Bold;

            infoLinkStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
            infoLinkStyle.alignment = TextAnchor.UpperLeft;
            infoLinkStyle.normal.textColor = Color.white;

            middleLeftLabel = new GUIStyle(leftLabel);
            middleLeftLabel.alignment = TextAnchor.MiddleLeft;

            middleLeftLabelOrange = new GUIStyle(middleLeftLabel);
            middleLeftLabelOrange.normal.textColor = XKCDColors.BloodOrange;

            targetModeStyle = new GUIStyle();
            targetModeStyle.alignment = TextAnchor.MiddleRight;
            targetModeStyle.fontSize = 9;
            targetModeStyle.normal.textColor = Color.white;

            targetModeStyleSelected = new GUIStyle(targetModeStyle);
            targetModeStyleSelected.normal.textColor = XKCDColors.BloodOrange;

            waterMarkStyle = new GUIStyle(middleLeftLabel);
            waterMarkStyle.normal.textColor = XKCDColors.LightBlueGrey;

            leftLabelRed = new GUIStyle();
            leftLabelRed.alignment = TextAnchor.UpperLeft;
            leftLabelRed.normal.textColor = Color.red;

            rightLabelRed = new GUIStyle();
            rightLabelRed.alignment = TextAnchor.UpperRight;
            rightLabelRed.normal.textColor = Color.red;

            leftLabelGray = new GUIStyle();
            leftLabelGray.alignment = TextAnchor.UpperLeft;
            leftLabelGray.normal.textColor = Color.gray;

            rippleSliderStyle = new GUIStyle(BDGuiSkin.horizontalSlider);
            rippleThumbStyle = new GUIStyle(BDGuiSkin.horizontalSliderThumb);
            rippleSliderStyle.fixedHeight = rippleThumbStyle.fixedHeight = 0;

            kspTitleLabel = new GUIStyle();
            kspTitleLabel.normal.textColor = BDGuiSkin.window.normal.textColor;
            kspTitleLabel.font = BDGuiSkin.window.font;
            kspTitleLabel.fontSize = BDGuiSkin.window.fontSize;
            kspTitleLabel.fontStyle = BDGuiSkin.window.fontStyle;
            kspTitleLabel.alignment = TextAnchor.UpperCenter;

            redErrorStyle = new GUIStyle(BDGuiSkin.label);
            redErrorStyle.normal.textColor = Color.red;
            redErrorStyle.fontStyle = FontStyle.Bold;
            redErrorStyle.fontSize = 24;
            redErrorStyle.alignment = TextAnchor.UpperCenter;

            redErrorShadowStyle = new GUIStyle(redErrorStyle);
            redErrorShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);
            //

            using (var a = AppDomain.CurrentDomain.GetAssemblies().ToList().GetEnumerator())
                while (a.MoveNext())
                {
                    string name = a.Current.FullName.Split(new char[1] { ',' })[0];
                    switch (name)
                    {
                        case "ModuleManager":
                            ModuleManagerLoaded = true;
                            break;

                        case "PhysicsRangeExtender":
                            foreach (var t in a.Current.GetTypes())
                            {
                                if (t != null && t.Name == "PreSettings")
                                {
                                    var PREInstance = FindObjectOfType(t);
                                    foreach (var propInfo in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                                        if (propInfo != null && propInfo.Name == "ModEnabled")
                                        {
                                            PREModEnabledField = propInfo;
                                            PhysicsRangeExtenderLoaded = true;
                                        }
                                }
                            }
                            break;

                        case "BDArmory":
                            Version = a.Current.GetName().Version.ToString();
                            break;
                    }
                }

            if (HighLogic.LoadedSceneIsFlight)
            {
                SaveVolumeSettings();

                GameEvents.onHideUI.Add(HideGameUI);
                GameEvents.onShowUI.Add(ShowGameUI);
                GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
                GameEvents.OnGameSettingsApplied.Add(SaveVolumeSettings);

                GameEvents.onVesselChange.Add(VesselChange);
            }

            BulletInfo.Load();
            RocketInfo.Load();
            ArmorInfo.Load();
            MutatorInfo.Load();

            compDistGui = BDArmorySettings.COMPETITION_DISTANCE.ToString();
            HoSTag = BDArmorySettings.HOS_BADGE;

            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            { StartCoroutine(ToolbarButtonRoutine()); }

            for (int i = 0; i < MutatorInfo.mutators.Count; i++)
            {
                mutators.Add(MutatorInfo.mutators[i].name);
            }
            mutators_selected = new bool[mutators.Count];
            for (int i = 0; i < mutators_selected.Length; ++i)
            {
                mutators_selected[i] = BDArmorySettings.MUTATOR_LIST.Contains(mutators[i]);
            }
        }

        /// <summary>
        /// Modify the background opacity of a window.
        /// 
        /// GUI.Window stores the color values it was called with, so call this with enable=true before GUI.Window to enable
        /// transparency for that window and again with enable=false afterwards to avoid affect later GUI.Window calls.
        ///
        /// Note: This can only lower the opacity of the window background, so windows with a background texture that
        /// already includes some transparency can only be made more transparent, not less.
        /// </summary>
        /// <param name="enable">Enable or reset the modified background opacity.</param>
        public static void SetGUIOpacity(bool enable = true)
        {
            if (!enable && BDArmorySettings.GUI_OPACITY == 1f) return; // Nothing to do.
            var guiColor = GUI.backgroundColor;
            if (guiColor.a != (enable ? BDArmorySettings.GUI_OPACITY : 1f))
            {
                guiColor.a = (enable ? BDArmorySettings.GUI_OPACITY : 1f);
                GUI.backgroundColor = guiColor;
            }
        }

        IEnumerator ToolbarButtonRoutine()
        {
            if (toolbarButtonAdded) yield break;
            while (!ApplicationLauncher.Ready)
            { yield return null; }
            if (toolbarButtonAdded) yield break;
            toolbarButtonAdded = true;
            Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon", false);
            ApplicationLauncher.Instance.AddModApplication(
                ToggleToolbarButton,
                ToggleToolbarButton,
                () => { },
                () => { },
                () => { },
                () => { },
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB,
                buttonTexture
            );
        }
        /// <summary>
        /// Toggle the BDAToolbar or BDA settings window depending on the scene.
        /// </summary>
        void ToggleToolbarButton()
        {
            if (HighLogic.LoadedSceneIsFlight) { windowBDAToolBarEnabled = !windowBDAToolBarEnabled; }
            else { windowSettingsEnabled = !windowSettingsEnabled; }
        }

        private void CheckIfWindowsSettingsAreWithinScreen()
        {
            GUIUtils.RepositionWindow(ref WindowRectEvolution);
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
            GUIUtils.RepositionWindow(ref WindowRectToolbar);
            GUIUtils.RepositionWindow(ref WindowRectSettings);
            GUIUtils.RepositionWindow(ref WindowRectRwr);
            GUIUtils.RepositionWindow(ref WindowRectVesselSwitcher);
            GUIUtils.RepositionWindow(ref WindowRectWingCommander);
            GUIUtils.RepositionWindow(ref WindowRectTargetingCam);
            GUIUtils.RepositionWindow(ref WindowRectAI);
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (missileWarning && Time.time - missileWarningTime > 1.5f)
                {
                    missileWarning = false;
                }

                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.GUI_WM_TOGGLE))
                {
                    windowBDAToolBarEnabled = !windowBDAToolBarEnabled;
                }

                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TIME_SCALING))
                {
                    BDArmorySettings.TIME_OVERRIDE = !BDArmorySettings.TIME_OVERRIDE;
                    Time.timeScale = BDArmorySettings.TIME_OVERRIDE ? BDArmorySettings.TIME_SCALE : 1f;
                }
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                if (Input.GetKeyDown(KeyCode.F2))
                {
                    showWeaponAlignment = !showWeaponAlignment;
                }
            }

            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKeyDown(KeyCode.B))
                {
                    ToggleWindowSettings();
                }
            }
        }

        void ToggleWindowSettings()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER)
            {
                return;
            }

            windowSettingsEnabled = !windowSettingsEnabled;
            if (windowSettingsEnabled)
            {
                // LoadConfig(); // Don't reload settings, since they're already loaded and mess with other settings windows.
            }
            else
            {
                SaveConfig();
            }
        }

        void LateUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                //UpdateCursorState();
            }
        }

        public void UpdateCursorState()
        {
            if (ActiveWeaponManager == null)
            {
                drawCursor = false;
                //Screen.showCursor = true;
                Cursor.visible = true;
                return;
            }

            if (!GAME_UI_ENABLED || CameraMouseLook.MouseLocked)
            {
                drawCursor = false;
                Cursor.visible = false;
                return;
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                drawCursor = false;
                if (!MapView.MapIsEnabled && !GUIUtils.CheckMouseIsOnGui() && !PauseMenu.isOpen)
                {
                    if (ActiveWeaponManager.selectedWeapon != null && ActiveWeaponManager.weaponIndex > 0 &&
                        !ActiveWeaponManager.guardMode)
                    {
                        if (ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Gun ||
                            ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket ||
                            ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                        {
                            ModuleWeapon mw = ActiveWeaponManager.selectedWeapon.GetPart().FindModuleImplementing<ModuleWeapon>();
                            if (mw != null && mw.weaponState == ModuleWeapon.WeaponStates.Enabled && mw.maxPitch > 1 && !mw.slaved && !mw.aiControlled)
                            {
                                //Screen.showCursor = false;
                                Cursor.visible = false;
                                drawCursor = true;
                                return;
                            }
                        }
                    }
                }
            }

            //Screen.showCursor = true;
            Cursor.visible = true;
        }

        void VesselChange(Vessel v)
        {
            if (v != null && v.isActiveVessel)
            {
                GetWeaponManager();
                Instance.UpdateCursorState();
            }
        }

        void GetWeaponManager()
        {
            ActiveWeaponManager = VesselModuleRegistry.GetMissileFire(FlightGlobals.ActiveVessel, true);
            if (ActiveWeaponManager != null)
            { ConfigTextFields(); }
        }
        public void ConfigTextFields()
        {
            textNumFields = new Dictionary<string, NumericInputField> {
                { "rippleRPM", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.rippleRPM, 0, 1600) },
                { "targetScanInterval", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetScanInterval, 0.5f, 60f) },
                { "fireBurstLength", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.fireBurstLength, 0, 10) },
                { "AutoFireCosAngleAdjustment", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.AutoFireCosAngleAdjustment, 0, 4) },
                { "guardAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.guardAngle, 10, 360) },
                { "guardRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.guardRange, 100, BDArmorySettings.MAX_GUARD_VISUAL_RANGE) },
                { "gunRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.gunRange, 0, ActiveWeaponManager.maxGunRange) },
                { "multiTargetNum", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.multiTargetNum, 1, 10) },
                { "multiMissileTgtNum", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.multiMissileTgtNum, 1, 10) },
                { "maxMissilesOnTarget", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.maxMissilesOnTarget, 1, MissileFire.maxAllowableMissilesOnTarget) },

                { "targetBias", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetBias, -10, 10) },
                { "targetWeightRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightRange, -10, 10) },
                { "targetWeightAirPreference", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAirPreference, -10, 10) },
                { "targetWeightATA", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightATA, -10, 10) },
                { "targetWeightAoD", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAoD, -10, 10) },
                { "targetWeightAccel", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAccel,-10, 10) },
                { "targetWeightClosureTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightClosureTime, -10, 10) },
                { "targetWeightWeaponNumber", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightWeaponNumber, -10, 10) },
                { "targetWeightMass", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightMass,-10, 10) },
                { "targetWeightFriendliesEngaging", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightFriendliesEngaging, -10, 10) },
                { "targetWeightThreat", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightThreat, -10, 10) },
                { "targetWeightProtectTeammate", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightProtectTeammate, -10, 10) },
                { "targetWeightProtectVIP", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightProtectVIP, -10, 10) },
                { "targetWeightAttackVIP", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveWeaponManager.targetWeightAttackVIP, -10, 10) },
            };
        }

        public static void LoadConfig()
        {
            try
            {
                Debug.Log("[BDArmory.BDArmorySetup]=== Loading settings.cfg ===");

                BDAPersistentSettingsField.Load();
                BDInputSettingsFields.LoadSettings();
                BDArmorySettings.ready = true;
            }
            catch (NullReferenceException e)
            {
                Debug.LogWarning("[BDArmory.BDArmorySetup]=== Failed to load settings config ===: " + e.Message);
            }
        }

        public static void SaveConfig()
        {
            try
            {
                Debug.Log("[BDArmory.BDArmorySetup] == Saving settings.cfg ==	");

                BDAPersistentSettingsField.Save();

                BDInputSettingsFields.SaveSettings();

                if (OnSavedSettings != null)
                {
                    OnSavedSettings();
                }
            }
            catch (NullReferenceException e)
            {
                Debug.LogWarning("[BDArmory.BDArmorySetup]: === Failed to save settings.cfg ====: " + e.Message);
            }
        }

        #region GUI

        void OnGUI()
        {
            if (!GAME_UI_ENABLED) return;
            if (windowSettingsEnabled)
            {
                WindowRectSettings = GUI.Window(129419, WindowRectSettings, WindowSettings, GUIContent.none);
            }

            if (drawCursor)
            {
                //mouse cursor
                int origDepth = GUI.depth;
                GUI.depth = -100;
                float cursorSize = 40;
                Vector3 cursorPos = Input.mousePosition;
                Rect cursorRect = new Rect(cursorPos.x - (cursorSize / 2), Screen.height - cursorPos.y - (cursorSize / 2), cursorSize, cursorSize);
                GUI.DrawTexture(cursorRect, cursorTexture);
                GUI.depth = origDepth;
            }

            if (!windowBDAToolBarEnabled || !HighLogic.LoadedSceneIsFlight) return;
            SetGUIOpacity();
            WindowRectToolbar = GUI.Window(321, WindowRectToolbar, WindowBDAToolbar, "", BDGuiSkin.window);//"BDA Weapon Manager"
            SetGUIOpacity(false);
            GUIUtils.UseMouseEventInRect(WindowRectToolbar);
            if (showWindowGPS && ActiveWeaponManager)
            {
                //gpsWindowRect = GUI.Window(424333, gpsWindowRect, GPSWindow, "", GUI.skin.box);
                GUIUtils.UseMouseEventInRect(WindowRectGps);
                using (var coord = BDATargetManager.GPSTargetList(ActiveWeaponManager.Team).GetEnumerator())
                    while (coord.MoveNext())
                    {
                        GUIUtils.DrawTextureOnWorldPos(coord.Current.worldPos, Instance.greenDotTexture, new Vector2(8, 8), 0);
                    }
            }

            if (Time.time - dependencyLastCheckTime > (dependencyWarnings.Count() == 0 ? 60 : 5)) // Only check once per minute if no issues are found, otherwise 5s.
            {
                dependencyLastCheckTime = Time.time;
                dependencyWarnings.Clear();
                if (!ModuleManagerLoaded) dependencyWarnings.Add("Module Manager dependency is missing!");
                if (!PhysicsRangeExtenderLoaded) dependencyWarnings.Add("Physics Range Extender dependency is missing!");
                else if (BDACompetitionMode.Instance != null && (BDACompetitionMode.Instance.competitionIsActive || BDACompetitionMode.Instance.competitionStarting) && !(bool)PREModEnabledField.GetValue(null)) dependencyWarnings.Add("Physics Range Extender is disabled!");
                if (dependencyWarnings.Count() > 0) dependencyWarnings.Add("BDArmory will not work properly.");
            }
            if (dependencyWarnings.Count() > 0)
            {
                GUI.Label(new Rect(Screen.width / 2 - 300 + 2, Screen.height / 6 + 2, 600, 100), string.Join("\n", dependencyWarnings), redErrorShadowStyle);
                GUI.Label(new Rect(Screen.width / 2 - 300, Screen.height / 6, 600, 100), string.Join("\n", dependencyWarnings), redErrorStyle);
            }
        }

        public bool hasVesselSwitcher = false;
        public bool hasVesselSpawner = false;
        public bool hasEvolution = false;
        public bool showVesselSwitcherGUI = false;
        public bool showVesselSpawnerGUI = false;
        public bool showEvolutionGUI = false;

        float rippleHeight;
        float weaponsHeight;
        float priorityheight;
        float guardHeight;
        float TargetingHeight;
        float EngageHeight;
        float modulesHeight;
        float gpsHeight;
        bool toolMinimized;

        void WindowBDAToolbar(int windowID)
        {
            float line = 0;
            float leftIndent = 10;
            float contentWidth = (columnWidth) - (2 * leftIndent);
            float windowColumns = 1;
            float contentTop = 10;
            float entryHeight = 20;
            float _buttonSize = 26;
            float _windowMargin = 4;
            int buttonNumber = 0;

            GUI.DragWindow(new Rect(_windowMargin + _buttonSize, 0, columnWidth - 2 * _windowMargin - numberOfButtons * _buttonSize, _windowMargin + _buttonSize));

            line += 1.25f;
            line += 0.25f;

            //title
            GUI.Label(new Rect(_windowMargin + _buttonSize, _windowMargin, columnWidth - 2 * _windowMargin - numberOfButtons * _buttonSize, _windowMargin + _buttonSize), Localizer.Format("#LOC_BDArmory_WMWindow_title") + "          ", kspTitleLabel);

            // Version.
            GUI.Label(new Rect(columnWidth - _windowMargin - (numberOfButtons - 1) * _buttonSize - 100, 23, 57, 10), Version, waterMarkStyle);

            //SETTINGS BUTTON
            if (!BDKeyBinder.current &&
                GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), settingsIconTexture, BDGuiSkin.button))
            {
                ToggleWindowSettings();
            }

            //vesselswitcher button
            if (hasVesselSwitcher)
            {
                GUIStyle vsStyle = showVesselSwitcherGUI ? BDGuiSkin.box : BDGuiSkin.button;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "VS", vsStyle))
                {
                    showVesselSwitcherGUI = !showVesselSwitcherGUI;
                }
            }

            //VesselSpawner button
            if (hasVesselSpawner)
            {
                GUIStyle vsStyle = showVesselSpawnerGUI ? BDGuiSkin.box : BDGuiSkin.button;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "Sp", vsStyle))
                {
                    showVesselSpawnerGUI = !showVesselSpawnerGUI;
                    if (!showVesselSpawnerGUI)
                        SaveConfig();
                }
            }

            // evolution button
            if (BDArmorySettings.EVOLUTION_ENABLED && hasEvolution)
            {
                var evolutionSkin = showEvolutionGUI ? BDGuiSkin.box : BDGuiSkin.button; ;
                if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "EV", evolutionSkin))
                {
                    showEvolutionGUI = !showEvolutionGUI;
                }
            }

            //infolink
            GUIStyle iStyle = infoLinkEnabled ? BDGuiSkin.box : BDGuiSkin.button;
            if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "i", iStyle))
            {
                infoLinkEnabled = !infoLinkEnabled;
            }

            //numeric fields
            GUIStyle nStyle = NumFieldsEnabled ? BDGuiSkin.box : BDGuiSkin.button;
            if (GUI.Button(new Rect(columnWidth - _windowMargin - ++buttonNumber * _buttonSize, _windowMargin, _buttonSize, _buttonSize), "#", nStyle))
            {
                NumFieldsEnabled = !NumFieldsEnabled;
                if (!NumFieldsEnabled)
                {
                    // Try to parse all the fields immediately so that they're up to date.
                    foreach (var field in textNumFields.Keys)
                    { textNumFields[field].tryParseValueNow(); }
                    if (ActiveWeaponManager != null)
                    {
                        foreach (var field in textNumFields.Keys)
                        {
                            try
                            {
                                var fieldInfo = typeof(MissileFire).GetField(field);
                                if (fieldInfo != null)
                                { fieldInfo.SetValue(ActiveWeaponManager, Convert.ChangeType(textNumFields[field].currentValue, fieldInfo.FieldType)); }
                                else // Check if it's a property instead of a field.
                                {
                                    var propInfo = typeof(MissileFire).GetProperty(field);
                                    propInfo.SetValue(ActiveWeaponManager, Convert.ChangeType(textNumFields[field].currentValue, propInfo.PropertyType));
                                }
                            }
                            catch (Exception e) { Debug.LogError($"[BDArmory.BDArmorySetup]: Failed to set current value of {field}: " + e.Message); }
                        }
                    }
                    // Then make any special conversions here.
                }
                else // Set the input fields to their current values.
                {
                    // Make any special conversions first.
                    // Then set each of the field values to the current slider value.   
                    if (ActiveWeaponManager != null)
                    {
                        foreach (var field in textNumFields.Keys)
                        {
                            try
                            {
                                var fieldInfo = typeof(MissileFire).GetField(field);
                                if (fieldInfo != null)
                                { textNumFields[field].currentValue = Convert.ToDouble(fieldInfo.GetValue(ActiveWeaponManager)); }
                                else // Check if it's a property instead of a field.
                                {
                                    var propInfo = typeof(MissileFire).GetProperty(field);
                                    textNumFields[field].currentValue = Convert.ToDouble(propInfo.GetValue(ActiveWeaponManager));
                                }
                            }
                            catch (Exception e) { Debug.LogError($"[BDArmory.BDArmorySetup]: Failed to set current value of {field}: " + e.Message); }
                        }
                    }
                }
            }

            if (ActiveWeaponManager != null)
            {
                //MINIMIZE BUTTON
                toolMinimized = GUI.Toggle(new Rect(_windowMargin, _windowMargin, _buttonSize, _buttonSize), toolMinimized, "_",
                    toolMinimized ? BDGuiSkin.box : BDGuiSkin.button);

                GUIStyle armedLabelStyle;
                Rect armedRect = new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight);
                if (ActiveWeaponManager.guardMode)
                {
                    if (GUI.Button(armedRect, "- " + Localizer.Format("#LOC_BDArmory_WMWindow_GuardModebtn") + " -", BDGuiSkin.box))//Guard Mode
                    {
                        showGuardMenu = true;
                    }
                }
                else
                {
                    string armedText = Localizer.Format("#LOC_BDArmory_WMWindow_ArmedText");//"Trigger is "
                    if (ActiveWeaponManager.isArmed)
                    {
                        armedText += Localizer.Format("#LOC_BDArmory_WMWindow_ArmedText_ARMED");//"ARMED."
                        armedLabelStyle = BDGuiSkin.box;
                    }
                    else
                    {
                        armedText += Localizer.Format("#LOC_BDArmory_WMWindow_ArmedText_DisArmed");//"disarmed."
                        armedLabelStyle = BDGuiSkin.button;
                    }
                    if (GUI.Button(armedRect, armedText, armedLabelStyle))
                    {
                        ActiveWeaponManager.ToggleArm();
                    }
                }

                GUIStyle teamButtonStyle = BDGuiSkin.box;
                string teamText = Localizer.Format("#LOC_BDArmory_WMWindow_TeamText") + ": " + ActiveWeaponManager.Team.Name + (ActiveWeaponManager.Team.Neutral ? (ActiveWeaponManager.Team.Name != "Neutral" ? "(N)" : "") : "");//Team
                if (GUI.Button(new Rect(leftIndent + (contentWidth / 2), contentTop + (line * entryHeight), contentWidth / 2, entryHeight), teamText, teamButtonStyle))
                {
                    if (Event.current.button == 1)
                    {
                        BDTeamSelector.Instance.Open(ActiveWeaponManager, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
                    }
                    else
                    {
                        ActiveWeaponManager.NextTeam();
                    }
                }
                line++;
                line += 0.25f;
                string weaponName = ActiveWeaponManager.selectedWeaponString;
                // = ActiveWeaponManager.selectedWeapon == null ? "None" : ActiveWeaponManager.selectedWeapon.GetShortName();
                string selectionText = Localizer.Format("#LOC_BDArmory_WMWindow_selectionText", weaponName);//Weapon: <<1>>
                GUI.Label(new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth, entryHeight * 1.25f), selectionText, BDGuiSkin.box);
                line += 1.25f;
                line += 0.1f;
                //if weapon can ripple, show option and slider.
                if (ActiveWeaponManager.hasLoadedRippleData && ActiveWeaponManager.canRipple)
                {
                    if (ActiveWeaponManager.selectedWeapon != null && ActiveWeaponManager.weaponIndex > 0 &&
                        (ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Gun
                        || ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket
                        || ActiveWeaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)) //remove rocket ripple slider - moved to editor
                    {
                        string rippleText = ActiveWeaponManager.rippleFire
                            ? Localizer.Format("#LOC_BDArmory_WMWindow_rippleText1", ActiveWeaponManager.gunRippleRpm.ToString("0"))//"Barrage: " +  + " RPM"
                            : Localizer.Format("#LOC_BDArmory_WMWindow_rippleText2");//"Salvo"
                        GUIStyle rippleStyle = ActiveWeaponManager.rippleFire
                            ? BDGuiSkin.box
                            : BDGuiSkin.button;
                        if (
                            GUI.Button(
                                new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight * 1.25f),
                                rippleText, rippleStyle))
                        {
                            ActiveWeaponManager.ToggleRippleFire();
                        }

                        rippleHeight = Mathf.Lerp(rippleHeight, 1.25f, 0.15f);
                    }
                    else
                    {
                        string rippleText = ActiveWeaponManager.rippleFire
                            ? Localizer.Format("#LOC_BDArmory_WMWindow_rippleText3", ActiveWeaponManager.rippleRPM.ToString("0"))//"Ripple: " +  + " RPM"
                            : Localizer.Format("#LOC_BDArmory_WMWindow_rippleText4");//"Ripple: OFF"
                        GUIStyle rippleStyle = ActiveWeaponManager.rippleFire
                            ? BDGuiSkin.box
                            : BDGuiSkin.button;
                        if (
                            GUI.Button(
                                new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 2, entryHeight * 1.25f),
                                rippleText, rippleStyle))
                        {
                            ActiveWeaponManager.ToggleRippleFire();
                        }
                        if (ActiveWeaponManager.rippleFire)
                        {
                            Rect sliderRect = new Rect(leftIndent + (contentWidth / 2) + 2,
                                contentTop + (line * entryHeight) + 6.5f, (contentWidth / 2) - 2, 12);

                            if (!NumFieldsEnabled)
                            {
                                ActiveWeaponManager.rippleRPM = GUI.HorizontalSlider(sliderRect,
                                    ActiveWeaponManager.rippleRPM, 100, 1600, rippleSliderStyle, rippleThumbStyle);
                            }
                            else
                            {
                                textNumFields["rippleRPM"].tryParseValue(GUI.TextField(sliderRect, textNumFields["rippleRPM"].possibleValue, 4));
                                ActiveWeaponManager.rippleRPM = (float)textNumFields["rippleRPM"].currentValue;
                            }
                        }
                        rippleHeight = Mathf.Lerp(rippleHeight, 1.25f, 0.15f);
                    }
                }
                else
                {
                    rippleHeight = Mathf.Lerp(rippleHeight, 0, 0.15f);
                }
                //line += 1.25f;
                line += rippleHeight;
                line += 0.1f;

                if (!toolMinimized)
                {
                    showWeaponList =
                        GUI.Toggle(new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth / 4, entryHeight),
                            showWeaponList, Localizer.Format("#LOC_BDArmory_WMWindow_ListWeapons"), showWeaponList ? BDGuiSkin.box : BDGuiSkin.button);//"Weapons"
                    showGuardMenu =
                        GUI.Toggle(
                            new Rect(leftIndent + (contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                                entryHeight), showGuardMenu, Localizer.Format("#LOC_BDArmory_WMWindow_GuardMenu"),//"Guard Menu"
                            showGuardMenu ? BDGuiSkin.box : BDGuiSkin.button);
                    showPriorities =
                        GUI.Toggle(new Rect(leftIndent + (2 * contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                             entryHeight), showPriorities, Localizer.Format("#LOC_BDArmory_WMWindow_TargetPriority"),//"Tgt priority"
                            showPriorities ? BDGuiSkin.box : BDGuiSkin.button);
                    showModules =
                        GUI.Toggle(
                            new Rect(leftIndent + (3 * contentWidth / 4), contentTop + (line * entryHeight), contentWidth / 4,
                                entryHeight), showModules, Localizer.Format("#LOC_BDArmory_WMWindow_ModulesToggle"),//"Modules"
                            showModules ? BDGuiSkin.box : BDGuiSkin.button);
                    line++;
                }

                float weaponLines = 0;
                if (showWeaponList && !toolMinimized)
                {
                    line += 0.25f;
                    Rect weaponListGroupRect = new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, weaponsHeight * entryHeight);
                    GUI.BeginGroup(weaponListGroupRect, GUIContent.none, BDGuiSkin.box); //darker box
                    weaponLines += 0.1f;

                    for (int i = 0; i < ActiveWeaponManager.weaponArray.Length; i++)
                    {
                        GUIStyle wpnListStyle;
                        GUIStyle tgtStyle;
                        if (i == ActiveWeaponManager.weaponIndex)
                        {
                            wpnListStyle = middleLeftLabelOrange;
                            tgtStyle = targetModeStyleSelected;
                        }
                        else
                        {
                            wpnListStyle = middleLeftLabel;
                            tgtStyle = targetModeStyle;
                        }
                        string label;
                        string subLabel;
                        if (ActiveWeaponManager.weaponArray[i] != null)
                        {
                            label = ActiveWeaponManager.weaponArray[i].GetShortName();
                            subLabel = ActiveWeaponManager.weaponArray[i].GetSubLabel();
                        }
                        else
                        {
                            label = Localizer.Format("#LOC_BDArmory_WMWindow_NoneWeapon");//"None"
                            subLabel = String.Empty;
                        }
                        Rect weaponButtonRect = new Rect(leftIndent, (weaponLines * entryHeight),
                            weaponListGroupRect.width - (2 * leftIndent), entryHeight);

                        GUI.Label(weaponButtonRect, subLabel, tgtStyle);

                        if (GUI.Button(weaponButtonRect, label, wpnListStyle))
                        {
                            ActiveWeaponManager.CycleWeapon(i);
                        }

                        if (i < ActiveWeaponManager.weaponArray.Length - 1)
                        {
                            GUIUtils.DrawRectangle(
                                new Rect(weaponButtonRect.x, weaponButtonRect.y + weaponButtonRect.height,
                                    weaponButtonRect.width, 1), Color.white);
                        }
                        weaponLines++;
                    }

                    weaponLines += 0.1f;
                    GUI.EndGroup();
                }
                weaponsHeight = Mathf.Lerp(weaponsHeight, weaponLines, 0.15f);
                line += weaponsHeight;

                float guardLines = 0;
                if (showGuardMenu && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(
                        new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, (guardHeight) * entryHeight),
                        GUIContent.none, BDGuiSkin.box);
                    guardLines += 0.1f;

                    contentWidth -= 16;
                    leftIndent += 3;
                    string guardButtonLabel = Localizer.Format("#LOC_BDArmory_WMWindow_NoneWeapon", (ActiveWeaponManager.guardMode ? Localizer.Format("#LOC_BDArmory_Generic_On") : Localizer.Format("#LOC_BDArmory_Generic_Off")));//"Guard Mode " + "ON""Off"
                    if (GUI.Button(new Rect(leftIndent, (guardLines * entryHeight), contentWidth, entryHeight),
                        guardButtonLabel, ActiveWeaponManager.guardMode ? BDGuiSkin.box : BDGuiSkin.button))
                    {
                        ActiveWeaponManager.ToggleGuardMode();
                    }
                    guardLines += 1.25f;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_FiringInterval"), leftLabel);//"Firing Interval"                 
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetScanInterval =
                       GUI.HorizontalSlider(
                           new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                           ActiveWeaponManager.targetScanInterval, 0.5f, 60f);
                        ActiveWeaponManager.targetScanInterval = Mathf.Round(ActiveWeaponManager.targetScanInterval * 2f) / 2f;
                    }
                    else
                    {
                        textNumFields["targetScanInterval"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetScanInterval"].possibleValue, 4));
                        ActiveWeaponManager.targetScanInterval = (float)textNumFields["targetScanInterval"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                            ActiveWeaponManager.targetScanInterval.ToString(), leftLabel);
                    guardLines++;

                    // extension for feature_engagementenvelope: set the firing burst length
                    string burstLabel = Localizer.Format("#LOC_BDArmory_WMWindow_BurstLength");//"Burst Length"
                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), burstLabel, leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.fireBurstLength =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                ActiveWeaponManager.fireBurstLength, 0, 10);
                        ActiveWeaponManager.fireBurstLength = Mathf.Round(ActiveWeaponManager.fireBurstLength * 20f) / 20f;
                    }
                    else
                    {
                        textNumFields["fireBurstLength"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["fireBurstLength"].possibleValue, 4));
                        ActiveWeaponManager.fireBurstLength = (float)textNumFields["fireBurstLength"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.fireBurstLength.ToString(), leftLabel);
                    guardLines++;

                    // extension for feature_engagementenvelope: set the firing accuracy tolarance
                    var oldAutoFireCosAngleAdjustment = ActiveWeaponManager.AutoFireCosAngleAdjustment;
                    string accuracyLabel = Localizer.Format("#LOC_BDArmory_WMWindow_FiringTolerance");//"Firing Angle"
                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), accuracyLabel, leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.AutoFireCosAngleAdjustment =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                ActiveWeaponManager.AutoFireCosAngleAdjustment, 0, 4);
                        ActiveWeaponManager.AutoFireCosAngleAdjustment = Mathf.Round(ActiveWeaponManager.AutoFireCosAngleAdjustment * 20f) / 20f;
                    }
                    else
                    {
                        textNumFields["AutoFireCosAngleAdjustment"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["AutoFireCosAngleAdjustment"].possibleValue, 4));
                        ActiveWeaponManager.AutoFireCosAngleAdjustment = (float)textNumFields["AutoFireCosAngleAdjustment"].currentValue;
                    }
                    if (ActiveWeaponManager.AutoFireCosAngleAdjustment != oldAutoFireCosAngleAdjustment)
                        ActiveWeaponManager.OnAFCAAUpdated(null, null);
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.AutoFireCosAngleAdjustment.ToString(), leftLabel);
                    guardLines++;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_FieldofView"),//"Field of View"
                        leftLabel);
                    if (!NumFieldsEnabled)
                    {
                        float guardAngle = ActiveWeaponManager.guardAngle;
                        guardAngle =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + 90, (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                guardAngle, 10, 360);
                        guardAngle = guardAngle / 10f;
                        guardAngle = Mathf.Round(guardAngle);
                        ActiveWeaponManager.guardAngle = guardAngle * 10f;
                    }
                    else
                    {
                        textNumFields["guardAngle"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["guardAngle"].possibleValue, 4));
                        ActiveWeaponManager.guardAngle = (float)textNumFields["guardAngle"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.guardAngle.ToString(), leftLabel);
                    guardLines++;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_VisualRange"), leftLabel);//"Visual Range"
                    if (!NumFieldsEnabled)
                    {
                        float guardRange = ActiveWeaponManager.guardRange;
                        guardRange =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + 90, (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                guardRange, 100, BDArmorySettings.MAX_GUARD_VISUAL_RANGE);
                        guardRange = guardRange / 100;
                        guardRange = Mathf.Round(guardRange);
                        ActiveWeaponManager.guardRange = guardRange * 100;
                    }
                    else
                    {
                        textNumFields["guardRange"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["guardRange"].possibleValue, 8));
                        ActiveWeaponManager.guardRange = (float)textNumFields["guardRange"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.guardRange.ToString(), leftLabel);
                    guardLines++;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_GunsRange"), leftLabel);//"Guns Range"
                    if (!NumFieldsEnabled)
                    {
                        float gRange = ActiveWeaponManager.gunRange;
                        gRange =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + 90, (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                gRange, 0, ActiveWeaponManager.maxGunRange);
                        gRange /= 10f;
                        gRange = Mathf.Round(gRange);
                        gRange *= 10f;
                        ActiveWeaponManager.gunRange = gRange;
                    }
                    else
                    {
                        textNumFields["gunRange"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["gunRange"].possibleValue, 8));
                        ActiveWeaponManager.gunRange = (float)textNumFields["gunRange"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.gunRange.ToString(), leftLabel);
                    guardLines++;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_MultiTargetNum"), leftLabel);//"Max Turret targets "
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.multiTargetNum =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + 90, (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                ActiveWeaponManager.multiTargetNum, 1, 10);
                        ActiveWeaponManager.multiTargetNum = Mathf.Round(ActiveWeaponManager.multiTargetNum);
                    }
                    else
                    {
                        textNumFields["multiTargetNum"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["multiTargetNum"].possibleValue, 2));
                        ActiveWeaponManager.multiTargetNum = (float)textNumFields["multiTargetNum"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.multiTargetNum.ToString(), leftLabel);
                    guardLines++;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_MultiMissileNum"), leftLabel);//"Max Turret targets "
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.multiMissileTgtNum =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + 90, (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                ActiveWeaponManager.multiMissileTgtNum, 1, 10);
                        ActiveWeaponManager.multiMissileTgtNum = Mathf.Round(ActiveWeaponManager.multiMissileTgtNum);
                    }
                    else
                    {
                        textNumFields["multiMissileTgtNum"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["multiMissileTgtNum"].possibleValue, 2));
                        ActiveWeaponManager.multiMissileTgtNum = (float)textNumFields["multiMissileTgtNum"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.multiMissileTgtNum.ToString(), leftLabel);
                    guardLines++;

                    GUI.Label(new Rect(leftIndent, (guardLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_MissilesTgt"), leftLabel);//"Missiles/Tgt"
                    if (!NumFieldsEnabled)
                    {
                        float mslCount = ActiveWeaponManager.maxMissilesOnTarget;
                        mslCount =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + 90, (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight),
                                mslCount, 1, MissileFire.maxAllowableMissilesOnTarget);
                        mslCount = Mathf.Round(mslCount);
                        ActiveWeaponManager.maxMissilesOnTarget = mslCount;
                    }
                    else
                    {
                        textNumFields["maxMissilesOnTarget"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (guardLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["maxMissilesOnTarget"].possibleValue, 2));
                        ActiveWeaponManager.maxMissilesOnTarget = (float)textNumFields["maxMissilesOnTarget"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (guardLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.maxMissilesOnTarget.ToString(), leftLabel);
                    guardLines += 0.5f;

                    showTargetOptions = GUI.Toggle(new Rect(leftIndent, contentTop + (guardLines * entryHeight), columnWidth - (2 * leftIndent), entryHeight),
                        showTargetOptions, Localizer.Format("#LOC_BDArmory_Settings_Adv_Targeting"), showTargetOptions ? BDGuiSkin.box : BDGuiSkin.button);//"Advanced Targeting"
                    guardLines += 1.15f;

                    float TargetLines = 0;
                    if (showTargetOptions && showGuardMenu && !toolMinimized)
                    {
                        TargetLines += 0.1f;
                        GUI.BeginGroup(
                            new Rect(5, contentTop + (guardLines * entryHeight), columnWidth - 10, TargetingHeight * entryHeight),
                            GUIContent.none, BDGuiSkin.box);
                        TargetLines += 0.25f;
                        string CoMlabel = Localizer.Format("#LOC_BDArmory_TargetCOM", (ActiveWeaponManager.targetCoM ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, (TargetLines * entryHeight), (contentWidth - (2 * leftIndent)), entryHeight),
                            CoMlabel, ActiveWeaponManager.targetCoM ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetCoM = !ActiveWeaponManager.targetCoM;
                            ActiveWeaponManager.StartGuardTurretFiring(); //reset weapon targeting assignments
                            if (ActiveWeaponManager.targetCoM)
                            {
                                ActiveWeaponManager.targetCommand = false;
                                ActiveWeaponManager.targetEngine = false;
                                ActiveWeaponManager.targetWeapon = false;
                                ActiveWeaponManager.targetMass = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass))
                            {
                                ActiveWeaponManager.targetMass = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Commandlabel = Localizer.Format("#LOC_BDArmory_Command", (ActiveWeaponManager.targetCommand ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, (TargetLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Commandlabel, ActiveWeaponManager.targetCommand ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetCommand = !ActiveWeaponManager.targetCommand;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetCommand)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        string Engineslabel = Localizer.Format("#LOC_BDArmory_Engines", (ActiveWeaponManager.targetEngine ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Missile; True, False
                        if (GUI.Button(new Rect(leftIndent + ((contentWidth - (2 * leftIndent)) / 2), (TargetLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Engineslabel, ActiveWeaponManager.targetEngine ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetEngine = !ActiveWeaponManager.targetEngine;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetEngine)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;
                        string Weaponslabel = Localizer.Format("#LOC_BDArmory_Weapons", (ActiveWeaponManager.targetWeapon ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, (TargetLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Weaponslabel, ActiveWeaponManager.targetWeapon ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetWeapon = !ActiveWeaponManager.targetWeapon;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetWeapon)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        string Masslabel = Localizer.Format("#LOC_BDArmory_Mass", (ActiveWeaponManager.targetMass ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage SLW; True, False
                        if (GUI.Button(new Rect(leftIndent + ((contentWidth - (2 * leftIndent)) / 2), (TargetLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Masslabel, ActiveWeaponManager.targetMass ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.targetMass = !ActiveWeaponManager.targetMass;
                            ActiveWeaponManager.StartGuardTurretFiring();
                            if (ActiveWeaponManager.targetMass)
                            {
                                ActiveWeaponManager.targetCoM = false;
                            }
                            if (!ActiveWeaponManager.targetCoM && (!ActiveWeaponManager.targetWeapon && !ActiveWeaponManager.targetEngine && !ActiveWeaponManager.targetCommand && !ActiveWeaponManager.targetMass))
                            {
                                ActiveWeaponManager.targetCoM = true;
                            }
                        }
                        TargetLines += 1.1f;

                        ActiveWeaponManager.targetingString = (ActiveWeaponManager.targetCoM ? Localizer.Format("#LOC_BDArmory_TargetCOM") + "; " : "")
                            + (ActiveWeaponManager.targetMass ? Localizer.Format("#LOC_BDArmory_Mass") + "; " : "")
                            + (ActiveWeaponManager.targetCommand ? Localizer.Format("#LOC_BDArmory_Command") + "; " : "")
                            + (ActiveWeaponManager.targetEngine ? Localizer.Format("#LOC_BDArmory_Engines") + "; " : "")
                            + (ActiveWeaponManager.targetWeapon ? Localizer.Format("#LOC_BDArmory_Weapons") + "; " : "");
                        GUI.EndGroup();
                        TargetLines += 0.1f;
                    }
                    TargetingHeight = Mathf.Lerp(TargetingHeight, TargetLines, 0.15f);
                    guardLines += TargetingHeight;
                    guardLines += 0.1f;

                    showEngageList = GUI.Toggle(new Rect(leftIndent, contentTop + (guardLines * entryHeight), columnWidth - (2 * leftIndent), entryHeight),
                        showEngageList, showEngageList ? Localizer.Format("#LOC_BDArmory_DisableEngageOptions") : Localizer.Format("#LOC_BDArmory_EnableEngageOptions"), showEngageList ? BDGuiSkin.box : BDGuiSkin.button);//"Enable/Disable Engagement options"
                    guardLines += 1.15f;

                    float EngageLines = 0;
                    if (showEngageList && showGuardMenu && !toolMinimized)
                    {
                        EngageLines += 0.1f;
                        GUI.BeginGroup(
                            new Rect(5, contentTop + (guardLines * entryHeight), columnWidth - 10, EngageHeight * entryHeight),
                            GUIContent.none, BDGuiSkin.box);
                        EngageLines += 0.25f;

                        string Airlabel = Localizer.Format("#LOC_BDArmory_EngageAir", (ActiveWeaponManager.engageAir ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Air; True, False
                        if (GUI.Button(new Rect(leftIndent, (EngageLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Airlabel, ActiveWeaponManager.engageAir ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageAir();
                        }
                        string Missilelabel = Localizer.Format("#LOC_BDArmory_EngageMissile", (ActiveWeaponManager.engageMissile ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Missile; True, False
                        if (GUI.Button(new Rect(leftIndent + ((contentWidth - (2 * leftIndent)) / 2), (EngageLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Missilelabel, ActiveWeaponManager.engageMissile ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageMissile();
                        }
                        EngageLines += 1.1f;
                        string Srflabel = Localizer.Format("#LOC_BDArmory_EngageSurface", (ActiveWeaponManager.engageSrf ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage Surface; True, False
                        if (GUI.Button(new Rect(leftIndent, (EngageLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            Srflabel, ActiveWeaponManager.engageSrf ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageSrf();
                        }

                        string SLWlabel = Localizer.Format("#LOC_BDArmory_EngageSLW", (ActiveWeaponManager.engageSLW ? Localizer.Format("#LOC_BDArmory_false") : Localizer.Format("#LOC_BDArmory_true")));//"Engage SLW; True, False
                        if (GUI.Button(new Rect(leftIndent + ((contentWidth - (2 * leftIndent)) / 2), (EngageLines * entryHeight), ((contentWidth - (2 * leftIndent)) / 2), entryHeight),
                            SLWlabel, ActiveWeaponManager.engageSLW ? BDGuiSkin.box : BDGuiSkin.button))
                        {
                            ActiveWeaponManager.ToggleEngageSLW();
                        }
                        EngageLines += 1.1f;
                        GUI.EndGroup();
                        EngageLines += 0.1f;
                    }
                    EngageHeight = Mathf.Lerp(EngageHeight, EngageLines, 0.15f);
                    guardLines += EngageHeight;
                    guardLines += 0.1f;
                    guardLines += 0.5f;

                    guardLines += 0.1f;
                    GUI.EndGroup();
                }
                guardHeight = Mathf.Lerp(guardHeight, guardLines, 0.15f);
                line += guardHeight;

                float priorityLines = 0;
                if (showPriorities && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(
                        new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, (priorityheight) * entryHeight),
                        GUIContent.none, BDGuiSkin.box);
                    priorityLines += 0.1f;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetBias"), leftLabel);//"current target bias"                 
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetBias =
                       GUI.HorizontalSlider(
                           new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                           ActiveWeaponManager.targetBias, -10, 10);
                        ActiveWeaponManager.targetBias = Mathf.Round(ActiveWeaponManager.targetBias * 10f) / 10f;
                    }
                    else
                    {
                        textNumFields["targetBias"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetBias"].possibleValue, 4));
                        ActiveWeaponManager.targetBias = (float)textNumFields["targetBias"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                            ActiveWeaponManager.targetBias.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetProximity"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightRange =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightRange, -10, 10);
                        ActiveWeaponManager.targetWeightRange = Mathf.Round(ActiveWeaponManager.targetWeightRange * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightRange"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightRange"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightRange = (float)textNumFields["targetWeightRange"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightRange.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetPreference"), leftLabel); //target Air preference"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAirPreference =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightAirPreference, -10, 10);
                        ActiveWeaponManager.targetWeightAirPreference = Mathf.Round(ActiveWeaponManager.targetWeightAirPreference * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightAirPreference"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightAirPreference"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightAirPreference = (float)textNumFields["targetWeightAirPreference"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightAirPreference.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetAngletoTarget"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightATA =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightATA, -10, 10);
                        ActiveWeaponManager.targetWeightATA = Mathf.Round(ActiveWeaponManager.targetWeightATA * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightATA"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightATA"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightATA = (float)textNumFields["targetWeightATA"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightATA.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetAngleDist"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAoD =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightAoD, -10, 10);
                        ActiveWeaponManager.targetWeightAoD = Mathf.Round(ActiveWeaponManager.targetWeightAoD * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightAoD"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightAoD"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightAoD = (float)textNumFields["targetWeightAoD"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightAoD.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetAccel"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAccel =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightAccel, -10, 10);
                        ActiveWeaponManager.targetWeightAccel = Mathf.Round(ActiveWeaponManager.targetWeightAccel * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightAccel"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightAccel"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightAccel = (float)textNumFields["targetWeightAccel"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightAccel.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetClosingTime"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightClosureTime =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightClosureTime, -10, 10);
                        ActiveWeaponManager.targetWeightClosureTime = Mathf.Round(ActiveWeaponManager.targetWeightClosureTime * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightClosureTime"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightClosureTime"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightClosureTime = (float)textNumFields["targetWeightClosureTime"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightClosureTime.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetgunNumber"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightWeaponNumber =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightWeaponNumber, -10, 10);
                        ActiveWeaponManager.targetWeightWeaponNumber = Mathf.Round(ActiveWeaponManager.targetWeightWeaponNumber * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightWeaponNumber"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightWeaponNumber"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightWeaponNumber = (float)textNumFields["targetWeightWeaponNumber"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightWeaponNumber.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetMass"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightMass =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightMass, -10, 10);
                        ActiveWeaponManager.targetWeightMass = Mathf.Round(ActiveWeaponManager.targetWeightMass * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightMass"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightMass"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightMass = (float)textNumFields["targetWeightMass"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightMass.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetAllies"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightFriendliesEngaging =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightFriendliesEngaging, -10, 10);
                        ActiveWeaponManager.targetWeightFriendliesEngaging = Mathf.Round(ActiveWeaponManager.targetWeightFriendliesEngaging * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightFriendliesEngaging"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightFriendliesEngaging"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightFriendliesEngaging = (float)textNumFields["targetWeightFriendliesEngaging"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightFriendliesEngaging.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetThreat"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightThreat =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightThreat, -10, 10);
                        ActiveWeaponManager.targetWeightThreat = Mathf.Round(ActiveWeaponManager.targetWeightThreat * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightThreat"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightThreat"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightThreat = (float)textNumFields["targetWeightThreat"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightThreat.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_defendTeammate"), leftLabel); //defend teammate"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightProtectTeammate =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightProtectTeammate, -10, 10);
                        ActiveWeaponManager.targetWeightProtectTeammate = Mathf.Round(ActiveWeaponManager.targetWeightProtectTeammate * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightProtectTeammate"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightProtectTeammate"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightProtectTeammate = (float)textNumFields["targetWeightProtectTeammate"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightProtectTeammate.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_defendVIP"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightProtectVIP =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightProtectVIP, -10, 10);
                        ActiveWeaponManager.targetWeightProtectVIP = Mathf.Round(ActiveWeaponManager.targetWeightProtectVIP * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightProtectVIP"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightProtectVIP"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightProtectVIP = (float)textNumFields["targetWeightProtectVIP"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightProtectVIP.ToString(), leftLabel);
                    priorityLines++;

                    GUI.Label(new Rect(leftIndent, (priorityLines * entryHeight), 85, entryHeight), Localizer.Format("#LOC_BDArmory_WMWindow_targetVIP"), leftLabel); //target proximity"
                    if (!NumFieldsEnabled)
                    {
                        ActiveWeaponManager.targetWeightAttackVIP =
                            GUI.HorizontalSlider(
                                new Rect(leftIndent + (150), (priorityLines * entryHeight), contentWidth - 150 - 38, entryHeight),
                                ActiveWeaponManager.targetWeightAttackVIP, -10, 10);
                        ActiveWeaponManager.targetWeightAttackVIP = Mathf.Round(ActiveWeaponManager.targetWeightAttackVIP * 10) / 10;
                    }
                    else
                    {
                        textNumFields["targetWeightAttackVIP"].tryParseValue(GUI.TextField(new Rect(leftIndent + (90), (priorityLines * entryHeight), contentWidth - 90 - 38, entryHeight), textNumFields["targetWeightAttackVIP"].possibleValue, 4));
                        ActiveWeaponManager.targetWeightAttackVIP = (float)textNumFields["targetWeightAttackVIP"].currentValue;
                    }
                    GUI.Label(new Rect(leftIndent + (contentWidth - 35), (priorityLines * entryHeight), 35, entryHeight),
                        ActiveWeaponManager.targetWeightAttackVIP.ToString(), leftLabel);
                    priorityLines++;

                    priorityLines += 0.1f;
                    GUI.EndGroup();
                }
                priorityheight = Mathf.Lerp(priorityheight, priorityLines, 0.15f);
                line += priorityheight;

                float moduleLines = 0;
                if (showModules && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(
                        new Rect(5, contentTop + (line * entryHeight), columnWidth - 10, numberOfModules * entryHeight),
                        GUIContent.none, BDGuiSkin.box);
                    moduleLines += 0.1f;

                    numberOfModules = 0;
                    //RWR
                    if (ActiveWeaponManager.rwr)
                    {
                        numberOfModules++;
                        bool isEnabled = ActiveWeaponManager.rwr.displayRWR;
                        string label = Localizer.Format("#LOC_BDArmory_WMWindow_RadarWarning");//"Radar Warning Receiver"
                        Rect rwrRect = new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight);
                        if (GUI.Button(rwrRect, label, isEnabled ? centerLabelOrange : centerLabel))
                        {
                            if (isEnabled)
                            {
                                //ActiveWeaponManager.rwr.DisableRWR();
                                ActiveWeaponManager.rwr.displayRWR = false;
                            }
                            else
                            {
                                //ActiveWeaponManager.rwr.EnableRWR();
                                ActiveWeaponManager.rwr.displayRWR = true;
                            }
                        }
                        moduleLines++;
                    }

                    //TGP
                    using (List<ModuleTargetingCamera>.Enumerator mtc = ActiveWeaponManager.targetingPods.GetEnumerator())
                        while (mtc.MoveNext())
                        {
                            if (mtc.Current == null) continue;
                            numberOfModules++;
                            bool isEnabled = (mtc.Current.cameraEnabled);
                            bool isActive = (mtc.Current == ModuleTargetingCamera.activeCam);
                            GUIStyle moduleStyle = isEnabled ? centerLabelOrange : centerLabel; // = mtc
                            string label = mtc.Current.part.partInfo.title;
                            if (isActive)
                            {
                                moduleStyle = centerLabelRed;
                                label = "[" + label + "]";
                            }
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                                label, moduleStyle))
                            {
                                if (isActive)
                                {
                                    mtc.Current.ToggleCamera();
                                }
                                else
                                {
                                    mtc.Current.EnableCamera();
                                }
                            }
                            moduleLines++;
                        }

                    //RADAR
                    using (List<ModuleRadar>.Enumerator mr = ActiveWeaponManager.radars.GetEnumerator())
                        while (mr.MoveNext())
                        {
                            if (mr.Current == null) continue;
                            numberOfModules++;
                            GUIStyle moduleStyle = mr.Current.radarEnabled ? centerLabelBlue : centerLabel;
                            string label = mr.Current.radarName;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                                label, moduleStyle))
                            {
                                mr.Current.Toggle();
                            }
                            moduleLines++;
                        }
                    using (List<ModuleIRST>.Enumerator mr = ActiveWeaponManager.irsts.GetEnumerator())
                        while (mr.MoveNext())
                        {
                            if (mr.Current == null) continue;
                            numberOfModules++;
                            GUIStyle moduleStyle = mr.Current.irstEnabled ? centerLabelBlue : centerLabel;
                            string label = mr.Current.IRSTName;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                                label, moduleStyle))
                            {
                                mr.Current.Toggle();
                            }
                            moduleLines++;
                        }
                    //JAMMERS
                    using (List<ModuleECMJammer>.Enumerator jammer = ActiveWeaponManager.jammers.GetEnumerator())
                        while (jammer.MoveNext())
                        {
                            if (jammer.Current == null) continue;
                            if (jammer.Current.alwaysOn) continue;

                            numberOfModules++;
                            GUIStyle moduleStyle = jammer.Current.jammerEnabled ? centerLabelBlue : centerLabel;
                            string label = jammer.Current.part.partInfo.title;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                                label, moduleStyle))
                            {
                                jammer.Current.Toggle();
                            }
                            moduleLines++;
                        }
                    //CLOAKS
                    using (List<ModuleCloakingDevice>.Enumerator cloak = ActiveWeaponManager.cloaks.GetEnumerator())
                        while (cloak.MoveNext())
                        {
                            if (cloak.Current == null) continue;
                            if (cloak.Current.alwaysOn) continue;

                            numberOfModules++;
                            GUIStyle moduleStyle = cloak.Current.cloakEnabled ? centerLabelBlue : centerLabel;
                            string label = cloak.Current.part.partInfo.title;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                                label, moduleStyle))
                            {
                                cloak.Current.Toggle();
                            }
                            moduleLines++;
                        }

                    //Other modules
                    using (var module = ActiveWeaponManager.wmModules.GetEnumerator())
                        while (module.MoveNext())
                        {
                            if (module.Current == null) continue;

                            numberOfModules++;
                            GUIStyle moduleStyle = module.Current.Enabled ? centerLabelBlue : centerLabel;
                            string label = module.Current.Name;
                            if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                                label, moduleStyle))
                            {
                                module.Current.Toggle();
                            }
                            moduleLines++;
                        }

                    //GPS coordinator
                    GUIStyle gpsModuleStyle = showWindowGPS ? centerLabelBlue : centerLabel;
                    numberOfModules++;
                    if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                        Localizer.Format("#LOC_BDArmory_WMWindow_GPSCoordinator"), gpsModuleStyle))//"GPS Coordinator"
                    {
                        showWindowGPS = !showWindowGPS;
                    }
                    moduleLines++;

                    //wingCommander
                    if (ActiveWeaponManager.wingCommander)
                    {
                        GUIStyle wingComStyle = ActiveWeaponManager.wingCommander.showGUI
                            ? centerLabelBlue
                            : centerLabel;
                        numberOfModules++;
                        if (GUI.Button(new Rect(leftIndent, +(moduleLines * entryHeight), contentWidth, entryHeight),
                            Localizer.Format("#LOC_BDArmory_WMWindow_WingCommand"), wingComStyle))//"Wing Command"
                        {
                            ActiveWeaponManager.wingCommander.ToggleGUI();
                        }
                        moduleLines++;
                    }

                    moduleLines += 0.1f;
                    GUI.EndGroup();
                }
                modulesHeight = Mathf.Lerp(modulesHeight, moduleLines, 0.15f);
                line += modulesHeight;

                float gpsLines = 0;
                if (showWindowGPS && !toolMinimized)
                {
                    line += 0.25f;
                    GUI.BeginGroup(new Rect(5, contentTop + (line * entryHeight), columnWidth, WindowRectGps.height));
                    WindowGPS();
                    GUI.EndGroup();
                    gpsLines = WindowRectGps.height / entryHeight;
                }
                gpsHeight = Mathf.Lerp(gpsHeight, gpsLines, 0.15f);
                line += gpsHeight;

                if (infoLinkEnabled && !toolMinimized)
                {
                    windowColumns = 2;

                    GUI.Label(new Rect(leftIndent + columnWidth, contentTop, columnWidth - (leftIndent), entryHeight), Localizer.Format("#LOC_BDArmory_AIWindow_infoLink"), kspTitleLabel);//"infolink"
                    GUILayout.BeginArea(new Rect(leftIndent + columnWidth, contentTop + (entryHeight * 1.5f), columnWidth - (leftIndent), toolWindowHeight - (entryHeight * 1.5f) - (2 * contentTop)));
                    using (var scrollViewScope = new GUILayout.ScrollViewScope(scrollInfoVector, GUILayout.Width(columnWidth - (leftIndent)), GUILayout.Height(toolWindowHeight - (entryHeight * 1.5f) - (2 * contentTop))))
                    {
                        scrollInfoVector = scrollViewScope.scrollPosition;
                        if (showWeaponList)
                        {
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_ListWeapons"), leftLabelBold, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Weapons
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_Weapons_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //weapons desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_Ripple_Salvo_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //ripple/salvo desc
                        }
                        if (showGuardMenu)
                        {
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_GuardMenu"), leftLabelBold, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Guard Mode
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_GuardTab_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Guard desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_FiringInterval_Desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //firing inverval desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_BurstLength_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //burst length desc
                            GUILayout.Label(FiringAngleImage);
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_FiringTolerance_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //firing angle desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_FieldofView_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //FoV desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_VisualRange_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //guard range desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_GunsRange_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //weapon range desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_MultiTargetNum_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //multiturrets desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_MultiMissileTgtNum_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //multiturrets desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_MissilesTgt_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //multimissiles desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_TargetType_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //subsection targeting desc
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_EngageType_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //engagement toggles desc
                        }
                        if (showPriorities)
                        {
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_Prioritues_Desc"), leftLabelBold, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt Priorities
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetBias_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt Bias
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetPreference_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt engagement Pref
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetProximity_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt dist
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetAngletoTarget_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt angle
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetAngleDist_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt angle/dist
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetAccel_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt accel
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetClosingTime_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt closing time
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetgunNumber_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt weapons num
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetMass_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt mass
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetAllies_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt allies attacking
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetThreat_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt threat
                            GUILayout.Label(Localizer.Format("#LOC_BDArmory_WMWindow_targetVIP_desc"), infoLinkStyle, GUILayout.Width(columnWidth - (leftIndent * 4) - 20)); //Tgt VIP
                        }

                    }
                    GUILayout.EndArea();
                }
            }
            else
            {
                GUI.Label(new Rect(leftIndent, contentTop + (line * entryHeight), contentWidth, entryHeight),
                   Localizer.Format("#LOC_BDArmory_WMWindow_NoWeaponManager"), BDGuiSkin.box);// "No Weapon Manager found."
                line++;
            }
            toolWindowWidth = Mathf.Lerp(toolWindowWidth, columnWidth * windowColumns, 0.15f);
            toolWindowHeight = Mathf.Lerp(toolWindowHeight, contentTop + (line * entryHeight) + 5, 1);
            var previousWindowHeight = WindowRectToolbar.height;
            WindowRectToolbar.height = toolWindowHeight;
            WindowRectToolbar.width = toolWindowWidth;
            numberOfButtons = buttonNumber + 1;
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES && toolWindowHeight < previousWindowHeight && Mathf.Round(WindowRectToolbar.y + previousWindowHeight) == Screen.height) // Window shrunk while being at edge of screen.
                WindowRectToolbar.y = Screen.height - WindowRectToolbar.height;
            GUIUtils.RepositionWindow(ref WindowRectToolbar);
        }

        bool validGPSName = true;

        //GPS window
        public void WindowGPS()
        {
            GUI.Box(WindowRectGps, GUIContent.none, BDGuiSkin.box);
            gpsEntryCount = 0;
            Rect listRect = new Rect(gpsBorder, gpsBorder, WindowRectGps.width - (2 * gpsBorder),
                WindowRectGps.height - (2 * gpsBorder));
            GUI.BeginGroup(listRect);
            string targetLabel = Localizer.Format("#LOC_BDArmory_WMWindow_GPSTarget") + ": " + ActiveWeaponManager.designatedGPSInfo.name;//GPS Target
            GUI.Label(new Rect(0, 0, listRect.width, gpsEntryHeight), targetLabel, kspTitleLabel);

            // Expand/Collapse Target Toggle button
            if (GUI.Button(new Rect(listRect.width - gpsEntryHeight, 0, gpsEntryHeight, gpsEntryHeight), showTargets ? "-" : "+", BDGuiSkin.button))
                showTargets = !showTargets;

            gpsEntryCount += 0.85f;
            if (ActiveWeaponManager.designatedGPSCoords != Vector3d.zero)
            {
                GUI.Label(new Rect(0, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight, gpsEntryHeight),
                    BodyUtils.FormattedGeoPos(ActiveWeaponManager.designatedGPSCoords, true), BDGuiSkin.box);
                if (
                    GUI.Button(
                        new Rect(listRect.width - gpsEntryHeight, gpsEntryCount * gpsEntryHeight, gpsEntryHeight,
                            gpsEntryHeight), "X", BDGuiSkin.button))
                {
                    ActiveWeaponManager.designatedGPSInfo = new GPSTargetInfo();
                }
            }
            else
            {
                GUI.Label(new Rect(0, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight, gpsEntryHeight),
                    Localizer.Format("#LOC_BDArmory_WMWindow_NoTarget"), BDGuiSkin.box);//"No Target"
            }

            gpsEntryCount += 1.35f;
            int indexToRemove = -1;
            int index = 0;
            BDTeam myTeam = ActiveWeaponManager.Team;
            if (showTargets)
            {
                List<GPSTargetInfo>.Enumerator coordinate = BDATargetManager.GPSTargetList(myTeam).GetEnumerator();
                while (coordinate.MoveNext())
                {
                    Color origWColor = GUI.color;
                    if (coordinate.Current.EqualsTarget(ActiveWeaponManager.designatedGPSInfo))
                    {
                        GUI.color = XKCDColors.LightOrange;
                    }

                    string label = BodyUtils.FormattedGeoPosShort(coordinate.Current.gpsCoordinates, false);
                    float nameWidth = 100;
                    if (editingGPSName && index == editingGPSNameIndex)
                    {
                        if (validGPSName && Event.current.type == EventType.KeyDown &&
                            Event.current.keyCode == KeyCode.Return)
                        {
                            editingGPSName = false;
                            hasEnteredGPSName = true;
                        }
                        else
                        {
                            Color origColor = GUI.color;
                            if (newGPSName.Contains(";") || newGPSName.Contains(":") || newGPSName.Contains(","))
                            {
                                validGPSName = false;
                                GUI.color = Color.red;
                            }
                            else
                            {
                                validGPSName = true;
                            }

                            newGPSName = GUI.TextField(
                              new Rect(0, gpsEntryCount * gpsEntryHeight, nameWidth, gpsEntryHeight), newGPSName, 12);
                            GUI.color = origColor;
                        }
                    }
                    else
                    {
                        if (GUI.Button(new Rect(0, gpsEntryCount * gpsEntryHeight, nameWidth, gpsEntryHeight),
                          coordinate.Current.name,
                          BDGuiSkin.button))
                        {
                            editingGPSName = true;
                            editingGPSNameIndex = index;
                            newGPSName = coordinate.Current.name;
                        }
                    }

                    if (
                      GUI.Button(
                        new Rect(nameWidth, gpsEntryCount * gpsEntryHeight, listRect.width - gpsEntryHeight - nameWidth,
                          gpsEntryHeight), label, BDGuiSkin.button))
                    {
                        ActiveWeaponManager.designatedGPSInfo = coordinate.Current;
                        editingGPSName = false;
                    }

                    if (
                      GUI.Button(
                        new Rect(listRect.width - gpsEntryHeight, gpsEntryCount * gpsEntryHeight, gpsEntryHeight,
                          gpsEntryHeight), "X", BDGuiSkin.button))
                    {
                        indexToRemove = index;
                    }

                    gpsEntryCount++;
                    index++;
                    GUI.color = origWColor;
                }
                coordinate.Dispose();
            }

            if (hasEnteredGPSName && editingGPSNameIndex < BDATargetManager.GPSTargetList(myTeam).Count)
            {
                hasEnteredGPSName = false;
                GPSTargetInfo old = BDATargetManager.GPSTargetList(myTeam)[editingGPSNameIndex];
                if (ActiveWeaponManager.designatedGPSInfo.EqualsTarget(old))
                {
                    ActiveWeaponManager.designatedGPSInfo.name = newGPSName;
                }
                BDATargetManager.GPSTargetList(myTeam)[editingGPSNameIndex] =
                    new GPSTargetInfo(BDATargetManager.GPSTargetList(myTeam)[editingGPSNameIndex].gpsCoordinates,
                        newGPSName);
                editingGPSNameIndex = 0;
                BDATargetManager.Instance.SaveGPSTargets();
            }

            GUI.EndGroup();

            if (indexToRemove >= 0)
            {
                BDATargetManager.GPSTargetList(myTeam).RemoveAt(indexToRemove);
                BDATargetManager.Instance.SaveGPSTargets();
            }

            WindowRectGps.height = (2 * gpsBorder) + (gpsEntryCount * gpsEntryHeight);
        }

        Rect SLineRect(float line, float indentLevel = 0, bool symmetric = false)
        {
            return new Rect(settingsMargin + indentLevel * settingsMargin, line * settingsLineHeight, settingsWidth - 2 * settingsMargin - (symmetric ? 2 : 1) * indentLevel * settingsMargin, settingsLineHeight);
        }

        Rect SLeftRect(float line, float indentLevel = 0, bool symmetric = false)
        {
            return new Rect(settingsMargin + indentLevel * settingsMargin, line * settingsLineHeight, settingsWidth / 2 - settingsMargin - settingsMargin / 4 - (symmetric ? 2 : 1) * indentLevel * settingsMargin, settingsLineHeight);
        }

        Rect SRightRect(float line, float indentLevel = 0, bool symmetric = false)
        {
            return new Rect(settingsWidth / 2 + settingsMargin / 4 + indentLevel * settingsMargin, line * settingsLineHeight, settingsWidth / 2 - settingsMargin - settingsMargin / 4 - (symmetric ? 2 : 1) * indentLevel * settingsMargin, settingsLineHeight);
        }

        Rect SLeftSliderRect(float line, float indentLevel = 0)
        {
            return new Rect(settingsMargin + indentLevel * settingsMargin, (line + 0.1f) * settingsLineHeight, settingsWidth / 2 + settingsMargin / 2 - indentLevel * settingsMargin, settingsLineHeight); // Sliders are slightly out of alignment vertically.
        }

        Rect SRightSliderRect(float line)
        {
            return new Rect(settingsMargin + settingsWidth / 2 + settingsMargin / 2, (line + 0.2f) * settingsLineHeight, settingsWidth / 2 - 7 / 2 * settingsMargin, settingsLineHeight); // Sliders are slightly out of alignment vertically.
        }

        Rect SLeftButtonRect(float line)
        {
            return new Rect(settingsMargin, line * settingsLineHeight, (settingsWidth - 2 * settingsMargin) / 2 - settingsMargin / 4, settingsLineHeight);
        }

        Rect SRightButtonRect(float line)
        {
            return new Rect(settingsWidth / 2 + settingsMargin / 4, line * settingsLineHeight, (settingsWidth - 2 * settingsMargin) / 2 - settingsMargin / 4, settingsLineHeight);
        }

        Rect SLineThirdRect(float line, int pos)
        {
            return new Rect(settingsMargin + pos * (settingsWidth - 2f * settingsMargin) / 3f, line * settingsLineHeight, (settingsWidth - 2f * settingsMargin) / 3f, settingsLineHeight);
        }

        Rect SQuarterRect(float line, int pos, int span = 1)
        {
            return new Rect(settingsMargin + (pos % 4) * (settingsWidth - 2f * settingsMargin) / 4f, (line + (int)(pos / 4)) * settingsLineHeight, span * (settingsWidth - 2f * settingsMargin) / 4f, settingsLineHeight);
        }

        Rect SEighthRect(float line, int pos)
        {
            return new Rect(settingsMargin + (pos % 8) * (settingsWidth - 2f * settingsMargin) / 8f, (line + (int)(pos / 8)) * settingsLineHeight, (settingsWidth - 2.5f * settingsMargin) / 8f, settingsLineHeight);
        }

        List<Rect> SRight2Rects(float line)
        {
            var rectGap = settingsMargin / 2;
            var rectWidth = ((settingsWidth - 2 * settingsMargin) / 2 - 2 * rectGap) / 2;
            var rects = new List<Rect>();
            rects.Add(new Rect(settingsWidth / 2 + rectGap / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            rects.Add(new Rect(settingsWidth / 2 + rectWidth + rectGap * 3 / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            return rects;
        }

        List<Rect> SRight3Rects(float line)
        {
            var rectGap = settingsMargin / 3;
            var rectWidth = ((settingsWidth - 2 * settingsMargin) / 2 - 3 * rectGap) / 3;
            var rects = new List<Rect>();
            rects.Add(new Rect(settingsWidth / 2 + rectGap / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            rects.Add(new Rect(settingsWidth / 2 + rectWidth + rectGap * 3 / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            rects.Add(new Rect(settingsWidth / 2 + 2 * rectWidth + rectGap * 5 / 2, line * settingsLineHeight, rectWidth, settingsLineHeight));
            return rects;
        }

        float settingsWidth;
        float settingsHeight;
        float settingsLeft;
        float settingsTop;
        float settingsLineHeight;
        float settingsMargin;

        private Vector2 scrollViewVector;
        private bool selectMutators = false;
        public List<string> selectedMutators;
        float mutatorHeight = 25;
        bool editKeys;

        void SetupSettingsSize()
        {
            settingsWidth = 420;
            settingsHeight = 480;
            settingsLeft = Screen.width / 2 - settingsWidth / 2;
            settingsTop = 100;
            settingsLineHeight = 22;
            settingsMargin = 12;
            WindowRectSettings = new Rect(settingsLeft, settingsTop, settingsWidth, settingsHeight);
        }

        void WindowSettings(int windowID)
        {
            float line = 0.25f; // Top internal margin.
            GUI.Box(new Rect(0, 0, settingsWidth, settingsHeight), Localizer.Format("#LOC_BDArmory_Settings_Title"));//"BDArmory Settings"
            if (GUI.Button(new Rect(settingsWidth - 18, 2, 16, 16), "X"))
            {
                windowSettingsEnabled = false;
            }
            GUI.DragWindow(new Rect(0, 0, settingsWidth, 25));
            if (editKeys)
            {
                InputSettings();
                return;
            }

            GameSettings.ADVANCED_TWEAKABLES = GUI.Toggle(GameSettings.ADVANCED_TWEAKABLES ? SLeftRect(++line) : SLineRect(++line), GameSettings.ADVANCED_TWEAKABLES, Localizer.Format("#autoLOC_900906") + (GameSettings.ADVANCED_TWEAKABLES ? "" : " <— Access many more AI tuning options")); // Advanced tweakables
            BDArmorySettings.ADVANDED_USER_SETTINGS = GUI.Toggle(GameSettings.ADVANCED_TWEAKABLES ? SRightRect(line) : SLineRect(++line), BDArmorySettings.ADVANDED_USER_SETTINGS, Localizer.Format("#LOC_BDArmory_Settings_AdvancedUserSettings"));// Advanced User Settings

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.GRAPHICS_UI_SECTION_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_GraphicsSettingsToggle")))//Show/hide Graphics/UI settings.
            {
                BDArmorySettings.GRAPHICS_UI_SECTION_TOGGLE = !BDArmorySettings.GRAPHICS_UI_SECTION_TOGGLE;
            }
            if (BDArmorySettings.GRAPHICS_UI_SECTION_TOGGLE)
            {
                line += 0.2f;
                BDArmorySettings.DRAW_AIMERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DRAW_AIMERS, Localizer.Format("#LOC_BDArmory_Settings_DrawAimers"));//"Draw Aimers"

                if (!BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    BDArmorySettings.BULLET_HITS = GUI.Toggle(SRightRect(line), BDArmorySettings.BULLET_HITS, Localizer.Format("#LOC_BDArmory_Settings_BulletFX"));//"Bullet Hits"
                    BDArmorySettings.BULLET_DECALS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.EJECT_SHELLS = BDArmorySettings.BULLET_HITS;
                    BDArmorySettings.SHELL_COLLISIONS = BDArmorySettings.BULLET_HITS;
                }
                else
                {
                    BDArmorySettings.BULLET_HITS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BULLET_HITS, Localizer.Format("#LOC_BDArmory_Settings_BulletHits"));//"Bullet Hits"
                    if (BDArmorySettings.BULLET_HITS)
                    {
                        BDArmorySettings.BULLET_DECALS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.BULLET_DECALS, Localizer.Format("#LOC_BDArmory_Settings_BulletHoleDecals"));//"Bullet Hole Decals"
                        if (BDArmorySettings.BULLET_HITS)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1), $"{Localizer.Format("#LOC_BDArmory_Settings_MaxBulletHoles")}:  ({BDArmorySettings.MAX_NUM_BULLET_DECALS})", leftLabel); // Max Bullet Holes
                            if (BDArmorySettings.MAX_NUM_BULLET_DECALS != (BDArmorySettings.MAX_NUM_BULLET_DECALS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MAX_NUM_BULLET_DECALS, 1f, 999f))))
                                BulletHitFX.AdjustDecalPoolSizes(BDArmorySettings.MAX_NUM_BULLET_DECALS);
                        }
                    }
                    BDArmorySettings.EJECT_SHELLS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.EJECT_SHELLS, Localizer.Format("#LOC_BDArmory_Settings_EjectShells"));//"Eject Shells"
                    if (BDArmorySettings.EJECT_SHELLS)
                    {
                        BDArmorySettings.SHELL_COLLISIONS = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.SHELL_COLLISIONS, Localizer.Format("#LOC_BDArmory_Settings_ShellCollisions"));//"Shell Collisions"}
                    }
                }

                BDArmorySettings.SHOW_AMMO_GAUGES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SHOW_AMMO_GAUGES, Localizer.Format("#LOC_BDArmory_Settings_AmmoGauges"));//"Ammo Gauges"
                //BDArmorySettings.PERSISTENT_FX = GUI.Toggle(SRightRect(line), BDArmorySettings.PERSISTENT_FX, Localizer.Format("#LOC_BDArmory_Settings_PersistentFX"));//"Persistent FX"
                BDArmorySettings.GAPLESS_PARTICLE_EMITTERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.GAPLESS_PARTICLE_EMITTERS, Localizer.Format("#LOC_BDArmory_Settings_GaplessParticleEmitters"));//"Gapless Particle Emitters"
                if (BDArmorySettings.FLARE_SMOKE != (BDArmorySettings.FLARE_SMOKE = GUI.Toggle(SRightRect(line), BDArmorySettings.FLARE_SMOKE, Localizer.Format("#LOC_BDArmory_Settings_FlareSmoke"))))//"Flare Smoke"
                {
                    foreach (var flareObj in CMDropper.flarePool.pool)
                        if (flareObj.activeInHierarchy)
                        {
                            var flare = flareObj.GetComponent<CMFlare>();
                            if (flare == null) continue;
                            flare.EnableEmitters();
                        }
                }
                BDArmorySettings.STRICT_WINDOW_BOUNDARIES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.STRICT_WINDOW_BOUNDARIES, Localizer.Format("#LOC_BDArmory_Settings_StrictWindowBoundaries"));//"Strict Window Boundaries"
                if (BDArmorySettings.AI_TOOLBAR_BUTTON != (BDArmorySettings.AI_TOOLBAR_BUTTON = GUI.Toggle(SRightRect(line), BDArmorySettings.AI_TOOLBAR_BUTTON, Localizer.Format("#LOC_BDArmory_Settings_AIToolbarButton")))) // AI Toobar Button
                {
                    if (BDArmorySettings.AI_TOOLBAR_BUTTON)
                    { BDArmoryAIGUI.Instance.AddToolbarButton(); }
                    else
                    { BDArmoryAIGUI.Instance.RemoveToolbarButton(); }
                }
                BDArmorySettings.DISPLAY_COMPETITION_STATUS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DISPLAY_COMPETITION_STATUS, Localizer.Format("#LOC_BDArmory_Settings_DisplayCompetitionStatus"));
                if (BDArmorySettings.DISPLAY_COMPETITION_STATUS)
                {
                    BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI, Localizer.Format("#LOC_BDArmory_Settings_DisplayCompetitionStatusHiddenUI"));
                }
                if (HighLogic.LoadedSceneIsEditor && BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    if (BDArmorySettings.SHOW_CATEGORIES != (BDArmorySettings.SHOW_CATEGORIES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SHOW_CATEGORIES, Localizer.Format("#LOC_BDArmory_Settings_ShowEditorSubcategories"))))//"Show Editor Subcategories"
                    {
                        KSP.UI.Screens.PartCategorizer.Instance.editorPartList.Refresh();
                    }
                    if (BDArmorySettings.AUTOCATEGORIZE_PARTS != (BDArmorySettings.AUTOCATEGORIZE_PARTS = GUI.Toggle(SRightRect(line), BDArmorySettings.AUTOCATEGORIZE_PARTS, Localizer.Format("#LOC_BDArmory_Settings_AutocategorizeParts"))))//"Autocategorize Parts"
                    {
                        KSP.UI.Screens.PartCategorizer.Instance.editorPartList.Refresh();
                    }
                }

                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    { // GUI background opacity
                        GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_GUIBackgroundOpacity") + $" ({BDArmorySettings.GUI_OPACITY.ToString("F2")})", leftLabel);
                        BDArmorySettings.GUI_OPACITY = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.GUI_OPACITY, 0f, 1f), 0.05f);
                    }

                    if (GUI.Button(SLineRect(++line, 1, true), (BDArmorySettings.DEBUG_SETTINGS_TOGGLE ? "Disable " : "Enable ") + Localizer.Format("#LOC_BDArmory_Settings_DebugSettingsToggle")))//Enable/Disable Debugging.
                    {
                        BDArmorySettings.DEBUG_SETTINGS_TOGGLE = !BDArmorySettings.DEBUG_SETTINGS_TOGGLE;
                        if (!BDArmorySettings.DEBUG_SETTINGS_TOGGLE) // Disable all debugging when closing the debugging section.
                        {
                            BDArmorySettings.DEBUG_AI = false;
                            BDArmorySettings.DEBUG_ARMOR = false;
                            BDArmorySettings.DEBUG_DAMAGE = false;
                            BDArmorySettings.DEBUG_OTHER = false;
                            BDArmorySettings.DEBUG_LINES = false;
                            BDArmorySettings.DEBUG_MISSILES = false;
                            BDArmorySettings.DEBUG_RADAR = false;
                            BDArmorySettings.DEBUG_TELEMETRY = false;
                            BDArmorySettings.DEBUG_WEAPONS = false;
                            BDArmorySettings.DEBUG_SPAWNING = false;
                        }
                    }
                    if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE)
                    {
                        BDArmorySettings.DEBUG_TELEMETRY = GUI.Toggle(SQuarterRect(++line, 0, 2), BDArmorySettings.DEBUG_TELEMETRY, Localizer.Format("#LOC_BDArmory_Settings_DebugTelemetry"));//"On-Screen Telemetry"
                        BDArmorySettings.DEBUG_LINES = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_LINES, Localizer.Format("#LOC_BDArmory_Settings_DebugLines"));//"Debug Lines"
                        BDArmorySettings.DEBUG_WEAPONS = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_WEAPONS, Localizer.Format("#LOC_BDArmory_Settings_DebugWeapons"));//"Debug Weapons"
                        BDArmorySettings.DEBUG_MISSILES = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_MISSILES, Localizer.Format("#LOC_BDArmory_Settings_DebugMissiles"));//"Debug Missiles"
                        BDArmorySettings.DEBUG_ARMOR = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_ARMOR, Localizer.Format("#LOC_BDArmory_Settings_DebugArmor"));//"Debug Armor"
                        BDArmorySettings.DEBUG_DAMAGE = GUI.Toggle(SQuarterRect(line, 3), BDArmorySettings.DEBUG_DAMAGE, Localizer.Format("#LOC_BDArmory_Settings_DebugDamage"));//"Debug Damage"
                        BDArmorySettings.DEBUG_AI = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_AI, Localizer.Format("#LOC_BDArmory_Settings_DebugAI"));//"Debug AI"
                        BDArmorySettings.DEBUG_COMPETITION = GUI.Toggle(SQuarterRect(line, 1), BDArmorySettings.DEBUG_COMPETITION, Localizer.Format("#LOC_BDArmory_Settings_DebugCompetition"));//"Debug Competition"
                        BDArmorySettings.DEBUG_RADAR = GUI.Toggle(SQuarterRect(line, 2), BDArmorySettings.DEBUG_RADAR, Localizer.Format("#LOC_BDArmory_Settings_DebugRadar"));//"Debug Detectors"
                        BDArmorySettings.DEBUG_SPAWNING = GUI.Toggle(SQuarterRect(line, 3), BDArmorySettings.DEBUG_SPAWNING, Localizer.Format("#LOC_BDArmory_Settings_DebugSpawning"));//"Debug Spawning"
                        BDArmorySettings.DEBUG_OTHER = GUI.Toggle(SQuarterRect(++line, 0), BDArmorySettings.DEBUG_OTHER, Localizer.Format("#LOC_BDArmory_Settings_DebugOther"));//"Debug Other"
                    }
#if DEBUG  // Only visible when compiled in Debug configuration.
                    if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE)
                    {
                        if (BDACompetitionMode.Instance != null)
                        {
                            if (GUI.Button(SLeftRect(++line), "Run DEBUG checks"))// Run DEBUG checks
                            {
                                switch (Event.current.button)
                                {
                                    case 1: // right click
                                        StartCoroutine(BDACompetitionMode.Instance.CheckGCPerformance());
                                        break;
                                    default:
                                        BDACompetitionMode.Instance.CleanUpKSPsDeadReferences();
                                        BDACompetitionMode.Instance.RunDebugChecks();
                                        break;
                                }
                            }
                            if (GUI.Button(SLeftRect(++line), "Test Vessel Module Registry"))
                            {
                                StartCoroutine(VesselModuleRegistry.Instance.PerformanceTest());
                            }
                        }
                        // if (GUI.Button(SLineRect(++line), "timing test")) // Timing tests.
                        // {
                        //     var test = FlightGlobals.ActiveVessel.transform.position;
                        //     float FiringTolerance = 1f;
                        //     float targetRadius = 20f;
                        //     Vector3 finalAimTarget = new Vector3(10f, 20f, 30f);
                        //     Vector3 pos = new Vector3(2f, 3f, 4f);
                        //     float theta_const = Mathf.Deg2Rad * 1f;
                        //     float test_out = 0f;
                        //     int iters = 10000000;
                        //     var now = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < iters; ++i)
                        //     {
                        //         test_out = i > iters ? 1f : 1f - 0.5f * FiringTolerance * FiringTolerance * targetRadius * targetRadius / (finalAimTarget - pos).sqrMagnitude;
                        //     }
                        //     Debug.Log("DEBUG sqrMagnitude " + (Time.realtimeSinceStartup - now) / iters + "s/iter, out: " + test_out);
                        //     now = Time.realtimeSinceStartup;
                        //     for (int i = 0; i < iters; ++i)
                        //     {
                        //         var theta = FiringTolerance * targetRadius / (finalAimTarget - pos).magnitude + theta_const;
                        //         test_out = i > iters ? 1f : 1f - 0.5f * (theta * theta);
                        //     }
                        //     Debug.Log("DEBUG magnitude " + (Time.realtimeSinceStartup - now) / iters + "s/iter, out: " + test_out);
                        // }
                        if (GUI.Button(SLeftRect(++line), "Hash vs SubStr test"))
                        {
                            var armourParts = PartLoader.LoadedPartsList.Select(p => p.partPrefab.partInfo.name).Where(name => name.ToLower().Contains("armor")).ToHashSet();
                            Debug.Log($"DEBUG Armour parts in game: " + string.Join(", ", armourParts));
                            int N = 1 << 24;
                            var tic = Time.realtimeSinceStartup;
                            for (int i = 0; i < N; ++i)
                                armourParts.Contains("BD.PanelArmor");
                            var dt = Time.realtimeSinceStartup - tic;
                            Debug.Log($"DEBUG HashSet lookup took {dt / N:G3}s");
                            var armourPart = "BD.PanelArmor";
                            tic = Time.realtimeSinceStartup;
                            for (int i = 0; i < N; ++i)
                                armourPart.ToLower().Contains("armor");
                            dt = Time.realtimeSinceStartup - tic;
                            Debug.Log($"DEBUG SubStr lookup took {dt / N:G3}s");

                            // Using an actual part to include the part name access.
                            var testPart = PartLoader.LoadedPartsList.Select(p => p.partPrefab).First();
                            ProjectileUtils.IsArmorPart(testPart); // Bootstrap the HashSet
                            tic = Time.realtimeSinceStartup;
                            for (int i = 0; i < N; ++i)
                                ProjectileUtils.IsArmorPart(testPart);
                            dt = Time.realtimeSinceStartup - tic;
                            Debug.Log($"DEBUG Real part HashSet lookup first part took {dt / N:G3}s");
                            testPart = PartLoader.LoadedPartsList.Select(p => p.partPrefab).Last();
                            tic = Time.realtimeSinceStartup;
                            for (int i = 0; i < N; ++i)
                                ProjectileUtils.IsArmorPart(testPart);
                            dt = Time.realtimeSinceStartup - tic;
                            Debug.Log($"DEBUG Real part HashSet lookup last part took {dt / N:G3}s");
                            tic = Time.realtimeSinceStartup;
                            for (int i = 0; i < N; ++i)
                                testPart.partInfo.name.ToLower().Contains("armor");
                            dt = Time.realtimeSinceStartup - tic;
                            Debug.Log($"DEBUG Real part SubStr lookup took {dt / N:G3}s");

                        }
                        if (GUI.Button(SLeftRect(++line), "Layer test"))
                        {
                            for (int i = 0; i < 32; ++i)
                            {
                                // Vector3 mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
                                Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                                RaycastHit hit;

                                if (Physics.Raycast(ray, out hit, 1000f, (1 << i)))
                                {
                                    var hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                    var hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                    var hitBuilding = hit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>();
                                    if (hitEVA != null) hitPart = hitEVA.part;
                                    if (hitPart != null) Debug.Log($"DEBUG Bitmask at {i} hit {hitPart.name}.");
                                    else if (hitBuilding != null) Debug.Log($"DEBUG Bitmask at {i} hit {hitBuilding.name}");
                                    else Debug.Log($"DEBUG Bitmask at {i} hit {hit.collider.gameObject.name}");
                                }
                            }
                        }
                        if (GUI.Button(SLeftRect(++line), "Test vessel position timing."))
                        { StartCoroutine(TestVesselPositionTiming()); }
                        if (GUI.Button(SLeftRect(++line), "FS engine status"))
                        {
                            foreach (var vessel in FlightGlobals.VesselsLoaded)
                                FireSpitter.CheckStatus(vessel);
                        }
                        if (GUI.Button(SLeftRect(++line), "Quit KSP."))
                        {
                            TournamentAutoResume.AutoQuit(0);
                        }
                    }
#endif
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_GeneralSettingsToggle")))//Show/hide Gameplay settings.
            {
                BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE = !BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.GAMEPLAY_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING, Localizer.Format("#LOC_BDArmory_Settings_AutoEnableVesselSwitching"));
                { // Kerbal Safety
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_KerbalSafety")}:  ({(KerbalSafetyLevel)BDArmorySettings.KERBAL_SAFETY})", leftLabel); // Kerbal Safety
                    if (BDArmorySettings.KERBAL_SAFETY != (BDArmorySettings.KERBAL_SAFETY = BDArmorySettings.KERBAL_SAFETY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.KERBAL_SAFETY, (float)KerbalSafetyLevel.Off, (float)KerbalSafetyLevel.Full))))
                    {
                        if (BDArmorySettings.KERBAL_SAFETY != (int)KerbalSafetyLevel.Off)
                            KerbalSafetyManager.Instance.EnableKerbalSafety();
                        else
                            KerbalSafetyManager.Instance.DisableKerbalSafety();
                    }
                    if (BDArmorySettings.KERBAL_SAFETY != (int)KerbalSafetyLevel.Off)
                    {
                        string inventory;
                        switch (BDArmorySettings.KERBAL_SAFETY_INVENTORY)
                        {
                            case 1:
                                inventory = Localizer.Format("#LOC_BDArmory_Settings_KerbalSafetyInventory_ResetDefault");
                                break;
                            case 2:
                                inventory = Localizer.Format("#LOC_BDArmory_Settings_KerbalSafetyInventory_ChuteOnly");
                                break;
                            default:
                                inventory = Localizer.Format("#LOC_BDArmory_Settings_KerbalSafetyInventory_NoChange");
                                break;
                        }
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_KerbalSafetyInventory")}:  ({inventory})", leftLabel); // Kerbal Safety inventory
                        if (BDArmorySettings.KERBAL_SAFETY_INVENTORY != (BDArmorySettings.KERBAL_SAFETY_INVENTORY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.KERBAL_SAFETY_INVENTORY, 0f, 2f))))
                        { KerbalSafetyManager.Instance.ReconfigureInventories(); }
                    }
                }
                if (BDArmorySettings.HACK_INTAKES != (BDArmorySettings.HACK_INTAKES = GUI.Toggle(SLeftRect(++line), BDArmorySettings.HACK_INTAKES, Localizer.Format("#LOC_BDArmory_Settings_IntakeHack"))))// Hack Intakes
                {
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        SpawnUtils.HackIntakesOnNewVessels(BDArmorySettings.HACK_INTAKES);
                        if (BDArmorySettings.HACK_INTAKES) // Add the hack to all in-game intakes.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel == null || !vessel.loaded) continue;
                                SpawnUtils.HackIntakes(vessel, true);
                            }
                        }
                        else // Reset all the in-game intakes back to their part-defined settings.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (vessel == null || !vessel.loaded) continue;
                                SpawnUtils.HackIntakes(vessel, false);
                            }
                        }
                    }
                }

                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    BDArmorySettings.DEFAULT_FFA_TARGETING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.DEFAULT_FFA_TARGETING, Localizer.Format("#LOC_BDArmory_Settings_DefaultFFATargeting"));// Free-for-all combat style
                    BDArmorySettings.DISABLE_RAMMING = GUI.Toggle(SRightRect(line), BDArmorySettings.DISABLE_RAMMING, Localizer.Format("#LOC_BDArmory_Settings_DisableRamming"));// Disable Ramming
                    BDArmorySettings.AUTONOMOUS_COMBAT_SEATS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTONOMOUS_COMBAT_SEATS, Localizer.Format("#LOC_BDArmory_Settings_AutonomousCombatSeats"));
                    BDArmorySettings.DESTROY_UNCONTROLLED_WMS = GUI.Toggle(SRightRect(line), BDArmorySettings.DESTROY_UNCONTROLLED_WMS, Localizer.Format("#LOC_BDArmory_Settings_DestroyWMWhenNotControlled"));
                    BDArmorySettings.AIM_ASSIST = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AIM_ASSIST, Localizer.Format("#LOC_BDArmory_Settings_AimAssist"));//"Aim Assist"
                    BDArmorySettings.BOMB_CLEARANCE_CHECK = GUI.Toggle(SRightRect(line), BDArmorySettings.BOMB_CLEARANCE_CHECK, Localizer.Format("#LOC_BDArmory_Settings_ClearanceCheck"));//"Clearance Check"
                    BDArmorySettings.REMOTE_SHOOTING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.REMOTE_SHOOTING, Localizer.Format("#LOC_BDArmory_Settings_RemoteFiring"));//"Remote Firing"
                    BDArmorySettings.RESET_HP = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_HP, Localizer.Format("#LOC_BDArmory_Settings_ResetHP"));
                    BDArmorySettings.BULLET_WATER_DRAG = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BULLET_WATER_DRAG, Localizer.Format("#LOC_BDArmory_Settings_waterDrag"));// Underwater bullet drag
                    BDArmorySettings.RESET_ARMOUR = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_ARMOUR, Localizer.Format("#LOC_BDArmory_Settings_ResetArmor"));
                    BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_RELATIVE_BULLET_CHECKS, Localizer.Format("#LOC_BDArmory_Settings_VesselRelativeBulletChecks"));//"Vessel-Relative Bullet Checks"
                    BDArmorySettings.RESET_HULL = GUI.Toggle(SRightRect(line), BDArmorySettings.RESET_HULL, Localizer.Format("#LOC_BDArmory_Settings_ResetHull")); //Reset Hull
                    BDArmorySettings.AUTO_LOAD_TO_KSC = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_LOAD_TO_KSC, Localizer.Format("#LOC_BDArmory_Settings_AutoLoadToKSC")); // Auto-Load To KSC
                    BDArmorySettings.GENERATE_CLEAN_SAVE = GUI.Toggle(SRightRect(line), BDArmorySettings.GENERATE_CLEAN_SAVE, Localizer.Format("#LOC_BDArmory_Settings_GenerateCleanSave")); // Generate Clean Save
                    BDArmorySettings.AUTO_RESUME_TOURNAMENT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.AUTO_RESUME_TOURNAMENT, Localizer.Format("#LOC_BDArmory_Settings_AutoResumeTournaments")); // Auto-Resume Tournaments
                    if (BDArmorySettings.AUTO_RESUME_TOURNAMENT)
                    {
                        BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT = GUI.Toggle(SRightRect(line), BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT, Localizer.Format("#LOC_BDArmory_Settings_AutoQuitAtEndOfTournament")); // Auto Quit At End Of Tournament
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AutoQuitMemoryUsage")}:  ({(BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > SystemMaxMemory ? "Off" : $"{BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD}GB")})", leftLabel); // Auto-Quit Memory Threshold
                        BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD, 1f, SystemMaxMemory + 1));
                        if (BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD <= SystemMaxMemory)
                        {
                            GUI.Label(SLineRect(++line, 1), $"{Localizer.Format("#LOC_BDArmory_Settings_CurrentMemoryUsageEstimate")}: {TournamentAutoResume.memoryUsage:F1}GB / {SystemMaxMemory}GB", leftLabel);
                        }
                    }
                    if (BDArmorySettings.TIME_OVERRIDE != (BDArmorySettings.TIME_OVERRIDE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TIME_OVERRIDE, Localizer.Format("#LOC_BDArmory_Settings_TimeOverride")))) // Time override.
                    {
                        OtherUtils.SetTimeOverride(BDArmorySettings.TIME_OVERRIDE);
                    }
                    if (BDArmorySettings.TIME_OVERRIDE)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1), $"{Localizer.Format("#LOC_BDArmory_Settings_TimeScale")}; ({BDArmorySettings.TIME_SCALE:G2}x)", leftLabel);
                        if (BDArmorySettings.TIME_SCALE != (BDArmorySettings.TIME_SCALE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TIME_SCALE, 0f, BDArmorySettings.TIME_SCALE_MAX), BDArmorySettings.TIME_SCALE > 5f ? 1f : 0.1f)))
                        {
                            Time.timeScale = BDArmorySettings.TIME_SCALE;
                        }
                    }
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.SLIDER_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_SliderSettingsToggle")))//Show/hide General Slider settings.
            {
                BDArmorySettings.SLIDER_SETTINGS_TOGGLE = !BDArmorySettings.SLIDER_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.SLIDER_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                float dmgMultiplier = BDArmorySettings.DMG_MULTIPLIER <= 100f ? BDArmorySettings.DMG_MULTIPLIER / 10f : BDArmorySettings.DMG_MULTIPLIER / 50f + 8f;
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_DamageMultiplier")}:  ({BDArmorySettings.DMG_MULTIPLIER})", leftLabel); // Damage Multiplier
                dmgMultiplier = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), dmgMultiplier, 1f, 28f));
                BDArmorySettings.DMG_MULTIPLIER = dmgMultiplier < 11 ? (int)(dmgMultiplier * 10f) : (int)(50f * (dmgMultiplier - 8f));
                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    BDArmorySettings.EXTRA_DAMAGE_SLIDERS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.EXTRA_DAMAGE_SLIDERS, Localizer.Format("#LOC_BDArmory_Settings_ExtraDamageSliders"));

                    if (BDArmorySettings.EXTRA_DAMAGE_SLIDERS)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_BallisticDamageMultiplier")}:  ({BDArmorySettings.BALLISTIC_DMG_FACTOR})", leftLabel);
                        BDArmorySettings.BALLISTIC_DMG_FACTOR = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BALLISTIC_DMG_FACTOR * 20f, 0f, 60f))) / 20f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_ExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_BALLISTIC_NEW * 20f, 0f, 30f))) / 20f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_RocketExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_ROCKET})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_ROCKET = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_ROCKET * 20f, 0f, 40f))) / 20f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_MissileExplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_MISSILE})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_MISSILE = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_MISSILE * 4f, 0f, 40f))) / 4f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_ImplosiveDamageMultiplier")}:  ({BDArmorySettings.EXP_IMP_MOD})", leftLabel);
                        BDArmorySettings.EXP_IMP_MOD = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_IMP_MOD * 20, 0f, 20f))) / 20f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_ExplosiveBattleDamageMultiplier")}:  ({BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE})", leftLabel);
                        BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.EXP_DMG_MOD_BATTLE_DAMAGE * 10f, 0f, 20f))) / 10f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_SecondaryEffectDuration")}:  ({BDArmorySettings.WEAPON_FX_DURATION})", leftLabel);
                        BDArmorySettings.WEAPON_FX_DURATION = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WEAPON_FX_DURATION, 5f, 20f));

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_BallisticTrajectorSimulationMultiplier")}:  ({BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER})", leftLabel);
                        BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER, 1f, 256f));
                    }
                }

                // Kill categories
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_Scoring_HeadShot")}:  ({BDArmorySettings.SCORING_HEADSHOT}s)", leftLabel); // Scoring head-shot time limit
                BDArmorySettings.SCORING_HEADSHOT = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SCORING_HEADSHOT, 1f, 10f));
                BDArmorySettings.SCORING_KILLSTEAL = Mathf.Max(BDArmorySettings.SCORING_HEADSHOT, BDArmorySettings.SCORING_KILLSTEAL);
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_Scoring_KillSteal")}:  ({BDArmorySettings.SCORING_KILLSTEAL}s)", leftLabel); // Scoring kill-steal time limit
                BDArmorySettings.SCORING_KILLSTEAL = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SCORING_KILLSTEAL, BDArmorySettings.SCORING_HEADSHOT, 30f));

                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_TerrainAlertFrequency")}:  ({BDArmorySettings.TERRAIN_ALERT_FREQUENCY})", leftLabel); // Terrain alert frequency. Note: this is scaled by (int)(1+(radarAlt/500)^2) to avoid wasting too many cycles.
                    BDArmorySettings.TERRAIN_ALERT_FREQUENCY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TERRAIN_ALERT_FREQUENCY, 1f, 5f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CameraSwitchFrequency")}:  ({BDArmorySettings.CAMERA_SWITCH_FREQUENCY}s)", leftLabel); // Minimum camera switching frequency
                BDArmorySettings.CAMERA_SWITCH_FREQUENCY = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.CAMERA_SWITCH_FREQUENCY, 1f, 10f));

                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_DeathCameraInhibitPeriod")}:  ({(BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD == 0 ? BDArmorySettings.CAMERA_SWITCH_FREQUENCY / 2f : BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD)}s)", leftLabel); // Camera switch inhibit period after the active vessel dies.
                    BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD, 0f, 10f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_Max_PWing_HP")}:  {(BDArmorySettings.MAX_PWING_HP >= 100 ? (BDArmorySettings.MAX_PWING_HP.ToString()) : "Unclamped")}", leftLabel); // Max PWing HP
                BDArmorySettings.MAX_PWING_HP = GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MAX_PWING_HP, 0, 10000);
                BDArmorySettings.MAX_PWING_HP = Mathf.Round(BDArmorySettings.MAX_PWING_HP / 100) * 100;

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_GameModesSettingsToggle")))//Show/hide Game Modes settings.
            {
                BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE = !BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.GAME_MODES_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                BDArmorySettings.BATTLEDAMAGE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BATTLEDAMAGE, Localizer.Format("#LOC_BDArmory_Settings_BattleDamage"));
                BDArmorySettings.INFINITE_AMMO = GUI.Toggle(SRightRect(line), BDArmorySettings.INFINITE_AMMO, Localizer.Format("#LOC_BDArmory_Settings_InfiniteAmmo"));//"Infinite Ammo"
                BDArmorySettings.TAG_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TAG_MODE, Localizer.Format("#LOC_BDArmory_Settings_TagMode"));//"Tag Mode"
                if (BDArmorySettings.PAINTBALL_MODE != (BDArmorySettings.PAINTBALL_MODE = GUI.Toggle(SRightRect(line), BDArmorySettings.PAINTBALL_MODE, Localizer.Format("#LOC_BDArmory_Settings_PaintballMode"))))//"Paintball Mode"
                {
                    BulletHitFX.SetupShellPool();
                    BDArmorySettings.BATTLEDAMAGE = false;
                }
                if (BDArmorySettings.GRAVITY_HACKS != (BDArmorySettings.GRAVITY_HACKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.GRAVITY_HACKS, Localizer.Format("#LOC_BDArmory_Settings_GravityHacks"))))//"Gravity hacks"
                {
                    if (BDArmorySettings.GRAVITY_HACKS)
                    {
                        BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD = 10; // For gravity hacks, we need a shorter grace period.
                        BDArmorySettings.COMPETITION_KILL_TIMER = 1; // and a shorter kill timer.
                    }
                    else
                    {
                        BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD = 60; // Reset grace period back to default of 60s.
                        BDArmorySettings.COMPETITION_KILL_TIMER = 15; // Reset kill timer period back to default of 15s.
                        PhysicsGlobals.GraviticForceMultiplier = 1;
                        VehiclePhysics.Gravity.Refresh();
                    }
                }
                if (BDArmorySettings.PEACE_MODE != (BDArmorySettings.PEACE_MODE = GUI.Toggle(SRightRect(line), BDArmorySettings.PEACE_MODE, Localizer.Format("#LOC_BDArmory_Settings_PeaceMode"))))//"Peace Mode"
                {
                    BDATargetManager.ClearDatabase();
                    if (OnPeaceEnabled != null)
                    {
                        OnPeaceEnabled();
                    }
                }
                //Mutators
                var oldMutators = BDArmorySettings.MUTATOR_MODE;
                BDArmorySettings.MUTATOR_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.MUTATOR_MODE, Localizer.Format("#LOC_BDArmory_Settings_Mutators"));
                {
                    if (BDArmorySettings.MUTATOR_MODE)
                    {
                        if (!oldMutators)  // Add missing modules when Space Hacks is toggled.
                        {
                            foreach (var vessel in FlightGlobals.Vessels)
                            {
                                if (VesselModuleRegistry.GetMissileFire(vessel, true) != null && vessel.rootPart.FindModuleImplementing<BDAMutator>() == null)
                                {
                                    vessel.rootPart.AddModule("BDAMutator");
                                }
                            }
                        }
                        selectMutators = GUI.Toggle(SLeftRect(++line, 1f), selectMutators, Localizer.Format("#LOC_BDArmory_MutatorSelect"));
                        if (selectMutators)
                        {
                            ++line;
                            scrollViewVector = GUI.BeginScrollView(new Rect(settingsMargin + 1 * settingsMargin, line * settingsLineHeight, settingsWidth - 2 * settingsMargin - 1 * settingsMargin, settingsLineHeight * 6f), scrollViewVector,
                                               new Rect(0, 0, settingsWidth - 2 * settingsMargin - 2 * settingsMargin, mutatorHeight));
                            GUI.BeginGroup(new Rect(0, 0, settingsWidth - 2 * settingsMargin - 2 * settingsMargin, mutatorHeight), GUIContent.none);
                            int mutatorLine = 0;
                            for (int i = 0; i < mutators.Count; i++)
                            {
                                Rect buttonRect = new Rect(0, (i * 25), (settingsWidth - 4 * settingsMargin) / 2, 20);
                                if (mutators_selected[i] != (mutators_selected[i] = GUI.Toggle(buttonRect, mutators_selected[i], mutators[i])))
                                {
                                    if (mutators_selected[i])
                                    {
                                        BDArmorySettings.MUTATOR_LIST.Add(mutators[i]);
                                    }
                                    else
                                    {
                                        BDArmorySettings.MUTATOR_LIST.Remove(mutators[i]);
                                    }
                                }
                                mutatorLine++;
                            }

                            mutatorHeight = Mathf.Lerp(mutatorHeight, (mutatorLine * 25), 1);
                            GUI.EndGroup();
                            GUI.EndScrollView();
                            line += 6.5f;

                            if (GUI.Button(SRightRect(line), Localizer.Format("#LOC_BDArmory_reset")))
                            {
                                switch (Event.current.button)
                                {
                                    case 1: // right click
                                        Debug.Log("[BDArmory.BDArmorySetup]: MutatorList: " + string.Join("; ", BDArmorySettings.MUTATOR_LIST));
                                        break;
                                    default:
                                        BDArmorySettings.MUTATOR_LIST.Clear();
                                        for (int i = 0; i < mutators_selected.Length; ++i) mutators_selected[i] = false;
                                        Debug.Log("[BDArmory.BDArmorySetup]: Resetting Mutator list");
                                        break;
                                }
                            }
                            line += .2f;
                        }
                        BDArmorySettings.MUTATOR_APPLY_GLOBAL = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_APPLY_GLOBAL, Localizer.Format("#LOC_BDArmory_Settings_MutatorGlobal"));
                        if (BDArmorySettings.MUTATOR_APPLY_GLOBAL) //if more than 1 mutator selected, will shuffle each round
                        {
                            BDArmorySettings.MUTATOR_APPLY_KILL = false;
                        }
                        BDArmorySettings.MUTATOR_APPLY_KILL = GUI.Toggle(SRightRect(line, 1f), BDArmorySettings.MUTATOR_APPLY_KILL, Localizer.Format("#LOC_BDArmory_Settings_MutatorKill"));
                        if (BDArmorySettings.MUTATOR_APPLY_KILL) // if more than 1 mutator selected, will randomly assign mutator on kill
                        {
                            BDArmorySettings.MUTATOR_APPLY_GLOBAL = false;
                            BDArmorySettings.MUTATOR_APPLY_TIMER = false;
                        }

                        if (BDArmorySettings.MUTATOR_LIST.Count > 1)

                        {
                            BDArmorySettings.MUTATOR_APPLY_TIMER = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_APPLY_TIMER, Localizer.Format("#LOC_BDArmory_Settings_MutatorTimed"));
                            if (BDArmorySettings.MUTATOR_APPLY_TIMER) //only an option if more than one mutator selected
                            {
                                BDArmorySettings.MUTATOR_APPLY_KILL = false;
                                //BDArmorySettings.MUTATOR_APPLY_GLOBAL = false; //global + timer causes a single globally appled mutator that shuffles, instead of chaos mode
                            }
                        }
                        else
                        {
                            BDArmorySettings.MUTATOR_APPLY_TIMER = false;
                        }
                        if (!BDArmorySettings.MUTATOR_APPLY_TIMER && !BDArmorySettings.MUTATOR_APPLY_KILL)
                        {
                            BDArmorySettings.MUTATOR_APPLY_GLOBAL = true;
                        }

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_MutatorDuration")}: ({(BDArmorySettings.MUTATOR_DURATION > 0 ? BDArmorySettings.MUTATOR_DURATION + (BDArmorySettings.MUTATOR_DURATION > 1 ? " mins" : " min") : "Unlimited")})", leftLabel);
                        BDArmorySettings.MUTATOR_DURATION = (float)Math.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MUTATOR_DURATION, 0f, BDArmorySettings.COMPETITION_DURATION), 1);

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_MutatorNum")}:  ({BDArmorySettings.MUTATOR_APPLY_NUM})", leftLabel);//Number of active mutators
                        BDArmorySettings.MUTATOR_APPLY_NUM = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.MUTATOR_APPLY_NUM, 1f, BDArmorySettings.MUTATOR_LIST.Count));
                        if (BDArmorySettings.MUTATOR_LIST.Count < BDArmorySettings.MUTATOR_APPLY_NUM)
                        {
                            BDArmorySettings.MUTATOR_APPLY_NUM = BDArmorySettings.MUTATOR_LIST.Count;
                        }
                        if (BDArmorySettings.MUTATOR_LIST.Count > 0 && BDArmorySettings.MUTATOR_APPLY_NUM < 1)
                        {
                            BDArmorySettings.MUTATOR_APPLY_NUM = 1;
                        }
                        BDArmorySettings.MUTATOR_ICONS = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.MUTATOR_ICONS, Localizer.Format("#LOC_BDArmory_Settings_MutatorIcons"));
                    }
                }
                // Heartbleed
                BDArmorySettings.HEART_BLEED_ENABLED = GUI.Toggle(SLeftRect(++line), BDArmorySettings.HEART_BLEED_ENABLED, Localizer.Format("#LOC_BDArmory_Settings_HeartBleed"));//"Heart Bleed"
                if (BDArmorySettings.HEART_BLEED_ENABLED)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_HeartBleedRate")}:  ({BDArmorySettings.HEART_BLEED_RATE})", leftLabel);//Heart Bleed Rate
                    BDArmorySettings.HEART_BLEED_RATE = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_RATE, 0f, 0.1f) * 1000f) / 1000f;
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_HeartBleedInterval")}:  ({BDArmorySettings.HEART_BLEED_INTERVAL})", leftLabel);//Heart Bleed Interval
                    BDArmorySettings.HEART_BLEED_INTERVAL = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_INTERVAL, 1f, 60f));
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_HeartBleedThreshold")}:  ({BDArmorySettings.HEART_BLEED_THRESHOLD})", leftLabel);//Heart Bleed Threshold
                    BDArmorySettings.HEART_BLEED_THRESHOLD = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.HEART_BLEED_THRESHOLD, 1f, 100f));
                }
                // Resource steal
                BDArmorySettings.RESOURCE_STEAL_ENABLED = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RESOURCE_STEAL_ENABLED, Localizer.Format("#LOC_BDArmory_Settings_ResourceSteal"));//"Resource Steal"
                if (BDArmorySettings.RESOURCE_STEAL_ENABLED)
                {
                    BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_IN = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_IN, Localizer.Format("#LOC_BDArmory_Settings_ResourceSteal_RespectFlowStateIn"));//Respect Flow State In
                    BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_OUT = GUI.Toggle(SRightRect(line, 1), BDArmorySettings.RESOURCE_STEAL_RESPECT_FLOWSTATE_OUT, Localizer.Format("#LOC_BDArmory_Settings_ResourceSteal_RespectFlowStateOut"));//Respect Flow State Out
                    GUI.Label(SLeftSliderRect(++line, 1), $"{Localizer.Format("#LOC_BDArmory_Settings_FuelStealRation")}:  ({BDArmorySettings.RESOURCE_STEAL_FUEL_RATION})", leftLabel);//Fuel Steal Ration
                    BDArmorySettings.RESOURCE_STEAL_FUEL_RATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RESOURCE_STEAL_FUEL_RATION, 0f, 1f) * 100f) / 100f;
                    GUI.Label(SLeftSliderRect(++line, 1), $"{Localizer.Format("#LOC_BDArmory_Settings_AmmoStealRation")}:  ({BDArmorySettings.RESOURCE_STEAL_AMMO_RATION})", leftLabel);//Ammo Steal Ration
                    BDArmorySettings.RESOURCE_STEAL_AMMO_RATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RESOURCE_STEAL_AMMO_RATION, 0f, 1f) * 100f) / 100f;
                    GUI.Label(SLeftSliderRect(++line, 1), $"{Localizer.Format("#LOC_BDArmory_Settings_CMStealRation")}:  ({BDArmorySettings.RESOURCE_STEAL_CM_RATION})", leftLabel);//CM Steal Ration
                    BDArmorySettings.RESOURCE_STEAL_CM_RATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RESOURCE_STEAL_CM_RATION, 0f, 1f) * 100f) / 100f;
                }
                var oldSpaceHacks = BDArmorySettings.SPACE_HACKS;
                BDArmorySettings.SPACE_HACKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.SPACE_HACKS, Localizer.Format("#LOC_BDArmory_Settings_SpaceHacks"));
                {
                    if (BDArmorySettings.SPACE_HACKS)
                    {
                        if (!oldSpaceHacks) ModuleSpaceFriction.AddSpaceFrictionToAllValidVessels(); // Add missing modules when Space Hacks is toggled.
                        BDArmorySettings.SF_FRICTION = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.SF_FRICTION, Localizer.Format("#LOC_BDArmory_Settings_SpaceFriction"));
                        BDArmorySettings.SF_GRAVITY = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.SF_GRAVITY, Localizer.Format("#LOC_BDArmory_Settings_IgnoreGravity"));
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_SpaceFrictionMult")}:  ({BDArmorySettings.SF_DRAGMULT})", leftLabel);//Space Friction Mult
                        BDArmorySettings.SF_DRAGMULT = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.SF_DRAGMULT, 1f, 10));
                        BDArmorySettings.SF_REPULSOR = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.SF_REPULSOR, Localizer.Format("#LOC_BDArmory_Settings_Repulsor"));
                    }
                    else
                    {
                        BDArmorySettings.SF_FRICTION = false;
                        BDArmorySettings.SF_GRAVITY = false;
                        BDArmorySettings.SF_REPULSOR = false;
                    }
                }
                // Asteroids
                if (BDArmorySettings.ASTEROID_FIELD != (BDArmorySettings.ASTEROID_FIELD = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_FIELD, Localizer.Format("#LOC_BDArmory_Settings_AsteroidField")))) // Asteroid Field
                {
                    if (!BDArmorySettings.ASTEROID_FIELD) AsteroidField.Instance.Reset(true);
                }
                if (BDArmorySettings.ASTEROID_FIELD)
                {
                    if (GUI.Button(SRightButtonRect(line), "Spawn Field Now"))//"Spawn Field Now"))
                    {
                        if (Event.current.button == 1)
                            AsteroidField.Instance.Reset();
                        else if (Event.current.button == 2) // Middle click
                                                            // AsteroidUtils.CheckOrbit();
                            AsteroidField.Instance.CheckPooledAsteroids();
                        else
                            AsteroidField.Instance.SpawnField(BDArmorySettings.ASTEROID_FIELD_NUMBER, BDArmorySettings.ASTEROID_FIELD_ALTITUDE, BDArmorySettings.ASTEROID_FIELD_RADIUS, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS);
                    }
                    line += 0.25f;
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidFieldNumber")}:  ({BDArmorySettings.ASTEROID_FIELD_NUMBER})", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_NUMBER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), Mathf.Round(BDArmorySettings.ASTEROID_FIELD_NUMBER / 10f), 1f, 200f) * 10f); // Asteroid Field Number
                    var altitudeString = BDArmorySettings.ASTEROID_FIELD_ALTITUDE < 10f ? $"{BDArmorySettings.ASTEROID_FIELD_ALTITUDE * 100f:F0}m" : $"{BDArmorySettings.ASTEROID_FIELD_ALTITUDE / 10f:F1}km";
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidFieldAltitude")}:  ({altitudeString})", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_ALTITUDE = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_ALTITUDE, 1f, 200f)); // Asteroid Field Altitude
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidFieldRadius")}:  ({BDArmorySettings.ASTEROID_FIELD_RADIUS}km)", leftLabel);
                    BDArmorySettings.ASTEROID_FIELD_RADIUS = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_RADIUS, 1f, 10f)); // Asteroid Field Radius
                    line -= 0.25f;
                    if (BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION != (BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION, BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION ? $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidFieldAnomalousAttraction")}:  ({BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH:G2})" : Localizer.Format("#LOC_BDArmory_Settings_AsteroidFieldAnomalousAttraction")))) // Anomalous Attraction
                    {
                        if (!BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION && AsteroidField.Instance != null)
                        { AsteroidField.Instance.anomalousAttraction = Vector3d.zero; }
                    }
                    if (BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION)
                    {
                        BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_FIELD_ANOMALOUS_ATTRACTION_STRENGTH * 20f, 1f, 20f)) / 20f; // Asteroid Field Anomalous Attraction Strength
                    }
                }
                if (BDArmorySettings.ASTEROID_RAIN != (BDArmorySettings.ASTEROID_RAIN = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_RAIN, Localizer.Format("#LOC_BDArmory_Settings_AsteroidRain")))) // Asteroid Rain
                {
                    if (!BDArmorySettings.ASTEROID_RAIN) AsteroidRain.Instance.Reset();
                }
                if (BDArmorySettings.ASTEROID_RAIN)
                {
                    if (GUI.Button(SRightButtonRect(line), "Spawn Rain Now"))
                    {
                        if (Event.current.button == 1)
                            AsteroidRain.Instance.Reset();
                        else if (Event.current.button == 2)
                            AsteroidRain.Instance.CheckPooledAsteroids();
                        else
                            AsteroidRain.Instance.SpawnRain(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS);
                    }
                    BDArmorySettings.ASTEROID_RAIN_FOLLOWS_CENTROID = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ASTEROID_RAIN_FOLLOWS_CENTROID, Localizer.Format("#LOC_BDArmory_Settings_AsteroidRainFollowsCentroid")); // Follows Vessels' Location.
                    if (BDArmorySettings.ASTEROID_RAIN_FOLLOWS_CENTROID)
                    {
                        BDArmorySettings.ASTEROID_RAIN_FOLLOWS_SPREAD = GUI.Toggle(SRightRect(line), BDArmorySettings.ASTEROID_RAIN_FOLLOWS_SPREAD, Localizer.Format("#LOC_BDArmory_Settings_AsteroidRainFollowsSpread")); // Follows Vessels' Spread.
                    }
                    line += 0.25f;
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidRainNumber")}:  ({BDArmorySettings.ASTEROID_RAIN_NUMBER})", leftLabel);
                    if (BDArmorySettings.ASTEROID_RAIN_NUMBER != (BDArmorySettings.ASTEROID_RAIN_NUMBER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), Mathf.Round(BDArmorySettings.ASTEROID_RAIN_NUMBER / 10f), 1f, 200f) * 10f))) // Asteroid Rain Number
                    { if (HighLogic.LoadedSceneIsFlight) AsteroidRain.Instance.UpdateSettings(); }
                    var altitudeString = BDArmorySettings.ASTEROID_RAIN_ALTITUDE < 10f ? $"{BDArmorySettings.ASTEROID_RAIN_ALTITUDE * 100f:F0}m" : $"{BDArmorySettings.ASTEROID_RAIN_ALTITUDE / 10f:F1}km";
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidRainAltitude")}:  ({altitudeString})", leftLabel);
                    if (BDArmorySettings.ASTEROID_RAIN_ALTITUDE != (BDArmorySettings.ASTEROID_RAIN_ALTITUDE = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_RAIN_ALTITUDE, 1f, 100f)))) // Asteroid Rain Altitude
                    { if (HighLogic.LoadedSceneIsFlight) AsteroidRain.Instance.UpdateSettings(); }
                    if (!BDArmorySettings.ASTEROID_RAIN_FOLLOWS_SPREAD)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_AsteroidRainRadius")}:  ({BDArmorySettings.ASTEROID_RAIN_RADIUS}km)", leftLabel);
                        if (BDArmorySettings.ASTEROID_RAIN_RADIUS != (BDArmorySettings.ASTEROID_RAIN_RADIUS = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ASTEROID_RAIN_RADIUS, 1f, 10f)))) // Asteroid Rain Radius
                        { if (HighLogic.LoadedSceneIsFlight) AsteroidRain.Instance.UpdateSettings(); }
                    }
                    line -= 0.25f;
                }
                BDArmorySettings.WAYPOINTS_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.WAYPOINTS_MODE, Localizer.Format("#LOC_BDArmory_Settings_WaypointsMode"));
                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    BDArmorySettings.RUNWAY_PROJECT = GUI.Toggle(SLeftRect(++line), BDArmorySettings.RUNWAY_PROJECT, Localizer.Format("#LOC_BDArmory_Settings_RunwayProject"));//Runway Project

                    if (BDArmorySettings.RUNWAY_PROJECT)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_RunwayProjectRound")}: ({(BDArmorySettings.RUNWAY_PROJECT_ROUND > 10 ? $"S{(BDArmorySettings.RUNWAY_PROJECT_ROUND - 1) / 10}R{(BDArmorySettings.RUNWAY_PROJECT_ROUND - 1) % 10 + 1}" : "—")})", leftLabel); // RWP round
                        BDArmorySettings.RUNWAY_PROJECT_ROUND = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.RUNWAY_PROJECT_ROUND, 10f, 60f));

                        if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_settings_FireRateCenter")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER})", leftLabel);//Fire Rate Override Center
                            BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER, 10f, 300f) / 5f) * 5f;
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_settings_FireRateSpread")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD})", leftLabel);//Fire Rate Override Spread
                            BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_SPREAD, 0f, 50f));
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_settings_FireRateBias")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS * BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS:G2})", leftLabel);//Fire Rate Override Bias
                            BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_BIAS, 0f, 1f) * 50f) / 50f;
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_settings_FireRateHitMultiplier")}:  ({BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER})", leftLabel);//Fire Rate Hit Multiplier
                            BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.FIRE_RATE_OVERRIDE_HIT_MULTIPLIER, 1f, 4f) * 10f) / 10f;
                        }
                        // if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 46) BDArmorySettings.NO_ENGINES = true;
                        if (CheatCodeGUI != (CheatCodeGUI = GUI.TextField(SLeftRect(++line, 1, true), CheatCodeGUI))) //if we need super-secret stuff
                        {
                            if (CheatCodeGUI == "ZombieMode")
                            {
                                BDArmorySettings.ZOMBIE_MODE = !BDArmorySettings.ZOMBIE_MODE; //sticking this here until we figure out a better home for it
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI == "DiscoInferno")
                            {
                                BDArmorySettings.DISCO_MODE = !BDArmorySettings.DISCO_MODE;
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI == "NoEngines")
                            {
                                BDArmorySettings.NO_ENGINES = !BDArmorySettings.NO_ENGINES;
                                CheatCodeGUI = "";
                            }
                            else if (CheatCodeGUI == "HallOfShame")
                            {
                                BDArmorySettings.ENABLE_HOS = !BDArmorySettings.ENABLE_HOS;
                                CheatCodeGUI = "";
                            }
                        }
                        //BDArmorySettings.ZOMBIE_MODE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.ZOMBIE_MODE, Localizer.Format("#LOC_BDArmory_settings_ZombieMode"));
                        if (BDArmorySettings.ZOMBIE_MODE)
                        {
                            GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_settings_zombieDmgMod")}:  ({BDArmorySettings.ZOMBIE_DMG_MULT})", leftLabel);//"S4R2 Non-headshot Dmg Mult"

                            //if (BDArmorySettings.RUNWAY_PROJECT_ROUND == -1) // FIXME Set when the round is actually run! Also check for other "RUNWAY_PROJECT_ROUND == -1" checks.
                            //{
                            //    GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_settings_zombieDmgMod")}:  ({BDArmorySettings.ZOMBIE_DMG_MULT})", leftLabel);//"Zombie Non-headshot Dmg Mult"

                            BDArmorySettings.ZOMBIE_DMG_MULT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.ZOMBIE_DMG_MULT, 0.05f, 0.95f) * 100f) / 100f;
                            if (BDArmorySettings.BATTLEDAMAGE)
                            {
                                BDArmorySettings.ALLOW_ZOMBIE_BD = GUI.Toggle(SLeftRect(++line, 1), BDArmorySettings.ALLOW_ZOMBIE_BD, Localizer.Format("#LOC_BDArmory_Settings_BD_ZombieMode"));//"Allow battle Damage"
                            }
                        }
                        if (BDArmorySettings.ENABLE_HOS)
                        {
                            GUI.Label(SLeftRect(++line), Localizer.Format("--Hall Of Shame Enabled--"));//"Competition Distance"
                            HoSString = GUI.TextField(SLeftRect(++line, 1, true), HoSString);
                            if (!string.IsNullOrEmpty(HoSString))
                            {
                                enteredHoS = GUI.Toggle(SRightRect(line), enteredHoS, Localizer.Format("Enter to Hall of Shame"));
                                {
                                    if (enteredHoS)
                                    {
                                        if (HoSString == "Clear()")
                                        {
                                            BDArmorySettings.HALL_OF_SHAME_LIST.Clear();
                                        }
                                        else
                                        {
                                            if (!BDArmorySettings.HALL_OF_SHAME_LIST.Contains(HoSString))
                                            {
                                                BDArmorySettings.HALL_OF_SHAME_LIST.Add(HoSString);
                                            }
                                            else
                                            {
                                                BDArmorySettings.HALL_OF_SHAME_LIST.Remove(HoSString);
                                            }
                                        }
                                        HoSString = "";
                                        enteredHoS = false;
                                    }
                                }
                            }
                            GUI.Label(SLeftRect(++line), Localizer.Format("--Select Punishment--"));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{Localizer.Format("Fire")}:  ({(float)Math.Round(BDArmorySettings.HOS_FIRE, 1)} Burn Rate)", leftLabel);
                            BDArmorySettings.HOS_FIRE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_FIRE, 1), 0, 10));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{Localizer.Format("Mass")}:  ({(float)Math.Round(BDArmorySettings.HOS_MASS, 1)} ton deadweight)", leftLabel);
                            BDArmorySettings.HOS_MASS = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_MASS, 1), -10, 10));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{Localizer.Format("Frailty")}:  ({(float)Math.Round(BDArmorySettings.HOS_DMG, 2) * 100}%) Dmg taken", leftLabel);
                            BDArmorySettings.HOS_DMG = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_DMG, 2), 0.1f, 10));
                            GUI.Label(SLeftSliderRect(++line, 2f), $"{Localizer.Format("Thrust")}:  ({(float)Math.Round(BDArmorySettings.HOS_THRUST, 1)}%) Engine Thrust", leftLabel);
                            BDArmorySettings.HOS_THRUST = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.HOS_THRUST, 1), 0, 200));
                            GUI.Label(SLeftRect(++line), Localizer.Format("--Shame badge--"));
                            HoSTag = GUI.TextField(SLeftRect(++line, 1, true), HoSTag);
                            BDArmorySettings.HOS_BADGE = HoSTag;
                        }
                        else
                        {
                            BDArmorySettings.HOS_FIRE = 0;
                            BDArmorySettings.HOS_MASS = 0;
                            BDArmorySettings.HOS_DMG = 100;
                            BDArmorySettings.HOS_THRUST = 100;
                            //partloss = false; //- would need special module, but could also be a mutator mode
                            //timebomb = false //same
                            //might be more elegant to simply have this use Mutator framework and load the HoS craft with a select mutator(s) instead... Something to look into later, maybe, but ideally this shouldn't need to be used in the first place.
                        }
                    }
                }

                line += 0.5f;
            }

            if (BDArmorySettings.BATTLEDAMAGE)
            {
                if (GUI.Button(SLineRect(++line), (BDArmorySettings.BATTLEDAMAGE_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_BDSettingsToggle")))//Show/hide Battle Damage settings.
                {
                    BDArmorySettings.BATTLEDAMAGE_TOGGLE = !BDArmorySettings.BATTLEDAMAGE_TOGGLE;
                }
                if (BDArmorySettings.BATTLEDAMAGE_TOGGLE)
                {
                    line += 0.2f;

                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Proc")}: ({BDArmorySettings.BD_DAMAGE_CHANCE}%)", leftLabel); //Proc Chance Frequency
                    BDArmorySettings.BD_DAMAGE_CHANCE = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_DAMAGE_CHANCE, 0f, 100));

                    BDArmorySettings.BD_PROPULSION = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_PROPULSION, Localizer.Format("#LOC_BDArmory_Settings_BD_Engines"));//"Propulsion Systems Damage"
                    if (BDArmorySettings.BD_PROPULSION && BDArmorySettings.ADVANDED_USER_SETTINGS)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Prop_Dmg_Mult")}:  ({BDArmorySettings.BD_PROP_DAM_RATE}x)", leftLabel); //Propulsion Damage Multiplier
                        BDArmorySettings.BD_PROP_DAM_RATE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_PROP_DAM_RATE, 1), 0, 2));
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Prop_floor")}:  ({BDArmorySettings.BD_PROP_FLOOR}%)", leftLabel); //Min Engine Thrust
                        BDArmorySettings.BD_PROP_FLOOR = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_PROP_FLOOR, 1), 0, 100));

                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Prop_flameout")}:  ({BDArmorySettings.BD_PROP_FLAMEOUT}% HP)", leftLabel); //Engine Flameout
                        BDArmorySettings.BD_PROP_FLAMEOUT = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_PROP_FLAMEOUT, 0), 0, 95));
                        BDArmorySettings.BD_INTAKES = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_INTAKES, Localizer.Format("#LOC_BDArmory_Settings_BD_Intakes"));//"Intake Damage"
                        BDArmorySettings.BD_GIMBALS = GUI.Toggle(SRightRect(line, 1f), BDArmorySettings.BD_GIMBALS, Localizer.Format("#LOC_BDArmory_Settings_BD_Gimbals"));//"Gimbal Damage"
                    }

                    BDArmorySettings.BD_AEROPARTS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_AEROPARTS, Localizer.Format("#LOC_BDArmory_Settings_BD_Aero"));//"Flight Systems Damage"
                    if (BDArmorySettings.BD_AEROPARTS && BDArmorySettings.ADVANDED_USER_SETTINGS)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Aero_Dmg_Mult")}:  ({BDArmorySettings.BD_LIFT_LOSS_RATE}x)", leftLabel); //Wing Damage Magnitude
                        BDArmorySettings.BD_LIFT_LOSS_RATE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_LIFT_LOSS_RATE, 1), 0, 5));
                        BDArmorySettings.BD_CTRL_SRF = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_CTRL_SRF, Localizer.Format("#LOC_BDArmory_Settings_BD_CtrlSrf"));//"Ctrl Surface Damage"
                    }

                    BDArmorySettings.BD_COCKPITS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_COCKPITS, Localizer.Format("#LOC_BDArmory_Settings_BD_Command"));//"Command & Control Damage"
                    if (BDArmorySettings.BD_COCKPITS && BDArmorySettings.ADVANDED_USER_SETTINGS)
                    {
                        BDArmorySettings.BD_PILOT_KILLS = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_PILOT_KILLS, Localizer.Format("#LOC_BDArmory_Settings_BD_PilotKill"));//"Crew Fatalities"
                    }

                    BDArmorySettings.BD_TANKS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_TANKS, Localizer.Format("#LOC_BDArmory_Settings_BD_Tanks"));//"FuelTank Damage"
                    if (BDArmorySettings.BD_TANKS && BDArmorySettings.ADVANDED_USER_SETTINGS)
                    {
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Leak_Time")}:  ({BDArmorySettings.BD_TANK_LEAK_TIME}s)", leftLabel); // Leak Duration
                        BDArmorySettings.BD_TANK_LEAK_TIME = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_TANK_LEAK_TIME, 0, 100)));
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Leak_Rate")}:  ({BDArmorySettings.BD_TANK_LEAK_RATE}x)", leftLabel); //Leak magnitude
                        BDArmorySettings.BD_TANK_LEAK_RATE = (GUI.HorizontalSlider(SRightSliderRect(line), (float)Math.Round(BDArmorySettings.BD_TANK_LEAK_RATE, 1), 0, 5));
                    }
                    BDArmorySettings.BD_SUBSYSTEMS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_SUBSYSTEMS, Localizer.Format("#LOC_BDArmory_Settings_BD_SubSystems"));//"Subsystem Damage"
                    BDArmorySettings.BD_AMMOBINS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_AMMOBINS, Localizer.Format("#LOC_BDArmory_Settings_BD_Ammo"));//"Ammo Explosions"
                    if (BDArmorySettings.BD_AMMOBINS && BDArmorySettings.ADVANDED_USER_SETTINGS)
                    {
                        BDArmorySettings.BD_VOLATILE_AMMO = GUI.Toggle(SLineRect(++line, 1f), BDArmorySettings.BD_VOLATILE_AMMO, Localizer.Format("#LOC_BDArmory_Settings_BD_Volatile_Ammo"));//"Ammo Bins Explode When Destroyed"
                    }

                    BDArmorySettings.BD_FIRES_ENABLED = GUI.Toggle(SLeftRect(++line), BDArmorySettings.BD_FIRES_ENABLED, Localizer.Format("#LOC_BDArmory_Settings_BD_Fires"));//"Fires"
                    if (BDArmorySettings.BD_FIRES_ENABLED && BDArmorySettings.ADVANDED_USER_SETTINGS)
                    {
                        BDArmorySettings.BD_FIRE_DOT = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_FIRE_DOT, Localizer.Format("#LOC_BDArmory_Settings_BD_DoT"));//"Fire Damage"
                        GUI.Label(SLeftSliderRect(++line, 1f), $"{Localizer.Format("#LOC_BDArmory_Settings_BD_Fire_Dmg")}:  ({BDArmorySettings.BD_FIRE_DAMAGE}/s)", leftLabel); // "Fire Damage magnitude"
                        BDArmorySettings.BD_FIRE_DAMAGE = Mathf.Round((GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.BD_FIRE_DAMAGE, 0f, 20)));
                        BDArmorySettings.BD_FIRE_FUELEX = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_FIRE_FUELEX, Localizer.Format("#LOC_BDArmory_Settings_BD_FuelFireEX"));//"Fueltank Explosions
                        BDArmorySettings.BD_FIRE_HEATDMG = GUI.Toggle(SLeftRect(++line, 1f), BDArmorySettings.BD_FIRE_HEATDMG, Localizer.Format("#LOC_BDArmory_Settings_BD_FireHeat"));//"Fires add Heat
                    }

                    line += 0.5f;
                }
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.RADAR_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_RadarSettingsToggle"))) // Show/hide Radar settings.
            {
                BDArmorySettings.RADAR_SETTINGS_TOGGLE = !BDArmorySettings.RADAR_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.RADAR_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_RWRWindowScale") + ": " + (BDArmorySettings.RWR_WINDOW_SCALE * 100).ToString("0") + "%", leftLabel); // RWR Window Scale
                float rwrScale = BDArmorySettings.RWR_WINDOW_SCALE;
                rwrScale = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), rwrScale, BDArmorySettings.RWR_WINDOW_SCALE_MIN, BDArmorySettings.RWR_WINDOW_SCALE_MAX) * 100.0f) * 0.01f;
                if (rwrScale.ToString(CultureInfo.InvariantCulture) != BDArmorySettings.RWR_WINDOW_SCALE.ToString(CultureInfo.InvariantCulture))
                {
                    ResizeRwrWindow(rwrScale);
                }

                GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_RadarWindowScale") + ": " + (BDArmorySettings.RADAR_WINDOW_SCALE * 100).ToString("0") + "%", leftLabel); // Radar Window Scale
                float radarScale = BDArmorySettings.RADAR_WINDOW_SCALE;
                radarScale = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), radarScale, BDArmorySettings.RADAR_WINDOW_SCALE_MIN, BDArmorySettings.RADAR_WINDOW_SCALE_MAX) * 100.0f) * 0.01f;
                if (radarScale.ToString(CultureInfo.InvariantCulture) != BDArmorySettings.RADAR_WINDOW_SCALE.ToString(CultureInfo.InvariantCulture))
                {
                    ResizeRadarWindow(radarScale);
                }

                GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_TargetWindowScale") + ": " + (BDArmorySettings.TARGET_WINDOW_SCALE * 100).ToString("0") + "%", leftLabel); // Target Window Scale
                float targetScale = BDArmorySettings.TARGET_WINDOW_SCALE;
                targetScale = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), targetScale, BDArmorySettings.TARGET_WINDOW_SCALE_MIN, BDArmorySettings.TARGET_WINDOW_SCALE_MAX) * 100.0f) * 0.01f;
                if (targetScale.ToString(CultureInfo.InvariantCulture) != BDArmorySettings.TARGET_WINDOW_SCALE.ToString(CultureInfo.InvariantCulture))
                {
                    ResizeTargetWindow(targetScale);
                }

                GUI.Label(SLeftRect(++line), Localizer.Format("#LOC_BDArmory_Settings_TargetWindowInvertMouse"), leftLabel);
                BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_X = GUI.Toggle(SEighthRect(line, 5), BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_X, "X");
                BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_Y = GUI.Toggle(SEighthRect(line, 6), BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_Y, "Y");
                BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY = GUI.Toggle(SLeftRect(++line), BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY, Localizer.Format("#LOC_BDArmory_Settings_LogarithmicRWRDisplay")); //"Logarithmic RWR Display"

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.OTHER_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_OtherSettingsToggle"))) // Show/hide Other settings.
            {
                BDArmorySettings.OTHER_SETTINGS_TOGGLE = !BDArmorySettings.OTHER_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.OTHER_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_TriggerHold") + ": " + BDArmorySettings.TRIGGER_HOLD_TIME.ToString("0.00") + "s", leftLabel);//Trigger Hold
                BDArmorySettings.TRIGGER_HOLD_TIME = GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TRIGGER_HOLD_TIME, 0.02f, 1f);

                GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_UIVolume") + ": " + (BDArmorySettings.BDARMORY_UI_VOLUME * 100).ToString("0"), leftLabel);//UI Volume
                float uiVol = BDArmorySettings.BDARMORY_UI_VOLUME;
                uiVol = GUI.HorizontalSlider(SRightSliderRect(line), uiVol, 0f, 1f);
                if (uiVol != BDArmorySettings.BDARMORY_UI_VOLUME && OnVolumeChange != null)
                {
                    OnVolumeChange();
                }
                BDArmorySettings.BDARMORY_UI_VOLUME = uiVol;

                GUI.Label(SLeftSliderRect(++line), Localizer.Format("#LOC_BDArmory_Settings_WeaponVolume") + ": " + (BDArmorySettings.BDARMORY_WEAPONS_VOLUME * 100).ToString("0"), leftLabel);//Weapon Volume
                float weaponVol = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
                weaponVol = GUI.HorizontalSlider(SRightSliderRect(line), weaponVol, 0f, 1f);
                if (uiVol != BDArmorySettings.BDARMORY_WEAPONS_VOLUME && OnVolumeChange != null)
                {
                    OnVolumeChange();
                }
                BDArmorySettings.BDARMORY_WEAPONS_VOLUME = weaponVol;

                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS = GUI.Toggle(new Rect(settingsMargin, ++line * settingsLineHeight, 2f * (settingsWidth - 2f * settingsMargin) / 3f, settingsLineHeight), BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS, Localizer.Format("#LOC_BDArmory_Settings_TraceVessels"));// Trace Vessels (custom 2/3 width)
                    if (LoadedVesselSwitcher.Instance != null)
                    {
                        if (GUI.Button(SLineThirdRect(line, 2), LoadedVesselSwitcher.Instance.vesselTraceEnabled ? Localizer.Format("#LOC_BDArmory_Settings_TraceVesselsManualStop") : Localizer.Format("#LOC_BDArmory_Settings_TraceVesselsManualStart")))
                        {
                            if (LoadedVesselSwitcher.Instance.vesselTraceEnabled)
                            { LoadedVesselSwitcher.Instance.StopVesselTracing(); }
                            else
                            { LoadedVesselSwitcher.Instance.StartVesselTracing(); }
                        }
                    }
                }

                line += 0.5f;
            }

            if (GUI.Button(SLineRect(++line), (BDArmorySettings.COMPETITION_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_CompSettingsToggle")))//Show/hide Competition settings.
            {
                BDArmorySettings.COMPETITION_SETTINGS_TOGGLE = !BDArmorySettings.COMPETITION_SETTINGS_TOGGLE;
            }
            if (BDArmorySettings.COMPETITION_SETTINGS_TOGGLE)
            {
                line += 0.2f;

                BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START = GUI.Toggle(SLineRect(++line), BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START, Localizer.Format("#LOC_BDArmory_Settings_CompetitionCloseSettingsOnCompetitionStart"));

                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_DebrisCleanUpDelay")}:  ({BDArmorySettings.DEBRIS_CLEANUP_DELAY}s)", leftLabel); // Debris Clean-up delay
                    BDArmorySettings.DEBRIS_CLEANUP_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.DEBRIS_CLEANUP_DELAY, 1f, 60f));

                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionNonCompetitorRemovalDelay")}:  ({(BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY > 60 ? "Off" : BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY + "s")})", leftLabel); // Non-competitor removal frequency
                    BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY, 1f, 61f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionDuration")}: ({(BDArmorySettings.COMPETITION_DURATION > 0 ? BDArmorySettings.COMPETITION_DURATION + (BDArmorySettings.COMPETITION_DURATION > 1 ? " mins" : " min") : "Unlimited")})", leftLabel);
                BDArmorySettings.COMPETITION_DURATION = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_DURATION, 0f, 15f));
                if (BDArmorySettings.ADVANDED_USER_SETTINGS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionInitialGracePeriod")}: ({BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD}s)", leftLabel);
                    BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD, 0f, 60f));
                }
                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionFinalGracePeriod")}: ({(BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD > 60 ? "Inf" : BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD + "s")})", leftLabel);
                BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD, 0f, 61f));

                { // Auto Start Competition NOW Delay
                    string startNowAfter;
                    if (BDArmorySettings.COMPETITION_START_NOW_AFTER > 10)
                    {
                        startNowAfter = "Off";
                    }
                    else if (BDArmorySettings.COMPETITION_START_NOW_AFTER > 5)
                    {
                        startNowAfter = $"{BDArmorySettings.COMPETITION_START_NOW_AFTER - 5}mins";
                    }
                    else
                    {
                        startNowAfter = $"{BDArmorySettings.COMPETITION_START_NOW_AFTER * 10}s";
                    }
                    GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionStartNowAfter")}: ({startNowAfter})", leftLabel);
                    BDArmorySettings.COMPETITION_START_NOW_AFTER = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_START_NOW_AFTER, 0f, 11f));
                }

                GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionKillTimer")}: (" + (BDArmorySettings.COMPETITION_KILL_TIMER > 0 ? (BDArmorySettings.COMPETITION_KILL_TIMER + "s") : "Off") + ")", leftLabel); // FIXME the toggle and this slider could be merged
                BDArmorySettings.COMPETITION_KILL_TIMER = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILL_TIMER, 0, 60f));

                GUI.Label(SLeftRect(++line), Localizer.Format("#LOC_BDArmory_Settings_CompetitionDistance"));//"Competition Distance"
                float cDist;
                compDistGui = GUI.TextField(SRightRect(line, 1, true), compDistGui);
                if (Single.TryParse(compDistGui, out cDist))
                {
                    BDArmorySettings.COMPETITION_DISTANCE = (int)cDist;
                }

                line += 0.2f;
                if (GUI.Button(SLineRect(++line, 1, true), (BDArmorySettings.GM_SETTINGS_TOGGLE ? Localizer.Format("#LOC_BDArmory_Generic_Hide") : Localizer.Format("#LOC_BDArmory_Generic_Show")) + " " + Localizer.Format("#LOC_BDArmory_Settings_GMSettingsToggle")))//Show/hide slider settings.
                {
                    BDArmorySettings.GM_SETTINGS_TOGGLE = !BDArmorySettings.GM_SETTINGS_TOGGLE;
                }
                if (BDArmorySettings.GM_SETTINGS_TOGGLE)
                {
                    line += 0.2f;

                    { // Killer GM Max Altitude
                        string killerGMMaxAltitudeText;
                        if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH > 54f) killerGMMaxAltitudeText = "Never";
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 20f) killerGMMaxAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH * 100f) + "m";
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 39f) killerGMMaxAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 18f) + "km";
                        else killerGMMaxAltitudeText = Mathf.RoundToInt((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 38f) * 5f + 20f) + "km";
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionAltitudeLimitHigh")}: ({killerGMMaxAltitudeText})", leftLabel);
                        BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH, 1f, 55f));
                    }
                    { // Killer GM Min Altitude
                        string killerGMMinAltitudeText;
                        if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -38f) killerGMMinAltitudeText = "Never"; // Never
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -28f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 28f) + "km"; // -10km — -1km @ 1km
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -19f) killerGMMinAltitudeText = Mathf.RoundToInt((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 19f) * 100f) + "m"; // -900m — -100m @ 100m
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 0f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 5f) + "m"; // -95m — -5m  @ 5m
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 20f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 100f) + "m"; // 0m — 1900m @ 100m
                        else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 39f) killerGMMinAltitudeText = Mathf.RoundToInt(BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 18f) + "km"; // 2km — 20km @ 1km
                        else killerGMMinAltitudeText = Mathf.RoundToInt((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 38f) * 5f + 20f) + "km"; // 25km — 50km @ 5km
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionAltitudeLimitLow")}: ({killerGMMinAltitudeText})", leftLabel);
                        BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW, -39f, 44f));
                    }
                    if (BDArmorySettings.RUNWAY_PROJECT)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionKillerGMGracePeriod")}: ({BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD}s)", leftLabel);
                        BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD / 10f, 0f, 18f)) * 10f;

                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionKillerGMFrequency")}: ({(BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? "Off" : BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY + "s")}, {(BDACompetitionMode.Instance != null && BDACompetitionMode.Instance.killerGMenabled ? "on" : "off")})", leftLabel);
                        BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY / 10f, 1, 6)) * 10f; // For now, don't control the killerGMEnabled flag (it's controlled by right clicking M).
                    }

                    line += 0.2f;
                }

                if (BDArmorySettings.REMOTE_LOGGING_VISIBLE)
                {
                    if (GUI.Button(SLineRect(++line, 1, true), Localizer.Format(BDArmorySettings.REMOTE_LOGGING_ENABLED ? "#LOC_BDArmory_Disable" : "#LOC_BDArmory_Enable") + " " + Localizer.Format("#LOC_BDArmory_Settings_RemoteLogging")))
                    {
                        BDArmorySettings.REMOTE_LOGGING_ENABLED = !BDArmorySettings.REMOTE_LOGGING_ENABLED;
                    }
                    if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                    {
                        GUI.Label(SLeftRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_CompetitionID")}: ", leftLabel); // Competition hash.
                        BDArmorySettings.COMPETITION_HASH = GUI.TextField(SRightRect(line, 1, true), BDArmorySettings.COMPETITION_HASH);
                        GUI.Label(SLeftSliderRect(++line), $"{Localizer.Format("#LOC_BDArmory_Settings_RemoteInterheatDelay")}: ({BDArmorySettings.REMOTE_INTERHEAT_DELAY}s)", leftLabel); // Inter-heat delay
                        BDArmorySettings.REMOTE_INTERHEAT_DELAY = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.REMOTE_INTERHEAT_DELAY, 1f, 30f));
                    }
                }
                else
                {
                    BDArmorySettings.REMOTE_LOGGING_ENABLED = false;
                }

                line += 0.5f;
            }

            if (HighLogic.LoadedSceneIsFlight && BDACompetitionMode.Instance != null)
            {
                line += 0.5f;

                GUI.Label(SLineRect(++line), "=== " + Localizer.Format("#LOC_BDArmory_Settings_DogfightCompetition") + " ===", centerLabel);//Dogfight Competition
                if (BDACompetitionMode.Instance.competitionIsActive)
                {
                    if (GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_StopCompetition"))) // Stop competition.
                    {
                        BDACompetitionMode.Instance.StopCompetition();
                    }
                }
                else if (BDACompetitionMode.Instance.competitionStarting)
                {
                    GUI.Label(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_CompetitionStarting") + " (" + compDistGui + ")");//Starting Competition...
                    if (GUI.Button(SLeftButtonRect(++line), Localizer.Format("#LOC_BDArmory_Generic_Cancel")))//"Cancel"
                    {
                        BDACompetitionMode.Instance.StopCompetition();
                    }
                    if (GUI.Button(SRightButtonRect(line), Localizer.Format("#LOC_BDArmory_Settings_StartCompetitionNow"))) // Start competition NOW button.
                    {
                        BDACompetitionMode.Instance.StartCompetitionNow();
                        if (BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START) CloseSettingsWindow();
                    }
                }
                else
                {
                    if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
                    {
                        if (GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_RemoteSync"))) // Run Via Remote Orchestration
                        {
                            string vesselPath = Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn");
                            if (!System.IO.Directory.Exists(vesselPath))
                            {
                                System.IO.Directory.CreateDirectory(vesselPath);
                            }
                            BDAScoreService.Instance.Configure(vesselPath, BDArmorySettings.COMPETITION_HASH);
                            if (BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START) CloseSettingsWindow();
                        }
                    }
                    else
                    {
                        string startCompetitionText = Localizer.Format("#LOC_BDArmory_Settings_StartCompetition");
                        if (BDArmorySettings.RUNWAY_PROJECT)
                        {
                            switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                            {
                                case 33:
                                    startCompetitionText = Localizer.Format("#LOC_BDArmory_Settings_StartRapidDeployment");
                                    break;
                                case 44:
                                    startCompetitionText = Localizer.Format("#LOC_BDArmory_Settings_LowGravDeployment");
                                    break;
                                case 60: // FIXME temporary index, to be assigned later
                                    startCompetitionText = Localizer.Format("#LOC_BDArmory_Settings_StartOrbitalDeployment");
                                    break;
                            }
                        }
                        if (GUI.Button(SLineRect(++line), startCompetitionText))//"Start Competition"
                        {

                            BDArmorySettings.COMPETITION_DISTANCE = Mathf.Max(BDArmorySettings.COMPETITION_DISTANCE, 0);
                            compDistGui = BDArmorySettings.COMPETITION_DISTANCE.ToString();
                            if (BDArmorySettings.RUNWAY_PROJECT)
                            {
                                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                                {
                                    case 33:
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    case 44:
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    case 60: // FIXME temporary index, to be assigned later
                                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                                        break;
                                    default:
                                        BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE);
                                        break;
                                }
                            }
                            else
                                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE);
                            if (BDArmorySettings.COMPETITION_CLOSE_SETTINGS_ON_COMPETITION_START) CloseSettingsWindow();
                        }
                    }
                }
            }

            ++line;
            if (GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Settings_EditInputs")))//"Edit Inputs"
            {
                editKeys = true;
            }
            line += 0.5f;
            if (!BDKeyBinder.current && GUI.Button(SLineRect(++line), Localizer.Format("#LOC_BDArmory_Generic_SaveandClose")))//"Save and Close"
            {
                SaveConfig();
                windowSettingsEnabled = false;
            }

            line += 1.5f; // Bottom internal margin
            settingsHeight = (line * settingsLineHeight);
            WindowRectSettings.height = settingsHeight;
            GUIUtils.RepositionWindow(ref WindowRectSettings);
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
        }

        void CloseSettingsWindow()
        {
            SaveConfig();
            windowSettingsEnabled = false;
        }

        internal static void ResizeRwrWindow(float rwrScale)
        {
            BDArmorySettings.RWR_WINDOW_SCALE = rwrScale;
            RadarWarningReceiver.RwrDisplayRect = new Rect(0, 0, RadarWarningReceiver.RwrSize * rwrScale,
              RadarWarningReceiver.RwrSize * rwrScale);
            BDArmorySetup.WindowRectRwr =
              new Rect(BDArmorySetup.WindowRectRwr.x, BDArmorySetup.WindowRectRwr.y,
                RadarWarningReceiver.RwrDisplayRect.height + RadarWarningReceiver.BorderSize,
                RadarWarningReceiver.RwrDisplayRect.height + RadarWarningReceiver.BorderSize + RadarWarningReceiver.HeaderSize);
        }

        internal static void ResizeRadarWindow(float radarScale)
        {
            BDArmorySettings.RADAR_WINDOW_SCALE = radarScale;
            VesselRadarData.RadarDisplayRect =
              new Rect(VesselRadarData.BorderSize / 2, VesselRadarData.BorderSize / 2 + VesselRadarData.HeaderSize,
                VesselRadarData.RadarScreenSize * radarScale,
                VesselRadarData.RadarScreenSize * radarScale);
            WindowRectRadar =
              new Rect(WindowRectRadar.x, WindowRectRadar.y,
                VesselRadarData.RadarDisplayRect.height + VesselRadarData.BorderSize + VesselRadarData.ControlsWidth + VesselRadarData.Gap * 3,
                VesselRadarData.RadarDisplayRect.height + VesselRadarData.BorderSize + VesselRadarData.HeaderSize);
        }

        internal static void ResizeTargetWindow(float targetScale)
        {
            BDArmorySettings.TARGET_WINDOW_SCALE = targetScale;
            ModuleTargetingCamera.ResizeTargetWindow();
        }

        private static Vector2 _displayViewerPosition = Vector2.zero;

        void InputSettings()
        {
            float line = 0f;
            int inputID = 0;
            float origSettingsWidth = settingsWidth;
            float origSettingsHeight = settingsHeight;
            float origSettingsMargin = settingsMargin;

            settingsMargin = 10;
            settingsWidth = origSettingsWidth - 2 * settingsMargin;
            settingsHeight = origSettingsHeight - 100;
            Rect viewRect = new Rect(2, 20, settingsWidth + GUI.skin.verticalScrollbar.fixedWidth, settingsHeight);
            Rect scrollerRect = new Rect(0, 0, settingsWidth - GUI.skin.verticalScrollbar.fixedWidth - 1, inputFields != null ? (inputFields.Length + 11) * settingsLineHeight : settingsHeight);

            _displayViewerPosition = GUI.BeginScrollView(viewRect, _displayViewerPosition, scrollerRect, false, true);

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_GUI") + " -", centerLabel); //GUI
            InputSettingsList("GUI_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_Weapons") + " -", centerLabel);//Weapons
            InputSettingsList("WEAP_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_TargetingPod") + " -", centerLabel);//Targeting Pod
            InputSettingsList("TGP_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_Radar") + " -", centerLabel);//Radar
            InputSettingsList("RADAR_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_VesselSwitcher") + " -", centerLabel);//Vessel Switcher
            InputSettingsList("VS_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_Tournament") + " -", centerLabel);//Tournament
            InputSettingsList("TOURNAMENT_", ref inputID, ref line);
            ++line;

            GUI.Label(SLineRect(line++), "- " + Localizer.Format("#LOC_BDArmory_InputSettings_TimeScaling") + " -", centerLabel);//Time Scaling
            InputSettingsList("TIME_", ref inputID, ref line);
            GUI.EndScrollView();

            line = settingsHeight / settingsLineHeight;
            line += 2;
            settingsWidth = origSettingsWidth;
            settingsMargin = origSettingsMargin;
            if (!BDKeyBinder.current && GUI.Button(SLineRect(line), Localizer.Format("#LOC_BDArmory_InputSettings_BackBtn")))//"Back"
            {
                editKeys = false;
            }

            settingsHeight = origSettingsHeight;
            WindowRectSettings.height = origSettingsHeight;
            GUIUtils.UseMouseEventInRect(WindowRectSettings);
        }

        void InputSettingsList(string prefix, ref int id, ref float line)
        {
            if (inputFields != null)
            {
                for (int i = 0; i < inputFields.Length; i++)
                {
                    string fieldName = inputFields[i].Name;
                    if (fieldName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        InputSettingsLine(fieldName, id++, ref line);
                    }
                }
            }
        }

        void InputSettingsLine(string fieldName, int id, ref float line)
        {
            GUI.Box(SLineRect(line), GUIContent.none);
            string label = String.Empty;
            if (BDKeyBinder.IsRecordingID(id))
            {
                string recordedInput;
                if (BDKeyBinder.current.AcquireInputString(out recordedInput))
                {
                    BDInputInfo orig = (BDInputInfo)typeof(BDInputSettingsFields).GetField(fieldName).GetValue(null);
                    BDInputInfo recorded = new BDInputInfo(recordedInput, orig.description);
                    typeof(BDInputSettingsFields).GetField(fieldName).SetValue(null, recorded);
                }

                label = "      " + Localizer.Format("#LOC_BDArmory_InputSettings_recordedInput");//Press a key or button.
            }
            else
            {
                BDInputInfo inputInfo = new BDInputInfo();
                try
                {
                    inputInfo = (BDInputInfo)typeof(BDInputSettingsFields).GetField(fieldName).GetValue(null);
                }
                catch (NullReferenceException e)
                {
                    Debug.LogWarning("[BDArmory.BDArmorySetup]: Reflection failed to find input info of field: " + fieldName + ": " + e.Message);
                    editKeys = false;
                    return;
                }
                label = " " + inputInfo.description + " : " + inputInfo.inputString;

                if (GUI.Button(SSetKeyRect(line), Localizer.Format("#LOC_BDArmory_InputSettings_SetKey")))//"Set Key"
                {
                    BDKeyBinder.BindKey(id);
                }
                if (GUI.Button(SClearKeyRect(line), Localizer.Format("#LOC_BDArmory_InputSettings_Clear")))//"Clear"
                {
                    typeof(BDInputSettingsFields).GetField(fieldName)
                        .SetValue(null, new BDInputInfo(inputInfo.description));
                }
            }
            GUI.Label(SLeftRect(line), label);
            line++;
        }

        Rect SSetKeyRect(float line)
        {
            return new Rect(settingsMargin + (2 * (settingsWidth - 2 * settingsMargin) / 3), line * settingsLineHeight,
                (settingsWidth - (2 * settingsMargin)) / 6, settingsLineHeight);
        }

        Rect SClearKeyRect(float line)
        {
            return
                new Rect(
                    settingsMargin + (2 * (settingsWidth - 2 * settingsMargin) / 3) + (settingsWidth - 2 * settingsMargin) / 6,
                    line * settingsLineHeight, (settingsWidth - (2 * settingsMargin)) / 6, settingsLineHeight);
        }

        #endregion GUI

        void HideGameUI()
        {
            GAME_UI_ENABLED = false;
            BDACompetitionMode.Instance.UpdateGUIElements();
        }

        void ShowGameUI()
        {
            GAME_UI_ENABLED = true;
            BDACompetitionMode.Instance.UpdateGUIElements();
        }

        internal void OnDestroy()
        {
            if (saveWindowPosition)
            {
                BDAWindowSettingsField.Save();
            }
            if (windowSettingsEnabled || showVesselSpawnerGUI)
                SaveConfig();

            GameEvents.onHideUI.Remove(HideGameUI);
            GameEvents.onShowUI.Remove(ShowGameUI);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.OnGameSettingsApplied.Remove(SaveVolumeSettings);
            GameEvents.onVesselChange.Remove(VesselChange);
        }

        void OnVesselGoOffRails(Vessel v)
        {
            if (BDArmorySettings.DEBUG_OTHER)
            {
                Debug.Log("[BDArmory.BDArmorySetup]: Loaded vessel: " + v.vesselName + ", Velocity: " + v.Velocity() + ", packed: " + v.packed);
                //v.SetWorldVelocity(Vector3d.zero);
            }
        }

        public void SaveVolumeSettings()
        {
            SeismicChargeFX.originalShipVolume = GameSettings.SHIP_VOLUME;
            SeismicChargeFX.originalMusicVolume = GameSettings.MUSIC_VOLUME;
            SeismicChargeFX.originalAmbienceVolume = GameSettings.AMBIENCE_VOLUME;
        }

#if DEBUG
        IEnumerator TestVesselPositionTiming()
        {
            var wait = new WaitForFixedUpdate();
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, ObscenelyEarly);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Early, Early);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, Precalc);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Earlyish, Earlyish);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Normal, Normal);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, FashionablyLate);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FlightIntegrator, FlightIntegrator);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Late, Late);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, BetterLateThanNever);
            yield return wait;
            yield return wait;
            yield return wait;
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.ObscenelyEarly, ObscenelyEarly);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Early, Early);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, Precalc);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Earlyish, Earlyish);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Normal, Normal);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FashionablyLate, FashionablyLate);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FlightIntegrator, FlightIntegrator);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Late, Late);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, BetterLateThanNever);
        }
        void ObscenelyEarly() { Debug.Log($"DEBUG {Time.time} ObscenelyEarly, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void Early() { Debug.Log($"DEBUG {Time.time} Early, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void Precalc() { Debug.Log($"DEBUG {Time.time} Precalc, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void Earlyish() { Debug.Log($"DEBUG {Time.time} Earlyish, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void Normal() { Debug.Log($"DEBUG {Time.time} Normal, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void FashionablyLate() { Debug.Log($"DEBUG {Time.time} FashionablyLate, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void FlightIntegrator() { Debug.Log($"DEBUG {Time.time} FlightIntegrator, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void Late() { Debug.Log($"DEBUG {Time.time} Late, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
        void BetterLateThanNever() { Debug.Log($"DEBUG {Time.time} BetterLateThanNever, active vessel position: {FlightGlobals.ActiveVessel.transform.position.ToString("G6")}"); }
#endif
    }
}
