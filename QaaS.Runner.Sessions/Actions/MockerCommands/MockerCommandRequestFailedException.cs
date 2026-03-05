namespace QaaS.Runner.Sessions.Actions.MockerCommands;

public sealed class MockerCommandRequestFailedException(string message) : InvalidOperationException(message);
