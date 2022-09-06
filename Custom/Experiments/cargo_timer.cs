//
// Edit the following strings to configure the script.
//

private static readonly string ContainerName = "Large Cargo Container (Base) (Ice Staging)";
private static readonly string TimerName = "Test Timer (Base)";

//
// No editing beyond this point.
//

private bool DoesCargoContainIce(IMyCargoContainer cargo)
{
    List<MyInventoryItem> items = new List<MyInventoryItem>();
    cargo.GetInventory(0).GetItems(items);

    foreach (MyInventoryItem item in items)
    {
        if (item.Type.ToString().Contains("Ice"))
            return true;
    }

    return false;
}

public void Main(string argument, UpdateType updateSource)
{
    IMyCargoContainer cargo = GridTerminalSystem.GetBlockWithName(ContainerName) as IMyCargoContainer;
    IMyTimerBlock timer = GridTerminalSystem.GetBlockWithName(TimerName) as IMyTimerBlock;

    if (DoesCargoContainIce(cargo))
    {
        Echo("Ice found in cargo container. Doing nothing.");
    }
    else
    {
        Echo("No ice found in cargo container. Triggering the timer.");
        timer.Trigger();
    }
}
