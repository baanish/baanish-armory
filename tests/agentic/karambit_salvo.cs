using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NoAgenticFramework.Shared;

public static class KarambitSalvo
{
    private const string MissileType = "baanish_karambit";
    private const string TruthMissiles = "truth.missiles";
    private const string TestState = "mods.dev.baanish.karambit.salvo-test.";
    private const double BoatDefenseSpeedCeiling = 500;
    private static readonly HashSet<string> InboundNames = new HashSet<string>(Enumerable.Range(1, 20)
        .Select(i => "Karambit_Inbound_" + i.ToString("D2")), StringComparer.Ordinal);

    // The peak 40-missile scene exceeds the 32-entry truth cap. Count original
    // inbound IDs only in complete snapshots, before the boat can engage them.
    public static Reading CruiseSalvoObserved(TraceRecord sample, ConditionScope scope, JsonElement args) =>
        EvaluateSalvo(sample, scope, args, false);

    public static Reading CruiseSalvoDestroyed(TraceRecord sample, ConditionScope scope, JsonElement args) =>
        EvaluateSalvo(sample, scope, args, true);

    private static Reading EvaluateSalvo(TraceRecord sample, ConditionScope scope, JsonElement args, bool requireDestroyed)
    {
        if (!(sample.Fields.TryGetValue(TestState + "mission_time_s", out object time) && time is double missionTime))
            return Unreadable("no native mission clock from the test selector");
        if (missionTime < 45 || missionTime > 50)
            return Unreadable("salvo measurement requires mission time 45..50 s; actual " + Format.Number(missionTime));
        string mark = args.GetProperty("since").GetString();
        if (scope.Since(mark, sample.T) is not int from || from < 1)
            return Unreadable("no salvo mark yet");
        var records = scope.Records;
        var marker = records[from - 1];
        TraceRecord initial = null;
        for (int i = from - 2; i >= 0; i--)
            if (records[i].Kind == RecordKinds.Truth && records[i].T <= marker.T)
            {
                initial = records[i];
                break;
            }
        if (initial == null || marker.T - initial.T > 0.5 || !CompleteMissiles(initial, out var initialMissiles) ||
            initialMissiles.Count != 20 || initialMissiles.Any(m => (string)m["type"] != "AShM1"))
            return Unreadable("the salvo mark needs a complete twenty-AShM-300 truth snapshot within 0.5 s before it");
        var initialIds = new HashSet<uint>(initialMissiles.Select(m => (uint)(double)m["id"]));
        TraceRecord final = null;
        for (int i = records.Count - 1; i >= from; i--)
            if (records[i].Kind == RecordKinds.Truth && records[i].T <= sample.T)
            {
                final = records[i];
                break;
            }
        if (final == null || sample.T - final.T > 0.5 || !CompleteMissiles(final, out var finalMissiles))
            return Unreadable("the final survivor measurement needs a fresh uncapped truth list with fewer than 32 entries");
        int survivors = finalMissiles.Count(m => initialIds.Contains((uint)(double)m["id"]));
        int cruiseCount = finalMissiles.Count(m => (string)m["type"] == "AShM1");
        var launches = records.Skip(from).Where(r => r.T <= final.T && IsEvent(r, "own_launch") && r.Text("type") == MissileType).ToArray();
        var ids = new HashSet<uint>();
        bool validLaunches = true;
        foreach (var launch in launches)
            if (!TryId(launch.Fields, "missile_id", out uint id) || !ids.Add(id))
                validLaunches = false;
        var targets = new HashSet<string>(launches.Select(r => r.Text("target")), StringComparer.Ordinal);
        var endedIds = new HashSet<uint>(records.Skip(from).Where(r => r.T <= final.T && IsEvent(r, "missile_end") &&
            r.Text("type") == MissileType && TryId(r.Fields, "missile_id", out uint id) && ids.Contains(id))
            .Select(r => (uint)(double)r.Fields["missile_id"]));
        int pitchedLaunches = 0;
        foreach (var launch in launches)
        {
            if (!TryId(launch.Fields, "missile_id", out uint launchedId))
                continue;
            string path = "mods.dev.baanish.karambit.missiles." + launchedId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
            double endedAt = records.Skip(from).FirstOrDefault(r => IsEvent(r, "missile_end") &&
                TryId(r.Fields, "missile_id", out uint endedId) && endedId == launchedId)?.T ?? final.T;
            if (records.Skip(from).Any(r => r.Kind == RecordKinds.Sample && r.T >= launch.T && r.T <= endedAt &&
                r.Fields.TryGetValue(path + "launch_pitch_deg", out object pitch) && pitch is double p && Math.Abs(p + 5) < 0.1 &&
                r.Fields.TryGetValue(path + "launch_yaw_deg", out object yaw) && yaw is double y && Math.Abs(y) < 0.1))
                pitchedLaunches++;
        }
        string ammo = sample.Fields.TryGetValue("own.station.ammo", out object rounds) && rounds is double number
            ? Format.Number(number) : "unknown";
        bool defenseProof = OutsideBoatDefense(records, initial.T, final, initialIds, out string defenseDetail);
        if (!(sample.Fields.TryGetValue("mods.dev.baanish.karambit.inbound_cruise_speed_mps", out object cruiseSpeed) &&
            cruiseSpeed is double speed && double.IsFinite(speed) && speed > 0 && speed <= BoatDefenseSpeedCeiling))
        {
            defenseProof = false;
            defenseDetail += "; stock cruise speed is missing or exceeds the 500 m/s boat-defense ceiling";
        }
        string detail = "At mission " + Format.Number(missionTime) + " s: original AShM-300 survivors " + survivors +
            "/20, removed " + (20 - survivors) + "/20; all live AShM-300s " + cruiseCount + "; complete truth count " +
            finalMissiles.Count + "; Karambit launches " + launches.Length + " (unique IDs " + ids.Count +
            ", distinct launch targets " + targets.Count + "), ended " + endedIds.Count + "; five-degree nose-down launches " +
            pitchedLaunches + "; ammo " + ammo + "; " + defenseDetail;
        if (!requireDestroyed)
            return new Reading(true, 50 - missionTime, detail);
        bool success = survivors == 0 && cruiseCount == 0 && launches.Length == 20 && validLaunches && ids.Count == 20 &&
            targets.SetEquals(InboundNames) && endedIds.Count == 20 && pitchedLaunches == 20 && rounds is double remaining && remaining == 0 && defenseProof;
        return new Reading(success, success ? 20 : -Math.Max(1, survivors), detail);
    }

