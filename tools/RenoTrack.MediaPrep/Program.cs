using System.Globalization;
using RenoTrack.MediaPrep;

// RenoTrack.MediaPrep — offline photo preparation (Phase 13 Slice 5a, D106). See MEDIA_PREPARATION.md.
//
//   dotnet run --project tools/RenoTrack.MediaPrep -- <crop-spec.json> <source-directory> <output-directory>
//
// Exit codes: 0 all files written and within budget; 1 refused (nothing further written); 3 files written, but at
// least one is over its budget — a human decision, never an automatic re-encode.

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: RenoTrack.MediaPrep <crop-spec.json> <source-directory> <output-directory>");
    return 1;
}

try
{
    var spec = CropSpec.Parse(File.ReadAllText(args[0]));
    spec.Validate();

    var report = new List<PreparedFile>();
    foreach (var item in spec.Items)
    {
        report.AddRange(MediaPipeline.Prepare(item, args[1], args[2]));
    }

    Console.WriteLine($"{"File",-60} {"Bytes",10} {"Budget",10}  Status");
    foreach (var file in report)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{file.FileName,-60} {file.Bytes,10} {file.Derivative.MaxBytes,10}  {(file.IsWithinBudget ? "ok" : "OVER BUDGET")}"));
    }

    var over = report.Count(file => !file.IsWithinBudget);
    if (over > 0)
    {
        Console.Error.WriteLine(
            $"{over} file(s) over budget. Do not lower one photo's quality; follow the budget procedure in MEDIA_PREPARATION.md.");
        return 3;
    }

    return 0;
}
catch (MediaPrepException refusal)
{
    Console.Error.WriteLine(refusal.Message);
    return 1;
}
