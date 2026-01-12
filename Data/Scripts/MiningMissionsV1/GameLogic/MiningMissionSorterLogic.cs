using MiningMissionsV1.Support;
using MiningMissionsV1.Session;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Text;

using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.ModAPI;
using VRageMath;

namespace MiningMissionsV1.GameLogic
{
  public abstract class MiningMissionSorterLogicBase : MyGameLogicComponent
  {
    private const int PrototechDrillWeight = 5;
    private readonly List<Sandbox.ModAPI.IMyShipDrill> _terminalDrills = new List<Sandbox.ModAPI.IMyShipDrill>();
    private readonly List<VRage.Game.ModAPI.IMySlimBlock> _slimBlocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
    private Sandbox.ModAPI.IMyTerminalBlock _block;
    private int _lastDrillCount = -1;
    private double _lastMaxAcceleration = -1d;
    private long _lastPilotKey = long.MinValue;
    private long _lastOreKey = long.MinValue;
    private double _lastExpectedSeconds = -1d;
    private double _lastFreeOreCapacityKg = -1d;
    private bool _customInfoHooked;

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      base.Init(objectBuilder);
      NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
      _block = Entity as Sandbox.ModAPI.IMyTerminalBlock;
    }

    public override void UpdateOnceBeforeFrame()
    {
      base.UpdateOnceBeforeFrame();

      if (_block == null)
        _block = Entity as Sandbox.ModAPI.IMyTerminalBlock;

      if (_block == null)
        return;

      MiningMissionControls.EnsureControls();

      if (!_customInfoHooked)
      {
        _customInfoHooked = true;
        _block.AppendingCustomInfo += AppendCustomInfo;
        _block.OnMarkForClose += OnClose;
        _block.OnClose += OnClose;
      }
    }

    public override void UpdateAfterSimulation100()
    {
      base.UpdateAfterSimulation100();

      if (_block == null)
        return;

      var grid = _block.CubeGrid;
      if (grid == null)
        return;

      var maxDirectionalCount = 0;
      var terminalDrillCount = 0;
      var slimDrillCount = 0;
      var terminalMaxDirectional = 0;
      var slimMaxDirectional = 0;
      var slimDirCounts = new int[6];

      var useSlimScan = IsAnyTerminalOpen();
      if (useSlimScan)
      {
        _slimBlocks.Clear();
        grid.GetBlocks(_slimBlocks, b => b != null && b.FatBlock != null && b.FatBlock.IsFunctional);
        for (int i = 0; i < _slimBlocks.Count; i++)
        {
          var fat = _slimBlocks[i].FatBlock;
          var drill = fat as Sandbox.ModAPI.IMyShipDrill;
          if (drill != null && drill.IsFunctional)
          {
            slimDrillCount++;
            AddDirectionalCount(slimDirCounts, drill.Orientation.Forward, GetDrillWeight(drill));
          }
        }
        slimMaxDirectional = GetMaxDirectionalCount(slimDirCounts);
        maxDirectionalCount = slimMaxDirectional;
      }
      else
      {
        var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
        if (terminalSystem != null)
        {
          _terminalDrills.Clear();
          terminalSystem.GetBlocksOfType(_terminalDrills, b => b.CubeGrid == grid && b.IsFunctional);
          terminalDrillCount = _terminalDrills.Count;
          terminalMaxDirectional = GetMaxDirectionalDrillCount(_terminalDrills);
          maxDirectionalCount = terminalMaxDirectional;
        }
      }

      var maxAcceleration = GetMaxAcceleration(grid);
      var pilotKey = MiningMissionControls.GetSelectedPilotKey(_block);
      var oreKey = MiningMissionControls.GetSelectedOreKey(_block);
      var pilot = MiningMissionControls.GetSelectedPilot(_block);
      var speedSkill = pilot != null ? pilot.Speed : 0;
      var oreName = MiningMissionControls.GetSelectedOreName(_block);
      var missionScale = MiningMissionControls.GetMissionLengthScale(_block);
      var isLargeGrid = grid.GridSizeEnum == VRage.Game.MyCubeSize.Large;
      var expectedSeconds = MiningMissionSession.EstimateMissionTimeMeanSeconds(speedSkill, oreName, maxAcceleration, maxDirectionalCount, isLargeGrid) * missionScale;
      var freeOreCapacityKg = MiningMissionSession.EstimateFreeOreMassKg(grid, oreName);
      var accelChanged = Math.Abs(maxAcceleration - _lastMaxAcceleration) > 0.01d;
      var drillChanged = maxDirectionalCount != _lastDrillCount;
      var pilotChanged = pilotKey != _lastPilotKey;
      var oreChanged = oreKey != _lastOreKey;
      var expectedChanged = Math.Abs(expectedSeconds - _lastExpectedSeconds) > 1.0d;
      var capacityChanged = Math.Abs(freeOreCapacityKg - _lastFreeOreCapacityKg) > 1.0d;
      if (!drillChanged && !accelChanged && !pilotChanged && !oreChanged && !expectedChanged && !capacityChanged)
        return;

      _lastDrillCount = maxDirectionalCount;
      _lastMaxAcceleration = maxAcceleration;
      _lastPilotKey = pilotKey;
      _lastOreKey = oreKey;
      _lastExpectedSeconds = expectedSeconds;
      _lastFreeOreCapacityKg = freeOreCapacityKg;
      _block.RefreshCustomInfo();
    }

