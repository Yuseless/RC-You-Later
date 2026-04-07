using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public static class Timer
{
    private static readonly Stopwatch stopwatch = new();
    private static List<long> steps = new();
    private static string filePath = "score.txt";

    public static bool IsRunning
    {
        get => stopwatch.IsRunning;
    }

    public static double ElapsedSeconds
    {
        get => stopwatch.ElapsedMilliseconds * 0.001f;
    }

    public static int StepsCount
    {
        get => steps.Count;
    }

    public static double GetStepElapsedSeconds(int index)
    {
        return steps[index] * 0.001f;
    }

    /// <summary>
    /// Reset the timer and remove any steps.
    /// </summary>
    public static void Reset()
    {
        stopwatch.Reset();
        steps.Clear();
    }

    public static void Start()
    {
        stopwatch.Start();
    }

    public static void Stop()
    {
        stopwatch.Stop();
    }

    public static void Step()
    {
        steps.Add(stopwatch.ElapsedMilliseconds);
    }

    public static void Save()
    {
        // TODO : save our time steps (line 7 of this script) inside a file.

        long TempActuel = stopwatch.ElapsedMilliseconds;

        if (File.Exists(filePath))
        {
            string Coder = File.ReadAllText(filePath);
            string Decoder = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(Coder));
            string[] Lines = Decoder.Split('\n');

            if (Lines.Length > 0 && long.TryParse(Lines[Lines.Length - 1], out long bestTime))
            {
                if (TempActuel < bestTime)
                {
                    UnityEngine.Debug.Log("Nouveau record! " + TempActuel + "ms");
                }
                else
                {
                    UnityEngine.Debug.Log("Temps Actuel: " + TempActuel + "ms, Meilleur temps: " + bestTime + "ms");
                    return;
                }
            }
        }
        List<string> linesToSave = new List<string>();
        foreach (long step in steps)
        {
            linesToSave.Add(step.ToString());
        }

        string joined = string.Join("\n", linesToSave);
        string encodedData = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(joined));

        File.WriteAllText(filePath, encodedData);
    }

    public static void Load()
    {
        if (File.Exists(filePath))
        {
            // TODO : load our time steps from a file (if we have any)
            // and store them inside our steps variable (line 7 of this script)
            // to show them to the player before starting a race.
            string Coder = File.ReadAllText(filePath);
            string Decoder = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(Coder));
            string[] Lines = Decoder.Split('\n');

            steps.Clear();

            foreach (string Line in Lines)
            {
                if (long.TryParse(Line, out long value))
                {
                    steps.Add(value);
                }
            }
        }
    }
}
