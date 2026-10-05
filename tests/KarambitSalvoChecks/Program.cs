using System.Text.Json;
using NoAgenticFramework.Runner;
using NoAgenticFramework.Shared;

using var argsDoc = JsonDocument.Parse("{\"since\":\"salvo\"}");
int tests = 0;
TraceRecord Parse(object record) => TraceFile.Parse(JsonSerializer.Serialize(record));
TraceRecord Event(double t, string name, int id = 100, string target = "Karambit_Inbound_01") => Parse(new
{ k = "e", t, name, label = "salvo", missile_id = id, type = "baanish_karambit", target });
TraceRecord Truth(double t, int survivors, double range = 20000, int extra = 0) => Parse(new
{
    k = "truth", t, truth = new { missiles = Enumerable.Range(1, survivors).Select(id => (object)new
    { id, type = "AShM1", target = "Karambit_Target", range_m = range }).Concat(Enumerable.Range(100, extra).Select(id => (object)new
    { id, type = "baanish_karambit" })) }
});
TraceRecord Sample(double t = 45.1, double missionTime = 45.1, int ammo = 0) => Parse(new
{
    k = "s", t, own = new { station = new { ammo } }, mods = new Dictionary<string, object>
    {
        ["dev.baanish.karambit.salvo-test"] = new { mission_time_s = missionTime },
        ["dev.baanish.karambit"] = new { inbound_cruise_speed_mps = 426.99 }
    }
});
TraceRecord LaunchAttitudes(double pitch = -5, double yaw = 0, int count = 20) => Parse(new
{
    k = "s", t = 10, mods = new Dictionary<string, object>
    {
        ["dev.baanish.karambit"] = new
        {
            missiles = Enumerable.Range(100, count).ToDictionary(id => id.ToString(), id => new
                { launch_pitch_deg = pitch, launch_yaw_deg = yaw })
        }
    }
});
List<TraceRecord> Baseline(double lastRange = 20000, double absenceTime = 20.2, int survivors = 0)
{
    var records = new List<TraceRecord> { Truth(4.9, 20, 28000), Event(5, "mark") };
    records.AddRange(Enumerable.Range(0, 20).Select(i => Event(5.5 + i * 0.22, "own_launch", 100 + i, "Karambit_Inbound_" + (i + 1).ToString("D2"))));
    records.Add(LaunchAttitudes());
    records.Add(Truth(20, 20, lastRange));
    records.AddRange(Enumerable.Range(0, 20).Select(i => Event(20.1, "missile_end", 100 + i)));
    records.Add(Truth(absenceTime, survivors, lastRange));
    records.Add(Truth(45, survivors, lastRange));
    records.Add(Sample());
    return records;
}
void Check(string name, List<TraceRecord> records, bool expected, string exactDetail = null, bool observe = false)
{
    var scope = new ConditionScope(records, 0);
    Reading reading = observe ? KarambitSalvo.CruiseSalvoObserved(records[^1], scope, argsDoc.RootElement)
        : KarambitSalvo.CruiseSalvoDestroyed(records[^1], scope, argsDoc.RootElement);
    if (reading.Ok != expected || exactDetail != null && !reading.Detail.Contains(exactDetail, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(name + ": " + reading.Detail);
        Environment.Exit(1);
    }
    tests++;
}
Check("twenty distinct five-degree launches and original IDs removed", Baseline(), true, "five-degree nose-down launches 20");
Check("500 m/s closure exact 10000 m boundary refuses", Baseline(15000, 30), false, "boat-distance lower bound 10,000 m");
Check("500 m/s closure 10000.1 m boundary accepts", Baseline(15000.1, 30), true, "minimum bound 10,000.1 m");
Check("same complete truth time leaves no closure gap", Baseline(10000.1, 20), true, "minimum bound 10,000.1 m");
Check("below defense guard refuses", Baseline(10000, 20), false, "boat-distance lower bound 10,000 m");
Check("partial result remains measurable before failure", Baseline(survivors: 3), true, "original AShM-300 survivors 3/20, removed 17/20", observe: true);
Check("surviving original IDs fail interception", Baseline(survivors: 3), false);
var trace = Baseline(); trace.Insert(trace.Count - 1, Truth(45.05, 20, extra: 12));
Check("32-entry final truth cannot prove absence", trace, false, "uncapped truth list");
trace = Baseline(); trace.RemoveAt(3);
Check("nineteen launches fail", trace, false, "Karambit launches 19");
trace = Baseline(); trace[3] = Event(5.72, "own_launch", 100, "Karambit_Inbound_02");
Check("duplicate launch ID fails", trace, false, "unique IDs 19");
trace = Baseline(); trace[3] = Event(5.72, "own_launch", 101, "Karambit_Inbound_01");
Check("repeated designation fails", trace, false, "distinct launch targets 19");
trace = Baseline(); trace.RemoveAt(24);
Check("all twenty own ends are required", trace, false, "ended 19");
trace = Baseline(); trace[^1] = Sample(missionTime: 50.001);
Check("late observation refuses boat-defense attribution", trace, false, "requires mission time 45..50 s");
trace = Baseline(); trace[^1] = Sample(ammo: 1);
Check("one unspent round fails", trace, false, "ammo 1");
trace = Baseline(); trace[22] = LaunchAttitudes(pitch: 0);
Check("level launch cannot prove pitched racks", trace, false, "five-degree nose-down launches 0");
trace = Baseline(); trace[22] = LaunchAttitudes(pitch: -10);
Check("old ten-degree launches fail the five-degree contract", trace, false, "five-degree nose-down launches 0");
trace = Baseline(); trace[22] = LaunchAttitudes(yaw: 5);
Check("hardpoint roll turning pitch into yaw fails", trace, false, "five-degree nose-down launches 0");
trace = Baseline(); trace[22] = LaunchAttitudes(count: 19);
Check("every launch needs pitch telemetry", trace, false, "five-degree nose-down launches 19");
trace = Baseline();
for (int i = 0; i < trace.Count; i++)
    if (trace[i].Kind == RecordKinds.Event && trace[i].Text("name") == "missile_end")
        trace[i] = Event(12, "missile_end", (int)(double)trace[i].Fields["missile_id"]);
trace = trace.OrderBy(r => r.T).ToList();
Check("targets observed after every interceptor ended cannot pass", trace, false, "no compatible Karambit end after its last sighting");

List<TraceRecord> Intercept(double range = 207.5, double sampleTime = 9.875, bool includeRange = true)
{
    var telemetry = new Dictionary<string, object> { ["target_id"] = 1, ["designated_id"] = 1 };
    if (includeRange) telemetry["range_m"] = range;
    return new List<TraceRecord>
    {
        Event(5, "mark"), Event(5.5, "own_launch"),
        Parse(new { k = "s", t = sampleTime, mods = new Dictionary<string, object>
        { ["dev.baanish.karambit"] = new { missiles = new Dictionary<string, object> { ["100"] = telemetry } } } }),
        Truth(9.95, 1), Event(10, "missile_end"), Truth(10.2, 0), Sample(10.3)
    };
}
void CheckIntercept(string name, List<TraceRecord> records, bool expected)
{
    Reading reading = KarambitIntercept.LockedCruiseMissileDestroyed(records[^1], new ConditionScope(records, 0), argsDoc.RootElement);
    if (reading.Ok != expected)
    {
        Console.Error.WriteLine(name + ": " + reading.Detail);
        Environment.Exit(1);
    }
    tests++;
}
CheckIntercept("compatible separation at the exact 207.5 m closure limit accepts", Intercept(), true);
CheckIntercept("207.6 m exceeds the 0.125-second closure limit", Intercept(207.6), false);
CheckIntercept("distant self-destruction cannot borrow target disappearance", Intercept(8000), false);
CheckIntercept("missing final separation cannot prove interception", Intercept(includeRange: false), false);
CheckIntercept("negative final separation refuses", Intercept(-1), false);
CheckIntercept("stale final lock telemetry refuses", Intercept(10, 9.49), false);
Console.WriteLine("PASS: " + tests + " full-salvo identity and boat-defense characterization fixtures.");
