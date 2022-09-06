//////////////////////////////////////////////////////////////////
//////////////////////// Configuration ///////////////////////////
//////////////////////////////////////////////////////////////////

// Elevator
public string ElevatorGroupName = "Elevator";
public string Tag1 = "[L]"; // apply to piston and connector
public string Tag2 = "[R]"; // ditto

// Elevator shaft (optional if there are no intermediate stops)
public string ElevatorShaftGroupName = "Elevator Shaft";

//////////////////////////////////////////////////////////////////
/////////////////// Do not edit below this line //////////////////
//////////////////////////////////////////////////////////////////

private class Actuator
{
    public Actuator(IMyPistonBase piston, IMyShipConnector connector)
    {
        Piston = piston;
        if (Piston == null)
            throw new ArgumentNullException(nameof(piston));

        Connector = connector;
        if (Connector == null)
            throw new ArgumentNullException(nameof(connector));
    }
    
    public bool IsConnected { get { return Connector.Status == MyShipConnectorStatus.Connected; } }
    public bool IsReadyToConnect { get { return Connector.Status == MyShipConnectorStatus.Connectable; } }
    public bool IsFullyExtended { get { return Piston.CurrentPosition == Piston.MaxLimit; } }
    public bool IsFullyRetracted { get { return Piston.CurrentPosition == Piston.MinLimit; } }
    
    public IMyPistonBase Piston { get; private set; }
    public IMyShipConnector Connector { get; private set; }
}

private class Elevator
{
    public Elevator(Actuator actuator1, Actuator actuator2)
    {
        _actuators[0] = actuator1;
        if (_actuators[0] == null)
            throw new ArgumentNullException(nameof(actuator1));

        _actuators[1] = actuator2;
        if (_actuators[1] == null)
            throw new ArgumentNullException(nameof(actuator2));
    }

    // public Actuator ConnectedActuator { get { return _actuators[0].IsConnected ? _actuators[0] : _actuators[1]; } }
    // public Actuator FreeActuator { get { return _actuators[0].IsConnected ? _actuators[1] : _actuators[0]; } }
    
    public bool Toggle(bool on)
    {
        if (_actuators == null)
            return false;
        
        bool ok = true;
        foreach (Actuator actuator in _actuators)
        {
            if (actuator == null && actuator.Piston == null)
            {
                ok = false;
                continue;
            }
            
            actuator.Piston.Enabled = on;
        }
        
        return ok;
    }

    private Actuator[] _actuators = new Actuator[2];
}

private class ElevatorShaftLevel
{
    public ElevatorShaftLevel(int level, IMyShipConnector connector, IMyDoor door = null)
    {
        Level = level;
        if (level == int.MinValue || level == int.MaxValue)
            throw new Exception($"Invalid elevator shaft level {level}");
        
        Connector = connector;
        if (Connector == null)
            throw new ArgumentNullException(nameof(connector));
        
        Door = door;
    }

    public int Level { get; private set; }
    public IMyShipConnector Connector { get; private set; }
    public IMyDoor Door { get; private set; }
}

private class ElevatorShaft
{
    public bool AddLevel(ElevatorShaftLevel level)
    {
        if (level == null || _levels.ContainsKey(level.Level))
            return false;

        _levels[level.Level] = level;

        return true;
    }

    private Dictionary<int, ElevatorShaftLevel> _levels = new Dictionary<int, ElevatorShaftLevel>();
}

public void Main(string argument, UpdateType updateSource)
{
    if (string.IsNullOrEmpty(argument))
    {
        ExecuteTick();
        return;
    }

    bool ok = false;
    if (argument == "up" || argument == "Up")
        ok = GoUp();
    else if (argument == "down" || argument == "Down")
        ok = GoDown();
    else if (argument == "stop" || argument == "Stop")
        ok = Stop();
    else if (argument.StartsWith("go ") || argument.StartsWith("Go "))
        ok = GoTo(int.Parse(argument.Split(' ')[1].Trim()));

    if (!ok)
        Echo($"Failed to execute command \"{argument}\"");
}

private void ForEachBlock<T>(IList<T> blocks, Action<T> action) where T : class, IMyTerminalBlock
{
    if (blocks == null || action == null)
        return;
    
    foreach (T block in blocks)
        action(block);
}

