///////////////////////////////////////
// Configuration
///////////////////////////////////////

private static bool EnablePowerManagement = true;
private static bool PreferToSpendHydrogenOverUranium = false;
private static int MinHydrogenReservePercent = 10;

private static int PanicEnergyLevelPercent = 5;
private static int PanicTimeToZeroEnergySec = 15 * 60;

private static string InfoLCDs = "Power Management LCD (Goliath)";
private static string StatusLights = "Power Management Light (Goliath)";

///////////////////////////////////////
// Code - do not modify
///////////////////////////////////////

public abstract class Script
{
    public void Run(MyGridProgram program, string argument, UpdateType updateSource)
    {
        try
        {
            Program = program;
            
            if (Program.Runtime.UpdateFrequency != UpdateFrequency.None && (updateSource & UpdateTypeFromUpdateFrequency(Program.Runtime.UpdateFrequency)) != 0)
                OnTickScript();
            else
                SetUpScript(argument, updateSource);
        }
        catch (Exception ex)
        {
            Clear();
            Print($"Fatal error: {ex.Message}", Severity.Error);
            TearDownScript();
            throw;
        }
    }
    
    protected abstract void OnSetUp(MyCommandLine cmdLine, UpdateType updateSource);
    protected virtual void OnTearDown(bool ok) { }
    protected virtual void OnDiscover() { }
    protected virtual void OnTick(uint tick) { }
    
    protected enum Severity
    {
        Info,
        Warning,
        Error
    }
    protected virtual void Print(string message, Severity severity = Severity.Info) { Program.Echo(message); }
    protected virtual void Clear() { Print(""); }
    
    protected void Start(UpdateFrequency updateFreq, uint tickPeriod = 1, uint discoveryPeriod = 0)
    {
        Stop(true);
        _tick = 0;
        _tickPeriod = tickPeriod;
        _discoveryPeriod = discoveryPeriod;
        Program.Runtime.UpdateFrequency = updateFreq;
    }
    
    protected void Stop(bool ok = false)
    {
        TearDownScript(ok);
    }

    protected MyGridProgram Program { get; private set;}
    protected MyGridProgram P { get { return Program; } }
    protected MyCommandLine CmdLine { get; private set; }
    
    public enum Location
    {
        Everywhere,
        SameGrid,
        OtherGrids
    }
    public enum State
    {
        Any,
        Functional,
        NonFunctional
    }
    protected IList<T> GetBlocks<T>(string filter = "", Location location = Location.Everywhere, State state = State.Functional, Func<T, bool> criteria = null) where T : class, IMyTerminalBlock
    {
        if (filter == null || filter == "null")
            return new List<T>();
        
        Func<T, bool> expandedCriteria1 = criteria;
        if (location == Location.SameGrid)
            expandedCriteria1 = (b => (b.IsSameConstructAs(Program.Me) && (criteria == null ? true : criteria(b))));
        else if (location == Location.OtherGrids)
            expandedCriteria1 = (b => (!b.IsSameConstructAs(Program.Me) && (criteria == null ? true : criteria(b))));
        
        Func<T, bool> expandedCriteria2 = expandedCriteria1;
        if (state == State.Functional)
            expandedCriteria2 = (b => (b.IsFunctional && (expandedCriteria1 == null ? true : expandedCriteria1(b))));
        else if (state == State.NonFunctional)
            expandedCriteria2 = (b => (!b.IsFunctional && (expandedCriteria1 == null ? true : expandedCriteria1(b))));

        List<T> blocks = new List<T>();
        if (filter == string.Empty || filter == "*")
        {
            Program.GridTerminalSystem.GetBlocksOfType(blocks, expandedCriteria2);
        }
        else
        {
            IMyBlockGroup group = Program.GridTerminalSystem.GetBlockGroupWithName(filter);
            if (group != null)
                group.GetBlocksOfType(blocks, expandedCriteria2);
            
            if (blocks.Count == 0)
            {
                T block = Program.GridTerminalSystem.GetBlockWithName(filter) as T;
                if (block != null && (expandedCriteria2 == null ? true : expandedCriteria2(block)))
                    blocks.Add(block);
            }
        }
        
        return blocks;
    }
    