    private void AppendCustomInfo(Sandbox.ModAPI.IMyTerminalBlock block, StringBuilder sb)
    {
      if (sb == null)
        return;

      var count = _lastDrillCount < 0 ? 0 : _lastDrillCount;
      sb.AppendLine("Mining Missions");
      sb.AppendLine($"Max drills in one direction: {count}");
      var accel = _lastMaxAcceleration < 0 ? 0d : _lastMaxAcceleration;
      sb.AppendLine($"Max acceleration in one direction: {accel:0.00} m/s^2");
      var pilot = MiningMissionControls.GetSelectedPilot(block);
      var speedSkill = pilot != null ? pilot.Speed : 0;
      var oreName = MiningMissionControls.GetSelectedOreName(block);
      var missionScale = MiningMissionControls.GetMissionLengthScale(block);
      var isLargeGrid = block?.CubeGrid != null && block.CubeGrid.GridSizeEnum == VRage.Game.MyCubeSize.Large;
      var expected = MiningMissionSession.EstimateMissionTimeMeanSeconds(speedSkill, oreName, accel, count, isLargeGrid) * missionScale;
      if (expected < 0d)
        expected = 0d;
      _lastExpectedSeconds = expected;
      sb.AppendLine($"Expected mission time: {FormatDuration(expected)}");

      if (pilot != null)
      {
        sb.AppendLine($"Pilot: {pilot.Name}");
        sb.AppendLine($"Skill {pilot.Skill} | Reliability {pilot.Reliability} | Yield {pilot.Yield} | Speed {pilot.Speed}");
        var expectedYield = MiningMissionSession.EstimateYieldMeanUnits(pilot.Yield, pilot.Skill, count, oreName, missionScale, isLargeGrid);
        sb.AppendLine($"Expected yield: {expectedYield:0} kg {oreName}");
        var freeCapacityKg = _lastFreeOreCapacityKg < 0d ? MiningMissionSession.EstimateFreeOreMassKg(block?.CubeGrid, oreName) : _lastFreeOreCapacityKg;
        sb.AppendLine($"Available {oreName} capacity: {freeCapacityKg:0} kg");
        var price = MiningMissionSession.EstimateMissionCost(pilot.Skill, oreName, expected);
        sb.AppendLine($"Mission cost: {price} credits");
        var successProb = MiningMissionSession.EstimateMissionSuccessProbability(pilot.Reliability, expected, isLargeGrid);
        sb.AppendLine($"Full mission completion chance: {successProb:P1}");
      }
    }