private int? GetBlockNumber(IMyTerminalBlock block)
{
    if (block == null)
        return null;
    
    string[] parts = block.CustomName.Split(' ');
    if (parts == null)
        return null;
    
    foreach (string part in parts)
    {
        string trimmedPart = part?.Trim() ?? string.Empty;
        if (trimmedPart == string.Empty || !char.IsNumber(trimmedPart[0]))
            continue;
        
        int res = 0;
        if (!int.TryParse(trimmedPart, out res))
            continue;
        
        return res;
    }
    
    return null;
}

private bool UpdateElevatorBlocks()
{
    IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(ElevatorGroupName);
    if (group == null)
    {
        Echo($"No group named \"{ElevatorGroupName}\" was found.");
        return false;
    }
    
    List<IMyTimerBlock> timers = new List<IMyTimerBlock>();
    group.GetBlocksOfType(timers);
    if (timers == null || timers.Count == 0)
    {
        Echo($"No timer found in group \"{ElevatorGroupName}\".");
        return false;
    }
    else if (timers.Count != 1)
    {
        Echo($"More than one timer found in group \"{ElevatorGroupName}\". There must only be one timer in it.");
        return false;
    }
    else
    {
        _timer = timers[0];
    }

    List<IMyPistonBase> pistons = new List<IMyPistonBase>();
    group.GetBlocksOfType(pistons);
    if (pistons.Count == 0)
    {
        Echo($"No pistons found in group \"{ElevatorGroupName}\".");
        return false;
    }
    else if (pistons.Count != 2)
    {
        Echo($"Group \"{ElevatorGroupName}\" must contain exactly two pistons, but it contains {pistons.Count}.");
        return false;
    }

    List<IMyShipConnector> connectors = new List<IMyShipConnector>();
    group.GetBlocksOfType(connectors);
    if (connectors.Count == 0)
    {
        Echo($"No connectors found in group \"{ElevatorGroupName}\".");
        return false;
    }
    else if (connectors.Count != 2)
    {
        Echo($"Group \"{ElevatorGroupName}\" must contain exactly two connectors, but it contains {connectors.Count}.");
        return false;
    }
    
    try
    {
        _elevator = new Elevator(
            new Actuator(pistons.Single(p => p.CustomName.Contains(Tag1)), connectors.Single(c => c.CustomName.Contains(Tag1))),
            new Actuator(pistons.Single(p => p.CustomName.Contains(Tag2)), connectors.Single(c => c.CustomName.Contains(Tag2))));
    }
    catch (Exception ex)
    {
        Echo($"Connectors and pistons in group \"{ElevatorGroupName}\" must be properly labeled with \"{Tag1}\" and \"{Tag2}\". {ex.Message}");
        return false;
    }
    
    group.GetBlocksOfType(_magnets);

    return true;
}

private bool UpdateElevatorShaftBlocks()
{
    // ...
    
    // List<IMyLandingGear> magnets = new List<IMyLandingGear>();
    // group.GetBlocksOfType(magnets);
    // _magnets.AddRange(magnets);
    
    return true;
}

private bool UpdateBlocks(bool force)
{
    if (_timer != null && _elevator != null) // if the essential blocks are known
    {
        if (!force)
            return true;
    }

    if (!UpdateElevatorBlocks())
    {
        Echo($"Failed to find essential elevator blocks.");
        return false;
    }
    
    if (!string.IsNullOrEmpty(ElevatorShaftGroupName) && !UpdateElevatorShaftBlocks())
        Echo($"Failed to find essential elevator shaft blocks.");

    return true;
}

private bool GoUp() { return GoTo(int.MaxValue); }
private bool GoDown() { return GoTo(int.MinValue); }

private bool GoTo(int level)
{
    if (!UpdateBlocks(true))
        return false;

    _targetLevel = level;
    _timer.StartCountdown();
    ExecuteTick();

    return true;
}

private bool Stop()
{
    UpdateBlocks(true);
    
    _targetLevel = null;
    _timer?.StopCountdown();
    _elevator?.Toggle(false);

    return true;
}

private void ExecuteTick()
{
    if (_targetLevel == null)
    {
        Stop();
        return;
    }

    if (!UpdateBlocks(false))
    {
        Stop();
        return;
    }
    
    // ...
    
    // Let go of any restraints
    ForEachBlock(_magnets, m => m.Unlock());
    _elevator.Toggle(true);
}

private int? _targetLevel = null;
private IMyTimerBlock _timer = null;
private Elevator _elevator = null;
private List<IMyLandingGear> _magnets = new List<IMyLandingGear>();
