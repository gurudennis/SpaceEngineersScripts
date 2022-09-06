//////////////////////////////////////////////////////////////////
//////////////////////// Configuration ///////////////////////////
//////////////////////////////////////////////////////////////////

// These can be group names, block names, "" for default
// (parking-designated where appropriate), "*" for all,
// or null for none.
private string Connectors = "";
private string LandingGears = "";
private string Antennas = "";
private string Batteries = "";
private string HydrogenTanks = "";
private string OxygenTanks = "";
private string Thrusters_1 = "";
private string Thrusters_2 = null;
private string Thrusters_3 = null;
private string Gyroscopes = ""; // consider setting to null for atmo ships for safety reasons
private string OreDetectors = "";
private string Lights = "";
private string ExtrasToggle = null; // useful for unconventional and modded blocks
private string ExtrasOffOnDock = null; // useful for e.g. miner sorters

// If set to false, allows docking with landing gears only.
private bool RequireConnector = true;

//////////////////////////////////////////////////////////////////
/////////////////// Do not edit below this line //////////////////
//////////////////////////////////////////////////////////////////

private bool IsDefault(string filter)
{
    return filter == string.Empty;
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

void EnableBlock(IMyTerminalBlock block, bool enable)
{
    block?.ApplyAction(enable ? "OnOff_On" : "OnOff_Off");
}

private bool IsHydrogenTank(IMyGasTank tank)
{
    return tank?.BlockDefinition.SubtypeId.Contains("Hydro") ?? false;
}

private IList<IMyShipConnector> GetConnectors()
{
    IList<IMyShipConnector> connectors = GetBlocks<IMyShipConnector>(Connectors, true);
    return IsDefault(Connectors) ? connectors.Where(c => c.IsParkingEnabled).ToList() : connectors;
}

private IList<IMyLandingGear> GetLandingGears()
{
    IList<IMyLandingGear> landingGears = GetBlocks<IMyLandingGear>(LandingGears, true);
    return IsDefault(LandingGears) ? landingGears.Where(c => c.IsParkingEnabled).ToList() : landingGears;
}

private IList<IMyRadioAntenna> GetAntennas()
{
    return GetBlocks<IMyRadioAntenna>(Antennas, true);
}

private IList<IMyBatteryBlock> GetBatteries()
{
    return GetBlocks<IMyBatteryBlock>(Batteries, true);
}

private IList<IMyGasTank> GetHydrogenTanks()
{
    return GetBlocks<IMyGasTank>(HydrogenTanks, true, t => IsHydrogenTank(t));
}

private IList<IMyGasTank> GetOxygenTanks()
{
    return GetBlocks<IMyGasTank>(OxygenTanks, true, t => !IsHydrogenTank(t));
}

private IList<IMyThrust> GetThrusters(int ordinal)
{
    string filter = (ordinal == 3 ? Thrusters_3 : (ordinal == 2 ? Thrusters_2 : Thrusters_1));
    return GetBlocks<IMyThrust>(filter, true);
}

private IList<IMyGyro> GetGyroscopes()
{
    return GetBlocks<IMyGyro>(Gyroscopes, true);
}

private IList<IMyOreDetector> GetOreDetectors()
{
    return GetBlocks<IMyOreDetector>(OreDetectors, true);
}

private IList<IMyLightingBlock> GetLights()
{
    return GetBlocks<IMyLightingBlock>(Lights, true);
}

private IList<IMyTerminalBlock> GetExtrasToggle()
{
    return GetBlocks<IMyTerminalBlock>(ExtrasToggle, true);
}

private IList<IMyTerminalBlock> GetExtrasOffOnDock()
{
    return GetBlocks<IMyTerminalBlock>(ExtrasOffOnDock, true);
}

public void Main(string argument, UpdateType updateSource)
{
    IList<IMyShipConnector> connectors = GetConnectors();
    IList<IMyLandingGear> landingGears = GetLandingGears();
    bool isDocked = (connectors.Any(c => c.Status == MyShipConnectorStatus.Connected) || landingGears.Any(g => g.IsLocked));
    bool isConnectorDockable = !isDocked && connectors.Any(c => c.Status == MyShipConnectorStatus.Connectable);
    bool isDockable = !isDocked && (isConnectorDockable || landingGears.Any(g => g.LockMode == LandingGearMode.ReadyToLock));
    if (isDockable && !isConnectorDockable && RequireConnector)
        isDockable = false;
    
    const int thrusterCount = 3;

    bool goingToDock = false;
    bool goingToUndock = false;
    
    if (argument == "Dock")
    {
        if (isDockable)
            goingToDock = true;
    }
    else if (argument == "Undock")
    {
        if (isDocked)
            goingToUndock = true;
    }
    else
    {
        if (isDocked)
            goingToUndock = true;
        else if (isDockable)
            goingToDock = true;
    }
    
    if (!goingToDock && !goingToUndock)
    {
        Echo("Not ready.");
        return;
    }
    else if (goingToDock)
    {
        Echo("Docking...");
        
        ForEachBlock(GetExtrasOffOnDock(), e => EnableBlock(e, false));

        ForEachBlock(GetExtrasToggle(), e => EnableBlock(e, false));
        ForEachBlock(GetAntennas(), a => EnableBlock(a, false));
        ForEachBlock(GetGyroscopes(), g => EnableBlock(g, false));
        ForEachBlock(GetOreDetectors(), d => EnableBlock(d, false));
        ForEachBlock(GetLights(), l => EnableBlock(l, false));
        for (int i = 1; i <= thrusterCount; ++i)
            ForEachBlock(GetThrusters(i), t => EnableBlock(t, false));

        ForEachBlock(GetHydrogenTanks(), t => t.Stockpile = true);
        ForEachBlock(GetOxygenTanks(), t => t.Stockpile = true);
        ForEachBlock(GetBatteries(), b => b.ChargeMode = ChargeMode.Recharge);
        
        ForEachBlock(landingGears, g => g.Lock());
        
        ForEachBlock(connectors, c => c.Connect()); // must be the last action
    }
    else if (goingToUndock)
    {
        Echo("Undocking...");

        ForEachBlock(GetExtrasToggle(), e => EnableBlock(e, true));
        ForEachBlock(GetAntennas(), a => EnableBlock(a, true));
        ForEachBlock(GetGyroscopes(), g => EnableBlock(g, true));
        ForEachBlock(GetOreDetectors(), d => EnableBlock(d, true));
        ForEachBlock(GetLights(), l => EnableBlock(l, true));
        for (int i = 1; i <= thrusterCount; ++i)
            ForEachBlock(GetThrusters(i), t => EnableBlock(t, true));

        ForEachBlock(GetBatteries(), b => b.ChargeMode = ChargeMode.Auto);
        ForEachBlock(GetHydrogenTanks(), t => t.Stockpile = false);
        ForEachBlock(GetOxygenTanks(), t => t.Stockpile = false);

        ForEachBlock(landingGears, g => g.Unlock());
        
        ForEachBlock(connectors, c => c.Disconnect()); // must be the last action
    }

    Echo("Done.");
}
