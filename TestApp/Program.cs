using DrunkDeer.Protocol;
using Newtonsoft.Json;
using Serilog;
using System.Text.Json.Serialization;

const string logFile = "DrunkDeer.log";

Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Verbose()
	.WriteTo.Console(
		restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
		outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
	.WriteTo.File(
		logFile,
		rollingInterval: RollingInterval.Infinite,
		outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
	.CreateLogger();

Log.Information("Log file: {Path}", Path.GetFullPath(logFile));

try
{
	Log.Information("Opening keyboard…");
	using var session = KeyboardSession.OpenFirst();
	Log.Information("Connected: {Name} ({Variant}), fw {Fw}",
		session.Model.Name, session.Variant, session.FirmwareVersion);

	session.KeyDown    += (_, e) => Log.Information("[KeyDown]    idx={Index:000}  depth={Height}", e.Index, e.Height);
	session.KeyUp      += (_, e) => Log.Information("[KeyUp]      idx={Index:000}  depth={Height}", e.Index, e.Height);
	session.KeyPressed += (_, e) => Log.Information("[KeyPressed] idx={Index:000}  depth={Height}", e.Index, e.Height);

	session.KeyHeightChanged += (_, e) =>
		Log.Debug("[HeightChange] idx={Index:000}  {Prev} -> {Height}", e.Index, e.PreviousHeight, e.Height);

	session.Polled += (_, e) =>
		Log.Verbose("[Polled] {Hz} Hz  ({ElapsedMs:F2} ms)", e.Hz, e.Elapsed.TotalMilliseconds);

	//var profile = session.CaptureProfile();
	//Log.Information(JsonConvert.SerializeObject(profile));

	//session.SetKeyColor(DDKey.B, 0, 255, 0, 3);
	session.SetLightPreset(effect: 3, brightness: 7, speed: 4);

	//Log.Information(JsonConvert.SerializeObject(session.HasFuncBlock));
	//Log.Information(JsonConvert.SerializeObject(session.ReadLiveColors()));
	;

	session.StartPolling();

	Log.Information("Polling. Press Ctrl+C to exit.");

	Console.CancelKeyPress += (_, e) => { e.Cancel = true; session.StopPolling(); };
	Thread.Sleep(Timeout.Infinite);
}
catch (Exception ex)
{
	Log.Fatal(ex, "Fatal error");
}
finally
{
	Log.CloseAndFlush();
}
