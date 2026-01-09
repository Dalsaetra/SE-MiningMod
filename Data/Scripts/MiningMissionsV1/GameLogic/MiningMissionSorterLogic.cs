using MiningMissionsV1.Support;
using MiningMissionsV1.Session;

using Sandbox.Common.ObjectBuilders;
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
    private readonly List<Sandbox.ModAPI.IMyShipDrill> _terminalDrills = new List<Sandbox.ModAPI.IMyShipDrill>();
    private readonly List<VRage.Game.ModAPI.IMySlimBlock> _slimBlocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
    private readonly HashSet<VRage.ModAPI.IMyEntity> _entities = new HashSet<VRage.ModAPI.IMyEntity>();
    private Sandbox.ModAPI.IMyTerminalBlock _block;
    private int _lastDrillCount = -1;
    private double _lastMaxAcceleration = -1d;
    private long _lastPilotKey = long.MinValue;
    private long _lastOreKey = long.MinValue;
    private double _lastExpectedSeconds = -1d;
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
      var entityDrillCount = 0;
      var terminalMaxDirectional = 0;
      var slimMaxDirectional = 0;
      var entityMaxDirectional = 0;
      var slimDirCounts = new int[6];
      var entityDirCounts = new int[6];

      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem != null)
      {
        _terminalDrills.Clear();
        terminalSystem.GetBlocksOfType(_terminalDrills, b => b.CubeGrid == grid);
        terminalDrillCount = _terminalDrills.Count;
        terminalMaxDirectional = GetMaxDirectionalDrillCount(_terminalDrills);
      }
      _slimBlocks.Clear();
      grid.GetBlocks(_slimBlocks, b => b != null);
      for (int i = 0; i < _slimBlocks.Count; i++)
      {
        var fat = _slimBlocks[i].FatBlock;
        var drill = fat as Sandbox.ModAPI.IMyShipDrill;
        if (drill != null)
        {
          slimDrillCount++;
          AddDirectionalCount(slimDirCounts, drill.Orientation.Forward);
        }
      }
      slimMaxDirectional = GetMaxDirectionalCount(slimDirCounts);

      _entities.Clear();
      MyAPIGateway.Entities.GetEntities(_entities, e => e is Sandbox.ModAPI.IMyShipDrill);
      foreach (var entity in _entities)
      {
        var drill = entity as Sandbox.ModAPI.IMyShipDrill;
        if (drill != null && drill.CubeGrid == grid)
        {
          entityDrillCount++;
          AddDirectionalCount(entityDirCounts, drill.Orientation.Forward);
        }
      }
      entityMaxDirectional = GetMaxDirectionalCount(entityDirCounts);

      if (terminalDrillCount >= slimDrillCount && terminalDrillCount >= entityDrillCount)
        maxDirectionalCount = terminalMaxDirectional;
      else if (slimDrillCount >= entityDrillCount)
        maxDirectionalCount = slimMaxDirectional;
      else
        maxDirectionalCount = entityMaxDirectional;

      var maxAcceleration = GetMaxAcceleration(grid);
      var pilotKey = MiningMissionControls.GetSelectedPilotKey(_block);
      var oreKey = MiningMissionControls.GetSelectedOreKey(_block);
      var pilot = MiningMissionControls.GetSelectedPilot(_block);
      var speedSkill = pilot != null ? pilot.Speed : 0;
      var oreName = MiningMissionControls.GetSelectedOreName(_block);
      var expectedSeconds = MiningMissionSession.EstimateMissionTimeMeanSeconds(speedSkill, oreName, maxAcceleration, maxDirectionalCount);
      var accelChanged = Math.Abs(maxAcceleration - _lastMaxAcceleration) > 0.01d;
      var drillChanged = maxDirectionalCount != _lastDrillCount;
      var pilotChanged = pilotKey != _lastPilotKey;
      var oreChanged = oreKey != _lastOreKey;
      var expectedChanged = Math.Abs(expectedSeconds - _lastExpectedSeconds) > 1.0d;
      if (!drillChanged && !accelChanged && !pilotChanged && !oreChanged && !expectedChanged)
        return;

      _lastDrillCount = maxDirectionalCount;
      _lastMaxAcceleration = maxAcceleration;
      _lastPilotKey = pilotKey;
      _lastOreKey = oreKey;
      _lastExpectedSeconds = expectedSeconds;
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
      var expected = MiningMissionSession.EstimateMissionTimeMeanSeconds(speedSkill, oreName, accel, count);
      if (expected < 0d)
        expected = 0d;
      _lastExpectedSeconds = expected;
      sb.AppendLine($"Expected mission time: {FormatDuration(expected)}");

      if (pilot != null)
      {
        sb.AppendLine($"Pilot: {pilot.Name}");
        sb.AppendLine($"Skill {pilot.Skill} | Reliability {pilot.Reliability} | Yield {pilot.Yield} | Speed {pilot.Speed}");
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
        AddDirectionalCount(counts, drills[i].Orientation.Forward);

      var max = 0;
      for (int i = 0; i < counts.Length; i++)
      {
        if (counts[i] > max)
          max = counts[i];
      }

      return max;
    }

    private void AddDirectionalCount(int[] counts, Base6Directions.Direction direction)
    {
      switch (direction)
      {
        case Base6Directions.Direction.Forward:
          counts[0]++;
          break;
        case Base6Directions.Direction.Backward:
          counts[1]++;
          break;
        case Base6Directions.Direction.Left:
          counts[2]++;
          break;
        case Base6Directions.Direction.Right:
          counts[3]++;
          break;
        case Base6Directions.Direction.Up:
          counts[4]++;
          break;
        case Base6Directions.Direction.Down:
          counts[5]++;
          break;
      }
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
