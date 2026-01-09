using System;
using System.Collections.Generic;
using System.Text;

using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

using MiningMissionsV1.Session;

using VRage.Game.ModAPI;
using VRage.Utils;

namespace MiningMissionsV1.Support
{
  public static class MiningMissionControls
  {
    private const string SorterSubtype = "MiningMissionSorter";
    private const string SorterSubtypeSmall = "MiningMissionSorterSmall";
    private static bool _controlsCreated;
    private static readonly Dictionary<long, long> MinerSelections = new Dictionary<long, long>();
    private static readonly List<PilotProfile> Pilots = new List<PilotProfile>
    {
      new PilotProfile(0, "Doug",     skill: 1, reliability: 3, yield: 1, speed: 3),
      new PilotProfile(1, "Dylan",    skill: 1, reliability: 1, yield: 2, speed: 5),
      new PilotProfile(0, "Marcus",   skill: 1, reliability: 5, yield: 3, speed: 2),
      new PilotProfile(1, "Max",      skill: 2, reliability: 3, yield: 2, speed: 2),
      new PilotProfile(2, "Rasmus",   skill: 2, reliability: 2, yield: 3, speed: 3),
      new PilotProfile(3, "Carl",     skill: 3, reliability: 5, yield: 4, speed: 2),
      new PilotProfile(4, "Nelson",   skill: 3, reliability: 3, yield: 3, speed: 4),
      new PilotProfile(5, "Antilles", skill: 4, reliability: 3, yield: 3, speed: 3),
      new PilotProfile(6, "Billy",    skill: 4, reliability: 5, yield: 4, speed: 1),
      new PilotProfile(7, "Gaius",    skill: 5, reliability: 4, yield: 4, speed: 3),
      new PilotProfile(8, "Jackal",   skill: 5, reliability: 2, yield: 3, speed: 5),
      new PilotProfile(9, "Singer",   skill: 5, reliability: 5, yield: 5, speed: 1),
    };

    internal static void EnsureControls()
    {
      if (_controlsCreated)
        return;

      _controlsCreated = true;

      var controlsToHide = new HashSet<string>
      {
        "OnOff",
        "DrainAll",
        "blacklistWhitelist",
        "CurrentList",
        "removeFromSelectionButton",
        "candidatesList",
        "addToSelectionButton"
      };

      List<IMyTerminalControl> controls;
      MyAPIGateway.TerminalControls.GetControls<IMyConveyorSorter>(out controls);

      for (int i = 0; i < controls.Count; i++)
      {
        var ctrl = controls[i];
        if (!controlsToHide.Contains(ctrl.Id))
          continue;

        ctrl.Enabled = Combine(ctrl.Enabled, b => !IsMiningMissionSorter(b));
        ctrl.Visible = Combine(ctrl.Visible, b => !IsMiningMissionSorter(b));
      }

      var powerToggle = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyConveyorSorter>("MmOnOff");
      powerToggle.Title = MyStringId.GetOrCompute("Block Power");
      powerToggle.Tooltip = MyStringId.GetOrCompute("Enable or disable this block.");
      powerToggle.OnText = MyStringId.GetOrCompute("On");
      powerToggle.OffText = MyStringId.GetOrCompute("Off");
      powerToggle.SupportsMultipleBlocks = false;
      powerToggle.Enabled = Combine(powerToggle.Enabled, IsMiningMissionSorter);
      powerToggle.Visible = Combine(powerToggle.Visible, IsMiningMissionSorter);
      powerToggle.Getter = GetBlockEnabled;
      powerToggle.Setter = SetBlockEnabled;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(powerToggle);

      var minerSelect = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyConveyorSorter>("MmMinerSelect");
      minerSelect.Title = MyStringId.GetOrCompute("Miner");
      minerSelect.Tooltip = MyStringId.GetOrCompute(BuildPilotTooltip());
      minerSelect.SupportsMultipleBlocks = false;
      minerSelect.Enabled = Combine(minerSelect.Enabled, IsMiningMissionSorter);
      minerSelect.Visible = Combine(minerSelect.Visible, IsMiningMissionSorter);
      minerSelect.ComboBoxContent = MinerComboContent;
      minerSelect.Getter = GetMinerSelection;
      minerSelect.Setter = SetMinerSelection;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(minerSelect);

      var startMission = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("MmStartMission");
      startMission.Title = MyStringId.GetOrCompute("Start Mining Mission");
      startMission.Tooltip = MyStringId.GetOrCompute("Stores the grid, runs a 10s mission, then returns with ore.");
      startMission.SupportsMultipleBlocks = false;
      startMission.Enabled = Combine(startMission.Enabled, IsMiningMissionSorter);
      startMission.Visible = Combine(startMission.Visible, IsMiningMissionSorter);
      startMission.Action = block => MiningMissionSession.Instance?.TryStartMission(block);
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(startMission);

      var applyMiner = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyConveyorSorter>("MmApplyMiner");
      applyMiner.Title = MyStringId.GetOrCompute("Apply Miner");
      applyMiner.Tooltip = MyStringId.GetOrCompute("Apply the selected miner and refresh the info panel.");
      applyMiner.SupportsMultipleBlocks = false;
      applyMiner.Enabled = Combine(applyMiner.Enabled, IsMiningMissionSorter);
      applyMiner.Visible = Combine(applyMiner.Visible, IsMiningMissionSorter);
      applyMiner.Action = block =>
      {
        if (block == null)
          return;

        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
        {
          block.SetDetailedInfoDirty();
          block.RefreshCustomInfo();
        });
      };
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(applyMiner);

    }

