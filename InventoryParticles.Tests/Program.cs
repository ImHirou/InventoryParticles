using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    private static string? gameDirectory;

    /// <summary> Runs before anything in this assembly, so the game assemblies resolve even while Main is compiled. </summary>
    [ModuleInitializer]
    internal static void RegisterGameAssemblies()
    {
        string? directory = Environment.GetEnvironmentVariable("VINTAGE_STORY");

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        gameDirectory = Path.GetFullPath(directory);
        string[] searchDirectories = { gameDirectory, Path.Combine(gameDirectory, "Lib"), Path.Combine(gameDirectory, "Mods") };
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string folder in searchDirectories)
            {
                string candidate = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
    }

    public static int Main()
    {
        try
        {
            if (gameDirectory == null)
            {
                throw new InvalidOperationException("Set VINTAGE_STORY to the installed game directory.");
            }

            int checks = RunTests();
            Console.WriteLine($"InventoryParticles tests: {checks} checks passed (no window or OpenGL context created).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    // Keeps the game types out of Main.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunTests() => Tests.Run();
}
