//////////////////////////////////////////////////////////////////
//////////////////////// Configuration ///////////////////////////
//////////////////////////////////////////////////////////////////

public bool LocalTurretsOnly = true;
public bool SkipCodeYellow = true;
public VRageMath.Color ConditionGreenColor = VRageMath.Color.White;
public string Turrets = "";
public string Lights = "";
public string Connectors = "";

//////////////////////////////////////////////////////////////////
/////////////////// Do not edit below this line //////////////////
//////////////////////////////////////////////////////////////////

public void Main(string argument, UpdateType updateSource)
{
    if (string.IsNullOrEmpty(argument)) // timer activation
        ExecuteTick();
    else // manual command
        SetCondition((Condition)Enum.Parse(typeof(Condition), argument), true);
}

private enum Condition
{
    Green,   // no threats
    Yellow,  // no more detected threats after condition red
    Red      // active combat
}

private IList<T> GetBlocks<T>(string filter, bool local, Func<T, bool> criteria = null) where T : class, IMyTerminalBlock
{
    if (filter == null || filter == "null")
        return new List<T>();
    
    Func<T, bool> expandedCriteria = criteria;
    if (local)
        expandedCriteria = (b => (b.IsSameConstructAs(Me) && (criteria == null ? true : criteria(b))));

    List<T> blocks = new List<T>();
    if (filter == string.Empty || filter == "*")
    {
        GridTerminalSystem.GetBlocksOfType(blocks, expandedCriteria);
    }
    else
    {
        IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(filter);
        if (group != null)
            group.GetBlocksOfType(blocks, expandedCriteria);
        
        if (blocks.Count == 0)
        {
            T block = GridTerminalSystem.GetBlockWithName(filter) as T;
            if (block != null && (expandedCriteria == null ? true : expandedCriteria(block)))
                blocks.Add(block);
        }
    }
    
    return blocks;
}

private void ForEachBlock<T>(IList<T> blocks, Action<T> action) where T : class, IMyTerminalBlock
{
    if (blocks == null || action == null)
        return;
    
    foreach (T block in blocks)
        action(block);
}

private void ExecuteTick()
{
    ++_activationCount;
    
    UpdateTurrets();
    UpdateCondition();
}

private void UpdateTurrets()
{
    if ((_activationCount != 1) && (_activationCount % 10 != 0)) // neither first time nor time to update yet
        return;
        
    _turrets = GetBlocks<IMyLargeTurretBase>(Turrets, LocalTurretsOnly);
}

private void UpdateCondition()
{
    if (_activationCount == 1)
        SetCondition(Condition.Green, true);
    
    if (_turrets.Any(t => t.Enabled && t.HasTarget))
    {
        SetCondition(Condition.Red);
        return;
    }
    
    if (_condition == Condition.Red)
    {
        if (SkipCodeYellow)
            SetCondition(Condition.Green);
        else
            SetCondition(Condition.Yellow);
    }
}

private void UpdateLight(IMyLightingBlock light, Condition condition)
{
    if (condition == Condition.Yellow)
    {
        light.Enabled = true;
        light.Color = VRageMath.Color.Yellow;
    }
    else if (condition == Condition.Red)
    {
        light.Enabled = true;
        light.Color = VRageMath.Color.Red;
    }
    else
    {
        light.Enabled = true;
        light.Color = ConditionGreenColor;
    }
}

private void UpdateConnector(IMyShipConnector connector, Condition condition)
{
    try
    {
        if (condition == Condition.Red) // connect if we possibly can
        {
            if (connector.Status == MyShipConnectorStatus.Connectable)
                connector.Connect();
        }
        else // disconnect if the other side is connected by landing gears and isn't on the same grid; if own connector is for parking, it is exempt
        {
            if (connector.Status != MyShipConnectorStatus.Connected || connector.IsParkingEnabled || !connector.OtherConnector.IsParkingEnabled || connector.OtherConnector.IsSameConstructAs(Me))
                return;
            
            if (GetBlocks<IMyLandingGear>(string.Empty, false, g => g.IsSameConstructAs(connector.OtherConnector) && g.IsParkingEnabled && g.IsLocked).Any()) // has a connected parking landing gear
                connector.Disconnect();
        }
    }
    catch (Exception ex)
    {
    }
}

private void SetCondition(Condition condition, bool force = false)
{
    if (!force)
    {
        if (condition == _condition)
            return;
        
        if (_sticky && condition != Condition.Red)
            return;
    }

    Echo($"Condition {condition}");
    
    ForEachBlock(GetBlocks<IMyLightingBlock>(Lights, true), light => UpdateLight(light, condition));
    ForEachBlock(GetBlocks<IMyShipConnector>(Connectors, true), connector => UpdateConnector(connector, condition));
    
    _condition = condition;
    _sticky = (force && _condition != Condition.Green);
}

private Condition _condition;
private bool _sticky;
private IList<IMyLargeTurretBase> _turrets;
private ulong _activationCount;
