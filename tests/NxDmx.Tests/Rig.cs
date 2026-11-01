using NxDmx.Core;
using NxDmx.Core.Commands;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;

namespace NxDmx.Tests;

/// <summary>A show with an interpreter on top, for driving the console the way an operator does.</summary>
internal sealed class Rig
{
    public Rig()
    {
        Interpreter = new CommandInterpreter(Show, Programmer, Output);
    }

    public Show Show { get; } = new();

    public Programmer Programmer { get; } = new();

    public OutputState Output { get; } = new();

    public CommandInterpreter Interpreter { get; }

    public CommandResult Run(string line) => Interpreter.Execute(line);

    /// <summary>Runs commands that must succeed.</summary>
    public void Do(params string[] lines)
    {
        foreach (var line in lines)
        {
            var result = Run(line);
            Assert.True(result.Success, $"'{line}' failed: {result.Message}");
        }
    }

    public CommandResult Fail(string line)
    {
        var result = Run(line);
        Assert.False(result.Success, $"'{line}' should have failed");
        return result;
    }

    public byte[] Universe(int universe) => FrameRenderer.Render(Show, Programmer, Output).Universes[universe];

    public double Value(int fixtureId, FixtureAttribute attribute) =>
        Programmer.Values[new ValueKey(fixtureId, attribute)];

    /// <summary>
    /// Eight RGBDim fixtures on 1.1-1.32, four dimmers on 1.101-1.104 (fixtures 21-24). Patched directly,
    /// so the Oops history starts empty.
    /// </summary>
    public static Rig WithPatch()
    {
        var rig = new Rig();
        var rgbDim = FixtureTypeLibrary.Find("RGBDim")!;
        var dimmer = FixtureTypeLibrary.Find("Dimmer")!;

        for (var i = 0; i < 8; i++)
            rig.Show.Patch.Add(new Fixture(1 + i, rgbDim, 1, 1 + i * 4));
        for (var i = 0; i < 4; i++)
            rig.Show.Patch.Add(new Fixture(21 + i, dimmer, 1, 101 + i));

        return rig;
    }
}
