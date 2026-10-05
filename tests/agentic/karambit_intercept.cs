using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using NoAgenticFramework.Shared;

public static class KarambitIntercept
{
    private const string MissileType = "baanish_karambit";
    private const string Telemetry = "mods.dev.baanish.karambit.missiles.";
    private const string TruthMissiles = "truth.missiles";
    private const double EvidenceWindowSeconds = 0.5;
    // Allow closure between the final sample and detonation, without crediting distant self-destruction.
    private const double TerminalRangeToleranceMeters = 20;
    private const double MaximumClosingSpeedMetersPerSecond = 1500;

    public static Reading CruiseMachObserved(TraceRecord sample, ConditionScope scope, JsonElement args)
    {
        if (scope.Since(args.GetProperty("since").GetString(), sample.T) is not int from)
            return Unreadable("no intercept mark yet");
        var records = scope.Records.Skip(from).Where(r => r.T <= sample.T).ToArray();
        var launch = records.FirstOrDefault(r => IsEvent(r, "own_launch") && r.Text("type") == MissileType);
        if (launch == null || !TryId(launch.Fields, "missile_id", out uint id))
            return Unreadable("no Karambit launch yet");
        string path = Telemetry + id.ToString(CultureInfo.InvariantCulture) + ".";
        var speeds = records.Where(r => r.Kind == RecordKinds.Sample && Number(r, path + "mach", out _))
            .Select(r => (double)r.Fields[path + "mach"]).ToArray();
        if (speeds.Length == 0 || !records.Any(r => IsEvent(r, "missile_end") && TryId(r.Fields, "missile_id", out uint ended) && ended == id))
            return Unreadable("waiting for complete interceptor flight speed evidence");
        double peak = speeds.Max();
        // Native thrust ticks and descent can overshoot the cutoff; Mach 3 cannot pass this tolerance.
        return new Reading(peak >= 2.45 && peak <= 2.55, 2.55 - peak,
            "Karambit " + id + " peak Mach " + Format.Number(peak) + " across " + speeds.Length + " flight samples; expected 2.45..2.55");
    }

    public static Reading LowCruiseBelowTarget(TraceRecord sample, ConditionScope scope, JsonElement args)
    {
        if (scope.Since(args.GetProperty("since").GetString(), sample.T) is not int from)
            return Unreadable("no intercept mark yet");
        var launch = scope.Records.Skip(from).FirstOrDefault(r => r.T <= sample.T && IsEvent(r, "own_launch") && r.Text("type") == MissileType);
        if (launch == null || !TryId(launch.Fields, "missile_id", out uint id))
            return Unreadable("no Karambit launch yet");
        string path = Telemetry + id.ToString(CultureInfo.InvariantCulture) + ".";
        double start = -1, previous = -1, minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        foreach (var record in scope.Records.Skip(from).Where(r => r.Kind == RecordKinds.Sample && r.T >= launch.T && r.T <= sample.T))
        {
            bool valid = record.Text(path + "phase") == "cruise" &&
                Number(record, path + "alt_m", out double altitude) && altitude >= 2.4 && altitude <= 3.8 &&
                Number(record, path + "known_alt_m", out double targetAltitude) && targetAltitude - altitude >= 2 &&
                Number(record, path + "terrain_ahead_m", out double terrain) && terrain == 0 &&
                Number(record, path + "cruise_alt_m", out double goal) && Math.Abs(goal - 3.048) < 0.001 &&
                Number(record, path + "launch_pitch_deg", out double pitch) && Math.Abs(pitch + 5) < 0.1 &&
                Number(record, path + "launch_yaw_deg", out double yaw) && Math.Abs(yaw) < 0.1;
            if (!valid || previous >= 0 && record.T - previous > 0.25)
            {
                start = -1;
                minimum = double.PositiveInfinity;
                maximum = double.NegativeInfinity;
            }
            if (valid)
            {
                if (start < 0) start = record.T;
                altitude = (double)record.Fields[path + "alt_m"];
                minimum = Math.Min(minimum, altitude);
                maximum = Math.Max(maximum, altitude);
                if (record.T - start >= 1)
                    return new Reading(true, record.T - start, "Karambit " + id + " held " + Format.Number(minimum) + ".." +
                        Format.Number(maximum) + " m over sea below its target for " + Format.Number(record.T - start) +
                        " s, with a 3.048 m setpoint and five-degree nose-down launch");
            }
            previous = record.T;
        }
        return Unreadable("waiting for one sustained second near 10 ft, below the target, after a five-degree nose-down launch");
    }

    private static bool Number(TraceRecord record, string path, out double number)
    {
        if (record.Fields.TryGetValue(path, out object value) && value is double result && double.IsFinite(result))
        {
            number = result;
            return true;
        }
        number = 0;
        return false;
    }

