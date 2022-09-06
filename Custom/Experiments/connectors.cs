public void Main(string argument, UpdateType updateSource)
{
    List<IMyShipConnector> conns = GetUnusedLocalConnectors();
    foreach (IMyShipConnector conn in conns)
    {
        Echo($"Disabling \"{conn.CustomName}\"");
        conn.Enabled = false;
    }
}

private bool IsUnusedLocalConnector(IMyShipConnector conn)
{
    return conn.Status == MyShipConnectorStatus.Unconnected; // this excludes all connected or connectable ones
}

private List<IMyShipConnector> GetUnusedLocalConnectors()
{
    List<IMyShipConnector> conns = new List<IMyShipConnector>();
    GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(conns, c => c.CubeGrid == Me.CubeGrid && IsUnusedLocalConnector(c));
    return conns;
}
