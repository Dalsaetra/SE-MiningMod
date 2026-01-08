using System;
using System.Collections.Generic;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;

using ProtoBuf;

using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.ModAPI;
using VRageMath;

namespace MiningMissionsV1.Session
{
  [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
  public class MiningMissionSession : MySessionComponentBase
  {
    private const string StorageFile = "MiningMissionsV1_Missions.bin";
    private const double MissionDurationSeconds = 15.0;
    private const double CountdownSeconds = 10.0;
    private const int FramesPerSecond = 60;
    private const float OreMassKg = 1000f;
    private const string OreSubtype = "Iron";
    private const string JumpOutEffect = "Warp_Prototech";
    private const string JumpInEffect = "Warp_Prototech";
    private const string JumpOutSound = "ShipJumpDriveJumpOut";
    private const string JumpInSound = "ShipJumpDriveJumpIn";

    public static MiningMissionSession Instance;

    private readonly List<MissionEntry> _active = new List<MissionEntry>();
    private readonly List<IMyCockpit> _cockpits = new List<IMyCockpit>();
    private readonly List<IMyCargoContainer> _cargo = new List<IMyCargoContainer>();

    public override void LoadData()
    {
      Instance = this;
      LoadFromStorage();
    }

    protected override void UnloadData()
    {
      SaveToStorage();
      _active.Clear();
      Instance = null;
    }

    public override void UpdateAfterSimulation()
    {
      if (!MyAPIGateway.Multiplayer.IsServer || _active.Count == 0)
        return;

      var now = MyAPIGateway.Session.GameplayFrameCounter;
      var completed = false;

      for (int i = _active.Count - 1; i >= 0; i--)
      {
        var entry = _active[i];
        if (now < entry.EndFrame)
          continue;

        _active.RemoveAt(i);
        completed = true;
        if (entry.PendingJump)
          BeginMission(entry);
        else if (entry.PlayReturnEffect)
          TriggerReturnEffect(entry);
        else
          CompleteMission(entry);
      }

      if (completed)
        SaveToStorage();
    }

    public void TryStartMission(IMyTerminalBlock block)
    {
      if (block == null)
        return;

      if (!MyAPIGateway.Multiplayer.IsServer)
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Start mission on the server.");
        return;
      }

      var grid = block.CubeGrid;
      if (grid == null)
        return;

      if (IsMissionActive(grid.EntityId))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Mission already in progress.");
        return;
      }

      var oreAmount = ComputeOreAmount(OreMassKg);
      if (!HasCargoCapacity(grid, oreAmount))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Not enough cargo space for 1000 kg of iron ore.");
        return;
      }

      PlayJumpEffect(grid, JumpOutEffect, JumpOutSound);

