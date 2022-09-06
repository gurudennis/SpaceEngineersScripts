public void Main(string argument, UpdateType updateSource)
{
    Color color = Color.White;
    if (argument == "Red")
        color = Color.Red;
    else if (argument == "White")
        color = Color.White;
    else if (argument == "Yellow")
        color = Color.Yellow;

    List<IMyInteriorLight> lights = GetLocalLights();
    foreach (IMyInteriorLight light in lights)
    {
        light.Color = color;
    }
}

private List<IMyInteriorLight> GetLocalLights()
{
    List<IMyInteriorLight> lights = new List<IMyInteriorLight>();
    GridTerminalSystem.GetBlocksOfType<IMyInteriorLight>(lights, l => l.CubeGrid == Me.CubeGrid);
    return lights;
}
