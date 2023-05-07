using UnityEngine;

namespace BDArmory.Utils
{
    /// <summary>
    /// Brown's double exponential smoothing (for constant dt between samples, i.e., Holt linear).
    /// float version.
    /// </summary>
    public class SmoothingF // .Net 7 will allow using T where: System.IMultiplyOperators, System.IAdditionOperators, System.ISubtractionOperators
    {
        float S1;
        float S2;
        float alpha;
        float beta;
        float rate;
        public float Value => 2f * S1 - S2; // The value at the current time.

        /// <summary>
        /// Constructor for Brown's double exponential smoothing.
        /// </summary>
        /// <param name="beta">Smoothing factor.</param>
        /// <param name="initialValue">The initial value.</param>
        /// <param name="rate">The update frequency (for scaling delta in At).</param>
        public SmoothingF(float beta, float initialValue = 0, float rate = 0)
        {
            this.alpha = 1f - beta;
            this.beta = beta;
            this.rate = rate > 0 ? rate : Time.fixedDeltaTime;
            Reset(initialValue);
        }

        public void Update(float value)
        {
            S1 = alpha * value + beta * S1;
            S2 = alpha * S1 + beta * S2;
        }

        public void Reset(float initialValue = 0)
        {
            S1 = initialValue;
            S2 = initialValue;
        }

        /// <summary>
        /// Estimate the value at a time later than the most recent update.
        /// </summary>
        /// <param name="delta">The time difference from now to estimate the value at.</param>
        /// <returns>The estimated value.</returns>
        public float At(float delta)
        {
            var a = 2f * S1 - S2;
            var b = alpha / beta * (S1 - S2);
            return a + delta / rate * b;
        }
    }

    /// <summary>
    /// Brown's double exponential smoothing (for constant dt between samples, i.e., Holt linear).
    /// Vector3 version.
    /// </summary>
    public class SmoothingV3 // .Net 7 allows T where: System.IMultiplyOperators, System.IAdditionOperators  // Note: we may need to just make multiple versions for float and Vector3 until .Net 7.
    {
        Vector3 S1;
        Vector3 S2;
        float alpha;
        float beta;
        float rate;
        public Vector3 Value => 2f * S1 - S2; // The value at the current time.

        /// <summary>
        /// Constructor for Brown's double exponential smoothing.
        /// </summary>
        /// <param name="beta">Smoothing factor.</param>
        public SmoothingV3(float beta, Vector3 initialValue = default, float rate = 0)
        {
            this.alpha = 1f - beta;
            this.beta = beta;
            this.rate = rate > 0 ? rate : Time.fixedDeltaTime;
            Reset(initialValue);
        }

        public void Update(Vector3 value)
        {
            S1 = alpha * value + beta * S1;
            S2 = alpha * S1 + beta * S2;
        }

        public void Reset(Vector3 initialValue = default)
        {
            S1 = initialValue;
            S2 = initialValue;
        }

        /// <summary>
        /// Estimate the value at a time later than the most recent update.
        /// </summary>
        /// <param name="delta">The time difference from now to estimate the value at.</param>
        /// <returns>The estimated value.</returns>
        public Vector3 At(float delta)
        {
            var a = 2f * S1 - S2;
            var b = alpha / beta * (S1 - S2);
            return a + delta / rate * b;
        }
    }
}
