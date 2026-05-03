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
            ////
            bool addDefaults = false;
            if (!fileNode.HasNode("PresetColors"))
            {
                fileNode.AddNode("PresetColors");
                addDefaults = true;
            }
            ConfigNode preset = fileNode.GetNode("PresetColors");
            var presetRGB = preset.GetValues().ToHashSet(); // Get the existing part names, then add our ones.
            if (addDefaults)
            {
                presetRGB.Add("255,0,0,255"); //Red
                presetRGB.Add("255,255,0,255"); //Yellow
                presetRGB.Add("0,255,0,255"); //Green
                presetRGB.Add("0,255,255,255"); //Blue
                presetRGB.Add("0,0,255,255"); //Indigo
                presetRGB.Add("128,0,192,255"); //Purple
                presetRGB.Add("255,255,255,255"); //White
                presetRGB.Add("0,0,0,255"); //Black
                presetRGB.Add("255,128,0,255"); //Orange
                presetRGB.Add("62,50,0,255"); //Brown
                presetRGB.Add("16,96,16,255"); //Dark Green
                presetRGB.Add("192,255,192,255"); //Light Green
                presetRGB.Add("192,192,255,255"); //Light Blue
                presetRGB.Add("192,0,192,255"); //Marroon
                presetRGB.Add("255,192,255,255"); //Pink
                presetRGB.Add("128,128,128,255"); //Grey
            }
            preset.ClearValues();
			if (addDefaults)
			{
				int partIndex = 0;
				foreach (var rgb in presetRGB)
					preset.SetValue($"{++partIndex}", rgb, true);
			}
			else
			{
				foreach (var keyValuePair in BDTISetup.Instance.ColorPresets)
				{
					string color = $"{Mathf.RoundToInt(keyValuePair.Value.r * 255)},{Mathf.RoundToInt(keyValuePair.Value.g * 255)},{Mathf.RoundToInt(keyValuePair.Value.b * 255)},{Mathf.RoundToInt(keyValuePair.Value.a * 255)}";
					preset.SetValue(keyValuePair.Key.ToString(), color, true);
				}
			}
            /////
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
                if (BDTISetup.Instance.ColorPresets.ContainsKey(i))
                {
                    BDTISetup.Instance.ColorPresets[i] = GUIUtils.ParseColor255(presets.values[i].value);
                }
                else
                {
                    BDTISetup.Instance.ColorPresets.Add(i, GUIUtils.ParseColor255(presets.values[i].value));
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