    private static bool OutsideBoatDefense(IReadOnlyList<TraceRecord> records, double initialTime, TraceRecord final,
        IReadOnlyCollection<uint> initialIds, out string detail)
    {
        var failures = new List<string>();
        double minimumRange = double.PositiveInfinity;
        double maximumAge = 0;
        double maximumGap = 0;
        foreach (uint incomingId in initialIds)
        {
            TraceRecord lastSeen = null;
            TraceRecord firstAbsent = null;
            IReadOnlyDictionary<string, object> lastMissile = null;
            foreach (var record in records)
            {
                if (record.Kind != RecordKinds.Truth || record.T < initialTime || record.T > final.T ||
                    !record.Lists.TryGetValue(TruthMissiles, out var missiles))
                    continue;
                var missile = missiles.FirstOrDefault(m => TryId(m, "id", out uint id) && id == incomingId);
                if (missile != null)
                {
                    lastSeen = record;
                    lastMissile = missile;
                    firstAbsent = null;
                }
                else if (lastSeen != null && firstAbsent == null && CompleteMissiles(record, out _))
                    firstAbsent = record;
            }
            if (lastSeen == null || lastMissile == null ||
                !lastMissile.TryGetValue("target", out object target) || !(target is string name) || name != "Karambit_Target" ||
                !lastMissile.TryGetValue("range_m", out object distance) || !(distance is double range) || !double.IsFinite(range))
            {
                failures.Add("inbound " + incomingId + " missing actual last boat range");
                continue;
            }
            double gap = (firstAbsent ?? final).T - lastSeen.T;
            // A capped gap is safe only if even 500 m/s closure leaves over 10 km
            // before the first complete snapshot proves the inbound absent.
            double lowerRange = range - BoatDefenseSpeedCeiling * gap;
            minimumRange = Math.Min(minimumRange, lowerRange);
            maximumGap = Math.Max(maximumGap, gap);
            maximumAge = Math.Max(maximumAge, final.T - lastSeen.T);
            if (lowerRange <= 10000 || (firstAbsent == null && gap > 0.5))
                failures.Add("inbound " + incomingId + " boat-distance lower bound " + Format.Number(lowerRange) + " m");
        }
        detail = failures.Count == 0 ? "all last inbound boat ranges >10 km after 500 m/s closure across truth gaps (minimum bound " +
            Format.Number(minimumRange) + " m, maximum gap " + Format.Number(maximumGap) + " s, oldest last sighting " +
            Format.Number(maximumAge) + " s before final snapshot)"
            : "boat-defense exclusion unproved: " + string.Join("; ", failures);
        return failures.Count == 0;
    }

    private static bool CompleteMissiles(TraceRecord record, out IReadOnlyList<IReadOnlyDictionary<string, object>> missiles)
    {
        if (record.Lists.TryGetValue(TruthMissiles, out missiles) && missiles.Count < Protocol.MaxContacts &&
            record.Fields.TryGetValue(TruthMissiles + ".count", out object count) && count is double number && number == missiles.Count &&
            missiles.All(m => TryId(m, "id", out _) && m.TryGetValue("type", out object type) && type is string) &&
            missiles.Select(m => (double)m["id"]).Distinct().Count() == missiles.Count)
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
