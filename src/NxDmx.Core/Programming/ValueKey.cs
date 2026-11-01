using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Programming;

/// <summary>Addresses one attribute of one fixture, e.g. the dimmer of fixture 5.</summary>
public readonly record struct ValueKey(int FixtureId, FixtureAttribute Attribute);
