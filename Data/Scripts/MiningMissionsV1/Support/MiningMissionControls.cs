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
    private static readonly Dictionary<long, long> OreSelections = new Dictionary<long, long>();
    private static readonly Dictionary<long, float> MissionLengthScales = new Dictionary<long, float>();
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
    private static readonly List<OreOption> Ores = new List<OreOption>
    {
      new OreOption(0, "Stone", requiredSkill: 1),
      new OreOption(1, "Iron", requiredSkill: 1),
      new OreOption(2, "Nickel", requiredSkill: 1),
      new OreOption(3, "Silicon", requiredSkill: 1),
      new OreOption(4, "Ice", requiredSkill: 1),
      new OreOption(5, "Cobalt", requiredSkill: 2),
      new OreOption(6, "Magnesium", requiredSkill: 3),
      new OreOption(7, "Silver", requiredSkill: 4),
      new OreOption(8, "Gold", requiredSkill: 4),
      new OreOption(9, "Platinum", requiredSkill: 5),
      new OreOption(10, "Uranium", requiredSkill: 5)
    };
    private static int _lastUiPilotSkill = 1;
    private static IMyTerminalControlCombobox _oreSelectControl;
    private static IMyTerminalControlSlider _missionLengthControl;

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

      _oreSelectControl = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyConveyorSorter>("MmOreSelect");
      _oreSelectControl.Title = MyStringId.GetOrCompute("Ore");
      _oreSelectControl.Tooltip = MyStringId.GetOrCompute("Select the ore to mine.");
      _oreSelectControl.SupportsMultipleBlocks = false;
      _oreSelectControl.Enabled = Combine(_oreSelectControl.Enabled, IsMiningMissionSorter);
      _oreSelectControl.Visible = Combine(_oreSelectControl.Visible, IsMiningMissionSorter);
      _oreSelectControl.ComboBoxContent = OreComboContent;
      _oreSelectControl.Getter = GetOreSelection;
      _oreSelectControl.Setter = SetOreSelection;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(_oreSelectControl);

      _missionLengthControl = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyConveyorSorter>("MmMissionLength");
      _missionLengthControl.Title = MyStringId.GetOrCompute("Mission Length");
      _missionLengthControl.Tooltip = MyStringId.GetOrCompute("Scales mission time and yield.");
      _missionLengthControl.SupportsMultipleBlocks = false;
      _missionLengthControl.Enabled = Combine(_missionLengthControl.Enabled, IsMiningMissionSorter);
      _missionLengthControl.Visible = Combine(_missionLengthControl.Visible, IsMiningMissionSorter);
      _missionLengthControl.SetLimits(0.5f, 2.0f);
      _missionLengthControl.Writer = (block, sb) =>
      {
        var value = GetMissionLengthScale(block);
        sb.Append($"{value:0.00}x");
      };
      _missionLengthControl.Getter = GetMissionLengthScale;
      _missionLengthControl.Setter = SetMissionLengthScale;
      MyAPIGateway.TerminalControls.AddControl<IMyConveyorSorter>(_missionLengthControl);

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
        items.Add(new MyTerminalControlComboBoxItem { Key = i, Value = MyStringId.GetOrCompute(pilot.Name) });
      }
    }

    private static void OreComboContent(List<MyTerminalControlComboBoxItem> items)
    {
      var skill = _lastUiPilotSkill;
      for (int i = 0; i < Ores.Count; i++)
      {
        var ore = Ores[i];
        if (ore.RequiredSkill > skill)
          continue;

        items.Add(new MyTerminalControlComboBoxItem { Key = ore.Key, Value = MyStringId.GetOrCompute(ore.Name) });
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

      var value = GetMinerSelectionRaw(block);
      UpdateLastUiPilotSkill(value);
      return value;
    }

    private static void SetMinerSelection(IMyTerminalBlock block, long value)
    {
      if (block == null)
        return;

      MinerSelections[block.EntityId] = value;
      UpdateLastUiPilotSkill(value);
      EnsureValidOreSelection(block);
      MyAPIGateway.Utilities.InvokeOnGameThread(() =>
      {
        block.SetDetailedInfoDirty();
        block.RefreshCustomInfo();
        _oreSelectControl?.UpdateVisual();
      });
    }

    private static long GetOreSelection(IMyTerminalBlock block)
    {
      if (block == null)
        return 0;

      UpdateLastUiPilotSkill(GetMinerSelectionRaw(block));
      EnsureValidOreSelection(block);
      long value;
      return OreSelections.TryGetValue(block.EntityId, out value) ? value : GetDefaultOreKey(_lastUiPilotSkill);
    }

    private static void SetOreSelection(IMyTerminalBlock block, long value)
    {
      if (block == null)
        return;

      UpdateLastUiPilotSkill(GetMinerSelectionRaw(block));
      var ore = FindOreByKey(value);
      if (ore == null || ore.RequiredSkill > _lastUiPilotSkill)
        value = GetDefaultOreKey(_lastUiPilotSkill);

      OreSelections[block.EntityId] = value;
      MyAPIGateway.Utilities.InvokeOnGameThread(() =>
      {
        block.SetDetailedInfoDirty();
        block.RefreshCustomInfo();
      });
    }

    private static void EnsureValidOreSelection(IMyTerminalBlock block)
    {
      if (block == null)
        return;

      long value;
      if (!OreSelections.TryGetValue(block.EntityId, out value))
      {
        OreSelections[block.EntityId] = GetDefaultOreKey(_lastUiPilotSkill);
        return;
      }

      var ore = FindOreByKey(value);
      if (ore == null || ore.RequiredSkill > _lastUiPilotSkill)
        OreSelections[block.EntityId] = GetDefaultOreKey(_lastUiPilotSkill);
    }

    private static long GetDefaultOreKey(int skill)
    {
      for (int i = 0; i < Ores.Count; i++)
      {
        if (Ores[i].RequiredSkill <= skill)
          return Ores[i].Key;
      }

      return 0;
    }

    private static OreOption FindOreByKey(long key)
    {
      for (int i = 0; i < Ores.Count; i++)
      {
        if (Ores[i].Key == key)
          return Ores[i];
      }

      return null;
    }

    private static long GetMinerSelectionRaw(IMyTerminalBlock block)
    {
      if (block == null)
        return 0;

      long value;
      return MinerSelections.TryGetValue(block.EntityId, out value) ? value : 0;
    }

    private static void UpdateLastUiPilotSkill(long key)
    {
      if (key < 0 || key >= Pilots.Count)
      {
        _lastUiPilotSkill = 1;
        return;
      }

      _lastUiPilotSkill = Pilots[(int)key].Skill;
    }

    public static PilotProfile GetSelectedPilot(IMyTerminalBlock block)
    {
      if (block == null || Pilots.Count == 0)
        return null;

      var key = GetMinerSelection(block);
      if (key < 0 || key >= Pilots.Count)
        return Pilots[0];

      return Pilots[(int)key];
    }

    public static long GetSelectedPilotKey(IMyTerminalBlock block)
    {
      if (block == null)
        return 0;

      return GetMinerSelection(block);
    }

    public static long GetSelectedOreKey(IMyTerminalBlock block)
    {
      if (block == null)
        return 0;

      return GetOreSelection(block);
    }

    public static string GetSelectedOreName(IMyTerminalBlock block)
    {
      var key = GetOreSelection(block);
      var ore = FindOreByKey(key);
      return ore != null ? ore.Name : "Iron";
    }

    public static float GetMissionLengthScale(IMyTerminalBlock block)
    {
      if (block == null)
        return 1.0f;

      float value;
      if (!MissionLengthScales.TryGetValue(block.EntityId, out value))
        return 1.0f;

      if (value < 0.5f)
        return 0.5f;
      if (value > 2.0f)
        return 2.0f;

      return value;
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

    private static void SetMissionLengthScale(IMyTerminalBlock block, float value)
    {
      if (block == null)
        return;

      if (value < 0.5f)
        value = 0.5f;
      else if (value > 2.0f)
        value = 2.0f;

      MissionLengthScales[block.EntityId] = value;
      MyAPIGateway.Utilities.InvokeOnGameThread(() =>
      {
        block.SetDetailedInfoDirty();
        block.RefreshCustomInfo();
      });
    }

    public class OreOption
    {
      public readonly long Key;
      public readonly string Name;
      public readonly int RequiredSkill;

      public OreOption(long key, string name, int requiredSkill)
      {
        Key = key;
        Name = name;
        RequiredSkill = requiredSkill;
      }
    }

  }
}
