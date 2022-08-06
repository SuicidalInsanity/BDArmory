using UnityEngine;

namespace BDArmory.Utils
{
    public static class BDAMath
    {
        public static float RangedProbability(float[] probs)
        {
            float total = 0;
            foreach (float elem in probs)
            {
                total += elem;
            }

            float randomPoint = UnityEngine.Random.value * total;

            for (int i = 0; i < probs.Length; i++)
            {
                if (randomPoint < probs[i])
                {
                    return i;
                }
                else
                {
                    randomPoint -= probs[i];
                }
            }
            return probs.Length - 1;
        }

        public static bool Between(this float num, float lower, float upper, bool inclusive = true)
        {
            return inclusive
                ? lower <= num && num <= upper
                : lower < num && num < upper;
        }

        public static Vector3 ProjectOnPlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
        {
            planeNormal = planeNormal.normalized;

            Plane plane = new Plane(planeNormal, planePoint);
            float distance = plane.GetDistanceToPoint(point);

            return point - (distance * planeNormal);
        }

        public static float SignedAngle(Vector3 fromDirection, Vector3 toDirection, Vector3 referenceRight)
        {
            float angle = Vector3.Angle(fromDirection, toDirection);
            float sign = Mathf.Sign(Vector3.Dot(toDirection, referenceRight));
            float finalAngle = sign * angle;
            return finalAngle;
        }

        public static float RoundToUnit(float value, float unit = 1f)
        {
            var rounded = Mathf.Round(value / unit) * unit;
            return (unit % 1 != 0) ? rounded : Mathf.Round(rounded); // Fix near-integer loss of precision.
        }

        // This is a fun workaround for M1-chip Macs (Apple Silicon). Specific issue the workaround is for is here: 
        // https://issuetracker.unity3d.com/issues/m1-incorrect-calculation-of-values-using-multiplication-with-mathf-dot-sqrt-when-an-unused-variable-is-declared
        public static float Sqrt(float value) => (float)System.Math.Sqrt((double)value);
    }
}
