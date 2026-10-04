using Baanish.Karambit;

int checks = 0;
void Equal<T>(T expected, T actual, string name)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        Console.Error.WriteLine($"{name}: expected {expected}, received {actual}");
        Environment.Exit(1);
    }
}

Equal(2.5f, KarambitPolicy.CruiseMach, "motor acceleration deliberately stops at Mach 2.5");
Equal(850f, KarambitPolicy.MotorSpeedLimit(340f), "sea-level motor speed limit");
Equal(842.38f, KarambitPolicy.MotorSpeedLimit(336.952f), "2000 ft atmospheric sound speed changes the motor cutoff");
Equal(725f, KarambitPolicy.MotorSpeedLimit(290f), "cold-altitude motor speed limit");

Equal(304.8f, KarambitPolicy.GroundProfileMaximumAgl, "1000 ft AGL ground boundary is exactly 304.8 m");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 0f, 0f, 350f), "sea-level ground profile");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 304.799f, 0f, 350f), "missile below 1000 ft AGL remains ground");
Equal(GuidanceProfile.HighAltitude, KarambitPolicy.SelectProfile(false, false, false, 304.8f, 0f, 350f), "1000 ft AGL itself uses air");
Equal(GuidanceProfile.HighAltitude, KarambitPolicy.SelectProfile(false, false, false, 304.801f, 0f, 350f), "missile above 1000 ft AGL uses air");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 250f, 0f, 350f), "250 m AGL above mountain terrain stays ground regardless of MSL");
Equal(GuidanceProfile.Aircraft, KarambitPolicy.SelectProfile(true, false, false, 10f, 0f, 200f), "low aircraft always uses intercept lead");
Equal(GuidanceProfile.Aircraft, KarambitPolicy.SelectProfile(true, false, false, 1000f, -50f, 200f), "descending aircraft uses air");
Equal(GuidanceProfile.AntiAirMissile, KarambitPolicy.SelectProfile(false, true, false, 10f, 0f, 500f), "low incoming SAM uses air even while level");
Equal(GuidanceProfile.AntiAirMissile, KarambitPolicy.SelectProfile(false, true, false, 10f, -200f, 500f), "descending incoming SAM remains air");
Equal(GuidanceProfile.BallisticMissile, KarambitPolicy.SelectProfile(false, false, true, 10f, -1000f, 1200f), "terminal ballistic missile remains air below 1000 ft");
Equal(GuidanceProfile.BallisticMissile, KarambitPolicy.SelectProfile(false, false, true, 10f, 0f, 1200f), "ballistic role persists without observed climb");
Equal(GuidanceProfile.UnknownAltitude, KarambitPolicy.SelectProfile(false, false, false, float.NaN, 0f, 350f), "unknown AGL keeps air lead");
Equal(GuidanceProfile.UnknownAltitude, KarambitPolicy.SelectProfile(false, false, false, float.PositiveInfinity, 0f, 350f), "infinite AGL keeps air lead");
Equal(GuidanceProfile.UnknownAltitude, KarambitPolicy.SelectProfile(false, false, false, -1f, 0f, 350f), "invalid negative AGL keeps air lead");
Equal(GuidanceProfile.AntiAirMissile, KarambitPolicy.SelectProfile(false, true, false, float.NaN, 0f, 350f), "authored SAM role overrides unknown AGL");
float climbBoundarySpeed = 5f / MathF.Sin(5f * MathF.PI / 180f);
Equal(5f, KarambitPolicy.LoftMinimumVerticalSpeed, "loft minimum climb speed is deliberately 5 m/s");
Equal(5f, KarambitPolicy.LoftMinimumClimbDegrees, "loft minimum climb angle is deliberately 5 degrees");
Equal(GuidanceProfile.Lofting, KarambitPolicy.SelectProfile(false, false, false, 10f, 5f, climbBoundarySpeed * 0.99f), "loft includes exactly 5 m/s with a climb angle safely above 5 degrees");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 10f, 4.999f, 50f), "steep climb below 5 m/s is terrain correction");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 10f, 5f, climbBoundarySpeed + 0.001f), "5 m/s climb below 5 degrees stays ground");
Equal(GuidanceProfile.Lofting, KarambitPolicy.SelectProfile(false, false, false, 10f, 5f, climbBoundarySpeed - 0.001f), "5 m/s climb above 5 degrees uses air");
Equal(GuidanceProfile.Lofting, KarambitPolicy.SelectProfile(false, false, false, 100f, 100f, 500f), "lofting surface missile below 1000 ft uses air");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 100f, -100f, 500f), "descending surface missile below 1000 ft returns to ground");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 100f, 100f, float.NaN), "invalid speed does not assert a loft");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 100f, float.NaN, 500f), "invalid vertical speed does not assert a loft");
Equal(GuidanceProfile.Ground, KarambitPolicy.SelectProfile(false, false, false, 100f, 100f, 0f), "stationary speed does not assert a loft");

