///////////////////////////////////////
// Configuration
///////////////////////////////////////

private static string InfoLCDs = "Base Power Management LCD";

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
                SetUpScript(argument);
        }
        catch (Exception ex)
        {
            Clear();
            Print($"Fatal error: {ex.Message}", Severity.Error);
            TearDownScript();
            throw;
        }
    }
    
    protected abstract void OnSetUp();
    protected virtual void OnTearDown(bool ok) { }
    protected virtual void OnDiscover() { }
    protected virtual void OnTick() { }
    
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
    protected IList<T> GetBlocks<T>(string filter, Location location, Func<T, bool> criteria = null) where T : class, IMyTerminalBlock
    {
        if (filter == null || filter == "null")
            return new List<T>();
        
        Func<T, bool> expandedCriteria = criteria;
        if (location == Location.SameGrid)
            expandedCriteria = (b => (b.IsSameConstructAs(Program.Me) && (criteria == null ? true : criteria(b))));
        else if (location == Location.OtherGrids)
            expandedCriteria = (b => (!b.IsSameConstructAs(Program.Me) && (criteria == null ? true : criteria(b))));

        List<T> blocks = new List<T>();
        if (filter == string.Empty || filter == "*")
        {
            Program.GridTerminalSystem.GetBlocksOfType(blocks, expandedCriteria);
        }
        else
        {
            IMyBlockGroup group = Program.GridTerminalSystem.GetBlockGroupWithName(filter);
            if (group != null)
                group.GetBlocksOfType(blocks, expandedCriteria);
            
            if (blocks.Count == 0)
            {
                T block = Program.GridTerminalSystem.GetBlockWithName(filter) as T;
                if (block != null && (expandedCriteria == null ? true : expandedCriteria(block)))
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
    
    private void SetUpScript(string argument)
    {
        CmdLine = new MyCommandLine();
        if (!string.IsNullOrEmpty(argument) && !CmdLine.TryParse(argument))
            throw new Exception($"Failed to parse command line: {argument}");
        
        OnSetUp();
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
            OnTick();
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

class PowerManagementScript : Script
{
    protected override void OnSetUp()
    {
        Start(UpdateFrequency.Update100, 3, 6);
    }
    
    protected override void OnDiscover()
    {
        _ownBatteries.Batteries?.Clear();
        _attachedBatteries.Batteries?.Clear();
        _lcds?.Clear();
        
        _ownBatteries.Batteries = GetBlocks<IMyBatteryBlock>(string.Empty, Location.SameGrid, b => b.IsFunctional);
        _attachedBatteries.Batteries = GetBlocks<IMyBatteryBlock>(string.Empty, Location.OtherGrids, b => b.IsFunctional);
        _lcds = GetBlocks<IMyTextPanel>(InfoLCDs, Location.SameGrid);

        if (_lcds.Count == 0)
            Stop(true);
    }

    protected override void OnTearDown(bool ok)
    {
        Clear();
    }

    protected override void OnTick()
    {
        StringBuilder text = new StringBuilder();
        
        text.AppendLine("Local batteries:");
        AppendBatteryListStatus(text, _ownBatteries);
        
        text.AppendLine();
        
        text.AppendLine("Connected batteries:");
        AppendBatteryListStatus(text, _attachedBatteries);

        ForEachBlock(_lcds, lcd => lcd.WriteText(text, false));
    }
    
    private void AppendBatteryListStatus(StringBuilder text, BatteryGroup batteryGroup)
    {
        if (batteryGroup.Batteries.Count == 0)
        {
            text.AppendLine("  (none)");
            return;
        }
        
        float maxEnergy = 0.0f;
        float curEnergy = 0.0f;
        float curInput = 0.0f;
        float curOutput = 0.0f;
        ForEachBlock(batteryGroup.Batteries, battery =>
        {
            maxEnergy += battery.MaxStoredPower;
            curEnergy += battery.CurrentStoredPower;
            curInput += battery.CurrentInput;
            curOutput += battery.CurrentOutput;
        });
        
        int storedPercent = (int)(curEnergy * 100.0f / maxEnergy);
        
        batteryGroup.AvgInput.Add(curInput, 0.25f);
        batteryGroup.AvgOutput.Add(curOutput, 0.25f);
        curInput = batteryGroup.AvgInput.Get();
        curOutput = batteryGroup.AvgOutput.Get();
        
        float balanceInOut = curInput - curOutput;
        string balanceInOutSign = (balanceInOut >= 0.01f) ? "+" : " ";

        text.AppendLine($"  Stored: {storedPercent}% ({curEnergy:0.#} / {maxEnergy:0.#} MWh)");
        text.AppendLine($"  Trend: {balanceInOutSign}{balanceInOut:0.##} MW ({curInput:0.#} in / {curOutput:0.#} out)");
    }
    
    private class BatteryGroup
    {
        public IList<IMyBatteryBlock> Batteries;
        public RunningAverage AvgInput = new RunningAverage();
        public RunningAverage AvgOutput = new RunningAverage();
    }

    private BatteryGroup _ownBatteries = new BatteryGroup();
    private BatteryGroup _attachedBatteries = new BatteryGroup();
    private IList<IMyTextPanel> _lcds;
}

public void Main(string argument, UpdateType updateSource)
{
    _script.Run(this, argument, updateSource);
}

private PowerManagementScript _script = new PowerManagementScript();
