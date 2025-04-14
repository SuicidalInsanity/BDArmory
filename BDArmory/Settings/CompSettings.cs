using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;

namespace BDArmory.Settings
{
    public class CompSettings
    {
        // Settings overrides for AI/WM settings for competition rules compliance

        static readonly string CompSettingsPath = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/BDArmory/PluginData/Comp_settings.cfg"));
        static public bool CompOverridesEnabled = false;
        static public bool CompVesselChecksEnabled = false;
        static public bool CompPriceChecksEnabled = false;
        static public bool CompBanChecksEnabled = false;
        public static readonly Dictionary<string, float> CompOverrides = new()
        {
				// FIXME there's probably a few more things that could get set here for AI/WM override if needed in specific rounds.
				//AI Min/max Alt?
                //AI postStallAoA?
                //AI allowRamming?
                //WM gunRange?
                //WM multiMissileTgtNum
				{"extensionCutoffTime", -1},
                {"extendDistanceAirToAir", -1},
                {"MONOCOCKPIT_VIEWRANGE", -1},
                {"DUALCOCKPIT_VIEWRANGE", -1},
                {"guardAngle", -1},
                {"collisionAvoidanceThreshold", -1},
                {"vesselCollisionAvoidanceLookAheadPeriod", -1},
                {"vesselCollisionAvoidanceStrength", -1 },
                {"idleSpeed", -1},
                {"DISABLE_SAS", 0}, //0/1 for F/T
		    };
        public static readonly Dictionary<string, float> vesselChecks = new()
        {
                {"maxStacking", -1}, //wing Stacking %. No limit if -1
                {"maxPartCount", -1}, //part count. no limit if -1
                {"maxLtW", -1},        //Lift-to-Weight ratio. -1 for no limit   
                {"maxTWR", -1},        //Thrust-Weight ratio. -1 for no limit   
                {"maxEngines", 999},    //set to negative to mandate that number of engines on the craft
                {"maxMass", -1},
                {"pointBuyBudget", -1}, //for comps with point buy systems for limiting armament/etc. if enabled, will check parts against partPointCosts
		    };
        public static readonly Dictionary<string, float> partPointCosts = new()
        {
            //{"bahaBrowningAnm2", 1},
        };
        public static readonly Dictionary<string, float> partBlacklist = new()
        {
            //{"bahaCloakingDevice", 2}, //flag craft containing more than allowed number of X
        };
        /// <summary>
        /// Load P:S AI/Wm override settings from file.
        /// </summary>
        public static void Load()
        {
            CompOverridesEnabled = false;
            CompVesselChecksEnabled = false;
            CompPriceChecksEnabled = false;
            CompBanChecksEnabled = false;
            if (!File.Exists(CompSettingsPath))
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.CompSettings]: Override settings not present, skipping.");
                return;
            }
            ConfigNode fileNode = ConfigNode.Load(CompSettingsPath);