Equal(3.048f, KarambitPolicy.AimAltitude(0f, 12f, false), "10 ft cruise floor stays below 12 m sea-skimmer");
Equal(580f, KarambitPolicy.AimAltitude(300f, 600f, false), "ground-profile cruise stays below a low-AGL missile over high terrain");
Equal(3.048f, KarambitPolicy.AimAltitude(-50f, 12f, false), "10 ft sea floor");
Equal(3.048f, KarambitPolicy.AimAltitude(0f, 7.9248f, false), "10 ft cruise floor stays below 26 ft sea-skimmer");
Equal(103.048f, KarambitPolicy.AimAltitude(100f, 8f, false), "terrain clearance overrides low target cue");
Equal(600f, KarambitPolicy.TerrainLookAhead(10f, 10000f), "terrain scan keeps stock minimum 100 m/s and six seconds");
Equal(6000f, KarambitPolicy.TerrainLookAhead(1000f, 10000f), "terrain scan reaches six seconds ahead at cruise speed");
Equal(2000f, KarambitPolicy.TerrainLookAhead(1000f, 2000f), "terrain scan stops at the target instead of climbing for terrain behind it");
Equal(5f, KarambitPolicy.LaunchAngleDegrees, "mount launch angle is five degrees nose-down relative to aircraft");
Equal(-0.1f, KarambitPolicy.CruiseTrim(0f, -2f, 0f, 0.2f), "steady altitude bias trims the cruise aimpoint");
Equal(-2f, KarambitPolicy.CruiseTrim(-1.99f, -2f, 0f, 0.2f), "cruise trim is bounded to avoid windup");
Equal(0f, KarambitPolicy.CruiseTrim(0f, -50f, 0f, 0.2f), "initial descent does not accumulate altitude trim");
Equal(0f, KarambitPolicy.CruiseTrim(0f, -2f, -50f, 0.2f), "fast leveling does not accumulate altitude trim");
Equal(12f, KarambitPolicy.AimAltitude(0f, 12f, true), "terminal pursuit can reach sea-skimmer");
Equal(600f, KarambitPolicy.AimAltitude(0f, 600f, true), "terminal pursuit can climb to aircraft");

Equal(true, KarambitPolicy.InSeekerEnvelope(0f, 0f, 60f), "seeker accepts forward horizon");
Equal(true, KarambitPolicy.InSeekerEnvelope(-60f, -10f, 60f), "seeker lower-left corner is inclusive");
Equal(true, KarambitPolicy.InSeekerEnvelope(60f, -10f, 60f), "seeker lower-right corner is inclusive");
Equal(true, KarambitPolicy.InSeekerEnvelope(-60f, 60f, 60f), "seeker upper-left corner is inclusive");
Equal(true, KarambitPolicy.InSeekerEnvelope(60f, 60f, 60f), "seeker upper-right corner is inclusive");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, -10.001f, 60f), "seeker rejects below shallow downward coverage");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, 60.001f, 60f), "seeker rejects above upward coverage");
Equal(false, KarambitPolicy.InSeekerEnvelope(-60.001f, 0f, 60f), "seeker rejects beyond left azimuth boundary");
Equal(false, KarambitPolicy.InSeekerEnvelope(60.001f, 0f, 60f), "seeker rejects beyond right azimuth boundary");
Equal(false, KarambitPolicy.InSeekerEnvelope(-180f, 0f, 60f), "seeker rejects rearward negative azimuth");
Equal(false, KarambitPolicy.InSeekerEnvelope(180f, 0f, 60f), "seeker rejects rearward positive azimuth");
Equal(false, KarambitPolicy.InSeekerEnvelope(float.NaN, 0f, 60f), "seeker rejects nonfinite azimuth");
Equal(false, KarambitPolicy.InSeekerEnvelope(float.PositiveInfinity, 0f, 60f), "seeker rejects positive infinite azimuth");
Equal(false, KarambitPolicy.InSeekerEnvelope(float.NegativeInfinity, 0f, 60f), "seeker rejects negative infinite azimuth");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, float.NaN, 60f), "seeker rejects nonfinite elevation");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, float.PositiveInfinity, 60f), "seeker rejects positive infinite elevation");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, float.NegativeInfinity, 60f), "seeker rejects negative infinite elevation");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, 0f, float.NaN), "seeker rejects nonfinite azimuth limit");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, 0f, float.PositiveInfinity), "seeker rejects positive infinite azimuth limit");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, 0f, float.NegativeInfinity), "seeker rejects negative infinite azimuth limit");
Equal(false, KarambitPolicy.InSeekerEnvelope(0f, 0f, -1f), "seeker rejects invalid negative azimuth limit");
Equal(true, KarambitPolicy.InSeekerEnvelope(0f, 0f, 0f), "zero azimuth limit accepts exact centerline");
Equal(false, KarambitPolicy.InSeekerEnvelope(0.001f, 0f, 0f), "zero azimuth limit rejects off-center return");

