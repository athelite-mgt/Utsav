namespace Utsav.UnitTests.Branding;

/// <summary>
/// The app began life as one Durga Puja committee's finance tool, and its
/// name and wording were written into screens, documents, prompts and the PWA
/// manifest. A committee of any festival must not meet another's name or the
/// old product's, so this fails on any such literal in the source.
/// </summary>
public class NoHardcodedIdentityTests
{
    private static readonly string[] Forbidden = ["Durga Puja", "Puja Finance"];

    private static readonly string[] Extensions = [".cs", ".ts", ".tsx", ".html", ".json", ".webmanifest", ".css", ".md", ".js"];

    private static readonly string[] SkippedDirectories =
        ["bin", "obj", "node_modules", "dist", "Migrations", "wwwroot", "fonts"];

    /// <summary>
    /// Festival names legitimately live here: it is seed data keyed BY festival,
    /// which is the opposite of hardcoding one.
    /// </summary>
    private const string TemplatesFile = "FestivalTemplates.cs";

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Utsav.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Utsav.slnx not found above the test binary.");
    }

    [Fact]
    public void No_source_file_hardcodes_one_festival_or_the_old_product_name()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            var segments = relative.Split(Path.DirectorySeparatorChar);

            if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                || segments.Any(s => SkippedDirectories.Contains(s))
                || segments[^1] is "package-lock.json" or TemplatesFile
                // Test data may name a festival: that is what is being tested.
                || segments[^1].Contains(".test.", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var hit = Forbidden.FirstOrDefault(f => lines[i].Contains(f, StringComparison.Ordinal));
                if (hit is not null)
                {
                    offenders.Add($"{relative}:{i + 1}  \"{hit}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Hardcoded identity strings:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_guard_actually_sees_the_files_it_is_meant_to_police()
    {
        // A guard that scans nothing passes forever. Prove it reaches real source.
        var root = RepoRoot();

        Assert.True(File.Exists(Path.Combine(root, "src", "Utsav.Web", "index.html")));
        Assert.True(File.Exists(Path.Combine(root, "src", "Utsav.Api", "Program.cs")));
    }
}