    private static bool IsMiningMissionSorter(IMyTerminalBlock block)
    {
      if (block == null)
        return false;

      var defString = block.BlockDefinition.ToString();
      if (string.IsNullOrEmpty(defString))
        return false;

      return EndsWithSubtype(defString, SorterSubtype) || EndsWithSubtype(defString, SorterSubtypeSmall);
    }

    private static bool EndsWithSubtype(string defString, string subtype)
    {
      return defString.EndsWith("/" + subtype, StringComparison.OrdinalIgnoreCase)
        || defString.EndsWith("\\" + subtype, StringComparison.OrdinalIgnoreCase)
        || string.Equals(defString, subtype, StringComparison.OrdinalIgnoreCase);
    }

    private static Func<IMyTerminalBlock, bool> Combine(Func<IMyTerminalBlock, bool> first, Func<IMyTerminalBlock, bool> second)
    {
      if (first == null)
        return second;

      if (second == null)
        return first;

      return b => first(b) && second(b);
    }

    private static bool GetBlockEnabled(IMyTerminalBlock block)
    {
      var functional = block as IMyFunctionalBlock;
      return functional?.Enabled ?? true;
    }

    private static void SetBlockEnabled(IMyTerminalBlock block, bool enabled)
    {
      var functional = block as IMyFunctionalBlock;
      if (functional != null)
        functional.Enabled = enabled;
    }

    private static void MinerComboContent(List<MyTerminalControlComboBoxItem> items)
    {
      for (int i = 0; i < Pilots.Count; i++)
      {
        var pilot = Pilots[i];
        items.Add(new MyTerminalControlComboBoxItem { Key = pilot.Key, Value = MyStringId.GetOrCompute(pilot.Name) });
      }
    }

    private static string BuildPilotTooltip()
    {
      var sb = new StringBuilder();
      sb.AppendLine("Select a miner profile.");
      for (int i = 0; i < Pilots.Count; i++)
      {
        var pilot = Pilots[i];
        sb.AppendLine($"{pilot.Name} - Skill {pilot.Skill}, Reliability {pilot.Reliability}, Yield {pilot.Yield}, Speed {pilot.Speed}");
      }

      return sb.ToString();
    }

    private static long GetMinerSelection(IMyTerminalBlock block)
    {
      if (block == null)
        return 0;

      long value;
      return MinerSelections.TryGetValue(block.EntityId, out value) ? value : 0;
    }

    private static void SetMinerSelection(IMyTerminalBlock block, long value)
    {
      if (block == null)
        return;

      MinerSelections[block.EntityId] = value;
      MyAPIGateway.Utilities.InvokeOnGameThread(() =>
      {
        block.SetDetailedInfoDirty();
        block.RefreshCustomInfo();
      });
    }

    public static PilotProfile GetSelectedPilot(IMyTerminalBlock block)
    {
      if (block == null || Pilots.Count == 0)
        return null;

      var key = GetMinerSelection(block);
      for (int i = 0; i < Pilots.Count; i++)
      {
        if (Pilots[i].Key == key)
          return Pilots[i];
      }

      return Pilots[0];
    }

    public static long GetSelectedPilotKey(IMyTerminalBlock block)
    {
      if (block == null)
        return 0;

      return GetMinerSelection(block);
    }

    public class PilotProfile
    {
      public readonly long Key;
      public readonly string Name;
      public readonly int Skill;
      public readonly int Reliability;
      public readonly int Yield;
      public readonly int Speed;

      public PilotProfile(long key, string name, int skill, int reliability, int yield, int speed)
      {
        Key = key;
        Name = name;
        Skill = skill;
        Reliability = reliability;
        Yield = yield;
        Speed = speed;
      }
    }

  }
}
