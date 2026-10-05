using System;
using System.Collections.Generic;

namespace Baanish.Karambit;

internal enum GuidanceProfile
{
    Ground,
    Aircraft,
    AntiAirMissile,
    BallisticMissile,
    Lofting,
    HighAltitude,
    UnknownAltitude
}

internal readonly struct RadarCandidate
{
    public readonly uint TargetId;
    public readonly bool Enemy;
    public readonly bool Airborne;
    public readonly bool Alive;
    public readonly float CueDistanceSquared;
    public readonly float Signal;

    public RadarCandidate(uint targetId, bool enemy, bool airborne, float cueDistanceSquared, float signal, bool alive = true)
    {
        TargetId = targetId;
        Enemy = enemy;
        Airborne = airborne;
        Alive = alive;
        CueDistanceSquared = cueDistanceSquared;
        Signal = signal;
    }
}

internal static class KarambitPolicy
{
    public const string MissileKey = "baanish_karambit";
    public const float CueRadius = 1500f;
    public const float CruiseMach = 2.5f;
    public const float CruiseClearance = 3.048f;
    public const float TerrainLookAheadSeconds = 6f;
    public const float LaunchAngleDegrees = 5f;
    public const float SeekerMinimumElevationDegrees = -10f;
    public const float SeekerMaximumElevationDegrees = 60f;
    public const float TargetAltitudeMargin = 20f;
    public const float TerminalRange = 1250f;
    public const float GroundProfileMaximumAgl = 304.8f;
    public const float LoftMinimumVerticalSpeed = 5f;
    public const float LoftMinimumClimbDegrees = 5f;
    public const float JamTolerance = 0.60f;
    public const float LockPersistence = 4f;
    public const string TestMission = "SAAM-18 Karambit - Ignus Interception";

    // This stops motor acceleration at Mach 2.5; gravity and launch velocity can still exceed it.
    public static float MotorSpeedLimit(float speedOfSound) => CruiseMach * speedOfSound;

    // Low aircraft and anti-air/ballistic missiles need intercept lead even below 1000 ft AGL.
    // A loft is a climb of at least 5 degrees and 5 m/s; smaller terrain-following corrections stay ground.
    public static GuidanceProfile SelectProfile(bool aircraft, bool antiAirMissile, bool ballisticMissile,
        float altitudeAgl, float verticalSpeed, float targetSpeed)
    {
        if (aircraft)
            return GuidanceProfile.Aircraft;
        if (antiAirMissile)
            return GuidanceProfile.AntiAirMissile;
        if (ballisticMissile)
            return GuidanceProfile.BallisticMissile;
        if (float.IsFinite(verticalSpeed) && float.IsFinite(targetSpeed) && targetSpeed > 0f &&
            verticalSpeed >= LoftMinimumVerticalSpeed &&
            verticalSpeed / targetSpeed >= MathF.Sin(LoftMinimumClimbDegrees * MathF.PI / 180f))
            return GuidanceProfile.Lofting;
        if (!float.IsFinite(altitudeAgl) || altitudeAgl < 0f)
            return GuidanceProfile.UnknownAltitude;
        return altitudeAgl < GroundProfileMaximumAgl ? GuidanceProfile.Ground : GuidanceProfile.HighAltitude;
    }

    // Favor returns above the missile while allowing only ten degrees of downward coverage.
    public static bool InSeekerEnvelope(float azimuthDegrees, float elevationDegrees, float maxAzimuthDegrees) =>
        float.IsFinite(azimuthDegrees) && float.IsFinite(elevationDegrees) && float.IsFinite(maxAzimuthDegrees) &&
        Math.Abs(azimuthDegrees) <= maxAzimuthDegrees &&
        elevationDegrees >= SeekerMinimumElevationDegrees && elevationDegrees <= SeekerMaximumElevationDegrees;

