using System;
using System.Collections.Generic;
using BDArmory.Settings;
using UnityEngine;

namespace BDArmory.Utils
{
  /// <summary>
  /// A basic PID based on https://en.wikipedia.org/wiki/Proportional%E2%80%93integral%E2%80%93derivative_controller#Pseudocode
  /// </summary>
  [Serializable]
  public class PID
  {
    public float P, I, D;
    public float TimeStep = 0.02f, ILimit = 0, SetPoint = 0;
    public float Value { get; private set; } = 0;

    public bool smoothInput = false;
    public float smoothingHalflife = 0.1f;
    SmoothingF inputSmoothing = null;

    public float IntegralRescaling = 1f; // Magnify the rate of change of the integral. This is useful for having a larger ILimit

    [NonSerialized] public bool debug = false;
    [NonSerialized] public string DebugString;

    float integral = 0;
    float previousError = float.NaN;

    public float Update(float measurement)
    {
      if (smoothInput)
      {
        if (inputSmoothing == null) inputSmoothing = new(Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime / smoothingHalflife), measurement, TimeStep);
        inputSmoothing.Update(measurement);
        measurement = inputSmoothing.Value;
      }
      float error = SetPoint - measurement;
      float proportional = error;
      integral += error * TimeStep * IntegralRescaling;
      if (ILimit > 0) integral = Mathf.Clamp(integral, -ILimit, ILimit);
      float derivative = float.IsNaN(previousError) ? 0 : (error - previousError) / TimeStep;
      float p = P * proportional;
      float i = I * integral;
      float d = D * derivative;
      if (debug) DebugString = $"P:{p:0.00}, I:{i:0.00}, D:{d:0.00}";
      Value = p + i + d;
      previousError = error;
      return Value;
    }

    public void Reset(float value)
    {
      Value = value;
      integral = 0;
      previousError = float.NaN;
      inputSmoothing = null;
      DebugString = null;
    }

    // Call this in response to GameEvents.onAboutToSaveShip and store the string in a [KSPField(isPersistant = true, guiActive = false)] field.
    public static string Serialize(PID pid)
    {
      return JsonUtility.ToJson(pid).Trim(['{', '}']); // KSPField serialises braces, but doesn't deserialise them.
    }
    public string Serialize() => Serialize(this);

    // Call this during Start on the stored field.
    public static PID Deserialize(string config, Dictionary<string, string> overrides = null)
    {
      if (string.IsNullOrEmpty(config)) return null;
      try
      {
        var pid = JsonUtility.FromJson<PID>($"{{{config}}}");
        if (overrides != null)
        {
          foreach (var kvp in overrides)
          {
            var field = typeof(PID).GetField(kvp.Key);
            if (field != null)
            {
              object value = BDAPersistentSettingsField.ParseValue(field.FieldType, kvp.Value, field.Name);
              if (value != null)
              {
                field.SetValue(pid, value);
              }
            }
            else
            {
              Debug.LogWarning($"Override {kvp.Key} doesn't correspond with a known field of the PID class.");
            }
          }
        }
        return pid;
      }
      catch (Exception e)
      {
        Debug.LogError($"Failed to deserialize PID: {e.Message}");
        return null;
      }
    }
  }
}