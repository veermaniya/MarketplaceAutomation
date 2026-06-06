namespace MA.Core.Enums;

public enum Marketplace
{
    Flipkart = 1,
    Amazon   = 2,
    Meesho   = 3
}

public enum MappingStatus
{
    Pending  = 0,
    Listed   = 1,
    Failed   = 2,
    Disabled = 3
}

public enum RetryStatus
{
    Pending    = 0,
    InProgress = 1,
    Completed  = 2,
    Dead       = 3
}

public enum AutomationStatus
{
    Started = 0,
    Success = 1,
    Failed  = 2
}
