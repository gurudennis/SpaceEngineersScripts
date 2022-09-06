//////////////////////////////////////////////////////////////////
//////////////////////// Configuration ///////////////////////////
//////////////////////////////////////////////////////////////////

/* Build example (you can add more than one piston per direction):

  XPISTONX
  P      X
  I      X
  S      X
  T      X
  O      X
  N      X
  D      X
  R      X
  I      X
  L      X
  L      X
         X
 ICE   ROTOR

*/

// Mandatory
public string GroupName = "Base Ice Rig";

// Optional
public string VertPistonTag = null; // null to auto-detect based on the orientation of the drill(s)
public string HorzPistonTag = null; // ditto
public float RotorMiningRPM = 0.03f;
public float VertPistonMetersPerSec = 1.0f;
public float HorzRowIncrementMeters = 2.45f;

//////////////////////////////////////////////////////////////////
/////////////////// Do not edit below this line //////////////////
//////////////////////////////////////////////////////////////////

public void Main(string argument, UpdateType updateSource)
{
    if (string.IsNullOrEmpty(argument))
    {
        ExecuteTick();
        return;
    }

    bool ok = false;
    if (argument == "start" || argument == "Start")
        ok = Start();
    else if (argument == "stop" || argument == "Stop")
        ok = Stop();
    else if (argument == "reset" || argument == "Reset")
        ok = Reset();
    
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

private float DegFromRad(float rad)
{
    return (180.0f / (float)Math.PI) * rad;
}

private bool UpdateBlocks(bool force)
{
    if (_timer != null && _rotor != null && _drills.Count > 0 && _horzPistons.Count > 0) // if the essential blocks are known
    {
        if (!force)
            return true;
    }

    IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(GroupName);
    if (group == null)
    {
        Echo($"No group named \"{GroupName}\" was found.");
        return false;
    }
    
    List<IMyTimerBlock> timers = new List<IMyTimerBlock>();
    group.GetBlocksOfType(timers);
    if (timers == null || timers.Count == 0)
    {
        Echo($"No timer found in group \"{GroupName}\".");
        return false;
    }
    else if (timers.Count != 1)
    {
        Echo($"More than one timer found in group \"{GroupName}\". There must only be one timer in it.");
        return false;
    }
    else
    {
        _timer = timers[0];
    }
    
    List<IMyMotorAdvancedStator> rotors = new List<IMyMotorAdvancedStator>();
    group.GetBlocksOfType(rotors);
    if (rotors == null || rotors.Count == 0)
    {
        Echo($"No rotor found in group \"{GroupName}\".");
        return false;
    }
    else if (rotors.Count != 1)
    {
        Echo($"More than one rotor found in group \"{GroupName}\". There must only be one rotor in it.");
        return false;
    }
    else
    {
        _rotor = rotors[0];
    }
    
    group.GetBlocksOfType(_lights);
    
    group.GetBlocksOfType(_drills);
    if (_drills.Count == 0)
    {
        Echo($"No drills found in group \"{GroupName}\".");
        return false;
    }
    
    List<IMyPistonBase> pistons = new List<IMyPistonBase>();
    group.GetBlocksOfType(pistons);
    if (pistons.Count == 0)
    {
        Echo($"No pistons found in group \"{GroupName}\".");
        return false;
    }
    
    // Piston/rotor part is its "Up" direction. Hence we're looking for the pistons whose "Up" direction is the inverse of the rotor's "Up" direction, i.e. "Down" in its coordinate system.
    if (string.IsNullOrEmpty(VertPistonTag))
        _vertPistons = pistons.Where(p => Base6Directions.GetClosestDirection(Vector3D.TransformNormal(p.WorldMatrix.Up, MatrixD.Transpose(_rotor.WorldMatrix))) == VRageMath.Base6Directions.Direction.Down).ToList();
    else
        _vertPistons = pistons.Where(p => p.CustomName.Contains(VertPistonTag)).ToList();

    // All the other pistons can be considered to be horizontal by default.
    if (string.IsNullOrEmpty(HorzPistonTag))
        _horzPistons = pistons.Except(_vertPistons).ToList();
    else
        _horzPistons = pistons.Where(p => p.CustomName.Contains(HorzPistonTag)).ToList();
    
    if (_horzPistons.Count == 0)
    {
        Echo($"No horizontal pistons found in group \"{GroupName}\".");
        return false;
    }
    
    // Print debug info
    Echo($"Blocks: Rotors=1, Drills={_drills.Count}, VPistons={_vertPistons.Count}, HPistons={_horzPistons.Count}, Lights={_lights.Count}.\n");
    
    return true;
}

private enum Action
{
    None,
    MoveLeft,
    MoveRight
};

private void AnalyzeState(out Action CurrentAction, out Action DesiredAction)
{
    bool isRotorOnLeft = (DegFromRad(_rotor.Angle) < (_rotor.LowerLimitDeg + RotorMarginDeg));
    bool isRotorOnRight = (DegFromRad(_rotor.Angle) > (_rotor.UpperLimitDeg - RotorMarginDeg));
    bool isRotorHalted = _rotor.TargetVelocityRPM == 0.0f;
    bool isRotorMovingLeft = _rotor.TargetVelocityRPM < 0.0f;
    bool isFullyExtended = !_horzPistons.Any(p => !IsApproxEqual(p.CurrentPosition, p.HighestPosition, 0.1f));
    
    if (isRotorHalted) // must be the initial pre-start state
        CurrentAction = Action.None;
    else if (isRotorMovingLeft)
        CurrentAction = Action.MoveLeft;
    else
        CurrentAction = Action.MoveRight;

    if (isRotorOnLeft)
        DesiredAction = (isFullyExtended && isRotorMovingLeft) ? Action.None : Action.MoveRight;
    else if (isRotorOnRight)
        DesiredAction = Action.MoveLeft;
    else
        DesiredAction = (CurrentAction == Action.None) ? Action.MoveRight : CurrentAction;
}

private bool IsApproxEqual(float left, float right, float delta = 0.001f)
{
    return Math.Abs(left - right) <= delta;
}

private bool Start()
{
    if (!UpdateBlocks(true))
        return false;

    _rotor.Enabled = true;
    _timer.StartCountdown();
    
    ExecuteTick();

    return true;
}

private bool Stop()
{
    UpdateBlocks(true);
    
    _timer?.StopCountdown();
    
    // Disable all blocks
    if (_rotor != null)
        _rotor.Enabled = false;
    ForEachBlock(_lights, light => light.Enabled = false);
    ForEachBlock(_drills, drill => drill.Enabled = false);
    ForEachBlock(_vertPistons, piston => piston.Enabled = false);
    ForEachBlock(_horzPistons, piston => piston.Enabled = false);
    
    return true;
}

private bool Reset()
{
    UpdateBlocks(true);
    
    if (_rotor != null)
    {
        _rotor.Enabled = true;
        _rotor.TargetVelocityRPM = -1.0f;
    }
    ForEachBlock(_lights, light => light.Enabled = false);
    ForEachBlock(_drills, drill => drill.Enabled = false);
    ForEachBlock(_vertPistons, piston => { piston.Enabled = true; piston.Velocity = -2.0f; });
    ForEachBlock(_horzPistons, piston => { piston.Enabled = true; piston.Velocity = -2.0f; });
    
    return true;
}

private void ExtendForward(float desiredMeters)
{
    float remainingMeters = desiredMeters;
    foreach (IMyPistonBase piston in _horzPistons)
    {
        float hasMeters = piston.HighestPosition - piston.CurrentPosition;
        if (hasMeters < 0.001f)
            continue;
        
        if (hasMeters <= remainingMeters)
        {
            piston.MaxLimit = piston.HighestPosition;
            piston.Extend();
            remainingMeters -= hasMeters;
        }
        else
        {
            piston.MaxLimit += remainingMeters;
            piston.Extend();
            remainingMeters = 0.0f;
            break;
        }
    }
}

private void ExecuteTick()
{
    if (!UpdateBlocks(false))
    {
        Stop();
        return;
    }
    
    if (!_rotor.Enabled) // are we even running?
        return;
    
    // Enable all blocks
    _rotor.RotorLock = false;
    _rotor.Enabled = true;
    ForEachBlock(_lights, light => light.Enabled = true);
    ForEachBlock(_drills, drill => drill.Enabled = true);
    ForEachBlock(_vertPistons, piston => piston.Enabled = true);
    ForEachBlock(_horzPistons, piston => piston.Enabled = true);
    
    // Determine the course of action
    Action currentAction;
    Action desiredAction;
    AnalyzeState(out currentAction, out desiredAction);
    bool extend = (currentAction != Action.None && desiredAction == Action.MoveRight && currentAction != desiredAction); // extend the horz pistons by one block if we're moving right and didn't just start out
    
    // Print debug info
    Echo($"Actions: Current={currentAction}, Desired={desiredAction}, Extend={extend}.\n");
    
    // Reciprocate each vertical piston that is at its extreme position
    ForEachBlock(_vertPistons, piston =>
    {
        if (desiredAction == Action.MoveLeft)
            piston.Retract();
        else if (IsApproxEqual(piston.CurrentPosition, piston.MinLimit, PistonMarginMeters) || IsApproxEqual(piston.CurrentPosition, piston.MaxLimit, PistonMarginMeters))
            piston.Reverse();
    });

    // if (currentAction == desiredAction)
    //     return;
    
    if (currentAction == Action.None)
        ForEachBlock(_horzPistons, p => { p.MaxLimit = p.LowestPosition; p.Retract(); });

    if (extend)
        ExtendForward(HorzRowIncrementMeters);

    // Rotate the drill arm
    if (desiredAction == Action.MoveLeft)
        _rotor.TargetVelocityRPM = RotorResetRPM;
    else if (desiredAction == Action.MoveRight)
        _rotor.TargetVelocityRPM = RotorMiningRPM;
    else if (desiredAction == Action.None) // at the end of travel
        Reset();
}

private float RotorMarginDeg = 0.1f;
private float PistonMarginMeters = 0.2f;
private float RotorResetRPM = -1.0f;

private IMyTimerBlock _timer = null;
private IMyMotorAdvancedStator _rotor = null;
private List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
private List<IMyShipDrill> _drills = new List<IMyShipDrill>();
private List<IMyPistonBase> _vertPistons = new List<IMyPistonBase>();
private List<IMyPistonBase> _horzPistons = new List<IMyPistonBase>();
