using UnityEngine;

namespace BDArmory.Utils
{
  /// <summary>
  /// A basic PID based on https://en.wikipedia.org/wiki/Proportional%E2%80%93integral%E2%80%93derivative_controller#Pseudocode
  /// </summary>
  public class PID
  {
    public float P, I, D;
    public float TimeStep = 0.02f, ILimit = 0, SetPoint = 0;
    public float Value { get; private set; } = 0;

    public bool Debug = false;
    public string DebugString;

    float integral = 0;
    float previousError = float.NaN;

    public float Update(float measurement)
    {
      float error = SetPoint - measurement;
      float proportional = error;
      integral += error * TimeStep;
      if (ILimit > 0) integral = Mathf.Clamp(integral, -ILimit, ILimit);
      float derivative = float.IsNaN(previousError) ? 0 : (error - previousError) / TimeStep;
      float p = P * proportional;
      float i = I * integral;
      float d = D * derivative;
      if (Debug) DebugString = $"P:{p:0.00}, I:{i:0.00}, D:{d:0.00}";
      Value = p + i + d;
      previousError = error;
      return Value;
    }

    public void Reset(float value)
    {
      integral = 0;
      previousError = float.NaN;
      Value = value;
    }
  }
}