    private string FormatDuration(double seconds)
    {
      if (seconds < 0d)
        seconds = 0d;

      var time = TimeSpan.FromSeconds(seconds);
      if (time.TotalHours >= 1d)
        return $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s";

      return $"{time.Minutes}m {time.Seconds}s";
    }

    private int GetMaxDirectionalDrillCount(List<Sandbox.ModAPI.IMyShipDrill> drills)
    {
      var counts = new int[6];
      for (int i = 0; i < drills.Count; i++)
        AddDirectionalCount(counts, drills[i].Orientation.Forward, GetDrillWeight(drills[i]));

      var max = 0;
      for (int i = 0; i < counts.Length; i++)
      {
        if (counts[i] > max)
          max = counts[i];
      }

      return max;
    }

    private void AddDirectionalCount(int[] counts, Base6Directions.Direction direction, int weight)
    {
      if (weight < 1)
        weight = 1;

      switch (direction)
      {
        case Base6Directions.Direction.Forward:
          counts[0] += weight;
          break;
        case Base6Directions.Direction.Backward:
          counts[1] += weight;
          break;
        case Base6Directions.Direction.Left:
          counts[2] += weight;
          break;
        case Base6Directions.Direction.Right:
          counts[3] += weight;
          break;
        case Base6Directions.Direction.Up:
          counts[4] += weight;
          break;
        case Base6Directions.Direction.Down:
          counts[5] += weight;
          break;
      }
    }

    private int GetDrillWeight(Sandbox.ModAPI.IMyShipDrill drill)
    {
      if (drill == null)
        return 1;

      var defString = drill.BlockDefinition.ToString();
      if (!string.IsNullOrEmpty(defString)
        && defString.IndexOf("PrototechDrill", StringComparison.OrdinalIgnoreCase) >= 0)
        return PrototechDrillWeight;

      return 1;
    }

    private int GetMaxDirectionalCount(int[] counts)
    {
      var max = 0;
      for (int i = 0; i < counts.Length; i++)
      {
        if (counts[i] > max)
          max = counts[i];
      }

      return max;
    }

    private double GetMaxAcceleration(VRage.Game.ModAPI.IMyCubeGrid grid)
    {
      if (grid?.Physics == null)
        return 0d;

      var mass = (double)grid.Physics.Mass;
      if (mass <= 0d)
        return 0d;

      var max = 0d;
      var forward = grid.GetMaxThrustInDirection(Base6Directions.Direction.Forward);
      var backward = grid.GetMaxThrustInDirection(Base6Directions.Direction.Backward);
      var left = grid.GetMaxThrustInDirection(Base6Directions.Direction.Left);
      var right = grid.GetMaxThrustInDirection(Base6Directions.Direction.Right);
      var up = grid.GetMaxThrustInDirection(Base6Directions.Direction.Up);
      var down = grid.GetMaxThrustInDirection(Base6Directions.Direction.Down);

      max = Math.Max(max, forward / mass);
      max = Math.Max(max, backward / mass);
      max = Math.Max(max, left / mass);
      max = Math.Max(max, right / mass);
      max = Math.Max(max, up / mass);
      max = Math.Max(max, down / mass);

      return max;
    }

    private void OnClose(IMyEntity entity)
    {
      if (_block == null)
        return;

      _block.AppendingCustomInfo -= AppendCustomInfo;
      _block.OnMarkForClose -= OnClose;
      _block.OnClose -= OnClose;
      _customInfoHooked = false;
    }

    private static bool IsAnyTerminalOpen()
    {
      return MyVisualScriptLogicProvider.GetOpenedTerminal() != null;
    }
  }

  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false, "MiningMissionSorter")]
  public class MiningMissionSorterLogic : MiningMissionSorterLogicBase
  {
  }

  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false, "MiningMissionSorterSmall")]
  public class MiningMissionSorterSmallLogic : MiningMissionSorterLogicBase
  {
  }
}
