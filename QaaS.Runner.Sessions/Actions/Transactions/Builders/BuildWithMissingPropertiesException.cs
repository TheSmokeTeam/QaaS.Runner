namespace QaaS.Runner.Sessions.Actions.Transactions.Builders;

public class BuildWithMissingPropertiesException(string propertyNameOrNames)
    : Exception($"Exception: Tried to build the transaction object with no {propertyNameOrNames} set!");