RadarCandidate[] candidates =
[
    new(10, true, true, 1500f * 1500f, 0.6f),
    new(11, true, true, 100f, 0.8f),
    new(12, false, true, 100f, 2f),
    new(13, true, false, 100f, 3f),
    new(14, true, true, 1500f * 1500f + 1f, 4f),
    new(15, true, true, 100f, float.NaN)
];
Equal(11u, KarambitPolicy.PickCandidate(candidates, 0.5f), "strongest detectable enemy airborne return");
Equal(10u, KarambitPolicy.PickCandidate([candidates[0]], 0.5f), "inclusive 1500 m cue boundary");
Equal(0u, KarambitPolicy.PickCandidate([new(16, true, true, 0f, 0.5f)], 0.5f), "min signal is not a lock");
Equal(17u, KarambitPolicy.PickCandidate([new(19, true, true, 1f, 1f), new(17, true, true, 1f, 1f)], 0.5f), "saturated return tie uses stable target id");
Equal(10u, KarambitPolicy.PickCandidate(candidates, 0.5f, 10), "detectable designation outranks stronger greedy return");
Equal(10u, KarambitPolicy.PickCandidate([candidates[1], candidates[0]], 0.5f, 10), "designation outranks a stronger return encountered first");
Equal(11u, KarambitPolicy.PickCandidate([new(0, true, true, 100f, 2f), candidates[1]], 0.5f, 0), "invalid designation falls back to strongest eligible return");
Equal(11u, KarambitPolicy.PickCandidate([new(10, true, true, 100f, 2f, alive: false), candidates[1]], 0.5f, 10), "dead designation falls back");
Equal(11u, KarambitPolicy.PickCandidate([new(10, false, true, 100f, 2f), candidates[1]], 0.5f, 10), "friendly designation falls back");
Equal(11u, KarambitPolicy.PickCandidate([new(10, true, false, 100f, 2f), candidates[1]], 0.5f, 10), "ground designation falls back");
Equal(11u, KarambitPolicy.PickCandidate([new(10, true, true, 1500f * 1500f + 1f, 2f), candidates[1]], 0.5f, 10), "designation outside cue falls back");
Equal(11u, KarambitPolicy.PickCandidate([new(10, true, true, 100f, 0.5f), candidates[1]], 0.5f, 10), "undetectable designation falls back");
Equal(11u, KarambitPolicy.PickCandidate([new(10, true, true, 100f, float.NaN), candidates[1]], 0.5f, 10), "nonfinite designation return falls back");
Equal(true, KarambitPolicy.KeepLock(true, 4f), "sticky lock held for exact four seconds");
Equal(false, KarambitPolicy.KeepLock(true, 4.001f), "lost lock permits reacquisition");
Equal(false, KarambitPolicy.KeepLock(false, 0f), "disabled or friendly target drops immediately");
Equal(0.60f, KarambitPolicy.JamTolerance, "weak radar jammer policy");
Equal(0.94f, KarambitPolicy.DecayJam(1f, 0.1f), "stock-shaped jammer decay with weaker tolerance");
Equal(0f, KarambitPolicy.DecayJam(0f, 0.1f), "jam floor");
Equal(true, KarambitPolicy.WarmStartInbound(KarambitPolicy.TestMission, "Karambit_Inbound_01", "AShM1", "Karambit_Target", false), "saved test mission warmstarts inbound");
Equal(true, KarambitPolicy.WarmStartInbound("_agentic karambit", "Karambit_Inbound_01", "AShM1", "Karambit_Target", true), "framework test fixture warmstarts inbound");
Equal(false, KarambitPolicy.WarmStartInbound("Other mission", "Karambit_Inbound_01", "AShM1", "Karambit_Target", true), "other missions stay stock");
Equal(false, KarambitPolicy.WarmStartInbound("_agentic unrelated", "Karambit_Inbound_01", "AShM1", "Karambit_Target", false), "unrelated framework missions stay stock");
Equal(false, KarambitPolicy.WarmStartInbound(KarambitPolicy.TestMission, "Other_Inbound_01", "AShM1", "Karambit_Target", true), "other salvo names stay stock");
Equal(false, KarambitPolicy.WarmStartInbound(KarambitPolicy.TestMission, "Karambit_Inbound_01", "AShM2", "Karambit_Target", true), "other missile types stay stock");