      var entry = CreateEntry(grid, countdown: true);
      _active.Add(entry);
      SaveToStorage();
    }

    private bool IsMissionActive(long gridId)
    {
      for (int i = 0; i < _active.Count; i++)
      {
        if (_active[i].OriginalGridId == gridId)
          return true;
      }

      return false;
    }

    private MissionEntry CreateEntry(IMyCubeGrid grid, bool countdown)
    {
      var matrix = grid.WorldMatrix;
      var radius = (float)grid.PositionComp.WorldAABB.HalfExtents.Length();
      var duration = countdown ? CountdownSeconds : MissionDurationSeconds;

      var entry = new MissionEntry
      {
        OriginalGridId = grid.EntityId,
        Position = matrix.Translation,
        Forward = matrix.Forward,
        Up = matrix.Up,
        Radius = radius,
        RemainingSeconds = duration,
        PendingJump = countdown,
        PlayReturnEffect = false,
      };

      entry.EndFrame = MyAPIGateway.Session.GameplayFrameCounter + (long)(duration * FramesPerSecond);
      return entry;
    }

    private void CompleteMission(MissionEntry entry)
    {
      if (entry?.GridBytes == null)
        return;

      var builder = MyAPIGateway.Utilities.SerializeFromBinary<MyObjectBuilder_CubeGrid>(entry.GridBytes);
      if (builder == null)
        return;

      MyAPIGateway.Entities.RemapObjectBuilder(builder);

      var position = entry.Position;
      var freePos = MyAPIGateway.Entities.FindFreePlace(position, entry.Radius);
      if (freePos.HasValue)
        position = freePos.Value;

      var world = MatrixD.CreateWorld(position, entry.Forward, entry.Up);
      builder.PositionAndOrientation = new MyPositionAndOrientation(world);

      var entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(builder);
      var grid = entity as IMyCubeGrid;
      if (grid == null)
        return;

      AddOreToGrid(grid, ComputeOreAmount(OreMassKg));
    }

    private void BeginMission(MissionEntry entry)
    {
      if (entry == null)
        return;

      IMyEntity entity;
      if (!MyAPIGateway.Entities.TryGetEntityById(entry.OriginalGridId, out entity))
        return;

      var grid = entity as IMyCubeGrid;
      if (grid == null)
        return;

      KickPilots(grid);
      StopGrid(grid);

      var builder = grid.GetObjectBuilder(true) as MyObjectBuilder_CubeGrid;
      entry.GridBytes = MyAPIGateway.Utilities.SerializeToBinary(builder);
      entry.PendingJump = false;
      entry.PlayReturnEffect = true;
      entry.RemainingSeconds = MissionDurationSeconds;
      entry.EndFrame = MyAPIGateway.Session.GameplayFrameCounter + (long)(MissionDurationSeconds * FramesPerSecond);

      _active.Add(entry);
      grid.Close();
    }

    private void TriggerReturnEffect(MissionEntry entry)
    {
      if (entry == null)
        return;

      var position = entry.Position;
      var freePos = MyAPIGateway.Entities.FindFreePlace(position, entry.Radius);
      if (freePos.HasValue)
        position = freePos.Value;

      entry.Position = position;
      entry.PlayReturnEffect = false;
      entry.RemainingSeconds = CountdownSeconds;
      entry.EndFrame = MyAPIGateway.Session.GameplayFrameCounter + (long)(CountdownSeconds * FramesPerSecond);

      if (!string.IsNullOrEmpty(JumpInEffect))
        MyVisualScriptLogicProvider.CreateParticleEffectAtPosition(JumpInEffect, position);

      if (!string.IsNullOrEmpty(JumpInSound))
        MyVisualScriptLogicProvider.PlaySingleSoundAtPosition(JumpInSound, position);

      _active.Add(entry);
    }

    private void KickPilots(IMyCubeGrid grid)
    {
      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem == null)
        return;

      _cockpits.Clear();
      terminalSystem.GetBlocksOfType(_cockpits, c => c.CubeGrid == grid);
      for (int i = 0; i < _cockpits.Count; i++)
        _cockpits[i].RemovePilot();
    }

    private void StopGrid(IMyCubeGrid grid)
    {
      if (grid?.Physics == null)
        return;

      grid.Physics.LinearVelocity = Vector3.Zero;
      grid.Physics.AngularVelocity = Vector3.Zero;
    }

    private void PlayJumpEffect(IMyCubeGrid grid, string effectName, string soundName)
    {
      if (grid == null)
        return;

      var name = EnsureEntityName(grid);
      if (!string.IsNullOrEmpty(effectName))
        MyVisualScriptLogicProvider.CreateParticleEffectAtEntity(effectName, name);

      if (!string.IsNullOrEmpty(soundName))
        MyVisualScriptLogicProvider.PlaySingleSoundAtEntity(soundName, name);
    }

    private string EnsureEntityName(IMyEntity entity)
    {
      if (!string.IsNullOrEmpty(entity.Name))
        return entity.Name;

      var name = $"MiningMissions_{entity.EntityId}";
      entity.Name = name;
      return name;
    }

    private MyFixedPoint ComputeOreAmount(float massKg)
    {
      var def = GetOreDefinition();
      if (def == null)
        return (MyFixedPoint)massKg;

      var unitMass = (float)def.Mass;
      if (unitMass <= 0f)
        return (MyFixedPoint)massKg;

      return (MyFixedPoint)(massKg / unitMass);
    }

    private bool HasCargoCapacity(IMyCubeGrid grid, MyFixedPoint amount)
    {
      var def = GetOreDefinition();
      if (def == null)
        return false;

      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem == null)
        return false;

      _cargo.Clear();
      terminalSystem.GetBlocksOfType(_cargo, c => c.CubeGrid == grid);

      double totalCapacity = 0d;
      var unitVolume = (double)def.Volume;
      if (unitVolume <= 0d)
        return false;

      for (int i = 0; i < _cargo.Count; i++)
      {
        var inv = _cargo[i].GetInventory(0);
        if (inv == null)
          continue;

        var available = (double)(inv.MaxVolume - inv.CurrentVolume);
        if (available <= 0d)
          continue;

        totalCapacity += available / unitVolume;
      }

      return totalCapacity >= (double)amount;
    }

    private void AddOreToGrid(IMyCubeGrid grid, MyFixedPoint amount)
    {
      var def = GetOreDefinition();
      if (def == null)
        return;

      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem == null)
        return;

      _cargo.Clear();
      terminalSystem.GetBlocksOfType(_cargo, c => c.CubeGrid == grid);

      var remaining = amount;
      var oreObject = new MyObjectBuilder_Ore { SubtypeName = OreSubtype };
      var oreId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), OreSubtype);
      var unitVolume = (double)def.Volume;

      for (int i = 0; i < _cargo.Count; i++)
      {
        if (remaining <= 0)
          break;

        var inv = _cargo[i].GetInventory(0);
        if (inv == null)
          continue;

        var add = remaining;
        if (!inv.CanItemsBeAdded(add, oreId))
        {
          if (unitVolume <= 0d)
            continue;

          var available = (double)(inv.MaxVolume - inv.CurrentVolume);
          if (available <= 0d)
            continue;

          var fitAmount = (MyFixedPoint)(available / unitVolume);
          if (fitAmount <= 0)
            continue;

          add = fitAmount;
        }

        if (add > 0)
        {
          inv.AddItems(add, oreObject);
          remaining -= add;
        }
      }
    }

    private MyPhysicalItemDefinition GetOreDefinition()
    {
      var oreId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), OreSubtype);
      return MyDefinitionManager.Static.GetPhysicalItemDefinition(oreId);
    }

    private void SaveToStorage()
    {
      if (!MyAPIGateway.Multiplayer.IsServer)
        return;

      var data = new MissionSaveData();
      var now = MyAPIGateway.Session.GameplayFrameCounter;

      for (int i = 0; i < _active.Count; i++)
      {
        var entry = _active[i];
        entry.RemainingSeconds = Math.Max(0d, (entry.EndFrame - now) / (double)FramesPerSecond);
        data.Active.Add(entry);
      }

      var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);
      var payload = Convert.ToBase64String(bytes);
      using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(StorageFile, typeof(MiningMissionSession)))
      {
        writer.Write(payload);
      }
    }

    private void LoadFromStorage()
    {
      if (!MyAPIGateway.Multiplayer.IsServer)
        return;

      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(StorageFile, typeof(MiningMissionSession)))
          return;

        using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(StorageFile, typeof(MiningMissionSession)))
        {
          var payload = reader.ReadToEnd();
          if (string.IsNullOrEmpty(payload))
            return;

          var bytes = Convert.FromBase64String(payload);
          var data = MyAPIGateway.Utilities.SerializeFromBinary<MissionSaveData>(bytes);
          if (data?.Active == null)
            return;

          _active.Clear();
          var now = MyAPIGateway.Session.GameplayFrameCounter;
          for (int i = 0; i < data.Active.Count; i++)
          {
            var entry = data.Active[i];
            entry.EndFrame = now + (long)(entry.RemainingSeconds * FramesPerSecond);
            _active.Add(entry);
          }
        }
      }
      catch
      {
      }
    }

    [ProtoContract]
    private class MissionSaveData
    {
      [ProtoMember(1)]
      public List<MissionEntry> Active = new List<MissionEntry>();
    }

    [ProtoContract]
    private class MissionEntry
    {
      [ProtoMember(1)]
      public long OriginalGridId;
      [ProtoMember(2)]
      public byte[] GridBytes;
      [ProtoMember(3)]
      public Vector3D Position;
      [ProtoMember(4)]
      public Vector3D Forward;
      [ProtoMember(5)]
      public Vector3D Up;
      [ProtoMember(6)]
      public float Radius;
      [ProtoMember(7)]
      public double RemainingSeconds;
      [ProtoMember(8)]
      public long EndFrame;
      [ProtoMember(9)]
      public bool PendingJump;
      [ProtoMember(10)]
      public bool PlayReturnEffect;
    }
  }
}
