using UnityEngine;
using System.Reflection;

using BDArmory.Settings;

namespace BDArmory.Utils
{
    public static class OtherUtils // FIXME Suggestions for a better name?
    {
        /// <summary>
        /// Parses the string to a curve.
        /// Format: "key:pair,key:pair"
        /// </summary>
        /// <returns>The curve.</returns>
        /// <param name="curveString">Curve string.</param>
        public static FloatCurve ParseCurve(string curveString)
        {
            string[] pairs = curveString.Split(new char[] { ',' });
            Keyframe[] keys = new Keyframe[pairs.Length];
            for (int p = 0; p < pairs.Length; p++)
            {
                string[] pair = pairs[p].Split(new char[] { ':' });
                keys[p] = new Keyframe(float.Parse(pair[0]), float.Parse(pair[1]));
            }

            FloatCurve curve = new FloatCurve(keys);

            return curve;
        }

        private static int lineOfSightLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23);
        public static bool CheckSightLine(Vector3 origin, Vector3 target, float maxDistance, float threshold,
            float startDistance)
        {
            float dist = maxDistance;
            Ray ray = new Ray(origin, target - origin);
            ray.origin += ray.direction * startDistance;
            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit, dist, lineOfSightLayerMask))
            {
                if ((target - rayHit.point).sqrMagnitude < threshold * threshold)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        public static bool CheckSightLineExactDistance(Vector3 origin, Vector3 target, float maxDistance,
            float threshold, float startDistance)
        {
            float dist = maxDistance;
            Ray ray = new Ray(origin, target - origin);
            ray.origin += ray.direction * startDistance;
            RaycastHit rayHit;

            if (Physics.Raycast(ray, out rayHit, dist, lineOfSightLayerMask))
            {
                if ((target - rayHit.point).sqrMagnitude < threshold * threshold)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public static float[] ParseToFloatArray(string floatString)
        {
            string[] floatStrings = floatString.Split(new char[] { ',' });
            float[] floatArray = new float[floatStrings.Length];
            for (int i = 0; i < floatStrings.Length; i++)
            {
                floatArray[i] = float.Parse(floatStrings[i]);
            }

            return floatArray;
        }

        public static KeyBinding AGEnumToKeybinding(KSPActionGroup group)
        {
            string groupName = group.ToString();
            if (groupName.Contains("Custom"))
            {
                groupName = groupName.Substring(6);
                int customNumber = int.Parse(groupName);
                groupName = "CustomActionGroup" + customNumber;
            }
            else
            {
                return null;
            }

            FieldInfo field = typeof(GameSettings).GetField(groupName);
            return (KeyBinding)field.GetValue(null);
        }

        public static string JsonCompat(string json)
        {
            return json.Replace('{', '<').Replace('}', '>');
        }

        public static string JsonDecompat(string json)
        {
            return json.Replace('<', '{').Replace('>', '}');
        }

        public static void SetTimeOverride(bool enabled)
        {
            BDArmorySettings.TIME_OVERRIDE = enabled;
            Time.timeScale = enabled ? BDArmorySettings.TIME_SCALE : 1f;
        }
    }
}