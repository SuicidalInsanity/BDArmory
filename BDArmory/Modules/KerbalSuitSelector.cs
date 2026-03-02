using System;
using BDArmory.Settings;
using UnityEngine;

namespace BDArmory.Modules
{
  /// <summary>
  /// This allows setting the suit worn by EVA kerbals if spawned via BDArmory or via going EVA from a part.
  /// EVA kerbals can't have their suits changed once spawned.
  /// </summary>
  public class KerbalSuitSelector : PartModule
  {
    /// <summary>
    /// Same as ProtoCrewMember.KerbalSuit, but with an extra "Random" option.
    /// </summary>
    public enum KerbalSuit
    {
      Default = 0,
      Vintage = 1,
      Future = 2,
      Slim = 3,
      Random = 4
    }

    [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Kerbal Suit Type"),
        UI_ChooseOption(options = new string[5] { "Default", "Vintage", "Future", "Slim", "Random" })]
    public string suit;

    public ProtoCrewMember.KerbalSuit Suit
    {
      get;
      set
      {
        field = value;
        foreach (var crew in part.protoModuleCrew)
          crew.suit = value; // Update existing proto-crew on the part.
      }
    }

    /// <summary>
    /// This is called during loading of the shipConstruct during spawning AND after the vessel has spawned!
    /// Also on loading parts in the SPH, but not for new parts.
    /// </summary>
    /// <param name="node"></param>
    public override void OnLoad(ConfigNode node)
    {
      base.OnLoad(node);
      if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) return;
      if (!CheckValidPart())
        part.RemoveModule(this);
      if (string.IsNullOrEmpty(suit))
      {
        suit = ((KerbalSuit)Mathf.Clamp(BDArmorySettings.VESSEL_SPAWN_DEFAULT_KERBAL_SUIT, 0, 4)).ToString();
      }
      if (HighLogic.LoadedSceneIsFlight && part.FindModuleImplementing<KerbalSeat>() != null)
      {
        Fields[nameof(suit)].guiActive = false; // The seat's suit type can't be changed in flight.
      }
      else
      {
        SetOnSuitChanged();
      }
      OnSuitChanged();
    }
    
    /// <summary>
    /// This is called for new and loaded parts in the SPH, but too late for spawning.
    /// </summary>
    public override void OnAwake()
    {
      base.OnAwake();
      if (!HighLogic.LoadedSceneIsEditor) return;
      if (!CheckValidPart())
        part.RemoveModule(this);
      if (string.IsNullOrEmpty(suit))
      {
        suit = ((KerbalSuit)Mathf.Clamp(BDArmorySettings.VESSEL_SPAWN_DEFAULT_KERBAL_SUIT, 0, 4)).ToString();
      }
    }

    bool CheckValidPart()
    {
      if (part.FindModuleImplementing<KerbalSeat>() != null) return true;
      var command = part.FindModuleImplementing<ModuleCommand>();
      if (command != null && command.minimumCrew >= 1) return true;
      return false;
    }

    void SetOnSuitChanged()
    {
      (
        HighLogic.LoadedSceneIsEditor ?
          (UI_ChooseOption)Fields[nameof(suit)].uiControlEditor :
          (UI_ChooseOption)Fields[nameof(suit)].uiControlFlight
      ).onFieldChanged = OnSuitChanged;
    }

    void OnSuitChanged(BaseField field = null, object obj = null)
    {
      var suitType = (KerbalSuit)Enum.Parse(typeof(KerbalSuit), suit);
      Suit = Enum.IsDefined(typeof(ProtoCrewMember.KerbalSuit), (ProtoCrewMember.KerbalSuit)suitType) ?
        (ProtoCrewMember.KerbalSuit)suitType :
        (ProtoCrewMember.KerbalSuit)UnityEngine.Random.Range(0, 4);
    }
  }
}