    protected static void ForEachBlock<T>(IList<T> blocks, Action<T> action) where T : class, IMyTerminalBlock
    {
        if (blocks == null || action == null)
            return;
        
        foreach (T block in blocks)
            action(block);
    }
    
    protected class RunningAverage
    {
        public RunningAverage(float initialValue = 0.0f) { _avg = initialValue; }
        public float AddHypothetical(float value, float weight) { return (weight * value) + (1.0f - weight) * _avg; }
        public void Add(float value, float weight) { Set(AddHypothetical(value, weight)); }
        public void Set(float value) { _avg = value; }
        public float Get() { return _avg; }
        private float _avg;
    }
    
    private void SetUpScript(string argument, UpdateType updateSource)
    {
        CmdLine = new MyCommandLine();
        if (!string.IsNullOrEmpty(argument) && !CmdLine.TryParse(argument))
            throw new Exception($"Failed to parse command line: {argument}");
        
        OnSetUp(CmdLine, updateSource);
        OnDiscover();
    }

    private void TearDownScript(bool ok = false)
    {
        Program.Runtime.UpdateFrequency = UpdateFrequency.None;
        _tick = 0;
        _tickPeriod = 0;
        _discoveryPeriod = 0;
        OnTearDown(ok);
    }

    private void OnTickScript()
    {
        ++_tick;
        
        if (_discoveryPeriod > 0 && (_tick % _discoveryPeriod) == 0)
            OnDiscover();
        
        if (_tickPeriod > 0 && (_tick % _tickPeriod) == 0)
            OnTick(_tick / _tickPeriod);
    }
    
    private UpdateType UpdateTypeFromUpdateFrequency(UpdateFrequency updateFreq)
    {
        if (updateFreq == UpdateFrequency.Update1)
            return UpdateType.Update1;
        else if (updateFreq == UpdateFrequency.Update10)
            return UpdateType.Update10;
        else if (updateFreq == UpdateFrequency.Update100)
            return UpdateType.Update100;
        
        return UpdateType.None;
    }
    
    private uint _tickPeriod = 0;
    private uint _discoveryPeriod = 0;
    private uint _tick = 0;
}

public class PowerManagementScript : Script
{
    protected override void OnSetUp(MyCommandLine cmdLine, UpdateType updateSource)
    {
        Start(UpdateFrequency.Update100, 3, 9);
    }
    
    protected override void OnDiscover()
    {
        _ownBatteries.Batteries?.Clear();
        _attachedBatteries.Batteries?.Clear();
        _wind?.Clear();
        _solar?.Clear();
        _hydrogenEngines?.Clear();
        _reactors?.Clear();
        _lcds?.Clear();
        
        _ownBatteries.Batteries = GetBlocks<IMyBatteryBlock>(string.Empty, Location.SameGrid);
        _attachedBatteries.Batteries = GetBlocks<IMyBatteryBlock>(string.Empty, Location.OtherGrids);
        _wind = GetBlocks<IMyWindTurbine>(string.Empty, Location.SameGrid);
        _solar = GetBlocks<IMySolarPanel>(string.Empty, Location.SameGrid);
        _hydrogenEngines = GetBlocks<IMyPowerProducer>(string.Empty, Location.SameGrid, State.Functional, b => b.BlockDefinition.ToString().Contains("HydrogenEngine"));
        _reactors = GetBlocks<IMyReactor>(string.Empty, Location.SameGrid);
        _lcds = GetBlocks<IMyTextPanel>(InfoLCDs, Location.SameGrid);

        if (_lcds.Count == 0)
            Stop(true);
    }

    protected override void OnTearDown(bool ok)
    {
        Clear();
    }

    protected override void OnTick(uint tick)
    {
        PowerSummary powerSummary = GetPowerSummary();

        PrintPowerSummary(powerSummary, _lcds, tick);
        
        ManagePower(powerSummary);
    }
    
    private void ManagePower(PowerSummary powerSummary)
    {
        if (!EnablePowerManagement)
            return;
        
        int secondsToFullEnergy = powerSummary.LocalBatteries.GetSecondsToFullEnergy();
        PowerStateMachine.Status status = _powerStateMachine.Update(secondsToFullEnergy, powerSummary.LocalBatteries.EnergyPercent);
        
        Print($"Energy level: {powerSummary.LocalBatteries.EnergyPercent}%");
        Print($"Time to full: {GetTimeStr(secondsToFullEnergy)}");
        Print($"Status: {status}");
        
        // ...
    }
    
