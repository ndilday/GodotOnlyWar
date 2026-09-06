using OnlyWar.Models.Supply;
using System;

namespace OnlyWar.Helpers.Supply;

public static class GovernorOfferCalculator
{
    public static int Calculate(
        int pricedRequestValue,
        GovernorWillingness willingness,
        GovernorOfferRules rules)
    {
        if (pricedRequestValue < 0)
            throw new ArgumentOutOfRangeException(nameof(pricedRequestValue));
        ArgumentNullException.ThrowIfNull(willingness);
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.MinimumOffer < 0 || rules.MaximumOffer < rules.MinimumOffer)
            throw new ArgumentOutOfRangeException(nameof(rules));
        if (rules.MinimumWillingnessMultiplier < 0 ||
            rules.MaximumWillingnessMultiplier < rules.MinimumWillingnessMultiplier)
            throw new ArgumentOutOfRangeException(nameof(rules));

        decimal combinedWillingness;
        try
        {
            combinedWillingness = willingness.DesperationMultiplier
                * willingness.RelationshipMultiplier
                * willingness.AuthorityMultiplier;
        }
        catch (OverflowException)
        {
            combinedWillingness = decimal.MaxValue;
        }

        combinedWillingness = Math.Clamp(
            combinedWillingness,
            rules.MinimumWillingnessMultiplier,
            rules.MaximumWillingnessMultiplier);

        decimal rawOffer;
        try
        {
            rawOffer = pricedRequestValue * combinedWillingness;
        }
        catch (OverflowException)
        {
            rawOffer = decimal.MaxValue;
        }

        return RequestValueCalculator.RoundAndClamp(rawOffer, rules.MinimumOffer, rules.MaximumOffer);
    }
}
