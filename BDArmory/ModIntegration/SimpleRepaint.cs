using System;
using System.Reflection;
using BDArmory.Settings;
using UnityEngine;

namespace BDArmory.ModIntegration
{
  [KSPAddon(KSPAddon.Startup.FlightAndEditor, true)]
  public class SimpleRepaint : MonoBehaviour
  {
    /// <summary>
    /// Check if all the modules of Type moduleType are due to SimpleRepaint.
    /// Note: SimpleRepaint converts parts to variant parts just for repainting them without any structural changes.
    /// </summary>
    /// <param name="part"></param>
    /// <param name="moduleType"></param>
    /// <returns></returns>
    public static bool CheckForSimpleRepaint(Part part, Type moduleType)
    {
      bool allModulesAreDueToSimpleRepaint = true;
      foreach (var module in part.Modules)
      {
        if (module.GetType() != moduleType) continue;
        // SimpleRepaint adds a B9PS or ModulePartVariants module with "moduleID = SimpleRepaint".
        var field = moduleType.GetField("moduleID", BindingFlags.Public | BindingFlags.Instance);
        if (field == null) { allModulesAreDueToSimpleRepaint = false; break; }
        if ((string)field.GetValue(module) != "SimpleRepaint") { allModulesAreDueToSimpleRepaint = false; break; }
      }
      if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.SimpleRepaint]: {part.name} has a module of type {moduleType} that is {(allModulesAreDueToSimpleRepaint?"only":"not")} due to SimpleRepaint.");
      return allModulesAreDueToSimpleRepaint;
    }
  }
}
