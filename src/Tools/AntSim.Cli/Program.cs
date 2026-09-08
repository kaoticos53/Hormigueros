using System;
using System.Globalization;
using AntSim.Core.Scenario;

namespace AntSim.Cli;

/// <summary>
/// antsim — herramienta headless (predecesora del modo análisis/verificación).
///
/// Uso:
///   antsim --seed 12345 --ticks 1200 [--grid 96]
///
/// Ejecuta el microcosmos determinista e imprime hashes de hito por tick.
/// Dos ejecuciones con la misma semilla deben producir salida idéntica.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        ulong seed = 12345UL;
        int ticks = 1200;
        int grid = 96;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                case "--seed":
                    if (!ulong.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                        return Fail("--seed requiere un entero sin signo.");
                    break;
                case "--ticks":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || ticks < 1)
                        return Fail("--ticks requiere un entero positivo.");
                    break;
                case "--grid":
                    if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out grid) || grid < 8)
                        return Fail("--grid requiere un entero ≥ 8.");
                    break;
                default:
                    return Fail($"Argumento desconocido: {args[i]}");
            }
        }

        try
        {
            string output = Microcosm.Run(seed, ticks, grid);
            Console.Out.Write(output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static string Next(string[] args, ref int i)
    {
        i++;
        if (i >= args.Length) throw new ArgumentException("Falta el valor del argumento.");
        return args[i];
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Out.WriteLine("Uso: antsim [--seed N] [--ticks N] [--grid N]");
    }
}
