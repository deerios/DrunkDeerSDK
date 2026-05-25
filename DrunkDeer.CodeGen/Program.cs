using DrunkDeer.Codegen;

// AppContext.BaseDirectory = DrunkDeer.CodeGen/bin/{cfg}/net10.0/
// Go up 4 levels to reach the solution root, then into DrunkDeer/ for protocol/templates/output.
var binDir = AppContext.BaseDirectory;
var sdkRoot = Path.GetFullPath(Path.Combine(binDir, "..", "..", "..", "..", "DrunkDeer"));

var protocolDir = Path.Combine(sdkRoot, "protocol");
var templatesDir = Path.Combine(sdkRoot, "Templates");
var outputDir = Path.Combine(sdkRoot, "Generated");

Console.WriteLine($"SDK root     : {sdkRoot}");
Console.WriteLine($"Protocol dir : {protocolDir}");
Console.WriteLine($"Templates dir: {templatesDir}");
Console.WriteLine($"Output dir   : {outputDir}");
Console.WriteLine();

Generator.Run(protocolDir, templatesDir, outputDir);

Console.WriteLine();
Console.WriteLine("Done.");
