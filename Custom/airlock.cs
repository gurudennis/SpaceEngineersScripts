///////////////////////////////////////
// Configuration
///////////////////////////////////////

// Name of the airlock to operate on. If empty, expected as
// the second command line parameter.
private static string AirlockGroupName = "";

// Add this tag to the name of all airlock doors that lead to outer space vacuum.
// All other doors in the airlock group will be considered to be inner doors that
// lead to a pressurized area.
private static string OuterDoorTag = "[Outer]";

// ADVANCED: Adjust these to wait longer or shorter at various stages
private static int DoorWaitSec = 1;
private static int PressurizeWaitSec = 2;
private static int DepressurizeWaitSec = 4;

///////////////////////////////////////
// Code - do not modify
///////////////////////////////////////

public enum Verb
{
    Pressurize,
    Depressurize,
    AtmoReady,
    SpaceReady,
    EnableDemoMode,
    DisableDemoMode
}

public void Main(string argument, UpdateType updateSource)
{
    if ((updateSource & UpdateType.Update10) != 0)
        OnTick();
    else
        SetUp(argument);
}

private void SetUp(string argument)
{
    bool ok = false;
    try
    {
        ok = SetUpInternal(argument);
    }
    catch (Exception)
    {
        TearDown();
        throw;
    }

    if (!ok)
        TearDown();
}

private bool SetUpInternal(string argument)
{
    TearDown();

    Echo("Starting sequence.");

    MyCommandLine cmdLine = new MyCommandLine();
    if (!cmdLine.TryParse(argument) || cmdLine.ArgumentCount < (string.IsNullOrEmpty(AirlockGroupName) ? 2 : 1))
    {
        Echo(GetCmdLineErrorString("Invalid command line"));
        return false;
    }

    if (!Enum.TryParse(cmdLine.Argument(0), out _verb))
    {
        Echo(GetCmdLineErrorString("Invalid verb on the command line"));
        return false;
    }
    
    if (_verb == Verb.EnableDemoMode)
    {
        _demoMode = true;
        Echo("Sequence complete.");
        return false;
    }
    else if (_verb == Verb.DisableDemoMode)
    {
        _demoMode = false;
        Echo("Sequence complete.");
        return false;
    }

    string groupName = string.IsNullOrEmpty(AirlockGroupName) ? cmdLine.Argument(1) : AirlockGroupName;
    _airlockGroup = GridTerminalSystem.GetBlockGroupWithName(groupName);

    string errorMessage = ValidateAirlockGroup();
    if (errorMessage != null)
    {
        Echo(GetCmdLineErrorString(errorMessage));
        return false;
    }

    if (!IsInDesiredState() && _verb != Verb.AtmoReady)
    {
        errorMessage = PerformDoorAction(null, DoorAction.Close, true);
        if (errorMessage != null)
        {
            Echo($"Failed to cycle doors: {errorMessage}");
            return false;
        }
    }

    _curStageIndex = 0;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;

    return true;
}

private void OnTick()
{
    try
    {
        OnTickInternal();
    }
    catch (Exception)
    {
        TearDown();
        throw;
    }
}

private void OnTickInternal()
{
    if (IsWaiting())
        return;

    const int StageCount = 2;

    string errorMessage = null;
    if (_curStageIndex == 0)
    {
        errorMessage = ChangePressure();

        if (errorMessage != null)
            Echo($"Failed to change the pressure: {errorMessage}");
    }
    else if(_curStageIndex == 1)
    {
        errorMessage = PerformDoorAction(!IsPressurizing(), DoorAction.Open, false);
        if (errorMessage == null)
            errorMessage = PostAction();

        if (errorMessage != null)
            Echo($"Failed to cycle doors: {errorMessage}");
    }

    bool done = (_curStageIndex == (StageCount - 1));
    if (done)
        Echo("Sequence complete.");

    if (errorMessage != null || _curStageIndex < 0 || _curStageIndex >= (StageCount - 1))
    {
        TearDown(done);
        return;
    }

    ++_curStageIndex;
}