            string settingsComment = "Settings overrides for AI/WM settings for competition rules compliance. Use -1 to disable override of that setting at competition start. MONO/DUALCOCKPIT settings set single-seat and 2+ seat cockpit view range. DISABLE_SAS uses 0/1 for False/True ";
            if (!fileNode.HasNode("AIWMChecks"))
            {
                Initialize("AIWMChecks", CompOverrides, settingsComment, fileNode);
            }
            if (fileNode.HasNode("AIWMChecks"))
            {
                ConfigNode settings = fileNode.GetNode("AIWMChecks");
                settings.comment = settingsComment;
                foreach (ConfigNode.Value fieldNode in settings.values)
                {
                    if (float.TryParse(fieldNode.value, out float fieldValue))
                    {
                        CompOverrides[fieldNode.name] = fieldValue; // Add or set the override.
                        if ((fieldNode.name == "DISABLE_SAS" && fieldValue > 0) || fieldValue >= 0) CompOverridesEnabled = true;
                    }
                }
                if (BDArmorySettings.DEBUG_OTHER)
                {
                    Debug.Log($"[BDArmory.CompSettings]: Comp AI/WM overrides loaded");
                    foreach (KeyValuePair<string, float> entry in CompOverrides)
                    {
                        Debug.Log($"[BDArmory.CompSettings]: {entry.Key}, value {entry.Value} added");
                    }
                }
            }
            string VCComment = "Set construction rule limits here. maxStacking is Wing Stacking% in the SPH/VAB BDA Utilities Tool GUI. Use -1 for no limit for the respective field. Setting maxEngines to a negative number will mandate a minimum engine count, e.g. maxEngines = -2 requires 2+ engines on the craft."; // Note: reading the node doesn't seem to get the comment, so we need to reset it each time.
            if (!fileNode.HasNode("VesselChecks"))
            {
                Initialize("VesselChecks", vesselChecks, VCComment, fileNode);
            }
            if (fileNode.HasNode("VesselChecks"))
            {
                ConfigNode settings = fileNode.GetNode("VesselChecks");
                settings.comment = VCComment;
                foreach (ConfigNode.Value fieldNode in settings.values)
                {
                    if (float.TryParse(fieldNode.value, out float fieldValue))
                    {
                        vesselChecks[fieldNode.name] = fieldValue; // Add or set the override.
                        switch (fieldNode.name)
                        {
                            case "maxEngines": if (fieldValue != 999) CompVesselChecksEnabled = true; break;
                            default: if (fieldValue >= 0) CompVesselChecksEnabled = true; break;
                        }
                    }
                }

                if (BDArmorySettings.DEBUG_OTHER)
                {
                    Debug.Log($"[BDArmory.CompSettings]: Comp vessel checks loaded");
                    foreach (KeyValuePair<string, float> entry in vesselChecks)
                    {
                        Debug.Log($"[BDArmory.CompSettings]: {entry.Key}, value {entry.Value} added");
                    }
                }
            }
            string partCostComment = "Set parts and their point cost if pointBuyBudget > 0, e.g. 'bahaGAU-8 = 4'. Any underscores in part names need to be relaced with periods."; // Note: reading the node doesn't seem to get the comment, so we need to reset it each time.
            if (!fileNode.HasNode("PartCosts"))
            {
                Initialize("PartCosts", partPointCosts, partCostComment, fileNode);
            }
            if (fileNode.HasNode("PartCosts"))
            {
                ConfigNode settings = fileNode.GetNode("PartCosts");
                settings.comment = partCostComment;
                foreach (ConfigNode.Value fieldNode in settings.values)
                {
                    if (float.TryParse(fieldNode.value, out float fieldValue))
                        partPointCosts[fieldNode.name] = fieldValue; // Add or set the override.
                }
                if (partPointCosts.Keys.Count > 0) CompPriceChecksEnabled = true;
                if (BDArmorySettings.DEBUG_OTHER)
                {
                    Debug.Log($"[BDArmory.CompSettings]: Comp part costs loaded");
                    foreach (KeyValuePair<string, float> entry in partPointCosts)
                    {
                        Debug.Log($"[BDArmory.CompSettings]: {entry.Key}, value {entry.Value} added");
                    }
                    Debug.Log($"[BDArmory.CompSettings]: {partPointCosts.Keys.Count} parts have a pointCost");
                }
            }
            string blacklistComment = "Add parts to limit/ban select parts on a vessel. Identify part via part name, and max quantity that is allowed. If pointBuy is enabled, weapons/missiles not on the pricelist are autoblacklisted. A negative value will whitelist the part, and require it on the vessel in that quantity for the vessel to be legal - e.g. smallOreTank = -2 for a ruleset where the craft must have 2 ore tanks. Use an * for wildcard searches, e.g. baha* = 3 to limit craft to a max of 3 BDA parts";
            if (!fileNode.HasNode("PartBlackList"))
            {
                Initialize("PartBlackList", partBlacklist, blacklistComment, fileNode);
            }
            if (fileNode.HasNode("PartBlackList"))
            {
                ConfigNode settings = fileNode.GetNode("PartBlackList");
                settings.comment = blacklistComment;
                foreach (ConfigNode.Value fieldNode in settings.values)
                {
                    if (float.TryParse(fieldNode.value, out float fieldValue))
                        partBlacklist[fieldNode.name] = fieldValue;
                }
                if (partBlacklist.Keys.Count > 0) CompBanChecksEnabled = true;
                if (BDArmorySettings.DEBUG_OTHER)
                {
                    Debug.Log($"[BDArmory.CompSettings]: Comp part blacklist loaded");
                    foreach (KeyValuePair<string, float> entry in partBlacklist)
                    {
                        Debug.Log($"[BDArmory.CompSettings]: {entry.Key}, value {entry.Value} added");
                    }
                    Debug.Log($"[BDArmory.CompSettings]: {partBlacklist.Keys.Count} parts limited/banned");
                }
            }
            fileNode.Save(CompSettingsPath);
        }
        public static void Initialize(string Node, Dictionary<string, float> defaultSettings, string comment, ConfigNode fileNode)
        {
            fileNode.AddNode(Node, comment);
            var settingsNode = fileNode.GetNode(Node);
            settingsNode.comment = comment;
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.CompSettings]: initializing {Node}, {defaultSettings.Keys.Count} fields to add");
            foreach (var fieldName in defaultSettings.Keys)
            {
                var fieldValue = defaultSettings[fieldName];
                try
                {
                    settingsNode.SetValue(fieldName, fieldValue.ToString(), true);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.CompSettings]: Adding {fieldName}, value {fieldValue} to Comp_settings.cfg");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.CompSettings]: Exception triggered while trying to save field {fieldName} with value {fieldValue}: {e.Message}");
                }
            }
            fileNode.Save(CompSettingsPath);
        }
    }
}