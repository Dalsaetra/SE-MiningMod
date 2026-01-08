using MiningMissionsV1.Support;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

using System.Collections.Generic;
using System.Text;

using VRage.Game.Components;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.ModAPI;

namespace MiningMissionsV1.GameLogic
{
  public abstract class MiningMissionSorterLogicBase : MyGameLogicComponent
  {
    private readonly List<Sandbox.ModAPI.IMyShipDrill> _terminalDrills = new List<Sandbox.ModAPI.IMyShipDrill>();
    private readonly List<VRage.Game.ModAPI.IMySlimBlock> _slimBlocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
    private readonly HashSet<VRage.ModAPI.IMyEntity> _entities = new HashSet<VRage.ModAPI.IMyEntity>();
    private Sandbox.ModAPI.IMyTerminalBlock _block;
    private int _lastDrillCount = -1;
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

      var count = 0;
      var terminalDrillCount = 0;
      var slimDrillCount = 0;
      var entityDrillCount = 0;

      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem != null)
      {
        _terminalDrills.Clear();
        terminalSystem.GetBlocksOfType(_terminalDrills, b => b.CubeGrid == grid);
        terminalDrillCount = _terminalDrills.Count;
      }
      _slimBlocks.Clear();
      grid.GetBlocks(_slimBlocks, b => b != null);
      for (int i = 0; i < _slimBlocks.Count; i++)
      {
        var fat = _slimBlocks[i].FatBlock;
        if (fat is Sandbox.ModAPI.IMyShipDrill)
          slimDrillCount++;
      }

      _entities.Clear();
      MyAPIGateway.Entities.GetEntities(_entities, e => e is Sandbox.ModAPI.IMyShipDrill);
      foreach (var entity in _entities)
      {
        var drill = entity as Sandbox.ModAPI.IMyShipDrill;
        if (drill != null && drill.CubeGrid == grid)
          entityDrillCount++;
      }

      count = terminalDrillCount;
      if (slimDrillCount > count)
        count = slimDrillCount;
      if (entityDrillCount > count)
        count = entityDrillCount;
      var drillChanged = count != _lastDrillCount;
      if (!drillChanged)
        return;

      if (drillChanged)
      {
        _lastDrillCount = count;
        _block.RefreshCustomInfo();
      }
    }

    private void AppendCustomInfo(Sandbox.ModAPI.IMyTerminalBlock block, StringBuilder sb)
    {
      if (sb == null)
        return;

      var count = _lastDrillCount < 0 ? 0 : _lastDrillCount;
      sb.AppendLine("Mining Missions");
      sb.AppendLine($"Drills detected: {count}");
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