    private void PrintPowerSummary(PowerSummary powerSummary, IList<IMyTextPanel> lcds, uint tick)
    {
        if (lcds.Count == 0)
            return;
        
        StringBuilder text = new StringBuilder();
        
        text.AppendLine("Local batteries:");
        AppendBatteryListStatus(text, powerSummary.LocalBatteries);
        
        text.AppendLine();
        
        text.AppendLine("Connected batteries:");
        AppendBatteryListStatus(text, powerSummary.AttachedBatteries);
        
        text.AppendLine();
        
        text.AppendLine("Local power production:");
        AppendPowerProducerListStatus(text, powerSummary.WindTurbines, "Wind");
        AppendPowerProducerListStatus(text, powerSummary.SolarPanels, "Solar");
        AppendPowerProducerListStatus(text, powerSummary.HydrogenEngines, "Hydrogen");
        AppendPowerProducerListStatus(text, powerSummary.NuclearReactors, "Nuclear");
        
        text.AppendLine();
        
        for (uint i = 0; i != ((tick % 3) + 1); ++i)
            text.Append('.');

        text.AppendLine();

        ForEachBlock(lcds, lcd => lcd.WriteText(text, false));
    }
    
    private void AppendBatteryListStatus(StringBuilder text, PowerStats stats)
    {
        if (stats.Count == 0)
        {
            text.AppendLine("  (none)");
            return;
        }

        string balanceInOutSign = (stats.BalanceInOut >= 0.01f) ? "+" : " ";
        
        bool? isBalanceInOutSubstantiallyPositive = stats.IsBalanceInOutSubstantiallyPositive();
        string chargedStrValue = GetTimeStr(Math.Abs(stats.GetSecondsToFullEnergy()));
        string chargedStr = (isBalanceInOutSubstantiallyPositive == null) ? "  No substantial change." :
            ((bool)isBalanceInOutSubstantiallyPositive ? $"  Full in {chargedStrValue}." : $"  Empty in {chargedStrValue}.");
        
        text.Append("  ");
        AppendPercentageBar(text, stats.EnergyPercent, 50, isBalanceInOutSubstantiallyPositive);

        text.AppendLine($"  Stored: {stats.EnergyPercent}% ({stats.CurEnergy:0.#} / {stats.MaxEnergy:0.#} MWh)");
        text.AppendLine($"  Trend: {balanceInOutSign}{stats.BalanceInOut:0.##} MW ({stats.CurInput:0.#} in / {stats.CurOutput:0.#} out)");
        text.AppendLine(chargedStr);
    }
    
    private void AppendPowerProducerListStatus(StringBuilder text, PowerStats stats, string type)
    {
        if (stats.Count == 0)
            return;

        text.AppendLine($"  {type} ({stats.Count}): +{stats.CurOutput:0.##}/{stats.MaxOutput:0.##} MW ({stats.OutputPercent}%)");
    }
    
    private static bool? IsSubstantiallyPositive(float value, float deviation = 0.02f)
    {
        if (value > -deviation && value < deviation)
            return null;
        
        return value > 0.0f;
    }
    
    private void AppendPercentageBar(StringBuilder text, int percent, int width, bool? growing = null)
    {
        if (percent < 0)
            percent = 0;
        else if (percent > 100)
            percent = 100;
        
        text.Append("[");
        
        for (int i = 0; i < width; ++i)
        {
            char c = ' ';
            int threshold = percent / (100 / width);
            bool isOnlySymbol = (growing != null && i == 0 && i == threshold);
            if (i < threshold || isOnlySymbol)
            {
                if (growing != null && (i == threshold - 1 || isOnlySymbol))
                {
                    c = ((bool)growing) ? '>' : '<';
                }
                else
                {
                    c = '-';
                }
            }
            else
            {
                c = ' ';
            }
            
            text.Append(c);
        }

        text.AppendLine("]");
    }
    
