using BDArmory.UI;
using BDArmory.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BDArmory.Settings
{
    [AttributeUsage(AttributeTargets.Field)]
    public class SettingsDataField : Attribute
    {
        public SettingsDataField()
        {
        }
        public static void Save()
        {
            ConfigNode fileNode = ConfigNode.Load(BDTISettings.settingsConfigURL);

            if (!fileNode.HasNode("IconSettings"))
            {
                fileNode.AddNode("IconSettings");
            }

            ConfigNode settings = fileNode.GetNode("IconSettings");
            using (IEnumerator<FieldInfo> field = typeof(BDTISettings).GetFields().AsEnumerable().GetEnumerator())
                while (field.MoveNext())
                {
                    if (field.Current == null) continue;
                    if (!field.Current.IsDefined(typeof(SettingsDataField), false)) continue;

                    settings.SetValue(field.Current.Name, field.Current.GetValue(null).ToString(), true);
                }
            if (BDTISettings.STORE_TEAM_COLORS)
            {
                if (!fileNode.HasNode("TeamColors"))
                {
                    fileNode.AddNode("TeamColors");
                }

                ConfigNode colors = fileNode.GetNode("TeamColors");

                foreach (var keyValuePair in BDTISetup.Instance.ColorAssignments)
                {
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log(keyValuePair.ToString());
                    string color = $"{Mathf.RoundToInt(keyValuePair.Value.r * 255)},{Mathf.RoundToInt(keyValuePair.Value.g * 255)},{Mathf.RoundToInt(keyValuePair.Value.b * 255)},{Mathf.RoundToInt(keyValuePair.Value.a * 255)}";
                    colors.SetValue(keyValuePair.Key.ToString(), color, true);
                }
            }
            else
            {
                if (fileNode.HasNode("TeamColors"))
                {
                    fileNode.RemoveNode("TeamColors");
                }
            }

            if (!fileNode.HasNode("PresetColors"))
            {
                fileNode.AddNode("PresetColors");
            }
            ConfigNode preset = fileNode.GetNode("PresetColors");
            preset.ClearValues(); // Clear old values
            int i = 0; // Reset the indexing
            foreach (Color presetColor in BDTISetup.Instance.ColorPresets)
            {
                string color = $"{Mathf.RoundToInt(presetColor.r * 255)},{Mathf.RoundToInt(presetColor.g * 255)},{Mathf.RoundToInt(presetColor.b * 255)},{Mathf.RoundToInt(presetColor.a * 255)}";
                preset.SetValue((++i).ToString(), color, true);
            }
            fileNode.Save(BDTISettings.settingsConfigURL);
        }
        public static void Load()
        {
            ConfigNode fileNode = ConfigNode.Load(BDTISettings.settingsConfigURL);
            if (!fileNode.HasNode("IconSettings")) return;

            ConfigNode settings = fileNode.GetNode("IconSettings");
            using (IEnumerator<FieldInfo> field = typeof(BDTISettings).GetFields().AsEnumerable().GetEnumerator())
                while (field.MoveNext())
                {
                    if (field.Current == null) continue;
                    if (!field.Current.IsDefined(typeof(SettingsDataField), false)) continue;

                    if (!settings.HasValue(field.Current.Name)) continue;
                    object parsedValue = ParseValue(field.Current.FieldType, settings.GetValue(field.Current.Name));
                    if (parsedValue != null)
                    {
                        field.Current.SetValue(null, parsedValue);
                    }
                }

            ConfigNode presets = fileNode.GetNode("PresetColors");
            BDTISetup.Instance.ColorPresets.Clear();
            foreach (var colorPreset in presets.GetValues())
            {
                // Note: the keys aren't really important, they're just for interacting with the currently loaded values
                var color = GUIUtils.ParseColor255(colorPreset);
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BDTISettingsField]: Loading preset color: {color}");
                BDTISetup.Instance.ColorPresets.Add(color);
            }
            if (BDTISetup.Instance.ColorPresets.Count < 16)
            {
                // If fewer than 16 presets, pad with defaults.
                List<Color> defaultPresets = [
                    GUIUtils.ParseColor255("255,0,0,255"), //Red
                    GUIUtils.ParseColor255("255,255,0,255"), //Yellow
                    GUIUtils.ParseColor255("0,255,0,255"), //Green
                    GUIUtils.ParseColor255("0,255,255,255"), //Blue
                    GUIUtils.ParseColor255("0,0,255,255"), //Indigo
                    GUIUtils.ParseColor255("128,0,192,255"), //Purple
                    GUIUtils.ParseColor255("255,255,255,255"), //White
                    GUIUtils.ParseColor255("0,0,0,255"), //Black
                    GUIUtils.ParseColor255("255,128,0,255"), //Orange
                    GUIUtils.ParseColor255("62,50,0,255"), //Brown
                    GUIUtils.ParseColor255("16,96,16,255"), //Dark Green
                    GUIUtils.ParseColor255("192,255,192,255"), //Light Green
                    GUIUtils.ParseColor255("192,192,255,255"), //Light Blue
                    GUIUtils.ParseColor255("192,0,192,255"), //Marroon
                    GUIUtils.ParseColor255("255,192,255,255"), //Pink
                    GUIUtils.ParseColor255("128,128,128,255"), //Grey
                ];
                foreach (var rgba in defaultPresets)
                {
                    if (BDTISetup.Instance.ColorPresets.Count >= 16) break;
                    if (BDTISetup.Instance.ColorPresets.Contains(rgba)) continue;
                    BDTISetup.Instance.ColorPresets.Add(rgba);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BDTISettingsField]: Adding preset {rgba}.");
                }
            }

            if (!BDTISettings.STORE_TEAM_COLORS || !fileNode.HasNode("TeamColors")) return;
            ConfigNode colors = fileNode.GetNode("TeamColors");
            for (int i = 0; i < colors.CountValues; i++)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDTISettingsField]: loading team " + colors.values[i].name + "; color: " + GUIUtils.ParseColor255(colors.values[i].value));
                if (BDTISetup.Instance.ColorAssignments.ContainsKey(colors.values[i].name))
                {
                    BDTISetup.Instance.ColorAssignments[colors.values[i].name] = GUIUtils.ParseColor255(colors.values[i].value);
                }
                else
                {
                    BDTISetup.Instance.ColorAssignments.Add(colors.values[i].name, GUIUtils.ParseColor255(colors.values[i].value));
                }
            }
        }

        public static object ParseValue(Type type, string value)
        {
            if (type == typeof(bool))
            {
                return bool.Parse(value);
            }
            else if (type == typeof(float))
            {
                return float.Parse(value);
            }
            else if (type == typeof(string))
            {
                return value;
            }
            Debug.LogError("[BDArmory.BDTISettingsField]: BDAPersistentSettingsField to parse settings field of type " + type +
                             " and value " + value);

            return null;
        }
    }
}
