using System.Collections.Generic;
using System.Linq;
using BDArmory.Utils;
using UnityEngine;

// credit to Brian Jones (https://github.com/boj)& KSP ForumMember TaxiService
namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class TeamColorConfig : MonoBehaviour
    {
        private Texture2D displayPicker;
        public int displayTextureWidth = 360;
        public int displayTextureHeight = 360;
        private Texture2D prefabColorPreview;

        public int HorizPos;
        public int VertPos;

        public Color selectedColor;
        public Color presetColor;
        private Texture2D selectedColorPreview;

        private float hueSlider = 0f;
        private float prevHueSlider = 0f;
        private Texture2D hueTexture;

        private GUIStyle style;
        private GUIStyle GetStyle(Color color)
        {
            if (style == null)
            {
                style = new()
                {
                    padding = new(0, 0, 0, 0),
                    normal = new GUIStyleState { background = prefabColorPreview }
                };
            }
            prefabColorPreview.SetPixel(0, 0, color);
            prefabColorPreview.Apply();
            style.normal.background = prefabColorPreview;
            return style;
        }

        protected void Awake()
        {
            HorizPos = (Screen.width / 2) - (displayTextureWidth / 2);
            VertPos = (Screen.height / 2) - (displayTextureHeight / 2);

            renderColorPicker();

            hueTexture = new Texture2D(10, displayTextureHeight, TextureFormat.ARGB32, false);
            for (int x = 0; x < hueTexture.width; x++)
            {
                for (int y = 0; y < hueTexture.height; y++)
                {
                    float h = (y / (hueTexture.height * 1.0f)) * 1f;
                    hueTexture.SetPixel(x, y, new ColorHSV(h, 1f, 1f).ToColor());
                }
            }
            hueTexture.Apply();

            selectedColorPreview = new Texture2D(1, 1);
            selectedColorPreview.SetPixel(0, 0, selectedColor);
            prefabColorPreview = selectedColorPreview;
        }

        private void renderColorPicker()
        {
            Texture2D colorPicker = new Texture2D(displayTextureWidth, displayTextureHeight, TextureFormat.ARGB32, false);
            for (int x = 0; x < displayTextureWidth; x++)
            {
                for (int y = 0; y < displayTextureHeight; y++)
                {
                    float h = hueSlider;
                    float v = (y / (displayTextureHeight * 1.0f)) * 1f;
                    float s = (x / (displayTextureWidth * 1.0f)) * 1f;
                    colorPicker.SetPixel(x, y, new ColorHSV(h, s, v).ToColor());
                }
            }

            colorPicker.Apply();
            displayPicker = colorPicker;
        }

        protected void OnGUI()
        {
            if (!BDTISetup.Instance.showColorSelect) return;

            GUI.Box(new Rect(HorizPos - 3, VertPos - 3, displayTextureWidth + 60, displayTextureHeight + 60), "");

            if (hueSlider != prevHueSlider) // new Hue value
            {
                prevHueSlider = hueSlider;
                renderColorPicker();
            }

            if (GUI.RepeatButton(new Rect(HorizPos, VertPos, displayTextureWidth, displayTextureHeight), displayPicker))
            {
                int a = (int)Input.mousePosition.x;
                int b = Screen.height - (int)Input.mousePosition.y;

                selectedColor = displayPicker.GetPixel(a - HorizPos, -(b - VertPos));
            }

            hueSlider = GUI.VerticalSlider(new Rect(HorizPos + displayTextureWidth + 3, VertPos, 10, displayTextureHeight), hueSlider, 1, 0);
            GUI.Box(new Rect(HorizPos + displayTextureWidth + 20, VertPos, 20, displayTextureHeight), hueTexture);

            if (GUI.Button(new Rect(HorizPos + displayTextureWidth - 60, VertPos + displayTextureHeight + 10, 60, 25), StringUtils.Localize("#LOC_BDArmory_Icon_colorget")))
            {
                selectedColor = selectedColorPreview.GetPixel(0, 0);
                BDTISetup.Instance.showColorSelect = false;
                BDTISetup.Instance.UpdateTeamColor = true;
            }

            //preset colors
            int row = 0, column = 0, index = 0;
            KeyValuePair<int, Color> setColor = new(-1, default);
            foreach (var presetColor in BDTISetup.Instance.ColorPresets)
            {
                if (GUI.Button(new Rect(HorizPos + 10 + column * 20, VertPos + displayTextureHeight + 5 + 20 * row, 15, 15), new GUIContent(""), GetStyle(presetColor)))
                {
                    switch (Event.current.button)
                    {
                        case 1: // right click
                            if ((Event.current.modifiers & EventModifiers.Control) != 0) setColor = new(index, new(0, 0, 0, 0)); // Ctrl-right click to remove
                            else if (selectedColor.a != 0) setColor = new(index, selectedColor); // Update color
                            break;
                        default:
                            selectedColor = presetColor;
                            selectedColorPreview.SetPixel(0, 0, presetColor);
                            selectedColorPreview.Apply();
                            break;
                    }
                }
                ++column;
                if (2 * column >= BDTISetup.Instance.ColorPresets.Count)
                {
                    column = 0;
                    ++row;
                }
                ++index;
            }
            if (setColor.Key >= 0)
            {
                if (setColor.Value.a == 0)
                {
                    BDTISetup.Instance.ColorPresets.RemoveAt(setColor.Key);
                }
                else
                {
                    // If the color isn't already in the presets, update it.
                    if (!BDTISetup.Instance.ColorPresets.Contains(setColor.Value))
                        BDTISetup.Instance.ColorPresets[setColor.Key] = setColor.Value;
                }
            }

            // "Add preset" button
            column = BDTISetup.Instance.ColorPresets.Count / 2;
            row = BDTISetup.Instance.ColorPresets.Count % 2;
            if (GUI.Button(new Rect(HorizPos + 10 + column * 20, VertPos + displayTextureHeight + 5 + 20 * row, 15, 15), (Event.current.modifiers & EventModifiers.Control) != 0 ? " -" : " +", GetStyle(new(0, 0, 0, 0))))
            {
                // Left click: Add the current colour as a new entry.
                if (Event.current.button == 0 && selectedColor.a != 0 && !BDTISetup.Instance.ColorPresets.Contains(selectedColor))
                {
                    BDTISetup.Instance.ColorPresets.Add(selectedColor);
                }
            }

            // box for chosen color
            GUI.Box(new Rect(HorizPos + displayTextureWidth + 10, VertPos + displayTextureHeight + 10, 30, 30), new GUIContent(""), GetStyle(selectedColor));
        }
        float updateTimer;

        void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (BDTISetup.Instance.UpdateTeamColor)
            {
                updateTimer -= Time.deltaTime;
                if (updateTimer < 0)
                {
                    updateTimer = 1f;    //next update in half a sec only

                    if (BDTISetup.Instance.ColorAssignments.ContainsKey(BDTISetup.Instance.selectedTeam))
                    {
                        BDTISetup.Instance.ColorAssignments[BDTISetup.Instance.selectedTeam] = selectedColor;
                    }
                    else
                    {
                        Debug.Log("[TEAMICONS] Selected team is null.");
                    }
                    BDTISetup.Instance.UpdateTeamColor = false;
                }
            }
        }
    }
}