    private string GetTimeStr(int seconds)
    {
        string prefix = string.Empty;
        if (seconds < 0)
        {
            prefix = "-";
            seconds = Math.Abs(seconds);
        }
        
        if (seconds < 60)
            return seconds == 1 ? $"{prefix}1 second" : $"{prefix}{seconds} seconds";
        
        int minutes = seconds / 60;
        if (minutes < 60)
            return minutes == 1 ? $"{prefix}1 minute" : $"{prefix}{minutes} minutes";
        
        int hours = minutes / 60;
        if (hours < 24)
            return hours == 1 ? $"{prefix}1 hour" : $"{prefix}{hours} hours";
        
        int days = hours / 24;
        return days == 1 ? $"{prefix}1 day" : $"{prefix}{days} days";
    }

    private class BatteryGroup
    {
        public IList<IMyBatteryBlock> Batteries;
        public RunningAverage AvgInput = new RunningAverage();
        public RunningAverage AvgOutput = new RunningAverage();
    }
    
    private class PowerStats
    {
        public int Count { get; set; }
        public float MaxEnergy { get; set; }
        public float CurEnergy { get; set; }
        public float CurInput { get; set; }
        public float CurOutput { get; set; }
        public float MaxOutput { get; set; }
        public int EnergyPercent { get { return Count == 0 || MaxEnergy == 0.0f ? 0 : (int)(CurEnergy * 100.0f / MaxEnergy); } }
        public int OutputPercent { get { return Count == 0 || MaxOutput == 0.0f ? 0 : (int)(CurOutput * 100.0f / MaxOutput); } }
        public float BalanceInOut { get { return CurInput - CurOutput; } }
        
        public bool? IsBalanceInOutSubstantiallyPositive()
        {
            if (Math.Abs(GetSecondsToFullEnergy()) >= MaxSignificantSeconds)
                return null;
            
            return PowerManagementScript.IsSubstantiallyPositive(BalanceInOut, 0.02f);
        }
        
        public int GetSecondsToFullEnergy()
        {
            if (Count == 0 || MaxEnergy == 0.0f || IsSubstantiallyPositive(BalanceInOut, 0.0001f) == null)
                return 0;
            
            bool upwardTrend = (BalanceInOut > 0.0f);
            float leftToGoInMWSec = (upwardTrend ? (MaxEnergy - CurEnergy) : CurEnergy) * 60.0f * 60.0f;
            int result = (int)(leftToGoInMWSec / BalanceInOut);
            if (result > MaxSignificantSeconds)
                result = MaxSignificantSeconds;
            else if (result < -MaxSignificantSeconds)
                result = -MaxSignificantSeconds;
            
            return result;
        }
    }
    
    private PowerStats GetPowerStats(BatteryGroup batteryGroup)
    {
        PowerStats stats = new PowerStats();
        
        if (batteryGroup.Batteries.Count == 0)
            return stats;

        ForEachBlock(batteryGroup.Batteries, battery =>
        {
            ++stats.Count;
            stats.MaxEnergy += battery.MaxStoredPower;
            stats.CurEnergy += battery.CurrentStoredPower;
            stats.CurInput += battery.CurrentInput;
            stats.CurOutput += battery.CurrentOutput;
        });
        
        batteryGroup.AvgInput.Add(stats.CurInput, 0.33f);
        batteryGroup.AvgOutput.Add(stats.CurOutput, 0.33f);
        
        stats.CurInput = batteryGroup.AvgInput.Get();
        stats.CurOutput = batteryGroup.AvgOutput.Get();
        
        return stats;
    }
    
    private PowerStats GetPowerStats(IList<IMyPowerProducer> producers)
    {
        PowerStats stats = new PowerStats();
        
        if (producers.Count == 0)
            return stats;

        ForEachBlock(producers, producer =>
        {
            ++stats.Count;
            stats.CurOutput += producer.CurrentOutput;
            stats.MaxOutput += producer.MaxOutput;
        });
        
        return stats;
    }
    
    private class PowerSummary
    {
        public PowerStats LocalBatteries { get; set; }
        public PowerStats AttachedBatteries { get; set; }
        public PowerStats WindTurbines { get; set; }
        public PowerStats SolarPanels { get; set; }
        public PowerStats HydrogenEngines { get; set; }
        public PowerStats NuclearReactors { get; set; }
    }
    
