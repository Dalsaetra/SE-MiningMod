using System;
using System.Collections.Generic;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;

using ProtoBuf;

using MiningMissionsV1.Support;

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
    private const double CountdownSeconds = 10.0;
    private const int FramesPerSecond = 60;
    private const float OreMassKg = 1000f;
    private const string DefaultOreSubtype = "Iron";
    private const string JumpOutEffect = "Warp_Prototech";
    private const string JumpInEffect = "Warp_Prototech";
    private const string JumpOutSound = "ShipJumpDriveJumpOut";
    private const string JumpInSound = "ShipJumpDriveJumpIn";

    private const double KSpeedSkill = 0.15;
    private const double ARef = 5.0;
    private const double AMin = 0.1;
    private const double DrillExponentDefault = 0.75;
    private const double RSpeedSkill = 0.08;
    private const double MinVarianceFactor = 0.5;
    private const double MinMissionTimeSeconds = 90.0;
    private const double MaxMissionTimeSeconds = 5400.0;

    private const double KYieldSkill = 0.18;
    private const double DrillBeta = 0.45;
    private const double RYieldSkill = 0.10;
    private const double MinYieldVarianceFactor = 0.5;
    private const int ReliabilityTicks = 5;

    private const double PriceSkillRateMultiplier = 0.10;
    private static readonly long[] BasePriceBySkill = { 0, 1000, 2000, 3000, 4500, 6000 };
    private static readonly double[] RatePerMinuteByRarity = { 0.0, 100.0, 200.0, 350.0, 500.0, 750.0 };
    private static readonly Dictionary<string, int> OreRarity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
      ["Stone"] = 1,
      ["Iron"] = 1,
      ["Nickel"] = 1,
      ["Silicon"] = 1,
      ["Ice"] = 1,
      ["Cobalt"] = 2,
      ["Magnesium"] = 3,
      ["Silver"] = 4,
      ["Gold"] = 4,
      ["Platinum"] = 5,
      ["Uranium"] = 5
    };

    private static readonly Random Rng = new Random();
    private static readonly Dictionary<string, OreSpeedParams> OreParams = new Dictionary<string, OreSpeedParams>(StringComparer.OrdinalIgnoreCase)
    {
      ["Stone"] = new OreSpeedParams
      {
        BaseTravel_s = 180.0,
        BaseMine_s = 360.0,
        TravelDifficulty = 0.1,
        DrillExponent = 0.85,
        Sigma0 = 0.10
      },
      ["Iron"] = new OreSpeedParams
      {
        BaseTravel_s = 240.0,
        BaseMine_s = 480.0,
        TravelDifficulty = 0.2,
        DrillExponent = 0.80,
        Sigma0 = 0.12
      },
      ["Nickel"] = new OreSpeedParams
      {
        BaseTravel_s = 260.0,
        BaseMine_s = 520.0,
        TravelDifficulty = 0.25,
        DrillExponent = 0.80,
        Sigma0 = 0.14
      },
      ["Silicon"] = new OreSpeedParams
      {
        BaseTravel_s = 270.0,
        BaseMine_s = 540.0,
        TravelDifficulty = 0.3,
        DrillExponent = 0.78,
        Sigma0 = 0.14
      },
      ["Ice"] = new OreSpeedParams
      {
        BaseTravel_s = 220.0,
        BaseMine_s = 420.0,
        TravelDifficulty = 0.2,
        DrillExponent = 0.82,
        Sigma0 = 0.12
      },
      ["Cobalt"] = new OreSpeedParams
      {
        BaseTravel_s = 360.0,
        BaseMine_s = 520.0,
        TravelDifficulty = 0.4,
        DrillExponent = 0.72,
        Sigma0 = 0.18
      },
      ["Magnesium"] = new OreSpeedParams
      {
        BaseTravel_s = 420.0,
        BaseMine_s = 450.0,
        TravelDifficulty = 0.55,
        DrillExponent = 0.70,
        Sigma0 = 0.20
      },
      ["Silver"] = new OreSpeedParams
      {
        BaseTravel_s = 520.0,
        BaseMine_s = 500.0,
        TravelDifficulty = 0.7,
        DrillExponent = 0.65,
        Sigma0 = 0.22
      },
      ["Gold"] = new OreSpeedParams
      {
        BaseTravel_s = 560.0,
        BaseMine_s = 520.0,
        TravelDifficulty = 0.75,
        DrillExponent = 0.65,
        Sigma0 = 0.23
      },
      ["Platinum"] = new OreSpeedParams
      {
        BaseTravel_s = 680.0,
        BaseMine_s = 420.0,
        TravelDifficulty = 0.85,
        DrillExponent = 0.62,
        Sigma0 = 0.24
      },
      ["Uranium"] = new OreSpeedParams
      {
        BaseTravel_s = 720.0,
        BaseMine_s = 300.0,
        TravelDifficulty = 0.9,
        DrillExponent = 0.60,
        Sigma0 = 0.25
      }
    };
    private static readonly Dictionary<string, OreYieldParams> OreYield = new Dictionary<string, OreYieldParams>(StringComparer.OrdinalIgnoreCase)
    {
      ["Stone"] = new OreYieldParams { BaseYield = 1200.0, CV0 = 0.20 },
      ["Iron"] = new OreYieldParams { BaseYield = 1000.0, CV0 = 0.20 },
      ["Nickel"] = new OreYieldParams { BaseYield = 900.0, CV0 = 0.20 },
      ["Silicon"] = new OreYieldParams { BaseYield = 850.0, CV0 = 0.20 },
      ["Ice"] = new OreYieldParams { BaseYield = 1100.0, CV0 = 0.20 },
      ["Cobalt"] = new OreYieldParams { BaseYield = 700.0, CV0 = 0.25 },
      ["Magnesium"] = new OreYieldParams { BaseYield = 650.0, CV0 = 0.25 },
      ["Silver"] = new OreYieldParams { BaseYield = 550.0, CV0 = 0.30 },
      ["Gold"] = new OreYieldParams { BaseYield = 500.0, CV0 = 0.30 },
      ["Platinum"] = new OreYieldParams { BaseYield = 420.0, CV0 = 0.30 },
      ["Uranium"] = new OreYieldParams { BaseYield = 380.0, CV0 = 0.30 }
    };
    private static readonly Dictionary<string, double> OreMinedRatios = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
      ["Stone"] = 5.0,
      ["Iron"] = 5.0,
      ["Nickel"] = 3.0,
      ["Silicon"] = 3.0,
      ["Cobalt"] = 3.0,
      ["Magnesium"] = 3.0,
      ["Silver"] = 1.0,
      ["Gold"] = 1.0,
      ["Platinum"] = 1.0,
      ["Uranium"] = 0.3,
      ["Ice"] = 5.0
    };

    public static MiningMissionSession Instance;

    private readonly List<MissionEntry> _active = new List<MissionEntry>();
    private readonly List<IMyCockpit> _cockpits = new List<IMyCockpit>();
    private readonly List<IMyCargoContainer> _cargo = new List<IMyCargoContainer>();
    private readonly List<IMyShipDrill> _drills = new List<IMyShipDrill>();
    private readonly List<IMyGyro> _gyros = new List<IMyGyro>();
    private readonly List<IMyThrust> _thrusters = new List<IMyThrust>();
    private readonly List<IMyRadioAntenna> _antennas = new List<IMyRadioAntenna>();
    private readonly HashSet<Base6Directions.Direction> _thrustDirs = new HashSet<Base6Directions.Direction>();
    private readonly List<IMyPlayer> _players = new List<IMyPlayer>();

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

      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem == null)
        return;

      if (!HasMinimumDrills(terminalSystem, grid, 2))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Requires at least 2 drills.");
        return;
      }

      if (!HasGyroscope(terminalSystem, grid))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Requires at least 1 gyroscope.");
        return;
      }

      if (!HasCockpit(terminalSystem, grid))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Requires at least 1 cockpit.");
        return;
      }

      string missingDirections;
      if (!HasThrustersAllDirections(terminalSystem, grid, out missingDirections))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", $"Missing thrusters: {missingDirections}.");
        return;
      }

      if (!HasAntenna(terminalSystem, grid))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Requires at least 1 antenna.");
        return;
      }

      var pilot = MiningMissionControls.GetSelectedPilot(block);
      var speedSkill = pilot != null ? pilot.Speed : 0;
      var yieldSkill = pilot != null ? pilot.Yield : 0;
      var miningSkill = pilot != null ? pilot.Skill : 0;
      var drillCount = GetMaxDirectionalDrillCount(terminalSystem, grid);
      var maxAcceleration = GetMaxAcceleration(grid);
      var oreSubtype = MiningMissionControls.GetSelectedOreName(block);
      var missionScale = MiningMissionControls.GetMissionLengthScale(block);
      var yieldMean = EstimateYieldMeanUnits(yieldSkill, miningSkill, drillCount, oreSubtype) * missionScale;
      var oreAmount = (MyFixedPoint)Math.Max(1d, yieldMean);
      if (!HasCargoCapacity(grid, oreAmount, oreSubtype))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", $"Not enough cargo space for the expected {oreSubtype} ore yield.");
        return;
      }

      var yieldUnits = ComputeYieldUnits(yieldMean, yieldSkill, oreSubtype);
      if (yieldUnits < 1d)
        yieldUnits = 1d;

      var reliabilitySkill = pilot != null ? pilot.Reliability : 0;
      var fullMissionDuration = ComputeMissionDurationSeconds(speedSkill, oreSubtype, maxAcceleration, drillCount) * missionScale;
      double returnProgress;
      bool missionFailed;
      var yieldFactor = ResolveMissionYieldFactor(reliabilitySkill, fullMissionDuration, ReliabilityTicks, Rng, out returnProgress, out missionFailed);
      yieldUnits *= yieldFactor;
      var missionDuration = fullMissionDuration * returnProgress;
      var chargeIdentityId = GetChargeIdentityId(block, grid);
      if (chargeIdentityId <= 0)
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "No valid owner to charge for this mission.");
        return;
      }

      var fullMissionCost = EstimateMissionCost(miningSkill, oreSubtype, fullMissionDuration);
      if (!TryChargeMissionCost(chargeIdentityId, fullMissionCost))
      {
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Not enough credits to start the mission.");
        return;
      }

      if (fullMissionCost > 0)
        MyAPIGateway.Utilities.ShowMessage("MiningMissions", $"Charged {fullMissionCost} credits for the mission.");

      PlayJumpEffect(grid, JumpOutEffect, JumpOutSound);
      var entry = CreateEntry(grid, countdown: true, oreSubtype: oreSubtype, missionDurationSeconds: missionDuration);
      entry.OreUnits = yieldUnits;
      entry.MissionFailed = missionFailed;
      entry.MiningSkill = miningSkill;
      entry.ChargeIdentityId = chargeIdentityId;
      entry.FullMissionCost = fullMissionCost;
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

    private bool HasMinimumDrills(IMyGridTerminalSystem terminalSystem, IMyCubeGrid grid, int minCount)
    {
      _drills.Clear();
      terminalSystem.GetBlocksOfType(_drills, d => d.CubeGrid == grid);
      return _drills.Count >= minCount;
    }

    private bool HasGyroscope(IMyGridTerminalSystem terminalSystem, IMyCubeGrid grid)
    {
      _gyros.Clear();
      terminalSystem.GetBlocksOfType(_gyros, g => g.CubeGrid == grid);
      return _gyros.Count > 0;
    }

    private bool HasCockpit(IMyGridTerminalSystem terminalSystem, IMyCubeGrid grid)
    {
      _cockpits.Clear();
      terminalSystem.GetBlocksOfType(_cockpits, c => c.CubeGrid == grid);
      return _cockpits.Count > 0;
    }

    private bool HasAntenna(IMyGridTerminalSystem terminalSystem, IMyCubeGrid grid)
    {
      _antennas.Clear();
      terminalSystem.GetBlocksOfType(_antennas, a => a.CubeGrid == grid);
      return _antennas.Count > 0;
    }

    private bool HasThrustersAllDirections(IMyGridTerminalSystem terminalSystem, IMyCubeGrid grid, out string missingDirections)
    {
      _thrusters.Clear();
      _thrustDirs.Clear();
      terminalSystem.GetBlocksOfType(_thrusters, t => t.CubeGrid == grid);

      for (int i = 0; i < _thrusters.Count; i++)
      {
        var thrustDir = Base6Directions.GetOppositeDirection(_thrusters[i].Orientation.Forward);
        _thrustDirs.Add(thrustDir);
        if (_thrustDirs.Count == 6)
          break;
      }

      var missing = new List<string>(6);
      if (!_thrustDirs.Contains(Base6Directions.Direction.Forward))
        missing.Add("Forward");
      if (!_thrustDirs.Contains(Base6Directions.Direction.Backward))
        missing.Add("Backward");
      if (!_thrustDirs.Contains(Base6Directions.Direction.Left))
        missing.Add("Left");
      if (!_thrustDirs.Contains(Base6Directions.Direction.Right))
        missing.Add("Right");
      if (!_thrustDirs.Contains(Base6Directions.Direction.Up))
        missing.Add("Up");
      if (!_thrustDirs.Contains(Base6Directions.Direction.Down))
        missing.Add("Down");

      if (missing.Count == 0)
      {
        missingDirections = string.Empty;
        return true;
      }

      missingDirections = string.Join(", ", missing);
      return false;
    }

    private int GetMaxDirectionalDrillCount(IMyGridTerminalSystem terminalSystem, IMyCubeGrid grid)
    {
      _drills.Clear();
      terminalSystem.GetBlocksOfType(_drills, d => d.CubeGrid == grid);
      if (_drills.Count == 0)
        return 0;

      var counts = new int[6];
      for (int i = 0; i < _drills.Count; i++)
        AddDirectionalCount(counts, _drills[i].Orientation.Forward);

      return GetMaxDirectionalCount(counts);
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

    private double GetMaxAcceleration(IMyCubeGrid grid)
    {
      if (grid?.Physics == null)
        return 0d;

      var mass = (double)grid.Physics.Mass;
      if (mass <= 0d)
        return 0d;

      var maxThrust = 0d;
      maxThrust = Math.Max(maxThrust, grid.GetMaxThrustInDirection(Base6Directions.Direction.Forward));
      maxThrust = Math.Max(maxThrust, grid.GetMaxThrustInDirection(Base6Directions.Direction.Backward));
      maxThrust = Math.Max(maxThrust, grid.GetMaxThrustInDirection(Base6Directions.Direction.Left));
      maxThrust = Math.Max(maxThrust, grid.GetMaxThrustInDirection(Base6Directions.Direction.Right));
      maxThrust = Math.Max(maxThrust, grid.GetMaxThrustInDirection(Base6Directions.Direction.Up));
      maxThrust = Math.Max(maxThrust, grid.GetMaxThrustInDirection(Base6Directions.Direction.Down));

      return maxThrust / mass;
    }

    private double ComputeMissionDurationSeconds(int speedSkill0to5, string oreSubtype, double aMax, int drillCount)
    {
      var p = GetOreSpeedParams(oreSubtype);
      var mean = MissionTimeMean(speedSkill0to5, aMax, drillCount, p);
      var std = MissionTimeStd(mean, speedSkill0to5, p);
      var value = mean;

      if (std > 0d)
        value = mean + (NextGaussian() * std);

      if (double.IsNaN(value) || double.IsInfinity(value))
        value = mean;

      if (value < MinMissionTimeSeconds)
        value = MinMissionTimeSeconds;
      else if (value > MaxMissionTimeSeconds)
        value = MaxMissionTimeSeconds;

      return value;
    }

    public static double EstimateMissionTimeMeanSeconds(int speedSkill0to5, string oreSubtype, double aMax, int drillCount)
    {
      var p = GetOreSpeedParams(oreSubtype);
      return MissionTimeMean(speedSkill0to5, aMax, drillCount, p);
    }

    public static double EstimateYieldMeanUnits(int yieldSkill0to5, int overallMiningSkill0to5, int drillCount, string oreSubtype)
    {
      var p = GetOreYieldParams(oreSubtype);
      var ratio = GetMinedOreRatio(oreSubtype);
      return ComputeYieldMean(yieldSkill0to5, overallMiningSkill0to5, drillCount, ratio, p);
    }

    private static OreSpeedParams GetOreSpeedParams(string oreSubtype)
    {
      OreSpeedParams p;
      if (string.IsNullOrEmpty(oreSubtype) || !OreParams.TryGetValue(oreSubtype, out p))
      {
        if (!OreParams.TryGetValue(DefaultOreSubtype, out p))
        {
          p = new OreSpeedParams
          {
            BaseTravel_s = 300.0,
            BaseMine_s = 600.0,
            TravelDifficulty = 0.3,
            DrillExponent = DrillExponentDefault,
            Sigma0 = 0.15
          };
        }
      }

      if (p.DrillExponent <= 0d)
        p.DrillExponent = DrillExponentDefault;

      if (p.Sigma0 <= 0d)
        p.Sigma0 = 0.15;

      return p;
    }

    private static double MissionTimeMean(int speedSkill0to5, double aMax, int drillCount, OreSpeedParams p)
    {
      var s = ClampInt(speedSkill0to5, 0, 5);
      var d = Math.Max(1, drillCount);
      var accel = Math.Max(AMin, aMax);

      var fSkill = 1.0 / (1.0 + KSpeedSkill * s);
      var fAccel = 1.0 / Math.Sqrt(accel / ARef);
      var fDrills = 1.0 / Math.Pow(d, p.DrillExponent);

      var travel = p.BaseTravel_s * (1.0 + p.TravelDifficulty) * fSkill * fAccel;
      var mine = p.BaseMine_s * fSkill * fDrills;

      return travel + mine;
    }

    private static double MissionTimeStd(double meanTime, int speedSkill0to5, OreSpeedParams p)
    {
      var s = ClampInt(speedSkill0to5, 0, 5);
      var consistency = Clamp(1.0 - RSpeedSkill * s, MinVarianceFactor, 1.0);
      return meanTime * p.Sigma0 * consistency;
    }

    private static double NextGaussian()
    {
      var u1 = 1.0 - Rng.NextDouble();
      var u2 = 1.0 - Rng.NextDouble();
      return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static int ClampInt(int value, int min, int max)
    {
      if (value < min)
        return min;
      if (value > max)
        return max;
      return value;
    }

    private static double Clamp(double value, double min, double max)
    {
      if (value < min)
        return min;
      if (value > max)
        return max;
      return value;
    }

    private double ResolveMissionYieldFactor(int reliabilitySkill0to5, double missionSeconds, int ticks, Random rng, out double returnProgress, out bool missionFailed)
    {
      var pSuccess = ComputeMissionSuccessProbability(reliabilitySkill0to5, missionSeconds);
      var pTick = PerTickFailureProbability(pSuccess, ticks);
      var tFail = SampleFailureTick(pTick, ticks, rng);

      if (tFail > ticks)
      {
        returnProgress = 1.0;
        missionFailed = false;
        return 1.0;
      }

      var returnTick = Math.Min(tFail + 1, ticks);
      returnProgress = (double)returnTick / ticks;
      var yieldProgress = (double)tFail / ticks;
      missionFailed = true;
      return Math.Pow(yieldProgress, 0.8);
    }

    private static double ComputeMissionSuccessProbability(int reliabilitySkill0to5, double missionSeconds, double pMin = 0.75, double pMax = 0.995, double gamma = 1.7)
    {
      var r = ClampInt(reliabilitySkill0to5, 0, 5);
      var x = r / 5.0;
      var pBase = pMin + (pMax - pMin) * Math.Pow(x, gamma);
      var lengthFactor = MissionLengthFactor(missionSeconds, GetLengthRefSeconds(reliabilitySkill0to5));
      return Math.Min(pBase * lengthFactor, 0.999);
    }

    private static double MissionLengthFactor(double missionSeconds, double tRefSeconds, double lambda = 0.25, double fMin = 0.5)
    {
      if (missionSeconds <= tRefSeconds)
        return 1.0;

      var f = Math.Pow(tRefSeconds / missionSeconds, lambda);
      return Clamp(f, fMin, 1.0);
    }

    private static double GetLengthRefSeconds(int reliabilitySkill0to5)
    {
      var r = ClampInt(reliabilitySkill0to5, 0, 5);
      switch (r)
      {
        case 0:
          return 300.0;
        case 1:
          return 600.0;
        case 2:
          return 1200.0;
        case 3:
          return 1800.0;
        case 4:
          return 2400.0;
        default:
          return 3000.0;
      }
    }

    private static double PerTickFailureProbability(double pSuccess, int ticks)
    {
      if (ticks <= 0)
        return 1.0;

      if (pSuccess <= 0.0)
        return 1.0;

      if (pSuccess >= 1.0)
        return 0.0;

      return 1.0 - Math.Pow(pSuccess, 1.0 / ticks);
    }

    private static int SampleFailureTick(double pTick, int ticks, Random rng)
    {
      if (ticks <= 0)
        return 1;

      if (pTick <= 0.0)
        return ticks + 1;

      if (pTick >= 1.0)
        return 1;

      var u = Math.Max(rng.NextDouble(), 1e-12);
      return (int)Math.Ceiling(Math.Log(1.0 - u) / Math.Log(1.0 - pTick));
    }

    private double ComputeYieldUnits(double meanUnits, int yieldSkill0to5, string oreSubtype)
    {
      var p = GetOreYieldParams(oreSubtype);
      var std = ComputeYieldStd(meanUnits, yieldSkill0to5, p);
      var value = meanUnits;
      if (std > 0d)
        value = meanUnits + (NextGaussian() * std);

      if (double.IsNaN(value) || double.IsInfinity(value))
        value = meanUnits;

      if (value < 1d)
        value = 1d;

      return value;
    }

    public static long EstimateMissionCost(int miningSkill0to5, string oreSubtype, double missionDurationSeconds)
    {
      var skill = ClampInt(miningSkill0to5, 0, 5);
      var baseCost = BasePriceBySkill[Math.Min(skill, BasePriceBySkill.Length - 1)];
      var rarity = GetOreRarityLevel(oreSubtype);
      var rate = RatePerMinuteByRarity[Math.Min(rarity, RatePerMinuteByRarity.Length - 1)];
      var skillRate = 1.0 + PriceSkillRateMultiplier * skill;
      var minutes = Math.Max(0d, missionDurationSeconds / 60d);
      var cost = baseCost + (rate * skillRate * minutes);
      return (long)Math.Ceiling(cost);
    }

    public static double EstimateMissionSuccessProbability(int reliabilitySkill0to5, double missionSeconds)
    {
      return ComputeMissionSuccessProbability(reliabilitySkill0to5, missionSeconds);
    }

    private static int GetOreRarityLevel(string oreSubtype)
    {
      int rarity;
      if (string.IsNullOrEmpty(oreSubtype) || !OreRarity.TryGetValue(oreSubtype, out rarity))
        return 1;

      if (rarity < 1)
        return 1;
      if (rarity > 5)
        return 5;

      return rarity;
    }

    private bool TryChargeMissionCost(long identityId, long missionCost)
    {
      if (missionCost <= 0)
        return true;

      if (identityId <= 0)
        return false;

      _players.Clear();
      MyAPIGateway.Players.GetPlayers(_players, p => p.IdentityId == identityId);
      if (_players.Count == 0)
        return false;

      var player = _players[0];
      long balance;
      if (!player.TryGetBalanceInfo(out balance))
        return false;

      if (balance < missionCost)
        return false;

      player.RequestChangeBalance(-missionCost);
      return true;
    }

    private bool TryRefundMissionCost(long identityId, long refund)
    {
      if (refund <= 0)
        return true;

      if (identityId <= 0)
        return false;

      _players.Clear();
      MyAPIGateway.Players.GetPlayers(_players, p => p.IdentityId == identityId);
      if (_players.Count == 0)
        return false;

      var player = _players[0];
      player.RequestChangeBalance(refund);
      return true;
    }

    private long GetChargeIdentityId(IMyTerminalBlock block, IMyCubeGrid grid)
    {
      if (block != null)
      {
        if (block.OwnerId != 0)
          return block.OwnerId;
      }

      if (grid != null && grid.BigOwners != null && grid.BigOwners.Count > 0)
        return grid.BigOwners[0];

      return 0;
    }

    private static OreYieldParams GetOreYieldParams(string oreSubtype)
    {
      OreYieldParams p;
      if (string.IsNullOrEmpty(oreSubtype) || !OreYield.TryGetValue(oreSubtype, out p))
      {
        if (!OreYield.TryGetValue(DefaultOreSubtype, out p))
          p = new OreYieldParams { BaseYield = 1000.0, CV0 = 0.20 };
      }

      if (p.BaseYield <= 0d)
        p.BaseYield = 1000.0;
      if (p.CV0 <= 0d)
        p.CV0 = 0.20;

      return p;
    }

    private static double GetMinedOreRatio(string oreSubtype)
    {
      double ratio;
      if (string.IsNullOrEmpty(oreSubtype) || !OreMinedRatios.TryGetValue(oreSubtype, out ratio))
        return 1.0;

      return ratio;
    }

    private static int DrillCapFromMiningSkill(int overallMiningSkill0to5)
    {
      var m = ClampInt(overallMiningSkill0to5, 0, 5);
      return 1 + (int)Math.Floor(m * 7.0 / 5.0);
    }

    private static double ComputeYieldMean(int yieldSkill0to5, int overallMiningSkill0to5, int drillCount, double minedOreRatio, OreYieldParams p)
    {
      var y = ClampInt(yieldSkill0to5, 0, 5);
      var m = ClampInt(overallMiningSkill0to5, 0, 5);
      var d = Math.Max(1, drillCount);

      var dCap = DrillCapFromMiningSkill(m);
      var dEff = Math.Min(Math.Max(1, d), dCap);

      var fSkill = 1.0 + KYieldSkill * y;
      var fDrill = Math.Pow(dEff, DrillBeta);

      return p.BaseYield * minedOreRatio * fSkill * fDrill;
    }

    private static double ComputeYieldStd(double mean, int yieldSkill0to5, OreYieldParams p)
    {
      var y = ClampInt(yieldSkill0to5, 0, 5);
      var consistency = Math.Max(MinYieldVarianceFactor, 1.0 - RYieldSkill * y);
      return mean * p.CV0 * consistency;
    }

    private MissionEntry CreateEntry(IMyCubeGrid grid, bool countdown, string oreSubtype, double missionDurationSeconds)
    {
      var matrix = grid.WorldMatrix;
      var radius = (float)grid.PositionComp.WorldAABB.HalfExtents.Length();
      var duration = countdown ? CountdownSeconds : missionDurationSeconds;

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
        OreSubtype = string.IsNullOrEmpty(oreSubtype) ? DefaultOreSubtype : oreSubtype,
        MissionDurationSeconds = missionDurationSeconds > 0d ? missionDurationSeconds : MinMissionTimeSeconds,
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

      var oreSubtype = string.IsNullOrEmpty(entry.OreSubtype) ? DefaultOreSubtype : entry.OreSubtype;
      var oreUnits = entry.OreUnits > 0d ? entry.OreUnits : EstimateYieldMeanUnits(0, 0, 1, oreSubtype);
      AddOreToGrid(grid, (MyFixedPoint)oreUnits, oreSubtype);

      var status = entry.MissionFailed ? "Mission ended early." : "Mission successful.";
      MyAPIGateway.Utilities.ShowMessage("MiningMissions", status);

      var missionCost = EstimateMissionCost(entry.MiningSkill, oreSubtype, entry.MissionDurationSeconds);
      var refund = entry.FullMissionCost - missionCost;
      if (entry.MissionFailed && refund > 0)
      {
        if (TryRefundMissionCost(entry.ChargeIdentityId, refund))
          MyAPIGateway.Utilities.ShowMessage("MiningMissions", $"Refunded {refund} credits due to early return.");
        else
          MyAPIGateway.Utilities.ShowMessage("MiningMissions", "Mission complete, but refund could not be issued.");
      }
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
      var missionDuration = entry.MissionDurationSeconds > 0d ? entry.MissionDurationSeconds : MinMissionTimeSeconds;
      entry.RemainingSeconds = missionDuration;
      entry.EndFrame = MyAPIGateway.Session.GameplayFrameCounter + (long)(missionDuration * FramesPerSecond);

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

    private MyFixedPoint ComputeOreAmount(float massKg, string oreSubtype)
    {
      var def = GetOreDefinition(oreSubtype);
      if (def == null)
        return (MyFixedPoint)massKg;

      var unitMass = (float)def.Mass;
      if (unitMass <= 0f)
        return (MyFixedPoint)massKg;

      return (MyFixedPoint)(massKg / unitMass);
    }

    private bool HasCargoCapacity(IMyCubeGrid grid, MyFixedPoint amount, string oreSubtype)
    {
      var def = GetOreDefinition(oreSubtype);
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

    private void AddOreToGrid(IMyCubeGrid grid, MyFixedPoint amount, string oreSubtype)
    {
      var def = GetOreDefinition(oreSubtype);
      if (def == null)
        return;

      var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
      if (terminalSystem == null)
        return;

      _cargo.Clear();
      terminalSystem.GetBlocksOfType(_cargo, c => c.CubeGrid == grid);

      var remaining = amount;
      var oreName = string.IsNullOrEmpty(oreSubtype) ? DefaultOreSubtype : oreSubtype;
      var oreObject = new MyObjectBuilder_Ore { SubtypeName = oreName };
      var oreId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), oreName);
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

    private MyPhysicalItemDefinition GetOreDefinition(string oreSubtype)
    {
      var oreName = string.IsNullOrEmpty(oreSubtype) ? DefaultOreSubtype : oreSubtype;
      var oreId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), oreName);
      return MyDefinitionManager.Static.GetPhysicalItemDefinition(oreId);
    }

    private struct OreYieldParams
    {
      public double BaseYield;
      public double CV0;
    }

    private struct OreSpeedParams
    {
      public double BaseTravel_s;
      public double BaseMine_s;
      public double TravelDifficulty;
      public double DrillExponent;
      public double Sigma0;
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
      [ProtoMember(11)]
      public string OreSubtype;
      [ProtoMember(12)]
      public double MissionDurationSeconds;
      [ProtoMember(13)]
      public double OreUnits;
      [ProtoMember(14)]
      public bool MissionFailed;
      [ProtoMember(15)]
      public int MiningSkill;
      [ProtoMember(16)]
      public long ChargeIdentityId;
      [ProtoMember(17)]
      public long FullMissionCost;
    }
  }
}
