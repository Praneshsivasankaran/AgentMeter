namespace AgentMeter.Tests;

// Native window activation is shared desktop state even when each test has its own STA.
[CollectionDefinition("Windows UI", DisableParallelization = true)]
public sealed class WindowsUiCollection;
