using System.Collections.Generic;
using System;
using UniLinq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;

namespace BDArmory.Utils
{
    public static class GUIUtils
    {
        public static Texture2D pixel;

        public static Camera GetMainCamera()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                return FlightCamera.fetch.mainCamera;
            }
            else
            {
                return Camera.main;
            }
        }

        public static void DrawTextureOnWorldPos(Vector3 worldPos, Texture texture, Vector2 size, float wobble)
        {
            var cam = GetMainCamera();
            if (cam == null) return;
            Vector3 screenPos = cam.WorldToViewportPoint(worldPos);
            if (screenPos.z < 0) return; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) return; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) return;
            float xPos = screenPos.x * Screen.width - (0.5f * size.x);
            float yPos = (1 - screenPos.y) * Screen.height - (0.5f * size.y);
            if (wobble > 0)
            {
                xPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
                yPos += UnityEngine.Random.Range(-wobble / 2, wobble / 2);
            }
            Rect iconRect = new Rect(xPos, yPos, size.x, size.y);

            GUI.DrawTexture(iconRect, texture);
        }

        public static bool WorldToGUIPos(Vector3 worldPos, out Vector2 guiPos)
        {
            var cam = GetMainCamera();
            if (cam == null)
            {
                guiPos = Vector2.zero;
                return false;
            }
            Vector3 screenPos = cam.WorldToViewportPoint(worldPos);
            bool offScreen = false;
            if (screenPos.z < 0) offScreen = true; //dont draw if point is behind camera
            if (screenPos.x != Mathf.Clamp01(screenPos.x)) offScreen = true; //dont draw if off screen
            if (screenPos.y != Mathf.Clamp01(screenPos.y)) offScreen = true;
            if (!offScreen)
            {
                float xPos = screenPos.x * Screen.width;
                float yPos = (1 - screenPos.y) * Screen.height;
                guiPos = new Vector2(xPos, yPos);
                return true;
            }
            else
            {
                guiPos = Vector2.zero;
                return false;
            }
        }

        public static void DrawLineBetweenWorldPositions(Vector3 worldPosA, Vector3 worldPosB, float width, Color color)
        {
            Camera cam = GetMainCamera();

            if (cam == null) return;

            GUI.matrix = Matrix4x4.identity;

            bool aBehind = false;

            Plane clipPlane = new Plane(cam.transform.forward, cam.transform.position + cam.transform.forward * 0.05f);

            if (Vector3.Dot(cam.transform.forward, worldPosA - cam.transform.position) < 0)
            {
                Ray ray = new Ray(worldPosB, worldPosA - worldPosB);
                float dist;
                if (clipPlane.Raycast(ray, out dist))
                {
                    worldPosA = ray.GetPoint(dist);
                }
                aBehind = true;
            }
            if (Vector3.Dot(cam.transform.forward, worldPosB - cam.transform.position) < 0)
            {
                if (aBehind) return;

                Ray ray = new Ray(worldPosA, worldPosB - worldPosA);
                float dist;
                if (clipPlane.Raycast(ray, out dist))
                {
                    worldPosB = ray.GetPoint(dist);
                }
            }

            Vector3 screenPosA = cam.WorldToViewportPoint(worldPosA);
            screenPosA.x = screenPosA.x * Screen.width;
            screenPosA.y = (1 - screenPosA.y) * Screen.height;
            Vector3 screenPosB = cam.WorldToViewportPoint(worldPosB);
            screenPosB.x = screenPosB.x * Screen.width;
            screenPosB.y = (1 - screenPosB.y) * Screen.height;

            screenPosA.z = screenPosB.z = 0;

            float angle = Vector2.Angle(Vector3.up, screenPosB - screenPosA);
            if (screenPosB.x < screenPosA.x)
            {
                angle = -angle;
            }

            Vector2 vector = screenPosB - screenPosA;
            float length = vector.magnitude;

            Rect upRect = new Rect(screenPosA.x - (width / 2), screenPosA.y - length, width, length);

            GUIUtility.RotateAroundPivot(-angle + 180, screenPosA);
            DrawRectangle(upRect, color);
            GUI.matrix = Matrix4x4.identity;
        }

        public static void DrawRectangle(Rect rect, Color color)
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
            }

            Color originalColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = originalColor;
        }

        public static void MarkPosition(Transform transform, Color color) => MarkPosition(transform.position, transform, color);

        public static void MarkPosition(Vector3 position, Transform transform, Color color, float size = 3, float thickness = 2)
        {
            DrawLineBetweenWorldPositions(position + transform.right * size, position - transform.right * size, thickness, color);
            DrawLineBetweenWorldPositions(position + transform.up * size, position - transform.up * size, thickness, color);
            DrawLineBetweenWorldPositions(position + transform.forward * size, position - transform.forward * size, thickness, color);
        }

        public static void UseMouseEventInRect(Rect rect)
        {
            if (Event.current == null) return;
            if (GUIUtils.MouseIsInRect(rect) && Event.current.isMouse && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp))
            {
                Event.current.Use();
            }
        }

        public static Rect CleanRectVals(Rect rect)
        {
            // Remove decimal places so Mac does not complain.
            rect.x = (int)rect.x;
            rect.y = (int)rect.y;
            rect.width = (int)rect.width;
            rect.height = (int)rect.height;
            return rect;
        }

        internal static void RepositionWindow(ref Rect windowPosition)
        {
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES)
            {
                // This method uses Gui point system.
                if (windowPosition.x < 0) windowPosition.x = 0;
                if (windowPosition.y < 0) windowPosition.y = 0;

                if (windowPosition.xMax > Screen.width) // Don't go off the right of the screen.
                    windowPosition.x = Screen.width - windowPosition.width;
                if (windowPosition.height > Screen.height) // Don't go off the top of the screen.
                    windowPosition.y = 0;
                else if (windowPosition.yMax > Screen.height) // Don't go off the bottom of the screen.
                    windowPosition.y = Screen.height - windowPosition.height;
            }
            else // If the window is completely off-screen, bring it just onto the screen.
            {
                if (windowPosition.width == 0) windowPosition.width = 1;
                if (windowPosition.height == 0) windowPosition.height = 1;
                if (windowPosition.x >= Screen.width) windowPosition.x = Screen.width - 1;
                if (windowPosition.y >= Screen.height) windowPosition.y = Screen.height - 1;
                if (windowPosition.x + windowPosition.width < 1) windowPosition.x = 1 - windowPosition.width;
                if (windowPosition.y + windowPosition.height < 1) windowPosition.y = 1 - windowPosition.height;
            }
        }

        internal static Rect GuiToScreenRect(Rect rect)
        {
            // Must run during OnGui to work...
            Rect newRect = new Rect
            {
                position = GUIUtility.GUIToScreenPoint(rect.position),
                width = rect.width,
                height = rect.height
            };
            return newRect;
        }

        public static Texture2D resizeTexture
        {
            get
            {
                if (_resizeTexture == null) _resizeTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "resizeSquare", false);
                return _resizeTexture;
            }
        }
        static Texture2D _resizeTexture;

        public static Color ParseColor255(string color)
        {
            Color outputColor = new Color(0, 0, 0, 1);

            string[] strings = color.Split(","[0]);
            for (int i = 0; i < 4; i++)
            {
                outputColor[i] = Single.Parse(strings[i]) / 255;
            }

            return outputColor;
        }

        public static AnimationState[] SetUpAnimation(string animationName, Part part) //Thanks Majiir!
        {
            List<AnimationState> states = new List<AnimationState>();
            using (IEnumerator<UnityEngine.Animation> animation = part.FindModelAnimators(animationName).AsEnumerable().GetEnumerator())
                while (animation.MoveNext())
                {
                    if (animation.Current == null) continue;
                    AnimationState animationState = animation.Current[animationName];
                    animationState.speed = 0; // FIXME Shouldn't this be 1?
                    animationState.enabled = true;
                    animationState.wrapMode = WrapMode.ClampForever;
                    animation.Current.Blend(animationName);
                    states.Add(animationState);
                }
            return states.ToArray();
        }

        public static AnimationState SetUpSingleAnimation(string animationName, Part part)
        {
            using (IEnumerator<UnityEngine.Animation> animation = part.FindModelAnimators(animationName).AsEnumerable().GetEnumerator())
                while (animation.MoveNext())
                {
                    if (animation.Current == null) continue;
                    AnimationState animationState = animation.Current[animationName];
                    animationState.speed = 0; // FIXME Shouldn't this be 1?
                    animationState.enabled = true;
                    animationState.wrapMode = WrapMode.ClampForever;
                    animation.Current.Blend(animationName);
                    return animationState;
                }
            return null;
        }

        public static bool CheckMouseIsOnGui()
        {
            if (!BDArmorySetup.GAME_UI_ENABLED) return false;

            if (!BDInputSettingsFields.WEAP_FIRE_KEY.inputString.Contains("mouse")) return false;

            Vector3 inverseMousePos = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0);
            Rect topGui = new Rect(0, 0, Screen.width, 65);

            if (topGui.Contains(inverseMousePos)) return true;
            if (BDArmorySetup.windowBDAToolBarEnabled && BDArmorySetup.WindowRectToolbar.Contains(inverseMousePos))
                return true;
            if (ModuleTargetingCamera.windowIsOpen && BDArmorySetup.WindowRectTargetingCam.Contains(inverseMousePos))
                return true;
            if (BDArmorySetup.Instance.ActiveWeaponManager)
            {
                MissileFire wm = BDArmorySetup.Instance.ActiveWeaponManager;

                if (wm.vesselRadarData && wm.vesselRadarData.guiEnabled)
                {
                    if (BDArmorySetup.WindowRectRadar.Contains(inverseMousePos)) return true;
                    if (wm.vesselRadarData.linkWindowOpen && wm.vesselRadarData.linkWindowRect.Contains(inverseMousePos))
                        return true;
                }
                if (wm.rwr && wm.rwr.rwrEnabled && wm.rwr.displayRWR && BDArmorySetup.WindowRectRwr.Contains(inverseMousePos))
                    return true;
                if (wm.wingCommander && wm.wingCommander.showGUI)
                {
                    if (BDArmorySetup.WindowRectWingCommander.Contains(inverseMousePos)) return true;
                    if (wm.wingCommander.showAGWindow && wm.wingCommander.agWindowRect.Contains(inverseMousePos))
                        return true;
                }

                if (extraGUIRects != null)
                {
                    for (int i = 0; i < extraGUIRects.Count; i++)
                    {
                        if (extraGUIRects[i].Contains(inverseMousePos)) return true;
                    }
                }
            }

            return false;
        }

        public static void ResizeGuiWindow(Rect windowrect, Vector2 mousePos)
        {
        }

        public static List<Rect> extraGUIRects;

        public static int RegisterGUIRect(Rect rect)
        {
            if (extraGUIRects == null)
            {
                extraGUIRects = new List<Rect>();
            }

            int index = extraGUIRects.Count;
            extraGUIRects.Add(rect);
            return index;
        }

        public static void UpdateGUIRect(Rect rect, int index)
        {
            if (extraGUIRects == null)
            {
                Debug.LogWarning("[BDArmory.Misc]: Trying to update a GUI rect for mouse position check, but Rect list is null.");
            }

            extraGUIRects[index] = rect;
        }

        public static bool MouseIsInRect(Rect rect)
        {
            Vector3 inverseMousePos = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0);
            return rect.Contains(inverseMousePos);
        }

        //Thanks FlowerChild
        //refreshes part action window
        public static void RefreshAssociatedWindows(Part part)
        {
            if (part == null || part.PartActionWindow == null) return;
            part.PartActionWindow.UpdateWindow();
            // part.PartActionWindow.displayDirty = true;
            // IEnumerator<UIPartActionWindow> window = Object.FindObjectsOfType(typeof(UIPartActionWindow)).Cast<UIPartActionWindow>().GetEnumerator();
            // while (window.MoveNext())
            // {
            //     if (window.Current == null) continue;
            //     if (window.Current.part == part)
            //     {
            //         window.Current.displayDirty = true;
            //     }
            // }
            // window.Dispose();
        }

    }
}
