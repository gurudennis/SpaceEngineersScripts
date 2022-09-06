//////////////////////////////////////////////////////////////////
//////////////////////// Configuration ///////////////////////////
//////////////////////////////////////////////////////////////////

private string Pistons = "Piston name or piston group name goes here";

//////////////////////////////////////////////////////////////////
/////////////////// Do not edit below this line //////////////////
//////////////////////////////////////////////////////////////////

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

public void Main(string argument, UpdateType updateSource)
{
    float speed = float.Parse(argument);
    foreach (IMyPistonBase piston in GetBlocks<IMyPistonBase>(Pistons, false))
    {
        piston.Velocity = speed;
    }
}