    private PowerSummary GetPowerSummary()
    {
        PowerSummary summary = new PowerSummary();
        summary.LocalBatteries = GetPowerStats(_ownBatteries);
        summary.AttachedBatteries = GetPowerStats(_attachedBatteries);
        summary.WindTurbines = GetPowerStats(_wind.Cast<IMyPowerProducer>().ToList());
        summary.SolarPanels = GetPowerStats(_solar.Cast<IMyPowerProducer>().ToList());
        summary.HydrogenEngines = GetPowerStats(_hydrogenEngines.Cast<IMyPowerProducer>().ToList());
        summary.NuclearReactors = GetPowerStats(_reactors.Cast<IMyPowerProducer>().ToList());
        return summary;
    }
    
    private const int MaxSignificantSeconds = 31 * 24 * 60 * 60; // a month

    private BatteryGroup _ownBatteries = new BatteryGroup();
    private BatteryGroup _attachedBatteries = new BatteryGroup();
    private IList<IMyWindTurbine> _wind;
    private IList<IMySolarPanel> _solar;
    private IList<IMyPowerProducer> _hydrogenEngines;
    private IList<IMyReactor> _reactors;
    private IList<IMyTextPanel> _lcds;
    private PowerStateMachine _powerStateMachine = new PowerStateMachine(PanicEnergyLevelPercent, PanicTimeToZeroEnergySec);
}

class PowerStateMachine
{
    public PowerStateMachine(int panicEnergyLevelPercent, int panicTimeToZeroEnergySec)
    {
        _states[0].secondsDown = panicTimeToZeroEnergySec;
        _states[0].percentDown = panicEnergyLevelPercent;
        _states[0].percentUp = panicEnergyLevelPercent + 5;
        
        _states[1].secondsDown = panicTimeToZeroEnergySec * 2;
        _states[1].percentDown = panicEnergyLevelPercent + 5;
        _states[1].percentUp = panicEnergyLevelPercent + 10;
        
        _states[2].secondsDown = panicTimeToZeroEnergySec * 3;
        _states[2].percentDown = panicEnergyLevelPercent + 15;
        _states[2].percentUp = panicEnergyLevelPercent + 20;
        
        _status.State = State.Green;
        _status.CyclesInState = 0;
        StateChanged = false;
    }
    
    public enum State
    {
        Red,
        Yellow,
        Green
    }
    
    public struct Status
    {
        public State State { get; set; }
        public int CyclesInState { get; set; }
        public override string ToString() { return $"State={State}, CyclesInState={CyclesInState}"; }
    }
    
    public Status Update(int secondsToFullEnergy, int energyStoredPercent)
    {
        StateChanged = false;
        
        for (int i = 0; i < _stateCount; ++i)
        {
            if (ShouldSwitchToState((State)i, secondsToFullEnergy, energyStoredPercent))
            {
                _status.State = (State)i;
                _status.CyclesInState = 0;
                StateChanged = true;
                return _status;
            }
        }
        
        ++_status.CyclesInState;
        
        return _status;
    }
    
    public Status GetStatus() { return _status; }
    public bool StateChanged { get; private set; }
    
    private bool ShouldSwitchToState(State state, int secondsToFullEnergy, int energyStoredPercent)
    {
        if (state == _status.State)
            return false;
        
        StateInfo info = _states[(int)state];
        
        bool up = ((int)state) > ((int)_status.State);
        int secondsThreshold = up ? 0 : info.secondsDown;
        int percentThreshold = up ? info.percentUp : info.percentDown;
        Func<int, int, bool> comparer = up ?
            new Func<int, int, bool>((v1, v2) => { return v1 >= v2; }) :
            new Func<int, int, bool>((v1, v2) => { return v1 <= v2; });
        
        if (comparer(Math.Abs(secondsToFullEnergy), secondsThreshold) || comparer(energyStoredPercent, percentThreshold))
            return true;
        
        return false;
    }
    
    private struct StateInfo
    {
        public int secondsDown;
        public int percentDown;
        public int percentUp;
    }
    
    private const int _stateCount = ((int)State.Green) + 1;
    private readonly StateInfo[] _states = new StateInfo[_stateCount];
    private Status _status = new Status();
}

public void Main(string argument, UpdateType updateSource)
{
    _script.Run(this, argument, updateSource);
}

private PowerManagementScript _script = new PowerManagementScript();
