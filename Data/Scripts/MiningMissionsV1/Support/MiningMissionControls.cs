using System;
using System.Collections.Generic;

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
      minerSelect.Tooltip = MyStringId.GetOrCompute("Select a miner profile.");
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
      items.Add(new MyTerminalControlComboBoxItem { Key = 0, Value = MyStringId.GetOrCompute("Miner 1") });
      items.Add(new MyTerminalControlComboBoxItem { Key = 1, Value = MyStringId.GetOrCompute("Miner 2") });
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
    }

  }
}
