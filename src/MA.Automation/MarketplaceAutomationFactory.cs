using MA.Core.Enums;
using MA.Core.Interfaces;

namespace MA.Automation;

public class MarketplaceAutomationFactory : IMarketplaceAutomationFactory
{
    private readonly IServiceProvider _sp;
    public MarketplaceAutomationFactory(IServiceProvider sp) { _sp = sp; }

    public IMarketplaceAutomation Get(Marketplace marketplace) => marketplace switch
    {
        Marketplace.Flipkart => (IMarketplaceAutomation)_sp.GetService(typeof(Drivers.FlipkartAutomation))!,
        Marketplace.Amazon   => (IMarketplaceAutomation)_sp.GetService(typeof(Drivers.AmazonAutomation))!,
        Marketplace.Meesho   => (IMarketplaceAutomation)_sp.GetService(typeof(Drivers.MeeshoAutomation))!,
        _ => throw new ArgumentOutOfRangeException(nameof(marketplace))
    };

    public IMarketplaceAutomation Get(string marketplaceName) =>
        Get(Enum.Parse<Marketplace>(marketplaceName, ignoreCase: true));
}
