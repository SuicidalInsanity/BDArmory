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

            foreach (var keyValuePair in BDTISetup.Instance.ColorPresets)
            {
                string color = $"{Mathf.RoundToInt(keyValuePair.Value.r * 255)},{Mathf.RoundToInt(keyValuePair.Value.g * 255)},{Mathf.RoundToInt(keyValuePair.Value.b * 255)},{Mathf.RoundToInt(keyValuePair.Value.a * 255)}";
                preset.SetValue(keyValuePair.Key.ToString(), color, true);
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

            for (int i = 0; i < presets.CountValues; i++)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDTISettingsField]: loading preset " + presets.values[i].name + "; color: " + GUIUtils.ParseColor255(presets.values[i].value));
                if (BDTISetup.Instance.ColorPresets.ContainsKey(i + 1)) // The dictionary indexing is 1-based.
                {
                    BDTISetup.Instance.ColorPresets[i + 1] = GUIUtils.ParseColor255(presets.values[i].value);
                }
                else
                {
                    BDTISetup.Instance.ColorPresets.Add(i + 1, GUIUtils.ParseColor255(presets.values[i].value));
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

        public static void presetSetup()
        {
            ConfigNode fileNode = ConfigNode.Load(BDTISettings.settingsConfigURL);

            if (!fileNode.HasNode("PresetColors"))
            {
                fileNode.AddNode("PresetColors");
            }
            ConfigNode preset = fileNode.GetNode("PresetColors");
            List<string> presetRGB = [.. preset.GetValues().ToHashSet()]; // We may lose the user's colour order, but they'll still be the first entries.
            List<string> defaultPresets = [
                 "255,0,0,255", //Red
                "255,255,0,255", //Yellow
                "0,255,0,255", //Green
                "0,255,255,255", //Blue
                "0,0,255,255", //Indigo
                "128,0,192,255", //Purple
                "255,255,255,255", //White
                "0,0,0,255", //Black
                "255,128,0,255", //Orange
                "62,50,0,255", //Brown
                "16,96,16,255", //Dark Green
                "192,255,192,255", //Light Green
                "192,192,255,255", //Light Blue
                "192,0,192,255", //Marroon
                "255,192,255,255", //Pink
                "128,128,128,255" //Grey
            ];
            foreach (var rgba in defaultPresets)
            {
                if (presetRGB.Count >= 16) break;
                if (presetRGB.Contains(rgba)) continue;
                presetRGB.Add(rgba);
            }
            preset.ClearValues();
            for (int i = 0; i < presetRGB.Count; ++i)
            { preset.SetValue($"{i + 1}", presetRGB[i], true); }
            fileNode.Save(BDTISettings.settingsConfigURL);
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