static float Degrees(float pitch) => MathF.Round(pitch * 180f / MathF.PI, 3);
Equal(-45f, Degrees(KarambitPolicy.CruisePitch(609.6f, 0f, 455f, 0f, 10f, 15f)), "high launch commands immediate descent");
Equal(-24.143f, Degrees(KarambitPolicy.CruisePitch(400f, 0f, 830f, -120f, 10f, 15f)), "descent does not lift waypoint hundreds of metres");
Equal(3.665f, Degrees(KarambitPolicy.CruisePitch(70f, 0f, 850f, -200f, 10f, 15f)), "turn-radius envelope starts pull-up before floor");
Equal(6.465f, Degrees(KarambitPolicy.CruisePitch(15f, 0f, 800f, -100f, 10f, 15f)), "descending velocity starts pull-up before 10 ft floor");
Equal(-16.285f, Degrees(KarambitPolicy.CruisePitch(600f, 0f, 1200f, 0f, 10f, 5f)), "g-limit reduces descent envelope");
Equal(0f, Degrees(KarambitPolicy.CruisePitch(3.048f, 0f, 800f, 0f, 10f, 15f)), "10 ft cruise floor commands level flight");

static (float MinimumAltitude, float MaximumAltitude, float CaptureTime) SimulateCruiseCapture(float startingAltitude, float targetAltitude = 0f)
{
    const float dt = 0.02f;
    float altitude = startingAltitude, pitch = 0f, minimum = startingAltitude, maximum = startingAltitude, captureTime = 0f;
    for (int frame = 0; frame < 750; frame++)
    {
        float time = frame * dt;
        float speed = Math.Min(1000f, 261f + 180f * time);
        if (time >= 0.5f)
        {
            float command = KarambitPolicy.CruisePitch(altitude, 0f, speed, speed * MathF.Sin(pitch), 10f, 15f, targetAltitude);
            float turnRate = Math.Min(10f * MathF.PI / 180f, 9.81f * 15f / speed);
            pitch += Math.Clamp(command - pitch, -turnRate * dt, turnRate * dt);
        }
        altitude += speed * MathF.Sin(pitch) * dt;
        minimum = Math.Min(minimum, altitude);
        maximum = Math.Max(maximum, altitude);
        if (captureTime == 0f && Math.Abs(altitude - KarambitPolicy.CruiseAltitude(0f, targetAltitude)) < 5f && Math.Abs(speed * MathF.Sin(pitch)) < 5f)
            captureTime = time;
    }
    return (minimum, maximum, captureTime);
}

// The 10 deg/s, 15 g model pins guidance, with a synthetic speed ramp rather than Unity aerodynamics or motors.
var lowLaunch = SimulateCruiseCapture(609.6f);
var highLaunch = SimulateCruiseCapture(1000f);
Equal(3.075f, MathF.Round(lowLaunch.MinimumAltitude, 3), "609.6 m launch bounded-turn model minimum");
Equal(6f, MathF.Round(lowLaunch.CaptureTime, 2), "609.6 m launch bounded-turn model capture time");
Equal(3.147f, MathF.Round(highLaunch.MinimumAltitude, 3), "1000 m launch bounded-turn model minimum");
Equal(7.38f, MathF.Round(highLaunch.CaptureTime, 2), "1000 m launch bounded-turn model capture time");
Console.WriteLine($"PASS: {checks} Karambit profile, policy, seeker-envelope, target-selection, sticky-lock, jammer, inbound-scope and bounded-turn model characterization checks.");
