using UnityEngine;
using System;
using System.Collections;

using BDArmory.Settings;

namespace BDArmory.Utils
{
    public class BDInputUtils
    {
        public static string GetInputString()
        {
            //keyCodes
            string[] names = System.Enum.GetNames(typeof(KeyCode));
            int numberOfKeycodes = names.Length;

            for (int i = 0; i < numberOfKeycodes; i++)
            {
                string output = names[i];
                if (output.ToLower().StartsWith("mouse") || output.ToLower().StartsWith("joystick")) continue; // Handle mouse and joystick separately.

                if (output.Contains("Keypad"))
                {
                    output = "[" + output.Substring(6).ToLower() + "]";
                }
                else if (output.Contains("Alpha"))
                {
                    output = output.Substring(5);
                }
                else //lower case key
                {
                    output = output.ToLower();
                }

                //modifiers
                if (output.Contains("control"))
                {
                    output = output.Split('c')[0] + " ctrl";
                }
                else if (output.Contains("alt"))
                {
                    output = output.Split('a')[0] + " alt";
                }
                else if (output.Contains("shift"))
                {
                    output = output.Split('s')[0] + " shift";
                }
                else if (output.Contains("command"))
                {
                    output = output.Split('c')[0] + " cmd";
                }

                //special keys
                else if (output == "backslash")
                {
                    output = @"\";
                }
                else if (output == "backquote")
                {
                    output = "`";
                }
                else if (output == "[period]")
                {
                    output = "[.]";
                }
                else if (output == "[plus]")
                {
                    output = "[+]";
                }
                else if (output == "[multiply]")
                {
                    output = "[*]";
                }
                else if (output == "[divide]")
                {
                    output = "[/]";
                }
                else if (output == "[minus]")
                {
                    output = "[-]";
                }
                else if (output == "[enter]")
                {
                    output = "enter";
                }
                else if (output.Contains("page"))
                {
                    output = output.Insert(4, " ");
                }
                else if (output.Contains("arrow"))
                {
                    output = output.Split('a')[0];
                }
                else if (output == "capslock")
                {
                    output = "caps lock";
                }
                else if (output == "minus")
                {
                    output = "-";
                }

                //test if input is valid
                try
                {
                    if (Input.GetKey(output))
                    {
                        return output;
                    }
                }
                catch (System.Exception e)
                {
                    if (!e.Message.EndsWith("is unknown")) // Ignore unknown keys
                        Debug.LogWarning("[BDArmory.BDInputUtils]: Exception thrown in GetInputString: " + e.Message + "\n" + e.StackTrace);
                }
            }

            //mouse
            for (int m = 0; m < 6; m++)
            {
                string inputString = "mouse " + m;
                try
                {
                    if (Input.GetKey(inputString))
                    {
                        return inputString;
                    }
                }
                catch (UnityException e)
                {
                    Debug.Log("[BDArmory.BDInputUtils]: Invalid mouse: " + inputString);
                    Debug.LogWarning("[BDArmory.BDInputUtils]: Exception thrown in GetInputString: " + e.Message + "\n" + e.StackTrace);
                }
            }

            //joysticks
            for (int j = 1; j < 12; j++)
            {
                for (int b = 0; b < 20; b++)
                {
                    string inputString = "joystick " + j + " button " + b;
                    try
                    {
                        if (Input.GetKey(inputString))
                        {
                            return inputString;
                        }
                    }
                    catch (UnityException e)
                    {
                        Debug.LogWarning("[BDArmory.BDInputUtils]: Exception thrown in GetInputString: " + e.Message + "\n" + e.StackTrace);
                        return string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        public static bool GetKey(BDInputInfo input)
        {
            return !string.IsNullOrEmpty(input.inputString) && Input.GetKey(input.inputString);
        }

        public static bool GetKeyDown(BDInputInfo input)
        {
            return !string.IsNullOrEmpty(input.inputString) && Input.GetKeyDown(input.inputString);
        }
    }

    /// <summary>
    /// A class for more easily inputting numeric values in TextFields.
    /// There's a 0.5s delay after the last keystroke before attempting to interpret the string as a double.
    /// Explicit cast to lower precision types may be needed when assigning the current value.
    /// </summary>
    public class NumericInputField : MonoBehaviour
    {
        public NumericInputField Initialise(double l, double v, double minV = double.MinValue, double maxV = double.MaxValue) { lastUpdated = l; currentValue = v; minValue = minV; maxValue = maxV; return this; }
        public double lastUpdated;
        public string possibleValue = string.Empty;
        private double _value;
        public double currentValue { get { return _value; } set { _value = value; possibleValue = _value.ToString("G6"); } }
        public double minValue;
        public double maxValue;
        private bool coroutineRunning = false;
        private Coroutine coroutine;

        public void tryParseValue(string v)
        {
            if (v != possibleValue)
            {
                lastUpdated = !string.IsNullOrEmpty(v) ? Time.time : Time.time + 0.5; // Give the empty string an extra 0.5s.
                possibleValue = v;
                if (!coroutineRunning)
                {
                    coroutine = StartCoroutine(UpdateValueCoroutine());
                }
            }
        }

        IEnumerator UpdateValueCoroutine()
        {
            coroutineRunning = true;
            while (Time.time - lastUpdated < 0.5)
                yield return new WaitForFixedUpdate();
            tryParseCurrentValue();
            coroutineRunning = false;
            yield return new WaitForFixedUpdate();
        }

        void tryParseCurrentValue()
        {
            double newValue;
            if (double.TryParse(possibleValue, out newValue))
            {
                currentValue = Math.Min(Math.Max(newValue, minValue), Math.Max(maxValue, currentValue)); // Clamp the new value between the min and max, but not if it's been set higher with the unclamped tuning option. This still allows reducing the value while still above the clamp limit.
                lastUpdated = Time.time;
            }
            possibleValue = currentValue.ToString("G6");
        }

        // Parse the current possible value immediately.
        public void tryParseValueNow()
        {
            tryParseCurrentValue();
            if (coroutineRunning)
            {
                StopCoroutine(coroutine);
                coroutineRunning = false;
            }
        }
    }
}