private string ValidateAirlockGroup()
{
    if (_airlockGroup == null)
        return "No airlock group found by this name";

    if (GetDoors(true).Count <= 0)
        return $"An airlock must have at least 1 door with the \"{OuterDoorTag}\" tag (no quotes)";

    if (GetDoors(false).Count <= 0)
        return $"An airlock must have at least 1 door without the \"{OuterDoorTag}\" tag (no quotes)";

    if (GetVent() == null)
        return "An airlock must have exactly one air vent";

    return null;
}

private enum DoorAction
{
    Open,
    Close,
    Enable,
    Disable
}

private string PerformDoorAction(bool? outer, DoorAction action, bool wait)
{
    List<IMyDoor> doors = GetDoors(outer);
    if (!(doors?.Any() ?? false))
        return "Door not found";

    foreach (IMyDoor door in doors)
    {
        door.Enabled = true;

        if (action == DoorAction.Open)
            door.OpenDoor();
        else if (action == DoorAction.Close)
            door.CloseDoor();
        
        door.Enabled = !(action == DoorAction.Disable);
    }

    if (wait)
        Wait(DoorWaitSec);

    return null;
}

private string ChangePressure()
{
    if (_verb != Verb.AtmoReady)
        PerformDoorAction(null, DoorAction.Disable, false);

    IMyAirVent vent = GetVent();
    if (vent == null)
        return "Air vent not found";

    vent.ApplyAction (IsPressurizing() ? "Depressurize_Off" : "Depressurize_On");

    Wait(IsPressurizing() ? PressurizeWaitSec : DepressurizeWaitSec);

    return null;
}

private string PostAction()
{
    if (_verb == Verb.SpaceReady)
        return PerformDoorAction(false, DoorAction.Close, false);
    else if (_verb == Verb.AtmoReady)
        return PerformDoorAction(null, DoorAction.Open, false);

    return null;
}

private void Wait(int seconds)
{
    if (_timeToResumeUTC != null)
        throw new Exception("Another wait is in progress");

    if (seconds <= 0)
        return;

    _timeToResumeUTC = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
}

private bool IsWaiting()
{
    if (_timeToResumeUTC == null)
        return false;

    if (DateTime.UtcNow >= _timeToResumeUTC.Value)
    {
        _timeToResumeUTC = null;
        return false;
    }

    return true;
}

private void TearDown(bool ok = false)
{
     _verb = Verb.SpaceReady;
    _airlockGroup = null;
    _curStageIndex = -1;
    _timeToResumeUTC = null;

    Runtime.UpdateFrequency = UpdateFrequency.None;

    if (!ok)
        PerformDoorAction(null, DoorAction.Enable, false);
}

private List<IMyDoor> GetDoors(bool? outer = null)
{
    List<IMyDoor> doors = new List<IMyDoor>();
    _airlockGroup?.GetBlocksOfType<IMyDoor>(doors, d => (outer == null || d.CustomName.Contains(OuterDoorTag) == outer.Value));
    return doors;
}

private IMyAirVent GetVent()
{
    List<IMyAirVent> vents = new List<IMyAirVent>();
    _airlockGroup.GetBlocksOfType<IMyAirVent>(vents, v => true);
    if ((vents?.Count ?? 0) != 1)
        return null;

    return vents[0];
}

private static string GetCmdLineErrorString(string prefix)
{
    if (prefix != null && !prefix.EndsWith(". "))
        prefix += ". ";
    
    return $"{prefix}Expected: {{Pressurize|Depressurize|AtmoReady|SpaceReady}} [airlock_group_name]";
}

bool IsPressurizing()
{
    return _verb != Verb.Depressurize;
}

bool IsInDesiredState()
{
    if (_demoMode)
        return false;

    IMyAirVent vent = GetVent();
    return vent != null && IsPressurizing() == vent.CanPressurize;
}

private Verb _verb;
private IMyBlockGroup _airlockGroup;
private int _curStageIndex;
private DateTime? _timeToResumeUTC;
private bool _demoMode;