    public static bool WarmStartInbound(string missionName, string uniqueName, string missileKey, string targetName, bool hasTestIfrit) =>
        missileKey == "AShM1" && uniqueName.StartsWith("Karambit_Inbound_", StringComparison.Ordinal) &&
        targetName == "Karambit_Target" && (missionName == TestMission ||
        hasTestIfrit && missionName.StartsWith("_agentic", StringComparison.Ordinal));

    // Ten feet keeps the seeker below 6-12 m sea-skimmers; terrain clearance still takes priority.
    public static float CruiseAltitude(float surfaceHeight, float targetAltitude = 0f) =>
        Math.Max(Math.Max(surfaceHeight, 0f) + CruiseClearance, targetAltitude - TargetAltitudeMargin);

    public static float AimAltitude(float surfaceHeight, float targetAltitude, bool terminal) =>
        terminal ? targetAltitude : CruiseAltitude(surfaceHeight, targetAltitude);

    public static float TerrainLookAhead(float speed, float targetDistance) =>
        Math.Min(Math.Max(speed, 100f) * TerrainLookAheadSeconds, Math.Max(targetDistance, 0f));

    // Trim settled flight only, so the initial descent cannot wind up the altitude controller.
    public static float CruiseTrim(float trim, float altitudeError, float verticalSpeed, float deltaTime) =>
        Math.Abs(altitudeError) < 5f && Math.Abs(verticalSpeed) < 5f
            ? Math.Clamp(trim + altitudeError * deltaTime * 0.25f, -2f, 2f) : trim;

    public static float CruisePitch(float altitude, float surfaceHeight, float speed, float verticalSpeed, float turnRateDegrees, float gLimit, float targetAltitude = 0f)
    {
        speed = Math.Max(speed, 1f);
        float turnRate = Math.Min(turnRateDegrees * MathF.PI / 180f, 9.81f * gLimit / speed);
        float radius = speed / Math.Max(turnRate, 0.001f);
        float clearance = altitude - CruiseAltitude(surfaceHeight, targetAltitude);
        float currentPitch = MathF.Asin(Math.Clamp(verticalSpeed / speed, -1f, 1f));
        float pullupMargin = Math.Max(5f, Math.Abs(verticalSpeed) * 0.15f);
        float availableHeight = Math.Max(Math.Abs(clearance) - pullupMargin, 0f);
        // Reserve half the leveling arc for acceleration and steering lag; use velocity feedback to start the pull-up.
        float desiredPitch = -Math.Sign(clearance) * Math.Min(35f * MathF.PI / 180f,
            MathF.Acos(Math.Clamp(1f - 0.5f * availableHeight / radius, 0f, 1f)));
        if (availableHeight == 0f)
            desiredPitch = MathF.Asin(Math.Clamp(-clearance * 0.5f / speed, -5f / speed, 5f / speed));
        return Math.Clamp(2f * desiredPitch - currentPitch, -MathF.PI / 4f, MathF.PI / 4f);
    }

    public static uint PickCandidate(IReadOnlyList<RadarCandidate> candidates, float minSignal, uint designatedTargetId = 0)
    {
        uint bestId = 0;
        float bestSignal = minSignal;
        foreach (RadarCandidate candidate in candidates)
        {
            if (!candidate.Alive || !candidate.Enemy || !candidate.Airborne || candidate.TargetId == 0 ||
                candidate.CueDistanceSquared > CueRadius * CueRadius || !float.IsFinite(candidate.Signal) ||
                candidate.Signal <= minSignal)
                continue;

            if (candidate.TargetId == designatedTargetId)
                return candidate.TargetId;

            if (candidate.Signal > bestSignal || candidate.Signal == bestSignal && candidate.TargetId < bestId)
            {
                bestId = candidate.TargetId;
                bestSignal = candidate.Signal;
            }
        }
        return bestId;
    }

    public static bool KeepLock(bool enemyAirborne, float secondsWithoutReturn) =>
        enemyAirborne && secondsWithoutReturn <= LockPersistence;

    public static float DecayJam(float accumulation, float deltaTime) =>
        Math.Clamp(accumulation - Math.Max(accumulation, 0.2f) * JamTolerance * deltaTime, 0f, 1f);
}