    // Require compatible final separation and disappearance of the designated target.
    public static Reading LockedCruiseMissileDestroyed(TraceRecord sample, ConditionScope scope, JsonElement args)
    {
        string mark = args.GetProperty("since").GetString();
        if (scope.Since(mark, sample.T) is not int from)
            return Unreadable("no intercept mark yet");

        var records = scope.Records;
        int launchIndex = -1;
        uint missileId = 0;
        for (int i = from; i < records.Count; i++)
        {
            var record = records[i];
            if (record.T > sample.T || !IsEvent(record, "own_launch") || record.Text("type") != MissileType)
                continue;
            if (launchIndex >= 0)
                return new Reading(false, -1, "more than one Karambit launched after the intercept mark");
            if (!TryId(record.Fields, "missile_id", out missileId))
                return Unreadable("Karambit launch has no valid missile_id");
            launchIndex = i;
        }
        if (launchIndex < 0)
            return Unreadable("no Karambit launch after the intercept mark");

        int endIndex = -1;
        for (int i = launchIndex + 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.T <= sample.T && IsEvent(record, "missile_end") && record.Text("type") == MissileType &&
                TryId(record.Fields, "missile_id", out uint endedId) && endedId == missileId)
            {
                endIndex = i;
                break;
            }
        }
        if (endIndex < 0)
            return Unreadable("Karambit " + missileId + " has not ended");

        var end = records[endIndex];
        string missilePath = Telemetry + missileId.ToString(CultureInfo.InvariantCulture);
        TraceRecord telemetry = null;
        uint targetId = 0;
        for (int i = endIndex - 1; i > launchIndex; i--)
        {
            var record = records[i];
            if (record.Kind == RecordKinds.Sample && record.T <= end.T && record.T <= sample.T &&
                end.T - record.T <= EvidenceWindowSeconds && TryId(record.Fields, missilePath + ".target_id", out targetId))
            {
                telemetry = record;
                break;
            }
        }
        if (telemetry == null)
            return Unreadable("Karambit " + missileId + " has no final lock telemetry within 0.5 s before its end");
        if (!TryId(telemetry.Fields, missilePath + ".designated_id", out uint designatedId))
            return Unreadable("Karambit " + missileId + " final telemetry has no valid designated target ID");
        if (targetId != designatedId)
            return new Reading(false, -1, "Karambit " + missileId + " locked target " + targetId + " instead of designated target " +
                designatedId + " (greedy fallback; designated interception not proved)");
        double maximumRange = TerminalRangeToleranceMeters + MaximumClosingSpeedMetersPerSecond * (end.T - telemetry.T);
        if (!Number(telemetry, missilePath + ".range_m", out double range) || range < 0 || range > maximumRange)
            return new Reading(false, -1, "Karambit " + missileId + " final separation is incompatible with interception; limit " +
                Format.Number(maximumRange) + " m at the last lock sample");

        var before = PreviousRecord(records, endIndex, RecordKinds.Truth, end.T);
        if (before == null || end.T - before.T > EvidenceWindowSeconds || !CompleteMissiles(before, out var beforeMissiles))
            return Unreadable("no complete truth missile snapshot within 0.5 s before Karambit " + missileId + " ended");
        if (!beforeMissiles.Any(m => TryId(m, "id", out uint id) && id == targetId &&
            m.TryGetValue("type", out object type) && type is string name && name == "AShM1"))
            return new Reading(false, -1, "Karambit " + missileId + " final target " + targetId + " was not a live AShM-300 immediately before its end");

        for (int i = endIndex + 1; i < records.Count; i++)
        {
            var after = records[i];
            double delay = after.T - end.T;
            if (after.Kind != RecordKinds.Truth || after.T > sample.T || delay < 0 || delay > EvidenceWindowSeconds)
                continue;
            if (!CompleteMissiles(after, out var afterMissiles))
                continue;
            if (!afterMissiles.Any(m => TryId(m, "id", out uint id) && id == targetId))
                return new Reading(true, EvidenceWindowSeconds - delay,
                    "Karambit " + missileId + " final AShM-300 target " + targetId + " disappeared " +
                    Format.Number(delay) + " s after its end; complete truth lists " + beforeMissiles.Count + " -> " + afterMissiles.Count);
        }
        return sample.T - end.T <= EvidenceWindowSeconds
            ? Unreadable("waiting for a complete truth snapshot after Karambit " + missileId + " ended")
            : new Reading(false, -1, "Karambit " + missileId + " final target " + targetId + " did not disappear from a complete truth list within 0.5 s");
    }

    private static TraceRecord PreviousRecord(IReadOnlyList<TraceRecord> records, int before, string kind, double latestTime)
    {
        for (int i = before - 1; i >= 0; i--)
            if (records[i].Kind == kind && records[i].T <= latestTime)
                return records[i];
        return null;
    }

    private static bool CompleteMissiles(TraceRecord record, out IReadOnlyList<IReadOnlyDictionary<string, object>> missiles)
    {
        if (record.Lists.TryGetValue(TruthMissiles, out missiles) && missiles.Count < Protocol.MaxContacts &&
            record.Fields.TryGetValue(TruthMissiles + ".count", out object count) && count is double number && number == missiles.Count &&
            missiles.All(m => TryId(m, "id", out _) && m.TryGetValue("type", out object type) && type is string))
            return true;
        missiles = Array.Empty<IReadOnlyDictionary<string, object>>();
        return false;
    }

    private static bool TryId(IReadOnlyDictionary<string, object> fields, string path, out uint id)
    {
        if (fields.TryGetValue(path, out object value) && value is double number &&
            number > 0 && number <= uint.MaxValue && number == Math.Truncate(number))
        {
            id = (uint)number;
            return true;
        }
        id = 0;
        return false;
    }

    private static bool IsEvent(TraceRecord record, string name) => record.Kind == RecordKinds.Event && record.Text("name") == name;
    private static Reading Unreadable(string detail) => new Reading(false, double.NegativeInfinity, detail);
}
